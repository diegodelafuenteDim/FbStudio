using FbStudio.Core.Profiles;

namespace FbStudio.Workspace.Tests;

/// <summary>Backup y restore de los registros (F-22).</summary>
public sealed class ProfileBackupTests : IDisposable
{
    private const string Phrase = "una frase bastante larga";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-bkp-" + Guid.NewGuid().ToString("N"));

    private string File(string name) => Path.Combine(_folder, name);

    private ProfileStore Store(string name)
    {
        Directory.CreateDirectory(_folder);
        return new ProfileStore(Path.Combine(_folder, name), new MemorySecretStore());
    }

    private static ConnectionProfile P(string name, string db, string user = "SYSDBA", string? folder = null) => new()
    {
        Name = name, Host = "srv.example", Port = 3050, Database = db, User = user, Folder = folder,
        Environment = ProfileEnvironment.Produccion, ReadOnly = true, Role = "RDB$ADMIN",
    };

    private ProfileStore Origin()
    {
        var store = Store("origen");
        store.Upsert(P("Clinica", "clinica", folder: "Clientes/Norte"), "clave-clinica");
        store.Upsert(P("Stock", "stock"), "clave-stock");
        store.Upsert(P("Sin clave", "otra"), null);
        return store;
    }

    [Fact]
    public void Sin_contrasenas_queda_legible_y_conserva_todo_menos_la_clave()
    {
        var origin = Origin();
        ProfileBackup.Export(File("b.json"), origin.Load(), includeUser: true, origin.GetPassword, passphrase: null);

        var text = System.IO.File.ReadAllText(File("b.json"));
        Assert.Contains("srv.example", text);
        Assert.DoesNotContain("clave-", text);
        var file = ProfileBackup.Read(File("b.json"));
        Assert.False(file.IsEncrypted);
        Assert.Equal(3, file.Count);
        var content = ProfileBackup.Open(file, null);
        var clinica = content.Profiles.Single(p => p.Name == "Clinica");
        Assert.Equal(origin.Load().Single(p => p.Name == "Clinica"), clinica);       // carpeta, entorno, solo lectura, rol…
        Assert.Empty(content.Passwords);
    }

    [Fact]
    public void Sin_usuario_no_lleva_ni_usuario_ni_contrasena()
    {
        var origin = Origin();
        ProfileBackup.Export(File("b.json"), origin.Load(), includeUser: false, origin.GetPassword, Phrase);

        var file = ProfileBackup.Read(File("b.json"));
        Assert.False(file.IsEncrypted);                                         // sin usuario no van contraseñas
        var content = ProfileBackup.Open(file, null);
        Assert.All(content.Profiles, p => Assert.Equal("", p.User));
        Assert.Equal(3, content.WithoutUser);
        Assert.DoesNotContain("SYSDBA", System.IO.File.ReadAllText(File("b.json")));
    }

    [Fact]
    public void Con_contrasenas_todo_va_cifrado_y_solo_se_abre_con_la_frase()
    {
        var origin = Origin();
        ProfileBackup.Export(File("b.json"), origin.Load(), includeUser: true, origin.GetPassword, Phrase);

        var text = System.IO.File.ReadAllText(File("b.json"));
        Assert.DoesNotContain("clave-", text);
        Assert.DoesNotContain("srv.example", text);                             // ni el servidor: nadie lo puede cambiar
        Assert.DoesNotContain("Clinica", text);
        var file = ProfileBackup.Read(File("b.json"));
        Assert.True(file.IsEncrypted);
        Assert.Equal(3, file.Count);

        Assert.Equal(Core.Resources.CoreStrings.BackupWrongPhrase,
            Assert.Throws<InvalidOperationException>(() => ProfileBackup.Open(file, "otra frase cualquiera")).Message);
        Assert.Throws<InvalidOperationException>(() => ProfileBackup.Open(file, null));

        var content = ProfileBackup.Open(file, Phrase);
        Assert.Equal(3, content.Profiles.Count);
        Assert.Equal(2, content.Passwords.Count);                               // «Sin clave» no tenía
        Assert.Equal("clave-stock", content.Passwords[content.Profiles.Single(p => p.Name == "Stock").Id]);
    }

    [Fact]
    public void Un_archivo_alterado_no_se_abre()
    {
        var origin = Origin();
        ProfileBackup.Export(File("b.json"), origin.Load(), includeUser: true, origin.GetPassword, Phrase);
        var file = ProfileBackup.Read(File("b.json"));
        var data = Convert.FromBase64String(file.Encrypted!.Data);
        data[40] ^= 1;
        var altered = file with { Encrypted = file.Encrypted with { Data = Convert.ToBase64String(data) } };
        Assert.Throws<InvalidOperationException>(() => ProfileBackup.Open(altered, Phrase));
        var fewRounds = file with { Encrypted = file.Encrypted with { Iterations = 1000 } };   // no se acepta debilitarlo
        Assert.Throws<InvalidOperationException>(() => ProfileBackup.Open(fewRounds, Phrase));
    }

    [Fact]
    public void Frase_corta_archivo_ajeno_y_version_nueva()
    {
        var origin = Origin();
        Assert.Throws<ArgumentException>(() => ProfileBackup.Export(File("b.json"), origin.Load(), true, origin.GetPassword, "corta"));

        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File("ajeno.json"), "[{\"Name\": \"x\"}]");
        Assert.Equal(Core.Resources.CoreStrings.BackupNotOurs, Assert.Throws<InvalidOperationException>(() => ProfileBackup.Read(File("ajeno.json"))).Message);
        System.IO.File.WriteAllText(File("nuevo.json"), "{\"Format\": \"FbStudio.Profiles\", \"Version\": 9, \"Count\": 0, \"Profiles\": []}");
        Assert.Contains("9", Assert.Throws<InvalidOperationException>(() => ProfileBackup.Read(File("nuevo.json"))).Message);
    }

    [Fact]
    public void Importa_en_otra_PC_con_las_contrasenas_y_reconoce_los_repetidos()
    {
        var origin = Origin();
        ProfileBackup.Export(File("b.json"), origin.Load(), includeUser: true, origin.GetPassword, Phrase);
        var content = ProfileBackup.Open(ProfileBackup.Read(File("b.json")), Phrase);

        // Otra PC: ya tiene «Stock» (registrado a mano, otro Id) y otra base que se llama «Clinica».
        var target = Store("destino");
        var mine = P("Stock mío", "STOCK") with { Folder = null };
        target.Upsert(mine, "clave-mia");
        target.Upsert(P("Clinica", "otra-clinica") with { Host = "otro.example" }, null);

        var plan = ProfileBackup.Plan(target.Load(), content);
        Assert.Equal(mine.Id, plan.Single(i => i.Profile.Name == "Stock").Existing!.Id);   // misma base, servidor y usuario
        Assert.Null(plan.Single(i => i.Profile.Name == "Clinica").Existing);

        var result = ProfileBackup.Apply(target, plan, updateExisting: false);
        Assert.Equal(new ProfileImportResult(Added: 2, Updated: 0, Skipped: 1, Invalid: 0, WithPassword: 1), result);
        var all = target.Load();
        var clinica = all.Single(p => p.Name == "Clinica (2)");                 // el nombre ya estaba
        Assert.Equal(("Clientes/Norte", ProfileEnvironment.Produccion, true), (clinica.Folder, clinica.Environment, clinica.ReadOnly));
        Assert.Equal("clave-clinica", target.GetPassword(clinica.Id));
        Assert.Equal("clave-mia", target.GetPassword(mine.Id));                 // el repetido quedó como estaba

        // Otra vez, actualizando: el mismo Id se reconoce y no se duplica.
        var again = ProfileBackup.Apply(target, ProfileBackup.Plan(target.Load(), content), updateExisting: true);
        Assert.Equal((0, 3), (again.Added, again.Updated));
        Assert.Equal(4, target.Load().Count);
        Assert.Equal("clave-stock", target.GetPassword(mine.Id));               // actualizado con la del archivo
        Assert.Equal("Stock", target.Load().Single(p => p.Id == mine.Id).Name);
    }

    [Fact]
    public void Sin_usuario_se_completa_con_el_que_se_elige_al_importar()
    {
        var origin = Origin();
        ProfileBackup.Export(File("b.json"), origin.Load(), includeUser: false, origin.GetPassword, null);
        var content = ProfileBackup.Open(ProfileBackup.Read(File("b.json")), null);
        var bad = P("", "x");
        content = content with { Profiles = [.. content.Profiles, bad] };

        var target = Store("colega");
        var result = ProfileBackup.Apply(target, ProfileBackup.Plan(target.Load(), content), updateExisting: false, user: "JPEREZ", password: "suya");
        Assert.Equal((3, 1, 3), (result.Added, result.Invalid, result.WithPassword));
        Assert.All(target.Load(), p => Assert.Equal("JPEREZ", p.User));
        Assert.All(target.Load(), p => Assert.Equal("suya", target.GetPassword(p.Id)));
    }

    [Fact]
    public void Un_archivo_no_baja_protecciones_ni_lleva_la_contrasena_a_otro_servidor()
    {
        var target = Store("mio");
        var mine = P("Clinica", "clinica") with { WireCrypt = WireCryptMode.Required, AreasFile = @"C:\Areas\clinica.json" };
        target.Upsert(mine, "clave-mia");
        var same = P("Stock", "stock") with { ReadOnly = false, Environment = ProfileEnvironment.Desarrollo };
        target.Upsert(same, "clave-stock");

        // El mismo registro (mismo Id) apuntando a otro servidor, sin protecciones ni contraseña; otro igual pero con
        // solo lectura (subir protecciones sí se acepta); y uno nuevo con un archivo de áreas en otra PC.
        var content = new ProfileBackupContent
        {
            Profiles =
            [
                mine with { Host = "otro.example", ReadOnly = false, Environment = ProfileEnvironment.Desarrollo, WireCrypt = WireCryptMode.Disabled,
                    AreasFile = @"\\otro\areas.json" },
                same with { ReadOnly = true },
                P("Nueva", "nueva") with { AreasFile = @"\\otro\areas.json" },
            ],
        };
        var result = ProfileBackup.Apply(target, ProfileBackup.Plan(target.Load(), content), updateExisting: true);
        Assert.Equal(new ProfileImportResult(Added: 1, Updated: 2, Skipped: 0, Invalid: 0, WithPassword: 0, PasswordsForgotten: 1, KeptProtection: 1),
            result);

        var clinica = target.Load().Single(p => p.Id == mine.Id);
        Assert.Equal("otro.example", clinica.Host);                                 // lo demás se actualiza…
        Assert.Equal((true, ProfileEnvironment.Produccion, WireCryptMode.Required), (clinica.ReadOnly, clinica.Environment, clinica.WireCrypt));
        Assert.Equal(@"C:\Areas\clinica.json", clinica.AreasFile);                 // …menos las protecciones y las áreas
        Assert.Null(target.GetPassword(mine.Id));                                   // la clave era de otro servidor
        Assert.True(target.Load().Single(p => p.Id == same.Id).ReadOnly);
        Assert.Equal("clave-stock", target.GetPassword(same.Id));                   // mismo servidor: la conserva
        Assert.Null(target.Load().Single(p => p.Name == "Nueva").AreasFile);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
