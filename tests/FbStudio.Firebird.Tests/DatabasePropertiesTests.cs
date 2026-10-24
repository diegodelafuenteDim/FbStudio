using FbStudio.Core.Profiles;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>Propiedades de la base (F-34) contra Firebird 3 en Docker, con una base propia.</summary>
[Collection(nameof(FirebirdCollection))]
public class DatabasePropertiesTests(FirebirdFixture fb)
{
    private async Task<ConnectionProfile> NewDatabaseAsync()
    {
        var profile = fb.Profile with { Name = "docker-propiedades", Database = $"/firebird/data/prop_{Guid.NewGuid():N}.fdb", User = "SYSDBA" };
        await FbConnection.CreateDatabaseAsync(FbConnectionFactory.ConnectionString(profile, fb.Password), 8192, true, true);
        await using var c = FbConnectionFactory.Create(profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var cmd = new FbCommand("CREATE TABLE T (ID INTEGER)", c, tx)) await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return profile;
    }

    private async Task InsertAsync(ConnectionProfile profile)
    {
        await using var c = FbConnectionFactory.Create(profile, fb.Password, "test");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var cmd = new FbCommand("INSERT INTO T VALUES (1)", c, tx)) await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    [Fact]
    public async Task Lee_y_cambia_sweep_escrituras_forzadas_cache_y_solo_lectura()
    {
        var db = await NewDatabaseAsync();
        FbConnection.ClearAllPools();
        var before = await DatabaseProperties.ReadAsync(db, fb.Password);
        Assert.Equal(new DatabaseSettings(20_000, true, false, 0), before);

        var after = new DatabaseSettings(5_000, false, false, 512);
        await DatabaseProperties.ApplyAsync(db, fb.Password, before, after);
        Assert.Equal(after, await DatabaseProperties.ReadAsync(db, fb.Password));

        // Solo lectura: no acepta escrituras; volver la deja como estaba.
        var readOnly = after with { ReadOnly = true, SweepInterval = 0 };
        await DatabaseProperties.ApplyAsync(db, fb.Password, after, readOnly);
        Assert.Equal(readOnly, await DatabaseProperties.ReadAsync(db, fb.Password));
        await Assert.ThrowsAsync<FbException>(() => InsertAsync(db));
        var back = readOnly with { ReadOnly = false, ForcedWrites = true, SweepInterval = 20_000 };
        await DatabaseProperties.ApplyAsync(db, fb.Password, readOnly, back);
        Assert.Equal(back, await DatabaseProperties.ReadAsync(db, fb.Password));
        await InsertAsync(db);

        // Pasar a solo lectura con alguien conectado: Firebird lo rechaza.
        await using (var other = FbConnectionFactory.Create(db, fb.Password, "otro"))
        {
            await other.OpenAsync();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => DatabaseProperties.ApplyAsync(db, fb.Password, back, back with { ReadOnly = true }));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseProperties.ApplyAsync(db with { ReadOnly = true }, fb.Password, back, back with { SweepInterval = 1 }));
    }
}
