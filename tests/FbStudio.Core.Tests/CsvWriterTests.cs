using FbStudio.Core.Export;
using FbStudio.Core.Results;

namespace FbStudio.Core.Tests;

public class CsvWriterTests
{
    private static readonly ColumnInfo[] Columns =
    [
        new(0, "ID", "INTEGER"), new(1, "NOMBRE", "VARCHAR"), new(2, "IMPORTE", "NUMERIC"), new(3, "FECHA", "TIMESTAMP"), new(4, "OBS", "VARCHAR"),
    ];

    private static string Write(CsvOptions o, params object[][] rows)
    {
        using var w = new StringWriter();
        CsvWriter.WriteHeader(w, Columns, o);
        foreach (var r in rows) CsvWriter.WriteRow(w, r, Columns, o);
        return w.ToString();
    }

    [Fact]
    public void Formato_Excel_en_espanol_por_defecto()
    {
        var csv = Write(CsvOptions.ExcelAr, [1, "PÉREZ, JUAN", 1234.5m, new DateTime(2026, 7, 30, 9, 15, 0), DBNull.Value]);
        Assert.Equal("ID;NOMBRE;IMPORTE;FECHA;OBS\r\n1;PÉREZ, JUAN;1234,5;2026-07-30 09:15:00;\r\n", csv);
    }

    [Fact]
    public void Entrecomilla_solo_cuando_hace_falta()
    {
        var csv = Write(CsvOptions.ExcelAr, [2, "dice \"hola\"; chau", 0m, DBNull.Value, "línea 1\nlínea 2"]);
        Assert.Equal("2;\"dice \"\"hola\"\"; chau\";0;;\"línea 1\nlínea 2\"\r\n", csv.Split("\r\n", 2)[1]);
    }

    [Fact]
    public void Separador_y_decimal_configurables()
    {
        var csv = Write(new CsvOptions(Separator: ',', DecimalSeparator: '.', Header: false), [3, "A", 10.25m, DBNull.Value, "B"]);
        Assert.Equal("3,A,10.25,,B\r\n", csv);
    }

    [Fact]
    public void Los_textos_que_Excel_tomaria_como_formula_van_con_apostrofo()
    {
        var o = CsvOptions.ExcelAr with { Header = false };
        Assert.Equal("1;'=1+1;-12,5;;'@SUM(A1)\r\n", Write(o, [1, "=1+1", -12.5m, DBNull.Value, "@SUM(A1)"]));
        Assert.Equal("2;'-x;0;;-5\r\n", Write(o, [2, "-x", 0m, DBNull.Value, "-5"]));          // un número negativo como texto, no
        Assert.Equal("3;'+54 11;0;;'\tx\r\n", Write(o, [3, "+54 11", 0m, DBNull.Value, "\tx"]));
        Assert.Equal("4;=1+1;0;;x\r\n", Write(o with { ProtectFormulas = false }, [4, "=1+1", 0m, DBNull.Value, "x"]));
    }

    [Fact]
    public void Quita_el_relleno_de_los_CHAR() =>
        Assert.Equal("ABC", CsvWriter.FormatValue("ABC   ", new ColumnInfo(0, "C", "CHAR"), CsvOptions.ExcelAr));
}
