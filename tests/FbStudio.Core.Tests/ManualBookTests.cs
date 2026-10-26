using FbStudio.Core.Manual;

namespace FbStudio.Core.Tests;

/// <summary>El manual (Markdown) como un HTML para leer sin internet.</summary>
public class ManualBookTests
{
    [Fact]
    public void Anclas_como_las_de_GitHub()
    {
        Assert.Equal("buscar-y-reemplazar", ManualBook.Slug("Buscar y reemplazar"));
        Assert.Equal("dónde-se-usa", ManualBook.Slug("¿Dónde se usa?"));
        Assert.Equal("ctrlshifte-el-plan", ManualBook.Slug("`Ctrl+Shift+E`: el plan"));
        var headings = ManualBook.Headings("# Editor\n## Ejecutar\n```\n## no es un título\n```\n## Ejecutar\n### Más");
        Assert.Equal(["editor", "ejecutar", "ejecutar-1", "más"], headings.Select(h => h.Slug));
        Assert.Equal([1, 2, 2, 3], headings.Select(h => h.Level));
    }

    [Fact]
    public void Markdown_del_manual_a_HTML()
    {
        const string md = """
            # El editor

            Un párrafo con **negrita**, `Ctrl+F` y un <b>texto</b> que no es HTML:
            sigue en el renglón de abajo.

            ## Ejecutar

            - Uno con `SELECT * FROM T` y COUNT(*) sin tocar
              que sigue abajo
              - adentro
            - Dos, ver [resultados](03-results.md#copiar) y [arriba](#ejecutar) y [web](https://firebirdsql.org)

            1. Primero
            2. Segundo

            | Atajo | Qué hace | N |
            |---|---|---:|
            | `F9` | Ejecuta | 1 |
            | `Ctrl+\|` | Barra | 2 |

            > **Ojo:** en producción pide confirmación.

            ```
            SELECT * FROM T WHERE A < 1
            ```
            """;
        var html = ManualBook.ToHtml(md, "02-editor");
        Assert.Contains("<h1 id=\"02-editor\">El editor</h1>", html);
        Assert.Contains("<h2 id=\"02-editor--ejecutar\">Ejecutar</h2>", html);
        Assert.Contains("<p>Un párrafo con <strong>negrita</strong>, <code>Ctrl+F</code> y un &lt;b&gt;texto&lt;/b&gt; que no es HTML: sigue en el renglón de abajo.</p>", html);
        Assert.Contains("<li>Uno con <code>SELECT * FROM T</code> y COUNT(*) sin tocar que sigue abajo<ul><li>adentro</li></ul>", html);
        Assert.Contains("<a href=\"#03-results--copiar\">resultados</a>", html);
        Assert.Contains("<a href=\"#02-editor--ejecutar\">arriba</a>", html);
        Assert.Contains("<a href=\"https://firebirdsql.org\">web</a>", html);
        Assert.Contains("<ol><li>Primero</li><li>Segundo</li></ol>", html);
        Assert.Contains("<th style=\"text-align:right\">N</th>", html);
        Assert.Contains("<td><code>Ctrl+|</code></td>", html);
        Assert.Contains("<blockquote><strong>Ojo:</strong> en producción pide confirmación.</blockquote>", html);
        Assert.Contains("<pre><code>SELECT * FROM T WHERE A &lt; 1</code></pre>", html);
    }

    [Fact]
    public void Un_solo_HTML_con_indice_y_buscador()
    {
        ManualChapter[] chapters = [new("README", "# Manual\n\n1. [El editor](02-editor.md)"), new("02-editor", "# El editor\n\n## Ejecutar\n\nTexto.")];
        Assert.Equal("El editor", chapters[1].Title);
        var html = ManualBook.Html(chapters, "Manual de FbStudio", "Buscar en el manual…", "es");
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>Manual de FbStudio</title>", html);
        Assert.Contains("<h3><a href=\"#02-editor\">El editor</a></h3>", html);
        Assert.Contains("<li><a href=\"#02-editor--ejecutar\">Ejecutar</a></li>", html);
        Assert.Contains("<section class=\"chapter\" id=\"README\">", html);
        Assert.Contains("<a href=\"#02-editor\">El editor</a>", html);                  // el índice enlaza a los capítulos
        Assert.Contains("placeholder=\"Buscar en el manual…\"", html);
    }
}
