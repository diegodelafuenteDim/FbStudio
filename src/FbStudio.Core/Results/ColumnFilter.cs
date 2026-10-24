using System.Globalization;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Results;

/// <summary>Lo que se entendió de un filtro, para armarlo también como SQL (pestaña Datos: filtra en el servidor).</summary>
public abstract record FilterCondition;

/// <summary>"null" (Negated = false) o "!null".</summary>
public sealed record NullCondition(bool Negated) : FilterCondition;

/// <param name="Op">"", "=", "&lt;&gt;", "&gt;", "&gt;=", "&lt;" o "&lt;=" ("" es igual).</param>
public sealed record NumberCondition(string Op, decimal Value) : FilterCondition;

/// <summary>"10..20": entre los dos, inclusive.</summary>
public sealed record NumberRangeCondition(decimal From, decimal To) : FilterCondition;

/// <param name="DayOnly">Se escribió solo la fecha: sin operador es "ese día"; "&lt;=" incluye todo ese día y "&gt;" empieza el siguiente.</param>
public sealed record DateCondition(string Op, DateTime Value, bool DayOnly) : FilterCondition;

/// <summary>
/// Desde <paramref name="From"/> hasta <paramref name="To"/> (sin incluirlo si <paramref name="ToExclusive"/>):
/// «2026-07-01..2026-07-31» (los dos días enteros), «hoy», «este mes», «últimos 7 días».
/// </summary>
public sealed record DateRangeCondition(DateTime From, DateTime To, bool ToExclusive) : FilterCondition;

public sealed record TimeCondition(string Op, TimeSpan Value) : FilterCondition;

/// <summary>"08:00..12:00": entre las dos horas, inclusive.</summary>
public sealed record TimeRangeCondition(TimeSpan From, TimeSpan To) : FilterCondition;

public sealed record TextCompareCondition(string Op, string Value) : FilterCondition;

/// <summary>Con comodines * o %.</summary>
public sealed record TextLikeCondition(string Pattern) : FilterCondition;

public sealed record TextContainsCondition(string Value) : FilterCondition;

/// <summary>"!texto": no lo contiene. Los NULL entran (no contienen nada).</summary>
public sealed record TextNotContainsCondition(string Value) : FilterCondition;

/// <summary>Columna BOOLEAN: "sí" o "no".</summary>
public sealed record BoolCondition(bool Value) : FilterCondition;

/// <summary>"a;b;c": vale cualquiera de las condiciones.</summary>
public sealed record AnyCondition(IReadOnlyList<FilterCondition> Items) : FilterCondition;

/// <summary>
/// Filtro de una columna escrito en la fila de filtros, al estilo DevExpress (la ventanita de cada columna escribe el
/// mismo texto, así se puede ver y corregir a mano):
/// <list type="bullet">
/// <item>Texto: contiene, sin distinguir mayúsculas ni acentos ("perez" encuentra "PÉREZ"); con * o % es comodín
/// ("abc*" empieza, "*abc" termina); "!abc" no contiene; "=abc" igual, "&lt;&gt;abc" distinto.</item>
/// <item>Números: "1500" es igual; también &gt;, &lt;, &gt;=, &lt;=, =, &lt;&gt; (coma o punto decimal) y "10..20" entre.</item>
/// <item>Fechas: "2026-07-01" o "01/07/2026" es ese día; con operadores compara (un día entero cuenta todo el día);
/// "2026-07-01..2026-07-31" entre; "hoy", "ayer", "esta semana", "semana pasada", "este mes", "mes pasado",
/// "este año", "año pasado", "últimos 7 días" (también en inglés).</item>
/// <item>Horas: "08:30", con operadores, y "08:00..12:00".</item>
/// <item>BOOLEAN: "sí" o "no".</item>
/// <item>Varias con ";": cualquiera de ellas ("1;5;9", "=CAJA;PCIA", "perez;gomez"). Un operador adelante vale para las
/// que no traen el suyo.</item>
/// <item>"null" o "vacío": solo NULL; "!null": solo los que tienen valor.</item>
/// </list>
/// </summary>
public sealed class ColumnFilter
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy"];
    private static readonly string[] TimeFormats = [@"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss"];

    private readonly Func<object?, bool> _predicate;

    private ColumnFilter(string text, Func<object?, bool> predicate, string? error = null, FilterCondition? condition = null)
    {
        Text = text;
        _predicate = predicate;
        Error = error;
        Condition = error is null ? condition : null;
    }

    public string Text { get; }

    /// <summary>El filtro interpretado (null si tiene error).</summary>
    public FilterCondition? Condition { get; }

    /// <summary>Si el texto no se pudo interpretar (p. ej. "&gt;abc" en una columna numérica), no filtra y explica por qué.</summary>
    public string? Error { get; }

    public bool Matches(object? value) => _predicate(value);

    public static bool IsDate(ColumnInfo column) => column.DataTypeName is "DATE" or "TIMESTAMP";

    public static ColumnFilter? Parse(string? text, ColumnInfo column)
    {
        var t = text?.Trim() ?? "";
        if (t.Length == 0) return null;
        if (t.Contains(';') && List(t, column) is { } list) return list;
        return ParseOne(t, column);
    }

    /// <summary>"a;b;c": cualquiera. Un operador adelante vale para las que no traen el suyo ("=CAJA;PCIA").</summary>
    private static ColumnFilter? List(string t, ColumnInfo column)
    {
        var (op, rest) = SplitOperator(t);
        var items = rest.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0) return null;
        string Item(string i) => IsNullWord(i.ToLowerInvariant()) || SplitOperator(i).Op != "" ? i : op + i;
        if (items.Length == 1) return ParseOne(Item(items[0]), column) is { } single ? Rename(single, t) : null;
        var parsed = items.Select(i => ParseOne(Item(i), column)).ToList();
        if (parsed.FirstOrDefault(p => p.Error is not null) is { } bad) return new ColumnFilter(t, _ => true, bad.Error);
        return new ColumnFilter(t, v => parsed.Any(p => p.Matches(v)), condition: new AnyCondition([.. parsed.Select(p => p.Condition!)]));
    }

    private static ColumnFilter Rename(ColumnFilter f, string text) => new(text, f._predicate, f.Error, f.Condition);

    private static bool IsNullWord(string lower) => lower is "null" or "nulo" or "vacío" or "vacio";

    private static ColumnFilter ParseOne(string t, ColumnInfo column)
    {
        var lower = t.ToLowerInvariant();
        if (IsNullWord(lower)) return new ColumnFilter(t, CellFormatter.IsNull, condition: new NullCondition(false));
        if (lower is "!null" or "no nulo" or "con valor") return new ColumnFilter(t, v => !CellFormatter.IsNull(v), condition: new NullCondition(true));

        if (column.DataTypeName == "BOOLEAN")
        {
            bool? b = RemoveAccents(lower) switch
            {
                "si" or "s" or "true" or "verdadero" or "1" or "yes" => true,
                "no" or "n" or "false" or "falso" or "0" => false,
                _ => null,
            };
            return b is { } value
                ? new ColumnFilter(t, v => v is bool x && x == value, condition: new BoolCondition(value))
                : new ColumnFilter(t, _ => true, string.Format(Resources.CoreStrings.FilterNotABoolean, t));
        }

        var (op, operand) = SplitOperator(t);
        if (column.IsNumeric) return ParseNumeric(t, op, operand);
        if (IsDate(column)) return ParseDate(t, op, operand, column);
        if (column.DataTypeName == "TIME") return ParseTime(t, op, operand);
        return ParseText(t, op, operand);
    }

    private static (string Op, string Operand) SplitOperator(string t)
    {
        foreach (var op in new[] { ">=", "<=", "<>", "!=", ">", "<", "=" })
            if (t.StartsWith(op, StringComparison.Ordinal)) return (op == "!=" ? "<>" : op, t[op.Length..].Trim());
        return ("", t);
    }

    private static bool Holds(string op, int cmp) => op switch
    {
        ">" => cmp > 0,
        ">=" => cmp >= 0,
        "<" => cmp < 0,
        "<=" => cmp <= 0,
        "<>" => cmp != 0,
        _ => cmp == 0,
    };

    /// <summary>"a..b" (sin operador adelante): las dos puntas.</summary>
    private static (string From, string To)? Range(string op, string operand)
    {
        var i = operand.IndexOf("..", StringComparison.Ordinal);
        return op == "" && i > 0 && i < operand.Length - 2 ? (operand[..i].Trim(), operand[(i + 2)..].Trim()) : null;
    }

    private static decimal? Number(string operand)
    {
        var normalized = operand.Replace(" ", "");
        // "1.234,5" (es-AR) o "1234.5": si hay coma, la coma es el decimal.
        if (normalized.Contains(',')) normalized = normalized.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static ColumnFilter ParseNumeric(string text, string op, string operand)
    {
        if (Range(op, operand) is var (fromText, toText))
        {
            if (Number(fromText) is not { } from || Number(toText) is not { } to)
                return new ColumnFilter(text, _ => true, string.Format(Resources.CoreStrings.FilterNotANumber, Number(fromText) is null ? fromText : toText));
            if (from > to) (from, to) = (to, from);
            return new ColumnFilter(text, v => ValueComparer.IsNumber(v) && Convert.ToDecimal(v, CultureInfo.InvariantCulture) is var d && d >= from && d <= to,
                condition: new NumberRangeCondition(from, to));
        }
        if (Number(operand) is not { } target)
            return new ColumnFilter(text, _ => true, string.Format(Resources.CoreStrings.FilterNotANumber, operand));
        return new ColumnFilter(text, v => ValueComparer.IsNumber(v)
            && Holds(op, Convert.ToDecimal(v, CultureInfo.InvariantCulture).CompareTo(target)), condition: new NumberCondition(op, target));
    }

    private static (DateTime Value, bool DayOnly)? Date(string operand) =>
        DateTime.TryParseExact(operand, DateFormats, Es, DateTimeStyles.AllowWhiteSpaces, out var d) ? (d, operand.Trim().Length <= 10) : null;

    private static ColumnFilter ParseDate(string text, string op, string operand, ColumnInfo column)
    {
        var dateOnly = column.DataTypeName == "DATE";
        if (op == "" && RelativeDays(operand, DateTime.Today) is var (relFrom, relTo))
            return DateRange(text, relFrom, relTo, toExclusive: true, dateOnly);
        if (Range(op, operand) is var (fromText, toText))
        {
            if (Date(fromText) is not var (from, _) || Date(toText) is not var (to, toDay))
                return new ColumnFilter(text, _ => true, string.Format(Resources.CoreStrings.FilterNotADate, Date(fromText) is null ? fromText : toText));
            if (from > to) (from, to) = (to, from);
            // La punta final escrita como día entero incluye todo ese día.
            return toDay ? DateRange(text, from, to.Date.AddDays(1), toExclusive: true, dateOnly) : DateRange(text, from, to, toExclusive: false, dateOnly);
        }
        if (Date(operand) is not var (target, dayOnly))
            return new ColumnFilter(text, _ => true, string.Format(Resources.CoreStrings.FilterNotADate, operand));
        var condition = new DateCondition(op, target, dayOnly);
        if (!dayOnly || dateOnly)
            return new ColumnFilter(text, v => v is DateTime d && Holds(op, (dateOnly ? d.Date : d).CompareTo(dateOnly ? target.Date : target)), condition: condition);
        // Un día entero en una columna con hora: "ese día" es de 0:00 a 0:00 del siguiente.
        var day = target.Date;
        var next = day.AddDays(1);
        Func<DateTime, bool> test = op switch
        {
            "" or "=" => d => d >= day && d < next,
            "<>" => d => d < day || d >= next,
            "<=" => d => d < next,
            ">" => d => d >= next,
            "<" => d => d < day,
            _ => d => d >= day, // >=
        };
        return new ColumnFilter(text, v => v is DateTime d && test(d), condition: condition);
    }

    private static ColumnFilter DateRange(string text, DateTime from, DateTime to, bool toExclusive, bool dateOnly) =>
        new(text, v => v is DateTime d && (dateOnly ? d.Date : d) is var x && x >= from && (toExclusive ? x < to : x <= to),
            condition: new DateRangeCondition(from, to, toExclusive));

    /// <summary>
    /// Fechas relativas: el rango [desde, hasta) que corresponde hoy. Las semanas empiezan el lunes.
    /// </summary>
    public static (DateTime From, DateTime To)? RelativeDays(string text, DateTime today)
    {
        var t = Regex.Replace(RemoveAccents(text.Trim().ToLowerInvariant()), @"\s+", " ");
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var month = new DateTime(today.Year, today.Month, 1);
        var year = new DateTime(today.Year, 1, 1);
        switch (t)
        {
            case "hoy" or "today": return (today, today.AddDays(1));
            case "ayer" or "yesterday": return (today.AddDays(-1), today);
            case "manana" or "tomorrow": return (today.AddDays(1), today.AddDays(2));
            case "esta semana" or "this week": return (monday, monday.AddDays(7));
            case "semana pasada" or "la semana pasada" or "last week": return (monday.AddDays(-7), monday);
            case "este mes" or "this month": return (month, month.AddMonths(1));
            case "mes pasado" or "el mes pasado" or "last month": return (month.AddMonths(-1), month);
            case "este ano" or "this year": return (year, year.AddYears(1));
            case "ano pasado" or "el ano pasado" or "last year": return (year.AddYears(-1), year);
        }
        var m = Regex.Match(t, @"^(?:ultimos|last) (\d{1,4}) (?:dias|days)$");
        if (m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var n and > 0)
            return (today.AddDays(1 - n), today.AddDays(1)); // con hoy incluido
        return null;
    }

    private static TimeSpan? Time(string operand) =>
        TimeSpan.TryParseExact(operand.Trim(), TimeFormats, CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1) ? t : null;

    private static TimeSpan? TimeOf(object? v) => v switch
    {
        TimeSpan t => t,
        DateTime d => d.TimeOfDay,
        _ => null,
    };

    private static ColumnFilter ParseTime(string text, string op, string operand)
    {
        if (Range(op, operand) is var (fromText, toText))
        {
            if (Time(fromText) is not { } from || Time(toText) is not { } to)
                return new ColumnFilter(text, _ => true, string.Format(Resources.CoreStrings.FilterNotATime, Time(fromText) is null ? fromText : toText));
            if (from > to) (from, to) = (to, from);
            return new ColumnFilter(text, v => TimeOf(v) is { } x && x >= from && x <= to, condition: new TimeRangeCondition(from, to));
        }
        if (Time(operand) is not { } target)
            return new ColumnFilter(text, _ => true, string.Format(Resources.CoreStrings.FilterNotATime, operand));
        return new ColumnFilter(text, v => TimeOf(v) is { } x && Holds(op, x.CompareTo(target)), condition: new TimeCondition(op, target));
    }

    private static ColumnFilter ParseText(string text, string op, string operand)
    {
        if (op != "")
        {
            var cmp = Es.CompareInfo;
            return new ColumnFilter(text, v => !CellFormatter.IsNull(v)
                && Holds(op, cmp.Compare(v!.ToString()!.TrimEnd(), operand, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)),
                condition: new TextCompareCondition(op, operand));
        }
        var compare = Es.CompareInfo;
        if (operand.Length > 1 && operand[0] == '!')
        {
            var value = operand[1..].Trim();
            return new ColumnFilter(text, v => CellFormatter.IsNull(v)
                || compare.IndexOf(v!.ToString()!, value, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0,
                condition: new TextNotContainsCondition(value));
        }
        if (operand.Contains('*') || operand.Contains('%'))
        {
            var regex = new Regex("^" + Regex.Escape(RemoveAccents(operand)).Replace(@"\*", ".*").Replace("%", ".*") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return new ColumnFilter(text, v => !CellFormatter.IsNull(v) && regex.IsMatch(RemoveAccents(v!.ToString()!.TrimEnd())),
                condition: new TextLikeCondition(operand));
        }
        return new ColumnFilter(text, v => !CellFormatter.IsNull(v)
            && compare.IndexOf(v!.ToString()!, operand, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0,
            condition: new TextContainsCondition(operand));
    }

    private static string RemoveAccents(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        return string.Concat(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
    }
}
