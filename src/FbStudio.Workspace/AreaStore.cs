using System.Collections.Concurrent;
using System.Text.Json;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;

namespace FbStudio.Workspace;

/// <summary>Lo leído de un archivo de áreas.</summary>
/// <param name="Path">De dónde salió; null = en memoria (pruebas, sin carpeta).</param>
/// <param name="Stamp">Cuándo se escribió el archivo (para avisar si otro lo cambió después); null si todavía no existe.</param>
/// <param name="Error">El archivo está pero no se pudo leer (roto, sin permiso, carpeta de red caída).</param>
public sealed record AreaLoad(SchemaAreaFile File, string? Path, DateTime? Stamp, string? Error = null);

/// <summary>Cómo terminó un guardado: si otro cambió el archivo desde que se leyó, no se pisa (<see cref="Conflict"/>).</summary>
public sealed record AreaSave(bool Conflict, DateTime? Stamp);

/// <summary>
/// Áreas de cada base (F-9), cada una en su archivo JSON: %APPDATA%\FbStudio\areas\&lt;id&gt;.json, o el que diga
/// <see cref="ConnectionProfile.AreasFile"/> (p. ej. uno en una carpeta de red que usa todo el equipo). El árbol y la
/// búsqueda usan lo último leído; la pestaña de áreas vuelve a leer el archivo al abrirse. Sin carpeta, las áreas de
/// las bases sin archivo propio viven en memoria (pruebas).
/// </summary>
public sealed class AreaStore(string? folder)
{
    // El árbol lee el archivo fuera del hilo de la interfaz (puede estar en una carpeta de red).
    private readonly ConcurrentDictionary<Guid, SchemaAreaFile> _memory = new();
    private readonly ConcurrentDictionary<string, SchemaAreaFile> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Se guardaron áreas (el árbol y la búsqueda se rearman).</summary>
    public event Action? Changed;

    public string? DefaultPath(Guid profileId) => folder is null ? null : Path.Combine(folder, "areas", $"{profileId:N}.json");

    public string? PathFor(ConnectionProfile profile) =>
        string.IsNullOrWhiteSpace(profile.AreasFile) ? DefaultPath(profile.Id) : profile.AreasFile;

    /// <summary>Lee el archivo de la base (siempre del disco).</summary>
    public AreaLoad Load(ConnectionProfile profile) =>
        PathFor(profile) is { } path ? LoadFile(path) : new AreaLoad(_memory.GetValueOrDefault(profile.Id) ?? new SchemaAreaFile(), null, null);

    public AreaLoad LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AreaLoad(_cache[path] = new SchemaAreaFile(), path, null);
            var stamp = File.GetLastWriteTimeUtc(path);
            var file = Clean(JsonSerializer.Deserialize(File.ReadAllText(path), WorkspaceJsonContext.Default.SchemaAreaFile));
            _cache[path] = file;
            return new AreaLoad(file, path, stamp);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new AreaLoad(new SchemaAreaFile(), path, null, ex.Message);
        }
    }

    /// <summary>Las áreas para el árbol y la búsqueda: lo último leído o guardado (la primera vez, del disco).</summary>
    public IReadOnlyList<SchemaArea> Areas(ConnectionProfile profile)
    {
        if (PathFor(profile) is not { } path) return _memory.GetValueOrDefault(profile.Id)?.Areas ?? [];
        return _cache.TryGetValue(path, out var cached) ? cached.Areas : LoadFile(path).File.Areas;
    }

    /// <summary>
    /// Guarda las áreas en <paramref name="path"/> (null = en memoria, para la base <paramref name="profileId"/>). Si el
    /// archivo cambió desde que se leyó (<paramref name="expectedStamp"/>: otra persona lo guardó), no lo pisa salvo con
    /// <paramref name="force"/>. Un error de disco o de red sale como excepción.
    /// </summary>
    public AreaSave Save(string? path, Guid profileId, SchemaAreaFile file, DateTime? expectedStamp, bool force = false)
    {
        file = Clean(file);
        if (path is null)
        {
            _memory[profileId] = file;
            Changed?.Invoke();
            return new AreaSave(false, null);
        }
        if (!force && File.Exists(path) && File.GetLastWriteTimeUtc(path) != expectedStamp)
            return new AreaSave(true, File.GetLastWriteTimeUtc(path));
        // El de esta PC queda privado; uno compartido (carpeta de red) se deja con los permisos de esa carpeta.
        var own = folder is not null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
        {
            if (own) PrivateFiles.EnsureFolder(dir);
            else Directory.CreateDirectory(dir);
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, WorkspaceJsonContext.Default.SchemaAreaFile));
        if (own) PrivateFiles.Restrict(tmp);
        File.Move(tmp, path, overwrite: true);
        _cache[path] = file;
        Changed?.Invoke();
        return new AreaSave(false, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>Sin nombres en blanco ni reglas vacías (un archivo escrito a mano puede traer nulls).</summary>
    private static SchemaAreaFile Clean(SchemaAreaFile? file) => new()
    {
        Version = file?.Version ?? 1,
        Areas = (file?.Areas ?? []).Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Name)).Select(a => a with
        {
            Name = a.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(a.Description) ? null : a.Description.Trim(),
            Rules = Patterns(a.Rules),
            Exclude = Patterns(a.Exclude),
            Color = ColorPalette.IsAllowed(null, a.Color) ? a.Color!.Trim().ToUpperInvariant() : null, // sin rojos, que son de producción
        }).ToList(),
    };

    private static IReadOnlyList<string> Patterns(IReadOnlyList<string>? list) =>
        (list ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
