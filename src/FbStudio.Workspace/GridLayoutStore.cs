using System.Text.Json;
using FbStudio.Core.Results;

namespace FbStudio.Workspace;

public sealed record GridLayoutEntry(GridColumnLayout Layout, DateTime Saved);

/// <summary>
/// Orden y anchos de las columnas de la pestaña Datos, por base y tabla, en %APPDATA%\FbStudio\grid-layouts.json.
/// Guarda las últimas 500 tablas tocadas. Sin carpeta, viven solo en memoria (pruebas). Un archivo roto se ignora.
/// </summary>
public sealed class GridLayoutStore(string? folder)
{
    private const int MaxEntries = 500;
    private Dictionary<string, GridLayoutEntry>? _entries;

    public string? FilePath => folder is null ? null : Path.Combine(folder, "grid-layouts.json");

    public GridColumnLayout? Get(Guid profileId, string table) => Entries().GetValueOrDefault(Key(profileId, table))?.Layout;

    public void Save(Guid profileId, string table, GridColumnLayout layout)
    {
        var all = Entries();
        all[Key(profileId, table)] = new GridLayoutEntry(layout, DateTime.UtcNow);
        if (all.Count > MaxEntries)
            foreach (var old in all.OrderBy(e => e.Value.Saved).Take(all.Count - MaxEntries).Select(e => e.Key).ToList()) all.Remove(old);
        Write(all);
    }

    public void Remove(Guid profileId, string table)
    {
        if (Entries().Remove(Key(profileId, table))) Write(Entries());
    }

    private static string Key(Guid profileId, string table) => $"{profileId:N}|{table}";

    private Dictionary<string, GridLayoutEntry> Entries()
    {
        if (_entries is not null) return _entries;
        _entries = [];
        if (FilePath is null || !File.Exists(FilePath)) return _entries;
        try
        {
            _entries = JsonSerializer.Deserialize(File.ReadAllText(FilePath), WorkspaceJsonContext.Default.DictionaryStringGridLayoutEntry) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _entries = []; // solo es cómo se veían las columnas: se vuelve a armar
        }
        return _entries;
    }

    private void Write(Dictionary<string, GridLayoutEntry> all)
    {
        if (FilePath is null) return;
        try
        {
            PrivateFiles.EnsureFolder(folder!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all, WorkspaceJsonContext.Default.DictionaryStringGridLayoutEntry));
            PrivateFiles.Restrict(tmp);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException)
        {
            // Otra instancia escribiendo al mismo tiempo: se pierde este cambio de vista, nada más.
        }
    }
}
