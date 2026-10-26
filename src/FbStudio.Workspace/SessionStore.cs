using System.Text.Json;

namespace FbStudio.Workspace;

/// <summary>Una pestaña de consulta al cerrar: su base, su título y su texto (o el archivo .sql de donde se vuelve a leer).</summary>
public sealed record SessionTab(Guid? ProfileId, string Title, string? FilePath, string? Text);

/// <summary>Las pestañas de consulta que quedaron abiertas al cerrar FbStudio y cuál estaba elegida.</summary>
public sealed record SessionFile
{
    public int Version { get; set; } = 1;
    public IReadOnlyList<SessionTab> Tabs { get; set; } = [];
    public int Selected { get; init; }
}

/// <summary>
/// Recordar las pestañas al cerrar: las consultas (con su texto, que puede tener datos de pacientes) se guardan cifradas
/// como el historial, en %APPDATA%\FbStudio\session.bin. Sin cifrado disponible (Linux sin llavero) o sin carpeta
/// (pruebas), viven solo en memoria. Un archivo roto se ignora.
/// </summary>
public sealed class SessionStore(string? folder, IDataProtector? protector)
{
    private SessionFile? _memory;

    public string? FilePath => folder is null || protector is null ? null : Path.Combine(folder, "session.bin");

    public void Save(SessionFile session)
    {
        if (FilePath is not { } path)
        {
            _memory = session;
            return;
        }
        PrivateFiles.EnsureFolder(folder!);
        File.WriteAllBytes(path, protector!.Protect(JsonSerializer.Serialize(session, WorkspaceJsonContext.Default.SessionFile)));
        PrivateFiles.Restrict(path);
    }

    public SessionFile? Load()
    {
        if (FilePath is not { } path) return _memory;
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize(protector!.Unprotect(File.ReadAllBytes(path)), WorkspaceJsonContext.Default.SessionFile);
        }
        catch (Exception ex) when (ex is JsonException or IOException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Olvida lo guardado (la opción está apagada).</summary>
    public void Clear()
    {
        _memory = null;
        if (FilePath is { } path && File.Exists(path)) File.Delete(path);
    }
}
