using System.Globalization;

namespace FbStudio.Core.Results;

/// <summary>Qué ventanita de filtro le toca a una columna.</summary>
public enum FilterKind
{
    Text,
    Number,
    Date,
    Time,
    Boolean,
}

public enum FilterOperator
{
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    Equals,
    NotEquals,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Between,

    /// <summary>Fechas: ese día.</summary>
    Day,

    /// <summary>Fechas: hoy, este mes, últimos 7 días…</summary>
    Relative,

    Yes,
    No,
    IsNull,
    IsNotNull,
}

/// <summary>Un valor de la columna y cuántas filas lo tienen (la lista para tildar de la ventanita).</summary>
public sealed record ValueCount(object? Value, long Count);

/// <summary>
/// La ventanita de filtro de cada columna escribe el mismo texto que se puede tipear en la fila de filtros
/// (<see cref="ColumnFilter"/>): así lo elegido queda a la vista y se puede corregir a mano. Esta clase arma ese texto
/// desde lo elegido, y al revés: entiende un texto para abrir la ventanita con lo que ya hay.
/// </summary>
public static class FilterBuilder
{
    public static FilterKind KindOf(ColumnInfo column) =>
        column.IsNumeric ? FilterKind.Number
        : ColumnFilter.IsDate(column) ? FilterKind.Date
        : column.DataTypeName == "TIME" ? FilterKind.Time
        : column.DataTypeName == "BOOLEAN" ? FilterKind.Boolean
        : FilterKind.Text;

    public static IReadOnlyList<FilterOperator> OperatorsFor(FilterKind kind) => kind switch
    {
        FilterKind.Number =>
        [
            FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.Greater, FilterOperator.GreaterOrEqual, FilterOperator.Less,
            FilterOperator.LessOrEqual, FilterOperator.Between, FilterOperator.IsNull, FilterOperator.IsNotNull,
        ],
        FilterKind.Date =>
        [
            FilterOperator.Day, FilterOperator.Relative, FilterOperator.Between, FilterOperator.GreaterOrEqual, FilterOperator.LessOrEqual,
            FilterOperator.Less, FilterOperator.Greater, FilterOperator.NotEquals, FilterOperator.IsNull, FilterOperator.IsNotNull,
        ],
        FilterKind.Time =>
        [
            FilterOperator.Equals, FilterOperator.Between, FilterOperator.GreaterOrEqual, FilterOperator.LessOrEqual, FilterOperator.IsNull,
            FilterOperator.IsNotNull,
        ],
        FilterKind.Boolean => [FilterOperator.Yes, FilterOperator.No, FilterOperator.IsNull, FilterOperator.IsNotNull],
        _ =>
        [
            FilterOperator.Contains, FilterOperator.NotContains, FilterOperator.StartsWith, FilterOperator.EndsWith, FilterOperator.Equals,
            FilterOperator.NotEquals, FilterOperator.IsNull, FilterOperator.IsNotNull,
        ],
    };

    public static bool NeedsValue(FilterOperator op) => op is not (FilterOperator.IsNull or FilterOperator.IsNotNull or FilterOperator.Yes or FilterOperator.No);

    /// <summary>El texto del filtro; null si falta el valor.</summary>
    public static string? Build(FilterOperator op, string? value, string? value2 = null)
    {
        var v = value?.Trim() ?? "";
        var v2 = value2?.Trim() ?? "";
        if (NeedsValue(op) && v.Length == 0) return null;
        if (op == FilterOperator.Between && v2.Length == 0) return null;
        return op switch
        {
            FilterOperator.Contains or FilterOperator.Day or FilterOperator.Relative => v,
            FilterOperator.NotContains => "!" + v,
            FilterOperator.StartsWith => v + "*",
            FilterOperator.EndsWith => "*" + v,
            FilterOperator.Equals => "=" + v,
            FilterOperator.NotEquals => "<>" + v,
            FilterOperator.Greater => ">" + v,
            FilterOperator.GreaterOrEqual => ">=" + v,
            FilterOperator.Less => "<" + v,
            FilterOperator.LessOrEqual => "<=" + v,
            FilterOperator.Between => v + ".." + v2,
            FilterOperator.Yes => "sí",
            FilterOperator.No => "no",
            FilterOperator.IsNull => "null",
            _ => "!null",
        };
    }

    /// <summary>Los valores tildados: cualquiera de ellos ("=CAJA;PCIA", "1;5;9", "sí;null").</summary>
    public static string? Values(IEnumerable<object?> values, ColumnInfo column)
    {
        var items = values.Select(v => CellFormatter.IsNull(v) ? "null" : Literal(v, column)).Distinct().ToList();
        if (items.Count == 0) return null;
        // Texto y fechas, con «=»: iguales (si no, el texto sería «contiene»). Números y BOOLEAN ya comparan iguales.
        var prefix = KindOf(column) is FilterKind.Number or FilterKind.Boolean ? "" : "=";
        return prefix + string.Join(";", items);
    }

    /// <summary>Un valor como se escribe en el filtro.</summary>
    public static string Literal(object? value, ColumnInfo column) => value switch
    {
        null or DBNull => "null",
        bool b => b ? "sí" : "no",
        DateTime d when column.DataTypeName == "DATE" || d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString(t.Seconds == 0 ? @"hh\:mm" : @"hh\:mm\:ss", CultureInfo.InvariantCulture),
        IFormattable f when ValueComparer.IsNumber(value) => f.ToString(null, CultureInfo.InvariantCulture),
        string s => s.TrimEnd(),
        _ => value.ToString() ?? "",
    };

    /// <summary>Los valores de una lista "=a;b;c" (para tildarlos al abrir); vacío si el texto no es una lista.</summary>
    public static IReadOnlySet<string> ListItems(string? text)
    {
        var t = text?.Trim() ?? "";
        if (!t.Contains(';')) return new HashSet<string>();
        if (t.StartsWith('=')) t = t[1..];
        return t.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Qué operador y valores muestra la ventanita para un filtro ya escrito (lo que no reconoce, el de por defecto).</summary>
    public static (FilterOperator Op, string Value, string Value2) Describe(string? text, ColumnInfo column)
    {
        var kind = KindOf(column);
        var fallback = OperatorsFor(kind)[0];
        var t = text?.Trim() ?? "";
        if (t.Length == 0 || t.Contains(';')) return (fallback, "", "");
        var lower = t.ToLowerInvariant();
        if (lower is "null" or "nulo" or "vacío" or "vacio") return (FilterOperator.IsNull, "", "");
        if (lower is "!null" or "no nulo" or "con valor") return (FilterOperator.IsNotNull, "", "");
        if (kind == FilterKind.Boolean) return (lower is "no" or "n" or "false" or "falso" or "0" ? FilterOperator.No : FilterOperator.Yes, "", "");
        if (kind == FilterKind.Date && ColumnFilter.RelativeDays(t, DateTime.Today) is not null) return (FilterOperator.Relative, t, "");

        foreach (var (prefix, op) in new[]
                 {
                     (">=", FilterOperator.GreaterOrEqual), ("<=", FilterOperator.LessOrEqual), ("<>", FilterOperator.NotEquals),
                     ("!=", FilterOperator.NotEquals), (">", FilterOperator.Greater), ("<", FilterOperator.Less), ("=", FilterOperator.Equals),
                 })
            if (t.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = t[prefix.Length..].Trim();
                return (op == FilterOperator.Equals && kind == FilterKind.Date ? FilterOperator.Day : op, rest, "");
            }

        var dots = t.IndexOf("..", StringComparison.Ordinal);
        if (dots > 0 && kind != FilterKind.Text) return (FilterOperator.Between, t[..dots].Trim(), t[(dots + 2)..].Trim());
        if (kind == FilterKind.Text)
        {
            if (t.Length > 1 && t[0] == '!') return (FilterOperator.NotContains, t[1..].Trim(), "");
            var star = t.IndexOfAny(['*', '%']);
            if (star == t.Length - 1 && star > 0) return (FilterOperator.StartsWith, t[..^1], "");
            if (star == 0 && t.Length > 1 && t.IndexOfAny(['*', '%'], 1) < 0) return (FilterOperator.EndsWith, t[1..], "");
            return (FilterOperator.Contains, t, "");
        }
        return (kind == FilterKind.Date ? FilterOperator.Day : FilterOperator.Equals, t, "");
    }
}
