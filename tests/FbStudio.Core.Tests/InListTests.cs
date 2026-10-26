using FbStudio.Core.Export;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>Los valores de una columna como «COL IN (…)».</summary>
public class InListTests
{
    [Fact]
    public void Sin_repetidos_ni_NULL_y_de_a_1500()
    {
        Assert.Equal("ID IN (3, 1, 2)", InList.Format(new ColumnInfo(0, "ID", "INTEGER"), [3, 1, null, 3, 2, DBNull.Value]));
        Assert.Equal("\"Nombre\" IN ('O''Brien', 'Peña')", InList.Format(new ColumnInfo(0, "Nombre", "VARCHAR"), ["O'Brien", "Peña"]));
        Assert.Equal("DIA IN (DATE '2026-10-24')", InList.Format(new ColumnInfo(0, "DIA", "DATE"), [new DateTime(2026, 10, 24)]));
        Assert.Null(InList.Format(new ColumnInfo(0, "ID", "INTEGER"), [null]));

        // Más de 1.500 valores: varios IN unidos con OR; los renglones no pasan de 100 columnas.
        var many = InList.Format(new ColumnInfo(0, "ID", "INTEGER"), Enumerable.Range(1, 3_200).Cast<object?>())!;
        Assert.Equal(3, many.Split("ID IN (").Length - 1);
        Assert.Contains("\n   OR ID IN (1501, ", many);
        Assert.All(many.Split('\n'), l => Assert.True(l.Length <= 100, l));
        Assert.EndsWith("3200)", many);
    }
}
