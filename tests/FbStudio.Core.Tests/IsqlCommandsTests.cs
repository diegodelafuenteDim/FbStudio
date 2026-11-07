using FbStudio.Core.Profiles;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>Las órdenes de isql de los scripts (D-120): qué es cada una y qué no lo es.</summary>
public class IsqlCommandsTests
{
    [Theory]
    [InlineData("SET BAIL ON", IsqlKind.Bail, "SET BAIL", "ON")]
    [InlineData("set bail off", IsqlKind.Bail, "SET BAIL", "off")]
    [InlineData("SET BAIL", IsqlKind.Bail, "SET BAIL", "")]
    [InlineData("SET BAIL ON;", IsqlKind.Bail, "SET BAIL", "ON")]                    // con el «;» pegado
    [InlineData("QUIT", IsqlKind.End, "QUIT", "")]
    [InlineData("exit", IsqlKind.End, "EXIT", "")]
    [InlineData("SET NAMES UTF8", IsqlKind.Names, "SET NAMES", "UTF8")]
    [InlineData("SET SQL DIALECT 3", IsqlKind.Dialect, "SET SQL DIALECT", "3")]
    [InlineData("SET SQL DIALECT 1", IsqlKind.OtherDialect, "SET SQL DIALECT", "1")]
    [InlineData("SET AUTODDL OFF", IsqlKind.AutoDdl, "SET AUTODDL", "OFF")]
    [InlineData("SET AUTO ON", IsqlKind.AutoDdl, "SET AUTODDL", "ON")]                // abreviatura de isql
    [InlineData("SET ECHO ON", IsqlKind.Display, "SET ECHO", "ON")]
    [InlineData("SET LIST OFF", IsqlKind.Display, "SET LIST", "OFF")]
    [InlineData("SET HEADING OFF", IsqlKind.Display, "SET HEADING", "OFF")]
    [InlineData("SET HEAD OFF", IsqlKind.Display, "SET HEADING", "OFF")]
    [InlineData("SET COUNT ON", IsqlKind.Display, "SET COUNT", "ON")]
    [InlineData("SET STATS ON", IsqlKind.Display, "SET STATS", "ON")]
    [InlineData("SET STAT ON", IsqlKind.Display, "SET STATS", "ON")]
    [InlineData("SET PLAN ON", IsqlKind.Display, "SET PLAN", "ON")]
    [InlineData("SET TIME ON", IsqlKind.Display, "SET TIME", "ON")]
    [InlineData("SET WIDTH MON$USER 32", IsqlKind.Display, "SET WIDTH", "MON$USER 32")]
    [InlineData("SET BLOBDISPLAY ALL", IsqlKind.Display, "SET BLOBDISPLAY", "ALL")]
    [InlineData("SET BLOB OFF", IsqlKind.Display, "SET BLOBDISPLAY", "OFF")]
    [InlineData("SET WARNINGS OFF", IsqlKind.Display, "SET WARNINGS", "OFF")]
    [InlineData("SET PLANONLY OFF", IsqlKind.Display, "SET PLANONLY", "OFF")]
    [InlineData("SET CLIENTLIB 'C:\\Firebird\\fbclient.dll'", IsqlKind.ClientLib, "SET CLIENTLIB", "'C:\\Firebird\\fbclient.dll'")]
    [InlineData("SHOW TABLES", IsqlKind.Show, "SHOW", "TABLES")]
    [InlineData("HELP", IsqlKind.Show, "HELP", "")]
    [InlineData("OUTPUT", IsqlKind.OutputOff, "OUTPUT", "")]
    [InlineData("OUTPUT 'C:/tmp/padron.sql'", IsqlKind.Output, "OUTPUT", "'C:/tmp/padron.sql'")]
    [InlineData("INPUT /scripts/079-novedades.sql", IsqlKind.Input, "INPUT", "/scripts/079-novedades.sql")]
    [InlineData("SHELL dir", IsqlKind.Shell, "SHELL", "dir")]
    [InlineData("CONNECT 'srv:/db/x.fdb' USER 'SYSDBA' PASSWORD 'masterkey'", IsqlKind.OtherDatabase, "CONNECT", "'srv:/db/x.fdb' USER 'SYSDBA' PASSWORD 'masterkey'")]
    [InlineData("CREATE DATABASE 'srv:ventas' USER 'SYSDBA' PASSWORD 'x' PAGE_SIZE 16384", IsqlKind.OtherDatabase, "CREATE DATABASE", "'srv:ventas' USER 'SYSDBA' PASSWORD 'x' PAGE_SIZE 16384")]
    [InlineData("DROP DATABASE", IsqlKind.OtherDatabase, "DROP DATABASE", "")]
    [InlineData("SET PLANONLY ON", IsqlKind.PlanOnly, "SET PLANONLY", "ON")]
    [InlineData("BLOBDUMP 32:d48 /tmp/x.bin", IsqlKind.Interactive, "BLOBDUMP", "32:d48 /tmp/x.bin")]
    [InlineData("EDIT", IsqlKind.Interactive, "EDIT", "")]
    public void Reconoce_las_ordenes_de_isql(string sql, IsqlKind kind, string name, string argument)
    {
        var command = IsqlCommands.Parse(sql);
        Assert.NotNull(command);
        Assert.Equal((kind, name, argument), (command.Kind, command.Name, command.Argument));

        var info = StatementClassifier.Classify(sql);
        Assert.Equal(StatementKind.Isql, info.Kind);
        Assert.Equal(name, info.Keyword);
        Assert.False(info.Modifies);
    }

    [Theory]
    [InlineData("SET GENERATOR G TO 10")]                        // SQL de Firebird
    [InlineData("SET STATISTICS INDEX TURNOS_FECHA")]            // no es SET STATS
    [InlineData("SET TRANSACTION NO WAIT")]
    [InlineData("SET TIME ZONE 'America/Argentina/Buenos_Aires'")] // Firebird 4: no es el SET TIME de isql
    [InlineData("SET ROLE R_LECTURA")]
    [InlineData("SET DECFLOAT ROUND CEILING")]
    [InlineData("SET BIND OF DECFLOAT TO DOUBLE PRECISION")]
    [InlineData("SET STATEMENT TIMEOUT 10 SECOND")]
    [InlineData("CREATE TABLE T (A INTEGER)")]
    [InlineData("SELECT 1 FROM RDB$DATABASE")]
    [InlineData("CREATE PROCEDURE P AS BEGIN EXIT; END")]         // el EXIT de PSQL va adentro del bloque
    [InlineData("EXIT WHEN 1")]                                   // un EXIT suelto es solo la palabra
    public void Lo_que_es_SQL_no_es_una_orden_de_isql(string sql)
    {
        Assert.Null(IsqlCommands.Parse(sql));
        Assert.NotEqual(StatementKind.Isql, StatementClassifier.Classify(sql).Kind);
    }

    [Theory]
    [InlineData("SET SHELL", false)]
    [InlineData("SHELL dir", true)]
    [InlineData("INPUT x.sql", true)]
    [InlineData("OUTPUT x.txt", true)]
    [InlineData("OUTPUT", false)]
    [InlineData("SET PLANONLY", true)]
    [InlineData("SET SQL DIALECT 3", false)]
    [InlineData("SET SQL DIALECT 1", true)]
    [InlineData("SET NAMES UTF8", false)]
    [InlineData("SET BAIL ON", false)]
    public void Rechaza_lo_que_FbStudio_no_puede_hacer(string sql, bool rejected) =>
        Assert.Equal(rejected, IsqlCommands.Parse(sql)?.IsRejected ?? false);

    [Theory]
    [InlineData("SET BAIL ON", true)]
    [InlineData("SET BAIL OFF", false)]
    [InlineData("SET BAIL", null)]
    public void SET_BAIL_sin_argumento_alterna(string sql, bool? on) =>
        Assert.Equal(on, IsqlCommands.Parse(sql)!.Switch);

    [Fact]
    public void La_red_de_seguridad_deja_pasar_las_ordenes_de_isql()
    {
        // Las rechazadas las frena el script antes de empezar; acá no se bloquean ni piden confirmación.
        var production = new ConnectionProfile { Name = "prod", Host = "h", Database = "d", User = "u", Environment = ProfileEnvironment.Produccion, ReadOnly = true };
        Assert.Equal(SafetyVerdict.Allow, SafetyPolicy.Evaluate(production, StatementClassifier.Classify("CREATE DATABASE 'x'")).Verdict);
        Assert.Equal(SafetyVerdict.Allow, SafetyPolicy.Evaluate(production, StatementClassifier.Classify("SET BAIL ON")).Verdict);
    }
}

/// <summary>SAVEPOINT, RELEASE SAVEPOINT y ROLLBACK TO SAVEPOINT (D-120).</summary>
public class SavepointClassifierTests
{
    [Theory]
    [InlineData("SAVEPOINT S1", "SAVEPOINT")]
    [InlineData("RELEASE SAVEPOINT S1", "RELEASE SAVEPOINT")]
    [InlineData("RELEASE SAVEPOINT S1 ONLY", "RELEASE SAVEPOINT")]
    [InlineData("ROLLBACK TO SAVEPOINT S1", "ROLLBACK TO SAVEPOINT")]
    [InlineData("rollback work to s1", "ROLLBACK TO SAVEPOINT")]
    [InlineData("ROLLBACK", "ROLLBACK")]
    [InlineData("ROLLBACK WORK", "ROLLBACK")]
    [InlineData("ROLLBACK RETAIN", "ROLLBACK")]
    public void Distingue_el_rollback_hasta_un_savepoint(string sql, string keyword)
    {
        var info = StatementClassifier.Classify(sql);
        Assert.Equal(StatementKind.Transaction, info.Kind);
        Assert.Equal(keyword, info.Keyword);
    }

    [Theory]
    [InlineData("SAVEPOINT S1")]
    [InlineData("RELEASE SAVEPOINT S1")]
    [InlineData("ROLLBACK TO SAVEPOINT S1")]
    public void Los_savepoints_ya_no_se_bloquean(string sql)
    {
        var dev = new ConnectionProfile { Name = "dev", Host = "h", Database = "d", User = "u", Environment = ProfileEnvironment.Desarrollo };
        Assert.Equal(SafetyVerdict.Allow, SafetyPolicy.Evaluate(dev, StatementClassifier.Classify(sql)).Verdict);
    }

    [Fact]
    public void SET_TRANSACTION_sigue_bloqueado()
    {
        var dev = new ConnectionProfile { Name = "dev", Host = "h", Database = "d", User = "u", Environment = ProfileEnvironment.Desarrollo };
        Assert.Equal(SafetyVerdict.Block, SafetyPolicy.Evaluate(dev, StatementClassifier.Classify("SET TRANSACTION NO WAIT")).Verdict);
    }
}

public class BlockErrorPositionTests
{
    [Fact]
    public void Lee_la_posicion_de_una_EXCEPTION_en_un_EXECUTE_BLOCK()
    {
        const string error = "exception 1\nEX_GUARD\nPARAR: el tipo 401 ya está tomado por <otra cosa>\nAt block line: 13, col: 5";
        Assert.Equal(new FirebirdErrorPosition(13, 5), FirebirdErrorPosition.Parse(error));
    }

    [Fact]
    public void La_posicion_dentro_de_un_procedimiento_no_es_de_la_sentencia()
    {
        // El renglón es del código del procedimiento, no del texto que se mandó: el cursor no tiene que ir ahí.
        Assert.Null(FirebirdErrorPosition.Parse("exception 1\nE_TURNO\nTurno inválido\nAt procedure 'P_VALIDAR' line: 3, col: 5"));
        // Llamado desde un EXECUTE BLOCK, vale el renglón del bloque.
        Assert.Equal(new FirebirdErrorPosition(4, 3),
            FirebirdErrorPosition.Parse("exception 1\nE_TURNO\nTurno inválido\nAt procedure 'P_VALIDAR' line: 3, col: 5\nAt block line: 4, col: 3"));
    }
}
