using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>
/// Lo de nbackup que se puede mirar sin tocar nada (F-29, paso 1): si la base quedó en modo backup, si el delta va a un
/// archivo propio y el historial de RDB$BACKUP_HISTORY. Una transacción de solo lectura; lo ve cualquier usuario.
/// </summary>
public static class NBackupInfo
{
    // RDB$FILES.RDB$FILE_FLAGS: 32 (FILE_difference) es el delta; 64, que está en uso. Mientras dura un backup sin delta
    // propio, Firebird agrega una fila con 96 y sin nombre: el delta propio (ADD DIFFERENCE FILE) es el que tiene nombre.
    private const string StateSql = """
        SELECT m.MON$BACKUP_STATE,
               (SELECT COUNT(*) FROM RDB$FILES f WHERE BIN_AND(COALESCE(f.RDB$FILE_FLAGS, 0), 32) <> 0 AND f.RDB$FILE_NAME IS NOT NULL)
        FROM MON$DATABASE m
        """;

    private const string HistorySql = """
        SELECT RDB$BACKUP_ID, RDB$TIMESTAMP, RDB$BACKUP_LEVEL, TRIM(RDB$GUID), RDB$SCN, TRIM(RDB$FILE_NAME)
        FROM RDB$BACKUP_HISTORY
        ORDER BY RDB$TIMESTAMP DESC, RDB$BACKUP_ID DESC
        """;

    public static async Task<NBackupStatus> ReadAsync(ConnectionProfile profile, string password, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "nbackup");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
        BackupState state;
        bool customDelta;
        await using (var cmd = new FbCommand(StateSql, connection, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            state = BackupStates.From(r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0)));
            customDelta = Convert.ToInt64(r.GetValue(1)) > 0;
        }
        var history = new List<NBackupEntry>();
        await using (var cmd = new FbCommand(HistorySql, connection, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                history.Add(new NBackupEntry(Convert.ToInt32(r.GetValue(0)), r.GetDateTime(1), Convert.ToInt32(r.GetValue(2)),
                    r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : Convert.ToInt64(r.GetValue(4)),
                    r.IsDBNull(5) ? "" : NBackupEntry.NameOf(r.GetString(5))));
        }
        await tx.CommitAsync(ct);
        return new NBackupStatus(state, customDelta, history);
    }
}
