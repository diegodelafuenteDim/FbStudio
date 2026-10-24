using FbStudio.Core.Metadata;
using FbStudio.Core.Monitoring;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FbStudio.Core.Results;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

public sealed record MonitorResult(ColumnInfo[] Columns, IReadOnlyList<object[]> Rows);

/// <summary>
/// Conexión propia del monitoreo (F-1). MON$ es una foto tomada al empezar cada transacción, así que cada refresco
/// usa una transacción nueva y la confirma enseguida: el monitoreo nunca frena la OAT que está mirando.
/// Cortar una conexión o cancelar una sentencia es un DELETE sobre MON$ en una transacción corta aparte.
/// </summary>
public sealed class MonitorSession(ConnectionProfile profile, string password) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FbConnection? _connection;

    public ConnectionProfile Profile { get; } = profile;

    /// <summary>Número de conexión del propio monitoreo (no se puede cortar a sí mismo).</summary>
    public long? OwnAttachment { get; private set; }

    public Task<MonitorSummary> SummaryAsync(CancellationToken ct = default) =>
        WithSnapshotAsync(async (connection, tx) =>
        {
            await using var cmd = new FbCommand(MonitorQueries.SummarySql, connection, tx);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            long L(int i) => r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();
            var summary = new MonitorSummary(L(0), L(1), L(2), L(3), (int)L(4), (int)L(5), (int)L(6),
                r.IsDBNull(7) ? null : L(7), L(8), r.GetString(9).Trim(), S(10))
            {
                OatUser = S(11), OatIp = S(12), OatProcess = S(13), OatSystem = L(14) == 1,
                WaitingStatements = (int)L(15), LongestWaitSeconds = r.IsDBNull(16) ? null : L(16), IsOwner = L(17) == 1,
                BackupState = BackupStates.From(r.IsDBNull(18) ? 0 : (int)L(18)), OatHost = S(19),
            };
            OwnAttachment = summary.OwnAttachment;
            return summary;
        }, ct);

    /// <summary>Classic, SuperClassic o SuperServer (F-16); se averigua una vez por conexión del monitoreo.</summary>
    public async Task<ServerModeEvidence?> ServerModeAsync(CancellationToken ct = default)
    {
        if (_mode is not null) return _mode;
        await _gate.WaitAsync(ct);
        try
        {
            return _mode = await ServerProbe.ModeAsync(await OpenAsync(ct), Profile, password, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ServerModeEvidence? _mode;

    public Task<MonitorResult> QueryAsync(string sql, CancellationToken ct = default) =>
        WithSnapshotAsync(async (connection, tx) =>
        {
            await using var cmd = new FbCommand(sql, connection, tx);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var columns = Enumerable.Range(0, r.FieldCount).Select(i => new ColumnInfo(i, r.GetName(i), r.GetDataTypeName(i))).ToArray();
            var rows = new List<object[]>();
            while (await r.ReadAsync(ct))
            {
                var values = new object[columns.Length];
                r.GetValues(values);
                rows.Add(values);
            }
            return new MonitorResult(columns, rows);
        }, ct);

    /// <summary>Corta una conexión: su transacción abierta se deshace. Devuelve false si ya no existía o no se ve (sin RDB$ADMIN).</summary>
    public Task<bool> KillAttachmentAsync(long attachmentId, CancellationToken ct = default)
    {
        if (attachmentId == OwnAttachment) throw new InvalidOperationException(CoreStrings.MonCannotKillSelf);
        return DeleteAsync(MonitorQueries.KillAttachmentSql, attachmentId, ct);
    }

    /// <summary>
    /// Usuario, dirección, equipo y programa de una conexión, leídos de MON$ATTACHMENTS (null si ya no está). La
    /// confirmación de «Cortar conexión» usa esto y no las columnas de la consulta, que puede ser una propia importada.
    /// </summary>
    public Task<AttachmentWho?> AttachmentAsync(long attachmentId, CancellationToken ct = default) =>
        WithSnapshotAsync(async (connection, tx) =>
        {
            await using var cmd = new FbCommand(MonitorQueries.AttachmentSql, connection, tx);
            cmd.Parameters.Add("@id", attachmentId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return (AttachmentWho?)null;
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();
            return new AttachmentWho(S(0), S(1), S(2), S(3));
        }, ct);

    /// <summary>Cancela una sentencia en curso sin cortar su conexión (le llega un error de «operación cancelada»).</summary>
    public Task<bool> CancelStatementAsync(long statementId, CancellationToken ct = default) =>
        DeleteAsync(MonitorQueries.CancelStatementSql, statementId, ct);

    private async Task<bool> DeleteAsync(string sql, long id, CancellationToken ct)
    {
        if (Profile.ReadOnly) throw new InvalidOperationException(string.Format(CoreStrings.ProfileIsReadOnly, Profile.Name));
        await _gate.WaitAsync(ct);
        try
        {
            var connection = await OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadWrite(TimeSpan.FromSeconds(5)), ct);
            await using var cmd = new FbCommand(sql, connection, tx);
            cmd.Parameters.Add("@id", id);
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return affected > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> WithSnapshotAsync<T>(Func<FbConnection, FbTransaction, Task<T>> read, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var connection = await OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(Transactions.Snapshot, ct);
            var result = await read(connection, tx);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (FbException) when (_connection is { State: not System.Data.ConnectionState.Open })
        {
            await DropConnectionAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FbConnection> OpenAsync(CancellationToken ct)
    {
        if (_connection is not null) return _connection;
        var c = FbConnectionFactory.Create(Profile, password, "monitoreo");
        await c.OpenAsync(ct);
        return _connection = c;
    }

    private async Task DropConnectionAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _connection = null;
        OwnAttachment = null;
    }

    public async ValueTask DisposeAsync() => await DropConnectionAsync();
}

/// <summary>Quién tiene una conexión, según el servidor.</summary>
public sealed record AttachmentWho(string? User, string? Address, string? Host, string? Process);
