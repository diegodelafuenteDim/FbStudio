using FbStudio.Core.Export;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;
using FirebirdSql.Data.FirebirdClient;

namespace FbStudio.Firebird.Tests;

/// <summary>El script INSERT se vuelve a ejecutar en Firebird y deja las filas iguales (todo en una transacción que se deshace).</summary>
[Collection(nameof(FirebirdCollection))]
public sealed class InsertScriptTests(FirebirdFixture fb) : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fbstudio-insert-{Guid.NewGuid():N}.sql");

    [Fact]
    public async Task El_script_reinserta_las_mismas_filas()
    {
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test-insert");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            // Valores difíciles: comillas, acentos, NULL, decimales y fecha con hora.
            await Exec(c, tx, "UPDATE TURNOS SET PACIENTE = 'O''Brien ñandú', OBS = NULL, IMPORTE = -12.34 WHERE ID = 1");
            var (columns, before) = await Read(c, tx);
            new InsertScriptExporter("turnos").Write(_path, columns, before);

            await Exec(c, tx, "DELETE FROM TURNOS WHERE ID < 5");
            foreach (var statement in SqlScriptSplitter.Split(await File.ReadAllTextAsync(_path)))
                if (StatementClassifier.Classify(statement.Text).Kind != StatementKind.Transaction) await Exec(c, tx, statement.Text);

            var (_, after) = await Read(c, tx);
            Assert.Equal(5, after.Count);
            for (var i = 0; i < before.Count; i++) Assert.Equal(before[i], after[i]);
            Assert.Equal("O'Brien ñandú", after[1][2]);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Las_filas_copiadas_como_INSERT_y_UPDATE_corren_tal_cual()
    {
        // «Copiar como INSERT / UPDATE» de la grilla: lo pegado en un script tiene que dejar las filas igual.
        await using var c = FbConnectionFactory.Create(fb.Profile, fb.Password, "test-insert");
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            await Exec(c, tx, "UPDATE TURNOS SET PACIENTE = 'O''Brien ñandú', OBS = NULL, IMPORTE = -12.34, FECHA = TIMESTAMP '2026-07-01 08:30:15.1234' WHERE ID = 1");
            var (columns, before) = await Read(c, tx);
            var target = new RowScriptTarget("turnos", ["ID"], new HashSet<string>());
            var inserts = RowScript.Insert(target, columns, before);
            Assert.True(inserts.StartsWith("INSERT INTO TURNOS (ID, FECHA, PACIENTE, OBS, IMPORTE) VALUES (0, TIMESTAMP '2026-01-01 00:00:00.0000', 'PACIENTE 0', '', 0);", StringComparison.Ordinal), inserts.Split('\n')[0]);

            await Exec(c, tx, "DELETE FROM TURNOS WHERE ID < 5");
            foreach (var statement in SqlScriptSplitter.Split(inserts)) await Exec(c, tx, statement.Text);
            var (_, after) = await Read(c, tx);
            Assert.Equal(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++) Assert.Equal(before[i], after[i]);

            // UPDATE: se cambian las filas a mano, se pega el UPDATE copiado antes y vuelven a estar como estaban.
            var updates = RowScript.Update(target, columns, before);
            Assert.Contains("UPDATE TURNOS SET FECHA = TIMESTAMP '2026-07-01 08:30:15.1234', PACIENTE = 'O''Brien ñandú', OBS = NULL, IMPORTE = -12.34 WHERE ID = 1;", updates);
            await Exec(c, tx, "UPDATE TURNOS SET PACIENTE = 'otro', IMPORTE = 0, OBS = 'x' WHERE ID < 5");
            foreach (var statement in SqlScriptSplitter.Split(updates)) await Exec(c, tx, statement.Text);
            var (_, restored) = await Read(c, tx);
            for (var i = 0; i < before.Count; i++) Assert.Equal(before[i], restored[i]);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    private static async Task<(ColumnInfo[] Columns, List<object[]> Rows)> Read(FbConnection c, FbTransaction tx)
    {
        await using var cmd = new FbCommand("SELECT ID, FECHA, PACIENTE, OBS, IMPORTE FROM TURNOS WHERE ID < 5 ORDER BY ID", c, tx);
        await using var reader = await cmd.ExecuteReaderAsync();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new ColumnInfo(i, reader.GetName(i), reader.GetDataTypeName(i))).ToArray();
        var rows = new List<object[]>();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }
        return (columns, rows);
    }

    private static async Task Exec(FbConnection c, FbTransaction tx, string sql)
    {
        await using var cmd = new FbCommand(sql, c, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose() => File.Delete(_path);
}
