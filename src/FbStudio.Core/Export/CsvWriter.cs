using System.Globalization;
using System.Text;
using FbStudio.Core.Results;

namespace FbStudio.Core.Export;

/// <param name="Separator">';' por defecto: es el separador de listas de Excel en español (Argentina).</param>
/// <param name="DecimalSeparator">',' por defecto, por la misma razón.</param>
/// <param name="Utf8Bom">Con BOM, Excel reconoce UTF-8 y muestra bien los acentos.</param>
/// <param name="ProtectFormulas">
/// Un texto que empieza con = + - @ (o tabulación o retorno) Excel lo toma como fórmula al abrir el CSV: se le pone una
/// «'» adelante para que quede como texto (un dato de la base no puede armar un vínculo o un comando en la planilla).
/// </param>
public sealed record CsvOptions(char Separator = ';', char DecimalSeparator = ',', bool Header = true, bool Utf8Bom = true,
    bool ProtectFormulas = true)
{
    public static CsvOptions ExcelAr { get; } = new();

    public Encoding Encoding => new UTF8Encoding(Utf8Bom);
}

/// <summary>CSV según RFC 4180 (comillas solo cuando hacen falta), pensado para abrir en Excel.</summary>
public static class CsvWriter
{
    public static void WriteHeader(TextWriter w, IReadOnlyList<ColumnInfo> columns, CsvOptions o)
    {
        if (!o.Header) return;
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) w.Write(o.Separator);
            w.Write(Quote(columns[i].Name, o.Separator));
        }
        w.Write("\r\n");
    }

    public static void WriteRow(TextWriter w, object[] row, IReadOnlyList<ColumnInfo> columns, CsvOptions o)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) w.Write(o.Separator);
            w.Write(Quote(FormatValue(row[columns[i].Ordinal], columns[i], o), o.Separator));
        }
        w.Write("\r\n");
    }

    /// <summary>NULL → campo vacío; decimales con el separador elegido y sin separador de miles; fechas ISO.</summary>
    public static string FormatValue(object? value, ColumnInfo column, CsvOptions o) => value switch
    {
        null or DBNull => "",
        decimal or double or float => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture).Replace('.', o.DecimalSeparator),
        byte[] b => Convert.ToHexString(b),
        string s => Protect(s.TrimEnd(), o), // sin el relleno de los CHAR, como en la grilla
        _ => CellFormatter.Format(value, column.DataTypeName),
    };

    private static string Protect(string s, CsvOptions o) =>
        o.ProtectFormulas && s.Length > 0
        && (s[0] is '=' or '+' or '@' or '\t' or '\r'
            || (s[0] == '-' && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
            ? "'" + s
            : s;

    public static string Quote(string s, char separator) =>
        s.AsSpan().IndexOfAny([separator, '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
