using FbStudio.Mcp;

namespace FbStudio.Mcp.Tests;

/// <summary>La copia virtual de %APPDATA% de un paquete MSIX, armada en una carpeta temporal (corre en cualquier sistema).</summary>
public sealed class WindowsPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbstudio-msix-" + Guid.NewGuid().ToString("N"));
    private string Roaming => Path.Combine(_root, "Roaming");
    private string Packages => Path.Combine(_root, "Packages");
    private string Data => Path.Combine(Roaming, "FbStudio");

    private string Write(string folder, string content, DateTime stamp)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "profiles.json");
        File.WriteAllText(file, content);
        File.SetLastWriteTimeUtc(file, stamp);
        return file;
    }

    private string Copy(string package) => Path.Combine(Packages, package, "LocalCache", "Roaming", "FbStudio");

    [Fact]
    public void Reconoce_que_lo_que_ve_es_la_copia_del_paquete()
    {
        var stamp = new DateTime(2026, 10, 18, 22, 31, 0, DateTimeKind.Utc);
        Write(Data, "[dos registros]", stamp);                                   // desde adentro, lo que se ve es la copia…
        Write(Copy("Otra_app"), "[otra cosa]", stamp);
        Write(Copy("App_123"), "[dos registros]", stamp);                        // …que está en la carpeta del paquete
        Assert.Equal("App_123", WindowsPackage.VirtualCopyInUse(Data, Roaming, Packages));
    }

    [Fact]
    public void Desde_afuera_lo_real_no_coincide_con_la_copia()
    {
        Write(Data, "[veintiséis registros, los reales]", new DateTime(2026, 10, 25, 14, 39, 0, DateTimeKind.Utc));
        Write(Copy("App_123"), "[dos registros]", new DateTime(2026, 10, 18, 22, 31, 0, DateTimeKind.Utc));
        Assert.Null(WindowsPackage.VirtualCopyInUse(Data, Roaming, Packages));
    }

    [Fact]
    public void Una_carpeta_de_datos_propia_o_sin_paquetes_no_avisa()
    {
        var stamp = new DateTime(2026, 10, 18, 22, 31, 0, DateTimeKind.Utc);
        var own = Path.Combine(_root, "Portable", "FbStudio");
        Write(own, "[x]", stamp);
        Write(Copy("App_123"), "[x]", stamp);
        Assert.Null(WindowsPackage.VirtualCopyInUse(own, Roaming, Packages));     // --data, fuera de %APPDATA%
        Write(Data, "[x]", stamp);
        Assert.Null(WindowsPackage.VirtualCopyInUse(Data, Roaming, Path.Combine(_root, "NoHay")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
