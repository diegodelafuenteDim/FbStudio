using System.Globalization;
using System.Text;
using FbStudio.Core.Export;
using FbStudio.Core.Import;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>Leer CSV y Excel para importar a una tabla (F-26).</summary>
public sealed class ImportFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-imp-" + Guid.NewGuid().ToString("N"));

    private string Write(string name, string text, Encoding encoding)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text, encoding);
        return path;
    }

    [Fact]
    public void CSV_con_punto_y_coma_comillas_y_Windows_1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Write("pacientes.csv",
            "Código;Nombre;Fecha alta;Importe\r\n" +
            "1;\"Pérez; Juan\";22/10/2026;12,50\r\n" +
            "\r\n" +                                                           // renglón vacío: se saltea
            "2;\"Dijo \"\"hola\"\"\nen dos renglones\";;\r\n" +
            "3;Ñandú;01/01/2025;1.234,5",
            Encoding.GetEncoding(1252));

        var file = ImportFile.Csv(path);
        Assert.Equal(["Código", "Nombre", "Fecha alta", "Importe"], file.Headers);   // detectó 1252 y el «;»
        Assert.Equal(2, file.FirstDataRow);
        var rows = file.Rows().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(["1", "Pérez; Juan", "22/10/2026", "12,50"], rows[0]);
        Assert.Equal("Dijo \"hola\"\nen dos renglones", rows[1][1]);
        Assert.Null(rows[1][2]);                                               // celda vacía
        Assert.Equal("Ñandú", rows[2][1]);
        Assert.Equal(3, file.Rows().Count());                                  // se puede recorrer de nuevo
    }

    [Fact]
    public void Detecta_UTF8_aunque_una_tilde_quede_cortada_al_leer_el_principio()
    {
        // El primer mega (lo que se mira) termina en la mitad de la «ñ», que ocupa dos bytes.
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sql");
        try
        {
            File.WriteAllText(path, new string('a', (1 << 20) - 1) + "ñ fin", new UTF8Encoding(false));
            Assert.Equal(65001, ImportFile.DetectEncoding(path).CodePage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CSV_en_UTF8_con_comas_y_sin_encabezado()
    {
        var path = Write("datos.csv", "10,ñoño,\"a,b\"\n20,dos,c\n", new UTF8Encoding(true));
        var file = ImportFile.Csv(path, hasHeader: false);
        Assert.Equal(["Columna 1", "Columna 2", "Columna 3"], file.Headers);
        Assert.Equal(1, file.FirstDataRow);
        Assert.Equal([["10", "ñoño", "a,b"], ["20", "dos", "c"]], file.Rows().ToList());
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("AB12", 27)]
    [InlineData("XFD1", ImportFile.MaxColumns - 1)]
    [InlineData("ZZZZZZZZZZ1", ImportFile.MaxColumns - 1)]                   // un archivo armado a mano: no pide millones de columnas
    public void La_columna_de_una_celda_no_pasa_de_la_ultima_de_Excel(string reference, int column) =>
        Assert.Equal(column, ImportFile.ColumnIndex(reference));

    [Fact]
    public void Excel_con_textos_compartidos_y_un_tope_para_no_llenar_la_memoria()
    {
        // Un .xlsx mínimo como los de Excel: los textos van en sharedStrings.xml y las celdas los nombran por número.
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "compartidos.xlsx");
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Entry(string name, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            Entry("xl/workbook.xml", $"<workbook xmlns=\"{ns}\"><sheets><sheet name=\"Hoja1\" sheetId=\"1\"/></sheets></workbook>");
            Entry("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{ns}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>"
                + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>1</v></c></row></sheetData></worksheet>");
            Entry("xl/sharedStrings.xml", $"<sst xmlns=\"{ns}\"><si><t>NOMBRE</t></si><si><t>Ana María</t></si></sst>");
        }

        var file = ImportFile.Excel(path);
        Assert.Equal(["NOMBRE"], file.Headers);
        Assert.Equal("Ana María", Assert.Single(file.Rows())[0]);
        var error = Assert.Throws<InvalidDataException>(() => ImportFile.Excel(path, 0, true, maxTextChars: 8));
        Assert.Contains("demasiado texto", error.Message);
    }

    [Fact]
    public void Excel_de_ida_y_vuelta_con_numeros_fechas_y_textos()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "libro.xlsx");
        ColumnInfo[] columns = [new(0, "ID", "INTEGER"), new(1, "PACIENTE", "VARCHAR"), new(2, "FECHA", "DATE"), new(3, "IMPORTE", "NUMERIC")];
        new XlsxExporter().Write(path, columns,
        [
            [1, "Ana", new DateTime(2026, 10, 22), 12.5m],
            [2, null!, new DateTime(2026, 1, 2), 0m],
        ]);

        Assert.True(ImportFile.IsExcel(path));
        Assert.Single(ImportFile.Sheets(path));
        var file = ImportFile.Excel(path);
        Assert.Equal(["ID", "PACIENTE", "FECHA", "IMPORTE"], file.Headers);
        var rows = file.Rows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1.0, rows[0][0]);
        Assert.Equal("Ana", rows[0][1]);
        Assert.Equal(new DateTime(2026, 10, 22), rows[0][2]);                   // con formato de fecha: fecha
        Assert.Equal(12.5, rows[0][3]);
        Assert.Null(rows[1][1]);
    }

    [Fact]
    public void Relaciona_las_columnas_por_nombre()
    {
        var map = ImportMapping.Auto(["Código", "Nombre del paciente", "fecha_alta", "Otro"], ["CODIGO", "NOMBREDELPACIENTE", "FECHA_ALTA", "OBS"]);
        Assert.Equal([0, 1, 2, null], map);
    }

    [Fact]
    public void Convierte_segun_el_tipo_de_la_columna()
    {
        var ar = CultureInfo.GetCultureInfo("es-AR");
        Assert.Equal("12.50", ImportMapping.Convert("12,50", "NUMERIC(12,2)", ar));
        Assert.Equal("1234.5", ImportMapping.Convert("1.234,5", "NUMERIC(12,2)", ar));
        Assert.Equal(new DateTime(2026, 10, 22), ImportMapping.Convert("22/10/2026", "DATE", ar));
        Assert.Equal(new DateTime(2026, 10, 22, 8, 30, 0), ImportMapping.Convert("22/10/2026 08:30", "TIMESTAMP", ar));
        Assert.Equal("2026-10-22", ImportMapping.Convert("2026-10-22", "DATE", ar));    // ISO: lo convierte Firebird
        Assert.Equal(true, ImportMapping.Convert("Sí", "BOOLEAN", ar));
        Assert.Equal(false, ImportMapping.Convert("0", "BOOLEAN", ar));
        Assert.Null(ImportMapping.Convert("", "VARCHAR(30)", ar));
        Assert.Null(ImportMapping.Convert("  ", "INTEGER", ar));
        Assert.Equal("", ImportMapping.Convert("", "VARCHAR(30)", ar, emptyIsNull: false));
        Assert.Equal("00123", ImportMapping.Convert("00123", "VARCHAR(10)", ar));

        // De Excel: números y fechas ya tipados.
        Assert.Equal(15L, ImportMapping.Convert(15.0, "INTEGER", ar));
        Assert.Equal(12.5m, ImportMapping.Convert(12.5, "NUMERIC(12,2)", ar));
        Assert.Equal("42", ImportMapping.Convert(42.0, "VARCHAR(10)", ar));
        Assert.Equal(new DateTime(2026, 10, 22), ImportMapping.Convert(new DateTime(2026, 10, 22).ToOADate(), "DATE", ar)); // número sin formato
        Assert.Equal("2026-10-22", ImportMapping.Convert(new DateTime(2026, 10, 22), "VARCHAR(20)", ar));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
