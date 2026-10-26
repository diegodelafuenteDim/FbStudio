using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>Pestaña «Trace» (F-24) en la ventana real, contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class TraceTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-trace-" + Guid.NewGuid().ToString("N"));

    private (MainWindow Window, MainWindowViewModel Vm) Open(ConnectionProfile profile)
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder),
            AppServices.CreateMonitorQueryStore(_folder));
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

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 20_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(50);
            Pump();
        }
    }

    [Fact]
    public Task Traza_otra_conexion_analiza_por_consulta_y_guarda_el_log_sin_rutas() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(fb.Profile);
        Assert.Null(vm.OpenTrace());                                          // sin base elegida
        var trace = vm.OpenTrace(fb.Profile)!;
        Assert.Same(trace, vm.OpenTrace(fb.Profile));                         // una por base
        Assert.Equal((EditorTabKind.Trace, "Trace"), (trace.Kind, trace.BaseTitle));
        Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<TraceTabView>());
        Assert.True(trace.SeesOnlyOwn);                                       // el usuario del contenedor no es SYSDBA

        trace.ThresholdMs = 0;
        await trace.StartAsync();
        Assert.True(trace.IsTracing);
        Assert.StartsWith("Trace activo en docker-fb3 desde las", trace.Status);
        Assert.False(trace.StartCommand.CanExecute(null));

        await using (var session = fb.NewSession())
        {
            await FirebirdFixture.RunAsync(session, "SELECT COUNT(*) FROM TURNOS WHERE PACIENTE = 'PACIENTE 7'");
            await FirebirdFixture.RunAsync(session, "SELECT COUNT(*) FROM TURNOS WHERE PACIENTE = 'PACIENTE 8'");
        }
        await WaitFor(() => trace.Events.Count(e => e.Sql?.Contains("PACIENTE 8") == true) == 1);
        var group = Assert.Single(trace.Groups, g => g.Key == "SELECT COUNT(*) FROM TURNOS WHERE PACIENTE = ?");
        Assert.Equal(2, group.Count);                                         // las dos juntas: solo cambia el valor
        Assert.True(group.FullScan);

        var view = (TraceTabView)Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<TraceTabView>().Single();
        Assert.True(view.Analysis.Rows.Count >= 1);
        trace.SelectGroup(view.Analysis.Rows.First(r => (string)r[4] == group.Key));
        Assert.Contains("Se ejecutó 2 veces", trace.Detail);
        Assert.Contains("PLAN (TURNOS NATURAL)", trace.Detail);
        Assert.Contains("TURNOS: 50.000 sin índice", trace.Detail.Replace(",", "."));

        trace.UnifyValues = false;                                            // separadas
        Assert.Equal(2, trace.Groups.Count(g => g.Key.StartsWith("SELECT COUNT(*) FROM TURNOS WHERE PACIENTE")));

        await trace.StopAsync();
        Assert.False(trace.IsTracing);
        Assert.StartsWith("Trace detenido", trace.Status);

        var log = Path.Combine(_folder, "traza.log");
        Directory.CreateDirectory(_folder);
        Assert.True(trace.SaveLog(log));
        var text = await File.ReadAllTextAsync(log);
        Assert.Contains("PACIENTE 7", text);
        Assert.DoesNotContain("/firebird/data", text);                       // la ruta no se guarda (D-54)

        trace.Clear();
        Assert.Empty(trace.Events);
        trace.OpenLog(log);
        Assert.Contains(trace.Events, e => e.Sql?.Contains("PACIENTE 7") == true);
        Assert.StartsWith("Log abierto:", trace.Status);

        // Comparar con un período guardado (acá, el mismo log): todo igual; se elige una fila y se quita.
        trace.CompareWith(log);
        Assert.True(trace.HasBaseline);
        Assert.Equal("traza.log", trace.BaselineName);
        Assert.Equal(trace.Groups.Count, trace.Changes.Count);
        Assert.All(trace.Changes, c => Assert.Equal(Core.Trace.TraceChangeKind.Same, c.Kind));
        Assert.Contains("0 más lentas, 0 más rápidas, 0 nuevas, 0 ya no aparecen", trace.ComparisonSummary);
        Pump();
        Assert.Equal(trace.Changes.Count, view.Comparison.Rows.Count);
        trace.SelectChange(view.Comparison.Rows[0]);
        Assert.StartsWith("igual", trace.Detail);
        Assert.Contains("Antes: ", trace.Detail);
        Assert.Contains("Ahora: ", trace.Detail);
        trace.ClearBaselineCommand.Execute(null);
        Assert.False(trace.HasBaseline);
        Assert.Empty(trace.Changes);

        // El análisis, a Excel: las consultas que se ven, con las mismas columnas.
        var xlsx = Path.Combine(_folder, "analisis.xlsx");
        Assert.Equal(view.Analysis.VisibleRowIndexes().Count, view.ExportAnalysisTo(xlsx));
        Assert.StartsWith("Análisis exportado:", trace.Status);
        var sheet = Core.Import.ImportFile.Excel(xlsx);
        Assert.Equal(view.Analysis.Columns.Select(c => c.Name), sheet.Headers);
        Assert.Contains(sheet.Rows(), r => r[4]?.ToString()?.Contains("FROM TURNOS") == true);

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task En_produccion_pregunta_y_se_detiene_sola() => ui.Session.DispatchAsync(async () =>
    {
        var prod = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-prod", Environment = ProfileEnvironment.Produccion };
        var (w, vm) = Open(prod);
        var prompts = new List<ConfirmPrompt>();
        var answer = false;
        vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(answer); };
        var trace = vm.OpenTrace(prod)!;

        await trace.StartAsync();
        Assert.False(trace.IsTracing);                                        // dijo que no
        var prompt = Assert.Single(prompts);
        Assert.Contains("PRODUCCIÓN", prompt.Reasons[0]);
        Assert.Contains("100 ms", prompt.Reasons[0]);
        Assert.Contains("solo a los 10 minutos", prompt.Reasons[0]);

        answer = true;
        trace.AutoStopAfter = TimeSpan.FromSeconds(1);
        await trace.StartAsync();
        Assert.True(trace.IsTracing);
        await WaitFor(() => !trace.IsTracing);
        Assert.StartsWith("El trace se detuvo solo a los 10 minutos", trace.Status);

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Si_el_servidor_corta_la_sesion_lo_dice() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(fb.Profile);
        var trace = vm.OpenTrace(fb.Profile)!;
        await trace.StartAsync();
        Assert.True(trace.IsTracing);
        var id = trace.SessionId!.Value;

        // Otro la detiene (como si la cortara el servidor): la pestaña deja de mostrarla activa y dice por qué.
        var other = new FirebirdSql.Data.Services.FbTrace(FirebirdSql.Data.Services.FbTraceVersion.Detect,
            FbStudio.Firebird.FbConnectionFactory.ConnectionString(fb.Profile, fb.Password));
        await other.StopAsync(id, CancellationToken.None);
        await WaitFor(() => !trace.IsTracing);
        Assert.Equal("El servidor terminó el trace · 0 eventos · motivo: no lo dijo", trace.Status);
        Assert.True(trace.StartCommand.CanExecute(null));

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
