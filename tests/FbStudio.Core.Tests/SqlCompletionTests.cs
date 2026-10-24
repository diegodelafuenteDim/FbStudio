using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class SqlCompletionTests
{
    private static readonly MetadataCatalog Catalog = new([
        ("CLINICA_TURNOS", "CODIGO"), ("CLINICA_TURNOS", "FECHA_TURNO"), ("CLINICA_TURNOS", "FECHA_ALTA"),
        ("CLINICA_PACIENTES", "CODIGO"), ("CLINICA_PACIENTES", "EMAIL"),
    ]);

    [Fact]
    public void Columnas_despues_del_alias()
    {
        const string sql = "SELECT t.FE FROM CLINICA_TURNOS t";
        var (start, items) = SqlCompletion.Suggest(sql, "SELECT t.FE".Length, Catalog);
        Assert.Equal("SELECT t.".Length, start);
        Assert.Equal(["FECHA_TURNO", "FECHA_ALTA"], items.Select(i => i.Text));
    }

    [Fact]
    public void Alias_con_AS_y_JOIN()
    {
        const string sql = "SELECT p. FROM CLINICA_TURNOS AS t JOIN CLINICA_PACIENTES p ON p.CODIGO = t.CODIGO";
        var (_, items) = SqlCompletion.Suggest(sql, "SELECT p.".Length, Catalog);
        Assert.Equal(["CODIGO", "EMAIL"], items.Select(i => i.Text));
    }

    [Fact]
    public void Tablas_y_palabras_clave_por_prefijo()
    {
        var (_, items) = SqlCompletion.Suggest("SELECT * FROM CLINICA_T", "SELECT * FROM CLINICA_T".Length, Catalog);
        Assert.Equal("CLINICA_TURNOS", Assert.Single(items).Text);
    }

    private static readonly MetadataCatalog WithProcedures = new(
        [("CLINICA_TURNOS", "CODIGO"), ("CLINICA_TURNOS", "FECHA_TURNO"), ("CLINICA_MEDICOS", "CODIGO"), ("CLINICA_MEDICOS", "NOMBRE")],
        [("CLINICA_TURNOS_LIBRES", "FECHA"), ("CLINICA_TURNOS_LIBRES", "HORA"), ("ACTUALIZAR_TODO", null)]);

    [Fact]
    public void Despues_de_FROM_van_primero_tablas_y_procedimientos()
    {
        var (start, items) = SqlCompletion.Suggest("SELECT * from cli", 17, WithProcedures);
        Assert.Equal(14, start);
        Assert.Equal(["CLINICA_MEDICOS", "CLINICA_TURNOS", "CLINICA_TURNOS_LIBRES"], items.Select(i => i.Text));
        Assert.Equal(CompletionKind.Procedure, items[2].Kind);
    }

    [Fact]
    public void Despues_de_UPDATE_no_ofrece_procedimientos() =>
        Assert.DoesNotContain(SqlCompletion.Suggest("UPDATE CLINICA_T", 16, WithProcedures).Items, i => i.Kind == CompletionKind.Procedure);

    [Fact]
    public void Columnas_de_las_tablas_del_FROM_sin_escribir_el_alias()
    {
        const string sql = "SELECT FE FROM CLINICA_TURNOS t JOIN CLINICA_MEDICOS m ON m.CODIGO = t.MEDICO";
        var items = SqlCompletion.Suggest(sql, "SELECT FE".Length, WithProcedures).Items;
        Assert.Equal("FECHA_TURNO", items[0].Text);
        Assert.Equal("CLINICA_TURNOS", items[0].Detail);

        var nombre = SqlCompletion.Suggest(sql.Replace("SELECT FE", "SELECT NOM"), "SELECT NOM".Length, WithProcedures).Items;
        Assert.Equal(("NOMBRE", "CLINICA_MEDICOS"), (nombre[0].Text, nombre[0].Detail));
    }

    [Fact]
    public void Columnas_de_un_procedimiento_por_su_alias() =>
        Assert.Equal(["FECHA", "HORA"], SqlCompletion.Suggest("SELECT l. FROM CLINICA_TURNOS_LIBRES(1) l", 9, WithProcedures).Items.Select(i => i.Text));

    [Fact]
    public void Nombre_de_tabla_como_calificador() =>
        Assert.Contains(SqlCompletion.Suggest("SELECT CLINICA_PACIENTES.E", 26, Catalog).Items, i => i.Text == "EMAIL");

    [Theory]
    [InlineData(null, "INTEGER", "NULL")]
    [InlineData("ABC   ", "CHAR", "ABC")]
    [InlineData(12.5, "DOUBLE PRECISION", "12.5")]
    public void Formato_de_celdas(object? value, string type, string expected) =>
        Assert.Equal(expected, CellFormatter.Format(value, type));

    [Fact]
    public void Formato_de_fechas()
    {
        var d = new DateTime(2026, 7, 30, 14, 5, 0);
        Assert.Equal("2026-07-30", CellFormatter.Format(d.Date, "DATE"));
        Assert.Equal("2026-07-30 14:05:00", CellFormatter.Format(d, "TIMESTAMP"));
    }
}
