using System.Globalization;
using FbStudio.Core.Import;

namespace FbStudio.Core.Tests;

/// <summary>Importar a una tabla nueva (F-26): nombres y tipos a partir del archivo.</summary>
public class ImportSchemaTests
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

    [Theory]
    [InlineData("Fecha de alta", "FECHA_DE_ALTA")]
    [InlineData("  Año / Número ", "ANO_NUMERO")]
    [InlineData("2026 ventas", "C_2026_VENTAS")]
    [InlineData("¿?", "COLUMNA")]
    [InlineData("una columna con un nombre larguísimo de verdad", "UNA_COLUMNA_CON_UN_NOMBRE_LARGU")]
    public void Arma_nombres_sin_comillas(string text, string expected) => Assert.Equal(expected, ImportSchema.Identifier(text, "COLUMNA"));

    [Fact]
    public void La_tabla_se_llama_como_el_archivo() =>
        Assert.Equal("PACIENTES_2026", ImportSchema.TableName(Path.Combine("datos", "Pacientes 2026.xlsx"))); // con el separador de cada sistema

    [Theory]
    [InlineData("INTEGER", "1", "-20", "300", "")]
    [InlineData("BIGINT", "1", "9999999999")]
    [InlineData("NUMERIC(18,2)", "12,50", "3", "1.234,99")]                   // la coma decimal de la PC, como al importar
    [InlineData("NUMERIC(18,3)", "0,125", "7")]
    [InlineData("DATE", "22/10/2026", "2026-01-02")]
    [InlineData("TIMESTAMP", "2026-01-02 08:30", "22/10/2026")]
    [InlineData("VARCHAR(10)", "00123", "00456")]                              // códigos con ceros adelante: texto
    [InlineData("VARCHAR(20)", "1", "Pérez", "una frase más larga")]
    [InlineData("VARCHAR(100)", "", "  ")]                                     // sin datos: un texto cualquiera
    public void Elige_el_tipo_segun_los_valores(string expected, params string[] values) =>
        Assert.Equal(expected, ImportSchema.InferType(values, Ar));

    [Fact]
    public void De_Excel_entiende_numeros_fechas_y_si_no()
    {
        Assert.Equal("INTEGER", ImportSchema.InferType([1.0, 25.0, null], Ar));
        Assert.Equal("NUMERIC(18,2)", ImportSchema.InferType([1.5, 2.0], Ar));
        Assert.Equal("DATE", ImportSchema.InferType([new DateTime(2026, 1, 2), new DateTime(2026, 3, 4)], Ar));
        Assert.Equal("TIMESTAMP", ImportSchema.InferType([new DateTime(2026, 1, 2, 8, 30, 0)], Ar));
        Assert.Equal("BOOLEAN", ImportSchema.InferType([true, false, null], Ar));
        Assert.Equal("VARCHAR(10)", ImportSchema.InferType([true, "sí"], Ar));
        Assert.Equal("BLOB SUB_TYPE TEXT", ImportSchema.InferType([new string('x', ImportSchema.MaxVarchar + 1)], Ar));
    }

    [Fact]
    public void Una_columna_por_cada_una_del_archivo_sin_repetir_nombres_y_su_create_table()
    {
        var columns = ImportSchema.Infer(["Nombre", "nombre", "Importe", ""],
            [["Pérez", "x", "12,50", "1"], ["Gómez", "y", "3", null]], Ar);
        Assert.Equal(["NOMBRE", "NOMBRE_2", "IMPORTE", "COLUMNA_4"], columns.Select(c => c.Name));
        Assert.Equal(["VARCHAR(10)", "VARCHAR(10)", "NUMERIC(18,2)", "INTEGER"], columns.Select(c => c.Type));
        Assert.Equal([0, 1, 2, 3], columns.Select(c => c.FileIndex));

        var sql = ImportSchema.CreateTableSql("VENTAS", [new("ID", "INTEGER", 0), new("Fecha alta", "DATE", 1)]);
        Assert.Equal("CREATE TABLE VENTAS (" + Environment.NewLine + "    ID           INTEGER," + Environment.NewLine +
                     "    \"Fecha alta\" DATE" + Environment.NewLine + ")", sql);
    }
}
