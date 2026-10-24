using System.Security.Cryptography;

namespace FbStudio.Workspace.Tests;

/// <summary>Contraseñas e historial fuera de Windows: el llavero de Linux y el cifrado del historial con su clave.</summary>
public sealed class SecretStoreTests
{
    [Fact]
    public void El_historial_se_cifra_con_AES_y_HMAC_y_detecta_cambios()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var protector = new AesDataProtector(key);
        var data = protector.Protect("SELECT * FROM PACIENTES WHERE DNI = '12345678'");
        Assert.DoesNotContain("12345678", System.Text.Encoding.UTF8.GetString(data));
        Assert.Equal("SELECT * FROM PACIENTES WHERE DNI = '12345678'", new AesDataProtector(key).Unprotect(data));
        Assert.NotEqual(data, protector.Protect("SELECT * FROM PACIENTES WHERE DNI = '12345678'"));      // IV al azar

        var tampered = (byte[])data.Clone();
        tampered[20] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
        Assert.ThrowsAny<CryptographicException>(() => new AesDataProtector(RandomNumberGenerator.GetBytes(32)).Unprotect(data)); // otra clave
        Assert.Throws<ArgumentException>(() => new AesDataProtector(new byte[16]));
    }

    [Fact]
    public void La_clave_del_historial_se_crea_una_vez_y_queda_en_el_llavero()
    {
        var keyring = new MemorySecretStore();
        var first = AesDataProtector.FromSecretStore(keyring);
        var stored = keyring.Get(AesDataProtector.KeyName);
        Assert.NotNull(stored);
        var second = AesDataProtector.FromSecretStore(keyring);                 // la próxima vez, la misma clave
        Assert.Equal(stored, keyring.Get(AesDataProtector.KeyName));
        Assert.Equal("hola", second.Unprotect(first.Protect("hola")));
        Assert.False(keyring.IsPersistent);
    }

    [Fact]
    public void Sin_llavero_las_contrasenas_quedan_solo_en_memoria()
    {
        var folder = Path.Combine(Path.GetTempPath(), "fbstudio-sec-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProfileStore(folder, new MemorySecretStore());
            Assert.False(store.SavesPasswords);
            var profile = new Core.Profiles.ConnectionProfile { Name = "p", Host = "h", Database = "d", User = "u" };
            store.Upsert(profile, "secreta");
            Assert.Equal("secreta", store.GetPassword(profile.Id));
            Assert.False(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("secreta")));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void En_Linux_el_llavero_del_escritorio_guarda_lee_y_borra()
    {
        // Solo donde hay Secret Service (un escritorio, o el contenedor de prueba con gnome-keyring).
        if (!OperatingSystem.IsLinux() || !LinuxSecretStore.IsAvailable()) return;
        var store = new LinuxSecretStore();
        var key = "test-" + Guid.NewGuid().ToString("N");
        Assert.Null(store.Get(key));
        store.Set(key, "con ñ, comillas \" y espacios ");
        Assert.Equal("con ñ, comillas \" y espacios ", store.Get(key));
        store.Set(key, "otra");                                                  // reemplaza
        Assert.Equal("otra", store.Get(key));
        store.Remove(key);
        Assert.Null(store.Get(key));
    }

    [Fact]
    public void Un_llavero_que_no_esta_no_se_ofrece()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.False(LinuxSecretStore.IsAvailable("no-existe-secret-tool"));
    }
}
