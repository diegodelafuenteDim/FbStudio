using System.IO.Pipelines;
using FbStudio.Core.Profiles;
using FbStudio.Mcp;
using FbStudio.Workspace;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FbStudio.Mcp.Tests;

/// <summary>El conector visto por un cliente MCP, en memoria (sin proceso aparte).</summary>
public sealed class ProtocolTests
{
    /// <summary>
    /// Todas las herramientas del conector. Sumar una obliga a cambiar esta lista y docs/disenos/conector-mcp.md
    /// (D-114): ninguna puede ejecutar SQL ni leer filas.
    /// </summary>
    public static readonly string[] Expected =
    [
        "describe_object", "explain_query", "find_column_usage", "find_join_path", "get_database_info", "get_dependencies",
        "get_monitor_snapshot", "get_schema", "get_statistics", "list_databases", "search_metadata",
    ];

    private static readonly ConnectorContext Context = new(
        () => [new ConnectionProfile { Name = "Pruebas", Host = "srv", Database = "/x.fdb", User = "U", AssistantAccess = true }],
        _ => [],
        _ => null);

    private static async Task<T> WithClient<T>(ConnectorContext? context, Func<McpClient, Task<T>> use)
    {
        context ??= Context;
        await using var catalog = new DatabaseCatalog(context);
        var options = McpHost.Options(context, catalog);
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = McpServer.Create(new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()), options);
        var running = server.RunAsync(cts.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: cts.Token);
            return await use(client);
        }
        finally
        {
            await cts.CancelAsync();
            try { await running; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Ofrece_solo_las_herramientas_acordadas_y_todas_de_solo_lectura()
    {
        var tools = await WithClient(null, async c => await c.ListToolsAsync());

        Assert.Equal(Expected, tools.Select(t => t.Name).Order());
        Assert.All(tools, t =>
        {
            var a = t.ProtocolTool.Annotations;
            Assert.NotNull(a);
            Assert.Equal((true, false, false), (a.ReadOnlyHint, a.DestructiveHint, a.OpenWorldHint));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
        });
    }

    [Fact]
    public async Task Se_presenta_y_lista_las_bases_habilitadas()
    {
        var (info, instructions, text) = await WithClient(null, async c =>
        {
            var result = await c.CallToolAsync("list_databases", new Dictionary<string, object?>());
            return (c.ServerInfo, c.ServerInstructions ?? "", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        });

        Assert.Equal(("fbstudio", "FbStudio"), (info?.Name, info?.Title));
        Assert.Contains("never returns table rows and cannot change anything", instructions);
        Assert.DoesNotContain("package", instructions);
        Assert.Equal("Databases available through FbStudio (1):\n- Pruebas · environment: Development", text);
    }

    [Fact]
    public async Task Cada_llamada_queda_registrada_y_las_respuestas_respetan_el_largo_maximo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fbstudio-mcp-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new McpCallLog(folder, null, TimeSpan.FromDays(90));
            var many = Enumerable.Range(0, 400).Select(i => new ConnectionProfile
            {
                Name = $"Base {i:000} con un nombre largo", Host = "srv", Database = "/x.fdb", User = "U", AssistantAccess = true,
            }).ToList();
            var context = new ConnectorContext(() => many, _ => [], _ => null, log, ResponseLimit: 5_000);
            var (list, error) = await WithClient(context, async c =>
            {
                var ok = await c.CallToolAsync("list_databases", new Dictionary<string, object?>());
                var bad = await c.CallToolAsync("get_schema", new Dictionary<string, object?> { ["database"] = "No existe", ["kind"] = "table" });
                return (Assert.IsType<TextContentBlock>(Assert.Single(ok.Content)).Text, bad);
            });

            Assert.True(list.Length < 5_300);
            Assert.Contains("[Truncated:", list);
            Assert.True(error.IsError);
            var calls = log.Read(packages: Path.Combine(folder, "sin-paquetes"));
            Assert.Equal(["get_schema", "list_databases"], calls.Select(c => c.Tool));
            Assert.Equal(("No existe", false), (calls[0].Database, calls[0].Ok));
            Assert.Equal((true, list.Length), (calls[1].Ok, calls[1].Characters));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Si_ve_la_copia_de_un_paquete_lo_avisa_en_las_instrucciones() =>
        Assert.Contains("app package App_x (MSIX), which may be old", McpHost.Instructions("App_x"));
}
