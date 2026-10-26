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
    public void Un_CASE_largo_va_con_un_WHEN_por_renglon()
    {
        Same("""
            SELECT
              id,
              CASE
                WHEN estado = 'A' THEN 'Activo'
                WHEN estado = 'B' THEN 'Dado de baja'
                WHEN estado = 'S' THEN 'Suspendido por falta de pago'
                ELSE 'Otro'
              END AS estado_texto,
              nombre
            FROM clientes
            """,
            F("select id, case when estado = 'A' then 'Activo' when estado = 'B' then 'Dado de baja' when estado = 'S' then 'Suspendido por falta de pago' else 'Otro' end as estado_texto, nombre from clientes"));
        // Un CASE simple (con operando) también.
        Same("""
            SELECT
              DECODE(x, 1, 'uno'),
              CASE tipo
                WHEN 1 THEN 'Consulta en el consultorio'
                WHEN 2 THEN 'Práctica de laboratorio'
                ELSE 'Otra cosa que no está en la lista'
              END
            FROM t
            """,
            F("select decode(x, 1, 'uno'), case tipo when 1 then 'Consulta en el consultorio' when 2 then 'Práctica de laboratorio' else 'Otra cosa que no está en la lista' end from t"));
    }

    [Fact]
    public void Un_procedimiento_se_sangra_por_bloques_con_las_consultas_formateadas()
    {
        const string Proc = "create or alter procedure p_total (desde date) returns (total numeric(18,2)) as declare variable x integer; -- lo leído\n" +
                            "begin total = 0; for select importe from turnos where fecha >= :desde into :x do begin if (x > 0) then total = total + x; " +
                            "else total = total - 1; end if (total > 100) then begin suspend; end else if (total < 0) then exception e_negativo; " +
                            "else exit; when any do total = null; end";
        var formatted = SqlFormatter.Format(Proc).Replace("\r", "");
        Same("""
            CREATE OR ALTER PROCEDURE p_total (desde DATE) RETURNS (total NUMERIC(18,2)) AS
            DECLARE VARIABLE x INTEGER; -- lo leído
            BEGIN
              total = 0;
              FOR SELECT importe
                  FROM turnos
                  WHERE fecha >= :desde
                  INTO :x
              DO
              BEGIN
                IF (x > 0) THEN
                  total = total + x;
                ELSE
                  total = total - 1;
              END
              IF (total > 100) THEN
              BEGIN
                SUSPEND;
              END
              ELSE IF (total < 0) THEN
                EXCEPTION e_negativo;
              ELSE
                EXIT;
              WHEN ANY DO
                total = NULL;
            END
            """, formatted);
        Assert.Equal(formatted, SqlFormatter.Format(formatted).Replace("\r", ""));          // formatear dos veces da lo mismo
    }

    [Fact]
    public void Triggers_execute_block_bucles_con_etiqueta_y_CASE_adentro()
    {
        Same("""
            CREATE TRIGGER turnos_bi FOR turnos ACTIVE BEFORE INSERT POSITION 0 AS
            BEGIN
              IF (NEW.id IS NULL) THEN
                NEW.id = GEN_ID(g_turnos, 1);
              NEW.estado = CASE WHEN NEW.fecha < CURRENT_DATE THEN 'V' ELSE 'P' END;
            END
            """, SqlFormatter.Format("create trigger turnos_bi for turnos active before insert position 0 as begin if (new.id is null) then new.id = gen_id(g_turnos, 1); new.estado = case when new.fecha < current_date then 'V' else 'P' end; end").Replace("\r", ""));
        Same("""
            EXECUTE BLOCK RETURNS (n INTEGER) AS
            DECLARE i INTEGER = 0;
            BEGIN
              siguiente: WHILE (i < 10) DO
              BEGIN
                i = i + 1;
                IF (MOD(i, 2) = 0) THEN
                  CONTINUE;
                n = i;
                SUSPEND;
              END
              IN AUTONOMOUS TRANSACTION DO
                INSERT INTO log (n)
                VALUES (:n);
            END
            """, SqlFormatter.Format("execute block returns (n integer) as declare i integer = 0; begin siguiente: while (i < 10) do begin i = i + 1; if (mod(i, 2) = 0) then continue; n = i; suspend; end in autonomous transaction do insert into log (n) values (:n); end").Replace("\r", ""));
    }

    [Fact]
    public void Lo_que_no_reconoce_queda_tal_cual()
    {
        // Un subprocedimiento (DECLARE PROCEDURE), un cuerpo externo y un bloque sin cerrar: no se tocan.
        const string Sub = "create procedure p as declare procedure q as begin end begin execute procedure q; end";
        const string External = "create procedure p external name 'udf!p' engine udr";
        const string Open = "create procedure p as begin if (1 = 1) then begin x = 1; end";
        foreach (var sql in new[] { Sub, External, Open }) Assert.Equal(sql, SqlFormatter.Format(sql));
        // Sin mayúsculas: las palabras quedan como se escribieron.
        Same("""
            create procedure p as
            begin
              if (a = 1) then
                Suspend;
            end
            """, SqlFormatter.Format("create procedure p as begin if (a = 1) then Suspend; end", new SqlFormatOptions(UppercaseKeywords: false)).Replace("\r", ""));
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
            CREATE PROCEDURE p AS
            BEGIN
              SELECT 1
              FROM rdb$database
              INTO :n;
            END^
            set term ; ^
            -- y otra
            UPDATE x
            SET id = 2;
            """, formatted);
        Assert.Equal(formatted, SqlFormatter.Format(formatted).Replace("\r", ""));
    }
}
