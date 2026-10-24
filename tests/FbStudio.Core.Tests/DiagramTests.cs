using FbStudio.Core.Diagrams;

namespace FbStudio.Core.Tests;

public sealed class DiagramTests
{
    private static DiagramTable T(string name, params string[] columns) =>
        new(name, false, columns.Select((c, i) => new DiagramColumn(c, "INTEGER", i == 0, IsPrimaryKey: i == 0)).ToList());

    private static DiagramLink Fk(string table, string column, string referenced, bool logical = false) =>
        new($"FK_{table}_{column}", table, [column], referenced, ["ID"], logical);

    /// <summary>
    /// PACIENTES y USUARIOS muy referenciadas (comodines: 5 vecinas con umbral 4); TURNOS → PACIENTES, MEDICOS;
    /// TURNOS_DET → TURNOS; una lógica INFORMES → TURNOS; una lógica que repite una física y otra a una tabla que no está.
    /// </summary>
    private static SchemaGraph Graph()
    {
        var tables = new[]
        {
            T("PACIENTES", "ID", "NOMBRE"), T("USUARIOS", "ID"), T("MEDICOS", "ID", "NOMBRE"),
            T("TURNOS", "ID", "ID_PACIENTE", "ID_MEDICO", "FECHA"), T("TURNOS_DET", "ID", "ID_TURNO", "NOTA"),
            T("INFORMES", "ID", "ID_TURNO", "ID_USUARIO"), T("CAJA", "ID", "ID_USUARIO", "ID_PACIENTE"), T("LAB", "ID", "ID_PACIENTE", "ID_USUARIO"),
            T("ODONTO", "ID", "ID_PACIENTE", "ID_USUARIO"), T("FARMACIA", "ID", "ID_PACIENTE", "ID_USUARIO"), T("SUELTA", "ID"),
        };
        var links = new[]
        {
            Fk("TURNOS", "ID_PACIENTE", "PACIENTES"), Fk("TURNOS", "ID_MEDICO", "MEDICOS"),
            Fk("TURNOS_DET", "ID_TURNO", "TURNOS"),
            Fk("INFORMES", "ID_TURNO", "TURNOS", logical: true), Fk("INFORMES", "ID_USUARIO", "USUARIOS"),
            Fk("CAJA", "ID_USUARIO", "USUARIOS"), Fk("CAJA", "ID_PACIENTE", "PACIENTES"),
            Fk("LAB", "ID_PACIENTE", "PACIENTES"), Fk("LAB", "ID_USUARIO", "USUARIOS"),
            Fk("ODONTO", "ID_PACIENTE", "PACIENTES"), Fk("ODONTO", "ID_USUARIO", "USUARIOS"),
            Fk("FARMACIA", "ID_PACIENTE", "PACIENTES"), Fk("FARMACIA", "ID_USUARIO", "USUARIOS"),
            Fk("TURNOS_DET", "ID_TURNO", "TURNOS", logical: true),          // repite la física: no se dibuja dos veces
            Fk("TURNOS", "ID_PACIENTE", "NO_EXISTE", logical: true),       // a una tabla que no está: se ignora
        };
        return new SchemaGraph(tables, links, hubThreshold: 4);
    }

    [Fact]
    public void Lee_las_FK_logicas_de_los_comentarios()
    {
        var link = LogicalForeignKeys.Parse("TURNOS", "PACIENTE", "#Required# #FK_%PACIENTES,%CODIGO,%NOMBRE,I#")!;
        Assert.Equal(("TURNOS", "PACIENTES", true), (link.Table, link.Referenced, link.IsLogical));
        Assert.Equal(["PACIENTE"], link.Columns);
        Assert.Equal(["CODIGO"], link.ReferencedColumns);
        Assert.NotNull(LogicalForeignKeys.Parse("A", "B", "#fk_%usuarios,%usuario,%apellido,%nombre,L#"));
        Assert.Null(LogicalForeignKeys.Parse("A", "B", "Clave del paciente"));
        Assert.Null(LogicalForeignKeys.Parse("A", "B", null));
    }

    [Fact]
    public void Vecinos_comodines_y_FK_logicas_repetidas()
    {
        var g = Graph();
        Assert.Equal(13, g.Links.Count);                                              // 15 menos la lógica repetida y la que no tiene tabla
        Assert.True(g.IsHub("PACIENTES"));
        Assert.True(g.IsHub("USUARIOS"));
        Assert.False(g.IsHub("TURNOS"));                                              // 4 vecinas: no pasa el umbral
        Assert.Equal(["MEDICOS", "PACIENTES", "TURNOS_DET"], g.Neighbors("TURNOS", logical: false).Order());
        Assert.Contains("INFORMES", g.Neighbors("TURNOS"));
    }

    [Fact]
    public void Una_tabla_y_sus_vecinas_sin_los_comodines()
    {
        var g = Graph();
        var one = g.Around(["TURNOS"], hops: 1);
        Assert.Equal(["TURNOS", "INFORMES", "MEDICOS", "TURNOS_DET"], one.Tables);
        Assert.Empty(one.Outside);
        Assert.Equal(["PACIENTES", "USUARIOS"], one.HiddenHubs.Select(h => h.Table)); // TURNOS → PACIENTES, INFORMES → USUARIOS
        Assert.Equal(1, one.HiddenHubs[0].Links);
        Assert.Equal(5, one.HiddenHubs[0].Neighbors);
        Assert.Contains(one.Links, l => l is { Table: "INFORMES", IsLogical: true });

        // Sin las lógicas, INFORMES no está; con un comodín pedido, entra.
        Assert.DoesNotContain("INFORMES", g.Around(["TURNOS"], 1, logical: false).Tables);
        var withHub = g.Around(["TURNOS"], 1, shownHubs: new HashSet<string> { "PACIENTES" });
        Assert.Contains("PACIENTES", withHub.Tables);
        Assert.DoesNotContain(withHub.HiddenHubs, h => h.Table == "PACIENTES");
        Assert.Contains(withHub.Links, l => l is { Table: "TURNOS", Referenced: "PACIENTES" });

        // A dos pasos no se pasa a través de un comodín (CAJA y LAB solo se tocan por USUARIOS y PACIENTES).
        var two = g.Around(["TURNOS_DET"], hops: 2);
        Assert.Equal(["TURNOS_DET", "INFORMES", "MEDICOS", "TURNOS"], two.Tables);           // la de partida y las demás por nombre
        // Empezar por un comodín lo dibuja igual.
        Assert.Contains("USUARIOS", g.Around(["USUARIOS"], 1).Tables);
        Assert.Equal(["TURNOS", "MEDICOS", "PACIENTES", "TURNOS_DET"], g.Around(["TURNOS"], 1, hideHubs: false, logical: false).Tables);
    }

    [Fact]
    public void Un_area_con_o_sin_sus_vecinas_de_afuera()
    {
        var g = Graph();
        var area = g.ForArea(["TURNOS", "TURNOS_DET", "NO_ESTA"]);
        Assert.Equal(["TURNOS", "TURNOS_DET"], area.Tables);
        Assert.Empty(area.Outside);
        Assert.Single(area.Links);
        var outside = g.ForArea(["TURNOS", "TURNOS_DET"], outside: true);
        Assert.Equal(["INFORMES", "MEDICOS"], outside.Outside);                        // sin los comodines
        Assert.Equal(["PACIENTES", "USUARIOS"], outside.HiddenHubs.Select(h => h.Table));
        Assert.Equal(["USUARIOS"], g.ForArea(["TURNOS"], shownHubs: new HashSet<string> { "USUARIOS" }).Outside);
    }

    private static double Measure(string text, bool bold) => text.Length * (bold ? 8 : 7);

    [Fact]
    public void Cajas_con_las_columnas_elegidas_acomodadas_sin_superponerse()
    {
        var g = Graph();
        var selection = g.Around(["TURNOS"], 1, hideHubs: false);
        var keys = DiagramScene.Build(g, selection, DiagramColumnsMode.Keys, Measure);
        var turnos = keys.Node("TURNOS")!;
        Assert.Equal(["ID", "ID_PACIENTE", "ID_MEDICO"], turnos.Rows.Select(r => r.Name));   // FECHA no es clave
        Assert.Equal("PK", turnos.Rows[0].Marker);
        Assert.Equal("FK", turnos.Rows[1].Marker);
        Assert.Equal(1, turnos.HiddenColumns);
        Assert.Equal(DiagramMetrics.HeaderHeight + 4 * DiagramMetrics.RowHeight + DiagramMetrics.BottomPadding, turnos.Height);
        Assert.Equal(4, DiagramScene.Build(g, selection, DiagramColumnsMode.All, Measure).Node("TURNOS")!.Rows.Count);
        Assert.Empty(DiagramScene.Build(g, selection, DiagramColumnsMode.None, Measure).Node("TURNOS")!.Rows);

        DiagramLayout.Arrange(keys);
        foreach (var a in keys.Nodes)
            foreach (var b in keys.Nodes.Where(b => b != a))
                Assert.False(a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height, $"{a.Name} pisa a {b.Name}");
        // Las referenciadas van arriba de las que las usan.
        Assert.True(keys.Node("PACIENTES")!.Y < turnos.Y);
        Assert.True(turnos.Y < keys.Node("TURNOS_DET")!.Y);

        // La línea sale del borde de una caja y llega al de la otra.
        var edge = keys.Edges.Single(e => e.From.Name == "TURNOS_DET");
        var route = DiagramScene.Route(edge);
        Assert.Equal(2, route.Count);
        Assert.Equal(edge.From.Y, route[0].Y, 1);
        Assert.Equal(edge.To.Y + edge.To.Height, route[1].Y, 1);
    }

    [Fact]
    public void Una_tabla_que_usan_muchas_no_queda_en_una_fila_interminable()
    {
        // Una «estrella»: 25 tablas que apuntan a la misma. En capas serían una sola fila; «Automático» las reparte.
        var tables = new List<DiagramTable> { T("CENTRO", "ID") };
        var links = new List<DiagramLink>();
        for (var i = 0; i < 25; i++)
        {
            tables.Add(T($"HIJA_{i:00}", "ID", "ID_CENTRO"));
            links.Add(Fk($"HIJA_{i:00}", "ID_CENTRO", "CENTRO"));
        }
        var g = new SchemaGraph(tables, links);
        var scene = DiagramScene.Build(g, g.Around(["CENTRO"], 1), DiagramColumnsMode.Keys, Measure);
        DiagramLayout.Arrange(scene, DiagramLayoutKind.Layered);
        var (_, _, lw, lh) = scene.Bounds();
        Assert.True(lw > DiagramLayout.MaxAspect * lh);                              // en capas: muy ancha
        DiagramLayout.Arrange(scene);
        var (_, _, w, h) = scene.Bounds();
        Assert.True(w <= DiagramLayout.MaxAspect * h && h <= DiagramLayout.MaxAspect * w, $"{w} × {h}");
        foreach (var a in scene.Nodes)
            foreach (var b in scene.Nodes.Where(b => b != a))
                Assert.False(a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height, $"{a.Name} pisa a {b.Name}");
    }

    [Fact]
    public void Drawio_con_tablas_editables_y_conectores_pegados()
    {
        var g = Graph();
        var scene = DiagramScene.Build(g, g.Around(["TURNOS"], 1), DiagramColumnsMode.Keys, Measure);
        DiagramLayout.Arrange(scene);
        var xml = System.Xml.Linq.XDocument.Parse(DiagramDrawio.Write(scene, "Diagrama · TURNOS"));
        Assert.Equal("Diagrama · TURNOS", xml.Root!.Element("diagram")!.Attribute("name")!.Value);
        var cells = xml.Descendants("mxCell").ToList();
        var tables = cells.Where(c => c.Attribute("style")?.Value.StartsWith("swimlane") == true).ToList();
        Assert.Equal(scene.Nodes.Select(n => n.Name), tables.Select(t => t.Attribute("value")!.Value));

        // Las posiciones de FbStudio (corridas para que la de más arriba a la izquierda quede cerca del origen).
        var (bx, by, _, _) = scene.Bounds();
        var turnos = scene.Node("TURNOS")!;
        var turnosCell = tables.Single(t => t.Attribute("value")!.Value == "TURNOS");
        var geometry = turnosCell.Element("mxGeometry")!;
        Assert.Equal(turnos.X - bx, double.Parse(geometry.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture), 1);
        Assert.Equal(turnos.Y - by, double.Parse(geometry.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture), 1);

        // Una fila por columna mostrada (y la de «+ n»), adentro de la tabla.
        var id = turnosCell.Attribute("id")!.Value;
        var rows = cells.Where(c => c.Attribute("parent")?.Value == id).ToList();
        Assert.Equal(turnos.Rows.Count + 1, rows.Count);
        Assert.Contains("PK", rows[0].Attribute("value")!.Value);
        Assert.Contains("ID_PACIENTE", rows[1].Attribute("value")!.Value);

        // Los conectores van de la tabla que apunta a la referenciada; las lógicas, punteadas.
        var edges = xml.Descendants("UserObject").ToList();
        Assert.Equal(scene.Edges.Count, edges.Count);
        var logical = edges.Single(u => u.Attribute("tooltip")!.Value.Contains("#FK"));
        var cell = logical.Element("mxCell")!;
        Assert.Contains("dashed=1", cell.Attribute("style")!.Value);
        Assert.Equal(id, cell.Attribute("target")!.Value);
        Assert.Equal(tables.Single(t => t.Attribute("value")!.Value == "INFORMES").Attribute("id")!.Value, cell.Attribute("source")!.Value);
    }

    [Fact]
    public void Svg_con_las_cajas_y_las_logicas_punteadas()
    {
        var g = Graph();
        var scene = DiagramScene.Build(g, g.Around(["TURNOS"], 1), DiagramColumnsMode.Keys, Measure);
        DiagramLayout.Arrange(scene);
        var svg = DiagramSvg.Write(scene, "Diagrama <TURNOS>");
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.Contains("<title>Diagrama &lt;TURNOS&gt;</title>", svg);
        Assert.Contains(">TURNOS_DET</text>", svg);
        Assert.Contains("class=\"fk logical\"", svg);
        Assert.Contains("INFORMES (ID_TURNO) → TURNOS (ID) · #FK", svg);
        Assert.Equal(scene.Edges.Count, svg.Split("<polyline").Length - 1);
        System.Xml.Linq.XDocument.Parse(svg);                                         // XML válido
    }
}
