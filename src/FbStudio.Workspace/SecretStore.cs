using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace FbStudio.Workspace;

/// <summary>Contraseñas y otros secretos, siempre cifrados por el sistema operativo.</summary>
public interface ISecretStore
{
    void Set(string key, string secret);
    string? Get(string key);
    void Remove(string key);

    /// <summary>Si lo guardado sigue ahí la próxima vez que se abre FbStudio (sin llavero del sistema, no).</summary>
    bool IsPersistent => true;
}

/// <summary>Windows: DPAPI con alcance de usuario (solo este usuario en esta PC puede descifrar).</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string folder) : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FbStudio.SecretStore.v1");

    public void Set(string key, string secret)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(PathFor(key), ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));
    }

    public string? Get(string key)
    {
        var path = PathFor(key);
        return File.Exists(path)
            ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser))
            : null;
    }

    public void Remove(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string key)
    {
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(folder, safe + ".bin");
    }
}

/// <summary>
/// Sin llavero del sistema (un Linux sin GNOME Keyring ni KWallet, por ejemplo): los secretos quedan solo en memoria
/// mientras FbStudio está abierto. Nunca se escriben en claro; la app avisa que no se recuerdan.
/// </summary>
public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public bool IsPersistent => false;

    public void Set(string key, string secret)
    {
        lock (_secrets) _secrets[key] = secret;
    }

    public string? Get(string key)
    {
        lock (_secrets) return _secrets.GetValueOrDefault(key);
    }

    public void Remove(string key)
    {
        lock (_secrets) _secrets.Remove(key);
    }
}
