using System.Reflection;

namespace FbStudio.App;

/// <summary>Datos del producto para «Acerca de» y los enlaces (repositorio, pedidos, licencia).</summary>
public static class AppInfo
{
    public const string Name = "FbStudio";
    public const string Author = "Diego de la Fuente";
    public const string Year = "2026";

    public const string RepositoryUrl = "https://github.com/diegodelafuenteDim/FbStudio";
    public const string IssuesUrl = RepositoryUrl + "/issues/new/choose";
    public const string BugReportUrl = RepositoryUrl + "/issues/new?template=bug_report.yml";
    public const string FeatureRequestUrl = RepositoryUrl + "/issues/new?template=feature_request.yml";
    public const string DiscussionsUrl = RepositoryUrl + "/discussions";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";
    public const string ThirdPartyUrl = RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.md";
    public const string ChangeLogUrl = RepositoryUrl + "/blob/main/CHANGELOG.md";

    /// <summary>«0.1.0» y, si se compiló desde git, el commit («0.1.0 · 3d75868»).</summary>
    public static string Version
    {
        get
        {
            var informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informational)) return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var plus = informational.IndexOf('+');
            if (plus < 0) return informational;
            var commit = informational[(plus + 1)..];
            return $"{informational[..plus]} · {(commit.Length > 7 ? commit[..7] : commit)}";
        }
    }

    /// <summary>Versión y sistema, para pegar en un reporte: «FbStudio 0.2.0 · 3d75868 · Windows 11 … · X64».</summary>
    public static string ReportInfo =>
        $"{Name} {Version} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";

    /// <summary>Solo el número («0.1.0»), sin el commit: el de la sección del registro de cambios.</summary>
    public static string VersionNumber => Version.Split(' ')[0];

    private static IReadOnlyList<Core.ChangeLogVersion>? _changeLog;

    /// <summary>El registro de cambios que va adentro de la app (CHANGELOG.md); vacío si no está.</summary>
    public static IReadOnlyList<Core.ChangeLogVersion> ChangeLog => _changeLog ??= LoadChangeLog();

    private static IReadOnlyList<Core.ChangeLogVersion> LoadChangeLog()
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("FbStudio.CHANGELOG.md");
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        return Core.ChangeLog.Parse(reader.ReadToEnd());
    }

    /// <summary>Componentes de terceros que van con la app (el detalle está en THIRD-PARTY-NOTICES.md).</summary>
    public static IReadOnlyList<(string Name, string License)> ThirdParty { get; } =
    [
        ("Avalonia", "MIT"),
        ("AvaloniaEdit + TextMate", "MIT"),
        ("Semi.Avalonia", "MIT"),
        ("Material.Icons.Avalonia", "MIT"),
        ("CommunityToolkit.Mvvm", "MIT"),
        ("Msagl (Microsoft Automatic Graph Layout)", "MIT"),
        ("FirebirdSql.Data.FirebirdClient", "IDPL 1.0"),
        ("Microsoft.Data.Sqlite / SQLitePCLRaw", "MIT / Apache-2.0"),
        ("SkiaSharp / HarfBuzzSharp", "MIT"),
        ("ANGLE", "BSD-3-Clause"),
    ];
}
