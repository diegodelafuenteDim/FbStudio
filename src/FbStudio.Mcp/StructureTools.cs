using System.Globalization;
using System.Text;
using FbStudio.Core.Diagrams;
using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;
using FbStudio.Firebird;

namespace FbStudio.Mcp;

/// <summary>
/// Las herramientas de estructura de la fase 1 (D-114): esquema, DDL, búsqueda, dependencias, uso de columnas, camino
/// entre tablas, plan y datos de la base. Devuelven texto compacto en inglés (lo lee el modelo). Todo sale del
/// explorador de metadatos: ninguna lee filas de las tablas.
/// </summary>
public sealed class StructureTools(DatabaseCatalog catalog)
{
    /// <summary>Cuántos objetos lista <c>get_schema</c> por vez.</summary>
    public const int PageSize = 400;

    /// <summary>Cuántos resultados devuelve <c>search_metadata</c> como máximo.</summary>
    public const int MaxSearchHits = 200;

    public Task<string> GetSchemaAsync(string database, string? area, string? kind, string? name, int offset, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var objects = await s.ObjectsAsync(ct);
            var kinds = ParseKinds(kind);
            var areas = s.Areas();
            var sb = new StringBuilder();
            sb.Append(s.Profile.Name).Append(": ").Append(Counts(objects)).Append('\n');

            IEnumerable<DbObject> selected = objects;
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(area))
            {
                var index = FindArea(areas, area);
                var uses = areas[index].IncludeDependents ? await s.UsesAsync(ct) : null;
                var membership = new AreaMembership(areas, objects, uses);
                selected = membership.Members(index);
                filters.Add($"area {areas[index].Name}");
            }
            if (kinds is not null)
            {
                selected = selected.Where(o => kinds.Contains(o.Kind));
                filters.Add("kind " + string.Join("/", kinds.Select(Kind)));
            }
            else if (string.IsNullOrWhiteSpace(area) && string.IsNullOrWhiteSpace(name))
            {
                selected = selected.Where(o => o.Kind == DbObjectKind.Table);
                filters.Add("kind table (pass kind, area or name to list other objects)");
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                var pattern = name.Trim();
                selected = selected.Where(o => SchemaAreas.Matches(pattern, o.Name));
                filters.Add($"name {pattern}");
            }
            if (areas.Count > 0 && string.IsNullOrWhiteSpace(area))
                sb.Append("Areas: ").Append(string.Join(", ", areas.Select(a => a.Name))).Append('\n');

            var list = selected.OrderBy(o => o.Kind).ThenBy(o => o.Name, StringComparer.Ordinal).ToList();
            var from = Math.Clamp(offset, 0, list.Count);
            var page = list.Skip(from).Take(PageSize).ToList();
            sb.Append("Filter: ").Append(string.Join(" · ", filters)).Append('\n');
            if (list.Count == 0) return sb.Append("No objects match.").ToString();
            sb.Append($"Objects {from + 1}-{from + page.Count} of {list.Count}:\n");
            foreach (var o in page) sb.Append(Line(o)).Append('\n');
            if (from + page.Count < list.Count) sb.Append($"More: call get_schema again with offset={from + page.Count}.");
            return sb.ToString().TrimEnd('\n');
        }, ct);

    public Task<string> DescribeObjectAsync(string database, string name, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var obj = await FindAsync(s, name, ct);
            var script = await s.Explorer.ScriptAsync(obj, ct);
            var sb = new StringBuilder($"-- {Kind(obj.Kind).ToUpperInvariant()} {obj.Name}");
            if (!string.IsNullOrWhiteSpace(script.Description)) sb.Append(" — ").Append(script.Description.Trim());
            return sb.Append('\n').Append(script.Ddl.TrimEnd()).ToString();
        }, ct);

    public Task<string> SearchMetadataAsync(string database, string text, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ConnectorException("Pass the text to search for.");
            var hits = await s.Explorer.SearchAsync(text.Trim(), ct: ct);
            if (hits.Count == 0) return $"\"{text.Trim()}\" does not appear in names, columns, comments or source code of {s.Profile.Name}.";
            var sb = new StringBuilder($"\"{text.Trim()}\" in {s.Profile.Name} ({hits.Count} match{(hits.Count == 1 ? "" : "es")}");
            sb.Append(hits.Count > MaxSearchHits ? $", first {MaxSearchHits}):\n" : "):\n");
            foreach (var h in hits.Take(MaxSearchHits))
            {
                sb.Append(Kind(h.Kind).ToUpperInvariant()).Append(' ').Append(h.Name).Append(" · ").Append(Where(h));
                if (!string.IsNullOrWhiteSpace(h.Snippet)) sb.Append(": ").Append(OneLine(h.Snippet));
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }, ct);

    public Task<string> GetDependenciesAsync(string database, string name, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var obj = await FindAsync(s, name, ct);
            var deps = await s.Explorer.DependenciesAsync(obj, ct);
            var sb = new StringBuilder($"{Kind(obj.Kind).ToUpperInvariant()} {obj.Name}\n");
            foreach (var (direction, title) in new[] { (DependencyDirection.Uses, "Uses"), (DependencyDirection.UsedBy, "Used by") })
                AppendDependencies(sb, title, deps.Where(d => d.Direction == direction).ToList());
            return sb.ToString().TrimEnd('\n');
        }, ct);

    /// <summary>Más que esto, las dependencias van agrupadas por tipo en un renglón, sin el detalle (gastan menos contexto).</summary>
    public const int CompactAbove = 60;

    private static void AppendDependencies(StringBuilder sb, string title, IReadOnlyList<DbDependency> list)
    {
        var sorted = list.OrderBy(d => d.Kind).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
        if (sorted.Count == 0)
        {
            sb.Append(title).Append(": nothing\n");
            return;
        }
        if (sorted.Count <= CompactAbove)
        {
            sb.Append(title).Append(":\n");
            foreach (var d in sorted) sb.Append("- ").Append(Dependency(d)).Append('\n');
            return;
        }
        sb.Append(title).Append($" ({sorted.Count}, without details):\n");
        foreach (var g in sorted.GroupBy(d => d.Kind))
            sb.Append($"- {Plural(Kind(g.Key), g.Count()).ToUpperInvariant()} ({g.Count()}): ").Append(string.Join(", ", g.Select(d => d.Name))).Append('\n');
    }

    public Task<string> FindColumnUsageAsync(string database, string table, string column, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var t = await FindAsync(s, table, ct);
            if (t.Kind is not (DbObjectKind.Table or DbObjectKind.View)) throw new ConnectorException($"{t.Name} is a {Kind(t.Kind)}, not a table or view.");
            var col = (column ?? "").Trim();
            var columns = await s.Explorer.LoadColumnsAsync(t.Name, ct);
            var match = columns.FirstOrDefault(c => c.Name == col) ?? columns.FirstOrDefault(c => string.Equals(c.Name, col, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ConnectorException($"{t.Name} has no column {col}. Columns: {string.Join(", ", columns.Select(c => c.Name))}.");
            var uses = await s.Explorer.ColumnUsageAsync(t.Name, match.Name, ct);
            if (uses.Count == 0) return $"{t.Name}.{match.Name} is not used by any view, procedure, function, trigger, index or constraint.";
            var sb = new StringBuilder($"{t.Name}.{match.Name}\n");
            AppendDependencies(sb, "Used by", uses);
            return sb.ToString().TrimEnd('\n');
        }, ct);

    public Task<string> FindJoinPathAsync(string database, string from, string to, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var graph = await s.GraphAsync(ct);
            var a = TableName(graph, from);
            var b = TableName(graph, to);
            var path = graph.Path(a, b);
            if (path is null)
                return $"{a} and {b} are not connected by foreign keys (neither declared nor logical ones written in column comments).";
            var sb = new StringBuilder($"Path: {string.Join(" -> ", path)} ({path.Count - 1} relationship{(path.Count == 2 ? "" : "s")})\n");
            sb.Append("SELECT *\nFROM ").Append(path[0]).Append('\n');
            var notes = new List<string>();
            for (var i = 1; i < path.Count; i++)
            {
                var link = Link(graph, path[i - 1], path[i]);
                sb.Append("JOIN ").Append(path[i]).Append(" ON ").Append(Condition(link)).Append('\n');
                if (link.IsLogical) notes.Add($"{link.Table}.{string.Join(", ", link.Columns)} -> {link.Referenced} is a logical key (from a column comment), not a declared foreign key.");
            }
            foreach (var n in notes) sb.Append("Note: ").Append(n).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }, ct);

    public Task<string> ExplainQueryAsync(string database, string sql, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var text = (sql ?? "").Trim().TrimEnd(';').Trim();
            CheckExplainable(text);
            var (plan, explained) = await s.Explorer.PlanAsync(text, ct);
            if (plan is null) return "The server returned no plan for this statement (it was prepared, not executed).";
            return $"Plan{(explained ? " (detailed)" : "")} — the statement was only prepared, never executed:\n{plan}";
        }, ct);

    public Task<string> GetDatabaseInfoAsync(string database, CancellationToken ct) =>
        catalog.UseAsync(database, async s =>
        {
            var info = await s.Explorer.ServerInfoAsync(ct);
            var settings = await DatabaseProperties.ReadAsync(s.Profile, s.Password, ct);
            var nbackup = await NBackupInfo.ReadAsync(s.Profile, s.Password, ct);
            var sb = new StringBuilder($"{s.Profile.Name} · environment: {DatabaseList.Environment(s.Profile.Environment)}\n");
            sb.Append("Server: ").Append(info.Display);
            if (info.Mode != ServerMode.Unknown) sb.Append(" · ").Append(info.Mode);
            if (info.Platform is { } platform) sb.Append(" · ").Append(platform);
            sb.Append('\n');
            sb.Append($"Database: ODS {info.OdsMajor}.{info.OdsMinor} · dialect {info.Dialect} · page size {info.PageSize} · charset {info.Charset ?? "NONE"}\n");
            if (info.PageBuffers > 0) sb.Append($"Cache: {info.PageBuffers.ToString(CultureInfo.InvariantCulture)} pages\n");
            sb.Append($"Sweep interval: {(settings.SweepInterval == 0 ? "0 (automatic sweep off)" : settings.SweepInterval.ToString(CultureInfo.InvariantCulture))}");
            sb.Append($" · forced writes: {(settings.ForcedWrites ? "on" : "off")} · read-only: {(settings.ReadOnly ? "yes" : "no")}\n");
            sb.Append("nbackup: ").Append(nbackup.State switch
            {
                BackupState.Stalled => "the database is in backup mode (changes go to the delta file)",
                BackupState.Merge => "merging the delta file",
                _ => "normal",
            });
            if (nbackup.History.Count > 0)
            {
                var last = nbackup.History.MaxBy(e => e.Timestamp)!;
                sb.Append($" · last backup {last.Timestamp:yyyy-MM-dd HH:mm} (level {last.Level})");
            }
            return sb.ToString();
        }, ct);

    /// <summary>
    /// Solo se pide el plan de lo que tiene plan: SELECT, WITH, INSERT, UPDATE, DELETE, MERGE y EXECUTE BLOCK. El resto
    /// (DDL, SET, COMMIT, EXECUTE PROCEDURE…) se rechaza antes de llegar al servidor, aunque preparar no lo ejecutaría.
    /// </summary>
    public static void CheckExplainable(string sql)
    {
        if (sql.Length == 0) throw new ConnectorException("Pass the SQL statement whose plan you want.");
        var info = StatementClassifier.Classify(sql);
        var ok = info.Kind is StatementKind.Select or StatementKind.Dml
                 || (info.Kind == StatementKind.Execute && info.Keyword == "EXECUTE BLOCK");
        if (!ok)
            throw new ConnectorException($"explain_query only takes SELECT, WITH, INSERT, UPDATE, DELETE, MERGE or EXECUTE BLOCK, one statement at a time (got {(info.Keyword.Length == 0 ? "nothing" : info.Keyword)}).");
    }

    /// <summary>«table», «tables», «procedure»…, separados por coma; null si no se pidió ninguno.</summary>
    public static IReadOnlySet<DbObjectKind>? ParseKinds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var set = new HashSet<DbObjectKind>();
        foreach (var word in text.Split([',', ' ', '/', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word.Trim().ToLowerInvariant();
            if (w.EndsWith("ies", StringComparison.Ordinal)) w = w[..^3] + "y";
            else if (w.EndsWith("es", StringComparison.Ordinal) && w is "indexes") w = "index";
            else if (w.EndsWith('s') && w != "s") w = w[..^1];
            var kind = DbObjectKinds.All.Cast<DbObjectKind?>().FirstOrDefault(k => Kind(k!.Value) == w || (w == "sequence" && k == DbObjectKind.Generator))
                       ?? throw new ConnectorException($"Unknown object kind \"{word}\". Use: {string.Join(", ", DbObjectKinds.All.Select(Kind))}.");
            set.Add(kind);
        }
        return set;
    }

    /// <summary>El tipo en inglés y en minúsculas («table», «procedure»…).</summary>
    public static string Kind(DbObjectKind kind) => kind.ToString().ToLowerInvariant();

    private static async Task<DbObject> FindAsync(DatabaseSession s, string name, CancellationToken ct)
    {
        var n = (name ?? "").Trim().Trim('"');
        if (n.Length == 0) throw new ConnectorException("Pass the object name.");
        return await s.Explorer.FindObjectAsync(n, ct)
               ?? (n != n.ToUpperInvariant() ? await s.Explorer.FindObjectAsync(n.ToUpperInvariant(), ct) : null)
               ?? throw new ConnectorException($"There is no object called {n} in {s.Profile.Name}. Use search_metadata or get_schema to find it.");
    }

    private static int FindArea(IReadOnlyList<SchemaArea> areas, string name)
    {
        var n = name.Trim();
        for (var i = 0; i < areas.Count; i++)
            if (string.Equals(areas[i].Name, n, StringComparison.OrdinalIgnoreCase)) return i;
        throw new ConnectorException(areas.Count == 0
            ? "This database has no areas."
            : $"There is no area called \"{n}\". Areas: {string.Join(", ", areas.Select(a => a.Name))}.");
    }

    private static string TableName(SchemaGraph graph, string name)
    {
        var n = (name ?? "").Trim().Trim('"');
        if (graph.Contains(n)) return n;
        if (graph.Contains(n.ToUpperInvariant())) return n.ToUpperInvariant();
        throw new ConnectorException($"There is no table or view called {n}.");
    }

    /// <summary>La relación entre dos tablas vecinas del camino (en cualquier sentido); la declarada antes que la lógica.</summary>
    private static DiagramLink Link(SchemaGraph graph, string a, string b) =>
        graph.Links.Where(l => (l.Table == a && l.Referenced == b) || (l.Table == b && l.Referenced == a))
            .OrderBy(l => l.IsLogical).ThenBy(l => l.Name, StringComparer.Ordinal).First();

    private static string Condition(DiagramLink link) =>
        string.Join(" AND ", link.Columns.Zip(link.ReferencedColumns, (c, r) => $"{link.Referenced}.{r} = {link.Table}.{c}"));

    private static string Counts(IReadOnlyList<DbObject> objects) =>
        string.Join(", ", objects.GroupBy(o => o.Kind).OrderBy(g => g.Key).Select(g => $"{g.Count()} {Plural(Kind(g.Key), g.Count())}"));

    private static string Plural(string word, int count) => count == 1 ? word : word switch
    {
        "index" => "indexes",
        _ when word.EndsWith('y') => word[..^1] + "ies",
        _ => word + "s",
    };

    private static string Line(DbObject o)
    {
        var sb = new StringBuilder(Kind(o.Kind).ToUpperInvariant()).Append(' ').Append(o.Name);
        switch (o.Kind)
        {
            case DbObjectKind.Trigger when o.Owner is not null:
            case DbObjectKind.Index when o.Owner is not null:
                sb.Append(" on ").Append(o.Owner);
                if (o.Kind == DbObjectKind.Index && o.Detail?.EndsWith(')') == true) sb.Append(" (unique)");
                break;
            case DbObjectKind.Trigger:
                sb.Append(" (database trigger)");
                break;
            case DbObjectKind.Table when o.Detail is not null:
                sb.Append(" (").Append(o.Detail == "externa" ? "external" : o.Detail).Append(')');
                break;
            case DbObjectKind.Domain or DbObjectKind.Function when o.Detail is not null:
                sb.Append(" (").Append(o.Detail).Append(')');
                break;
        }
        if (o.Inactive) sb.Append(" (inactive)");
        return sb.ToString();
    }

    private static string Dependency(DbDependency d) =>
        $"{Kind(d.Kind).ToUpperInvariant()} {d.Name}" + (string.IsNullOrWhiteSpace(d.Detail) ? "" : $" ({d.Detail})");

    private static string Where(MetadataSearchHit h) => h.Area switch
    {
        SearchArea.Name => "name",
        SearchArea.Column => "column",
        SearchArea.Source => h.Line is { } line ? $"source line {line}" : "source",
        _ => h.Line is { } l ? $"comment line {l}" : "comment",
    };

    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
}
