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
    public const string DiscussionsUrl = RepositoryUrl + "/discussions";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";
    public const string ThirdPartyUrl = RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.md";

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
