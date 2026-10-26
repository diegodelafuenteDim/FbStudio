using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>Los bloques que se pueden plegar en el editor.</summary>
public class SqlFoldingTests
{
    private static string[] Hidden(string text) => SqlFolding.Find(text).Select(r => text[r.Start..r.End].Trim()).ToArray();

    [Fact]
    public void Bloques_de_varios_renglones()
    {
        const string psql = """
            CREATE PROCEDURE P AS
            BEGIN
              /* recorre
                 los turnos */
              FOR SELECT ID FROM TURNOS INTO :ID DO
              BEGIN
                X = CASE WHEN ID > 1
                    THEN 'end' ELSE 0 END;
              END
            END
            """;
        var hidden = Hidden(psql);
        Assert.Equal(4, hidden.Length);
        Assert.StartsWith("/* recorre", hidden[0]);                              // el de afuera, primero (ordenados por comienzo)
        Assert.StartsWith("recorre", hidden[1]);                                 // el comentario
        Assert.StartsWith("X = CASE", hidden[2]);                                 // el BEGIN de adentro
        Assert.Equal("WHEN ID > 1\n        THEN 'end' ELSE 0", hidden[3].Replace("\r\n", "\n")); // un «end» en un texto no cierra

        // Paréntesis de varios renglones (una subconsulta), no los de un renglón; uno sin cerrar no rompe nada.
        Assert.Equal(["SELECT ID\n  FROM TURNOS"], Hidden("SELECT * FROM T WHERE ID IN (\n  SELECT ID\n  FROM TURNOS\n) AND F(1, 2) > 0"));
        Assert.Empty(Hidden("SELECT COUNT(*) FROM T"));
        Assert.Single(Hidden("BEGIN\n  X = (1 +\nEND"));
    }
}
