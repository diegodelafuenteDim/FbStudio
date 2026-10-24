using FbStudio.Core.Export;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>«Copiar como INSERT / UPDATE» de la grilla: una sentencia por fila, con literales de Firebird.</summary>
public class RowScriptTests
{
    private static readonly ColumnInfo[] Columns =
    [
        new(0, "ID", "INTEGER"), new(1, "NOMBRE", "VARCHAR"), new(2, "FECHA", "TIMESTAMP"), new(3, "DIA", "DATE"),
        new(4, "IMPORTE", "NUMERIC"), new(5, "TOTAL", "NUMERIC"), new(6, "CODIGO", "CHAR"),
    ];

    private static readonly object?[][] Rows =
    [
        [5, "O'Brien", new DateTime(2026, 7, 1, 8, 30, 0), new DateTime(2026, 7, 1), 12.5m, 25m, "AB   "],
        [6, null, null, null, DBNull.Value, null, null],
    ];

    private static readonly RowScriptTarget Turnos = new("turnos", ["ID"], new HashSet<string> { "TOTAL" });

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Un_INSERT_por_fila_sin_las_columnas_calculadas()
    {
        Assert.Equal(
        [
            "INSERT INTO TURNOS (ID, NOMBRE, FECHA, DIA, IMPORTE, CODIGO) VALUES (5, 'O''Brien', TIMESTAMP '2026-07-01 08:30:00.0000', DATE '2026-07-01', 12.5, 'AB');",
            "INSERT INTO TURNOS (ID, NOMBRE, FECHA, DIA, IMPORTE, CODIGO) VALUES (6, NULL, NULL, NULL, NULL, NULL);",
        ], Lines(RowScript.Insert(Turnos, Columns, Rows)));
    }

    [Fact]
    public void Un_UPDATE_por_fila_con_la_clave_en_el_WHERE()
    {
        Assert.Equal(
        [
            "UPDATE TURNOS SET NOMBRE = 'O''Brien', FECHA = TIMESTAMP '2026-07-01 08:30:00.0000', DIA = DATE '2026-07-01', IMPORTE = 12.5, CODIGO = 'AB' WHERE ID = 5;",
            "UPDATE TURNOS SET NOMBRE = NULL, FECHA = NULL, DIA = NULL, IMPORTE = NULL, CODIGO = NULL WHERE ID = 6;",
        ], Lines(RowScript.Update(Turnos, Columns, Rows)));

        // Clave de dos columnas, en cualquier lugar del resultado; un NULL en la clave va con IS NULL.
        var two = new RowScriptTarget("DETALLE", ["DIA", "ID"], new HashSet<string> { "TOTAL" });
        Assert.EndsWith("WHERE DIA = DATE '2026-07-01' AND ID = 5;", Lines(RowScript.Update(two, Columns, Rows))[0]);
        Assert.EndsWith("WHERE DIA IS NULL AND ID = 6;", Lines(RowScript.Update(two, Columns, Rows))[1]);

        Assert.Throws<InvalidOperationException>(() => RowScript.Update(Turnos with { KeyColumns = null }, Columns, Rows));
        Assert.Throws<InvalidOperationException>(() => RowScript.Update(Turnos with { KeyColumns = ["NO_ESTA"] }, Columns, Rows));
    }

    [Fact]
    public void El_nombre_de_la_tabla_y_las_columnas_repetidas()
    {
        Assert.Equal("TURNOS", RowScript.QuoteTable(" turnos "));
        Assert.Equal("\"MiTabla\"", RowScript.QuoteTable("\"MiTabla\""));
        Assert.Equal("\"MI TABLA\"", RowScript.QuoteTable("mi tabla"));

        // Un JOIN que trae dos ID: va el primero (el segundo no se puede escribir en la misma tabla).
        ColumnInfo[] join = [new(0, "ID", "INTEGER"), new(1, "ID", "INTEGER"), new(2, "NOMBRE", "VARCHAR")];
        Assert.Equal("INSERT INTO T (ID, NOMBRE) VALUES (1, 'x');", Lines(RowScript.Insert(new RowScriptTarget("T", null, new HashSet<string>()), join, [[1, 2, "x"]]))[0]);
    }
}
