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
        Assert.StartsWith("Versión 0.1.0", dialog.VersionText.Text);
        Assert.Equal(AppInfo.Version, dialog.VersionText.Text!["Versión ".Length..]);
        var urls = dialog.Links.Select(l => l.Url).ToList();
        Assert.Contains(AppInfo.RepositoryUrl, urls);
        Assert.Contains(AppInfo.IssuesUrl, urls);
        Assert.DoesNotContain(urls, u => u.Contains("sponsors") || u.Contains("cafecito"));    // sin donaciones por ahora
        Assert.Contains(AppInfo.LicenseUrl, urls);
        Assert.All(urls, u => Assert.StartsWith("https://", u));
        var texts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t?.Contains("no vender") == true);
        Assert.Contains(texts, t => t?.Contains("no está afiliado a la Firebird Foundation") == true);
        dialog.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);
}
