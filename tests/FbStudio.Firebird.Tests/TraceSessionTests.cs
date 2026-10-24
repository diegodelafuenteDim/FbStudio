using System.Collections.Concurrent;
using System.Diagnostics;
using FbStudio.Core.Trace;

namespace FbStudio.Firebird.Tests;

/// <summary>Sesión de traza (F-24) contra Firebird 3 en Docker.</summary>
[Collection(nameof(FirebirdCollection))]
public class TraceSessionTests(FirebirdFixture fb)
{
    [Theory]
    [InlineData("test", "[Tt][Ee][Ss][Tt]")]
    [InlineData(@"D:\Bases\CLINICA.FDB", "[Dd]__[Bb][Aa][Ss][Ee][Ss]_[Cc][Ll][Ii][Nn][Ii][Cc][Aa]_[Ff][Dd][Bb]")]
    [InlineData("/db/clinica-2.fdb", "_[Dd][Bb]_[Cc][Ll][Ii][Nn][Ii][Cc][Aa]_2_[Ff][Dd][Bb]")]
    public void El_patron_no_distingue_mayusculas_ni_escapa_nada(string name, string pattern) =>
        Assert.Equal(pattern, TraceSession.NamePattern(name));

    [Fact]
    public async Task Ve_las_sentencias_de_otra_conexion_con_su_plan_y_sin_la_ruta()
    {
        var lines = new ConcurrentQueue<string>();
        await using var trace = new TraceSession(fb.Profile, fb.Password);
        await trace.StartAsync(new TraceOptions(ThresholdMs: 0), lines.Enqueue);
        Assert.NotNull(trace.SessionId);
        Assert.True(trace.IsRunning);

        await using (var session = fb.NewSession())
        {
            await FirebirdFixture.RunAsync(session, "SELECT COUNT(*) FROM TURNOS WHERE PACIENTE = 'TRAZA DE PRUEBA'");
            await FirebirdFixture.RunAsync(session, "SELECT FIRST 5 ID FROM TURNOS WHERE ID BETWEEN 10 AND 20");
        }
        var sw = Stopwatch.StartNew();
        while (!(lines.Any(l => l.Contains("TRAZA DE PRUEBA")) && lines.Any(l => l.Contains("BETWEEN 10 AND 20"))) && sw.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(200);
        await trace.StopAsync();
        Assert.False(trace.IsRunning);

        var all = lines.ToList();
        Assert.DoesNotContain(all, l => l.Contains("/firebird/data"));                // la ruta no llega (D-54)
        var events = TraceParser.Parse(all);
        var count = Assert.Single(events, e => e.Sql?.Contains("TRAZA DE PRUEBA") == true);
        Assert.True(count.IsExecution);
        Assert.Equal("test", count.Database);
        Assert.Equal(fb.Profile.User.ToUpperInvariant(), count.User);
        Assert.Contains("TURNOS", count.Plan);
        Assert.True(count.HasFullScan);                                                // el índice de PACIENTE está inactivo
        Assert.NotNull(count.Fetches);

        var groups = TraceAnalysis.Group(events);
        Assert.Contains(groups, g => g.Key == "SELECT FIRST ? ID FROM TURNOS WHERE ID BETWEEN ? AND ?");
    }

    [Fact]
    public async Task Arranca_aunque_el_registro_tenga_un_rol()
    {
        // Firebird 3 pasa el rol de la conexión de servicio como «-role» al administrador de trazas, que no lo conoce
        // («unknown switch "-role" encountered»): la traza se inicia sin el rol.
        await using var trace = new TraceSession(fb.Profile with { Role = "RDB$ADMIN" }, fb.Password);
        await trace.StartAsync(new TraceOptions(ThresholdMs: 0), _ => { });
        Assert.NotNull(trace.SessionId);
        Assert.True(trace.IsRunning);
        await trace.StopAsync();
    }

    [Fact]
    public async Task Con_umbral_alto_no_anota_lo_rapido()
    {
        var lines = new ConcurrentQueue<string>();
        await using var trace = new TraceSession(fb.Profile, fb.Password);
        await trace.StartAsync(new TraceOptions(ThresholdMs: 60_000), lines.Enqueue);
        await using (var session = fb.NewSession())
            await FirebirdFixture.RunAsync(session, "SELECT 'RAPIDA' FROM RDB$DATABASE");
        await Task.Delay(1500);
        await trace.StopAsync();
        Assert.DoesNotContain(TraceParser.Parse(lines), e => e.Sql?.Contains("RAPIDA") == true);
    }
}
