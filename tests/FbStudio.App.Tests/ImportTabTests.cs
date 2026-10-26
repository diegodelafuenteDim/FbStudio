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
        foreach (var table in _tables) await DropAsync(table);
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// DROP TABLE reintentando si sigue «en uso» (una conexión que se está cerrando) y sin cortar si no existe (una
    /// prueba que falló antes de crearla): que una falle no deja tablas que les cambian los conteos a las demás.
    /// </summary>
    private async Task DropAsync(string table)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await ScalarAsync($"DROP TABLE {table}");
                return;
            }
            catch (FbException ex) when (attempt < 40 && ex.Message.Contains("in use", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(250);
            }
            catch (FbException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                                         || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
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
    public Task Actualiza_por_la_clave_y_a_una_tabla_nueva_la_crea_desde_el_archivo() => ui.Session.DispatchAsync(async () =>
    {
        var table = await NewTableAsync();
        await ScalarAsync($"INSERT INTO {table} (ID, NOMBRE, IMPORTE) VALUES (1, 'viejo', 10)");
        var (w, vm) = Open();
        try
        {
            var tab = vm.OpenImport(vm.Profiles.Single(), table);
            await tab.Working;

            // Actualizar o agregar: la clave de entrada es la PK; sin clave no se puede.
            tab.OpenFile(Csv("ID;NOMBRE\n1;nuevo\n2;agregado\n"));
            Pump();
            tab.EmptyTableFirst = true;
            tab.Upsert = true;
            Assert.False(tab.EmptyTableFirst);                                     // vaciar y actualizar no van juntos
            Assert.Equal(["ID"], tab.Columns.Where(c => c.IsKey).Select(c => c.Name));
            tab.Columns[0].IsKey = false;
            await tab.RunAsync();
            Assert.StartsWith("Para actualizar hace falta una clave", tab.Status);
            tab.Columns[0].IsKey = true;
            await tab.RunAsync();
            Assert.StartsWith("Guardadas 2 filas (actualizadas o agregadas)", tab.Status);
            Assert.Equal("nuevo", await ScalarAsync($"SELECT NOMBRE FROM {table} WHERE ID = 1"));
            Assert.Equal(10m, Convert.ToDecimal(await ScalarAsync($"SELECT IMPORTE FROM {table} WHERE ID = 1")));

            // A una tabla nueva: nombre del archivo, una columna por cada una y el tipo según lo que trae.
            var newTab = vm.OpenImport(vm.Profiles.Single(), null);
            Assert.True(newTab.IsNewTable);
            Assert.Equal("Importar · tabla nueva", newTab.BaseTitle);
            var path = Path.Combine(_folder, "Ventas " + Guid.NewGuid().ToString("N")[..6] + ".csv");
            Directory.CreateDirectory(_folder);
            File.WriteAllText(path, "Código;Fecha alta;Importe;Notas\n00123;22/10/2026;12,50;x\n00456;2026-01-02;3;\n", new UTF8Encoding(false));
            newTab.OpenFile(path);
            for (var i = 0; i < 200 && newTab.NewColumns.Count == 0; i++) { await Task.Delay(20); Pump(); }
            var newTable = newTab.NewTableName;
            Assert.StartsWith("VENTAS_", newTable);
            _tables.Add(newTable);
            Assert.Equal(["CODIGO", "FECHA_ALTA", "IMPORTE", "NOTAS"], newTab.NewColumns.Select(c => c.Name));
            Assert.Equal(["VARCHAR(10)", "DATE", "NUMERIC(18,2)", "VARCHAR(10)"], newTab.NewColumns.Select(c => c.Type));
            Assert.Equal("Crear la tabla e importar 2 filas", newTab.RunText);
            newTab.NewColumns[3].Include = false;                                   // NOTAS queda afuera
            newTab.NewColumns[0].Type = "VARCHAR(20)";

            // Un nombre que ya existe no se pisa.
            newTab.NewTableName = table;
            await newTab.RunAsync();
            Assert.StartsWith($"Ya hay un objeto llamado {table}", newTab.Status);
            newTab.NewTableName = newTable;
            await newTab.RunAsync();
            Pump();
            Assert.Contains($"La tabla {newTable} quedó creada", newTab.Status);
            Assert.Contains("CREATE TABLE", newTab.Document.Text);                  // el DDL queda en el registro
            Assert.False(newTab.IsNewTable);                                        // la pestaña pasa a ser la de esa tabla
            Assert.Equal($"Importar · {newTable}", newTab.BaseTitle);
            Assert.Equal(["CODIGO", "FECHA_ALTA", "IMPORTE"], newTab.Columns.Select(c => c.Name));
            Assert.Equal("00123", await ScalarAsync($"SELECT CODIGO FROM {newTable} WHERE IMPORTE = 12.5"));
            Assert.Equal(new DateTime(2026, 1, 2), Convert.ToDateTime(await ScalarAsync($"SELECT FECHA_ALTA FROM {newTable} WHERE CODIGO = '00456'")));

        }
        finally
        {
            // Siempre se cierra: si quedara abierta, sus conexiones no dejan borrar las tablas de la prueba.
            w.Close();
            await vm.DisposeAsync();
        }
    }, CancellationToken.None);

    [Fact]
    public Task Genera_datos_de_prueba_con_muestra_y_no_en_produccion() => ui.Session.DispatchAsync(async () =>
    {
        var table = await NewTableAsync();
        await ScalarAsync($"INSERT INTO {table} (ID, NOMBRE) VALUES (1, 'ya estaba')");
        var (w, vm) = Open();
        try
        {
            vm.ConfirmActionAsync = _ => Task.FromResult(true);
            var tab = vm.OpenTestData(vm.Profiles.Single(), table);
            await tab.Working;
            Assert.Same(tab, vm.OpenTestData(vm.Profiles.Single(), table));       // una pestaña por tabla
            Assert.Equal($"Datos de prueba · {table}", tab.BaseTitle);
            Assert.Equal([Core.Import.TestValueKind.Sequence, Core.Import.TestValueKind.FirstName, Core.Import.TestValueKind.Date,
                    Core.Import.TestValueKind.Decimal, Core.Import.TestValueKind.Boolean, Core.Import.TestValueKind.Skip],
                tab.Columns.Select(c => c.Kind));                                   // la calculada no se llena

            // La muestra no escribe nada.
            var preview = new List<int>();
            tab.PreviewChanged += (_, rows) => preview.Add(rows.Count);
            await tab.PreviewAsync();
            Assert.Equal([TestDataTabViewModel.PreviewRows], preview);
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}")));

            tab.RowCount = 50;
            await tab.RunAsync();
            Assert.StartsWith("Agregadas 50 filas de prueba", tab.Status);
            Assert.Equal(51, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table}")));
            Assert.Equal(51, Convert.ToInt32(await ScalarAsync($"SELECT MAX(ID) FROM {table}")));  // la clave siguió desde la mayor
            Assert.Equal(2, tab.Seed);                                              // para la próxima, otros datos

            // En producción no se genera.
            var prod = vm.CloneProfile(vm.Profiles.Single()) with { Environment = ProfileEnvironment.Produccion };
            var blocked = new TestDataTabViewModel(vm, prod, table);
            Assert.True(blocked.IsBlocked);
            Assert.StartsWith("Es una base de PRODUCCIÓN", blocked.Status);
        }
        finally
        {
            w.Close();
            await vm.DisposeAsync();
        }
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
