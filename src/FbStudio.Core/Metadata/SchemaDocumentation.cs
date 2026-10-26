using System.Globalization;
using System.Text;
using FbStudio.Core.Diagrams;
using FbStudio.Core.Resources;

namespace FbStudio.Core.Metadata;

/// <summary>
/// La documentación de una base en un solo archivo HTML: índice con buscador,
/// cada tabla con sus columnas (tipo, nulos, defecto, descripción), claves, FK enlazadas (también las lógicas de los
/// comentarios «#FK_»), quién la referencia, índices y triggers; las vistas, los procedimientos, las funciones, los
/// dominios, los generadores y las excepciones. El código PSQL va solo si se pide (en una base grande pesa mucho). Se
/// abre en cualquier navegador, sin internet: el estilo y el buscador van adentro.
/// </summary>
public static class SchemaDocumentation
{
    public static string Html(DatabaseSchema schema, string database, DateTime when, bool includeSources)
    {
        var sb = new StringBuilder();
        var title = string.Format(CultureInfo.CurrentCulture, CoreStrings.DocTitle, database);
        sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>").Append(E(title)).Append("</title>\n<style>").Append(Css).Append("</style>\n</head>\n<body>\n");

        // El índice, con el buscador.
        sb.Append("<nav>\n<input id=\"q\" type=\"search\" placeholder=\"").Append(E(CoreStrings.DocSearch)).Append("\" autofocus>\n");
        void IndexGroup(string label, string prefix, IEnumerable<string> names)
        {
            var list = names.ToList();
            if (list.Count == 0) return;
            sb.Append("<h3>").Append(E(label)).Append(" <small>").Append(list.Count).Append("</small></h3>\n<ul>");
            foreach (var n in list) sb.Append("<li><a href=\"#").Append(Id(prefix, n)).Append("\">").Append(E(n)).Append("</a></li>");
            sb.Append("</ul>\n");
        }
        IndexGroup(CoreStrings.KindTablePlural, "t", schema.Tables.Select(t => t.Name));
        IndexGroup(CoreStrings.KindViewPlural, "v", schema.Views.Select(v => v.Name));
        IndexGroup(CoreStrings.KindProcedurePlural, "p", schema.Procedures.Select(p => p.Name));
        IndexGroup(CoreStrings.KindFunctionPlural, "f", schema.Functions.Select(f => f.Name));
        IndexGroup(CoreStrings.KindPackagePlural, "k", schema.Packages.Select(p => p.Name));
        IndexGroup(CoreStrings.KindDomainPlural, "d", schema.Domains.Select(d => d.Field.Name));
        IndexGroup(CoreStrings.KindGeneratorPlural, "g", schema.Generators.Select(g => g.Name));
        IndexGroup(CoreStrings.KindExceptionPlural, "e", schema.Exceptions.Select(e => e.Name));
        sb.Append("</nav>\n<main>\n<header><h1>").Append(E(title)).Append("</h1>\n<p>")
          .Append(E(string.Format(CultureInfo.CurrentCulture, CoreStrings.DocGenerated, when)));
        if (schema.Area is { } area) sb.Append(" · ").Append(E(string.Format(CultureInfo.CurrentCulture, CoreStrings.DocArea, area)));
        sb.Append("</p></header>\n");

        // Quién referencia a cada tabla (FOREIGN KEY y lógicas de los comentarios).
        var tables = schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var referencedBy = new Dictionary<string, List<(string Table, string Columns, bool Logical)>>(StringComparer.Ordinal);
        foreach (var t in schema.Tables)
        {
            foreach (var fk in t.Constraints.Where(c => c is { Kind: ConstraintKind.ForeignKey, ReferencedTable: not null }))
                Add(fk.ReferencedTable!, (t.Name, string.Join(", ", fk.Columns), false));
            foreach (var c in t.Columns)
                if (LogicalForeignKeys.Parse(t.Name, c.Name, c.Description) is { } logical && tables.Contains(logical.Referenced))
                    Add(logical.Referenced, (t.Name, c.Name, true));
        }
        void Add(string table, (string, string, bool) reference)
        {
            if (!referencedBy.TryGetValue(table, out var list)) referencedBy[table] = list = [];
            list.Add(reference);
        }

        foreach (var t in schema.Tables)
        {
            Section(sb, "t", t.Name, CoreStrings.KindTable, t.Description);
            var primary = t.Constraints.FirstOrDefault(c => c.Kind == ConstraintKind.PrimaryKey)?.Columns ?? [];
            var foreign = t.Constraints.Where(c => c is { Kind: ConstraintKind.ForeignKey, ReferencedTable: not null })
                .SelectMany(c => c.Columns.Select((col, i) => (col, c))).GroupBy(x => x.col).ToDictionary(g => g.Key, g => g.First().c, StringComparer.Ordinal);
            sb.Append("<table><thead><tr><th>").Append(E(CoreStrings.DocColumn)).Append("</th><th>").Append(E(CoreStrings.DocType)).Append("</th><th>")
              .Append(E(CoreStrings.DocNull)).Append("</th><th>").Append(E(CoreStrings.DocDefault)).Append("</th><th>").Append(E(CoreStrings.DocDescription)).Append("</th></tr></thead><tbody>\n");
            foreach (var c in t.Columns)
            {
                sb.Append("<tr><td><code>").Append(E(c.Name)).Append("</code>");
                if (primary.Contains(c.Name)) sb.Append(" <span class=\"pk\">PK</span>");
                if (foreign.TryGetValue(c.Name, out var fk))
                    sb.Append(" <a class=\"fk\" href=\"#").Append(Id("t", fk.ReferencedTable!)).Append("\">FK → ").Append(E(fk.ReferencedTable!)).Append("</a>");
                else if (LogicalForeignKeys.Parse(t.Name, c.Name, c.Description) is { } logical && tables.Contains(logical.Referenced))
                    sb.Append(" <a class=\"fk logical\" href=\"#").Append(Id("t", logical.Referenced)).Append("\" title=\"").Append(E(CoreStrings.DocLogicalFk)).Append("\">→ ")
                      .Append(E(logical.Referenced)).Append('.').Append(E(logical.ReferencedColumns[0])).Append("</a>");
                sb.Append("</td><td>").Append(E(c.Computed is { } computed ? "COMPUTED BY " + computed : c.Type)).Append(c.Identity ? " <small>IDENTITY</small>" : "")
                  .Append("</td><td>").Append(c.NotNull ? "" : "✓").Append("</td><td>").Append(E(c.Default ?? "")).Append("</td><td>").Append(E(c.Description ?? "")).Append("</td></tr>\n");
            }
            sb.Append("</tbody></table>\n");
            var constraints = t.Constraints.Where(c => c.Kind != ConstraintKind.PrimaryKey || c.Columns.Count > 0).ToList();
            if (constraints.Count > 0)
            {
                sb.Append("<h4>").Append(E(CoreStrings.DocKeys)).Append("</h4><ul>");
                foreach (var c in constraints)
                {
                    sb.Append("<li><code>").Append(E(c.Name)).Append("</code> ");
                    switch (c.Kind)
                    {
                        case ConstraintKind.PrimaryKey: sb.Append("PRIMARY KEY (").Append(E(string.Join(", ", c.Columns))).Append(')'); break;
                        case ConstraintKind.Unique: sb.Append("UNIQUE (").Append(E(string.Join(", ", c.Columns))).Append(')'); break;
                        case ConstraintKind.ForeignKey:
                            sb.Append("FOREIGN KEY (").Append(E(string.Join(", ", c.Columns))).Append(") → <a href=\"#").Append(Id("t", c.ReferencedTable ?? "")).Append("\">")
                              .Append(E(c.ReferencedTable ?? "")).Append("</a> (").Append(E(string.Join(", ", c.ReferencedColumns ?? []))).Append(')');
                            if (c.OnDelete is { } onDelete) sb.Append(" ON DELETE ").Append(E(onDelete));
                            if (c.OnUpdate is { } onUpdate) sb.Append(" ON UPDATE ").Append(E(onUpdate));
                            break;
                        default: sb.Append(E(c.CheckSource ?? "CHECK")); break;
                    }
                    sb.Append("</li>");
                }
                sb.Append("</ul>\n");
            }
            if (referencedBy.TryGetValue(t.Name, out var references))
            {
                sb.Append("<h4>").Append(E(CoreStrings.DocReferencedBy)).Append("</h4><p>");
                sb.Append(string.Join(", ", references.Distinct().Select(r => $"<a href=\"#{Id("t", r.Table)}\">{E(r.Table)}</a> <small>({E(r.Columns)}{(r.Logical ? ", " + E(CoreStrings.DocLogicalFk) : "")})</small>")));
                sb.Append("</p>\n");
            }
            if (t.Indices.Count > 0)
            {
                sb.Append("<h4>").Append(E(CoreStrings.DocIndexes)).Append("</h4><ul>");
                foreach (var i in t.Indices)
                    sb.Append("<li><code>").Append(E(i.Name)).Append("</code> ").Append(i.Unique ? "UNIQUE " : "").Append(i.Descending ? "DESC " : "")
                      .Append(E(i.Expression is { } expression ? "COMPUTED BY " + expression : "(" + string.Join(", ", i.Columns) + ")"))
                      .Append(i.Inactive ? " <small>" + E(CoreStrings.DocInactive) + "</small>" : "").Append("</li>");
                sb.Append("</ul>\n");
            }
            Triggers(sb, t.Triggers, includeSources);
            sb.Append("</section>\n");
        }

        foreach (var v in schema.Views)
        {
            Section(sb, "v", v.Name, CoreStrings.KindView, v.Description);
            Fields(sb, CoreStrings.DocColumns, v.Columns);
            Source(sb, v.Source, open: false);
            Triggers(sb, v.Triggers, includeSources);
            sb.Append("</section>\n");
        }
        foreach (var p in schema.Procedures)
        {
            Section(sb, "p", p.Name, CoreStrings.KindProcedure, p.Description);
            Fields(sb, CoreStrings.DocInputs, p.Inputs);
            Fields(sb, CoreStrings.DocOutputs, p.Outputs);
            if (includeSources) Source(sb, p.Source, open: false);
            sb.Append("</section>\n");
        }
        foreach (var f in schema.Functions)
        {
            Section(sb, "f", f.Name, CoreStrings.KindFunction, f.Description);
            Fields(sb, CoreStrings.DocInputs, f.Inputs);
            sb.Append("<p>").Append(E(CoreStrings.DocReturns)).Append(" <code>").Append(E(f.Returns.Type)).Append("</code>")
              .Append(f.ModuleName is { } module ? $" <small>(UDF {E(module)}:{E(f.EntryPoint ?? "")})</small>" : "").Append("</p>\n");
            if (includeSources) Source(sb, f.Source, open: false);
            sb.Append("</section>\n");
        }
        foreach (var k in schema.Packages)
        {
            Section(sb, "k", k.Name, CoreStrings.KindPackage, k.Description);
            Source(sb, k.Header, open: false);
            sb.Append("</section>\n");
        }
        foreach (var d in schema.Domains)
        {
            Section(sb, "d", d.Field.Name, CoreStrings.KindDomain, d.Field.Description);
            sb.Append("<p><code>").Append(E(d.Field.Type)).Append("</code>").Append(d.Field.NotNull ? " NOT NULL" : "")
              .Append(d.Field.Default is { } def ? " " + E(def) : "").Append(d.Check is { } check ? " " + E(check) : "").Append("</p>\n</section>\n");
        }
        foreach (var g in schema.Generators)
        {
            Section(sb, "g", g.Name, CoreStrings.KindGenerator, g.Description);
            sb.Append("</section>\n");
        }
        foreach (var e in schema.Exceptions)
        {
            Section(sb, "e", e.Name, CoreStrings.KindException, e.Description);
            sb.Append("<p>").Append(E(CoreStrings.DocMessage)).Append(": <q>").Append(E(e.Message)).Append("</q></p>\n</section>\n");
        }
        sb.Append("</main>\n<script>").Append(Script).Append("</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string prefix, string name, string kind, string? description)
    {
        sb.Append("<section id=\"").Append(Id(prefix, name)).Append("\" data-name=\"").Append(E(name.ToLowerInvariant())).Append("\">\n<h2><small>")
          .Append(E(kind)).Append("</small> ").Append(E(name)).Append("</h2>\n");
        if (!string.IsNullOrWhiteSpace(description)) sb.Append("<p class=\"desc\">").Append(E(description.Trim()).Replace("\n", "<br>")).Append("</p>\n");
    }

    private static void Fields(StringBuilder sb, string title, IReadOnlyList<FieldDefinition> fields)
    {
        if (fields.Count == 0) return;
        sb.Append("<h4>").Append(E(title)).Append("</h4><table><tbody>");
        foreach (var f in fields)
            sb.Append("<tr><td><code>").Append(E(f.Name)).Append("</code></td><td>").Append(E(f.Type)).Append(f.NotNull ? " NOT NULL" : "")
              .Append(f.Default is { } def ? " " + E(def) : "").Append("</td><td>").Append(E(f.Description ?? "")).Append("</td></tr>");
        sb.Append("</tbody></table>\n");
    }

    private static void Triggers(StringBuilder sb, IReadOnlyList<TriggerDefinition> triggers, bool includeSources)
    {
        if (triggers.Count == 0) return;
        sb.Append("<h4>").Append(E(CoreStrings.DocTriggers)).Append("</h4><ul>");
        foreach (var t in triggers)
        {
            sb.Append("<li><code>").Append(E(t.Name)).Append("</code> ").Append(E(TriggerTypeFormatter.Describe(t.Type))).Append(" POSITION ").Append(t.Position)
              .Append(t.Inactive ? " <small>" + E(CoreStrings.DocInactive) + "</small>" : "");
            if (!string.IsNullOrWhiteSpace(t.Description)) sb.Append(" — ").Append(E(t.Description));
            if (includeSources) Source(sb, t.Source, open: false);
            sb.Append("</li>");
        }
        sb.Append("</ul>\n");
    }

    private static void Source(StringBuilder sb, string? source, bool open)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        sb.Append(open ? "<details open>" : "<details>").Append("<summary>").Append(E(CoreStrings.DocSource)).Append("</summary><pre>")
          .Append(E(source.Replace("\r\n", "\n").Trim('\n'))).Append("</pre></details>\n");
    }

    /// <summary>El ancla de un objeto: el tipo y el nombre (que puede tener espacios o comillas) escapado.</summary>
    private static string Id(string prefix, string name) => prefix + "-" + Uri.EscapeDataString(name).Replace("%", "_");

    /// <summary>Solo lo que hace falta escapar (los acentos quedan tal cual: el archivo es UTF-8).</summary>
    private static string E(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    private const string Css = """

        :root { color-scheme: light dark; --bg: #fff; --fg: #1f2328; --muted: #656d76; --line: #d0d7de; --accent: #0969da; --pk: #9a6700; --nav: #f6f8fa; }
        @media (prefers-color-scheme: dark) { :root { --bg: #0d1117; --fg: #e6edf3; --muted: #8d96a0; --line: #30363d; --accent: #4493f8; --pk: #d29922; --nav: #161b22; } }
        * { box-sizing: border-box; }
        body { margin: 0; display: flex; font: 14px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif; background: var(--bg); color: var(--fg); }
        nav { position: sticky; top: 0; height: 100vh; overflow: auto; width: 280px; flex: none; padding: 12px; background: var(--nav); border-right: 1px solid var(--line); }
        nav input { width: 100%; padding: 6px 8px; border: 1px solid var(--line); border-radius: 6px; background: var(--bg); color: var(--fg); }
        nav h3 { margin: 14px 0 4px; font-size: 12px; text-transform: uppercase; color: var(--muted); }
        nav ul { list-style: none; margin: 0; padding: 0; }
        nav li a { display: block; padding: 1px 4px; border-radius: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        main { flex: 1; min-width: 0; padding: 16px 28px 80px; }
        a { color: var(--accent); text-decoration: none; } a:hover { text-decoration: underline; }
        h1 { margin: 0 0 4px; } header p { color: var(--muted); margin-top: 0; }
        section { border-top: 1px solid var(--line); padding: 10px 0 14px; }
        h2 { margin: 6px 0; font-size: 18px; } h2 small { color: var(--muted); font-weight: normal; font-size: 12px; text-transform: uppercase; margin-right: 6px; }
        h4 { margin: 12px 0 4px; font-size: 13px; color: var(--muted); }
        .desc { white-space: normal; }
        table { border-collapse: collapse; width: 100%; font-size: 13px; }
        th, td { text-align: left; vertical-align: top; padding: 3px 8px; border-bottom: 1px solid var(--line); }
        th { font-weight: 600; color: var(--muted); }
        code, pre { font-family: "Cascadia Mono", Consolas, Menlo, monospace; font-size: 12.5px; }
        pre { overflow: auto; padding: 8px; border: 1px solid var(--line); border-radius: 6px; }
        .pk { color: var(--pk); font-size: 11px; font-weight: 700; } .fk { font-size: 11px; } .logical { font-style: italic; }
        .hidden { display: none; }
        @media print { nav { display: none; } }

        """;

    // El buscador: filtra el índice y las secciones por el nombre (sin distinguir mayúsculas).
    private const string Script = """

        const q = document.getElementById('q');
        q.addEventListener('input', () => {
          const text = q.value.trim().toLowerCase();
          document.querySelectorAll('nav li').forEach(li => li.classList.toggle('hidden', text !== '' && !li.textContent.toLowerCase().includes(text)));
          document.querySelectorAll('main section').forEach(s => s.classList.toggle('hidden', text !== '' && !s.dataset.name.includes(text)));
        });

        """;
}
