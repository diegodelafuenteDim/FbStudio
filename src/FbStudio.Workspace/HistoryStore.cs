using Microsoft.Data.Sqlite;

namespace FbStudio.Workspace;

public sealed record HistoryEntry(
    long Id,
    Guid ProfileId,
    string ProfileName,
    DateTimeOffset ExecutedAt,
    string Kind,
    string Sql,
    long DurationMs,
    long Rows,
    int RecordsAffected,
    bool Cancelled,
    string? Error,
    string? Plan)
{
    public string FirstLine => Sql.Split('\n', 2)[0].Trim();
    public bool Failed => Error is not null;
}

/// <summary>
/// Historial de sentencias en SQLite (%APPDATA%\FbStudio\history.db).
/// El SQL y los errores van cifrados (pueden tener datos de pacientes: Ley 25.326); el plan y los números, no.
/// Se purga por antigüedad al abrir.
/// </summary>
public sealed class HistoryStore
{
    private readonly string _connectionString;
    private readonly IDataProtector _protector;

    public HistoryStore(string dbPath, IDataProtector protector, TimeSpan retention)
    {
        PrivateFiles.EnsureFolder(Path.GetDirectoryName(dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
        _protector = protector;
        Execute("""
            CREATE TABLE IF NOT EXISTS history (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              profile_id TEXT NOT NULL,
              profile_name TEXT NOT NULL,
              executed_at TEXT NOT NULL,
              kind TEXT NOT NULL,
              sql_protected BLOB NOT NULL,
              duration_ms INTEGER NOT NULL,
              rows INTEGER NOT NULL,
              affected INTEGER NOT NULL,
              cancelled INTEGER NOT NULL,
              error_protected BLOB,
              plan TEXT
            );
            CREATE INDEX IF NOT EXISTS history_executed_at ON history (executed_at);
            """);
        Purge(DateTimeOffset.UtcNow - retention);
    }

    public void Add(HistoryEntry e)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history (profile_id, profile_name, executed_at, kind, sql_protected, duration_ms, rows, affected, cancelled, error_protected, plan)
            VALUES ($pid, $pname, $at, $kind, $sql, $ms, $rows, $aff, $cancel, $err, $plan)
            """;
        cmd.Parameters.AddWithValue("$pid", e.ProfileId.ToString("N"));
        cmd.Parameters.AddWithValue("$pname", e.ProfileName);
        cmd.Parameters.AddWithValue("$at", e.ExecutedAt.UtcDateTime.ToString("O"));
        cmd.Parameters.AddWithValue("$kind", e.Kind);
        cmd.Parameters.AddWithValue("$sql", _protector.Protect(e.Sql));
        cmd.Parameters.AddWithValue("$ms", e.DurationMs);
        cmd.Parameters.AddWithValue("$rows", e.Rows);
        cmd.Parameters.AddWithValue("$aff", e.RecordsAffected);
        cmd.Parameters.AddWithValue("$cancel", e.Cancelled ? 1 : 0);
        cmd.Parameters.AddWithValue("$err", e.Error is null ? DBNull.Value : _protector.Protect(e.Error));
        cmd.Parameters.AddWithValue("$plan", (object?)e.Plan ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Las más recientes primero. El filtro de texto se aplica después de descifrar (el SQL no se puede buscar en la base).</summary>
    public IReadOnlyList<HistoryEntry> Recent(Guid? profileId = null, string? filter = null, int limit = 500, int scanLimit = 5_000)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, profile_id, profile_name, executed_at, kind, sql_protected, duration_ms, rows, affected, cancelled, error_protected, plan
            FROM history {(profileId is null ? "" : "WHERE profile_id = $pid")}
            ORDER BY id DESC LIMIT $scan
            """;
        if (profileId is not null) cmd.Parameters.AddWithValue("$pid", profileId.Value.ToString("N"));
        cmd.Parameters.AddWithValue("$scan", scanLimit);

        var result = new List<HistoryEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read() && result.Count < limit)
        {
            var sql = _protector.Unprotect((byte[])r["sql_protected"]);
            var error = r.IsDBNull(10) ? null : _protector.Unprotect((byte[])r["error_protected"]);
            if (!string.IsNullOrWhiteSpace(filter)
                && !sql.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && error?.Contains(filter, StringComparison.OrdinalIgnoreCase) != true) continue;
            result.Add(new HistoryEntry(
                r.GetInt64(0), Guid.Parse(r.GetString(1)), r.GetString(2), DateTimeOffset.Parse(r.GetString(3)),
                r.GetString(4), sql, r.GetInt64(6), r.GetInt64(7), r.GetInt32(8), r.GetInt32(9) == 1, error,
                r.IsDBNull(11) ? null : r.GetString(11)));
        }
        return result;
    }

    public int Purge(DateTimeOffset olderThan)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM history WHERE executed_at < $limit";
        cmd.Parameters.AddWithValue("$limit", olderThan.UtcDateTime.ToString("O"));
        return cmd.ExecuteNonQuery();
    }

    public void Clear(Guid? profileId = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = profileId is null ? "DELETE FROM history" : "DELETE FROM history WHERE profile_id = $pid";
        if (profileId is not null) cmd.Parameters.AddWithValue("$pid", profileId.Value.ToString("N"));
        cmd.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        // Lo que se borra (vencido o con «Borrar historial») se pisa con ceros: no queda en las páginas libres del archivo.
        using (var pragma = c.CreateCommand())
        {
            pragma.CommandText = "PRAGMA secure_delete = ON";
            pragma.ExecuteNonQuery();
        }
        return c;
    }
}
