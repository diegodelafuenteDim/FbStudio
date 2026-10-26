using System.Text;
using FbStudio.Core.Export;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>El resultado como tabla de Markdown y como JSON.</summary>
public class TextExportersTests
{
    private static readonly ColumnInfo[] Columns =
    [
        new(0, "ID", "INTEGER"),
        new(1, "OBS", "VARCHAR"),
        new(2, "FECHA", "DATE"),
        new(3, "IMPORTE", "NUMERIC"),
        new(4, "ID", "BIGINT"),
    ];

    private static readonly object[][] Rows =
    [
        [1, "a|b\nsigue", new DateTime(2026, 10, 24), 10.50m, 7L],
        [2, DBNull.Value, new DateTime(2026, 1, 2), -3m, 8L],
    ];

    [Fact]
    public void Markdown_con_numeros_a_la_derecha_y_caracteres_escapados()
    {
        Assert.Equal(
            "| ID | OBS | FECHA | IMPORTE | ID |\n" +
            "| ---: | --- | --- | ---: | ---: |\n" +
            "| 1 | a\\|b<br>sigue | 2026-10-24 | 10.50 | 7 |\n" +
            "| 2 | NULL | 2026-01-02 | -3 | 8 |\n",
            MarkdownTable.ToText(Columns, Rows));
    }

    [Fact]
    public void Json_con_tipos_y_nombres_repetidos()
    {
        using var stream = new MemoryStream();
        object[][] rows = [Rows[0], [2, DBNull.Value, new DateTime(2026, 1, 2), -3m, new byte[] { 1, 2 }]];
        Assert.Equal(2, JsonExporter.Write(stream, Columns, rows));
        var json = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
        Assert.Equal(
            "[\n  {\n    \"ID\": 1,\n    \"OBS\": \"a|b\\nsigue\",\n    \"FECHA\": \"2026-10-24\",\n    \"IMPORTE\": 10.50,\n    \"ID_2\": 7\n  },\n" +
            "  {\n    \"ID\": 2,\n    \"OBS\": null,\n    \"FECHA\": \"2026-01-02\",\n    \"IMPORTE\": -3,\n    \"ID_2\": \"AQI=\"\n  }\n]", json);
    }
}
