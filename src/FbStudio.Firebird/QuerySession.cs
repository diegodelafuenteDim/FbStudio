using System.Diagnostics;
using System.Threading.Channels;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <param name="Script">
/// Modo script (sin grilla, para scripts largos): no pide el plan ni las lecturas por tabla (son varias idas y vueltas
/// al servidor por sentencia) y las filas de un SELECT solo se cuentan, no se mandan a la grilla.
/// </param>
/// <param name="Parameters">Los valores de los «?» de la sentencia, en orden (null = NULL); ver <see cref="SqlParameters"/>.</param>
public sealed record ExecuteOptions(int MaxRows = 10_000, int BatchSize = 500, int FetchSize = 200, bool Script = false,
    IReadOnlyList<object?>? Parameters = null);

/// <summary>
/// Una sesión por pestaña de editor: una conexión, a lo sumo una transacción de escritura abierta
/// (hay que confirmarla o deshacerla explícitamente) y una conexión lateral para leer MON$.
/// <list type="bullet">
/// <item>SELECT sin transacción abierta → transacción de solo lectura que se cierra sola.</item>
/// <item>DML/DDL/EXECUTE → abre (o reusa) la transacción de escritura y la deja abierta.</item>
/// <item>Cerrar la sesión con una transacción abierta → rollback.</item>
/// </list>
/// </summary>
public sealed class QuerySession(ConnectionProfile profile, string password) : IAsyncDisposable
{
    private const string TableStatsSql = """
        SELECT TRIM(ts.MON$TABLE_NAME), rs.MON$RECORD_IDX_READS, rs.MON$RECORD_SEQ_READS
        FROM MON$TABLE_STATS ts
        JOIN MON$RECORD_STATS rs ON rs.MON$STAT_ID = ts.MON$RECORD_STAT_ID
        JOIN MON$ATTACHMENTS a ON a.MON$STAT_ID = ts.MON$STAT_ID
        WHERE a.MON$ATTACHMENT_ID = @att
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private FbConnection? _connection;
    private FbConnection? _monitor;
    private FbTransaction? _tx;
    private long _attachmentId;

    public ConnectionProfile Profile { get; } = profile;
    public string? ServerVersion { get; private set; }
    public bool IsOpen => _connection is not null;

    /// <summary>Si la conexión va cifrada (la abre si hace falta); null si el servidor no lo dice. Sin transacción abierta.</summary>
    public async Task<bool?> WireEncryptedAsync(CancellationToken ct = default)
    {
        await OpenAsync(ct);
        if (_tx is not null) return null; // una sola transacción por conexión
        await _gate.WaitAsync(ct);
        try { return await ServerProbe.WireEncryptedAsync(_connection!, ct); }
        finally { _gate.Release(); }
    }
    public bool InTransaction => _tx is not null;

    /// <summary>Espera máxima ante un registro bloqueado; vale para la próxima transacción de escritura que se abra.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Aislamiento de la próxima transacción de escritura (D-84). En producción no se usa: siempre read committed,
    /// aunque la pantalla pida otro (la sesión no confía en eso).
    /// </summary>
    public TransactionIsolation Isolation { get; set; } = TransactionIsolation.ReadCommitted;

    /// <summary>Confirmar cada sentencia que escribe al terminar bien (D-84). En producción se ignora: Commit explícito.</summary>
    public bool AutoCommit { get; set; }

    /// <summary>Número de la transacción de escritura abierta (para saber quién tiene un registro bloqueado).</summary>
    public long? TransactionId { get; private set; }

    /// <summary>Desde cuándo está abierta la transacción de escritura (UTC); null si no hay.</summary>
    public DateTime? TransactionStartedAt { get; private set; }

    /// <summary>Se dispara cuando se abre o se cierra la transacción de escritura.</summary>
    public event Action? TransactionChanged;

    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (_connection is not null) return;
        var connection = FbConnectionFactory.Create(Profile, password);
        try
        {
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            await using var cmd = new FbCommand("SELECT CURRENT_CONNECTION FROM RDB$DATABASE", connection, tx);
            _attachmentId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            await tx.CommitAsync(ct);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        ServerVersion = connection.ServerVersion;
        _connection = connection;
    }

    public async Task<QueryOutcome> ExecuteAsync(
        string sql,
        StatementInfo statement,
        ExecuteOptions options,
        Action<ColumnInfo[]> onColumns,
        ChannelWriter<object[][]> batches,
        CancellationToken ct = default,
        Action<string?, bool>? onPlan = null)
    {
        var outcome = new QueryOutcome();
        var total = Stopwatch.StartNew();
        long cancelRequestedAt = 0;
        using var reg = ct.Register(() => Interlocked.CompareExchange(ref cancelRequestedAt, Stopwatch.GetTimestamp(), 0));
        var startedTxHere = false;

        // Defensa en profundidad: la UI ya evaluó SafetyPolicy, pero la sesión no confía en eso (ni en la clasificación
        // que le pasan: la rehace con el texto que va a mandar).
        var classified = StatementClassifier.Classify(sql);
        if (classified.Modifies && !statement.Modifies) statement = classified;
        if (Profile.ReadOnly && (statement.Modifies || classified.ChangesSequences))
        {
            batches.TryComplete();
            outcome.Error = string.Format(CoreStrings.ProfileIsReadOnly, Profile.Name);
            return outcome;
        }

        await _gate.WaitAsync(ct);
        try
        {
            await OpenAsync(ct);
            var connection = _connection!;

            if (statement.Kind == StatementKind.Transaction)
            {
                if (statement.Keyword == "COMMIT") await CommitCoreAsync(ct);
                else if (statement.Keyword == "ROLLBACK") await RollbackCoreAsync();
                return outcome;
            }

            var before = options.Script ? null : await SnapshotAsync(ct);

            FbTransaction tx;
            var readOnlyTx = false;
            if (_tx is not null) tx = _tx;
            else if (statement.Modifies)
            {
                _tx = tx = await connection.BeginTransactionAsync(
                    Transactions.ReadWrite(LockTimeout, Profile.IsProduction ? TransactionIsolation.ReadCommitted : Isolation), ct);
                TransactionStartedAt = DateTime.UtcNow;
                await using (var id = new FbCommand("SELECT CURRENT_TRANSACTION FROM RDB$DATABASE", connection, tx))
                    TransactionId = Convert.ToInt64(await id.ExecuteScalarAsync(ct));
                startedTxHere = true;
                TransactionChanged?.Invoke();
            }
            else
            {
                tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
                readOnlyTx = true;
            }

            try
            {
                await using var cmd = new FbCommand(sql, connection, tx) { FetchSize = options.FetchSize };
                AddParameters(cmd, options.Parameters);
                var sw = Stopwatch.StartNew();
                await cmd.PrepareAsync(ct);
                outcome.PrepareTime = sw.Elapsed;
                if (!options.Script)
                {
                    (outcome.Plan, outcome.PlanIsExplained) = await GetPlanAsync(cmd, ct);
                    onPlan?.Invoke(outcome.Plan, outcome.PlanIsExplained); // antes de ejecutar: sirve para avisar de una lectura completa
                }

                sw.Restart();
                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    if (reader.FieldCount > 0 && options.Script) await CountRowsOnlyAsync(reader, outcome, options, ct);
                    else if (reader.FieldCount > 0) await StreamRowsAsync(reader, outcome, options, onColumns, batches, sw, ct);
                    else outcome.RecordsAffected = reader.RecordsAffected; // en un SELECT no tiene sentido
                }
                if (readOnlyTx) await tx.CommitAsync(CancellationToken.None);
                startedTxHere = false; // salió bien: la transacción de escritura queda abierta a propósito…
                if (AutoCommit && !Profile.IsProduction && !readOnlyTx && _tx is not null)
                {
                    // …salvo con autocommit: se confirma ya (si el commit falla, se deshace y sale el error).
                    try
                    {
                        await CommitCoreAsync(ct);
                        outcome.AutoCommitted = true;
                    }
                    catch (FbException)
                    {
                        await RollbackCoreAsync();
                        throw;
                    }
                }
            }
            finally
            {
                if (readOnlyTx) await tx.DisposeAsync(); // si no se confirmó, esto hace rollback
            }

            if (before is not null)
            {
                var after = await SnapshotAsync(CancellationToken.None);
                outcome.Reads = Diff(before, after);
                outcome.Fetches = after.Fetches - before.Fetches;
                outcome.PageReads = after.Reads - before.Reads;
                outcome.PageWrites = after.Writes - before.Writes;
            }
        }
        catch (Exception ex) when (IsCancellation(ex, ct))
        {
            outcome.Cancelled = true;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // No la canceló esta pestaña: la cancelaron desde otra conexión (el monitoreo u otra herramienta).
            outcome.Cancelled = true;
            outcome.Error = string.Format(Core.Resources.CoreStrings.CancelledFromOutside, ex.InnerException?.Message ?? ex.Message);
        }
        catch (FbException ex)
        {
            outcome.Error = ex.Message;
            outcome.ErrorCode = ex.ErrorCode;
        }
        finally
        {
            // Si esta ejecución abrió la transacción de escritura y falló o se canceló, no se deja colgada.
            if (startedTxHere) await RollbackCoreAsync();
            outcome.TransactionOpen = InTransaction;
            var requested = Interlocked.Read(ref cancelRequestedAt);
            if (requested != 0) outcome.CancelLatency = Stopwatch.GetElapsedTime(requested);
            outcome.TotalTime = total.Elapsed;
            batches.TryComplete();
            _gate.Release();
        }
        return outcome;
    }

    /// <summary>Los valores de los «?», en orden (FirebirdClient los toma por posición si no tienen nombre).</summary>
    private static void AddParameters(FbCommand cmd, IReadOnlyList<object?>? values)
    {
        if (values is null) return;
        foreach (var value in values) cmd.Parameters.Add(new FbParameter { Value = value ?? DBNull.Value });
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await CommitCoreAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await RollbackCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task CommitCoreAsync(CancellationToken ct)
    {
        if (_tx is null) return;
        await _tx.CommitAsync(ct);
        await _tx.DisposeAsync();
        _tx = null;
        TransactionId = null;
        TransactionStartedAt = null;
        TransactionChanged?.Invoke();
    }

    private async Task RollbackCoreAsync()
    {
        if (_tx is null) return;
        try { await _tx.RollbackAsync(CancellationToken.None); }
        finally
        {
            await _tx.DisposeAsync();
            _tx = null;
            TransactionId = null;
            TransactionStartedAt = null;
            TransactionChanged?.Invoke();
        }
    }

    /// <summary>
    /// Cuenta las filas de un SELECT (<c>SELECT COUNT(*) FROM (…)</c>) con una conexión aparte, de solo lectura: para
    /// mostrar el avance de una exportación. Devuelve null si no se puede contar (CTE, sentencia rara, error).
    /// </summary>
    public static async Task<long?> CountRowsAsync(ConnectionProfile profile, string password, string select, CancellationToken ct = default,
        IReadOnlyList<object?>? parameters = null)
    {
        try
        {
            await using var connection = FbConnectionFactory.Create(profile, password, "conteo");
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            await using var cmd = new FbCommand($"SELECT COUNT(*) FROM ({select.Trim().TrimEnd(';')})", connection, tx);
            AddParameters(cmd, parameters);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            await tx.CommitAsync(ct);
            return count;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task StreamRowsAsync(FbDataReader reader, QueryOutcome outcome, ExecuteOptions options,
        Action<ColumnInfo[]> onColumns, ChannelWriter<object[][]> batches, Stopwatch sinceExecute, CancellationToken ct)
    {
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => new ColumnInfo(i, reader.GetName(i), reader.GetDataTypeName(i)))
            .ToArray();
        outcome.Columns = columns;
        onColumns(columns);

        var batch = new List<object[]>(options.BatchSize);
        var flush = Stopwatch.StartNew();
        try
        {
            while (outcome.RowsFetched < options.MaxRows && await reader.ReadAsync(ct))
            {
                if (outcome.RowsFetched == 0) outcome.FirstRowTime = sinceExecute.Elapsed;
                var values = new object[columns.Length];
                reader.GetValues(values);
                batch.Add(values);
                outcome.RowsFetched++;

                // Lotes por tamaño o cada 100 ms, así las primeras filas aparecen enseguida.
                if (batch.Count >= options.BatchSize || flush.ElapsedMilliseconds >= 100)
                {
                    await batches.WriteAsync([.. batch], ct);
                    batch.Clear();
                    flush.Restart();
                }
            }
            outcome.LimitReached = outcome.RowsFetched >= options.MaxRows;
        }
        finally
        {
            if (batch.Count > 0) batches.TryWrite([.. batch]);
        }
    }

    /// <summary>Modo script: un SELECT se ejecuta y se cuentan sus filas (hasta el límite), sin armarlas para la grilla.</summary>
    private static async Task CountRowsOnlyAsync(FbDataReader reader, QueryOutcome outcome, ExecuteOptions options, CancellationToken ct)
    {
        outcome.Columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => new ColumnInfo(i, reader.GetName(i), reader.GetDataTypeName(i)))
            .ToArray();
        while (outcome.RowsFetched < options.MaxRows && await reader.ReadAsync(ct)) outcome.RowsFetched++;
        outcome.LimitReached = outcome.RowsFetched >= options.MaxRows;
    }

    private static bool IsCancellation(Exception ex, CancellationToken ct) =>
        ct.IsCancellationRequested && (ex is OperationCanceledException
            || ex is FbException { ErrorCode: 335544794 } // isc_cancelled
            || ex.InnerException is not null && IsCancellation(ex.InnerException, ct));

    /// <summary>
    /// El plan de una sentencia sin ejecutarla: solo se prepara (no trae filas ni cambia nada). En la transacción abierta
    /// si hay una; si no, en una de solo lectura que se descarta.
    /// </summary>
    public async Task<(string? Plan, bool Explained)> PlanAsync(string sql, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await OpenAsync(ct);
            var own = _tx is null;
            var tx = _tx ?? await _connection!.BeginTransactionAsync(Transactions.ReadOnly, ct);
            try
            {
                await using var cmd = new FbCommand(sql, _connection, tx);
                await cmd.PrepareAsync(ct);
                return await GetPlanAsync(cmd, ct);
            }
            finally
            {
                if (own) await tx.DisposeAsync(); // sin confirmar: se deshace (no había nada)
            }
        }
        finally { _gate.Release(); }
    }

    private static async Task<(string?, bool)> GetPlanAsync(FbCommand cmd, CancellationToken ct)
    {
        try
        {
            var explained = await cmd.GetCommandExplainedPlanAsync(ct);
            if (!string.IsNullOrWhiteSpace(explained)) return (explained.Trim(), true);
        }
        catch (FbException) { /* servidor anterior a 3.0 */ }

        try
        {
            var classic = await cmd.GetCommandPlanAsync(ct);
            return (string.IsNullOrWhiteSpace(classic) ? null : classic.Trim(), false);
        }
        catch (FbException) { return (null, false); }
    }

    private sealed record Counters(Dictionary<string, long> Seq, Dictionary<string, long> Idx, long Fetches, long Reads, long Writes);

    /// <summary>
    /// FbDatabaseInfo da solo totales de la conexión; el detalle por tabla sale de MON$TABLE_STATS, leído desde
    /// una conexión lateral (así no choca con la transacción abierta del editor).
    /// </summary>
    private async Task<Counters> SnapshotAsync(CancellationToken ct)
    {
        var seq = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var idx = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_monitor is null)
            {
                var monitor = FbConnectionFactory.Create(Profile, password, "monitor");
                await monitor.OpenAsync(ct);
                _monitor = monitor;
            }
            await using var tx = await _monitor.BeginTransactionAsync(Transactions.Snapshot, ct);
            await using var cmd = new FbCommand(TableStatsSql, _monitor, tx);
            cmd.Parameters.Add("@att", _attachmentId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                idx[reader.GetString(0)] = reader.GetInt64(1);
                seq[reader.GetString(0)] = reader.GetInt64(2);
            }
            await tx.CommitAsync(ct);
        }
        catch (FbException) { /* sin MON$ (FB < 3) o sin permiso: queda sin detalle por tabla */ }

        var info = new FbDatabaseInfo(_connection!);
        return new Counters(seq, idx,
            await info.GetFetchesAsync(ct),
            await info.GetReadsAsync(ct),
            await info.GetWritesAsync(ct));
    }

    private static List<TableReads> Diff(Counters before, Counters after) =>
        after.Seq.Keys.Union(after.Idx.Keys)
            .Where(t => !t.StartsWith("RDB$", StringComparison.Ordinal) && !t.StartsWith("MON$", StringComparison.Ordinal))
            .Select(t => new TableReads(t,
                after.Idx.GetValueOrDefault(t) - before.Idx.GetValueOrDefault(t),
                after.Seq.GetValueOrDefault(t) - before.Seq.GetValueOrDefault(t)))
            .Where(r => r.Indexed != 0 || r.NonIndexed != 0)
            .OrderByDescending(r => r.Indexed + r.NonIndexed)
            .ToList();

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            // Si la conexión ya no existe (la cortaron desde el monitoreo, se cayó el servidor), cerrar igual.
            try { await RollbackCoreAsync(); } catch (Exception) { _tx = null; TransactionId = null; TransactionStartedAt = null; }
            try { if (_connection is not null) await _connection.DisposeAsync(); } catch (Exception) { }
            try { if (_monitor is not null) await _monitor.DisposeAsync(); } catch (Exception) { }
            _connection = _monitor = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
