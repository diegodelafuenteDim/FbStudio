using System.Diagnostics;
using System.Text.RegularExpressions;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FbStudio.Core.Sql;
using FbStudio.Core.Statistics;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace FbStudio.Firebird;

/// <summary>Dónde está la base en el servidor (ruta completa, aunque el perfil use un alias) y con qué se creó.</summary>
public sealed record DatabaseFileInfo(string Path, int PageSize, string Charset, int Dialect);

/// <param name="MetadataOnly">Solo la estructura (gbak -m): al restaurarla los generadores vuelven a 0 y las identity a 1.</param>
/// <param name="NoGarbageCollect">No limpia versiones viejas mientras lee (gbak -g): más rápido, la base queda igual.</param>
/// <param name="IgnoreChecksums">Sigue aunque haya páginas con errores de checksum (gbak -ig): para rescatar una base dañada.</param>
/// <param name="NonTransportable">Formato propio de la plataforma (gbak -nt): un poco más rápido; se restaura solo en el mismo tipo de sistema.</param>
/// <param name="SkipData">Tablas que van sin sus datos (gbak -skip_data): la estructura sí.</param>
public sealed record BackupOptions(bool MetadataOnly = false, bool NoGarbageCollect = false, bool IgnoreChecksums = false,
    bool NonTransportable = false, IReadOnlyList<string>? SkipData = null);

/// <param name="Replace">Pisa el archivo si existe. Nunca la base del perfil: eso se rechaza siempre.</param>
/// <param name="PageSize">Null: el mismo tamaño de página que tenía la base.</param>
/// <param name="NoValidity">Sin las reglas NOT NULL de las columnas (gbak -n): restaura aunque haya filas con NULL en una
/// columna NOT NULL (datos viejos). La base queda sin esos NOT NULL; los CHECK quedan (en Firebird 3, probado en Docker).</param>
/// <param name="OneAtATime">Confirma tabla por tabla (gbak -o): si los datos de una fallan, las demás quedan.</param>
/// <param name="NoShadow">Sin los archivos espejo (shadow) que tuviera la base (gbak -k).</param>
/// <param name="SkipData">Tablas que se restauran sin sus datos.</param>
public sealed record RestoreOptions(bool Replace = false, bool MetadataOnly = false, int? PageSize = null, bool DeactivateIndexes = false,
    bool NoValidity = false, bool OneAtATime = false, bool NoShadow = false, IReadOnlyList<string>? SkipData = null);

public enum RestoreStatus
{
    /// <summary>No hay base en esa ruta (todavía no empezó, o falló y gbak la borró).</summary>
    Missing,

    /// <summary>La base existe pero está en mantenimiento: el restore sigue corriendo (o quedó a medio hacer).</summary>
    InProgress,

    /// <summary>La base está lista para usar.</summary>
    Online,

    /// <summary>No se pudo saber (el detalle dice por qué).</summary>
    Unknown,
}

public sealed record RestoreState(RestoreStatus Status, string? Detail = null);

/// <summary>Lo que dijo la validación en línea: el texto completo y los totales de su último renglón.</summary>
public sealed record ValidationResult(IReadOnlyList<string> Lines, int? Errors, int? Warnings, TimeSpan Elapsed);

/// <summary>Una sentencia del script de la base vacía que falló, con lo que dijo Firebird.</summary>
public sealed record ScriptError(string Statement, string Message);

/// <param name="GeneratorValues">Generadores e identity con el valor actual del origen (si no, empiezan de cero).</param>
/// <param name="Tables">Tablas a copiar con sus datos; se agregan solas las que referencian sus FK.</param>
public sealed record EmptyCopyOptions(bool GeneratorValues = true, IReadOnlyCollection<string>? Tables = null);

public sealed record TableCopyResult(string Table, long Rows, string? Error = null);

public sealed record EmptyCopyResult(string Path, IReadOnlyList<ScriptError> Errors, IReadOnlyList<TableCopyResult> Tables, TimeSpan Elapsed);

/// <summary>
/// Backup, restore, validación y sweep por la API de servicios (lo mismo que gbak y gfix, corriendo en el servidor),
/// y la base vacía armada desde el script de la base.
/// <para>
/// El backup y el restore los hace el servidor: si FbStudio se cierra o se corta la red, el trabajo sigue hasta el final.
/// FirebirdClient vuelve antes de que termine (sin detalle enseguida; con detalle, a veces la salida se corta), así que
/// el final se comprueba aparte: <see cref="BackupRunningAsync"/> y <see cref="RestoreStateAsync"/>.
/// </para>
/// </summary>
public static partial class DatabaseServices
{
    public static async Task<DatabaseFileInfo> FileInfoAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "mantenimiento");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
        await using var cmd = new FbCommand("""
            SELECT TRIM(m.MON$DATABASE_NAME), m.MON$PAGE_SIZE, TRIM(COALESCE(d.RDB$CHARACTER_SET_NAME, 'NONE')), m.MON$SQL_DIALECT
            FROM MON$DATABASE m CROSS JOIN RDB$DATABASE d
            """, connection, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return new DatabaseFileInfo(r.GetString(0), Convert.ToInt32(r.GetValue(1)), r.GetString(2), Convert.ToInt32(r.GetValue(3)));
    }

    /// <summary>
    /// Ruta en el servidor de una base cualquiera (la del perfil, otra ruta o un alias), conectándose a ella; null si no
    /// existe o no se puede abrir. Sirve para no pisar nunca la base en uso, aunque la escriban con un alias.
    /// </summary>
    public static async Task<string?> ResolvePathAsync(ConnectionProfile profile, string password, string database, CancellationToken ct = default)
    {
        try { return (await FileInfoAsync(profile with { Database = database }, password, ct)).Path; }
        catch (FbException) { return null; }
    }

    /// <summary>
    /// ¿<paramref name="database"/> es la base del perfil? Por ruta, por el mismo alias, o conectándose a ella (otro alias
    /// de la misma base). Si no existe, no lo es.
    /// </summary>
    public static async Task<bool> IsCurrentDatabaseAsync(ConnectionProfile profile, string password, string database, CancellationToken ct = default)
    {
        var current = await FileInfoAsync(profile, password, ct);
        return SamePath(current.Path, database) || SamePath(profile.Database, database)
               || (await ResolvePathAsync(profile, password, database, ct) is { } path && SamePath(path, current.Path));
    }

    /// <summary>¿Es el mismo archivo? En Windows las rutas no distinguen mayúsculas y aceptan / y \.</summary>
    public static bool SamePath(string a, string b)
    {
        static string Norm(string p) => p.Trim().Replace('\\', '/');
        var windows = a.Length > 1 && (a[1] == ':' || a.StartsWith(@"\\", StringComparison.Ordinal));
        return string.Equals(Norm(a), Norm(b), windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Hora del servidor (para reconocer el backup que se lanzó desde acá entre las conexiones de MON$).</summary>
    public static async Task<DateTime> ServerTimeAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "mantenimiento");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
        await using var cmd = new FbCommand("SELECT CURRENT_TIMESTAMP FROM RDB$DATABASE", connection, tx);
        return Convert.ToDateTime(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Lanza el backup en el servidor; el archivo queda en <paramref name="backupFile"/> (ruta del servidor). Con
    /// <paramref name="onLine"/> pide el detalle y lo va pasando renglón por renglón. Cancelar solo deja de mirar:
    /// el servidor lo termina igual.
    /// </summary>
    public static async Task BackupAsync(ConnectionProfile profile, string password, string backupFile, BackupOptions options,
        Action<string>? onLine = null, CancellationToken ct = default)
    {
        var flags = default(FbBackupFlags);
        if (options.MetadataOnly) flags |= FbBackupFlags.MetaDataOnly;
        if (options.NoGarbageCollect) flags |= FbBackupFlags.NoGarbageCollect;
        if (options.IgnoreChecksums) flags |= FbBackupFlags.IgnoreChecksums;
        if (options.NonTransportable) flags |= FbBackupFlags.NonTransportable;
        var service = new FbBackup(FbConnectionFactory.ConnectionString(profile, password, "backup"))
        {
            Verbose = onLine is not null,
            Options = flags,
            SkipData = options.SkipData is { Count: > 0 } skip ? SqlNames.SimilarTo(skip) : null,
        };
        service.BackupFiles.Add(new FbBackupFile(backupFile, null));
        service.ServiceOutput += (_, e) => Lines(e.Message, onLine);
        await service.ExecuteAsync(ct);
    }

    /// <summary>
    /// ¿Sigue corriendo un backup de este usuario? Mientras dura, el servidor tiene una conexión a la base con el mismo
    /// usuario y sin proceso cliente (la abre el propio servicio). Con <paramref name="since"/> (hora del servidor) cuenta
    /// solo las que empezaron después.
    /// </summary>
    public static async Task<bool> BackupRunningAsync(ConnectionProfile profile, string password, DateTime? since = null, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "mantenimiento");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.Snapshot, ct);
        await using var cmd = new FbCommand($"""
            SELECT COUNT(*) FROM MON$ATTACHMENTS a
            WHERE a.MON$ATTACHMENT_ID <> CURRENT_CONNECTION AND a.MON$SYSTEM_FLAG = 0 AND a.MON$USER = CURRENT_USER
              AND a.MON$REMOTE_PROCESS IS NULL AND a.MON$REMOTE_PROTOCOL IS NOT NULL{(since is null ? "" : " AND a.MON$TIMESTAMP >= @since")}
            """, connection, tx);
        if (since is { } s) cmd.Parameters.Add("@since", s);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>
    /// Lanza el restore en el servidor a <paramref name="targetDatabase"/>. Nunca sobre la base del perfil (ni con un
    /// alias de ella): eso se rechaza antes de empezar. Cancelar solo deja de mirar; el servidor lo termina igual.
    /// </summary>
    public static async Task RestoreAsync(ConnectionProfile profile, string password, string backupFile, string targetDatabase, RestoreOptions options,
        Action<string>? onLine = null, CancellationToken ct = default)
    {
        if (profile.ReadOnly) throw new InvalidOperationException(string.Format(CoreStrings.ProfileIsReadOnly, profile.Name));
        if (await IsCurrentDatabaseAsync(profile, password, targetDatabase, ct)) throw new InvalidOperationException(CoreStrings.ServicesTargetIsCurrent);
        // Sin detalle, el error del servidor («ya existe») no le llega al cliente: se revisa antes.
        if (!options.Replace && (await RestoreStateAsync(profile, password, targetDatabase, ct)).Status != RestoreStatus.Missing)
            throw new InvalidOperationException(string.Format(CoreStrings.ServicesTargetExists, targetDatabase));

        var flags = FbRestoreFlags.Create;
        if (options.Replace) flags |= FbRestoreFlags.Replace;
        if (options.MetadataOnly) flags |= FbRestoreFlags.MetaDataOnly;
        if (options.DeactivateIndexes) flags |= FbRestoreFlags.DeactivateIndexes;
        if (options.NoValidity) flags |= FbRestoreFlags.NoValidity;
        if (options.OneAtATime) flags |= FbRestoreFlags.IndividualCommit;
        if (options.NoShadow) flags |= FbRestoreFlags.NoShadow;
        var service = new FbRestore(FbConnectionFactory.ConnectionString(profile with { Database = targetDatabase }, password, "restore"))
        {
            Verbose = onLine is not null,
            Options = flags,
            PageSize = options.PageSize,
            SkipData = options.SkipData is { Count: > 0 } skip ? SqlNames.SimilarTo(skip) : null,
        };
        service.BackupFiles.Add(new FbBackupFile(backupFile, null));
        service.ServiceOutput += (_, e) => Lines(e.Message, onLine);
        await service.ExecuteAsync(ct);
    }

    /// <summary>
    /// En qué quedó la base de un restore, leyendo solo su encabezado (no se conecta: mientras se restaura no se puede).
    /// </summary>
    public static async Task<RestoreState> RestoreStateAsync(ConnectionProfile profile, string password, string database, CancellationToken ct = default)
    {
        try
        {
            var header = (await DatabaseStatistics.ReadAsync(profile with { Database = database }, password, StatisticsScope.Header, ct: ct)).Header;
            var attributes = header.Attributes ?? "";
            return attributes.Contains("shutdown", StringComparison.OrdinalIgnoreCase) || attributes.Contains("maintenance", StringComparison.OrdinalIgnoreCase)
                ? new RestoreState(RestoreStatus.InProgress, attributes)
                : new RestoreState(RestoreStatus.Online, attributes);
        }
        catch (FbException ex) when (ex.ErrorCode is IoError or GstatOpenError)
        {
            return new RestoreState(RestoreStatus.Missing, FirstLine(ex.Message));
        }
        catch (FbException ex)
        {
            return new RestoreState(RestoreStatus.Unknown, FirstLine(ex.Message));
        }
    }

    /// <summary>
    /// Validación en línea (Firebird 3 en adelante): con la base en uso, sin conexión exclusiva. Bloquea por un rato
    /// cada tabla mientras la revisa. No corrige nada; solo informa. Con <paramref name="tables"/>, solo esas tablas (con
    /// sus índices).
    /// </summary>
    public static async Task<ValidationResult> ValidateAsync(ConnectionProfile profile, string password, Action<string>? onLine = null,
        CancellationToken ct = default, IReadOnlyList<string>? tables = null)
    {
        var lines = new List<string>();
        var service = new FbValidation2(FbConnectionFactory.ConnectionString(profile, password, "validación"))
        {
            TablesInclude = tables is { Count: > 0 } ? SqlNames.SimilarTo(tables) : null,
        };
        service.ServiceOutput += (_, e) => Lines(e.Message, line =>
        {
            lines.Add(line);
            onLine?.Invoke(line);
        });
        var clock = Stopwatch.StartNew();
        await service.ExecuteAsync(ct);
        // Sin problemas solo dice «Relation n (T) is ok»; con problemas, «Relation n (T) : 3 ERRORS found» (y WARNINGS).
        int? errors = null, warnings = null;
        if (lines.Any(l => l.Contains("Validation finished", StringComparison.OrdinalIgnoreCase)))
        {
            errors = lines.Sum(l => Found().Match(l) is { Success: true } m && m.Groups[2].Value[0] is 'E' or 'e' ? int.Parse(m.Groups[1].Value) : 0);
            warnings = lines.Sum(l => Found().Match(l) is { Success: true } m && m.Groups[2].Value[0] is 'W' or 'w' ? int.Parse(m.Groups[1].Value) : 0);
        }
        return new ValidationResult(lines, errors, warnings, clock.Elapsed);
    }

    /// <summary>Sweep (gfix -sweep): limpia las versiones viejas de toda la base. Vuelve cuando termina.</summary>
    public static async Task<TimeSpan> SweepAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        if (profile.ReadOnly) throw new InvalidOperationException(string.Format(CoreStrings.ProfileIsReadOnly, profile.Name));
        var service = new FbValidation(FbConnectionFactory.ConnectionString(profile, password, "sweep")) { Options = FbValidationFlags.SweepDatabase };
        var clock = Stopwatch.StartNew();
        await service.ExecuteAsync(ct);
        return clock.Elapsed;
    }

    // isc_io_error y isc_gstat_open_err: no se pudo abrir el archivo (no existe, sin permiso…).
    private const int IoError = 335544344;
    private const int GstatOpenError = 336920605;

    [GeneratedRegex(@"(\d+)\s+(ERRORS?|WARNINGS?)\s+found", RegexOptions.IgnoreCase)]
    private static partial Regex Found();

    private static string FirstLine(string message) => message.Replace("\r", "").Split('\n')[0];

    private static void Lines(string? message, Action<string>? onLine)
    {
        if (onLine is null) return;
        foreach (var line in (message ?? "").Replace("\r", "").Split('\n')) onLine(line);
    }
}
