using FbStudio.Core.Metadata;
using FbStudio.Core.Resources;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>DDL de un objeto armado desde RDB$ (sin usar herramientas externas ni extraer con gbak/isql).</summary>
/// <param name="Description">Comentario del objeto (COMMENT ON), para mostrarlo aparte.</param>
public sealed record ObjectScript(DbObject Object, string Ddl, string? Description);

public sealed partial class MetadataExplorer
{
    private string? _databaseCharset;

    // Todas las consultas de "campos" (columnas, parámetros, argumentos, dominios) devuelven estas columnas en este orden:
    //  0 nombre · 1 dominio (RDB$FIELD_SOURCE) · 2 NOT NULL propio · 3 default propio · 4 collation propia
    //  5 mecanismo (1 = TYPE OF) · 6 tabla y 7 columna de TYPE OF COLUMN · 8 identidad · 9 descripción
    // 10..20: tipo del dominio (ver FieldColumns).
    private const string FieldColumns = """
        f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, f.RDB$CHARACTER_LENGTH, f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE,
        TRIM(cs.RDB$CHARACTER_SET_NAME), TRIM(co.RDB$COLLATION_NAME), f.RDB$COLLATION_ID, f.RDB$DEFAULT_SOURCE, f.RDB$COMPUTED_SOURCE
        """;

    private const string TableColumnsSql = $"""
        SELECT TRIM(rf.RDB$FIELD_NAME), TRIM(rf.RDB$FIELD_SOURCE), COALESCE(rf.RDB$NULL_FLAG, 0), rf.RDB$DEFAULT_SOURCE, rf.RDB$COLLATION_ID,
               CAST(0 AS SMALLINT), CAST(NULL AS VARCHAR(63)), CAST(NULL AS VARCHAR(63)), rf.RDB$IDENTITY_TYPE, rf.RDB$DESCRIPTION,
               {FieldColumns}
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
        LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID
        LEFT JOIN RDB$COLLATIONS co ON co.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID
                                   AND co.RDB$COLLATION_ID = COALESCE(rf.RDB$COLLATION_ID, f.RDB$COLLATION_ID)
        WHERE rf.RDB$RELATION_NAME = @name
        ORDER BY rf.RDB$FIELD_POSITION
        """;

    private const string ProcedureParametersSql = $"""
        SELECT TRIM(p.RDB$PARAMETER_NAME), TRIM(p.RDB$FIELD_SOURCE), COALESCE(p.RDB$NULL_FLAG, 0), p.RDB$DEFAULT_SOURCE, p.RDB$COLLATION_ID,
               COALESCE(p.RDB$PARAMETER_MECHANISM, 0), TRIM(p.RDB$RELATION_NAME), TRIM(p.RDB$FIELD_NAME), CAST(NULL AS SMALLINT), p.RDB$DESCRIPTION,
               {FieldColumns},
               p.RDB$PARAMETER_TYPE
        FROM RDB$PROCEDURE_PARAMETERS p
        JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = p.RDB$FIELD_SOURCE
        LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID
        LEFT JOIN RDB$COLLATIONS co ON co.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID
                                   AND co.RDB$COLLATION_ID = COALESCE(p.RDB$COLLATION_ID, f.RDB$COLLATION_ID)
        WHERE p.RDB$PROCEDURE_NAME = @name AND p.RDB$PACKAGE_NAME IS NULL
        ORDER BY p.RDB$PARAMETER_TYPE, p.RDB$PARAMETER_NUMBER
        """;

    // Las UDF no tienen dominio: el tipo está en el propio argumento.
    private const string FunctionArgumentsSql = """
        SELECT TRIM(a.RDB$ARGUMENT_NAME), TRIM(a.RDB$FIELD_SOURCE), COALESCE(a.RDB$NULL_FLAG, 0), a.RDB$DEFAULT_SOURCE, a.RDB$COLLATION_ID,
               COALESCE(a.RDB$ARGUMENT_MECHANISM, 0), TRIM(a.RDB$RELATION_NAME), TRIM(a.RDB$FIELD_NAME), CAST(NULL AS SMALLINT), a.RDB$DESCRIPTION,
               COALESCE(f.RDB$FIELD_TYPE, a.RDB$FIELD_TYPE), COALESCE(f.RDB$FIELD_SUB_TYPE, a.RDB$FIELD_SUB_TYPE),
               COALESCE(f.RDB$FIELD_LENGTH, a.RDB$FIELD_LENGTH), COALESCE(f.RDB$CHARACTER_LENGTH, a.RDB$CHARACTER_LENGTH),
               COALESCE(f.RDB$FIELD_PRECISION, a.RDB$FIELD_PRECISION), COALESCE(f.RDB$FIELD_SCALE, a.RDB$FIELD_SCALE),
               TRIM(cs.RDB$CHARACTER_SET_NAME), TRIM(co.RDB$COLLATION_NAME), f.RDB$COLLATION_ID, f.RDB$DEFAULT_SOURCE, f.RDB$COMPUTED_SOURCE,
               a.RDB$ARGUMENT_POSITION, a.RDB$MECHANISM
        FROM RDB$FUNCTION_ARGUMENTS a
        LEFT JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = a.RDB$FIELD_SOURCE
        LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = COALESCE(f.RDB$CHARACTER_SET_ID, a.RDB$CHARACTER_SET_ID)
        LEFT JOIN RDB$COLLATIONS co ON co.RDB$CHARACTER_SET_ID = COALESCE(f.RDB$CHARACTER_SET_ID, a.RDB$CHARACTER_SET_ID)
                                   AND co.RDB$COLLATION_ID = COALESCE(a.RDB$COLLATION_ID, f.RDB$COLLATION_ID)
        WHERE a.RDB$FUNCTION_NAME = @name AND a.RDB$PACKAGE_NAME IS NULL
        ORDER BY a.RDB$ARGUMENT_POSITION
        """;

    private const string DomainSql = $"""
        SELECT TRIM(f.RDB$FIELD_NAME), TRIM(f.RDB$FIELD_NAME), COALESCE(f.RDB$NULL_FLAG, 0), f.RDB$DEFAULT_SOURCE, f.RDB$COLLATION_ID,
               CAST(0 AS SMALLINT), CAST(NULL AS VARCHAR(63)), CAST(NULL AS VARCHAR(63)), CAST(NULL AS SMALLINT), f.RDB$DESCRIPTION,
               {FieldColumns},
               f.RDB$VALIDATION_SOURCE
        FROM RDB$FIELDS f
        LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID
        LEFT JOIN RDB$COLLATIONS co ON co.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID AND co.RDB$COLLATION_ID = f.RDB$COLLATION_ID
        WHERE f.RDB$FIELD_NAME = @name
        """;

    private const string TriggersSql = """
        SELECT TRIM(RDB$TRIGGER_NAME), TRIM(RDB$RELATION_NAME), RDB$TRIGGER_TYPE, COALESCE(RDB$TRIGGER_SEQUENCE, 0),
               COALESCE(RDB$TRIGGER_INACTIVE, 0), RDB$TRIGGER_SOURCE, RDB$DESCRIPTION
        FROM RDB$TRIGGERS
        WHERE {0} AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY RDB$TRIGGER_TYPE, RDB$TRIGGER_SEQUENCE, RDB$TRIGGER_NAME
        """;

    /// <summary>
    /// DDL de un objeto. Procedimientos, triggers, vistas y funciones salen como una sentencia CREATE OR ALTER
    /// (lo que se edita y compila); el resto como script.
    /// </summary>
    public Task<ObjectScript> ScriptAsync(DbObject obj, CancellationToken ct = default) =>
        WithTransactionAsync(async (connection, tx) =>
        {
            var q = new MetaQuery(connection, tx, ct);
            _databaseCharset ??= (await q.ListAsync("SELECT TRIM(RDB$CHARACTER_SET_NAME) FROM RDB$DATABASE", r => Str(r, 0))).FirstOrDefault() ?? "NONE";
            return obj.Kind switch
            {
                DbObjectKind.Table => Script(await ReadTableAsync(q, obj.Name), t => DdlWriter.Table(t), t => t.Description),
                DbObjectKind.View => Script(await ReadViewAsync(q, obj.Name), v => DdlWriter.View(v), v => v.Description),
                DbObjectKind.Procedure => Script(await ReadProcedureAsync(q, obj.Name), p => DdlWriter.Procedure(p), p => p.Description),
                DbObjectKind.Function => Script(await ReadFunctionAsync(q, obj.Name), f => DdlWriter.Function(f), f => f.Description),
                DbObjectKind.Package => Script(await ReadPackageAsync(q, obj.Name), p => DdlWriter.Package(p), p => p.Description),
                DbObjectKind.Trigger => Script(await ReadTriggerAsync(q, obj.Name), t => DdlWriter.Trigger(t), t => t.Description),
                DbObjectKind.Generator => Script(await ReadGeneratorAsync(q, obj.Name), g => DdlWriter.Generator(g), g => g.Description),
                DbObjectKind.Exception => Script(await ReadExceptionAsync(q, obj.Name), e => DdlWriter.Exception(e), e => e.Description),
                DbObjectKind.Domain => Script(await ReadDomainAsync(q, obj.Name), d => DdlWriter.Domain(d), d => d.Field.Description),
                DbObjectKind.Index => await ScriptIndexAsync(q, obj),
                DbObjectKind.Role => Script(await ReadRoleAsync(q, obj.Name), r => DdlWriter.Role(r), r => r.Description),
                _ => throw new NotSupportedException(obj.Kind.ToString()),
            };

            ObjectScript Script<T>(T? definition, Func<T, string> write, Func<T, string?> description) where T : class =>
                definition is null
                    ? throw new InvalidOperationException(string.Format(CoreStrings.ObjectNotFound, DbObjectKinds.Singular(obj.Kind), obj.Name))
                    : new ObjectScript(obj, write(definition), description(definition));
        }, ct);

    private async Task<TableDefinition?> ReadTableAsync(MetaQuery q, string name)
    {
        var info = (await q.ListAsync("SELECT RDB$RELATION_TYPE, RDB$EXTERNAL_FILE, RDB$DESCRIPTION FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @name",
            r => (Type: Int(r, 0) ?? 0, File: Str(r, 1), Description: Clean(Text(r, 2))), ("@name", name))).ToList();
        if (info.Count == 0) return null;
        var columns = await q.ListAsync(TableColumnsSql, r => FieldFromRow(r), ("@name", name));
        var constraints = await ReadConstraintsAsync(q, name);
        var indices = await ReadIndicesAsync(q, name);
        var triggers = await ReadTriggersAsync(q, "RDB$RELATION_NAME = @name", name);
        return new TableDefinition(name, columns, constraints, indices, triggers, info[0].Description, info[0].Type, info[0].File);
    }

    private async Task<ViewDefinition?> ReadViewAsync(MetaQuery q, string name)
    {
        var info = await q.ListAsync("SELECT RDB$VIEW_SOURCE, RDB$DESCRIPTION FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @name AND RDB$VIEW_BLR IS NOT NULL",
            r => (Source: Text(r, 0), Description: Clean(Text(r, 1))), ("@name", name));
        if (info.Count == 0) return null;
        var columns = await q.ListAsync(TableColumnsSql, r => FieldFromRow(r), ("@name", name));
        var triggers = await ReadTriggersAsync(q, "RDB$RELATION_NAME = @name", name);
        return new ViewDefinition(name, columns, info[0].Source, triggers, info[0].Description);
    }

    private async Task<ProcedureDefinition?> ReadProcedureAsync(MetaQuery q, string name)
    {
        var info = await q.ListAsync("SELECT RDB$PROCEDURE_SOURCE, RDB$DESCRIPTION FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @name AND RDB$PACKAGE_NAME IS NULL",
            r => (Source: Text(r, 0), Description: Clean(Text(r, 1))), ("@name", name));
        if (info.Count == 0) return null;
        var parameters = await q.ListAsync(ProcedureParametersSql, r => (Output: Int(r, 21) == 1, Field: FieldFromRow(r)), ("@name", name));
        return new ProcedureDefinition(name,
            parameters.Where(p => !p.Output).Select(p => p.Field).ToList(),
            parameters.Where(p => p.Output).Select(p => p.Field).ToList(),
            info[0].Source, info[0].Description);
    }

    private async Task<FunctionDefinition?> ReadFunctionAsync(MetaQuery q, string name)
    {
        var info = await q.ListAsync("""
            SELECT RDB$FUNCTION_SOURCE, COALESCE(RDB$RETURN_ARGUMENT, 0), COALESCE(RDB$DETERMINISTIC_FLAG, 0),
                   TRIM(RDB$MODULE_NAME), TRIM(RDB$ENTRYPOINT), RDB$DESCRIPTION
            FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @name AND RDB$PACKAGE_NAME IS NULL
            """, r => (Source: Text(r, 0), ReturnArgument: Int(r, 1) ?? 0, Deterministic: Int(r, 2) == 1, Module: Str(r, 3), Entry: Str(r, 4), Description: Clean(Text(r, 5))),
            ("@name", name));
        if (info.Count == 0) return null;
        var f = info[0];
        var args = await q.ListAsync(FunctionArgumentsSql, r => (Position: Int(r, 21) ?? 0, Mechanism: Int(r, 22), Field: FieldFromRow(r)), ("@name", name));
        var returns = args.FirstOrDefault(a => a.Position == f.ReturnArgument);
        var inputs = args.Where(a => f.ReturnArgument > 0 || a.Position != 0).Select(a => a.Field).ToList();
        var returnField = f.ReturnArgument > 0 ? new FieldDefinition("", $"PARAMETER {f.ReturnArgument}") : returns.Field ?? new FieldDefinition("", "?");
        return new FunctionDefinition(name, inputs, returnField, f.Source, f.Deterministic, f.Module, f.Entry,
            f.ReturnArgument > 0 ? null : returns.Mechanism, f.Description);
    }

    private static async Task<PackageDefinition?> ReadPackageAsync(MetaQuery q, string name) =>
        (await q.ListAsync("SELECT RDB$PACKAGE_HEADER_SOURCE, RDB$PACKAGE_BODY_SOURCE, RDB$DESCRIPTION FROM RDB$PACKAGES WHERE RDB$PACKAGE_NAME = @name",
            r => new PackageDefinition(name, Text(r, 0), Text(r, 1), Clean(Text(r, 2))), ("@name", name))).FirstOrDefault();

    private static async Task<TriggerDefinition?> ReadTriggerAsync(MetaQuery q, string name) =>
        (await ReadTriggersAsync(q, "RDB$TRIGGER_NAME = @name", name)).FirstOrDefault();

    private static async Task<GeneratorDefinition?> ReadGeneratorAsync(MetaQuery q, string name)
    {
        var info = await q.ListAsync("""
            SELECT COALESCE(RDB$INITIAL_VALUE, 0), COALESCE(RDB$GENERATOR_INCREMENT, 1), RDB$DESCRIPTION
            FROM RDB$GENERATORS WHERE RDB$GENERATOR_NAME = @name
            """, r => (Initial: Convert.ToInt64(r.GetValue(0)), Increment: Convert.ToInt32(r.GetValue(1)), Description: Clean(Text(r, 2))), ("@name", name));
        if (info.Count == 0) return null;
        long? current;
        try
        {
            // GEN_ID(g, 0) no avanza el generador, pero en Firebird 3 necesita permiso USAGE.
            current = (await q.ListAsync($"SELECT GEN_ID({SqlIdentifier.Quote(name)}, 0) FROM RDB$DATABASE", r => Convert.ToInt64(r.GetValue(0)))).FirstOrDefault();
        }
        catch (FbException)
        {
            current = null;
        }
        return new GeneratorDefinition(name, current, info[0].Initial, info[0].Increment, info[0].Description);
    }

    private static async Task<ExceptionDefinition?> ReadExceptionAsync(MetaQuery q, string name) =>
        (await q.ListAsync("SELECT RDB$MESSAGE, RDB$DESCRIPTION FROM RDB$EXCEPTIONS WHERE RDB$EXCEPTION_NAME = @name",
            r => new ExceptionDefinition(name, Text(r, 0) ?? "", Clean(Text(r, 1))), ("@name", name))).FirstOrDefault();

    private async Task<DomainDefinition?> ReadDomainAsync(MetaQuery q, string name) =>
        (await q.ListAsync(DomainSql, r => new DomainDefinition(FieldFromRow(r, asDomain: true), Clean(Text(r, 21))), ("@name", name))).FirstOrDefault();

    private static async Task<RoleDefinition?> ReadRoleAsync(MetaQuery q, string name) =>
        (await q.ListAsync("SELECT TRIM(RDB$OWNER_NAME), RDB$DESCRIPTION FROM RDB$ROLES WHERE RDB$ROLE_NAME = @name",
            r => new RoleDefinition(name, Str(r, 0), Clean(Text(r, 1))), ("@name", name))).FirstOrDefault();

    private async Task<ObjectScript> ScriptIndexAsync(MetaQuery q, DbObject obj)
    {
        var info = await q.ListAsync("""
            SELECT TRIM(i.RDB$RELATION_NAME), COALESCE(i.RDB$UNIQUE_FLAG, 0), COALESCE(i.RDB$INDEX_TYPE, 0), COALESCE(i.RDB$INDEX_INACTIVE, 0),
                   i.RDB$EXPRESSION_SOURCE, i.RDB$DESCRIPTION, TRIM(rc.RDB$CONSTRAINT_NAME)
            FROM RDB$INDICES i
            LEFT JOIN RDB$RELATION_CONSTRAINTS rc ON rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME
            WHERE i.RDB$INDEX_NAME = @name
            """, r => (Table: Str(r, 0) ?? "", Unique: Int(r, 1) == 1, Descending: Int(r, 2) == 1, Inactive: Int(r, 3) == 1,
                       Expression: Clean(Text(r, 4)), Description: Clean(Text(r, 5)), Constraint: Str(r, 6)), ("@name", obj.Name));
        if (info.Count == 0) throw new InvalidOperationException(string.Format(CoreStrings.ObjectNotFound, DbObjectKinds.Singular(DbObjectKind.Index), obj.Name));
        var i = info[0];
        if (i.Constraint is { } constraintName)
        {
            var constraint = (await ReadConstraintsAsync(q, i.Table)).First(c => c.Name == constraintName);
            return new ObjectScript(obj,
                "-- " + Core.Sql.SqlIdentifier.OneLine(string.Format(CoreStrings.DdlIndexOfConstraint, obj.Name, constraintName, i.Table)) + "\n"
                + DdlWriter.Constraint(i.Table, constraint) + "\n", i.Description);
        }
        var segments = await IndexSegmentsAsync(q, "s.RDB$INDEX_NAME = @name", obj.Name);
        var definition = new IndexDefinition(obj.Name, i.Table, segments.GetValueOrDefault(obj.Name) ?? [], i.Unique, i.Descending, i.Inactive, i.Expression, i.Description);
        var ddl = DdlWriter.Index(definition) + "\n";
        if (DdlWriter.Comment("INDEX", SqlIdentifier.Quote(obj.Name), i.Description) is { } comment) ddl += "\n" + comment + "\n";
        return new ObjectScript(obj, ddl, i.Description);
    }

    private static async Task<List<ConstraintDefinition>> ReadConstraintsAsync(MetaQuery q, string table)
    {
        var keys = await q.ListAsync("""
            SELECT TRIM(rc.RDB$CONSTRAINT_NAME), TRIM(rc.RDB$CONSTRAINT_TYPE), TRIM(rc.RDB$INDEX_NAME), COALESCE(i.RDB$INDEX_TYPE, 0),
                   TRIM(uq.RDB$RELATION_NAME), TRIM(uq.RDB$INDEX_NAME), TRIM(refc.RDB$UPDATE_RULE), TRIM(refc.RDB$DELETE_RULE)
            FROM RDB$RELATION_CONSTRAINTS rc
            LEFT JOIN RDB$INDICES i ON i.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
            LEFT JOIN RDB$REF_CONSTRAINTS refc ON refc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
            LEFT JOIN RDB$RELATION_CONSTRAINTS uq ON uq.RDB$CONSTRAINT_NAME = refc.RDB$CONST_NAME_UQ
            WHERE rc.RDB$RELATION_NAME = @name AND rc.RDB$CONSTRAINT_TYPE IN ('PRIMARY KEY', 'UNIQUE', 'FOREIGN KEY')
            ORDER BY rc.RDB$CONSTRAINT_NAME
            """, r => (Name: Str(r, 0) ?? "", Type: Str(r, 1) ?? "", Index: Str(r, 2), Descending: Int(r, 3) == 1,
                       RefTable: Str(r, 4), RefIndex: Str(r, 5), OnUpdate: Str(r, 6), OnDelete: Str(r, 7)), ("@name", table));

        var segments = await IndexSegmentsAsync(q, """
            s.RDB$INDEX_NAME IN (SELECT i.RDB$INDEX_NAME FROM RDB$INDICES i WHERE i.RDB$RELATION_NAME = @name)
            OR s.RDB$INDEX_NAME IN (SELECT uq.RDB$INDEX_NAME FROM RDB$RELATION_CONSTRAINTS rc
                                    JOIN RDB$REF_CONSTRAINTS refc ON refc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
                                    JOIN RDB$RELATION_CONSTRAINTS uq ON uq.RDB$CONSTRAINT_NAME = refc.RDB$CONST_NAME_UQ
                                    WHERE rc.RDB$RELATION_NAME = @name)
            """, table);
        IReadOnlyList<string> Columns(string? index) => index is not null && segments.TryGetValue(index, out var c) ? c : [];

        var result = keys.Select(k => new ConstraintDefinition(
            k.Name,
            k.Type switch { "PRIMARY KEY" => ConstraintKind.PrimaryKey, "UNIQUE" => ConstraintKind.Unique, _ => ConstraintKind.ForeignKey },
            Columns(k.Index), k.Index, k.Descending, k.RefTable, Columns(k.RefIndex), k.OnUpdate, k.OnDelete)).ToList();

        // Cada CHECK tiene dos triggers de sistema (antes de insertar y de modificar): alcanza con el primero.
        result.AddRange(await q.ListAsync("""
            SELECT TRIM(rc.RDB$CONSTRAINT_NAME), t.RDB$TRIGGER_SOURCE
            FROM RDB$RELATION_CONSTRAINTS rc
            JOIN RDB$CHECK_CONSTRAINTS cc ON cc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME
            JOIN RDB$TRIGGERS t ON t.RDB$TRIGGER_NAME = cc.RDB$TRIGGER_NAME
            WHERE rc.RDB$RELATION_NAME = @name AND rc.RDB$CONSTRAINT_TYPE = 'CHECK' AND t.RDB$TRIGGER_TYPE = 1
            ORDER BY 1
            """, r => new ConstraintDefinition(Str(r, 0) ?? "", ConstraintKind.Check, [], CheckSource: Clean(Text(r, 1))), ("@name", table)));
        return result;
    }

    private static async Task<List<IndexDefinition>> ReadIndicesAsync(MetaQuery q, string table)
    {
        var rows = await q.ListAsync("""
            SELECT TRIM(i.RDB$INDEX_NAME), COALESCE(i.RDB$UNIQUE_FLAG, 0), COALESCE(i.RDB$INDEX_TYPE, 0), COALESCE(i.RDB$INDEX_INACTIVE, 0),
                   i.RDB$EXPRESSION_SOURCE, i.RDB$DESCRIPTION
            FROM RDB$INDICES i
            WHERE i.RDB$RELATION_NAME = @name AND COALESCE(i.RDB$SYSTEM_FLAG, 0) = 0
              AND NOT EXISTS (SELECT 1 FROM RDB$RELATION_CONSTRAINTS rc WHERE rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME)
            ORDER BY i.RDB$INDEX_NAME
            """, r => (Name: Str(r, 0) ?? "", Unique: Int(r, 1) == 1, Descending: Int(r, 2) == 1, Inactive: Int(r, 3) == 1,
                       Expression: Clean(Text(r, 4)), Description: Clean(Text(r, 5))), ("@name", table));
        var segments = await IndexSegmentsAsync(q, "s.RDB$INDEX_NAME IN (SELECT i.RDB$INDEX_NAME FROM RDB$INDICES i WHERE i.RDB$RELATION_NAME = @name)", table);
        return rows.Select(r => new IndexDefinition(r.Name, table, segments.GetValueOrDefault(r.Name) ?? [], r.Unique, r.Descending, r.Inactive, r.Expression, r.Description)).ToList();
    }

    private static async Task<Dictionary<string, IReadOnlyList<string>>> IndexSegmentsAsync(MetaQuery q, string where, string name)
    {
        var rows = await q.ListAsync($"""
            SELECT TRIM(s.RDB$INDEX_NAME), TRIM(s.RDB$FIELD_NAME) FROM RDB$INDEX_SEGMENTS s
            WHERE {where}
            ORDER BY s.RDB$INDEX_NAME, s.RDB$FIELD_POSITION
            """, r => (Index: Str(r, 0) ?? "", Field: Str(r, 1) ?? ""), ("@name", name));
        return rows.GroupBy(r => r.Index).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Field).ToList());
    }

    private static Task<List<TriggerDefinition>> ReadTriggersAsync(MetaQuery q, string where, string name) =>
        q.ListAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, TriggersSql, where),
            r => new TriggerDefinition(Str(r, 0) ?? "", Str(r, 1), Convert.ToInt64(r.GetValue(2)), Int(r, 3) ?? 0, Int(r, 4) == 1, Text(r, 5), Clean(Text(r, 6))),
            ("@name", name));

    /// <summary>Arma el tipo de una columna/parámetro según el layout de <see cref="FieldColumns"/>.</summary>
    private FieldDefinition FieldFromRow(FbDataReader r, bool asDomain = false)
    {
        var source = Str(r, 1) ?? "";
        var userDomain = !asDomain && source.Length > 0 && !source.StartsWith("RDB$", StringComparison.Ordinal);
        var typeOf = Int(r, 5) == 1;
        var typeOfTable = Str(r, 6);
        var typeOfColumn = Str(r, 7);
        var type = Int(r, 10) ?? 0;
        var subType = Int(r, 11);
        var charset = Str(r, 16);
        var collationName = Str(r, 17);
        var ownCollation = Int(r, 4);
        var domainCollation = Int(r, 18);
        var isText = type is 14 or 37 or 40 || (type == 261 && subType == 1);

        string typeText;
        string? characterSet = null;
        if (typeOf && typeOfTable is not null && typeOfColumn is not null) typeText = $"TYPE OF COLUMN {SqlIdentifier.Quote(typeOfTable)}.{SqlIdentifier.Quote(typeOfColumn)}";
        else if (typeOf && userDomain) typeText = "TYPE OF " + SqlIdentifier.Quote(source);
        else if (userDomain) typeText = SqlIdentifier.Quote(source);
        else
        {
            typeText = FieldTypeFormatter.Format(type, subType, Int(r, 12), Int(r, 13), Int(r, 14), Int(r, 15));
            if (isText && charset is not null && !string.Equals(charset, _databaseCharset, StringComparison.OrdinalIgnoreCase)) characterSet = charset;
        }

        string? collation = null;
        if (isText && collationName is not null)
        {
            // Sobre un dominio solo se escribe si la columna la cambia; con tipo propio, si no es la de por defecto (id 0).
            if (userDomain || typeOf) { if (ownCollation is { } own && own != domainCollation) collation = collationName; }
            else if ((ownCollation ?? domainCollation ?? 0) > 0) collation = collationName;
        }

        var defaultSource = Text(r, 3) ?? (!userDomain && !asDomain ? Text(r, 19) : null);
        return new FieldDefinition(
            Str(r, 0) ?? "",
            typeText,
            NotNull: Int(r, 2) == 1,
            Default: Clean(defaultSource),
            Computed: asDomain ? null : Clean(Text(r, 20)),
            CharacterSet: characterSet,
            Collation: collation,
            Identity: !asDomain && Int(r, 8) is not null,
            Description: Clean(Text(r, 9)));
    }

    private static string? Str(FbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();
    private static string? Text(FbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Consultas parametrizadas dentro de la transacción del explorador.</summary>
    private sealed class MetaQuery(FbConnection connection, FbTransaction tx, CancellationToken ct)
    {
        public async Task<List<T>> ListAsync<T>(string sql, Func<FbDataReader, T> map, params (string Name, object? Value)[] parameters)
        {
            await using var cmd = new FbCommand(sql, connection, tx);
            foreach (var (name, value) in parameters) cmd.Parameters.Add(name, value ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<T>();
            while (await reader.ReadAsync(ct)) list.Add(map(reader));
            return list;
        }
    }
}
