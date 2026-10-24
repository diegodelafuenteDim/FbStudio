using System.Globalization;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>Parámetros «?» y «:nombre» en las consultas del editor (F-25).</summary>
public class SqlParametersTests
{
    [Fact]
    public void Cambia_los_nombres_por_signos_y_repite_el_valor_del_mismo_nombre()
    {
        var p = SqlParameters.Find("SELECT * FROM TURNOS WHERE FECHA BETWEEN :desde AND :hasta OR FECHA_ALTA >= :Desde");
        Assert.Equal("SELECT * FROM TURNOS WHERE FECHA BETWEEN ? AND ? OR FECHA_ALTA >= ?", p.Sql);
        Assert.Equal(["desde", "hasta", "desde"], p.Positions);                // sin distinguir mayúsculas
        Assert.Equal(["desde", "hasta"], p.Names);
        var values = p.Values(new Dictionary<string, object?> { ["desde"] = "2026-01-01", ["hasta"] = null });
        Assert.Equal(["2026-01-01", null, "2026-01-01"], values);
    }

    [Fact]
    public void Los_signos_sin_nombre_se_numeran()
    {
        var p = SqlParameters.Find("UPDATE T SET A = ?, B = ? WHERE ID = :id");
        Assert.Equal("UPDATE T SET A = ?, B = ? WHERE ID = ?", p.Sql);
        Assert.Equal(["#1", "#2", "id"], p.Positions);
        Assert.Equal(["#1", "#2", "id"], p.Names);
    }

    [Theory]
    [InlineData("SELECT 'a:b ? c' FROM RDB$DATABASE")]                        // dentro de un texto
    [InlineData("SELECT \"COL:X?\" FROM T")]                                  // identificador entre comillas
    [InlineData("SELECT 1 FROM T -- ¿y si :x?\n")]                            // comentario de línea
    [InlineData("SELECT /* :x ? */ 1 FROM T")]                                // comentario de bloque
    [InlineData("SELECT q'{it's :x?}' FROM T")]                               // texto q'…'
    [InlineData("SELECT ARR[1:5] FROM T")]                                    // «:5» no es un nombre
    [InlineData("SELECT 'O''Brien: ?' FROM T")]                               // comilla doble dentro del texto
    public void No_busca_dentro_de_textos_comentarios_ni_comillas(string sql)
    {
        var p = SqlParameters.Find(sql);
        Assert.True(p.IsEmpty);
        Assert.Equal(sql, p.Sql);
    }

    [Theory]
    [InlineData("SELECT * FROM T WHERE ID = ?", true)]
    [InlineData("DELETE FROM T WHERE ID = :id", true)]
    [InlineData("EXECUTE PROCEDURE P_TURNOS(?)", true)]
    [InlineData("EXECUTE BLOCK AS DECLARE X INT; BEGIN SELECT 1 FROM RDB$DATABASE INTO :X; END", false)]
    [InlineData("CREATE PROCEDURE P AS DECLARE X INT; BEGIN X = :X; END", false)]
    [InlineData("COMMIT", false)]
    public void Solo_en_consultas_y_DML_no_en_PSQL(string sql, bool applies) =>
        Assert.Equal(applies, SqlParameters.Applies(StatementClassifier.Classify(sql)));

    [Fact]
    public void Convierte_lo_escrito_como_se_escribe_en_la_PC()
    {
        var ar = CultureInfo.GetCultureInfo("es-AR");
        Assert.Null(SqlParameters.ToValue("lo que sea", isNull: true, ar));
        Assert.Equal("12.5", SqlParameters.ToValue("12,5", false, ar));        // coma decimal
        Assert.Equal(new DateTime(2026, 10, 22), SqlParameters.ToValue("22/10/2026", false, ar));
        Assert.Equal(new DateTime(2026, 10, 22, 14, 30, 0), SqlParameters.ToValue("22/10/2026 14:30", false, ar));
        Assert.Equal("00123", SqlParameters.ToValue("00123", false, ar));      // sigue siendo texto: Firebird lo convierte
        Assert.Equal("2026-10-22", SqlParameters.ToValue("2026-10-22", false, ar));
        Assert.Equal("", SqlParameters.ToValue("", false, ar));                // texto vacío no es NULL

        var us = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("1,5", SqlParameters.ToValue("1,5", false, us));          // en inglés la coma no es decimal
        Assert.Equal(new DateTime(2026, 10, 22), SqlParameters.ToValue("10/22/2026", false, us));
    }
}
