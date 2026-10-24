using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

public class FirebirdErrorPositionTests
{
    [Theory]
    [InlineData("Dynamic SQL Error\nSQL error code = -104\nToken unknown - line 5, column 3\nFOO", 5, 3)]
    [InlineData("unsuccessful metadata update\nCREATE OR ALTER PROCEDURE P failed\nDynamic SQL Error\nSQL error code = -206\nColumn unknown\nX\nAt line 8, column 15", 8, 15)]
    [InlineData("Invalid command\n-Unexpected end of command - line 1, column 34", 1, 34)]
    [InlineData("At line 12, char 7", 12, 7)]
    public void Lee_linea_y_columna(string message, int line, int column) =>
        Assert.Equal(new FirebirdErrorPosition(line, column), FirebirdErrorPosition.Parse(message));

    [Fact]
    public void Sin_posicion_devuelve_null() =>
        Assert.Null(FirebirdErrorPosition.Parse("violation of PRIMARY or UNIQUE KEY constraint"));

    [Fact]
    public void Ajusta_la_posicion_al_documento_cuando_la_sentencia_no_empieza_al_principio()
    {
        const string doc = "SET TERM ^ ;\n  CREATE PROCEDURE P AS\nBEGIN\n  X;\nEND^";
        var start = doc.IndexOf("CREATE", StringComparison.Ordinal);
        Assert.Equal(new FirebirdErrorPosition(4, 3), new FirebirdErrorPosition(3, 3).InDocument(doc, start));
        Assert.Equal(new FirebirdErrorPosition(2, 5), new FirebirdErrorPosition(1, 3).InDocument(doc, start)); // misma línea: suma la sangría
    }
}

public class DdlTargetTests
{
    [Theory]
    [InlineData("CREATE OR ALTER PROCEDURE P_CONTAR RETURNS (N INTEGER) AS BEGIN END", "PROCEDURE", "P_CONTAR")]
    [InlineData("  /* comentario */ create or alter trigger turnos_bi for turnos", "TRIGGER", "TURNOS_BI")]
    [InlineData("RECREATE VIEW \"Mi Vista\" AS SELECT 1 FROM RDB$DATABASE", "VIEW", "Mi Vista")]
    [InlineData("ALTER FUNCTION F_DOBLE (X INTEGER) RETURNS INTEGER AS BEGIN RETURN 1; END", "FUNCTION", "F_DOBLE")]
    public void Lee_tipo_y_nombre_del_objeto(string sql, string type, string name) =>
        Assert.Equal((type, name), StatementClassifier.DdlTarget(sql));

    [Theory]
    [InlineData("SELECT 1 FROM RDB$DATABASE")]
    [InlineData("CREATE")]
    public void Sin_objeto_devuelve_null(string sql) => Assert.Null(StatementClassifier.DdlTarget(sql));
}
