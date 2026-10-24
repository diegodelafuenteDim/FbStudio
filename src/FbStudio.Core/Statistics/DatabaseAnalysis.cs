using System.Globalization;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Statistics;

/// <summary>Los números de una tabla o un índice tal como los escribe gstat («total records» → «50000»).</summary>
public abstract class StatisticsBlock
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    internal void Add(string key, string value) => _values[key.Trim()] = value.Trim();

    public string? Value(string key) => _values.GetValueOrDefault(key);

    public long? Long(string key) =>
        long.TryParse(Value(key)?.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    public double? Double(string key) =>
        double.TryParse(Value(key)?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}

/// <summary>Una tabla de la estadística completa (gstat -a -r): páginas, llenado, registros y versiones.</summary>
public sealed class TableStatistics(string name, int id) : StatisticsBlock
{
    public string Name { get; } = name;
    public int Id { get; } = id;
    public List<IndexStatistics> Indexes { get; } = [];

    public long? Records => Long("total records");
    public double? AverageRecordLength => Double("average record length");

    /// <summary>Versiones viejas de registros que todavía no limpió la recolección de basura.</summary>
    public long? Versions => Long("total versions");
    public long? MaxVersions => Long("max versions");
    public double? AverageVersionLength => Double("average version length");
    public long? Fragments => Long("total fragments");
    public long? DataPages => Long("data pages");
    public long? AverageFill => Long("average fill");
    public long? EmptyPages => Long("empty pages");
    public long? FullPages => Long("full pages");
    public double? CompressionRatio => Double("compression ratio");
    public long? Formats => Long("total formats");

    /// <summary>
    /// Muchas versiones viejas (más de la mitad de los registros, o un registro con 10 o más): la basura no se está
    /// limpiando, casi siempre por una transacción que quedó abierta.
    /// </summary>
    public bool ManyVersions => Versions is > 0 && (Versions > Records / 2 || MaxVersions >= 10);
}

/// <summary>Un índice de la estadística completa: profundidad, hojas, nodos y duplicados.</summary>
public sealed class IndexStatistics(string table, string name, int id) : StatisticsBlock
{
    public string Table { get; } = table;
    public string Name { get; } = name;
    public int Id { get; } = id;

    public long? Depth => Long("depth");
    public long? LeafBuckets => Long("leaf buckets");
    public long? Nodes => Long("nodes");
    public long? TotalDuplicates => Long("total dup");
    public long? MaxDuplicates => Long("max dup");
    public double? AverageKeyLength => Double("average key length");
    public long? ClusteringFactor => Long("clustering factor");

    /// <summary>Proporción de valores repetidos (0 = todos distintos, cerca de 1 = casi todos iguales).</summary>
    public double? DuplicateRatio => Nodes is > 0 && TotalDuplicates is { } dup ? (double)dup / Nodes.Value : null;

    /// <summary>Cuatro niveles o más: cada búsqueda lee más páginas; se arregla recreándolo (o con páginas más grandes).</summary>
    public bool TooDeep => Depth >= 4;

    /// <summary>Casi todos los valores repetidos en un índice con bastantes filas: el optimizador casi no lo aprovecha.</summary>
    public bool PoorSelectivity => Nodes >= 1000 && DuplicateRatio >= 0.95;
}

/// <summary>
/// La estadística completa leída: las tablas, cada una con sus índices. Sale del texto de gstat (por la API de
/// servicios), que no tiene otro formato.
/// </summary>
public sealed partial class DatabaseAnalysis
{
    public IReadOnlyList<TableStatistics> Tables { get; }

    public IEnumerable<IndexStatistics> Indexes => Tables.SelectMany(t => t.Indexes);

    private DatabaseAnalysis(IReadOnlyList<TableStatistics> tables) => Tables = tables;

    public static DatabaseAnalysis Parse(IEnumerable<string> lines)
    {
        var tables = new List<TableStatistics>();
        TableStatistics? table = null;
        StatisticsBlock? block = null;
        var analyzing = false;
        foreach (var raw in lines.SelectMany(l => l.Split('\n')))
        {
            var line = raw.TrimEnd('\r', ' ', '\t');
            if (line.StartsWith("Analyzing database pages", StringComparison.OrdinalIgnoreCase))
            {
                analyzing = true;
                continue;
            }
            if (!analyzing || line.Length == 0 || line.StartsWith("Gstat", StringComparison.OrdinalIgnoreCase)) continue;
            if (TableLine().Match(line) is { Success: true } t)
            {
                block = table = new TableStatistics(t.Groups["name"].Value, int.Parse(t.Groups["id"].Value, CultureInfo.InvariantCulture));
                tables.Add(table);
                continue;
            }
            if (IndexLine().Match(line) is { Success: true } i && table is not null)
            {
                var index = new IndexStatistics(table.Name, i.Groups["name"].Value, int.Parse(i.Groups["id"].Value, CultureInfo.InvariantCulture));
                table.Indexes.Add(index);
                block = index;
                continue;
            }
            // «Data pages: 1312, average fill: 88%»: pares nombre: valor separados por coma. La distribución del
            // llenado («0 - 19% = 2») no se guarda.
            if (block is null || line.Contains('=')) continue;
            foreach (var pair in line.Split(','))
            {
                var colon = pair.IndexOf(':');
                if (colon > 0 && colon < pair.Length - 1) block.Add(pair[..colon], pair[(colon + 1)..]);
            }
        }
        return new DatabaseAnalysis(tables);
    }

    // «TURNOS (128)» al principio del renglón.
    [GeneratedRegex(@"^(?<name>\S.*?) \((?<id>\d+)\)$")]
    private static partial Regex TableLine();

    // «    Index RDB$PRIMARY1 (0)»
    [GeneratedRegex(@"^\s+Index (?<name>.+?) \((?<id>\d+)\)$")]
    private static partial Regex IndexLine();
}
