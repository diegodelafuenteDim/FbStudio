using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FbStudio.Core.Profiles;
using FbStudio.Core.Resources;

namespace FbStudio.Workspace;

/// <summary>Lo que se guarda en el archivo. Sin contraseñas, los registros van legibles; con contraseñas, todo cifrado.</summary>
public sealed record ProfileBackupFile
{
    public string Format { get; init; } = ProfileBackup.FormatName;
    public int Version { get; init; } = ProfileBackup.CurrentVersion;
    public DateTime CreatedAt { get; init; }

    /// <summary>Cuántos registros trae (se ve antes de pedir la frase).</summary>
    public int Count { get; init; }

    public List<ConnectionProfile>? Profiles { get; init; }
    public EncryptedBackup? Encrypted { get; init; }

    [JsonIgnore]
    public bool IsEncrypted => Encrypted is not null;
}

/// <summary>El contenido cifrado y cómo se derivó la clave de la frase.</summary>
public sealed record EncryptedBackup(string Kdf, int Iterations, string Salt, string Data);

/// <summary>Lo que hay adentro: los registros y, si se incluyeron, sus contraseñas (por Id del registro).</summary>
public sealed record ProfileBackupContent
{
    public List<ConnectionProfile> Profiles { get; init; } = [];
    public Dictionary<Guid, string> Passwords { get; init; } = [];

    /// <summary>Registros que vienen sin usuario (se exportaron sin él).</summary>
    [JsonIgnore]
    public int WithoutUser => Profiles.Count(p => string.IsNullOrWhiteSpace(p.User));
}

/// <summary>Un registro del archivo y el que ya existe para la misma base (si hay).</summary>
public sealed record ProfileImportItem(ConnectionProfile Profile, string? Password, ConnectionProfile? Existing);

/// <param name="PasswordsForgotten">Registros que cambiaron de servidor, base o usuario sin traer contraseña: la guardada se olvidó.</param>
/// <param name="KeptProtection">Registros que conservaron su protección (solo lectura, producción, cifrado) que el archivo bajaba.</param>
public sealed record ProfileImportResult(int Added, int Updated, int Skipped, int Invalid, int WithPassword, int PasswordsForgotten = 0,
    int KeptProtection = 0);

/// <summary>
/// Backup y restore de los registros de conexión (F-22, D-55): un archivo para guardarlos o para pasarlos a otra PC, o
/// a Linux y macOS (cada sistema guarda las contraseñas en su llavero, así que no se pueden copiar de otro modo).
/// <list type="bullet">
/// <item>Sin contraseñas: JSON legible, con o sin el usuario.</item>
/// <item>Con contraseñas: todo el contenido (registros y contraseñas) cifrado con una frase que elige el usuario, con
/// AES-256 + HMAC-SHA256 y la clave derivada por PBKDF2-SHA256. Va todo cifrado, y no solo las contraseñas, para que
/// nadie pueda cambiar el servidor de un registro en el archivo y quedarse con su contraseña.</item>
/// </list>
/// La frase no se guarda en ningún lado: sin ella el archivo no se puede abrir.
/// </summary>
public static class ProfileBackup
{
    public const string FormatName = "FbStudio.Profiles";
    public const int CurrentVersion = 1;
    public const int MinPassphraseLength = 10;

    /// <summary>Vueltas de PBKDF2-SHA256 (lo que recomienda OWASP): unos décimos de segundo por intento.</summary>
    public const int Iterations = 600_000;

    private const string Kdf = "PBKDF2-SHA256";
    private const int MinIterations = 100_000;
    private const int MaxIterations = 10_000_000;
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const string Purpose = "FbStudio.ProfilesBackup";

    /// <summary>
    /// Guarda los registros. Sin <paramref name="includeUser"/> van sin usuario (y entonces sin contraseña); con
    /// <paramref name="passphrase"/>, con sus contraseñas y todo cifrado.
    /// </summary>
    public static void Export(string path, IReadOnlyList<ConnectionProfile> profiles, bool includeUser, Func<Guid, string?> passwords, string? passphrase)
    {
        var list = profiles.Select(p => includeUser ? p : p with { User = "" }).ToList();
        ProfileBackupFile file;
        if (passphrase is null || !includeUser)
        {
            file = new ProfileBackupFile { CreatedAt = DateTime.UtcNow, Count = list.Count, Profiles = list };
        }
        else
        {
            if (passphrase.Length < MinPassphraseLength) throw new ArgumentException(string.Format(CoreStrings.BackupShortPhrase, MinPassphraseLength));
            var content = new ProfileBackupContent
            {
                Profiles = list,
                Passwords = list.Select(p => (p.Id, Password: passwords(p.Id))).Where(x => x.Password is not null)
                    .ToDictionary(x => x.Id, x => x.Password!),
            };
            var salt = RandomNumberGenerator.GetBytes(16);
            var data = Protector(passphrase, salt, Iterations).Protect(JsonSerializer.Serialize(content, WorkspaceJsonContext.Default.ProfileBackupContent));
            file = new ProfileBackupFile
            {
                CreatedAt = DateTime.UtcNow, Count = list.Count,
                Encrypted = new EncryptedBackup(Kdf, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(data)),
            };
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, WorkspaceJsonContext.Default.ProfileBackupFile));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Lee el archivo (sin abrir lo cifrado): para saber cuántos registros trae y si pide la frase.</summary>
    public static ProfileBackupFile Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidOperationException(CoreStrings.BackupNotOurs);
        ProfileBackupFile? file;
        try { file = JsonSerializer.Deserialize(File.ReadAllText(path), WorkspaceJsonContext.Default.ProfileBackupFile); }
        catch (JsonException) { throw new InvalidOperationException(CoreStrings.BackupNotOurs); }
        if (file is null || file.Format != FormatName || (file.Profiles is null && file.Encrypted is null))
            throw new InvalidOperationException(CoreStrings.BackupNotOurs);
        if (file.Version > CurrentVersion) throw new InvalidOperationException(string.Format(CoreStrings.BackupNewer, file.Version));
        return file;
    }

    /// <summary>Los registros del archivo; si está cifrado, con la frase (si no es la correcta, avisa).</summary>
    public static ProfileBackupContent Open(ProfileBackupFile file, string? passphrase)
    {
        if (file.Encrypted is not { } e) return new ProfileBackupContent { Profiles = file.Profiles ?? [] };
        if (string.IsNullOrEmpty(passphrase)) throw new InvalidOperationException(CoreStrings.BackupWrongPhrase);
        if (e.Kdf != Kdf || e.Iterations is < MinIterations or > MaxIterations) throw new InvalidOperationException(CoreStrings.BackupNotOurs);
        try
        {
            var json = Protector(passphrase, Convert.FromBase64String(e.Salt), e.Iterations).Unprotect(Convert.FromBase64String(e.Data));
            return JsonSerializer.Deserialize(json, WorkspaceJsonContext.Default.ProfileBackupContent) ?? new ProfileBackupContent();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            throw new InvalidOperationException(CoreStrings.BackupWrongPhrase);
        }
    }

    /// <summary>
    /// Qué pasa con cada registro del archivo: la base ya registrada es la del mismo Id (el mismo registro, importado
    /// de nuevo) o la misma base en el mismo servidor y con el mismo usuario.
    /// </summary>
    public static IReadOnlyList<ProfileImportItem> Plan(IReadOnlyList<ConnectionProfile> existing, ProfileBackupContent content) =>
        (content.Profiles ?? []).Where(p => p is not null).Select(p => new ProfileImportItem(p, content.Passwords?.GetValueOrDefault(p.Id),
            existing.FirstOrDefault(x => x.Id == p.Id) ?? existing.FirstOrDefault(x => SameDatabase(x, p)))).ToList();

    // Un archivo escrito a mano puede traer nulls: se comparan como vacíos.
    private static bool SameDatabase(ConnectionProfile a, ConnectionProfile b) =>
        string.Equals(a.Host?.Trim(), b.Host?.Trim(), StringComparison.OrdinalIgnoreCase) && a.Port == b.Port
        && string.Equals(a.Database?.Trim(), b.Database?.Trim(), StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(b.User) || string.Equals(a.User?.Trim(), b.User.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Guarda los registros del plan. Los que ya existen se actualizan (conservando su Id y su contraseña si el archivo
    /// no trae otra) o se dejan como están. A los que vienen sin usuario se les pone <paramref name="user"/> y
    /// <paramref name="password"/>, si se dieron. Un archivo no puede usarse para llevar una contraseña guardada a otro
    /// servidor (si cambia el servidor, la base o el usuario y no trae contraseña, la guardada se olvida) ni para bajar
    /// protecciones: solo lectura, producción y el cifrado de la conexión se conservan (se cambian a mano en el registro).
    /// El archivo de áreas no se importa: cada PC elige el suyo (una ruta de red ajena se leería sola al conectar).
    /// </summary>
    public static ProfileImportResult Apply(ProfileStore store, IReadOnlyList<ProfileImportItem> items, bool updateExisting, string? user = null, string? password = null)
    {
        int added = 0, updated = 0, skipped = 0, invalid = 0, withPassword = 0, forgotten = 0, kept = 0;
        var names = store.Load().Select(p => p.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var item in items)
        {
            var p = item.Profile;
            if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Host) || string.IsNullOrWhiteSpace(p.Database) || p.Port is < 1 or > 65535)
            {
                invalid++;
                continue;
            }
            if (item.Existing is not null && !updateExisting)
            {
                skipped++;
                continue;
            }
            var secret = item.Password;
            if (string.IsNullOrWhiteSpace(p.User))
            {
                if (item.Existing is { } e) p = p with { User = e.User };
                else
                {
                    p = p with { User = user?.Trim() ?? "" };
                    secret = string.IsNullOrEmpty(password) ? null : password;
                }
            }
            var forget = false;
            if (item.Existing is { } current)
            {
                var safer = p with
                {
                    ReadOnly = p.ReadOnly || current.ReadOnly,
                    Environment = current.Environment == ProfileEnvironment.Produccion ? ProfileEnvironment.Produccion : p.Environment,
                    WireCrypt = Strength(p.WireCrypt) < Strength(current.WireCrypt) ? current.WireCrypt : p.WireCrypt,
                };
                if (safer != p) kept++;
                p = safer with { Id = current.Id, AreasFile = current.AreasFile };
                forget = secret is null && !SameTarget(current, p);
                updated++;
            }
            else
            {
                p = p with { AreasFile = null };
                if (names.Contains(p.Name))
                {
                    var n = 2;
                    while (names.Contains($"{p.Name} ({n})")) n++;
                    p = p with { Name = $"{p.Name} ({n})" };
                }
                names.Add(p.Name);
                added++;
            }
            if (secret is not null) withPassword++;
            store.Upsert(p, secret); // sin contraseña nueva se conserva la que había…
            if (forget)
            {
                store.ForgetPassword(p.Id); // …salvo que ahora apunte a otro servidor, base o usuario
                forgotten++;
            }
        }
        return new ProfileImportResult(added, updated, skipped, invalid, withPassword, forgotten, kept);
    }

    private static bool SameTarget(ConnectionProfile a, ConnectionProfile b) =>
        string.Equals(a.Host.Trim(), b.Host.Trim(), StringComparison.OrdinalIgnoreCase) && a.Port == b.Port
        && string.Equals(a.Database.Trim(), b.Database.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.User.Trim(), b.User.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Disabled &lt; Enabled &lt; Required.</summary>
    private static int Strength(WireCryptMode mode) => mode switch
    {
        WireCryptMode.Required => 2,
        WireCryptMode.Enabled => 1,
        _ => 0,
    };

    private static AesDataProtector Protector(string passphrase, byte[] salt, int iterations) =>
        new(Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32), Purpose);
}
