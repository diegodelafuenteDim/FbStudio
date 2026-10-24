using System.Text;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.App.Tests;

/// <summary>La pestaña «Importar · TABLA» (F-26), contra Firebird 3 en Docker.</summary>
[Collection(nameof(UiCollection))]
public sealed class ImportTabTests(FirebirdFixture fb, HeadlessFixture ui) : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-import-" + Guid.NewGuid().ToString("N"));

    // Las tablas de cada prueba se borran al terminar: la base es compartida y otras pruebas cuentan sus objetos.
    private readonly List<string> _tables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var table in _tables) await ScalarAsync($"DROP TABLE {table}");
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private (MainWindow Window, MainWindowViewModel Vm) Open(params ConnectionProfile[] profiles)
    {
        var store = AppServices.CreateProfileStore(_folder);
        foreach (var p in profiles.DefaultIfEmpty(fb.Profile)) store.Upsert(p, fb.Password);
        var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand(sql, c, tx);
        var value = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();
        return value;
    }

    private async Task<string> NewTableAsync()
    {
        var table = "IMPUI_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await ScalarAsync($"CREATE TABLE {table} (ID INTEGER NOT NULL PRIMARY KEY, NOMBRE VARCHAR(30), FECHA DATE, " +
                          "IMPORTE NUMERIC(12,2), ACTIVO BOOLEAN, DOBLE COMPUTED BY (IMPORTE * 2))");
        _tables.Add(table);
        return table;
    }

    private string Csv(string content)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public Task Relaciona_las_columnas_muestra_el_archivo_e_importa() => ui.Session.DispatchAsync(async () =>
    {
        var table = await NewTableAsync();
        var (w, vm) = Open();
        var tab = vm.OpenImport(vm.Profiles.Single(), table);
        await tab.Working;
        Assert.Same(tab, vm.OpenImport(vm.Profiles.Single(), table));   // una pestaña por tabla
        Assert.Equal($"Importar · {table}", tab.BaseTitle);
        Assert.Equal(["ID", "NOMBRE", "FECHA", "IMPORTE", "ACTIVO"], tab.Columns.Select(c => c.Name)); // sin la calculada

        var preview = new List<int>();
        tab.PreviewChanged += (_, rows) => preview.Add(rows.Count);
        // Nombres con otra mayúscula y con tilde; una columna que la tabla no tiene; otro orden.
        tab.OpenFile(Csv("Nómbre;id;Importe;Fecha;Activo;Observaciones\nPérez;1;12,50;22/10/2026;Sí;x\nGómez;2;1.234,99;2026-01-02;no;y\n;3;;;;\n"));
        Pump();
        Assert.True(tab.IsCsv);
        Assert.Equal(["(no importar)", "Nómbre", "id", "Importe", "Fecha", "Activo", "Observaciones"], tab.FileColumns);
        Assert.Equal([2, 1, 4, 3, 5], tab.Columns.Select(c => c.FileIndex));
        Assert.Equal([3], preview);

        await tab.RunAsync();
        Pump();
        Assert.StartsWith("Importadas 3 filas en", tab.Status);
        Assert.Contains($"Importación a {table} desde", tab.Document.Text);
        Assert.Equal(3, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}")));
        Assert.Equal(1247.49m, Convert.ToDecimal(await ScalarAsync($"SELECT SUM(IMPORTE) FROM {table}")));
        Assert.Equal(new DateTime(2026, 10, 22), Convert.ToDateTime(await ScalarAsync($"SELECT FECHA FROM {table} WHERE ID = 1")));
        Assert.Equal(DBNull.Value, await ScalarAsync($"SELECT NOMBRE FROM {table} WHERE ID = 3"));

        // Una columna se puede dejar afuera; con otro separador se vuelve a leer y a relacionar.
        tab.OpenFile(Csv("ID,NOMBRE\n10,Ruiz\n"));
        tab.DelimiterIndex = 2;                                            // «,»
        Pump();
        Assert.Equal([1, 2, 0, 0, 0], tab.Columns.Select(c => c.FileIndex));
        tab.Columns[1].FileIndex = 0;
        await tab.RunAsync();
        Assert.StartsWith("Importadas 1 filas", tab.Status);
        Assert.Equal(DBNull.Value, await ScalarAsync($"SELECT NOMBRE FROM {table} WHERE ID = 10"));

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Una_fila_mala_deshace_todo_o_se_saltea() => ui.Session.DispatchAsync(async () =>
    {
        var table = await NewTableAsync();
        var (w, vm) = Open();
        var tab = vm.OpenImport(vm.Profiles.Single(), table);
        await tab.Working;
        tab.OpenFile(Csv("ID;NOMBRE;FECHA\n1;a;01/02/2026\n2;b;no es fecha\n3;c;\n1;repetida;\n"));
        Pump();

        await tab.RunAsync();
        Assert.StartsWith("No se importó nada: la fila del renglón 3 falló y se deshizo todo.", tab.Status);
        Assert.Equal(0, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}")));
        Assert.Contains("Renglón 3:", tab.Document.Text);

        tab.StopOnError = false;
        await tab.RunAsync();
        Assert.StartsWith("Importadas 2 filas en", tab.Status);
        Assert.Contains("2 filas con error se saltearon", tab.Status);
        Assert.Contains("Renglón 5:", tab.Document.Text);                  // la clave repetida
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}")));

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task En_produccion_pregunta_y_vaciar_pide_el_nombre_de_la_tabla() => ui.Session.DispatchAsync(async () =>
    {
        var table = await NewTableAsync();
        await ScalarAsync($"EXECUTE BLOCK AS BEGIN INSERT INTO {table} (ID) VALUES (100); INSERT INTO {table} (ID) VALUES (101); END");
        var prod = fb.Profile with { Id = Guid.NewGuid(), Name = "docker-prod", Environment = ProfileEnvironment.Produccion };
        var (w, vm) = Open(prod);
        var prompts = new List<ConfirmPrompt>();
        var typed = new List<TypedConfirm>();
        var answer = false;
        vm.ConfirmActionAsync = p => { prompts.Add(p); return Task.FromResult(answer); };
        vm.ConfirmTypedAsync = p => { typed.Add(p); return Task.FromResult(true); };
        var tab = vm.OpenImport(vm.Profiles.Single(), table);
        await tab.Working;
        tab.OpenFile(Csv("ID\n1\n2\n3\n"));
        tab.EmptyTableFirst = true;
        Pump();

        await tab.RunAsync();                                             // dice que no: nada
        Assert.Contains("Vas a insertar 3 filas", prompts.Single().Reasons[0]);
        Assert.Contains("PRODUCCIÓN", prompts.Single().Reasons[0]);
        Assert.Empty(typed);
        Assert.Equal(2, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}")));

        answer = true;
        await tab.RunAsync();
        Assert.Equal(table, typed.Single().Expected);
        Assert.Contains("BORRAR TODAS las filas", typed.Single().Reasons[0]);
        Assert.StartsWith("Importadas 3 filas", tab.Status);
        Assert.Contains("Se borraron antes 2 filas.", tab.Status);
        Assert.Equal(3, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}")));

        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Con_un_registro_de_solo_lectura_no_se_importa() => ui.Session.DispatchAsync(async () =>
    {
        var table = await NewTableAsync();
        var (w, vm) = Open(fb.Profile with { ReadOnly = true });
        var tab = vm.OpenImport(vm.Profiles.Single(), table);
        await tab.Working;
        Pump();
        Assert.True(tab.IsReadOnlyProfile);
        Assert.False(tab.RunCommand.CanExecute(null));
        Assert.Equal("Este registro es de solo lectura: no se puede importar.", tab.Status);
        w.Close();
        await vm.DisposeAsync();
    }, CancellationToken.None);
}
