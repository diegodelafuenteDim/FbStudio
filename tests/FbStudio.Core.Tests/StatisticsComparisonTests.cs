using FbStudio.Core.Statistics;

namespace FbStudio.Core.Tests;

public class StatisticsComparisonTests
{
    private static DatabaseAnalysis Analysis(params (string Table, long Records, long Pages, string? Index, long Depth)[] tables) =>
        DatabaseAnalysis.Parse(tables.SelectMany((t, n) => new[]
        {
            $"{t.Table} ({128 + n})",
            $"    Average record length: 10.00, total records: {t.Records}",
            $"    Data pages: {t.Pages}, average fill: 80%",
        }.Concat(t.Index is null ? [] : [$"    Index {t.Index} (0)", $"\tRoot page: 1, depth: {t.Depth}, leaf buckets: 1, nodes: {t.Records}"]))
            .Prepend("Analyzing database pages ..."));

    [Fact]
    public void Relaciona_por_nombre_y_calcula_las_diferencias()
    {
        var test = Analysis(("TURNOS", 1000, 10, "PK_TURNOS", 2), ("SOLO_PRUEBA", 5, 1, null, 0));
        var prod = Analysis(("TURNOS", 674059, 7000, "PK_TURNOS", 3), ("SOLO_PROD", 1, 1, "IX_SOLO", 1));

        var c = StatisticsComparison.Compare(test, prod);

        Assert.Equal(["SOLO_PROD", "SOLO_PRUEBA", "TURNOS"], c.Tables.Select(t => t.Name));
        var turnos = c.Tables.Single(t => t.Name == "TURNOS");
        Assert.Equal((673059L, 6990L), (turnos.RecordsDifference, turnos.DataPagesDifference));
        Assert.Null(c.Tables.Single(t => t.Name == "SOLO_PROD").A);
        Assert.Null(c.Tables.Single(t => t.Name == "SOLO_PRUEBA").B);
        Assert.Null(c.Tables.Single(t => t.Name == "SOLO_PRUEBA").RecordsDifference);

        Assert.Equal([("SOLO_PROD", "IX_SOLO"), ("TURNOS", "PK_TURNOS")], c.Indexes.Select(i => (i.Table, i.Name)));
        Assert.True(c.Indexes.Single(i => i.Name == "PK_TURNOS").DeeperInB);
        Assert.False(c.Indexes.Single(i => i.Name == "IX_SOLO").DeeperInB);   // no está en A
    }
}
