using System.Threading.Channels;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;

namespace FbStudio.Firebird.Tests;

/// <summary>Savepoints, el corte de un script y el autocommit en un script (D-120), contra Firebird 3 en Docker.</summary>
[Collection(nameof(FirebirdCollection))]
public class ScriptTransactionTests(FirebirdFixture fb)
{
    private static async Task<QueryOutcome> ExecAsync(QuerySession s, string sql, bool inScript = false)
    {
        var channel = Channel.CreateUnbounded<object[][]>();
        return await s.ExecuteAsync(sql, StatementClassifier.Classify(sql), new ExecuteOptions(InScript: inScript), _ => { }, channel.Writer);
    }

    private static async Task<object?> ObsAsync(QuerySession s, int id)
    {
        var (o, rows) = await FirebirdFixture.RunAsync(s, $"SELECT OBS FROM TURNOS WHERE ID = {id}");
        Assert.Null(o.Error);
        return rows[0][0] is DBNull ? null : rows[0][0];
    }

    [Fact]
    public async Task ROLLBACK_TO_SAVEPOINT_vuelve_solo_hasta_el_savepoint()
    {
        await using var s = fb.NewSession();
        Assert.Null((await ExecAsync(s, "UPDATE TURNOS SET OBS = 'antes' WHERE ID = 45001")).Error);
        Assert.Null((await ExecAsync(s, "SAVEPOINT S1")).Error);
        Assert.Null((await ExecAsync(s, "UPDATE TURNOS SET OBS = 'despues' WHERE ID = 45001")).Error);
        Assert.Equal("despues", await ObsAsync(s, 45001));

        var back = await ExecAsync(s, "ROLLBACK TO SAVEPOINT S1");
        Assert.Null(back.Error);
        Assert.True(s.InTransaction);                                  // antes deshacía toda la transacción
        Assert.Equal("antes", await ObsAsync(s, 45001));
        Assert.Null((await ExecAsync(s, "RELEASE SAVEPOINT S1 ONLY")).Error);
        Assert.True(s.InTransaction);
        await s.RollbackAsync();
    }

    [Fact]
    public async Task Un_SAVEPOINT_abre_la_transaccion_de_escritura_y_uno_que_no_existe_da_error()
    {
        await using var s = fb.NewSession();
        var original = await ObsAsync(s, 45002);
        Assert.Null((await ExecAsync(s, "SAVEPOINT INICIO")).Error);
        Assert.True(s.InTransaction);                                  // en una de solo lectura se perdería enseguida
        await ExecAsync(s, "UPDATE TURNOS SET OBS = 'x' WHERE ID = 45002");
        Assert.Null((await ExecAsync(s, "ROLLBACK TO SAVEPOINT INICIO")).Error);
        Assert.Equal(original, await ObsAsync(s, 45002));

        var unknown = await ExecAsync(s, "ROLLBACK TO SAVEPOINT NO_EXISTE");
        Assert.NotNull(unknown.Error);
        Assert.True(s.InTransaction);                                  // el error no se lleva la transacción
        await s.RollbackAsync();
    }

    [Fact]
    public async Task El_corte_de_un_script_deshace_solo_lo_suyo()
    {
        await using var s = fb.NewSession();
        var original = await ObsAsync(s, 45004);

        // Sin transacción abierta al empezar: el rollback se lleva lo que hizo el script.
        await s.BeginScriptAsync();
        await ExecAsync(s, "UPDATE TURNOS SET OBS = 'script' WHERE ID = 45003", inScript: true);
        Assert.Equal(ScriptUndo.Transaction, await s.UndoScriptAsync());
        await s.EndScriptAsync();
        Assert.False(s.InTransaction);

        // Con algo pendiente de antes: vuelve al savepoint del comienzo y lo de antes queda.
        await ExecAsync(s, "UPDATE TURNOS SET OBS = 'de antes' WHERE ID = 45004");
        await s.BeginScriptAsync();
        await ExecAsync(s, "UPDATE TURNOS SET OBS = 'del script' WHERE ID = 45004", inScript: true);
        Assert.Equal(ScriptUndo.ToScriptStart, await s.UndoScriptAsync());
        await s.EndScriptAsync();
        Assert.True(s.InTransaction);
        Assert.Equal("de antes", await ObsAsync(s, 45004));

        // Si el script hizo COMMIT, lo que siga va en otra transacción: se deshace eso, lo confirmado queda.
        await s.BeginScriptAsync();
        await ExecAsync(s, "COMMIT", inScript: true);
        await ExecAsync(s, "UPDATE TURNOS SET OBS = 'despues del commit' WHERE ID = 45004", inScript: true);
        Assert.Equal(ScriptUndo.Transaction, await s.UndoScriptAsync());
        await s.EndScriptAsync();
        Assert.False(s.InTransaction);
        Assert.Equal("de antes", await ObsAsync(s, 45004));

        Assert.Equal(ScriptUndo.Nothing, await s.UndoScriptAsync());   // sin transacción no hay nada que deshacer
        await ExecAsync(s, $"UPDATE TURNOS SET OBS = {(original is null ? "NULL" : SqlIdentifier.Literal((string)original))} WHERE ID = 45004");
        await s.CommitAsync();
    }

    [Fact]
    public async Task Un_script_no_usa_el_autocommit_ni_lo_confirma_despues()
    {
        await using var s = fb.NewSession();
        s.AutoCommit = true;
        var o = await ExecAsync(s, "UPDATE TURNOS SET OBS = 'en script' WHERE ID = 45005", inScript: true);
        Assert.Null(o.Error);
        Assert.False(o.AutoCommitted);
        Assert.True(s.InTransaction);                                  // queda para su COMMIT, como en producción

        // Una sentencia suelta con autocommit no confirma lo que dejó abierto el script.
        var single = await ExecAsync(s, "UPDATE TURNOS SET OBS = 'suelta' WHERE ID = 45006");
        Assert.False(single.AutoCommitted);
        Assert.True(s.InTransaction);
        await s.RollbackAsync();

        // Sin nada abierto, el autocommit sigue como siempre.
        var alone = await ExecAsync(s, "UPDATE TURNOS SET OBS = OBS WHERE ID = 45006");
        Assert.True(alone.AutoCommitted);
        Assert.False(s.InTransaction);
    }

    [Fact]
    public async Task Una_orden_de_isql_no_llega_al_servidor()
    {
        await using var s = fb.NewSession();
        var o = await ExecAsync(s, "SET BAIL ON");
        Assert.Contains("isql", o.Error);
        Assert.DoesNotContain("Token unknown", o.Error);
        Assert.False(s.InTransaction);
    }
}
