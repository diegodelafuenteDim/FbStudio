using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.Views;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>Ayuda → Acerca de FbStudio.</summary>
[Collection(nameof(UiCollection))]
public sealed class AboutDialogTests(FirebirdFixture fb, HeadlessFixture ui)
{
    [Fact]
    public Task Muestra_version_licencia_y_los_enlaces() => ui.Session.DispatchAsync(() =>
    {
        _ = fb;
        var dialog = new AboutDialog();
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        Assert.Equal("Acerca de FbStudio", dialog.Title);
        Assert.StartsWith($"Versión {AppInfo.VersionNumber}", dialog.VersionText.Text);   // la de Directory.Build.props
        Assert.Equal(AppInfo.Version, dialog.VersionText.Text!["Versión ".Length..]);
        var urls = dialog.Links.Select(l => l.Url).ToList();
        Assert.Contains(AppInfo.RepositoryUrl, urls);
        Assert.Contains(AppInfo.BugReportUrl, urls);                            // cada formulario por separado
        Assert.Contains(AppInfo.FeatureRequestUrl, urls);
        Assert.Contains(AppInfo.DiscussionsUrl, urls);
        Assert.DoesNotContain(urls, u => u.Contains("sponsors") || u.Contains("cafecito"));    // sin donaciones por ahora
        Assert.Contains(AppInfo.LicenseUrl, urls);
        Assert.All(urls, u => Assert.StartsWith("https://", u));
        Assert.Equal(["Ver la licencia completa", "Código fuente en GitHub", "Novedades de esta versión", "Copiar versión y sistema",
            "Reportar un error", "Pedir una función", "Preguntas e ideas"], dialog.RowTexts.Take(7));
        Assert.StartsWith($"FbStudio {AppInfo.Version} · ", AppInfo.ReportInfo);
        var texts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t?.Contains("no vender") == true);
        Assert.Contains(texts, t => t?.Contains("no está afiliado a la Firebird Foundation") == true);
        dialog.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);
}
