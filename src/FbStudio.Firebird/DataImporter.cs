using System.Diagnostics;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <param name="StopOnError">Ante la primera fila que falla, se deshace todo. Si no, esa fila se saltea y se sigue.</param>
/// <param name="EmptyTableFirst">Borra las filas de la tabla antes de importar, en la misma transacción (si algo falla, vuelven).</param>
/// <param name="MatchingColumns">
/// Actualizar o agregar: la fila que ya está con esa clave se actualiza y la que no, se agrega (UPDATE OR INSERT …
/// MATCHING). null o vacía: siempre se agrega (INSERT).
/// </param>
/// <param name="CreateTableSql">
/// Importar a una tabla nueva: el CREATE TABLE, que se confirma antes de importar. Si la importación no se guarda (un
/// error, cancelar), la tabla se vuelve a borrar: no queda una tabla vacía a medias.
/// </param>
public sealed record ImportOptions(bool StopOnError = true, bool EmptyTableFirst = false, IReadOnlyList<string>? MatchingColumns = null,
    string? CreateTableSql = null);

/// <summary>Una fila que no entró: el renglón del archivo y lo que dijo Firebird.</summary>
public sealed record ImportRowError(long Row, string Message);

/// <param name="Inserted">Filas escritas: agregadas o, con <see cref="ImportOptions.MatchingColumns"/>, actualizadas o agregadas.</param>
/// <param name="Committed">Si los cambios quedaron guardados (no se deshicieron por un error o por cancelar).</param>
/// <param name="TableCreated">Se creó la tabla nueva y quedó, con lo importado.</param>
public sealed record ImportResult(long Inserted, long Deleted, IReadOnlyList<ImportRowError> Errors, long FailedRows, bool Committed, TimeSpan Elapsed,
    bool TableCreated = false);

/// <summary>
/// Inserta filas en una tabla (F-26): un INSERT (o UPDATE OR INSERT) preparado y los valores como parámetros, todo en una
/// transacción. Si <see cref="ImportOptions.StopOnError"/>, la primera fila que falla deshace todo (no queda una
/// importación a medias); si no, esa fila se saltea y el resto se guarda al final. Cancelar deshace todo.
/// </summary>
public static class DataImporter
{
    /// <summary>Errores que se guardan con su mensaje (después solo se cuentan).</summary>
    public const int MaxListedErrors = 1000;

    /// <param name="rows">Los valores de cada fila en el orden de <paramref name="columns"/>, con el renglón del archivo.</param>
    public static async Task<ImportResult> ImportAsync(ConnectionProfile profile, string password, string table, IReadOnlyList<string> columns,
        IEnumerable<(long Row, object?[] Values)> rows, ImportOptions options, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        if (profile.ReadOnly) throw new InvalidOperationException(string.Format(CoreStrings.ProfileIsReadOnly, profile.Name));
        if (columns.Count == 0) throw new InvalidOperationException(CoreStrings.ImportNoColumns);
        var clock = Stopwatch.StartNew();
        await using var connection = FbConnectionFactory.Create(profile, password, "importar");
        await connection.OpenAsync(ct);
        if (options.CreateTableSql is not { } ddl) return await ImportRowsAsync(connection, table, columns, rows, options, progress, clock, ct);

        // La tabla nueva tiene que existir (confirmada) antes de escribir en ella; si lo importado no queda, se borra.
        await ExecuteCommittedAsync(connection, ddl, ct);
        ImportResult result;
        try
        {
            result = await ImportRowsAsync(connection, table, columns, rows, options, progress, clock, ct);
        }
        catch (Exception)
        {
            await DropQuietlyAsync(connection, table);
            throw;
        }
        if (!result.Committed) await DropQuietlyAsync(connection, table);
        return result with { TableCreated = result.Committed };
    }

    private static async Task ExecuteCommittedAsync(FbConnection connection, string sql, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadWrite(TimeSpan.FromSeconds(10)), ct);
        await using (var command = new FbCommand(sql, connection, tx)) await command.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task DropQuietlyAsync(FbConnection connection, string table)
    {
        try { await ExecuteCommittedAsync(connection, $"DROP TABLE {SqlIdentifier.Quote(table)}", CancellationToken.None); }
        catch (FbException) { /* quedó vacía: se puede borrar a mano */ }
    }

    private static async Task<ImportResult> ImportRowsAsync(FbConnection connection, string table, IReadOnlyList<string> columns,
        IEnumerable<(long Row, object?[] Values)> rows, ImportOptions options, IProgress<long>? progress, Stopwatch clock, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadWrite(TimeSpan.FromSeconds(10)), ct);
        long inserted = 0, deleted = 0, failed = 0;
        var errors = new List<ImportRowError>();
        var committed = false;
        try
        {
            if (options.EmptyTableFirst)
            {
                await using var delete = new FbCommand($"DELETE FROM {SqlIdentifier.Quote(table)}", connection, tx);
                deleted = await delete.ExecuteNonQueryAsync(ct);
            }
            var upsert = options.MatchingColumns is { Count: > 0 } keys;
            var sql = (upsert ? "UPDATE OR INSERT INTO " : "INSERT INTO ") +
                      $"{SqlIdentifier.Quote(table)} ({string.Join(", ", columns.Select(SqlIdentifier.Quote))}) " +
                      $"VALUES ({string.Join(", ", columns.Select(_ => "?"))})" +
                      (upsert ? $" MATCHING ({string.Join(", ", options.MatchingColumns!.Select(SqlIdentifier.Quote))})" : "");
            await using var insert = new FbCommand(sql, connection, tx);
            foreach (var _ in columns) insert.Parameters.Add(new FbParameter());
            await insert.PrepareAsync(ct);
            foreach (var (row, values) in rows)
            {
                ct.ThrowIfCancellationRequested();
                for (var i = 0; i < columns.Count; i++) insert.Parameters[i].Value = (i < values.Length ? values[i] : null) ?? DBNull.Value;
                try
                {
                    await insert.ExecuteNonQueryAsync(ct);
                    inserted++;
                }
                // FirebirdClient convierte del lado del cliente (un texto que no es fecha en una columna DATE falla
                // antes de llegar al servidor): también es una fila con error.
                catch (Exception ex) when (ex is FbException or FormatException or InvalidCastException or OverflowException or ArgumentException)
                {
                    failed++;
                    if (errors.Count < MaxListedErrors) errors.Add(new ImportRowError(row, ex.Message.Split('\n', 2)[0].Trim()));
                    if (options.StopOnError) break;
                }
                if ((inserted + failed) % 500 == 0) progress?.Report(inserted + failed);
            }
            if (options.StopOnError && failed > 0)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return new ImportResult(0, 0, errors, failed, false, clock.Elapsed);
            }
            await tx.CommitAsync(ct);
            committed = true;
            progress?.Report(inserted + failed);
            return new ImportResult(inserted, deleted, errors, failed, true, clock.Elapsed);
        }
        finally
        {
            if (!committed && tx.Connection is not null)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch (Exception) { /* ya estaba deshecha */ }
            }
        }
    }
}
