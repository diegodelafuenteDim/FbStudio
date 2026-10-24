using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;
using FbStudio.Workspace;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.App.Tests;

/// <summary>Las áreas de una base (F-9): la pestaña, el árbol y la búsqueda, contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class AreasTabTests(FirebirdFixture fb, HeadlessFixture ui) : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-areas-" + Guid.NewGuid().ToString("N"));

    // Tres tablas con el mismo prefijo para «Proponer por prefijos»; se borran al terminar (la base es compartida).
    private readonly string _prefix = "AR" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    private string[] Tables => [$"{_prefix}_UNO", $"{_prefix}_DOS", $"{_prefix}_TRES"];

    public async Task InitializeAsync()
    {
        foreach (var table in Tables) await ExecuteAsync($"CREATE TABLE {table} (ID INTEGER)");
    }

    public async Task DisposeAsync()
    {
        foreach (var table in Tables) await ExecuteAsync($"DROP TABLE {table}");
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

    private (MainWindow Window, MainWindowViewModel Vm, ProfileStore Store) Open()
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder),
            areas: AppServices.CreateAreaStore(_folder));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        Pump();
        return (window, vm, store);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static AreaObjectRow Row(AreasTabViewModel tab, string name) => tab.Preview.Single(r => r.Name == name);

    [Fact]
    public Task Arma_areas_las_guarda_y_las_usan_el_arbol_y_la_busqueda() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm, store) = Open();
        var db = vm.Tree.Rows.Single(n => n.Kind == TreeNodeKind.Database);
        await vm.Tree.ConnectAsync(db);
        Assert.DoesNotContain(db.Children!, n => n.Kind == TreeNodeKind.Areas);            // sin áreas, el árbol no cambia

        var tab = vm.OpenAreas(vm.Profiles.Single())!;
        await tab.Loading;
        Pump();
        Assert.Same(tab, vm.OpenAreas(vm.Profiles.Single()));                              // una pestaña por base
        Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<AreasTabView>());
        var unassigned = Assert.Single(tab.Areas);
        Assert.True(unassigned.IsUnassigned);
        Assert.Contains(tab.Preview, r => r.Name == "TURNOS");

        // Un área con un patrón y una excepción: los triggers e índices van con su tabla.
        tab.NewAreaCommand.Execute(null);
        Assert.Equal("Área nueva", tab.AreaName);
        tab.AreaName = "Turnos";
        tab.RulesText = "TURNOS*\nV_TURNOS";
        tab.ExcludeText = "TURNOS_DET";
        Assert.True(tab.IsModified);
        Assert.Contains(vm.ExitWarnings(), m => m.StartsWith("Hay cambios sin compilar o sin guardar en: «" + tab.BaseTitle + "»"));
        Assert.Same(ColorChoice.None, tab.AreaColor);                                      // sin color
        Assert.DoesNotContain(tab.AreaColors, c => c.Hex.Length > 0 && ColorPalette.IsRed(c.Hex)); // sin rojos: son de producción
        tab.AreaColor = tab.AreaColors.Single(c => c.Hex == "#00796B");
        Assert.Equal("#00796B", tab.SelectedArea!.Area.Color);
        Assert.True(tab.SelectedArea.HasIconBrush);
        var names = tab.Preview.Select(r => r.Name).ToList();
        Assert.Contains("TURNOS", names);
        Assert.Contains("V_TURNOS", names);
        Assert.Contains("TURNOS_FECHA", names);
        Assert.Contains("TURNOS_BI", names);
        Assert.DoesNotContain("TURNOS_DET", names);
        Assert.DoesNotContain("TURNOS_DET_BIU", names);                                    // su tabla quedó afuera
        Assert.StartsWith("Turnos", tab.Areas[0].Name);
        Assert.Equal("TURNOS*, V_TURNOS + lo que usa sus tablas", tab.Areas[0].Subtitle);
        Assert.Equal(tab.Preview.Count, tab.Areas[0].Count);
        tab.KindIndex = 1 + (int)DbObjectKind.Index;                                       // solo índices
        Assert.All(tab.Preview, r => Assert.Equal(DbObjectKind.Index, r.Object.Kind));
        tab.KindIndex = 0;
        tab.PreviewFilter = "*_BI";
        Assert.Equal(["TURNOS_BI"], tab.Preview.Select(r => r.Name));
        tab.PreviewFilter = "";

        // Las áreas nuevas suman lo que usa solo sus tablas: los procedimientos de TURNOS entran sin regla.
        Assert.True(tab.IncludeDependents);
        Assert.Equal("por lo que usa", Row(tab, "P_CONTAR").UseText);
        Assert.Equal("", Row(tab, "TURNOS").UseText);
        Assert.Contains("por lo que usan", tab.PreviewSummary);
        Assert.EndsWith("+ lo que usa sus tablas", tab.Areas[0].Subtitle);
        tab.IncludeDependents = false;
        Assert.DoesNotContain(tab.Preview, r => r.Name == "P_CONTAR");
        tab.IncludeDependents = true;
        Assert.Contains(tab.Preview, r => r.Name == "P_TURNOS_DESDE");

        // Otra área: TURNOS queda en las dos.
        tab.NewAreaCommand.Execute(null);
        tab.AreaName = "Detalle";
        tab.RulesText = "TURNOS_DET";
        var turnos = tab.Areas[0];
        var detalle = tab.Areas[1];
        tab.SelectedArea = turnos;
        tab.AddTo(detalle, [Row(tab, "TURNOS")]);
        tab.SelectedArea = detalle;
        Assert.Equal(["TURNOS_DET", "TURNOS"], detalle.Area.Rules);
        Assert.Equal("TURNOS_DET\r\nTURNOS".Replace("\r\n", Environment.NewLine), tab.RulesText);
        Assert.Equal("también en Turnos", Row(tab, "TURNOS").AlsoIn);

        // Desde «Sin área» se agrega; desde un área se saca (la regla exacta se quita; si la toma un patrón, excepción).
        tab.SelectedArea = tab.Areas.Last();
        Assert.Contains(tab.Preview, r => r.Name == "G_TURNOS");
        tab.AddTo(turnos, [Row(tab, "G_TURNOS")]);
        Assert.DoesNotContain(tab.Preview, r => r.Name == "G_TURNOS");
        tab.SelectedArea = turnos;
        tab.RemoveFromSelected([Row(tab, "V_TURNOS"), Row(tab, "TURNOS_FECHA")]);
        Assert.Equal(["TURNOS*", "G_TURNOS"], turnos.Area.Rules);
        // V_TURNOS usa solo TURNOS: sin la regla volvería por lo que usa, así que queda como excepción.
        Assert.Equal(["TURNOS_DET", "TURNOS_FECHA", "V_TURNOS"], turnos.Area.Exclude);
        Assert.DoesNotContain(tab.Preview, r => r.Name == "V_TURNOS");
        tab.RemoveFromSelected([Row(tab, "P_CONTAR")]);                                   // entró por lo que usa: excepción
        Assert.Equal("P_CONTAR", turnos.Area.Exclude[^1]);

        // El menú de la vista previa: agregar a otra área y sacar de esta.
        var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<AreasTabView>().Single();
        view.PreviewListBox.SelectedItem = Row(tab, "TURNOS");
        var menu = view.BuildPreviewMenu(null)!;
        var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header).ToList();
        Assert.Equal(["Abrir", "Agregar a", "Quitar de «Turnos»"], headers);
        Assert.Equal(["Detalle"], menu.Items.OfType<MenuItem>().ElementAt(1).Items.OfType<MenuItem>().Select(m => m.Header));

        // Proponer por prefijos: las tres tablas de la prueba.
        tab.SuggestCommand.Execute(null);
        Assert.StartsWith("Se agregaron", tab.Status);
        var suggested = tab.Areas.Single(a => a.Name == _prefix);
        Assert.Equal([$"{_prefix}_*"], suggested.Area.Rules);
        Assert.Equal(3, suggested.Count);
        // Si otra prueba dejó tablas con otro prefijo, también se propusieron: se quitan.
        foreach (var extra in tab.Areas.Where(a => !a.IsUnassigned && a != turnos && a != detalle && a != suggested).ToList())
        {
            tab.SelectedArea = extra;
            tab.DeleteAreaCommand.Execute(null);
        }

        // Dos con el mismo nombre no se guardan.
        tab.SelectedArea = detalle;
        tab.AreaName = "turnos";
        Assert.False(await tab.SaveAsync());
        Assert.Equal("Hay dos áreas que se llaman «Turnos»: cambiale el nombre a una.", tab.Status);
        tab.AreaName = "Detalle";
        Assert.True(await tab.SaveAsync());
        Assert.False(tab.IsModified);
        var own = Path.Combine(_folder, "areas", $"{fb.Profile.Id:N}.json");
        Assert.True(File.Exists(own));
        Assert.Contains("\"Color\": \"#00796B\"", File.ReadAllText(own));                 // el color se guarda con el área
        Assert.StartsWith("Archivo de esta PC: ", tab.FileText);

        // El árbol: «Áreas» al final de la base, cada área con sus objetos por tipo, y «Sin área».
        var areasNode = db.Children!.Last();
        Assert.Equal((TreeNodeKind.Areas, "Áreas", "3"), (areasNode.Kind, areasNode.Text, areasNode.Detail));
        await vm.Tree.ToggleAsync(areasNode);
        Assert.Equal(["Turnos", "Detalle", _prefix, "Sin área"], areasNode.Children!.Select(n => n.Text));
        var turnosNode = areasNode.Children![0];
        Assert.Equal(Avalonia.Media.Color.Parse("#00796B"), ((Avalonia.Media.ISolidColorBrush)turnosNode.IconBrush!).Color);
        Assert.Null(areasNode.Children![^1].IconBrush);                                     // «Sin área»: sin color
        await vm.Tree.ToggleAsync(turnosNode);
        Assert.Contains(turnosNode.Children!, c => c.Category == DbObjectKind.Trigger);
        var tables = turnosNode.Children!.Single(c => c.Category == DbObjectKind.Table);
        Assert.Equal(["TURNOS"], tables.Children!.Select(o => o.Text));
        Assert.Equal(turnosNode.Level + 1, tables.Level);
        await vm.Tree.ToggleAsync(tables);
        Assert.Contains(vm.Tree.Rows, r => r.Parent == tables && r.Text == "TURNOS");

        // La búsqueda en metadatos filtra por área.
        var search = vm.NewSearchTab(vm.Profiles.Single());
        Assert.Equal(["Todas las áreas", "Turnos", "Detalle", _prefix, "Sin área"], search.AreaChoices);
        search.SearchText = "TURNOS";
        await search.SearchAsync();
        var all = search.Hits.Select(h => h.Hit.Name).Distinct().ToList();
        Assert.Contains("TURNOS_DET", all);
        search.AreaIndex = 1;
        await search.AreaFiltering;
        var inTurnos = search.Hits.Select(h => h.Hit.Name).Distinct().ToList();
        Assert.Contains("TURNOS", inTurnos);
        Assert.DoesNotContain("TURNOS_DET", inTurnos);
        Assert.StartsWith("Área «Turnos»: ", search.Summary);

        // Compartir: «Guardar como» deja la base usando ese archivo.
        var shared = Path.Combine(_folder, "compartido", "areas-clinica.json");
        await tab.SaveAsAsync(shared);
        Assert.Equal(shared, store.Load().Single().AreasFile);
        Assert.True(tab.IsSharedFile);
        Assert.Equal($"Archivo compartido: {shared}", tab.FileText);

        // Otro lo cambia: no se pisa sin preguntar.
        var other = new AreaStore(Path.Combine(_folder, "otra-pc"));
        other.Save(shared, Guid.NewGuid(), new SchemaAreaFile { Areas = [new SchemaArea { Name = "De otro", Rules = ["X"] }] }, null, force: true);
        tab.SelectedArea = detalle;
        tab.ExcludeText = "TURNOS";
        string? asked = null;
        vm.ConfirmActionAsync = p =>
        {
            asked = p.Title;
            return Task.FromResult(false);
        };
        Assert.False(await tab.SaveAsync());
        Assert.Equal("Archivo de áreas cambiado", asked);
        Assert.Equal("De otro", Assert.Single(other.Load(store.Load().Single()).File.Areas).Name);
        vm.ConfirmActionAsync = _ => Task.FromResult(true);
        Assert.True(await tab.SaveAsync());
        Assert.Equal(3, other.Load(store.Load().Single()).File.Areas.Count);

        // Volver al archivo de esta PC (tiene lo que se guardó antes de compartir).
        await tab.UseOwnFileCommand.ExecuteAsync(null);
        Assert.Null(store.Load().Single().AreasFile);
        Assert.False(tab.IsSharedFile);
        Assert.Equal(["Turnos", "Detalle", _prefix], tab.Areas.Where(a => !a.IsUnassigned).Select(a => a.Name));
        Assert.Empty(tab.Areas[1].Area.Exclude);

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);
}
