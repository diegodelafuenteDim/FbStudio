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
