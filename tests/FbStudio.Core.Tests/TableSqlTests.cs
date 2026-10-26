using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;

namespace FbStudio.Core.Tests;

/// <summary>«Generar SQL» de una tabla desde el árbol.</summary>
public class TableSqlTests
{
    private static readonly DbColumn[] Turnos =
    [
        new("ID", "INTEGER", NotNull: true, IsPrimaryKey: true, null, null),
        new("FECHA", "TIMESTAMP", NotNull: false, IsPrimaryKey: false, null, null),
        new("Nro Doc", "VARCHAR(20)", NotNull: false, IsPrimaryKey: false, null, null),
        new("TOTAL", "INTEGER", NotNull: false, IsPrimaryKey: false, null, null, IsComputed: true),
    ];

    [Fact]
    public void Cada_sentencia_con_parametros_y_sin_las_calculadas()
    {
        Assert.Equal("SELECT ID, FECHA, \"Nro Doc\", TOTAL\nFROM TURNOS", TableSql.Build(TableSqlKind.Select, "TURNOS", Turnos));
        Assert.Equal("INSERT INTO TURNOS (ID, FECHA, \"Nro Doc\")\nVALUES (:ID, :FECHA, :Nro_Doc)", TableSql.Build(TableSqlKind.Insert, "TURNOS", Turnos));
        Assert.Equal("UPDATE TURNOS\nSET FECHA = :FECHA,\n    \"Nro Doc\" = :Nro_Doc\nWHERE ID = :ID", TableSql.Build(TableSqlKind.Update, "TURNOS", Turnos));
        Assert.Equal("DELETE FROM TURNOS\nWHERE ID = :ID", TableSql.Build(TableSqlKind.Delete, "TURNOS", Turnos));
        Assert.Equal("UPDATE OR INSERT INTO TURNOS (ID, FECHA, \"Nro Doc\")\nVALUES (:ID, :FECHA, :Nro_Doc)\nMATCHING (ID)",
            TableSql.Build(TableSqlKind.UpdateOrInsert, "TURNOS", Turnos));

        // Lo que arma se puede ejecutar: los «:COLUMNA» son parámetros (F-25).
        Assert.Equal(["ID", "FECHA", "Nro_Doc"], SqlParameters.Find(TableSql.Build(TableSqlKind.Insert, "TURNOS", Turnos)).Names);
    }

    [Fact]
    public void Sin_clave_primaria_la_condicion_queda_para_completar_y_las_listas_largas_se_parten()
    {
        var sinClave = Turnos.Select(c => c with { IsPrimaryKey = false }).ToArray();
        Assert.Equal("DELETE FROM TURNOS\nWHERE /* sin clave primaria: completar la condición */", TableSql.Build(TableSqlKind.Delete, "TURNOS", sinClave));
        Assert.EndsWith("MATCHING (/* sin clave primaria: completar la condición */)", TableSql.Build(TableSqlKind.UpdateOrInsert, "TURNOS", sinClave));
        Assert.Equal(StatementKind.Dml, StatementClassifier.Classify(TableSql.Build(TableSqlKind.Delete, "TURNOS", sinClave)).Kind);

        var muchas = Enumerable.Range(1, 30).Select(i => new DbColumn($"COLUMNA_{i:00}", "INTEGER", false, false, null, null)).ToArray();
        var select = TableSql.Build(TableSqlKind.Select, "ANCHA", muchas).Split('\n');
        Assert.True(select.Length > 3);
        Assert.All(select, l => Assert.True(l.Length <= 100, l));
        Assert.StartsWith("       COLUMNA_", select[1]);                                     // los renglones que siguen, con sangría
        Assert.Equal(30, select.Sum(l => l.Split("COLUMNA_").Length - 1));
    }
}
