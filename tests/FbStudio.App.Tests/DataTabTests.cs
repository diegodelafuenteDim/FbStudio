using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.App.Tests;

/// <summary>Pestaña «Datos» (F-3) en la ventana real, contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class DataTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-data-" + Guid.NewGuid().ToString("N"));

    /// <summary>Lo que muestra «Aplicar» antes de ejecutar (por defecto se muestran los comandos) y qué se elige.</summary>
    private List<ScriptPreview> Previews { get; } = [];
    private ScriptPreviewChoice PreviewChoice { get; set; } = ScriptPreviewChoice.Run;
    private bool PreviewWithCommit { get; set; }

    private (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        vm.ConfirmAsync = _ => Task.FromResult(true); // los borrados piden confirmación: en la prueba se acepta
        vm.PreviewScriptAsync = p => { Previews.Add(p); return Task.FromResult(new ScriptPreviewResult(PreviewChoice, PreviewWithCommit)); };
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

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test-ui-datos");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand(sql, c, tx);
        var value = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();
        return value;
    }

    /// <summary>DROP TABLE reintentando unos segundos si la tabla sigue «en uso» (una conexión que se está cerrando).</summary>
    private Task DropTableAsync(string table) => DropAsync("TABLE", table);

    private async Task DropAsync(string kind, string name)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await ScalarAsync($"DROP {kind} {name}");
                return;
            }
            catch (FbException ex) when (attempt < 40 && ex.Message.Contains("in use", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(250);
            }
        }
    }

    private async Task<EditorTabViewModel> OpenDataAsync(MainWindowViewModel vm, string table, DbObjectKind kind = DbObjectKind.Table)
    {
        var tab = await vm.OpenObjectAsync(fb.Profile, new DbObject(kind, table));
        Assert.NotNull(tab);
        Assert.True(tab.HasDataTab);
        tab.SelectedResultTab = EditorTabViewModel.DataTab; // la tabla se lee recién ahora
        await WaitFor(() => tab.Data!.IsLoaded && !tab.Data.IsBusy);
        Pump();
        return tab;
    }

    [Fact]
    public Task Hojea_de_a_paginas_con_filtro_y_orden_en_el_servidor() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        try
        {
            var tab = await OpenDataAsync(vm, "TURNOS");
            var data = tab.Data!;
            var grid = w.CurrentView.FindControl<FbStudio.App.ResultGrid.DrawnGrid>("DataGrid")!;
            Assert.Equal(TableDataViewModel.PageSize, data.Rows.Count);
            Assert.True(data.HasMore);
            Assert.Equal(TableDataViewModel.PageSize, grid.DisplayCount);

            // Bajar hasta el final de lo cargado trae la página siguiente.
            await WaitFor(() =>
            {
                grid.Offset = new Avalonia.Vector(0, grid.Extent.Height);
                return data.Rows.Count == 2 * TableDataViewModel.PageSize;
            });
            Assert.Equal(TableDataViewModel.PageSize, Convert.ToInt32(data.Rows[TableDataViewModel.PageSize][0])); // sigue justo después

            // El filtro y el orden van al servidor: aparecen filas que no estaban cargadas.
            grid.SetFilter(2, "paciente 4998*");
            await WaitFor(() => !data.IsBusy && data.Rows.Count == 11);
            grid.SortBy(0, SortDirection.Descending);
            await WaitFor(() => !data.IsBusy && Convert.ToInt32(data.Rows[0][0]) == 49989);
            Assert.False(data.HasMore);
            Assert.Equal(SortDirection.Descending, grid.Spec.SortDirection);     // la flecha sigue en el encabezado

            await data.CountCommand.ExecuteAsync(null);
            Assert.Equal(11, data.TotalCount);

            // WHERE libre, combinado con el filtro de la grilla.
            data.ExtraWhere = "ID < 40000";
            await data.ReloadAsync();
            Assert.Equal([4998], data.Rows.Select(r => Convert.ToInt32(r[0])));
            Assert.True(data.CanEditTable);
        }
        finally
        {
            // Siempre se cierra: si quedara abierta (un Assert que falla), su conexión molesta a los otros tests.
            w.Close();
            await vm.DisposeAsync();
        }
    }, CancellationToken.None);

    [Fact]
    public Task Edita_y_borra_sin_bloquear_y_aplica_todo_junto() => ui.Session.DispatchAsync(async () =>
    {
        await ScalarAsync("RECREATE TABLE UI_DATOS (ID INTEGER NOT NULL PRIMARY KEY, NOMBRE VARCHAR(40), IMPORTE NUMERIC(12,2))");
        await ScalarAsync("EXECUTE BLOCK AS DECLARE I INTEGER = 1; BEGIN WHILE (I <= 5) DO BEGIN INSERT INTO UI_DATOS VALUES (:I, 'N' || :I, :I * 10); I = I + 1; END END");
        var (w, vm) = Open();
        try
        {
            var tab = await OpenDataAsync(vm, "UI_DATOS");
            var data = tab.Data!;
            var grid = w.CurrentView.FindControl<FbStudio.App.ResultGrid.DrawnGrid>("DataGrid")!;
            Assert.Equal(5, data.Rows.Count);

            // Escribir en una celda: queda pendiente, la base no cambia ni queda nada bloqueado.
            grid.SelectCell(0, 1);
            Assert.True(grid.EditSelectedCell("Ñandú"));
            grid.SelectCell(0, 2);
            Assert.False(grid.EditSelectedCell("12.345,6"));                       // los miles no se escriben: queda el editor con el error
            Assert.True(grid.IsEditingCell);
            Assert.Contains("12.345,6", data.Status);
            grid.SelectCell(1, 1);                                                // otra celda: el editor anterior se descarta
            Assert.True(grid.EditSelectedCell("dos"));
            Assert.False(grid.IsEditingCell);
            data.ToggleDelete([data.Rows[4]]);                                    // ID 5
            Assert.Equal(3, data.PendingCount);
            Assert.Equal("N1", await ScalarAsync("SELECT NOMBRE FROM UI_DATOS WHERE ID = 1"));
            Assert.Equal(0, Convert.ToInt32(await ScalarAsync("""
                SELECT COUNT(*) FROM MON$TRANSACTIONS t JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID
                WHERE a.MON$REMOTE_PROCESS = 'FbStudio (datos)'
                """)));

            // Reordenar no pierde los cambios (se identifican por la clave).
            grid.SortBy(0, SortDirection.Descending);
            await WaitFor(() => !data.IsBusy && Convert.ToInt32(data.Rows[0][0]) == 5);
            Assert.True(data.TryGetPending(data.Rows[4], 1, out var pending));     // ID 1, ahora al final
            Assert.Equal("Ñandú", pending);

            await data.ApplyAsync();
            // Antes de ejecutar se vieron los comandos, con el aviso del borrado.
            var preview = Assert.Single(Previews);
            Assert.Contains("DELETE FROM UI_DATOS WHERE ID = 5;", preview.Script);
            Assert.Contains("UPDATE UI_DATOS SET NOMBRE = 'Ñandú' WHERE ID = 1 AND NOMBRE IS NOT DISTINCT FROM 'N1';", preview.Script);
            Assert.Contains(preview.Warnings, w => w.Contains("borrar 1 fila"));
            Assert.Equal(0, data.PendingCount);
            Assert.Contains("2", data.Status);
            Assert.Equal("Ñandú", await ScalarAsync("SELECT NOMBRE FROM UI_DATOS WHERE ID = 1"));
            Assert.Equal("dos", await ScalarAsync("SELECT NOMBRE FROM UI_DATOS WHERE ID = 2"));   // se editó con el orden ascendente
            Assert.Equal(4, Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM UI_DATOS")));
            Assert.Equal(4, data.Rows.Count);                                      // la borrada se fue de la grilla
            Assert.Equal("Ñandú", data.Rows.Single(r => Convert.ToInt32(r[0]) == 1)[1]);

            // Lo aplicado queda en el historial, con los valores.
            Assert.Contains(vm.HistoryItems, h => h.Sql.Contains("UPDATE UI_DATOS SET NOMBRE = 'Ñandú' WHERE ID = 1 AND NOMBRE IS NOT DISTINCT FROM 'N1';"));

            // «Abrir en una pestaña de consulta»: el script pasa a una pestaña para correrlo a mano; acá no queda pendiente.
            PreviewChoice = ScriptPreviewChoice.OpenInTab;
            grid.SelectCell(0, 1);
            Assert.True(grid.EditSelectedCell("tres"));
            await data.ApplyAsync();
            Assert.Equal(0, data.PendingCount);
            Assert.Equal("Cambios en UI_DATOS", vm.SelectedTab!.BaseTitle);
            Assert.Contains("UPDATE UI_DATOS SET NOMBRE = 'tres'", w.Editor.Text);
            Assert.DoesNotContain("COMMIT", w.Editor.Text);                                // de fábrica, para confirmarlo a mano
            Assert.NotEqual("tres", await ScalarAsync($"SELECT NOMBRE FROM UI_DATOS WHERE ID = {data.Rows[0][0]}")); // no se ejecutó
            vm.SelectedTab = tab;

            // Con «Terminar con COMMIT», el script termina así, y se recuerda para la próxima.
            PreviewWithCommit = true;
            grid.SelectCell(0, 1);
            Assert.True(grid.EditSelectedCell("tres bis"));
            Assert.Equal("Aplicar (1)", data.ApplyText);
            await data.ApplyAsync();
            Assert.EndsWith("COMMIT;", w.Editor.Text.TrimEnd());
            Assert.True(vm.Options.DataScriptWithCommit);
            Assert.True(Previews[^1].WithCommit is false);                                 // esa vez la casilla arrancó sin marcar
            vm.SelectedTab = tab;
            PreviewWithCommit = false;

            // Con la opción «Ejecutarlos directamente» no se muestra nada antes…
            vm.SaveSettings(vm.Options with { PreviewDataChanges = false });
            Previews.Clear();
            grid.SelectCell(0, 1);
            Assert.True(grid.EditSelectedCell("cuatro"));
            PreviewChoice = ScriptPreviewChoice.Cancel;
            await data.ScriptAsync();                                                      // …salvo con «Generar script…»
            Assert.Single(Previews);
            Assert.True(Previews[0].WithCommit);                                           // la casilla, como quedó la última vez
            Assert.Equal(1, data.PendingCount);                                            // cancelado: sigue pendiente
            Previews.Clear();
            PreviewChoice = ScriptPreviewChoice.Run;
            await data.ApplyAsync();
            Assert.Empty(Previews);
            Assert.Equal("cuatro", await ScalarAsync($"SELECT NOMBRE FROM UI_DATOS WHERE ID = {data.Rows[0][0]}"));
        }
        finally
        {
            w.Close();
            await vm.DisposeAsync();              // suelta las conexiones antes de borrar la tabla
            await DropTableAsync("UI_DATOS");     // el test del árbol cuenta las tablas de la base
        }
    }, CancellationToken.None);

    [Fact]
    public Task Agrega_filas_nuevas_y_la_clave_la_pone_el_servidor() => ui.Session.DispatchAsync(async () =>
    {
        await ScalarAsync("RECREATE TABLE UI_ALTAS (ID INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, NOMBRE VARCHAR(40) NOT NULL, ALTA DATE DEFAULT '2026-01-02', IMPORTE NUMERIC(12,2))");
        await ScalarAsync("INSERT INTO UI_ALTAS (NOMBRE) VALUES ('existente')");
        var (w, vm) = Open();
        try
        {
            var tab = await OpenDataAsync(vm, "UI_ALTAS");
            var data = tab.Data!;
            var grid = w.CurrentView.FindControl<FbStudio.App.ResultGrid.DrawnGrid>("DataGrid")!;
            Assert.Single(data.Rows);

            // Insert: una fila vacía arriba de todo, con el editor abierto en la primera columna.
            Assert.True(grid.AddRowAndEdit());
            Assert.True(grid.IsEditingCell);
            Assert.Equal(2, data.Rows.Count);
            var created = data.Rows[0];
            Assert.True(data.IsNew(created));
            Assert.Contains("1 nueva(s)", data.PendingText);
            grid.SelectCell(0, 1);
            Assert.True(grid.EditSelectedCell("Alta desde FbStudio"));
            grid.SelectCell(0, 3);
            Assert.True(grid.EditSelectedCell("12,5"));

            // Otra fila nueva que se borra antes de aplicar: desaparece y no se inserta.
            var discarded = data.AddRow()!;
            Assert.Equal(3, data.Rows.Count);
            data.ToggleDelete([discarded]);
            Assert.Equal(2, data.Rows.Count);

            // Volver a leer (otro orden) no pierde la fila nueva: sigue arriba.
            grid.SortBy(1, SortDirection.Descending);
            await WaitFor(() => !data.IsBusy && data.Rows.Count == 2);
            Assert.Same(created, data.Rows[0]);

            Previews.Clear();
            await data.ApplyAsync();
            var preview = Assert.Single(Previews);
            Assert.Contains("INSERT INTO UI_ALTAS (NOMBRE, IMPORTE) VALUES ('Alta desde FbStudio', 12.5);", preview.Script);
            Assert.Equal(0, data.PendingCount);
            Assert.False(data.IsNew(created));
            Assert.Contains("1 nuevas", data.Status);
            // El RETURNING le dio a la fila la clave de la identity y el DEFAULT de la fecha.
            Assert.Equal(2, Convert.ToInt32(created[0]));
            Assert.Equal(new DateTime(2026, 1, 2), Convert.ToDateTime(created[2]));
            Assert.Equal("Alta desde FbStudio", await ScalarAsync("SELECT NOMBRE FROM UI_ALTAS WHERE ID = 2"));
            Assert.Equal(2, Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM UI_ALTAS")));

            // Ya es una fila como las demás: se edita por su clave.
            grid.SelectCell(data.Rows.IndexOf(created), 1);
            Assert.True(grid.EditSelectedCell("editada"));
            await data.ApplyAsync();
            Assert.Equal("editada", await ScalarAsync("SELECT NOMBRE FROM UI_ALTAS WHERE ID = 2"));

            // Una fila nueva sin el NOT NULL: el servidor la rechaza, no se guarda nada y sigue pendiente.
            data.AddRow();
            await data.ApplyAsync();
            Assert.Equal(1, data.PendingCount);
            Assert.Contains("la fila nueva", data.Status);
            Assert.Equal(2, Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM UI_ALTAS")));
        }
        finally
        {
            w.Close();
            await vm.DisposeAsync();
            await DropTableAsync("UI_ALTAS");
        }
    }, CancellationToken.None);

    [Fact]
    public Task Una_vista_de_una_sola_tabla_que_muestra_su_clave_se_edita() => ui.Session.DispatchAsync(async () =>
    {
        await ScalarAsync("RECREATE TABLE UI_VBASE (ID INTEGER NOT NULL PRIMARY KEY, NOMBRE VARCHAR(40), IMPORTE NUMERIC(12,2))");
        await ScalarAsync("INSERT INTO UI_VBASE VALUES (1, 'uno', 10)");
        await ScalarAsync("CREATE VIEW UI_V_EDIT AS SELECT ID, NOMBRE, IMPORTE, IMPORTE * 2 AS DOBLE FROM UI_VBASE");
        await ScalarAsync("CREATE VIEW UI_V_SINCLAVE AS SELECT NOMBRE, IMPORTE FROM UI_VBASE");
        await ScalarAsync("CREATE VIEW UI_V_JOIN AS SELECT b.ID, b.NOMBRE, t.ID AS TURNO FROM UI_VBASE b JOIN TURNOS t ON t.ID = b.ID");
        var (w, vm) = Open();
        try
        {
            var tab = await OpenDataAsync(vm, "UI_V_EDIT", DbObjectKind.View);
            var data = tab.Data!;
            var grid = w.CurrentView.FindControl<FbStudio.App.ResultGrid.DrawnGrid>("DataGrid")!;
            Assert.True(data.CanEditTable);
            Assert.Contains("vista sobre UI_VBASE", data.Status);
            Assert.True(data.CanEdit(1));                                      // NOMBRE sale de la tabla
            Assert.False(data.CanEdit(3));                                     // DOBLE es una expresión: no se escribe

            grid.SelectCell(0, 1);
            Assert.True(grid.EditSelectedCell("uno editado"));
            var created = data.AddRow()!;
            grid.SelectCell(data.Rows.IndexOf(created), 0);
            Assert.True(grid.EditSelectedCell("2"));
            grid.SelectCell(data.Rows.IndexOf(created), 1);
            Assert.True(grid.EditSelectedCell("dos"));
            grid.SelectCell(data.Rows.IndexOf(created), 2);
            Assert.True(grid.EditSelectedCell("7"));
            Previews.Clear();
            await data.ApplyAsync();
            var preview = Assert.Single(Previews);
            Assert.Contains("UPDATE UI_V_EDIT SET NOMBRE = 'uno editado' WHERE ID = 1", preview.Script);
            Assert.Contains("INSERT INTO UI_V_EDIT (ID, NOMBRE, IMPORTE) VALUES (2, 'dos', 7);", preview.Script);
            Assert.Equal(0, data.PendingCount);
            Assert.Equal("uno editado", await ScalarAsync("SELECT NOMBRE FROM UI_VBASE WHERE ID = 1"));  // llegó a la tabla
            Assert.Equal("dos", await ScalarAsync("SELECT NOMBRE FROM UI_VBASE WHERE ID = 2"));
            // En la vista el INSERT no devuelve la fila: se vuelve a leer y aparece la expresión calculada.
            await WaitFor(() => !data.IsBusy && data.Rows.Count == 2 && data.Rows.Any(r => Convert.ToInt32(r[0]) == 2 && Convert.ToDecimal(r[3]) == 14m));

            // Sin la clave de su tabla, o con un JOIN: se ve pero no se edita, y lo dice.
            var noKey = (await OpenDataAsync(vm, "UI_V_SINCLAVE", DbObjectKind.View)).Data!;
            Assert.False(noKey.CanEditTable);
            Assert.Contains("no muestra toda su clave primaria", noKey.ReadOnlyReason);
            var join = (await OpenDataAsync(vm, "UI_V_JOIN", DbObjectKind.View)).Data!;
            Assert.False(join.CanEditTable);
            Assert.Null(join.AddRow());
        }
        finally
        {
            w.Close();
            await vm.DisposeAsync();
            foreach (var view in new[] { "UI_V_EDIT", "UI_V_SINCLAVE", "UI_V_JOIN" }) await DropAsync("VIEW", view);
            await DropTableAsync("UI_VBASE");
        }
    }, CancellationToken.None);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
