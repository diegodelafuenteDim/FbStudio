using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>Los puntos de un gráfico rápido de un resultado.</summary>
public class ChartDataTests
{
    [Fact]
    public void Suma_o_cuenta_por_cada_X_ordenando_numeros_y_fechas()
    {
        var medico = new ColumnInfo(0, "MEDICO", "VARCHAR");
        var importe = new ColumnInfo(1, "IMPORTE", "NUMERIC");
        var dia = new ColumnInfo(2, "DIA", "DATE");
        object?[][] rows =
        [
            ["Pérez", 10.5m, new DateTime(2026, 10, 3)],
            ["Gómez", 4m, new DateTime(2026, 10, 1)],
            ["Pérez", null, new DateTime(2026, 10, 3)],
            [null, 1m, new DateTime(2026, 10, 2)],
        ];
        // Un texto queda en el orden en que aparece; Y se suma (NULL no suma); sin Y, la cantidad de filas.
        Assert.Equal([new ChartPoint("Pérez", 10.5), new ChartPoint("Gómez", 4), new ChartPoint("NULL", 1)], ChartData.Build(medico, importe, rows).Points);
        Assert.Equal([2d, 1d, 1d], ChartData.Build(medico, null, rows).Points.Select(p => p.Value));
        // Una fecha se ordena por fecha.
        Assert.Equal(["2026-10-01", "2026-10-02", "2026-10-03"], ChartData.Build(dia, null, rows).Points.Select(p => p.Label));

        var many = Enumerable.Range(0, 600).Select(i => (IReadOnlyList<object?>)[i]).ToList();
        var (points, truncated) = ChartData.Build(new ColumnInfo(0, "N", "INTEGER"), null, many);
        Assert.Equal((ChartData.MaxPoints, true), (points.Count, truncated));
    }

    [Fact]
    public void Divisiones_redondas_que_incluyen_el_cero()
    {
        Assert.Equal([0d, 5, 10, 15], ChartData.Ticks(0, 10.5));
        Assert.Equal([0d, 200, 400, 600, 800, 1000], ChartData.Ticks(3, 1000));
        Assert.Equal([-20d, -10, 0, 10, 20, 30], ChartData.Ticks(-15, 25));
        Assert.Equal([0d, 0.2, 0.4, 0.6, 0.8, 1], ChartData.Ticks(0, 0));
    }
}
