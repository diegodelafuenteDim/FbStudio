using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.Core.Manual;

namespace FbStudio.App;

/// <summary>
/// El manual (docs/manual, que va adentro de la app) en el idioma de la interfaz: Ayuda › Manual abre el índice y F1 el
/// capítulo de lo que se está usando. Se arma un HTML en la carpeta temporal y se abre en el navegador, sin internet.
/// </summary>
public static class ManualService
{
    private const string Prefix = "FbStudio.manual.";

    /// <summary>Los idiomas que trae el manual («es», «en»).</summary>
    public static IReadOnlyList<string> Languages() =>
        typeof(ManualService).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..].Replace('\\', '/')).Where(p => p.Contains('/')).Select(p => p[..p.IndexOf('/')]).Distinct().ToList();

    /// <summary>Los capítulos de un idioma («es» o «en»), el índice primero; si ese idioma no está, los del otro.</summary>
    public static IReadOnlyList<ManualChapter> Chapters(string language)
    {
        var assembly = typeof(ManualService).Assembly;
        var all = assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => (Resource: n, Path: n[Prefix.Length..].Replace('\\', '/')))
            .Where(r => r.Path.Contains('/') && r.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(r => (r.Resource, Language: r.Path[..r.Path.IndexOf('/')], Name: Path.GetFileNameWithoutExtension(r.Path)))
            .ToList();
        var chosen = all.Any(r => r.Language == language) ? language : all.Any(r => r.Language == "es") ? "es" : all.FirstOrDefault().Language;
        return all.Where(r => r.Language == chosen)
            .OrderBy(r => r.Name.Equals("README", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(r =>
            {
                using var stream = assembly.GetManifestResourceStream(r.Resource)!;
                using var reader = new StreamReader(stream);
                return new ManualChapter(r.Name, reader.ReadToEnd());
            })
            .ToList();
    }

    /// <summary>El HTML del manual en el idioma pedido.</summary>
    public static string Html(string language) =>
        ManualBook.Html(Chapters(language), string.Format(Strings.ManualTitle, AppInfo.Name), Strings.ManualSearch, language);

    /// <summary>
    /// Escribe el manual en la carpeta temporal y lo abre en el navegador, en <paramref name="chapter"/> si se pide
    /// («02-editor»). El ancla va por una página chica que redirige: al abrir un archivo, Windows pierde lo que va después de «#».
    /// </summary>
    public static async Task<string?> OpenAsync(TopLevel? top, string? chapter = null)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
        var folder = Path.Combine(Path.GetTempPath(), "FbStudio");
        Directory.CreateDirectory(folder);
        var book = Path.Combine(folder, $"manual-{language}.html");
        await File.WriteAllTextAsync(book, Html(language), new System.Text.UTF8Encoding(false));
        var target = Path.GetFileName(book) + (chapter is null ? "" : "#" + Uri.EscapeDataString(chapter));
        var open = Path.Combine(folder, "manual-open.html");
        await File.WriteAllTextAsync(open,
            $"<!DOCTYPE html><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0;url={target}\"><script>location.replace('{target}')</script>",
            new System.Text.UTF8Encoding(false));
        if (top?.Launcher is { } launcher) await launcher.LaunchFileInfoAsync(new FileInfo(open));
        return book;
    }
}
