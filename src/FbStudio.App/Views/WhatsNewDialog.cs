using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FbStudio.App.Resources;
using FbStudio.Core;
using Material.Icons;
using Material.Icons.Avalonia;

namespace FbStudio.App.Views;

/// <summary>
/// Ayuda → Novedades (D-81): el registro de cambios (CHANGELOG.md, que va adentro de la app) en el idioma de la
/// interfaz. A la izquierda las versiones (la más nueva arriba, la instalada marcada); a la derecha los cambios de la
/// elegida, cada uno en su tarjeta con el título resaltado, los nombres entre «…» en negrita y las correcciones con su
/// propio ícono. Lo que todavía no salió aparece arriba como «Próxima versión» solo en una copia compilada después de la
/// última versión.
/// </summary>
public sealed class WhatsNewDialog : Window
{
    private readonly List<(ChangeLogVersion Version, string Heading)> _versions = [];
    private readonly string _language;
    private readonly string _installed;
    private readonly ListBox _versionList = new() { Width = 190, Padding = new Thickness(0, 4) };
    private readonly StackPanel _cards = new() { Spacing = 8 };
    private readonly TextBlock _versionTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _versionBadges = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _scroll = new() { Padding = new Thickness(0, 0, 14, 0) };

    public WhatsNewDialog() : this(AppInfo.ChangeLog, AppInfo.VersionNumber, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) { }

    public WhatsNewDialog(IReadOnlyList<ChangeLogVersion> versions, string installed, string language)
    {
        _language = language;
        _installed = installed;
        Title = string.Format(Strings.WhatsNewTitle, AppInfo.Name);
        Width = 920;
        Height = 640;
        MinWidth = 560;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var version in versions.Where(v => v.In(language).Count > 0))
        {
            var heading = version.IsUnreleased
                ? Strings.WhatsNewUnreleased
                : version.Version + (version.Date is { } date ? $" · {date}" : "") + (version.Version == installed ? $"  ({Strings.WhatsNewInstalled})" : "");
            _versions.Add((version, heading));
            Lines.Add(heading);
            Lines.AddRange(version.In(language));
        }

        // Encabezado: el logo, el título y la versión instalada.
        var logo = new Image { Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0) };
        try { logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://FbStudio/Assets/fbstudio.png"))); }
        catch (Exception) { logo.IsVisible = false; } // sin el ícono la ventana sirve igual
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(logo, Avalonia.Controls.Dock.Left);
        header.Children.Add(logo);
        header.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeight.SemiBold },
                Muted(string.Format(Strings.WhatsNewInstalledVersion, installed), 13),
            },
        });

        var close = new Button { Content = Strings.Close, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(18, 5) };
        close.Click += (_, _) => Close();
        var full = new Button
        {
            Content = Strings.WhatsNewOnGitHub, Padding = new Thickness(0, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        full.Classes.Add("link");
        ToolTip.SetTip(full, AppInfo.ChangeLogUrl);
        full.Click += async (_, _) =>
        {
            if (GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(AppInfo.ChangeLogUrl));
        };
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(close, Avalonia.Controls.Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(full);

        Control body;
        if (_versions.Count == 0) body = Muted(Strings.WhatsNewEmpty, 14);
        else
        {
            // Izquierda: las versiones. Derecha: la elegida, con su título, sus marcas y sus cambios.
            _versionList.ItemsSource = _versions.Select(v => VersionItem(v.Version)).ToList();
            _versionList.SelectionChanged += (_, _) => ShowVersion(_versionList.SelectedIndex);
            var left = Card(_versionList, new Thickness(0));
            left.Margin = new Thickness(0, 0, 16, 0);
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 10), Children = { _versionTitle, _versionBadges } };
            _scroll.Content = _cards;
            var right = new DockPanel();
            DockPanel.SetDock(titleRow, Avalonia.Controls.Dock.Top);
            right.Children.Add(titleRow);
            right.Children.Add(_scroll);
            var split = new DockPanel();
            DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);
            split.Children.Add(left);
            split.Children.Add(right);
            body = split;
            _versionList.SelectedIndex = 0;
        }

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(bottom);
        root.Children.Add(body);
        Content = root;
    }

    /// <summary>Los títulos y los cambios de todas las versiones (para las pruebas).</summary>
    internal List<string> Lines { get; } = [];

    /// <summary>La versión que se está mostrando y sus tarjetas (para las pruebas).</summary>
    internal string ShownVersion => _versionTitle.Text ?? "";
    internal int ShownCards => _cards.Children.Count;

    internal void ShowVersion(int index)
    {
        if (index < 0 || index >= _versions.Count) return;
        var version = _versions[index].Version;
        var changes = version.In(_language).Select(ChangeLog.Split).ToList();
        _versionTitle.Text = version.IsUnreleased ? Strings.WhatsNewNext : version.Version;
        _versionBadges.Children.Clear();
        if (version.IsUnreleased) _versionBadges.Children.Add(Badge(Strings.WhatsNewInDevelopment, accent: true));
        if (version.Version == _installed) _versionBadges.Children.Add(Badge(Strings.WhatsNewInstalled, accent: true));
        if (version.Date is { } date) _versionBadges.Children.Add(Badge(date, accent: false));
        var fixes = changes.Count(c => c.IsFix);
        _versionBadges.Children.Add(Badge(fixes switch
        {
            0 => string.Format(Strings.WhatsNewCount, changes.Count),
            1 => string.Format(Strings.WhatsNewCountWithOneFix, changes.Count - 1),
            _ => string.Format(Strings.WhatsNewCountWithFixes, changes.Count - fixes, fixes),
        }, accent: false));
        _cards.Children.Clear();
        // Primero las novedades, después las correcciones.
        foreach (var change in changes.Where(c => !c.IsFix).Concat(changes.Where(c => c.IsFix))) _cards.Children.Add(ChangeCard(change));
        _scroll.Offset = default;
    }

    private Control VersionItem(ChangeLogVersion version)
    {
        var name = new TextBlock { Text = version.IsUnreleased ? Strings.WhatsNewNext : version.Version, FontWeight = FontWeight.SemiBold };
        var detail = version.IsUnreleased
            ? Strings.WhatsNewInDevelopment
            : (version.Date ?? "") + (version.Version == _installed ? " · " + Strings.WhatsNewInstalled : "");
        return new StackPanel { Margin = new Thickness(6, 4), Children = { name, Muted(detail, 12) } };
    }

    /// <summary>Una tarjeta: el ícono (novedad o corrección), el título resaltado y el cuerpo, con los nombres entre «…» en negrita.</summary>
    private static Control ChangeCard(ChangeLogEntry change)
    {
        var icon = new MaterialIcon
        {
            Kind = change.IsFix ? MaterialIconKind.WrenchOutline : MaterialIconKind.StarFourPointsOutline,
            Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0),
            [!MaterialIcon.ForegroundProperty] = new DynamicResourceExtension(change.IsFix ? "SemiColorWarning" : "SemiColorPrimary"),
        };
        var text = new StackPanel { Spacing = 3 };
        if (change.IsFix || change.Title is not null)
        {
            var title = new SelectableTextBlock { FontWeight = FontWeight.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
            title.Inlines = Rich(change.Title ?? Strings.WhatsNewFix, boldQuotes: false);
            text.Children.Add(title);
        }
        var body = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        body.Inlines = Rich(change.Body, boldQuotes: true);
        if (change.Title is not null || change.IsFix) body[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SemiColorText1");
        text.Children.Add(body);
        var row = new DockPanel();
        DockPanel.SetDock(icon, Avalonia.Controls.Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        return Card(row, new Thickness(14, 10));
    }

    /// <summary>El texto con los nombres entre «…» en negrita (lo que se ve en la app: menús, botones, opciones).</summary>
    private static InlineCollection Rich(string text, bool boldQuotes)
    {
        var inlines = new InlineCollection();
        var i = 0;
        while (i < text.Length)
        {
            var open = boldQuotes ? text.IndexOf('«', i) : -1;
            var close = open >= 0 ? text.IndexOf('»', open) : -1;
            if (open < 0 || close < 0)
            {
                inlines.Add(new Run(text[i..]));
                break;
            }
            if (open > i) inlines.Add(new Run(text[i..open]));
            inlines.Add(new Run(text[open..(close + 1)]) { FontWeight = FontWeight.SemiBold });
            i = close + 1;
        }
        return inlines;
    }

    private static Border Card(Control child, Thickness padding) => new()
    {
        Child = child,
        Padding = padding,
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        [!Border.BackgroundProperty] = new DynamicResourceExtension("SemiColorFill0"),
        [!Border.BorderBrushProperty] = new DynamicResourceExtension("SemiColorBorder"),
    };

    private static Border Badge(string text, bool accent) => new()
    {
        Padding = new Thickness(8, 2),
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
        [!Border.BorderBrushProperty] = new DynamicResourceExtension(accent ? "SemiColorPrimary" : "SemiColorBorder"),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension(accent ? "SemiColorPrimary" : "SemiColorText2"),
        },
    };

    private static TextBlock Muted(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SemiColorText2"),
    };
}
