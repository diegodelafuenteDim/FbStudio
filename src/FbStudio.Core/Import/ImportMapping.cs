using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Import;

/// <summary>
/// Qué columna del archivo va a cada columna de la tabla (F-26) y cómo se convierte lo leído para mandarlo.
/// </summary>
public static partial class ImportMapping
{
    /// <summary>
    /// Para cada columna de la tabla, la del archivo que se llama igual (sin distinguir mayúsculas, acentos, espacios ni
    /// guiones bajos: «Fecha alta» = «FECHA_ALTA»), o null.
    /// </summary>
    public static int?[] Auto(IReadOnlyList<string> fileHeaders, IReadOnlyList<string> tableColumns)
    {
        var normalized = fileHeaders.Select(Normalize).ToList();
        return tableColumns.Select(c =>
        {
            var i = normalized.IndexOf(Normalize(c));
            return i >= 0 ? (int?)i : null;
        }).ToArray();
    }

    public static string Normalize(string name) =>
        new string(name.Normalize(NormalizationForm.FormD)
            .Where(c => char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();

    /// <summary>
    /// El valor que se manda para una columna del tipo <paramref name="columnType"/> («INTEGER», «NUMERIC(12,2)», «DATE»…)
    /// a partir de lo leído del archivo. Una celda vacía va como NULL si <paramref name="emptyIsNull"/>. En columnas de
    /// fecha, una fecha como se escribe en la PC («22/10/2026») o un número de Excel sin formato de fecha se convierten;
    /// en columnas numéricas, la coma decimal de la PC pasa a punto; en BOOLEAN se aceptan sí/no, true/false y 1/0.
    /// Lo demás va como texto y Firebird lo convierte (o avisa en qué fila no pudo).
    /// </summary>
    public static object? Convert(object? value, string columnType, CultureInfo culture, bool emptyIsNull = true)
    {
        var type = columnType.ToUpperInvariant();
        if (value is null) return null;
        if (value is string text)
        {
            if (text.Length == 0 || (emptyIsNull && text.Trim().Length == 0)) return emptyIsNull ? null : text;
            var trimmed = text.Trim();
            if (IsNumeric(type))
            {
                var dec = culture.NumberFormat.NumberDecimalSeparator;
                var group = culture.NumberFormat.NumberGroupSeparator;
                if (dec == "," && DecimalComma().IsMatch(trimmed)) return trimmed.Replace(group, "").Replace(',', '.');
                return trimmed;
            }
            if (IsDateLike(type) && SlashDate().IsMatch(trimmed) && DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out var date))
                return type.StartsWith("TIME", StringComparison.Ordinal) && !type.StartsWith("TIMESTAMP", StringComparison.Ordinal) ? date.TimeOfDay : date;
            if (type == "BOOLEAN")
                return trimmed.ToLowerInvariant() switch
                {
                    "1" or "true" or "verdadero" or "si" or "sí" or "s" or "yes" or "y" => true,
                    "0" or "false" or "falso" or "no" or "n" => false,
                    _ => trimmed,
                };
            return text;
        }
        if (value is double d)
        {
            if (IsDateLike(type) && !type.StartsWith("TIME ", StringComparison.Ordinal) && type != "TIME") return DateTime.FromOADate(d);
            if (IsText(type)) return d.ToString("R", CultureInfo.InvariantCulture);
            if (type is "INTEGER" or "SMALLINT" or "BIGINT" or "INT128" && d == Math.Floor(d)) return (long)d;
            if (type.StartsWith("NUMERIC", StringComparison.Ordinal) || type.StartsWith("DECIMAL", StringComparison.Ordinal)) return (decimal)d;
            return d;
        }
        if (value is DateTime dt && IsText(type)) return dt.ToString(dt.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (value is bool b && IsNumeric(type)) return b ? 1 : 0;
        return value;
    }

    private static bool IsNumeric(string type) =>
        type is "SMALLINT" or "INTEGER" or "BIGINT" or "INT128" or "FLOAT" or "DOUBLE PRECISION" or "REAL"
        || type.StartsWith("NUMERIC", StringComparison.Ordinal) || type.StartsWith("DECIMAL", StringComparison.Ordinal)
        || type.StartsWith("DECFLOAT", StringComparison.Ordinal);

    private static bool IsDateLike(string type) => type is "DATE" or "TIMESTAMP" or "TIME" || type.StartsWith("TIMESTAMP", StringComparison.Ordinal) || type.StartsWith("TIME ", StringComparison.Ordinal);

    private static bool IsText(string type) =>
        type.StartsWith("VARCHAR", StringComparison.Ordinal) || type.StartsWith("CHAR", StringComparison.Ordinal) || type.StartsWith("BLOB", StringComparison.Ordinal);

    [GeneratedRegex(@"^-?[\d.]*\d,\d+$")]
    private static partial Regex DecimalComma();

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}/\d{2,4}(\s+\d{1,2}:\d{2}(:\d{2})?)?$")]
    private static partial Regex SlashDate();
}
