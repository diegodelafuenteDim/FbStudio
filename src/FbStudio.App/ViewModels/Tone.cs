using FbStudio.Core.Profiles;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Color con significado (no un color fijo): la vista lo traduce a un pincel del tema claro u oscuro
/// (FbsMutedBrush, FbsSuccessBrush… en App.axaml), así cambia al vuelo con el tema.
/// </summary>
public enum Tone
{
    Muted,
    Success,
    Warning,
    Danger,

    // Los entornos tienen sus propios pinceles (FbsEnv…Brush): el color se puede cambiar en Opciones (F-20).
    Development,
    Test,
    Production,
}

public static class Tones
{
    /// <summary>
    /// El color de cada entorno (de fábrica verde desarrollo, naranja prueba, rojo producción): la misma convención en el
    /// árbol, las pestañas y la franja.
    /// </summary>
    public static Tone ForEnvironment(ProfileEnvironment? environment) => environment switch
    {
        ProfileEnvironment.Produccion => Tone.Production,
        ProfileEnvironment.Prueba => Tone.Test,
        ProfileEnvironment.Desarrollo => Tone.Development,
        _ => Tone.Muted,
    };
}
