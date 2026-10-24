using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace FbStudio.App.Tests;

/// <summary>
/// nbackup, paso 1 (F-29): avisar si la base quedó en modo backup y mostrar el historial, contra Firebird 3 en Docker.
/// Con una base propia: poner la compartida en modo backup cambiaría lo que ven las demás pruebas.
/// </summary>
[Collection(nameof(UiCollection))]
public sealed class NBackupUiTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-nbackup-" + Guid.NewGuid().ToString("N"));

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

    private async Task ExecAsync(ConnectionProfile profile, string sql)
    {
        await using var c = FbConnectionFactory.Create(profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var cmd = new FbCommand(sql, c, tx)) await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    [Fact]
    public Task Avisa_en_el_arbol_el_monitoreo_y_las_estadisticas_y_muestra_el_historial() => ui.Session.DispatchAsync(async () =>
    {
        var name = $"nbkui_{Guid.NewGuid():N}";
        var db = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-nbackup", Database = $"/firebird/data/{name}.fdb", User = "SYSDBA" };
        await FbConnection.CreateDatabaseAsync(FbConnectionFactory.ConnectionString(db, fb.Password), 8192, true, true);
        await new FbNBackup(FbConnectionFactory.ConnectionString(db, fb.Password)) { Level = 0, BackupFile = $"/firebird/data/{name}.nbk0" }.ExecuteAsync();
        await ExecAsync(db, "ALTER DATABASE BEGIN BACKUP");                // como un nbackup -L que nadie terminó
        try
        {
            var store = AppServices.CreateProfileStore(_folder);
            store.Upsert(db, fb.Password);
            var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
            var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            w.Show();
            Pump();

            // Al conectar: la marca al lado de la base, el detalle en el tooltip y el aviso en la barra de estado.
            var node = Assert.Single(vm.Tree.Rows, n => n.Kind == TreeNodeKind.Database);
            await vm.Tree.ActivateAsync(node);
            Assert.Equal("⚠ modo backup", node.Alert);
            Assert.Contains("MODO BACKUP (nbackup)", node.Tooltip);
            Assert.Contains("MODO BACKUP", vm.Tree.Status);

            // Monitoreo y estadísticas.
            var monitor = vm.OpenMonitor(vm.Profiles.Single())!;
            await WaitFor(() => monitor.Summary is not null);
            monitor.Interval = 0;
            Assert.True(monitor.HasBackupWarning);
            var stats = vm.OpenStatistics(vm.Profiles.Single())!;
            await stats.Reading;
            Assert.True(stats.HasBackupWarning);

            // Mantenimiento → nbackup: estado, delta e historial, sin la carpeta del servidor (D-54).
            var maint = vm.OpenMaintenance(vm.Profiles.Single(), MaintenanceTask.NBackup)!;
            await WaitFor(() => maint.NBackup is not null);
            await maint.Working;
            Assert.True(maint.HasNBackupWarning);
            Assert.False(maint.RunCommand.CanExecute(null));                // por ahora solo se mira
            Assert.Equal("El archivo delta va junto a la base (lo de fábrica).", maint.NBackupDeltaText);
            Assert.Equal("Backups de nbackup registrados en la base (1)", maint.NBackupHistoryTitle);
            var entry = Assert.Single(maint.NBackupHistory);
            Assert.Equal(($"{name}.nbk0", "Nivel 0 (completo)"), (entry.FileName, entry.LevelText));
            Assert.DoesNotContain("/firebird/data", maint.Document.Text);

            // Terminado el modo backup, «Actualizar» lo dice y los avisos se van.
            await ExecAsync(db, "ALTER DATABASE END BACKUP");
            await maint.CheckStateAsync();
            Assert.False(maint.HasNBackupWarning);
            Assert.Equal("La base no está en modo backup.", maint.NBackupStateText);
            await monitor.RefreshAsync();
            Assert.False(monitor.HasBackupWarning);
            await vm.Tree.RefreshDatabaseAsync(node);
            Assert.Null(node.Alert);

            w.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { await ExecAsync(db, "ALTER DATABASE END BACKUP"); } catch (FbException) { /* ya estaba terminado */ }
        }
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
