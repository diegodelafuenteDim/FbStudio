using System.Text.Json;
using System.Text.Json.Serialization;
using FbStudio.Core.Profiles;

namespace FbStudio.Workspace;

/// <summary>
/// Perfiles en profiles.json (sin contraseñas), en %APPDATA%\FbStudio (Windows), ~/.config/FbStudio (Linux y macOS).
/// Las contraseñas van al <see cref="ISecretStore"/>: DPAPI, el llavero del escritorio o el Llavero de macOS.
/// </summary>
public sealed class ProfileStore(string folder, ISecretStore secrets)
{
    /// <summary>Si las contraseñas se recuerdan al volver a abrir FbStudio (sin llavero del sistema, no).</summary>
    public bool SavesPasswords => secrets.IsPersistent;

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FbStudio");

    private string FilePath => Path.Combine(folder, "profiles.json");

    public IReadOnlyList<ConnectionProfile> Load()
    {
        if (!File.Exists(FilePath)) return [];
        return JsonSerializer.Deserialize(File.ReadAllText(FilePath), WorkspaceJsonContext.Default.ListConnectionProfile) ?? [];
    }

    public void Save(IEnumerable<ConnectionProfile> profiles)
    {
        PrivateFiles.EnsureFolder(folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profiles.OrderBy(p => p.Name).ToList(), WorkspaceJsonContext.Default.ListConnectionProfile));
        PrivateFiles.Restrict(tmp);
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Alta o modificación. Si <paramref name="password"/> es null se conserva la guardada.</summary>
    public void Upsert(ConnectionProfile profile, string? password)
    {
        var all = Load().Where(p => p.Id != profile.Id).Append(profile);
        Save(all);
        if (password is not null) secrets.Set(SecretKey(profile.Id), password);
    }

    public void Delete(Guid id)
    {
        Save(Load().Where(p => p.Id != id));
        secrets.Remove(SecretKey(id));
    }

    public string? GetPassword(Guid id) => secrets.Get(SecretKey(id));

    /// <summary>Se olvida la contraseña guardada (p. ej. el registro pasó a apuntar a otro servidor).</summary>
    public void ForgetPassword(Guid id) => secrets.Remove(SecretKey(id));

    private static string SecretKey(Guid id) => $"profile-{id:N}";
}

/// <summary>JSON generado en compilación (sin reflexión: compatible con trimming/AOT).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<ConnectionProfile>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<CustomMonitorQuery>))]
[JsonSerializable(typeof(Dictionary<string, GridLayoutEntry>))]
[JsonSerializable(typeof(ProfileBackupFile))]
[JsonSerializable(typeof(ProfileBackupContent))]
[JsonSerializable(typeof(FbStudio.Core.Metadata.SchemaAreaFile))]
internal sealed partial class WorkspaceJsonContext : JsonSerializerContext;
