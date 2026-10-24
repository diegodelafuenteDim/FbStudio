using System.Collections.Immutable;
using System.Diagnostics;
using FbStudio.Core.Data;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>Pestaña «Datos» (F-3) contra Firebird 3 en Docker. Las escrituras van a tablas propias de cada test.</summary>
[Collection(nameof(FirebirdCollection))]
public class TableDataSessionTests(FirebirdFixture fb) : IAsyncLifetime
{
    private readonly List<string> _created = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Las tablas de cada test se borran al terminar: otros tests cuentan las tablas de la base.</summary>
    public async Task DisposeAsync()
    {
        foreach (var table in _created) await ScalarAsync($"DROP TABLE {table}");
    }

    private async Task<IReadOnlyList<DbColumn>> ColumnsAsync(string table)
    {
        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        return await ex.LoadColumnsAsync(table);
    }

    /// <summary>Tabla nueva con 20 filas (ID 1..20, NOMBRE 'N1'..., IMPORTE ID*10).</summary>
    private async Task<string> CreateTableAsync(string name)
    {
        _created.Add(name);
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test-datos");
        await c.OpenAsync();
        foreach (var sql in new[]
        {
            $"RECREATE TABLE {name} (ID INTEGER NOT NULL PRIMARY KEY, NOMBRE VARCHAR(40), IMPORTE NUMERIC(12,2), DOBLE COMPUTED BY (IMPORTE * 2))",
            $"EXECUTE BLOCK AS DECLARE I INTEGER = 1; BEGIN WHILE (I <= 20) DO BEGIN INSERT INTO {name} (ID, NOMBRE, IMPORTE) VALUES (:I, 'N' || :I, :I * 10); I = I + 1; END END",
        })
        {
            await using var tx = await c.BeginTransactionAsync();
            await using var cmd = new FbCommand(sql, c, tx);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        return name;
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test-datos");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand(sql, c, tx);
        var value = await cmd.ExecuteScalarAsync();
        await tx.CommitAsync();
        return value;
    }

    [Fact]
    public async Task Pagina_de_a_partes_y_sabe_si_hay_mas()
    {
        var q = new TableQuery("TURNOS", await ColumnsAsync("TURNOS"), false)
        {
            Filters = ImmutableDictionary<int, ColumnFilter>.Empty.Add(0, ColumnFilter.Parse("<1200", new ColumnInfo(0, "ID", "INTEGER"))!),
        };
        await using var s = new TableDataSession(fb.Profile, fb.Password);
        var first = await s.ReadPageAsync(q, 0, 500);
        var second = await s.ReadPageAsync(q, 500, 500);
        var third = await s.ReadPageAsync(q, 1000, 500);
        Assert.Equal((500, true, 0), (first.Rows.Count, first.HasMore, Convert.ToInt32(first.Rows[0][0])));
        Assert.Equal(500, Convert.ToInt32(second.Rows[0][0]));
        Assert.Equal((200, false, 1199), (third.Rows.Count, third.HasMore, Convert.ToInt32(third.Rows[^1][0])));
        Assert.Equal(1200, await s.CountAsync(q));
        Assert.Equal(["ID", "FECHA", "PACIENTE", "OBS", "IMPORTE"], first.Columns.Select(c => c.Name));

        // «Después de la última fila» trae lo mismo que ROWS, también al revés.
        var secondBySeek = await s.ReadPageAsync(q, 500, 500, first.Rows[^1]);
        Assert.Equal(second.Rows.Select(r => r[0]), secondBySeek.Rows.Select(r => r[0]));
        var thirdBySeek = await s.ReadPageAsync(q, 1000, 500, secondBySeek.Rows[^1]);
        Assert.Equal((200, false, 1199), (thirdBySeek.Rows.Count, thirdBySeek.HasMore, Convert.ToInt32(thirdBySeek.Rows[^1][0])));
        var desc = q with { SortColumn = 0, SortDirection = SortDirection.Descending };
        var top = await s.ReadPageAsync(desc, 0, 500);
        var next = await s.ReadPageAsync(desc, 500, 500, top.Rows[^1]);
        Assert.Equal((1199, 700, 699), (Convert.ToInt32(top.Rows[0][0]), Convert.ToInt32(top.Rows[^1][0]), Convert.ToInt32(next.Rows[0][0])));
    }

    [Fact]
    public async Task Con_la_clave_la_pagina_del_fondo_tarda_como_la_primera()
    {
        // ROWS 49.501 a 50.000 obliga al servidor a recorrer 49.500 filas; «ID > 49.499» va directo por el índice.
        var q = new TableQuery("TURNOS", await ColumnsAsync("TURNOS"), false);
        await using var s = new TableDataSession(fb.Profile, fb.Password);
        await s.ReadPageAsync(q, 0, 500);                                     // conexión y caché, fuera de la medición
        var last = (await s.ReadPageAsync(q with { ExtraWhere = "ID = 49499" }, 0, 1)).Rows[0];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var byRows = await s.ReadPageAsync(q, 49_500, 500);
        var rowsTime = clock.Elapsed;
        clock.Restart();
        var bySeek = await s.ReadPageAsync(q, 49_500, 500, last);
        var seekTime = clock.Elapsed;
        Assert.Equal(byRows.Rows.Select(r => r[0]), bySeek.Rows.Select(r => r[0]));
        Assert.Equal(49_500, Convert.ToInt32(bySeek.Rows[0][0]));
        Assert.True(seekTime < rowsTime, $"después de la última {seekTime.TotalMilliseconds:N0} ms, ROWS {rowsTime.TotalMilliseconds:N0} ms");
    }

    [Fact]
    public async Task Filtra_y_ordena_en_el_servidor_no_sobre_lo_cargado()
    {
        var columns = await ColumnsAsync("TURNOS");
        var q = new TableQuery("TURNOS", columns, false)
        {
            SortColumn = 0,
            SortDirection = SortDirection.Descending,
            Filters = ImmutableDictionary<int, ColumnFilter>.Empty.Add(2, ColumnFilter.Parse("paciente 4998*", new ColumnInfo(2, "PACIENTE", "VARCHAR"))!),
        };
        await using var s = new TableDataSession(fb.Profile, fb.Password);
        var page = await s.ReadPageAsync(q, 0, 100);
        // PACIENTE 4998 y PACIENTE 49980..49989, de mayor a menor ID: no estaban en ninguna página "cargada".
        // (La 49999 no sirve: otro test le confirma PACIENTE = 'COMMIT-TEST'.)
        Assert.Equal([49989, 49988, 49987], page.Rows.Take(3).Select(r => Convert.ToInt32(r[0])));
        Assert.Equal(11, page.Rows.Count);
        Assert.Equal(4998, Convert.ToInt32(page.Rows[^1][0]));
    }

    [Fact]
    public async Task Hojear_no_deja_transacciones_abiertas()
    {
        await using var s = new TableDataSession(fb.Profile, fb.Password);
        await s.ReadPageAsync(new TableQuery("TURNOS", await ColumnsAsync("TURNOS"), false), 0, 500);
        var open = await ScalarAsync("""
            SELECT COUNT(*) FROM MON$TRANSACTIONS t JOIN MON$ATTACHMENTS a ON a.MON$ATTACHMENT_ID = t.MON$ATTACHMENT_ID
            WHERE a.MON$REMOTE_PROCESS = 'FbStudio (datos)'
            """);
        Assert.Equal(0, Convert.ToInt32(open)); // la página se confirmó apenas se leyó: la OAT puede avanzar
    }

    [Fact]
    public async Task Aplicar_guarda_todo_junto_y_la_calculada_se_recalcula()
    {
        var table = await CreateTableAsync("DATOS_APLICAR");
        var columns = await ColumnsAsync(table);
        Assert.True(columns.Single(c => c.Name == "DOBLE").IsComputed);
        var q = new TableQuery(table, columns, false);
        await using var s = new TableDataSession(fb.Profile, fb.Password);
        var page = await s.ReadPageAsync(q, 0, 50);

        var changes = new TableChanges(columns);
        changes.Set(page.Rows[0], 1, "Ñandú");      // ID 1
        changes.Set(page.Rows[0], 2, 123.45m);
        changes.ToggleDeleted(page.Rows[1]);         // ID 2
        var result = await s.ApplyAsync(changes.BuildStatements(table));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.Applied);
        Assert.Equal("Ñandú", await ScalarAsync($"SELECT NOMBRE FROM {table} WHERE ID = 1"));
        Assert.Equal(246.90m, await ScalarAsync($"SELECT DOBLE FROM {table} WHERE ID = 1"));
        Assert.Equal(0, Convert.ToInt32(await ScalarAsync($"SELECT COUNT(*) FROM {table} WHERE ID = 2")));
    }

    [Fact]
    public async Task Si_otro_cambio_la_fila_no_se_guarda_nada()
    {
        var table = await CreateTableAsync("DATOS_CONFLICTO");
        var columns = await ColumnsAsync(table);
        await using var s = new TableDataSession(fb.Profile, fb.Password);
        var page = await s.ReadPageAsync(new TableQuery(table, columns, false), 0, 50);

        var changes = new TableChanges(columns);
        changes.Set(page.Rows[2], 1, "mío");         // ID 3: este sí podría guardarse...
        changes.Set(page.Rows[4], 1, "mío también"); // ID 5: ...pero este lo cambió otro mientras tanto
        await ScalarAsync($"UPDATE {table} SET NOMBRE = 'de otro' WHERE ID = 5 RETURNING ID");

        var result = await s.ApplyAsync(changes.BuildStatements(table));
        Assert.False(result.Ok);
        Assert.Equal(0, result.RowsAffected);
        Assert.Equal("ID = 5", changes.Describe(result.Failed!.RowKey));
        Assert.Equal("N3", await ScalarAsync($"SELECT NOMBRE FROM {table} WHERE ID = 3"));     // se deshizo todo
        Assert.Equal("de otro", await ScalarAsync($"SELECT NOMBRE FROM {table} WHERE ID = 5"));
    }

    [Fact]
    public async Task Un_registro_bloqueado_espera_lo_configurado_y_no_deja_nada_a_medias()
    {
        var table = await CreateTableAsync("DATOS_BLOQUEO");
        var columns = await ColumnsAsync(table);
        await using var s = new TableDataSession(fb.Profile, fb.Password) { LockTimeout = TimeSpan.FromSeconds(1) };
        var page = await s.ReadPageAsync(new TableQuery(table, columns, false), 0, 50);

        await using var other = fb.NewSession();
        var (held, _) = await FirebirdFixture.RunAsync(other, $"UPDATE {table} SET IMPORTE = IMPORTE WHERE ID = 7");
        Assert.Null(held.Error);

        var changes = new TableChanges(columns);
        changes.Set(page.Rows[0], 1, "uno");
        changes.Set(page.Rows[6], 1, "siete");       // lo tiene la otra transacción
        var clock = Stopwatch.StartNew();
        var result = await s.ApplyAsync(changes.BuildStatements(table));
        clock.Stop();

        Assert.False(result.Ok);
        Assert.Contains("lock", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(clock.Elapsed.TotalSeconds < 6, $"tardó {clock.Elapsed.TotalSeconds:N1} s"); // con 10 s de espera tardaría 10
        await other.RollbackAsync();
        Assert.Equal("N1", await ScalarAsync($"SELECT NOMBRE FROM {table} WHERE ID = 1"));
    }

    [Fact]
    public async Task Perfil_de_solo_lectura_no_aplica_cambios()
    {
        await using var s = new TableDataSession(fb.Profile with { ReadOnly = true }, fb.Password);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ApplyAsync([]));
    }

    [Fact]
    public async Task Los_filtros_nuevos_corren_en_Firebird()
    {
        // TURNOS de la base de Docker: ID 0..49.999, un minuto por fila desde 2026-01-01, IMPORTE ID/100, OBS con hasta 499 «x».
        var columns = await ColumnsAsync("TURNOS");
        var q = new TableQuery("TURNOS", columns, false);
        ColumnFilter F(int column, string text) => ColumnFilter.Parse(text, new ColumnInfo(column, columns[column].Name, columns[column].Type.Split('(')[0]))!;
        async Task<long?> Count(params (int Column, string Text)[] filters)
        {
            await using var s = new TableDataSession(fb.Profile, fb.Password);
            return await s.CountAsync(q with { Filters = filters.Aggregate(ImmutableDictionary<int, ColumnFilter>.Empty, (d, f) => d.Add(f.Column, F(f.Column, f.Text))) });
        }

        Assert.Equal(3, await Count((0, "1;5;9")));                                              // IN
        Assert.Equal(11, await Count((0, "10..20")));                                            // BETWEEN
        Assert.Equal(1440, await Count((1, "2026-01-01")));                                      // un día entero = 1.440 minutos
        Assert.Equal(2880, await Count((1, "<=2026-01-02")));                                    // «hasta el 2» incluye todo el 2
        Assert.Equal(2880, await Count((1, "2026-01-01..2026-01-02")));
        Assert.Equal(50_000 - 2880, await Count((1, ">2026-01-02")));
        Assert.Equal(2, await Count((2, "=PACIENTE 7;PACIENTE 8")));                              // iguales, sin mayúsculas
        Assert.Equal(1, await Count((0, "<100"), (3, "!x")));                                    // OBS vacía (ID 0): no contiene «x»
        Assert.Equal(101, await Count((4, "1..2")));                                             // IMPORTE entre 1 y 2
        Assert.Equal(0, await Count((1, "hoy")));

        // Los valores para tildar: con fecha y hora, por día (50.000 minutos son 35 días); sin el filtro de su columna.
        await using var session = new TableDataSession(fb.Profile, fb.Password);
        var days = await session.DistinctAsync(q with { Filters = ImmutableDictionary<int, ColumnFilter>.Empty.Add(1, F(1, "hoy")) }, 1, 500);
        Assert.Equal(35, days.Count);
        Assert.Equal(1440, days.Single(d => (DateTime)d.Value! == new DateTime(2026, 1, 1)).Count);
        var two = await session.DistinctAsync(q with { Filters = ImmutableDictionary<int, ColumnFilter>.Empty.Add(0, F(0, "1;2")) }, 2, 500);
        Assert.Equal(["PACIENTE 1", "PACIENTE 2"], two.Select(v => (string)v.Value!).Order());
    }
}
