using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace FbStudio.Firebird;

/// <summary>
/// Versión, tipo de servidor (Classic / SuperClassic / SuperServer) y sistema del servidor (F-16). El tipo sale de
/// MON$ATTACHMENTS (ver <see cref="ServerModeEvidence"/>): si la conexión ve menos de dos conexiones remotas, se abre
/// una segunda, corta, para comparar los procesos. El sistema sale de la API de servicios, que contesta a cualquier
/// usuario; lo que no es esencial, si falla o tarda, queda vacío y la base igual se conecta.
/// </summary>
public static class ServerProbe
{
    private static readonly TimeSpan ServicesTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Columnas para deducir el tipo de servidor: PID propio, conexiones remotas, procesos, Cache Writer y si se ve todo.</summary>
    private const string EvidenceColumns = """
        (SELECT a.MON$SERVER_PID FROM MON$ATTACHMENTS a WHERE a.MON$ATTACHMENT_ID = CURRENT_CONNECTION),
        (SELECT COUNT(*) FROM MON$ATTACHMENTS a WHERE a.MON$SYSTEM_FLAG = 0 AND a.MON$REMOTE_PROTOCOL IS NOT NULL),
        (SELECT COUNT(DISTINCT a.MON$SERVER_PID) FROM MON$ATTACHMENTS a WHERE a.MON$SYSTEM_FLAG = 0 AND a.MON$REMOTE_PROTOCOL IS NOT NULL),
        (SELECT COUNT(*) FROM MON$ATTACHMENTS a WHERE a.MON$SYSTEM_FLAG = 1 AND a.MON$USER = 'Cache Writer'),
        IIF(CURRENT_USER = 'SYSDBA' OR CURRENT_ROLE = 'RDB$ADMIN'
            OR CURRENT_USER = (SELECT TRIM(r.RDB$OWNER_NAME) FROM RDB$RELATIONS r WHERE r.RDB$RELATION_NAME = 'RDB$DATABASE'), 1, 0)
        """;

    private const string InfoSql = $"""
        SELECT RDB$GET_CONTEXT('SYSTEM', 'ENGINE_VERSION'), m.MON$ODS_MAJOR, m.MON$ODS_MINOR, m.MON$SQL_DIALECT, m.MON$PAGE_SIZE,
               TRIM(d.RDB$CHARACTER_SET_NAME), m.MON$PAGE_BUFFERS,
               {EvidenceColumns},
               m.MON$BACKUP_STATE
        FROM MON$DATABASE m CROSS JOIN RDB$DATABASE d
        """;

    private const string EvidenceSql = $"SELECT {EvidenceColumns} FROM RDB$DATABASE";

    private const string WireEncryptedSql = "SELECT RDB$GET_CONTEXT('SYSTEM', 'WIRE_ENCRYPTED') FROM RDB$DATABASE";

    /// <summary>
    /// Si la conexión va cifrada; null si el servidor no lo dice (una versión que no tenga WIRE_ENCRYPTED da error y se
    /// toma como «no se sabe»). La conexión no puede tener una transacción abierta.
    /// </summary>
    public static async Task<bool?> WireEncryptedAsync(FbConnection connection, CancellationToken ct = default)
    {
        try
        {
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            await using var cmd = new FbCommand(WireEncryptedSql, connection, tx);
            var value = await cmd.ExecuteScalarAsync(ct) as string;
            await tx.CommitAsync(ct);
            return value?.Trim() switch { "TRUE" => true, "FALSE" => false, _ => null };
        }
        catch (FbException)
        {
            return null;
        }
    }

    /// <summary>Lo que se muestra al conectar: versión, datos de la base, tipo de servidor y sistema.</summary>
    public static async Task<ServerInfo> InfoAsync(FbConnection connection, ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        ServerInfo info;
        ServerModeEvidence evidence;
        await using (var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct))
        {
            await using var cmd = new FbCommand(InfoSql, connection, tx);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            info = new ServerInfo(
                r.IsDBNull(0) ? "?" : r.GetString(0).Trim(), Int(r, 1), Int(r, 2), Int(r, 3), Int(r, 4),
                r.IsDBNull(5) ? null : r.GetString(5))
            {
                PageBuffers = Int(r, 6),
                BackupState = BackupStates.From(Int(r, 12)),
            };
            evidence = Evidence(r, 7);
            await r.CloseAsync();
            await tx.CommitAsync(ct);
        }

        var wire = await WireEncryptedAsync(connection, ct);
        var mode = evidence.RemoteAttachments >= 2 ? Task.FromResult<ServerModeEvidence?>(evidence) : CompareAsync(profile, password, evidence, ct);
        var implementation = ImplementationAsync(profile, password, ct);
        return info with { ModeEvidence = await mode, Implementation = await implementation, WireEncrypted = wire };
    }

    /// <summary>Lo de <see cref="InfoAsync"/> más la versión completa. Las carpetas del servidor no se piden: no se muestran rutas.</summary>
    public static async Task<ServerDetails> DetailsAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "datos del servidor");
        await connection.OpenAsync(ct);
        var info = await InfoAsync(connection, profile, password, ct);
        var cs = FbConnectionFactory.ConnectionString(profile, password, "datos del servidor");
        return new ServerDetails(info, await Service(p => p.GetServerVersionAsync(ct), cs, ct));
    }

    /// <summary>
    /// El tipo de servidor desde una conexión ya abierta (la del monitoreo); si ve menos de dos conexiones remotas,
    /// compara con una segunda.
    /// </summary>
    public static async Task<ServerModeEvidence?> ModeAsync(FbConnection connection, ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        var evidence = await ReadEvidenceAsync(connection, ct);
        return evidence.RemoteAttachments >= 2 ? evidence : await CompareAsync(profile, password, evidence, ct);
    }

    /// <summary>Abre una conexión corta y lee desde ella: en su foto de MON$ están las dos.</summary>
    private static async Task<ServerModeEvidence?> CompareAsync(ConnectionProfile profile, string password, ServerModeEvidence first, CancellationToken ct)
    {
        try
        {
            await using var second = FbConnectionFactory.Create(profile, password, "tipo de servidor");
            await second.OpenAsync(ct);
            return await ReadEvidenceAsync(second, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return first; // sin la segunda conexión el tipo queda sin saber (Unknown)
        }
    }

    private static async Task<ServerModeEvidence> ReadEvidenceAsync(FbConnection connection, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
        await using var cmd = new FbCommand(EvidenceSql, connection, tx);
        ServerModeEvidence evidence;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            evidence = Evidence(r, 0);
        }
        await tx.CommitAsync(ct);
        return evidence;
    }

    private static ServerModeEvidence Evidence(FbDataReader r, int first) => new(
        r.IsDBNull(first) ? 0 : Convert.ToInt64(r.GetValue(first)), Int(r, first + 1), Int(r, first + 2), Int(r, first + 3) > 0, Int(r, first + 4) == 1);

    /// <summary>«Firebird/Windows/AMD/Intel/x64»: la API de servicios se lo dice a cualquier usuario.</summary>
    private static Task<string?> ImplementationAsync(ConnectionProfile profile, string password, CancellationToken ct) =>
        Service(p => p.GetImplementationAsync(ct), FbConnectionFactory.ConnectionString(profile, password, "datos del servidor"), ct);

    /// <summary>Una pregunta a la API de servicios: null si falla (sin permiso, por ejemplo) o tarda más de 5 s.</summary>
    private static async Task<string?> Service(Func<FbServerProperties, Task<string>> ask, string connectionString, CancellationToken ct)
    {
        try
        {
            var value = await ask(new FbServerProperties(connectionString)).WaitAsync(ServicesTimeout, ct);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static int Int(FbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
}
