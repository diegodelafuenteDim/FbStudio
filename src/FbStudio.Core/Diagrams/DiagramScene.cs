using System.Globalization;
using System.Security;
using System.Text;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Incremental;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Layout.MDS;
using Microsoft.Msagl.Miscellaneous;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;

namespace FbStudio.Core.Diagrams;

/// <summary>Cómo se acomodan las cajas (MSAGL).</summary>
public enum DiagramLayoutKind
{
    /// <summary>En capas; si queda muy ancho o muy alto, por distancias.</summary>
    Auto,

    /// <summary>Capas de Sugiyama: las referenciadas arriba, las que las usan abajo.</summary>
    Layered,

    /// <summary>Por fuerzas: las relacionadas se atraen y las demás se separan.</summary>
    Forces,

    /// <summary>Por distancias (MDS): cada tabla cerca de las que tiene a pocos pasos.</summary>
    Distances,
}

/// <summary>Qué columnas lleva cada caja.</summary>
public enum DiagramColumnsMode
{
    /// <summary>Solo las de la clave primaria y las que apuntan a otra tabla.</summary>
    Keys,
    All,
    None,
}

public enum DiagramRowKind
{
    Normal,
    PrimaryKey,
    ForeignKey,
    PrimaryAndForeignKey,
}

public sealed record DiagramRow(string Name, string Type, DiagramRowKind Kind)
{
    public string Marker => Kind switch
    {
        DiagramRowKind.PrimaryKey => "PK",
        DiagramRowKind.ForeignKey => "FK",
        DiagramRowKind.PrimaryAndForeignKey => "PK FK",
        _ => "",
    };
}

/// <summary>Medidas de las cajas, iguales en pantalla, en PNG y en SVG.</summary>
public static class DiagramMetrics
{
    public const double HeaderHeight = 26;
    public const double RowHeight = 18;
    public const double PaddingX = 10;
    public const double BottomPadding = 6;
    public const double MarkerWidth = 34;
    public const double TypeGap = 14;
    public const double MinWidth = 110;
    public const double HeaderFontSize = 13;
    public const double FontSize = 12;
    public const double MarkerFontSize = 10;
}

/// <summary>Una caja del diagrama: la tabla, las columnas que se muestran y dónde está (se puede mover).</summary>
public sealed class DiagramNode
{
    public required string Name { get; init; }
    public bool IsView { get; init; }

    /// <summary>Tabla de afuera del área o comodín pedido: una cajita sin columnas.</summary>
    public bool IsOutside { get; init; }

    public IReadOnlyList<DiagramRow> Rows { get; init; } = [];

    /// <summary>Columnas que no se muestran («+ 12 columnas»).</summary>
    public int HiddenColumns { get; init; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>Una línea: de la tabla que apunta (<see cref="From"/>) a la referenciada (<see cref="To"/>, con la flecha).</summary>
public sealed record DiagramEdge(DiagramLink Link, DiagramNode From, DiagramNode To)
{
    /// <summary>Si hay varias entre las mismas dos tablas, cuál es (para separarlas un poco).</summary>
    public int Lane { get; init; }
    public int Lanes { get; init; } = 1;
}

/// <summary>Las cajas y líneas de un diagrama, con su lugar; la pantalla, el PNG y el SVG dibujan lo mismo.</summary>
public sealed class DiagramScene
{
    private DiagramScene(IReadOnlyList<DiagramNode> nodes, IReadOnlyList<DiagramEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
    }

    public IReadOnlyList<DiagramNode> Nodes { get; }
    public IReadOnlyList<DiagramEdge> Edges { get; }

    public DiagramNode? Node(string name) => Nodes.FirstOrDefault(n => n.Name == name);

    /// <summary>La caja que está en ese punto (la de arriba si se superponen).</summary>
    public DiagramNode? NodeAt(double x, double y) => Nodes.LastOrDefault(n => n.Contains(x, y));

    /// <summary>
    /// Arma las cajas (sin lugar todavía: <see cref="DiagramLayout"/>). <paramref name="measure"/> dice el ancho de un
    /// texto (true = en negrita, para el nombre de la tabla).
    /// </summary>
    public static DiagramScene Build(SchemaGraph graph, DiagramSelection selection, DiagramColumnsMode mode, Func<string, bool, double> measure,
        bool logical = true)
    {
        var fkColumns = graph.Links.Where(l => logical || !l.IsLogical)
            .SelectMany(l => l.Columns.Select(c => (l.Table, Column: c)))
            .ToHashSet();
        var nodes = new List<DiagramNode>();
        foreach (var (name, outside) in selection.Tables.Select(t => (t, false)).Concat(selection.Outside.Select(t => (t, true))))
        {
            if (graph.Table(name) is not { } table) continue;
            var rows = table.Columns.Select(c => new DiagramRow(c.Name, c.Type,
                (c.IsPrimaryKey, fkColumns.Contains((name, c.Name))) switch
                {
                    (true, true) => DiagramRowKind.PrimaryAndForeignKey,
                    (true, false) => DiagramRowKind.PrimaryKey,
                    (false, true) => DiagramRowKind.ForeignKey,
                    _ => DiagramRowKind.Normal,
                })).ToList();
            var shown = outside || mode == DiagramColumnsMode.None ? []
                : mode == DiagramColumnsMode.Keys ? rows.Where(r => r.Kind != DiagramRowKind.Normal).ToList()
                : rows;
            var node = new DiagramNode
            {
                Name = name, IsView = table.IsView, IsOutside = outside, Rows = shown,
                HiddenColumns = outside ? 0 : rows.Count - shown.Count,
            };
            var width = measure(name, true) + 2 * DiagramMetrics.PaddingX;
            foreach (var r in shown)
                width = Math.Max(width, 2 * DiagramMetrics.PaddingX + DiagramMetrics.MarkerWidth + measure(r.Name, false) + DiagramMetrics.TypeGap + measure(r.Type, false));
            if (node.HiddenColumns > 0) width = Math.Max(width, 2 * DiagramMetrics.PaddingX + measure(MoreText(node.HiddenColumns), false));
            node.Width = Math.Ceiling(Math.Max(outside ? 0 : DiagramMetrics.MinWidth, width));
            node.Height = DiagramMetrics.HeaderHeight
                          + (outside ? 0 : (shown.Count + (node.HiddenColumns > 0 ? 1 : 0)) * DiagramMetrics.RowHeight + DiagramMetrics.BottomPadding);
            nodes.Add(node);
        }
        var byName = nodes.ToDictionary(n => n.Name, StringComparer.Ordinal);
        var edges = selection.Links
            .Where(l => byName.ContainsKey(l.Table) && byName.ContainsKey(l.Referenced))
            .Select(l => new DiagramEdge(l, byName[l.Table], byName[l.Referenced]))
            .GroupBy(e => string.CompareOrdinal(e.From.Name, e.To.Name) <= 0 ? (e.From.Name, e.To.Name) : (e.To.Name, e.From.Name))
            .SelectMany(g => g.Select((e, i) => e with { Lane = i, Lanes = g.Count() }))
            .ToList();
        return new DiagramScene(nodes, edges);
    }

    /// <summary>«+ 12 columnas»; el texto lo pone quien dibuja (está traducido).</summary>
    public static Func<int, string> MoreText { get; set; } = n => $"+ {n}";

    /// <summary>El rectángulo que ocupa todo, con un margen.</summary>
    public (double X, double Y, double Width, double Height) Bounds(double margin = 20)
    {
        if (Nodes.Count == 0) return (0, 0, 2 * margin, 2 * margin);
        var left = Nodes.Min(n => n.X) - margin;
        var top = Nodes.Min(n => n.Y) - margin;
        var right = Nodes.Max(n => n.X + n.Width) + margin;
        var bottom = Nodes.Max(n => n.Y + n.Height) + margin;
        return (left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Los puntos de una línea: recta entre los bordes de las dos cajas (separada si hay varias entre las mismas
    /// tablas); una FK a la misma tabla es un lazo a la derecha.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Route(DiagramEdge edge)
    {
        var (a, b) = (edge.From, edge.To);
        if (ReferenceEquals(a, b))
        {
            var top = a.Y + 6 + edge.Lane * 8;
            var x = a.X + a.Width;
            return [(x, top + 4), (x + 18, top + 4), (x + 18, top + 18), (x, top + 18)];
        }
        var (ax, ay, bx, by) = (a.CenterX, a.CenterY, b.CenterX, b.CenterY);
        var offset = (edge.Lane - (edge.Lanes - 1) / 2.0) * 8;
        if (offset != 0)
        {
            var (dx, dy) = (bx - ax, by - ay);
            var len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            (ax, ay, bx, by) = (ax - dy / len * offset, ay + dx / len * offset, bx - dy / len * offset, by + dx / len * offset);
        }
        return [Clip(a, ax, ay, bx, by), Clip(b, bx, by, ax, ay)];
    }

    /// <summary>Dónde la recta de (x1, y1) a (x2, y2) sale de la caja.</summary>
    private static (double X, double Y) Clip(DiagramNode n, double x1, double y1, double x2, double y2)
    {
        var (dx, dy) = (x2 - x1, y2 - y1);
        var t = double.MaxValue;
        if (dx > 0) t = Math.Min(t, (n.X + n.Width - x1) / dx);
        if (dx < 0) t = Math.Min(t, (n.X - x1) / dx);
        if (dy > 0) t = Math.Min(t, (n.Y + n.Height - y1) / dy);
        if (dy < 0) t = Math.Min(t, (n.Y - y1) / dy);
        if (t == double.MaxValue || t < 0) return (x1, y1);
        t = Math.Min(t, 1);
        return (x1 + dx * t, y1 + dy * t);
    }
}

/// <summary>
/// Acomoda las cajas con MSAGL: en capas de Sugiyama (las tablas referenciadas arriba, las que las usan abajo), salvo
/// que eso quede mucho más ancho que alto (una tabla que usan muchas pone a todas en una fila): ahí, por fuerzas, con
/// la tabla del medio rodeada de las demás y sin que se pisen. Las sueltas quedan agrupadas al costado. Si MSAGL falla,
/// una grilla.
/// </summary>
public static class DiagramLayout
{
    /// <summary>Más ancho que esto por cada unidad de alto ya no se lee al ver todo junto.</summary>
    public const double MaxAspect = 2.5;

    public static void Arrange(DiagramScene scene, DiagramLayoutKind kind = DiagramLayoutKind.Auto)
    {
        if (scene.Nodes.Count == 0) return;
        try
        {
            var (graph, nodes) = Graph(scene);
            LayoutHelpers.CalculateLayout(graph, Settings(kind == DiagramLayoutKind.Auto ? DiagramLayoutKind.Layered : kind), null);
            var (width, height) = Size(graph);
            if (kind == DiagramLayoutKind.Auto && scene.Nodes.Count > 8 && (width > MaxAspect * height || height > MaxAspect * width))
            {
                (graph, nodes) = Graph(scene);
                LayoutHelpers.CalculateLayout(graph, Settings(AutoFallback), null);
            }
            // MSAGL tiene el eje Y para arriba: se da vuelta.
            var maxY = graph.Nodes.Max(n => n.BoundingBox.Top);
            var minX = graph.Nodes.Min(n => n.BoundingBox.Left);
            foreach (var (n, node) in nodes)
            {
                n.X = Math.Round(node.BoundingBox.Left - minX);
                n.Y = Math.Round(maxY - node.BoundingBox.Top);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Grid(scene);
        }
    }

    /// <summary>Qué usa «Automático» cuando las capas quedan muy anchas o muy altas.</summary>
    public static DiagramLayoutKind AutoFallback { get; set; } = DiagramLayoutKind.Distances;

    private static LayoutAlgorithmSettings Settings(DiagramLayoutKind kind) => kind switch
    {
        DiagramLayoutKind.Forces => new FastIncrementalLayoutSettings
        {
            AvoidOverlaps = true, NodeSeparation = 28, PackingAspectRatio = 1.6,
            EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.StraightLine },
        },
        DiagramLayoutKind.Distances => new MdsLayoutSettings
        {
            RemoveOverlaps = true, NodeSeparation = 28, PackingAspectRatio = 1.6, ScaleX = 1.6, ScaleY = 1.6,
            EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.StraightLine },
        },
        _ => new SugiyamaLayoutSettings
        {
            NodeSeparation = 36, LayerSeparation = 70, PackingAspectRatio = 1.6,
            EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.StraightLine },
        },
    };

    private static (GeometryGraph Graph, Dictionary<DiagramNode, Node> Nodes) Graph(DiagramScene scene)
    {
        var graph = new GeometryGraph();
        var nodes = new Dictionary<DiagramNode, Node>();
        foreach (var n in scene.Nodes)
        {
            var node = new Node(CurveFactory.CreateRectangle(n.Width, n.Height, new MsaglPoint(0, 0)), n);
            nodes[n] = node;
            graph.Nodes.Add(node);
        }
        foreach (var e in scene.Edges.Where(e => !ReferenceEquals(e.From, e.To)).DistinctBy(e => (e.From, e.To)))
            graph.Edges.Add(new Edge(nodes[e.To], nodes[e.From]));
        return (graph, nodes);
    }

    private static (double Width, double Height) Size(GeometryGraph graph) =>
        (graph.Nodes.Max(n => n.BoundingBox.Right) - graph.Nodes.Min(n => n.BoundingBox.Left),
         graph.Nodes.Max(n => n.BoundingBox.Top) - graph.Nodes.Min(n => n.BoundingBox.Bottom));

    /// <summary>Filas de cajas, de izquierda a derecha.</summary>
    public static void Grid(DiagramScene scene, double maxWidth = 1600)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var n in scene.Nodes)
        {
            if (x > 0 && x + n.Width > maxWidth)
            {
                x = 0;
                y += rowHeight + 40;
                rowHeight = 0;
            }
            (n.X, n.Y) = (x, y);
            x += n.Width + 40;
            rowHeight = Math.Max(rowHeight, n.Height);
        }
    }
}

/// <summary>El diagrama como SVG (se abre en el navegador, draw.io o Inkscape).</summary>
public static class DiagramSvg
{
    public static string Write(DiagramScene scene, string? title = null)
    {
        var (bx, by, bw, bh) = scene.Bounds();
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{N(bw)}" height="{N(bh)}" viewBox="{N(bx)} {N(by)} {N(bw)} {N(bh)}">""").Append('\n');
        if (title is not null) sb.Append("<title>").Append(X(title)).Append("</title>\n");
        sb.Append("""
            <style>
              text { font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; fill: #1f2937; }
              .box { fill: #ffffff; stroke: #64748b; stroke-width: 1; }
              .head { fill: #e2e8f0; }
              .view .head { fill: #e0f2f1; }
              .outside .box { fill: #f8fafc; stroke-dasharray: 3 2; }
              .outside .head { fill: #f1f5f9; }
              .name { font-size: 13px; font-weight: 600; }
              .type, .more { fill: #64748b; }
              .mk { font-size: 10px; fill: #b45309; font-weight: 600; }
              .fk { stroke: #475569; stroke-width: 1.2; fill: none; }
              .logical { stroke-dasharray: 6 4; }
            </style>
            <defs><marker id="arrow" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse">
              <path d="M0,0 L10,5 L0,10 z" fill="#475569" /></marker></defs>

            """);
        foreach (var e in scene.Edges)
        {
            var points = string.Join(" ", DiagramScene.Route(e).Select(p => $"{N(p.X)},{N(p.Y)}"));
            sb.Append("<polyline class=\"fk").Append(e.Link.IsLogical ? " logical" : "").Append("\" points=\"").Append(points)
              .Append("\" marker-end=\"url(#arrow)\"><title>").Append(X(Tooltip(e.Link))).Append("</title></polyline>\n");
        }
        foreach (var n in scene.Nodes)
        {
            sb.Append("<g class=\"table").Append(n.IsView ? " view" : "").Append(n.IsOutside ? " outside" : "").Append("\">\n");
            sb.Append(CultureInfo.InvariantCulture, $"""  <rect class="box" x="{N(n.X)}" y="{N(n.Y)}" width="{N(n.Width)}" height="{N(n.Height)}" rx="4" />""").Append('\n');
            sb.Append(CultureInfo.InvariantCulture, $"""  <rect class="head" x="{N(n.X + 0.5)}" y="{N(n.Y + 0.5)}" width="{N(n.Width - 1)}" height="{N(DiagramMetrics.HeaderHeight - 0.5)}" rx="4" />""").Append('\n');
            sb.Append(CultureInfo.InvariantCulture, $"""  <text class="name" x="{N(n.X + DiagramMetrics.PaddingX)}" y="{N(n.Y + 17.5)}">{X(n.Name)}</text>""").Append('\n');
            var y = n.Y + DiagramMetrics.HeaderHeight;
            foreach (var r in n.Rows)
            {
                var baseline = y + 13;
                if (r.Marker.Length > 0)
                    sb.Append(CultureInfo.InvariantCulture, $"""  <text class="mk" x="{N(n.X + DiagramMetrics.PaddingX)}" y="{N(baseline)}">{r.Marker}</text>""").Append('\n');
                sb.Append(CultureInfo.InvariantCulture, $"""  <text x="{N(n.X + DiagramMetrics.PaddingX + DiagramMetrics.MarkerWidth)}" y="{N(baseline)}">{X(r.Name)}</text>""").Append('\n');
                sb.Append(CultureInfo.InvariantCulture, $"""  <text class="type" x="{N(n.X + n.Width - DiagramMetrics.PaddingX)}" y="{N(baseline)}" text-anchor="end">{X(r.Type)}</text>""").Append('\n');
                y += DiagramMetrics.RowHeight;
            }
            if (n.HiddenColumns > 0)
                sb.Append(CultureInfo.InvariantCulture, $"""  <text class="more" x="{N(n.X + DiagramMetrics.PaddingX)}" y="{N(y + 13)}">{X(DiagramScene.MoreText(n.HiddenColumns))}</text>""").Append('\n');
            sb.Append("</g>\n");
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    /// <summary>«TURNOS_DET (ID_TURNO) → TURNOS (ID)».</summary>
    public static string Tooltip(DiagramLink l) =>
        $"{l.Table} ({string.Join(", ", l.Columns)}) → {l.Referenced} ({string.Join(", ", l.ReferencedColumns)})" + (l.IsLogical ? " · #FK" : "") +
        (l.IsLogical ? "" : $" · {l.Name}");

    private static string N(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

    private static string X(string s) => SecurityElement.Escape(s) ?? "";
}
