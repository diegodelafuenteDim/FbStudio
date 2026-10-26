using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>«Generar SQL» de procedimientos y funciones.</summary>
public class RoutineCallSqlTests
{
    [Fact]
    public void Llamadas_con_un_parametro_por_entrada()
    {
        var desde = new RoutineSignature("P_TURNOS_DESDE", [new("DESDE", "DATE"), new("HASTA", "DATE", HasDefault: true)]);
        Assert.Equal("SELECT *\nFROM P_TURNOS_DESDE(:DESDE, :HASTA)", RoutineCallSql.Procedure(desde, selectable: true));
        Assert.Equal("EXECUTE PROCEDURE P_TURNOS_DESDE(:DESDE, :HASTA)", RoutineCallSql.Procedure(desde, selectable: false));
        Assert.Equal("EXECUTE PROCEDURE P_CONTAR", RoutineCallSql.Procedure(new RoutineSignature("P_CONTAR", []), selectable: false));
        Assert.Equal("EXECUTE PROCEDURE PKG_TURNOS.\"Recalcular\"(:Desde_1)",
            RoutineCallSql.Procedure(new RoutineSignature("PKG_TURNOS.Recalcular", [new("Desde 1", "INTEGER")]), selectable: false));

        Assert.Equal("SELECT F_DOBLE(:X)\nFROM RDB$DATABASE", RoutineCallSql.Function(new RoutineSignature("F_DOBLE", [new("X", "INTEGER")], "INTEGER")));
        Assert.Equal("SELECT RTRIM(?)\nFROM RDB$DATABASE", RoutineCallSql.Function(new RoutineSignature("RTRIM", [new("", "CSTRING(255)")], "CSTRING(255)"))); // UDF
        Assert.Equal(["DESDE", "HASTA"], SqlParameters.Find(RoutineCallSql.Procedure(desde, selectable: false)).Names);
    }
}
