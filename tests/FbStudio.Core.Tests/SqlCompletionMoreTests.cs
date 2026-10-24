using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>El autocompletado ampliado (F-33): subconsultas y WITH, funciones, paquetes, generadores y firmas.</summary>
public class SqlCompletionMoreTests
{
    private static readonly MetadataCatalog Catalog = new(
        [("TURNOS", "CODIGO"), ("TURNOS", "FECHA"), ("TURNOS", "MEDICO"), ("PACIENTES", "CODIGO"), ("PACIENTES", "EMAIL")],
        [("TURNOS_LIBRES", "FECHA"), ("TURNOS_LIBRES", "HORA"), ("ACTUALIZAR", null)])
    {
        Functions = ["F_DOBLE", "F_EDAD"],
        Packages = ["PKG_TURNOS"],
        PackageMembers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["PKG_TURNOS"] = ["CANTIDAD", "RECALCULAR"] },
        Generators = ["G_TURNOS", "G_PACIENTES"],
        Exceptions = ["E_TURNO"],
        Signatures = new Dictionary<string, RoutineSignature>(StringComparer.OrdinalIgnoreCase)
        {
            ["TURNOS_LIBRES"] = new("TURNOS_LIBRES", [new("DESDE", "DATE"), new("HASTA", "DATE", HasDefault: true)]),
            ["F_DOBLE"] = new("F_DOBLE", [new("X", "INTEGER")], "INTEGER"),
            ["PKG_TURNOS.CANTIDAD"] = new("PKG_TURNOS.CANTIDAD", [new("MEDICO", "INTEGER")], "INTEGER"),
            ["PKG_TURNOS.RECALCULAR"] = new("PKG_TURNOS.RECALCULAR", []),
        },
    };

    private static string[] Texts(string sqlWithCaret)
    {
        var caret = sqlWithCaret.IndexOf('|');
        return SqlCompletion.Suggest(sqlWithCaret.Remove(caret, 1), caret, Catalog).Items.Select(i => i.Text).ToArray();
    }

    [Fact]
    public void Columnas_de_una_subconsulta_por_su_alias()
    {
        Assert.Equal(["CODIGO", "F", "N"], Texts("SELECT x.| FROM (SELECT t.CODIGO, t.FECHA AS F, COUNT(*) N FROM TURNOS t GROUP BY 1, 2) x"));
        Assert.Equal(["CODIGO", "EMAIL"], Texts("SELECT x.| FROM (SELECT * FROM PACIENTES) AS x"));
        Assert.Equal(["CODIGO", "FECHA", "MEDICO"], Texts("SELECT x.| FROM (SELECT t.*, 1 + 2 FROM TURNOS t) x"));   // una expresión sin alias no tiene nombre
        Assert.Equal("F", Texts("SELECT F| FROM (SELECT t.FECHA AS F FROM TURNOS t) x")[0]);   // sin escribir el alias
    }

    [Fact]
    public void Columnas_de_un_WITH()
    {
        const string With = "WITH ultimos AS (SELECT MEDICO, MAX(FECHA) AS ULTIMO FROM TURNOS GROUP BY 1) ";
        Assert.Equal(["MEDICO", "ULTIMO"], Texts(With + "SELECT u.| FROM ultimos u"));
        Assert.Equal(["ULTIMO"], Texts(With + "SELECT ULT| FROM ultimos u").Where(t => t.StartsWith("ULT")).ToArray());
        Assert.Contains("ultimos", Texts(With + "SELECT * FROM ult|"));
        Assert.Equal(["A", "B"], Texts("WITH x (A, B) AS (SELECT 1, 2 FROM RDB$DATABASE) SELECT x.| FROM x"));
        Assert.Equal(["HORA"], Texts("WITH RECURSIVE l AS (SELECT HORA FROM TURNOS_LIBRES('2026-01-01')) SELECT l.| FROM l"));
    }

    [Fact]
    public void Generadores_excepciones_funciones_y_paquetes()
    {
        Assert.Equal(["G_TURNOS"], Texts("SELECT GEN_ID(G_T|, 1) FROM RDB$DATABASE"));
        Assert.Equal(["G_TURNOS", "G_PACIENTES"], Texts("SELECT NEXT VALUE FOR | FROM RDB$DATABASE"));
        Assert.Equal(["E_TURNO"], Texts("EXECUTE BLOCK AS BEGIN EXCEPTION |"));
        Assert.Equal(["F_DOBLE"], Texts("SELECT F_D| FROM TURNOS"));
        Assert.Equal(["PKG_TURNOS"], Texts("SELECT PKG| FROM TURNOS"));
        Assert.Equal(["CANTIDAD", "RECALCULAR"], Texts("SELECT PKG_TURNOS.| FROM RDB$DATABASE"));
        // EXECUTE PROCEDURE: procedimientos y paquetes, no tablas.
        Assert.Equal(["ACTUALIZAR", "TURNOS_LIBRES", "PKG_TURNOS"], Texts("EXECUTE PROCEDURE |"));
    }

    [Fact]
    public void Kinds_de_lo_que_se_ofrece()
    {
        var items = SqlCompletion.Suggest("SELECT PKG_TURNOS.", 18, Catalog).Items;
        Assert.Equal([CompletionKind.Function, CompletionKind.Procedure], items.Select(i => i.Kind));
        Assert.Equal(CompletionKind.Generator, SqlCompletion.Suggest("SELECT GEN_ID(", 14, Catalog).Items[0].Kind);
    }

    [Fact]
    public void Solo_mira_la_sentencia_del_cursor()
    {
        // El alias «t» es TURNOS en la segunda sentencia, no PACIENTES de la primera.
        Assert.Equal(["CODIGO", "FECHA", "MEDICO"], Texts("SELECT * FROM PACIENTES t;\nSELECT t.| FROM TURNOS t"));
    }

    [Fact]
    public void Ayuda_de_parametros()
    {
        SignatureHelp? At(string sqlWithCaret)
        {
            var caret = sqlWithCaret.IndexOf('|');
            return SqlCompletion.SignatureAt(sqlWithCaret.Remove(caret, 1), caret, Catalog);
        }
        var help = At("SELECT * FROM TURNOS_LIBRES('2026-01-01', |");
        Assert.Equal(("TURNOS_LIBRES", 1, "SELECT * FROM TURNOS_LIBRES".Length), (help!.Signature.Name, help.Argument, help.OpenParen));
        Assert.Equal("TURNOS_LIBRES(DESDE DATE, HASTA DATE = …)", help.Signature.ToString());
        Assert.Equal(0, At("SELECT F_DOBLE(COALESCE(X, |0)) FROM TURNOS")!.Argument);          // dentro de COALESCE: la de afuera
        Assert.Equal("F_DOBLE(X INTEGER) RETURNS INTEGER", At("SELECT F_DOBLE(|")!.Signature.ToString());
        Assert.Equal("PKG_TURNOS.CANTIDAD", At("SELECT PKG_TURNOS.CANTIDAD(|")!.Signature.Name);
        Assert.Null(At("SELECT F_DOBLE(1)| FROM TURNOS"));                                          // ya se cerró
        Assert.Null(At("SELECT COUNT(|"));
        Assert.Null(At("SELECT F_DOBLE(1; SELECT (|"));                                             // otra sentencia
    }
}
