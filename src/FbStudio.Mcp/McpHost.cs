using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FbStudio.Mcp;

/// <summary>
/// El servidor MCP de <c>FbStudio mcp</c> (D-114): habla por la entrada y la salida estándar con el asistente que lo
/// lanzó, sin ventanas ni puertos. La salida estándar es solo del protocolo; los avisos van al error estándar.
/// </summary>
public static class McpHost
{
    public static McpServerOptions Options(ConnectorContext context, DatabaseCatalog catalog, string? package = null)
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in FbStudioTools.Create(context, catalog)) tools.Add(tool);
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "fbstudio", Title = "FbStudio", Version = Version },
            ServerInstructions = Instructions(package),
            ToolCollection = tools,
        };
    }

    /// <summary>Lo que el asistente lee al conectarse: qué hay, qué no, y el aviso del paquete MSIX si corresponde.</summary>
    public static string Instructions(string? package)
    {
        var text =
            "FbStudio gives read-only access to the structure of the Firebird databases the user enabled for assistants. " +
            "It never returns table rows and cannot change anything. Start with list_databases. In big databases, go by area " +
            "with get_schema. To see how a query would run, use explain_query: it prepares the statement without executing it. " +
            "For slowness, locks or garbage piling up, use get_monitor_snapshot and get_statistics.";
        if (package is not null)
            text += $" Note: Windows is showing FbStudio the copy of its settings kept inside the app package {package} (MSIX), " +
                    "which may be old. If a database the user enabled is missing, that old copy is the likely reason: tell the user.";
        return text;
    }

    /// <summary>Atiende al asistente hasta que cierra la entrada estándar. Devuelve el código de salida del proceso.</summary>
    /// <param name="dataFolder">La carpeta de datos de FbStudio (registros, llavero, áreas).</param>
    public static async Task<int> RunAsync(ConnectorContext context, string dataFolder, TextWriter log, CancellationToken ct = default)
    {
        var package = WindowsPackage.VirtualCopyInUse(dataFolder);
        if (package is not null)
            await log.WriteLineAsync($"FbStudio mcp: Windows is showing the copy of {dataFolder} kept in the app package {package}; it may be old.");
        await using var catalog = new DatabaseCatalog(context);
        var options = Options(context, catalog, package);
        try
        {
            await using var server = McpServer.Create(new StdioServerTransport(options), options);
            await server.RunAsync(ct);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            await log.WriteLineAsync($"FbStudio mcp: {ex.Message}");
            return 1;
        }
    }

    private static string Version =>
        typeof(McpHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
