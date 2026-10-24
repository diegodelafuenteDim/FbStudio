using System.Text.RegularExpressions;

namespace FbStudio.Core;

/// <summary>
/// Una versión del registro de cambios: su número («Unreleased» para lo que todavía no salió), la fecha y un renglón por
/// cambio en cada idioma.
/// </summary>
public sealed record ChangeLogVersion(string Version, string? Date, IReadOnlyList<string> English, IReadOnlyList<string> Spanish)
{
    public bool IsUnreleased => Version.Equals("Unreleased", StringComparison.OrdinalIgnoreCase);

    /// <summary>Los cambios en el idioma pedido («es» o cualquier otro = inglés); si faltan en ese idioma, los del otro.</summary>
    public IReadOnlyList<string> In(string language) =>
        language == "es" ? (Spanish.Count > 0 ? Spanish : English) : (English.Count > 0 ? English : Spanish);
}

/// <summary>
/// CHANGELOG.md (D-81): «## [versión] - fecha» y adentro «### English» y «### Español», con un renglón «- …» por cambio
/// (un cambio largo sigue en los renglones de abajo, con sangría). Lo muestra Ayuda → Novedades y el trabajo de Release
/// publica la sección de su versión.
/// </summary>
public static partial class ChangeLog
{
    [GeneratedRegex(@"^##\s+\[(?<v>[^\]]+)\](?:\s*-\s*(?<d>\S+))?")]
    private static partial Regex VersionHeader();

    [GeneratedRegex(@"^###\s+(?<l>.+?)\s*$")]
    private static partial Regex LanguageHeader();

    [GeneratedRegex(@"^\s*[-*]\s+(?<t>.+)$")]
    private static partial Regex Bullet();

    /// <summary>Las versiones en el orden del archivo (la más nueva arriba).</summary>
    public static IReadOnlyList<ChangeLogVersion> Parse(string markdown)
    {
        var versions = new List<ChangeLogVersion>();
        string? version = null, date = null;
        List<string> english = [], spanish = [];
        List<string>? current = null;

        void Flush()
        {
            if (version is not null) versions.Add(new ChangeLogVersion(version, date, english, spanish));
        }

        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (VersionHeader().Match(line) is { Success: true } v)
            {
                Flush();
                (version, date, english, spanish, current) = (v.Groups["v"].Value.Trim(), v.Groups["d"].Success ? v.Groups["d"].Value : null, [], [], null);
            }
            else if (version is not null && LanguageHeader().Match(line) is { Success: true } l)
            {
                current = l.Groups["l"].Value.StartsWith("Espa", StringComparison.OrdinalIgnoreCase) ? spanish
                    : l.Groups["l"].Value.StartsWith("English", StringComparison.OrdinalIgnoreCase) ? english
                    : null;
            }
            else if (current is not null && Bullet().Match(line) is { Success: true } b)
            {
                current.Add(Plain(b.Groups["t"].Value));
            }
            else if (current is { Count: > 0 } && line.Length > 0 && char.IsWhiteSpace(raw[0]))
            {
                current[^1] += " " + Plain(line.Trim()); // sigue el cambio de arriba
            }
        }
        Flush();
        return versions;
    }

    /// <summary>Sin las marcas de Markdown que se usan en los renglones (negrita y código).</summary>
    public static string Plain(string text) => text.Replace("**", "").Replace("`", "");
}
