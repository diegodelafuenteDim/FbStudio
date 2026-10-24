using System.Diagnostics;
using FbStudio.Core.Monitoring;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>Monitoreo (F-1) contra Firebird 3 en Docker (SYSDBA: ve todas las conexiones).</summary>
[Collection(nameof(FirebirdCollection))]
public class MonitorSessionTests(FirebirdFixture fb)
{
    private static MonitorPreset Preset(string key) => MonitorQueries.Presets.Single(p => p.Key == key);

    private static async Task<long> AttachmentIdAsync(QuerySession s)
    {
        var (_, rows) = await FirebirdFixture.RunAsync(s, "SELECT CURRENT_CONNECTION FROM RDB$DATABASE");
        return Convert.ToInt64(rows[0][0]);
    }

    [Fact]
    public async Task Lee_de_quien_es_una_conexion_para_confirmar_el_corte()
    {
        await using var other = fb.NewSession();
        var id = await AttachmentIdAsync(other);
        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        var who = await monitor.AttachmentAsync(id);
        Assert.NotNull(who);
        Assert.Equal(fb.Profile.User, who.User, ignoreCase: true);
        Assert.Equal("FbStudio (editor)", who.Process);
        Assert.False(string.IsNullOrEmpty(who.Address));
        Assert.Null(await monitor.AttachmentAsync(-1));                        // ya no está
    }

    [Fact]
    public async Task Ve_la_transaccion_que_retiene_la_OAT_y_de_quien_es()
    {
        await using var other = fb.NewSession();
        var (upd, _) = await FirebirdFixture.RunAsync(other, "UPDATE TURNOS SET OBS = OBS WHERE ID = 8");
        Assert.Null(upd.Error);
        var otherTx = other.TransactionId!.Value;
        var otherAttachment = await AttachmentIdAsync(other);

        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        var summary = await monitor.SummaryAsync();
        // El usuario del contenedor es «test», no SYSDBA, pero es el dueño de la base: MON$ le muestra todas (D-53).
        Assert.True(summary.IsOwner);
        Assert.True(summary.SeesEveryone);
        Assert.Equal(otherTx, summary.OldestActive);
        Assert.True(summary.Gap >= 1);
        Assert.InRange(summary.OldestActiveAge!.Value.TotalSeconds, 0, 60);  // recién abierta (y en la hora del servidor)
        Assert.True(summary.Attachments >= 2);
        Assert.Equal(summary.OwnAttachment, monitor.OwnAttachment);

        // Transacciones (las «Mini» de Diego): conexión, nota, Tx, desde, segundos, dirección, equipo, programa, estado, Top, OIT, OAT, usuario.
        var tx = await monitor.QueryAsync(Preset("transactions").Sql);
        Assert.Equal(["Conexión", "Nota", "Tx", "Desde", "Segundos", "Dirección", "Equipo", "Programa", "Estado", "Top", "OIT", "OAT", "Usuario"],
            tx.Columns.Select(c => c.Name));
        var row = Assert.Single(tx.Rows, r => Convert.ToInt64(r[2]) == otherTx);
        Assert.Equal(otherAttachment, Convert.ToInt64(row[0]));
        Assert.Contains("OAT", (string)row[1]);                               // la nota, al lado del número
        Assert.Contains("/", (string)row[5]);                                 // la dirección, con el puerto
        Assert.Equal("FbStudio (editor)", row[7]);
        Assert.True(Convert.ToInt64(row[9]) >= otherTx);                      // Top
        Assert.True(Convert.ToInt64(row[11]) <= otherTx);                     // la OAT que vio al empezar
        Assert.Equal(tx.Rows.Select(r => (DateTime)r[3]).Order(), tx.Rows.Select(r => (DateTime)r[3]));  // la más vieja arriba

        var attachments = await monitor.QueryAsync(Preset("attachments").Sql);
        Assert.Equal(["Conexión", "Nota", "PID del servidor", "Estado", "Dirección", "Equipo", "PID remoto", "Desde", "Programa", "Usuario"],
            attachments.Columns.Select(c => c.Name));
        var mine = Assert.Single(attachments.Rows, r => Convert.ToInt64(r[0]) == summary.OwnAttachment);
        Assert.Equal("este monitoreo", mine[1]);
        var holder = Assert.Single(attachments.Rows, r => Convert.ToInt64(r[0]) == otherAttachment);
        Assert.Contains("OAT", (string)holder[1]);
        Assert.Contains("/", (string)holder[4]);                              // la dirección, con el puerto
        Assert.Equal("FbStudio (editor)", holder[8]);                          // el programa: el nombre de la conexión

        await other.RollbackAsync();
    }

    [Fact]
    public async Task El_monitoreo_no_deja_transacciones_abiertas()
    {
        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        await monitor.SummaryAsync();
        await monitor.QueryAsync(Preset("attachments").Sql);
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test-monitor");
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand("""
            SELECT COUNT(*) FROM MON$TRANSACTIONS t JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID
            WHERE a.MON$REMOTE_PROCESS = 'FbStudio (monitoreo)'
            """, c, t);
        Assert.Equal(0, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
        await t.CommitAsync();
    }

    [Fact]
    public async Task Cortar_una_conexion_le_deshace_la_transaccion()
    {
        await using var victim = fb.NewSession();
        await FirebirdFixture.RunAsync(victim, "UPDATE TURNOS SET IMPORTE = -77 WHERE ID = 9");
        var id = await AttachmentIdAsync(victim);

        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        await monitor.SummaryAsync();
        Assert.True(await monitor.KillAttachmentAsync(id));
        Assert.False(await monitor.KillAttachmentAsync(id));                 // ya no existe
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.KillAttachmentAsync(monitor.OwnAttachment!.Value));

        // La conexión cortada ya no sirve, y su UPDATE se deshizo.
        var failed = false;
        try { failed = (await FirebirdFixture.RunAsync(victim, "SELECT 1 FROM RDB$DATABASE")).Outcome.Error is not null; }
        catch (Exception) { failed = true; }
        Assert.True(failed);
        await using var check = fb.NewSession();
        var (_, rows) = await FirebirdFixture.RunAsync(check, "SELECT IMPORTE FROM TURNOS WHERE ID = 9");
        Assert.NotEqual(-77m, Convert.ToDecimal(rows[0][0]));
    }

    [Fact]
    public async Task Cancelar_una_sentencia_larga_sin_cortar_la_conexion()
    {
        await using var busy = fb.NewSession();
        var busyAttachment = await AttachmentIdAsync(busy);
        var clock = Stopwatch.StartNew();
        var running = FirebirdFixture.RunAsync(busy, "SELECT COUNT(*) FROM TURNOS a CROSS JOIN TURNOS b");

        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        object[]? statement = null;
        while (statement is null && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            var result = await monitor.QueryAsync(Preset("statements").Sql);
            statement = result.Rows.FirstOrDefault(r => Convert.ToInt64(r[1]) == busyAttachment);
            if (statement is null) await Task.Delay(100);
        }
        Assert.NotNull(statement);
        Assert.Contains("CROSS JOIN", (string)statement[11]);

        Assert.True(await monitor.CancelStatementAsync(Convert.ToInt64(statement[0])));
        var (outcome, _) = await running;
        Assert.True(outcome.Cancelled);
        Assert.Contains("otra conexión", outcome.Error);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"tardó {clock.Elapsed.TotalSeconds:N0} s");
        // La conexión sigue viva.
        var (after, rows) = await FirebirdFixture.RunAsync(busy, "SELECT 1 FROM RDB$DATABASE");
        Assert.Null(after.Error);
        Assert.Single(rows);
    }

    [Fact]
    public async Task Las_consultas_de_ejemplo_corren_y_traen_el_numero_de_conexion()
    {
        // El archivo de ejemplo para importar (docs/examples, copiado al compilar); acá se lee directo: este proyecto no usa Workspace.
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "monitor-queries.json")));
        var queries = json.RootElement.EnumerateArray().Select(e => (Name: e.GetProperty("Name").GetString()!, Sql: e.GetProperty("Sql").GetString()!)).ToList();
        Assert.Equal(2, queries.Count);
        await using var other = fb.NewSession();
        await FirebirdFixture.RunAsync(other, "UPDATE TURNOS SET OBS = OBS WHERE ID = 12");
        var otherAttachment = await AttachmentIdAsync(other);

        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        foreach (var q in queries)
        {
            var preset = new MonitorPreset("c", q.Name, q.Sql) { CustomId = Guid.NewGuid() };
            var result = await monitor.QueryAsync(q.Sql);
            var map = MonitorColumns.Resolve(preset, result.Columns);
            Assert.Equal(0, map.Attachment);                                                 // ATTACHMENT_ID / "Attachment ID"
            Assert.Contains(result.Rows, r => Convert.ToInt64(r[0]) == otherAttachment);
        }
        await other.RollbackAsync();
    }

    [Fact]
    public async Task Muestra_quien_espera_un_registro_bloqueado_y_quien_lo_tiene()
    {
        await using var holder = fb.NewSession();
        await FirebirdFixture.RunAsync(holder, "UPDATE TURNOS SET OBS = OBS WHERE ID = 16");
        var holderTx = holder.TransactionId!.Value;
        var holderAttachment = await AttachmentIdAsync(holder);
        await using var waiter = fb.NewSession();
        waiter.LockTimeout = TimeSpan.FromSeconds(30);
        var waiting = FirebirdFixture.RunAsync(waiter, "UPDATE TURNOS SET OBS = 'esperando' WHERE ID = 16");

        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        MonitorSummary summary;
        var clock = Stopwatch.StartNew();
        do
        {
            await Task.Delay(200);
            summary = await monitor.SummaryAsync();
        } while (summary.WaitingStatements == 0 && clock.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(1, summary.WaitingStatements);                          // la tarjeta «Esperando un bloqueo»
        Assert.NotNull(summary.LongestWaitSeconds);
        Assert.Equal(holderTx, summary.OldestActive);
        Assert.Equal(("TEST", "FbStudio (editor)", false), (summary.OatUser, summary.OatProcess, summary.OatIsMaintenance));
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", summary.OatIp);

        var locks = await monitor.QueryAsync(MonitorQueries.Presets.Single(p => p.Key == "locks").Sql);
        var waitRow = Assert.Single(locks.Rows, r => (string)r[0] == "espera un registro");
        Assert.Contains("OBS = 'esperando'", (string)waitRow[11]);
        Assert.NotNull(waitRow[3]);                                           // la sentencia, para cancelarla
        var holderRow = Assert.Single(locks.Rows, r => (string)r[0] == "posible responsable");
        Assert.Equal((holderTx, holderAttachment), (Convert.ToInt64(holderRow[2]), Convert.ToInt64(holderRow[1])));
        Assert.True(Convert.ToInt64(holderRow[10]) >= 1);                     // tiene cambios
        var map = MonitorColumns.Resolve(MonitorQueries.Presets.Single(p => p.Key == "locks"), locks.Columns);
        Assert.Equal((1, 3, 11), (map.Attachment, map.Statement, map.Sql));

        // Cortar al responsable destraba la espera.
        Assert.True(await monitor.KillAttachmentAsync(holderAttachment));
        var (outcome, _) = await waiting;
        Assert.Null(outcome.Error);
        Assert.Equal(1, outcome.RecordsAffected);
        await waiter.RollbackAsync();
    }

    [Fact]
    public async Task En_solo_lectura_no_corta_ni_cancela()
    {
        await using var monitor = new MonitorSession(fb.Profile with { ReadOnly = true }, fb.Password);
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.KillAttachmentAsync(123456));
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.CancelStatementAsync(123456));
    }
}
