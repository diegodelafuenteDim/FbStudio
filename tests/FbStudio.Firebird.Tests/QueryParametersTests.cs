using System.Threading.Channels;
using FbStudio.Core.Sql;

namespace FbStudio.Firebird.Tests;

/// <summary>Consultas con parámetros «?» y «:nombre» (F-25) contra Firebird 3 en Docker.</summary>
[Collection(nameof(FirebirdCollection))]
public class QueryParametersTests(FirebirdFixture fb)
{
    private static async Task<(Core.Results.QueryOutcome Outcome, List<object[]> Rows)> RunAsync(QuerySession s, string sql, IReadOnlyList<object?> values)
    {
        var channel = Channel.CreateBounded<object[][]>(64);
        var rows = new List<object[]>();
        var consumer = Task.Run(async () => { await foreach (var b in channel.Reader.ReadAllAsync()) rows.AddRange(b); });
        var outcome = await s.ExecuteAsync(sql, StatementClassifier.Classify(sql), new ExecuteOptions(Parameters: values), _ => { }, channel.Writer);
        await consumer;
        return (outcome, rows);
    }

    [Fact]
    public async Task Los_nombres_repetidos_van_con_el_mismo_valor_y_Firebird_convierte_los_textos()
    {
        var p = SqlParameters.Find("SELECT ID FROM TURNOS WHERE ID BETWEEN :desde AND :hasta AND FECHA >= :fecha AND ID <> :desde ORDER BY ID");
        var values = p.Values(new Dictionary<string, object?>
        {
            ["desde"] = "10",                                                  // texto: Firebird lo convierte a INTEGER
            ["hasta"] = 15,
            ["fecha"] = new DateTime(2026, 1, 1),
        });
        await using var s = fb.NewSession();
        var (o, rows) = await RunAsync(s, p.Sql, values);
        Assert.Null(o.Error);
        Assert.Equal([11, 12, 13, 14, 15], rows.Select(r => Convert.ToInt32(r[0])));
    }

    [Fact]
    public async Task NULL_y_contar_filas_con_parametros()
    {
        await using var s = fb.NewSession();
        var (o, rows) = await RunAsync(s, "SELECT COUNT(*) FROM TURNOS WHERE ? IS NULL OR ID < ?", [null, 5]);
        Assert.Null(o.Error);
        Assert.Equal(50_000, Convert.ToInt32(rows[0][0]));                   // el primero es NULL: cuentan todas

        var count = await QuerySession.CountRowsAsync(fb.Profile, fb.Password, "SELECT ID FROM TURNOS WHERE ID < ?", parameters: [100]);
        Assert.Equal(100, count);
    }
}
