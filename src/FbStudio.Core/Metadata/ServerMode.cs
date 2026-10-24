using R = FbStudio.Core.Resources.CoreStrings;

namespace FbStudio.Core.Metadata;

/// <summary>Arquitectura del servidor Firebird (F-16): cómo atiende las conexiones.</summary>
public enum ServerMode
{
    /// <summary>No se pudo comparar con una segunda conexión.</summary>
    Unknown,

    /// <summary>Un proceso por conexión, cada uno con su propia caché.</summary>
    Classic,

    /// <summary>Un solo proceso, con una caché por conexión.</summary>
    SuperClassic,

    /// <summary>Un solo proceso, con una caché compartida.</summary>
    SuperServer,

    /// <summary>Un solo proceso, pero sin ver las conexiones internas del servidor no se sabe cuál de los dos es.</summary>
    SuperOrSuperClassic,
}

/// <summary>
/// Lo que muestra MON$ATTACHMENTS para deducir la arquitectura (D-53, probado en Docker con los tres modos). Classic
/// atiende cada conexión en un proceso propio: dos conexiones remotas con distinto MON$SERVER_PID lo delatan, y eso lo
/// ve cualquier usuario (sus propias conexiones siempre se ven). SuperServer y SuperClassic usan un solo proceso; solo
/// SuperServer tiene la conexión interna «Cache Writer», que se ve con SYSDBA, el rol RDB$ADMIN o siendo el dueño de la
/// base. Se cuentan solo las conexiones remotas: en SuperClassic, una herramienta embebida en el servidor (gbak o gfix
/// locales) corre en su propio proceso y haría parecer Classic.
/// </summary>
public sealed record ServerModeEvidence(long ServerPid, int RemoteAttachments, int Processes, bool CacheWriter, bool SeesEveryone)
{
    public ServerMode Mode => RemoteAttachments < 2 ? ServerMode.Unknown
        : Processes > 1 ? ServerMode.Classic
        : CacheWriter ? ServerMode.SuperServer
        : SeesEveryone ? ServerMode.SuperClassic
        : ServerMode.SuperOrSuperClassic;

    /// <summary>Cómo se supo, para mostrarlo junto al tipo.</summary>
    public string Reason => Mode switch
    {
        ServerMode.Classic => string.Format(R.ServerModeWhyClassic, RemoteAttachments, Processes),
        ServerMode.SuperServer => string.Format(R.ServerModeWhySuperServer, RemoteAttachments, ServerPid),
        ServerMode.SuperClassic => string.Format(R.ServerModeWhySuperClassic, RemoteAttachments, ServerPid),
        ServerMode.SuperOrSuperClassic => string.Format(R.ServerModeWhyShared, RemoteAttachments, ServerPid),
        _ => R.ServerModeWhyUnknown,
    };
}

public static class ServerModes
{
    /// <summary>En Classic cada lectura de MON$ le pide los datos a cada proceso: el refresco automático arranca acá.</summary>
    public const int ClassicRefreshSeconds = 10;

    /// <summary>«Classic», «SuperServer»…: para el árbol y la tarjeta del monitoreo.</summary>
    public static string Label(ServerMode mode) => mode switch
    {
        ServerMode.Classic => "Classic",
        ServerMode.SuperClassic => "SuperClassic",
        ServerMode.SuperServer => "SuperServer",
        ServerMode.SuperOrSuperClassic => "SuperServer/SuperClassic",
        _ => R.ServerModeUnknown,
    };

    /// <summary>Qué significa para quien administra el servidor.</summary>
    public static string Description(ServerMode mode) => mode switch
    {
        ServerMode.Classic => R.ServerModeClassic,
        ServerMode.SuperClassic => R.ServerModeSuperClassic,
        ServerMode.SuperServer => R.ServerModeSuperServer,
        ServerMode.SuperOrSuperClassic => R.ServerModeShared,
        _ => R.ServerModeWhyUnknown,
    };
}

/// <summary>Sistema y arquitectura del servidor, de lo que informa la API de servicios.</summary>
public static class ServerPlatform
{
    /// <summary>«Firebird/Linux/AMD/Intel/x64» → «Linux x64»; «Firebird/Windows/Intel/i386» → «Windows x86».</summary>
    public static string? Describe(string? implementation)
    {
        if (string.IsNullOrWhiteSpace(implementation)) return null;
        var parts = implementation.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return implementation.Trim();
        var os = parts[1] switch
        {
            "Darwin" or "MacOSX" => "macOS",
            var name => name,
        };
        if (parts.Length < 3) return os;
        var arch = parts[^1] switch
        {
            "i386" or "i686" or "Intel" => "x86",
            var name => name,
        };
        return $"{os} {arch}";
    }
}
