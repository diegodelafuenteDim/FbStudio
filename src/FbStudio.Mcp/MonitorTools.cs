using System.Globalization;
using System.Text;
using FbStudio.Core.Metadata;
using FbStudio.Core.Monitoring;
using FbStudio.Core.Sql;
using FbStudio.Core.Statistics;
using FbStudio.Firebird;

namespace FbStudio.Mcp;

/// <summary>
/// Las herramientas de rendimiento y monitoreo de la fase 2 (D-118): una foto de MON$ y las estadísticas de la base.
/// El SQL de otros programas sale con los valores cambiados por «?»; quién tiene cada conexión sale o no según la
/// opción de FbStudio. Nada corta conexiones ni cancela sentencias.
/// </summary>
/// <param name="showClients">Mostrar usuario, equipo y programa de cada conexión (opción de FbStudio).</param>
public sealed class MonitorTools(DatabaseCatalog catalog, bool showClients)
{
    /// <summary>Cuántas transacciones y sentencias muestra la foto del monitoreo.</summary>
    public const int MaxRows = 25;

    /// <summary>Hasta cuántas tablas acepta la estadística completa (cada una recorre todas sus páginas).</summary>
    public const int MaxTables = 20;

    /// <summary>Largo máximo del SQL de cada sentencia en curso.</summary>
    public const int MaxSqlLength = 1500;

    public Task<string> GetMonitorSnapshotAsync(string database, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var snapshot = await s.Explorer.MonitorSnapshotAsync(MaxRows, ct);
            var m = snapshot.Summary;
            var sb = new StringBuilder($"{s.Profile.Name} · environment: {DatabaseList.Environment(s.Profile.Environment)}\n");
            sb.Append(N($"Transactions: next {m.NextTransaction:N0}, OIT {m.OldestInteresting:N0}, OAT {m.OldestActive:N0}, OST {m.OldestSnapshot:N0}"));
            sb.Append(N($" · gap since the OAT {m.Gap:N0}"));
            if (snapshot.OatIsOwn) sb.Append(" · no transaction is older than this snapshot: nothing holds back garbage collection");
            else if (m.OldestActiveAge is { } age) sb.Append(" · the OAT is ").Append(Duration(age)).Append(" old");
            sb.Append('\n');
            if (m.OldestActiveAge is not null && !snapshot.OatIsOwn)
            {
                sb.Append("OAT held by: ");
                sb.Append(showClients ? Who(new MonitorClient(m.OatUser, m.OatIp, m.OatHost, m.OatProcess, m.OatSystem)) : "(hidden by the FbStudio option)");
                if (m.OatIsMaintenance) sb.Append(" · it is a backup, sweep or server task: expected while it runs");
                sb.Append('\n');
            }
            // El resumen cuenta la conexión y la transacción de la propia foto: se descuentan.
            sb.Append(N($"Other connections: {Math.Max(0, m.Attachments - 1):N0} · other open transactions: {Math.Max(0, m.Transactions - 1):N0} · running statements: {m.RunningStatements:N0}"));
            if (m.WaitingStatements > 0)
                sb.Append(N($" · waiting on a locked record: {m.WaitingStatements:N0}")).Append(m.LongestWaitSeconds is { } w ? $" (longest {Duration(TimeSpan.FromSeconds(w))})" : "");
            sb.Append('\n');
            if (m.BackupState != BackupState.Normal) sb.Append("nbackup: the database is in backup mode\n");
            if (!m.SeesEveryone)
                sb.Append("Note: this registration's user is not SYSDBA, the owner or RDB$ADMIN, so MON$ only shows its own connections.\n");

            sb.Append(snapshot.Transactions.Count == 0 ? "\nNo other open transactions.\n" : $"\nOldest open transactions (up to {MaxRows}):\n");
            foreach (var t in snapshot.Transactions)
            {
                sb.Append(N($"- #{t.Id:N0} · {Duration(TimeSpan.FromSeconds(t.Seconds))} · {(t.ReadOnly ? "read-only" : "read-write")}"));
                sb.Append(t.Active ? " · active" : " · idle");
                sb.Append(N($" · attachment {t.Attachment}"));
                if (showClients) sb.Append(" · ").Append(Who(t.Client));
                if (t.IsOat) sb.Append(" · THE OAT");
                sb.Append('\n');
            }

            sb.Append(snapshot.Statements.Count == 0 ? "\nNo statements running.\n" : $"\nStatements running or fetching (oldest first, up to {MaxRows}; values replaced by ?):\n");
            foreach (var st in snapshot.Statements)
            {
                sb.Append(N($"- {Duration(TimeSpan.FromSeconds(st.Seconds))} · {(st.Running ? "running" : "fetching rows")} · attachment {st.Attachment}"));
                if (st.Transaction is { } tx) sb.Append(N($" · transaction #{tx:N0}"));
                if (st.SequentialReads is { } seq) sb.Append(N($" · sequential reads {seq:N0}"));
                if (st.IndexedReads is { } idx) sb.Append(N($" · indexed reads {idx:N0}"));
                if (st.Waiting) sb.Append(" · WAITING ON A LOCKED RECORD");
                if (showClients) sb.Append(" · ").Append(Who(st.Client));
                sb.Append('\n');
                if (ValueRedactor.Statement(st.Sql, MaxSqlLength) is { Length: > 0 } sql) sb.Append("  ").Append(sql).Append('\n');
            }

            if (showClients && snapshot.Connections.Count > 0)
            {
                sb.Append("\nConnections by program:\n");
                foreach (var g in snapshot.Connections.GroupBy(c => c.Client.ProcessName ?? "(unknown program)")
                             .Select(g => (Name: g.Key, Count: g.Sum(c => c.Count), Users: g.Select(c => c.Client.User).Where(u => u is not null).Distinct().Count()))
                             .OrderByDescending(g => g.Count).Take(15))
                    sb.Append(N($"- {g.Count:N0} · {g.Name} ({g.Users:N0} user{(g.Users == 1 ? "" : "s")})\n"));
            }
            return sb.ToString().TrimEnd('\n');
        }, ct);

    public Task<string> GetStatisticsAsync(string database, string? tables, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var wanted = (tables ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('"')).ToList();
            List<string>? names = null;
            if (wanted.Count > 0)
            {
                if (wanted.Count > MaxTables) throw new ConnectorException($"At most {MaxTables} tables at a time: each one reads all its pages.");
                var existing = (await s.ObjectsAsync(ct)).Where(o => o.Kind == DbObjectKind.Table).Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
                names = [];
                foreach (var w in wanted)
                    names.Add(existing.Contains(w) ? w : existing.Contains(w.ToUpperInvariant()) ? w.ToUpperInvariant()
                        : throw new ConnectorException($"There is no table called {w} in {s.Profile.Name}."));
            }
            var result = await DatabaseStatistics.ReadAsync(s.Profile, s.Password, names is null ? StatisticsScope.Header : StatisticsScope.Full,
                ct: ct, tables: names);
            var h = result.Header;
            var sb = new StringBuilder(N($"{s.Profile.Name} · statistics ({(names is null ? "header" : "header and " + string.Join(", ", names))}, read in {result.Elapsed.TotalSeconds:0.0} s)\n"));
            sb.Append($"Page size {h.PageSize} · ODS {h.OdsVersion} · dialect {h.Dialect} · page buffers {h.PageBuffers} · forced writes {(h.ForcedWrites ? "on" : "off")}\n");
            sb.Append(N($"Transactions: OIT {h.OldestTransaction:N0}, OAT {h.OldestActive:N0}, OST {h.OldestSnapshot:N0}, next {h.NextTransaction:N0}"));
            sb.Append(N($" · next − OAT {h.ActiveGap:N0} · OST − OIT {h.SweepGap:N0}"));
            sb.Append(h.SweepInterval == 0 ? " · automatic sweep off" : N($" · sweep interval {h.SweepInterval:N0}{(h.SweepIntervalIsDefault ? " (default)" : "")}"));
            sb.Append('\n');
            if (h.Attributes is { Length: > 0 } attributes) sb.Append("Attributes: ").Append(attributes).Append('\n');
            if (names is null)
            {
                sb.Append($"For a table's records, versions, pages and indexes, pass up to {MaxTables} table names (each one reads all its pages).");
                return sb.ToString();
            }

            foreach (var t in DatabaseAnalysis.Parse(result.Lines).Tables)
            {
                sb.Append('\n').Append(N($"TABLE {t.Name}: {t.Records:N0} records"));
                if (t.AverageRecordLength is { } len) sb.Append(N($" · average length {len:0.#}"));
                sb.Append(N($" · versions {t.Versions:N0} (max {t.MaxVersions:N0} in one record)"));
                sb.Append(N($" · data pages {t.DataPages:N0} · average fill {t.AverageFill}%"));
                if (t.Fragments is > 0) sb.Append(N($" · fragments {t.Fragments:N0}"));
                if (t.ManyVersions) sb.Append(" · MANY OLD VERSIONS (garbage not collected, often an open transaction)");
                sb.Append('\n');
                foreach (var i in t.Indexes)
                {
                    sb.Append(N($"  INDEX {i.Name}: depth {i.Depth} · nodes {i.Nodes:N0} · average key {i.AverageKeyLength:0.#}"));
                    if (i.DuplicateRatio is { } dup) sb.Append(N($" · duplicates {dup * 100:0}%"));
                    if (i.TooDeep) sb.Append(" · TOO DEEP");
                    if (i.PoorSelectivity) sb.Append(" · POOR SELECTIVITY");
                    sb.Append('\n');
                }
            }
            return sb.ToString().TrimEnd('\n');
        }, ct);

    /// <summary>Usuario, equipo (o IP) y programa sin la carpeta.</summary>
    private static string Who(MonitorClient c)
    {
        if (c.System) return "(server task)";
        var parts = new[] { c.User is null ? null : "user " + c.User, c.Host ?? c.Ip, c.ProcessName }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" · ", parts) is { Length: > 0 } who ? who : "(unknown)";
    }

    private static string Duration(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds} s"
        : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} min"
        : t.TotalHours < 48 ? $"{(int)t.TotalHours} h {t.Minutes} min"
        : $"{(int)t.TotalDays} days";

    /// <summary>Números con separador de miles fijo (lo lee el modelo, no depende del idioma del equipo).</summary>
    private static string N(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
