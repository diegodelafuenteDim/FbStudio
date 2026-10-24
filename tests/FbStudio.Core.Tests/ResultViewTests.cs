using System.Collections.Immutable;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

public class ResultViewTests
{
    private static readonly ColumnInfo Id = new(0, "ID", "INTEGER");
    private static readonly ColumnInfo Medico = new(1, "MEDICO", "VARCHAR");
    private static readonly ColumnInfo Importe = new(2, "IMPORTE", "NUMERIC");
    private static readonly ColumnInfo Fecha = new(3, "FECHA", "TIMESTAMP");
    private static readonly ColumnInfo Paciente = new(4, "PACIENTE", "VARCHAR");

    private static readonly List<object[]> Rows =
    [
        [1, "M042", 100.5m, new DateTime(2026, 7, 1, 8, 0, 0), "PÉREZ, JUAN"],
        [2, "M107", 50m, new DateTime(2026, 7, 1, 9, 0, 0), "GÓMEZ, ANA"],
        [3, "M042", DBNull.Value, new DateTime(2026, 7, 2, 8, 0, 0), "perez, maria"],
        [4, "M001", 1500m, new DateTime(2026, 7, 3, 10, 30, 0), DBNull.Value],
        [5, "M107", 10m, new DateTime(2026, 7, 1, 11, 0, 0), "LÓPEZ, LUIS"],
    ];

    private static ResultViewSnapshot Build(ResultViewSpec spec) => ResultViewBuilder.Build(Rows, Rows.Count, spec);

    private static int[] Ids(ResultViewSnapshot s) =>
        Enumerable.Range(0, s.Count).Select(i => s[i]).Where(e => !e.IsGroup).Select(e => (int)Rows[e.RowIndex][0]).ToArray();

    private static ResultViewSpec Filter(ColumnInfo col, string text) =>
        ResultViewSpec.Empty with { Filters = ImmutableDictionary<int, ColumnFilter>.Empty.Add(col.Ordinal, ColumnFilter.Parse(text, col)!) };

    [Fact]
    public void Sin_nada_es_la_identidad()
    {
        var s = Build(ResultViewSpec.Empty);
        Assert.Equal([1, 2, 3, 4, 5], Ids(s));
    }

    [Fact]
    public void Ordena_ascendente_y_descendente_con_NULL_primero()
    {
        var asc = Build(ResultViewSpec.Empty with { SortColumn = 2, SortDirection = SortDirection.Ascending });
        Assert.Equal([3, 5, 2, 1, 4], Ids(asc)); // NULL, 10, 50, 100.5, 1500
        var desc = Build(ResultViewSpec.Empty with { SortColumn = 2, SortDirection = SortDirection.Descending });
        Assert.Equal([4, 1, 2, 5, 3], Ids(desc));
    }

    [Fact]
    public void Ordena_textos_sin_mayusculas_y_estable()
    {
        var s = Build(ResultViewSpec.Empty with { SortColumn = 1, SortDirection = SortDirection.Ascending });
        Assert.Equal([4, 1, 3, 2, 5], Ids(s)); // M001, M042 (1 antes que 3), M107 (2 antes que 5)
    }

    [Theory]
    [InlineData("perez", new[] { 1, 3 })]          // sin acentos ni mayúsculas
    [InlineData("PÉREZ*", new[] { 1, 3 })]         // comodín
    [InlineData("null", new[] { 4 })]
    [InlineData("!null", new[] { 1, 2, 3, 5 })]
    public void Filtro_de_texto(string text, int[] expected) => Assert.Equal(expected, Ids(Build(Filter(Paciente, text))));

    [Theory]
    [InlineData("50", new[] { 2 })]
    [InlineData(">50", new[] { 1, 4 })]
    [InlineData(">= 50", new[] { 1, 2, 4 })]
    [InlineData("<100,5", new[] { 2, 5 })]          // coma decimal
    [InlineData("<>10", new[] { 1, 2, 4 })]         // NULL no entra
    public void Filtro_numerico(string text, int[] expected) => Assert.Equal(expected, Ids(Build(Filter(Importe, text))));

    [Theory]
    [InlineData("2026-07-01", new[] { 1, 2, 5 })]
    [InlineData("01/07/2026", new[] { 1, 2, 5 })]
    [InlineData(">= 2026-07-02", new[] { 3, 4 })]
    public void Filtro_de_fechas(string text, int[] expected) => Assert.Equal(expected, Ids(Build(Filter(Fecha, text))));

    [Fact]
    public void Filtro_invalido_no_filtra_y_explica()
    {
        var f = ColumnFilter.Parse(">abc", Importe)!;
        Assert.NotNull(f.Error);
        Assert.Equal(5, Build(Filter(Importe, ">abc")).VisibleRowCount);
    }

    [Fact]
    public void Agrupa_cerrado_por_defecto_y_abre_por_clave()
    {
        var spec = ResultViewSpec.Empty with { GroupBy = [Medico.Ordinal] };
        var closed = Build(spec);
        Assert.Equal(3, closed.Count); // M001, M042, M107
        Assert.Equal([("M001", 1), ("M042", 2), ("M107", 2)], closed.Groups.Select(g => ((string)g.Value!, g.RowCount)));

        var open = Build(spec with { ExpandedGroups = [closed.Groups[1].Key] });
        Assert.Equal(5, open.Count); // 3 grupos + 2 filas de M042
        Assert.True(open[1].IsGroup);
        Assert.Equal([1, 3], Ids(open));
    }

    [Fact]
    public void Agrupado_multinivel_y_orden_dentro_del_grupo()
    {
        var first = Build(ResultViewSpec.Empty with { GroupBy = [Medico.Ordinal] });
        var m107 = first.Groups.Single(g => (string)g.Value! == "M107").Key;
        var spec = ResultViewSpec.Empty with
        {
            GroupBy = [Medico.Ordinal],
            ExpandedGroups = [m107],
            SortColumn = Importe.Ordinal,
            SortDirection = SortDirection.Descending,
        };
        Assert.Equal([2, 5], Ids(Build(spec))); // dentro de M107: 50 antes que 10

        var two = Build(ResultViewSpec.Empty with { GroupBy = [Medico.Ordinal, Fecha.Ordinal], ExpandedGroups = [m107] });
        Assert.Equal(2, two.Groups.Count(g => g.Level == 1)); // M107 abierto: dos fechas distintas
    }

    [Fact]
    public void Expandir_todo_con_excepciones()
    {
        var all = Build(ResultViewSpec.Empty with { GroupBy = [Medico.Ordinal], ExpandAll = true });
        Assert.Equal(8, all.Count); // 3 grupos + 5 filas
        var m042 = all.Groups.Single(g => (string)g.Value! == "M042").Key;
        var oneClosed = Build(ResultViewSpec.Empty with { GroupBy = [Medico.Ordinal], ExpandAll = true, ExpandedGroups = [m042] });
        Assert.Equal(6, oneClosed.Count); // M042 cerrado
    }

    [Fact]
    public void Totales_sobre_las_filas_filtradas()
    {
        var all = Build(ResultViewSpec.Empty);
        Assert.Equal(1660.5m, all.Aggregate(2, AggregateKind.Sum, Importe));
        Assert.Equal(4L, all.Aggregate(2, AggregateKind.Count, Importe));     // NULL no cuenta
        Assert.Equal(415.125m, all.Aggregate(2, AggregateKind.Average, Importe));
        Assert.Equal(10m, all.Aggregate(2, AggregateKind.Min, Importe));
        Assert.Equal("M107", all.Aggregate(1, AggregateKind.Max, Medico));
        Assert.Null(all.Aggregate(1, AggregateKind.Sum, Medico));            // suma de texto: no aplica

        var filtered = Build(Filter(Medico, "M107"));
        Assert.Equal(60m, filtered.Aggregate(2, AggregateKind.Sum, Importe));
    }
}
