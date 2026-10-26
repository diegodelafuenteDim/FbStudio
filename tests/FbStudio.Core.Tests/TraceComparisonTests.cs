using FbStudio.Core.Trace;

namespace FbStudio.Core.Tests;

/// <summary>Comparar dos períodos de trace (F-24).</summary>
public class TraceComparisonTests
{
    /// <summary>Un log como el de fbtracemgr con una ejecución por sentencia (SQL y milisegundos).</summary>
    private static IReadOnlyList<TraceQueryGroup> Analysis(params (string Sql, int Ms)[] runs)
    {
        var lines = new List<string>();
        var n = 100;
        foreach (var (sql, ms) in runs)
        {
            lines.AddRange([
                "2026-10-21T22:53:08.9510 (32:0x743ff55e7d40) EXECUTE_STATEMENT_FINISH",
                "\t/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)",
                "\t/usr/local/firebird/bin/isql:92",
                "\t\t(TRA_17, CONCURRENCY | WAIT | READ_WRITE)",
                "",
                $"Statement {n++}:",
                "-------------------------------------------------------------------------------",
                sql,
                "0 records fetched",
                $"      {ms} ms, 4 read(s), 9 fetch(es)",
                "",
            ]);
        }
        return TraceAnalysis.Group(TraceParser.Parse(lines));
    }

    [Fact]
    public void Compara_el_promedio_por_ejecucion_y_ordena_por_el_cambio_del_tiempo_total()
    {
        var before = Analysis(("select a from t where id = 1", 10), ("select a from t where id = 7", 10), ("select b from u", 100), ("select c from x", 5));
        var after = Analysis(("select a from t where id = 2", 30), ("select a from t where id = 3", 30), ("select b from u", 50), ("select d from y", 7));
        var changes = TraceComparison.Compare(before, after);

        Assert.Equal(["select b from u", "select a from t where id = ?", "select d from y", "select c from x"], changes.Select(c => c.Key));
        Assert.Equal([TraceChangeKind.Faster, TraceChangeKind.Slower, TraceChangeKind.New, TraceChangeKind.Gone], changes.Select(c => c.Kind));
        Assert.Equal(-0.5, changes[0].AverageChange);
        Assert.Equal(2.0, changes[1].AverageChange);                           // de 10 ms a 30 ms: el triple
        Assert.Equal((-50L, 40L, 7L, -5L), (changes[0].TotalDifference, changes[1].TotalDifference, changes[2].TotalDifference, changes[3].TotalDifference));
        Assert.Null(changes[2].AverageChange);
    }

    [Fact]
    public void Un_cambio_chico_no_es_mas_lenta_ni_mas_rapida()
    {
        // 1 ms → 2 ms es el doble pero no importa (menos de 5 ms); 100 → 110 es menos del 20 %.
        var changes = TraceComparison.Compare(Analysis(("select x from t", 1), ("select y from t", 100)), Analysis(("select x from t", 2), ("select y from t", 110)));
        Assert.All(changes, c => Assert.Equal(TraceChangeKind.Same, c.Kind));
    }
}
