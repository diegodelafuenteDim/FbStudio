using System.Text.Json;
using FbStudio.Core.Diagrams;

namespace FbStudio.Workspace;

/// <summary>Lee y escribe los diagramas guardados (.fbsdiagram): JSON legible, para versionarlo o pasarlo a otro.</summary>
public static class DiagramFiles
{
    public static void Write(string path, DiagramFile file) =>
        File.WriteAllText(path, JsonSerializer.Serialize(file, WorkspaceJsonContext.Default.DiagramFile));

    /// <summary>El diagrama del archivo; un archivo que no es un diagrama (o está roto) da InvalidDataException.</summary>
    public static DiagramFile Read(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(path), WorkspaceJsonContext.Default.DiagramFile);
            if (file is null || string.IsNullOrWhiteSpace(file.Source)) throw new InvalidDataException(path);
            // Escrito a mano con nulls: se limpia.
            return file with
            {
                Added = file.Added?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [],
                Removed = file.Removed?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [],
                ShownHubs = file.ShownHubs?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [],
                Path = file.Path?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [],
                Positions = file.Positions?.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, DiagramPosition>(),
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }
}
