using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ResultGrid;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>La ventanita de filtro según el tipo de la columna (F-12), en Resultados y en la pestaña Datos.</summary>
[Collection(nameof(UiCollection))]
public sealed class FilterPopupTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-filter-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static ColumnInfo[] Cols() =>
        [new(0, "ID", "INTEGER"), new(1, "OBRA", "VARCHAR"), new(2, "IMPORTE", "NUMERIC"), new(3, "FECHA", "TIMESTAMP"), new(4, "ACTIVO", "BOOLEAN")];

    private static AvaloniaList<object[]> Data() =>
    [
        [1, "CAJA", 10m, new DateTime(2026, 7, 1, 8, 0, 0), true],
        [2, "PCIA", 20m, new DateTime(2026, 7, 2, 9, 0, 0), false],
        [3, "CAJA  ", 30m, DateTime.Today.AddHours(10), true],
        [4, DBNull.Value, 40m, new DateTime(2026, 6, 30, 23, 0, 0), DBNull.Value],
        [5, "ART", 15m, new DateTime(2026, 7, 5, 18, 0, 0), false],
    ];

    [Fact]
    public Task En_Resultados_ofrece_lo_que_corresponde_a_cada_tipo() => ui.Session.DispatchAsync(async () =>
    {
        var grid = new DrawnGrid();
        var w = new Window { Content = grid, Width = 1200, Height = 600 };
        w.Show();
        grid.SetData(Cols(), Data());
        Pump();

        // Texto: los valores de lo cargado (sin el relleno de los CHAR), para tildar.
        grid.OpenFilterPopup(1);
        Pump();
        var popup = grid.CurrentFilterPopup!;
        Assert.Equal(FilterOperator.Contains, popup.SelectedOperator);
        await popup.LoadValuesAsync();
        Assert.Equal([(null, 1L), ("ART", 1L), ("CAJA", 2L), ("PCIA", 1L)], popup.Values.OrderBy(v => v.Value as string).Select(v => (v.Value, v.Count)));
        popup.Check("CAJA");
        popup.Check(null);
        Assert.Equal("=CAJA;null", popup.FilterText);
        popup.Apply();
        await grid.PendingBuild;
        Assert.Null(grid.CurrentFilterPopup);                                   // se cierra al aplicar
        Assert.Equal("=CAJA;null", grid.Spec.Filters[1].Text);                    // queda escrito en la fila de filtros
        Assert.Equal([0, 2, 3], grid.VisibleRowIndexes());

        // Al volver a abrirla, lo tildado sigue tildado; los valores de otra columna respetan este filtro.
        grid.OpenFilterPopup(1);
        await grid.CurrentFilterPopup!.LoadValuesAsync();
        Assert.Equal("=CAJA;null", grid.CurrentFilterPopup.FilterText);
        grid.CurrentFilterPopup.Cancelled += () => { };
        grid.OpenFilterPopup(2);                                                 // abrir otra cierra la anterior
        Pump();
        popup = grid.CurrentFilterPopup!;
        await popup.LoadValuesAsync();
        Assert.Equal([10m, 30m, 40m], popup.Values.Select(v => v.Value).Order());

        // Números: entre.
        popup.SelectOperator(FilterOperator.Between);
        popup.Value1.Text = "10";
        popup.Value2.Text = "30";
        Assert.Equal("10..30", popup.FilterText);
        popup.Apply();
        await grid.PendingBuild;
        Assert.Equal([0, 2], grid.VisibleRowIndexes());
        grid.SetFilter(1, null);
        await grid.PendingBuild;

        // Lo ya escrito abre con su operador: «>=15» es «mayor o igual» con 15.
        grid.SetFilter(2, ">=15");
        await grid.PendingBuild;
        grid.OpenFilterPopup(2);
        popup = grid.CurrentFilterPopup!;
        Assert.Equal((FilterOperator.GreaterOrEqual, "15"), (popup.SelectedOperator, popup.Value1.Text));
        popup.Applied += _ => { };
        grid.CurrentFilterPopup!.Apply();                                        // igual que estaba
        await grid.PendingBuild;
        Assert.Equal(">=15", grid.Spec.Filters[2].Text);
        grid.OpenFilterPopup(2);
        grid.CurrentFilterPopup!.SelectOperator(FilterOperator.IsNull);
        Assert.Equal("null", grid.CurrentFilterPopup.FilterText);
        grid.CurrentFilterPopup.Value1.Text = "";
        // «Quitar filtro».
        var clear = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(grid.CurrentFilterPopup).OfType<Button>()
            .First(b => b.Content as string == "Quitar filtro");
        clear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await grid.PendingBuild;
        Assert.False(grid.Spec.Filters.ContainsKey(2));

        // Fechas: un período («hoy») o un día.
        grid.OpenFilterPopup(3);
        popup = grid.CurrentFilterPopup!;
        Assert.Equal(FilterOperator.Day, popup.SelectedOperator);
        await popup.LoadValuesAsync();                                           // por día, no por minuto
        Assert.Equal(5, popup.Values.Count);
        Assert.All(popup.Values, v => Assert.Equal(TimeSpan.Zero, ((DateTime)v.Value!).TimeOfDay));
        popup.Check(new DateTime(2026, 7, 1));
        Assert.Equal("=2026-07-01", popup.FilterText);
        popup.Check(new DateTime(2026, 7, 1), false);
        popup.SelectOperator(FilterOperator.Relative);
        popup.PeriodBox.SelectedIndex = 0;
        Assert.Equal("hoy", popup.FilterText);
        popup.Apply();
        await grid.PendingBuild;
        Assert.Equal([2], grid.VisibleRowIndexes());
        grid.OpenFilterPopup(3);
        popup = grid.CurrentFilterPopup!;
        Assert.Equal(FilterOperator.Relative, popup.SelectedOperator);           // vuelve a abrir en «período: hoy»
        popup.SelectOperator(FilterOperator.LessOrEqual);
        popup.Value1.Text = "2026-07-01";
        Assert.Equal("<=2026-07-01", popup.FilterText);
        popup.Apply();
        await grid.PendingBuild;
        Assert.Equal([0, 3], grid.VisibleRowIndexes());                          // «hasta el 1» incluye todo el 1
        grid.SetFilter(3, null);

        // BOOLEAN: sí / no.
        grid.OpenFilterPopup(4);
        popup = grid.CurrentFilterPopup!;
        Assert.Equal([FilterOperator.Yes, FilterOperator.No, FilterOperator.IsNull, FilterOperator.IsNotNull], popup.Operators);
        popup.SelectOperator(FilterOperator.No);
        popup.Apply();
        await grid.PendingBuild;
        Assert.Equal([1, 4], grid.VisibleRowIndexes());
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task El_panel_de_filtros_lista_las_columnas_y_escribe_el_mismo_filtro() => ui.Session.DispatchAsync(async () =>
    {
        var grid = new DrawnGrid();
        var w = new Window { Content = grid, Width = 1200, Height = 600 };
        w.Show();
        grid.SetData(Cols(), Data());
        Pump();
        Assert.Null(grid.FilterPanel);                                          // se arma recién al abrirlo
        grid.IsFilterPanelOpen = true;
        Pump();
        var panel = grid.FilterPanel!;
        Assert.Equal([0, 1, 2, 3, 4], panel.ShownColumns);

        // Escribir en el cuadro de una columna y Enter: el mismo filtro que en la fila de filtros.
        var box = panel.BoxFor(2)!;
        box.Focus();
        box.Text = ">15";
        w.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
        await grid.PendingBuild;
        Pump();
        Assert.Equal(">15", grid.Spec.Filters[2].Text);
        Assert.Equal([1, 2, 3], grid.VisibleRowIndexes());

        // Lo que se pone en la grilla aparece en el panel; «Solo las que tienen filtro» y buscar la columna.
        grid.SetFilter(1, "=CAJA");
        await grid.PendingBuild;
        Pump();
        Assert.Equal("=CAJA", panel.BoxFor(1)!.Text);
        panel.OnlyFiltered = true;
        Assert.Equal([1, 2], panel.ShownColumns);
        panel.OnlyFiltered = false;
        panel.SearchText = "fe";
        Assert.Equal([3], panel.ShownColumns);                                  // FECHA

        // El ▾ de la columna abre su ventanita, al lado del panel.
        grid.OpenFilterPopup(3, panel.BoxFor(3)!);
        Pump();
        Assert.NotNull(grid.CurrentFilterPopup);
        grid.CurrentFilterPopup!.Apply();
        await grid.PendingBuild;

        grid.ClearFilters();
        await grid.PendingBuild;
        Pump();
        panel.SearchText = "";
        Assert.All(panel.ShownColumns, c => Assert.Equal("", panel.BoxFor(c)!.Text));

        // Desde el encabezado («Filtrar en el panel de filtros»): lo abre con el foco en esa columna.
        grid.IsFilterPanelOpen = false;
        grid.ShowFilterPanelFor(4);
        Pump();
        Assert.True(grid.IsFilterPanelOpen);
        Assert.True(panel.BoxFor(4)!.IsFocused);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task En_Datos_los_valores_salen_del_servidor_a_pedido() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        vm.NewQueryTab(vm.Profiles.Single());
        Pump();
        var tab = (await vm.OpenObjectAsync(fb.Profile, new DbObject(DbObjectKind.Table, "TURNOS")))!;
        tab.SelectedResultTab = EditorTabViewModel.DataTab;
        async Task Idle()
        {
            for (var i = 0; i < 500 && !(tab.Data!.IsLoaded && !tab.Data.IsBusy); i++) { await Task.Delay(20); Pump(); }
        }
        await Idle();
        var grid = w.CurrentView.FindControl<DrawnGrid>("DataGrid")!;

        // Con otro filtro puesto, los valores que trae son solo los de esas filas.
        grid.SetFilter(0, "1;2;3");
        await Task.Delay(50);
        await Idle();
        Assert.Equal(3, tab.Data!.Rows.Count);
        grid.OpenFilterPopup(2);                                                 // PACIENTE
        Pump();
        var popup = grid.CurrentFilterPopup!;
        Assert.Empty(popup.Values);                                              // no recorre la tabla hasta que se pide
        await popup.LoadValuesAsync();
        Assert.Equal(["PACIENTE 1", "PACIENTE 2", "PACIENTE 3"], popup.Values.Select(v => (string)v.Value!).Order());
        popup.Check("PACIENTE 2");
        popup.Apply();
        await Task.Delay(50);
        await Idle();
        Assert.Equal([2], tab.Data.Rows.Select(r => Convert.ToInt32(r[0])));

        // Sin otro filtro: los 500 más repetidos (TURNOS tiene 50.000 pacientes distintos).
        grid.SetFilter(0, null);
        grid.SetFilter(2, null);
        await Task.Delay(50);
        await Idle();
        grid.OpenFilterPopup(2);
        await grid.CurrentFilterPopup!.LoadValuesAsync();
        Assert.Equal(FilterPopup.ValuesLimit, grid.CurrentFilterPopup.Values.Count);

        // OBS es VARCHAR(2000): se lista; un número abre con sus operadores.
        grid.OpenFilterPopup(4);
        Assert.Equal(FilterOperator.Equals, grid.CurrentFilterPopup!.SelectedOperator);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temporal */ }
    }
}
