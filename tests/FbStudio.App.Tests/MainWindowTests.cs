using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.Core.Results;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird.Tests;
using FbStudio.Workspace;

namespace FbStudio.App.Tests;

/// <summary>Arranca Avalonia sin ventana (Skia) una sola vez para todos los tests de UI.</summary>
public sealed class HeadlessFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessFixture));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition(nameof(UiCollection))]
public sealed class UiCollection : ICollectionFixture<FirebirdFixture>, ICollectionFixture<HeadlessFixture>;

/// <summary>La ventana real (MainWindow + ViewModel + DrawnGrid + editor) contra Firebird 3.0 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class MainWindowTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-tests-" + Guid.NewGuid().ToString("N"));

    private (MainWindow Window, MainWindowViewModel Vm) Open(Func<ConnectionProfile, ConnectionProfile>? tweak = null)
    {
        var store = AppServices.CreateProfileStore(_folder);
        var profile = (tweak ?? (p => p))(fb.Profile);
        store.Upsert(profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
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

    [Fact]
    public Task Select_llena_la_grilla_respetando_el_limite_por_defecto() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT * FROM TURNOS WHERE FECHA >= '2026-01-10'";
        await w.RunAsync();
        Pump();

        Assert.Equal(10_000, vm.SelectedTab!.Rows.Count);
        Assert.Contains("10.000 filas", vm.SelectedTab!.Status);
        Assert.Contains("TURNOS_FECHA", vm.SelectedTab!.Plan);
        Assert.Contains("TURNOS", vm.SelectedTab!.Stats);
        Assert.True(w.ResultsGrid.CellsDrawnLastFrame > 0, "la grilla no dibujó celdas");

        // Ctrl+Shift+E: el plan sin ejecutar (no trae filas ni toca la grilla); con parámetros también.
        var rowsBefore = vm.SelectedTab!.Rows.Count;
        w.Editor.Text = "SELECT * FROM TURNOS WHERE OBS = :OBS";
        w.Editor.CaretOffset = 3;
        await w.ShowPlanOnlyAsync();
        Assert.Contains("TURNOS", vm.SelectedTab.Plan);
        Assert.Equal(EditorTabViewModel.PlanTab, vm.SelectedTab.SelectedResultTab);
        Assert.StartsWith("Plan sin ejecutar", vm.SelectedTab.Status);
        Assert.Contains("lectura completa", vm.SelectedTab.Status);                  // OBS no tiene índice
        Assert.Equal(rowsBefore, vm.SelectedTab.Rows.Count);
        w.Editor.Text = "CREATE TABLE X (A INTEGER)";
        await w.ShowPlanOnlyAsync();
        Assert.StartsWith("El plan es para", vm.SelectedTab.Status);

        // Ctrl+F con la grilla elegida busca en los resultados (sin mayúsculas ni acentos) y va a cada coincidencia.
        vm.SelectedTab.SelectedResultTab = 0;                                          // de vuelta a Resultados
        Pump();
        var grid = w.ResultsGrid;
        grid.SelectCell(0, 0);
        grid.FocusCells();
        Pump();
        w.KeyPress(Avalonia.Input.Key.F, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.F, null);
        Pump();
        Assert.True(grid.IsSearchOpen);
        Assert.False(w.CurrentView.FindBar.IsVisible);                                 // no abre la búsqueda del editor
        var id = vm.SelectedTab!.Rows[5][0]!.ToString()!;
        grid.SearchText = id;
        Assert.True(grid.SearchMatchCount >= 1);
        Assert.StartsWith("1 de ", grid.SearchCount);
        Assert.Contains(grid.CurrentRow!, v => v?.ToString()?.Contains(id) == true);
        grid.SearchText = "texto que no está en ningún turno";
        Assert.Equal("Sin coincidencias", grid.SearchCount);
        grid.CloseSearch();
        Assert.False(grid.IsSearchOpen);

        // Los valores de una columna como «COL IN (…)», para pegar en otra consulta.
        var inList = grid.InListText(0, [0, 1, 2, 1])!;
        Assert.Equal($"ID IN ({vm.SelectedTab!.Rows[0][0]}, {vm.SelectedTab.Rows[1][0]}, {vm.SelectedTab.Rows[2][0]})", inList);

        // La fila en vertical se recorre con ◀ ▶ (de a una, sin pasarse).
        var record = w.ResultsGrid.ShowRow(0)!;
        record.Go(1);
        Assert.Equal((1, "Fila 2 de 10000"), (record.Position, record.Title));
        Assert.Equal(vm.SelectedTab!.Rows[1][0], record.Grid.Rows[0][2]);
        record.Go(-5);
        Assert.Equal(0, record.Position);
        record.Close();
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Ver_el_valor_de_una_celda_texto_largo_json_e_imagen() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT CAST('{\"turno\":1,\"obs\":\"señal\"}' AS BLOB SUB_TYPE TEXT) AS J, x'0001FF' AS B, CAST(NULL AS VARCHAR(5)) AS N FROM RDB$DATABASE";
        await w.RunAsync();
        Pump();
        Assert.Single(vm.SelectedTab!.Rows);

        // El JSON se ve con sangría y también tal cual; el binario, en hexadecimal; NULL, como NULL.
        var json = w.ResultsGrid.ShowValue(0, 0)!;
        Assert.Equal(["Con sangría", "Texto"], json.Views);
        Assert.Equal("{\n  \"turno\": 1,\n  \"obs\": \"señal\"\n}", json.ShownText!.Replace("\r\n", "\n"));
        Assert.Equal("Valor de J", json.Title);
        json.SelectView("Texto");
        Assert.Equal("{\"turno\":1,\"obs\":\"señal\"}", json.ShownText);
        json.Close();
        var binary = w.ResultsGrid.ShowValue(0, 1)!;
        Assert.Equal(["Hexadecimal"], binary.Views);
        Assert.StartsWith("00000000  00 01 FF", binary.ShownText);
        binary.Close();
        var empty = w.ResultsGrid.ShowValue(0, 2)!;
        Assert.Equal("NULL", empty.ShownText);
        empty.Close();

        // La fila en vertical: un renglón por columna (con su tipo); NULL queda NULL y el binario se puede ver entero.
        var record = w.ResultsGrid.ShowRow(0)!;
        Pump();
        Assert.Equal(3, record.Grid.Rows.Count);
        Assert.Equal(["J", "B", "N"], record.Grid.Rows.Select(r => (string)r[0]));
        Assert.IsType<byte[]>(record.Grid.Rows[1][2]);
        Assert.Equal(DBNull.Value, record.Grid.Rows[2][2]);
        Assert.Equal("Fila 1 de 1", record.Title);
        record.Close();

        // La fila como tabla de Markdown (para pegar en un ticket) y el resultado a JSON (el binario, en base64).
        var markdown = w.ResultsGrid.MarkdownText([0]).Split('\n');
        Assert.Equal(["| J | B | N |", "| --- | --- | --- |"], markdown[..2]);
        Assert.StartsWith("| {\"turno\":1,\"obs\":\"señal\"} | ", markdown[2]);
        Assert.EndsWith(" | NULL |", markdown[2]);
        var jsonPath = Path.Combine(Path.GetTempPath(), $"fbstudio-{Guid.NewGuid():N}.json");
        await vm.SelectedTab!.ExportAsync(jsonPath, false, new FbStudio.Core.Export.JsonExporter());
        Assert.Contains("\"B\": \"AAH/\"", File.ReadAllText(jsonPath));
        File.Delete(jsonPath);

        // Una imagen se dibuja (y se puede ver en hexadecimal).
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var image = new ValueViewerWindow("FOTO", "BLOB", png);
        image.Show(w);
        Pump();
        Assert.Equal(["Imagen", "Hexadecimal"], image.Views);
        Assert.Null(image.ShownText);
        Assert.Contains(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(image), v => v is Avalonia.Controls.Image { Source: not null });
        image.Close();
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Grafico_rapido_de_un_resultado() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT 'A' AS GRUPO, 3 AS N FROM RDB$DATABASE UNION ALL SELECT 'B', 5 FROM RDB$DATABASE UNION ALL SELECT 'A', 2 FROM RDB$DATABASE";
        await w.RunAsync();
        Pump();
        var chart = w.ResultsGrid.ShowChart()!;
        Pump();
        // Propone X = la columna de texto e Y = la numérica, y suma por cada X.
        Assert.Equal([new ChartPoint("A", 5), new ChartPoint("B", 5)], chart.Chart.Points);
        Assert.Equal("N por GRUPO", chart.Title);
        Assert.Equal("2 puntos", chart.Info);
        Assert.Equal(2, chart.Chart.MarksDrawnLastFrame);
        chart.Choose("GRUPO", null);                                                 // la cantidad de filas
        Assert.Equal([2d, 1d], chart.Chart.Points.Select(p => p.Value));
        Assert.Equal("Cantidad de filas por GRUPO", chart.Title);
        chart.Chart.HoverAt(new Avalonia.Point(chart.Chart.Bounds.Width - 30, 100));
        Assert.Equal(1, chart.Chart.Hover);                                            // la franja de la segunda
        chart.Chart.Kind = FbStudio.App.ResultGrid.ChartKind.Line;
        Pump();
        Assert.Equal(2, chart.Chart.MarksDrawnLastFrame);
        chart.Close();
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Opciones_se_aplican_al_aceptar_y_quedan_guardadas() => ui.Session.Dispatch(() =>
    {
        var (w, vm) = Open();
        try
        {
            Assert.Equal(10_000, vm.RowLimit);
            Assert.Equal(14, w.Editor.FontSize);
            Assert.True(w.Editor.ShowLineNumbers);

            // La ventana carga los valores vigentes; nada se aplica hasta «Aceptar».
            var dialog = new OptionsWindow(new OptionsViewModel(vm.Options));
            dialog.Show();
            Pump();
            var opts = dialog.Options;
            Assert.Equal((10_000, 10m, 90m, 0), (opts.DefaultRowLimit, opts.LockTimeoutSeconds, opts.HistoryRetentionDays, opts.ThemeIndex));
            opts.DefaultRowLimit = 1_000;
            opts.LockTimeoutSeconds = 3;
            opts.EditorFontSize = 18;
            opts.ShowLineNumbers = false;
            opts.ThemeIndex = 2; // oscuro
            opts.HistoryRetentionDays = 30;
            Assert.Equal(14, w.Editor.FontSize);
            var chosen = opts.ToSettings();
            opts.ResetDefaultsCommand.Execute(null);
            Assert.Equal(10_000, opts.DefaultRowLimit);
            dialog.Close();

            vm.SaveSettings(chosen);
            Pump();
            Assert.Equal(1_000, vm.RowLimit);
            Assert.Equal(TimeSpan.FromSeconds(3), vm.LockTimeout);
            Assert.Equal(18, w.Editor.FontSize);
            Assert.False(w.Editor.ShowLineNumbers);
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
            var saved = new SettingsStore(_folder).Current; // quedó en settings.json
            Assert.Equal((AppTheme.Dark, 30, 1_000), (saved.Theme, saved.HistoryRetentionDays, saved.DefaultRowLimit));

            // Una pestaña nueva arranca con las mismas opciones.
            vm.NewQueryTab();
            Pump();
            Assert.Equal(18, w.Editor.FontSize);

            // Ctrl+rueda agranda la letra y Ctrl+0 la vuelve al tamaño de Opciones.
            w.Editor.Focus();
            var inside = w.Editor.TranslatePoint(new Point(80, 40), w)!.Value;
            w.MouseWheel(inside, new Vector(0, 1), Avalonia.Input.RawInputModifiers.Control);
            w.MouseWheel(inside, new Vector(0, 1), Avalonia.Input.RawInputModifiers.Control);
            Pump();
            Assert.Equal(20, w.Editor.FontSize);
            w.Editor.TextArea.Focus();
            Pump();
            Assert.True(w.Editor.IsKeyboardFocusWithin, "foco: " + w.FocusManager?.GetFocusedElement());
            w.KeyPress(Avalonia.Input.Key.D0, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Digit0, null);
            Pump();
            Assert.Equal(18, w.Editor.FontSize);

            // Aceptar Opciones sin cambiar el valor por defecto no pisa el límite elegido en la barra.
            vm.RowLimit = 100_000;
            vm.SaveSettings(vm.Options with { EditorFontSize = 15 });
            Assert.Equal(100_000, vm.RowLimit);
        }
        finally
        {
            vm.SaveSettings(new AppSettings()); // tema por defecto para los demás tests (comparten la aplicación)
            w.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task Ctrl_Enter_ejecuta_solo_la_sentencia_del_cursor_y_lo_avisa() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        const string text = "SELECT 1 FROM RDB$DATABASE;\nSELECT ID FROM TURNOS WHERE ID < 5;\nUPDATE TURNOS SET OBS = OBS WHERE ID = 1";
        w.Editor.Text = text;
        w.Editor.CaretOffset = text.IndexOf("ID < 5", StringComparison.Ordinal);
        w.Editor.TextArea.Focus();
        Pump();
        w.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Enter, null);
        var tab = vm.SelectedTab!;
        await WaitFor(() => !tab.IsRunning && tab.Rows.Count == 5);
        Assert.Equal(text, w.Editor.Text);                       // el Enter no llegó al editor
        Assert.Contains("se ejecutó solo la sentencia del cursor (línea 2)", tab.Status);
        Assert.False(tab.InTransaction);                          // el UPDATE de la línea 3 no corrió
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task F9_sin_seleccion_ejecuta_todo_como_script_y_el_COMMIT_lo_cierra() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 21;\nSELECT ID, IMPORTE FROM TURNOS WHERE ID BETWEEN 20 AND 22;\nCOMMIT;";
        w.Editor.CaretOffset = 0;
        await w.RunAsync();
        var tab = vm.SelectedTab!;

        Assert.StartsWith("Script: 3 de 3 sentencias", tab.Status);
        Assert.False(tab.InTransaction);                          // el COMMIT del script la cerró
        Assert.Equal(3, tab.Rows.Count);                          // la grilla muestra el último SELECT
        Assert.Equal(0, tab.SelectedResultTab);
        Assert.Contains("✓ línea 1 · UPDATE: 1 filas afectadas", tab.Messages);
        Assert.Contains("✓ línea 2 · SELECT: 3 filas", tab.Messages);
        Assert.Contains("✓ línea 3 · COMMIT hecho", tab.Messages);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Un_script_se_detiene_en_el_primer_error_y_va_a_esa_linea() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT 1 FROM RDB$DATABASE;\nSELECT * FROM NO_EXISTE;\nUPDATE TURNOS SET OBS = OBS WHERE ID = 22;";
        await w.RunAsync();
        var tab = vm.SelectedTab!;

        Assert.StartsWith("Script: 1 de 3 sentencias", tab.Status);
        Assert.Contains("se detuvo por un error en la línea 2", tab.Status);
        Assert.Contains("✗ línea 2", tab.Messages);
        Assert.Contains("NO_EXISTE", tab.Messages);
        Assert.False(tab.InTransaction);                          // el UPDATE no llegó a correr
        Assert.Equal(EditorTabViewModel.MessagesTab, tab.SelectedResultTab);
        Assert.Equal(2, w.Editor.TextArea.Caret.Line);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Un_script_puede_seguir_ante_errores_y_el_registro_se_guarda() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        vm.ScriptContinueOnError = true;                                     // menú Consulta (es la misma opción de Opciones)
        Assert.False(vm.Options.ScriptStopOnError);
        w.Editor.Text = "UPDATE TURNOS SET OBS = 'a' WHERE ID = 40;\nSELECT * FROM NO_EXISTE;\nUPDATE TURNOS SET OBS = 'b' WHERE ID = 41;\nINSERT INTO NO_EXISTE_2 VALUES (1);";
        await w.RunAsync();
        var tab = vm.SelectedTab!;
        Assert.StartsWith("Script: 2 de 4 sentencias", tab.Status);
        Assert.Contains("2 con error (líneas 2, 4)", tab.Status);
        Assert.Contains("✓ línea 3", tab.Messages);                         // siguió después del error
        Assert.Contains("✗ línea 4", tab.Messages);
        Assert.Contains("✗ línea 2 · SELECT: Table unknown · NO_EXISTE", tab.Messages);   // el renglón dice algo útil
        Assert.True(tab.InTransaction);                                      // los dos UPDATE quedan para Commit o Rollback
        var exit = Assert.Single(vm.ExitWarnings());                         // al salir, avisa que se deshacen
        Assert.Contains($"«{tab.BaseTitle}»", exit);
        Assert.Contains("rollback", exit);
        Assert.Equal(2, w.Editor.TextArea.Caret.Line);                       // el cursor, en el primer error
        Assert.Equal(EditorTabViewModel.MessagesTab, tab.SelectedResultTab);

        var log = tab.LogForFile(new DateTime(2026, 10, 19, 21, 30, 0));
        Assert.StartsWith("FbStudio · registro del 19/10/2026 21:30:00\r\nBase: " + fb.Profile.Name + " (", log);
        Assert.Contains("2 con error (líneas 2, 4)", log);
        Assert.DoesNotContain("password", log, StringComparison.OrdinalIgnoreCase); // el encabezado nunca lleva la contraseña
        Assert.DoesNotContain("\r\r", log);
        Assert.DoesNotMatch("[^\r]\n", log);                                // todo con saltos de Windows

        await tab.RollbackCommand.ExecuteAsync(null);
        vm.ScriptContinueOnError = false;
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Un_script_largo_corre_sin_grilla_y_queda_una_sola_vez_en_el_historial() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        w.Editor.Text = "SELECT FIRST 3 ID FROM TURNOS";
        await w.RunAsync();
        Assert.Equal(3, tab.Rows.Count);
        var before = vm.HistoryItems.Count;

        // 600 UPDATE: más de 200 → F9 lo corre sin grilla; más de 500 → el registro resume por tipo.
        w.Editor.Text = string.Join("\n", Enumerable.Range(100, 600).Select(i => $"UPDATE TURNOS SET OBS = OBS WHERE ID = {i};"));
        await w.RunAsync();
        Assert.StartsWith("Script: 600 de 600 sentencias", tab.Status);
        Assert.Contains("sin grilla: más de 200 sentencias", tab.Status);
        Assert.Contains("UPDATE: 600 sentencias · 600 filas", tab.Messages);
        Assert.DoesNotContain("✓ línea", tab.Messages);
        Assert.Equal(3, tab.Rows.Count);                                     // la grilla quedó como estaba
        Assert.True(tab.InTransaction);
        var entry = vm.HistoryItems[0];
        Assert.Equal(("SCRIPT", 600), (entry.Kind, entry.RecordsAffected));
        Assert.Equal(before + 1, vm.HistoryItems.Count);                     // una entrada, no 600
        await tab.RollbackCommand.ExecuteAsync(null);

        // Ctrl+F9 con una sola sentencia: también sin grilla (el SELECT solo cuenta sus filas).
        w.Editor.Text = "SELECT ID FROM TURNOS WHERE ID < 5";
        w.KeyPress(Avalonia.Input.Key.F9, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.F9, null);
        await WaitFor(() => !tab.IsRunning && tab.Status.Contains("sin grilla"));
        Assert.Contains("SELECT: 5 filas (sin grilla no se muestran)", tab.Messages);
        Assert.Equal(3, tab.Rows.Count);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Al_cerrar_recuerda_las_consultas_y_al_abrir_las_vuelve_a_abrir() => ui.Session.DispatchAsync(() =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var session = new SessionStore(null, null);
        var settings = AppServices.CreateSettingsStore(_folder);
        var first = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), settings, session: session);
        var profile = first.Profiles.Single();
        first.NewQueryTab(profile, "SELECT ID FROM TURNOS");
        first.NewQueryTab(profile);                                                    // vacía: no se guarda
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "guardada.sql");
        File.WriteAllText(path, "SELECT 1 FROM RDB$DATABASE");
        var fileTab = first.OpenSqlFile(path)!;
        fileTab.Document.Insert(0, "-- sin guardar\n");                                 // lo no guardado se descartó al salir
        var numbered = first.NewQueryTab(profile, "SELECT 2 FROM RDB$DATABASE");
        first.SelectedTab = fileTab;
        first.SaveSession();

        var second = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), settings, session: session);
        second.RestoreSession();
        Assert.Equal(3, second.Tabs.Count);
        Assert.Equal(("SELECT ID FROM TURNOS", profile.Id), (second.Tabs[0].Document.Text, second.Tabs[0].Profile!.Id));
        Assert.Equal((path, "SELECT 1 FROM RDB$DATABASE"), (second.Tabs[1].FilePath, second.Tabs[1].Document.Text));
        Assert.Same(second.Tabs[1], second.SelectedTab);
        Assert.Equal(numbered.BaseTitle, second.Tabs[2].BaseTitle);
        Assert.StartsWith("Se volvieron a abrir 3 consultas", second.SelectedTab!.Status);
        var next = second.NewQueryTab(profile);
        Assert.NotEqual(numbered.BaseTitle, next.BaseTitle);                            // sigue numerando desde la mayor

        // Con la opción apagada, al cerrar se olvida y al abrir no se reabre nada.
        second.SaveSettings(second.Options with { RestoreTabs = false });
        second.SaveSession();
        Assert.Null(session.Load());
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task Renombrar_duplicar_y_reabrir_la_pestana_cerrada() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        tab.Document.Text = "SELECT ID FROM TURNOS";
        tab.Rename("Turnos del día");
        Assert.Equal("Turnos del día", tab.BaseTitle);
        var menu = w.BuildTabMenu(vm, tab).ItemsSource!.OfType<Avalonia.Controls.MenuItem>().Select(m => ((string?)m.Header, m.IsEnabled)).ToList();
        Assert.Equal([("Renombrar…", true), ("Duplicar", true), ("Reabrir la pestaña cerrada (Ctrl+Shift+T)", false)], menu.Take(3));

        var copy = vm.DuplicateTab(tab);
        Assert.Equal(("SELECT ID FROM TURNOS", tab.Profile?.Id), (copy.Document.Text, copy.Profile?.Id));
        Assert.NotEqual(tab.BaseTitle, copy.BaseTitle);
        await vm.CloseTabAsync(tab);
        Assert.DoesNotContain(tab, vm.Tabs);
        Assert.True(vm.CanReopenClosedTab);

        // Ctrl+Shift+T la vuelve a abrir con su nombre y su texto.
        w.KeyPress(Avalonia.Input.Key.T, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.T, null);
        Pump();
        var reopened = vm.SelectedTab!;
        Assert.Equal(("Turnos del día", "SELECT ID FROM TURNOS"), (reopened.BaseTitle, reopened.Document.Text));
        Assert.False(vm.CanReopenClosedTab);
        Assert.Null(vm.ReopenClosedTab());
        Assert.StartsWith("No hay consultas cerradas", vm.SelectedTab!.Status);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Comandos_de_edicion_del_editor() => ui.Session.DispatchAsync(() =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT ID\nFROM turnos\nWHERE ID > 1";
        w.Editor.CaretOffset = "SELECT ID\nFROM tur".Length;
        w.Editor.TextArea.Focus();
        Pump();
        Assert.True(w.Editor.IsKeyboardFocusWithin);
        void Press(Avalonia.Input.Key key, Avalonia.Input.RawInputModifiers modifiers, Avalonia.Input.PhysicalKey physical)
        {
            w.KeyPress(key, modifiers, physical, null);
            Pump();
        }

        // Ctrl+Shift+U: la palabra del cursor en mayúsculas; Ctrl+D duplica el renglón; Alt+↑ lo sube.
        Press(Avalonia.Input.Key.U, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.U);
        Assert.Equal("SELECT ID\nFROM TURNOS\nWHERE ID > 1", w.Editor.Text);
        Press(Avalonia.Input.Key.D, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.D);
        Assert.Equal("SELECT ID\nFROM TURNOS\nFROM TURNOS\nWHERE ID > 1", w.Editor.Text);
        Assert.Equal(3, w.Editor.Document.GetLineByOffset(w.Editor.CaretOffset).LineNumber);
        Press(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.Alt, Avalonia.Input.PhysicalKey.ArrowDown);
        Assert.Equal("SELECT ID\nFROM TURNOS\nWHERE ID > 1\nFROM TURNOS", w.Editor.Text);

        // Ctrl+/ (en el teclado español, la tecla «-» de esa posición) comenta y descomenta; cada uno, un solo Ctrl+Z.
        w.Editor.Select(0, "SELECT ID\nFROM TURNOS".Length);
        Press(Avalonia.Input.Key.OemMinus, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Slash);
        Assert.Equal("-- SELECT ID\n-- FROM TURNOS\nWHERE ID > 1\nFROM TURNOS", w.Editor.Text);
        Assert.Equal("-- SELECT ID\n-- FROM TURNOS", w.Editor.SelectedText);
        Press(Avalonia.Input.Key.OemQuestion, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Slash);
        Assert.Equal("SELECT ID\nFROM TURNOS\nWHERE ID > 1\nFROM TURNOS", w.Editor.Text);
        w.Editor.Undo();
        Assert.Equal("-- SELECT ID\n-- FROM TURNOS\nWHERE ID > 1\nFROM TURNOS", w.Editor.Text);

        // Los bloques de varios renglones se pliegan (marcas en el margen); con la opción apagada, no hay marcas.
        w.Editor.Text = "SELECT *\nFROM T\nWHERE ID IN (\n  SELECT ID\n  FROM U\n)";
        var setup = w.CurrentView.EditorSetup!;
        setup.UpdateFoldings();
        var block = Assert.Single(setup.Foldings);
        Assert.Equal("\n  SELECT ID\n  FROM U\n", w.Editor.Text[block.Start..block.End]);
        vm.SaveSettings(vm.Options with { ShowFolding = false });
        Assert.False(setup.Folding);
        vm.SaveSettings(vm.Options with { ShowFolding = true });
        Assert.True(setup.Folding);

        // Los mismos comandos están en Consulta › Editar el texto.
        var menu = Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(w).OfType<Avalonia.Controls.MenuItem>().Single(m => (string?)m.Header == "Editar el texto");
        Assert.Equal(7, menu.Items.Count);
        w.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task Buscar_y_reemplazar_en_el_editor() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT ID FROM TURNOS\nWHERE TURNOS.ID > 1\n-- turnos de hoy\nAND TURNOS_DET.ID = 2";
        w.Editor.CaretOffset = 0;
        w.Editor.Focus();
        Pump();

        // Ctrl+F abre la barra; las coincidencias se cuentan y se elige la primera.
        w.KeyPress(Avalonia.Input.Key.F, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.F, null);
        Pump();
        var bar = w.CurrentView.FindBar;
        Assert.True(bar.IsVisible);
        bar.FindText = "turnos";
        Pump();
        Assert.Equal(4, bar.Matches.Count);
        Assert.Equal("1 de 4", bar.CountText);
        Assert.Equal("TURNOS", w.Editor.SelectedText);
        bar.Go(1);
        Assert.Equal("2 de 4", bar.CountText);
        bar.Go(-1);
        bar.Go(-1);                                                                   // da la vuelta: la última
        Assert.Equal("4 de 4", bar.CountText);
        bar.WholeWord = true;
        Assert.Equal(3, bar.Matches.Count);                                           // TURNOS_DET ya no
        bar.MatchCase = true;
        Assert.Equal(1, bar.Matches.Count);                                           // con mayúsculas: solo la del comentario

        // Reemplazar todo con los grupos de una expresión regular: un solo Ctrl+Z lo vuelve atrás.
        var before = w.Editor.Text;
        bar.WholeWord = false;
        bar.MatchCase = true;
        bar.UseRegex = true;
        bar.FindText = @"TURNOS(_DET)?\.ID";
        bar.ReplaceText = "X$1.ID";
        Assert.Equal(2, bar.ReplaceAll());
        Assert.Contains("WHERE X.ID > 1", w.Editor.Text);
        Assert.Contains("AND X_DET.ID = 2", w.Editor.Text);
        Assert.Equal("2 reemplazos", bar.CountText);
        w.Editor.Undo();
        Assert.Equal(before, w.Editor.Text);

        bar.FindText = "([";
        Assert.Equal("Expresión no válida", bar.CountText);

        // Con Ctrl+H, Tab y Mayús+Tab van y vienen entre buscar y reemplazar.
        bar.UseRegex = false;
        w.Editor.Focus();
        w.KeyPress(Avalonia.Input.Key.H, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.H, null);
        Pump();
        Assert.True(bar.FindHasFocus || bar.ReplaceHasFocus);
        var startedInFind = bar.FindHasFocus;
        w.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Tab, null);
        Pump();
        Assert.Equal(startedInFind, bar.ReplaceHasFocus);
        w.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.Tab, null);
        Pump();
        Assert.Equal(startedInFind, bar.FindHasFocus);
        bar.Close();
        Assert.False(bar.IsVisible);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Abrir_y_guardar_archivos_sql_con_aviso_de_cambios_sin_guardar() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var first = vm.SelectedTab!;
        Directory.CreateDirectory(_folder);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var latin1 = System.Text.Encoding.GetEncoding(1252);
        var path = Path.Combine(_folder, "consulta de prueba.sql");
        File.WriteAllText(path, "-- Pacientes con turno\nSELECT * FROM TURNOS WHERE OBS = 'revisión';\n", latin1);

        // La consulta vacía que estaba elegida se usa para el archivo; el título es su nombre.
        var tab = vm.OpenSqlFile(path)!;
        Assert.Same(first, tab);
        Assert.Equal(("consulta de prueba.sql", path), (tab.BaseTitle, tab.FilePath));
        Assert.Contains("'revisión'", tab.Document.Text);
        Assert.False(tab.IsModified);
        Assert.Equal([path], vm.RecentFiles);
        Assert.Same(tab, vm.OpenSqlFile(path));                                  // ya abierto: va a su pestaña

        // Cambios sin guardar: «*» en el título, aviso al cerrar y al salir.
        tab.Document.Insert(tab.Document.TextLength, "-- más\n");
        Assert.True(tab.IsModified);
        Assert.Contains(" *", tab.TitleText);
        Assert.Contains(vm.ExitWarnings(), m => m.Contains("«consulta de prueba.sql»"));
        DiscardPrompt? asked = null;
        vm.ConfirmDiscardAsync = p => { asked = p; return Task.FromResult(false); };
        await vm.CloseTabAsync(tab);
        Assert.Equal("«consulta de prueba.sql» tiene cambios sin guardar. Si cerrás la pestaña, se pierden.", asked!.Message);
        Assert.Contains(tab, vm.Tabs);

        // Guardar: en el mismo archivo y con la misma codificación (Windows-1252).
        tab.SaveFile(path);
        Assert.False(tab.IsModified);
        Assert.StartsWith("Guardado: ", tab.Status);
        Assert.Equal(tab.Document.Text, File.ReadAllText(path, latin1));
        Assert.Contains((byte)0xF3, File.ReadAllBytes(path));                   // la «ó» en un byte, como en 1252

        // Guardar como: otro nombre, y queda primero en los recientes; un emoji no entra en 1252 → UTF-8.
        var other = Path.Combine(_folder, "otra.sql");
        tab.Document.Insert(0, "-- ✓ revisado\n");
        w.SaveCurrentTabTo(other);
        Assert.Equal(("otra.sql", other), (tab.BaseTitle, tab.FilePath));
        Assert.Contains("UTF-8", tab.Status);
        Assert.StartsWith("-- ✓ revisado", File.ReadAllText(other, System.Text.Encoding.UTF8));
        Assert.Equal([other, path], vm.RecentFiles);

        // Otro archivo con la consulta ya ocupada: va a una pestaña nueva; uno que ya no está sale de los recientes.
        var count = vm.Tabs.Count;
        var second = vm.OpenSqlFile(path)!;
        Assert.NotSame(tab, second);
        Assert.Equal(count + 1, vm.Tabs.Count);
        var gone = Path.Combine(_folder, "borrado.sql");
        vm.RememberFile(gone);
        Assert.Null(vm.OpenSqlFile(gone));
        Assert.DoesNotContain(gone, vm.RecentFiles);
        Assert.StartsWith("Ya no está el archivo", vm.SelectedTab!.Status);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Un_script_de_un_archivo_corre_sin_pasar_por_el_editor() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        w.Editor.Text = "-- lo que había";
        vm.SaveSettings(vm.Options with { ScriptStopOnError = false });

        // Un .sql en Windows-1252 (con tilde), con un comentario arriba y la sentencia 151 con error.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var sql = new System.Text.StringBuilder("-- Corrección de observaciones\n");
        for (var i = 0; i < 300; i++)
            sql.Append(i == 150 ? "UPDATE TURNOS SET NO_EXISTE = 1 WHERE ID = 1;\n" : $"UPDATE TURNOS SET OBS = 'revisión' WHERE ID = {i + 1000};\n");
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "arreglo.sql");
        File.WriteAllText(path, sql.ToString(), System.Text.Encoding.GetEncoding(1252));

        await tab.RunFileAsync(path);
        Assert.StartsWith("Script: 299 de 300 sentencias", tab.Status);
        Assert.Contains("1 con error (línea 152)", tab.Status);                 // el renglón del archivo
        Assert.Contains("desde el archivo arreglo.sql", tab.Status);
        Assert.Contains("✗ línea 152 · UPDATE", tab.Messages);
        Assert.Equal("-- lo que había", w.Editor.Text);                          // el editor no se tocó
        Assert.True(tab.InTransaction);
        Assert.Equal("-- Script del archivo arreglo.sql (300 sentencias)", vm.HistoryItems[0].Sql);

        w.Editor.Text = "SELECT OBS FROM TURNOS WHERE ID = 1000";                // en la misma transacción
        await w.RunAsync();
        Assert.Equal("revisión", tab.Rows[0][0]);
        await tab.RollbackCommand.ExecuteAsync(null);

        File.WriteAllText(path, "-- solo un comentario\n");
        await tab.RunFileAsync(path);
        Assert.Equal("arreglo.sql no tiene sentencias para ejecutar.", tab.Status);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Modo_oscuro_cambia_el_resaltado_y_los_colores_al_vuelo() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        try
        {
            vm.SaveSettings(vm.Options with { Theme = AppTheme.Light });
            w.Editor.Text = "SELECT FIRST 20 * FROM TURNOS";
            await w.RunAsync();
            Pump();
            Assert.True(w.ResultsGrid.CellsDrawnLastFrame > 0);
            Assert.False(w.ResultsGrid.LastFrameDark);
            var db = Assert.Single(vm.Tree.Rows, n => n.Kind == TreeNodeKind.Database);
            Assert.True(db.IsDevTone);                                   // Desarrollo: verde
            Assert.True(vm.SelectedTab!.IsDevTone);                      // y el borde de la pestaña también
            Assert.Equal("LightPlus", w.CurrentEditorView!.EditorTheme);

            vm.SaveSettings(vm.Options with { Theme = AppTheme.Dark });
            Pump();
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, w.ActualThemeVariant);
            Assert.True(w.ResultsGrid.LastFrameDark);                         // la grilla ya cargada se redibuja oscura
            Assert.True(w.ResultsGrid.CellsDrawnLastFrame > 0);
            Assert.Equal("DarkPlus", w.CurrentEditorView!.EditorTheme);      // el resaltado del editor sigue al tema
            vm.NewQueryTab();
            Pump();
            Assert.Equal("DarkPlus", w.CurrentEditorView!.EditorTheme);      // y una pestaña nueva arranca oscura

            // Los colores con significado tienen su versión para fondo oscuro.
            Assert.True(Application.Current!.TryGetResource("FbsSuccessBrush", Avalonia.Styling.ThemeVariant.Dark, out var dark));
            Assert.True(Application.Current!.TryGetResource("FbsSuccessBrush", Avalonia.Styling.ThemeVariant.Light, out var light));
            Assert.NotEqual(((Avalonia.Media.ISolidColorBrush)light!).Color, ((Avalonia.Media.ISolidColorBrush)dark!).Color);

            vm.SaveSettings(vm.Options with { Theme = AppTheme.Light });
            Pump();
            Assert.Equal("LightPlus", w.CurrentEditorView!.EditorTheme);
        }
        finally
        {
            vm.SaveSettings(new AppSettings());
            w.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public async Task Los_tests_async_de_UI_se_esperan_hasta_el_final()
    {
        // Antes de DispatchAsync, una lambda async terminaba en el primer await y los Assert de después no se veían.
        var reachedEnd = false;
        await ui.Session.DispatchAsync(async () =>
        {
            await Task.Delay(20);
            reachedEnd = true;
        }, CancellationToken.None);
        Assert.True(reachedEnd);
    }

    [Fact]
    public Task F9_con_seleccion_ejecuta_solo_eso_y_lo_avisa() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        const string text = "SELECT 1 FROM RDB$DATABASE;\nSELECT ID FROM TURNOS WHERE ID BETWEEN 1 AND 3;\nSELECT 2 FROM RDB$DATABASE;";
        w.Editor.Text = text;
        var start = text.IndexOf("SELECT ID", StringComparison.Ordinal);
        w.Editor.Select(start, text.IndexOf(';', start) - start);
        await w.RunAsync();
        var tab = vm.SelectedTab!;
        Assert.Equal(3, tab.Rows.Count);
        Assert.Contains("se ejecutó solo la selección (línea 2)", tab.Status);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Script_con_una_escritura_en_solo_lectura_no_ejecuta_nada() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(p => p with { ReadOnly = true, SaveHistory = true });
        w.Editor.Text = "SELECT 1 FROM RDB$DATABASE;\nDELETE FROM TURNOS WHERE ID = 23;\nSELECT 2 FROM RDB$DATABASE;";
        await w.RunAsync();
        var tab = vm.SelectedTab!;
        Assert.StartsWith("Bloqueado (no se ejecutó ninguna sentencia del script)", tab.Status);
        Assert.Contains("Línea 2", tab.Messages);
        Assert.Empty(vm.HistoryItems);                            // ni siquiera el primer SELECT
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Script_en_produccion_pregunta_una_sola_vez() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(p => p with { Environment = ProfileEnvironment.Produccion });
        var asked = new List<IReadOnlyList<string>>();
        vm.ConfirmAsync = d => { asked.Add(d.Reasons); return Task.FromResult(false); };
        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 24;\nDELETE FROM TURNOS;\nSELECT 1 FROM RDB$DATABASE;";
        await w.RunAsync();
        var tab = vm.SelectedTab!;
        var reasons = Assert.Single(asked);
        Assert.Contains(reasons, r => r.Contains("2 sentencia(s) que modifican PRODUCCIÓN"));
        Assert.Contains(reasons, r => r.StartsWith("Línea 2: DELETE sin WHERE"));
        Assert.StartsWith("No se ejecutó el script", tab.Status);
        Assert.False(tab.InTransaction);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task DML_deja_la_transaccion_abierta_y_habilita_commit_y_rollback() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "UPDATE TURNOS SET IMPORTE = 0 WHERE ID = 7";
        await w.RunAsync();

        Assert.True(vm.SelectedTab!.InTransaction);
        Assert.Contains("1 filas afectadas", vm.SelectedTab!.Status);
        Assert.NotEmpty(vm.SelectedTab!.TransactionText);
        Assert.True(vm.SelectedTab!.RollbackCommand.CanExecute(null));
        Assert.False(vm.SelectedTab!.CanChangeProfile); // no se puede cambiar de perfil con una transacción abierta

        await vm.SelectedTab!.RollbackCommand.ExecuteAsync(null);
        Assert.False(vm.SelectedTab!.InTransaction);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Sin_bases_registradas_ofrece_registrar_una_o_importar_las_del_equipo() => ui.Session.DispatchAsync(() =>
    {
        var vm = new MainWindowViewModel(AppServices.CreateProfileStore(Path.Combine(_folder, "vacio")));
        var w = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        w.Show();
        Pump();
        Assert.True(vm.Tree.IsEmpty);
        Assert.Equal("Sin bases registradas: tocá «Registrar» para cargar la primera", vm.Banner);
        Assert.True(w.Welcome.IsVisible);
        var import = w.WelcomeImport;
        Assert.Equal("Importar registros…", import.Content);
        Assert.True(import.IsEffectivelyVisible);
        w.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task Autocommit_y_aislamiento_solo_en_desarrollo_y_prueba() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        Assert.True(tab.CanChooseTransactionMode);
        Assert.Equal(["Read committed", "Snapshot", "Snapshot table stability"], tab.Isolations);
        tab.AutoCommit = true;
        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 28";
        await w.RunAsync();
        Assert.False(tab.InTransaction);
        Assert.Contains("confirmado (autocommit)", tab.Status);

        tab.AutoCommit = false;
        await w.RunAsync();
        Assert.True(tab.InTransaction);
        Assert.False(tab.CanChooseTransactionMode);                            // con una abierta no se cambia
        Assert.StartsWith("Con una transacción abierta", tab.TransactionModeTip);
        await tab.RollbackCommand.ExecuteAsync(null);

        tab.AutoCommit = true;
        tab.IsolationIndex = 1;
        tab.Profile = tab.Profile! with { Environment = ProfileEnvironment.Produccion };
        Assert.False(tab.AutoCommit);                                          // en producción se apagan
        Assert.Equal(0, tab.IsolationIndex);
        Assert.False(tab.CanChooseTransactionMode);
        Assert.StartsWith("En producción", tab.TransactionModeTip);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Una_transaccion_abierta_hace_rato_pasa_a_ambar_y_a_rojo() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 27";
        await w.RunAsync();
        var tab = vm.SelectedTab!;
        Assert.True(tab.InTransaction);
        Assert.StartsWith("Transacción de escritura ABIERTA hace menos de 1 min", tab.TransactionText);
        Assert.False(tab.IsTransactionOld);

        var start = DateTime.UtcNow;
        var badge = w.TxBadge;
        Avalonia.Media.Color TextColor() => ((Avalonia.Media.ISolidColorBrush)((Avalonia.Controls.TextBlock)badge.Child!).Foreground!).Color;
        Pump();
        Assert.Equal(Avalonia.Media.Colors.White, TextColor());                // sobre el color del entorno

        tab.Clock = () => start.AddMinutes(6);                                 // de fábrica: ámbar a los 5, rojo a los 15
        tab.RefreshTransactionAge();
        Assert.True(tab.IsTransactionOld);
        Assert.Contains("hace 6 min", tab.TransactionText);
        Pump();
        Assert.Equal(Avalonia.Media.Color.Parse("#1C1F23"), TextColor());      // letra oscura sobre ámbar

        tab.Clock = () => start.AddMinutes(75);
        tab.RefreshTransactionAge();
        Assert.False(tab.IsTransactionOld);
        Assert.True(tab.IsTransactionVeryOld);
        Assert.Contains("hace 1 h 15 min", tab.TransactionText);
        Pump();
        Assert.Equal(Avalonia.Media.Color.Parse("#B71C1C"), TextColor());      // letra roja sobre blanco (antes quedaba blanca)
        Assert.StartsWith("La pestaña", tab.Status);                          // el aviso, una vez
        tab.Status = "";
        tab.RefreshTransactionAge();
        Assert.Equal("", tab.Status);

        await tab.RollbackCommand.ExecuteAsync(null);
        Assert.False(tab.IsTransactionVeryOld);
        Assert.Equal("", tab.TransactionText);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Produccion_avisa_un_SELECT_de_procedimiento_dentro_de_la_transaccion_abierta() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(p => p with { Environment = ProfileEnvironment.Produccion });
        var tab = vm.SelectedTab!;
        _ = tab.Catalog;
        await WaitFor(() => !tab.Catalog.IsEmpty);                          // P_CONTAR sin paréntesis se reconoce por el catálogo
        var asked = new List<IReadOnlyList<string>>();
        vm.ConfirmAsync = d => { asked.Add(d.Reasons); return Task.FromResult(true); };

        w.Editor.Text = "SELECT * FROM P_CONTAR";
        await w.RunAsync();
        Assert.Empty(asked);                                                // sin transacción abierta: corre en una de solo lectura

        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 26";
        await w.RunAsync();
        Assert.Single(asked);                                               // la confirmación de producción
        Assert.True(tab.InTransaction);

        w.Editor.Text = "SELECT * FROM P_CONTAR";
        await w.RunAsync();
        Assert.Equal(2, asked.Count);
        Assert.StartsWith("Usa P_CONTAR y va a correr dentro de la transacción de escritura abierta", Assert.Single(asked[1]));

        w.Editor.Text = "SELECT COUNT(*) FROM TURNOS";
        await w.RunAsync();
        Assert.Equal(2, asked.Count);                                       // sin procedimientos no pregunta
        await tab.RollbackCommand.ExecuteAsync(null);

        // En un script cuenta la transacción que abre el mismo script.
        vm.ConfirmAsync = d => { asked.Add(d.Reasons); return Task.FromResult(false); };
        w.Editor.Text = "SELECT * FROM P_TURNOS_DESDE(1);\nUPDATE TURNOS SET OBS = OBS WHERE ID = 26;\nSELECT * FROM P_TURNOS_DESDE(1);";
        await w.RunAsync();
        var reasons = asked[^1];
        Assert.Contains(reasons, r => r.StartsWith("Línea 3: usa P_TURNOS_DESDE dentro de la transacción de escritura abierta"));
        Assert.DoesNotContain(reasons, r => r.StartsWith("Línea 1"));
        Assert.False(tab.InTransaction);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task La_franja_dice_si_la_conexion_va_cifrada() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        Assert.DoesNotContain("cifr", vm.Banner);                          // todavía no se sabe
        var db = vm.Tree.Rows.Single(n => n.Kind == TreeNodeKind.Database);
        await vm.Tree.ConnectAsync(db);
        Pump();
        var wire = vm.Tree.ServerFor(vm.Profiles.Single().Id)!.WireEncrypted;
        Assert.NotNull(wire);
        Assert.EndsWith(wire == true ? " · conexión cifrada" : " · ⚠ sin cifrar", vm.Banner);
        Assert.Equal(wire == false, vm.BannerTip is not null);
        Assert.Contains(wire == true ? "Conexión cifrada" : "Conexión sin cifrar", db.Tooltip);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Produccion_pide_confirmacion_y_si_se_rechaza_no_ejecuta() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(p => p with { Environment = ProfileEnvironment.Produccion });
        var asked = 0;
        vm.ConfirmAsync = _ => { asked++; return Task.FromResult(false); };
        w.Editor.Text = "UPDATE TURNOS SET IMPORTE = 0 WHERE ID = 8";
        await w.RunAsync();

        Assert.Equal(1, asked);
        Assert.False(vm.SelectedTab!.InTransaction);
        Assert.StartsWith("No se ejecutó", vm.SelectedTab!.Status);
        Assert.StartsWith("PRODUCCIÓN", vm.Banner);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Perfil_de_solo_lectura_bloquea_sin_ir_al_servidor() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(p => p with { ReadOnly = true });
        w.Editor.Text = "DELETE FROM TURNOS WHERE ID = 9";
        await w.RunAsync();

        Assert.StartsWith("Bloqueado", vm.SelectedTab!.Status);
        Assert.Contains("solo lectura", vm.SelectedTab!.Messages);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Menu_de_la_base_agrupado_y_sin_lo_que_no_aplica() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        await vm.CloseTabsAsync(vm.Tabs.ToList());                           // sin pestañas: nada para desconectar
        var db = Assert.Single(vm.Tree.Rows, n => n.Kind == TreeNodeKind.Database);
        static string[] Headers(IEnumerable<object>? items) =>
            (items ?? []).Cast<Avalonia.Controls.Control>().Select(c => c is Avalonia.Controls.MenuItem mi ? (string)mi.Header! : "─").ToArray();
        static Avalonia.Controls.MenuItem Find(IEnumerable<object>? items, string header) =>
            (items ?? []).OfType<Avalonia.Controls.MenuItem>().Single(m => (string)m.Header! == header);

        var menu = w.BuildTreeMenu(db)!;
        Assert.Equal(["Conectar", "Consulta nueva en esta base", "Buscar en metadatos…", "Importar a una tabla nueva (CSV o Excel)…", "─", "Herramientas",
                "Mantenimiento", "Script y comparar", "─", "Registro"],
            Headers(menu.ItemsSource?.Cast<object>()));
        Assert.Equal(["Monitoreo (conexiones y transacciones)", "Trace (sentencias en vivo)", "Estadísticas", "Usuarios y permisos…", "Áreas…",
                "Abrir diagrama guardado…", "Datos del servidor…"],
            Headers(Find(menu.ItemsSource?.Cast<object>(), "Herramientas").ItemsSource?.Cast<object>()));
        Assert.Equal(["Backup", "Restore", "nbackup", "Validación", "Sweep", "Base vacía", "Propiedades"],
            Headers(Find(menu.ItemsSource?.Cast<object>(), "Mantenimiento").ItemsSource?.Cast<object>()));
        // Con una sola base registrada no hay con qué comparar.
        Assert.Equal(["Script de la base…", "Documentación en HTML…"], Headers(Find(menu.ItemsSource?.Cast<object>(), "Script y comparar").ItemsSource?.Cast<object>()));
        Assert.Equal(["Editar registro…", "Clonar registro… (y abrir sus propiedades)", "Mover a carpeta…", "Exportar este registro…"],
            Headers(Find(menu.ItemsSource?.Cast<object>(), "Registro").ItemsSource?.Cast<object>()));

        // Conectada: «Usar…», «Desconectar» y «Recargar metadatos».
        await vm.Tree.ActivateAsync(db);
        menu = w.BuildTreeMenu(db)!;
        Assert.Equal(["Usar esta base en la pestaña", "Desconectar", "Consulta nueva en esta base", "Buscar en metadatos…", "Recargar metadatos"],
            Headers(menu.ItemsSource?.Cast<object>()).Take(5));

        // Mantenimiento ▸ nbackup abre la pestaña en esa tarea.
        Find(Find(menu.ItemsSource?.Cast<object>(), "Mantenimiento").ItemsSource?.Cast<object>(), "nbackup")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
        Pump();
        var maint = Assert.IsType<MaintenanceTabViewModel>(vm.SelectedTab);
        Assert.Equal(MaintenanceTask.NBackup, maint.CurrentTask);
        await maint.Working;
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Generar_SQL_de_una_tabla_desde_el_arbol() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tree = vm.Tree;
        var db = Assert.Single(tree.Rows, n => n.Kind == TreeNodeKind.Database);
        await tree.ActivateAsync(db);
        var tables = tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.Table);
        await tree.ToggleAsync(tables);
        var turnos = tree.Rows.Single(n => n.Kind == TreeNodeKind.Object && n.Text == "TURNOS");

        // El submenú: de una tabla, las cinco; de una vista, solo el SELECT.
        var group = w.BuildTreeMenu(turnos)!.Items.OfType<Avalonia.Controls.MenuItem>().Single(m => (string?)m.Header == "Generar SQL");
        Assert.Equal(["SELECT con todas las columnas", "INSERT", "UPDATE por la clave primaria", "DELETE por la clave primaria", "UPDATE OR INSERT"],
            group.Items.OfType<Avalonia.Controls.MenuItem>().Select(m => (string?)m.Header));
        var views = tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.View);
        await tree.ToggleAsync(views);
        var view = tree.Rows.Single(n => n.Kind == TreeNodeKind.Object && n.Text == "V_TURNOS");
        Assert.Single(w.BuildTreeMenu(view)!.Items.OfType<Avalonia.Controls.MenuItem>().Single(m => (string?)m.Header == "Generar SQL").Items.OfType<Avalonia.Controls.MenuItem>());

        // Cada una va a una consulta nueva de esa base, sin ejecutar.
        var update = (await vm.GenerateTableSqlAsync(db.Profile!, turnos.Object!, FbStudio.Core.Sql.TableSqlKind.Update))!;
        Assert.Same(update, vm.SelectedTab);
        Assert.Equal(db.Profile!.Id, update.Profile!.Id);
        Assert.Equal("UPDATE TURNOS\nSET FECHA = :FECHA,\n    PACIENTE = :PACIENTE,\n    OBS = :OBS,\n    IMPORTE = :IMPORTE\nWHERE ID = :ID", update.Document.Text);
        Assert.False(update.IsRunning);
        var select = (await vm.GenerateTableSqlAsync(db.Profile, view.Object!, FbStudio.Core.Sql.TableSqlKind.Select))!;
        Assert.Equal("SELECT ID, FECHA\nFROM V_TURNOS", select.Document.Text);

        // De un procedimiento y una función: la llamada con sus parámetros (SELECT solo si devuelve algo).
        var profile = db.Profile;
        Assert.Equal("SELECT *\nFROM P_TURNOS_DESDE(:DESDE, :HASTA)",
            (await vm.GenerateRoutineSqlAsync(profile, new DbObject(DbObjectKind.Procedure, "P_TURNOS_DESDE"), select: true))!.Document.Text);
        Assert.Equal("EXECUTE PROCEDURE P_TURNOS_DESDE(:DESDE, :HASTA)",
            (await vm.GenerateRoutineSqlAsync(profile, new DbObject(DbObjectKind.Procedure, "P_TURNOS_DESDE"), select: false))!.Document.Text);
        Assert.Equal("SELECT F_DOBLE(:X)\nFROM RDB$DATABASE",
            (await vm.GenerateRoutineSqlAsync(profile, new DbObject(DbObjectKind.Function, "F_DOBLE"), select: true))!.Document.Text);
        var procedures = tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.Procedure);
        await tree.ToggleAsync(procedures);
        var contar = tree.Rows.Single(n => n.Kind == TreeNodeKind.Object && n.Text == "P_CONTAR");
        Assert.Equal(["SELECT * FROM …", "EXECUTE PROCEDURE …"], w.BuildTreeMenu(contar)!.Items.OfType<Avalonia.Controls.MenuItem>()
            .Single(m => (string?)m.Header == "Generar SQL").Items.OfType<Avalonia.Controls.MenuItem>().Select(m => (string?)m.Header));

        // Si el objeto ya no está, avisa y no abre nada.
        var count = vm.Tabs.Count;
        Assert.Null(await vm.GenerateTableSqlAsync(db.Profile, new DbObject(DbObjectKind.Table, "NO_EXISTE"), FbStudio.Core.Sql.TableSqlKind.Select));
        Assert.Equal(count, vm.Tabs.Count);
        Assert.StartsWith("No se encontraron las columnas de NO_EXISTE", vm.SelectedTab!.Status);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Arbol_doble_clic_conecta_muestra_categorias_objetos_y_columnas() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tree = vm.Tree;
        var folder = Assert.Single(tree.Rows, n => n.Kind == TreeNodeKind.Folder);
        Assert.Equal("Desarrollo", folder.Text);
        var db = Assert.Single(tree.Rows, n => n.Kind == TreeNodeKind.Database);
        Assert.False(tree.IsConnected(db.Profile!.Id));

        await tree.ActivateAsync(db); // doble clic
        Assert.True(tree.IsConnected(db.Profile.Id));
        Assert.True(db.IsActive);
        Assert.StartsWith("conectada · Firebird 3.0.", db.Detail);
        Assert.Contains("ODS 12.0 · dialecto 3", db.Tooltip);
        var tables = tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.Table);
        Assert.Equal("2", tables.Detail);

        var indices = tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.Index);
        await tree.ToggleAsync(indices);
        tree.Filter = "turnos_*";
        Assert.True(tree.HasFilter);
        // El filtro busca en todas las categorías (las carga) y cada una dice cuántos coinciden.
        await WaitFor(() => tree.Status.Contains("objetos coinciden"));
        Assert.Equal(["TURNOS_FECHA", "TURNOS_PACIENTE"], tree.Rows.Where(n => n.Kind == TreeNodeKind.Object && n.Parent == indices).Select(n => n.Text));
        Assert.True(tree.Rows.Single(n => n.Text == "TURNOS_PACIENTE").IsDimmed); // índice inactivo
        Assert.Equal("1 de 2", tables.Detail);                                   // TURNOS_DET sí, TURNOS no
        Assert.Contains(tree.Rows, n => n.Kind == TreeNodeKind.Object && n.Text == "TURNOS_DET"); // aunque Tablas esté cerrada
        Assert.True(tables.FilterOpen);
        var procedures = tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.Procedure);
        Assert.Equal(("0 de 2", true), (procedures.Detail, procedures.IsDimmed));
        tree.ClearFilterCommand.Execute(null);
        Assert.Equal("2", tables.Detail);
        Assert.False(tables.FilterOpen);

        await tree.ToggleAsync(tables);
        var turnos = tree.Rows.Single(n => n.Kind == TreeNodeKind.Object && n.Text == "TURNOS");
        await tree.ToggleAsync(turnos);
        var cols = tree.Rows.Where(n => n.Kind == TreeNodeKind.Column).ToList();
        Assert.Equal(5, cols.Count);
        Assert.Equal("INTEGER PK NOT NULL", cols[0].Detail);

        // «¿Dónde se usa?» de una columna: la lista de lo que la usa.
        var idColumn = cols.Single(c => c.Column!.Name == "ID");
        Assert.Contains(w.BuildTreeMenu(idColumn)!.Items.OfType<Avalonia.Controls.MenuItem>(), m => (string?)m.Header == "¿Dónde se usa?…");
        var usage = (await w.ShowColumnUsageAsync(vm, db.Profile!, "TURNOS", "ID"))!;
        Assert.Equal("¿Dónde se usa TURNOS.ID?", usage.Title);
        Assert.Contains(usage.Lines, l => l.StartsWith("La usa · Vista V_TURNOS", StringComparison.Ordinal));
        usage.Close();

        // Arrastrar la tabla y una columna al editor: escriben su nombre donde se sueltan.
        Assert.Equal("TURNOS", DatabaseTreeViewModel.EditorText(turnos));
        Assert.Equal("IMPORTE", DatabaseTreeViewModel.EditorText(cols.Single(c => c.Column!.Name == "IMPORTE")));
        Assert.Null(DatabaseTreeViewModel.EditorText(db));                      // la base se mueve entre carpetas, no va al editor
        Assert.True(w.Editor.Options.EnableTextDragDrop);
        w.Editor.Text = "SELECT  FROM ";
        Pump();
        DropText(w, "TURNOS", w.Editor.Text.Length);
        DropText(w, "IMPORTE", "SELECT ".Length);
        Assert.Equal("SELECT IMPORTE FROM TURNOS", w.Editor.Text);

        tree.RequestSelectFirst100(turnos); // clic derecho → SELECT FIRST 100 *, en su propia pestaña
        await WaitFor(() => vm.SelectedTab!.BaseTitle == "TURNOS" && !vm.SelectedTab.IsRunning && vm.SelectedTab.Rows.Count == 100);
        Assert.Equal(2, vm.Tabs.Count);
        Assert.StartsWith("SELECT FIRST 100 *", w.Editor.Text);

        await tree.DisconnectAsync(db);
        Assert.False(tree.IsConnected(db.Profile.Id));
        w.Close();
    }, CancellationToken.None);

    /// <summary>Suelta un texto arrastrado sobre el editor de la pestaña elegida, en el lugar de esa posición del texto.</summary>
    private static void DropText(MainWindow w, string text, int offset)
    {
        var view = w.Editor.TextArea.TextView;
        var at = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(w.Editor.Document.GetLocation(offset)), AvaloniaEdit.Rendering.VisualYPosition.LineMiddle);
        var point = view.TranslatePoint(at - view.ScrollOffset, w)!.Value;
        var data = new Avalonia.Input.DataTransfer();
        data.Add(Avalonia.Input.DataTransferItem.CreateText(text));
        foreach (var type in new[] { Avalonia.Input.Raw.RawDragEventType.DragEnter, Avalonia.Input.Raw.RawDragEventType.DragOver, Avalonia.Input.Raw.RawDragEventType.Drop })
            w.DragDrop(point, type, data, Avalonia.Input.DragDropEffects.Copy, Avalonia.Input.RawInputModifiers.None);
        Pump();
    }

    [Fact]
    public Task F9_ejecuta_la_seleccion_o_todo_el_editor() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT ID FROM TURNOS WHERE ID < 3";
        w.Editor.Focus();
        Pump();
        w.KeyPress(Avalonia.Input.Key.F9, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F9, null);
        await WaitFor(() => !vm.SelectedTab!.IsRunning && vm.SelectedTab!.Rows.Count == 3);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Grilla_ordena_filtra_agrupa_y_totaliza() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT ID, MOD(ID, 3) AS GRUPO, IMPORTE FROM TURNOS WHERE ID < 30";
        await w.RunAsync();
        var g = w.ResultsGrid;
        Assert.Equal(30, g.DisplayCount);

        g.SortBy(0, FbStudio.Core.Results.SortDirection.Descending);
        await g.PendingBuild;
        Pump();
        Assert.Equal(29, Convert.ToInt32(vm.SelectedTab!.Rows[g.Snapshot![0].RowIndex][0]));

        g.SetFilter(2, ">0,1");           // IMPORTE > 0,10 → ID 11..29
        await g.PendingBuild;
        Assert.Equal(19, g.VisibleRowCount);

        g.SetAggregate(0, FbStudio.Core.Results.AggregateKind.Count);
        g.SetAggregate(2, FbStudio.Core.Results.AggregateKind.Sum);
        Assert.Equal("n = 19", g.AggregateText(0));
        Assert.Equal("Σ 3,80", g.AggregateText(2).Replace(".", ","));   // 0,11 + … + 0,29

        g.SetGroupBy([1]);
        await g.PendingBuild;
        Assert.Equal(3, g.DisplayCount);  // tres grupos cerrados
        g.ToggleGroupAt(0);
        await g.PendingBuild;
        Assert.True(g.DisplayCount > 3);

        g.SetFilter(2, null);
        g.SetGroupBy([]);
        g.SortBy(0, FbStudio.Core.Results.SortDirection.None);
        await g.PendingBuild;
        Assert.Null(g.Snapshot);         // vuelve a la identidad
        Assert.Equal(30, g.DisplayCount);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Exporta_CSV_lo_cargado_o_todo_el_resultado() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        vm.RowLimit = 1_000;
        w.Editor.Text = "SELECT ID, PACIENTE, IMPORTE FROM TURNOS ORDER BY ID";
        await w.RunAsync();
        Assert.Equal(1_000, vm.SelectedTab!.Rows.Count);
        Assert.True(vm.SelectedTab!.CanExportFull);

        var loaded = Path.Combine(_folder, "cargado.csv");
        await vm.SelectedTab!.ExportCsvAsync(loaded, full: false);
        var lines = File.ReadAllLines(loaded);
        Assert.Equal(1_001, lines.Length);
        Assert.Equal("ID;PACIENTE;IMPORTE", lines[0].TrimStart('\uFEFF'));
        Assert.Equal("1;PACIENTE 1;0,01", lines[2]);
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(loaded)[..3]); // UTF-8 con BOM para Excel

        var all = Path.Combine(_folder, "todo.csv");
        await vm.SelectedTab!.ExportCsvAsync(all, full: true);
        Assert.Equal(FirebirdFixture.Rows + 1, File.ReadAllLines(all).Length);
        Assert.Contains("CSV exportado", vm.SelectedTab!.Status);
        Assert.Equal(1_000, vm.SelectedTab!.Rows.Count); // la grilla no se tocó
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Historial_registra_cada_ejecucion_y_se_puede_filtrar() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT COUNT(*) FROM TURNOS";
        await w.RunAsync();
        w.Editor.Text = "SELECT * FROM TABLA_QUE_NO_EXISTE";
        await w.RunAsync();

        Assert.Equal(2, vm.HistoryItems.Count);
        Assert.True(vm.HistoryItems[0].Failed);                 // la más reciente primero, con su error
        Assert.Equal("SELECT COUNT(*) FROM TURNOS", vm.HistoryItems[1].Sql);
        Assert.Contains("TURNOS", vm.HistoryItems[1].Plan);

        vm.HistoryFilter = "no_existe";
        Assert.Single(vm.HistoryItems);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Perfil_sin_historial_no_guarda_nada() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(p => p with { SaveHistory = false });
        w.Editor.Text = "SELECT 1 FROM RDB$DATABASE";
        await w.RunAsync();
        Assert.Empty(vm.HistoryItems);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Cada_pestana_tiene_su_base_y_su_transaccion() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var otra = AddProfile(vm, "Otra conexión");
        var first = vm.SelectedTab!;
        w.Editor.Text = "UPDATE TURNOS SET IMPORTE = 0 WHERE ID = 11";
        await w.RunAsync();
        Assert.True(first.InTransaction);
        Assert.StartsWith("● ", first.Title);

        // Con la transacción abierta, elegir otra base en el árbol no la cambia: abre una pestaña nueva.
        vm.Activate(otra);
        var second = vm.SelectedTab!;
        Assert.NotSame(first, second);
        Assert.Equal(otra.Id, second.Profile!.Id);
        Assert.Equal(fb.Profile.Id, first.Profile!.Id);

        // La pestaña nueva tiene su propia conexión: lee sin esperar a la otra transacción.
        w.Editor.Text = "SELECT IMPORTE FROM TURNOS WHERE ID = 11";
        await w.RunAsync();
        Assert.Single(second.Rows);
        Assert.NotEqual(0m, Convert.ToDecimal(second.Rows[0][0]));   // todavía no se confirmó
        Assert.False(second.InTransaction);
        Assert.True(first.InTransaction);

        vm.SelectedTab = first;
        Assert.Equal("UPDATE TURNOS SET IMPORTE = 0 WHERE ID = 11", w.Editor.Text); // cada pestaña conserva su texto
        await first.RollbackCommand.ExecuteAsync(null);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Cerrar_una_pestana_con_transaccion_pregunta_y_deshace() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var asked = 0;
        var answer = false;
        vm.ConfirmCloseAsync = _ => { asked++; return Task.FromResult(answer); };
        var tab = vm.SelectedTab!;
        w.Editor.Text = "UPDATE TURNOS SET IMPORTE = 0 WHERE ID = 12";
        await w.RunAsync();
        Assert.True(tab.InTransaction);

        await vm.CloseTabAsync(tab);                 // dice que no: sigue abierta
        Assert.Equal(1, asked);
        Assert.Contains(tab, vm.Tabs);

        answer = true;
        await vm.CloseTabAsync(tab);                 // dice que sí: se cierra con rollback y no queda ninguna pestaña
        Assert.Empty(vm.Tabs);
        Assert.Null(vm.SelectedTab);
        var remaining = vm.NewQueryTab(vm.Profiles.Single());
        Pump();

        w.Editor.Text = "SELECT IMPORTE FROM TURNOS WHERE ID = 12";
        await w.RunAsync();
        Assert.Equal(0.12m, Convert.ToDecimal(remaining.Rows[0][0]));   // el UPDATE se deshizo
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task La_consulta_nueva_va_a_la_base_de_lo_ultimo_que_se_toco() => ui.Session.DispatchAsync(() =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var first = vm.Profiles.Single();
        var second = vm.CloneProfile(first);
        var secondNode = vm.Tree.Rows.Single(n => n.Kind == TreeNodeKind.Database && n.Profile!.Id == second.Id);
        var firstTab = vm.NewQueryTab(first);

        // En la pestaña de la primera, eligiendo la segunda en el árbol: la consulta nueva va a la segunda (la de la foto).
        vm.Tree.SelectedNode = secondNode;
        Assert.Equal(second.Id, vm.NewQueryTabFor(second)!.Profile!.Id);

        // De vuelta en la pestaña de la primera, con la segunda todavía elegida en el árbol: va a la de la pestaña.
        vm.SelectedTab = firstTab;
        Assert.Equal(first.Id, vm.NewQueryTabFor(second)!.Profile!.Id);

        // Un clic en la fila que ya estaba elegida también cuenta, y volver al editor de la pestaña lo deshace.
        vm.TreeTouched();
        Assert.Equal(second.Id, vm.NewQueryTabFor(second)!.Profile!.Id);
        vm.SelectedTab = firstTab;
        vm.TreeTouched();
        vm.TabTouched();
        Assert.Equal(first.Id, vm.NewQueryTabFor(second)!.Profile!.Id);
        vm.TreeTouched();
        Assert.Equal(first.Id, vm.NewQueryTabFor(null)!.Profile!.Id);          // una carpeta elegida: la de la pestaña
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task Sin_base_elegida_no_hay_pestanas_y_desconectar_cierra_todas_las_de_esa_base() => ui.Session.DispatchAsync(async () =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Pump();

        // Al abrir: ninguna pestaña («Consulta 1» sin base no sirve), y la indicación de cómo empezar.
        Assert.Empty(vm.Tabs);
        // El menú Cortar / Copiar / Pegar del filtro del árbol (lo trae el tema): en castellano, no en chino.
        Assert.True(Avalonia.Application.Current!.TryGetResource("STRING_MENU_PASTE", null, out var paste));
        Assert.Equal("Pegar", paste);
        Assert.True(vm.ShowStartHint);
        Assert.True(Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.StackPanel>(w, "StartHint")!.IsVisible);
        Assert.Equal("Hacé doble clic en una base del árbol para conectarte", vm.Banner);
        Assert.Null(vm.NewQueryTabFor(null));                               // Ctrl+T sin base: no abre nada y lo dice
        Assert.Empty(vm.Tabs);
        Assert.StartsWith("Elegí una base en el árbol", vm.Tree.Status);

        // Doble clic en la base: se conecta y abre su consulta.
        var tree = vm.Tree;
        var db = tree.Rows.Single(n => n.Kind == TreeNodeKind.Database);
        await tree.ActivateAsync(db);
        var query = Assert.Single(vm.Tabs);
        Assert.Equal((fb.Profile.Id, "Consulta 1"), (query.Profile!.Id, query.BaseTitle));
        Assert.False(vm.ShowStartHint);
        Pump();
        w.Editor.Text = "UPDATE TURNOS SET OBS = 'sin confirmar' WHERE ID = 33";
        await w.RunAsync();
        Assert.True(query.InTransaction);
        var monitor = vm.OpenMonitor(fb.Profile)!;
        monitor.Interval = 0;
        var view = (await vm.OpenObjectAsync(fb.Profile, new DbObject(DbObjectKind.View, "V_TURNOS")))!;
        var other = vm.CloneProfile(vm.Profiles.Single());                   // otra base: sus pestañas no se tocan
        var otherTab = vm.NewQueryTab(other);
        Assert.Equal(4, vm.Tabs.Count);
        Assert.True(vm.CanDisconnect(fb.Profile.Id));

        // Desconectar pregunta una vez por la transacción abierta; si se dice que no, no cierra nada.
        var asked = new List<string>();
        var answer = false;
        vm.ConfirmCloseAsync = m => { asked.Add(m); return Task.FromResult(answer); };
        Assert.False(await vm.DisconnectAsync(fb.Profile));
        Assert.Equal(4, vm.Tabs.Count);
        Assert.True(tree.IsConnected(fb.Profile.Id));
        Assert.Contains("«Consulta 1» (transacción abierta)", Assert.Single(asked));

        answer = true;
        Assert.True(await vm.DisconnectAsync(fb.Profile));
        Assert.Same(otherTab, Assert.Single(vm.Tabs));                       // quedó solo la de la otra base
        Assert.False(tree.IsConnected(fb.Profile.Id));
        Assert.False(vm.CanDisconnect(fb.Profile.Id));
        Assert.Equal("Desconectado de " + fb.Profile.Name + ": se cerraron sus 3 pestaña(s).", tree.Status);
        Assert.DoesNotContain(monitor, vm.Tabs);
        Assert.DoesNotContain(view, vm.Tabs);
        Pump();
        w.Editor.Text = "SELECT OBS FROM TURNOS WHERE ID = 33";              // el UPDATE se deshizo al cerrar
        await w.RunAsync();
        Assert.NotEqual("sin confirmar", otherTab.Rows[0][0] as string);

        await vm.CloseTabAsync(otherTab);                                    // cerrar la última: no se abre otra vacía
        Assert.Empty(vm.Tabs);
        Assert.Null(vm.SelectedTab);
        Assert.True(vm.ShowStartHint);
        vm.ProfileStore.Delete(other.Id);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Carpetas_propias_en_el_arbol() => ui.Session.Dispatch(() =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        ConnectionProfile P(string name, ProfileEnvironment env) =>
            new() { Name = name, Host = "srv", Database = name.ToLowerInvariant(), User = "U", Environment = env };
        var (prod, dev, other) = (P("Clinica Prod", ProfileEnvironment.Produccion), P("Clinica Test", ProfileEnvironment.Desarrollo), P("Sucursal", ProfileEnvironment.Desarrollo));
        foreach (var p in new[] { prod, dev, other }) store.Upsert(p, "x");
        var vm = new MainWindowViewModel(store);
        string[] Folders() => vm.Tree.Rows.Where(n => n.Kind == TreeNodeKind.Folder).Select(n => n.Text).ToArray();
        Assert.Equal(["Producción", "Desarrollo"], Folders());

        vm.MoveToFolder(dev, "Clinica");
        vm.MoveToFolder(prod, "  CLINICA ");                                 // la misma carpeta, con su grafía
        Assert.Equal("Clinica Prod está ahora en la carpeta «Clinica».", vm.Tree.Status);
        Assert.Equal(["Desarrollo", "Clinica"], Folders());                  // los ambientes primero, las propias después
        var clinica = vm.Tree.Rows.Single(n => n.Text == "Clinica");
        Assert.True(clinica.IsCustomFolder);
        Assert.False(vm.Tree.Rows.Single(n => n.Text == "Desarrollo").IsCustomFolder);
        Assert.Equal(["Clinica Prod", "Clinica Test"], clinica.Children!.Select(n => n.Text));
        Assert.Equal(["Clinica"], vm.Folders);
        Assert.Equal("x", store.GetPassword(prod.Id));                       // mover no toca la contraseña

        vm.RenameFolder("Clinica", "Clinica S.A.");
        Assert.Equal(["Desarrollo", "Clinica S.A."], Folders());
        vm.MoveToFolder(dev, "");                                            // vacía: vuelve a su ambiente
        Assert.Equal("Clinica Test volvió al grupo «Desarrollo».", vm.Tree.Status);
        vm.RenameFolder("Clinica S.A.", null);                               // sin nombre: la carpeta desaparece
        Assert.Equal(["Producción", "Desarrollo"], Folders());

        // Carpetas dentro de carpetas: primero las carpetas, después las bases, cada nivel con su sangría.
        vm.RenameFolder("Clinica S.A.", null);
        vm.MoveToFolder(prod, "Clinica/Producción");
        vm.MoveToFolder(dev, "clinica");
        var rows = vm.Tree.Rows.Select(n => (n.Level, n.Text)).ToArray();
        Assert.Equal([(0, "Desarrollo"), (1, "Sucursal"), (0, "Clinica"), (1, "Producción"), (2, "Clinica Prod"), (1, "Clinica Test")], rows);
        Assert.Equal(["Clinica", "Clinica/Producción"], vm.Folders);

        // Arrastrar: una base a una carpeta, una carpeta adentro de otra o a la raíz; no adentro de sí misma.
        TreeNode Node(string text) => vm.Tree.Rows.Single(n => n.Text == text);
        Assert.True(vm.Drop(Node("Sucursal"), Node("Producción")));
        Assert.Equal("Clinica/Producción", store.Load().Single(p => p.Id == other.Id).Folder);
        Assert.False(vm.CanDrop(Node("Clinica"), Node("Producción")));                // adentro de sí misma
        Assert.False(vm.Drop(Node("Clinica"), Node("Producción")));
        Assert.Equal("La carpeta «Clinica» no puede ir adentro de sí misma.", vm.Tree.Status);
        Assert.True(vm.Drop(Node("Producción"), null));                               // a la raíz, con todo lo que tiene
        Assert.Equal(["Clinica", "Producción"], vm.Folders);
        Assert.Equal("Producción", store.Load().Single(p => p.Id == prod.Id).Folder);
        Assert.True(vm.Drop(Node("Producción"), Node("Clinica Test")));               // sobre una base: a su carpeta
        Assert.Equal("Clinica/Producción", store.Load().Single(p => p.Id == prod.Id).Folder);
        // Soltar una base sobre el grupo de otro ambiente no la cambia de ambiente.
        Assert.True(vm.Drop(Node("Sucursal"), null));                                    // sin carpeta: a su grupo (Desarrollo)
        Assert.False(vm.CanDrop(Node("Clinica Prod"), Node("Desarrollo")));
        Assert.False(vm.Drop(Node("Clinica Prod"), Node("Desarrollo")));
        Assert.Contains("va al grupo de su entorno (Producción)", vm.Tree.Status);
        Assert.True(vm.CanDrop(Node("Clinica Test"), Node("Sucursal")));                 // otra base de Desarrollo sin carpeta
        vm.RenameFolder("Clinica", null);
        Assert.Equal(["Producción", "Desarrollo"], Folders());

        // «Nueva carpeta…»: nombre y bases marcadas, en un paso.
        vm.CreateFolder(" Clinica ", [prod.Id, dev.Id]);
        Assert.Equal("Carpeta «Clinica» con 2 base(s).", vm.Tree.Status);
        Assert.Equal(["Desarrollo", "Clinica"], Folders());
        vm.CreateFolder("", [other.Id]);                                     // sin nombre no hace nada
        vm.CreateFolder("Otra", []);                                         // sin bases tampoco
        Assert.Equal(["Desarrollo", "Clinica"], Folders());
    }, CancellationToken.None);

    [Fact]
    public Task Al_cerrar_la_ventana_espera_a_que_se_cierren_las_conexiones() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT CURRENT_CONNECTION FROM RDB$DATABASE";
        await w.RunAsync();
        var attachment = Convert.ToInt64(vm.SelectedTab!.Rows[0][0]);
        Assert.Equal(1, await AttachmentsAsync(attachment));
        w.Close();
        await WaitFor(() => !w.IsVisible);
        Assert.Equal(0, await AttachmentsAsync(attachment));                   // la conexión ya no está en el servidor
    }, CancellationToken.None);

    private async Task<int> AttachmentsAsync(long id)
    {
        await using var c = FbStudio.Firebird.FbConnectionFactory.Create(fb.Profile, fb.Password, "test-cierre");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FirebirdSql.Data.FirebirdClient.FbCommand($"SELECT COUNT(*) FROM MON$ATTACHMENTS WHERE MON$ATTACHMENT_ID = {id}", c, tx);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        await tx.CommitAsync();
        return count;
    }

    [Fact]
    public Task Pestana_nueva_usa_la_base_de_la_actual_y_se_recorren_con_Ctrl_Tab() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var first = vm.SelectedTab!;
        w.KeyPress(Avalonia.Input.Key.T, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.T, null);
        Pump();
        Assert.Equal(2, vm.Tabs.Count);
        var second = vm.SelectedTab!;
        Assert.Equal(first.Profile!.Id, second.Profile!.Id);
        Assert.Equal("", w.Editor.Text);

        vm.SelectNextTab(1);
        Assert.Same(first, vm.SelectedTab);
        w.KeyPress(Avalonia.Input.Key.W, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.W, null);
        await WaitFor(() => vm.Tabs.Count == 1);
        Assert.Same(second, vm.SelectedTab);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Doble_clic_en_un_objeto_abre_su_DDL_en_una_pestana() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tree = vm.Tree;
        await tree.ActivateAsync(tree.Rows.Single(n => n.Kind == TreeNodeKind.Database));
        await tree.ToggleAsync(tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.Procedure));
        var proc = tree.Rows.Single(n => n.Kind == TreeNodeKind.Object && n.Text == "P_TURNOS_DESDE");

        await tree.ActivateAsync(proc); // doble clic
        await WaitFor(() => vm.SelectedTab!.IsObjectTab);
        var tab = vm.SelectedTab!;
        Assert.Equal(2, vm.Tabs.Count);
        Assert.True(tab.IsCompilable);
        Assert.Equal("Procedimiento P_TURNOS_DESDE", tab.ObjectCaption);
        Assert.Equal("Turnos entre dos ID", tab.Description);
        Assert.StartsWith("CREATE OR ALTER PROCEDURE P_TURNOS_DESDE (", w.Editor.Text);
        Assert.False(tab.IsModified);

        w.Editor.Document.Insert(w.Editor.Document.TextLength, "\n");
        Assert.True(tab.IsModified);
        Assert.Contains("P_TURNOS_DESDE *", tab.Title);

        // Otro doble clic no abre otra pestaña: elige la que ya está.
        vm.SelectedTab = vm.Tabs[0];
        await tree.ActivateAsync(proc);
        await WaitFor(() => vm.SelectedTab == tab);
        Assert.Equal(2, vm.Tabs.Count);

        // Recargar con cambios sin compilar pregunta antes de descartarlos.
        var asked = 0;
        vm.ConfirmDiscardAsync = _ => { asked++; return Task.FromResult(true); };
        await tab.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(1, asked);
        Assert.False(tab.IsModified);

        // Una tabla se muestra como script (no se compila desde su pestaña).
        await tree.ToggleAsync(tree.Rows.Single(n => n.Kind == TreeNodeKind.Category && n.Category == DbObjectKind.Table));
        await tree.ActivateAsync(tree.Rows.Single(n => n.Kind == TreeNodeKind.Object && n.Text == "TURNOS"));
        await WaitFor(() => vm.SelectedTab!.IsObjectTab && vm.SelectedTab.BaseTitle == "TURNOS");
        Assert.False(vm.SelectedTab!.IsCompilable);
        Assert.StartsWith("CREATE TABLE TURNOS (", w.Editor.Text);

        // Desde la pestaña de una tabla no se ejecuta nada (correr su DDL intentaría crearla de nuevo, con sus claves,
        // índices, triggers y permisos): F9, F5, Ctrl+Enter, Ctrl+F9 y el plan se niegan, y el DDL no se edita.
        var table = vm.SelectedTab;
        Assert.True(table.IsReadOnlyObject);
        Assert.True(w.Editor.IsReadOnly);
        w.Editor.TextArea.Focus();
        Pump();
        foreach (var (key, modifiers, physical) in new[]
        {
            (Avalonia.Input.Key.F9, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F9),
            (Avalonia.Input.Key.F5, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F5),
            (Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Enter),
            (Avalonia.Input.Key.F9, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.F9),
            (Avalonia.Input.Key.E, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.E),
        })
        {
            table.Status = "";
            w.KeyPress(key, modifiers, physical, null);
            await WaitFor(() => !table.IsRunning);
            Pump();
            Assert.StartsWith("No se ejecuta: la pestaña de TURNOS muestra su DDL", table.Status);
            Assert.False(table.InTransaction);
        }
        Assert.Null(await table.RunAsync("DROP TABLE TURNOS"));                       // ni desde el código
        Assert.StartsWith("CREATE TABLE TURNOS (", w.Editor.Text);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Compilar_un_procedimiento_marca_el_error_y_despues_confirma() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = (await vm.OpenObjectAsync(vm.SelectedProfile!, new DbObject(DbObjectKind.Procedure, "P_CONTAR")))!;
        Pump();
        var original = w.Editor.Text;
        Assert.Equal(5, original.ReplaceLineEndings("\n").Split('\n').Length); // CREATE…, RETURNS (, N INTEGER), AS, BEGIN … END

        // Con un error: no se aplica, se deshace y el cursor va a la línea que informa Firebird.
        w.Editor.Text = original.Replace("COUNT(*)", "COUNT(NO_EXISTE)");
        await w.RunAsync(); // F9 sin selección en la pestaña de un procedimiento = compilar
        Assert.StartsWith("No compiló (línea 5", tab.Status);
        Assert.Contains("NO_EXISTE", tab.Messages);
        Assert.Equal(5, w.Editor.TextArea.Caret.Line);
        Assert.False(tab.InTransaction);
        Assert.True(tab.IsModified);

        // Corregido: compila, confirma y queda como "guardado".
        w.Editor.Text = original.Replace("COUNT(*)", "COUNT(*) /* compilado desde FbStudio */");
        Assert.True(await tab.CompileAsync());
        Assert.False(tab.InTransaction);
        Assert.False(tab.IsModified);
        Assert.Contains("compilado y confirmado", tab.Status);

        await tab.ReloadCommand.ExecuteAsync(null);
        Assert.Contains("/* compilado desde FbStudio */", w.Editor.Text);

        // Desde otra pestaña (otra conexión) se ve el cambio y el procedimiento sigue andando.
        var query = vm.NewQueryTab();
        Pump();
        w.Editor.Text = "SELECT N FROM P_CONTAR";
        await w.RunAsync();
        Assert.Equal(FirebirdFixture.Rows, Convert.ToInt32(query.Rows[0][0]));
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Compilar_en_un_perfil_de_solo_lectura_se_bloquea() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open(p => p with { ReadOnly = true });
        var tab = (await vm.OpenObjectAsync(vm.SelectedProfile!, new DbObject(DbObjectKind.Trigger, "TURNOS_BI")))!;
        Pump();
        Assert.True(tab.IsCompilable);
        Assert.False(tab.CompileCommand.CanExecute(null)); // el botón queda deshabilitado
        Assert.False(await tab.CompileAsync());           // y por código igual se bloquea
        Assert.StartsWith("Bloqueado", tab.Status);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Dependencias_de_un_objeto_y_doble_clic_para_abrirlas() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var origin = vm.SelectedTab!;
        var originStatus = origin.Status;
        var tab = (await vm.OpenObjectAsync(vm.SelectedProfile!, new DbObject(DbObjectKind.Table, "TURNOS")))!;
        Assert.Equal(originStatus, origin.Status); // la pestaña de origen no queda con "Leyendo el DDL…"
        await WaitFor(() => tab.UsedBy.Count > 0);
        Assert.Contains(tab.UsedBy, r => r.Caption == "Vista V_TURNOS");
        Assert.Contains(tab.UsedBy, r => r.Caption == "Tabla TURNOS_DET" && r.Detail == "FK_DET_TURNO");
        Assert.StartsWith("Es usado por (", tab.UsedByHeader);
        // Agrupado por tipo, con su cantidad, y con filtro.
        Assert.Contains(tab.UsedByItems, i => i is { IsHeader: true, Caption: "Procedimientos (2)" });
        Assert.Contains(tab.UsedByItems, i => i is { IsHeader: false, Caption: "P_CONTAR" });
        tab.DependencyFilter = "contar";
        Assert.Equal(["Procedimientos (1)", "P_CONTAR"], tab.UsedByItems.Select(i => i.Caption));
        Assert.Matches(@"^Es usado por \(1 de \d+\)$", tab.UsedByHeader);
        tab.DependencyFilter = "";

        tab.SelectedResultTab = EditorTabViewModel.DependenciesTab;
        Pump();
        await tab.OpenDependencyAsync(tab.UsedBy.Single(r => r.Caption == "Procedimiento P_TURNOS_DESDE"));
        var proc = vm.SelectedTab!;
        Assert.Equal("P_TURNOS_DESDE", proc.Object!.Name);
        await WaitFor(() => proc.Uses.Count > 0);
        Assert.Equal("Tabla TURNOS", Assert.Single(proc.Uses).Caption);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Buscar_en_metadatos_y_abrir_el_resultado_en_la_linea() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.KeyPress(Avalonia.Input.Key.F, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Shift, Avalonia.Input.PhysicalKey.F, null);
        Pump();
        var search = Assert.IsType<SearchTabViewModel>(vm.SelectedTab);
        Assert.Equal(fb.Profile.Id, search.Profile!.Id);

        search.SearchText = "señal";
        await w.RunAsync(); // F9 en la pestaña de búsqueda = buscar
        var hit = Assert.Single(search.Hits);
        Assert.Equal(("Procedimiento P_TURNOS_DESDE", "línea 10"), (hit.Caption, hit.Where));
        // La lista agrupa por tipo y por objeto, con el texto buscado aparte para resaltarlo.
        Assert.Equal(2, search.Items.Count);
        Assert.Equal(("Procedimientos", "1 objeto · 1 coincidencia"), (search.Items[0].Caption, search.Items[0].Count));
        Assert.Equal(("P_TURNOS_DESDE", "línea 10", "señal"), (search.Items[1].Caption, search.Items[1].Where, search.Items[1].Match));
        Assert.Equal("Buscar «señal»", search.BaseTitle);

        await search.OpenHitAsync(hit);
        Pump();
        var tab = vm.SelectedTab!;
        Assert.Equal("P_TURNOS_DESDE", tab.Object!.Name);
        var caretLine = w.Editor.Document.GetLineByNumber(w.Editor.TextArea.Caret.Line);
        Assert.Contains("señal", w.Editor.Document.GetText(caretLine));
        Assert.Equal(0, w.Editor.SelectionLength); // sin selección: F9 sigue compilando

        // Una columna se ubica por su nombre en el DDL de la tabla.
        vm.SelectedTab = search;
        search.SearchText = "OBS";
        search.InSources = search.InNames = search.InDescriptions = false;
        await search.SearchAsync();
        await search.OpenHitAsync(Assert.Single(search.Hits));
        Pump();
        Assert.StartsWith("OBS", w.Editor.Document.GetText(w.Editor.Document.GetLineByNumber(w.Editor.TextArea.Caret.Line)).Trim());
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Exporta_a_Excel_todo_en_streaming_y_como_INSERT() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        vm.RowLimit = 1_000;
        w.Editor.Text = "SELECT ID, FECHA, PACIENTE, IMPORTE FROM TURNOS ORDER BY ID";
        await w.RunAsync();
        var tab = vm.SelectedTab!;
        Assert.True(tab.CanExportFull);

        var xlsx = Path.Combine(_folder, "todo.xlsx");
        await tab.ExportAsync(xlsx, full: true, new FbStudio.Core.Export.XlsxExporter());
        Assert.StartsWith($"Excel exportado: {FirebirdFixture.Rows:N0} filas", tab.Status);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(xlsx))
        {
            var sheet = System.Xml.Linq.XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            System.Xml.Linq.XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.Equal(FirebirdFixture.Rows + 1, sheet.Descendants(s + "row").Count());
        }
        Assert.Equal(1_000, tab.Rows.Count); // la grilla no se tocó

        Assert.Equal("TURNOS", FbStudio.Core.Sql.StatementClassifier.MainTable(tab.LastSql));
        var sql = Path.Combine(_folder, "cargado.sql");
        await tab.ExportAsync(sql, full: false, new FbStudio.Core.Export.InsertScriptExporter("TURNOS"));
        var inserts = File.ReadAllLines(sql).Count(l => l.StartsWith("INSERT INTO TURNOS (ID, FECHA, PACIENTE, IMPORTE) VALUES ("));
        Assert.Equal(1_000, inserts);
        Assert.Contains("Script INSERT exportado: 1.000 filas", tab.Status);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Autocompleta_objetos_sin_haber_ejecutado_y_acepta_con_Enter_o_Tab() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.NewQueryTab();
        Pump();
        _ = tab.Catalog; // pedirlo arranca la carga en segundo plano (no hace falta ejecutar nada antes)
        await WaitFor(() => !tab.Catalog.IsEmpty);
        Assert.Contains("TURNOS_DET", tab.Catalog.Tables);
        Assert.Contains("P_TURNOS_DESDE", tab.Catalog.Procedures);
        Assert.Same(tab.Catalog, vm.Tabs[0].Catalog); // compartido entre las pestañas de la misma base

        async Task Complete(string text, int caret, Avalonia.Input.Key accept, Avalonia.Input.PhysicalKey physical)
        {
            w.Editor.Text = text;
            w.Editor.CaretOffset = caret;
            w.Editor.TextArea.Focus();
            Pump();
            w.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Space, null);
            Pump();
            w.KeyPress(accept, Avalonia.Input.RawInputModifiers.None, physical, null);
            Pump();
            await Task.Yield();
        }

        await Complete("SELECT * FROM turnos_d", 22, Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
        Assert.Equal("SELECT * FROM TURNOS_DET", w.Editor.Text);

        await Complete("SELECT t.FEC FROM TURNOS t", 12, Avalonia.Input.Key.Tab, Avalonia.Input.PhysicalKey.Tab);
        Assert.Equal("SELECT t.FECHA FROM TURNOS t", w.Editor.Text);

        await Complete("SELECT PACI FROM TURNOS", 11, Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
        Assert.Equal("SELECT PACIENTE FROM TURNOS", w.Editor.Text); // columna del FROM sin alias
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Cerrar_varias_pestanas_pregunta_una_vez_por_las_que_tienen_trabajo_pendiente() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var first = vm.SelectedTab!;
        var withTx = vm.NewQueryTab();
        Pump();
        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 25";
        await w.RunAsync();
        Assert.True(withTx.InTransaction);
        Assert.Equal("● " + withTx.TitleText, withTx.Title); // el ● va aparte (en rojo) en la pestaña
        var third = vm.NewQueryTab();
        Pump();

        var asked = new List<string>();
        var answer = false;
        vm.ConfirmCloseAsync = m => { asked.Add(m); return Task.FromResult(answer); };

        await vm.CloseTabsAsync(vm.Tabs.Where(t => t != first).ToList()); // «cerrar todas menos esta», y dice que no
        Assert.Single(asked);
        Assert.Contains("«Consulta 2» (transacción abierta)", asked[0]);
        Assert.Equal(3, vm.Tabs.Count);

        answer = true;
        await vm.CloseTabsAsync(vm.Tabs.Where(t => t != first).ToList());
        Assert.Equal(2, asked.Count);
        Assert.Same(first, Assert.Single(vm.Tabs));
        Assert.Same(first, vm.SelectedTab);

        await vm.CloseTabsAsync(vm.Tabs.ToList());                  // «cerrar todas»: no queda ninguna
        Assert.Empty(vm.Tabs);
        Assert.Equal(2, asked.Count);                               // sin trabajo pendiente no pregunta
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Una_pestana_de_objeto_arranca_en_Dependencias() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = (await vm.OpenObjectAsync(vm.SelectedProfile!, new DbObject(DbObjectKind.View, "V_TURNOS")))!;
        Assert.Equal(EditorTabViewModel.DependenciesTab, tab.SelectedResultTab);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Avisa_si_el_plan_recorre_una_tabla_entera() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        w.Editor.Text = "SELECT ID FROM TURNOS WHERE OBS CONTAINING 'xyz'";
        await w.RunAsync();
        Assert.Equal("⚠ lectura completa de TURNOS (sin índice): puede tardar", tab.PlanWarning);
        Assert.Contains("Full Scan", tab.Plan);

        w.Editor.Text = "SELECT ID FROM TURNOS WHERE FECHA >= '2026-01-30'";
        await w.RunAsync();
        Assert.Null(tab.PlanWarning);                               // por el índice TURNOS_FECHA
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Un_bloqueo_dice_que_pestana_tiene_el_registro() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var holder = vm.SelectedTab!;
        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 26";
        await w.RunAsync();
        Assert.True(holder.InTransaction);

        var waiting = vm.NewQueryTab();
        Pump();
        w.Editor.Text = "UPDATE TURNOS SET OBS = OBS WHERE ID = 26";
        await w.RunAsync();                                         // espera hasta 10 s y falla por el bloqueo
        Assert.StartsWith("Bloqueado: el registro lo tiene tomado la pestaña «Consulta 1»", waiting.Status);
        Assert.False(waiting.InTransaction);

        await holder.RollbackCommand.ExecuteAsync(null);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Grilla_copia_filas_seleccionadas_todas_o_una_columna_para_Excel() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        w.Editor.Text = "SELECT ID, IMPORTE, OBS FROM TURNOS WHERE ID < 5 ORDER BY ID";
        await w.RunAsync();
        var g = w.ResultsGrid;
        var tab = vm.SelectedTab!;

        g.SelectAllRows();
        Assert.Equal([0, 1, 2, 3, 4], g.SelectedRowIndexes());
        var lines = g.ClipboardText(g.SelectedRowIndexes()).TrimEnd().Split(Environment.NewLine);
        Assert.Equal(6, lines.Length);
        Assert.Equal("ID\tIMPORTE\tOBS", lines[0]);
        var importe = Convert.ToDecimal(tab.Rows[1][1]).ToString(System.Globalization.CultureInfo.CurrentCulture);
        Assert.StartsWith($"1\t{importe}\t", lines[2]);              // número con la coma del sistema, para Excel

        g.SelectRows(3, 1);                                        // rango (como Shift+clic)
        Assert.Equal([1, 2, 3], g.SelectedRowIndexes());

        g.SetFilter(0, ">2");                                      // «todas las filas» respeta el filtro
        await g.PendingBuild;
        Assert.Equal([3, 4], g.VisibleRowIndexes());
        Assert.Equal($"3{Environment.NewLine}4", g.ColumnClipboardText(0));
        w.Close();
    }, CancellationToken.None);

    private static ConnectionProfile AddProfile(MainWindowViewModel vm, string name)
    {
        var current = vm.SelectedProfile!;
        var other = current with { Id = Guid.NewGuid(), Name = name };
        vm.ProfileStore.Upsert(other, vm.ProfileStore.GetPassword(current.Id));
        vm.ReloadProfiles();
        return other;
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

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
