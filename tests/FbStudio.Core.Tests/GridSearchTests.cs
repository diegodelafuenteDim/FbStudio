using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>Buscar un texto en los resultados.</summary>
public class GridSearchTests
{
    [Fact]
    public void Sin_mayusculas_ni_acentos_en_el_orden_en_que_se_ve()
    {
        ColumnInfo[] columns = [new(0, "ID", "INTEGER"), new(1, "NOMBRE", "VARCHAR"), new(2, "OBS", "VARCHAR")];
        (int, IReadOnlyList<object?>)[] rows =
        [
            (0, [1, "Peña", "sin obs"]),
            (2, [2, "PENA", null]),                 // la posición 1 es un encabezado de grupo
            (3, [3, "Otro", "pena leve, ojo"]),
        ];
        // Las columnas en el orden en que se ven: OBS antes que NOMBRE.
        Assert.Equal([(3, 2)], GridSearch.Find(rows, [columns[2]], "PEÑA"));
        Assert.Equal([(0, 1), (2, 1), (3, 2)], GridSearch.Find(rows, [columns[0], columns[1], columns[2]], "pena"));
        Assert.Equal([(3, 2), (3, 1)], GridSearch.Find(rows.Skip(2), [columns[2], columns[1]], "o"));
        Assert.Empty(GridSearch.Find(rows, columns, "  "));
        Assert.Empty(GridSearch.Find(rows, columns, "NULL"));                    // NULL no coincide
        Assert.Equal([(2, 0)], GridSearch.Find(rows, columns, "2"));
    }
}
