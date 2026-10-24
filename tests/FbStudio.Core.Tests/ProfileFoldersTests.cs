using FbStudio.Core.Profiles;

namespace FbStudio.Core.Tests;

public class ProfileFoldersTests
{
    private static ConnectionProfile P(string? folder) => new() { Name = "x", Host = "h", Database = "d", User = "u", Folder = folder };

    [Fact]
    public void Las_carpetas_se_comparan_sin_mayusculas_ni_espacios_y_vacia_es_ninguna()
    {
        var existing = new[] { P("Clinica"), P(" clinica"), P(null), P("  "), P("Sucursal") };
        Assert.Equal(["Clinica", "Sucursal"], ProfileFolders.All(existing));
        Assert.Equal("Clinica", ProfileFolders.Normalize("  CLINICA ", existing));      // usa la que ya existe
        Assert.Equal("Nueva", ProfileFolders.Normalize(" Nueva ", existing));
        Assert.Null(ProfileFolders.Normalize("   ", existing));
        Assert.Null(ProfileFolders.Normalize(null, existing));
        Assert.True(ProfileFolders.Same("Clinica", " CLINICA"));
        Assert.False(ProfileFolders.Same(null, null));                                  // sin carpeta no es «la misma carpeta»
    }

    [Fact]
    public void Una_carpeta_puede_ir_dentro_de_otra_con_la_barra()
    {
        var existing = new[] { P("Clinica/Producción"), P("Sucursal") };
        Assert.Equal(["Clinica", "Clinica/Producción", "Sucursal"], ProfileFolders.All(existing)); // también la que solo tiene carpetas
        Assert.Equal("Clinica/Producción", ProfileFolders.Clean("  Clinica /  Producción / "));
        Assert.Equal("Clinica/test", ProfileFolders.Normalize("clinica/test", existing));       // el nivel que existe, con su grafía
        Assert.Equal("Clinica/Producción/Viejas", ProfileFolders.Normalize("CLINICA/producción/Viejas", existing));
        Assert.True(ProfileFolders.IsInside("Clinica/Producción", "Clinica"));
        Assert.True(ProfileFolders.IsInside("clinica", "Clinica"));
        Assert.False(ProfileFolders.IsInside("Clinicaa", "Clinica"));                           // no alcanza con empezar igual
        Assert.Equal(("Producción", "Clinica"), (ProfileFolders.Name("Clinica/Producción"), ProfileFolders.Parent("Clinica/Producción")));
        Assert.Null(ProfileFolders.Parent("Clinica"));
    }
}
