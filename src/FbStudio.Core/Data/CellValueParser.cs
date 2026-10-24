using System.Globalization;
using System.Numerics;
using FbStudio.Core.Metadata;

namespace FbStudio.Core.Data;

/// <summary>
/// Lo que se escribe en una celda de la pestaña «Datos», convertido al tipo de la columna. Los números aceptan la coma
/// decimal del sistema (o el punto, si con la coma no se entiende) y nunca el separador de miles: «1.5» es 1,5 y no 15.
/// Las fechas, 2026-07-01 o 01/07/2026. Vacío es NULL salvo en los textos (un texto vacío es ''; para NULL, el menú).
/// </summary>
public static class CellValueParser
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"];
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss.FFFF", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "d/M/yyyy HH:mm", "d/M/yyyy",
    ];
    private static readonly string[] TimeFormats = [@"hh\:mm\:ss\.FFFF", @"hh\:mm\:ss", @"hh\:mm", @"h\:mm"];

    /// <summary>Se puede editar desde la grilla: no las calculadas ni los blobs binarios.</summary>
    public static bool IsEditable(DbColumn column) =>
        !column.IsComputed && !(column.Type.StartsWith("BLOB", StringComparison.Ordinal) && column.Type != "BLOB SUB_TYPE TEXT");

    public static bool IsText(DbColumn column) =>
        column.Type.StartsWith("CHAR", StringComparison.Ordinal) || column.Type.StartsWith("VARCHAR", StringComparison.Ordinal)
        || column.Type.StartsWith("CSTRING", StringComparison.Ordinal) || column.Type == "BLOB SUB_TYPE TEXT";

    public static bool TryParse(string text, DbColumn column, CultureInfo culture, out object? value, out string? error)
    {
        value = null;
        error = null;
        var type = column.Type;
        if (IsText(column))
        {
            value = text;
            return true;
        }
        var t = text.Trim();
        if (t.Length == 0) return true; // NULL

        if (type is "SMALLINT" or "INTEGER" or "BIGINT")
        {
            if (long.TryParse(t, NumberStyles.AllowLeadingSign, culture, out var n)) { value = n; return true; }
            return Fail(out error, Resources.CoreStrings.EditNotAnInteger, t);
        }
        if (type == "INT128")
        {
            if (BigInteger.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big)) { value = big; return true; }
            return Fail(out error, Resources.CoreStrings.EditNotAnInteger, t);
        }
        if (type.StartsWith("NUMERIC", StringComparison.Ordinal) || type.StartsWith("DECIMAL", StringComparison.Ordinal)
            || type is "DOUBLE PRECISION" or "FLOAT" || type.StartsWith("DECFLOAT", StringComparison.Ordinal))
        {
            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
            if (decimal.TryParse(t, styles, culture, out var d) || decimal.TryParse(t, styles, CultureInfo.InvariantCulture, out d))
            {
                value = d;
                return true;
            }
            return Fail(out error, Resources.CoreStrings.EditNotANumber, t);
        }
        if (type == "DATE")
        {
            if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { value = date.Date; return true; }
            return Fail(out error, Resources.CoreStrings.EditNotADate, t);
        }
        if (type.StartsWith("TIMESTAMP", StringComparison.Ordinal))
        {
            if (DateTime.TryParseExact(t, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) { value = ts; return true; }
            return Fail(out error, Resources.CoreStrings.EditNotATimestamp, t);
        }
        if (type.StartsWith("TIME", StringComparison.Ordinal))
        {
            if (TimeSpan.TryParseExact(t, TimeFormats, CultureInfo.InvariantCulture, out var time)) { value = time; return true; }
            return Fail(out error, Resources.CoreStrings.EditNotATime, t);
        }
        if (type == "BOOLEAN")
        {
            switch (t.ToLowerInvariant())
            {
                case "true" or "1" or "s" or "si" or "sí" or "y" or "yes" or "verdadero": value = true; return true;
                case "false" or "0" or "n" or "no" or "falso": value = false; return true;
            }
            return Fail(out error, Resources.CoreStrings.EditNotABoolean, t);
        }
        return Fail(out error, Resources.CoreStrings.EditNotEditable, column.Type);
    }

    /// <summary>El valor como se lo muestra para editarlo (sin separador de miles; fechas en formato ISO).</summary>
    public static string EditText(object? value, DbColumn column, CultureInfo culture) => value switch
    {
        null or DBNull => "",
        string s => column.Type.StartsWith("CHAR", StringComparison.Ordinal) ? s.TrimEnd() : s,
        DateTime d when column.Type == "DATE" => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString(d.TimeOfDay.Ticks % TimeSpan.TicksPerSecond == 0 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.FFFF", CultureInfo.InvariantCulture),
        TimeSpan ts => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        decimal m => m.ToString("0.############################", culture),
        double x => x.ToString("R", culture),
        float f => f.ToString("R", culture),
        IFormattable f => f.ToString(null, culture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Mismo valor aunque venga con otro tipo (INTEGER leído como int y escrito como long) o con relleno (CHAR).</summary>
    public static bool SameValue(object? a, object? b)
    {
        var nullA = a is null or DBNull;
        var nullB = b is null or DBNull;
        if (nullA || nullB) return nullA && nullB;
        if (Results.ValueComparer.IsNumber(a) && Results.ValueComparer.IsNumber(b))
        {
            try { return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture); }
            catch (OverflowException) { return Equals(a, b); }
        }
        if (a is string sa && b is string sb) return sa.TrimEnd() == sb.TrimEnd();
        if (a is byte[] ba && b is byte[] bb) return ba.AsSpan().SequenceEqual(bb);
        return Equals(a, b);
    }

    private static bool Fail(out string? error, string format, string text)
    {
        error = string.Format(format, text);
        return false;
    }
}
