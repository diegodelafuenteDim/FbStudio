namespace FbStudio.Core.Trace;

/// <summary>Cómo cambió una consulta entre los dos períodos.</summary>
public enum TraceChangeKind
{
    /// <summary>Solo aparece ahora.</summary>
    New,

    /// <summary>Aparecía antes y ahora no.</summary>
    Gone,

    /// <summary>Cada ejecución tarda bastante más que antes.</summary>
    Slower,

    /// <summary>Cada ejecución tarda bastante menos que antes.</summary>
    Faster,

    /// <summary>Más o menos lo mismo.</summary>
    Same,
}

/// <summary>Una consulta en los dos períodos (F-24): cómo estaba antes y cómo está ahora (null si no apareció en uno).</summary>
public sealed record TraceQueryChange(string Key, TraceQueryGroup? Before, TraceQueryGroup? After)
{
    public TraceChangeKind Kind => (Before, After) switch
    {
        (null, _) => TraceChangeKind.New,
        (_, null) => TraceChangeKind.Gone,
        var (b, a) when Math.Abs(a.AverageMs - b.AverageMs) >= TraceComparison.MinDifferenceMs
                        && Math.Abs(a.AverageMs - b.AverageMs) >= TraceComparison.ChangeRatio * b.AverageMs
            => a.AverageMs > b.AverageMs ? TraceChangeKind.Slower : TraceChangeKind.Faster,
        _ => TraceChangeKind.Same,
    };

    /// <summary>Cuánto cambió el promedio por ejecución (0,5 = 50 % más lenta); null si falta un período o antes era 0.</summary>
    public double? AverageChange => Before is { AverageMs: > 0 } b && After is { } a ? (a.AverageMs - b.AverageMs) / b.AverageMs : null;

    /// <summary>Cuánto tiempo de servidor de más (o de menos) suma ahora.</summary>
    public long TotalDifference => (After?.TotalMs ?? 0) - (Before?.TotalMs ?? 0);
}

/// <summary>
/// Comparar dos períodos de trace (F-24): el análisis de un log guardado (antes) con el de ahora, consulta por consulta.
/// Se compara el promedio por ejecución, que no depende de cuánto duró cada trace; el orden es por la diferencia de
/// tiempo total, lo que más cambió la carga del servidor arriba.
/// </summary>
public static class TraceComparison
{
    /// <summary>Más lenta o más rápida: el promedio cambió al menos este porcentaje…</summary>
    public const double ChangeRatio = 0.2;

    /// <summary>…y al menos estos milisegundos (1 ms a 2 ms es el doble, pero no importa).</summary>
    public const double MinDifferenceMs = 5;

    public static IReadOnlyList<TraceQueryChange> Compare(IReadOnlyList<TraceQueryGroup> before, IReadOnlyList<TraceQueryGroup> after)
    {
        var old = before.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);
        var now = after.ToDictionary(g => g.Key, StringComparer.OrdinalIgnoreCase);
        return old.Keys.Union(now.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(k => new TraceQueryChange(now.TryGetValue(k, out var a) ? a.Key : k, old.GetValueOrDefault(k), now.GetValueOrDefault(k)))
            .OrderByDescending(c => Math.Abs(c.TotalDifference)).ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
    }
}
