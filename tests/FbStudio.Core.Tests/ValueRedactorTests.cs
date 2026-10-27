using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>El SQL de otros programas sin valores, para el conector MCP (D-118).</summary>
public sealed class ValueRedactorTests
{
    [Theory]
    [InlineData("SELECT * FROM PACIENTES WHERE DNI = '20123456' AND EDAD > 65",
                "SELECT * FROM PACIENTES WHERE DNI = ? AND EDAD > ?")]
    [InlineData("UPDATE T SET NOMBRE = 'O''Brien', SALDO = -12.50 WHERE ID IN (1, 2, 3)",
                "UPDATE T SET NOMBRE = ?, SALDO = ? WHERE ID IN (?)")]
    [InlineData("select x from t /* paciente Pérez */ where a = 1 -- lo pidió Juana\n and b = 'c'",
                "select x from t where a = ? and b = ?")]
    [InlineData("SELECT '-- no es comentario', '/* tampoco */' FROM RDB$DATABASE",
                "SELECT ?, ? FROM RDB$DATABASE")]
    [InlineData("SELECT TABLA_2.COL3 FROM TABLA_2 WHERE FECHA = DATE '2026-10-27'",
                "SELECT TABLA_2.COL3 FROM TABLA_2 WHERE FECHA = DATE ?")]
    [InlineData("SELECT x'0A0B' FROM T WHERE NOMBRE = 'sin cerrar", "SELECT x? FROM T WHERE NOMBRE = ?")]
    public void Reemplaza_valores_y_quita_comentarios(string sql, string expected) =>
        Assert.Equal(expected, ValueRedactor.Statement(sql));

    [Fact]
    public void Corta_lo_largo_y_tolera_vacios()
    {
        Assert.Equal("", ValueRedactor.Statement(null));
        Assert.Equal("", ValueRedactor.Statement("   "));
        var cut = ValueRedactor.Statement("SELECT " + string.Join(", ", Enumerable.Range(0, 500).Select(i => $"COL{i}")) + " FROM T", 100);
        Assert.True(cut.Length <= 101);
        Assert.EndsWith("…", cut);
    }
}
