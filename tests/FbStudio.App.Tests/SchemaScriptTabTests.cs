using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>Pestaña «Script de la base» contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class SchemaScriptTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-schema-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public Task Arma_el_script_y_lo_lleva_a_una_pestana_sin_base() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.OpenSchemaScript(null, new SchemaScriptOptions()));          // sin base elegida: avisa
        Assert.StartsWith("Elegí una base en el árbol para sacar su script", vm.Tree.Status);
        var tab = vm.OpenSchemaScript(fb.Profile, new SchemaScriptOptions { Privileges = false })!;
        await tab.Generating;
        Assert.Equal((EditorTabKind.Script, "Script de la base"), (tab.Kind, tab.BaseTitle));
        Assert.StartsWith("Script listo: ", tab.Status);
        Assert.True(tab.HasScript);
        var script = tab.Document.Text;
        Assert.StartsWith("/* Script de la base docker-fb3", script);
        Assert.Contains("CREATE TABLE TURNOS (", script);
        Assert.Contains("CREATE OR ALTER PROCEDURE P_TURNOS_DESDE", script);
        Assert.DoesNotContain("/* Permisos */", script);                              // no se pidieron
        Assert.StartsWith("Generado a las ", tab.GeneratedAt);

        // La documentación HTML sale de lo mismo que se leyó (sin volver a leer la base).
        var html = tab.DocumentationHtml(includeSources: false);
        Assert.Contains("<title>Documentación de docker-fb3</title>", html);
        Assert.Contains("<section id=\"t-TURNOS\" data-name=\"turnos\">", html);
        Assert.Contains("<p class=\"desc\">Turnos de prueba</p>", html);
        Assert.Contains("FOREIGN KEY (TURNO) → <a href=\"#t-TURNOS\">TURNOS</a> (ID) ON DELETE CASCADE", html);
        Assert.Contains("<section id=\"p-P_TURNOS_DESDE\"", html);
        Assert.DoesNotContain("SUSPEND", html);
        Assert.Contains("SUSPEND", tab.DocumentationHtml(includeSources: true));

        // F9 en esta pestaña no ejecuta nada (el script es para otra base).
        await w.RunAsync();
        Assert.Equal(script, tab.Document.Text);

        // «Abrir en una pestaña sin base»: el mismo texto, en una consulta que todavía no apunta a ninguna base.
        tab.OpenWithoutDatabaseCommand.Execute(null);
        var query = vm.SelectedTab!;
        Assert.Null(query.Profile);
        Assert.Equal(script, query.Document.Text);
        Assert.StartsWith("Script sin base: hacé doble clic en la base VACÍA", query.Status);

        // Desconectar la base cierra también su pestaña de script; la consulta sin base queda.
        await vm.DisconnectAsync(fb.Profile);
        Assert.DoesNotContain(tab, vm.Tabs);
        Assert.Contains(query, vm.Tabs);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task El_script_se_puede_limitar_a_un_area_y_las_estadisticas_tildan_sus_tablas() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var profile = vm.Profiles.Single();
        vm.AreaStore.Save(vm.AreaStore.PathFor(profile), profile.Id,
            new SchemaAreaFile { Areas = [new SchemaArea { Name = "Detalle", Rules = ["TURNOS_DET"] }] }, null);

        var tab = vm.OpenSchemaScript(profile, new SchemaScriptOptions { Area = "Detalle" })!;
        await tab.Generating;
        var script = tab.Document.Text;
        Assert.Contains("-- Solo el área «Detalle»: sus objetos y lo que usan; 1 FK a tablas de afuera no se escriben.", script);
        Assert.Contains("CREATE TABLE TURNOS_DET (", script);
        Assert.DoesNotContain("CREATE TABLE TURNOS (", script);
        Assert.DoesNotContain("REFERENCES TURNOS ", script);                        // la FK a TURNOS no va
        Assert.Contains("área «Detalle»", tab.GeneratedAt);
        Assert.Contains("Área: Detalle", tab.DocumentationHtml(includeSources: false));

        // Estadísticas: «Tildar las tablas de un área».
        var stats = vm.OpenStatistics(profile)!;
        Assert.Equal(["Detalle"], stats.AreaNames);
        await stats.ChooseAreaAsync("Detalle");
        Assert.Equal(["TURNOS_DET"], stats.ChosenTables);
        Assert.Equal("Tildadas las 1 tablas del área «Detalle».", stats.Status);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temporal */ }
    }
}
