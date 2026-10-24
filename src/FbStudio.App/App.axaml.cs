using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Workspace;

namespace FbStudio.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Un error no previsto (un archivo raro, un caso que no se probó) no cierra FbStudio: se avisa en la barra
            // de estado y queda en errors.log, junto a los perfiles, para mandarlo.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write(e.Exception);
                (desktop.MainWindow?.DataContext as ViewModels.MainWindowViewModel)?.Notify(string.Format(FbStudio.App.Resources.Strings.UnexpectedError, e.Exception.Message));
                e.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorLog.Write(e.Exception);
                e.SetObserved();
            };
            var settings = AppServices.CreateSettingsStore();
            UiLanguage.Apply(settings.Current.Language); // antes de crear ventanas: los textos se leen al armarlas
            ApplyTheme(settings.Current.Theme);
            desktop.MainWindow = new MainWindow
            {
                // Abre maximizada (pedido de Diego); al restaurarla queda en el tamaño del XAML, centrada.
                WindowState = WindowState.Maximized,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                DataContext = new MainWindowViewModel(
                    AppServices.CreateProfileStore(),
                    AppServices.CreateHistoryStore(retentionDays: settings.Current.HistoryRetentionDays),
                    settings,
                    AppServices.CreateMonitorQueryStore(),
                    AppServices.CreateGridLayoutStore(),
                    AppServices.CreateAreaStore()),
            };
        }
        UiLanguage.ApplyThemeLocale(); // también sin ventana principal (pruebas)
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Tema claro, oscuro o el de Windows; cambia al vuelo.</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is null) return;
        Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
