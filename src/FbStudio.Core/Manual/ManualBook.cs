using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Manual;

/// <summary>Un capítulo del manual: el nombre del archivo sin «.md» («02-editor») y su Markdown.</summary>
public sealed record ManualChapter(string Name, string Markdown)
{
    /// <summary>El título (el primer «# …»), o el nombre si no tiene.</summary>
    public string Title => Markdown.Replace("\r\n", "\n").Split('\n').FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? Name;
}

/// <summary>
/// El manual (docs/manual, en Markdown) como un solo HTML para leer en el navegador sin internet (Ayuda › Manual y F1):
/// índice con los capítulos y sus temas, buscador y estilo claro u oscuro según el sistema. Entiende el Markdown que usa
/// el manual (títulos, párrafos, listas, tablas, citas, código y enlaces entre capítulos) y nada más; las anclas son las
/// de GitHub con el capítulo adelante («02-editor--buscar-y-reemplazar»), así un enlace «02-editor.md#buscar-y-reemplazar»
/// lleva al mismo lugar en GitHub y acá.
/// </summary>
public static partial class ManualBook
{
    public static string Html(IReadOnlyList<ManualChapter> chapters, string title, string searchPlaceholder, string language)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(E(language)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>").Append(E(title)).Append("</title>\n<style>")
          .Append(Css).Append("</style>\n</head>\n<body>\n<nav>\n<input id=\"q\" type=\"search\" placeholder=\"").Append(E(searchPlaceholder)).Append("\">\n");
        foreach (var chapter in chapters)
        {
            sb.Append("<h3><a href=\"#").Append(E(chapter.Name)).Append("\">").Append(Inline(chapter.Title, chapter.Name, plain: true)).Append("</a></h3>\n<ul>");
            foreach (var heading in Headings(chapter.Markdown).Where(h => h.Level == 2))
                sb.Append("<li><a href=\"#").Append(E(Anchor(chapter.Name, heading.Slug))).Append("\">").Append(Inline(heading.Text, chapter.Name, plain: true)).Append("</a></li>");
            sb.Append("</ul>\n");
        }
        sb.Append("</nav>\n<main>\n");
        foreach (var chapter in chapters)
            sb.Append("<section class=\"chapter\" id=\"").Append(E(chapter.Name)).Append("\">\n").Append(ToHtml(chapter.Markdown, chapter.Name)).Append("</section>\n");
        sb.Append("</main>\n<script>").Append(Script).Append("</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    public sealed record Heading(int Level, string Text, string Slug);

    /// <summary>Los títulos de un capítulo con su ancla (como GitHub: repetidos con -1, -2…).</summary>
    public static IReadOnlyList<Heading> Headings(string markdown)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var list = new List<Heading>();
        var inCode = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
            if (inCode || HeadingLine().Match(raw) is not { Success: true } m) continue;
            var text = m.Groups[2].Value.Trim();
            var slug = Slug(text);
            if (seen.TryGetValue(slug, out var n))
            {
                seen[slug] = n + 1;
                slug = $"{slug}-{n + 1}";
            }
            else seen[slug] = 0;
            list.Add(new Heading(m.Groups[1].Length, text, slug));
        }
        return list;
    }

    /// <summary>El ancla que pone GitHub a un título: minúsculas, sin signos (sí letras con acento, números, «-» y «_»), espacios a «-».</summary>
    public static string Slug(string heading)
    {
        var plain = heading.Replace("**", "").Replace("`", "");
        var sb = new StringBuilder();
        foreach (var c in plain.ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        }
        return sb.ToString();
    }

    public static string Anchor(string chapter, string slug) => $"{chapter}--{slug}";

    /// <summary>Un capítulo en HTML (los títulos con su ancla del libro).</summary>
    public static string ToHtml(string markdown, string chapter)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var headings = new Queue<Heading>(Headings(markdown));
        var sb = new StringBuilder();
        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            sb.Append("<p>").Append(Inline(string.Join(" ", paragraph), chapter)).Append("</p>\n");
            paragraph.Clear();
        }
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                sb.Append("<pre><code>").Append(E(string.Join("\n", code))).Append("</code></pre>\n");
                i++;
                continue;
            }
            if (HeadingLine().Match(line) is { Success: true } h)
            {
                FlushParagraph();
                var heading = headings.Dequeue();
                var level = h.Groups[1].Length;
                var id = level == 1 ? chapter : Anchor(chapter, heading.Slug);
                sb.Append("<h").Append(level).Append(" id=\"").Append(E(id)).Append("\">").Append(Inline(heading.Text, chapter)).Append("</h").Append(level).Append(">\n");
                i++;
                continue;
            }
            if (line.TrimStart().StartsWith('|') && i + 1 < lines.Length && TableSeparator().IsMatch(lines[i + 1]))
            {
                FlushParagraph();
                var header = Cells(line);
                var aligns = Cells(lines[i + 1]).Select(c => c.EndsWith(':') ? (c.StartsWith(':') ? "center" : "right") : "").ToList();
                sb.Append("<table><thead><tr>");
                for (var c = 0; c < header.Count; c++) sb.Append(Cell("th", header[c], aligns.ElementAtOrDefault(c), chapter));
                sb.Append("</tr></thead><tbody>\n");
                for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                {
                    var cells = Cells(lines[i]);
                    sb.Append("<tr>");
                    for (var c = 0; c < header.Count; c++) sb.Append(Cell("td", cells.ElementAtOrDefault(c) ?? "", aligns.ElementAtOrDefault(c), chapter));
                    sb.Append("</tr>\n");
                }
                sb.Append("</tbody></table>\n");
                continue;
            }
            if (line.StartsWith('>'))
            {
                FlushParagraph();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].StartsWith('>'); i++) quote.Add(lines[i].TrimStart('>').Trim());
                sb.Append("<blockquote>").Append(Inline(string.Join(" ", quote.Where(q => q.Length > 0)), chapter)).Append("</blockquote>\n");
                continue;
            }
            if (ListItem().Match(line) is { Success: true })
            {
                FlushParagraph();
                i = List(lines, i, sb, chapter, 0);
                continue;
            }
            if (line.Trim().Length == 0) FlushParagraph();
            else paragraph.Add(line.Trim());
            i++;
        }
        FlushParagraph();
        return sb.ToString();
    }

    /// <summary>Una lista (con un nivel de sangría adentro) desde el renglón <paramref name="i"/>; devuelve el siguiente renglón.</summary>
    private static int List(string[] lines, int i, StringBuilder sb, string chapter, int indent)
    {
        var ordered = ListItem().Match(lines[i]).Groups[2].Value.EndsWith('.');
        sb.Append(ordered ? "<ol>" : "<ul>");
        var open = false;
        var item = new StringBuilder();
        void CloseItem()
        {
            if (!open) return;
            sb.Append(Inline(item.ToString().Trim(), chapter));
            item.Clear();
        }
        while (i < lines.Length)
        {
            var m = ListItem().Match(lines[i]);
            var lineIndent = lines[i].Length - lines[i].TrimStart().Length;
            if (m.Success && lineIndent == indent)
            {
                CloseItem();
                if (open) sb.Append("</li>");
                sb.Append("<li>");
                open = true;
                item.Append(m.Groups[3].Value);
                i++;
            }
            else if (m.Success && lineIndent > indent && open)
            {
                CloseItem();
                i = List(lines, i, sb, chapter, lineIndent);
            }
            else if (!m.Success && lines[i].Trim().Length > 0 && lineIndent > indent && open)
            {
                item.Append(' ').Append(lines[i].Trim()); // sigue el mismo ítem
                i++;
            }
            else break;
        }
        CloseItem();
        if (open) sb.Append("</li>");
        sb.Append(ordered ? "</ol>\n" : "</ul>\n");
        return i;
    }

    private static List<string> Cells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t[..^1];
        return SplitCells().Split(t).Select(c => c.Trim().Replace("\\|", "|")).ToList();
    }

    private static string Cell(string tag, string text, string? align, string chapter) =>
        $"<{tag}{(string.IsNullOrEmpty(align) ? "" : $" style=\"text-align:{align}\"")}>{Inline(text, chapter)}</{tag}>";

    /// <summary>Lo de adentro de un renglón: `código`, **negrita** y [enlaces](otro-capítulo.md#ancla) (sin cursiva: un «*» de SQL no se toca).</summary>
    public static string Inline(string text, string chapter, bool plain = false)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var segments = text.Split('`');
        for (var s = 0; s < segments.Length; s++)
        {
            if (s % 2 == 1 && s < segments.Length - (segments.Length % 2 == 0 ? 1 : 0))
            {
                sb.Append(plain ? E(segments[s]) : "<code>" + E(segments[s]) + "</code>");
                continue;
            }
            var html = E(segments[s]);
            html = Link().Replace(html, m => plain ? m.Groups[1].Value : $"<a href=\"{E(Resolve(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value), chapter))}\">{m.Groups[1].Value}</a>");
            html = Bold().Replace(html, plain ? "$1" : "<strong>$1</strong>");
            sb.Append(html);
        }
        return sb.ToString();
    }

    /// <summary>«02-editor.md#tema» → «#02-editor--tema»; «#tema» → el del mismo capítulo; afuera del manual, tal cual.</summary>
    public static string Resolve(string href, string chapter)
    {
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return href;
        var hash = href.IndexOf('#');
        var file = hash >= 0 ? href[..hash] : href;
        var fragment = hash >= 0 ? href[(hash + 1)..] : "";
        var target = file.Length == 0 ? chapter : Path.GetFileNameWithoutExtension(file);
        if (target.Equals("README", StringComparison.OrdinalIgnoreCase)) target = "README";
        return "#" + (fragment.Length == 0 ? target : Anchor(target, fragment));
    }

    private static string E(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [GeneratedRegex(@"^(#{1,3})\s+(.+)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"^(\s*)([-*]|\d+\.)\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex SplitCells();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    private const string Css = """

        :root { color-scheme: light dark; --bg: #fff; --fg: #1f2328; --muted: #656d76; --line: #d0d7de; --accent: #0969da; --nav: #f6f8fa; --code: #f3f4f6; --quote: #ddf4ff; }
        @media (prefers-color-scheme: dark) { :root { --bg: #0d1117; --fg: #e6edf3; --muted: #8d96a0; --line: #30363d; --accent: #4493f8; --nav: #161b22; --code: #1f242c; --quote: #0c2d48; } }
        * { box-sizing: border-box; }
        body { margin: 0; display: flex; font: 15px/1.6 system-ui, -apple-system, "Segoe UI", sans-serif; background: var(--bg); color: var(--fg); }
        nav { position: sticky; top: 0; height: 100vh; overflow: auto; width: 300px; flex: none; padding: 14px; background: var(--nav); border-right: 1px solid var(--line); }
        nav input { width: 100%; padding: 7px 9px; border: 1px solid var(--line); border-radius: 6px; background: var(--bg); color: var(--fg); margin-bottom: 6px; }
        nav h3 { margin: 14px 0 2px; font-size: 14px; } nav h3 a { color: var(--fg); }
        nav ul { list-style: none; margin: 0; padding: 0 0 0 10px; }
        nav li a { display: block; padding: 1px 4px; border-radius: 4px; font-size: 13px; color: var(--muted); }
        nav li a:hover, nav h3 a:hover { color: var(--accent); text-decoration: none; }
        main { flex: 1; min-width: 0; max-width: 980px; padding: 10px 40px 120px; }
        a { color: var(--accent); text-decoration: none; } a:hover { text-decoration: underline; }
        .chapter { border-bottom: 1px solid var(--line); padding: 14px 0 26px; }
        h1 { font-size: 28px; margin: 10px 0 8px; } h2 { font-size: 21px; margin: 26px 0 8px; padding-top: 6px; } h3 { font-size: 17px; margin: 20px 0 6px; }
        p, li { margin: 6px 0; } ul, ol { padding-left: 24px; }
        code { font-family: "Cascadia Mono", Consolas, Menlo, monospace; font-size: 13px; background: var(--code); padding: 1px 5px; border-radius: 4px; }
        pre { background: var(--code); padding: 10px 12px; border-radius: 6px; overflow: auto; } pre code { padding: 0; background: none; }
        table { border-collapse: collapse; margin: 10px 0; font-size: 14px; } th, td { border: 1px solid var(--line); padding: 5px 10px; vertical-align: top; } th { background: var(--nav); text-align: left; }
        blockquote { margin: 12px 0; padding: 8px 14px; border-left: 4px solid var(--accent); background: var(--quote); border-radius: 0 6px 6px 0; }
        .hidden { display: none; }
        @media print { nav { display: none; } main { padding: 0; } }

        """;

    // El buscador: deja a la vista los capítulos y los temas del índice que tienen el texto, sin distinguir mayúsculas ni acentos.
    private const string Script = """

        const fold = s => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
        const q = document.getElementById('q');
        q.addEventListener('input', () => {
          const text = fold(q.value.trim());
          document.querySelectorAll('main .chapter').forEach(c => c.classList.toggle('hidden', text !== '' && !fold(c.textContent).includes(text)));
          document.querySelectorAll('nav li').forEach(li => {
            const id = decodeURIComponent(li.querySelector('a').getAttribute('href').slice(1));
            const h = document.getElementById(id);
            let block = '', n = h ? h.nextElementSibling : null;
            while (n && n.tagName !== 'H2' && n.tagName !== 'H1') { block += n.textContent; n = n.nextElementSibling; }
            li.classList.toggle('hidden', text !== '' && !fold(li.textContent + block).includes(text));
          });
        });

        """;
}
