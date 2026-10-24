using FbStudio.Core.Profiles;

namespace FbStudio.Core.Tests;

/// <summary>Los colores que se pueden elegir para los entornos y las carpetas (F-20).</summary>
public class ColorPaletteTests
{
    [Fact]
    public void Todos_se_leen_con_letra_blanca() =>
        Assert.All(ColorPalette.Production.Concat(ColorPalette.Others),
            c => Assert.True(ColorPalette.ContrastWithWhite(c.Hex) >= 4.2, $"{c.Key} {c.Hex}: {ColorPalette.ContrastWithWhite(c.Hex):0.00}"));

    [Fact]
    public void Los_rojos_son_solo_de_produccion()
    {
        Assert.All(ColorPalette.Production, c => Assert.True(ColorPalette.IsRed(c.Hex), c.Key));
        Assert.All(ColorPalette.Others, c => Assert.False(ColorPalette.IsRed(c.Hex), c.Key));    // el marrón, poco saturado, no cuenta
        Assert.False(ColorPalette.IsAllowed(ProfileEnvironment.Desarrollo, "#C62828"));
        Assert.False(ColorPalette.IsAllowed(null, "#C62828"));                                   // una carpeta tampoco
        Assert.True(ColorPalette.IsAllowed(ProfileEnvironment.Produccion, " #b71c1c "));
        Assert.False(ColorPalette.IsAllowed(ProfileEnvironment.Produccion, "#1565C0"));          // producción sigue siendo roja
        Assert.False(ColorPalette.IsAllowed(ProfileEnvironment.Prueba, "#123456"));              // fuera de la paleta
        Assert.False(ColorPalette.IsAllowed(ProfileEnvironment.Prueba, null));
    }

    [Fact]
    public void Los_de_siempre_estan_en_la_paleta() =>
        Assert.All(Enum.GetValues<ProfileEnvironment>(), e => Assert.True(ColorPalette.IsAllowed(e, ColorPalette.Default(e)), e.ToString()));
}
