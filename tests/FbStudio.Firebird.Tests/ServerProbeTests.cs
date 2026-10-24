using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;
using Testcontainers.FirebirdSql;

namespace FbStudio.Firebird.Tests;

/// <summary>Tipo de servidor y datos del servidor (F-16) contra el Firebird 3 de Docker, que viene en SuperServer.</summary>
[Collection(nameof(FirebirdCollection))]
public class ServerProbeTests(FirebirdFixture fb)
{
    [Fact]
    public async Task Detecta_SuperServer_y_lee_los_datos_del_servidor()
    {
        var details = await ServerProbe.DetailsAsync(fb.Profile, fb.Password);
        var info = details.Info;

        Assert.Equal(ServerMode.SuperServer, info.Mode);                       // el dueño de la base ve el «Cache Writer»
        Assert.Contains("Cache Writer", info.ModeEvidence!.Reason);
        Assert.StartsWith("Linux", info.Platform);
        Assert.StartsWith("Firebird/Linux", info.Implementation);
        Assert.StartsWith("LI-V3.", details.ServerVersion);
        Assert.True(info.PageBuffers > 0);
        Assert.Contains("SuperServer", info.Summary);

        var rows = details.Rows("localhost:3050");
        Assert.Contains(rows, r => r.Label == Core.Resources.CoreStrings.ServerRowMode && r.Value == "SuperServer");
        Assert.Contains(rows, r => r.Label == Core.Resources.CoreStrings.ServerRowCache && r.Value.Contains("compartida"));
    }

    [Fact]
    public async Task Ni_siquiera_con_SYSDBA_muestra_rutas_del_servidor()
    {
        var sysdba = await ServerProbe.DetailsAsync(fb.Profile with { User = "SYSDBA" }, fb.Password);
        Assert.Equal(ServerMode.SuperServer, sysdba.Info.Mode);
        var text = sysdba.Text("localhost:3050");
        Assert.DoesNotContain("/firebird", text);                              // ni el archivo (/firebird/data/test) ni /firebird/etc
        Assert.DoesNotContain("/usr/local/firebird", text);
    }

    [Fact]
    public async Task Dice_si_la_conexion_va_cifrada_y_Required_la_exige()
    {
        await using var required = new QuerySession(fb.Profile with { WireCrypt = WireCryptMode.Required }, fb.Password);
        Assert.True(await required.WireEncryptedAsync());
        await using var disabled = new QuerySession(fb.Profile with { WireCrypt = WireCryptMode.Disabled }, fb.Password);
        var plain = await Record.ExceptionAsync(() => disabled.WireEncryptedAsync());
        Assert.True(plain is not null || await disabled.WireEncryptedAsync() == false);   // o el servidor la rechaza, o va sin cifrar
    }

    [Fact]
    public async Task El_explorador_y_el_monitoreo_lo_detectan_al_conectar()
    {
        await using var explorer = new MetadataExplorer(fb.Profile, fb.Password);
        var info = await explorer.ServerInfoAsync();
        Assert.Equal(ServerMode.SuperServer, info.Mode);
        Assert.NotNull(info.WireText);                                          // cifrada o no: el servidor lo dice
        Assert.StartsWith($"{info.Display} · SuperServer", info.Heading);

        await using var monitor = new MonitorSession(fb.Profile, fb.Password);
        Assert.Equal(ServerMode.SuperServer, (await monitor.ServerModeAsync())!.Mode);
        Assert.True((await monitor.SummaryAsync()).SeesEveryone);              // el dueño de la base ve todas las conexiones
    }
}

/// <summary>
/// Classic y SuperClassic: el mismo Firebird 3 de Docker con otro ServerMode en firebird.conf (se cambia antes de que
/// el contenedor lo copie a /firebird/etc). Dos contenedores más, que arrancan juntos.
/// </summary>
public sealed class ServerModeContainers : IAsyncLifetime
{
    public FirebirdSqlContainer Classic { get; } = Build("Classic");
    public FirebirdSqlContainer SuperClassic { get; } = Build("SuperClassic");

    private static FirebirdSqlContainer Build(string mode) => new FirebirdSqlBuilder("jacobalberty/firebird:v3.0")
        .WithEntrypoint("bash", "-c",
            $"sed -i 's/^#*ServerMode.*/ServerMode = {mode}/' /usr/local/firebird/skel/etc/firebird.conf && exec /usr/local/firebird/docker-entrypoint.sh firebird")
        .Build();

    public static (ConnectionProfile Profile, string Password) ProfileOf(FirebirdSqlContainer container)
    {
        var cs = new FbConnectionStringBuilder(container.GetConnectionString());
        return (new ConnectionProfile { Name = "docker-modo", Host = cs.DataSource, Port = cs.Port, Database = cs.Database, User = cs.UserID }, cs.Password);
    }

    public Task InitializeAsync() => Task.WhenAll(Classic.StartAsync(), SuperClassic.StartAsync());

    public Task DisposeAsync() => Task.WhenAll(Classic.DisposeAsync().AsTask(), SuperClassic.DisposeAsync().AsTask());
}

public class ServerModeTests(ServerModeContainers servers) : IClassFixture<ServerModeContainers>
{
    [Fact]
    public async Task Detecta_Classic_por_los_procesos_de_dos_conexiones()
    {
        var (profile, password) = ServerModeContainers.ProfileOf(servers.Classic);
        await using var explorer = new MetadataExplorer(profile, password);
        var info = await explorer.ServerInfoAsync();                          // una sola conexión: abre otra para comparar

        Assert.Equal(ServerMode.Classic, info.Mode);
        Assert.Equal(2, info.ModeEvidence!.Processes);
        Assert.EndsWith("· Classic", info.ShortDisplay);

        await using var monitor = new MonitorSession(profile, password);
        Assert.Equal(ServerMode.Classic, (await monitor.ServerModeAsync())!.Mode);
    }

    [Fact]
    public async Task Detecta_SuperClassic_porque_no_tiene_Cache_Writer()
    {
        var (profile, password) = ServerModeContainers.ProfileOf(servers.SuperClassic);
        var details = await ServerProbe.DetailsAsync(profile, password);

        Assert.Equal(ServerMode.SuperClassic, details.Info.Mode);
        Assert.Equal(1, details.Info.ModeEvidence!.Processes);
        Assert.Contains(details.Rows("x"), r => r.Label == Core.Resources.CoreStrings.ServerRowCache && r.Value.Contains("por conexión"));
    }
}
