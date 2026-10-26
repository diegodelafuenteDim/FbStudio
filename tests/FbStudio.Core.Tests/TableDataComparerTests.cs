using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>Comparar los datos de una tabla entre dos bases.</summary>
public class TableDataComparerTests
{
    private static readonly ColumnInfo[] Columns =
    [
        new(0, "ID", "INTEGER"),
        new(1, "NOMBRE", "VARCHAR"),
        new(2, "CODIGO", "CHAR"),
        new(3, "IMPORTE", "NUMERIC"),
    ];

    [Fact]
    public void Faltan_sobran_y_cambian_por_la_clave()
    {
        object?[][] source =
        [
            [1, "Ecografía", "ECO", 10.50m],
            [2, "Rayos", "RX", 20m],
            [3, "Nueva", "NV", null],
            [5, "Igual", "IG", 1m],
        ];
        object?[][] target =
        [
            [1, "Ecografia", "ECO   ", 10.5m],          // CHAR más largo e importe con otra escala: solo cambia el nombre
            [2, "Rayos", "RX", 25m],
            [4, "Sobra", "SO", 0m],
            [5L, "Igual", "IG", 1],                     // la clave como BIGINT y el importe como INTEGER: es la misma fila
        ];
        var c = TableDataComparer.Compare("PRESTACIONES", Columns, [0], source, target);
        Assert.Equal((4, 4, 1), (c.SourceRows, c.TargetRows, c.Equal));
        Assert.Equal((1, 2, 1), (c.OnlyInSource, c.Different, c.OnlyInTarget));
        Assert.Equal([RowChange.Different, RowChange.Different, RowChange.OnlyInSource, RowChange.OnlyInTarget], c.Differences.Select(d => d.Change));
        var first = c.Differences.First(d => TableDataComparer.KeyText(d) == "1");
        Assert.Equal(["NOMBRE: 'Ecografia' → 'Ecografía'"], TableDataComparer.ChangeDetails(c, first));

        var script = TableDataComparer.Script(c, deleteExtra: false, "-- igualar\n");
        Assert.Equal(
            "-- igualar\n" +
            "UPDATE PRESTACIONES SET NOMBRE = 'Ecografía' WHERE ID = 1;\n" +
            "UPDATE PRESTACIONES SET IMPORTE = 20 WHERE ID = 2;\n" +
            "INSERT INTO PRESTACIONES (ID, NOMBRE, CODIGO, IMPORTE) VALUES (3, 'Nueva', 'NV', NULL);\n" +
            "-- 1 filas están solo en el destino: no se borran (tildá «Borrar las que sobran» para incluirlas).\n", script);
        Assert.EndsWith("DELETE FROM PRESTACIONES WHERE ID = 4;\n", TableDataComparer.Script(c, deleteExtra: true, ""));
    }

    [Fact]
    public void Clave_de_varias_columnas_binarios_y_sin_clave()
    {
        ColumnInfo[] columns = [new(0, "A", "INTEGER"), new(1, "B", "VARCHAR"), new(2, "FOTO", "BLOB")];
        object?[][] source = [[1, "x", new byte[] { 1, 2 }], [1, "y", null]];
        object?[][] target = [[1, "x ", new byte[] { 1, 2 }], [1, "y", new byte[] { 9 }]];
        var c = TableDataComparer.Compare("T", columns, [0, 1], source, target);
        Assert.Equal(1, c.Equal);
        var d = Assert.Single(c.Differences);
        Assert.Equal(("1 · y", RowChange.Different), (TableDataComparer.KeyText(d), d.Change));
        Assert.Equal("UPDATE T SET FOTO = NULL WHERE A = 1 AND B = 'y';\n", TableDataComparer.Script(c, false, ""));

        Assert.Throws<ArgumentException>(() => TableDataComparer.Compare("T", columns, [], source, target));
        Assert.True(TableDataComparer.ValuesEqual(DBNull.Value, null));
        Assert.False(TableDataComparer.ValuesEqual(0, null));
        Assert.True(TableDataComparer.ValuesEqual(1.5d, 1.5m));
    }
}
