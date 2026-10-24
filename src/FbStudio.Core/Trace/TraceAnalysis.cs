using System.Text.RegularExpressions;

namespace FbStudio.Core.Trace;

/// <summary>
/// Una consulta del análisis (F-24): todas las veces que se ejecutó la misma sentencia, con los totales y la que más
/// tardó como ejemplo (su SQL, plan y parámetros).
/// </summary>
public sealed record TraceQueryGroup(string Key, TraceEvent Slowest, int Count, long TotalMs, long MaxMs, long Reads, long Writes,
    long Fetches, long Marks, long Records, int Failed, bool FullScan)
{
    public double AverageMs => Count == 0 ? 0 : (double)TotalMs / Count;
}

/// <summary>
/// El análisis por consulta: junta las ejecuciones de la misma sentencia y las ordena por el tiempo total, que es lo que
/// más carga el servidor (una consulta de 50 ms que corre 10.000 veces pesa más que una de 5 s que corre una vez).
/// </summary>
public static partial class TraceAnalysis
{
    /// <summary>
    /// La clave de una sentencia: el SQL sin espacios de más y, con <paramref name="unifyValues"/>, con los números y
    /// los textos cambiados por «?» (así «WHERE ID = 1» y «WHERE ID = 2» son la misma consulta, aunque el programa no
    /// use parámetros).
    /// </summary>
    public static string Key(string sql, bool unifyValues)
    {
        var text = Spaces().Replace(sql, " ").Trim();
        if (!unifyValues) return text;
        text = StringLiteral().Replace(text, "?");
        text = NumberLiteral().Replace(text, "?");
        return InList().Replace(text, "IN (?)");
    }

    public static IReadOnlyList<TraceQueryGroup> Group(IEnumerable<TraceEvent> events, bool unifyValues = true) =>
        events.Where(e => e.IsExecution && e.Sql is not null)
            .GroupBy(e => Key(e.Sql!, unifyValues), StringComparer.OrdinalIgnoreCase)
            .Select(g => new TraceQueryGroup(
                g.Key,
                g.MaxBy(e => e.Milliseconds ?? 0)!,
                g.Count(),
                g.Sum(e => e.Milliseconds ?? 0),
                g.Max(e => e.Milliseconds ?? 0),
                g.Sum(e => e.Reads ?? 0),
                g.Sum(e => e.Writes ?? 0),
                g.Sum(e => e.Fetches ?? 0),
                g.Sum(e => e.Marks ?? 0),
                g.Sum(e => e.Records ?? 0),
                g.Count(e => e.Failed),
                g.Any(e => e.HasFullScan)))
            .OrderByDescending(g => g.TotalMs).ThenByDescending(g => g.Count)
            .ToList();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"'(?:[^']|'')*'")]
    private static partial Regex StringLiteral();

    // Números sueltos (no los que forman parte de un nombre, como TABLA_2 o RDB$PRIMARY2).
    [GeneratedRegex(@"(?<![\w$])-?\d+(?:\.\d+)?(?![\w$])")]
    private static partial Regex NumberLiteral();

    [GeneratedRegex(@"\bIN\s*\(\s*\?(?:\s*,\s*\?)*\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex InList();
}
