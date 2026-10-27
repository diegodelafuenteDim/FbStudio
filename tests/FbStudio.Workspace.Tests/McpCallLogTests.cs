using System.Text;

namespace FbStudio.Workspace.Tests;

/// <summary>El registro de llamadas del conector MCP (D-117).</summary>
public sealed class McpCallLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbstudio-mcplog-" + Guid.NewGuid().ToString("N"));
    private string Roaming => Path.Combine(_root, "Roaming");
    private string Packages => Path.Combine(_root, "Packages");
    private string Folder => Path.Combine(Roaming, "FbStudio");

    /// <summary>Un «cifrado» de prueba (da vuelta los bytes): alcanza para ver que no se guarda en claro.</summary>
    private sealed class FakeProtector : IDataProtector
    {
        public byte[] Protect(string text) => Encoding.UTF8.GetBytes(text).Reverse().ToArray();
        public string Unprotect(byte[] data) => Encoding.UTF8.GetString(data.Reverse().ToArray());
    }

    private McpCallLog Log(string? folder = null, IDataProtector? protector = null, int days = 90) =>
        new(folder ?? Folder, protector ?? new FakeProtector(), TimeSpan.FromDays(days));

    [Fact]
    public void Guarda_cada_llamada_con_lo_pedido_cifrado_y_las_lee_la_mas_nueva_primero()
    {
        var log = Log();
        log.Add(new McpCall(DateTime.Now.AddMinutes(-5), "get_schema", "Base de pruebas", "area=LAB", 120, true, 3500));
        log.Add(new McpCall(DateTime.Now, "explain_query", "Base de pruebas", "SELECT * FROM T WHERE DNI = 20123456", 40, false, 80, "Column unknown X"));

        var raw = File.ReadAllText(log.FilePath);
        Assert.Contains("\"tool\":\"explain_query\"", raw);                                  // lo que no es sensible, a la vista
        Assert.DoesNotContain("20123456", raw);                                             // lo pedido y el error, cifrados
        Assert.DoesNotContain("Column unknown", raw);

        var calls = Log().Read(packages: Packages, roaming: Roaming);
        Assert.Equal(["explain_query", "get_schema"], calls.Select(c => c.Tool));
        Assert.Equal(("SELECT * FROM T WHERE DNI = 20123456", false, "Column unknown X"), (calls[0].Arguments, calls[0].Ok, calls[0].Error));
        Assert.Equal(("area=LAB", 120L, 3500), (calls[1].Arguments, calls[1].Milliseconds, calls[1].Characters));
    }

    [Fact]
    public void Sin_cifrado_no_guarda_lo_pedido_y_un_renglon_roto_se_saltea()
    {
        var plain = new McpCallLog(Folder, null, TimeSpan.FromDays(90));
        plain.Add(new McpCall(DateTime.Now, "describe_object", "Pruebas", "name=PACIENTES", 10, true, 100));
        File.AppendAllText(plain.FilePath, "{\"time\":\"2026-10-\n");                           // cortado (disco lleno)
        Assert.DoesNotContain("PACIENTES", File.ReadAllText(plain.FilePath));
        var call = Assert.Single(plain.Read(packages: Packages, roaming: Roaming));
        Assert.Equal(("describe_object", null), (call.Tool, call.Arguments));
    }

    [Fact]
    public void Purga_las_llamadas_mas_viejas_que_la_retencion()
    {
        var log = Log(days: 7);
        log.Add(new McpCall(DateTime.Now.AddDays(-30), "get_schema", "A", null, 1, true, 1));
        log.Add(new McpCall(DateTime.Now.AddDays(-1), "get_schema", "B", null, 1, true, 1));
        log.Purge();
        Assert.Single(File.ReadAllLines(log.FilePath));
        Assert.Equal("B", Assert.Single(log.Read(packages: Packages, roaming: Roaming)).Database);
    }

    [Fact]
    public void Junta_lo_que_el_conector_escribio_dentro_de_un_paquete_MSIX()
    {
        Log().Add(new McpCall(DateTime.Now.AddMinutes(-1), "list_databases", null, null, 5, true, 200));
        // Lanzado desde una app con paquete, el conector escribió en la copia de %APPDATA% de ese paquete.
        var copy = Path.Combine(Packages, "App_123", "LocalCache", "Roaming", "FbStudio");
        Log(copy).Add(new McpCall(DateTime.Now, "get_schema", "Base de pruebas", "kind=table", 900, true, 9000));

        var calls = Log().Read(packages: Packages, roaming: Roaming);
        Assert.Equal(["get_schema", "list_databases"], calls.Select(c => c.Tool));
        Assert.Equal("kind=table", calls[0].Arguments);
        Assert.Empty(Log(Path.Combine(_root, "Portable")).Read(packages: Packages, roaming: Roaming)); // --data, fuera de %APPDATA%: sin copias
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
