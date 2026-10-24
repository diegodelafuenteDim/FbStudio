using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Metadata;
using FbStudio.Firebird;
using FbStudio.Firebird.Tests;

namespace FbStudio.App.Tests;

/// <summary>«Datos del servidor…» y el tipo de servidor en el monitoreo (F-16).</summary>
[Collection(nameof(UiCollection))]
public sealed class ServerDialogTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-srv-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string?> Texts(Control root) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<TextBlock>().Select(t => t.Text).ToList();

    [Fact]
    public Task Muestra_tipo_sistema_y_cache_del_servidor_de_Docker() => ui.Session.DispatchAsync(async () =>
    {
        var dialog = new ServerDialog("docker-fb3", $"{fb.Profile.Host}:{fb.Profile.Port}", ServerProbe.DetailsAsync(fb.Profile, fb.Password));
        dialog.Show();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (dialog.Table is null && sw.ElapsedMilliseconds < 15_000)
        {
            await Task.Delay(20);
            Pump();
        }

        Assert.Equal("Datos del servidor · docker-fb3", dialog.Title);
        Assert.NotNull(dialog.Table);
        var texts = Texts(dialog);
        Assert.Contains("SuperServer", texts);
        Assert.Contains(texts, t => t?.StartsWith("Linux") == true);
        Assert.Contains(texts, t => t?.Contains("compartida por todas las conexiones") == true);
        Assert.DoesNotContain(texts, t => t?.Contains("/firebird") == true);   // sin la ruta del archivo
        dialog.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Si_no_puede_leer_lo_dice() => ui.Session.DispatchAsync(() =>
    {
        var dialog = new ServerDialog("x", "x:3050", Task.FromException<ServerDetails>(new InvalidOperationException("sin red\ndetalle")));
        dialog.Show();
        Pump();
        Assert.Contains("No se pudieron leer los datos del servidor: sin red", Texts(dialog));
        dialog.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task En_Classic_avisa_si_el_refresco_es_corto() => ui.Session.DispatchAsync(async () =>
    {
        var vm = new MainWindowViewModel(AppServices.CreateProfileStore(_folder), null, AppServices.CreateSettingsStore(_folder),
            AppServices.CreateMonitorQueryStore(_folder));
        await using var tab = new MonitorTabViewModel(vm, null);

        Assert.Equal("—", tab.ServerModeText);
        tab.ModeEvidence = new ServerModeEvidence(145, 30, 30, false, true);
        Assert.Equal("Classic", tab.ServerModeText);
        Assert.Contains("10 s", tab.ServerModeTip);
        Assert.Equal(5, tab.Interval);
        Assert.True(tab.HasClassicNote);
        Assert.Contains("cada 5 s", tab.ClassicNote);
        tab.Interval = 10;
        Assert.False(tab.HasClassicNote);
        tab.Interval = 0;                                                       // a mano: no carga nada
        Assert.False(tab.HasClassicNote);

        tab.ModeEvidence = new ServerModeEvidence(33, 30, 1, true, true);
        tab.Interval = 2;
        Assert.Equal("SuperServer", tab.ServerModeText);
        Assert.False(tab.HasClassicNote);
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
