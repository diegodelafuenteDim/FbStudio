using System.Collections.Immutable;
using FbStudio.Core.Data;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>La sintaxis del filtro que escribe la ventanita de cada columna (F-12), en la grilla y como SQL.</summary>
public class ColumnFilterSyntaxTests
{
    private static readonly ColumnInfo Texto = new(0, "OBRA", "VARCHAR");
    private static readonly ColumnInfo Numero = new(0, "IMPORTE", "NUMERIC");
    private static readonly ColumnInfo Fecha = new(0, "FECHA", "TIMESTAMP");
    private static readonly ColumnInfo Dia = new(0, "DIA", "DATE");
    private static readonly ColumnInfo Hora = new(0, "HORA", "TIME");
    private static readonly ColumnInfo Booleano = new(0, "ACTIVO", "BOOLEAN");

    private static bool[] Match(string text, ColumnInfo column, params object?[] values)
    {
        var f = ColumnFilter.Parse(text, column)!;
        Assert.Null(f.Error);
        return values.Select(f.Matches).ToArray();
    }

    private static string Sql(string text, string type, out List<object?> values)
    {
        var parameters = new List<SqlParameterValue>();
        var sql = FilterSql.ToSql(ColumnFilter.Parse(text, new ColumnInfo(0, "C", type))!.Condition!, new DbColumn("C", type, false, false, null, null), parameters);
        values = parameters.Select(p => p.Value).ToList();
        return sql;
    }

    [Fact]
    public void Texto_no_contiene_empieza_termina_y_listas()
    {
        Assert.Equal([false, true, true], Match("!art", Texto, "ALTA DE ART", "CAJA", null));        // los NULL no contienen nada
        Assert.Equal([true, false], Match("caj*", Texto, "CAJA", "LA CAJA"));
        Assert.Equal([false, true], Match("*caja", Texto, "CAJAS", "LA CAJA"));
        Assert.Equal([true, true, false], Match("perez;gomez", Texto, "PÉREZ, JUAN", "GÓMEZ", "LÓPEZ"));  // contiene alguno
        Assert.Equal([true, true, false, true], Match("=CAJA;PCIA;null", Texto, "caja", "PCIA", "PCIA2", null)); // iguales (y vacío)
        Assert.Equal([false, true], Match("caja;", Texto, "PCIA", "CAJA"));                    // un solo valor
    }

    [Fact]
    public void Numeros_entre_y_en_la_lista()
    {
        Assert.Equal([false, true, true, true, false], Match("10..20", Numero, 9.99m, 10, 15.5m, 20m, 21));
        Assert.Equal([true, true], Match("20..10", Numero, 10, 20));                          // al revés también
        Assert.Equal([true, false, true], Match("1;3", Numero, 1, 2, 3m));
        Assert.Equal([true, false, true], Match("<2;>=3", Numero, 1, 2, 3));                     // cada uno con su operador
        Assert.Contains("abc", ColumnFilter.Parse("1;abc", Numero)!.Error);
        Assert.NotNull(ColumnFilter.Parse("1..x", Numero)!.Error);
    }

    [Fact]
    public void Fechas_con_dia_entero_rangos_y_relativas()
    {
        var d5 = new DateTime(2026, 7, 5, 18, 30, 0);
        Assert.Equal([true, false], Match("<=2026-07-05", Fecha, d5, d5.AddDays(1)));       // «hasta el 5» incluye todo el 5
        Assert.Equal([false, true], Match(">2026-07-05", Fecha, d5, d5.AddDays(1)));        // «después del 5» es desde el 6
        Assert.Equal([false, true], Match("<>2026-07-05", Fecha, d5, d5.AddDays(1)));
        Assert.Equal([true, true, false], Match("2026-07-01..2026-07-05", Fecha, new DateTime(2026, 7, 1), d5, new DateTime(2026, 7, 6)));
        Assert.Equal([true, false], Match("2026-07-05 10:00..2026-07-05 19:00", Fecha, d5, d5.AddHours(1)));
        Assert.Equal([true, true], Match("2026-07-01..2026-07-05", Dia, new DateTime(2026, 7, 1), new DateTime(2026, 7, 5)));

        Assert.Equal([true, false, false], Match("hoy", Fecha, DateTime.Today.AddHours(23), DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1)));
        var wed = new DateTime(2026, 10, 21); // miércoles
        Assert.Equal((new DateTime(2026, 10, 19), new DateTime(2026, 10, 26)), ColumnFilter.RelativeDays("esta semana", wed));
        Assert.Equal((new DateTime(2026, 10, 12), new DateTime(2026, 10, 19)), ColumnFilter.RelativeDays("Semana pasada", wed));
        Assert.Equal((new DateTime(2026, 9, 1), new DateTime(2026, 10, 1)), ColumnFilter.RelativeDays("mes pasado", wed));
        Assert.Equal((new DateTime(2026, 1, 1), new DateTime(2027, 1, 1)), ColumnFilter.RelativeDays("este año", wed));
        Assert.Equal((new DateTime(2026, 10, 15), new DateTime(2026, 10, 22)), ColumnFilter.RelativeDays("últimos 7 días", wed));
        Assert.Equal((new DateTime(2026, 10, 20), new DateTime(2026, 10, 21)), ColumnFilter.RelativeDays("yesterday", wed));
        Assert.Null(ColumnFilter.RelativeDays("el otro día", wed));
    }

    [Fact]
    public void Horas_y_booleanos()
    {
        Assert.Equal([true, false, true], Match("08:00..12:00", Hora, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 1), new TimeSpan(9, 30, 0)));
        Assert.Equal([false, true], Match(">=9:30", Hora, new TimeSpan(9, 0, 0), new TimeSpan(9, 30, 0)));
        Assert.NotNull(ColumnFilter.Parse("25:00", Hora)!.Error);
        Assert.Equal([true, false, false], Match("sí", Booleano, true, false, null));
        Assert.Equal([false, true], Match("no", Booleano, true, false));
        Assert.NotNull(ColumnFilter.Parse("quizás", Booleano)!.Error);
    }

    [Fact]
    public void En_el_servidor_lo_mismo_como_SQL()
    {
        Assert.Equal("C BETWEEN @f0 AND @f1", Sql("10..20", "NUMERIC", out var v));
        Assert.Equal([10m, 20m], v);
        Assert.Equal("C IN (@f0, @f1, @f2)", Sql("1;5;9", "INTEGER", out v));
        Assert.Equal([1m, 5m, 9m], v);
        Assert.Equal("(UPPER(C) = UPPER(@f0) OR UPPER(C) = UPPER(@f1) OR C IS NULL)", Sql("=CAJA;PCIA;null", "VARCHAR(10)", out v));
        Assert.Equal("(C CONTAINING @f0 OR C CONTAINING @f1)", Sql("perez;gomez", "VARCHAR(60)", out _));
        Assert.Equal("(C IS NULL OR NOT (C CONTAINING @f0))", Sql("!art", "VARCHAR(60)", out v));
        Assert.Equal(["art"], v);
        Assert.Equal("C < @f0", Sql("<=2026-07-05", "TIMESTAMP", out v));
        Assert.Equal([new DateTime(2026, 7, 6)], v);
        Assert.Equal("C >= @f0", Sql(">2026-07-05", "TIMESTAMP", out v));
        Assert.Equal([new DateTime(2026, 7, 6)], v);
        Assert.Equal("C <= @f0", Sql("<=2026-07-05", "DATE", out v));
        Assert.Equal("(C < @f0 OR C >= @f1)", Sql("<>2026-07-05", "TIMESTAMP", out _));
        Assert.Equal("(C >= @f0 AND C < @f1)", Sql("2026-07-01..2026-07-05", "TIMESTAMP", out v));
        Assert.Equal([new DateTime(2026, 7, 1), new DateTime(2026, 7, 6)], v);
        Assert.Equal("(C >= @f0 AND C < @f1)", Sql("este mes", "DATE", out v));
        Assert.Equal(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), v[0]);
        Assert.Equal("C BETWEEN @f0 AND @f1", Sql("08:00..12:30", "TIME", out v));
        Assert.Equal([new TimeSpan(8, 0, 0), new TimeSpan(12, 30, 0)], v);
        Assert.Equal("C = TRUE", Sql("si", "BOOLEAN", out _));
    }

    [Fact]
    public void La_pagina_de_Datos_lleva_los_filtros_nuevos()
    {
        DbColumn[] columns = [new("ID", "INTEGER", true, true, null, null), new("FECHA", "TIMESTAMP", false, false, null, null)];
        var q = new TableQuery("TURNOS", columns, false)
        {
            Filters = ImmutableDictionary<int, ColumnFilter>.Empty
                .Add(0, ColumnFilter.Parse("1;2;3", new ColumnInfo(0, "ID", "INTEGER"))!)
                .Add(1, ColumnFilter.Parse("2026-07-01..2026-07-31", new ColumnInfo(1, "FECHA", "TIMESTAMP"))!),
        };
        Assert.Contains("WHERE ID IN (@f0, @f1, @f2)\n  AND (FECHA >= @f3 AND FECHA < @f4)", q.Page(0, 10).Sql);
        // Los valores de una columna, sin su propio filtro; con fecha y hora, por día.
        Assert.Equal("SELECT FIRST 500 CAST(FECHA AS DATE), COUNT(*) FROM TURNOS\nWHERE ID IN (@f0, @f1, @f2)\nGROUP BY 1\nORDER BY 2 DESC, 1", q.Distinct(1, 500).Sql);
    }
}
