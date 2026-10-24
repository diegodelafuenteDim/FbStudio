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
}

public static class Tones
{
    /// <summary>Verde desarrollo, naranja prueba, rojo producción: la misma convención en el árbol, las pestañas y la franja.</summary>
    public static Tone ForEnvironment(ProfileEnvironment? environment) => environment switch
    {
        ProfileEnvironment.Produccion => Tone.Danger,
        ProfileEnvironment.Prueba => Tone.Warning,
        ProfileEnvironment.Desarrollo => Tone.Success,
        _ => Tone.Muted,
    };
}
