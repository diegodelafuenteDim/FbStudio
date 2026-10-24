using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Firebird.Tests;
using FbStudio.Workspace;
using Material.Icons.Avalonia;

namespace FbStudio.App.Tests;

/// <summary>Colores propios de los entornos y las carpetas (F-20).</summary>
[Collection(nameof(UiCollection))]
public sealed class EnvironmentColorsTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-colors-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Color ColorOf(object? brush) => ((ISolidColorBrush)brush!).Color;

    [Fact]
    public Task Opciones_elige_el_color_de_cada_entorno_y_no_toca_el_de_las_carpetas() => ui.Session.DispatchAsync(() =>
    {
        var options = new OptionsViewModel(new AppSettings { FolderColors = new() { ["Clientes"] = "#1565C0" } }.Normalized());
        Assert.Equal("#2E7D32", options.DevelopmentColor!.Hex);                // los de siempre
        Assert.Equal("#C62828", options.ProductionColor!.Hex);
        Assert.DoesNotContain(options.TestColors, c => ColorPalette.IsRed(c.Hex));
        Assert.All(options.ProductionColors, c => Assert.True(ColorPalette.IsRed(c.Hex)));

        options.TestColor = options.TestColors.Single(c => c.Hex == "#1565C0");
        Assert.Equal("Azul", options.TestColor.Name);
        var saved = options.ToSettings();
        Assert.Equal(new Dictionary<ProfileEnvironment, string> { [ProfileEnvironment.Prueba] = "#1565C0" }, saved.EnvironmentColors);
        Assert.Equal("#1565C0", saved.FolderColors!["clientes"]);              // las de las carpetas pasan sin cambios
        options.ResetDefaultsCommand.Execute(null);
        Assert.Equal("#B26A00", options.TestColor!.Hex);
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task La_franja_y_los_iconos_toman_el_color_elegido() => ui.Session.DispatchAsync(() =>
    {
        var app = Application.Current!;
        try
        {
            App.ApplyEnvironmentColors(new AppSettings { EnvironmentColors = new() { [ProfileEnvironment.Prueba] = "#1565C0" } }.Normalized());
            Assert.Equal(Color.Parse("#1565C0"), ColorOf(app.Resources["FbsBannerTestBrush"]));
            Assert.True(app.TryGetResource("FbsEnvTestBrush", ThemeVariant.Light, out var light));
            Assert.Equal(Color.Parse("#1565C0"), ColorOf(light));
            Assert.True(app.TryGetResource("FbsEnvTestBrush", ThemeVariant.Dark, out var dark));
            Assert.True(ColorOf(dark).R > 0x15 && ColorOf(dark).B > 0xC0);      // en el tema oscuro, más claro
            Assert.Equal(Color.Parse("#C62828"), ColorOf(app.Resources["FbsBannerProdBrush"]));   // los demás, los de siempre
        }
        finally
        {
            App.ApplyEnvironmentColors(new AppSettings());
        }
        Assert.Equal(Color.Parse("#B26A00"), ColorOf(app.Resources["FbsBannerTestBrush"]));
        Assert.True(app.TryGetResource("FbsEnvTestBrush", ThemeVariant.Dark, out var back));
        Assert.Equal(Color.Parse("#FFB74D"), ColorOf(back));
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task Una_carpeta_con_su_color_en_el_arbol() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile with { Folder = "Clientes/Norte" }, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var w = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        w.Show();
        Pump();

        vm.SetFolderColor("Clientes", "#C62828");                             // rojo: es de producción, no se acepta
        Assert.Null(vm.FolderColor("Clientes"));
        vm.SetFolderColor("Clientes", "#00796B");
        Assert.Equal("#00796B", vm.FolderColor("clientes"));
        Pump();
        var folder = vm.Tree.Rows.Single(n => n.FolderPath == "Clientes");
        Assert.Equal(Color.Parse("#00796B"), ColorOf(folder.IconBrush));
        var icons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w.FindControl<ListBox>("Tree")!).OfType<MaterialIcon>()
            .Where(i => i.DataContext is TreeNode).ToList();
        Assert.Equal(Color.Parse("#00796B"), ColorOf(icons.Single(i => i.DataContext == folder).Foreground));
        var db = icons.Single(i => i.DataContext is TreeNode { Kind: TreeNodeKind.Database });
        Assert.True(app(db).TryGetResource("FbsEnvDevBrush", db.ActualThemeVariant, out var dev));
        Assert.Equal(ColorOf(dev), ColorOf(db.Foreground));                    // sin color propio: el del entorno

        vm.RenameFolder("Clientes", "Empresas");                               // el color acompaña a la carpeta
        Assert.Equal("#00796B", vm.FolderColor("Empresas"));
        Assert.Null(vm.FolderColor("Clientes"));
        vm.SetFolderColor("Empresas", null);
        Assert.Null(vm.FolderColor("Empresas"));
        Assert.Null(vm.Tree.Rows.Single(n => n.FolderPath == "Empresas").IconBrush);
        w.Close();
        await vm.DisposeAsync();

        static Application app(Control _) => Application.Current!;
    }, CancellationToken.None);

    [Fact]
    public Task La_franja_muestra_las_otras_bases_conectadas_y_lleva_a_ellas() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        var otra = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-prueba", Environment = ProfileEnvironment.Prueba };
        store.Upsert(fb.Profile, fb.Password);
        store.Upsert(otra, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
        w.Show();
        Pump();
        TreeNode Db(Guid id) => vm.Tree.Rows.Single(n => n.Kind == TreeNodeKind.Database && n.Profile!.Id == id);

        await vm.Tree.ConnectAsync(Db(fb.Profile.Id));
        Pump();
        Assert.Empty(vm.OtherConnections);                                      // la única conectada es la de la pestaña
        Assert.False(vm.HasOtherConnections);

        await vm.Tree.ConnectAsync(Db(otra.Id));                                // la consulta pasa a la otra base
        Pump();
        var chip = Assert.Single(vm.OtherConnections);
        Assert.Equal((fb.Profile.Name, true), (chip.Name, chip.IsDev));
        Assert.Contains("Clic para ir a su pestaña", chip.Tip);

        var tabs = vm.Tabs.Count;
        chip.GoTo.Execute(null);                                                // no tiene pestaña: abre una consulta en ella
        Assert.Equal(fb.Profile.Id, vm.SelectedProfile!.Id);
        Assert.Equal(tabs + 1, vm.Tabs.Count);
        var back = Assert.Single(vm.OtherConnections);
        Assert.Equal(("docker-prueba", true), (back.Name, back.IsTest));
        back.GoTo.Execute(null);                                                // ya tiene pestaña: va a esa
        Assert.Equal(otra.Id, vm.SelectedProfile!.Id);
        Assert.Equal(tabs + 1, vm.Tabs.Count);

        await vm.Tree.DisconnectAsync(fb.Profile.Id);
        Assert.Empty(vm.OtherConnections);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
