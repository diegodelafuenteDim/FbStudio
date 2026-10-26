using System.Globalization;
using FbStudio.Core.Import;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>Importar filas a una tabla (F-26) contra Firebird 3 en Docker.</summary>
[Collection(nameof(FirebirdCollection))]
public class DataImporterTests(FirebirdFixture fb) : IAsyncLifetime
{
    // Las tablas de cada prueba se borran al terminar: la base es compartida y otras pruebas cuentan sus objetos.
    private readonly List<string> _tables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var table in _tables) await ExecAsync($"DROP TABLE {table}");
    }

    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
    private static readonly string[] Columns = ["ID", "NOMBRE", "FECHA", "IMPORTE", "ACTIVO"];
    private static readonly string[] Types = ["INTEGER", "VARCHAR(30)", "DATE", "NUMERIC(12,2)", "BOOLEAN"];

    private async Task ExecAsync(string sql)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var cmd = new FbCommand(sql, c, tx)) await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private async Task<List<object[]>> RowsAsync(string table)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand($"SELECT ID, NOMBRE, FECHA, IMPORTE, ACTIVO FROM {table} ORDER BY ID", c, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<object[]>();
        while (await r.ReadAsync())
        {
            var v = new object[5];
            r.GetValues(v);
            rows.Add(v);
        }
        return rows;
    }

    /// <summary>Filas «como vienen de un CSV», convertidas según el tipo de cada columna.</summary>
    private static IEnumerable<(long, object?[])> Csv(params string?[][] rows) =>
        rows.Select((r, i) => ((long)(i + 2), r.Select((v, c) => ImportMapping.Convert(v, Types[c], Ar)).ToArray()));

    private async Task<string> NewTableAsync()
    {
        var table = "IMP_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await ExecAsync($"CREATE TABLE {table} (ID INTEGER NOT NULL PRIMARY KEY, NOMBRE VARCHAR(30), FECHA DATE, IMPORTE NUMERIC(12,2), ACTIVO BOOLEAN)");
        _tables.Add(table);
        return table;
    }

    [Fact]
    public async Task Importa_convirtiendo_fechas_comas_y_booleanos()
    {
        var table = await NewTableAsync();
        var result = await DataImporter.ImportAsync(fb.Profile, fb.Password, table, Columns,
            Csv(["1", "Pérez", "22/10/2026", "12,50", "Sí"], ["2", "Gómez", "2026-01-02", "0,99", "no"], ["3", "", "", "", ""]),
            new ImportOptions());
        Assert.True(result.Committed);
        Assert.Equal((3L, 0L, 0L), (result.Inserted, result.FailedRows, result.Deleted));

        var rows = await RowsAsync(table);
        Assert.Equal(3, rows.Count);
        Assert.Equal(("Pérez", new DateTime(2026, 10, 22), 12.50m, true), ((string)rows[0][1], (DateTime)rows[0][2], (decimal)rows[0][3], (bool)rows[0][4]));
        Assert.Equal(0.99m, (decimal)rows[1][3]);
        Assert.All(rows[2].Skip(1), v => Assert.Equal(DBNull.Value, v));       // vacíos como NULL
    }

    [Fact]
    public async Task Ante_una_fila_con_error_deshace_todo_o_la_saltea()
    {
        var table = await NewTableAsync();
        await ExecAsync($"INSERT INTO {table} (ID, NOMBRE) VALUES (100, 'ya estaba')");

        var stop = await DataImporter.ImportAsync(fb.Profile, fb.Password, table, Columns,
            Csv(["1", "a", null, null, null], ["100", "repetida", null, null, null], ["2", "b", null, null, null]), new ImportOptions());
        Assert.False(stop.Committed);
        var error = Assert.Single(stop.Errors);
        Assert.Equal(3, error.Row);                                            // el renglón del archivo (con el encabezado)
        Assert.Contains("PRIMARY", error.Message.ToUpperInvariant());
        Assert.Single(await RowsAsync(table));                                 // no quedó nada a medias

        var skip = await DataImporter.ImportAsync(fb.Profile, fb.Password, table, Columns,
            Csv(["1", "a", null, null, null], ["100", "repetida", null, null, null], ["2", "b", "no es fecha", null, null], ["3", "c", null, null, null]),
            new ImportOptions(StopOnError: false));
        Assert.True(skip.Committed);
        Assert.Equal((2L, 2L), (skip.Inserted, skip.FailedRows));
        Assert.Equal([3L, 4L], skip.Errors.Select(e => e.Row));
        Assert.Equal([1, 3, 100], (await RowsAsync(table)).Select(r => Convert.ToInt32(r[0])));
    }

    [Fact]
    public async Task Actualiza_las_que_ya_estan_por_la_clave_y_agrega_las_demas()
    {
        var table = await NewTableAsync();
        await ExecAsync($"INSERT INTO {table} (ID, NOMBRE, IMPORTE) VALUES (1, 'viejo', 10)");
        await ExecAsync($"INSERT INTO {table} (ID, NOMBRE, IMPORTE) VALUES (2, 'no viene', 20)");
        var result = await DataImporter.ImportAsync(fb.Profile, fb.Password, table, ["ID", "NOMBRE"],
            [(2, [1, "nuevo"]), (3, [3, "agregado"])], new ImportOptions(MatchingColumns: ["ID"]));
        Assert.True(result.Committed);
        Assert.Equal(2, result.Inserted);                                       // escritas: una actualizada y una agregada
        var rows = await RowsAsync(table);
        Assert.Equal([(1, "nuevo"), (2, "no viene"), (3, "agregado")], rows.Select(r => (Convert.ToInt32(r[0]), (string)r[1])));
        Assert.Equal(10m, (decimal)rows[0][3]);                                 // lo que no viene en el archivo no se toca
    }

    [Fact]
    public async Task Crea_la_tabla_nueva_y_si_no_se_importa_la_vuelve_a_borrar()
    {
        var table = "IMPNEW_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var columns = ImportSchema.Infer(["Id", "Nombre", "Importe"], [["1", "Pérez", "12,50"], ["2", "Gómez", "3"]], Ar);
        var ddl = ImportSchema.CreateTableSql(table, columns);
        IEnumerable<(long, object?[])> Rows(params string[][] rows) =>
            rows.Select((r, i) => ((long)(i + 2), r.Select((v, c) => ImportMapping.Convert(v, columns[c].Type, Ar)).ToArray()));

        // Una fila que falla (con «deshacer todo»): no queda la tabla vacía.
        var failed = await DataImporter.ImportAsync(fb.Profile, fb.Password, table, columns.Select(c => c.Name).ToList(),
            Rows(["1", "Pérez", "12,50"], ["no es número", "Gómez", "3"]), new ImportOptions(CreateTableSql: ddl));
        Assert.False(failed.Committed);
        Assert.False(failed.TableCreated);
        Assert.Equal(0, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = '{table}'")));

        var created = await DataImporter.ImportAsync(fb.Profile, fb.Password, table, columns.Select(c => c.Name).ToList(),
            Rows(["1", "Pérez", "12,50"], ["2", "Gómez", "3"]), new ImportOptions(CreateTableSql: ddl));
        _tables.Add(table);
        Assert.True(created.TableCreated);
        Assert.Equal(2, created.Inserted);
        Assert.Equal(15.50m, Convert.ToDecimal(await ScalarAsync($"SELECT SUM(IMPORTE) FROM {table}")));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand(sql, c, tx);
        return await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Vaciar_antes_y_registro_de_solo_lectura()
    {
        var table = await NewTableAsync();
        await ExecAsync($"INSERT INTO {table} (ID) VALUES (7)");
        var result = await DataImporter.ImportAsync(fb.Profile, fb.Password, table, ["ID", "NOMBRE"],
            [(2, [1, "uno"]), (3, [2, "dos"])], new ImportOptions(EmptyTableFirst: true));
        Assert.Equal((2L, 1L), (result.Inserted, result.Deleted));
        Assert.Equal([1, 2], (await RowsAsync(table)).Select(r => Convert.ToInt32(r[0])));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DataImporter.ImportAsync(fb.Profile with { ReadOnly = true }, fb.Password, table,
            ["ID"], [(2, [9])], new ImportOptions()));
    }
}
