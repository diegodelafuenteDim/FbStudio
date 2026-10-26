using System.Globalization;

namespace FbStudio.Core.Results;

public sealed record ChartPoint(string Label, double Value);

/// <summary>
/// Los puntos de un gráfico rápido de un resultado: una barra (o un punto de la línea) por cada valor distinto de la
/// columna X, con la suma de la columna Y o, sin Y, la cantidad de filas. Si X es un número o una fecha se ordena por X;
/// si no, queda en el orden en que aparece. Más de 500 valores distintos se cortan (y se avisa).
/// </summary>
public static class ChartData
{
    public const int MaxPoints = 500;

    public static bool IsTemporal(ColumnInfo column) =>
        column.DataTypeName.ToUpperInvariant() is "DATE" or "TIME" or "TIMESTAMP" || column.DataTypeName.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase);

    /// <param name="y">Null: la cantidad de filas de cada X.</param>
    public static (IReadOnlyList<ChartPoint> Points, bool Truncated) Build(ColumnInfo x, ColumnInfo? y, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var groups = new Dictionary<string, (object? Key, double Value, int Order)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = row[x.Ordinal];
            var label = CellFormatter.Format(key, x.DataTypeName);
            var add = y is null ? 1 : Number(row[y.Ordinal]) ?? 0;
            groups[label] = groups.TryGetValue(label, out var g) ? g with { Value = g.Value + add } : (key, add, groups.Count);
        }
        IEnumerable<KeyValuePair<string, (object? Key, double Value, int Order)>> ordered = groups.OrderBy(g => g.Value.Order);
        if (x.IsNumeric || IsTemporal(x))
            ordered = groups.OrderBy(g => CellFormatter.IsNull(g.Value.Key) ? 1 : 0).ThenBy(g => g.Value.Key is IComparable c && !CellFormatter.IsNull(g.Value.Key) ? c : null, Comparer<object?>.Create(CompareValues));
        var points = ordered.Select(g => new ChartPoint(g.Key, g.Value.Value)).ToList();
        return points.Count > MaxPoints ? (points.Take(MaxPoints).ToList(), true) : (points, false);
    }

    /// <summary>Divisiones del eje «redondas» (1, 2 o 5 por una potencia de 10), unas cinco, que incluyen el 0.</summary>
    public static IReadOnlyList<double> Ticks(double min, double max)
    {
        min = Math.Min(0, min);
        max = Math.Max(0, max);
        if (max - min < 1e-12) max = min + 1;
        var rough = (max - min) / 5;
        var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = new[] { 1d, 2d, 5d, 10d }.Select(f => f * power).First(s => s >= rough);
        var ticks = new List<double>();
        for (var k = Math.Floor(min / step); k * step <= max + step * 1e-9; k++) ticks.Add(Math.Round(k * step, 10));
        if (ticks[^1] < max) ticks.Add(Math.Round(ticks[^1] + step, 10));
        return ticks;
    }

    /// <summary>«1.500», «12,5», «0,25» (con la cultura de la interfaz).</summary>
    public static string Format(double value) =>
        Math.Abs(value) >= 1000 || value == Math.Floor(value) ? value.ToString("N0", CultureInfo.CurrentCulture) : value.ToString("0.##", CultureInfo.CurrentCulture);

    private static double? Number(object? value) => value switch
    {
        null or DBNull => null,
        IConvertible c when value is not string and not DateTime and not bool => Convert.ToDouble(c, CultureInfo.InvariantCulture),
        System.Numerics.BigInteger big => (double)big,
        _ => null,
    };

    private static int CompareValues(object? a, object? b)
    {
        if (a is null || b is null) return a is null ? (b is null ? 0 : 1) : -1;
        if (a.GetType() == b.GetType() && a is IComparable ca) return ca.CompareTo(b);
        return Number(a) is { } na && Number(b) is { } nb ? na.CompareTo(nb) : string.CompareOrdinal(a.ToString(), b.ToString());
    }
}
