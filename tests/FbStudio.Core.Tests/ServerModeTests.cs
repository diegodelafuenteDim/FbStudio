using FbStudio.Core.Metadata;

namespace FbStudio.Core.Tests;

public class ServerModeTests
{
    [Theory]
    // pid, conexiones remotas, procesos, Cache Writer, ve todo → tipo
    [InlineData(31, 2, 2, false, false, ServerMode.Classic)]                // procesos distintos: Classic, lo vea quien lo vea
    [InlineData(31, 40, 40, false, true, ServerMode.Classic)]
    [InlineData(33, 2, 1, true, true, ServerMode.SuperServer)]               // un proceso y el Cache Writer
    [InlineData(31, 2, 1, false, true, ServerMode.SuperClassic)]             // un proceso, se ve todo y no hay Cache Writer
    [InlineData(31, 2, 1, false, false, ServerMode.SuperOrSuperClassic)]     // sin RDB$ADMIN no se ven las internas
    [InlineData(31, 1, 1, false, true, ServerMode.Unknown)]                  // con una sola conexión no hay con qué comparar
    [InlineData(0, 0, 0, false, false, ServerMode.Unknown)]
    public void Deduce_el_tipo_de_servidor(long pid, int remote, int processes, bool cacheWriter, bool seesEveryone, ServerMode expected) =>
        Assert.Equal(expected, new ServerModeEvidence(pid, remote, processes, cacheWriter, seesEveryone).Mode);

    [Fact]
    public void Dice_como_se_supo()
    {
        Assert.Equal("Las 3 conexiones remotas que se ven corren en 3 procesos distintos.",
            new ServerModeEvidence(31, 3, 3, false, false).Reason);
        Assert.Contains("Cache Writer", new ServerModeEvidence(33, 2, 1, true, true).Reason);
        Assert.Contains("RDB$ADMIN", ServerModes.Description(ServerMode.SuperOrSuperClassic));
        Assert.Equal("SuperServer/SuperClassic", ServerModes.Label(ServerMode.SuperOrSuperClassic));
    }

    [Theory]
    [InlineData("Firebird/Linux/AMD/Intel/x64", "Linux x64")]
    [InlineData("Firebird/Windows/AMD/Intel/x64", "Windows x64")]
    [InlineData("Firebird/Windows/Intel/i386", "Windows x86")]
    [InlineData("Firebird/Darwin/ARM64", "macOS ARM64")]
    [InlineData("Firebird/Linux", "Linux")]
    [InlineData("algo raro", "algo raro")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void Describe_el_sistema_del_servidor(string? implementation, string? expected) =>
        Assert.Equal(expected, ServerPlatform.Describe(implementation));

    [Fact]
    public void El_arbol_y_el_tooltip_suman_el_tipo_y_el_sistema()
    {
        var info = new ServerInfo("3.0.11", 12, 0, 3, 16384, "NONE")
        {
            ModeEvidence = new ServerModeEvidence(145, 5, 5, false, true),
            Implementation = "Firebird/Windows/AMD/Intel/x64",
        };
        Assert.Equal("Firebird 3.0.11 · Classic", info.ShortDisplay);
        Assert.StartsWith("Firebird 3.0.11 · Classic · Windows x64 · ODS 12.0", info.Summary);

        var unknown = new ServerInfo("3.0.11", 12, 0, 3, 16384, "NONE");
        Assert.Equal("Firebird 3.0.11", unknown.ShortDisplay);                 // sin detectar, no se agrega nada
    }

    [Fact]
    public void Los_datos_del_servidor_dicen_la_cache_segun_el_tipo()
    {
        var classic = new ServerInfo("3.0.11", 12, 0, 3, 16384, "NONE")
        {
            ModeEvidence = new ServerModeEvidence(145, 5, 5, false, true), PageBuffers = 256,
        };
        var details = new ServerDetails(classic, "WI-V3.0.11.33703 Firebird 3.0");
        var rows = details.Rows("servidor:3050");
        string Value(string label) => rows.Single(r => r.Label == label).Value;

        Assert.Equal("servidor:3050", Value("Servidor"));
        Assert.Equal("Firebird 3.0.11 (WI-V3.0.11.33703 Firebird 3.0)", Value("Versión"));
        Assert.Equal("Classic", Value("Tipo"));
        Assert.Equal("256 páginas (4 MB) por conexión", Value("Caché"));
        Assert.Equal("—", Value("Sistema"));
        // Nunca rutas del servidor: ni el archivo de la base ni la carpeta de Firebird (para eso están los alias).
        Assert.DoesNotContain(rows, r => r.Label is "Archivo de la base" or "Carpeta de Firebird");
        Assert.Equal(rows.Count, details.Text("servidor:3050").Split(Environment.NewLine).Length);

        var super = classic with { ModeEvidence = new ServerModeEvidence(33, 5, 1, true, true), PageBuffers = 2048 };
        Assert.Equal("2.048 páginas (32 MB), compartida por todas las conexiones", new ServerDetails(super, null).Rows("x")
            .Single(r => r.Label == "Caché").Value);
    }

    [Fact]
    public void Tapa_la_ruta_de_la_base_con_como_la_nombra_el_registro()
    {
        Assert.Equal("Database \"clinica\"", ServerPaths.Hide("Database \"/db/bases/clinica.fdb\"", "/db/bases/clinica.fdb", "clinica"));
        Assert.Equal("no se pudo abrir clinica", ServerPaths.Hide(@"no se pudo abrir D:\BASES\CLINICA.FDB", @"d:\bases\clinica.fdb", "clinica"));
        Assert.Equal("sin ruta", ServerPaths.Hide("sin ruta", null, "clinica"));
        Assert.Equal("/db/x.fdb", ServerPaths.Hide("/db/x.fdb", "/db/x.fdb", "/db/x.fdb"));   // registrada con la ruta: queda igual
    }

    [Theory]
    [InlineData(4L << 20, "4 MB")]
    [InlineData(512L << 10, "512 KB")]
    [InlineData(3L << 30, "3 GB")]
    public void Tamano_legible(long bytes, string expected) => Assert.Equal(expected, ServerDetails.Size(bytes));
}
