using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
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
            ApplyEnvironmentColors(settings.Current);
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
            // Solo en la app de verdad (las pruebas arman la ventana sin esto): las novedades de una versión nueva, una vez.
            if (desktop.MainWindow is MainWindow main)
            {
                async void OnOpened(object? sender, EventArgs e)
                {
                    main.Opened -= OnOpened;
                    await main.ShowWhatsNewIfUpdatedAsync();
                }
                main.Opened += OnOpened;
            }
        }
        UiLanguage.ApplyThemeLocale(); // también sin ventana principal (pruebas)
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Los colores de los entornos (F-20): el elegido en Opciones o el de siempre, en la franja (el mismo en los dos temas,
    /// con letra blanca) y en el árbol y las pestañas (en el tema oscuro, más claro para que se vea sobre el fondo).
    /// </summary>
    public static void ApplyEnvironmentColors(AppSettings settings)
    {
        if (Current is null) return;
        foreach (var environment in Enum.GetValues<ProfileEnvironment>())
        {
            var name = environment switch { ProfileEnvironment.Produccion => "Prod", ProfileEnvironment.Prueba => "Test", _ => "Dev" };
            var custom = settings.EnvironmentColors?.GetValueOrDefault(environment);
            var color = Color.Parse(custom ?? ColorPalette.Default(environment));
            Current.Resources[$"FbsBanner{name}Brush"] = new SolidColorBrush(color);
            var dark = custom is null ? Color.Parse(environment switch
            {
                ProfileEnvironment.Produccion => "#EF5350",
                ProfileEnvironment.Prueba => "#FFB74D",
                _ => "#66BB6A",
            }) : Lighter(color);
            if (Current.Resources.ThemeDictionaries.TryGetValue(ThemeVariant.Light, out var light) && light is IResourceDictionary l)
                l[$"FbsEnv{name}Brush"] = new SolidColorBrush(color);
            if (Current.Resources.ThemeDictionaries.TryGetValue(ThemeVariant.Dark, out var darkTheme) && darkTheme is IResourceDictionary d)
                d[$"FbsEnv{name}Brush"] = new SolidColorBrush(dark);
        }
    }

    /// <summary>El color mezclado con un 40 % de blanco.</summary>
    private static Color Lighter(Color c) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * 0.4), (byte)(c.G + (255 - c.G) * 0.4), (byte)(c.B + (255 - c.B) * 0.4));

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
