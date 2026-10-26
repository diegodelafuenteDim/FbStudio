using FbStudio.Core.Profiles;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Firebird;

/// <summary>Las filas leídas para comparar los datos de una tabla; <paramref name="TooMany"/>: había más que el tope.</summary>
public sealed record TableRows(IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<object?[]> Rows, bool TooMany);

/// <summary>
/// Lee una tabla entera para comparar sus datos con otra base: conexión propia, una transacción de solo lectura que se
/// confirma al terminar y las filas ordenadas por la clave. Con más filas que el tope no sigue (comparar datos es para
/// tablas chicas: configuración, tipos, nomencladores).
/// </summary>
public static class TableDataReader
{
    public static async Task<TableRows> ReadAsync(ConnectionProfile profile, string password, string table, IReadOnlyList<string> columns,
        IReadOnlyList<string> key, int limit, CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "comparar datos");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
        var sql = $"SELECT FIRST {limit + 1} {string.Join(", ", columns.Select(SqlIdentifier.Quote))} FROM {SqlIdentifier.Quote(table)} "
                  + $"ORDER BY {string.Join(", ", key.Select(SqlIdentifier.Quote))}";
        await using var cmd = new FirebirdSql.Data.FirebirdClient.FbCommand(sql, connection, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var info = Enumerable.Range(0, reader.FieldCount).Select(i => new ColumnInfo(i, reader.GetName(i), reader.GetDataTypeName(i))).ToArray();
        var rows = new List<object?[]>();
        var tooMany = false;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count == limit)
            {
                tooMany = true;
                break;
            }
            var values = new object?[info.Length];
            for (var i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(values);
        }
        await reader.CloseAsync();
        await tx.CommitAsync(ct);
        return new TableRows(info, rows, tooMany);
    }
}
