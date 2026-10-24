using System.Globalization;
using System.Security;
using System.Text;

namespace FbStudio.Core.Diagrams;

/// <summary>
/// El diagrama en el formato de draw.io (.drawio, XML sin comprimir), para seguir editándolo ahí: cada tabla es un
/// contenedor con una fila por columna (marca PK/FK, nombre y tipo) y cada relación un conector pegado a las dos
/// tablas (las lógicas punteadas), con las posiciones que tenían en FbStudio. Mover una tabla en draw.io arrastra sus
/// líneas.
/// </summary>
public static class DiagramDrawio
{
    private const string Table = "swimlane;fontStyle=1;childLayout=stackLayout;horizontal=1;startSize=26;horizontalStack=0;resizeParent=1;"
                                 + "resizeParentMax=0;resizeLast=0;collapsible=0;marginBottom=0;html=1;align=left;spacingLeft=10;rounded=1;arcSize=6;"
                                 + "fontColor=#1F2937;strokeColor=#64748B;swimlaneFillColor=#FFFFFF;";
    private const string Row = "text;strokeColor=none;fillColor=none;align=left;verticalAlign=middle;spacingLeft=10;spacingRight=10;"
                               + "overflow=hidden;points=[[0,0.5],[1,0.5]];portConstraint=eastwest;rotatable=0;whiteSpace=wrap;html=1;fontSize=12;";
    private const string Link = "edgeStyle=none;html=1;endArrow=block;endFill=1;strokeColor=#475569;strokeWidth=1.2;rounded=0;";

    public static string Write(DiagramScene scene, string title)
    {
        var (bx, by, _, _) = scene.Bounds();
        var ids = scene.Nodes.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => $"t{x.i + 1}");
        var sb = new StringBuilder();
        sb.Append("""<mxfile host="FbStudio" type="device">""").Append('\n');
        sb.Append($"""  <diagram id="fbstudio" name="{X(title)}">""").Append('\n');
        sb.Append("""    <mxGraphModel grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="0" pageScale="1" math="0" shadow="0">""").Append('\n');
        sb.Append("      <root>\n        <mxCell id=\"0\" />\n        <mxCell id=\"1\" parent=\"0\" />\n");

        foreach (var n in scene.Nodes)
        {
            var id = ids[n];
            var fill = n.IsOutside ? "#F1F5F9" : n.IsView ? "#D9F0EE" : "#E2E8F0";
            var style = Table + $"fillColor={fill};" + (n.IsOutside ? "dashed=1;fontColor=#64748B;" : "");
            sb.Append(CultureInfo.InvariantCulture,
                $"""        <mxCell id="{id}" value="{X(H(n.Name))}" style="{style}" vertex="1" parent="1">""").Append('\n');
            sb.Append(CultureInfo.InvariantCulture,
                $"""          <mxGeometry x="{N(n.X - bx)}" y="{N(n.Y - by)}" width="{N(n.Width)}" height="{N(n.Height)}" as="geometry" />""").Append('\n');
            sb.Append("        </mxCell>\n");
            var y = DiagramMetrics.HeaderHeight;
            var row = 0;
            foreach (var r in n.Rows)
            {
                var html = (r.Marker.Length > 0 ? $"<b><font color=\"#B45309\" style=\"font-size:10px\">{H(r.Marker)}</font></b>&nbsp;&nbsp;" : "")
                           + H(r.Name) + $"&nbsp;&nbsp;<font color=\"#64748B\">{H(r.Type)}</font>";
                AppendRow(sb, $"{id}r{++row}", id, html, y, n.Width);
                y += DiagramMetrics.RowHeight;
            }
            if (n.HiddenColumns > 0)
                AppendRow(sb, $"{id}r{++row}", id, $"<i><font color=\"#64748B\">{H(DiagramScene.MoreText(n.HiddenColumns))}</font></i>", y, n.Width);
        }

        var e = 0;
        foreach (var edge in scene.Edges)
        {
            var style = Link + (edge.Link.IsLogical ? "dashed=1;dashPattern=6 4;" : "");
            sb.Append($"""        <UserObject label="" tooltip="{X(H(DiagramSvg.Tooltip(edge.Link)))}" id="e{++e}">""").Append('\n');
            sb.Append($"""          <mxCell style="{style}" edge="1" parent="1" source="{ids[edge.From]}" target="{ids[edge.To]}">""").Append('\n');
            sb.Append("""            <mxGeometry relative="1" as="geometry" />""").Append('\n');
            sb.Append("          </mxCell>\n        </UserObject>\n");
        }
        sb.Append("      </root>\n    </mxGraphModel>\n  </diagram>\n</mxfile>\n");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string id, string parent, string html, double y, double width)
    {
        sb.Append($"""        <mxCell id="{id}" value="{X(html)}" style="{Row}" vertex="1" parent="{parent}">""").Append('\n');
        sb.Append(CultureInfo.InvariantCulture,
            $"""          <mxGeometry y="{N(y)}" width="{N(width)}" height="{N(DiagramMetrics.RowHeight)}" as="geometry" />""").Append('\n');
        sb.Append("        </mxCell>\n");
    }

    private static string N(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Para un atributo XML.</summary>
    private static string X(string s) => SecurityElement.Escape(s) ?? "";

    /// <summary>Para el HTML de una etiqueta (que después va escapado en el atributo).</summary>
    private static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
