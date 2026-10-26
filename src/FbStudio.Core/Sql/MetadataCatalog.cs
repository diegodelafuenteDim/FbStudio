using FbStudio.Core.Diagrams;

namespace FbStudio.Core.Sql;

/// <summary>Un parámetro de entrada de un procedimiento o una función.</summary>
/// <param name="Type">El tipo («INTEGER», «VARCHAR(60)») o el dominio.</param>
/// <param name="HasDefault">Tiene valor por defecto: se puede omitir.</param>
public sealed record RoutineParameter(string Name, string Type, bool HasDefault = false)
{
    public string Text => (Name.Length > 0 ? Name + " " : "") + Type + (HasDefault ? " = …" : "");
}

/// <summary>Cómo se llama un procedimiento o una función (la ayuda que aparece al escribir «(»).</summary>
/// <param name="Name">El nombre, con el paquete adelante si es de uno («PKG_TURNOS.CANTIDAD»).</param>
/// <param name="Returns">El tipo que devuelve una función; null en un procedimiento.</param>
public sealed record RoutineSignature(string Name, IReadOnlyList<RoutineParameter> Parameters, string? Returns = null)
{
    public bool IsFunction => Returns is not null;

    public override string ToString() =>
        $"{Name}({string.Join(", ", Parameters.Select(p => p.Text))})" + (Returns is null ? "" : " RETURNS " + Returns);
}

/// <summary>
/// Lo que el autocompletado sabe de una base: tablas y vistas con sus columnas, procedimientos (sus columnas son los
/// parámetros de salida), funciones, paquetes con lo que tienen adentro, generadores, excepciones y la firma de cada
/// procedimiento y función. Lo carga la capa Firebird.
/// </summary>
public sealed class MetadataCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoMembers =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, RoutineSignature> NoSignatures =
        new Dictionary<string, RoutineSignature>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Columnas por tabla, vista o procedimiento (sin distinguir mayúsculas).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Columns { get; }
    public IReadOnlyList<string> Tables { get; }
    public IReadOnlyList<string> Procedures { get; }

    /// <summary>Funciones fuera de paquetes (PSQL y las externas, UDF).</summary>
    public IReadOnlyList<string> Functions { get; init; } = [];

    /// <summary>Las de <see cref="Functions"/> que no son UDF (PSQL o UDR): pueden leer y escribir la base.</summary>
    public IReadOnlyList<string> StoredFunctions { get; init; } = [];
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>Procedimientos y funciones de cada paquete.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PackageMembers { get; init; } = NoMembers;
    public IReadOnlyList<string> Generators { get; init; } = [];
    public IReadOnlyList<string> Exceptions { get; init; } = [];

    /// <summary>Firmas por nombre («P_TURNOS_DESDE», «PKG_TURNOS.CANTIDAD»), sin distinguir mayúsculas.</summary>
    public IReadOnlyDictionary<string, RoutineSignature> Signatures { get; init; } = NoSignatures;

    /// <summary>Las FOREIGN KEY y, después, las lógicas de los comentarios «#FK_»: para sugerir el JOIN con su ON.</summary>
    public IReadOnlyList<DiagramLink> ForeignKeys { get; init; } = [];

    /// <param name="packageProcedures">
    /// Procedimientos de paquetes («PKG.PROC») con sus parámetros de salida: sus columnas en el FROM, sin sumarlos a la
    /// lista de procedimientos (en el autocompletado se ofrecen dentro de su paquete).
    /// </param>
    public MetadataCatalog(IEnumerable<(string Table, string Column)> pairs, IEnumerable<(string Procedure, string? Output)>? procedures = null,
        IEnumerable<(string Procedure, string? Output)>? packageProcedures = null)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, column) in pairs)
        {
            if (!map.TryGetValue(table, out var cols)) map[table] = cols = [];
            cols.Add(column);
        }
        Tables = map.Keys.Order(StringComparer.Ordinal).ToList();

        var procs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (procedure, output) in procedures ?? [])
        {
            procs.Add(procedure);
            if (output is null) continue;
            if (!map.TryGetValue(procedure, out var outs)) map[procedure] = outs = [];
            outs.Add(output);
        }
        Procedures = procs.ToList();
        foreach (var (procedure, output) in packageProcedures ?? [])
        {
            if (output is null) continue;
            if (!map.TryGetValue(procedure, out var outs)) map[procedure] = outs = [];
            outs.Add(output);
        }
        Columns = map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static MetadataCatalog Empty { get; } = new([]);

    public bool IsEmpty => Tables.Count == 0 && Procedures.Count == 0;
}
