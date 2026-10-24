using System.Text;
using FbStudio.Core.Resources;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Metadata;

/// <summary>
/// El script de la base entera, en un orden que se puede ejecutar de corrido
/// sobre una base vacía: roles, dominios, generadores, excepciones y UDF; tablas con su clave; procedimientos,
/// funciones y paquetes «vacíos» (solo la firma), así no importa quién usa a quién; vistas en orden de dependencias;
/// columnas calculadas, claves foráneas, CHECK e índices; los cuerpos de verdad; triggers, comentarios y permisos.
/// Entre secciones va un COMMIT (FbStudio e isql lo ejecutan).
/// </summary>
public static class SchemaScriptWriter
{
    private static string Q(string name) => SqlIdentifier.Quote(name);

    /// <summary>Encabezado del script: de qué base salió, cuándo, y sobre qué base hay que ejecutarlo.</summary>
    public static string Header(string database, string location, DateTime when, string? charset) =>
        "/* " + string.Format(CoreStrings.ScriptHeader, database, location, when, charset ?? "NONE").Replace("*/", "* /") + " */";

    /// <summary>
    /// El script de la base, entero o una de sus dos partes (<see cref="SchemaScriptParts"/>). Entero, cada sección va
    /// en el mismo orden que siempre.
    /// </summary>
    public static string Write(DatabaseSchema schema, SchemaScriptOptions options, string? header = null, SchemaScriptParts parts = SchemaScriptParts.All)
    {
        var structure = parts.HasFlag(SchemaScriptParts.Structure);
        var deferred = parts.HasFlag(SchemaScriptParts.Deferred);
        var sb = new StringBuilder();
        if (header is not null) sb.Append(header.TrimEnd()).Append("\n\n");

        var tables = options.Tables ? schema.Tables.OrderBy(t => t.Name, StringComparer.Ordinal).ToList() : [];
        var procedures = options.Procedures ? schema.Procedures.OrderBy(p => p.Name, StringComparer.Ordinal).ToList() : [];
        var functions = options.Functions ? schema.Functions.Where(f => f.ModuleName is null).OrderBy(f => f.Name, StringComparer.Ordinal).ToList() : [];
        var packages = options.Packages ? schema.Packages.OrderBy(p => p.Name, StringComparer.Ordinal).ToList() : [];

        if (structure)
        {
            if (options.Roles) Section(sb, CoreStrings.ScriptRoles, schema.Roles.OrderBy(r => r.Name, StringComparer.Ordinal).Select(DdlWriter.Role));
            if (options.Domains) Section(sb, CoreStrings.ScriptDomains, schema.Domains.OrderBy(d => d.Field.Name, StringComparer.Ordinal).Select(DdlWriter.Domain));
            if (options.Generators) Section(sb, CoreStrings.ScriptGenerators, schema.Generators.OrderBy(g => g.Name, StringComparer.Ordinal).Select(g => Generator(g, options.GeneratorValues)));
            if (options.Exceptions) Section(sb, CoreStrings.ScriptExceptions, schema.Exceptions.OrderBy(e => e.Name, StringComparer.Ordinal).Select(DdlWriter.Exception));
            if (options.Functions) Section(sb, CoreStrings.ScriptExternalFunctions, schema.Functions.Where(f => f.ModuleName is not null).OrderBy(f => f.Name, StringComparer.Ordinal).Select(DdlWriter.Function));

            Section(sb, CoreStrings.ScriptTables, tables.Select(TableWithoutDependencies));
            if (options.GeneratorValues)
            {
                var names = tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
                PsqlSection(sb, CoreStrings.ScriptIdentityValues, [.. schema.IdentityValues.Where(i => names.Contains(i.Table) && i.Value != 0)
                    .OrderBy(i => i.Table, StringComparer.Ordinal).ThenBy(i => i.Column, StringComparer.Ordinal).Select(Identity)]);
            }

            // Firmas primero: con todo declarado, las vistas, los cuerpos y los triggers se crean en cualquier orden.
            PsqlSection(sb, CoreStrings.ScriptSignatures,
            [
                .. procedures.Select(p => DdlWriter.Procedure(p with { Source = "BEGIN\n  EXIT;\nEND" })),
                .. functions.Select(f => DdlWriter.Function(f with { Source = "BEGIN\n  RETURN NULL;\nEND" })),
                .. packages.Select(p => $"CREATE OR ALTER PACKAGE {Q(p.Name)}\nAS\n{(p.Header ?? "BEGIN\nEND").Trim()}"),
            ]);

            if (options.Views) Section(sb, CoreStrings.ScriptViews, OrderViews(schema).Select(v => DdlWriter.View(v) + ";"));
            Section(sb, CoreStrings.ScriptComputedColumns, tables.SelectMany(ComputedColumns));
        }

        if (deferred)
        {
            Section(sb, CoreStrings.ScriptForeignKeys, tables.SelectMany(t => t.Constraints.Where(c => c.Kind == ConstraintKind.ForeignKey).Select(c => DdlWriter.Constraint(t.Name, c))));
            Section(sb, CoreStrings.ScriptChecks, tables.SelectMany(t => t.Constraints.Where(c => c.Kind == ConstraintKind.Check).Select(c => DdlWriter.Constraint(t.Name, c))));
        }

        if (structure)
        {
            Section(sb, CoreStrings.ScriptIndices, tables.SelectMany(t => t.Indices.OrderBy(i => i.Name, StringComparer.Ordinal).Select(DdlWriter.Index)));
            PsqlSection(sb, CoreStrings.ScriptBodies,
            [
                .. procedures.Select(DdlWriter.Procedure),
                .. functions.Select(DdlWriter.Function),
                .. packages.Where(p => p.Body is not null).Select(p => $"RECREATE PACKAGE BODY {Q(p.Name)}\nAS\n{p.Body!.Trim()}"),
            ]);
        }

        if (deferred && options.Triggers)
        {
            var views = options.Views ? schema.Views : [];
            var triggers = schema.Tables.SelectMany(t => t.Triggers).Concat(views.SelectMany(v => v.Triggers)).Concat(schema.DatabaseTriggers)
                .OrderBy(t => t.Table ?? "", StringComparer.Ordinal).ThenBy(t => t.Type).ThenBy(t => t.Position).ThenBy(t => t.Name, StringComparer.Ordinal);
            PsqlSection(sb, CoreStrings.ScriptTriggers, [.. triggers.Select(DdlWriter.Trigger)]);
        }

        Section(sb, CoreStrings.ScriptComments, Comments(schema, options, tables, procedures, functions, packages, structure, deferred));
        if (deferred && options.Privileges)
            Section(sb, CoreStrings.ScriptPrivileges, [.. Grants(schema.Privileges),
                .. schema.OrphanPrivileges > 0 ? ["-- " + string.Format(CoreStrings.ScriptOrphanPrivileges, schema.OrphanPrivileges)] : Array.Empty<string>()]);
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>CREATE TABLE sin columnas calculadas (pueden usar funciones u otras tablas) y con su clave primaria y únicas.</summary>
    private static string TableWithoutDependencies(TableDefinition t)
    {
        var plain = t with
        {
            Columns = t.Columns.Where(c => c.Computed is null).ToList(),
            Constraints = t.Constraints.Where(c => c.Kind is ConstraintKind.PrimaryKey or ConstraintKind.Unique).ToList(),
            Indices = [],
            Triggers = [],
            Description = null,
        };
        // Sin comentarios: van todos juntos al final.
        return DdlWriter.Table(plain with { Columns = plain.Columns.Select(c => c with { Description = null }).ToList() }).TrimEnd();
    }

    /// <summary>Las columnas calculadas al final, y cada una vuelta a su lugar (ALTER … POSITION).</summary>
    private static IEnumerable<string> ComputedColumns(TableDefinition t)
    {
        var computed = t.Columns.Select((c, i) => (Column: c, Position: i + 1)).Where(x => x.Column.Computed is not null).ToList();
        foreach (var (column, _) in computed)
            yield return $"ALTER TABLE {Q(t.Name)} ADD {DdlWriter.Column(column with { Description = null })};";
        foreach (var (column, position) in computed.Where(x => x.Position < t.Columns.Count))
            yield return $"ALTER TABLE {Q(t.Name)} ALTER {Q(column.Name)} POSITION {position};";
    }

    /// <summary>
    /// Deja la identity con el mismo valor actual que en el origen. No sirve RESTART WITH: en Firebird 3 el próximo
    /// valor es n + 1 y en 4 y 5 es n. GEN_ID suma la diferencia y da lo mismo en todas; el generador interno (RDB$n)
    /// se busca en la base nueva porque ahí puede tener otro número.
    /// </summary>
    private static string Identity(IdentityValue i) => $$"""
        EXECUTE BLOCK AS
          DECLARE G VARCHAR(63);
          DECLARE V BIGINT;
        BEGIN
          SELECT TRIM(RDB$GENERATOR_NAME) FROM RDB$RELATION_FIELDS
          WHERE RDB$RELATION_NAME = '{{i.Table.Replace("'", "''")}}' AND RDB$FIELD_NAME = '{{i.Column.Replace("'", "''")}}' INTO :G;
          EXECUTE STATEMENT 'SELECT GEN_ID("' || G || '", 0) FROM RDB$DATABASE' INTO :V;
          EXECUTE STATEMENT 'SELECT GEN_ID("' || G || '", ' || ({{i.Value}} - V) || ') FROM RDB$DATABASE' INTO :V;
        END
        """;

    private static string Generator(GeneratorDefinition g, bool withValue)
    {
        var sb = new StringBuilder("CREATE SEQUENCE ").Append(Q(g.Name));
        if (g.InitialValue != 0) sb.Append(" START WITH ").Append(g.InitialValue);
        if (g.Increment != 1) sb.Append(" INCREMENT BY ").Append(g.Increment);
        sb.Append(';');
        // SET GENERATOR deja el valor actual (el próximo es ese + incremento) igual en Firebird 3 y 4.
        if (withValue && g.CurrentValue is { } value && value != g.InitialValue) sb.Append($"\nSET GENERATOR {Q(g.Name)} TO {value};");
        if (DdlWriter.Comment("SEQUENCE", Q(g.Name), g.Description) is { } comment) sb.Append('\n').Append(comment);
        return sb.ToString();
    }

    /// <summary>Las vistas de modo que cada una venga después de las que usa.</summary>
    public static IReadOnlyList<ViewDefinition> OrderViews(DatabaseSchema schema)
    {
        var byName = schema.Views.ToDictionary(v => v.Name, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<ViewDefinition>();
        void Visit(ViewDefinition view, HashSet<string> path)
        {
            if (done.Contains(view.Name) || !path.Add(view.Name)) return;
            foreach (var used in schema.ViewDependencies.GetValueOrDefault(view.Name) ?? [])
                if (byName.TryGetValue(used, out var dependency)) Visit(dependency, path);
            if (done.Add(view.Name)) ordered.Add(view);
        }
        foreach (var view in schema.Views.OrderBy(v => v.Name, StringComparer.Ordinal)) Visit(view, []);
        return ordered;
    }

    /// <param name="objects">Los de todo menos los triggers (van con la estructura).</param>
    /// <param name="ofTriggers">Los de los triggers (van con la parte diferida, después de crearlos).</param>
    private static IEnumerable<string> Comments(DatabaseSchema schema, SchemaScriptOptions options, IReadOnlyList<TableDefinition> tables,
        IReadOnlyList<ProcedureDefinition> procedures, IReadOnlyList<FunctionDefinition> functions, IReadOnlyList<PackageDefinition> packages,
        bool objects, bool ofTriggers)
    {
        var list = new List<string?>();
        if (!objects) tables = [];
        foreach (var t in tables)
        {
            list.Add(DdlWriter.Comment("TABLE", Q(t.Name), t.Description));
            list.AddRange(t.Columns.Select(c => DdlWriter.Comment("COLUMN", $"{Q(t.Name)}.{Q(c.Name)}", c.Description)));
            list.AddRange(t.Indices.Select(i => DdlWriter.Comment("INDEX", Q(i.Name), i.Description)));
        }
        if (objects && options.Views)
            foreach (var v in schema.Views.OrderBy(v => v.Name, StringComparer.Ordinal))
            {
                list.Add(DdlWriter.Comment("VIEW", Q(v.Name), v.Description));
                list.AddRange(v.Columns.Select(c => DdlWriter.Comment("COLUMN", $"{Q(v.Name)}.{Q(c.Name)}", c.Description)));
            }
        foreach (var p in objects ? procedures : [])
        {
            list.Add(DdlWriter.Comment("PROCEDURE", Q(p.Name), p.Description));
            list.AddRange(p.Inputs.Concat(p.Outputs).Select(a => DdlWriter.Comment("PROCEDURE PARAMETER", $"{Q(p.Name)}.{Q(a.Name)}", a.Description)));
        }
        foreach (var f in objects ? functions : [])
        {
            list.Add(DdlWriter.Comment("FUNCTION", Q(f.Name), f.Description));
            list.AddRange(f.Inputs.Select(a => DdlWriter.Comment("FUNCTION PARAMETER", $"{Q(f.Name)}.{Q(a.Name)}", a.Description)));
        }
        if (objects) list.AddRange(packages.Select(p => DdlWriter.Comment("PACKAGE", Q(p.Name), p.Description)));
        if (ofTriggers && options.Triggers)
        {
            var triggers = schema.Tables.SelectMany(t => t.Triggers).Concat(options.Views ? schema.Views.SelectMany(v => v.Triggers) : []).Concat(schema.DatabaseTriggers);
            list.AddRange(triggers.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => DdlWriter.Comment("TRIGGER", Q(t.Name), t.Description)));
        }
        return list.OfType<string>();
    }

    /// <summary>
    /// GRANT agrupados por quién, qué y sobre qué. Los permisos de DDL (CREATE/ALTER/DROP de un tipo de objeto) no se
    /// escriben: se avisa cuántos son.
    /// </summary>
    public static IEnumerable<string> Grants(IReadOnlyList<PrivilegeDefinition> privileges)
    {
        var skipped = 0;
        var lines = new List<string>();
        foreach (var group in privileges.GroupBy(p => (p.Object, p.ObjectType, p.Grantee, p.GranteeType, p.GrantOption))
                     .OrderBy(g => g.Key.Object, StringComparer.Ordinal).ThenBy(g => g.Key.Grantee, StringComparer.Ordinal))
        {
            var (obj, objectType, grantee, granteeType, option) = group.Key;
            if (Grantee(grantee, granteeType) is not { } to)
            {
                skipped += group.Count();
                continue;
            }
            string? statement = objectType switch
            {
                0 or 1 => RelationGrant(group, obj),
                5 when group.Any(p => p.Privilege == "X") => $"GRANT EXECUTE ON PROCEDURE {Q(obj)}",
                15 when group.Any(p => p.Privilege == "X") => $"GRANT EXECUTE ON FUNCTION {Q(obj)}",
                18 when group.Any(p => p.Privilege == "X") => $"GRANT EXECUTE ON PACKAGE {Q(obj)}",
                14 when group.Any(p => p.Privilege == "G") => $"GRANT USAGE ON SEQUENCE {Q(obj)}",
                7 when group.Any(p => p.Privilege == "G") => $"GRANT USAGE ON EXCEPTION {Q(obj)}",
                9 when group.Any(p => p.Privilege == "G") => $"GRANT USAGE ON DOMAIN {Q(obj)}",
                13 when group.Any(p => p.Privilege == "M") => $"GRANT {Q(obj)}",
                _ => null,
            };
            if (statement is null)
            {
                skipped += group.Count();
                continue;
            }
            var suffix = option switch
            {
                2 when objectType == 13 => " WITH ADMIN OPTION",
                > 0 when objectType != 13 => " WITH GRANT OPTION",
                _ => "",
            };
            lines.Add($"{statement} TO {to}{suffix};");
        }
        if (skipped > 0) lines.Add("-- " + string.Format(CoreStrings.ScriptPrivilegesSkipped, skipped));
        return lines;
    }

    private static string? RelationGrant(IEnumerable<PrivilegeDefinition> group, string table)
    {
        var parts = new List<string>();
        foreach (var (letter, word) in new[] { ("S", "SELECT"), ("I", "INSERT"), ("U", "UPDATE"), ("D", "DELETE"), ("R", "REFERENCES") })
        {
            var rows = group.Where(p => p.Privilege == letter).ToList();
            if (rows.Count == 0) continue;
            // UPDATE y REFERENCES pueden ser por columna; si alguna fila es de toda la tabla, vale para toda.
            var columns = rows.Any(r => r.Field is null) ? [] : rows.Select(r => r.Field!).Distinct().Order(StringComparer.Ordinal).ToList();
            parts.Add(columns.Count == 0 ? word : $"{word} ({string.Join(", ", columns.Select(Q))})");
        }
        return parts.Count == 0 ? null : $"GRANT {string.Join(", ", parts)} ON {Q(table)}";
    }

    private static string? Grantee(string name, int type) => type switch
    {
        8 => name == "PUBLIC" ? "PUBLIC" : "USER " + Q(name),
        13 => "ROLE " + Q(name),
        5 => "PROCEDURE " + Q(name),
        2 => "TRIGGER " + Q(name),
        1 => "VIEW " + Q(name),
        15 => "FUNCTION " + Q(name),
        18 => "PACKAGE " + Q(name),
        _ => null,
    };

    private static void Section(StringBuilder sb, string title, IEnumerable<string> statements)
    {
        var list = statements.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (list.Count == 0) return;
        sb.Append("/* ").Append(title).Append(" */\n\n");
        foreach (var s in list) sb.Append(s.TrimEnd()).Append("\n\n");
        sb.Append("COMMIT;\n\n");
    }

    /// <summary>Bloques PSQL entre SET TERM ^ (cada uno termina en ^).</summary>
    private static void PsqlSection(StringBuilder sb, string title, IReadOnlyList<string> blocks)
    {
        if (blocks.Count == 0) return;
        sb.Append("/* ").Append(title).Append(" */\n\nSET TERM ^ ;\n\n");
        foreach (var b in blocks) sb.Append(b.TrimEnd()).Append("^\n\n");
        sb.Append("SET TERM ; ^\n\nCOMMIT;\n\n");
    }
}
