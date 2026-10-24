using System.Text;
using FbStudio.Core.Resources;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Metadata;

/// <summary>Qué se compara: los objetos de la base y, aparte, los permisos.</summary>
public enum SchemaItemKind
{
    Role,
    Domain,
    Generator,
    Exception,
    Table,
    Index,
    View,
    Procedure,
    Function,
    Package,
    Trigger,
    Privilege,
}

public enum SchemaChange
{
    /// <summary>Está en el origen y no en el destino: se crea.</summary>
    Added,

    /// <summary>Está en los dos con distinta definición: se modifica.</summary>
    Changed,

    /// <summary>Está solo en el destino: se borra solo si se pide.</summary>
    Removed,
}

/// <summary>Una sentencia del script para igualar, con la etapa en la que va (ver <see cref="SchemaComparer"/>).</summary>
public sealed record SyncStatement(int Phase, string Sql, bool IsPsql = false, int Order = 0);

/// <summary>Una diferencia entre las dos bases, con lo que hay que ejecutar en el destino para igualarla.</summary>
/// <param name="Details">Qué cambió, en castellano («columna OBS: VARCHAR(100) → VARCHAR(200)»).</param>
/// <param name="SourceDdl">DDL en el origen (null si no está).</param>
/// <param name="TargetDdl">DDL en el destino (null si no está).</param>
public sealed record SchemaDifference(
    SchemaItemKind Kind, string Name, SchemaChange Change, IReadOnlyList<string> Details,
    string? SourceDdl, string? TargetDdl, IReadOnlyList<SyncStatement> Statements);

public sealed record SchemaCompareOptions
{
    /// <summary>Borrar en el destino lo que no está en el origen (objetos, columnas, índices, permisos). Por defecto, no.</summary>
    public bool DropExtra { get; init; }

    public bool Privileges { get; init; } = true;

    /// <summary>Dejar las columnas en el mismo orden que en el origen (ALTER … POSITION).</summary>
    public bool ColumnPositions { get; init; } = true;
}

/// <summary>
/// Compara dos bases y arma el script para que el destino quede igual al
/// origen: las tablas se modifican columna por columna (nunca se recrean: los datos no se tocan); procedimientos,
/// vistas, funciones y triggers van con CREATE OR ALTER. El script sigue el mismo orden que el de la base entera
/// (primero lo que se borra, después dominios, tablas, firmas, vistas, restricciones, índices, cuerpos, triggers,
/// comentarios y permisos), con COMMIT entre etapas.
/// </summary>
public static class SchemaComparer
{
    // Etapas del script para igualar.
    public const int PhaseDropTriggers = 0, PhaseDropViews = 1, PhaseDropPsql = 2, PhaseDropConstraints = 3, PhaseDropIndices = 4,
        PhaseDropColumns = 5, PhaseDropTables = 6, PhaseDropOthers = 7, PhaseRoles = 10, PhaseDomains = 11, PhaseGenerators = 12,
        PhaseExceptions = 13, PhaseExternalFunctions = 14, PhaseNewTables = 20, PhaseColumns = 21, PhaseSignatures = 30, PhaseViews = 40,
        PhaseComputed = 50, PhaseConstraints = 60, PhaseIndices = 70, PhaseBodies = 80, PhaseTriggers = 90, PhaseComments = 95, PhasePrivileges = 99;

    private static string Q(string name) => SqlIdentifier.Quote(name);

    public static IReadOnlyList<SchemaDifference> Compare(DatabaseSchema source, DatabaseSchema target, SchemaCompareOptions options)
    {
        var list = new List<SchemaDifference>();
        var drop = options.DropExtra;

        Simple(list, SchemaItemKind.Role, source.Roles, target.Roles, r => r.Name, r => DdlWriter.Role(r with { Owner = null }),
            r => [new(PhaseRoles, $"CREATE ROLE {Q(r.Name)};")], (_, _) => [], r => drop ? [new(PhaseDropOthers, $"DROP ROLE {Q(r.Name)};")] : []);
        Simple(list, SchemaItemKind.Domain, source.Domains, target.Domains, d => d.Field.Name, DdlWriter.Domain,
            d => [new(PhaseDomains, DdlWriter.Domain(d with { Field = d.Field with { Description = null } }).Trim()), .. Comment("DOMAIN", d.Field.Name, d.Field.Description)],
            AlterDomain, d => drop ? [new(PhaseDropOthers, $"DROP DOMAIN {Q(d.Field.Name)};")] : []);
        Simple(list, SchemaItemKind.Generator, source.Generators, target.Generators, g => g.Name, GeneratorDdl,
            g => [new(PhaseGenerators, GeneratorDdl(g)), .. Comment("SEQUENCE", g.Name, g.Description)],
            (s, t) => [.. s.Increment != t.Increment ? [new SyncStatement(PhaseGenerators, $"ALTER SEQUENCE {Q(s.Name)} INCREMENT BY {s.Increment};")] : Array.Empty<SyncStatement>(),
                       .. CommentIfChanged("SEQUENCE", s.Name, s.Description, t.Description)],
            g => drop ? [new(PhaseDropOthers, $"DROP SEQUENCE {Q(g.Name)};")] : []);
        Simple(list, SchemaItemKind.Exception, source.Exceptions, target.Exceptions, e => e.Name, DdlWriter.Exception,
            e => [new(PhaseExceptions, DdlWriter.Exception(e with { Description = null }).Trim()), .. Comment("EXCEPTION", e.Name, e.Description)],
            (s, t) => [.. s.Message != t.Message ? [new SyncStatement(PhaseExceptions, DdlWriter.Exception(s with { Description = null }).Trim())] : Array.Empty<SyncStatement>(),
                       .. CommentIfChanged("EXCEPTION", s.Name, s.Description, t.Description)],
            e => drop ? [new(PhaseDropOthers, $"DROP EXCEPTION {Q(e.Name)};")] : []);

        Tables(list, source, target, options);
        Indices(list, source, target, drop);
        Views(list, source, target, drop);

        Simple(list, SchemaItemKind.Procedure, source.Procedures, target.Procedures, p => p.Name,
            p => DdlWriter.Procedure(p) + CommentLines("PROCEDURE", p.Name, p.Description, "PROCEDURE PARAMETER", p.Inputs.Concat(p.Outputs)),
            p => [new(PhaseSignatures, DdlWriter.Procedure(p with { Source = "BEGIN\n  EXIT;\nEND" }), true), new(PhaseBodies, DdlWriter.Procedure(p), true),
                  .. Comment("PROCEDURE", p.Name, p.Description), .. ParameterComments("PROCEDURE PARAMETER", p.Name, p.Inputs.Concat(p.Outputs), [])],
            (s, t) => [.. Same(DdlWriter.Procedure(s), DdlWriter.Procedure(t)) ? Array.Empty<SyncStatement>() : [new SyncStatement(PhaseBodies, DdlWriter.Procedure(s), true)],
                       .. CommentIfChanged("PROCEDURE", s.Name, s.Description, t.Description),
                       .. ParameterComments("PROCEDURE PARAMETER", s.Name, s.Inputs.Concat(s.Outputs), t.Inputs.Concat(t.Outputs))],
            p => drop ? [new(PhaseDropPsql, $"DROP PROCEDURE {Q(p.Name)};")] : []);
        Simple(list, SchemaItemKind.Function, source.Functions, target.Functions, f => f.Name,
            f => f.ModuleName is not null ? DdlWriter.Function(f) : DdlWriter.Function(f) + CommentLines("FUNCTION", f.Name, f.Description, "FUNCTION PARAMETER", f.Inputs),
            f => f.ModuleName is not null
                ? [new(PhaseExternalFunctions, DdlWriter.Function(f with { Description = null }).Trim()), .. Comment("EXTERNAL FUNCTION", f.Name, f.Description)]
                : [new(PhaseSignatures, DdlWriter.Function(f with { Source = "BEGIN\n  RETURN NULL;\nEND" }), true), new(PhaseBodies, DdlWriter.Function(f), true),
                   .. Comment("FUNCTION", f.Name, f.Description)],
            (s, t) => [.. Same(DdlWriter.Function(s with { Description = null }), DdlWriter.Function(t with { Description = null })) ? Array.Empty<SyncStatement>()
                           : s.ModuleName is null ? [new SyncStatement(PhaseBodies, DdlWriter.Function(s), true)]
                           : [new SyncStatement(PhaseDropPsql, $"DROP EXTERNAL FUNCTION {Q(s.Name)};"), new SyncStatement(PhaseExternalFunctions, DdlWriter.Function(s with { Description = null }).Trim())],
                       .. CommentIfChanged(s.ModuleName is null ? "FUNCTION" : "EXTERNAL FUNCTION", s.Name, s.Description, t.Description)],
            f => drop ? [new(PhaseDropPsql, f.ModuleName is null ? $"DROP FUNCTION {Q(f.Name)};" : $"DROP EXTERNAL FUNCTION {Q(f.Name)};")] : []);
        Simple(list, SchemaItemKind.Package, source.Packages, target.Packages, p => p.Name, DdlWriter.Package,
            p => [new(PhaseSignatures, PackageHeader(p), true), .. p.Body is null ? [] : new[] { new SyncStatement(PhaseBodies, PackageBody(p), true) }, .. Comment("PACKAGE", p.Name, p.Description)],
            (s, t) => [.. Same(s.Header, t.Header) ? Array.Empty<SyncStatement>() : [new SyncStatement(PhaseBodies, PackageHeader(s), true)],
                       .. Same(s.Header, t.Header) && Same(s.Body, t.Body) || s.Body is null ? Array.Empty<SyncStatement>() : [new SyncStatement(PhaseBodies, PackageBody(s), true)],
                       .. CommentIfChanged("PACKAGE", s.Name, s.Description, t.Description)],
            p => drop ? [new(PhaseDropPsql, $"DROP PACKAGE {Q(p.Name)};")] : []);
        Simple(list, SchemaItemKind.Trigger, AllTriggers(source), AllTriggers(target), t => t.Name, t => DdlWriter.Trigger(t) + CommentLines("TRIGGER", t.Name, t.Description, null, []),
            t => [new(PhaseTriggers, DdlWriter.Trigger(t), true), .. Comment("TRIGGER", t.Name, t.Description)],
            (s, t) => [.. Same(DdlWriter.Trigger(s), DdlWriter.Trigger(t)) ? Array.Empty<SyncStatement>() : [new SyncStatement(PhaseTriggers, DdlWriter.Trigger(s), true)],
                       .. CommentIfChanged("TRIGGER", s.Name, s.Description, t.Description)],
            t => drop ? [new(PhaseDropTriggers, $"DROP TRIGGER {Q(t.Name)};")] : []);

        if (options.Privileges) Privileges(list, source, target, drop);
        return list;
    }

    // ---------- objetos simples: nuevo, distinto (con su ALTER) o sobra ----------

    private static void Simple<T>(List<SchemaDifference> list, SchemaItemKind kind, IReadOnlyList<T> source, IReadOnlyList<T> target,
        Func<T, string> name, Func<T, string> ddl, Func<T, IEnumerable<SyncStatement>> create, Func<T, T, IEnumerable<SyncStatement>> alter,
        Func<T, IEnumerable<SyncStatement>> remove)
    {
        var targets = target.ToDictionary(name, StringComparer.Ordinal);
        foreach (var s in source.OrderBy(name, StringComparer.Ordinal))
        {
            if (!targets.Remove(name(s), out var t))
            {
                list.Add(new SchemaDifference(kind, name(s), SchemaChange.Added, [CoreStrings.CompareNew], ddl(s), null, [.. create(s)]));
                continue;
            }
            if (Same(ddl(s), ddl(t))) continue;
            var statements = alter(s, t).ToList();
            list.Add(new SchemaDifference(kind, name(s), SchemaChange.Changed, Describe(ddl(s), ddl(t)), ddl(s), ddl(t), statements));
        }
        foreach (var t in targets.Values.OrderBy(name, StringComparer.Ordinal))
            list.Add(new SchemaDifference(kind, name(t), SchemaChange.Removed, [CoreStrings.CompareRemoved], null, ddl(t), [.. remove(t)]));
    }

    /// <summary>«definición distinta» o, si es solo eso, «comentario distinto».</summary>
    private static List<string> Describe(string source, string target)
    {
        static string WithoutComments(string ddl) => string.Join("\n", Lines(ddl).Where(l => !l.StartsWith("COMMENT ON ", StringComparison.Ordinal)));
        return Same(WithoutComments(source), WithoutComments(target)) ? [CoreStrings.CompareDescription] : [CoreStrings.CompareDefinition];
    }

    private static IEnumerable<SyncStatement> AlterDomain(DomainDefinition s, DomainDefinition t)
    {
        var (a, b) = (s.Field, t.Field);
        var name = Q(a.Name);
        if (a.Type != b.Type || a.CharacterSet != b.CharacterSet)
            yield return new(PhaseDomains, $"ALTER DOMAIN {name} TYPE {a.Type}{(a.CharacterSet is { } cs ? " CHARACTER SET " + Q(cs) : "")};");
        if (a.Default != b.Default)
            yield return new(PhaseDomains, a.Default is { } d ? $"ALTER DOMAIN {name} SET DEFAULT {DefaultValue(d)};" : $"ALTER DOMAIN {name} DROP DEFAULT;");
        if (a.NotNull != b.NotNull) yield return new(PhaseDomains, $"ALTER DOMAIN {name} {(a.NotNull ? "SET" : "DROP")} NOT NULL;");
        if (!Same(s.Check, t.Check))
        {
            if (t.Check is not null) yield return new(PhaseDomains, $"ALTER DOMAIN {name} DROP CONSTRAINT;");
            if (s.Check is { } check) yield return new(PhaseDomains, $"ALTER DOMAIN {name} ADD {DdlWriter.InlineSafe(check.Trim())};");
        }
        if (a.Collation != b.Collation) yield return new(PhaseDomains, "-- " + Sql.SqlIdentifier.OneLine(string.Format(CoreStrings.CompareCollationManual, a.Name)));
        foreach (var c in CommentIfChanged("DOMAIN", a.Name, a.Description, b.Description)) yield return c;
    }

    private static string GeneratorDdl(GeneratorDefinition g) =>
        $"CREATE SEQUENCE {Q(g.Name)}{(g.InitialValue != 0 ? $" START WITH {g.InitialValue}" : "")}{(g.Increment != 1 ? $" INCREMENT BY {g.Increment}" : "")};"
        + (DdlWriter.Comment("SEQUENCE", Q(g.Name), g.Description) is { } c ? "\n" + c : "");

    private static string PackageHeader(PackageDefinition p) => $"CREATE OR ALTER PACKAGE {Q(p.Name)}\nAS\n{(p.Header ?? "BEGIN\nEND").Trim()}";

    private static string PackageBody(PackageDefinition p) => $"RECREATE PACKAGE BODY {Q(p.Name)}\nAS\n{p.Body!.Trim()}";

    private static IReadOnlyList<TriggerDefinition> AllTriggers(DatabaseSchema s) =>
        [.. s.Tables.SelectMany(t => t.Triggers), .. s.Views.SelectMany(v => v.Triggers), .. s.DatabaseTriggers];

    // ---------- tablas: columna por columna ----------

    private static void Tables(List<SchemaDifference> list, DatabaseSchema source, DatabaseSchema target, SchemaCompareOptions options)
    {
        var targets = target.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        foreach (var s in source.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var ddl = TableDdl(s);
            if (!targets.Remove(s.Name, out var t))
            {
                list.Add(new SchemaDifference(SchemaItemKind.Table, s.Name, SchemaChange.Added, [CoreStrings.CompareNew], ddl, null, [.. NewTable(s)]));
                continue;
            }
            var (details, statements) = AlterTable(s, t, options);
            if (details.Count > 0) list.Add(new SchemaDifference(SchemaItemKind.Table, s.Name, SchemaChange.Changed, details, ddl, TableDdl(t), statements));
        }
        foreach (var t in targets.Values.OrderBy(t => t.Name, StringComparer.Ordinal))
            list.Add(new SchemaDifference(SchemaItemKind.Table, t.Name, SchemaChange.Removed, [CoreStrings.CompareRemoved], null, TableDdl(t),
                options.DropExtra ? [new(PhaseDropTables, $"DROP TABLE {Q(t.Name)};")] : []));
    }

    /// <summary>El DDL de la tabla sin índices ni triggers (se comparan aparte).</summary>
    private static string TableDdl(TableDefinition t) => DdlWriter.Table(t with { Indices = [], Triggers = [] });

    private static IEnumerable<SyncStatement> NewTable(TableDefinition t)
    {
        var plain = t with
        {
            Columns = t.Columns.Where(c => c.Computed is null).Select(c => c with { Description = null }).ToList(),
            Constraints = t.Constraints.Where(c => c.Kind is ConstraintKind.PrimaryKey or ConstraintKind.Unique).ToList(),
            Indices = [],
            Triggers = [],
            Description = null,
        };
        yield return new(PhaseNewTables, DdlWriter.Table(plain).Trim());
        var computed = t.Columns.Select((c, i) => (Column: c, Position: i + 1)).Where(x => x.Column.Computed is not null).ToList();
        foreach (var (column, _) in computed) yield return new(PhaseComputed, $"ALTER TABLE {Q(t.Name)} ADD {DdlWriter.Column(column with { Description = null })};");
        foreach (var (column, position) in computed.Where(x => x.Position < t.Columns.Count))
            yield return new(PhaseComputed, $"ALTER TABLE {Q(t.Name)} ALTER {Q(column.Name)} POSITION {position};");
        foreach (var c in t.Constraints.Where(c => c.Kind is ConstraintKind.ForeignKey or ConstraintKind.Check))
            yield return new(PhaseConstraints, DdlWriter.Constraint(t.Name, c));
        foreach (var c in Comment("TABLE", t.Name, t.Description)) yield return c;
        foreach (var column in t.Columns)
            if (DdlWriter.Comment("COLUMN", $"{Q(t.Name)}.{Q(column.Name)}", column.Description) is { } comment)
                yield return new(PhaseComments, comment);
    }

    private static (List<string> Details, List<SyncStatement> Statements) AlterTable(TableDefinition s, TableDefinition t, SchemaCompareOptions options)
    {
        var details = new List<string>();
        var statements = new List<SyncStatement>();
        var table = Q(s.Name);
        var targetColumns = t.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

        foreach (var a in s.Columns)
        {
            var col = Q(a.Name);
            if (!targetColumns.Remove(a.Name, out var b))
            {
                details.Add(string.Format(CoreStrings.CompareColumnAdded, a.Name, ColumnType(a)));
                statements.Add(new(a.Computed is null ? PhaseColumns : PhaseComputed, $"ALTER TABLE {table} ADD {DdlWriter.Column(a with { Description = null })};"));
                if (DdlWriter.Comment("COLUMN", $"{table}.{col}", a.Description) is { } comment) statements.Add(new(PhaseComments, comment));
                continue;
            }
            if (a.Computed is not null || b.Computed is not null)
            {
                if (!Same(a.Computed, b.Computed))
                {
                    details.Add(string.Format(CoreStrings.CompareColumnComputed, a.Name));
                    statements.Add(a.Computed is not null && b.Computed is not null
                        ? new(PhaseComputed, $"ALTER TABLE {table} ALTER {col} COMPUTED BY {Parenthesized(a.Computed)};")
                        : new(PhaseComputed, "-- " + Sql.SqlIdentifier.OneLine(string.Format(CoreStrings.CompareComputedManual, s.Name, a.Name))));
                }
            }
            else
            {
                if (ColumnType(a) != ColumnType(b))
                {
                    details.Add(string.Format(CoreStrings.CompareColumnType, a.Name, ColumnType(b), ColumnType(a)));
                    statements.Add(new(PhaseColumns, $"ALTER TABLE {table} ALTER {col} TYPE {a.Type}{(a.CharacterSet is { } cs ? " CHARACTER SET " + Q(cs) : "")}{(a.Collation is { } co ? " COLLATE " + co : "")};"));
                }
                if (a.Default != b.Default)
                {
                    details.Add(string.Format(CoreStrings.CompareColumnDefault, a.Name, b.Default ?? "—", a.Default ?? "—"));
                    statements.Add(new(PhaseColumns, a.Default is { } d ? $"ALTER TABLE {table} ALTER {col} SET DEFAULT {DefaultValue(d)};" : $"ALTER TABLE {table} ALTER {col} DROP DEFAULT;"));
                }
                if (a.NotNull != b.NotNull)
                {
                    details.Add(string.Format(a.NotNull ? CoreStrings.CompareColumnNowNotNull : CoreStrings.CompareColumnNowNullable, a.Name));
                    statements.Add(new(PhaseColumns, $"ALTER TABLE {table} ALTER {col} {(a.NotNull ? "SET" : "DROP")} NOT NULL;"));
                }
                if (a.Identity != b.Identity)
                {
                    details.Add(string.Format(CoreStrings.CompareColumnIdentity, a.Name));
                    statements.Add(new(PhaseColumns, "-- " + Sql.SqlIdentifier.OneLine(string.Format(CoreStrings.CompareIdentityManual, s.Name, a.Name))));
                }
            }
            if (!Same(a.Description, b.Description))
            {
                details.Add(string.Format(CoreStrings.CompareColumnComment, a.Name));
                statements.Add(new(PhaseComments, $"COMMENT ON COLUMN {table}.{col} IS {(a.Description is { } text ? SqlIdentifier.Literal(text) : "NULL")};"));
            }
        }
        foreach (var b in targetColumns.Values)
        {
            details.Add(string.Format(CoreStrings.CompareColumnRemoved, b.Name));
            if (options.DropExtra) statements.Add(new(PhaseDropColumns, $"ALTER TABLE {table} DROP {Q(b.Name)};"));
        }

        // Mismo orden de columnas que en el origen (las que sobran, si no se borran, quedan al final).
        if (options.ColumnPositions)
        {
            var current = t.Columns.Select(c => c.Name).Where(n => s.Columns.Any(c => c.Name == n) || !options.DropExtra).ToList();
            current.AddRange(s.Columns.Select(c => c.Name).Where(n => !current.Contains(n)));
            var moved = false;
            for (var i = 0; i < s.Columns.Count; i++)
            {
                var name = s.Columns[i].Name;
                if (current.IndexOf(name) == i) continue;
                current.Remove(name);
                current.Insert(i, name);
                statements.Add(new(PhaseComputed, $"ALTER TABLE {table} ALTER {Q(name)} POSITION {i + 1};"));
                moved = true;
            }
            if (moved) details.Add(CoreStrings.CompareColumnOrder);
        }

        // Restricciones: por su definición (los nombres INTEG_n los pone el servidor y cambian de una base a otra).
        var sourceConstraints = s.Constraints.GroupBy(c => DdlWriter.Constraint(s.Name, c)).ToDictionary(g => g.Key, g => g.First());
        var targetConstraints = t.Constraints.GroupBy(c => DdlWriter.Constraint(s.Name, c)).ToDictionary(g => g.Key, g => g.First());
        foreach (var (text, c) in targetConstraints.Where(x => !sourceConstraints.ContainsKey(x.Key)))
        {
            details.Add(string.Format(CoreStrings.CompareConstraintRemoved, ConstraintLabel(c)));
            // Una restricción distinta se reemplaza siempre; una que solo sobra, solo si se pide borrar.
            var replaced = s.Constraints.Any(x => x.Name == c.Name || (x.Kind == ConstraintKind.PrimaryKey && c.Kind == ConstraintKind.PrimaryKey));
            if (replaced || options.DropExtra) statements.Add(new(PhaseDropConstraints, $"ALTER TABLE {table} DROP CONSTRAINT {Q(c.Name)};"));
        }
        foreach (var (text, c) in sourceConstraints.Where(x => !targetConstraints.ContainsKey(x.Key)))
        {
            details.Add(string.Format(CoreStrings.CompareConstraintAdded, ConstraintLabel(c)));
            statements.Add(new(PhaseConstraints, text));
        }

        if (!Same(s.Description, t.Description))
        {
            details.Add(CoreStrings.CompareDescription);
            statements.Add(new(PhaseComments, $"COMMENT ON TABLE {table} IS {(s.Description is { } text ? SqlIdentifier.Literal(text) : "NULL")};"));
        }
        return (details, statements);
    }

    private static string ColumnType(FieldDefinition f) =>
        f.Computed is { } e ? "COMPUTED BY " + Parenthesized(e)
        : f.Type + (f.CharacterSet is { } cs ? " CHARACTER SET " + Q(cs) : "") + (f.Collation is { } co ? " COLLATE " + co : "");

    private static string ConstraintLabel(ConstraintDefinition c) => c.Name.StartsWith("INTEG_", StringComparison.Ordinal)
        ? c.Kind switch { ConstraintKind.PrimaryKey => "PRIMARY KEY", ConstraintKind.Unique => "UNIQUE", ConstraintKind.ForeignKey => "FOREIGN KEY", _ => "CHECK" }
          + (c.Columns.Count > 0 ? $" ({string.Join(", ", c.Columns)})" : "")
        : c.Name;

    // ---------- índices (los de las restricciones van con la tabla) ----------

    private static void Indices(List<SchemaDifference> list, DatabaseSchema source, DatabaseSchema target, bool drop)
    {
        var targets = target.Tables.SelectMany(t => t.Indices).ToDictionary(i => i.Name, StringComparer.Ordinal);
        foreach (var s in source.Tables.SelectMany(t => t.Indices).OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            if (!targets.Remove(s.Name, out var t))
            {
                list.Add(new SchemaDifference(SchemaItemKind.Index, s.Name, SchemaChange.Added, [string.Format(CoreStrings.CompareIndexOn, s.Table)], DdlWriter.Index(s), null,
                    [new(PhaseIndices, DdlWriter.Index(s)), .. Comment("INDEX", s.Name, s.Description)]));
                continue;
            }
            var statements = new List<SyncStatement>();
            var details = new List<string>();
            if (!Same(DdlWriter.Index(s with { Inactive = false }), DdlWriter.Index(t with { Inactive = false })))
            {
                details.Add(CoreStrings.CompareDefinition);
                statements.Add(new(PhaseDropIndices, $"DROP INDEX {Q(s.Name)};"));
                statements.Add(new(PhaseIndices, DdlWriter.Index(s)));
            }
            else if (s.Inactive != t.Inactive)
            {
                details.Add(s.Inactive ? CoreStrings.CompareIndexNowInactive : CoreStrings.CompareIndexNowActive);
                statements.Add(new(PhaseIndices, $"ALTER INDEX {Q(s.Name)} {(s.Inactive ? "INACTIVE" : "ACTIVE")};"));
            }
            if (!Same(s.Description, t.Description))
            {
                details.Add(CoreStrings.CompareDescription);
                statements.AddRange(CommentIfChanged("INDEX", s.Name, s.Description, t.Description));
            }
            if (details.Count > 0) list.Add(new SchemaDifference(SchemaItemKind.Index, s.Name, SchemaChange.Changed, details, DdlWriter.Index(s), DdlWriter.Index(t), statements));
        }
        foreach (var t in targets.Values.OrderBy(i => i.Name, StringComparer.Ordinal))
            list.Add(new SchemaDifference(SchemaItemKind.Index, t.Name, SchemaChange.Removed, [CoreStrings.CompareRemoved], null, DdlWriter.Index(t),
                drop ? [new(PhaseDropIndices, $"DROP INDEX {Q(t.Name)};")] : []));
    }

    // ---------- vistas: en orden de dependencias ----------

    private static void Views(List<SchemaDifference> list, DatabaseSchema source, DatabaseSchema target, bool drop)
    {
        var order = SchemaScriptWriter.OrderViews(source).Select((v, i) => (v.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);
        var targetOrder = SchemaScriptWriter.OrderViews(target).Select((v, i) => (v.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);
        Simple(list, SchemaItemKind.View, source.Views, target.Views, v => v.Name, v => DdlWriter.View(v) + ";" + CommentLines("VIEW", v.Name, v.Description, "COLUMN", v.Columns),
            v => [new(PhaseViews, DdlWriter.View(v) + ";", Order: order.GetValueOrDefault(v.Name)), .. Comment("VIEW", v.Name, v.Description),
                  .. ParameterComments("COLUMN", v.Name, v.Columns, [])],
            (s, t) => [.. Same(DdlWriter.View(s), DdlWriter.View(t)) ? Array.Empty<SyncStatement>() : [new SyncStatement(PhaseViews, DdlWriter.View(s) + ";", Order: order.GetValueOrDefault(s.Name))],
                       .. CommentIfChanged("VIEW", s.Name, s.Description, t.Description), .. ParameterComments("COLUMN", s.Name, s.Columns, t.Columns)],
            // Las que usan a otra se borran antes que esa.
            v => drop ? [new(PhaseDropViews, $"DROP VIEW {Q(v.Name)};", Order: -targetOrder.GetValueOrDefault(v.Name))] : []);
    }

    // ---------- permisos ----------

    private static void Privileges(List<SchemaDifference> list, DatabaseSchema source, DatabaseSchema target, bool drop)
    {
        var sourceGrants = SchemaScriptWriter.Grants(source.Privileges).Where(g => g.StartsWith("GRANT ", StringComparison.Ordinal)).ToHashSet();
        var targetGrants = SchemaScriptWriter.Grants(target.Privileges).Where(g => g.StartsWith("GRANT ", StringComparison.Ordinal)).ToHashSet();
        foreach (var grant in sourceGrants.Except(targetGrants).Order(StringComparer.Ordinal))
            list.Add(new SchemaDifference(SchemaItemKind.Privilege, grant.TrimEnd(';'), SchemaChange.Added, [CoreStrings.CompareNew], grant, null, [new(PhasePrivileges, grant)]));
        foreach (var grant in targetGrants.Except(sourceGrants).Order(StringComparer.Ordinal))
            list.Add(new SchemaDifference(SchemaItemKind.Privilege, grant.TrimEnd(';'), SchemaChange.Removed, [CoreStrings.CompareRemoved], null, grant,
                drop ? [new(PhasePrivileges, Revoke(grant))] : []));
    }

    /// <summary>«GRANT SELECT ON T TO USER X WITH GRANT OPTION;» → «REVOKE SELECT ON T FROM USER X;».</summary>
    public static string Revoke(string grant)
    {
        var g = grant.TrimEnd(';');
        foreach (var suffix in new[] { " WITH GRANT OPTION", " WITH ADMIN OPTION" })
            if (g.EndsWith(suffix, StringComparison.Ordinal)) g = g[..^suffix.Length];
        var to = g.LastIndexOf(" TO ", StringComparison.Ordinal);
        return "REVOKE " + g["GRANT ".Length..to] + " FROM " + g[(to + 4)..] + ";";
    }

    // ---------- comentarios ----------

    /// <summary>Los COMMENT ON de un objeto y de sus parámetros o columnas, para agregarlos a su DDL al comparar.</summary>
    private static string CommentLines(string type, string name, string? description, string? childType, IEnumerable<FieldDefinition> children)
    {
        var lines = new List<string?> { DdlWriter.Comment(type, Q(name), description) };
        if (childType is not null) lines.AddRange(children.Select(c => DdlWriter.Comment(childType, $"{Q(name)}.{Q(c.Name)}", c.Description)));
        var present = lines.OfType<string>().ToList();
        return present.Count == 0 ? "" : "\n\n" + string.Join("\n", present);
    }

    private static IEnumerable<SyncStatement> Comment(string type, string name, string? description) =>
        DdlWriter.Comment(type, Q(name), description) is { } c ? [new(PhaseComments, c)] : [];

    private static IEnumerable<SyncStatement> CommentIfChanged(string type, string name, string? source, string? target) =>
        Same(source, target) ? [] : [new(PhaseComments, $"COMMENT ON {type} {Q(name)} IS {(source is { } text ? SqlIdentifier.Literal(text) : "NULL")};")];

    /// <summary>Comentarios de parámetros o columnas («COMMENT ON PROCEDURE PARAMETER P.X»), los que cambiaron o son nuevos.</summary>
    private static IEnumerable<SyncStatement> ParameterComments(string type, string owner, IEnumerable<FieldDefinition> source, IEnumerable<FieldDefinition> target)
    {
        var old = target.ToDictionary(f => f.Name, f => f.Description, StringComparer.Ordinal);
        foreach (var f in source)
        {
            var before = old.GetValueOrDefault(f.Name);
            if (Same(f.Description, before) || (f.Description is null && !old.ContainsKey(f.Name))) continue;
            yield return new(PhaseComments, $"COMMENT ON {type} {Q(owner)}.{Q(f.Name)} IS {(f.Description is { } text ? SqlIdentifier.Literal(text) : "NULL")};");
        }
    }

    // ---------- el script ----------

    /// <summary>Encabezado del script para igualar: de dónde a dónde y cuándo.</summary>
    public static string Header(string source, string target, DateTime when) =>
        "/* " + string.Format(CoreStrings.CompareHeader, source, target, when).Replace("*/", "* /") + " */";

    /// <summary>El script para igualar el destino con las diferencias elegidas, por etapas y con COMMIT entre ellas.</summary>
    public static string Script(IEnumerable<SchemaDifference> differences, string? header = null)
    {
        var sb = new StringBuilder();
        if (header is not null) sb.Append(header.TrimEnd()).Append("\n\n");
        var statements = differences.SelectMany(d => d.Statements).Select((s, i) => (Statement: s, Index: i))
            .OrderBy(x => x.Statement.Phase).ThenBy(x => x.Statement.Order).ThenBy(x => x.Index).Select(x => x.Statement).ToList();
        foreach (var phase in statements.GroupBy(s => s.Phase))
        {
            sb.Append("/* ").Append(PhaseTitle(phase.Key)).Append(" */\n\n");
            var inPsql = false;
            foreach (var s in phase)
            {
                if (s.IsPsql && !inPsql) sb.Append("SET TERM ^ ;\n\n");
                if (!s.IsPsql && inPsql) sb.Append("SET TERM ; ^\n\n");
                inPsql = s.IsPsql;
                sb.Append(s.Sql.TrimEnd()).Append(s.IsPsql ? "^" : "").Append("\n\n");
            }
            if (inPsql) sb.Append("SET TERM ; ^\n\n");
            sb.Append("COMMIT;\n\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static string PhaseTitle(int phase) => phase switch
    {
        PhaseDropTriggers => CoreStrings.ComparePhaseDropTriggers,
        PhaseDropViews => CoreStrings.ComparePhaseDropViews,
        PhaseDropPsql => CoreStrings.ComparePhaseDropPsql,
        PhaseDropConstraints => CoreStrings.ComparePhaseDropConstraints,
        PhaseDropIndices => CoreStrings.ComparePhaseDropIndices,
        PhaseDropColumns => CoreStrings.ComparePhaseDropColumns,
        PhaseDropTables => CoreStrings.ComparePhaseDropTables,
        PhaseDropOthers => CoreStrings.ComparePhaseDropOthers,
        PhaseRoles => CoreStrings.ScriptRoles,
        PhaseDomains => CoreStrings.ScriptDomains,
        PhaseGenerators => CoreStrings.ScriptGenerators,
        PhaseExceptions => CoreStrings.ScriptExceptions,
        PhaseExternalFunctions => CoreStrings.ScriptExternalFunctions,
        PhaseNewTables => CoreStrings.ComparePhaseNewTables,
        PhaseColumns => CoreStrings.ComparePhaseColumns,
        PhaseSignatures => CoreStrings.ScriptSignatures,
        PhaseViews => CoreStrings.ScriptViews,
        PhaseComputed => CoreStrings.ComparePhaseComputed,
        PhaseConstraints => CoreStrings.ComparePhaseConstraints,
        PhaseIndices => CoreStrings.ScriptIndices,
        PhaseBodies => CoreStrings.ScriptBodies,
        PhaseTriggers => CoreStrings.ScriptTriggers,
        PhaseComments => CoreStrings.ScriptComments,
        _ => CoreStrings.ScriptPrivileges,
    };

    // ---------- utilidades ----------

    /// <summary>Iguales sin contar saltos de línea (CRLF/LF) ni espacios al final de cada renglón.</summary>
    public static bool Same(string? a, string? b) =>
        a is null || b is null ? a is null && b is null : string.Join("\n", Lines(a)) == string.Join("\n", Lines(b));

    private static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n").TrimEnd().Split('\n').Select(l => l.TrimEnd());

    private static string DefaultValue(string stored)
    {
        var s = DdlWriter.InlineSafe(stored.Trim());
        return s.StartsWith("DEFAULT", StringComparison.OrdinalIgnoreCase) ? s[7..].Trim() : s.TrimStart('=').Trim();
    }

    private static string Parenthesized(string expression)
    {
        var e = DdlWriter.InlineSafe(expression.Trim());
        return e.StartsWith('(') && e.EndsWith(')') ? e : "(" + e + ")";
    }
}
