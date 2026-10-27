using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FbStudio.App.Tests;

/// <summary>
/// <c>FbStudio mcp</c> como lo lanza un asistente (D-114, D-115): el ejecutable de verdad, por la entrada y la salida
/// estándar, sin ventanas. En Windows es un ejecutable de interfaz gráfica (WinExe) y tiene que hablar igual.
/// </summary>
public sealed class McpCommandTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "fbstudio-mcp-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task El_subcomando_mcp_atiende_a_un_cliente_por_stdio()
    {
        Directory.CreateDirectory(_data);
        File.WriteAllText(Path.Combine(_data, "profiles.json"), """
            [ { "Id": "6f1b2c3d-0000-0000-0000-000000000001", "Name": "Pruebas", "Host": "srv", "Database": "/x.fdb", "User": "U",
                "Environment": "Prueba", "AssistantAccess": true },
              { "Id": "6f1b2c3d-0000-0000-0000-000000000002", "Name": "Otra", "Host": "srv", "Database": "/y.fdb", "User": "U" } ]
            """);
        var (command, args) = Launcher();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "fbstudio",
            Command = command,
            Arguments = [.. args, "mcp", "--data", _data],
        }), cancellationToken: cts.Token);

        Assert.Equal("fbstudio", client.ServerInfo?.Name);
        var tools = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToList();
        Assert.Equal(11, tools.Count);
        Assert.Contains("list_databases", tools);
        Assert.Contains("explain_query", tools);
        var result = await client.CallToolAsync("list_databases", new Dictionary<string, object?>(), cancellationToken: cts.Token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("Databases available through FbStudio (1):\n- Pruebas · environment: Test\n1 more registration not enabled.", text);
    }

    /// <summary>El ejecutable de FbStudio junto a las pruebas, o «dotnet FbStudio.dll» si no se copió.</summary>
    private static (string Command, string[] Args) Launcher()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FbStudio.exe" : "FbStudio");
        return File.Exists(exe) ? (exe, []) : ("dotnet", [Path.Combine(AppContext.BaseDirectory, "FbStudio.dll")]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
    }
}
