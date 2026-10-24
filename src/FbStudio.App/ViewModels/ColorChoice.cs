using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Profiles;

namespace FbStudio.App.ViewModels;

/// <summary>Un color de la paleta para elegir (F-20): su nombre traducido y una muestra.</summary>
public sealed record ColorChoice(string Hex, string Name)
{
    public IBrush Brush { get; } = Hex.Length == 0 ? Brushes.Transparent : new SolidColorBrush(Color.Parse(Hex));

    /// <summary>«Sin color» al principio de una lista para elegir.</summary>
    public static ColorChoice None { get; } = new("", Strings.NoColor);

    /// <summary>Los de un entorno, o de las carpetas con null (sin rojos).</summary>
    public static IReadOnlyList<ColorChoice> For(ProfileEnvironment? environment) =>
        ColorPalette.For(environment).Select(c => new ColorChoice(c.Hex, Strings.ResourceManager.GetString("Color" + c.Key, Strings.Culture) ?? c.Key)).ToList();
}
