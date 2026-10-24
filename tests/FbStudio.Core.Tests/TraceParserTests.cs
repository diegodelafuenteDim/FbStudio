using FbStudio.Core.Trace;

namespace FbStudio.Core.Tests;

/// <summary>La salida real de la traza de Firebird 3 (capturada en Docker con fbtracemgr) y su análisis (F-24).</summary>
public class TraceParserTests
{
    private const string Sample = """
        Trace session ID 2 started
        2026-10-21T22:53:08.8730 (32:0x743ff55e7d40) TRACE_INIT
        	SESSION_2 prueba


        2026-10-21T22:53:08.8730 (32:0x743ff55e7d40) ATTACH_DATABASE
        	/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)
        	/usr/local/firebird/bin/isql:92

        2026-10-21T22:53:08.8850 (32:0x743ff55e7d40) EXECUTE_STATEMENT_FINISH
        	/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)
        	/usr/local/firebird/bin/isql:92
        		(TRA_0, CONCURRENCY | WAIT | READ_WRITE)

        Statement 104:
        -------------------------------------------------------------------------------
         select rel.rdb$relation_id, rel.rdb$relation_type from rdb$indices idx join rdb$relations rel using (rdb$relation_name) where idx.rdb$index_name = ?   and rel.rdb$relation_id is not null
        ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
        PLAN JOIN (IDX INDEX (<index id 1>), REL INDEX (<index id 1>))

        param0 = varchar(93), "RDB$PRIMARY2"

        0 records fetched
              0 ms, 7 fetch(es)

        Table                             Natural     Index    Update    Insert    Delete   Backout     Purge   Expunge
        ***************************************************************************************************************
        RDB$INDICES                                       1
        RDB$RELATIONS                                     1

        2026-10-21T22:53:08.9510 (32:0x743ff55e7d40) EXECUTE_STATEMENT_FINISH
        	/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)
        	/usr/local/firebird/bin/isql:92
        		(TRA_17, CONCURRENCY | WAIT | READ_WRITE)

        Statement 142:
        -------------------------------------------------------------------------------
        insert into t values (1, 'uno')
        0 records fetched
              3 ms, 2 read(s), 1 write(s), 9 fetch(es), 5 mark(s)

        Table                             Natural     Index    Update    Insert    Delete   Backout     Purge   Expunge
        ***************************************************************************************************************
        T                                                                     1

        2026-10-21T22:53:08.9510 (32:0x743ff55e7d40) EXECUTE_STATEMENT_FINISH
        	/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)
        	/usr/local/firebird/bin/isql:92
        		(TRA_17, CONCURRENCY | WAIT | READ_WRITE)

        Statement 143:
        -------------------------------------------------------------------------------
        insert into t values (2, 'dos')
        0 records fetched
              1 ms, 6 fetch(es), 2 mark(s)

        2026-10-21T22:53:08.9630 (32:0x743ff55e7d40) EXECUTE_STATEMENT_FINISH
        	prueba (ATT_12, JPEREZ:RDB$ADMIN, ISO8859_1, TCPv4:192.0.2.7/40450)
        	C:\Program Files\Turnos\turnos.exe:4321
        		(TRA_20, CONCURRENCY | WAIT | READ_WRITE)

        Statement 146:
        -------------------------------------------------------------------------------
        select count(*) from rdb$relations
        ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
        PLAN (RDB$RELATIONS NATURAL)
        1 records fetched
             12 ms, 55 fetch(es)

        Table                             Natural     Index    Update    Insert    Delete   Backout     Purge   Expunge
        ***************************************************************************************************************
        RDB$RELATIONS                          51

        2026-10-21T22:53:08.9630 (32:0x743ff55e7d40) ERROR AT JStatement::prepare
        	/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)
        	/usr/local/firebird/bin/isql:92
        335544569 : Dynamic SQL Error
        335544436 : SQL error code = -204
        335544580 : Table unknown
        335544382 : NO_EXISTE

        2026-10-21T22:53:09.0100 (32:0x743ff55e7d40) FAILED EXECUTE_PROCEDURE_FINISH
        	/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)
        	/usr/local/firebird/bin/isql:92
        		(TRA_21, CONCURRENCY | WAIT | READ_WRITE)

        Procedure P_TURNOS:
        param0 = integer, "5"

        0 records fetched
            250 ms, 900 read(s), 1200 fetch(es)

        2026-10-21T22:53:09.0200 (32:0x743ff55e7d40) DETACH_DATABASE
        	/firebird/data/test.fdb (ATT_11, SYSDBA:NONE, NONE, TCPv4:127.0.0.1/55648)
        	/usr/local/firebird/bin/isql:92
        """;

    private static IReadOnlyList<TraceEvent> Events() => TraceParser.Parse(Sample.Split('\n'));

    [Fact]
    public void Un_numero_que_no_entra_en_un_long_no_corta_la_lectura()
    {
        var events = TraceParser.Parse(Sample.Replace("ATT_11", "ATT_99999999999999999999").Split('\n'));
        Assert.Equal(Events().Count, events.Count);
        Assert.Equal(0L, events[1].Attachment);
    }

    [Fact]
    public void Lee_cada_evento_con_su_conexion_y_su_transaccion()
    {
        var events = Events();
        Assert.Equal(["TRACE_INIT", "ATTACH_DATABASE", "EXECUTE_STATEMENT_FINISH", "EXECUTE_STATEMENT_FINISH", "EXECUTE_STATEMENT_FINISH",
            "EXECUTE_STATEMENT_FINISH", "ERROR AT JStatement::prepare", "EXECUTE_PROCEDURE_FINISH", "DETACH_DATABASE"], events.Select(e => e.Kind));

        var attach = events[1];
        Assert.Equal(new DateTime(2026, 10, 21, 22, 53, 8, 873), attach.Time);
        Assert.Equal(("test.fdb", 11L, "SYSDBA", null as string), (attach.Database, attach.Attachment, attach.User, attach.Role));
        Assert.Equal(("TCPv4", "127.0.0.1", "isql", 92L), (attach.Protocol, attach.Address, attach.Process, attach.ProcessId));
        Assert.False(attach.IsExecution);
    }

    [Fact]
    public void Lee_el_SQL_el_plan_los_parametros_el_rendimiento_y_las_tablas()
    {
        var e = Events()[2];
        Assert.Equal((0L, 104L), (e.Transaction, e.Statement));
        Assert.StartsWith("select rel.rdb$relation_id", e.Sql);
        Assert.Equal("PLAN JOIN (IDX INDEX (<index id 1>), REL INDEX (<index id 1>))", e.Plan);
        Assert.Equal("param0 = varchar(93), \"RDB$PRIMARY2\"", e.Parameters);
        Assert.Equal((0L, 0L, 7L), (e.Records, e.Milliseconds, e.Fetches));
        Assert.Null(e.Reads);
        Assert.Equal([new TraceTableStat("RDB$INDICES", 0, 1, 0, 0, 0, 0, 0, 0), new TraceTableStat("RDB$RELATIONS", 0, 1, 0, 0, 0, 0, 0, 0)], e.Tables);
        Assert.True(e.IsExecution);
        Assert.False(e.HasFullScan);

        var insert = Events()[3];
        Assert.Equal("insert into t values (1, 'uno')", insert.Sql);
        Assert.Null(insert.Plan);
        Assert.Equal((3L, 2L, 1L, 9L, 5L), (insert.Milliseconds, insert.Reads, insert.Writes, insert.Fetches, insert.Marks));
        Assert.Equal(1, Assert.Single(insert.Tables).Insert);
    }

    [Fact]
    public void Otra_conexion_por_alias_con_rol_y_programa_de_Windows()
    {
        var e = Events()[5];
        Assert.Equal(("prueba", "JPEREZ", "RDB$ADMIN", "192.0.2.7"), (e.Database, e.User, e.Role, e.Address));
        Assert.Equal(("turnos.exe", 4321L), (e.Process, e.ProcessId));       // solo el nombre del programa, sin su carpeta
        Assert.Equal("PLAN (RDB$RELATIONS NATURAL)", e.Plan);
        Assert.True(e.HasFullScan);                                          // 51 lecturas sin índice
    }

    [Fact]
    public void Errores_y_procedimientos_que_fallaron()
    {
        var error = Events()[6];
        Assert.True(error.IsError);
        Assert.Equal("Dynamic SQL Error · SQL error code = -204 · Table unknown · NO_EXISTE", error.Error);

        var proc = Events()[7];
        Assert.True(proc.Failed);
        Assert.Equal("EXECUTE PROCEDURE P_TURNOS", proc.Sql);
        Assert.Equal("param0 = integer, \"5\"", proc.Parameters);
        Assert.Equal((250L, 900L, 1200L), (proc.Milliseconds, proc.Reads, proc.Fetches));
    }

    [Fact]
    public void Los_renglones_llegan_de_a_uno_y_el_evento_sale_al_empezar_el_siguiente()
    {
        var parser = new TraceParser();
        var seen = new List<TraceEvent>();
        foreach (var line in Sample.Split('\n'))
            if (parser.Feed(line) is { } e) seen.Add(e);
        Assert.Equal(8, seen.Count);                                         // el último sale con Flush
        Assert.Equal("DETACH_DATABASE", parser.Flush()!.Kind);
        Assert.Null(parser.Flush());
    }

    [Theory]
    [InlineData("SELECT *  FROM T\n WHERE ID = 15", true, "SELECT * FROM T WHERE ID = ?")]
    [InlineData("select * from t where nombre = 'O''Brien' and x > -2.5", true, "select * from t where nombre = ? and x > ?")]
    [InlineData("SELECT * FROM TABLA_2 WHERE C IN (1, 2, 3)", true, "SELECT * FROM TABLA_2 WHERE C IN (?)")]
    [InlineData("SELECT * FROM T WHERE ID = 15", false, "SELECT * FROM T WHERE ID = 15")]
    public void La_clave_de_una_consulta_unifica_los_valores(string sql, bool unify, string expected) =>
        Assert.Equal(expected, TraceAnalysis.Key(sql, unify));

    [Fact]
    public void El_analisis_junta_las_ejecuciones_y_ordena_por_tiempo_total()
    {
        var groups = TraceAnalysis.Group(Events());
        Assert.Equal(["EXECUTE PROCEDURE P_TURNOS", "select count(*) from rdb$relations", "insert into t values (?, ?)"], groups.Take(3).Select(g => g.Key));

        var insert = groups.Single(g => g.Key == "insert into t values (?, ?)");
        Assert.Equal((2, 4L, 3L, 15L), (insert.Count, insert.TotalMs, insert.MaxMs, insert.Fetches));
        Assert.Equal(2.0, insert.AverageMs);
        Assert.Equal("insert into t values (1, 'uno')", insert.Slowest.Sql);
        Assert.Equal(1, groups.Single(g => g.Key.StartsWith("EXECUTE")).Failed);
        Assert.True(groups.Single(g => g.Key.StartsWith("select count")).FullScan);
        Assert.Equal(4, groups.Count);                                       // el error y los ATTACH no son ejecuciones

        var separate = TraceAnalysis.Group(Events(), unifyValues: false);
        Assert.Equal(5, separate.Count);                                     // los dos INSERT por separado
    }
}
