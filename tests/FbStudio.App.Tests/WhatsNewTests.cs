using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.Views;
using FbStudio.Core;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>Ayuda → Novedades y el registro de cambios que va adentro de la app (D-81).</summary>
[Collection(nameof(UiCollection))]
public sealed class WhatsNewTests(FirebirdFixture fb, HeadlessFixture ui)
{
    [Fact]
    public void El_registro_de_cambios_va_adentro_y_cada_version_esta_en_los_dos_idiomas()
    {
        _ = fb;
        var versions = AppInfo.ChangeLog;
        Assert.Contains(versions, v => v.Version == "0.1.0");
        // La versión de esta compilación tiene su sección (o hay cambios sin publicar todavía): una Release no sale sin ella.
        Assert.True(versions.Any(v => v.Version == AppInfo.VersionNumber) || versions.Any(v => v.IsUnreleased && v.English.Count > 0),
            $"CHANGELOG.md no tiene la sección de {AppInfo.VersionNumber}");
        foreach (var v in versions.Where(v => !v.IsUnreleased || v.English.Count + v.Spanish.Count > 0))
            Assert.True(v.English.Count == v.Spanish.Count && v.English.Count > 0, $"{v.Version}: {v.English.Count} en inglés y {v.Spanish.Count} en español");
    }

    [Fact]
    public Task Se_abre_sola_una_vez_al_actualizar_y_no_en_una_instalacion_nueva() => ui.Session.DispatchAsync(() =>
    {
        var changeLog = ChangeLog.Parse("## [0.2.0] - 2026-11-01\n\n### English\n\n- New.\n\n### Español\n\n- Nuevo.\n");
        var folder = Path.Combine(Path.GetTempPath(), "fbstudio-novedades-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Instalación nueva: nada anotado y ninguna base. No se muestra, pero queda anotada la versión.
            var fresh = new ViewModels.MainWindowViewModel(AppServices.CreateProfileStore(Path.Combine(folder, "nueva")),
                settings: AppServices.CreateSettingsStore(Path.Combine(folder, "nueva")));
            Assert.False(fresh.ShouldShowWhatsNew("0.2.0", changeLog));
            Assert.Equal("0.2.0", fresh.Options.LastSeenVersion);

            // Quien venía de la 0.1.0 (sin nada anotado, pero con bases registradas): se muestra una vez.
            var store = AppServices.CreateProfileStore(Path.Combine(folder, "vieja"));
            store.Upsert(fb.Profile, fb.Password);
            var settings = AppServices.CreateSettingsStore(Path.Combine(folder, "vieja"));
            var upgraded = new ViewModels.MainWindowViewModel(store, settings: settings);
            Assert.True(upgraded.ShouldShowWhatsNew("0.2.0", changeLog));
            Assert.False(upgraded.ShouldShowWhatsNew("0.2.0", changeLog));          // ya se mostró
            Assert.Equal("0.2.0", AppServices.CreateSettingsStore(Path.Combine(folder, "vieja")).Current.LastSeenVersion);
            Assert.False(upgraded.ShouldShowWhatsNew("0.2.1", changeLog));          // una versión sin su sección: nada que mostrar
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task Muestra_la_version_instalada_en_el_idioma_de_la_interfaz() => ui.Session.DispatchAsync(() =>
    {
        var versions = ChangeLog.Parse("""
            ## [Unreleased]

            ### English

            - Something new.

            ### Español

            - Algo nuevo.

            ## [0.1.0] - 2026-10-24

            ### English

            - First public version.

            ### Español

            - Primera versión pública.
            """);
        var dialog = new WhatsNewDialog(versions, "0.1.0", "es");
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.Equal("Novedades de FbStudio", dialog.Title);
        Assert.Equal(["Próxima versión (en desarrollo)", "Algo nuevo.", "0.1.0 · 2026-10-24  (instalada)", "Primera versión pública."], dialog.Lines);
        dialog.Close();

        var english = new WhatsNewDialog(versions, "0.1.0", "en");
        Assert.Contains("First public version.", english.Lines);
        var empty = new WhatsNewDialog([], "0.1.0", "es");
        empty.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(empty.Lines);
        Assert.Contains(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(empty).OfType<Avalonia.Controls.TextBlock>(),
            t => t.Text == "No se encontró el registro de cambios.");
        empty.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);
}
