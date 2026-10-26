using System.Text.RegularExpressions;

namespace FbStudio.Core.Diagrams;

/// <summary>Una columna para el diagrama (F-8).</summary>
public sealed record DiagramColumn(string Name, string Type, bool NotNull, bool IsPrimaryKey);

/// <summary>Una tabla o vista con sus columnas en orden.</summary>
public sealed record DiagramTable(string Name, bool IsView, IReadOnlyList<DiagramColumn> Columns);

/// <summary>Una relación: <paramref name="Table"/>(<paramref name="Columns"/>) apunta a <paramref name="Referenced"/>.</summary>
/// <param name="IsLogical">Sale de un comentario «#FK_» de la columna, no de una FOREIGN KEY: se dibuja punteada.</param>
public sealed record DiagramLink(string Name, string Table, IReadOnlyList<string> Columns, string Referenced, IReadOnlyList<string> ReferencedColumns,
    bool IsLogical = false);

/// <summary>
/// Las FK «lógicas» que algunas bases guardan en el comentario de la columna: <c>#FK_%TABLA,%CLAVE,%MOSTRAR…,I#</c>
/// (la columna apunta a TABLA.CLAVE; lo demás es para mostrar). Puede venir junto a otras marcas, como #Required#.
/// </summary>
public static partial class LogicalForeignKeys
{
    [GeneratedRegex(@"#FK_%([^,#%]+),%([^,#%]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>La relación que dice el comentario, o null si no tiene una.</summary>
    public static DiagramLink? Parse(string table, string column, string? description)
    {
        if (description is null || Pattern().Match(description) is not { Success: true } m) return null;
        return new DiagramLink($"#FK {table}.{column}", table, [column], m.Groups[1].Value.Trim().ToUpperInvariant(),
            [m.Groups[2].Value.Trim().ToUpperInvariant()], IsLogical: true);
    }
}

/// <summary>
/// Lo que se dibuja: las tablas, las de afuera (de un área, como cajitas sin columnas), las relaciones entre ellas, y las
/// tablas «comodín» que se escondieron (con cuántas relaciones tienen con lo dibujado).
/// </summary>
public sealed record DiagramSelection(
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Outside,
    IReadOnlyList<DiagramLink> Links,
    IReadOnlyList<HiddenHub> HiddenHubs);

public sealed record HiddenHub(string Table, int Links, int Neighbors);

/// <summary>
/// Las tablas de la base y sus relaciones (FOREIGN KEY y, si se quiere, las lógicas de los comentarios), para elegir qué
/// dibujar. Las tablas con muchísimas vecinas (USUARIOS, PACIENTES…) son «comodines»: se esconden si no se piden,
/// porque si no todo queda conectado con todo.
/// </summary>
public sealed class SchemaGraph
{
    /// <summary>Una tabla con más vecinas que esto es un comodín.</summary>
    public const int DefaultHubThreshold = 30;

    private readonly Dictionary<string, DiagramTable> _tables;
    private readonly Dictionary<string, HashSet<string>> _physical = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _all = new(StringComparer.Ordinal);

    public SchemaGraph(IEnumerable<DiagramTable> tables, IEnumerable<DiagramLink> links, int hubThreshold = DefaultHubThreshold)
    {
        _tables = tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        HubThreshold = hubThreshold;
        var list = new List<DiagramLink>();
        var physicalKeys = new HashSet<(string, string, string)>();
        // Primero las FOREIGN KEY: una lógica que repite una (misma tabla, columna y destino) no se dibuja dos veces.
        foreach (var link in links.OrderBy(l => l.IsLogical))
        {
            if (!_tables.ContainsKey(link.Table) || !_tables.ContainsKey(link.Referenced)) continue;
            var key = (link.Table, string.Join(",", link.Columns), link.Referenced);
            if (!link.IsLogical) physicalKeys.Add(key);
            else if (physicalKeys.Contains(key)) continue;
            list.Add(link);
            if (link.Table == link.Referenced) continue;
            Connect(_all, link.Table, link.Referenced);
            if (!link.IsLogical) Connect(_physical, link.Table, link.Referenced);
        }
        Links = list;
    }

    public int HubThreshold { get; }

    public IReadOnlyCollection<DiagramTable> Tables => _tables.Values;

    public IReadOnlyList<DiagramLink> Links { get; }

    public DiagramTable? Table(string name) => _tables.GetValueOrDefault(name);

    public bool Contains(string name) => _tables.ContainsKey(name);

    /// <summary>Las tablas con que se relaciona (para los dos lados de la FK).</summary>
    public IReadOnlySet<string> Neighbors(string table, bool logical = true) =>
        (logical ? _all : _physical).TryGetValue(table, out var set) ? set : Empty;

    public bool IsHub(string table, bool logical = true) => Neighbors(table, logical).Count > HubThreshold;

    /// <summary>
    /// Una o más tablas y sus vecinas hasta <paramref name="hops"/> pasos. Los comodines no se dibujan (salvo que se
    /// empiece por uno o esté en <paramref name="shownHubs"/>) y no se pasa a través de ellos.
    /// </summary>
    public DiagramSelection Around(IEnumerable<string> start, int hops, bool hideHubs = true, ISet<string>? shownHubs = null, bool logical = true)
    {
        var starts = start.Where(Contains).Distinct().ToList();
        bool Hidden(string t) => hideHubs && IsHub(t, logical) && !starts.Contains(t) && shownHubs?.Contains(t) != true;
        var chosen = new HashSet<string>(starts, StringComparer.Ordinal);
        var frontier = starts.ToList();
        for (var step = 0; step < hops && frontier.Count > 0; step++)
        {
            var next = new List<string>();
            foreach (var t in frontier)
                foreach (var n in Neighbors(t, logical).Where(n => !Hidden(n) && chosen.Add(n)))
                    next.Add(n);
            frontier = next;
        }
        return Compose(Ordered(chosen, starts), [], hideHubs, shownHubs, logical);
    }

    /// <summary>
    /// Las tablas y vistas de un área. Con <paramref name="outside"/>, también sus vecinas de afuera (sin comodines),
    /// como cajitas sin columnas.
    /// </summary>
    public DiagramSelection ForArea(IEnumerable<string> members, bool outside = false, bool hideHubs = true, ISet<string>? shownHubs = null, bool logical = true)
    {
        var inside = members.Where(Contains).Distinct().ToList();
        var set = inside.ToHashSet(StringComparer.Ordinal);
        bool Hidden(string t) => !set.Contains(t) && hideHubs && IsHub(t, logical) && shownHubs?.Contains(t) != true;
        var extra = new List<string>();
        if (outside)
            extra = inside.SelectMany(t => Neighbors(t, logical)).Where(n => !set.Contains(n) && !Hidden(n)).Distinct().Order(StringComparer.Ordinal).ToList();
        // Un comodín pedido entra como de afuera.
        if (!outside && shownHubs is not null)
            extra = shownHubs.Where(h => Contains(h) && !set.Contains(h)).Order(StringComparer.Ordinal).ToList();
        return Compose(inside, extra, hideHubs, shownHubs, logical);
    }

    /// <summary>
    /// Lo que se dibuja con estas tablas (y las de afuera): sus relaciones entre sí, y qué comodines quedaron escondidos
    /// con cuántas relaciones con lo dibujado.
    /// </summary>
    public DiagramSelection Compose(IReadOnlyList<string> tables, IReadOnlyList<string> outside, bool hideHubs = true, ISet<string>? shownHubs = null,
        bool logical = true)
    {
        var drawn = tables.Concat(outside).ToHashSet(StringComparer.Ordinal);
        bool Hidden(string t) => hideHubs && IsHub(t, logical) && shownHubs?.Contains(t) != true;
        var links = Links.Where(l => (logical || !l.IsLogical) && drawn.Contains(l.Table) && drawn.Contains(l.Referenced)).ToList();
        var hubs = Links.Where(l => logical || !l.IsLogical)
            .SelectMany(l => new[] { (Hub: l.Referenced, Other: l.Table), (Hub: l.Table, Other: l.Referenced) })
            .Where(x => !drawn.Contains(x.Hub) && drawn.Contains(x.Other) && Hidden(x.Hub))
            .GroupBy(x => x.Hub)
            .Select(g => new HiddenHub(g.Key, g.Count(), Neighbors(g.Key, logical).Count))
            .OrderByDescending(h => h.Links).ThenBy(h => h.Table, StringComparer.Ordinal)
            .ToList();
        return new DiagramSelection(tables, outside, links, hubs);
    }

    /// <summary>
    /// El camino más corto por FK entre dos tablas, sin importar hacia dónde apunta cada una: sin pasar por comodines si
    /// se puede (un camino por USUARIOS casi nunca es el que se busca) y si no, por donde haga falta. null si no están
    /// conectadas. Entre caminos igual de cortos, el de nombres primeros en orden alfabético (siempre el mismo).
    /// </summary>
    public IReadOnlyList<string>? Path(string from, string to, bool logical = true)
    {
        if (!Contains(from) || !Contains(to)) return null;
        if (from == to) return [from];
        return ShortestPath(from, to, logical, avoidHubs: true) ?? ShortestPath(from, to, logical, avoidHubs: false);
    }

    private List<string>? ShortestPath(string from, string to, bool logical, bool avoidHubs)
    {
        var previous = new Dictionary<string, string>(StringComparer.Ordinal) { [from] = from };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Neighbors(current, logical).Order(StringComparer.Ordinal))
            {
                if (previous.ContainsKey(next) || (avoidHubs && next != to && IsHub(next, logical))) continue;
                previous[next] = current;
                if (next == to)
                {
                    var path = new List<string> { to };
                    while (path[^1] != from) path.Add(previous[path[^1]]);
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(next);
            }
        }
        return null;
    }

    /// <summary>Las de partida primero, después las demás por nombre.</summary>
    private static List<string> Ordered(HashSet<string> chosen, List<string> starts) =>
        [.. starts, .. chosen.Except(starts).Order(StringComparer.Ordinal)];

    private static void Connect(Dictionary<string, HashSet<string>> graph, string a, string b)
    {
        (graph.TryGetValue(a, out var sa) ? sa : graph[a] = new HashSet<string>(StringComparer.Ordinal)).Add(b);
        (graph.TryGetValue(b, out var sb) ? sb : graph[b] = new HashSet<string>(StringComparer.Ordinal)).Add(a);
    }

    private static readonly HashSet<string> Empty = [];
}
