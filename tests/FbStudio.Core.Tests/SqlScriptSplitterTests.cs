using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class SqlScriptSplitterTests
{
    [Fact]
    public void Parte_por_punto_y_coma_ignorando_strings_y_comentarios()
    {
        var parts = SqlScriptSplitter.Split("SELECT ';' FROM A; -- ; comentario\nSELECT 2 FROM B /* ; */ ;\n\nSELECT 3 FROM C");
        Assert.Equal(["SELECT ';' FROM A", "-- ; comentario\nSELECT 2 FROM B /* ; */", "SELECT 3 FROM C"], parts.Select(p => p.Text));
    }

    [Fact]
    public void Sin_punto_y_coma_es_una_sola_sentencia() =>
        Assert.Single(SqlScriptSplitter.Split("SELECT *\nFROM T\nWHERE A = 1"));

    [Fact]
    public void Ignora_sentencias_vacias() =>
        Assert.Equal(2, SqlScriptSplitter.Split(";;SELECT 1 FROM X;;  ;SELECT 2 FROM Y;").Count);

    [Theory]
    [InlineData(3, "SELECT 1 FROM A")]   // dentro de la primera
    [InlineData(16, "SELECT 1 FROM A")]  // justo después del ';' de la primera
    [InlineData(17, "SELECT 2 FROM B")]  // al inicio de la segunda línea
    [InlineData(22, "SELECT 2 FROM B")]  // dentro de la segunda
    public void Encuentra_la_sentencia_bajo_el_cursor(int caret, string expected) =>
        Assert.Equal(expected, SqlScriptSplitter.AtCaret("SELECT 1 FROM A;\nSELECT 2 FROM B;", caret)?.Text);

    [Fact]
    public void Un_procedimiento_sin_SET_TERM_es_una_sola_sentencia()
    {
        const string proc = """
            CREATE OR ALTER PROCEDURE P (A INTEGER = CAST(1 AS INTEGER))
            RETURNS (N INTEGER)
            AS
            DECLARE VARIABLE X INTEGER;
            DECLARE VARIABLE Y INTEGER = 2;
            BEGIN
              X = CASE WHEN A > 0 THEN 1 ELSE 0 END;
              IF (X = 1) THEN
              BEGIN
                N = 1;
                SUSPEND;
              END
            END
            """;
        var parts = SqlScriptSplitter.Split("SELECT 1 FROM A;\n" + proc + ";\nSELECT 2 FROM B;");
        Assert.Equal(3, parts.Count);
        Assert.Equal(proc, parts[1].Text);
        Assert.Equal("SELECT 2 FROM B", parts[2].Text);
    }

    [Fact]
    public void Entiende_SET_TERM_de_los_scripts_de_isql()
    {
        const string script = """
            SET TERM ^ ;
            CREATE TRIGGER T FOR X ACTIVE BEFORE INSERT POSITION 0 AS
            BEGIN
              NEW.ID = 1;
            END^
            EXECUTE BLOCK AS BEGIN END^
            SET TERM ; ^
            COMMIT;
            """;
        var parts = SqlScriptSplitter.Split(script);
        Assert.Equal(3, parts.Count);
        Assert.StartsWith("CREATE TRIGGER T", parts[0].Text);
        Assert.EndsWith("END", parts[0].Text);
        Assert.Equal("EXECUTE BLOCK AS BEGIN END", parts[1].Text);
        Assert.Equal("COMMIT", parts[2].Text);
    }

    [Fact]
    public void ALTER_TRIGGER_sin_cuerpo_termina_en_el_punto_y_coma() =>
        Assert.Equal(["ALTER TRIGGER T INACTIVE", "SELECT 1 FROM X"],
            SqlScriptSplitter.Split("ALTER TRIGGER T INACTIVE;SELECT 1 FROM X").Select(p => p.Text));

    [Fact]
    public void Un_paquete_con_declaraciones_adentro_es_una_sola_sentencia()
    {
        const string pkg = "CREATE PACKAGE PKG AS BEGIN PROCEDURE P1 (A INTEGER); FUNCTION F1 RETURNS INTEGER; END";
        Assert.Equal([pkg, "SELECT 1 FROM X"], SqlScriptSplitter.Split(pkg + ";\nSELECT 1 FROM X;").Select(p => p.Text));
    }

    [Fact]
    public void El_cursor_dentro_de_un_procedimiento_toma_el_procedimiento_entero()
    {
        var text = "SELECT 1 FROM A;\nCREATE PROCEDURE P AS DECLARE X INTEGER; BEGIN X = 1; END;\nSELECT 2 FROM B;";
        var caret = text.IndexOf("X = 1", StringComparison.Ordinal);
        Assert.Equal("CREATE PROCEDURE P AS DECLARE X INTEGER; BEGIN X = 1; END", SqlScriptSplitter.AtCaret(text, caret)?.Text);
    }
}
