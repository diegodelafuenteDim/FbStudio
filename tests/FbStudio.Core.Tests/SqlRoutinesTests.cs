using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>Qué procedimientos y funciones usa un SELECT (D-73).</summary>
public class SqlRoutinesTests
{
    private static readonly MetadataCatalog Catalog = new([("TURNOS", "ID")], [("P_CONTAR", "N")])
    {
        Functions = ["F_DOBLE", "UDF_LEFT"],
        StoredFunctions = ["F_DOBLE"],
    };

    [Theory]
    [InlineData("SELECT * FROM P_CONTAR", "P_CONTAR")]                                    // por el catálogo
    [InlineData("select * from p_turnos_desde(1, 5)", "P_TURNOS_DESDE")]                  // con paréntesis: procedimiento
    [InlineData("SELECT T.ID FROM TURNOS T JOIN P_CONTAR C ON 1 = 1", "P_CONTAR")]
    [InlineData("SELECT F_DOBLE(ID) FROM TURNOS", "F_DOBLE")]
    [InlineData("SELECT PKG_TURNOS.CANTIDAD() FROM RDB$DATABASE", "PKG_TURNOS.CANTIDAD")]
    [InlineData("SELECT * FROM PKG_TURNOS.DESDE(1)", "PKG_TURNOS.DESDE")]
    [InlineData("SELECT * FROM \"P_Mixto\"(1)", "P_Mixto")]
    [InlineData("SELECT F_DOBLE(1), F_DOBLE(2) FROM P_CONTAR", "F_DOBLE,P_CONTAR")]       // sin repetir, en orden
    public void Encuentra_lo_que_puede_escribir(string sql, string expected) =>
        Assert.Equal(expected.Split(','), SqlRoutines.Used(sql, Catalog));

    [Theory]
    [InlineData("SELECT * FROM TURNOS")]
    [InlineData("SELECT T.ID, COUNT(*) FROM TURNOS T GROUP BY T.ID")]                     // funciones del motor y ALIAS.COLUMNA
    [InlineData("SELECT UDF_LEFT(OBS, 2) FROM TURNOS")]                                   // una UDF no escribe
    [InlineData("SELECT * FROM (SELECT ID FROM TURNOS) X")]                               // tabla derivada
    [InlineData("SELECT 'FROM P_CONTAR' FROM RDB$DATABASE")]                              // dentro de un texto
    public void Sin_rutinas_que_escriban(string sql) => Assert.Empty(SqlRoutines.Used(sql, Catalog));

    [Fact]
    public void Sin_catalogo_alcanza_la_sintaxis() =>
        Assert.Equal(["PKG_TURNOS.CANTIDAD", "P_TURNOS_DESDE"],                             // P_CONTAR sin paréntesis necesita el catálogo
            SqlRoutines.Used("SELECT PKG_TURNOS.CANTIDAD() FROM P_TURNOS_DESDE(1) JOIN P_CONTAR ON 1 = 1", MetadataCatalog.Empty));
}
