using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.App.Tests;

/// <summary>La pestaña «Diagrama» (F-8), contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class DiagramTabTests(FirebirdFixture fb, HeadlessFixture ui) : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-diagram-" + Guid.NewGuid().ToString("N"));

    // DIAG_TUR apunta a TURNOS con una FOREIGN KEY y a DIAG_PAC con un comentario #FK_ (lógica). Se borran al terminar.
    public async Task InitializeAsync()
    {
        await ExecuteAsync("CREATE TABLE DIAG_PAC (CODIGO INTEGER NOT NULL PRIMARY KEY, NOMBRE VARCHAR(40))");
        await ExecuteAsync("CREATE TABLE DIAG_TUR (ID INTEGER NOT NULL PRIMARY KEY, PACIENTE INTEGER, TURNO INTEGER, OBS VARCHAR(20), " +
                           "CONSTRAINT FK_DIAG_TUR FOREIGN KEY (TURNO) REFERENCES TURNOS (ID))");
        await ExecuteAsync("COMMENT ON COLUMN DIAG_TUR.PACIENTE IS '#Required# #FK_%DIAG_PAC,%CODIGO,%NOMBRE,I#'");
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync("DROP TABLE DIAG_TUR");
        await ExecuteAsync("DROP TABLE DIAG_PAC");
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand(sql, c, tx);
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder),
            areas: AppServices.CreateAreaStore(_folder));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 5; i++) { Pump(); await Task.Delay(40); }
    }

    private static IEnumerable<string> Names(DiagramTabViewModel tab) => tab.Scene!.Nodes.Select(n => n.Name);

    [Fact]
    public Task Diagrama_de_una_tabla_y_de_un_area() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var profile = vm.Profiles.Single();
        var tab = vm.OpenDiagram(profile, new DiagramSource("TURNOS"))!;
        await tab.Loading;
        await Settle();
        Assert.Same(tab, vm.OpenDiagram(profile, new DiagramSource("TURNOS")));
        Assert.Equal("Diagrama · TURNOS", tab.BaseTitle);
        var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<DiagramTabView>().Single();
        var canvas = view.DiagramCanvas;
        Assert.Same(tab.Scene, canvas.Scene);

        // Un paso: las que apuntan a TURNOS. DIAG_PAC está a dos (por la FK lógica de DIAG_TUR).
        Assert.Equal(["TURNOS", "DIAG_TUR", "TURNOS_DET"], Names(tab));
        Assert.Equal("3 tablas · 2 relaciones", tab.Summary);
        var turnos = tab.Scene!.Node("TURNOS")!;
        Assert.Equal("ID", turnos.Rows[0].Name);
        Assert.Equal("PK", turnos.Rows[0].Marker);
        Assert.True(turnos.HiddenColumns > 0);                                            // solo claves
        tab.HopIndex = 1;
        Assert.Contains("DIAG_PAC", Names(tab));
        Assert.Contains(tab.Scene!.Edges, e => e is { Link.IsLogical: true, From.Name: "DIAG_TUR", To.Name: "DIAG_PAC" });
        Assert.EndsWith("· 1 lógica", tab.Summary);
        tab.ShowLogical = false;
        Assert.DoesNotContain("DIAG_PAC", Names(tab));
        tab.ShowLogical = true;
        tab.HopIndex = 0;
        tab.ColumnsIndex = 1;                                                             // todas las columnas
        Assert.Equal(0, tab.Scene!.Node("TURNOS")!.HiddenColumns);
        tab.ColumnsIndex = 0;

        // Sin superposiciones.
        foreach (var a in tab.Scene!.Nodes)
            foreach (var b in tab.Scene.Nodes.Where(b => b != a))
                Assert.False(a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height, $"{a.Name} pisa a {b.Name}");

        // Elegir una tabla: con qué se relaciona; sumar sus vecinas y sacarla.
        var diagTur = tab.Scene.Node("DIAG_TUR")!;
        canvas.Select(diagTur);
        Assert.Same(diagTur, tab.SelectedNode);
        Assert.Contains("Usa: DIAG_PAC, TURNOS", tab.SelectedInfo);
        tab.ExpandSelectedCommand.Execute(null);
        Assert.Contains("DIAG_PAC", Names(tab));
        canvas.Select(tab.Scene!.Node("TURNOS_DET"));
        tab.RemoveSelectedCommand.Execute(null);
        Assert.DoesNotContain("TURNOS_DET", Names(tab));

        // Arrastrar una tabla con el mouse, y la rueda.
        await Settle();
        var node = tab.Scene!.Node("DIAG_PAC")!;
        var (x0, y0) = (node.X, node.Y);
        var center = new Point(node.CenterX * canvas.Zoom + canvas.Offset.X, node.CenterY * canvas.Zoom + canvas.Offset.Y);
        var start = canvas.TranslatePoint(center, w)!.Value;
        w.MouseDown(start, MouseButton.Left);
        w.MouseMove(start + new Point(60, 40));
        w.MouseUp(start + new Point(60, 40), MouseButton.Left);
        Pump();
        Assert.Equal(60 / canvas.Zoom, node.X - x0, 0);
        Assert.Equal(40 / canvas.Zoom, node.Y - y0, 0);
        Assert.Same(node, tab.SelectedNode);
        var zoom = canvas.Zoom;
        canvas.ZoomAt(center, 2);
        Assert.Equal(Math.Min(3, zoom * 2), canvas.Zoom, 3);
        tab.RelayoutCommand.Execute(null);

        // Exportar.
        Directory.CreateDirectory(_folder);
        var png = Path.Combine(_folder, "diagrama.png");
        tab.Export(png);
        Assert.Equal("Se guardó diagrama.png.", tab.Status);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], File.ReadAllBytes(png).Take(4));
        var svg = Path.Combine(_folder, "diagrama.svg");
        tab.Export(svg);
        var text = File.ReadAllText(svg);
        Assert.Contains(">DIAG_TUR</text>", text);
        Assert.Contains("class=\"fk logical\"", text);
        var drawio = Path.Combine(_folder, "diagrama.drawio");
        tab.Export(drawio);
        Assert.Equal("Se guardó diagrama.drawio.", tab.Status);
        var model = System.Xml.Linq.XDocument.Load(drawio);
        Assert.Contains(model.Descendants("mxCell"), c => c.Attribute("value")?.Value == "DIAG_TUR");
        Assert.Contains(model.Descendants("mxCell"), c => c.Attribute("edge")?.Value == "1" && c.Attribute("style")!.Value.Contains("dashed=1"));
        if (Environment.GetEnvironmentVariable("FBS_SHOT") is { } shotDir)
        {
            tab.HopIndex = 1;
            await Settle();
            canvas.Select(tab.Scene!.Node("DIAG_TUR"));
            await Settle();
            w.CaptureRenderedFrame()?.Save(Path.Combine(shotDir, "diagrama-tabla.png"));
            File.Copy(png, Path.Combine(shotDir, "diagrama-exportado.png"), overwrite: true);
            tab.Export(Path.Combine(shotDir, "diagrama-turnos.drawio"));
            tab.HopIndex = 0;
        }

        // Un área: sus tablas, y con «vecinas de afuera» las que se relacionan, como cajitas.
        var area = new SchemaArea { Name = "Turnos", Rules = ["TURNOS*"] };
        var areaTab = vm.OpenDiagram(profile, new DiagramSource("Turnos", area))!;
        await areaTab.Loading;
        Assert.Equal("Diagrama · área Turnos", areaTab.BaseTitle);
        Assert.Equal(["TURNOS", "TURNOS_DET"], Names(areaTab).Order());
        areaTab.ShowOutside = true;
        Assert.True(areaTab.Scene!.Node("DIAG_TUR")!.IsOutside);
        Assert.Empty(areaTab.Scene.Node("DIAG_TUR")!.Rows);

        // El menú del árbol: «Diagrama de relaciones» en una tabla.
        var db = vm.Tree.Rows.Single(n => n.Kind == TreeNodeKind.Database);
        await vm.Tree.ConnectAsync(db);
        var tables = db.Children!.First(n => n.Category == DbObjectKind.Table);
        await vm.Tree.ToggleAsync(tables);
        var menu = w.BuildTreeMenu(tables.Children!.First(n => n.Text == "TURNOS"))!;
        Assert.Contains("Diagrama de relaciones", menu.Items.OfType<MenuItem>().Select(m => m.Header));

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);
}
