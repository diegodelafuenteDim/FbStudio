namespace FbStudio.Core.Metadata;

/// <summary>
/// La parte de una base que corresponde a un área (F-9): sus tablas (con sus índices y triggers), vistas,
/// procedimientos, funciones, paquetes, generadores y excepciones, los dominios que usan, los roles y permisos sobre
/// ellos. Las FK a tablas de afuera no van (se cuentan): así el script del área corre en una base vacía y la
/// comparación de un área no marca diferencias por lo que está afuera.
/// </summary>
public static class SchemaFilter
{
    /// <param name="members">Los nombres de los objetos del área (como los da <see cref="AreaMembership"/>).</param>
    public static DatabaseSchema Only(DatabaseSchema schema, string area, IReadOnlySet<string> members)
    {
        bool In(string name) => members.Contains(name);
        var outside = 0;
        var tables = schema.Tables.Where(t => In(t.Name)).Select(t =>
        {
            var constraints = t.Constraints.Where(c => c.Kind != ConstraintKind.ForeignKey || c.ReferencedTable is null || In(c.ReferencedTable)).ToList();
            outside += t.Constraints.Count - constraints.Count;
            return t with { Constraints = constraints };
        }).ToList();
        var views = schema.Views.Where(v => In(v.Name)).ToList();
        var procedures = schema.Procedures.Where(p => In(p.Name)).ToList();
        var functions = schema.Functions.Where(f => In(f.Name)).ToList();
        var packages = schema.Packages.Where(p => In(p.Name)).ToList();

        // Los dominios que usan las columnas y parámetros de lo que queda (o que están en el área).
        var types = tables.SelectMany(t => t.Columns).Concat(views.SelectMany(v => v.Columns))
            .Concat(procedures.SelectMany(p => p.Inputs.Concat(p.Outputs)))
            .Concat(functions.SelectMany(f => f.Inputs.Append(f.Returns)))
            .Select(f => f.Type.Trim().Trim('"'))
            .ToHashSet(StringComparer.Ordinal);
        var domains = schema.Domains.Where(d => In(d.Field.Name) || types.Contains(d.Field.Name)).ToList();

        var kept = tables.Select(t => t.Name).Concat(views.Select(v => v.Name)).Concat(procedures.Select(p => p.Name))
            .Concat(functions.Select(f => f.Name)).Concat(packages.Select(p => p.Name))
            .Concat(schema.Generators.Where(g => In(g.Name)).Select(g => g.Name))
            .Concat(schema.Exceptions.Where(e => In(e.Name)).Select(e => e.Name))
            .ToHashSet(StringComparer.Ordinal);
        var privileges = schema.Privileges.Where(p => kept.Contains(p.Object)).ToList();
        var roles = schema.Roles.Where(r => privileges.Any(p => p.Grantee == r.Name)).ToList();
        return schema with
        {
            Area = area,
            ForeignKeysOutside = outside,
            Roles = roles,
            Domains = domains,
            Generators = schema.Generators.Where(g => In(g.Name)).ToList(),
            Exceptions = schema.Exceptions.Where(e => In(e.Name)).ToList(),
            Tables = tables,
            Views = views,
            Procedures = procedures,
            Functions = functions,
            Packages = packages,
            DatabaseTriggers = schema.DatabaseTriggers.Where(t => In(t.Name)).ToList(),
            ViewDependencies = schema.ViewDependencies.Where(d => In(d.Key))
                .ToDictionary(d => d.Key, d => (IReadOnlyList<string>)d.Value.Where(In).ToList()),
            Privileges = privileges,
            IdentityValues = schema.IdentityValues.Where(i => In(i.Table)).ToList(),
        };
    }
}
