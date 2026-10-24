using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Firebird.Tests;
using FbStudio.Workspace;

namespace FbStudio.App.Tests;

public partial class AppResourcesTests
{
    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static Dictionary<string, string> Read(CultureInfo culture) =>
        Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    private static IEnumerable<int> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order();

    [Fact]
    public void Los_textos_de_la_interfaz_estan_en_espanol_e_ingles_con_los_mismos_parametros()
    {
        var es = Read(CultureInfo.InvariantCulture);
        var en = Read(CultureInfo.GetCultureInfo("en"));
        Assert.True(es.Count > 300, $"solo {es.Count} textos");
        Assert.Empty(es.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(es.Keys));
        foreach (var (key, text) in es)
        {
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"{key}: traducción vacía");
            Assert.True(Placeholders(text).SequenceEqual(Placeholders(en[key])), $"{key}: los {{n}} no coinciden entre «{text}» y «{en[key]}»");
        }
    }

    [Fact]
    public void Las_teclas_de_acceso_de_los_menus_no_se_repiten_dentro_de_cada_menu_principal()
    {
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en") })
        {
            var texts = Read(culture);
            string Key(string k) => texts[k] is var t && t.IndexOf('_') is var i and >= 0 ? char.ToUpperInvariant(t[i + 1]).ToString() : "";
            string[][] menus =
            [
                ["MenuDatabase", "MenuQuery", "MenuView", "MenuTools", "MenuHelp"],
                ["MenuRegister", "MenuEditRegistrations", "MenuConnect", "MenuDisconnect", "MenuExit"],
                ["MenuNewTab", "MenuCloseTab", "MenuSearchMetadata", "MenuRun", "MenuRunStatement", "MenuCancel", "MenuCommit", "MenuRollback",
                 "MenuExportCsv", "MenuExportXlsx", "MenuExportInsert"],
                ["MenuViewResults", "MenuViewPlan", "MenuViewStats", "MenuViewHistory", "MenuViewMessages"],
            ];
            foreach (var menu in menus)
            {
                var keys = menu.Select(Key).Where(k => k.Length > 0).ToList();
                Assert.True(keys.Count == keys.Distinct().Count(), $"{culture.Name}: tecla repetida en {string.Join(", ", menu.Select(m => texts[m]))}");
            }
        }
    }
}

/// <summary>La ventana armada en inglés (Opciones → Idioma: English, al volver a abrir).</summary>
[Collection(nameof(UiCollection))]
public sealed class LanguageTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-lang-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public Task Con_ingles_la_ventana_y_los_mensajes_salen_en_ingles() => ui.Session.Dispatch(() =>
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            UiLanguage.Apply("en");
            var store = AppServices.CreateProfileStore(_folder);
            store.Upsert(fb.Profile, fb.Password);
            var vm = new MainWindowViewModel(store, AppServices.CreateHistoryStore(_folder), AppServices.CreateSettingsStore(_folder));
            var w = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
            w.Show();
            Assert.Equal("_Options…", w.FindControl<MenuItem>("MenuOptions")!.Header);
            Assert.Equal("Double-click a database in the tree to connect", vm.Banner);   // sin pestañas al abrir
            Assert.True(Avalonia.Application.Current!.TryGetResource("STRING_MENU_PASTE", null, out var paste));
            Assert.Equal("Paste", paste);                                                 // el menú de los cuadros de texto
            vm.NewQueryTab(vm.Profiles.Single());
            Assert.Equal("Query 1", vm.SelectedTab!.BaseTitle);
            Assert.StartsWith("Ready.", vm.SelectedTab.Status);
            Assert.Equal("Development", Assert.Single(vm.Tree.Rows, n => n.Kind == TreeNodeKind.Folder).Text);
            Assert.StartsWith("Development · ", vm.Banner);                  // la pestaña arranca con la única base
            w.Close();
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("es");
            CultureInfo.CurrentUICulture = original;
            UiLanguage.ApplyThemeLocale();
        }
    }, CancellationToken.None);

    [Fact]
    public void La_opcion_de_idioma_se_guarda_y_avisa_que_vale_al_reabrir()
    {
        var opts = new OptionsViewModel(new AppSettings());
        Assert.Equal(0, opts.LanguageIndex);                       // igual que Windows
        Assert.Equal(["Igual que Windows", "Español", "English"], opts.Languages);
        opts.LanguageIndex = 2;
        Assert.Equal("en", opts.ToSettings().Language);
        Assert.Equal(2, new OptionsViewModel(new AppSettings { Language = "en" }).LanguageIndex);
        Assert.Equal("", new AppSettings { Language = "fr" }.Normalized().Language); // un idioma que no hay vuelve a «igual que Windows»

        // El aviso de reinicio va en el idioma elegido, no en el que está usando FbStudio.
        var toEnglish = ConfirmPrompt.Restart("en", openTabs: 2);
        Assert.Equal(("Restart now", "Later", "Language change"), (toEnglish.RunText, toEnglish.CancelText, toEnglish.Title));
        Assert.Equal("The 2 open tab(s) will be closed (if any has an open transaction, you will be asked first).", toEnglish.Reasons[1]);
        var toSpanish = ConfirmPrompt.Restart("es", openTabs: 0);
        Assert.Equal(("Reiniciar ahora", "Más tarde"), (toSpanish.RunText, toSpanish.CancelText));
        Assert.Single(toSpanish.Reasons);                                     // sin pestañas no habla de cerrarlas
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Igual_que_el_sistema_es_espanol_si_el_sistema_esta_en_espanol_y_si_no_ingles()
    {
        Assert.Equal("es-AR", UiLanguage.SystemCulture(CultureInfo.GetCultureInfo("es-AR")).Name);
        Assert.Equal("es-ES", UiLanguage.SystemCulture(CultureInfo.GetCultureInfo("es-ES")).Name);
        Assert.Equal("en", UiLanguage.SystemCulture(CultureInfo.GetCultureInfo("de-DE")).Name);
        Assert.Equal("en", UiLanguage.SystemCulture(CultureInfo.GetCultureInfo("pt-BR")).Name);
        Assert.Equal("en", UiLanguage.SystemCulture(CultureInfo.InvariantCulture).Name);   // un Linux sin LANG
        Assert.Equal("es", UiLanguage.CultureFor("es").Name);
        Assert.Equal("en", UiLanguage.CultureFor("en").Name);
    }
}
