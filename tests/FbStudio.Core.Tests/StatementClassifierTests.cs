using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class StatementClassifierTests
{
    [Theory]
    [InlineData("SELECT * FROM T", StatementKind.Select, "SELECT")]
    [InlineData("  -- comentario\n select 1 from rdb$database", StatementKind.Select, "SELECT")]
    [InlineData("/* bloque */ WITH x AS (SELECT 1 FROM RDB$DATABASE) SELECT * FROM x", StatementKind.Select, "WITH")]
    [InlineData("INSERT INTO T (A) VALUES (1)", StatementKind.Dml, "INSERT")]
    [InlineData("UPDATE OR INSERT INTO T (A) VALUES (1) MATCHING (A)", StatementKind.Dml, "UPDATE OR INSERT")]
    [InlineData("MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED THEN UPDATE SET A = S.A", StatementKind.Dml, "MERGE")]
    [InlineData("CREATE TABLE T (A INTEGER)", StatementKind.Ddl, "CREATE")]
    [InlineData("recreate view V as select 1 from rdb$database", StatementKind.Ddl, "RECREATE")]
    [InlineData("GRANT SELECT ON T TO ROLE READER", StatementKind.Ddl, "GRANT")]
    [InlineData("SET GENERATOR G TO 10", StatementKind.Ddl, "SET GENERATOR")]
    [InlineData("EXECUTE PROCEDURE P(1)", StatementKind.Execute, "EXECUTE PROCEDURE")]
    [InlineData("execute block as begin end", StatementKind.Execute, "EXECUTE BLOCK")]
    [InlineData("COMMIT", StatementKind.Transaction, "COMMIT")]
    [InlineData("ROLLBACK", StatementKind.Transaction, "ROLLBACK")]
    [InlineData("   ", StatementKind.Empty, "")]
    [InlineData("-- solo un comentario", StatementKind.Empty, "")]
    public void Clasifica_por_la_primera_palabra_clave(string sql, StatementKind kind, string keyword)
    {
        var info = StatementClassifier.Classify(sql);
        Assert.Equal(kind, info.Kind);
        Assert.Equal(keyword, info.Keyword);
    }

    [Theory]
    [InlineData("DELETE FROM T", true)]
    [InlineData("UPDATE T SET A = 1", true)]
    [InlineData("DELETE FROM T WHERE ID = 1", false)]
    [InlineData("UPDATE T SET A = (SELECT MAX(B) FROM S WHERE S.ID = 1)", true)] // el WHERE está en la subconsulta
    [InlineData("UPDATE T SET A = 'WHERE'", true)]                               // WHERE dentro de un string
    [InlineData("update t set a = 1 -- where id = 1", true)]                       // WHERE comentado
    [InlineData("UPDATE T SET A = 1\nWHERE ID IN (SELECT ID FROM S)", false)]
    public void Detecta_UPDATE_DELETE_sin_WHERE(string sql, bool affectsAll) =>
        Assert.Equal(affectsAll, StatementClassifier.Classify(sql).AffectsAllRows);

    [Theory]
    [InlineData("MERGE INTO T USING S ON 1 = 1 WHEN MATCHED THEN DELETE", true)]
    [InlineData("MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED AND S.BAJA = 1 THEN DELETE", false)]
    [InlineData("MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED THEN UPDATE SET A = S.A", false)]
    [InlineData("EXECUTE BLOCK AS BEGIN DELETE FROM T; END", true)]
    [InlineData("EXECUTE BLOCK AS BEGIN UPDATE T SET A = 1 WHERE ID = 1; END", false)]
    [InlineData("EXECUTE BLOCK AS BEGIN IF (1 = 1) THEN UPDATE T SET A = 1; END", true)]
    [InlineData("EXECUTE BLOCK AS DECLARE X INTEGER; BEGIN SELECT COUNT(*) FROM T INTO :X; END", false)]
    [InlineData("EXECUTE BLOCK AS BEGIN UPDATE OR INSERT INTO T (ID) VALUES (1) MATCHING (ID); END", false)]
    [InlineData("EXECUTE PROCEDURE BORRAR_TODO", false)]
    public void Detecta_MERGE_y_EXECUTE_BLOCK_que_tocan_todas_las_filas(string sql, bool affectsAll) =>
        Assert.Equal(affectsAll, StatementClassifier.Classify(sql).AffectsAllRows);

    [Theory]
    [InlineData("SELECT NEXT VALUE FOR G FROM RDB$DATABASE", true)]
    [InlineData("SELECT GEN_ID(G, 1) FROM RDB$DATABASE", true)]
    [InlineData("select gen_id(g, -5) from rdb$database", true)]
    [InlineData("SELECT GEN_ID(G, :PASO) FROM RDB$DATABASE", true)]
    [InlineData("SELECT GEN_ID(G, 0) FROM RDB$DATABASE", false)]                // solo lee el valor
    [InlineData("SELECT 'GEN_ID(G, 1)' FROM RDB$DATABASE", false)]              // dentro de un string
    [InlineData("SELECT * FROM T -- NEXT VALUE FOR G", false)]                  // comentado
    public void Detecta_lo_que_mueve_un_generador(string sql, bool changes) =>
        Assert.Equal(changes, StatementClassifier.Classify(sql).ChangesSequences);

    [Theory]
    [InlineData("UPDATE T SET A = q'{WHERE}'", true)]
    [InlineData("UPDATE T SET A = Q'!it's WHERE!'", true)]
    [InlineData("UPDATE T SET A = q'(x)' WHERE ID = 1", false)]
    public void Un_literal_q_es_un_string(string sql, bool affectsAll)
    {
        Assert.Equal(affectsAll, StatementClassifier.Classify(sql).AffectsAllRows);
        Assert.Single(SqlLexer.Tokenize(sql), t => t.Kind == SqlTokenKind.String);
    }
}
