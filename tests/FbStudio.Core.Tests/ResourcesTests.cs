using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using FbStudio.Core.Metadata;
using FbStudio.Core.Resources;

namespace FbStudio.Core.Tests;

public static partial class ResourceCheck
{
    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    /// <summary>Claves → texto, del idioma neutral (español) o de una traducción sola, sin caer en el neutral.</summary>
    public static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                  ?? throw new InvalidOperationException($"No hay recursos para «{culture.Name}».");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    public static IEnumerable<int> Placeholders(string text) => Placeholder().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order();

    /// <summary>Cada texto en español tiene su traducción, no vacía y con los mismos {0}, {1}… (si no, string.Format falla).</summary>
    public static void AssertComplete(ResourceManager manager)
    {
        var es = Read(manager, CultureInfo.InvariantCulture);
        var en = Read(manager, CultureInfo.GetCultureInfo("en"));
        Assert.NotEmpty(es);
        Assert.Empty(es.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(es.Keys));
        foreach (var (key, text) in es)
        {
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"{key}: traducción vacía");
            Assert.True(Placeholders(text).SequenceEqual(Placeholders(en[key])), $"{key}: los {{n}} no coinciden entre «{text}» y «{en[key]}»");
        }
    }
}

public class ResourcesTests
{
    [Fact]
    public void Los_textos_del_nucleo_estan_en_espanol_e_ingles_con_los_mismos_parametros() =>
        ResourceCheck.AssertComplete(CoreStrings.ResourceManager);

    [Fact]
    public void Los_textos_siguen_el_idioma_de_la_interfaz_y_otro_idioma_cae_en_espanol()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Procedures", DbObjectKinds.Label(DbObjectKind.Procedure));
            Assert.Equal("Production", Core.Profiles.ProfileEnvironments.Label(Core.Profiles.ProfileEnvironment.Produccion));

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR"); // sin traducción: español
            Assert.Equal("Procedimientos", DbObjectKinds.Label(DbObjectKind.Procedure));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
        Assert.Equal("Índice", DbObjectKinds.Singular(DbObjectKind.Index));
    }
}
