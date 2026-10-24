using FbStudio.Core.Statistics;

namespace FbStudio.Core.Tests;

public class DatabaseAnalysisTests
{
    // Salida real de gstat -a -r de Firebird 3 (el contenedor de los tests), desde «Analyzing database pages».
    private static readonly string[] Sample =
    [
        "Database \"/firebird/data/test\"",
        "\tPage size\t\t8192",
        "Analyzing database pages ...",
        "TURNOS (128)",
        "    Primary pointer page: 181, Index root page: 182",
        "    Total formats: 1, used formats: 1",
        "    Average record length: 171.81, total records: 50000",
        "    Average version length: 0.00, total versions: 0, max versions: 0",
        "    Average fragment length: 0.00, total fragments: 0, max fragments: 0",
        "    Average unpacked length: 8272.00, compression ratio: 48.15",
        "    Pointer pages: 1, data page slots: 1312",
        "    Data pages: 1312, average fill: 88%",
        "    Primary pages: 1312, secondary pages: 0, swept pages: 0",
        "    Empty pages: 2, full pages: 1309",
        "    Fill distribution:",
        "\t 0 - 19% = 2",
        "\t20 - 39% = 0",
        "\t40 - 59% = 1",
        "\t60 - 79% = 0",
        "\t80 - 99% = 1309",
        "",
        "    Index RDB$PRIMARY1 (0)",
        "\tRoot page: 265, depth: 2, leaf buckets: 32, nodes: 50000",
        "\tAverage node length: 5.16, total dup: 0, max dup: 0",
        "\tAverage key length: 3.00, compression ratio: 1.21",
        "\tAverage prefix length: 2.62, average data length: 1.00",
        "\tClustering factor: 1310, ratio: 0.03",
        "\tFill distribution:",
        "\t     0 - 19% = 0",
        "\t    20 - 39% = 0",
        "\t    40 - 59% = 0",
        "\t    60 - 79% = 0",
        "\t    80 - 99% = 32",
        "",
        "    Index TURNOS_FECHA (1)",
        "\tRoot page: 223, depth: 2, leaf buckets: 50, nodes: 50000",
        "\tAverage node length: 7.95, total dup: 0, max dup: 0",
        "\tAverage key length: 5.79, compression ratio: 1.34",
        "\tAverage prefix length: 4.96, average data length: 2.79",
        "\tClustering factor: 1310, ratio: 0.03",
        "\tFill distribution:",
        "\t     0 - 19% = 0",
        "\t    20 - 39% = 1",
        "\t    40 - 59% = 0",
        "\t    60 - 79% = 0",
        "\t    80 - 99% = 49",
        "",
        "TURNOS_DET (130)",
        "    Primary pointer page: 195, Index root page: 196",
        "    Total formats: 1, used formats: 0",
        "    Average record length: 0.00, total records: 0",
        "    Average version length: 0.00, total versions: 0, max versions: 0",
        "    Average fragment length: 0.00, total fragments: 0, max fragments: 0",
        "    Average unpacked length: 0.00, compression ratio: 0.00",
        "    Pointer pages: 1, data page slots: 0",
        "    Data pages: 0, average fill: 0%",
        "    Primary pages: 0, secondary pages: 0, swept pages: 0",
        "    Empty pages: 0, full pages: 0",
        "    Fill distribution:",
        "\t 0 - 19% = 0",
        "\t20 - 39% = 0",
        "\t40 - 59% = 0",
        "\t60 - 79% = 0",
        "\t80 - 99% = 0",
        "",
        "    Index FK_DET_TURNO (1)",
        "\tRoot page: 198, depth: 1, leaf buckets: 1, nodes: 0",
        "\tAverage node length: 0.00, total dup: 0, max dup: 0",
        "\tAverage key length: 0.00, compression ratio: 0.00",
        "\tAverage prefix length: 0.00, average data length: 0.00",
        "\tClustering factor: 0, ratio: 0.00",
        "\tFill distribution:",
        "\t     0 - 19% = 1",
        "\t    20 - 39% = 0",
        "\t    40 - 59% = 0",
        "\t    60 - 79% = 0",
        "\t    80 - 99% = 0",
        "",
        "    Index PK_TURNOS_DET (0)",
        "\tRoot page: 197, depth: 1, leaf buckets: 1, nodes: 0",
        "\tAverage node length: 0.00, total dup: 0, max dup: 0",
        "\tAverage key length: 0.00, compression ratio: 0.00",
        "\tAverage prefix length: 0.00, average data length: 0.00",
        "\tClustering factor: 0, ratio: 0.00",
        "\tFill distribution:",
        "\t     0 - 19% = 1",
        "\t    20 - 39% = 0",
        "\t    40 - 59% = 0",
        "\t    60 - 79% = 0",
        "\t    80 - 99% = 0",
        "",
        "Gstat completion time Mon Oct 19 23:46:17 2026",
        "Gstat completion time Mon Oct 19 23:46:17 2026",
    ];

    [Fact]
    public void Lee_tablas_e_indices_de_la_estadistica_completa()
    {
        var analysis = DatabaseAnalysis.Parse(Sample);
        Assert.Equal(["TURNOS", "TURNOS_DET"], analysis.Tables.Select(t => t.Name));
        var turnos = analysis.Tables[0];
        Assert.Equal((128, 50000L, 171.81, 1312L, 88L), (turnos.Id, turnos.Records!.Value, turnos.AverageRecordLength!.Value, turnos.DataPages!.Value, turnos.AverageFill!.Value));
        Assert.Equal((0L, 0L, 0L, 2L, 1309L), (turnos.Versions!.Value, turnos.MaxVersions!.Value, turnos.Fragments!.Value, turnos.EmptyPages!.Value, turnos.FullPages!.Value));
        Assert.Equal(48.15, turnos.CompressionRatio);
        Assert.False(turnos.ManyVersions);

        Assert.Equal(["RDB$PRIMARY1", "TURNOS_FECHA"], turnos.Indexes.Select(i => i.Name));
        var fecha = turnos.Indexes[1];
        Assert.Equal(("TURNOS", 1, 2L, 50L, 50000L), (fecha.Table, fecha.Id, fecha.Depth!.Value, fecha.LeafBuckets!.Value, fecha.Nodes!.Value));
        Assert.Equal((0L, 0L, 5.79, 1310L), (fecha.TotalDuplicates!.Value, fecha.MaxDuplicates!.Value, fecha.AverageKeyLength!.Value, fecha.ClusteringFactor!.Value));
        Assert.Equal(0.0, fecha.DuplicateRatio);
        Assert.Equal(0.03, fecha.Double("ratio"));                              // del clustering, no se confunde con «compression ratio»
        Assert.Equal(4, analysis.Indexes.Count());

        var vacia = analysis.Tables[1];
        Assert.Equal((0L, 0L), (vacia.Records!.Value, vacia.DataPages!.Value));
        Assert.Null(vacia.Indexes[0].DuplicateRatio);                           // sin nodos no hay proporción
    }

    [Fact]
    public void Marca_tablas_con_muchas_versiones_e_indices_profundos_o_poco_selectivos()
    {
        var analysis = DatabaseAnalysis.Parse(
        [
            "Analyzing database pages ...",
            "CLINICA_TURNOS (140)",
            "    Average record length: 210.00, total records: 1000000",
            "    Average version length: 40.00, total versions: 650000, max versions: 3",
            "    Index CLINICA_TURNOS_FECHA (2)",
            "\tRoot page: 1, depth: 4, leaf buckets: 9000, nodes: 1000000",
            "\tAverage node length: 5.00, total dup: 999000, max dup: 400000",
            "LOG (141)",
            "    Average record length: 50.00, total records: 10",
            "    Average version length: 10.00, total versions: 2, max versions: 12",
            "    Index LOG_IDX (0)",
            "\tRoot page: 2, depth: 1, leaf buckets: 1, nodes: 10",
            "\tAverage node length: 5.00, total dup: 10, max dup: 10",
        ]);
        var (turnos, log) = (analysis.Tables[0], analysis.Tables[1]);
        Assert.True(turnos.ManyVersions);                                       // más de la mitad de los registros
        Assert.True(log.ManyVersions);                                          // un registro con 12 versiones
        var fecha = turnos.Indexes[0];
        Assert.True(fecha.TooDeep);
        Assert.True(fecha.PoorSelectivity);
        Assert.Equal(0.999, fecha.DuplicateRatio);
        Assert.False(log.Indexes[0].PoorSelectivity);                           // pocas filas: no importa
        Assert.Empty(DatabaseAnalysis.Parse(["\tPage size\t\t8192"]).Tables); // solo el encabezado
    }
}
