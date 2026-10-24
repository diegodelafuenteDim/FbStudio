using System.Globalization;
using System.Runtime.CompilerServices;

namespace FbStudio.Workspace.Tests;

/// <summary>Los tests comparan textos en español: la interfaz se fija en español aunque Windows esté en otro idioma.</summary>
internal static class SpanishUi
{
    [ModuleInitializer]
    internal static void Init()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("es");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
        // También los números y las fechas («8.192», «12,5»): así da lo mismo la configuración regional de la máquina (Linux sin LANG, GitHub en inglés).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("es-AR");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
    }
}
