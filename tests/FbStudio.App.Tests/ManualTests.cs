using System.Text.RegularExpressions;
using FbStudio.Core.Manual;

namespace FbStudio.App.Tests;

/// <summary>El manual va adentro, en los dos idiomas con los mismos capítulos y títulos, y sin enlaces rotos.</summary>
public class ManualTests
{
    private static readonly string[] Expected =
        ["README", "01-start", "02-editor", "03-results", "04-data", "05-tree", "06-monitoring", "07-maintenance", "08-scripts", "09-options", "10-assistants"];

    [Fact]
    public void Los_dos_idiomas_tienen_los_mismos_capitulos_y_la_misma_cantidad_de_titulos()
    {
        Assert.Equal(["en", "es"], ManualService.Languages().Order());
        var es = ManualService.Chapters("es");
        var en = ManualService.Chapters("en");
        Assert.Equal(Expected, es.Select(c => c.Name));
        Assert.Equal(Expected, en.Select(c => c.Name));
        foreach (var (spanish, english) in es.Zip(en))
        {
            var levelsEs = ManualBook.Headings(spanish.Markdown).Select(h => h.Level).ToList();
            var levelsEn = ManualBook.Headings(english.Markdown).Select(h => h.Level).ToList();
            Assert.True(levelsEs.SequenceEqual(levelsEn),
                $"{spanish.Name}: {levelsEs.Count} títulos en español y {levelsEn.Count} en inglés (tienen que ir a la par)");
            Assert.NotEqual(spanish.Markdown, english.Markdown);
        }
    }

    [Fact]
    public void Los_enlaces_entre_capitulos_llevan_a_un_titulo_que_existe()
    {
        foreach (var language in new[] { "es", "en" })
        {
            var chapters = ManualService.Chapters(language);
            var anchors = chapters.Select(c => c.Name)
                .Concat(chapters.SelectMany(c => ManualBook.Headings(c.Markdown).Select(h => ManualBook.Anchor(c.Name, h.Slug))))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var chapter in chapters)
                foreach (Match link in Regex.Matches(chapter.Markdown, @"\]\(([^)\s]+)\)"))
                {
                    var href = link.Groups[1].Value;
                    if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    var target = ManualBook.Resolve(href, chapter.Name)[1..];
                    Assert.True(anchors.Contains(target), $"{language}/{chapter.Name}.md: el enlace «{href}» no lleva a ningún título");
                }
        }
    }

    [Fact]
    public void El_HTML_trae_todos_los_capitulos()
    {
        var html = ManualService.Html("es");
        foreach (var name in Expected) Assert.Contains($"<section class=\"chapter\" id=\"{name}\">", html);
        Assert.Contains("<title>Manual de FbStudio</title>", html);
    }
}
