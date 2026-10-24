using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>Pestaña «Estadísticas» (gstat por la API de servicios) contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class StatisticsTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-stats-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Lee_el_encabezado_al_abrir_y_la_completa_solo_si_se_confirma() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Pump();

        // Sin pestañas ni base elegida: avisa. Con la base, abre la pestaña y lee el encabezado sola.
        Assert.Null(vm.OpenStatistics());
        Assert.StartsWith("Elegí una base en el árbol para ver sus estadísticas", vm.Tree.Status);
        var stats = vm.OpenStatistics(fb.Profile)!;
        await stats.Reading;
        Pump();
        Assert.Equal((EditorTabKind.Statistics, "Estadísticas"), (stats.Kind, stats.BaseTitle));
        Assert.Equal($"Encabezado de {fb.Profile.Name} leído.", stats.Status);
        Assert.True(stats.HasHeader);
        Assert.Equal("8192", Assert.Single(stats.HeaderRows, r => r.Label == "Tamaño de página").Value);
        Assert.Equal("Oldest active", Assert.Single(stats.HeaderRows, r => r.Label == "Transacción activa más vieja (OAT)").Key);
        Assert.Equal("20.000 (de fábrica)", stats.SweepText);
        Assert.Equal("8.192 · ODS 12.0", stats.PageText);
        Assert.Equal(("Sí", false), (stats.ForcedWritesText, stats.IsForcedWritesOff));
        Assert.Contains("Database header page information", stats.Document.Text);
        Assert.StartsWith("Leído a las ", stats.ReadAt);

        // Otra vez desde el menú: la misma pestaña. F9 vuelve a leer el encabezado.
        Assert.Same(stats, vm.OpenStatistics(fb.Profile));
        await stats.Reading;
        await w.RunAsync();
        Assert.Single(vm.Tabs);

        // La completa recorre toda la base: pregunta antes; si se dice que no, no lee nada.
        var prompts = new List<ConfirmPrompt>();
        var answer = false;
        vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(answer); };
        await stats.ReadFullAsync();
        Assert.DoesNotContain("TURNOS (", stats.Document.Text);
        Assert.DoesNotContain(prompts[0].Reasons, r => r.Contains("PRODUCCIÓN"));
        answer = true;
        await stats.ReadFullAsync();
        Assert.Contains("TURNOS (", stats.Document.Text);
        Assert.Contains("Data pages", stats.Document.Text);
        Assert.StartsWith("Estadística completa leída: ", stats.Status);
        Assert.Equal("12.0", stats.Header!.OdsVersion);                      // el encabezado también viene en la completa

        // Tablas e índices en grillas (se ordenan y filtran como cualquier resultado), y se pasa a la de tablas.
        Pump();
        Assert.Equal(0, stats.SelectedDetail);
        Assert.Contains("tablas", stats.Status);
        var turnos = Assert.Single(stats.Analysis!.Tables, t => t.Name == "TURNOS");
        Assert.Equal(50_000, turnos.Records);
        Assert.Contains(turnos.Indexes, i => i.Name == "TURNOS_FECHA" && i.Nodes == 50_000);
        Assert.StartsWith("Tablas (", stats.TablesHeader);
        var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<StatisticsTabView>().Single();
        Assert.Equal(stats.Analysis.Tables.Count, view.Tables.Rows.Count);
        Assert.Equal("Registros", view.Tables.Columns[1].Name);
        Assert.Contains(view.Indexes.Rows, r => (string)r[1] == "TURNOS_FECHA" && Convert.ToInt64(r[4]) == 50_000);

        // En producción, la pregunta lo dice.
        var prod = vm.CloneProfile(vm.Profiles.Single());
        vm.ProfileStore.Upsert(prod with { Environment = ProfileEnvironment.Produccion }, null);
        vm.ReloadProfiles();
        var prodStats = vm.OpenStatistics(vm.Profiles.Single(p => p.Id == prod.Id))!;
        await prodStats.Reading;
        answer = false;
        await prodStats.ReadFullAsync();
        Assert.Contains(prompts[^1].Reasons, r => r.Contains("PRODUCCIÓN"));

        // Desconectar la base cierra también sus estadísticas.
        Assert.True(await vm.DisconnectAsync(fb.Profile));
        Assert.DoesNotContain(stats, vm.Tabs);
        Assert.Contains(prodStats, vm.Tabs);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Lee_solo_las_tablas_elegidas_y_compara_con_otra_base() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        // La «otra base» es la misma del contenedor con otro registro (de producción): alcanza para ver la comparación.
        var other = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-otra", Environment = ProfileEnvironment.Produccion };
        store.Upsert(other, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        var stats = vm.OpenStatistics(vm.Profiles.Single(p => p.Id == fb.Profile.Id))!;
        await stats.Reading;
        Pump();
        Assert.Equal(("Tablas: todas", "Estadística completa…"), (stats.TablesText, stats.FullText));
        Assert.Equal(["(no comparar)", "docker-otra"], stats.CompareChoices);

        await stats.LoadTablesAsync();
        stats.TableFilter = "turnos";
        Assert.Contains(stats.VisibleTables, t => t.Name == "TURNOS_DET");
        Assert.All(stats.VisibleTables, t => Assert.Contains("TURNOS", t.Name));
        foreach (var t in stats.AllTables.Where(t => t.Name is "TURNOS" or "TURNOS_DET")) t.IsChecked = true;
        Assert.Equal(("Tablas: 2 elegidas", "Estadística de 2 tablas…"), (stats.TablesText, stats.FullText));
        stats.CompareIndex = 1;

        var prompts = new List<ConfirmPrompt>();
        vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(true); };
        await stats.ReadFullAsync();
        Pump();
        var reasons = prompts.Single().Reasons;
        Assert.Equal("Estadística de tablas elegidas", prompts[0].Title);
        Assert.StartsWith($"Se van a leer las páginas de datos e índices de 2 tablas de {fb.Profile.Name}: TURNOS, TURNOS_DET.", reasons[0]);
        Assert.Contains("Después se lee lo mismo en «docker-otra», para compararlas.", reasons);
        Assert.Contains("«docker-otra» es una base de PRODUCCIÓN: mejor fuera del horario de atención.", reasons);

        // Solo esas dos tablas, en las dos bases, lado a lado.
        Assert.Equal(["TURNOS", "TURNOS_DET"], stats.Analysis!.Tables.Select(t => t.Name).Order());
        Assert.StartsWith("Estadística de 2 tablas leída", stats.Status);
        Assert.Contains("Comparada con «docker-otra»", stats.Status);
        Assert.Equal(StatisticsTabViewModel.CompareTablesDetail, stats.SelectedDetail);
        Assert.Equal(("Comparar tablas (2)", $"A = {fb.Profile.Name} · B = docker-otra. Diferencia = B − A."), (stats.CompareTablesHeader, stats.CompareLegend));
        var turnos = stats.Comparison!.Tables.Single(t => t.Name == "TURNOS");
        Assert.Equal((50_000L, 50_000L), (turnos.A!.Records, turnos.B!.Records));
        Assert.Equal(0, turnos.RecordsDifference);
        Assert.Contains("── docker-otra ──", stats.Document.Text);
        var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<StatisticsTabView>().Single();
        Assert.Equal(2, view.CompareTables.Rows.Count);
        Assert.Equal(["Tabla", "Registros (A)", "Registros (B)", "Diferencia"], view.CompareTables.Columns.Take(4).Select(c => c.Name));
        Assert.Contains(view.CompareIndexes.Rows, r => (string)r[1] == "TURNOS_FECHA" && Convert.ToInt64(r[4]) == 50_000 && Convert.ToInt64(r[5]) == 50_000);

        // «Todas» quita la elección; sin comparar, la completa ya no muestra la comparación vieja.
        stats.ClearTablesCommand.Execute(null);
        stats.CompareIndex = 0;
        await stats.ReadFullAsync();
        Assert.Equal("Estadística completa", prompts[^1].Title);
        Assert.False(stats.HasComparison);
        Assert.StartsWith("Estadística completa leída: ", stats.Status);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temporal */ }
    }
}
