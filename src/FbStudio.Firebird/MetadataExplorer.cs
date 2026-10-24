using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>
/// Lecturas de RDB$ para el explorador, con una conexión propia (no toca la transacción del editor).
/// Las consultas asumen Firebird 3+ (paquetes, funciones PSQL); en servidores más viejos esas categorías quedan vacías.
/// </summary>
/// <summary>
/// Cómo se edita una vista: su tabla (si sale de una sola), las columnas de la vista que forman la clave primaria de
/// esa tabla (vacío = no se puede editar) y las que son expresiones (no se escriben).
/// </summary>
public sealed record ViewEditInfo(string? BaseTable, IReadOnlySet<string> KeyColumns, IReadOnlySet<string> ExpressionColumns)
{
    public bool IsEditable => KeyColumns.Count > 0;
}

public sealed partial class MetadataExplorer(ConnectionProfile profile, string password) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FbConnection? _connection;

    public ConnectionProfile Profile { get; } = profile;

    private const string NotSystem = "COALESCE(RDB$SYSTEM_FLAG, 0) = 0";

    private static readonly Dictionary<DbObjectKind, string> ObjectsSql = new()
    {
        [DbObjectKind.Table] = $"""
            SELECT TRIM(RDB$RELATION_NAME),
                   CASE RDB$RELATION_TYPE WHEN 2 THEN 'externa' WHEN 4 THEN 'GTT (preserve rows)' WHEN 5 THEN 'GTT (delete rows)' END,
                   0
            FROM RDB$RELATIONS WHERE RDB$VIEW_BLR IS NULL AND {NotSystem}
            """,
        [DbObjectKind.View] = $"SELECT TRIM(RDB$RELATION_NAME), NULL, 0 FROM RDB$RELATIONS WHERE RDB$VIEW_BLR IS NOT NULL AND {NotSystem}",
        [DbObjectKind.Procedure] = $"SELECT TRIM(RDB$PROCEDURE_NAME), NULL, 0 FROM RDB$PROCEDURES WHERE RDB$PACKAGE_NAME IS NULL AND {NotSystem}",
        [DbObjectKind.Function] = $"""
            SELECT TRIM(RDB$FUNCTION_NAME), CASE WHEN RDB$MODULE_NAME IS NOT NULL THEN 'UDF' ELSE 'PSQL' END, 0
            FROM RDB$FUNCTIONS WHERE RDB$PACKAGE_NAME IS NULL AND {NotSystem}
            """,
        [DbObjectKind.Package] = $"SELECT TRIM(RDB$PACKAGE_NAME), NULL, 0 FROM RDB$PACKAGES WHERE {NotSystem}",
        [DbObjectKind.Trigger] = $"""
            SELECT TRIM(RDB$TRIGGER_NAME), COALESCE(TRIM(RDB$RELATION_NAME), '(base de datos)'), COALESCE(RDB$TRIGGER_INACTIVE, 0), TRIM(RDB$RELATION_NAME)
            FROM RDB$TRIGGERS WHERE {NotSystem}
            """,
        [DbObjectKind.Generator] = $"SELECT TRIM(RDB$GENERATOR_NAME), NULL, 0 FROM RDB$GENERATORS WHERE {NotSystem}",
        [DbObjectKind.Exception] = $"SELECT TRIM(RDB$EXCEPTION_NAME), NULL, 0 FROM RDB$EXCEPTIONS WHERE {NotSystem}",
        [DbObjectKind.Index] = $"""
            SELECT TRIM(RDB$INDEX_NAME), TRIM(TRIM(RDB$RELATION_NAME) || IIF(RDB$UNIQUE_FLAG = 1, ' (único)', '')), COALESCE(RDB$INDEX_INACTIVE, 0),
                   TRIM(RDB$RELATION_NAME)
            FROM RDB$INDICES WHERE {NotSystem}
            """,
        [DbObjectKind.Role] = $"SELECT TRIM(RDB$ROLE_NAME), TRIM(RDB$OWNER_NAME), 0 FROM RDB$ROLES WHERE {NotSystem}",
    };

    private const string DomainsSql = $"""
        SELECT TRIM(RDB$FIELD_NAME), RDB$FIELD_TYPE, RDB$FIELD_SUB_TYPE, RDB$FIELD_LENGTH, RDB$CHARACTER_LENGTH,
               RDB$FIELD_PRECISION, RDB$FIELD_SCALE
        FROM RDB$FIELDS WHERE RDB$FIELD_NAME NOT STARTING WITH 'RDB$' AND {NotSystem}
        """;

    private const string CountsSql = $"""
        SELECT 0, COUNT(*) FROM RDB$RELATIONS WHERE RDB$VIEW_BLR IS NULL AND {NotSystem}
        UNION ALL SELECT 1, COUNT(*) FROM RDB$RELATIONS WHERE RDB$VIEW_BLR IS NOT NULL AND {NotSystem}
        UNION ALL SELECT 2, COUNT(*) FROM RDB$PROCEDURES WHERE RDB$PACKAGE_NAME IS NULL AND {NotSystem}
        UNION ALL SELECT 3, COUNT(*) FROM RDB$FUNCTIONS WHERE RDB$PACKAGE_NAME IS NULL AND {NotSystem}
        UNION ALL SELECT 4, COUNT(*) FROM RDB$PACKAGES WHERE {NotSystem}
        UNION ALL SELECT 5, COUNT(*) FROM RDB$TRIGGERS WHERE {NotSystem}
        UNION ALL SELECT 6, COUNT(*) FROM RDB$GENERATORS WHERE {NotSystem}
        UNION ALL SELECT 7, COUNT(*) FROM RDB$EXCEPTIONS WHERE {NotSystem}
        UNION ALL SELECT 8, COUNT(*) FROM RDB$FIELDS WHERE RDB$FIELD_NAME NOT STARTING WITH 'RDB$' AND {NotSystem}
        UNION ALL SELECT 9, COUNT(*) FROM RDB$INDICES WHERE {NotSystem}
        UNION ALL SELECT 10, COUNT(*) FROM RDB$ROLES WHERE {NotSystem}
        """;

    private const string ColumnsSql = """
        SELECT TRIM(rf.RDB$FIELD_NAME), f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, f.RDB$CHARACTER_LENGTH,
               f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0),
               TRIM(rf.RDB$FIELD_SOURCE),
               -- La descripción como VARCHAR (no blob): evita un viaje al servidor por columna.
               CAST(SUBSTRING(rf.RDB$DESCRIPTION FROM 1 FOR 1000) AS VARCHAR(1000)),
               IIF(f.RDB$COMPUTED_BLR IS NULL, 0, 1)
        FROM RDB$RELATION_FIELDS rf
        JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE
        WHERE rf.RDB$RELATION_NAME = @rel
        ORDER BY rf.RDB$FIELD_POSITION
        """;

    private const string PrimaryKeySql = """
        SELECT TRIM(s.RDB$FIELD_NAME)
        FROM RDB$RELATION_CONSTRAINTS rc
        JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME
        WHERE rc.RDB$RELATION_NAME = @rel AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'
        """;

    public Task<IReadOnlyDictionary<DbObjectKind, int>> CountAsync(CancellationToken ct = default) =>
        WithReaderAsync(CountsSql, null, async reader =>
        {
            var counts = new Dictionary<DbObjectKind, int>();
            while (await reader.ReadAsync(ct)) counts[(DbObjectKind)reader.GetInt32(0)] = reader.GetInt32(1);
            return (IReadOnlyDictionary<DbObjectKind, int>)counts;
        }, ct);

    /// <summary>Versión del motor, datos de la base y tipo de servidor; se lee una vez por conexión del explorador.</summary>
    public ServerInfo? Server { get; private set; }

    /// <summary>Al recargar, se vuelve a leer (p. ej. si la base salió del modo backup).</summary>
    public void ForgetServerInfo() => Server = null;

    public async Task<ServerInfo> ServerInfoAsync(CancellationToken ct = default) =>
        Server ??= await WithConnectionAsync(connection => ServerProbe.InfoAsync(connection, Profile, password, ct), ct);

    // Ctrl+clic (F-32): qué es un nombre. Un nombre puede repetirse entre tipos (una tabla y un dominio): manda el orden.
    private const string FindSql = """
        SELECT 1, IIF(RDB$VIEW_BLR IS NULL, 'T', 'V'), CAST(NULL AS VARCHAR(63)) FROM RDB$RELATIONS
            WHERE RDB$RELATION_NAME = @rel AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        UNION ALL SELECT 2, 'P', NULL FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @rel AND RDB$PACKAGE_NAME IS NULL
        UNION ALL SELECT 3, 'F', NULL FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @rel AND RDB$PACKAGE_NAME IS NULL
        UNION ALL SELECT 4, 'K', NULL FROM RDB$PACKAGES WHERE RDB$PACKAGE_NAME = @rel
        UNION ALL SELECT 5, 'G', NULL FROM RDB$GENERATORS WHERE RDB$GENERATOR_NAME = @rel AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        UNION ALL SELECT 6, 'E', NULL FROM RDB$EXCEPTIONS WHERE RDB$EXCEPTION_NAME = @rel
        UNION ALL SELECT 7, 'R', TRIM(RDB$RELATION_NAME) FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @rel AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        UNION ALL SELECT 8, 'I', TRIM(RDB$RELATION_NAME) FROM RDB$INDICES WHERE RDB$INDEX_NAME = @rel AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        UNION ALL SELECT 9, 'D', NULL FROM RDB$FIELDS WHERE RDB$FIELD_NAME = @rel AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        UNION ALL SELECT 10, 'O', NULL FROM RDB$ROLES WHERE RDB$ROLE_NAME = @rel AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0
        ORDER BY 1
        """;

    /// <summary>El objeto que se llama así (tabla, vista, procedimiento, función, paquete, generador…), o null.</summary>
    public Task<DbObject?> FindObjectAsync(string name, CancellationToken ct = default) =>
        WithReaderAsync<DbObject?>(FindSql, name, async reader =>
        {
            if (!await reader.ReadAsync(ct)) return null;
            var kind = reader.GetString(1) switch
            {
                "T" => DbObjectKind.Table,
                "V" => DbObjectKind.View,
                "P" => DbObjectKind.Procedure,
                "F" => DbObjectKind.Function,
                "K" => DbObjectKind.Package,
                "G" => DbObjectKind.Generator,
                "E" => DbObjectKind.Exception,
                "R" => DbObjectKind.Trigger,
                "I" => DbObjectKind.Index,
                "D" => DbObjectKind.Domain,
                _ => DbObjectKind.Role,
            };
            return new DbObject(kind, name, reader.IsDBNull(2) ? null : reader.GetString(2));
        }, ct);

    public async Task<IReadOnlyList<DbObject>> LoadObjectsAsync(DbObjectKind kind, CancellationToken ct = default)
    {
        try
        {
            if (kind == DbObjectKind.Domain)
            {
                return await WithReaderAsync(DomainsSql, null, async reader =>
                {
                    var list = new List<DbObject>();
                    while (await reader.ReadAsync(ct))
                        list.Add(new DbObject(kind, reader.GetString(0), FieldTypeFormatter.Format(
                            reader.GetInt16(1), Int(reader, 2), Int(reader, 3), Int(reader, 4), Int(reader, 5), Int(reader, 6))));
                    return Sorted(list);
                }, ct);
            }

            return await WithReaderAsync(ObjectsSql[kind], null, async reader =>
            {
                var list = new List<DbObject>();
                while (await reader.ReadAsync(ct))
                    list.Add(new DbObject(kind, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1).Trim(), Convert.ToInt32(reader.GetValue(2)) == 1,
                        reader.FieldCount > 3 && !reader.IsDBNull(3) ? reader.GetString(3) : null));
                return Sorted(list);
            }, ct);
        }
        catch (FbException) when (kind is DbObjectKind.Package or DbObjectKind.Function)
        {
            return []; // Firebird 2.5: no hay paquetes ni funciones PSQL
        }
    }

    /// <summary>Todos los objetos de la base, tipo por tipo (para las áreas, F-9).</summary>
    public async Task<IReadOnlyList<DbObject>> LoadAllObjectsAsync(CancellationToken ct = default)
    {
        var all = new List<DbObject>();
        foreach (var kind in DbObjectKinds.All) all.AddRange(await LoadObjectsAsync(kind, ct));
        return all;
    }

    // Qué tablas y vistas usa cada vista (1), trigger (2) y procedimiento (5), sin las del sistema (RDB$DATABASE, MON$…):
    // para las áreas que suman «lo que usa solo sus tablas» (F-9).
    private const string TableUsesSql = """
        SELECT DISTINCT TRIM(d.RDB$DEPENDENT_NAME), d.RDB$DEPENDENT_TYPE, TRIM(d.RDB$DEPENDED_ON_NAME)
        FROM RDB$DEPENDENCIES d
        WHERE d.RDB$DEPENDED_ON_TYPE = 0 AND d.RDB$DEPENDENT_TYPE IN (1, 2, 5)
          AND d.RDB$DEPENDENT_NAME <> d.RDB$DEPENDED_ON_NAME
          AND d.RDB$DEPENDED_ON_NAME NOT STARTING WITH 'RDB$' AND d.RDB$DEPENDED_ON_NAME NOT STARTING WITH 'MON$'
          AND d.RDB$DEPENDED_ON_NAME NOT STARTING WITH 'SEC$'
        """;

    /// <summary>Las tablas y vistas que usa cada procedimiento, vista y trigger (para las áreas, F-9).</summary>
    public Task<TableUses> LoadTableUsesAsync(CancellationToken ct = default) =>
        WithReaderAsync(TableUsesSql, null, async reader =>
        {
            var rows = new List<(DbObjectKind, string, string)>();
            while (await reader.ReadAsync(ct))
            {
                var kind = Convert.ToInt32(reader.GetValue(1)) switch
                {
                    1 => DbObjectKind.View,
                    2 => DbObjectKind.Trigger,
                    _ => DbObjectKind.Procedure,
                };
                rows.Add((kind, reader.GetString(0), reader.GetString(2)));
            }
            return new TableUses(rows);
        }, ct);

    // Cada columna de la vista con la columna y la tabla de donde sale (nulas si es una expresión), y cuántas
    // tablas o vistas usa la vista.
    private const string ViewColumnsSql = """
        SELECT TRIM(vf.RDB$FIELD_NAME), TRIM(vf.RDB$BASE_FIELD), TRIM(vr.RDB$RELATION_NAME),
               (SELECT COUNT(*) FROM RDB$VIEW_RELATIONS x WHERE x.RDB$VIEW_NAME = @rel)
        FROM RDB$RELATION_FIELDS vf
        LEFT JOIN RDB$VIEW_RELATIONS vr ON vr.RDB$VIEW_NAME = vf.RDB$RELATION_NAME AND vr.RDB$VIEW_CONTEXT = vf.RDB$VIEW_CONTEXT
        WHERE vf.RDB$RELATION_NAME = @rel
        """;

    /// <summary>
    /// Si una vista se puede editar en la pestaña Datos: tiene que salir de una sola tabla y mostrar toda su clave
    /// primaria (así se sabe qué fila cambiar). Las columnas que son expresiones no se escriben. Que la vista acepte
    /// el cambio lo decide el servidor al aplicar (una vista con DISTINCT o GROUP BY no deja).
    /// </summary>
    public async Task<ViewEditInfo> ViewEditInfoAsync(string view, CancellationToken ct = default)
    {
        var columns = await WithReaderAsync(ViewColumnsSql, view, async reader =>
        {
            var list = new List<(string Name, string? BaseField, string? BaseTable, int Relations)>();
            while (await reader.ReadAsync(ct))
                list.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    Convert.ToInt32(reader.GetValue(3))));
            return list;
        }, ct);
        var expressions = columns.Where(c => c.BaseField is null).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var tables = columns.Where(c => c.BaseTable is not null).Select(c => c.BaseTable!).Distinct().ToList();
        if (columns.Count == 0 || columns[0].Relations != 1 || tables.Count != 1)
            return new ViewEditInfo(null, new HashSet<string>(), expressions);
        var baseTable = tables[0];
        var pk = await WithReaderAsync(PrimaryKeySql, baseTable, async reader =>
        {
            var set = new List<string>();
            while (await reader.ReadAsync(ct)) set.Add(reader.GetString(0));
            return set;
        }, ct);
        var key = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in pk)
        {
            if (columns.FirstOrDefault(c => c.BaseField == field && c.BaseTable == baseTable) is not { Name: { } viewColumn })
                return new ViewEditInfo(baseTable, new HashSet<string>(), expressions); // la vista no muestra toda la clave
            key.Add(viewColumn);
        }
        return new ViewEditInfo(baseTable, key, expressions);
    }

    public async Task<IReadOnlyList<DbColumn>> LoadColumnsAsync(string relation, CancellationToken ct = default)
    {
        var pk = await WithReaderAsync(PrimaryKeySql, relation, async reader =>
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(ct)) set.Add(reader.GetString(0));
            return set;
        }, ct);

        return await WithReaderAsync(ColumnsSql, relation, async reader =>
        {
            var list = new List<DbColumn>();
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                var source = reader.GetString(8);
                list.Add(new DbColumn(
                    name,
                    FieldTypeFormatter.Format(reader.GetInt16(1), Int(reader, 2), Int(reader, 3), Int(reader, 4), Int(reader, 5), Int(reader, 6)),
                    NotNull: Convert.ToInt32(reader.GetValue(7)) == 1,
                    IsPrimaryKey: pk.Contains(name),
                    Domain: source.StartsWith("RDB$", StringComparison.Ordinal) ? null : source,
                    Description: reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                    IsComputed: Convert.ToInt32(reader.GetValue(10)) == 1));
            }
            return (IReadOnlyList<DbColumn>)list;
        }, ct);
    }

    private Task<T> WithReaderAsync<T>(string sql, string? relation, Func<FbDataReader, Task<T>> read, CancellationToken ct) =>
        WithTransactionAsync(async (connection, tx) =>
        {
            await using var cmd = new FbCommand(sql, connection, tx);
            if (relation is not null) cmd.Parameters.Add("@rel", relation);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await read(reader);
        }, ct);

    /// <summary>Varias lecturas en una misma transacción de solo lectura, de a una por vez sobre la conexión del explorador.</summary>
    private Task<T> WithTransactionAsync<T>(Func<FbConnection, FbTransaction, Task<T>> work, CancellationToken ct) =>
        WithConnectionAsync(async connection =>
        {
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            var result = await work(connection, tx);
            await tx.CommitAsync(ct);
            return result;
        }, ct);

    /// <summary>La conexión del explorador (se abre la primera vez), de a un uso por vez.</summary>
    private async Task<T> WithConnectionAsync<T>(Func<FbConnection, Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is null)
            {
                var c = FbConnectionFactory.Create(Profile, password, "explorador");
                await c.OpenAsync(ct);
                _connection = c;
            }
            return await work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int? Int(FbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));

    private static IReadOnlyList<DbObject> Sorted(List<DbObject> list)
    {
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _connection = null;
        Server = null;
    }
}
