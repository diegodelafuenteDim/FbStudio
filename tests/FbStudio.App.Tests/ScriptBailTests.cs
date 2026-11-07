using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>
/// Scripts con SET BAIL, órdenes de isql, el corte que deshace lo pendiente y el historial de una sola entrada
/// (D-120, D-121), en la ventana real contra Firebird 3 en Docker. Cada prueba usa sus propias filas de TURNOS (46xxx).
/// </summary>
[Collection(nameof(UiCollection))]
public sealed class ScriptBailTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-tests-" + Guid.NewGuid().ToString("N"));

    private (MainWindow Window, MainWindowViewModel Vm) Open(Func<ConnectionProfile, ConnectionProfile>? tweak = null)
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert((tweak ?? (p => p))(fb.Profile), fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
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

    /// <summary>OBS de una fila de TURNOS leída por otra conexión (lo confirmado).</summary>
    private async Task<object?> CommittedObsAsync(int id)
    {
        await using var other = fb.NewSession();
        var (o, rows) = await FirebirdFixture.RunAsync(other, $"SELECT OBS FROM TURNOS WHERE ID = {id}");
        Assert.Null(o.Error);
        return rows[0][0] is DBNull ? null : rows[0][0];
    }

    private async Task SetObsAsync(int id, string? obs)
    {
        await using var s = fb.NewSession();
        await FirebirdFixture.RunAsync(s, $"UPDATE TURNOS SET OBS = {(obs is null ? "NULL" : $"'{obs}'")} WHERE ID = {id}");
        await s.CommitAsync();
    }

    [Fact]
    public Task SET_BAIL_ON_corta_en_la_guarda_aunque_la_opcion_diga_seguir() => ui.Session.DispatchAsync(async () =>
    {
        foreach (var id in new[] { 46001, 46002, 46003 }) await SetObsAsync(id, null);
        var (w, vm) = Open();
        vm.ScriptContinueOnError = true;                          // como lo tenía Diego: la opción dice seguir
        var tab = vm.SelectedTab!;
        var before = vm.HistoryItems.Count;
        // Como un script de migración: guardas en un EXECUTE BLOCK con EXCEPTION, entre COMMIT, y SET BAIL al principio y al final.
        w.Editor.Text = """
            SET BAIL ON;
            UPDATE TURNOS SET OBS = 'bail-1' WHERE ID = 46001;
            COMMIT;
            UPDATE TURNOS SET OBS = 'bail-2' WHERE ID = 46002;
            SET TERM ^ ;
            EXECUTE BLOCK AS
              DECLARE c INTEGER;
            BEGIN
              SELECT COUNT(*) FROM TURNOS WHERE ID = 46001 AND OBS = 'bail-1' INTO :c;
              IF (c > 0) THEN
                EXCEPTION E_TURNO 'PARAR: el 46001 ya esta tomado';
            END^
            SET TERM ; ^
            UPDATE TURNOS SET OBS = 'bail-3' WHERE ID = 46003;
            COMMIT;
            SET BAIL OFF;
            """.Replace("\r\n", "\n");
        await w.RunAsync();

        Assert.StartsWith("Script: 4 de 8 sentencias", tab.Status);               // SET TERM no es una sentencia
        Assert.Contains("se detuvo por un error en la línea 11", tab.Status);     // la EXCEPTION, no el comienzo del bloque
        Assert.Contains("se deshizo lo pendiente del script", tab.Status);
        Assert.False(tab.InTransaction);
        Assert.Contains("ℹ línea 1 · SET BAIL ON: desde acá, el script se detiene en el primer error.", tab.Messages);
        Assert.Contains("✗ línea 6 · EXECUTE BLOCK", tab.Messages);
        Assert.Contains("Error en la línea 11, dentro del EXECUTE BLOCK que empieza en la línea 6:", tab.Messages);
        Assert.Contains("    EXECUTE BLOCK AS", tab.Messages);                    // el comienzo de la sentencia
        Assert.Contains("PARAR: el 46001 ya esta tomado", tab.Messages);
        Assert.Contains("Se deshizo lo que hizo el script después del COMMIT de la línea 3; lo anterior ya estaba confirmado.", tab.Messages);
        Assert.DoesNotContain("Token unknown", tab.Messages);
        Assert.DoesNotContain("línea 14", tab.Messages);                           // no siguió
        Assert.Equal(11, w.Editor.TextArea.Caret.Line);

        Assert.Equal("bail-1", await CommittedObsAsync(46001));                   // confirmado por su COMMIT
        Assert.Null(await CommittedObsAsync(46002));                               // deshecho
        Assert.Null(await CommittedObsAsync(46003));                               // nunca corrió

        // El historial guarda el script una vez, entero, como se ejecutó (D-121).
        Assert.Equal(before + 1, vm.HistoryItems.Count);
        Assert.Equal(("SCRIPT", w.Editor.Text), (vm.HistoryItems[0].Kind, vm.HistoryItems[0].Sql));
        Assert.True(vm.HistoryItems[0].Failed);

        vm.ScriptContinueOnError = false;
        await SetObsAsync(46001, null);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task SET_BAIL_OFF_sigue_aunque_la_opcion_diga_parar() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        Assert.True(vm.Options.ScriptStopOnError);                // de fábrica: se detiene
        var tab = vm.SelectedTab!;
        w.Editor.Text = "SET BAIL OFF;\nUPDATE TURNOS SET OBS = 'off-1' WHERE ID = 46011;\nINSERT INTO NO_EXISTE VALUES (1);\nUPDATE TURNOS SET OBS = 'off-2' WHERE ID = 46012;";
        await w.RunAsync();

        Assert.StartsWith("Script: 3 de 4 sentencias", tab.Status);
        Assert.Contains("1 con error (línea 3)", tab.Status);
        Assert.DoesNotContain("se detuvo", tab.Status);
        Assert.Contains("ℹ línea 1 · SET BAIL OFF: desde acá, el script sigue aunque haya errores.", tab.Messages);
        Assert.Contains("✓ línea 4", tab.Messages);
        Assert.True(tab.InTransaction);                           // lo que salió bien queda para Commit o Rollback
        Assert.Contains("Error en la sentencia de la línea 3 (INSERT):", tab.Messages);
        await tab.RollbackCommand.ExecuteAsync(null);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task En_produccion_se_detiene_aunque_la_opcion_diga_seguir() => ui.Session.DispatchAsync(async () =>
    {
        await SetObsAsync(46021, null);
        var (w, vm) = Open(p => p with { Environment = ProfileEnvironment.Produccion });
        var asked = new List<IReadOnlyList<string>>();
        vm.ConfirmAsync = d => { asked.Add(d.Reasons); return Task.FromResult(true); };
        vm.ScriptContinueOnError = true;
        var tab = vm.SelectedTab!;
        w.Editor.Text = "UPDATE TURNOS SET OBS = 'prod-1' WHERE ID = 46021;\nSELECT * FROM NO_EXISTE;\nUPDATE TURNOS SET OBS = 'prod-2' WHERE ID = 46022;";
        await w.RunAsync();

        Assert.Contains("se detuvo por un error en la línea 2", tab.Status);
        Assert.Contains("se deshizo lo pendiente del script", tab.Status);
        Assert.Contains("Se deshizo todo lo que hizo el script: no había confirmado nada.", tab.Messages);
        Assert.False(tab.InTransaction);
        Assert.Null(await CommittedObsAsync(46021));

        // Un SET BAIL OFF con sentencias después se avisa en la confirmación de producción.
        asked.Clear();
        vm.ConfirmAsync = d => { asked.Add(d.Reasons); return Task.FromResult(false); };
        w.Editor.Text = "SET BAIL OFF;\nUPDATE TURNOS SET OBS = 'x' WHERE ID = 46021;";
        await w.RunAsync();
        Assert.Contains("Línea 1: SET BAIL OFF: desde ahí el script sigue aunque haya errores.", Assert.Single(asked));
        vm.ScriptContinueOnError = false;
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task El_corte_no_toca_lo_que_la_pestana_tenia_pendiente_de_antes() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        w.Editor.Text = "UPDATE TURNOS SET OBS = 'mio' WHERE ID = 46031";
        await w.RunAsync();
        Assert.True(tab.InTransaction);

        w.Editor.Text = "UPDATE TURNOS SET OBS = 'del script' WHERE ID = 46032;\nSELECT * FROM NO_EXISTE;";
        await w.RunAsync();
        Assert.Contains("se detuvo por un error en la línea 2", tab.Status);
        Assert.Contains("lo que ya estaba pendiente antes de empezar sigue en la transacción abierta", tab.Messages);
        Assert.True(tab.InTransaction);

        w.Editor.Text = "SELECT ID, OBS FROM TURNOS WHERE ID IN (46031, 46032) ORDER BY ID";
        await w.RunAsync();
        Assert.Equal("mio", tab.Rows[0][1]);                     // lo de antes, en pie
        Assert.NotEqual("del script", tab.Rows[1][1]);           // lo del script, deshecho
        await tab.RollbackCommand.ExecuteAsync(null);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Las_ordenes_de_isql_se_ignoran_con_un_aviso_o_frenan_el_script() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        w.Editor.Text = "SET SQL DIALECT 3;\nSET NAMES UTF8;\nSET AUTODDL ON;\nSET LIST ON;\nSELECT 1 FROM RDB$DATABASE;";
        await w.RunAsync();
        Assert.StartsWith("Script: 5 de 5 sentencias", tab.Status);
        Assert.Contains("órdenes de isql sin ejecutar: 4", tab.Status);
        Assert.Contains("ℹ línea 1 · SET SQL DIALECT 3: no se ejecuta; FbStudio ya trabaja en dialecto 3.", tab.Messages);
        Assert.Contains("ℹ línea 2 · SET NAMES UTF8: no se ejecuta; ⚠ la conexión usa ISO8859_1", tab.Messages);
        Assert.Contains("ℹ línea 3 · SET AUTODDL ON: no se ejecuta; FbStudio no confirma el DDL solo", tab.Messages);
        Assert.Contains("✓ línea 5 · SELECT", tab.Messages);
        Assert.DoesNotContain("✗", tab.Messages);

        // Lo que no puede hacer frena el script antes de la primera sentencia; la contraseña del CONNECT no se muestra.
        w.Editor.Text = "UPDATE TURNOS SET OBS = 'no corre' WHERE ID = 46041;\nINPUT otro.sql;\nSHELL dir;\nCONNECT 'srv:x.fdb' USER 'SYSDBA' PASSWORD 'secreta';";
        await w.RunAsync();
        Assert.StartsWith("No se ejecutó ninguna sentencia: INPUT (línea 2)", tab.Status);
        Assert.Contains("Línea 3: SHELL: FbStudio no ejecuta comandos del sistema.", tab.Messages);
        Assert.Contains("Línea 4: CONNECT: el script corre en la base de la pestaña", tab.Messages);
        Assert.DoesNotContain("secreta", tab.Messages);
        Assert.False(tab.InTransaction);

        // Sola, una orden de isql no va al servidor.
        var history = vm.HistoryItems.Count;
        w.Editor.Text = "SET BAIL ON";
        await w.RunAsync();
        Assert.StartsWith("SET BAIL ON: orden de isql; vale dentro de un script", tab.Status);
        Assert.Equal(history, vm.HistoryItems.Count);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task EXIT_termina_el_script_y_el_autocommit_no_se_usa() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        tab.AutoCommit = true;                                    // Desarrollo
        w.Editor.Text = "UPDATE TURNOS SET OBS = 'exit-1' WHERE ID = 46051;\nEXIT;\nUPDATE TURNOS SET OBS = 'exit-2' WHERE ID = 46052;";
        await w.RunAsync();
        Assert.StartsWith("Script: 2 de 3 sentencias", tab.Status);
        Assert.Contains("terminó en el EXIT de la línea 2", tab.Status);
        Assert.Contains("los scripts no usan el autocommit", tab.Status);
        Assert.DoesNotContain("línea 3", tab.Messages);
        Assert.True(tab.InTransaction);                           // sin autocommit: queda para Commit o Rollback
        await tab.RollbackCommand.ExecuteAsync(null);
        tab.AutoCommit = false;
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task En_Mensajes_se_elige_una_parte_y_Copiar_copia_eso() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        w.Editor.Text = "SELECT 1 FROM RDB$DATABASE;\nSELECT 2 FROM RDB$DATABASE;";
        await w.RunAsync();
        Pump();
        var viewer = w.CurrentView.MessagesViewer;
        Assert.Equal(tab.Messages, viewer.Text);                  // el visor sigue a la pestaña
        Assert.StartsWith("FbStudio · registro del", w.CurrentView.LogToCopy(DateTime.Now));   // sin selección, todo

        var start = viewer.Text.IndexOf("línea 2", StringComparison.Ordinal);
        viewer.Select(start, "línea 2".Length);
        Assert.Equal("línea 2", w.CurrentView.LogToCopy(DateTime.Now));                       // con selección, eso
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task El_renglon_es_el_de_la_sentencia_no_el_del_comentario_de_arriba() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        w.Editor.Text = "/* encabezado\n   de dos renglones */\nSET BAIL ON;\n-- la guarda\nSELECT * FROM NO_EXISTE;";
        await w.RunAsync();
        Assert.Contains("ℹ línea 3 · SET BAIL ON", tab.Messages);
        Assert.Contains("✗ línea 5 · SELECT", tab.Messages);
        Assert.Contains("se detuvo por un error en la línea 5", tab.Status);
        Assert.Contains("Error en la sentencia de la línea 5 (SELECT):\r\n    SELECT * FROM NO_EXISTE", tab.Messages.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Equal(5, w.Editor.TextArea.Caret.Line);
        w.Close();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
