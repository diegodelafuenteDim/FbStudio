using System.Globalization;
using System.Text;
using System.Text.Json;
using FbStudio.Core.Results;

namespace FbStudio.Core.Export;

/// <summary>
/// El resultado como tabla de Markdown, para pegar en un ticket, un chat o un documento: encabezado, los números alineados
/// a la derecha, «|» escapado y los saltos de línea como &lt;br&gt;.
/// </summary>
public static class MarkdownTable
{
    public static void Write(TextWriter w, IReadOnlyList<ColumnInfo> columns, IEnumerable<IReadOnlyList<object?>> rows, Func<ColumnInfo, int>? position = null)
    {
        position ??= c => c.Ordinal;
        w.Write("| " + string.Join(" | ", columns.Select(c => Escape(c.Name))) + " |\n");
        w.Write("|" + string.Concat(columns.Select(c => c.IsNumeric ? " ---: |" : " --- |")) + "\n");
        foreach (var row in rows)
            w.Write("| " + string.Join(" | ", columns.Select(c => Escape(CellFormatter.Format(row[position(c)], c.DataTypeName)))) + " |\n");
    }

    public static string ToText(IReadOnlyList<ColumnInfo> columns, IEnumerable<IReadOnlyList<object?>> rows, Func<ColumnInfo, int>? position = null)
    {
        var w = new StringWriter(CultureInfo.InvariantCulture);
        Write(w, columns, rows, position);
        return w.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
}

public sealed class MarkdownExporter : IResultExporter
{
    public string Name => "Markdown";
    public string Extension => "md";

    public long Write(string path, IReadOnlyList<ColumnInfo> columns, IEnumerable<object[]> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        long count = 0;
        MarkdownTable.Write(w, columns, rows.Select(r =>
        {
            count++;
            return (IReadOnlyList<object?>)r;
        }));
        return count;
    }
}

/// <summary>
/// El resultado como JSON: una lista de objetos con el nombre de cada columna (una repetida lleva «_2», «_3»…). Los
/// números y BOOLEAN van como tales, las fechas en ISO 8601, los binarios en base64 y NULL como null.
/// </summary>
public sealed class JsonExporter : IResultExporter
{
    public string Name => "JSON";
    public string Extension => "json";

    public long Write(string path, IReadOnlyList<ColumnInfo> columns, IEnumerable<object[]> rows)
    {
        using var stream = File.Create(path);
        return Write(stream, columns, rows);
    }

    public static long Write(Stream stream, IReadOnlyList<ColumnInfo> columns, IEnumerable<object[]> rows)
    {
        var names = UniqueNames(columns);
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartArray();
        long count = 0;
        foreach (var row in rows)
        {
            w.WriteStartObject();
            for (var i = 0; i < columns.Count; i++)
            {
                w.WritePropertyName(names[i]);
                WriteValue(w, row[columns[i].Ordinal], columns[i]);
            }
            w.WriteEndObject();
            count++;
        }
        w.WriteEndArray();
        return count;
    }

    private static void WriteValue(Utf8JsonWriter w, object? value, ColumnInfo column)
    {
        switch (value)
        {
            case null or DBNull:
                w.WriteNullValue();
                break;
            case bool b:
                w.WriteBooleanValue(b);
                break;
            case string s:
                w.WriteStringValue(column.DataTypeName == "CHAR" ? s.TrimEnd() : s);
                break;
            case DateTime d:
                w.WriteStringValue(column.DataTypeName == "DATE" ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : column.DataTypeName == "TIME" ? d.ToString("HH:mm:ss.ffff", CultureInfo.InvariantCulture)
                    : d.ToString("yyyy-MM-ddTHH:mm:ss.ffff", CultureInfo.InvariantCulture));
                break;
            case TimeSpan t:
                w.WriteStringValue(t.ToString(@"hh\:mm\:ss\.ffff", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                w.WriteBase64StringValue(bytes);
                break;
            case double d when double.IsNaN(d) || double.IsInfinity(d):
                w.WriteNullValue();
                break;
            case decimal m:
                w.WriteNumberValue(m);
                break;
            case double d:
                w.WriteNumberValue(d);
                break;
            case float f:
                w.WriteNumberValue(f);
                break;
            case long or int or short or byte or sbyte or ushort or uint:
                w.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case System.Numerics.BigInteger big:
                w.WriteRawValue(big.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                w.WriteStringValue(CellFormatter.Format(value, column.DataTypeName));
                break;
        }
    }

    private static List<string> UniqueNames(IReadOnlyList<ColumnInfo> columns)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var c in columns)
        {
            var name = c.Name;
            for (var n = 2; !used.Add(name); n++) name = $"{c.Name}_{n}";
            names.Add(name);
        }
        return names;
    }
}
