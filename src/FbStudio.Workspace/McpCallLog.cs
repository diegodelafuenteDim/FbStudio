using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FbStudio.Workspace;

/// <summary>Una llamada de un asistente al conector MCP (D-114, punto 7).</summary>
/// <param name="Arguments">Lo que pidió («area=LAB · kind=table», el SQL del plan); null si no se pudo descifrar o no se guardó.</param>
/// <param name="Error">El error que se le devolvió, ya sin datos internos; null si salió bien.</param>
public sealed record McpCall(DateTime Time, string Tool, string? Database, string? Arguments, long Milliseconds, bool Ok, int Characters, string? Error = null);

/// <summary>
/// El registro de llamadas del conector MCP (D-117): mcp-log.jsonl en la carpeta de FbStudio, un renglón JSON por llamada.
/// La fecha, la herramienta, la base, el tiempo y el tamaño van a la vista; lo que pidió el asistente y el error van
/// cifrados como el historial (el SQL de un plan puede traer un valor que la persona le dictó). Sin cifrado, eso no se
/// guarda. Se purga por antigüedad cuando arranca el conector.
/// <para>
/// En Windows, un conector lanzado desde una app con paquete MSIX escribe en la copia de %APPDATA% de ese paquete (D-115):
/// <see cref="Read"/> junta esos registros con el propio, así FbStudio los ve igual.
/// </para>
/// </summary>
public sealed class McpCallLog(string folder, IDataProtector? protector, TimeSpan retention)
{
    public const string FileName = "mcp-log.jsonl";

    public string FilePath => Path.Combine(folder, FileName);

    /// <summary>Suma una llamada. Un error de disco no corta la herramienta: el registro es para mirar después.</summary>
    public void Add(McpCall call)
    {
        try
        {
            PrivateFiles.EnsureFolder(folder);
            var line = Encoding.UTF8.GetBytes(Serialize(call) + "\n");
            var isNew = !File.Exists(FilePath);
            using (var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                stream.Write(line); // de una vez: dos conectores a la vez no mezclan renglones
            if (isNew) PrivateFiles.Restrict(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Saca las llamadas más viejas que la retención (las del archivo que ve este proceso).</summary>
    public void Purge()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var limit = DateTime.UtcNow - retention;
            var lines = File.ReadAllLines(FilePath);
            var kept = lines.Where(l => TimeOf(l) is not { } time || time >= limit).ToList();
            if (kept.Count < lines.Length) File.WriteAllLines(FilePath, kept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Las últimas llamadas, la más nueva primero, de este registro y de las copias de los paquetes MSIX.</summary>
    /// <param name="packages">%LOCALAPPDATA%\Packages (null: el del sistema, solo en Windows).</param>
    /// <param name="roaming">%APPDATA% (null: el del sistema).</param>
    public IReadOnlyList<McpCall> Read(int max = 200, string? packages = null, string? roaming = null)
    {
        var limit = DateTime.UtcNow - retention;
        var calls = new List<McpCall>();
        foreach (var file in Files(packages, roaming))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                    if (Deserialize(line) is { } call && call.Time >= limit) calls.Add(call);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return calls.OrderByDescending(c => c.Time).Take(max).ToList();
    }

    private IEnumerable<string> Files(string? packages, string? roaming)
    {
        if (File.Exists(FilePath)) yield return FilePath;
        packages ??= OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages")
            : null;
        roaming ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (packages is null || !Directory.Exists(packages) || string.IsNullOrEmpty(roaming)) yield break;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(roaming));
        var data = Path.GetFullPath(folder);
        if (!data.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) yield break;
        var relative = Path.GetRelativePath(root, data);
        IEnumerable<string> copies;
        try
        {
            copies = Directory.EnumerateDirectories(packages)
                .Select(p => Path.Combine(p, "LocalCache", "Roaming", relative, FileName))
                .Where(File.Exists).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var copy in copies)
            if (!string.Equals(Path.GetFullPath(copy), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase)) yield return copy;
    }

    private string Serialize(McpCall call)
    {
        var node = new JsonObject
        {
            ["time"] = call.Time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["tool"] = call.Tool,
            ["database"] = call.Database,
            ["ms"] = call.Milliseconds,
            ["ok"] = call.Ok,
            ["chars"] = call.Characters,
        };
        if (protector is not null && (call.Arguments is not null || call.Error is not null))
        {
            var secret = new JsonObject { ["args"] = call.Arguments, ["error"] = call.Error }.ToJsonString();
            node["protected"] = Convert.ToBase64String(protector.Protect(secret));
        }
        return node.ToJsonString();
    }

    private McpCall? Deserialize(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject o || TimeOf(o) is not { } time) return null;
            string? args = null, error = null;
            if (protector is not null && o["protected"]?.GetValue<string>() is { } blob)
            {
                try
                {
                    if (JsonNode.Parse(protector.Unprotect(Convert.FromBase64String(blob))) is JsonObject secret)
                    {
                        args = secret["args"]?.GetValue<string>();
                        error = secret["error"]?.GetValue<string>();
                    }
                }
                catch (Exception) { /* de otro usuario o de otro equipo: se muestra sin el detalle */ }
            }
            return new McpCall(time.ToLocalTime(), o["tool"]?.GetValue<string>() ?? "?", o["database"]?.GetValue<string>(), args,
                o["ms"]?.GetValue<long>() ?? 0, o["ok"]?.GetValue<bool>() ?? false, o["chars"]?.GetValue<int>() ?? 0, error);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null; // un renglón cortado (el disco se llenó): se saltea
        }
    }

    private static DateTime? TimeOf(string line)
    {
        try
        {
            return JsonNode.Parse(line) is JsonObject o ? TimeOf(o) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTime? TimeOf(JsonObject o) =>
        o["time"]?.GetValue<string>() is { } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time.ToUniversalTime()
            : null;
}
