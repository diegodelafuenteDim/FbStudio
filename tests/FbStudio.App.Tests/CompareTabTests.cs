using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Core.Sql;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.App.Tests;

/// <summary>Pestaña «Comparar bases» contra Firebird 3 en Docker (el destino es una copia cambiada a propósito).</summary>
[Collection(nameof(UiCollection))]
public sealed class CompareTabTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-compare-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Compara_arma_el_script_con_lo_tildado_y_lo_lleva_al_destino() => ui.Session.DispatchAsync(async () =>
    {
        var copy = await CopySchemaAsync("/firebird/data/copia_ui_comparar.fdb", "DROP INDEX TURNOS_FECHA; COMMIT; SET TERM ^ ; CREATE OR ALTER PROCEDURE P_CONTAR RETURNS (N INTEGER) AS BEGIN N = 1; SUSPEND; END^ SET TERM ; ^ COMMIT;");
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        store.Upsert(copy, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Pump();

        var tab = vm.OpenCompare(fb.Profile, copy, new SchemaCompareOptions { Privileges = false });
        await tab.Comparing;
        Pump();
        Assert.Equal((EditorTabKind.Compare, copy.Id), (tab.Kind, tab.Profile!.Id));   // la pestaña es del destino
        Assert.Equal("2 diferencias: 1 nuevas, 1 distintas, 0 sobran", tab.Summary);
        var index = Assert.Single(tab.Items, i => i.Name == "TURNOS_FECHA");
        var procedure = Assert.Single(tab.Items, i => i.Name == "P_CONTAR");
        Assert.Equal(("Índice", "nuevo", true), (index.KindLabel, index.ChangeLabel, index.IsChecked));
        Assert.Equal("distinto", procedure.ChangeLabel);
        tab.SelectedItem = procedure;
        Assert.Contains("N = 1", tab.SelectedTargetDdl);
        Assert.Contains("SELECT COUNT(*)", tab.SelectedSourceDdl);
        // Los renglones que cambian se marcan en los dos lados (solo esos).
        Pump();
        var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<CompareTabView>().Single();
        var sourceLines = tab.SelectedSourceDdl.Replace("\r\n", "\n").Split('\n');
        var targetLines = tab.SelectedTargetDdl.Replace("\r\n", "\n").Split('\n');
        Assert.NotEmpty(view.SourceChangedLines);
        Assert.Contains(view.SourceChangedLines, l => sourceLines[l - 1].Contains("COUNT(*)"));
        Assert.Contains(view.TargetChangedLines, l => targetLines[l - 1].Contains("N = 1"));
        Assert.True(view.SourceChangedLines.Count < sourceLines.Length);                // el encabezado igual no se marca
        Assert.DoesNotContain(view.SourceChangedLines, l => sourceLines[l - 1].StartsWith("CREATE OR ALTER PROCEDURE"));

        // Solo el índice: el procedimiento se destilda.
        procedure.IsChecked = false;
        tab.BuildScript();
        Assert.Equal(1, tab.SelectedDetail);
        Assert.Contains("CREATE INDEX TURNOS_FECHA ON TURNOS (FECHA);", tab.Script);
        Assert.DoesNotContain("P_CONTAR", tab.Script);
        Assert.StartsWith("/* Script para que docker-fb3-copia quede igual a docker-fb3", tab.Script);

        // A una pestaña de consulta del destino, y F9: después solo queda la diferencia del procedimiento.
        tab.OpenInTargetCommand.Execute(null);
        var query = vm.SelectedTab!;
        Assert.Equal(copy.Id, query.Profile!.Id);
        Pump();
        await w.RunAsync();
        Assert.StartsWith("Script: ", query.Status);
        Assert.False(query.InTransaction);
        vm.SelectedTab = tab;
        await tab.CompareAsync();
        Assert.Equal("P_CONTAR", Assert.Single(tab.Items).Name);
        w.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Comparar_datos_de_una_tabla_entre_dos_bases_y_llevar_el_script_al_destino() => ui.Session.DispatchAsync(async () =>
    {
        // Dos copias con la misma tabla y datos distintos: en el origen la 3 es nueva y la 2 cuesta 20; en el destino sobra la 4.
        const string table = "CREATE TABLE PRECIOS (ID INTEGER NOT NULL PRIMARY KEY, NOMBRE VARCHAR(30), IMPORTE NUMERIC(10,2)";
        var source = (await CopySchemaAsync("/firebird/data/copia_datos_a.fdb", table + ", SOLO_A INTEGER); COMMIT; "
            + "INSERT INTO PRECIOS (ID, NOMBRE, IMPORTE) VALUES (1, 'Ecografía', 10); INSERT INTO PRECIOS (ID, NOMBRE, IMPORTE) VALUES (2, 'Rayos', 20); "
            + "INSERT INTO PRECIOS (ID, NOMBRE, IMPORTE) VALUES (3, 'Nueva', NULL); COMMIT;")) with { Name = "copia-a" };
        var target = (await CopySchemaAsync("/firebird/data/copia_datos_b.fdb", table + "); COMMIT; "
            + "INSERT INTO PRECIOS (ID, NOMBRE, IMPORTE) VALUES (1, 'Ecografía', 10.00); INSERT INTO PRECIOS (ID, NOMBRE, IMPORTE) VALUES (2, 'Rayos', 25); "
            + "INSERT INTO PRECIOS (ID, NOMBRE, IMPORTE) VALUES (4, 'Sobra', 1); COMMIT;")) with { Name = "copia-b" };
        var store = AppServices.CreateProfileStore(_folder);
        store.Upsert(fb.Profile, fb.Password);
        store.Upsert(source, fb.Password);
        store.Upsert(target, fb.Password);
        var vm = new MainWindowViewModel(store);
        var w = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        w.Show();
        Pump();

        var tab = vm.OpenDataCompare(source, "PRECIOS");
        Assert.Same(tab, vm.OpenDataCompare(source, "PRECIOS"));                            // una por tabla y base
        Assert.Equal(["copia-b", "docker-fb3"], tab.Targets.Select(p => p.Name));
        tab.Target = tab.Targets.Single(p => p.Id == target.Id);
        await tab.CompareCommand.ExecuteAsync(null);
        Pump();
        Assert.Equal("1 faltan en el destino, 1 distintas, 1 sobran y 1 iguales · columnas solo en el origen (no se comparan): SOLO_A", tab.Summary);
        Assert.Equal([("2", "Distinta"), ("3", "Falta en el destino"), ("4", "Sobra en el destino")], tab.Items.Select(i => (i.Key, i.ChangeLabel)));
        Assert.Equal("IMPORTE: 25 → 20", tab.Items[0].Details);
        Assert.Contains("UPDATE PRECIOS SET IMPORTE = 20 WHERE ID = 2;", tab.Script);
        Assert.Contains("INSERT INTO PRECIOS (ID, NOMBRE, IMPORTE) VALUES (3, 'Nueva', NULL);", tab.Script);
        Assert.DoesNotContain("DELETE", tab.Script);
        tab.DeleteExtra = true;
        Assert.Contains("DELETE FROM PRECIOS WHERE ID = 4;", tab.Script);

        // El script va a una consulta del destino, sin ejecutar; con F9 y confirmar, quedan iguales.
        tab.OpenInTargetCommand.Execute(null);
        var query = vm.SelectedTab!;
        Assert.Equal(target.Id, query.Profile!.Id);
        Assert.False(query.IsRunning);
        Pump();
        await w.RunAsync();
        Assert.True(query.InTransaction, query.Status);
        await query.CommitCommand.ExecuteAsync(null);
        vm.SelectedTab = tab;
        await tab.CompareCommand.ExecuteAsync(null);
        Assert.Empty(tab.Items);
        Assert.StartsWith("Son iguales: 3 filas.", tab.Summary);

        // Sin clave primaria no se compara.
        var other = vm.OpenDataCompare(source, "RDB$DATABASE");
        other.Target = other.Targets.Single(p => p.Id == target.Id);
        await other.CompareCommand.ExecuteAsync(null);
        Assert.StartsWith("La tabla RDB$DATABASE no tiene clave primaria", other.Summary);
        w.Close();
    }, CancellationToken.None);

    /// <summary>Una copia de la base de prueba (SYSDBA) con el script de la base entera y después <paramref name="changes"/>.</summary>
    private async Task<ConnectionProfile> CopySchemaAsync(string file, string changes)
    {
        await using var explorer = new MetadataExplorer(fb.Profile, fb.Password);
        var schema = await explorer.ReadSchemaAsync(new SchemaScriptOptions());
        var copy = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-fb3-copia", Database = file, User = "SYSDBA" };
        var create = new FbConnectionStringBuilder(FbConnectionFactory.ConnectionString(copy, fb.Password)) { Charset = schema.Charset };
        await FbConnection.CreateDatabaseAsync(create.ConnectionString, 8192, true, true);
        await using var session = new QuerySession(copy, fb.Password);
        foreach (var statement in SqlScriptSplitter.Split(SchemaScriptWriter.Write(schema, new SchemaScriptOptions()) + "\n" + changes))
        {
            if (StatementClassifier.Classify(statement.Text).Kind == StatementKind.Empty) continue;
            var (outcome, _) = await FirebirdFixture.RunAsync(session, statement.Text);
            Assert.True(outcome.Error is null, outcome.Error);
        }
        return copy;
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temporal */ }
    }
}
