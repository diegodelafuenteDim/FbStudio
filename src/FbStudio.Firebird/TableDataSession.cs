using FbStudio.Core.Data;
using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

public sealed record TablePage(ColumnInfo[] Columns, IReadOnlyList<object[]> Rows, bool HasMore);

/// <summary>Resultado de «Aplicar»: o se guardó todo, o no se guardó nada (y por qué).</summary>
/// <param name="Failed">La sentencia que falló (no encontró la fila, o el servidor dio error).</param>
/// <param name="RowsAffected">Filas que tocó la que falló: 0 = la fila cambió o ya no está; más de 1 = la clave no es única.</param>
public sealed record ApplyResult(bool Ok, int Applied, ChangeStatement? Failed = null, int RowsAffected = 1, string? Error = null)
{
    /// <summary>Lo que devolvió cada INSERT (por la clave interna de la fila nueva): la fila como quedó en la base.</summary>
    public IReadOnlyDictionary<string, object[]> Inserted { get; init; } = new Dictionary<string, object[]>();
}

/// <summary>
/// Conexión propia de la pestaña «Datos» (F-3). Cada página se lee en una transacción corta de solo lectura que se
/// confirma enseguida: hojear una tabla grande no frena la OAT ni bloquea nada. Los cambios se aplican todos juntos
/// en una transacción de escritura corta (con la espera de bloqueos de las opciones): o entran todos o ninguno.
/// </summary>
public sealed class TableDataSession(ConnectionProfile profile, string password) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private FbConnection? _connection;
    private bool _disposed;

    public ConnectionProfile Profile { get; } = profile;
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Trae <paramref name="count"/> filas desde <paramref name="offset"/> (pide una de más para saber si hay más).
    /// Con <paramref name="after"/> (la última fila traída) pide las que siguen por la clave, si el orden lo permite.
    /// </summary>
    public Task<TablePage> ReadPageAsync(TableQuery query, int offset, int count, IReadOnlyList<object?>? after = null, CancellationToken ct = default) =>
        WithConnectionAsync(async connection =>
        {
            var command = query.Page(offset, count + 1, after);
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            await using var cmd = Command(command, connection, tx);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new ColumnInfo(i, reader.GetName(i), reader.GetDataTypeName(i))).ToArray();
            var rows = new List<object[]>(count);
            var more = false;
            while (await reader.ReadAsync(ct))
            {
                if (rows.Count == count) { more = true; break; }
                var values = new object[columns.Length];
                reader.GetValues(values);
                rows.Add(values);
            }
            await reader.CloseAsync();
            await tx.CommitAsync(ct);
            return new TablePage(columns, rows, more);
        }, ct);

    /// <summary>Los valores de una columna y cuántas filas tiene cada uno (ver <see cref="TableQuery.Distinct"/>).</summary>
    public Task<List<ValueCount>> DistinctAsync(TableQuery query, int column, int limit, CancellationToken ct = default) =>
        WithConnectionAsync(async connection =>
        {
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            await using var cmd = Command(query.Distinct(column, limit), connection, tx);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var values = new List<ValueCount>();
            while (await reader.ReadAsync(ct)) values.Add(new ValueCount(reader.IsDBNull(0) ? null : reader.GetValue(0), Convert.ToInt64(reader.GetValue(1))));
            await reader.CloseAsync();
            await tx.CommitAsync(ct);
            return values;
        }, ct);

    public Task<long> CountAsync(TableQuery query, CancellationToken ct = default) =>
        WithConnectionAsync(async connection =>
        {
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
            await using var cmd = Command(query.Count(), connection, tx);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            await tx.CommitAsync(ct);
            return count;
        }, ct);

    /// <summary>
    /// Ejecuta los cambios en una sola transacción. Cada sentencia tiene que tocar exactamente una fila; si no (otro la
    /// cambió o la borró, o la clave no es única) o si el servidor da error, se deshace todo.
    /// </summary>
    public Task<ApplyResult> ApplyAsync(IReadOnlyList<ChangeStatement> statements, CancellationToken ct = default)
    {
        if (Profile.ReadOnly) throw new InvalidOperationException(string.Format(Core.Resources.CoreStrings.ProfileIsReadOnly, Profile.Name));
        return WithConnectionAsync(async connection =>
        {
            await using var tx = await connection.BeginTransactionAsync(Transactions.ReadWrite(LockTimeout), ct);
            var applied = 0;
            var inserted = new Dictionary<string, object[]>(StringComparer.Ordinal);
            foreach (var statement in statements)
            {
                int affected;
                try
                {
                    await using var cmd = Command(statement.Command, connection, tx);
                    if (statement.IsInsert)
                    {
                        // Un INSERT entra o da error; con RETURNING trae la fila como quedó (clave de un trigger, defaults).
                        affected = 1;
                        await using var reader = await cmd.ExecuteReaderAsync(ct);
                        if (reader.FieldCount > 0 && await reader.ReadAsync(ct))
                        {
                            var values = new object[reader.FieldCount];
                            reader.GetValues(values);
                            inserted[statement.RowKey] = values;
                        }
                    }
                    else
                    {
                        affected = await cmd.ExecuteNonQueryAsync(ct);
                    }
                }
                catch (FbException ex)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    return new ApplyResult(false, 0, statement, 0, ex.Message);
                }
                if (affected != 1)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    return new ApplyResult(false, 0, statement, affected);
                }
                applied++;
            }
            await tx.CommitAsync(ct);
            return new ApplyResult(true, applied) { Inserted = inserted };
        }, ct);
    }

    private static FbCommand Command(SqlCommandText command, FbConnection connection, FbTransaction tx)
    {
        var cmd = new FbCommand(command.Sql, connection, tx);
        foreach (var p in command.Parameters) cmd.Parameters.Add(p.Name, p.Value ?? DBNull.Value);
        return cmd;
    }

    private async Task<T> WithConnectionAsync<T>(Func<FbConnection, Task<T>> work, CancellationToken ct)
    {
        // Cerrar la pestaña cancela lo que esté en curso (una lectura, el conteo).
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closing.Token);
        ct = linked.Token;
        await _gate.WaitAsync(ct);
        try
        {
            // Una lectura que estaba esperando turno cuando se cerró la pestaña no abre otra conexión: quedaría
            // colgada con la tabla «en uso» (y un DROP o un ALTER de esa tabla daría «object in use»).
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is null)
            {
                var c = FbConnectionFactory.Create(Profile, password, "datos");
                await c.OpenAsync(ct);
                _connection = c;
            }
            return await work(_connection);
        }
        catch (FbException) when (_connection is { State: not System.Data.ConnectionState.Open })
        {
            // Conexión caída (servidor reiniciado, red): la próxima vez se abre otra.
            await _connection.DisposeAsync();
            _connection = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Cancela lo que esté en curso, espera a que suelte la conexión y la cierra. Después no abre otra.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _closing.CancelAsync();
        await _gate.WaitAsync();
        try
        {
            if (_connection is not null) await _connection.DisposeAsync();
            _connection = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
