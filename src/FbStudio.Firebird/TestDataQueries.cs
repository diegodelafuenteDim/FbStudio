using FbStudio.Core.Profiles;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird;

/// <summary>Lo que hace falta leer para generar datos de prueba: dónde sigue una clave y qué valores hay en una tabla referenciada.</summary>
public static class TestDataQueries
{
    /// <summary>El mayor valor de una columna numérica (0 si la tabla está vacía).</summary>
    public static async Task<long> MaxAsync(ConnectionProfile profile, string password, string table, string column, CancellationToken ct = default)
    {
        var value = await ScalarAsync(profile, password, $"SELECT MAX({SqlIdentifier.Quote(column)}) FROM {SqlIdentifier.Quote(table)}", ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    /// <summary>Hasta <paramref name="limit"/> valores distintos de una columna (los de la tabla referenciada por una FK).</summary>
    public static async Task<IReadOnlyList<object>> ValuesAsync(ConnectionProfile profile, string password, string table, string column, int limit = 1000,
        CancellationToken ct = default)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "datos de prueba");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
        var col = SqlIdentifier.Quote(column);
        await using var cmd = new FbCommand($"SELECT FIRST {limit} DISTINCT {col} FROM {SqlIdentifier.Quote(table)} WHERE {col} IS NOT NULL", connection, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var values = new List<object>();
        while (await reader.ReadAsync(ct)) values.Add(reader.GetValue(0));
        return values;
    }

    private static async Task<object?> ScalarAsync(ConnectionProfile profile, string password, string sql, CancellationToken ct)
    {
        await using var connection = FbConnectionFactory.Create(profile, password, "datos de prueba");
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(Transactions.ReadOnly, ct);
        await using var cmd = new FbCommand(sql, connection, tx);
        return await cmd.ExecuteScalarAsync(ct);
    }
}
