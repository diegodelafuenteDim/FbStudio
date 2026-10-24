using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ResultGrid;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>«Copiar como INSERT / UPDATE» desde la grilla, en Resultados y en la pestaña Datos.</summary>
[Collection(nameof(UiCollection))]
public sealed class CopyAsScriptTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-copyas-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private (MainWindow W, MainWindowViewModel Vm) Open()
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Pump();
        return (w, vm);
    }

    private static string[] Lines(string? text) => (text ?? "").Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public Task En_Resultados_toma_la_tabla_del_FROM_y_su_clave() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.NewQueryTab(vm.Profiles.Single(), "SELECT ID, PACIENTE, IMPORTE, IMPORTE * 2 AS DOBLE FROM turnos WHERE ID IN (1, 2) ORDER BY ID");
        await w.RunAsync();
        Pump();
        var grid = w.CurrentView.FindControl<DrawnGrid>("Results")!;
        Assert.NotNull(grid.CopyAsScript);
        grid.MoveColumn(2, 0);                                                   // IMPORTE primero: se copia como se ve
        var (columns, rows) = grid.DisplayedRows([0, 1]);

        var (insert, message) = await tab.RowScriptAsync(columns, rows, update: false, fromData: false);
        Assert.Equal("INSERT INTO TURNOS (IMPORTE, ID, PACIENTE) VALUES (0.01, 1, 'PACIENTE 1');", Lines(insert)[0]);
        Assert.Equal("2 fila(s) copiadas como INSERT (TURNOS). Sin DOBLE: no son columnas de la tabla o son calculadas.", message);

        var (update, _) = await tab.RowScriptAsync(columns, rows, update: true, fromData: false);
        Assert.Equal("UPDATE TURNOS SET IMPORTE = 0.02, PACIENTE = 'PACIENTE 2' WHERE ID = 2;", Lines(update)[1]);

        // Desde el menú: copia y lo dice en la barra de estado.
        await grid.CopyAsScript!([0], true);
        Assert.StartsWith("1 fila(s) copiadas como UPDATE (TURNOS). Sin DOBLE", tab.Status);

        // Sin la clave en el SELECT, el UPDATE no se puede armar; el INSERT sí.
        tab.Document.Text = "SELECT PACIENTE FROM TURNOS WHERE ID = 3";
        await w.RunAsync();
        (columns, rows) = w.CurrentView.FindControl<DrawnGrid>("Results")!.DisplayedRows([0]);
        var (none, why) = await tab.RowScriptAsync(columns, rows, update: true, fromData: false);
        Assert.Null(none);
        Assert.Equal("El resultado no trae toda la clave de TURNOS (falta ID): agregala al SELECT para copiar como UPDATE.", why);
        Assert.Equal("INSERT INTO TURNOS (PACIENTE) VALUES ('PACIENTE 3');", Lines((await tab.RowScriptAsync(columns, rows, false, false)).Text)[0]);

        // Una consulta sin tabla reconocible: el INSERT va a «TABLA» para cambiar al pegar.
        tab.Document.Text = "SELECT 1 AS X FROM (SELECT 1 AS Y FROM RDB$DATABASE) D";
        await w.RunAsync();
        (columns, rows) = w.CurrentView.FindControl<DrawnGrid>("Results")!.DisplayedRows([0]);
        var (guessed, note) = await tab.RowScriptAsync(columns, rows, update: false, fromData: false);
        Assert.Equal("INSERT INTO TABLA (X) VALUES (1);", Lines(guessed)[0]);
        Assert.EndsWith("No se reconoció la tabla de la consulta: cambiá «TABLA» al pegar.", note);
        Assert.Null((await tab.RowScriptAsync(columns, rows, update: true, fromData: false)).Text);
        w.Close();
        await vm.DisposeAsync();                                                 // suelta las conexiones
    }, CancellationToken.None);

    [Fact]
    public Task En_Datos_usa_la_clave_de_la_tabla_y_los_cambios_sin_aplicar() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        vm.NewQueryTab(vm.Profiles.Single());
        Pump();
        var tab = (await vm.OpenObjectAsync(fb.Profile, new DbObject(DbObjectKind.Table, "TURNOS_DET")))!;
        tab.SelectedResultTab = EditorTabViewModel.DataTab;
        for (var i = 0; i < 300 && !(tab.Data!.IsLoaded && !tab.Data.IsBusy); i++) { await Task.Delay(20); Pump(); }
        var grid = w.CurrentView.FindControl<DrawnGrid>("DataGrid")!;
        Assert.NotNull(grid.CopyAsScript);

        // TURNOS_DET está vacía: una fila nueva sin aplicar (la columna calculada TOTAL no se escribe).
        Assert.True(grid.AddRowAndEdit());
        Assert.True(grid.EditSelectedCell("77"));                                // ID
        var (columns, rows) = grid.DisplayedRows([0]);
        var (insert, message) = await tab.RowScriptAsync(columns, rows, update: false, fromData: true);
        Assert.Equal("INSERT INTO TURNOS_DET (ID, TURNO, CANTIDAD, NOTA) VALUES (77, NULL, NULL, NULL);", Lines(insert)[0]);
        Assert.Contains("Sin TOTAL", message);
        var (update, _) = await tab.RowScriptAsync(columns, rows, update: true, fromData: true);
        Assert.Equal("UPDATE TURNOS_DET SET TURNO = NULL, CANTIDAD = NULL, NOTA = NULL WHERE ID = 77;", Lines(update)[0]);
        tab.Data!.DiscardCommand.Execute(null);                                  // nada se aplica
        w.Close();
        await vm.DisposeAsync();                                                 // suelta las conexiones
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temporal */ }
    }
}
