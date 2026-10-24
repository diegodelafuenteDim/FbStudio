using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class SqlFormatterTests
{
    private static string F(string sql) => SqlFormatter.FormatStatement(sql).Replace("\r", "");

    // Los textos esperados de varios renglones toman el fin de línea del archivo: en Windows, con el checkout de git,
    // llegan con \r\n. Se comparan sin importar el fin de línea.
    private static void Same(string expected, string actual) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));

    [Fact]
    public void Cada_clausula_en_su_renglon_y_las_condiciones_una_por_renglon()
    {
        Same("""
            SELECT t.id, p.nombre, COUNT(*)
            FROM turnos t
            LEFT OUTER JOIN pacientes p ON p.id = t.paciente
            WHERE t.fecha BETWEEN '2026-01-01' AND '2026-01-31'
              AND (p.activo = 1 OR p.id IS NULL)
              AND t.estado <> 'X'
            GROUP BY t.id, p.nombre
            HAVING COUNT(*) > 1
            ORDER BY t.fecha DESC
            """,
            F("select t.id,p.nombre, count(*) from turnos t left outer join pacientes p on p.id=t.paciente where t.fecha between '2026-01-01' and '2026-01-31' and (p.activo = 1 or p.id is null) and t.estado<>'X' group by t.id, p.nombre having count(*)>1 order by t.fecha desc"));
    }

    [Fact]
    public void Las_subconsultas_van_sangradas()
    {
        Same("""
            SELECT a
            FROM t
            WHERE EXISTS (
              SELECT 1
              FROM u
              WHERE u.id = t.id
            )
              AND b IN (1, 2)
            """,
            F("select a from t where exists (select 1 from u where u.id = t.id) and b in (1,2)"));
        Same("""
            WITH x AS (
              SELECT a
              FROM t
            )
            SELECT *
            FROM x
            """,
            F("with x as (select a from t) select * from x"));
    }

    [Fact]
    public void Una_lista_larga_va_un_elemento_por_renglon()
    {
        var columns = string.Join(", ", Enumerable.Range(1, 15).Select(i => $"COLUMNA_{i}"));
        var formatted = F($"select {columns} from t");
        Assert.StartsWith("SELECT\n  COLUMNA_1,\n  COLUMNA_2,\n", formatted);
        Assert.EndsWith("  COLUMNA_15\nFROM t", formatted);
    }

    [Fact]
    public void Espacios_operadores_signos_parametros_y_funciones()
    {
        Same("""
            SELECT -1, a - 1, EXTRACT(YEAR FROM fecha), CAST(x AS INTEGER), t.*, d || e
            FROM t
            WHERE x = -5
              AND a <> :desde
              AND b >= ?
              AND c IS DISTINCT FROM d
            """,
            F("select -1, a-1, extract(year from fecha), cast(x as integer), t.*, d||e from t where x=-5 and a<>:desde and b>=? and c is distinct from d"));
    }

    [Fact]
    public void Insert_update_delete_union_y_for_update()
    {
        Assert.Equal("INSERT INTO t (a, b)\nVALUES (1, 'x')", F("insert into t (a,b) values (1,'x')"));
        Assert.Equal("UPDATE t\nSET a = 1, b = 2\nWHERE id = 3", F("update t set a=1, b=2 where id=3"));
        Assert.Equal("DELETE FROM t\nWHERE id = 1", F("delete from t where id = 1"));
        Assert.Equal("SELECT a\nFROM t\nUNION ALL\nSELECT a\nFROM u", F("select a from t union all select a from u"));
        Assert.Equal("SELECT FIRST 10 SKIP 5 DISTINCT a\nFROM t\nWHERE id = 1\nFOR UPDATE WITH LOCK",
            F("select first 10 skip 5 distinct a from t where id = 1 for update with lock"));
        Assert.Equal("UPDATE OR INSERT INTO t (id, a)\nVALUES (1, 2)\nMATCHING (id)", F("update or insert into t (id, a) values (1, 2) matching (id)"));
        Assert.Equal("SELECT a\nFROM t\nJOIN u USING (id)", F("select a from t join u using (id)"));
    }

    [Fact]
    public void Los_comentarios_textos_y_nombres_entre_comillas_quedan()
    {
        Same("""
            -- turnos de hoy
            SELECT "Mixta", 'select from where'
            FROM turnos /* todos */
            WHERE fecha = CURRENT_DATE -- solo hoy
              AND x = 1
            """,
            F("-- turnos de hoy\nselect \"Mixta\", 'select from where' from turnos /* todos */ where fecha = current_date -- solo hoy\n and x = 1"));
        Assert.Equal("SELECT CASE WHEN a = 1 THEN 'x' ELSE 'y' END AS c\nFROM t", F("select case when a = 1 then 'x' else 'y' end as c from t"));
    }

    [Fact]
    public void Sin_mayusculas_las_palabras_quedan_como_se_escribieron()
    {
        Assert.Equal("select a\nfrom t\nwhere b = 1", SqlFormatter.FormatStatement("select a from t where b = 1", new SqlFormatOptions(UppercaseKeywords: false)));
    }

    [Fact]
    public void En_un_script_solo_cambian_las_consultas_y_formatear_dos_veces_da_lo_mismo()
    {
        const string Script = """
            create table x (id integer);
            select * from x where id=1;
            set term ^ ;
            create procedure p as begin select 1 from rdb$database into :n; end^
            set term ; ^
            -- y otra
            update x set id=2;
            """;
        var formatted = SqlFormatter.Format(Script).Replace("\r", "");
        Same("""
            create table x (id integer);
            SELECT *
            FROM x
            WHERE id = 1;
            set term ^ ;
            create procedure p as begin select 1 from rdb$database into :n; end^
            set term ; ^
            -- y otra
            UPDATE x
            SET id = 2;
            """, formatted);
        Assert.Equal(formatted, SqlFormatter.Format(formatted).Replace("\r", ""));
    }
}
