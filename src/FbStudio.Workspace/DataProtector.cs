using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace FbStudio.Workspace;

/// <summary>Cifra textos que pueden contener datos de pacientes (SQL del historial, mensajes de error).</summary>
public interface IDataProtector
{
    byte[] Protect(string text);
    string Unprotect(byte[] data);
}

[SupportedOSPlatform("windows")]
public sealed class DpapiDataProtector : IDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FbStudio.History.v1");

    public byte[] Protect(string text) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.CurrentUser);

    public string Unprotect(byte[] data) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser));
}

/// <summary>
/// Linux y macOS (no tienen DPAPI): AES-256 en modo CBC más HMAC-SHA256 (cifrar y después firmar), con una clave
/// maestra al azar que se guarda en el llavero del sistema. Formato: versión (1) · IV (16) · cifrado · HMAC (32).
/// </summary>
public sealed class AesDataProtector : IDataProtector
{
    public const string KeyName = "history-key";
    private const byte Version = 1;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _macKey;

    /// <param name="purpose">Para qué es la clave: cada uso deriva sus propias subclaves (el historial, el backup de registros).</param>
    public AesDataProtector(byte[] masterKey, string purpose = "FbStudio.History")
    {
        if (masterKey.Length != 32) throw new ArgumentException("La clave tiene que tener 32 bytes.", nameof(masterKey));
        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: Encoding.UTF8.GetBytes(purpose + ".encrypt"));
        _macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: Encoding.UTF8.GetBytes(purpose + ".mac"));
    }

    /// <summary>La clave del historial guardada en el llavero; la primera vez se crea al azar.</summary>
    public static AesDataProtector FromSecretStore(ISecretStore secrets)
    {
        if (secrets.Get(KeyName) is { } stored) return new AesDataProtector(Convert.FromBase64String(stored));
        var key = RandomNumberGenerator.GetBytes(32);
        secrets.Set(KeyName, Convert.ToBase64String(key));
        return new AesDataProtector(key);
    }

    public byte[] Protect(string text)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        var iv = RandomNumberGenerator.GetBytes(16);
        var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(text), iv);
        var body = new byte[1 + iv.Length + cipher.Length];
        body[0] = Version;
        iv.CopyTo(body, 1);
        cipher.CopyTo(body, 1 + iv.Length);
        return [.. body, .. HMACSHA256.HashData(_macKey, body)];
    }

    public string Unprotect(byte[] data)
    {
        if (data.Length < 1 + 16 + 16 + 32 || data[0] != Version) throw new CryptographicException("Formato desconocido.");
        var body = data.AsSpan(0, data.Length - 32);
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_macKey, body), data.AsSpan(data.Length - 32)))
            throw new CryptographicException("El dato fue alterado o se cifró con otra clave.");
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        return Encoding.UTF8.GetString(aes.DecryptCbc(body[17..], body[1..17]));
    }
}
