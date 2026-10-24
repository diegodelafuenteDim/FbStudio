using System.Text.Json;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;

namespace FbStudio.Workspace;

/// <summary>
/// Importa perfiles desde la configuración de servidores MCP de Firebird (args --host/--port/--database/--user/
/// --password/--role), para no tipear de nuevo conexiones que ya existen. Nunca muestra la contraseña.
/// </summary>
public static class McpConfigImporter
{
    public static (ConnectionProfile Profile, string Password) Read(string jsonPath, string serverName, string charset, ProfileEnvironment environment)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var args = doc.RootElement.GetProperty("mcpServers").GetProperty(serverName).GetProperty("args")
            .EnumerateArray().Select(a => a.GetString() ?? "").ToList();

        string? Arg(string flag)
        {
            var i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        string Required(string flag) => Arg(flag) ?? throw new InvalidOperationException(string.Format(CoreStrings.McpMissingFlag, flag, serverName));

        var profile = new ConnectionProfile
        {
            Name = serverName,
            Host = Required("--host"),
            Port = int.TryParse(Arg("--port"), out var port) ? port : 3050,
            Database = Required("--database"),
            User = Required("--user"),
            Role = Arg("--role"),
            Charset = charset,
            Environment = environment,
            // Las conexiones de los MCP son de lectura (rol READER): el perfil también.
            ReadOnly = true,
        };
        return (profile, Required("--password"));
    }
}
