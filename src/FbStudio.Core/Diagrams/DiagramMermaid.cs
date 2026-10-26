using System.Text;
using System.Text.RegularExpressions;

namespace FbStudio.Core.Diagrams;

/// <summary>
/// El diagrama como texto de Mermaid (erDiagram), para pegarlo en un README, un issue o una wiki que lo dibuje (GitHub,
/// GitLab, Azure DevOps…): cada tabla con sus columnas a la vista (marca PK/FK y tipo) y cada relación de la tabla con
/// la FK (muchas) a la referenciada (una); las lógicas, punteadas. Mermaid acomoda solo: no lleva las posiciones.
/// </summary>
public static partial class DiagramMermaid
{
    public static string Write(DiagramScene scene, string title)
    {
        var sb = new StringBuilder();
        sb.Append("---\ntitle: ").Append(title.Replace('\n', ' ')).Append("\n---\nerDiagram\n");
        foreach (var n in scene.Nodes)
        {
            if (n.Rows.Count == 0)
            {
                // Sin columnas a la vista (una de afuera): alcanza con su nombre, si no aparece en ninguna relación.
                if (!scene.Edges.Any(e => e.From == n || e.To == n)) sb.Append("    ").Append(Name(n.Name)).Append(" {\n    }\n");
                continue;
            }
            sb.Append("    ").Append(Name(n.Name)).Append(" {\n");
            foreach (var r in n.Rows)
            {
                var keys = r.Kind switch
                {
                    DiagramRowKind.PrimaryAndForeignKey => " PK, FK",
                    DiagramRowKind.PrimaryKey => " PK",
                    DiagramRowKind.ForeignKey => " FK",
                    _ => "",
                };
                sb.Append("        ").Append(Type(r.Type)).Append(' ').Append(Name(r.Name)).Append(keys).Append('\n');
            }
            sb.Append("    }\n");
        }
        foreach (var e in scene.Edges)
            sb.Append("    ").Append(Name(e.From.Name)).Append(e.Link.IsLogical ? " }o..|| " : " }o--|| ").Append(Name(e.To.Name))
                .Append(" : \"").Append(e.Link.Name.Replace("\"", "'")).Append("\"\n");
        return sb.ToString();
    }

    /// <summary>Mermaid acepta letras, números, «_» y «-» en los nombres: lo demás («$», espacios) pasa a «_».</summary>
    private static string Name(string name) => NotName().Replace(name, "_");

    /// <summary>En el tipo no van comas ni espacios: «NUMERIC(12,2)» → «NUMERIC(12_2)», «DOUBLE PRECISION» → «DOUBLE_PRECISION».</summary>
    private static string Type(string type)
    {
        var text = NotType().Replace(type.Trim(), "_");
        return text.Length > 0 && char.IsLetter(text[0]) ? text : "T_" + text;
    }

    [GeneratedRegex(@"[^A-Za-z0-9_\-]")]
    private static partial Regex NotName();

    [GeneratedRegex(@"[^A-Za-z0-9_\-()\[\]]")]
    private static partial Regex NotType();
}
