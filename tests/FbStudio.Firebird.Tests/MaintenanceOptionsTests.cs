using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>
/// Las opciones de gbak y de la validación en línea que se sumaron a Mantenimiento, contra Firebird 3 en Docker. Con una
/// base propia, para no cambiar la compartida.
/// </summary>
[Collection(nameof(FirebirdCollection))]
public class MaintenanceOptionsTests(FirebirdFixture fb)
{
    private async Task ExecAsync(ConnectionProfile profile, string sql)
    {
        await using var c = FbConnectionFactory.Create(profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var cmd = new FbCommand(sql, c, tx)) await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private async Task<object?> ScalarAsync(ConnectionProfile profile, string sql)
    {
        await using var c = FbConnectionFactory.Create(profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = new FbCommand(sql, c, tx);
        return await cmd.ExecuteScalarAsync();
    }

    private async Task<string> CountsAsync(ConnectionProfile profile) =>
        $"{await ScalarAsync(profile, "SELECT COUNT(*) FROM T_A")}/{await ScalarAsync(profile, "SELECT COUNT(*) FROM T_B")}/{await ScalarAsync(profile, "SELECT COUNT(*) FROM TXA")}";

    [Fact]
    public async Task Saltea_datos_de_tablas_restaura_sin_NOT_NULL_y_valida_solo_lo_elegido()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var db = fb.Profile with { Name = "docker-opciones", Database = $"/firebird/data/opt_{id}.fdb", User = "SYSDBA" };
        await FbConnection.CreateDatabaseAsync(FbConnectionFactory.ConnectionString(db, fb.Password), 8192, true, true);
        // T_B y TXA: «_» es comodín en SIMILAR TO; saltear T_B no tiene que saltear TXA, ni al revés.
        await ExecAsync(db, "CREATE TABLE T_A (ID INTEGER, Z INTEGER NOT NULL)");
        await ExecAsync(db, "CREATE TABLE T_B (ID INTEGER)");
        await ExecAsync(db, "CREATE TABLE TXA (ID INTEGER)");
        await ExecAsync(db, "EXECUTE BLOCK AS BEGIN INSERT INTO T_A VALUES (1, 1); INSERT INTO T_B VALUES (1); INSERT INTO TXA VALUES (1); END");

        var backup = $"/firebird/data/opt_{id}.fbk";
        await DatabaseServices.BackupAsync(db, fb.Password, backup,
            new BackupOptions(IgnoreChecksums: true, NonTransportable: true, SkipData: ["T_B"]), _ => { });

        var plain = db with { Database = $"/firebird/data/opt_{id}_a.fdb" };
        await DatabaseServices.RestoreAsync(db, fb.Password, backup, plain.Database, new RestoreOptions(), _ => { });
        Assert.Equal("1/0/1", await CountsAsync(plain));                   // T_B sin datos en el backup; TXA entera
        Assert.Equal(1, Convert.ToInt32(await ScalarAsync(plain,
            "SELECT RDB$NULL_FLAG FROM RDB$RELATION_FIELDS WHERE RDB$RELATION_NAME = 'T_A' AND RDB$FIELD_NAME = 'Z'")));

        // Sin NOT NULL (gbak -n), tabla por tabla, sin shadow y salteando TXA al restaurar.
        var rescue = db with { Database = $"/firebird/data/opt_{id}_b.fdb" };
        await DatabaseServices.RestoreAsync(db, fb.Password, backup, rescue.Database,
            new RestoreOptions(NoValidity: true, OneAtATime: true, NoShadow: true, SkipData: ["TXA"]), _ => { });
        Assert.Equal("1/0/0", await CountsAsync(rescue));
        Assert.Equal(DBNull.Value, await ScalarAsync(rescue,
            "SELECT RDB$NULL_FLAG FROM RDB$RELATION_FIELDS WHERE RDB$RELATION_NAME = 'T_A' AND RDB$FIELD_NAME = 'Z'"));
        await ExecAsync(rescue, "INSERT INTO T_A VALUES (2, NULL)");         // la regla NOT NULL ya no está

        // Validación en línea de una sola tabla.
        var result = await DatabaseServices.ValidateAsync(db, fb.Password, tables: ["T_A"]);
        Assert.Equal(0, result.Errors);
        Assert.Contains(result.Lines, l => l.Contains("(T_A)", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Lines, l => l.Contains("(TXA)", StringComparison.Ordinal) || l.Contains("(T_B)", StringComparison.Ordinal));
    }
}
