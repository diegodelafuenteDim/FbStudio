namespace FbStudio.Core.Metadata;

/// <summary>
/// Un área (F-9): un grupo de objetos de la base con nombre propio, para no ver cientos de tablas juntas. Un objeto
/// entra si su nombre (o el de su tabla, en triggers e índices) cumple alguna regla y ninguna excepción. Una regla es
/// un nombre o un patrón con * y ? (% vale como *), sin distinguir mayúsculas. Un objeto puede estar en varias áreas.
/// </summary>
public sealed record SchemaArea
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Rules { get; set; } = [];
    public IReadOnlyList<string> Exclude { get; set; } = [];

    /// <summary>
    /// Sumar los procedimientos, vistas y triggers que usan solo tablas del área (por las dependencias de la base):
    /// sirve cuando los procedimientos no siguen el prefijo de las tablas. Las excepciones igual ganan.
    /// </summary>
    public bool IncludeDependents { get; init; }

    /// <summary>Color del área en el árbol y en la pestaña Áreas (D-86), de la paleta de las carpetas; null: sin color.</summary>
    public string? Color { get; init; }
}

/// <summary>El archivo de áreas de una base. No tiene datos de conexión: se puede compartir con otros.</summary>
public sealed record SchemaAreaFile
{
    public int Version { get; set; } = 1;
    public IReadOnlyList<SchemaArea> Areas { get; set; } = [];
}

/// <summary>
/// Un área propuesta por el prefijo de los nombres: el más corto de una familia («CLI») con una regla por cada prefijo
/// que empieza igual («CLI_*», «CLIENTES_*»), y cuántas tablas sin área juntaría.
/// </summary>
public sealed record AreaSuggestion(string Name, IReadOnlyList<string> Rules, int Count)
{
    /// <summary>«CLI_, CLIENTES_»: para la descripción.</summary>
    public string Prefixes => string.Join(", ", Rules.Select(r => r.TrimEnd('*')));
}

/// <summary>
/// Qué tablas y vistas usa cada procedimiento, vista y trigger (de RDB$DEPENDENCIES, sin las tablas del sistema), para
/// las áreas que suman «lo que usa solo sus tablas».
/// </summary>
public sealed class TableUses
{
    private static readonly HashSet<string> NoTables = [];
    private readonly Dictionary<(DbObjectKind, string), HashSet<string>> _uses = [];

    public TableUses(IEnumerable<(DbObjectKind Kind, string Name, string Table)> rows)
    {
        foreach (var (kind, name, table) in rows)
        {
            if (!_uses.TryGetValue((kind, name), out var tables)) _uses[(kind, name)] = tables = new HashSet<string>(StringComparer.Ordinal);
            tables.Add(table);
        }
    }

    public static TableUses None { get; } = new([]);

    public IReadOnlySet<string> Of(DbObject obj) => _uses.TryGetValue((obj.Kind, obj.Name), out var t) ? t : NoTables;
}

/// <summary>
/// Quién está en cada área, calculado una vez para todos los objetos: los que toman las reglas y, en las áreas que lo
/// piden, los procedimientos, vistas y triggers que usan solo sus tablas (una vista sumada así cuenta como tabla del
/// área para los demás; un trigger cuenta también su tabla). Las excepciones ganan siempre.
/// </summary>
public sealed class AreaMembership
{
    private readonly IReadOnlyList<SchemaArea> _areas;
    private readonly TableUses _uses;
    private readonly List<HashSet<DbObject>> _members = [];
    private readonly List<HashSet<DbObject>> _byUse = [];

    /// <param name="reuse">
    /// El cálculo anterior con los mismos objetos: las áreas que no cambiaron no se recalculan (al escribir en una
    /// área, se recalcula solo esa).
    /// </param>
    public AreaMembership(IReadOnlyList<SchemaArea> areas, IReadOnlyList<DbObject> objects, TableUses? uses = null, AreaMembership? reuse = null)
    {
        _areas = areas;
        Objects = objects;
        _uses = uses ??= TableUses.None;
        if (reuse is not null && (!ReferenceEquals(reuse.Objects, objects) || !ReferenceEquals(reuse._uses, uses))) reuse = null;
        foreach (var area in areas)
        {
            // Un área igual (mismo nombre, mismas listas de reglas y excepciones) da lo mismo que antes.
            var same = reuse is null ? -1 : IndexOf(reuse._areas, area);
            if (same >= 0)
            {
                _members.Add(reuse!._members[same]);
                _byUse.Add(reuse._byUse[same]);
                continue;
            }
            var members = objects.Where(o => SchemaAreas.Contains(area, o)).ToHashSet();
            var byUse = new HashSet<DbObject>();
            if (area.IncludeDependents)
            {
                var relations = members.Where(o => DbObjectKinds.IsRelation(o.Kind)).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
                var candidates = objects.Where(o => o.Kind is DbObjectKind.Procedure or DbObjectKind.View or DbObjectKind.Trigger
                                                    && !members.Contains(o) && !SchemaAreas.Excluded(area, o)).ToList();
                // Una vista que entra puede hacer entrar a lo que la usa: se repite hasta que no entra nada más.
                for (var changed = true; changed;)
                {
                    changed = false;
                    foreach (var o in candidates.Where(c => !byUse.Contains(c)))
                    {
                        var tables = uses.Of(o);
                        var owner = o.Kind == DbObjectKind.Trigger ? o.Owner : null;
                        if (tables.Count == 0 && owner is null) continue;
                        if (!tables.All(relations.Contains) || (owner is not null && !relations.Contains(owner))) continue;
                        byUse.Add(o);
                        if (o.Kind == DbObjectKind.View && relations.Add(o.Name)) changed = true;
                    }
                }
                members.UnionWith(byUse);
            }
            _members.Add(members);
            _byUse.Add(byUse);
        }
        Unassigned = objects.Where(o => !_members.Any(m => m.Contains(o))).ToList();
    }

    private static int IndexOf(IReadOnlyList<SchemaArea> areas, SchemaArea area)
    {
        for (var i = 0; i < areas.Count; i++) if (areas[i].Equals(area)) return i;
        return -1;
    }

    public IReadOnlyList<DbObject> Objects { get; }

    /// <summary>Los objetos que no están en ninguna área.</summary>
    public IReadOnlyList<DbObject> Unassigned { get; }

    /// <summary>Los objetos del área, en el orden en que vinieron.</summary>
    public IReadOnlyList<DbObject> Members(int area) => Objects.Where(_members[area].Contains).ToList();

    public int Count(int area) => _members[area].Count;

    public bool Contains(int area, DbObject obj) => _members[area].Contains(obj);

    /// <summary>Entró por lo que usa (no por una regla).</summary>
    public bool IsByUse(int area, DbObject obj) => _byUse[area].Contains(obj);

    /// <summary>Los nombres de las áreas donde está el objeto.</summary>
    public IReadOnlyList<string> AreasOf(DbObject obj) =>
        _areas.Where((_, i) => _members[i].Contains(obj)).Select(a => a.Name).ToList();
}

public static class SchemaAreas
{
    /// <summary>Un prefijo de menos letras («A_», «T_») no dice nada.</summary>
    public const int MinPrefixLength = 2;

    /// <summary>Si el nombre cumple el patrón: * es cualquier cosa (también nada), ? es un carácter, % vale como *.</summary>
    public static bool Matches(string pattern, string name)
    {
        int p = 0, n = 0, star = -1, mark = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && pattern[p] is '*' or '%')
            {
                star = p++;
                mark = n;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(name[n])))
            {
                p++;
                n++;
            }
            else if (star >= 0)
            {
                // Lo que tomó el último * no alcanzó: que tome un carácter más.
                p = star + 1;
                n = ++mark;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] is '*' or '%') p++;
        return p == pattern.Length;
    }

    /// <summary>
    /// Si una regla pone al objeto en el área: alguna regla toma su nombre o su tabla, y ninguna excepción (lo que entra
    /// por lo que usa lo calcula <see cref="AreaMembership"/>).
    /// </summary>
    public static bool Contains(SchemaArea area, DbObject obj) =>
        Any(area.Rules, obj) && !Excluded(area, obj);

    /// <summary>Alguna excepción toma su nombre o su tabla.</summary>
    public static bool Excluded(SchemaArea area, DbObject obj) => Any(area.Exclude, obj);

    public static IReadOnlyList<DbObject> Members(SchemaArea area, IEnumerable<DbObject> objects) =>
        objects.Where(o => Contains(area, o)).ToList();

    /// <summary>Los objetos que no están en ninguna área.</summary>
    public static IReadOnlyList<DbObject> Unassigned(IReadOnlyList<SchemaArea> areas, IEnumerable<DbObject> objects) =>
        objects.Where(o => !areas.Any(a => Contains(a, o))).ToList();

    /// <summary>Los nombres de las áreas donde está el objeto.</summary>
    public static IReadOnlyList<string> AreasOf(IReadOnlyList<SchemaArea> areas, DbObject obj) =>
        areas.Where(a => Contains(a, obj)).Select(a => a.Name).ToList();

    /// <summary>
    /// Propone áreas por el prefijo de las tablas sin área (lo que va antes del primer _). Los prefijos que empiezan con
    /// otro van juntos, en una familia con el nombre del más corto («CLI»: CLI_ y CLIENTES_; «PROV»: PROV_ y
    /// PROVEEDORES_). Se propone la familia que junta al menos <paramref name="minCount"/> tablas, si su nombre no es el
    /// de un área que ya existe. Las que juntan más tablas van primero.
    /// </summary>
    public static IReadOnlyList<AreaSuggestion> SuggestByPrefix(IEnumerable<string> tables, IReadOnlyList<SchemaArea> existing, int minCount = 3)
    {
        var taken = existing.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var counts = tables
            .Where(t => !existing.Any(a => Contains(a, new DbObject(DbObjectKind.Table, t))))
            .Select(t => t.IndexOf('_') is var i && i >= MinPrefixLength ? t[..i].ToUpperInvariant() : null)
            .OfType<string>()
            .GroupBy(prefix => prefix)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        // Del más corto al más largo: cada prefijo va a la familia del primero con que empieza.
        var families = new List<(string Root, List<string> Prefixes)>();
        foreach (var prefix in counts.Keys.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal))
        {
            var family = families.FirstOrDefault(f => prefix.StartsWith(f.Root, StringComparison.Ordinal));
            if (family.Root is null) families.Add((prefix, [prefix]));
            else family.Prefixes.Add(prefix);
        }
        return families
            .Select(f => new AreaSuggestion(f.Root, f.Prefixes.Select(p => p + "_*").ToList(), f.Prefixes.Sum(p => counts[p])))
            .Where(s => s.Count >= minCount && !taken.Contains(s.Name))
            .OrderByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Las reglas escritas en un cuadro de texto: una por renglón (o separadas por coma), sin repetidas.</summary>
    public static IReadOnlyList<string> ParseRules(string? text) =>
        (text ?? "").Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Las reglas para mostrar en el cuadro de texto, una por renglón.</summary>
    public static string FormatRules(IEnumerable<string> rules) => string.Join(Environment.NewLine, rules);

    /// <summary>
    /// Agrega el objeto al área. Si una excepción lo dejaba afuera, se saca esa excepción cuando es su nombre exacto;
    /// si no, se agrega el nombre como regla.
    /// </summary>
    public static SchemaArea Add(SchemaArea area, string name)
    {
        var exclude = area.Exclude.Where(e => !e.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var updated = area with { Exclude = exclude };
        return Contains(updated, new DbObject(DbObjectKind.Table, name))
            ? updated
            : updated with { Rules = [.. area.Rules, name] };
    }

    /// <summary>
    /// Saca el objeto del área: si estaba por su nombre exacto se quita esa regla; si lo toma un patrón (o entró por lo
    /// que usa, <paramref name="byUse"/>), se agrega una excepción con su nombre.
    /// </summary>
    public static SchemaArea Remove(SchemaArea area, string name, bool byUse = false)
    {
        var rules = area.Rules.Where(r => !r.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var updated = area with { Rules = rules };
        return byUse || Contains(updated, new DbObject(DbObjectKind.Table, name))
            ? updated with { Exclude = [.. area.Exclude, name] }
            : updated;
    }

    private static bool Any(IReadOnlyList<string> patterns, DbObject obj) =>
        patterns.Any(p => Matches(p, obj.Name) || (obj.Owner is { } owner && Matches(p, owner)));
}
