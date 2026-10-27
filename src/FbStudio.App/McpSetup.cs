using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FbStudio.App.Resources;
using FbStudio.Workspace;

namespace FbStudio.App;

/// <summary>
/// Lo que muestra la pestaña «Asistentes (MCP)» de Opciones (D-117): las bases habilitadas, la configuración para copiar
/// en el asistente y las últimas llamadas.
/// </summary>
public sealed record AssistantsInfo(IReadOnlyList<string> EnabledDatabases, string Command, IReadOnlyList<string> Arguments, IReadOnlyList<McpCall> Calls)
{
    /// <summary>Para las pruebas y para abrir Opciones sin datos: nada habilitado ni registrado.</summary>
    public static AssistantsInfo Empty { get; } = new([], "FbStudio", ["mcp"], []);
}

/// <summary>Cómo se lanza el conector desde este FbStudio, y los textos de la pestaña.</summary>
public static class McpSetup
{
    /// <summary>El ejecutable que está corriendo con el argumento «mcp» (o «dotnet FbStudio.dll mcp», si se abrió así).</summary>
    public static (string Command, IReadOnlyList<string> Arguments) Launch()
    {
        var exe = Environment.ProcessPath ?? "FbStudio";
        return Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? ("dotnet", [typeof(McpSetup).Assembly.Location, "mcp"])
            : (exe, ["mcp"]);
    }

    /// <summary>La configuración MCP estándar, la que va en el archivo de servidores MCP del asistente.</summary>
    public static string Config(string command, IReadOnlyList<string> arguments)
    {
        var server = new JsonObject { ["command"] = command, ["args"] = new JsonArray(arguments.Select(a => (JsonNode?)a).ToArray()) };
        var root = new JsonObject { ["mcpServers"] = new JsonObject { ["fbstudio"] = server } };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Una llamada en un renglón (y lo que pidió o el error, en el siguiente).</summary>
    public static string Line(McpCall call)
    {
        var head = string.Format(CultureInfo.CurrentCulture, "{0:dd/MM HH:mm:ss}  {1}  {2}  {3:N0} ms  {4}", call.Time, call.Database ?? "—",
            call.Tool, call.Milliseconds, call.Ok ? string.Format(Strings.AssistantsCallChars, call.Characters.ToString("N0", CultureInfo.CurrentCulture)) : Strings.AssistantsCallError);
        var detail = call.Ok ? call.Arguments : call.Error ?? call.Arguments;
        return string.IsNullOrWhiteSpace(detail) ? head : head + "\n    " + string.Join(' ', detail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
