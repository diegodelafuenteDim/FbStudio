using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.App.Tests;

/// <summary>Pestaña «Mantenimiento» (backup, restore, base vacía, validación, sweep) contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class MaintenanceTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-maint-" + Guid.NewGuid().ToString("N"));

    // SYSDBA puede crear bases; como producción, para ver las confirmaciones.
    private ConnectionProfile Sysdba => fb.Profile with { Name = "docker-sysdba", User = "SYSDBA", Environment = ProfileEnvironment.Produccion };

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private (MainWindowViewModel Vm, MainWindow Window) Open(params ConnectionProfile[] profiles)
    {
        var store = AppServices.CreateProfileStore(_folder);
        foreach (var p in profiles) store.Upsert(p, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Pump();
        return (vm, w);
    }

    [Fact]
    public Task Backup_y_restore_en_el_servidor_y_registrar_la_base_nueva() => ui.Session.DispatchAsync(async () =>
    {
        var (vm, w) = Open(Sysdba);
        Assert.Null(vm.OpenMaintenance());
        Assert.StartsWith("Elegí una base en el árbol para el mantenimiento", vm.Tree.Status);
        var tab = vm.OpenMaintenance(Sysdba)!;
        await tab.LoadAsync();
        Pump();
        Assert.Equal((EditorTabKind.Maintenance, "Mantenimiento"), (tab.Kind, tab.BaseTitle));
        Assert.Same(tab, vm.OpenMaintenance(Sysdba));
        // Sin la ruta de la base (D-54): el alias del registro, y los archivos los escribe el usuario.
        Assert.Equal("Base test · página de 8.192 · UTF8", tab.DatabaseInfo);
        Assert.Equal(("", "", ""), (tab.BackupFile, tab.RestoreTarget, tab.EmptyTarget));
        Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<MaintenanceTabView>());

        // Backup: lo hace el servidor; al terminar, el restore propone ese archivo.
        tab.BackupFile = "/firebird/data/ui_mant.fbk";
        await tab.RunAsync();
        Pump();
        Assert.StartsWith("Backup terminado: /firebird/data/ui_mant.fbk", tab.Status);
        Assert.Equal("/firebird/data/ui_mant.fbk", tab.RestoreSource);
        tab.FlushLog();
        Assert.Contains("Backup de docker-sysdba a /firebird/data/ui_mant.fbk", tab.Document.Text);
        // gbak dice «readied database /firebird/data/test for backup»: en el registro va el alias.
        Assert.Contains("readied database test", tab.Document.Text);
        Assert.DoesNotContain("/firebird/data/test", tab.Document.Text);
        await tab.CheckStateAsync();
        Assert.Equal("No hay ningún backup de SYSDBA corriendo: si lanzaste uno, ya terminó.", tab.Status);

        // Restore a un archivo nuevo; después se puede registrar (producción queda como prueba).
        const string Restored = "/firebird/data/ui_mant_rest.fdb";
        tab.SelectedTaskIndex = (int)MaintenanceTask.Restore;
        Assert.Equal("Restaurar", tab.RunText);
        tab.RestoreTarget = Restored;
        Assert.False(tab.CanRegisterNew);
        await tab.RunAsync();
        Pump();
        Assert.StartsWith($"Restore terminado: {Restored} quedó lista para usar", tab.Status);
        Assert.True(tab.CanRegisterNew);
        await tab.CheckStateAsync();
        Assert.Equal($"{Restored} está lista para usar.", tab.Status);

        // Otra vez al mismo archivo: no se pisa. Reemplazar pide escribir el nombre del archivo.
        await tab.RunAsync();
        Assert.Equal($"Error: Ya hay una base en {Restored}. Elegí otro archivo, o marcá «reemplazar si existe».", tab.Status);
        var typed = new List<TypedConfirm>();
        var answer = false;
        vm.ConfirmTypedAsync = p => { typed.Add(p); return Task.FromResult(answer); };
        tab.RestoreReplace = true;
        tab.Status = "";
        await tab.RunAsync();
        Assert.Equal(("ui_mant_rest", ""), (typed[^1].Expected, tab.Status));                // dijo que no: nada
        answer = true;
        await tab.RunAsync();
        Assert.StartsWith($"Restore terminado: {Restored}", tab.Status);

        // Ni reemplazando se pisa la base del registro, aunque se la escriba con su alias.
        tab.RestoreTarget = "test";
        await tab.RunAsync();
        Assert.StartsWith("Error: La base de destino es la misma base del perfil", tab.Status);

        tab.RestoreTarget = Restored;
        tab.RegisterNew();
        var copy = Assert.Single(vm.Profiles, p => p.Name == "docker-sysdba · ui_mant_rest");
        Assert.Equal((Restored, ProfileEnvironment.Prueba, "SYSDBA"), (copy.Database, copy.Environment, copy.User));
        Assert.Equal(fb.Password, vm.ProfileStore.GetPassword(copy.Id));
        Assert.Equal("Registrada como «docker-sysdba · ui_mant_rest» (Prueba). Se edita desde el árbol, como cualquier otra.", tab.Status);

        w.Close();
        await vm.DisposeAsync();
        await DropAsync(Restored);
    }, CancellationToken.None);

    [Fact]
    public Task Base_vacia_con_las_tablas_elegidas_validacion_y_sweep_con_sus_confirmaciones() => ui.Session.DispatchAsync(async () =>
    {
        await ExecAsync("CREATE TABLE UI_MANT_CHICA (ID INTEGER NOT NULL PRIMARY KEY, N VARCHAR(10))");
        await ExecAsync("INSERT INTO UI_MANT_CHICA SELECT 1, 'a' FROM RDB$DATABASE UNION ALL SELECT 2, 'b' FROM RDB$DATABASE UNION ALL SELECT 3, 'c' FROM RDB$DATABASE");
        const string Empty = "/firebird/data/ui_mant_vacia.fdb";
        try
        {
            var readOnly = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-lectura", ReadOnly = true };
            var (vm, w) = Open(Sysdba, readOnly);
            var tab = vm.OpenMaintenance(Sysdba, MaintenanceTask.EmptyCopy)!;
            await tab.LoadAsync();
            for (var i = 0; i < 200 && (tab.TablesLoading || tab.AllTables.Count == 0); i++)
            {
                await Task.Delay(50);
                Pump();
            }
            Assert.Contains(tab.AllTables, t => t.Name == "TURNOS");
            Assert.Equal("Ninguna: solo la estructura", tab.ChosenText);
            tab.TableFilter = "mant";
            var chica = Assert.Single(tab.VisibleTables);
            chica.IsChecked = true;
            Assert.Equal("1 elegida(s); se agregan solas las que referencian sus FK", tab.ChosenText);

            tab.EmptyTarget = Empty;
            await tab.RunAsync();
            Pump();
            Assert.StartsWith($"Base vacía creada en {Empty}: 1 tabla(s) copiadas con 3 filas", tab.Status);
            tab.FlushLog();
            Assert.Contains("UI_MANT_CHICA: 3 filas copiadas", tab.Document.Text);
            Assert.True(tab.CanRegisterNew);
            await using (var c = new FbConnection(FbConnectionFactory.ConnectionString(Sysdba with { Database = Empty }, fb.Password)))
            {
                await c.OpenAsync();
                await using var tx = await c.BeginTransactionAsync();
                Assert.Equal(3, Convert.ToInt32(await new FbCommand("SELECT COUNT(*) FROM UI_MANT_CHICA", c, tx).ExecuteScalarAsync()));
                Assert.Equal(0, Convert.ToInt32(await new FbCommand("SELECT COUNT(*) FROM TURNOS", c, tx).ExecuteScalarAsync()));
            }

            // Validación: en producción avisa que bloquea cada tabla un instante.
            var prompts = new List<ConfirmPrompt>();
            vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(true); };
            tab.SelectedTaskIndex = (int)MaintenanceTask.Validate;
            await tab.RunAsync();
            Assert.Contains("PRODUCCIÓN", Assert.Single(prompts).Reasons[0]);
            Assert.StartsWith("Validación terminada sin errores", tab.Status);
            tab.FlushLog();                                                     // la salida llega de otro hilo, de a tandas
            Assert.Contains("Relation", tab.Document.Text);

            // Sweep en producción: hay que escribir el nombre de la base.
            var typed = new List<TypedConfirm>();
            var answer = false;
            vm.ConfirmTypedAsync = p => { typed.Add(p); return Task.FromResult(answer); };
            tab.SelectedTaskIndex = (int)MaintenanceTask.Sweep;
            tab.Status = "";
            await tab.RunAsync();
            Assert.Equal(("docker-sysdba", ""), (Assert.Single(typed).Expected, tab.Status));
            answer = true;
            await tab.RunAsync();
            Assert.StartsWith("Sweep terminado", tab.Status);

            // Con un registro de solo lectura: backup y validación sí; restore, base vacía y sweep no.
            var ro = vm.OpenMaintenance(vm.Profiles.Single(p => p.Id == readOnly.Id))!;
            Assert.NotSame(tab, ro);
            foreach (var (task, allowed) in new[] { (MaintenanceTask.Backup, true), (MaintenanceTask.Restore, false), (MaintenanceTask.EmptyCopy, false),
                         (MaintenanceTask.Validate, true), (MaintenanceTask.Sweep, false) })
            {
                ro.SelectedTaskIndex = (int)task;
                Assert.Equal((task, allowed, !allowed), (task, ro.RunCommand.CanExecute(null), ro.IsBlockedReadOnly));
            }

            // Mientras trabaja, la base no se desconecta (se cortaría la base vacía a medio armar).
            Assert.False(tab.IsRunning);
            w.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            await DropAsync(Empty);
            await ExecAsync("DROP TABLE UI_MANT_CHICA");
        }
    }, CancellationToken.None);

    [Fact]
    public void Carpeta_y_nombre_de_una_ruta_del_servidor()
    {
        Assert.Equal(("D:\\Bases\\", "SUCURSAL"), MaintenanceTabViewModel.Split("D:\\Bases\\SUCURSAL.FDB"));
        Assert.Equal(("/firebird/data/", "test"), MaintenanceTabViewModel.Split("/firebird/data/test"));
        Assert.Equal(("", "sucursal"), MaintenanceTabViewModel.Split("sucursal"));
        Assert.True(MaintenanceTabViewModel.IsDatabaseFile(@"D:\Bases\SUCURSAL.FDB"));     // un backup no puede pisar una base
        Assert.True(MaintenanceTabViewModel.IsDatabaseFile("/data/vieja.gdb "));
        Assert.False(MaintenanceTabViewModel.IsDatabaseFile(@"D:\Backups\SUCURSAL.fbk"));
        Assert.True(TypedConfirmDialog.Matches(" Docker-SYSDBA ", "docker-sysdba"));
        Assert.False(TypedConfirmDialog.Matches("docker", "docker-sysdba"));
    }

    private async Task ExecAsync(string sql)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await new FbCommand(sql, c, tx).ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private async Task DropAsync(string database)
    {
        try { await FbConnection.DropDatabaseAsync(FbConnectionFactory.ConnectionString(Sysdba with { Database = database }, fb.Password)); }
        catch (FbException) { }
    }

    [Fact]
    public Task Opciones_para_rescatar_saltear_tablas_y_validar_solo_algunas() => ui.Session.DispatchAsync(async () =>
    {
        var dev = Sysdba with { Environment = ProfileEnvironment.Desarrollo };
        var (vm, w) = Open(dev);
        var tab = vm.OpenMaintenance(dev, MaintenanceTask.Backup)!;
        await tab.LoadAsync();
        var id = Guid.NewGuid().ToString("N")[..8];

        // Backup salteando los datos de TURNOS (escrito en minúsculas), y las opciones quedan anotadas.
        tab.BackupFile = $"/firebird/data/ui_opt_{id}.fbk";
        tab.BackupSkipData = "turnos";
        tab.BackupNonTransportable = true;
        await tab.RunAsync();
        tab.FlushLog();
        Assert.StartsWith("Backup terminado", tab.Status);
        Assert.Contains("Opciones: Formato no transportable", tab.Document.Text);
        Assert.Contains("Sin los datos de estas tablas (la estructura sí): TURNOS", tab.Document.Text);

        // Restore sin las reglas NOT NULL y tabla por tabla: TURNOS vuelve vacía; TURNOS_DET con su estructura.
        tab.SelectedTaskIndex = (int)MaintenanceTask.Restore;
        tab.RestoreTarget = $"/firebird/data/ui_opt_{id}.fdb";
        tab.RestoreNoValidity = true;
        tab.RestoreOneAtATime = true;
        await tab.RunAsync();
        Assert.StartsWith("Restore terminado", tab.Status);
        await using (var c = FbConnectionFactory.Create(dev with { Database = tab.RestoreTarget }, fb.Password, "test"))
        {
            await c.OpenAsync();
            await using var tx = await c.BeginTransactionAsync();
            await using var cmd = new FbCommand("SELECT COUNT(*) FROM TURNOS", c, tx);
            Assert.Equal(0, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
        }

        // Validación de una sola tabla.
        tab.SelectedTaskIndex = (int)MaintenanceTask.Validate;
        tab.ValidateTables = "turnos";
        await tab.RunAsync();
        tab.FlushLog();
        Assert.Contains("Validación de docker-sysdba, solo TURNOS", tab.Document.Text);
        Assert.Contains("(TURNOS)", tab.Document.Text);
        Assert.DoesNotContain("(TURNOS_DET)", tab.Document.Text);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Propiedades_de_la_base_con_sus_confirmaciones() => ui.Session.DispatchAsync(async () =>
    {
        // Una base propia (de producción, para ver las confirmaciones): las de las otras pruebas no se tocan.
        var db = Sysdba with { Id = Guid.NewGuid(), Database = $"/firebird/data/ui_prop_{Guid.NewGuid():N}.fdb" };
        await FbConnection.CreateDatabaseAsync(FbConnectionFactory.ConnectionString(db, fb.Password), 8192, true, true);
        var readOnlyProfile = db with { Id = Guid.NewGuid(), Name = "docker-ro", ReadOnly = true };
        var (vm, w) = Open(db, readOnlyProfile);
        var prompts = new List<ConfirmPrompt>();
        var typed = new List<TypedConfirm>();
        vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(true); };
        vm.ConfirmTypedAsync = p => { typed.Add(p); return Task.FromResult(true); };
        var tab = vm.OpenMaintenance(vm.Profiles.Single(p => p.Id == db.Id), MaintenanceTask.Properties)!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!tab.PropertiesLoaded && sw.ElapsedMilliseconds < 10_000) { await Task.Delay(20); Pump(); }
        await tab.Working;
        Assert.Equal((20_000m, 0m, true, false), (tab.PropSweepInterval, tab.PropPageBuffers, tab.PropForcedWrites, tab.PropReadOnly));
        Assert.Equal("Aplicar cambios", tab.RunText);

        await tab.RunAsync();
        Assert.Equal("No cambió nada.", tab.Status);

        // Cambios comunes: pregunta con cada cambio.
        tab.PropSweepInterval = 0;
        tab.PropPageBuffers = 1024;
        await tab.RunAsync();
        Assert.Contains("Sweep automático: 20.000 → apagado", prompts[^1].Reasons);
        Assert.Empty(typed);
        Assert.Equal((0m, 1024m), (tab.PropSweepInterval, tab.PropPageBuffers));
        Assert.Equal($"Propiedades aplicadas en {db.Name}.", tab.Status);

        // Apagar las escrituras forzadas en producción: hay que escribir el nombre.
        tab.PropForcedWrites = false;
        Assert.True(tab.IsForcedWritesOff);
        await tab.RunAsync();
        Assert.Equal(db.Name, typed.Single().Expected);
        Assert.False(tab.PropForcedWrites);

        // Solo lectura con otra conexión abierta: Firebird no deja y se dice por qué.
        await using (var other = FbConnectionFactory.Create(db, fb.Password, "otro"))
        {
            await other.OpenAsync();
            tab.PropReadOnly = true;
            await tab.RunAsync();
            tab.FlushLog();
            Assert.StartsWith("Error:", tab.Status);
            Assert.Contains("no tiene que haber nadie más conectado", tab.Document.Text);
        }
        tab.PropReadOnly = true;
        await tab.RunAsync();
        Assert.True(tab.PropReadOnly);
        tab.PropReadOnly = false;
        tab.PropForcedWrites = true;
        await tab.RunAsync();
        Assert.Equal((false, true), (tab.PropReadOnly, tab.PropForcedWrites));

        // Con un registro de solo lectura no se ofrece.
        var ro = vm.OpenMaintenance(vm.Profiles.Single(p => p.Id == readOnlyProfile.Id), MaintenanceTask.Properties)!;
        Assert.True(ro.IsBlockedReadOnly);
        Assert.False(ro.RunCommand.CanExecute(null));
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temporal */ }
    }
}
