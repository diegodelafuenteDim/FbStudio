using System.Diagnostics;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

public static partial class DatabaseServices
{
    /// <summary>
    /// Arma una base nueva en <paramref name="targetDatabase"/> (ruta del servidor) con toda la estructura del origen:
    /// mismo charset, tamaño de página y dialecto; generadores e identity con su valor actual si se pide.
    /// <list type="number">
    /// <item>El script de la base, parte estructura (tablas, claves, índices, vistas, procedimientos…).</item>
    /// <item>Los datos de las tablas elegidas (y las que referencian sus FK), leídos en una sola foto del origen.</item>
    /// <item>La parte diferida: FK, CHECK, triggers y permisos.</item>
    /// </list>
    /// Cada sentencia se confirma sola (como isql con AUTODDL): una que falla se anota y se sigue. Nunca pisa un
    /// archivo que ya existe; si se cancela o falla, la base queda a medio armar y se avisa (no se borra sola).
    /// </summary>
    public static async Task<EmptyCopyResult> CreateEmptyCopyAsync(ConnectionProfile profile, string password, string targetDatabase,
        EmptyCopyOptions options, Action<string>? onLine = null, CancellationToken ct = default)
    {
        if (profile.ReadOnly) throw new InvalidOperationException(string.Format(CoreStrings.ProfileIsReadOnly, profile.Name));
        var clock = Stopwatch.StartNew();
        var info = await FileInfoAsync(profile, password, ct);
        if (await IsCurrentDatabaseAsync(profile, password, targetDatabase, ct)) throw new InvalidOperationException(CoreStrings.ServicesTargetIsCurrent);

        onLine?.Invoke(CoreStrings.EmptyCopyReading);
        var scriptOptions = new SchemaScriptOptions { GeneratorValues = options.GeneratorValues };
        DatabaseSchema schema;
        await using (var explorer = new MetadataExplorer(profile, password))
            schema = await explorer.ReadSchemaAsync(scriptOptions, ct: ct);

        var target = profile with { Database = targetDatabase };
        var create = new FbConnectionStringBuilder(FbConnectionFactory.ConnectionString(target, password, "base vacía"))
        {
            Charset = schema.Charset ?? "NONE",
            Dialect = info.Dialect,
        };
        onLine?.Invoke(string.Format(CoreStrings.EmptyCopyCreating, targetDatabase, info.PageSize, schema.Charset ?? "NONE"));
        await FbConnection.CreateDatabaseAsync(create.ConnectionString, info.PageSize, forcedWrites: true, overwrite: false, ct);

        var errors = new List<ScriptError>();
        var copied = new List<TableCopyResult>();
        await using (var connection = FbConnectionFactory.Create(target, password, "base vacía"))
        {
            await connection.OpenAsync(ct);
            await RunScriptAsync(connection, SchemaScriptWriter.Write(schema, scriptOptions, parts: SchemaScriptParts.Structure), errors, onLine, ct);

            var tables = TableSelection.WithReferenced(schema, options.Tables ?? []);
            if (tables.Count > 0)
            {
                var added = tables.Except(options.Tables ?? [], StringComparer.Ordinal).ToList();
                if (added.Count > 0) onLine?.Invoke(string.Format(CoreStrings.EmptyCopyAddedTables, string.Join(", ", added)));
                await using var source = FbConnectionFactory.Create(profile, password, "base vacía (lectura)");
                await source.OpenAsync(ct);
                // Una sola foto para todas las tablas: las filas de una tabla y las que referencia son del mismo momento.
                await using var snapshot = await source.BeginTransactionAsync(Transactions.Snapshot, ct);
                foreach (var name in tables)
                {
                    var table = schema.Tables.First(t => t.Name == name);
                    copied.Add(await CopyTableAsync(source, snapshot, connection, table, onLine, ct));
                }
            }

            await RunScriptAsync(connection, SchemaScriptWriter.Write(schema, scriptOptions, parts: SchemaScriptParts.Deferred), errors, onLine, ct);
        }
        return new EmptyCopyResult(targetDatabase, errors, copied, clock.Elapsed);
    }

    /// <summary>Corre un script sentencia por sentencia, cada una con su COMMIT; las que fallan se anotan y se sigue.</summary>
    private static async Task RunScriptAsync(FbConnection connection, string script, List<ScriptError> errors, Action<string>? onLine, CancellationToken ct)
    {
        var statements = SqlScriptSplitter.Split(script)
            .Where(s => StatementClassifier.Classify(s.Text).Kind is not (StatementKind.Empty or StatementKind.Transaction)).ToList();
        for (var i = 0; i < statements.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0 && i % 200 == 0) onLine?.Invoke(string.Format(CoreStrings.EmptyCopyProgress, i, statements.Count));
            await using var tx = await connection.BeginTransactionAsync(ct);
            try
            {
                await using (var cmd = new FbCommand(statements[i].Text, connection, tx)) await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (FbException ex)
            {
                errors.Add(new ScriptError(statements[i].Text, ex.Message));
                onLine?.Invoke(string.Format(CoreStrings.EmptyCopyStatementFailed, FirstLine(ex.Message), Abbreviate(statements[i].Text)));
            }
        }
    }

    /// <summary>
    /// Copia las filas de una tabla (sin las columnas calculadas) en una sola transacción: si algo falla, la tabla queda
    /// vacía y se informa. Sin triggers ni FK todavía, no importa el orden.
    /// </summary>
    private static async Task<TableCopyResult> CopyTableAsync(FbConnection source, FbTransaction snapshot, FbConnection target, TableDefinition table,
        Action<string>? onLine, CancellationToken ct)
    {
        var columns = table.Columns.Where(c => c.Computed is null).Select(c => SqlIdentifier.Quote(c.Name)).ToList();
        var name = SqlIdentifier.Quote(table.Name);
        long rows = 0;
        await using var tx = await target.BeginTransactionAsync(ct);
        try
        {
            await using var select = new FbCommand($"SELECT {string.Join(", ", columns)} FROM {name}", source, snapshot) { FetchSize = 1000 };
            await using var insert = new FbCommand(
                $"INSERT INTO {name} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select((_, i) => "@p" + i))})", target, tx);
            for (var i = 0; i < columns.Count; i++) insert.Parameters.Add("@p" + i, DBNull.Value);
            await insert.PrepareAsync(ct);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                for (var i = 0; i < columns.Count; i++) insert.Parameters[i].Value = reader.GetValue(i);
                await insert.ExecuteNonQueryAsync(ct);
                if (++rows % 10_000 == 0) onLine?.Invoke(string.Format(CoreStrings.EmptyCopyTableProgress, table.Name, rows));
            }
            await tx.CommitAsync(ct);
            onLine?.Invoke(string.Format(CoreStrings.EmptyCopyTableDone, table.Name, rows));
            return new TableCopyResult(table.Name, rows);
        }
        catch (FbException ex)
        {
            onLine?.Invoke(string.Format(CoreStrings.EmptyCopyTableFailed, table.Name, FirstLine(ex.Message)));
            return new TableCopyResult(table.Name, 0, ex.Message);
        }
    }

    private static string Abbreviate(string sql)
    {
        var lines = sql.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("/*", StringComparison.Ordinal)).ToList();
        var first = lines.FirstOrDefault() ?? "";
        return first.Length > 120 ? first[..120] + "…" : first;
    }
}
