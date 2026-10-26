using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace FbStudio.Core.Import;

/// <summary>
/// Un archivo para importar a una tabla (F-26): los nombres de las columnas (de la primera fila, o «Columna 1, 2…») y
/// las filas, que se leen de nuevo cada vez que se recorren (así un archivo grande no queda entero en memoria).
/// </summary>
public sealed class ImportFile
{
    private readonly Func<IEnumerable<object?[]>> _rows;

    private ImportFile(IReadOnlyList<string> headers, Func<IEnumerable<object?[]>> rows, int firstDataRow)
    {
        Headers = headers;
        _rows = rows;
        FirstDataRow = firstDataRow;
    }

    public IReadOnlyList<string> Headers { get; }

    /// <summary>En qué renglón del archivo empiezan los datos (1 = el primero), para decir dónde está una fila con error.</summary>
    public int FirstDataRow { get; }

    /// <summary>Las filas de datos: textos (CSV) o textos, números, fechas y booleanos (Excel); null en una celda vacía.</summary>
    public IEnumerable<object?[]> Rows() => _rows();

    public static bool IsExcel(string path) => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    // ---------- CSV ----------

    /// <param name="delimiter">null: se elige el que más aparece en la primera línea entre ; , tabulador y |.</param>
    /// <param name="encoding">null: UTF-8 si el archivo lo es (con o sin BOM); si no, Windows-1252 (el CSV de Excel en castellano).</param>
    public static ImportFile Csv(string path, bool hasHeader = true, char? delimiter = null, Encoding? encoding = null)
    {
        encoding ??= DetectEncoding(path);
        var sep = delimiter ?? DetectDelimiter(path, encoding);
        IEnumerable<string?[]> Read() => CsvRows(path, encoding, sep);
        var first = Read().FirstOrDefault() ?? [];
        var headers = hasHeader
            ? first.Select((h, i) => string.IsNullOrWhiteSpace(h) ? Unnamed(i) : h!.Trim()).ToList()
            : first.Select((_, i) => Unnamed(i)).ToList();
        return new ImportFile(headers, () => (hasHeader ? Read().Skip(1) : Read()).Select(r => r.Cast<object?>().ToArray()), hasHeader ? 2 : 1);
    }

    public static char DetectDelimiter(string path, Encoding encoding)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        var line = reader.ReadLine() ?? "";
        return new[] { ';', ',', '\t', '|' }.OrderByDescending(c => line.Count(x => x == c)).First();
    }

    /// <summary>UTF-8 si los bytes lo son (o hay BOM); si no, Windows-1252.</summary>
    public static Encoding DetectEncoding(string path)
    {
        var length = new FileInfo(path).Length;
        var bytes = new byte[Math.Min(1 << 20, length)];
        using (var f = File.OpenRead(path)) f.ReadExactly(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(true);
        try
        {
            // Si se leyó solo el principio, una letra con tilde puede quedar cortada al final: eso no es un error.
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetDecoder().GetCharCount(bytes, 0, bytes.Length, flush: bytes.Length == length);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return CodePagesEncoding1252();
        }
    }

    private static Encoding CodePagesEncoding1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    /// <summary>Renglones de un CSV con comillas («"a;b"», «"dijo ""hola"""»), saltos de línea dentro de comillas incluidos.</summary>
    private static IEnumerable<string?[]> CsvRows(string path, Encoding encoding, char sep)
    {
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        var fields = new List<string?>();
        var field = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        int ch;
        while ((ch = reader.Read()) >= 0)
        {
            var c = (char)ch;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"') { field.Append('"'); reader.Read(); }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0) { quoted = wasQuoted = true; continue; }
            if (c == sep) { fields.Add(Field()); continue; }
            if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                fields.Add(Field());
                if (!(fields.Count == 1 && fields[0] is null)) yield return fields.ToArray(); // renglones vacíos se saltean
                fields.Clear();
                continue;
            }
            field.Append(c);
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(Field());
            yield return fields.ToArray();
        }

        string? Field()
        {
            var text = field.ToString();
            field.Clear();
            var result = text.Length == 0 && !wasQuoted ? null : text;
            wasQuoted = false;
            return result;
        }
    }

    // ---------- Excel (.xlsx) ----------

    /// <summary>Las hojas del libro, en orden.</summary>
    public static IReadOnlyList<string> Sheets(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return SheetEntries(zip).Select(s => s.Name).ToList();
    }

    /// <summary>Caracteres de texto que puede traer un Excel (unos 100 MB en memoria): más es un archivo enorme o armado.</summary>
    public const long MaxTextChars = 50_000_000;

    public static ImportFile Excel(string path, int sheet = 0, bool hasHeader = true) => Excel(path, sheet, hasHeader, MaxTextChars);

    internal static ImportFile Excel(string path, int sheet, bool hasHeader, long maxTextChars)
    {
        IEnumerable<object?[]> Read() => XlsxRows(path, sheet, maxTextChars);
        var first = Read().FirstOrDefault() ?? [];
        var headers = hasHeader
            ? first.Select((h, i) => h is null || string.IsNullOrWhiteSpace(Convert.ToString(h, CultureInfo.InvariantCulture)) ? Unnamed(i) : Convert.ToString(h, CultureInfo.InvariantCulture)!.Trim()).ToList()
            : first.Select((_, i) => Unnamed(i)).ToList();
        return new ImportFile(headers, () => hasHeader ? Read().Skip(1) : Read(), hasHeader ? 2 : 1);
    }

    private static string Unnamed(int index) => string.Format(Resources.CoreStrings.ImportColumnN, index + 1);

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private sealed record SheetEntry(string Name, string Entry);

    /// <summary>Hojas del libro con el archivo de cada una (workbook.xml + sus relaciones).</summary>
    private static List<SheetEntry> SheetEntries(ZipArchive zip)
    {
        var rels = new Dictionary<string, string>();
        if (zip.GetEntry("xl/_rels/workbook.xml.rels") is { } relsEntry)
        {
            using var r = XmlReader.Create(relsEntry.Open());
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship" && r.GetAttribute("Id") is { } id && r.GetAttribute("Target") is { } target)
                    rels[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        }
        var sheets = new List<SheetEntry>();
        if (zip.GetEntry("xl/workbook.xml") is { } workbook)
        {
            using var r = XmlReader.Create(workbook.Open());
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
                {
                    var name = r.GetAttribute("name") ?? $"Sheet{sheets.Count + 1}";
                    var id = r.GetAttribute("id", Rel);
                    sheets.Add(new SheetEntry(name, id is not null && rels.TryGetValue(id, out var entry) ? entry : $"xl/worksheets/sheet{sheets.Count + 1}.xml"));
                }
        }
        return sheets;
    }

    private static IEnumerable<object?[]> XlsxRows(string path, int sheetIndex, long maxTextChars)
    {
        using var zip = ZipFile.OpenRead(path);
        var sheets = SheetEntries(zip);
        if (sheetIndex < 0 || sheetIndex >= sheets.Count || zip.GetEntry(sheets[sheetIndex].Entry) is not { } entry) yield break;
        var shared = SharedStrings(zip, maxTextChars);
        var dateStyles = DateStyles(zip);

        using var r = XmlReader.Create(entry.Open());
        var row = new List<object?>();
        var inRow = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "row")
            {
                row.Clear();
                inRow = true;
                if (r.IsEmptyElement) { inRow = false; continue; }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "row" && inRow)
            {
                inRow = false;
                if (row.Any(v => v is not null)) yield return row.ToArray();
            }
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "c" && inRow)
            {
                var reference = r.GetAttribute("r");
                var type = r.GetAttribute("t");
                var style = int.TryParse(r.GetAttribute("s"), out var s) ? s : 0;
                var column = reference is null ? row.Count : ColumnIndex(reference);
                object? value = null;
                if (!r.IsEmptyElement)
                {
                    string? raw = null;
                    var inline = new StringBuilder();
                    // La celda aparte: al terminar, el lector queda en su cierre. Después de leer un contenido no se
                    // llama a Read (ReadElementContentAsString ya avanzó y se salteaba el nodo siguiente).
                    using (var cell = r.ReadSubtree())
                    {
                        cell.Read();
                        while (!cell.EOF)
                        {
                            if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "v") raw = cell.ReadElementContentAsString();
                            else if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "t") inline.Append(cell.ReadElementContentAsString());
                            else cell.Read();
                        }
                    }
                    value = type switch
                    {
                        "s" => int.TryParse(raw, out var i) && i >= 0 && i < shared.Count ? shared[i] : null,
                        "inlineStr" => inline.ToString(),
                        "str" => raw,
                        "b" => raw == "1",
                        "e" => null,
                        _ when raw is null => null,
                        _ => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                            ? dateStyles.Contains(style) && d is > -657435.0 and < 2958466.0 ? DateTime.FromOADate(d) : d
                            : raw,
                    };
                }
                while (row.Count < column) row.Add(null);
                if (row.Count == column) row.Add(value);
                else row[column] = value;
            }
        }
    }

    /// <summary>Excel tiene hasta la columna XFD (16.384): una referencia más allá es un archivo roto o armado a mano.</summary>
    public const int MaxColumns = 16_384;

    /// <summary>«AB12» → 27 (columna, desde 0); nunca pasa de la última columna de Excel.</summary>
    internal static int ColumnIndex(string reference)
    {
        var n = 0;
        foreach (var c in reference)
        {
            if (!char.IsAsciiLetter(c)) break;
            n = n * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            if (n > MaxColumns) return MaxColumns - 1;
        }
        return Math.Max(0, n - 1);
    }

    private static List<string> SharedStrings(ZipArchive zip, long maxTextChars)
    {
        var list = new List<string>();
        long chars = 0;
        if (zip.GetEntry("xl/sharedStrings.xml") is not { } entry) return list;
        using var r = XmlReader.Create(entry.Open());
        var current = new StringBuilder();
        r.Read();
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "t")
            {
                var text = r.ReadElementContentAsString(); // ya avanzó: no se llama a Read
                if ((chars += text.Length) > maxTextChars)
                    throw new InvalidDataException(string.Format(Resources.CoreStrings.ImportTooMuchText, maxTextChars));
                current.Append(text);
                continue;
            }
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "si" && !r.IsEmptyElement) current.Clear();
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "si") list.Add("");
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "si") list.Add(current.ToString());
            r.Read();
        }
        return list;
    }

    /// <summary>Los estilos de celda que son fechas u horas (formatos 14 a 22 y 45 a 47, o propios con d, m, y, h o s).</summary>
    private static HashSet<int> DateStyles(ZipArchive zip)
    {
        var result = new HashSet<int>();
        if (zip.GetEntry("xl/styles.xml") is not { } entry) return result;
        var custom = new Dictionary<int, string>();
        var xfs = new List<int>();
        using var r = XmlReader.Create(entry.Open());
        var inCellXfs = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "numFmt" && int.TryParse(r.GetAttribute("numFmtId"), out var id))
                custom[id] = r.GetAttribute("formatCode") ?? "";
            else if (r.NodeType == XmlNodeType.Element && r.LocalName == "cellXfs") inCellXfs = !r.IsEmptyElement;
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") inCellXfs = false;
            else if (inCellXfs && r.NodeType == XmlNodeType.Element && r.LocalName == "xf")
                xfs.Add(int.TryParse(r.GetAttribute("numFmtId"), out var f) ? f : 0);
        }
        for (var i = 0; i < xfs.Count; i++)
        {
            var f = xfs[i];
            if (f is >= 14 and <= 22 or >= 45 and <= 47 || (custom.TryGetValue(f, out var code) && IsDateFormat(code))) result.Add(i);
        }
        return result;
    }

    private static bool IsDateFormat(string code)
    {
        var plain = new StringBuilder();
        var inQuotes = false;
        var inBrackets = false;
        foreach (var c in code)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == '[') { inBrackets = true; continue; }
            if (c == ']') { inBrackets = false; continue; }
            if (!inQuotes && !inBrackets) plain.Append(char.ToLowerInvariant(c));
        }
        var text = plain.ToString();
        return text.IndexOfAny(['d', 'y', 'h', 's']) >= 0 || (text.Contains('m') && !text.Contains('0'));
    }
}
