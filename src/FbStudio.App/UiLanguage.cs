using System.Globalization;
using Avalonia;
using Semi.Avalonia;

namespace FbStudio.App;

/// <summary>
/// Idioma de la interfaz (F-6). Los textos están en Resources/Strings.resx (español, el idioma neutral) y
/// Strings.en.resx. «Igual que el sistema»: español si el sistema está en español; cualquier otro idioma, inglés (el
/// público de FbStudio no es solo hispanohablante). Solo cambia el idioma de los textos: los números y las fechas
/// siguen el formato regional del sistema. Se aplica al abrir FbStudio.
/// </summary>
public static class UiLanguage
{
    /// <summary>Códigos que acepta la opción del sistema; "" es «igual que Windows».</summary>
    public static IReadOnlyList<string> Codes { get; } = ["", "es", "en"];

    public static void Apply(string? code)
    {
        var culture = CultureFor(code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        ApplyThemeLocale();
    }

    /// <summary>
    /// Cultura de una opción de idioma. "" es la del sistema (no la que FbStudio esté usando ahora): la del sistema si
    /// está en español; si no, inglés.
    /// </summary>
    public static CultureInfo CultureFor(string? code) =>
        string.IsNullOrEmpty(code) || !Codes.Contains(code) ? SystemCulture(CultureInfo.InstalledUICulture) : CultureInfo.GetCultureInfo(code);

    /// <summary>Lo que corresponde a un idioma del sistema: él mismo si es español, inglés si es cualquier otro.</summary>
    public static CultureInfo SystemCulture(CultureInfo installed) =>
        installed.TwoLetterISOLanguageName == "es" ? installed : CultureInfo.GetCultureInfo("en");

    /// <summary>Un texto de la interfaz en otro idioma que el actual (el aviso de reinicio va en el idioma elegido).</summary>
    public static string Text(string key, CultureInfo culture) =>
        FbStudio.App.Resources.Strings.ResourceManager.GetString(key, culture) ?? key;

    /// <summary>Idioma para los textos que trae el tema Semi: español si la interfaz está en español; si no, inglés.</summary>
    public static CultureInfo ThemeCulture =>
        CultureInfo.GetCultureInfo(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es-ES" : "en-US");

    /// <summary>
    /// Textos propios del tema (el menú Cortar / Copiar / Pegar de los cuadros de texto, entre otros). Semi los trae
    /// en es-ES y en-US, pero si no se le dice cuál usar con una cultura que no tiene (es-AR), los muestra en chino.
    /// </summary>
    public static void ApplyThemeLocale()
    {
        if (Application.Current is { } app) SemiTheme.OverrideLocaleResources(app, ThemeCulture);
    }
}
