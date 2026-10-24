namespace FbStudio.Core.Statistics;

/// <summary>Una tabla en las dos bases (null en la que no está o no se leyó).</summary>
public sealed record TableComparison(string Name, TableStatistics? A, TableStatistics? B)
{
    /// <summary>Registros de B menos los de A (null si falta alguna).</summary>
    public long? RecordsDifference => A?.Records is { } a && B?.Records is { } b ? b - a : null;

    public long? DataPagesDifference => A?.DataPages is { } a && B?.DataPages is { } b ? b - a : null;
}

/// <summary>Un índice en las dos bases.</summary>
public sealed record IndexComparison(string Table, string Name, IndexStatistics? A, IndexStatistics? B)
{
    /// <summary>En B tiene más niveles que en A: conviene recrearlo ahí (o mirar por qué creció).</summary>
    public bool DeeperInB => A?.Depth is { } a && B?.Depth is { } b && b > a;
}

/// <summary>
/// Las estadísticas de dos bases lado a lado (F-27), por ejemplo la de prueba contra la de producción: registros,
/// páginas, versiones y llenado de cada tabla, y profundidad, nodos y duplicados de cada índice. Se relacionan por
/// nombre; lo que está en una sola queda con la otra vacía.
/// </summary>
public sealed class StatisticsComparison
{
    private StatisticsComparison(IReadOnlyList<TableComparison> tables, IReadOnlyList<IndexComparison> indexes)
    {
        Tables = tables;
        Indexes = indexes;
    }

    public IReadOnlyList<TableComparison> Tables { get; }
    public IReadOnlyList<IndexComparison> Indexes { get; }

    public static StatisticsComparison Compare(DatabaseAnalysis a, DatabaseAnalysis b)
    {
        var tablesA = a.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var tablesB = b.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var tables = tablesA.Keys.Union(tablesB.Keys).Order(StringComparer.Ordinal)
            .Select(n => new TableComparison(n, tablesA.GetValueOrDefault(n), tablesB.GetValueOrDefault(n)))
            .ToList();
        var indexesA = a.Indexes.ToDictionary(i => (i.Table, i.Name));
        var indexesB = b.Indexes.ToDictionary(i => (i.Table, i.Name));
        var indexes = indexesA.Keys.Union(indexesB.Keys).OrderBy(k => k.Table, StringComparer.Ordinal).ThenBy(k => k.Name, StringComparer.Ordinal)
            .Select(k => new IndexComparison(k.Table, k.Name, indexesA.GetValueOrDefault(k), indexesB.GetValueOrDefault(k)))
            .ToList();
        return new StatisticsComparison(tables, indexes);
    }
}
