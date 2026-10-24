using System.Collections.Immutable;
using System.Globalization;

namespace FbStudio.Core.Results;

public enum SortDirection
{
    None,
    Ascending,
    Descending,
}

public enum AggregateKind
{
    None,
    Count,
    Sum,
    Min,
    Max,
    Average,
}

/// <summary>Qué se pidió ver: orden, filtros por columna, columnas de agrupado y grupos expandidos.</summary>
public sealed record ResultViewSpec
{
    public static ResultViewSpec Empty { get; } = new();

    public int? SortColumn { get; init; }
    public SortDirection SortDirection { get; init; }
    public ImmutableDictionary<int, ColumnFilter> Filters { get; init; } = ImmutableDictionary<int, ColumnFilter>.Empty;
    public ImmutableList<int> GroupBy { get; init; } = [];
    /// <summary>Estado por defecto de los grupos: cerrados (como en DevExpress) o todos abiertos.</summary>
    public bool ExpandAll { get; init; }

    /// <summary>Grupos que el usuario abrió (o cerró, si <see cref="ExpandAll"/>): la excepción al estado por defecto.</summary>
    public ImmutableHashSet<string> ExpandedGroups { get; init; } = [];

    public bool IsIdentity => SortDirection == SortDirection.None && Filters.IsEmpty && GroupBy.IsEmpty;
}

/// <summary>Una línea visible: fila de datos (RowIndex ≥ 0) o encabezado de grupo (GroupIndex ≥ 0).</summary>
public readonly record struct DisplayEntry(int RowIndex, int GroupIndex)
{
    public bool IsGroup => GroupIndex >= 0;
}

public sealed record GroupInfo(string Key, int Column, object? Value, int Level, int RowCount, bool Expanded);

/// <summary>
/// Resultado de aplicar un <see cref="ResultViewSpec"/> a las filas: índices filtrados y ordenados, y
/// la lista de líneas visibles con los grupos. Se arma en segundo plano y se reemplaza entero (inmutable).
/// </summary>
public sealed class ResultViewSnapshot
{
    private readonly IReadOnlyList<object[]> _rows;
    private readonly int[]? _visible;        // null = identidad (todas las filas, en orden)
    private readonly DisplayEntry[]? _entries; // null = sin grupos (entrada i = fila visible i)
    private readonly int _rowCount;

    internal ResultViewSnapshot(IReadOnlyList<object[]> rows, int rowCount, int[]? visible, DisplayEntry[]? entries, GroupInfo[] groups, ResultViewSpec spec)
    {
        _rows = rows;
        _rowCount = rowCount;
        _visible = visible;
        _entries = entries;
        Groups = groups;
        Spec = spec;
    }

    public ResultViewSpec Spec { get; }
    public IReadOnlyList<GroupInfo> Groups { get; }

    /// <summary>Filas que pasan los filtros.</summary>
    public int VisibleRowCount => _visible?.Length ?? _rowCount;

    /// <summary>Líneas a dibujar (filas + encabezados de grupo).</summary>
    public int Count => _entries?.Length ?? VisibleRowCount;

    public DisplayEntry this[int i] =>
        _entries is not null ? _entries[i] : new DisplayEntry(_visible is null ? i : _visible[i], -1);

    public IEnumerable<int> VisibleRows() => _visible ?? Enumerable.Range(0, _rowCount);

    /// <summary>Total de una columna sobre las filas visibles (filtradas). Null si no aplica.</summary>
    public object? Aggregate(int column, AggregateKind kind, ColumnInfo info)
    {
        if (kind == AggregateKind.None) return null;
        long count = 0;
        decimal sum = 0;
        object? min = null, max = null;
        var cmp = ValueComparer.Default;
        foreach (var r in VisibleRows())
        {
            var v = _rows[r][column];
            if (CellFormatter.IsNull(v)) continue;
            count++;
            if (kind is AggregateKind.Sum or AggregateKind.Average && ValueComparer.IsNumber(v))
                sum += Convert.ToDecimal(v, CultureInfo.InvariantCulture);
            if (kind == AggregateKind.Min && (min is null || cmp.Compare(v, min) < 0)) min = v;
            if (kind == AggregateKind.Max && (max is null || cmp.Compare(v, max) > 0)) max = v;
        }
        return kind switch
        {
            AggregateKind.Count => count,
            AggregateKind.Sum => info.IsNumeric ? sum : null,
            AggregateKind.Average => info.IsNumeric && count > 0 ? sum / count : null,
            AggregateKind.Min => min,
            AggregateKind.Max => max,
            _ => null,
        };
    }
}

public static class ResultViewBuilder
{
    /// <summary>
    /// Arma la vista. Con 1M filas el orden tarda del orden de un segundo: llamarlo fuera del hilo de UI.
    /// Toma las primeras <paramref name="rowCount"/> filas (las que había al empezar), así no choca con filas que llegan.
    /// </summary>
    public static ResultViewSnapshot Build(IReadOnlyList<object[]> rows, int rowCount, ResultViewSpec spec, CancellationToken ct = default)
    {
        if (spec.IsIdentity) return new ResultViewSnapshot(rows, rowCount, null, null, [], spec);

        // 1) Filtros
        var visible = new List<int>(rowCount);
        var filters = spec.Filters.Where(f => f.Value.Error is null).ToArray();
        for (var i = 0; i < rowCount; i++)
        {
            if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            var row = rows[i];
            var ok = true;
            foreach (var (col, filter) in filters)
                if (!filter.Matches(row[col])) { ok = false; break; }
            if (ok) visible.Add(i);
        }

        // 2) Orden: primero por las columnas de agrupado, después por la columna elegida (estable por índice).
        var keys = new List<(int Column, int Sign)>();
        foreach (var g in spec.GroupBy)
            keys.Add((g, spec.SortColumn == g && spec.SortDirection == SortDirection.Descending ? -1 : 1));
        if (spec.SortColumn is int sc && spec.SortDirection != SortDirection.None && !spec.GroupBy.Contains(sc))
            keys.Add((sc, spec.SortDirection == SortDirection.Descending ? -1 : 1));

        var idx = visible.ToArray();
        if (keys.Count > 0)
        {
            var cmp = ValueComparer.Default;
            Array.Sort(idx, (a, b) =>
            {
                foreach (var (col, sign) in keys)
                {
                    var c = cmp.Compare(rows[a][col], rows[b][col]);
                    if (c != 0) return c * sign;
                }
                return a.CompareTo(b);
            });
        }
        ct.ThrowIfCancellationRequested();
        if (spec.GroupBy.IsEmpty) return new ResultViewSnapshot(rows, rowCount, idx, null, [], spec);

        // 3) Grupos (multinivel): corridas consecutivas con el mismo valor en las columnas de agrupado.
        var entries = new List<DisplayEntry>();
        var groups = new List<GroupInfo>();
        AddGroups(rows, idx, 0, idx.Length, 0, "", spec, entries, groups);
        return new ResultViewSnapshot(rows, rowCount, idx, [.. entries], [.. groups], spec);
    }

    private static void AddGroups(IReadOnlyList<object[]> rows, int[] idx, int from, int to, int level, string parentKey,
        ResultViewSpec spec, List<DisplayEntry> entries, List<GroupInfo> groups)
    {
        var col = spec.GroupBy[level];
        var cmp = ValueComparer.Default;
        var start = from;
        while (start < to)
        {
            var value = rows[idx[start]][col];
            var end = start + 1;
            while (end < to && cmp.SameGroup(rows[idx[end]][col], value)) end++;

            var key = parentKey + "|" + CellFormatter.Format(value, "");
            var expanded = spec.ExpandAll != spec.ExpandedGroups.Contains(key);
            entries.Add(new DisplayEntry(-1, groups.Count));
            groups.Add(new GroupInfo(key, col, value, level, end - start, expanded));
            if (expanded)
            {
                if (level + 1 < spec.GroupBy.Count) AddGroups(rows, idx, start, end, level + 1, key, spec, entries, groups);
                else for (var i = start; i < end; i++) entries.Add(new DisplayEntry(idx[i], -1));
            }
            start = end;
        }
    }
}
