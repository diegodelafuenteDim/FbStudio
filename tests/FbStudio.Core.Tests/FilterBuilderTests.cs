using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

/// <summary>Lo que escribe la ventanita de filtro (F-12) y cómo la abre con un filtro ya escrito.</summary>
public class FilterBuilderTests
{
    private static readonly ColumnInfo Texto = new(0, "OBRA", "VARCHAR");
    private static readonly ColumnInfo Numero = new(0, "IMPORTE", "NUMERIC");
    private static readonly ColumnInfo Fecha = new(0, "FECHA", "TIMESTAMP");
    private static readonly ColumnInfo Dia = new(0, "DIA", "DATE");
    private static readonly ColumnInfo Hora = new(0, "HORA", "TIME");
    private static readonly ColumnInfo Booleano = new(0, "ACTIVO", "BOOLEAN");

    [Fact]
    public void Cada_tipo_tiene_sus_operadores()
    {
        Assert.Equal(FilterKind.Text, FilterBuilder.KindOf(Texto));
        Assert.Equal(FilterKind.Number, FilterBuilder.KindOf(Numero));
        Assert.Equal(FilterKind.Date, FilterBuilder.KindOf(Dia));
        Assert.Equal(FilterKind.Time, FilterBuilder.KindOf(Hora));
        Assert.Equal(FilterKind.Boolean, FilterBuilder.KindOf(Booleano));
        Assert.Equal(FilterOperator.Contains, FilterBuilder.OperatorsFor(FilterKind.Text)[0]);
        Assert.Contains(FilterOperator.Between, FilterBuilder.OperatorsFor(FilterKind.Number));
        Assert.DoesNotContain(FilterOperator.Contains, FilterBuilder.OperatorsFor(FilterKind.Number));
        Assert.Equal([FilterOperator.Day, FilterOperator.Relative], FilterBuilder.OperatorsFor(FilterKind.Date).Take(2));
        Assert.Equal([FilterOperator.Yes, FilterOperator.No, FilterOperator.IsNull, FilterOperator.IsNotNull], FilterBuilder.OperatorsFor(FilterKind.Boolean));
    }

    [Theory]
    [InlineData(FilterOperator.Contains, "caja", null, "caja")]
    [InlineData(FilterOperator.NotContains, "art", null, "!art")]
    [InlineData(FilterOperator.StartsWith, "AL", null, "AL*")]
    [InlineData(FilterOperator.EndsWith, "ART", null, "*ART")]
    [InlineData(FilterOperator.Equals, "5", null, "=5")]
    [InlineData(FilterOperator.LessOrEqual, "2026-07-05", null, "<=2026-07-05")]
    [InlineData(FilterOperator.Between, " 10 ", "20", "10..20")]
    [InlineData(FilterOperator.Relative, "este mes", null, "este mes")]
    [InlineData(FilterOperator.IsNull, null, null, "null")]
    [InlineData(FilterOperator.IsNotNull, "x", null, "!null")]
    [InlineData(FilterOperator.No, null, null, "no")]
    [InlineData(FilterOperator.Contains, "  ", null, null)]          // sin valor, sin filtro
    [InlineData(FilterOperator.Between, "10", "", null)]
    public void Arma_el_texto_de_la_fila_de_filtros(FilterOperator op, string? value, string? value2, string? expected) =>
        Assert.Equal(expected, FilterBuilder.Build(op, value, value2));

    [Fact]
    public void Los_valores_tildados_son_cualquiera_de_ellos()
    {
        Assert.Equal("=CAJA;PCIA;null", FilterBuilder.Values(["CAJA   ", "PCIA", DBNull.Value], Texto));
        Assert.Equal("1;2.5", FilterBuilder.Values([1, 2.5m], Numero));
        Assert.Equal("=2026-07-01;2026-07-01 08:30:00", FilterBuilder.Values([new DateTime(2026, 7, 1), new DateTime(2026, 7, 1, 8, 30, 0)], Fecha));
        Assert.Equal("sí;null", FilterBuilder.Values([true, null], Booleano));
        Assert.Null(FilterBuilder.Values([], Texto));
        Assert.Equal("08:30", FilterBuilder.Literal(new TimeSpan(8, 30, 0), Hora));
        // Lo que arma se entiende igual en la grilla.
        Assert.True(ColumnFilter.Parse("=CAJA;PCIA;null", Texto)!.Matches(null));
        Assert.True(ColumnFilter.Parse("sí;null", Booleano)!.Matches(true));
        Assert.Equal(["CAJA", "PCIA", "null"], FilterBuilder.ListItems("=CAJA;PCIA;null").Order(StringComparer.Ordinal));
        Assert.Empty(FilterBuilder.ListItems("caja"));
    }

    [Theory]
    [InlineData("", "OBRA", FilterOperator.Contains, "", "")]
    [InlineData("!art", "OBRA", FilterOperator.NotContains, "art", "")]
    [InlineData("AL*", "OBRA", FilterOperator.StartsWith, "AL", "")]
    [InlineData("*ART", "OBRA", FilterOperator.EndsWith, "ART", "")]
    [InlineData("a*b", "OBRA", FilterOperator.Contains, "a*b", "")]
    [InlineData("=PCIA", "OBRA", FilterOperator.Equals, "PCIA", "")]
    [InlineData("null", "OBRA", FilterOperator.IsNull, "", "")]
    [InlineData(">= 5", "IMPORTE", FilterOperator.GreaterOrEqual, "5", "")]
    [InlineData("10..20", "IMPORTE", FilterOperator.Between, "10", "20")]
    [InlineData("1500", "IMPORTE", FilterOperator.Equals, "1500", "")]
    [InlineData("2026-07-01", "FECHA", FilterOperator.Day, "2026-07-01", "")]
    [InlineData("=2026-07-01", "FECHA", FilterOperator.Day, "2026-07-01", "")]
    [InlineData("este mes", "FECHA", FilterOperator.Relative, "este mes", "")]
    [InlineData("2026-07-01..2026-07-31", "FECHA", FilterOperator.Between, "2026-07-01", "2026-07-31")]
    [InlineData("1;2", "IMPORTE", FilterOperator.Equals, "", "")]             // una lista se ve tildada en los valores
    [InlineData("no", "ACTIVO", FilterOperator.No, "", "")]
    public void Abre_la_ventanita_con_lo_que_ya_estaba_escrito(string text, string column, FilterOperator op, string value, string value2)
    {
        var col = new[] { Texto, Numero, Fecha, Booleano }.Single(c => c.Name == column);
        Assert.Equal((op, value, value2), FilterBuilder.Describe(text, col));
    }
}
