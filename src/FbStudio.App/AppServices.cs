using FbStudio.Workspace;

namespace FbStudio.App;

/// <summary>Composición de dependencias (chica a propósito: sin contenedor de DI hasta que haga falta).</summary>
public static class AppServices
{
    /// <summary>
    /// Dónde se guardan las contraseñas: DPAPI en Windows, el Llavero en macOS y el llavero del escritorio en Linux
    /// (GNOME Keyring, KWallet… con libsecret-tools). Sin llavero, solo en memoria mientras FbStudio está abierto:
    /// nunca en claro.
    /// </summary>
    public static ISecretStore CreateSecretStore(string folder)
    {
        if (OperatingSystem.IsWindows()) return new DpapiSecretStore(Path.Combine(folder, "secrets"));
        if (OperatingSystem.IsMacOS() && MacKeychainStore.IsAvailable()) return new MacKeychainStore();
        if (OperatingSystem.IsLinux() && LinuxSecretStore.IsAvailable()) return new LinuxSecretStore();
        return new MemorySecretStore();
    }

    public static ProfileStore CreateProfileStore(string? folder = null)
    {
        folder ??= ProfileStore.DefaultFolder;
        return new ProfileStore(folder, CreateSecretStore(folder));
    }

    /// <summary>Opciones del sistema (settings.json, junto a los perfiles).</summary>
    public static SettingsStore CreateSettingsStore(string? folder = null) => new(folder ?? ProfileStore.DefaultFolder);

    /// <summary>Consultas propias del monitoreo (monitor-queries.json, junto a los perfiles).</summary>
    public static MonitorQueryStore CreateMonitorQueryStore(string? folder = null) => new(folder ?? ProfileStore.DefaultFolder);

    /// <summary>Orden y anchos de las columnas de la pestaña Datos por tabla (grid-layouts.json, junto a los perfiles).</summary>
    public static GridLayoutStore CreateGridLayoutStore(string? folder = null) => new(folder ?? ProfileStore.DefaultFolder);

    /// <summary>Áreas de cada base (F-9): areas\&lt;id&gt;.json junto a los perfiles, o el archivo compartido que elija la base.</summary>
    public static AreaStore CreateAreaStore(string? folder = null) => new(folder ?? ProfileStore.DefaultFolder);

    /// <summary>
    /// Historial cifrado; la retención es una opción del sistema (90 días por defecto). En Windows con DPAPI; en Linux y
    /// macOS con AES y su clave en el llavero. Sin llavero no hay historial (null): no se podría volver a leer.
    /// </summary>
    public static HistoryStore? CreateHistoryStore(string? folder = null, int retentionDays = 90)
    {
        folder ??= ProfileStore.DefaultFolder;
        return Protector(folder) is { } protector ? new HistoryStore(Path.Combine(folder, "history.db"), protector, TimeSpan.FromDays(retentionDays)) : null;
    }

    /// <summary>Las pestañas de la última vez, cifradas como el historial (sin cifrado, solo en memoria).</summary>
    public static SessionStore CreateSessionStore(string? folder = null)
    {
        folder ??= ProfileStore.DefaultFolder;
        return new SessionStore(folder, Protector(folder));
    }

    private static IDataProtector? Protector(string folder) => OperatingSystem.IsWindows()
        ? new DpapiDataProtector()
        : CreateSecretStore(folder) is { IsPersistent: true } secrets ? AesDataProtector.FromSecretStore(secrets) : null;
}
