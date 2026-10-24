namespace FbStudio.Core.Metadata;

/// <summary>Versión del motor y datos generales de la base (MON$DATABASE), leídos al conectar.</summary>
public sealed record ServerInfo(string EngineVersion, int OdsMajor, int OdsMinor, int Dialect, int PageSize, string? Charset)
{
    /// <summary>Versión mayor del motor («3.0.11» → 3); 0 si no se pudo leer.</summary>
    public int Major => MajorOf(EngineVersion);

    /// <summary>Por ahora FbStudio se prueba contra Firebird 3.x.</summary>
    public bool IsTested => Major == 3;

    public string Display => $"Firebird {EngineVersion}";

    /// <summary>De dónde sale el tipo de servidor (F-16); null si no se pudo leer.</summary>
    public ServerModeEvidence? ModeEvidence { get; init; }

    public ServerMode Mode => ModeEvidence?.Mode ?? ServerMode.Unknown;

    /// <summary>Si la conexión con el servidor va cifrada (WIRE_ENCRYPTED); null si no se pudo saber.</summary>
    public bool? WireEncrypted { get; init; }

    /// <summary>«Conexión cifrada» / «Conexión sin cifrar», o null.</summary>
    public string? WireText => WireEncrypted switch
    {
        true => Resources.CoreStrings.ServerWireEncrypted,
        false => Resources.CoreStrings.ServerWireNotEncrypted,
        null => null,
    };

    /// <summary>Lo que dice la API de servicios del servidor: «Firebird/Windows/AMD/Intel/x64».</summary>
    public string? Implementation { get; init; }

    /// <summary>«Windows x64», «Linux x64».</summary>
    public string? Platform => ServerPlatform.Describe(Implementation);

    /// <summary>Páginas de caché de la base (MON$PAGE_BUFFERS): compartidas en SuperServer, por conexión en los demás.</summary>
    public int PageBuffers { get; init; }

    /// <summary>Si la base quedó en modo backup de nbackup (F-29): se avisa al conectar y en el árbol.</summary>
    public BackupState BackupState { get; init; }

    /// <summary>Para el árbol: «Firebird 3.0.11 · Classic».</summary>
    public string ShortDisplay => Mode == ServerMode.Unknown ? Display : $"{Display} · {ServerModes.Label(Mode)}";

    /// <summary>«Firebird 3.0.11 · Classic · Windows x64»: lo que se sabe del servidor, en una línea.</summary>
    public string Heading => string.Join(" · ", new[] { ShortDisplay, Platform }.Where(s => s is not null));

    /// <summary>Para el tooltip de la base en el árbol.</summary>
    public string Summary =>
        string.Format(Resources.CoreStrings.ServerSummary, Heading, $"{OdsMajor}.{OdsMinor}", Dialect, PageSize, Charset ?? "NONE");

    public string? Warning => string.Join(" ", new[]
    {
        IsTested ? null : string.Format(Resources.CoreStrings.ServerUntested, Display),
        BackupStates.Warning(BackupState),
    }.Where(w => w is not null)) is { Length: > 0 } warning ? warning : null;

    public static int MajorOf(string? version)
    {
        var digits = (version ?? "").TrimStart().Split('.', 2)[0];
        return int.TryParse(digits, out var major) ? major : 0;
    }
}
