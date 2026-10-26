using FbStudio.Core.Metadata;
using FbStudio.Core.Resources;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

public enum DependencyDirection
{
    /// <summary>Objetos que el objeto usa (tablas que lee un procedimiento, tabla a la que apunta una FK…).</summary>
    Uses,

    /// <summary>Objetos que usan al objeto (lo que se rompe si se borra o se cambia).</summary>
    UsedBy,
}

/// <param name="Detail">Columnas usadas, FK, "columna calculada", etc.</param>
public sealed record DbDependency(DependencyDirection Direction, DbObjectKind Kind, string Name, string? Detail);

/// <summary>Quién tiene abierta una transacción (de MON$; sin RDB$ADMIN solo se ven las conexiones propias).</summary>
public sealed record TransactionOwner(long AttachmentId, string? User, string? Address, string? Process, DateTime? Since);

public sealed partial class MetadataExplorer
{
    public Task<TransactionOwner?> TransactionOwnerAsync(long transactionId, CancellationToken ct = default) =>
        WithTransactionAsync(async (connection, tx) =>
        {
            var q = new MetaQuery(connection, tx, ct);
            var rows = await q.ListAsync("""
                SELECT a.MON$ATTACHMENT_ID, TRIM(a.MON$USER), TRIM(a.MON$REMOTE_ADDRESS), TRIM(a.MON$REMOTE_PROCESS), t.MON$TIMESTAMP
                FROM MON$TRANSACTIONS t
                JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID
                WHERE t.MON$TRANSACTION_ID = @id
                """, r => new TransactionOwner(Convert.ToInt64(r.GetValue(0)), Str(r, 1), Str(r, 2), Str(r, 3), r.IsDBNull(4) ? null : r.GetDateTime(4)),
                ("@id", transactionId));
            return rows.FirstOrDefault();
        }, ct);

    // Tipos de RDB$DEPENDENCIES (RDB$TYPES, RDB$OBJECT_TYPE) que corresponden a cada clase de objeto.
    private static readonly Dictionary<DbObjectKind, int[]> DependencyTypes = new()
    {
        [DbObjectKind.Table] = [0],
        [DbObjectKind.View] = [0, 1],
        [DbObjectKind.Trigger] = [2],
        [DbObjectKind.Procedure] = [5],
        [DbObjectKind.Exception] = [7],
        [DbObjectKind.Domain] = [9],
        [DbObjectKind.Index] = [6, 10],
        [DbObjectKind.Role] = [13],
        [DbObjectKind.Generator] = [14],
        [DbObjectKind.Function] = [15],
        [DbObjectKind.Package] = [18, 19],
    };

    /// <summary>Qué usa el objeto y quién lo usa (RDB$DEPENDENCIES, más FK y columnas de un dominio, que no figuran ahí).</summary>
    public Task<IReadOnlyList<DbDependency>> DependenciesAsync(DbObject obj, CancellationToken ct = default) =>
        WithTransactionAsync(async (connection, tx) =>
        {
            var q = new MetaQuery(connection, tx, ct);
            var types = string.Join(", ", DependencyTypes[obj.Kind]);
            var found = new List<DbDependency>();

            // Lo que usa.
            found.AddRange((await q.ListAsync($"""
                SELECT TRIM(d.RDB$DEPENDED_ON_NAME), d.RDB$DEPENDED_ON_TYPE, TRIM(d.RDB$FIELD_NAME), TRIM(d.RDB$PACKAGE_NAME),
                       IIF(r.RDB$VIEW_BLR IS NULL, 0, 1)
                FROM RDB$DEPENDENCIES d
                LEFT JOIN RDB$RELATIONS r ON d.RDB$DEPENDED_ON_TYPE IN (0, 1) AND r.RDB$RELATION_NAME = d.RDB$DEPENDED_ON_NAME
                WHERE d.RDB$DEPENDENT_NAME = @name AND d.RDB$DEPENDENT_TYPE IN ({types})
                """, r =>
                {
                    var package = Str(r, 3);
                    var kind = package is not null ? DbObjectKind.Package : KindOf(Int(r, 1) ?? -1, Int(r, 4) == 1);
                    return kind is null ? null : new DbDependency(DependencyDirection.Uses, kind.Value, package ?? Str(r, 0) ?? "",
                        package is not null ? Str(r, 0) : Str(r, 2));
                }, ("@name", obj.Name))).OfType<DbDependency>());

            // Quién lo usa. Las columnas calculadas y los CHECK aparecen como dominios RDB$… y triggers de sistema:
            // se muestran como su tabla.
            found.AddRange((await q.ListAsync($"""
                SELECT TRIM(d.RDB$DEPENDENT_NAME), d.RDB$DEPENDENT_TYPE, TRIM(d.RDB$FIELD_NAME),
                       TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME),
                       COALESCE(t.RDB$SYSTEM_FLAG, 0), TRIM(t.RDB$RELATION_NAME), TRIM(cc.RDB$CONSTRAINT_NAME),
                       IIF(r.RDB$VIEW_BLR IS NULL, 0, 1)
                FROM RDB$DEPENDENCIES d
                LEFT JOIN RDB$RELATION_FIELDS rf ON d.RDB$DEPENDENT_TYPE = 3 AND rf.RDB$FIELD_SOURCE = d.RDB$DEPENDENT_NAME
                LEFT JOIN RDB$TRIGGERS t ON d.RDB$DEPENDENT_TYPE = 2 AND t.RDB$TRIGGER_NAME = d.RDB$DEPENDENT_NAME
                LEFT JOIN RDB$CHECK_CONSTRAINTS cc ON cc.RDB$TRIGGER_NAME = t.RDB$TRIGGER_NAME
                LEFT JOIN RDB$RELATIONS r ON d.RDB$DEPENDENT_TYPE IN (0, 1) AND r.RDB$RELATION_NAME = d.RDB$DEPENDENT_NAME
                WHERE d.RDB$DEPENDED_ON_NAME = @name AND d.RDB$DEPENDED_ON_TYPE IN ({types})
                """, UsedByRow, ("@name", obj.Name))).OfType<DbDependency>());

            if (obj.Kind == DbObjectKind.Table)
            {
                found.AddRange(await q.ListAsync("""
                    SELECT TRIM(uq.RDB$RELATION_NAME), TRIM(fk.RDB$CONSTRAINT_NAME)
                    FROM RDB$RELATION_CONSTRAINTS fk
                    JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONSTRAINT_NAME = fk.RDB$CONSTRAINT_NAME
                    JOIN RDB$RELATION_CONSTRAINTS uq ON uq.RDB$CONSTRAINT_NAME = ref.RDB$CONST_NAME_UQ
                    WHERE fk.RDB$RELATION_NAME = @name AND fk.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
                    """, r => new DbDependency(DependencyDirection.Uses, DbObjectKind.Table, Str(r, 0) ?? "", ForeignKeyLabel(Str(r, 1))), ("@name", obj.Name)));
                found.AddRange(await q.ListAsync("""
                    SELECT TRIM(fk.RDB$RELATION_NAME), TRIM(fk.RDB$CONSTRAINT_NAME)
                    FROM RDB$RELATION_CONSTRAINTS fk
                    JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONSTRAINT_NAME = fk.RDB$CONSTRAINT_NAME
                    JOIN RDB$RELATION_CONSTRAINTS uq ON uq.RDB$CONSTRAINT_NAME = ref.RDB$CONST_NAME_UQ
                    WHERE uq.RDB$RELATION_NAME = @name AND fk.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
                    """, r => new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Table, Str(r, 0) ?? "", ForeignKeyLabel(Str(r, 1))), ("@name", obj.Name)));
            }
            else if (obj.Kind == DbObjectKind.Domain)
            {
                found.AddRange(await q.ListAsync("""
                    SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), IIF(r.RDB$VIEW_BLR IS NULL, 0, 1)
                    FROM RDB$RELATION_FIELDS rf JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
                    WHERE rf.RDB$FIELD_SOURCE = @name
                    """, r => new DbDependency(DependencyDirection.UsedBy, Int(r, 2) == 1 ? DbObjectKind.View : DbObjectKind.Table, Str(r, 0) ?? "", Str(r, 1)),
                    ("@name", obj.Name)));
                found.AddRange(await q.ListAsync("""
                    SELECT TRIM(p.RDB$PROCEDURE_NAME), TRIM(p.RDB$PARAMETER_NAME) FROM RDB$PROCEDURE_PARAMETERS p
                    WHERE p.RDB$FIELD_SOURCE = @name AND p.RDB$PACKAGE_NAME IS NULL
                    """, r => new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Procedure, Str(r, 0) ?? "", Str(r, 1)), ("@name", obj.Name)));
            }

            // Una fila por objeto y dirección, con sus columnas juntas; sin referencias a sí mismo.
            return (IReadOnlyList<DbDependency>)found
                .Where(d => !(d.Name == obj.Name && SameKind(d.Kind, obj.Kind)))
                .GroupBy(d => (d.Direction, d.Kind, d.Name))
                .Select(g => g.First() with
                {
                    Detail = string.Join(", ", g.Select(d => d.Detail).Where(s => !string.IsNullOrEmpty(s)).Distinct()) is { Length: > 0 } detail ? detail : null,
                })
                .OrderBy(d => d.Direction).ThenBy(d => d.Kind).ThenBy(d => d.Name, StringComparer.Ordinal)
                .ToList();
        }, ct);

    private static DbDependency? UsedByRow(FbDataReader r)
    {
        var name = Str(r, 0) ?? "";
        var type = Int(r, 1) ?? -1;
        var field = Str(r, 2);
        switch (type)
        {
            case 3 when Str(r, 3) is { } table:
                return new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Table, table, string.Format(CoreStrings.DependencyComputedColumn, Str(r, 4)));
            case 2 when Int(r, 5) != 0:
                // Trigger de sistema: solo interesan los de un CHECK (se muestran como la tabla).
                return Str(r, 7) is { } check && Str(r, 6) is { } checkTable
                    ? new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Table, checkTable, $"CHECK {check}")
                    : null;
            case 4:
                return new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Domain, name, CoreStrings.DependencyDomainCheck);
        }
        return KindOf(type, Int(r, 8) == 1) is { } kind ? new DbDependency(DependencyDirection.UsedBy, kind, name, field) : null;
    }

    /// <summary>
    /// ¿Dónde se usa una columna? Lo que hay que mirar antes de cambiarla o borrarla: vistas, procedimientos, funciones y
    /// triggers que la nombran (RDB$DEPENDENCIES por columna), sus índices y claves, las FK de otras tablas que la apuntan,
    /// la FK de la columna misma y las FK lógicas de los comentarios («#FK_%TABLA,%COLUMNA») que la apuntan.
    /// </summary>
    public Task<IReadOnlyList<DbDependency>> ColumnUsageAsync(string table, string column, CancellationToken ct = default) =>
        WithTransactionAsync(async (connection, tx) =>
        {
            var q = new MetaQuery(connection, tx, ct);
            var found = new List<DbDependency>();
            found.AddRange((await q.ListAsync("""
                SELECT TRIM(d.RDB$DEPENDENT_NAME), d.RDB$DEPENDENT_TYPE, TRIM(d.RDB$FIELD_NAME),
                       TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME),
                       COALESCE(t.RDB$SYSTEM_FLAG, 0), TRIM(t.RDB$RELATION_NAME), TRIM(cc.RDB$CONSTRAINT_NAME),
                       IIF(r.RDB$VIEW_BLR IS NULL, 0, 1)
                FROM RDB$DEPENDENCIES d
                LEFT JOIN RDB$RELATION_FIELDS rf ON d.RDB$DEPENDENT_TYPE = 3 AND rf.RDB$FIELD_SOURCE = d.RDB$DEPENDENT_NAME
                LEFT JOIN RDB$TRIGGERS t ON d.RDB$DEPENDENT_TYPE = 2 AND t.RDB$TRIGGER_NAME = d.RDB$DEPENDENT_NAME
                LEFT JOIN RDB$CHECK_CONSTRAINTS cc ON cc.RDB$TRIGGER_NAME = t.RDB$TRIGGER_NAME
                LEFT JOIN RDB$RELATIONS r ON d.RDB$DEPENDENT_TYPE IN (0, 1) AND r.RDB$RELATION_NAME = d.RDB$DEPENDENT_NAME
                WHERE d.RDB$DEPENDED_ON_NAME = @name AND d.RDB$DEPENDED_ON_TYPE IN (0, 1) AND d.RDB$FIELD_NAME = @col
                """, UsedByRow, ("@name", table), ("@col", column))).OfType<DbDependency>());
            // Los índices que la tienen (y la clave que respaldan, si es una).
            found.AddRange(await q.ListAsync("""
                SELECT TRIM(i.RDB$INDEX_NAME), TRIM(rc.RDB$CONSTRAINT_NAME), TRIM(rc.RDB$CONSTRAINT_TYPE)
                FROM RDB$INDEX_SEGMENTS s
                JOIN RDB$INDICES i ON i.RDB$INDEX_NAME = s.RDB$INDEX_NAME
                LEFT JOIN RDB$RELATION_CONSTRAINTS rc ON rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME
                WHERE i.RDB$RELATION_NAME = @name AND s.RDB$FIELD_NAME = @col
                """, r => new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Index, Str(r, 0) ?? "",
                    Str(r, 2) is { } type ? $"{type} {Str(r, 1)}" : null), ("@name", table), ("@col", column)));
            // Las FK de otras tablas que la apuntan, y la FK de la columna misma.
            found.AddRange(await q.ListAsync("""
                SELECT TRIM(fk.RDB$RELATION_NAME), TRIM(fk.RDB$CONSTRAINT_NAME), TRIM(fs.RDB$FIELD_NAME)
                FROM RDB$RELATION_CONSTRAINTS uq
                JOIN RDB$INDEX_SEGMENTS us ON us.RDB$INDEX_NAME = uq.RDB$INDEX_NAME AND us.RDB$FIELD_NAME = @col
                JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONST_NAME_UQ = uq.RDB$CONSTRAINT_NAME
                JOIN RDB$RELATION_CONSTRAINTS fk ON fk.RDB$CONSTRAINT_NAME = ref.RDB$CONSTRAINT_NAME
                JOIN RDB$INDEX_SEGMENTS fs ON fs.RDB$INDEX_NAME = fk.RDB$INDEX_NAME AND fs.RDB$FIELD_POSITION = us.RDB$FIELD_POSITION
                WHERE uq.RDB$RELATION_NAME = @name
                """, r => new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Table, Str(r, 0) ?? "", $"{ForeignKeyLabel(Str(r, 1))} ({Str(r, 2)})"),
                ("@name", table), ("@col", column)));
            found.AddRange(await q.ListAsync("""
                SELECT TRIM(uq.RDB$RELATION_NAME), TRIM(fk.RDB$CONSTRAINT_NAME)
                FROM RDB$RELATION_CONSTRAINTS fk
                JOIN RDB$INDEX_SEGMENTS fs ON fs.RDB$INDEX_NAME = fk.RDB$INDEX_NAME AND fs.RDB$FIELD_NAME = @col
                JOIN RDB$REF_CONSTRAINTS ref ON ref.RDB$CONSTRAINT_NAME = fk.RDB$CONSTRAINT_NAME
                JOIN RDB$RELATION_CONSTRAINTS uq ON uq.RDB$CONSTRAINT_NAME = ref.RDB$CONST_NAME_UQ
                WHERE fk.RDB$RELATION_NAME = @name AND fk.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
                """, r => new DbDependency(DependencyDirection.Uses, DbObjectKind.Table, Str(r, 0) ?? "", ForeignKeyLabel(Str(r, 1))), ("@name", table), ("@col", column)));
            // Las FK lógicas de los comentarios que apuntan a esta columna.
            found.AddRange((await q.ListAsync("""
                SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), CAST(SUBSTRING(rf.RDB$DESCRIPTION FROM 1 FOR 1000) AS VARCHAR(1000))
                FROM RDB$RELATION_FIELDS rf
                WHERE rf.RDB$DESCRIPTION CONTAINING '#FK_'
                """, r => Core.Diagrams.LogicalForeignKeys.Parse(Str(r, 0) ?? "", Str(r, 1) ?? "", Str(r, 2)) is { } link
                          && link.Referenced == table && link.ReferencedColumns[0] == column
                    ? new DbDependency(DependencyDirection.UsedBy, DbObjectKind.Table, link.Table, string.Format(CoreStrings.DependencyLogicalFk, link.Columns[0]))
                    : null)).OfType<DbDependency>());
            return (IReadOnlyList<DbDependency>)found
                .Where(d => !(d.Direction == DependencyDirection.UsedBy && d.Kind == DbObjectKind.Table && d.Name == table && d.Detail is null))
                .GroupBy(d => (d.Direction, d.Kind, d.Name))
                .Select(g => g.First() with
                {
                    Detail = string.Join(", ", g.Select(d => d.Detail).Where(s => !string.IsNullOrEmpty(s)).Distinct()) is { Length: > 0 } detail ? detail : null,
                })
                .OrderBy(d => d.Direction).ThenBy(d => d.Kind).ThenBy(d => d.Name, StringComparer.Ordinal)
                .ToList();
        }, ct);

    /// <summary>"FK_DET_TURNO" tal cual; un nombre que no dice que es una FK (p. ej. INTEG_45) lleva "FK " adelante.</summary>
    private static string ForeignKeyLabel(string? constraint) =>
        constraint is null ? "FK" : constraint.StartsWith("FK", StringComparison.OrdinalIgnoreCase) ? constraint : "FK " + constraint;

    private static DbObjectKind? KindOf(int type, bool isView) => type switch
    {
        0 => isView ? DbObjectKind.View : DbObjectKind.Table,
        1 => DbObjectKind.View,
        2 => DbObjectKind.Trigger,
        5 => DbObjectKind.Procedure,
        6 or 10 => DbObjectKind.Index,
        7 => DbObjectKind.Exception,
        9 => DbObjectKind.Domain,
        13 => DbObjectKind.Role,
        14 => DbObjectKind.Generator,
        15 => DbObjectKind.Function,
        18 or 19 => DbObjectKind.Package,
        _ => null,
    };

    private static bool SameKind(DbObjectKind a, DbObjectKind b) =>
        a == b || (DbObjectKinds.IsRelation(a) && DbObjectKinds.IsRelation(b));
}
