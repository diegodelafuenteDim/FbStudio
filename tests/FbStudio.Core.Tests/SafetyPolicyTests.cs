using FbStudio.Core.Profiles;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class SafetyPolicyTests
{
    private static ConnectionProfile Profile(ProfileEnvironment env = ProfileEnvironment.Desarrollo, bool readOnly = false) =>
        new() { Name = "sucursal", Host = "srv", Database = "sucursal", User = "U", Environment = env, ReadOnly = readOnly };

    private static SafetyVerdict Verdict(ConnectionProfile p, string sql) =>
        SafetyPolicy.Evaluate(p, StatementClassifier.Classify(sql)).Verdict;

    [Fact]
    public void En_produccion_un_SELECT_de_procedimiento_dentro_de_la_transaccion_abierta_pide_confirmacion()
    {
        var select = StatementClassifier.Classify("SELECT * FROM P_CONTAR");
        var inside = new SafetyContext(true, ["P_CONTAR"]);
        var d = SafetyPolicy.Evaluate(Profile(ProfileEnvironment.Produccion), select, inside);
        Assert.Equal(SafetyVerdict.Confirm, d.Verdict);
        Assert.StartsWith("Usa P_CONTAR y va a correr dentro de la transacción de escritura abierta en PRODUCCIÓN (sucursal)", Assert.Single(d.Reasons));

        Assert.Equal(SafetyVerdict.Allow, SafetyPolicy.Evaluate(Profile(), select, inside).Verdict);                       // fuera de producción
        Assert.Equal(SafetyVerdict.Allow, SafetyPolicy.Evaluate(Profile(ProfileEnvironment.Produccion), select,
            new SafetyContext(false, ["P_CONTAR"])).Verdict);                                                           // sin transacción: solo lectura
        Assert.Equal(SafetyVerdict.Allow, SafetyPolicy.Evaluate(Profile(ProfileEnvironment.Produccion), select,
            new SafetyContext(true, [])).Verdict);                                                                      // sin rutinas
    }

    [Fact]
    public void Select_se_permite_siempre() =>
        Assert.Equal(SafetyVerdict.Allow, Verdict(Profile(ProfileEnvironment.Produccion, readOnly: true), "SELECT 1 FROM RDB$DATABASE"));

    [Theory]
    [InlineData("UPDATE T SET A = 1 WHERE ID = 1")]
    [InlineData("CREATE TABLE X (A INTEGER)")]
    [InlineData("EXECUTE PROCEDURE P")]
    [InlineData("EXECUTE BLOCK AS BEGIN END")]
    public void Perfil_de_solo_lectura_bloquea_lo_que_modifica(string sql) =>
        Assert.Equal(SafetyVerdict.Block, Verdict(Profile(readOnly: true), sql));

    [Theory]
    [InlineData("SELECT NEXT VALUE FOR G FROM RDB$DATABASE", SafetyVerdict.Block)]
    [InlineData("SELECT GEN_ID(G, 1) FROM RDB$DATABASE", SafetyVerdict.Block)]
    [InlineData("SELECT GEN_ID(G, 0) FROM RDB$DATABASE", SafetyVerdict.Allow)]
    public void Solo_lectura_no_deja_mover_un_generador(string sql, SafetyVerdict verdict) =>
        Assert.Equal(verdict, Verdict(Profile(readOnly: true), sql));

    [Theory]
    [InlineData("MERGE INTO T USING S ON 1 = 1 WHEN MATCHED THEN DELETE")]
    [InlineData("EXECUTE BLOCK AS BEGIN DELETE FROM T; END")]
    public void MERGE_y_EXECUTE_BLOCK_que_borran_todo_piden_confirmacion(string sql) =>
        Assert.Equal(SafetyVerdict.Confirm, Verdict(Profile(), sql));

    [Fact]
    public void Produccion_pide_confirmacion_para_DML()
    {
        var d = SafetyPolicy.Evaluate(Profile(ProfileEnvironment.Produccion), StatementClassifier.Classify("DELETE FROM T WHERE ID = 1"));
        Assert.Equal(SafetyVerdict.Confirm, d.Verdict);
        Assert.Contains(d.Reasons, r => r.Contains("PRODUCCIÓN"));
    }

    [Fact]
    public void Desarrollo_permite_DML_con_WHERE() =>
        Assert.Equal(SafetyVerdict.Allow, Verdict(Profile(), "DELETE FROM T WHERE ID = 1"));

    [Fact]
    public void Sin_WHERE_pide_confirmacion_en_cualquier_entorno()
    {
        var d = SafetyPolicy.Evaluate(Profile(), StatementClassifier.Classify("DELETE FROM T"));
        Assert.Equal(SafetyVerdict.Confirm, d.Verdict);
        Assert.Contains(d.Reasons, r => r.Contains("TODAS"));
    }

    [Fact]
    public void Produccion_y_sin_WHERE_suma_las_dos_razones() =>
        Assert.Equal(2, SafetyPolicy.Evaluate(Profile(ProfileEnvironment.Produccion), StatementClassifier.Classify("UPDATE T SET A = 1")).Reasons.Count);

    [Fact]
    public void Vacio_se_bloquea() => Assert.Equal(SafetyVerdict.Block, Verdict(Profile(), "  -- nada "));
}
