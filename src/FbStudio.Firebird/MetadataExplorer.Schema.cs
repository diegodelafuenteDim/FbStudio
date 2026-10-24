using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>Avance de la lectura de la base entera: cuántos objetos van, de cuántos, y cuál se está leyendo.</summary>
public sealed record SchemaProgress(int Done, int Total, string Current);

public sealed partial class MetadataExplorer
{
    // Permisos sin los del dueño de cada objeto (los tiene por ser el dueño) ni los de objetos del sistema.
    private const string PrivilegesSql = $"""
        SELECT TRIM(p.RDB$USER), p.RDB$USER_TYPE, TRIM(p.RDB$PRIVILEGE), COALESCE(p.RDB$GRANT_OPTION, 0), TRIM(p.RDB$RELATION_NAME),
               p.RDB$OBJECT_TYPE, TRIM(p.RDB$FIELD_NAME)
        FROM RDB$USER_PRIVILEGES p
        WHERE p.RDB$RELATION_NAME NOT STARTING WITH 'RDB$' AND p.RDB$RELATION_NAME NOT STARTING WITH 'MON$'
          AND p.RDB$RELATION_NAME NOT STARTING WITH 'SEC$'
          AND NOT {Orphan}
          -- Uso de charsets, collations y de la base: Firebird se lo da solo al dueño de la base al crearla.
          AND NOT (p.RDB$OBJECT_TYPE IN (11, 17, 20) AND p.RDB$USER = (SELECT MON$OWNER FROM MON$DATABASE))
          AND p.RDB$USER IS DISTINCT FROM COALESCE(
                (SELECT r.RDB$OWNER_NAME FROM RDB$RELATIONS r WHERE p.RDB$OBJECT_TYPE IN (0, 1) AND r.RDB$RELATION_NAME = p.RDB$RELATION_NAME),
                (SELECT r.RDB$OWNER_NAME FROM RDB$PROCEDURES r WHERE p.RDB$OBJECT_TYPE = 5 AND r.RDB$PROCEDURE_NAME = p.RDB$RELATION_NAME AND r.RDB$PACKAGE_NAME IS NULL),
                (SELECT r.RDB$OWNER_NAME FROM RDB$FUNCTIONS r WHERE p.RDB$OBJECT_TYPE = 15 AND r.RDB$FUNCTION_NAME = p.RDB$RELATION_NAME AND r.RDB$PACKAGE_NAME IS NULL),
                (SELECT r.RDB$OWNER_NAME FROM RDB$PACKAGES r WHERE p.RDB$OBJECT_TYPE = 18 AND r.RDB$PACKAGE_NAME = p.RDB$RELATION_NAME),
                (SELECT r.RDB$OWNER_NAME FROM RDB$GENERATORS r WHERE p.RDB$OBJECT_TYPE = 14 AND r.RDB$GENERATOR_NAME = p.RDB$RELATION_NAME),
                (SELECT r.RDB$OWNER_NAME FROM RDB$EXCEPTIONS r WHERE p.RDB$OBJECT_TYPE = 7 AND r.RDB$EXCEPTION_NAME = p.RDB$RELATION_NAME),
                (SELECT r.RDB$OWNER_NAME FROM RDB$ROLES r WHERE p.RDB$OBJECT_TYPE = 13 AND r.RDB$ROLE_NAME = p.RDB$RELATION_NAME),
                (SELECT r.RDB$OWNER_NAME FROM RDB$FIELDS r WHERE p.RDB$OBJECT_TYPE = 9 AND r.RDB$FIELD_NAME = p.RDB$RELATION_NAME),
                '')
        ORDER BY 5, 1, 3
        """;

    // Permisos dados a un procedimiento, trigger, vista, función o paquete que ya no existe (quedan huérfanos al borrarlo).
    private const string Orphan = """
        ((p.RDB$USER_TYPE = 5 AND NOT EXISTS (SELECT 1 FROM RDB$PROCEDURES x WHERE x.RDB$PROCEDURE_NAME = p.RDB$USER))
              OR (p.RDB$USER_TYPE = 2 AND NOT EXISTS (SELECT 1 FROM RDB$TRIGGERS x WHERE x.RDB$TRIGGER_NAME = p.RDB$USER))
              OR (p.RDB$USER_TYPE = 1 AND NOT EXISTS (SELECT 1 FROM RDB$RELATIONS x WHERE x.RDB$RELATION_NAME = p.RDB$USER))
              OR (p.RDB$USER_TYPE = 15 AND NOT EXISTS (SELECT 1 FROM RDB$FUNCTIONS x WHERE x.RDB$FUNCTION_NAME = p.RDB$USER))
              OR (p.RDB$USER_TYPE = 18 AND NOT EXISTS (SELECT 1 FROM RDB$PACKAGES x WHERE x.RDB$PACKAGE_NAME = p.RDB$USER)))
        """;

    private const string OrphanPrivilegesSql = $"SELECT COUNT(*) FROM RDB$USER_PRIVILEGES p WHERE {Orphan}";

    // Columnas identity con el generador que Firebird les creó por dentro (RDB$n).
    private const string IdentitiesSql = """
        SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), TRIM(rf.RDB$GENERATOR_NAME)
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
        WHERE rf.RDB$GENERATOR_NAME IS NOT NULL AND COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY 1, 2
        """;

    // Vista → relaciones que usa (después se dejan solo las que también son vistas).
    private const string ViewDependenciesSql = """
        SELECT DISTINCT TRIM(d.RDB$DEPENDENT_NAME), TRIM(d.RDB$DEPENDED_ON_NAME)
        FROM RDB$DEPENDENCIES d
        JOIN RDB$RELATIONS v ON v.RDB$RELATION_NAME = d.RDB$DEPENDENT_NAME AND v.RDB$VIEW_BLR IS NOT NULL
        WHERE d.RDB$DEPENDENT_TYPE = 1 AND d.RDB$DEPENDED_ON_TYPE IN (0, 1)
        """;

    /// <summary>
    /// Lee todos los objetos de la base en una sola transacción (una foto coherente), con el mismo DDL que se ve en
    /// la pestaña de cada objeto. Lee solo lo que pide <paramref name="options"/>. En una base grande son miles de
    /// consultas cortas: avisa el avance y se puede cancelar.
    /// </summary>
    public Task<DatabaseSchema> ReadSchemaAsync(SchemaScriptOptions options, IProgress<SchemaProgress>? progress = null, CancellationToken ct = default) =>
        WithTransactionAsync(async (connection, tx) =>
        {
            var q = new MetaQuery(connection, tx, ct);
            _databaseCharset ??= (await q.ListAsync("SELECT TRIM(RDB$CHARACTER_SET_NAME) FROM RDB$DATABASE", r => Str(r, 0))).FirstOrDefault() ?? "NONE";

            async Task<List<string>> Names(DbObjectKind kind) =>
                await q.ListAsync(kind == DbObjectKind.Domain ? DomainsSql : ObjectsSql[kind], r => r.GetString(0).Trim());
            var names = new Dictionary<DbObjectKind, List<string>>();
            foreach (var (kind, wanted) in new[]
                     {
                         (DbObjectKind.Role, options.Roles), (DbObjectKind.Domain, options.Domains), (DbObjectKind.Generator, options.Generators),
                         (DbObjectKind.Exception, options.Exceptions), (DbObjectKind.Table, options.Tables || options.Triggers), (DbObjectKind.View, options.Views),
                         (DbObjectKind.Procedure, options.Procedures), (DbObjectKind.Function, options.Functions), (DbObjectKind.Package, options.Packages),
                     })
                names[kind] = wanted ? await Names(kind) : [];

            var total = names.Values.Sum(n => n.Count);
            var done = 0;
            async Task<List<T>> ReadAll<T>(DbObjectKind kind, Func<string, Task<T?>> read) where T : class
            {
                var list = new List<T>();
                foreach (var name in names[kind])
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new SchemaProgress(done, total, $"{DbObjectKinds.Singular(kind)} {name}"));
                    if (await read(name) is { } definition) list.Add(definition);
                    done++;
                }
                return list;
            }

            var schema = new DatabaseSchema
            {
                Charset = _databaseCharset,
                Roles = await ReadAll(DbObjectKind.Role, n => ReadRoleAsync(q, n)),
                Domains = await ReadAll(DbObjectKind.Domain, n => ReadDomainAsync(q, n)),
                Generators = await ReadAll(DbObjectKind.Generator, n => ReadGeneratorAsync(q, n)),
                Exceptions = await ReadAll(DbObjectKind.Exception, n => ReadExceptionAsync(q, n)),
                Tables = await ReadAll(DbObjectKind.Table, n => ReadTableAsync(q, n)),
                Views = await ReadAll(DbObjectKind.View, n => ReadViewAsync(q, n)),
                Procedures = await ReadAll(DbObjectKind.Procedure, n => ReadProcedureAsync(q, n)),
                Functions = await ReadAll(DbObjectKind.Function, n => ReadFunctionAsync(q, n)),
                Packages = await ReadAll(DbObjectKind.Package, n => ReadPackageAsync(q, n)),
                DatabaseTriggers = options.Triggers ? await ReadTriggersAsync(q, "COALESCE(RDB$RELATION_NAME, '') = @name", "") : [],
            };
            if (options.Views)
            {
                var views = schema.Views.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
                var dependencies = await q.ListAsync(ViewDependenciesSql, r => (View: r.GetString(0).Trim(), Uses: r.GetString(1).Trim()));
                schema = schema with
                {
                    ViewDependencies = dependencies.Where(d => views.Contains(d.Uses) && d.Uses != d.View)
                        .GroupBy(d => d.View).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(d => d.Uses).ToList()),
                };
            }
            if (options.Privileges)
                schema = schema with
                {
                    Privileges = await q.ListAsync(PrivilegesSql, r => new PrivilegeDefinition(
                        Str(r, 0) ?? "", Convert.ToInt32(r.GetValue(1)), Str(r, 2) ?? "", Convert.ToInt32(r.GetValue(3)), Str(r, 4) ?? "",
                        Convert.ToInt32(r.GetValue(5)), Str(r, 6))),
                    OrphanPrivileges = (await q.ListAsync(OrphanPrivilegesSql, r => Convert.ToInt32(r.GetValue(0)))).FirstOrDefault(),
                };
            if (options.Tables && options.GeneratorValues) schema = schema with { IdentityValues = await ReadIdentityValuesAsync(q) };
            progress?.Report(new SchemaProgress(total, total, ""));
            return schema;
        }, ct);

    private static async Task<List<IdentityValue>> ReadIdentityValuesAsync(MetaQuery q)
    {
        var list = new List<IdentityValue>();
        foreach (var (table, column, generator) in await q.ListAsync(IdentitiesSql, r => (r.GetString(0), r.GetString(1), r.GetString(2))))
        {
            try
            {
                var value = (await q.ListAsync($"SELECT GEN_ID({SqlIdentifier.Quote(generator)}, 0) FROM RDB$DATABASE", r => Convert.ToInt64(r.GetValue(0)))).FirstOrDefault();
                list.Add(new IdentityValue(table, column, value));
            }
            catch (FbException)
            {
                // Sin permiso de uso sobre el generador: la identity queda como en una base nueva.
            }
        }
        return list;
    }
}
