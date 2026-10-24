using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FbStudio.App.Resources;

namespace FbStudio.App.Views;

/// <summary>
/// Ayuda → Acerca de FbStudio: versión, autor y licencia (en palabras simples), enlaces al repositorio, a los pedidos y a
/// las donaciones, los componentes de terceros y la aclaración de que no hay relación con la Firebird Foundation.
/// </summary>
public sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Title = string.Format(Strings.AboutTitle, AppInfo.Name);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var logo = new Image { Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top };
        try { logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://FbStudio/Assets/fbstudio.png"))); }
        catch (Exception) { logo.IsVisible = false; } // sin el ícono el diálogo sirve igual

        VersionText = new TextBlock { Text = string.Format(Strings.AboutVersion, AppInfo.Version), Opacity = 0.75 };
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = AppInfo.Name, FontSize = 22, FontWeight = FontWeight.SemiBold },
                VersionText,
                new TextBlock { Text = Strings.AboutTagline, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) },
                new TextBlock { Text = $"© {AppInfo.Year} {AppInfo.Author}", Opacity = 0.75, Margin = new Thickness(0, 6, 0, 0) },
            },
        };

        var links = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        links.Children.Add(Link(Strings.AboutRepository, AppInfo.RepositoryUrl));
        links.Children.Add(Link(Strings.AboutReport, AppInfo.IssuesUrl));
        links.Children.Add(Link(Strings.AboutDiscussions, AppInfo.DiscussionsUrl));

        var thirdParty = new StackPanel { Spacing = 2 };
        foreach (var (name, license) in AppInfo.ThirdParty)
            thirdParty.Children.Add(new TextBlock { Text = $"{name} — {license}", FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        thirdParty.Children.Add(Link(Strings.AboutThirdPartyDetail, AppInfo.ThirdPartyUrl));

        var close = new Button { Content = Strings.Close, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new DockPanel { Children = { Dock(logo), header } },
                Section(Strings.AboutLicenseTitle,
                    new TextBlock { Text = Strings.AboutLicense, TextWrapping = TextWrapping.Wrap },
                    Link(Strings.AboutLicenseFull, AppInfo.LicenseUrl)),
                Section(Strings.AboutHelpTitle, new TextBlock { Text = Strings.AboutHelp, TextWrapping = TextWrapping.Wrap }, links),
                new Expander { Header = Strings.AboutThirdParty, Content = thirdParty, HorizontalAlignment = HorizontalAlignment.Stretch },
                new TextBlock { Text = Strings.AboutTrademarks, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap },
                close,
            },
        };
    }

    /// <summary>La línea de la versión (para las pruebas).</summary>
    public TextBlock VersionText { get; }

    /// <summary>Los enlaces que ofrece, con su dirección (para las pruebas).</summary>
    public IReadOnlyList<(string Text, string Url)> Links => _links;

    private readonly List<(string, string)> _links = [];

    private static Control Dock(Control logo)
    {
        DockPanel.SetDock(logo, Avalonia.Controls.Dock.Left);
        logo.Margin = new Thickness(0, 0, 16, 0);
        return logo;
    }

    private static Control Section(string title, params Control[] body)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        foreach (var c in body) panel.Children.Add(c);
        return panel;
    }

    /// <summary>Un enlace que abre el navegador.</summary>
    private Button Link(string text, string url)
    {
        _links.Add((text, url));
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(0, 2, 14, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        button.Classes.Add("link");
        ToolTip.SetTip(button, url);
        button.Click += async (_, _) =>
        {
            if (GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(url));
        };
        return button;
    }
}
