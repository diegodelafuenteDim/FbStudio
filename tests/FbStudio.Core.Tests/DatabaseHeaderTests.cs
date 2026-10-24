using FbStudio.Core.Statistics;

namespace FbStudio.Core.Tests;

public class DatabaseHeaderTests
{
    // Salida de gstat -h de Firebird 3 (con tabuladores, como la manda el servidor).
    private static readonly string[] Sample =
    [
        "",
        "Database \"/firebird/data/test.fdb\"",
        "Gstat execution time Mon Oct  5 16:50:01 2026",
        "",
        "Database header page information:",
        "\tFlags\t\t\t0",
        "\tGeneration\t\t1234",
        "\tSystem Change Number\t0",
        "\tPage size\t\t8192",
        "\tODS version\t\t12.0",
        "\tOldest transaction\t9051",
        "\tOldest active\t\t9052",
        "\tOldest snapshot\t\t39052",
        "\tNext transaction\t42158",
        "\tSequence number\t\t0",
        "\tNext attachment ID\t123",
        "\tImplementation\t\tHW=AMD/Intel/x64 little-endian OS=Windows CC=MSVC",
        "\tShadow count\t\t0",
        "\tPage buffers\t\t0",
        "\tNext header page\t0",
        "\tDatabase dialect\t3",
        "\tCreation date\t\tOct 1, 2026 10:00:00",
        "\tAttributes\t\tforce write",
        "",
        "    Variable header data:",
        "\tSweep interval:\t\t20000",
        "\t*END*",
        "Gstat completion time Mon Oct  5 16:50:01 2026",
    ];

    [Fact]
    public void Lee_el_encabezado_de_gstat()
    {
        var h = DatabaseHeader.Parse(Sample);
        Assert.Equal("/firebird/data/test.fdb", h.Database);
        Assert.Equal((8192L, "12.0", 1234L, 3L), (h.PageSize!.Value, h.OdsVersion, h.Generation!.Value, h.Dialect!.Value));
        Assert.Equal((9051L, 9052L, 39052L, 42158L), (h.OldestTransaction!.Value, h.OldestActive!.Value, h.OldestSnapshot!.Value, h.NextTransaction!.Value));
        Assert.Equal((20000, false), (h.SweepInterval, h.SweepIntervalIsDefault));
        Assert.True(h.ForcedWrites);
        Assert.Equal("HW=AMD/Intel/x64 little-endian OS=Windows CC=MSVC", h.Value("Implementation"));
        Assert.Equal("Oct 1, 2026 10:00:00", h.Value("creation date"));       // sin importar mayúsculas
        Assert.Equal(33106, h.ActiveGap);                                     // próxima − OAT
        Assert.Equal(30001, h.SweepGap);                                      // OST − OIT
        Assert.True(h.SweepDue);                                              // pasó el intervalo de 20.000
        Assert.Equal(19, h.Entries.Count);                                    // sin títulos, fechas de gstat ni *END*
        Assert.DoesNotContain(h.Entries, e => e.Key.StartsWith("Gstat") || e.Key.StartsWith("Database header"));
    }

    [Fact]
    public void Sin_sweep_automatico_no_avisa_y_tolera_salida_incompleta()
    {
        var h = DatabaseHeader.Parse(["\tSweep interval:\t\t0", "\tOldest transaction\t1", "\tOldest snapshot\t\t900000"]);
        Assert.False(h.SweepDue);
        Assert.Null(h.NextTransaction);
        Assert.Equal((20000, true), (DatabaseHeader.Parse(["\tPage size\t\t8192"]).SweepInterval, DatabaseHeader.Parse([]).SweepIntervalIsDefault));
        Assert.Null(h.ActiveGap);
        Assert.False(h.ForcedWrites);
        Assert.Empty(DatabaseHeader.Parse([]).Entries);
    }
}
