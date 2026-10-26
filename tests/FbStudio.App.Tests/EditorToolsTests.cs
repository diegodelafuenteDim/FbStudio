using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Sql;
using FbStudio.Firebird.Tests;
using FbStudio.Workspace;

namespace FbStudio.App.Tests;

/// <summary>Formatear SQL, plantillas y abrir el objeto con Ctrl+clic o F12 (F-32), contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class EditorToolsTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-editortools-" + Guid.NewGuid().ToString("N"));

    private (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
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

    [Fact]
    public Task Formatea_la_seleccion_o_todo_y_se_deshace() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var view = w.CurrentView;
        const string Original = "create table x (id integer);\nselect t.id, count(*) from turnos t where t.id between 1 and 3 group by t.id;";
        view.Editor.Text = Original;
        view.FormatSql();
        Assert.Equal("create table x (id integer);\nSELECT t.id, COUNT(*)\nFROM turnos t\nWHERE t.id BETWEEN 1 AND 3\nGROUP BY t.id;",
            view.Editor.Text.ReplaceLineEndings("\n"));
        Assert.Equal("SQL formateado (Ctrl+Z lo deshace).", vm.SelectedTab!.Status);
        view.Editor.Undo();
        Assert.Equal(Original, view.Editor.Text);

        // Solo la selección; si no hay consultas, lo dice.
        view.Editor.Text = "select 1 from rdb$database;\nselect 2 from rdb$database;";
        view.Editor.Select(0, "select 1 from rdb$database".Length);
        view.FormatSql();
        Assert.Equal("SELECT 1\nFROM rdb$database;\nselect 2 from rdb$database;", view.Editor.Text.ReplaceLineEndings("\n"));
        view.Editor.Text = "create table y (id integer)";
        view.FormatSql();
        Assert.StartsWith("No hay nada para formatear", vm.SelectedTab.Status);

        // Sin mayúsculas, según Opciones.
        vm.SaveSettings(vm.Options with { FormatUppercaseKeywords = false });
        view.Editor.Text = "select a from t where b = 1";
        view.FormatSql();
        Assert.Equal("select a\nfrom t\nwhere b = 1", view.Editor.Text.ReplaceLineEndings("\n"));
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Tab_despues_de_una_abreviatura_pone_la_plantilla() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var view = w.CurrentView;
        view.Editor.Text = "  selw";
        view.Editor.CaretOffset = view.Editor.Text.Length;
        Assert.True(view.ExpandTemplateAtCaret());
        var text = view.Editor.Text.ReplaceLineEndings("\n");
        Assert.Equal("  SELECT *\n  FROM \n  WHERE ", text);
        Assert.Equal("  SELECT *\n  FROM ".Length, view.Editor.Text[..view.Editor.CaretOffset].ReplaceLineEndings("\n").Length);

        // Una palabra que no es abreviatura: Tab hace lo de siempre.
        view.Editor.Text = "turnos";
        view.Editor.CaretOffset = 6;
        Assert.False(view.ExpandTemplateAtCaret());

        // Las propias, desde Opciones.
        vm.SaveSettings(vm.Options with { Templates = [new SqlTemplate("hoy", "Turnos de hoy", "SELECT * FROM TURNOS WHERE FECHA = CURRENT_DATE${cursor}")] });
        view.Editor.Text = "hoy";
        view.Editor.CaretOffset = 3;
        Assert.True(view.ExpandTemplateAtCaret());
        Assert.Equal("SELECT * FROM TURNOS WHERE FECHA = CURRENT_DATE", view.Editor.Text);
        view.Editor.Text = "sel";
        view.Editor.CaretOffset = 3;
        Assert.False(view.ExpandTemplateAtCaret());                         // las de fábrica ya no están
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Ctrl_clic_o_F12_abre_el_objeto() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var query = vm.SelectedTab!;
        const string Sql = "SELECT t.ID, NOEXISTE FROM TURNOS t\nJOIN TURNOS_DET d ON d.TURNO = t.ID;\nEXECUTE PROCEDURE P_CONTAR;";

        // El alias lleva a su tabla.
        Assert.True(await query.GoToDefinitionAsync(Sql, Sql.IndexOf("t.ID", StringComparison.Ordinal) + 2));
        Pump();
        Assert.Equal(("TURNOS", true), (vm.SelectedTab!.BaseTitle, vm.SelectedTab.IsObjectTab));
        Assert.True(await query.GoToDefinitionAsync(Sql, Sql.IndexOf("d.TURNO", StringComparison.Ordinal)));
        Assert.Equal("TURNOS_DET", vm.SelectedTab!.BaseTitle);
        Assert.True(await query.GoToDefinitionAsync(Sql, Sql.IndexOf("P_CONTAR", StringComparison.Ordinal) + 3));
        Assert.Equal("P_CONTAR", vm.SelectedTab!.BaseTitle);

        // Lo que no existe lo dice; fuera de un nombre, también.
        Assert.False(await query.GoToDefinitionAsync(Sql, Sql.IndexOf("NOEXISTE", StringComparison.Ordinal)));
        Assert.Equal($"No hay ningún objeto llamado NOEXISTE en {fb.Profile.Name}.", query.Status);
        Assert.False(await query.GoToDefinitionAsync(Sql, Sql.IndexOf(';') + 1));   // entre sentencias
        Assert.StartsWith("Poné el cursor", query.Status);

        // F12 en el editor: donde está el cursor.
        vm.SelectedTab = query;
        Pump();
        var view = w.CurrentView;
        view.Editor.Text = "SELECT * FROM V_TURNOS";
        view.Editor.CaretOffset = view.Editor.Text.Length;
        Assert.True(await view.GoToDefinitionAtCaretAsync());
        Assert.Equal("V_TURNOS", vm.SelectedTab!.BaseTitle);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task La_ayuda_de_parametros_sigue_al_cursor() => ui.Session.DispatchAsync(async () =>
    {
        var (w, vm) = Open();
        var tab = vm.SelectedTab!;
        _ = tab.Catalog;                                                    // empieza a cargarse
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (tab.Catalog.Signatures.Count == 0 && sw.ElapsedMilliseconds < 10_000) { await Task.Delay(20); Pump(); }
        var view = w.CurrentView;
        view.Editor.Text = "SELECT * FROM P_TURNOS_DESDE";
        view.Editor.CaretOffset = view.Editor.Text.Length;
        view.Editor.TextArea.PerformTextInput("(");
        Pump();
        var help = view.EditorSetup!.CurrentSignature!;
        Assert.Equal(("P_TURNOS_DESDE(DESDE INTEGER, HASTA INTEGER = …)", 0), (help.Signature.ToString(), help.Argument));

        view.Editor.TextArea.PerformTextInput("1, ");
        Pump();
        Assert.Equal(1, view.EditorSetup.CurrentSignature!.Argument);       // la coma pasa al segundo

        view.Editor.TextArea.PerformTextInput("5)");
        Pump();
        Assert.Null(view.EditorSetup.CurrentSignature);                     // al cerrar el paréntesis se va
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public void Las_plantillas_de_Opciones_se_guardan_solo_si_cambian()
    {
        var options = new OptionsViewModel(new AppSettings());
        Assert.Equal(SqlTemplates.Defaults.Count, options.Templates.Count);
        Assert.Null(options.ToSettings().Templates);
        // Lo que la ventana no muestra no se pierde al aceptar.
        var kept = new ViewModels.OptionsViewModel(new AppSettings { RecentFiles = [@"C:\x\a.sql"], DataCountOnFilter = true, DataScriptWithCommit = true, LastSeenVersion = "0.2.0" })
            .ToSettings();
        Assert.Equal([@"C:\x\a.sql"], kept.RecentFiles!);
        Assert.Equal((true, true, "0.2.0"), (kept.DataCountOnFilter, kept.DataScriptWithCommit, kept.LastSeenVersion));

        options.AddTemplateCommand.Execute(null);
        options.SelectedTemplate!.Abbreviation = "hoy";
        options.SelectedTemplate.Name = "Turnos de hoy";
        options.SelectedTemplate.Text = "SELECT * FROM TURNOS WHERE FECHA = CURRENT_DATE";
        var saved = options.ToSettings();
        Assert.Equal(SqlTemplates.Defaults.Count + 1, saved.Templates!.Count);
        Assert.Equal("hoy", saved.EffectiveTemplates[^1].Abbreviation);

        options.DeleteTemplateCommand.Execute(null);
        Assert.Null(options.ToSettings().Templates);
        options.Templates.Clear();
        Assert.Empty(options.ToSettings().EffectiveTemplates);              // sin ninguna: vacía, no las de fábrica
        options.DefaultTemplatesCommand.Execute(null);
        Assert.Null(options.ToSettings().Templates);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
