using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Workspace;
using FbStudio.Core.Monitoring;
using FbStudio.Core.Results;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>Pestaña «Monitoreo» (F-1) en la ventana real, contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class MonitorTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-mon-" + Guid.NewGuid().ToString("N"));

    private (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder),
            AppServices.CreateMonitorQueryStore(_folder));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        vm.NewQueryTab(vm.Profiles.Single());                               // como un doble clic en la base
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 10_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("La condición no se cumplió a tiempo.");
            await Task.Delay(20);
            Pump();
        }
    }

    private static async Task<long> AttachmentIdAsync(Firebird.QuerySession s)
    {
        var (_, rows) = await FirebirdFixture.RunAsync(s, "SELECT CURRENT_CONNECTION FROM RDB$DATABASE");
        return Convert.ToInt64(rows[0][0]);
    }

    [Fact]
    public Task Muestra_la_OAT_y_corta_la_conexion_que_la_tiene() => ui.Session.DispatchAsync(async () =>
    {
        await using var holder = fb.NewSession();
        await FirebirdFixture.RunAsync(holder, "UPDATE TURNOS SET OBS = OBS WHERE ID = 10");
        var holderTx = holder.TransactionId!.Value;
        var holderAttachment = await AttachmentIdAsync(holder);

        var (w, vm) = Open();
        var asked = new List<ConfirmPrompt>();
        vm.ConfirmActionAsync = p => { asked.Add(p); return Task.FromResult(true); };
        var monitor = vm.OpenMonitor(fb.Profile)!;
        Assert.Same(monitor, vm.SelectedTab);
        Assert.Same(monitor, vm.OpenMonitor(fb.Profile));                   // una sola pestaña por base
        await WaitFor(() => monitor.Summary is not null && monitor.Results.Count > 0);
        monitor.Interval = 0;                                                // en esta prueba, a mano
        Assert.Equal(holderTx.ToString("N0"), monitor.OatText);
        Assert.Contains("conexiones", monitor.CountsText);
        Assert.False(monitor.HasVisibilityWarning);                         // el usuario del contenedor es el dueño de la base: ve todas
        Assert.Equal("transactions", monitor.SelectedPreset.Key);           // abre siempre en «Transacciones»
        monitor.SelectedPreset = monitor.Presets.Single(p => p.Key == "attachments");
        await WaitFor(() => monitor.ResultColumns.Length > 2 && monitor.ResultColumns[2].Name == "PID del servidor");

        // Conexiones: la fila de quien tiene la OAT, marcada; se elige en la grilla y se corta.
        var view = (MonitorTabView)w.FindDescendant<MonitorTabView>()!;
        var index = monitor.Results.ToList().FindIndex(r => Convert.ToInt64(r[0]) == holderAttachment);
        Assert.Equal("⚠ tiene la OAT", monitor.Results[index][1]);         // el símbolo lo pone la app, no el SQL
        view.Grid.SelectCell(index, 0);
        Pump();
        Assert.Same(monitor.Results[index], monitor.SelectedRow);
        Assert.True(monitor.KillCommand.CanExecute(null));
        Assert.False(monitor.CancelStatementCommand.CanExecute(null));     // en «Conexiones» no hay sentencia
        await monitor.KillCommand.ExecuteAsync(null);
        Assert.Contains($"{holderAttachment}", Assert.Single(asked).Reasons[0]);
        Assert.Contains("cortada", monitor.Status);
        Assert.DoesNotContain(monitor.Results, r => Convert.ToInt64(r[0]) == holderAttachment);

        // Transacciones: ya no está la del que se cortó.
        monitor.SelectedPreset = monitor.Presets.Single(p => p.Key == "transactions");
        await WaitFor(() => monitor.ResultColumns.Length > 2 && monitor.ResultColumns[2].Name == "Tx");
        Assert.DoesNotContain(monitor.Results, r => Convert.ToInt64(r[2]) == holderTx);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public void La_OAT_vieja_se_pinta_ambar_a_los_10_minutos_y_roja_pasada_la_hora() => ui.Session.Dispatch(() =>
    {
        var (w, vm) = Open();
        var monitor = new MonitorTabViewModel(vm, fb.Profile);
        MonitorSummary At(long seconds, string? process = @"C:\Apps\Agenda\AgendaApp.exe") =>
            new(100, 10, 11, 11, 3, 2, 0, seconds, 1, "TEST", null) { OatUser = "APP_WEB", OatIp = "192.0.2.10", OatProcess = process };
        // Por defecto: ámbar pasados 2 minutos, rojo pasados 10 (Opciones → Monitoreo).
        monitor.Summary = At(90);
        Assert.Equal((false, false, false), (monitor.IsOatWarn, monitor.IsOatDanger, monitor.HasOatWarning));
        monitor.Summary = At(3 * 60);
        Assert.Equal((true, false, true), (monitor.IsOatWarn, monitor.IsOatDanger, monitor.HasOatWarning));
        Assert.Contains("APP_WEB · 192.0.2.10 · AgendaApp.exe", monitor.OatWarning);   // quién la tiene
        monitor.Summary = At(7 * 3600 + 16 * 60);                           // lo que se vio en una base de prueba: 7 h 16 min
        Assert.Equal((false, true), (monitor.IsOatWarn, monitor.IsOatDanger));
        Assert.Equal("7 h 16 min", monitor.OatAgeText);
        Assert.Equal("APP_WEB · 192.0.2.10 · AgendaApp.exe", monitor.OatHolderText);
        monitor.Summary = At(7 * 3600) with { OatHost = "PC-LAB-03" };    // con el equipo, si el programa lo informa
        Assert.Equal("APP_WEB · 192.0.2.10 · PC-LAB-03 · AgendaApp.exe", monitor.OatHolderText);

        // Un backup (gbak) o un sweep (gfix) tienen la OAT mientras trabajan: se informa sin alarma…
        monitor.Summary = At(7 * 3600, @"C:\Program Files\Firebird\gbak.exe");
        Assert.Equal((false, false, false, true), (monitor.IsOatWarn, monitor.IsOatDanger, monitor.HasOatWarning, monitor.HasOatInfo));
        // …salvo que se apague la opción. Y los umbrales se eligen en Opciones.
        vm.SaveSettings(vm.Options with { OatIgnoreMaintenance = false, OatWarnMinutes = 30, OatDangerMinutes = 120 });
        monitor.Summary = At(7 * 3600, @"C:\Program Files\Firebird\gbak.exe");
        Assert.True(monitor.IsOatDanger);
        monitor.Summary = At(20 * 60);
        Assert.False(monitor.HasOatWarning);
        vm.SaveSettings(new AppSettings());

        // «Es una conexión de FbStudio en esta PC»: por el equipo o, si la consulta no lo trae, por la IP de la dirección.
        Assert.True(MonitorTabViewModel.IsThisMachine(Environment.MachineName, null));
        Assert.True(MonitorTabViewModel.IsThisMachine(null, "127.0.0.1/60029"));
        Assert.False(MonitorTabViewModel.IsThisMachine(null, "203.0.113.9/60029"));
        Assert.False(MonitorTabViewModel.IsThisMachine(null, null));

        // Esperas de bloqueo: ámbar si hay alguna, rojo si alguna lleva 30 s o más.
        monitor.Summary = At(10) with { WaitingStatements = 1, LongestWaitSeconds = 5 };
        Assert.Equal((true, false, "1 · la más larga 5 s"), (monitor.IsLockWarn, monitor.IsLockDanger, monitor.WaitingText));
        monitor.Summary = At(10) with { WaitingStatements = 2, LongestWaitSeconds = 45 };
        Assert.Equal((false, true), (monitor.IsLockWarn, monitor.IsLockDanger));
        w.Close();
    }, CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public Task Consultas_propias_se_arman_prueban_usan_comparten_y_borran() => ui.Session.DispatchAsync(async () =>
    {
        await using var holder = fb.NewSession();
        await FirebirdFixture.RunAsync(holder, "UPDATE TURNOS SET OBS = OBS WHERE ID = 13");
        var holderAttachment = await AttachmentIdAsync(holder);

        var (w, vm) = Open();
        vm.ConfirmActionAsync = _ => Task.FromResult(true);
        // El editor: se «escribe» el nombre y el SQL de «Mis conexiones» (alias "Attachment ID") y se prueba antes de guardar.
        MonitorQueryEditorViewModel? shown = null;
        vm.EditMonitorQueryAsync = async editor =>
        {
            shown = editor;
            editor.Name = "Mis conexiones";
            editor.Sql.Text = "SELECT a.mon$attachment_id as \"Attachment ID\", a.mon$remote_process as \"Remote Process\", a.mon$user as User_Name FROM mon$attachments a";
            await editor.TestAsync();
            return editor.Validate() is null;
        };
        var monitor = vm.OpenMonitor(fb.Profile)!;
        await WaitFor(() => monitor.Results.Count > 0);
        monitor.Interval = 0;
        Assert.Equal(["Predefinidas", "Propias (0)"], monitor.Items.Where(i => i.IsHeader).Select(i => i.Caption));

        await monitor.NewQueryCommand.ExecuteAsync(false);
        Assert.False(shown!.TestFailed, shown.TestResult);
        Assert.Equal("Attachment ID", shown.AttachmentColumn);                // detectada al probar
        Assert.True(monitor.SelectedPreset.IsCustom);
        Assert.Contains(monitor.Items, i => i is { IsHeader: true, Caption: "Propias (1)" });
        await WaitFor(() => monitor.ResultColumns.Length > 0 && monitor.ResultColumns[0].Name == "Attachment ID");

        // Desde la consulta propia también se corta una conexión (como «Shutdown attachment»).
        var view = w.FindDescendant<MonitorTabView>()!;
        var index = monitor.Results.ToList().FindIndex(r => Convert.ToInt64(r[0]) == holderAttachment);
        view.Grid.SelectCell(index, 0);
        Pump();
        Assert.True(monitor.KillCommand.CanExecute(null));
        await monitor.KillCommand.ExecuteAsync(null);
        Assert.Contains("cortada", monitor.Status);

        // Compartir: exportar, borrar e importar.
        var file = Path.Combine(_folder, "equipo.json");
        Assert.Equal(1, monitor.ExportQueries(file));
        await monitor.DeleteQueryCommand.ExecuteAsync(null);
        Assert.Contains(monitor.Items, i => i is { IsHeader: true, Caption: "Propias (0)" });
        Assert.False(monitor.SelectedPreset.IsCustom);
        Assert.Equal(1, monitor.ImportQueries(file));
        Assert.Contains(monitor.Items, i => i.Caption == "Mis conexiones");

        // Un SQL que no es un SELECT no se guarda.
        var bad = new MonitorQueryEditorViewModel(null, "borrar", "DELETE FROM MON$ATTACHMENTS", _ => throw new InvalidOperationException());
        Assert.NotNull(bad.Validate());
        await bad.TestAsync();
        Assert.True(bad.TestFailed);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Clonar_un_registro_copia_todo_con_otro_nombre_y_clonar_una_consulta_propia() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var original = vm.Profiles.Single();
        var copy = vm.CloneProfile(original);
        var second = vm.CloneProfile(original);
        Assert.Equal($"{original.Name} (copia)", copy.Name);
        Assert.Equal($"{original.Name} (copia 2)", second.Name);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal((original.Host, original.Database, original.User, original.Role, original.Environment),
            (copy.Host, copy.Database, copy.User, copy.Role, copy.Environment));
        Assert.Equal(fb.Password, vm.ProfileStore.GetPassword(copy.Id));     // la contraseña también (cifrada)
        Assert.Equal(3, vm.Tree.Rows.Count(n => n.Kind == TreeNodeKind.Database));

        // Clonar una consulta (propia o predefinida): mismo SQL y columnas, nombre «(copia)».
        vm.EditMonitorQueryAsync = _ => Task.FromResult(true);              // «Guardar» sin cambiar nada
        vm.MonitorQueries.Upsert(new CustomMonitorQuery(Guid.NewGuid(), "Mis conexiones", "SELECT a.MON$ATTACHMENT_ID FROM MON$ATTACHMENTS a", AttachmentColumn: "MON$ATTACHMENT_ID"));
        var monitor = vm.OpenMonitor(fb.Profile)!;
        monitor.Interval = 0;
        monitor.SelectedPreset = monitor.Presets.Single(p => p.Name == "Mis conexiones");
        await monitor.NewQueryCommand.ExecuteAsync(true);
        var clone = vm.MonitorQueries.Load().Single(q => q.Name == "Mis conexiones (copia)");
        Assert.Equal(("SELECT a.MON$ATTACHMENT_ID FROM MON$ATTACHMENTS a", "MON$ATTACHMENT_ID"), (clone.Sql, clone.AttachmentColumn));
        Assert.Equal("Mis conexiones (copia)", monitor.SelectedPreset.Name);
        await vm.CloseTabAsync(monitor);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Se_refresca_solo_conservando_el_orden_y_se_pausa_si_no_esta_a_la_vista() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var monitor = vm.OpenMonitor(fb.Profile)!;
        await WaitFor(() => monitor.Results.Count > 0);
        var view = (MonitorTabView)w.FindDescendant<MonitorTabView>()!;
        view.Grid.SortBy(0, SortDirection.Descending);
        await view.Grid.PendingBuild;

        monitor.Interval = 2;
        var first = monitor.LastRefresh;
        await WaitFor(() => monitor.LastRefresh != first, 6_000);
        await view.Grid.PendingBuild;
        Pump();
        Assert.Equal((0, SortDirection.Descending), (view.Grid.Spec.SortColumn, view.Grid.Spec.SortDirection)); // el orden sigue

        // Con otra pestaña a la vista no consulta la base.
        vm.SelectedTab = vm.Tabs[0];
        var paused = monitor.LastRefresh;
        await Task.Delay(2_600);
        Pump();
        Assert.Equal(paused, monitor.LastRefresh);
        await vm.CloseTabAsync(monitor);                                     // cerrarla para el refresco y suelta la conexión
        Assert.DoesNotContain(monitor, vm.Tabs);
        w.Close();
    }, CancellationToken.None);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}

internal static class VisualTreeSearch
{
    public static T? FindDescendant<T>(this Avalonia.Visual root) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().FirstOrDefault(v => v.IsVisible);
}
