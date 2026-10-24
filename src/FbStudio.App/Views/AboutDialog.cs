using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FbStudio.App.Resources;
using Material.Icons;
using Material.Icons.Avalonia;

namespace FbStudio.App.Views;

/// <summary>
/// Ayuda → Acerca de FbStudio: versión, autor y licencia (en palabras simples), enlaces al repositorio y a los pedidos,
/// los componentes de terceros y la aclaración de que no hay relación con la Firebird Foundation.
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

        // Dos columnas parejas: a la izquierda lo de FbStudio, a la derecha lo para escribir en GitHub.
        var copy = Action(MaterialIconKind.ContentCopy, Strings.AboutCopyInfo, async button =>
        {
            if (GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(AppInfo.ReportInfo);
            Label(button).Text = Strings.AboutCopied;
        });
        var links = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0, 2, 0, 0) };
        Place(links, 0, 0, Link(MaterialIconKind.Github, Strings.AboutRepository, AppInfo.RepositoryUrl));
        Place(links, 1, 0, Action(MaterialIconKind.BullhornOutline, Strings.AboutWhatsNew, _ => new WhatsNewDialog().ShowDialog(this)));
        Place(links, 2, 0, copy);
        Place(links, 0, 1, Link(MaterialIconKind.BugOutline, Strings.AboutReportBug, AppInfo.BugReportUrl));
        Place(links, 1, 1, Link(MaterialIconKind.LightbulbOnOutline, Strings.AboutRequestFeature, AppInfo.FeatureRequestUrl));
        Place(links, 2, 1, Link(MaterialIconKind.ForumOutline, Strings.AboutDiscussions, AppInfo.DiscussionsUrl));

        var thirdParty = new StackPanel { Spacing = 2 };
        foreach (var (name, license) in AppInfo.ThirdParty)
            thirdParty.Children.Add(new TextBlock { Text = $"{name} — {license}", FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        thirdParty.Children.Add(Link(MaterialIconKind.OpenInNew, Strings.AboutThirdPartyDetail, AppInfo.ThirdPartyUrl));

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
                    Link(MaterialIconKind.ScaleBalance, Strings.AboutLicenseFull, AppInfo.LicenseUrl)),
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

    /// <summary>Un enlace que abre el navegador: ícono y texto, alineado a la izquierda.</summary>
    private Button Link(MaterialIconKind icon, string text, string url)
    {
        _links.Add((text, url));
        var button = Row(icon, text);
        ToolTip.SetTip(button, url);
        button.Click += async (_, _) =>
        {
            if (GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(url));
        };
        return button;
    }

    /// <summary>Lo mismo que un enlace, pero hace algo en FbStudio (abrir las novedades, copiar la versión).</summary>
    private Button Action(MaterialIconKind icon, string text, Func<Button, Task> action)
    {
        var button = Row(icon, text);
        button.Click += async (_, _) => await action(button);
        return button;
    }

    private static Button Row(MaterialIconKind icon, string text)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    new MaterialIcon { Kind = icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
                },
            },
            Padding = new Thickness(0, 3, 12, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        button.Classes.Add("link");
        return button;
    }

    private static TextBlock Label(Button button) => ((StackPanel)button.Content!).Children.OfType<TextBlock>().First();

    private static void Place(Grid grid, int row, int column, Control control)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    /// <summary>Los textos de los enlaces y acciones que muestra (para las pruebas).</summary>
    public IReadOnlyList<string> RowTexts =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<Button>().Where(b => b.Classes.Contains("link")).Select(b => Label(b).Text ?? "").ToList();
}
