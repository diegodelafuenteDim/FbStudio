using System.Diagnostics;

namespace FbStudio.Firebird.Tests;

[Collection(nameof(FirebirdCollection))]
public class QuerySessionTests(FirebirdFixture fb)
{
    [Fact]
    public async Task Select_trae_hasta_el_limite_con_plan_detallado_y_lecturas_indexadas()
    {
        await using var s = fb.NewSession();
        var (o, rows) = await FirebirdFixture.RunAsync(s, "SELECT * FROM TURNOS WHERE FECHA >= '2026-01-10'", maxRows: 10_000);

        Assert.Null(o.Error);
        Assert.Equal(10_000, rows.Count);
        Assert.True(o.LimitReached);
        Assert.Equal(5, o.Columns.Length);
        Assert.True(o.PlanIsExplained);
        Assert.Contains("TURNOS_FECHA", o.Plan);
        var reads = Assert.Single(o.Reads, r => r.Table == "TURNOS");
        Assert.True(reads.Indexed >= 10_000);
        Assert.Equal(0, reads.NonIndexed);
        Assert.False(o.TransactionOpen); // un SELECT no deja transacciones abiertas
    }

    [Fact]
    public async Task En_modo_script_no_pide_plan_ni_lecturas_y_las_filas_solo_se_cuentan()
    {
        await using var s = fb.NewSession();
        var channel = System.Threading.Channels.Channel.CreateUnbounded<object[][]>();
        var columns = 0;
        const string sql = "SELECT ID FROM TURNOS WHERE ID < 7";
        var o = await s.ExecuteAsync(sql, FbStudio.Core.Sql.StatementClassifier.Classify(sql), new ExecuteOptions(MaxRows: 5, Script: true),
            _ => columns++, channel.Writer);
        Assert.Null(o.Error);
        Assert.Equal((5L, true), (o.RowsFetched, o.LimitReached));          // contadas hasta el límite
        Assert.Equal("ID", Assert.Single(o.Columns).Name);
        Assert.Equal(0, columns);                                             // la grilla no se entera
        Assert.False(channel.Reader.TryRead(out _));
        Assert.Null(o.Plan);
        Assert.Empty(o.Reads);
    }

    [Fact]
    public async Task Lectura_sin_indice_aparece_como_no_indexada()
    {
        await using var s = fb.NewSession();
        var (o, _) = await FirebirdFixture.RunAsync(s, "SELECT COUNT(*) FROM TURNOS WHERE OBS LIKE 'z%'");
        Assert.Null(o.Error);
        Assert.Equal(FirebirdFixture.Rows, Assert.Single(o.Reads, r => r.Table == "TURNOS").NonIndexed);
    }

    [Fact]
    public async Task Cancelar_corta_en_el_servidor_y_la_sesion_sigue_usable()
    {
        await using var s = fb.NewSession();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        var sw = Stopwatch.StartNew();
        var (o, rows) = await FirebirdFixture.RunAsync(s, "SELECT a.ID, b.ID FROM TURNOS a CROSS JOIN TURNOS b", maxRows: int.MaxValue, ct: cts.Token);

        Assert.True(o.Cancelled, o.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"tardó {sw.Elapsed}");
        Assert.True(o.CancelLatency < TimeSpan.FromSeconds(2), $"latencia {o.CancelLatency}");
        Assert.NotEmpty(rows);

        var (after, one) = await FirebirdFixture.RunAsync(s, "SELECT 1 FROM RDB$DATABASE");
        Assert.Null(after.Error);
        Assert.Single(one);
    }

    [Fact]
    public async Task DML_abre_transaccion_y_rollback_descarta_los_cambios()
    {
        await using var s = fb.NewSession();
        var (upd, _) = await FirebirdFixture.RunAsync(s, "UPDATE TURNOS SET IMPORTE = -1 WHERE ID < 10");
        Assert.Null(upd.Error);
        Assert.Equal(10, upd.RecordsAffected);
        Assert.True(upd.TransactionOpen);
        Assert.True(s.InTransaction);

        // Dentro de la misma transacción el cambio se ve...
        var (_, inside) = await FirebirdFixture.RunAsync(s, "SELECT COUNT(*) FROM TURNOS WHERE IMPORTE = -1");
        Assert.Equal(10, Convert.ToInt32(inside[0][0]));

        // ...y después del rollback ya no.
        await s.RollbackAsync();
        Assert.False(s.InTransaction);
        var (_, after) = await FirebirdFixture.RunAsync(s, "SELECT COUNT(*) FROM TURNOS WHERE IMPORTE = -1");
        Assert.Equal(0, Convert.ToInt32(after[0][0]));
    }

    [Fact]
    public async Task COMMIT_escrito_en_el_editor_confirma_y_otra_sesion_lo_ve()
    {
        await using var s = fb.NewSession();
        await FirebirdFixture.RunAsync(s, "UPDATE TURNOS SET PACIENTE = 'COMMIT-TEST' WHERE ID = 49999");
        Assert.True(s.InTransaction);
        var (commit, _) = await FirebirdFixture.RunAsync(s, "COMMIT");
        Assert.Null(commit.Error);
        Assert.False(s.InTransaction);

        await using var other = fb.NewSession();
        var (_, rows) = await FirebirdFixture.RunAsync(other, "SELECT PACIENTE FROM TURNOS WHERE ID = 49999");
        Assert.Equal("COMMIT-TEST", rows[0][0]);
    }

    [Fact]
    public async Task Un_error_no_deja_la_transaccion_colgada()
    {
        await using var s = fb.NewSession();
        var (o, _) = await FirebirdFixture.RunAsync(s, "UPDATE NO_EXISTE SET A = 1 WHERE B = 2");
        Assert.NotNull(o.Error);
        Assert.False(o.TransactionOpen);
        Assert.False(s.InTransaction);
    }

    [Fact]
    public async Task Perfil_de_solo_lectura_no_ejecuta_DML_aunque_se_saltee_la_UI()
    {
        await using var s = fb.NewSession(readOnly: true);
        var (o, _) = await FirebirdFixture.RunAsync(s, "UPDATE TURNOS SET IMPORTE = -2 WHERE ID = 1");
        Assert.Contains("solo lectura", o.Error);

        var (_, rows) = await FirebirdFixture.RunAsync(s, "SELECT COUNT(*) FROM TURNOS WHERE IMPORTE = -2");
        Assert.Equal(0, Convert.ToInt32(rows[0][0]));
    }

    /// <summary>
    /// Un SELECT no deja una transacción abierta que frene la OAT (y con ella la recolección de basura); una sentencia
    /// de escritura sí, hasta el Commit o Rollback. Es la diferencia con dejar un SELECT en una transacción larga.
    /// </summary>
    [Fact]
    public async Task Un_SELECT_no_frena_la_transaccion_activa_mas_vieja_y_un_UPDATE_si()
    {
        await using var s = fb.NewSession();
        var (select, _) = await FirebirdFixture.RunAsync(s, "SELECT FIRST 10 * FROM TURNOS");
        Assert.Null(select.Error);
        Assert.False(s.InTransaction);
        var (oat, mine) = await OldestActiveAsync();
        Assert.Equal(mine, oat); // la más vieja activa es la transacción recién abierta para mirar: nada quedó colgado

        var (update, _) = await FirebirdFixture.RunAsync(s, "UPDATE TURNOS SET IMPORTE = IMPORTE WHERE ID = 3");
        Assert.True(update.TransactionOpen);
        var (held, now) = await OldestActiveAsync();
        Assert.True(held < now, $"con el UPDATE sin confirmar la OAT queda atrás ({held} < {now})");

        await s.RollbackAsync();
        var (released, after) = await OldestActiveAsync();
        Assert.Equal(after, released);
    }

    [Fact]
    public async Task La_espera_de_un_registro_bloqueado_respeta_la_opcion()
    {
        await using var owner = fb.NewSession();
        await using var waiter = fb.NewSession();
        waiter.LockTimeout = TimeSpan.FromSeconds(1);
        var (first, _) = await FirebirdFixture.RunAsync(owner, "UPDATE TURNOS SET OBS = OBS WHERE ID = 6");
        Assert.Null(first.Error);

        var clock = Stopwatch.StartNew();
        var (second, _) = await FirebirdFixture.RunAsync(waiter, "UPDATE TURNOS SET OBS = OBS WHERE ID = 6");
        clock.Stop();

        Assert.Contains("lock", second.Error, StringComparison.OrdinalIgnoreCase); // "lock time-out on wait transaction"
        // Cortó mucho antes que los 10 s de antes. No se pide un mínimo: Firebird mide la espera con granularidad
        // gruesa y con 1 s se lo vio cortar entre 0,16 y 0,5 s.
        Assert.True(clock.Elapsed.TotalSeconds < 6, $"tardó {clock.Elapsed.TotalSeconds:N1} s");
        Assert.False(waiter.InTransaction);
        await owner.RollbackAsync();
    }

    [Fact]
    public async Task La_sesion_conoce_su_transaccion_y_MON_dice_de_quien_es()
    {
        await using var s = fb.NewSession();
        Assert.Null(s.TransactionId);
        await FirebirdFixture.RunAsync(s, "UPDATE TURNOS SET OBS = OBS WHERE ID = 4");
        Assert.NotNull(s.TransactionId);
        var id = s.TransactionId!.Value;

        await using var ex = new MetadataExplorer(fb.Profile, fb.Password);
        var owner = await ex.TransactionOwnerAsync(id);
        Assert.NotNull(owner);
        Assert.True(owner.AttachmentId > 0);
        Assert.Equal(fb.Profile.User.ToUpperInvariant(), owner.User?.ToUpperInvariant());

        await s.RollbackAsync();
        Assert.Null(s.TransactionId);
        Assert.Null(await ex.TransactionOwnerAsync(id)); // ya no existe
    }

    [Fact]
    public async Task Cuenta_las_filas_de_un_SELECT_para_la_exportacion()
    {
        Assert.Equal(10, await QuerySession.CountRowsAsync(fb.Profile, fb.Password, "SELECT ID, FECHA FROM TURNOS WHERE ID < 10 ORDER BY ID;"));
        Assert.Equal(FirebirdFixture.Rows, await QuerySession.CountRowsAsync(fb.Profile, fb.Password, "SELECT * FROM TURNOS"));
        Assert.Null(await QuerySession.CountRowsAsync(fb.Profile, fb.Password, "SELECT * FROM NO_EXISTE"));
    }

    /// <summary>OAT del encabezado de la base y número de la transacción con la que se mira (desde otra conexión).</summary>
    private async Task<(long Oat, long Current)> OldestActiveAsync()
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test-oat");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FirebirdSql.Data.FirebirdClient.FbCommand("SELECT MON$OLDEST_ACTIVE, CURRENT_TRANSACTION FROM MON$DATABASE", c, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        var result = (Convert.ToInt64(r.GetValue(0)), Convert.ToInt64(r.GetValue(1)));
        await tx.CommitAsync();
        return result;
    }

    [Fact]
    public async Task Catalogo_de_metadatos_para_autocompletar()
    {
        var catalog = await MetadataReader.LoadCatalogAsync(fb.Profile, fb.Password);
        Assert.Equal(["ID", "FECHA", "PACIENTE", "OBS", "IMPORTE"], catalog.Columns["TURNOS"]);

        // F-33: funciones, paquetes, generadores, excepciones y las firmas.
        Assert.Contains("F_DOBLE", catalog.Functions);
        Assert.DoesNotContain("CANTIDAD", catalog.Functions);                              // la del paquete va con el paquete
        Assert.Contains("F_DOBLE", catalog.StoredFunctions);                               // PSQL: puede escribir (D-73)
        Assert.Equal(["PKG_TURNOS"], catalog.Packages);
        Assert.Equal(["CANTIDAD"], catalog.PackageMembers["PKG_TURNOS"]);
        Assert.Contains("G_TURNOS", catalog.Generators);
        Assert.Contains("E_TURNO", catalog.Exceptions);
        Assert.Equal("P_TURNOS_DESDE(DESDE INTEGER, HASTA INTEGER = …)", catalog.Signatures["P_TURNOS_DESDE"].ToString());
        Assert.Equal("P_CONTAR()", catalog.Signatures["P_CONTAR"].ToString());
        Assert.Equal("F_DOBLE(X INTEGER) RETURNS INTEGER", catalog.Signatures["f_doble"].ToString());
        Assert.Equal("PKG_TURNOS.CANTIDAD() RETURNS INTEGER", catalog.Signatures["PKG_TURNOS.CANTIDAD"].ToString());
    }
}
