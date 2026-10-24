using System.Collections.Immutable;
using System.Globalization;
using FbStudio.Core.Data;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

public class TableDataTests
{
    private static readonly DbColumn[] Turnos =
    [
        new("ID", "INTEGER", true, true, null, null),
        new("FECHA", "TIMESTAMP", false, false, null, null),
        new("PACIENTE", "VARCHAR(60)", false, false, null, null),
        new("IMPORTE", "NUMERIC(12,2)", false, false, "D_IMPORTE", null),
        new("DIA", "DATE", false, false, null, null),
        new("TOTAL", "BIGINT", false, false, null, null, IsComputed: true),
        new("OBS", "BLOB SUB_TYPE TEXT", false, false, null, null),
    ];

    private static ColumnFilter Filter(string text, string type) => ColumnFilter.Parse(text, new ColumnInfo(0, "X", type))!;

    [Fact]
    public void Las_filas_nuevas_van_como_INSERT_al_final_con_solo_lo_completado_y_RETURNING()
    {
        var changes = new TableChanges(Turnos);
        object?[] row6 = [6, new DateTime(2026, 1, 1), "PACIENTE 6", 0.06m, null, 12L, null];
        changes.ToggleDeleted(row6);
        var empty = changes.AddNew();
        var filled = changes.AddNew();
        Assert.All(filled, v => Assert.Equal(DBNull.Value, v));             // arranca vacía
        Assert.True(changes.IsNew(filled) && changes.IsChanged(filled) && !changes.IsDeleted(filled));
        changes.Set(filled, 2, "NUEVO");
        changes.Set(filled, 3, 10.5m);
        changes.Set(filled, 4, new DateTime(2026, 10, 19));
        changes.Set(filled, 4, null);                                      // vaciarla: la columna no va (vale el DEFAULT)
        Assert.True(changes.TryGetValue(filled, 2, out var name));
        Assert.Equal("NUEVO", name);
        Assert.Equal((3, 0, 1, 2), (changes.Count, changes.UpdateCount, changes.DeleteCount, changes.InsertCount));

        var statements = changes.BuildStatements("TURNOS");
        Assert.Equal([true, false, false], statements.Select(s => s.IsDelete));   // primero el DELETE, los INSERT al final
        Assert.Equal([false, true, true], statements.Select(s => s.IsInsert));
        var all = "RETURNING ID, FECHA, PACIENTE, IMPORTE, DIA, TOTAL, OBS";
        Assert.Equal("INSERT INTO TURNOS DEFAULT VALUES " + all, statements[1].Command.Sql);
        Assert.Equal("INSERT INTO TURNOS DEFAULT VALUES;", statements[1].Readable);
        Assert.Equal("INSERT INTO TURNOS (PACIENTE, IMPORTE) VALUES (@v0, @v1) " + all, statements[2].Command.Sql);
        Assert.Equal(["NUEVO", 10.5m], statements[2].Command.Parameters.Select(p => p.Value));
        Assert.Equal("INSERT INTO TURNOS (PACIENTE, IMPORTE) VALUES ('NUEVO', 10.5);", statements[2].Readable);
        Assert.Same(filled, changes.NewRowFor(statements[2].RowKey));
        Assert.Equal("la fila nueva 2", changes.Describe(statements[2].RowKey));
        Assert.DoesNotContain("RETURNING", changes.BuildStatements("V_TURNOS", returning: false)[2].Command.Sql); // en una vista no se pide
        Assert.StartsWith("-- Cambios de datos en TURNOS (FbStudio): 0 UPDATE, 1 DELETE, 2 INSERT", TableChanges.Script("TURNOS", statements));

        // Borrar o deshacer una fila nueva antes de aplicar la saca: no queda ningún INSERT de ella.
        changes.ToggleDeleted(empty);
        changes.Revert(filled);
        Assert.Equal((1, 0), (changes.Count, changes.InsertCount));
        Assert.Empty(changes.NewRows);
        Assert.False(changes.IsNew(filled));
    }

    [Fact]
    public void La_pagina_siguiente_se_pide_despues_de_la_ultima_fila_si_el_orden_lo_permite()
    {
        object?[] last = [499, new DateTime(2026, 1, 1), "PACIENTE 499", 4.99m, null, 9L, "obs"];
        var q = new TableQuery("TURNOS", Turnos, IsView: false);
        var next = q.Page(500, 500, last);
        Assert.Equal("SELECT ID, FECHA, PACIENTE, IMPORTE, DIA, TOTAL, OBS\nFROM TURNOS\nWHERE (ID > @s0)\nORDER BY ID\nROWS 500", next.Sql);
        Assert.Equal([499], next.Parameters.Select(p => p.Value));

        // Con filtro: la condición de la clave va primero y el filtro se respeta.
        var filtered = (q with { Filters = ImmutableDictionary<int, ColumnFilter>.Empty.Add(2, Filter("pérez", "VARCHAR(60)")) }).Page(500, 500, last);
        Assert.Contains("WHERE (ID > @s0)\n  AND PACIENTE CONTAINING @f1", filtered.Sql);

        // Ordenada por una columna sin NULL (descendente) y desempatada por la clave.
        DbColumn[] notNullDate = [Turnos[0], Turnos[1] with { NotNull = true }, .. Turnos[2..]];
        var byDate = new TableQuery("TURNOS", notNullDate, false) { SortColumn = 1, SortDirection = SortDirection.Descending }.Page(500, 500, last);
        Assert.Contains("WHERE ((FECHA < @s0) OR (FECHA = @s1 AND ID > @s2))\nORDER BY FECHA DESC, ID\nROWS 500", byDate.Sql);

        // Por la clave descendente: «menor que la última».
        Assert.Contains("WHERE (ID < @s0)\nORDER BY ID DESC", (q with { SortColumn = 0, SortDirection = SortDirection.Descending }).Page(500, 500, last).Sql);

        // Una columna que admite NULL, o una tabla sin clave: como antes, con ROWS (un NULL no se compara con «mayor que»).
        Assert.EndsWith("ROWS 501 TO 1000", (q with { SortColumn = 2, SortDirection = SortDirection.Ascending }).Page(500, 500, last).Sql);
        DbColumn[] noKey = [.. Turnos.Select(c => c with { IsPrimaryKey = false })];
        Assert.EndsWith("ROWS 501 TO 1000", new TableQuery("LOG", noKey, false).Page(500, 500, last).Sql);
    }

    [Fact]
    public void Pagina_ordenada_por_la_clave_primaria_y_con_ROWS()
    {
        var page = new TableQuery("TURNOS", Turnos, IsView: false).Page(500, 500);
        Assert.Equal("SELECT ID, FECHA, PACIENTE, IMPORTE, DIA, TOTAL, OBS\nFROM TURNOS\nORDER BY ID\nROWS 501 TO 1000", page.Sql);
        Assert.Empty(page.Parameters);
    }

    [Fact]
    public void Orden_elegido_mas_la_clave_para_que_las_paginas_no_se_pisen()
    {
        var q = new TableQuery("TURNOS", Turnos, false) { SortColumn = 2, SortDirection = SortDirection.Descending };
        Assert.Contains("ORDER BY PACIENTE DESC, ID", q.Page(0, 10).Sql);
        // Sin clave: por RDB$DB_KEY en las tablas; las vistas, sin orden.
        DbColumn[] sinClave = [new("A", "INTEGER", false, false, null, null)];
        Assert.Contains("ORDER BY RDB$DB_KEY", new TableQuery("T", sinClave, false).Page(0, 10).Sql);
        Assert.DoesNotContain("ORDER BY", new TableQuery("V", sinClave, true).Page(0, 10).Sql);
    }

    [Fact]
    public void Los_filtros_de_la_grilla_van_al_WHERE_con_parametros()
    {
        var q = new TableQuery("TURNOS", Turnos, false)
        {
            ExtraWhere = "ID > 100;",
            Filters = ImmutableDictionary<int, ColumnFilter>.Empty
                .Add(2, Filter("pérez*", "VARCHAR"))
                .Add(3, Filter(">1500,5", "NUMERIC"))
                .Add(1, Filter("2026-07-01", "TIMESTAMP"))
                .Add(4, Filter("null", "DATE")),
        };
        var page = q.Page(0, 100);
        Assert.Contains("WHERE (ID > 100)\n  AND (FECHA >= @f0 AND FECHA < @f1)\n  AND UPPER(PACIENTE) LIKE UPPER(@f2) ESCAPE '\\'\n  AND IMPORTE > @f3\n  AND DIA IS NULL", page.Sql);
        Assert.Equal([new DateTime(2026, 7, 1), new DateTime(2026, 7, 2), "pérez%", 1500.5m], page.Parameters.Select(p => p.Value));
        Assert.Equal("SELECT COUNT(*) FROM TURNOS\nWHERE (ID > 100)\n  AND (FECHA >= @f0 AND FECHA < @f1)\n  AND UPPER(PACIENTE) LIKE UPPER(@f2) ESCAPE '\\'\n  AND IMPORTE > @f3\n  AND DIA IS NULL",
            q.Count().Sql);
    }

    [Fact]
    public void Texto_simple_es_CONTAINING_y_el_guion_bajo_vale_literal()
    {
        var parameters = new List<SqlParameterValue>();
        Assert.Equal("PACIENTE CONTAINING @f0", FilterSql.ToSql(Filter("gonz", "VARCHAR").Condition!, Turnos[2], parameters));
        Assert.Equal("UPPER(PACIENTE) LIKE UPPER(@f1) ESCAPE '\\'", FilterSql.ToSql(Filter("A_B*", "VARCHAR").Condition!, Turnos[2], parameters));
        Assert.Equal(["gonz", "A\\_B%"], parameters.Select(p => p.Value));
        // Un filtro con error no se manda al servidor.
        Assert.Null(Filter(">abc", "INTEGER").Condition);
    }

    [Theory]
    [InlineData("INTEGER", "42", 42L)]
    [InlineData("INTEGER", "-7", -7L)]
    [InlineData("NUMERIC(12,2)", "1500,50", 1500.50)]
    [InlineData("NUMERIC(12,2)", "1.5", 1.5)]   // con es-AR: la coma no está, el punto se toma como decimal (no como miles)
    [InlineData("DOUBLE PRECISION", "0,25", 0.25)]
    [InlineData("BOOLEAN", "sí", true)]
    [InlineData("BOOLEAN", "0", false)]
    public void Interpreta_lo_escrito_segun_el_tipo(string type, string text, object expected)
    {
        Assert.True(CellValueParser.TryParse(text, new DbColumn("X", type, false, false, null, null), CultureInfo.GetCultureInfo("es-AR"), out var value, out var error), error);
        Assert.Equal(expected is double d ? (decimal)d : expected, value);
    }

    [Fact]
    public void Fechas_horas_textos_y_vacios()
    {
        var es = CultureInfo.GetCultureInfo("es-AR");
        object? v;
        Assert.True(CellValueParser.TryParse("01/07/2026", Turnos[4], es, out v, out _));
        Assert.Equal(new DateTime(2026, 7, 1), v);
        Assert.True(CellValueParser.TryParse("2026-07-01 14:30", Turnos[1], es, out v, out _));
        Assert.Equal(new DateTime(2026, 7, 1, 14, 30, 0), v);
        Assert.True(CellValueParser.TryParse("09:05", new DbColumn("H", "TIME", false, false, null, null), es, out v, out _));
        Assert.Equal(new TimeSpan(9, 5, 0), v);
        Assert.True(CellValueParser.TryParse("", Turnos[3], es, out v, out _));
        Assert.Null(v);                                              // número vacío = NULL
        Assert.True(CellValueParser.TryParse("", Turnos[2], es, out v, out _));
        Assert.Equal("", v);                                         // texto vacío = '' (para NULL está el menú)
        Assert.False(CellValueParser.TryParse("1.500,5", Turnos[3], es, out _, out var error));
        Assert.Contains("1.500,5", error);                            // los miles no se escriben
        Assert.False(CellValueParser.TryParse("31/02/2026", Turnos[4], es, out _, out _));
        Assert.False(CellValueParser.IsEditable(Turnos[5]));        // calculada
        Assert.True(CellValueParser.IsEditable(Turnos[6]));         // blob de texto sí
        Assert.False(CellValueParser.IsEditable(new DbColumn("B", "BLOB SUB_TYPE BINARY", false, false, null, null)));
        Assert.Equal("1500,5", CellValueParser.EditText(1500.50m, Turnos[3], es));
        Assert.Equal("2026-07-01 14:30:00", CellValueParser.EditText(new DateTime(2026, 7, 1, 14, 30, 0), Turnos[1], es));
    }

    [Fact]
    public void Cambios_pendientes_arman_UPDATE_con_control_optimista_y_DELETE()
    {
        var changes = new TableChanges(Turnos);
        object?[] row5 = [5, new DateTime(2026, 1, 1), "PACIENTE 5", 0.05m, null, 10L, "obs"];
        object?[] row6 = [6, new DateTime(2026, 1, 1), "PACIENTE 6", 0.06m, null, 12L, null];

        changes.Set(row5, 2, "PÉREZ");
        changes.Set(row5, 3, 99.9m);
        changes.Set(row5, 6, "nueva obs");
        Assert.True(changes.TryGetValue(row5, 2, out var pending));
        Assert.Equal("PÉREZ", pending);
        changes.Set(row5, 3, 0.05m);                                // vuelve a lo leído: deja de ser un cambio
        Assert.False(changes.TryGetValue(row5, 3, out _));
        changes.ToggleDeleted(row6);
        Assert.Equal((2, 1, 1), (changes.Count, changes.UpdateCount, changes.DeleteCount));

        var statements = changes.BuildStatements("TURNOS");
        Assert.Equal(2, statements.Count);
        Assert.True(statements[0].IsDelete);
        Assert.Equal("DELETE FROM TURNOS WHERE ID = @k0", statements[0].Command.Sql);
        Assert.Equal("DELETE FROM TURNOS WHERE ID = 6;", statements[0].Readable);
        var update = statements[1];
        // El blob no entra en el control optimista; el VARCHAR sí.
        Assert.Equal("UPDATE TURNOS SET PACIENTE = @v0, OBS = @v1 WHERE ID = @k2 AND PACIENTE IS NOT DISTINCT FROM @o3", update.Command.Sql);
        Assert.Equal(["PÉREZ", "nueva obs", 5, "PACIENTE 5"], update.Command.Parameters.Select(p => p.Value));
        // Lo legible es lo mismo que se ejecuta, con el control optimista incluido.
        Assert.Equal("UPDATE TURNOS SET PACIENTE = 'PÉREZ', OBS = 'nueva obs' WHERE ID = 5 AND PACIENTE IS NOT DISTINCT FROM 'PACIENTE 5';", update.Readable);
        var script = TableChanges.Script("TURNOS", statements).Split('\n');
        Assert.Equal(4, script.Length);
        Assert.StartsWith("-- Cambios de datos en TURNOS (FbStudio): 1 UPDATE, 1 DELETE, 0 INSERT", script[0]);
        Assert.Equal(["DELETE FROM TURNOS WHERE ID = 6;", update.Readable], script[2..]);
        Assert.Equal("ID = 5", changes.Describe(update.RowKey));

        // Las filas se reconocen por la clave aunque cambie el orden o se vuelvan a leer.
        object?[] row5Again = [5, new DateTime(2026, 1, 1), "PACIENTE 5", 0.05m, null, 10L, "obs"];
        Assert.True(changes.IsChanged(row5Again));
        changes.Revert(row5Again);
        changes.ToggleDeleted(row6);                                // desmarcar
        Assert.Equal(0, changes.Count);

        Assert.False(new TableChanges([new DbColumn("A", "INTEGER", false, false, null, null)]).HasKey);
    }
}
