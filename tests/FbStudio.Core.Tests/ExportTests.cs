using System.IO.Compression;
using System.Xml.Linq;
using FbStudio.Core.Export;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public sealed class ExportTests : IDisposable
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly string _folder = Directory.CreateTempSubdirectory("fbstudio-export-").FullName;

    private static readonly ColumnInfo[] Columns =
    [
        new(0, "ID", "INTEGER"),
        new(1, "NOMBRE", "VARCHAR"),
        new(2, "IMPORTE", "NUMERIC"),
        new(3, "FECHA", "DATE"),
        new(4, "ALTA", "TIMESTAMP"),
        new(5, "SEXO", "CHAR"),
    ];

    private static readonly object[][] Rows =
    [
        [1, "O'Brien <&> ñandú", 1234.5m, new DateTime(2026, 7, 1), new DateTime(2026, 7, 1, 8, 30, 15), "M   "],
        [2, null!, -0.01m, DBNull.Value, new DateTime(2026, 7, 2, 23, 59, 59, 500), "F"],
        [3, "con\u0001control y  espacios  ", 0m, new DateTime(1900, 3, 1), new DateTime(2026, 1, 1), DBNull.Value],
    ];

    [Fact]
    public void Excel_con_tipos_encabezado_y_celdas_vacias()
    {
        var path = Path.Combine(_folder, "r.xlsx");
        Assert.Equal(3, new XlsxExporter().Write(path, Columns, Rows));

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        Assert.NotNull(zip.GetEntry("xl/styles.xml"));
        var sheet = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var rows = sheet.Descendants(S + "row").ToList();
        Assert.Equal(4, rows.Count);

        Assert.Equal(["ID", "NOMBRE", "IMPORTE", "FECHA", "ALTA", "SEXO"], rows[0].Elements(S + "c").Select(c => c.Value));
        Assert.All(rows[0].Elements(S + "c"), c => Assert.Equal("1", (string?)c.Attribute("s"))); // negrita

        var first = rows[1].Elements(S + "c").ToList();
        Assert.Equal("1", first[0].Value);
        Assert.Null(first[0].Attribute("t"));                      // número
        Assert.Equal("O'Brien <&> ñandú", first[1].Value);
        Assert.Equal("1234.5", first[2].Value);
        Assert.Equal((new DateTime(2026, 7, 1) - new DateTime(1899, 12, 30)).TotalDays.ToString(System.Globalization.CultureInfo.InvariantCulture), first[3].Value);
        Assert.Equal("2", (string?)first[3].Attribute("s"));        // fecha
        Assert.Equal("3", (string?)first[4].Attribute("s"));        // fecha y hora
        Assert.Equal("M", first[5].Value);                         // CHAR sin el relleno

        var second = rows[2].Elements(S + "c").ToList();
        Assert.Equal(6, second.Count);                             // los NULL ocupan su lugar
        Assert.Equal("", second[1].Value);
        Assert.Equal("", second[3].Value);

        Assert.Equal("concontrol y  espacios  ", rows[3].Elements(S + "c").ElementAt(1).Value); // sin caracteres de control
        Assert.Equal("A1:F4", (string?)sheet.Descendants(S + "autoFilter").Single().Attribute("ref"));
    }

    [Fact]
    public void Excel_sigue_en_otra_hoja_al_pasar_el_limite()
    {
        var path = Path.Combine(_folder, "hojas.xlsx");
        var many = Enumerable.Range(1, 7).Select(i => new object[] { i, "x", 0m, DBNull.Value, DBNull.Value, "A" });
        Assert.Equal(7, new XlsxExporter(maxRowsPerSheet: 4).Write(path, Columns, many)); // 3 filas de datos por hoja

        using var zip = ZipFile.OpenRead(path);
        int Rows(int n) => XDocument.Load(zip.GetEntry($"xl/worksheets/sheet{n}.xml")!.Open()).Descendants(S + "row").Count() - 1;
        Assert.Equal([3, 3, 1], [Rows(1), Rows(2), Rows(3)]);
        var workbook = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open());
        Assert.Equal(["Resultado", "Resultado 2", "Resultado 3"], workbook.Descendants(S + "sheet").Select(s => (string)s.Attribute("name")!));
    }

    [Fact]
    public void Script_INSERT_con_literales_y_COMMIT_cada_N_filas()
    {
        var path = Path.Combine(_folder, "r.sql");
        Assert.Equal(3, new InsertScriptExporter("turnos", commitEvery: 2).Write(path, Columns, Rows));
        var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("--") && l.Length > 0).ToList();

        Assert.Equal([
            "INSERT INTO TURNOS (ID, NOMBRE, IMPORTE, FECHA, ALTA, SEXO) VALUES (1, 'O''Brien <&> ñandú', 1234.5, DATE '2026-07-01', TIMESTAMP '2026-07-01 08:30:15.0000', 'M');",
            "INSERT INTO TURNOS (ID, NOMBRE, IMPORTE, FECHA, ALTA, SEXO) VALUES (2, NULL, -0.01, NULL, TIMESTAMP '2026-07-02 23:59:59.5000', 'F');",
            "COMMIT WORK;",
            "INSERT INTO TURNOS (ID, NOMBRE, IMPORTE, FECHA, ALTA, SEXO) VALUES (3, 'con\u0001control y  espacios  ', 0, DATE '1900-03-01', TIMESTAMP '2026-01-01 00:00:00.0000', NULL);",
            "COMMIT WORK;",
        ], lines);
        Assert.Equal(5, SqlScriptSplitter.Split(File.ReadAllText(path)).Count);
    }

    [Theory]
    [InlineData(true, "TRUE")]
    [InlineData(new byte[] { 0xCA, 0xFE }, "x'CAFE'")]
    [InlineData(double.NaN, "NULL")]
    [InlineData(0.1, "0.1")]
    public void Literales_de_otros_tipos(object value, string expected) => Assert.Equal(expected, SqlLiteral.Format(value, ""));

    [Theory]
    [InlineData("SELECT * FROM clinica_turnos t WHERE t.ID = 1", "CLINICA_TURNOS")]
    [InlineData("SELECT a.X, (SELECT MAX(Y) FROM OTRA) FROM \"Mi Tabla\" a", "\"Mi Tabla\"")]
    [InlineData("SELECT * FROM (SELECT 1 FROM RDB$DATABASE)", null)]
    [InlineData("UPDATE T SET A = 1", null)]
    public void Adivina_la_tabla_del_FROM(string sql, string? expected) => Assert.Equal(expected, StatementClassifier.MainTable(sql));

    [Fact]
    public void Nombre_de_tabla_entre_comillas_se_respeta()
    {
        Assert.Equal("\"Mi Tabla\"", new InsertScriptExporter("\"Mi Tabla\"").Table);
        Assert.Equal("CLINICA_TURNOS", new InsertScriptExporter(" clinica_turnos ").Table);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
