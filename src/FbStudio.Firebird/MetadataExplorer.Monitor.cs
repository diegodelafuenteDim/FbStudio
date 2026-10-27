using FbStudio.Core.Metadata;
using FbStudio.Core.Monitoring;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>Lecturas de MON$ que comparten el monitoreo y el conector MCP.</summary>
internal static class MonitorReads
{
    /// <summary>El resumen de MON$DATABASE (contadores, OAT y quién la tiene, esperas por bloqueo).</summary>
    public static async Task<MonitorSummary> SummaryAsync(FbConnection connection, FbTransaction tx, CancellationToken ct)
    {
        await using var cmd = new FbCommand(MonitorQueries.SummarySql, connection, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        long L(int i) => r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();
        return new MonitorSummary(L(0), L(1), L(2), L(3), (int)L(4), (int)L(5), (int)L(6),
            r.IsDBNull(7) ? null : L(7), L(8), r.GetString(9).Trim(), S(10))
        {
            OatUser = S(11), OatIp = S(12), OatProcess = S(13), OatSystem = L(14) == 1,
            WaitingStatements = (int)L(15), LongestWaitSeconds = r.IsDBNull(16) ? null : L(16), IsOwner = L(17) == 1,
            BackupState = BackupStates.From(r.IsDBNull(18) ? 0 : (int)L(18)), OatHost = S(19),
        };
    }
}

public sealed partial class MetadataExplorer
{
    // Columnas fijas, sin encabezados: las lee el código. La conexión propia (la del explorador) no se lista.
    private const string ClientColumns = "TRIM(a.MON$USER), a.MON$REMOTE_ADDRESS, a.MON$REMOTE_HOST, a.MON$REMOTE_PROCESS, a.MON$SYSTEM_FLAG";

    private static string TransactionsSql(int max) => $"""
        SELECT FIRST {max} t.MON$TRANSACTION_ID, t.MON$ATTACHMENT_ID, t.MON$TIMESTAMP,
               DATEDIFF(SECOND FROM t.MON$TIMESTAMP TO CURRENT_TIMESTAMP), t.MON$STATE, t.MON$READ_ONLY,
               IIF(t.MON$TRANSACTION_ID = d.MON$OLDEST_ACTIVE, 1, 0), {ClientColumns}
        FROM MON$TRANSACTIONS t
        JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID
        CROSS JOIN MON$DATABASE d
        WHERE t.MON$ATTACHMENT_ID <> CURRENT_CONNECTION
        ORDER BY t.MON$TRANSACTION_ID
        """;

    private static string StatementsSql(int max) => $"""
        SELECT FIRST {max} s.MON$STATEMENT_ID, s.MON$ATTACHMENT_ID, s.MON$TRANSACTION_ID, s.MON$TIMESTAMP,
               DATEDIFF(SECOND FROM s.MON$TIMESTAMP TO CURRENT_TIMESTAMP), s.MON$STATE,
               r.MON$RECORD_SEQ_READS, r.MON$RECORD_IDX_READS, COALESCE(r.MON$RECORD_WAITS, 0), {ClientColumns}, s.MON$SQL_TEXT
        FROM MON$STATEMENTS s
        JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = s.MON$ATTACHMENT_ID
        LEFT JOIN MON$RECORD_STATS r ON r.MON$STAT_ID = s.MON$STAT_ID
        WHERE s.MON$STATE <> 0 AND a.MON$SYSTEM_FLAG = 0 AND s.MON$ATTACHMENT_ID <> CURRENT_CONNECTION
        ORDER BY s.MON$TIMESTAMP
        """;

    private const string ConnectionsSql = $"""
        SELECT {ClientColumns}, COUNT(*)
        FROM MON$ATTACHMENTS a
        WHERE a.MON$SYSTEM_FLAG = 0 AND a.MON$ATTACHMENT_ID <> CURRENT_CONNECTION
        GROUP BY 1, 2, 3, 4, 5
        """;

    /// <summary>
    /// Una foto del monitoreo para el conector MCP (D-118): el resumen, las transacciones abiertas más viejas, las
    /// sentencias en curso y las conexiones agrupadas, en una transacción de solo lectura que se confirma enseguida (no
    /// frena la OAT). Solo consultas fijas sobre MON$: no corta conexiones ni cancela sentencias.
    /// </summary>
    /// <param name="max">Cuántas transacciones y sentencias trae como máximo.</param>
    public Task<MonitorSnapshot> MonitorSnapshotAsync(int max = 30, CancellationToken ct = default) =>
        WithTransactionAsync(Transactions.Snapshot, async (connection, tx) =>
        {
            var summary = await MonitorReads.SummaryAsync(connection, tx, ct);
            long own;
            await using (var cmd = new FbCommand("SELECT CURRENT_TRANSACTION FROM RDB$DATABASE", connection, tx))
                own = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            var transactions = new List<MonitorTransaction>();
            await using (var cmd = new FbCommand(TransactionsSql(max), connection, tx))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    transactions.Add(new MonitorTransaction(r.GetInt64(0), r.GetInt64(1), r.GetDateTime(2), Long(r, 3) ?? 0,
                        Long(r, 4) == 1, Long(r, 5) == 1, Long(r, 6) == 1, Client(r, 7)));

            var statements = new List<MonitorStatement>();
            await using (var cmd = new FbCommand(StatementsSql(max), connection, tx))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    statements.Add(new MonitorStatement(r.GetInt64(0), r.GetInt64(1), Long(r, 2), r.GetDateTime(3), Long(r, 4) ?? 0,
                        Long(r, 5) == 1, Long(r, 6), Long(r, 7), Long(r, 8) > 0, Client(r, 9), r.IsDBNull(14) ? null : r.GetString(14)));

            var connections = new List<MonitorConnections>();
            await using (var cmd = new FbCommand(ConnectionsSql, connection, tx))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    connections.Add(new MonitorConnections(Client(r, 0), (int)(Long(r, 5) ?? 0)));

            return new MonitorSnapshot(summary, transactions, statements, connections, own);
        }, ct);

    private static long? Long(FbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i));

    private static MonitorClient Client(FbDataReader r, int first)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim() is { Length: > 0 } s ? s : null;
        return new MonitorClient(S(first), S(first + 1), S(first + 2), S(first + 3), Long(r, first + 4) == 1);
    }
}
