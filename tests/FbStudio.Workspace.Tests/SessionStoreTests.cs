using System.Text;

namespace FbStudio.Workspace.Tests;

/// <summary>Recordar las pestañas de consulta al cerrar.</summary>
public sealed class SessionStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-session-" + Guid.NewGuid().ToString("N"));

    /// <summary>Un «cifrado» de prueba (da vuelta los bytes): alcanza para ver que no se guarda en claro.</summary>
    private sealed class FakeProtector : IDataProtector
    {
        public byte[] Protect(string text) => Encoding.UTF8.GetBytes(text).Reverse().ToArray();
        public string Unprotect(byte[] data) => Encoding.UTF8.GetString(data.Reverse().ToArray());
    }

    [Fact]
    public void Guarda_cifrado_lo_vuelve_a_leer_y_lo_olvida()
    {
        var store = new SessionStore(_folder, new FakeProtector());
        var id = Guid.NewGuid();
        store.Save(new SessionFile { Tabs = [new SessionTab(id, "Consulta 3", null, "SELECT 'revisión' FROM T"), new SessionTab(null, "a.sql", @"C:\x\a.sql", null)], Selected = 1 });
        var raw = File.ReadAllBytes(store.FilePath!);
        Assert.DoesNotContain("SELECT", Encoding.UTF8.GetString(raw));                    // no queda en claro
        var again = new SessionStore(_folder, new FakeProtector()).Load()!;
        Assert.Equal(1, again.Selected);
        Assert.Equal((id, "Consulta 3", "SELECT 'revisión' FROM T"), (again.Tabs[0].ProfileId!.Value, again.Tabs[0].Title, again.Tabs[0].Text!));
        Assert.Equal(@"C:\x\a.sql", again.Tabs[1].FilePath);

        File.WriteAllText(store.FilePath!, "roto");
        Assert.Null(store.Load());                                                         // un archivo roto se ignora
        store.Clear();
        Assert.False(File.Exists(store.FilePath!));

        // Sin cifrado disponible: solo en memoria.
        var memory = new SessionStore(_folder, null);
        Assert.Null(memory.FilePath);
        memory.Save(new SessionFile { Tabs = [new SessionTab(null, "Consulta 1", null, "x")] });
        Assert.Single(memory.Load()!.Tabs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
