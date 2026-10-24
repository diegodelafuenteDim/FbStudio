using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using FbStudio.App.ResultGrid;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;
using FbStudio.Firebird.Tests;
using FbStudio.Workspace;

namespace FbStudio.App.Tests;

/// <summary>Reordenar columnas (F-13): es solo la vista; la pestaña Datos lo recuerda por base y tabla.</summary>
[Collection(nameof(UiCollection))]
public sealed class ColumnOrderTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-cols-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static ColumnInfo[] Cols() => [new(0, "ID", "INTEGER"), new(1, "NOMBRE", "VARCHAR"), new(2, "IMPORTE", "NUMERIC")];

    private static AvaloniaList<object[]> Data() => [[3, "c", 30m], [1, "a", 10m], [2, "b", 20m]];

    [Fact]
    public Task Mover_una_columna_cambia_solo_la_vista() => ui.Session.DispatchAsync(async () =>
    {
        var grid = new DrawnGrid();
        var w = new Window { Content = grid, Width = 900, Height = 500 };
        w.Show();
        grid.SetData(Cols(), Data());
        Pump();
        var events = 0;
        grid.ColumnLayoutChanged += () => events++;

        grid.MoveColumn(2, 0);                                                 // IMPORTE adelante de todo
        Assert.Equal([2, 0, 1], grid.ColumnOrder);
        Assert.Equal(["IMPORTE", "ID", "NOMBRE"], grid.DisplayColumns.Select(c => c.Name));
        Assert.Equal(1, events);
        grid.MoveColumn(2, 0);                                                 // ya está ahí: nada
        grid.MoveColumn(2, 1);                                                 // «antes de la que está a su derecha»: tampoco
        Assert.Equal(1, events);
        grid.MoveColumn(0, 3);                                                 // ID al final
        Assert.Equal([2, 1, 0], grid.ColumnOrder);

        // Copiar sigue lo que se ve; ordenar y filtrar siguen siendo de su columna.
        Assert.StartsWith("IMPORTE\tNOMBRE\tID", grid.ClipboardText([1]));
        Assert.Contains("a\t1", grid.ClipboardText([1]).Replace(",00", "").Replace(".00", ""));
        grid.SortBy(0, SortDirection.Ascending);
        await grid.PendingBuild;
        Assert.Equal([1, 2, 0], grid.VisibleRowIndexes());                     // por ID, aunque ID esté al final
        grid.SetFilter(1, "b");
        await grid.PendingBuild;
        Assert.Equal([2], grid.VisibleRowIndexes());
        grid.SetFilter(1, null);
        await grid.PendingBuild;

        // Izquierda y derecha van por lo que se ve: de NOMBRE (al medio) a la derecha es ID.
        grid.SelectCell(0, 1);
        w.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Pump();
        Assert.Equal(1, Convert.ToInt32(grid.CurrentRow![0]));
        w.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
        Pump();

        // El mismo resultado vuelto a leer (monitoreo) mantiene el orden; uno distinto vuelve al de la consulta.
        grid.SetData(Cols(), Data(), keepView: true);
        Assert.Equal([2, 1, 0], grid.ColumnOrder);
        grid.SetData([new(0, "OTRA", "INTEGER"), new(1, "MAS", "INTEGER")], [[1, 2]]);
        Assert.Equal([0, 1], grid.ColumnOrder);

        // Lo guardado va por nombre: lo que ya no existe se ignora y lo nuevo va al final.
        grid.SetData(Cols(), Data());
        grid.ApplyColumnLayout(new GridColumnLayout(["NOMBRE", "BORRADA", "ID"], new() { ["NOMBRE"] = 333, ["BORRADA"] = 50 }));
        Assert.Equal([1, 0, 2], grid.ColumnOrder);
        Assert.Equal(333, grid.ColumnWidth(1));
        var layout = grid.ColumnLayout();
        Assert.Equal(["NOMBRE", "ID", "IMPORTE"], layout.Order);
        Assert.Equal(333, layout.Widths["NOMBRE"]);
        grid.ResetColumnLayout();
        Assert.Equal([0, 1, 2], grid.ColumnOrder);
        Assert.NotEqual(333, grid.ColumnWidth(1));
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task La_pestana_Datos_recuerda_orden_y_anchos_por_tabla() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        async Task<(MainWindow W, MainWindowViewModel Vm, DrawnGrid Grid)> OpenTurnos()
        {
            var vm = new MainWindowViewModel(store, gridLayouts: new GridLayoutStore(_folder));
            var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            w.Show();
            vm.NewQueryTab(vm.Profiles.Single());
            Pump();
            var tab = (await vm.OpenObjectAsync(fb.Profile, new DbObject(DbObjectKind.Table, "TURNOS")))!;
            tab.SelectedResultTab = EditorTabViewModel.DataTab;
            for (var i = 0; i < 500 && !(tab.Data!.IsLoaded && !tab.Data.IsBusy); i++)
            {
                await Task.Delay(20);
                Pump();
            }
            Pump();
            return (w, vm, w.CurrentView.FindControl<DrawnGrid>("DataGrid")!);
        }

        var (w1, vm1, grid1) = await OpenTurnos();
        Assert.Equal([0, 1, 2, 3, 4], grid1.ColumnOrder);
        grid1.MoveColumn(4, 0);                                                // IMPORTE adelante
        var widths = Enumerable.Range(0, 5).Select(grid1.ColumnWidth).ToList();
        Assert.True(File.Exists(Path.Combine(_folder, "grid-layouts.json")));
        w1.Close();
        await vm1.DisposeAsync();

        var (w2, vm2, grid2) = await OpenTurnos();                               // otra sesión de FbStudio
        Assert.Equal([4, 0, 1, 2, 3], grid2.ColumnOrder);
        Assert.Equal(widths, Enumerable.Range(0, 5).Select(grid2.ColumnWidth));
        grid2.ResetColumnLayout();
        w2.Close();
        await vm2.DisposeAsync();

        var (w3, vm3, grid3) = await OpenTurnos();
        Assert.Equal([0, 1, 2, 3, 4], grid3.ColumnOrder);
        w3.Close();
        await vm3.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public void Un_archivo_roto_no_impide_trabajar_y_se_guardan_las_ultimas_500()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "grid-layouts.json"), "{ esto no es json");
        var store = new GridLayoutStore(_folder);
        var id = Guid.NewGuid();
        Assert.Null(store.Get(id, "T"));
        for (var i = 0; i < 505; i++) store.Save(id, "T" + i, new GridColumnLayout(["A"], new() { ["A"] = i }));
        var again = new GridLayoutStore(_folder);
        Assert.Null(again.Get(id, "T0"));                                      // las más viejas se van
        Assert.Equal(504, again.Get(id, "T504")!.Widths["A"]);
        again.Remove(id, "T504");
        Assert.Null(new GridLayoutStore(_folder).Get(id, "T504"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temporal */ }
    }
}
