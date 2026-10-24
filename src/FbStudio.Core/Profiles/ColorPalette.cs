using System.Globalization;

namespace FbStudio.Core.Profiles;

/// <summary>Un color de la paleta: la clave de su nombre (para mostrarlo traducido) y el valor «#RRGGBB».</summary>
public sealed record PaletteColor(string Key, string Hex);

/// <summary>
/// Los colores que se pueden elegir para los entornos y las carpetas (F-20). Todos se leen con letra blanca encima (la
/// franja de la base) y los rojos quedan solo para producción: nada se puede confundir con ella.
/// </summary>
public static class ColorPalette
{
    public static IReadOnlyList<PaletteColor> Production { get; } =
    [
        new("Red", "#C62828"), new("DarkRed", "#B71C1C"), new("BrightRed", "#D32F2F"), new("Wine", "#8E2424"), new("Crimson", "#A0153E"),
    ];

    public static IReadOnlyList<PaletteColor> Others { get; } =
    [
        new("Green", "#2E7D32"), new("DarkGreen", "#1B5E20"), new("Teal", "#00796B"), new("Cyan", "#00838F"), new("Blue", "#1565C0"),
        new("Indigo", "#283593"), new("Purple", "#6A1B9A"), new("Orange", "#B26A00"), new("Olive", "#827717"), new("Brown", "#5D4037"),
        new("Slate", "#455A64"),
    ];

    /// <summary>El color de siempre de cada entorno: verde desarrollo, naranja prueba, rojo producción.</summary>
    public static string Default(ProfileEnvironment environment) => environment switch
    {
        ProfileEnvironment.Produccion => "#C62828",
        ProfileEnvironment.Prueba => "#B26A00",
        _ => "#2E7D32",
    };

    /// <summary>Los que se pueden elegir para un entorno (null: para una carpeta, que no puede ser roja).</summary>
    public static IReadOnlyList<PaletteColor> For(ProfileEnvironment? environment) =>
        environment == ProfileEnvironment.Produccion ? Production : Others;

    /// <summary>Si el color está en la paleta de ese entorno (o de las carpetas): uno escrito a mano, o un rojo fuera de producción, no vale.</summary>
    public static bool IsAllowed(ProfileEnvironment? environment, string? hex) =>
        hex is not null && For(environment).Any(c => string.Equals(c.Hex, hex.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Contraste de la letra blanca sobre el color (WCAG: de 4,5 para arriba se lee bien en texto común).</summary>
    public static double ContrastWithWhite(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Linear(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        var luminance = 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
        return 1.05 / (luminance + 0.05);
    }

    /// <summary>Un rojo o un rosado fuerte: el tono de producción (los marrones, poco saturados, no cuentan).</summary>
    public static bool IsRed(string hex)
    {
        var (r, g, b) = Rgb(hex);
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        if (delta == 0) return false;
        var lightness = (max + min) / 2;
        var saturation = delta / (1 - Math.Abs(2 * lightness - 1));
        var hue = max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return saturation >= 0.35 && (hue <= 20 || hue >= 330);
    }

    private static (double R, double G, double B) Rgb(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Color inválido: {hex}");
        return (((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
    }
}
