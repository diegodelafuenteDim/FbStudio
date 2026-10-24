using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FbStudio.Core.Results;

namespace FbStudio.Core.Export;

/// <summary>
/// Excel (.xlsx) escrito en streaming, sin librerías: SpreadsheetML mínimo dentro de un zip. Números como números,
/// fechas como fechas (con formato dd/mm/aaaa), encabezado en negrita con filtro y fila congelada. Pasado el límite de
/// Excel (1.048.576 filas por hoja) sigue en otra hoja.
/// </summary>
public sealed class XlsxExporter(int maxRowsPerSheet = XlsxExporter.ExcelMaxRows) : IResultExporter
{
    public const int ExcelMaxRows = 1_048_576;
    private const int MaxCellText = 32_767;
    private static readonly DateTime Epoch = new(1899, 12, 30);
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Estilos (índices de cellXfs en styles.xml).
    private const int StyleHeader = 1, StyleDate = 2, StyleDateTime = 3, StyleTime = 4;

    public string Name => "Excel";
    public string Extension => "xlsx";

    public long Write(string path, IReadOnlyList<ColumnInfo> columns, IEnumerable<object[]> rows)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        long written = 0;
        var sheets = 0;
        using var enumerator = rows.GetEnumerator();
        var pending = enumerator.MoveNext();
        do
        {
            sheets++;
            var entry = zip.CreateEntry($"xl/worksheets/sheet{sheets}.xml", CompressionLevel.Fastest);
            using var stream = entry.Open();
            using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CheckCharacters = false });
            var rowsInSheet = WriteSheet(xml, columns, enumerator, ref pending, maxRowsPerSheet - 1);
            written += rowsInSheet;
        }
        while (pending);

        WriteEntry(zip, "[Content_Types].xml", ContentTypes(sheets));
        WriteEntry(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
            """);
        WriteEntry(zip, "xl/workbook.xml", Workbook(sheets));
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets));
        WriteEntry(zip, "xl/styles.xml", Styles);
        return written;
    }

    private static long WriteSheet(XmlWriter xml, IReadOnlyList<ColumnInfo> columns, IEnumerator<object[]> rows, ref bool pending, int maxDataRows)
    {
        xml.WriteStartDocument(true);
        xml.WriteStartElement("worksheet", Ns);

        // Encabezado congelado.
        xml.WriteStartElement("sheetViews", Ns);
        xml.WriteStartElement("sheetView", Ns);
        xml.WriteAttributeString("workbookViewId", "0");
        xml.WriteStartElement("pane", Ns);
        xml.WriteAttributeString("ySplit", "1");
        xml.WriteAttributeString("topLeftCell", "A2");
        xml.WriteAttributeString("activePane", "bottomLeft");
        xml.WriteAttributeString("state", "frozen");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();

        if (columns.Count > 0)
        {
            xml.WriteStartElement("cols", Ns);
            for (var i = 0; i < columns.Count; i++)
            {
                xml.WriteStartElement("col", Ns);
                xml.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("width", Width(columns[i]).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("customWidth", "1");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }

        xml.WriteStartElement("sheetData", Ns);
        xml.WriteStartElement("row", Ns);
        foreach (var c in columns) WriteText(xml, c.Name, StyleHeader);
        xml.WriteEndElement();

        long count = 0;
        while (pending && count < maxDataRows)
        {
            var row = rows.Current;
            xml.WriteStartElement("row", Ns);
            foreach (var c in columns) WriteCell(xml, row[c.Ordinal], c);
            xml.WriteEndElement();
            count++;
            pending = rows.MoveNext();
        }
        xml.WriteEndElement(); // sheetData

        if (columns.Count > 0)
        {
            xml.WriteStartElement("autoFilter", Ns);
            xml.WriteAttributeString("ref", $"A1:{ColumnLetters(columns.Count)}{count + 1}");
            xml.WriteEndElement();
        }
        xml.WriteEndElement(); // worksheet
        xml.WriteEndDocument();
        return count;
    }

    private static void WriteCell(XmlWriter xml, object? value, ColumnInfo column)
    {
        switch (value)
        {
            case null or DBNull:
                xml.WriteStartElement("c", Ns);
                xml.WriteEndElement();
                break;
            case string s:
                WriteText(xml, column.DataTypeName == "CHAR" ? s.TrimEnd() : s, 0);
                break;
            case bool b:
                WriteValue(xml, b ? "1" : "0", 0, "b");
                break;
            case DateTime d:
                var serial = (d - Epoch).TotalDays;
                WriteValue(xml, serial.ToString("R", CultureInfo.InvariantCulture), column.DataTypeName == "DATE" ? StyleDate : StyleDateTime);
                break;
            case DateTimeOffset o:
                WriteValue(xml, (o.DateTime - Epoch).TotalDays.ToString("R", CultureInfo.InvariantCulture), StyleDateTime);
                break;
            case TimeSpan t:
                WriteValue(xml, t.TotalDays.ToString("R", CultureInfo.InvariantCulture), StyleTime);
                break;
            case TimeOnly t:
                WriteValue(xml, t.ToTimeSpan().TotalDays.ToString("R", CultureInfo.InvariantCulture), StyleTime);
                break;
            case decimal or double or float or int or long or short or byte or sbyte or uint or ulong or ushort:
                var number = ((IFormattable)value).ToString(value is double or float ? "R" : null, CultureInfo.InvariantCulture);
                // Excel no tiene NaN ni infinito.
                if (value is double dbl && (double.IsNaN(dbl) || double.IsInfinity(dbl))) WriteText(xml, number, 0);
                else WriteValue(xml, number, 0);
                break;
            case System.Numerics.BigInteger big:
                WriteText(xml, big.ToString(CultureInfo.InvariantCulture), 0); // INT128: más de 15 dígitos se perderían como número
                break;
            case byte[] bytes:
                WriteText(xml, bytes.Length > 1024 ? string.Format(Resources.CoreStrings.XlsxBinary, bytes.Length) : Convert.ToHexString(bytes), 0);
                break;
            default:
                WriteText(xml, CellFormatter.Format(value, column.DataTypeName), 0);
                break;
        }
    }

    private static void WriteValue(XmlWriter xml, string value, int style, string? type = null)
    {
        xml.WriteStartElement("c", Ns);
        if (style != 0) xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        if (type is not null) xml.WriteAttributeString("t", type);
        xml.WriteElementString("v", Ns, value);
        xml.WriteEndElement();
    }

    private static void WriteText(XmlWriter xml, string text, int style)
    {
        xml.WriteStartElement("c", Ns);
        if (style != 0) xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("t", "inlineStr");
        xml.WriteStartElement("is", Ns);
        xml.WriteStartElement("t", Ns);
        var clean = CleanXml(text.Length > MaxCellText ? text[..MaxCellText] : text);
        if (clean.Length > 0 && (char.IsWhiteSpace(clean[0]) || char.IsWhiteSpace(clean[^1])))
            xml.WriteAttributeString("xml", "space", null, "preserve");
        xml.WriteString(clean);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>XML 1.0 no admite caracteres de control (salvo tab y saltos de línea): se quitan.</summary>
    private static string CleanXml(string s)
    {
        if (!s.Any(c => c < 0x20 && c is not ('\t' or '\n' or '\r') || c is '￾' or '￿')) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (!(c < 0x20 && c is not ('\t' or '\n' or '\r') || c is '￾' or '￿')) sb.Append(c);
        return sb.ToString();
    }

    private static int Width(ColumnInfo c)
    {
        var byType = c.DataTypeName switch
        {
            "DATE" => 12,
            "TIMESTAMP" => 20,
            "TIME" => 10,
            _ when c.IsNumeric => 12,
            _ => 18,
        };
        return Math.Clamp(Math.Max(byType, c.Name.Length + 4), 8, 60);
    }

    public static string ColumnLetters(int number)
    {
        var sb = new StringBuilder();
        while (number > 0)
        {
            var rem = (number - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            number = (number - 1) / 26;
        }
        return sb.ToString();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        var bytes = new UTF8Encoding(false).GetBytes(content.ReplaceLineEndings("\n").Trim());
        stream.Write(bytes);
    }

    private static string ContentTypes(int sheets)
    {
        var sb = new StringBuilder("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            """);
        for (var i = 1; i <= sheets; i++)
            sb.Append($"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        return sb.Append("</Types>").ToString();
    }

    private static string Workbook(int sheets)
    {
        var sb = new StringBuilder("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>
            """);
        for (var i = 1; i <= sheets; i++) sb.Append($"""<sheet name="{System.Security.SecurityElement.Escape(i == 1 ? Resources.CoreStrings.XlsxSheet : $"{Resources.CoreStrings.XlsxSheet} {i}")}" sheetId="{i}" r:id="rId{i}"/>""");
        return sb.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRels(int sheets)
    {
        var sb = new StringBuilder("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            """);
        for (var i = 1; i <= sheets; i++)
            sb.Append($"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        sb.Append($"""<Relationship Id="rId{sheets + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        return sb.Append("</Relationships>").ToString();
    }

    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="3"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/><numFmt numFmtId="165" formatCode="dd/mm/yyyy hh:mm:ss"/><numFmt numFmtId="166" formatCode="hh:mm:ss"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="11"/><name val="Calibri"/><family val="2"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
        """;
}
