namespace FbStudio.Core.Metadata;

/// <summary>
/// Un permiso tal como está en RDB$USER_PRIVILEGES (sin los del dueño de cada objeto, que son implícitos).
/// </summary>
/// <param name="GranteeType">RDB$USER_TYPE: 8 usuario, 13 rol, 5 procedimiento, 2 trigger, 1 vista, 15 función, 18 paquete.</param>
/// <param name="Privilege">S, I, U, D, R (tablas y vistas), X (ejecutar), G (uso de un generador o excepción), M (pertenecer a un rol).</param>
/// <param name="GrantOption">1 WITH GRANT OPTION; 2 WITH ADMIN OPTION (roles).</param>
/// <param name="ObjectType">RDB$OBJECT_TYPE: 0 tabla, 1 vista, 5 procedimiento, 7 excepción, 9 dominio, 13 rol, 14 generador, 15 función, 18 paquete.</param>
/// <param name="Field">Columna, en un UPDATE o REFERENCES por columna.</param>
public sealed record PrivilegeDefinition(string Grantee, int GranteeType, string Privilege, int GrantOption, string Object, int ObjectType, string? Field = null);

/// <summary>Valor actual del generador que Firebird crea por dentro para una columna identity.</summary>
public sealed record IdentityValue(string Table, string Column, long Value);

/// <summary>
/// Todos los objetos de una base, leídos de RDB$ en una sola transacción: lo que hace falta para el script de la base
/// entera y para comparar dos bases.
/// </summary>
public sealed record DatabaseSchema
{
    /// <summary>Charset por defecto de la base (el script lo menciona: la base nueva tiene que tener el mismo).</summary>
    public string? Charset { get; init; }

    public IReadOnlyList<RoleDefinition> Roles { get; init; } = [];
    public IReadOnlyList<DomainDefinition> Domains { get; init; } = [];
    public IReadOnlyList<GeneratorDefinition> Generators { get; init; } = [];
    public IReadOnlyList<ExceptionDefinition> Exceptions { get; init; } = [];
    public IReadOnlyList<TableDefinition> Tables { get; init; } = [];
    public IReadOnlyList<ViewDefinition> Views { get; init; } = [];
    public IReadOnlyList<ProcedureDefinition> Procedures { get; init; } = [];

    /// <summary>Funciones PSQL y externas (UDF).</summary>
    public IReadOnlyList<FunctionDefinition> Functions { get; init; } = [];

    public IReadOnlyList<PackageDefinition> Packages { get; init; } = [];

    /// <summary>Triggers de base de datos (ON CONNECT, ON TRANSACTION…); los de tablas y vistas van con ellas.</summary>
    public IReadOnlyList<TriggerDefinition> DatabaseTriggers { get; init; } = [];

    /// <summary>Vista → las vistas que usa (para crearlas en orden).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ViewDependencies { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<PrivilegeDefinition> Privileges { get; init; } = [];

    /// <summary>Permisos dados a procedimientos, triggers, vistas, funciones o paquetes que ya no existen (no se escriben).</summary>
    public int OrphanPrivileges { get; init; }

    /// <summary>Valor actual de cada identity (se lee con los valores de los generadores).</summary>
    public IReadOnlyList<IdentityValue> IdentityValues { get; init; } = [];

    /// <summary>Solo los objetos de esta área (<see cref="SchemaFilter"/>); null, la base entera.</summary>
    public string? Area { get; init; }

    /// <summary>Con un área: las FK a tablas de afuera que no se escriben.</summary>
    public int ForeignKeysOutside { get; init; }
}

/// <summary>
/// Qué parte del script escribir. La base vacía con datos copia las filas entre las dos partes: con las tablas, las
/// claves y los índices ya creados, pero antes de las FK, los CHECK y los triggers. Así no importa en qué orden se copian
/// las tablas, no se disparan triggers y no molesta un CHECK agregado después que los datos viejos no cumplen.
/// </summary>
[Flags]
public enum SchemaScriptParts
{
    /// <summary>Todo menos las FK, los CHECK, los triggers y los permisos.</summary>
    Structure = 1,

    /// <summary>Las FK, los CHECK, los triggers (con sus comentarios) y los permisos.</summary>
    Deferred = 2,

    All = Structure | Deferred,
}

/// <summary>Las tablas que se copian con datos a una base vacía.</summary>
public static class TableSelection
{
    /// <summary>
    /// Las elegidas más las que referencian sus FK (y las que referencian esas): sin ellas la FK no se podría crear.
    /// Devuelve todas en orden alfabético.
    /// </summary>
    public static IReadOnlyList<string> WithReferenced(DatabaseSchema schema, IEnumerable<string> chosen)
    {
        var byName = schema.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(chosen.Where(byName.ContainsKey));
        while (pending.TryPop(out var name))
        {
            if (!result.Add(name)) continue;
            foreach (var fk in byName[name].Constraints.Where(c => c.Kind == ConstraintKind.ForeignKey))
                if (fk.ReferencedTable is { } referenced && byName.ContainsKey(referenced)) pending.Push(referenced);
        }
        return [.. result];
    }
}

/// <summary>Qué incluir en el script de la base.</summary>
public sealed record SchemaScriptOptions
{
    public bool Domains { get; init; } = true;
    public bool Generators { get; init; } = true;

    /// <summary>El valor actual de cada generador (SET GENERATOR … TO n): para una copia que siga numerando igual.</summary>
    public bool GeneratorValues { get; init; } = true;

    public bool Exceptions { get; init; } = true;
    public bool Tables { get; init; } = true;
    public bool Views { get; init; } = true;
    public bool Procedures { get; init; } = true;
    public bool Functions { get; init; } = true;
    public bool Packages { get; init; } = true;
    public bool Triggers { get; init; } = true;
    public bool Roles { get; init; } = true;
    public bool Privileges { get; init; } = true;

    /// <summary>Solo los objetos de esta área de la base (F-9); null, la base entera.</summary>
    public string? Area { get; init; }
}
