using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>Consultas con parámetros en el editor (F-25), contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class QueryParametersUiTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-params-" + Guid.NewGuid().ToString("N"));
    private FbStudio.Workspace.HistoryStore? _history;

    private (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        _history = AppServices.CreateHistoryStore(_folder);
        var vm = new MainWindowViewModel(store, _history, AppServices.CreateSettingsStore(_folder));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        vm.NewQueryTab(vm.Profiles.Single());
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Pide_los_valores_los_recuerda_y_si_se_cancela_no_ejecuta() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var asked = new List<IReadOnlyList<ParameterEntry>>();
        IReadOnlyList<ParameterEntry>? answer = [new("desde", "10", false), new("hasta", "12", false)];
        vm.AskParametersAsync = entries => { asked.Add(entries); return Task.FromResult<IReadOnlyList<ParameterEntry>?>(answer); };
        var tab = vm.SelectedTab!;

        await tab.RunTextAsync("SELECT ID FROM TURNOS WHERE ID BETWEEN :desde AND :hasta ORDER BY ID", 0, ExecutionScope.All);
        Pump();
        Assert.Equal([10, 11, 12], tab.Rows.Select(r => Convert.ToInt32(r[0])));
        Assert.Equal(["desde", "hasta"], asked[0].Select(e => e.Name));
        Assert.All(asked[0], e => Assert.Equal("", e.Text));                 // la primera vez, vacíos

        // La segunda vez vienen con lo último que se usó; «?» sin nombre se numera y no se recuerda.
        answer = [new("hasta", "3", false), new("#1", "1", false)];
        await tab.RunTextAsync("SELECT ID FROM TURNOS WHERE ID <= :Hasta AND ID >= ? ORDER BY ID", 0, ExecutionScope.All);
        Pump();
        Assert.Equal(("12", "#1"), (asked[1][0].Text, asked[1][1].Name));
        Assert.Equal([1, 2, 3], tab.Rows.Select(r => Convert.ToInt32(r[0])));

        // El historial guarda el SQL como se escribió, con sus nombres.
        Assert.Contains(_history!.Recent(fb.Profile.Id), h => h.Sql.Contains(":Hasta"));

        answer = null;                                                         // Cancelar
        await tab.RunTextAsync("SELECT ID FROM TURNOS WHERE ID = :desde", 0, ExecutionScope.All);
        Assert.Equal("No se ejecutó: se cancelaron los parámetros.", tab.Status);

        // En un script no se piden: se avisa cómo hacerlo.
        await tab.RunTextAsync("SELECT 1 FROM RDB$DATABASE;\nSELECT ID FROM TURNOS WHERE ID = :desde;", 0, ExecutionScope.All);
        Assert.StartsWith("Los parámetros (? o :nombre) se piden al ejecutar una sentencia sola", tab.Status);

        // En PSQL «:variable» no es un parámetro.
        asked.Clear();
        await tab.RunTextAsync("EXECUTE BLOCK RETURNS (N INTEGER) AS BEGIN SELECT COUNT(*) FROM TURNOS WHERE ID < 5 INTO :N; SUSPEND; END", 0, ExecutionScope.All);
        Pump();
        Assert.Empty(asked);
        Assert.Equal(5, Convert.ToInt32(tab.Rows[0][0]));

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task La_ventana_devuelve_lo_escrito_y_el_NULL() => ui.Session.DispatchAsync(async () =>
    {
        var owner = new Window();
        owner.Show();
        var dialog = new ParameterDialog([new ParameterEntry("desde", "01/10/2026", false), new ParameterEntry("#1", "", false)]);
        var result = dialog.ShowDialog<IReadOnlyList<ParameterEntry>?>(owner);
        Pump();
        Assert.Equal("Parámetros de la consulta", dialog.Title);
        var texts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(":desde", texts);
        Assert.Contains("Parámetro 1", texts);
        dialog.Boxes[1].Text = "x";
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<CheckBox>().Last().IsChecked = true;
        Pump();
        Assert.False(dialog.Boxes[1].IsEnabled);
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Button>().First(b => b.Content as string == "Ejecutar")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var values = await result;
        Assert.NotNull(values);
        Assert.Equal([new ParameterEntry("desde", "01/10/2026", false), new ParameterEntry("#1", "x", true)], values);
        owner.Close();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
