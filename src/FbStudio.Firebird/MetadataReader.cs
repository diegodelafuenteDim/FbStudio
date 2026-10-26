using FbStudio.Core.Diagrams;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

public static class MetadataReader
{
    private const string ColumnsSql = """
        SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME)
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
        WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY rf.RDB$RELATION_NAME, rf.RDB$FIELD_POSITION
        """;

    // Procedimientos con sus parámetros de salida (las "columnas" de un procedimiento seleccionable).
    private const string ProceduresSql = """
        SELECT TRIM(p.RDB$PROCEDURE_NAME), TRIM(pp.RDB$PARAMETER_NAME)
        FROM RDB$PROCEDURES p
        LEFT JOIN RDB$PROCEDURE_PARAMETERS pp ON pp.RDB$PROCEDURE_NAME = p.RDB$PROCEDURE_NAME
             AND pp.RDB$PARAMETER_TYPE = 1 AND pp.RDB$PACKAGE_NAME IS NULL
        WHERE p.RDB$PACKAGE_NAME IS NULL AND COALESCE(p.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY p.RDB$PROCEDURE_NAME, pp.RDB$PARAMETER_NUMBER
        """;

    // Los parámetros de salida de los procedimientos de paquetes, como «PKG.PROC» (sus columnas en el FROM).
    private const string PackageProceduresSql = """
        SELECT TRIM(p.RDB$PACKAGE_NAME) || '.' || TRIM(p.RDB$PROCEDURE_NAME), TRIM(pp.RDB$PARAMETER_NAME)
        FROM RDB$PROCEDURES p
        JOIN RDB$PROCEDURE_PARAMETERS pp ON pp.RDB$PROCEDURE_NAME = p.RDB$PROCEDURE_NAME
             AND pp.RDB$PACKAGE_NAME = p.RDB$PACKAGE_NAME AND pp.RDB$PARAMETER_TYPE = 1
        WHERE p.RDB$PACKAGE_NAME IS NOT NULL AND COALESCE(p.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY p.RDB$PACKAGE_NAME, p.RDB$PROCEDURE_NAME, pp.RDB$PARAMETER_NUMBER
        """;

    // Firmas (F-33): los parámetros de entrada de cada procedimiento (también los de paquetes), con su tipo o dominio.
    private const string ProcedureInputsSql = """
        SELECT TRIM(p.RDB$PACKAGE_NAME), TRIM(p.RDB$PROCEDURE_NAME), TRIM(pp.RDB$PARAMETER_NAME),
               f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, f.RDB$CHARACTER_LENGTH, f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE,
               TRIM(pp.RDB$FIELD_SOURCE), IIF(pp.RDB$DEFAULT_SOURCE IS NULL, 0, 1)
        FROM RDB$PROCEDURES p
        LEFT JOIN RDB$PROCEDURE_PARAMETERS pp ON pp.RDB$PROCEDURE_NAME = p.RDB$PROCEDURE_NAME
             AND pp.RDB$PACKAGE_NAME IS NOT DISTINCT FROM p.RDB$PACKAGE_NAME AND pp.RDB$PARAMETER_TYPE = 0
        LEFT JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE
        WHERE COALESCE(p.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY 1, 2, pp.RDB$PARAMETER_NUMBER
        """;

    // Funciones PSQL y externas (UDF), con sus argumentos; el de la posición RDB$RETURN_ARGUMENT es lo que devuelve.
    private const string FunctionsSql = """
        SELECT TRIM(fn.RDB$PACKAGE_NAME), TRIM(fn.RDB$FUNCTION_NAME), a.RDB$ARGUMENT_POSITION, TRIM(a.RDB$ARGUMENT_NAME),
               COALESCE(f.RDB$FIELD_TYPE, a.RDB$FIELD_TYPE), COALESCE(f.RDB$FIELD_SUB_TYPE, a.RDB$FIELD_SUB_TYPE),
               COALESCE(f.RDB$FIELD_LENGTH, a.RDB$FIELD_LENGTH), COALESCE(f.RDB$CHARACTER_LENGTH, a.RDB$CHARACTER_LENGTH),
               COALESCE(f.RDB$FIELD_PRECISION, a.RDB$FIELD_PRECISION), COALESCE(f.RDB$FIELD_SCALE, a.RDB$FIELD_SCALE),
               TRIM(a.RDB$FIELD_SOURCE), IIF(a.RDB$DEFAULT_SOURCE IS NULL, 0, 1), fn.RDB$RETURN_ARGUMENT,
               IIF(fn.RDB$LEGACY_FLAG = 1, 1, 0)
        FROM RDB$FUNCTIONS fn
        LEFT JOIN RDB$FUNCTION_ARGUMENTS a ON a.RDB$FUNCTION_NAME = fn.RDB$FUNCTION_NAME
             AND a.RDB$PACKAGE_NAME IS NOT DISTINCT FROM fn.RDB$PACKAGE_NAME
        LEFT JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = a.RDB$FIELD_SOURCE
        WHERE COALESCE(fn.RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY 1, 2, 3
        """;

    private const string PackagesSql = "SELECT TRIM(RDB$PACKAGE_NAME) FROM RDB$PACKAGES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY 1";

    private const string GeneratorsSql = "SELECT TRIM(RDB$GENERATOR_NAME) FROM RDB$GENERATORS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY 1";

    private const string ExceptionsSql = "SELECT TRIM(RDB$EXCEPTION_NAME) FROM RDB$EXCEPTIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY 1";

    // Las columnas con una FK lógica en el comentario («#FK_%TABLA,%CLAVE…»), como en los diagramas.
    private const string LogicalForeignKeysSql = """
        SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), CAST(SUBSTRING(rf.RDB$DESCRIPTION FROM 1 FOR 1000) AS VARCHAR(1000))
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME
        WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 AND rf.RDB$DESCRIPTION CONTAINING '#FK_'
        """;

    /// <summary>
    /// Carga tablas/vistas, procedimientos, funciones, paquetes, generadores, excepciones y las firmas con una conexión
    /// propia (no toca la transacción del editor). Lo que una versión vieja de Firebird no tiene (paquetes) queda vacío.
    /// </summary>
    public static async Task<MetadataCatalog> LoadCatalogAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "metadatos");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);

        async Task<List<object?[]>> Rows(string sql)
        {
            var rows = new List<object?[]>();
            try
            {
                await using var cmd = new FbCommand(sql, connection, tx);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var values = new object?[reader.FieldCount];
                    for (var i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(values);
                }
            }
            catch (FbException) when (sql != ColumnsSql && sql != ProceduresSql)
            {
                // Firebird 2.5 no tiene paquetes ni algunas columnas: sin eso, el resto del autocompletado igual anda.
            }
            return rows;
        }
        static string? S(object? v) => v is null ? null : Convert.ToString(v)!.Trim() is { Length: > 0 } s ? s : null;
        static int? I(object? v) => v is null ? null : Convert.ToInt32(v);

        var columns = (await Rows(ColumnsSql)).Select(r => (S(r[0])!, S(r[1])!)).ToList();
        var procedures = (await Rows(ProceduresSql)).Select(r => (S(r[0])!, S(r[1]))).ToList();
        var packageProcedures = (await Rows(PackageProceduresSql)).Select(r => (S(r[0])!, S(r[1]))).ToList();

        var signatures = new Dictionary<string, RoutineSignature>(StringComparer.OrdinalIgnoreCase);
        var members = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string Key(string? package, string name)
        {
            if (package is null) return name;
            if (!members.TryGetValue(package, out var list)) members[package] = list = [];
            if (!list.Contains(name)) list.Add(name);
            return $"{package}.{name}";
        }
        // Un parámetro con dominio propio muestra el dominio; los demás, el tipo.
        static string Type(object?[] r, int at, string? source) =>
            source is not null && !source.StartsWith("RDB$", StringComparison.Ordinal)
                ? source
                : r[at] is null ? "?" : FieldTypeFormatter.Format(I(r[at])!.Value, I(r[at + 1]), I(r[at + 2]), I(r[at + 3]), I(r[at + 4]), I(r[at + 5]));

        foreach (var g in (await Rows(ProcedureInputsSql)).GroupBy(r => (Package: S(r[0]), Name: S(r[1])!)))
        {
            var parameters = g.Where(r => r[2] is not null).Select(r => new RoutineParameter(S(r[2])!, Type(r, 3, S(r[9])), I(r[10]) == 1)).ToList();
            var key = Key(g.Key.Package, g.Key.Name);
            signatures[key] = new RoutineSignature(key, parameters);
        }

        var functions = new List<string>();
        var stored = new List<string>(); // las que no son UDF
        foreach (var g in (await Rows(FunctionsSql)).GroupBy(r => (Package: S(r[0]), Name: S(r[1])!)))
        {
            var returnPosition = I(g.First()[12]) ?? 0;
            var arguments = g.Where(r => r[2] is not null).ToList();
            var returns = arguments.FirstOrDefault(r => I(r[2]) == returnPosition) is { } ret ? Type(ret, 4, S(ret[10])) : "?";
            var parameters = arguments.Where(r => I(r[2]) != returnPosition)
                .Select(r => new RoutineParameter(S(r[3]) ?? "", Type(r, 4, S(r[10])), I(r[11]) == 1)).ToList();
            var key = Key(g.Key.Package, g.Key.Name);
            if (g.Key.Package is null) functions.Add(g.Key.Name);
            if (g.Key.Package is null && I(g.First()[13]) != 1) stored.Add(g.Key.Name);
            signatures[key] = new RoutineSignature(key, parameters, returns);
        }

        var packages = (await Rows(PackagesSql)).Select(r => S(r[0])!).ToList();
        var generators = (await Rows(GeneratorsSql)).Select(r => S(r[0])!).ToList();
        var exceptions = (await Rows(ExceptionsSql)).Select(r => S(r[0])!).ToList();
        // Las FK (para sugerir el JOIN): primero las de verdad, después las lógicas.
        var foreignKeys = (await Rows(MetadataExplorer.DiagramForeignKeysSql)).GroupBy(r => S(r[0])!)
            .Select(g => new DiagramLink(g.Key, S(g.First()[1])!, g.Select(r => S(r[3])!).ToList(), S(g.First()[2])!,
                g.Where(r => r[4] is not null).Select(r => S(r[4])!).ToList()))
            .Concat((await Rows(LogicalForeignKeysSql)).Select(r => LogicalForeignKeys.Parse(S(r[0])!, S(r[1])!, S(r[2]))).OfType<DiagramLink>())
            .ToList();
        await tx.CommitAsync(ct);

        return new MetadataCatalog(columns, procedures, packageProcedures)
        {
            Functions = functions.Order(StringComparer.Ordinal).ToList(),
            StoredFunctions = stored.Order(StringComparer.Ordinal).ToList(),
            Packages = packages,
            PackageMembers = members.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Order(StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase),
            Generators = generators,
            Exceptions = exceptions,
            Signatures = signatures,
            ForeignKeys = foreignKeys,
        };
    }
}
