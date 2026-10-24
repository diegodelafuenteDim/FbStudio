using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core;

namespace FbStudio.App.Views;

/// <summary>
/// Ayuda → Novedades (D-81): el registro de cambios (CHANGELOG.md, que va adentro de la app) en el idioma de la
/// interfaz, de la versión más nueva a la más vieja, con la instalada marcada. Lo que todavía no salió aparece arriba
/// como «Próxima versión» solo en una copia compilada después de la última versión.
/// </summary>
public sealed class WhatsNewDialog : Window
{
    public WhatsNewDialog() : this(AppInfo.ChangeLog, AppInfo.VersionNumber, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) { }

    public WhatsNewDialog(IReadOnlyList<ChangeLogVersion> versions, string installed, string language)
    {
        Title = string.Format(Strings.WhatsNewTitle, AppInfo.Name);
        Width = 640;
        Height = 560;
        MinWidth = 420;
        MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var list = new StackPanel { Spacing = 14 };
        foreach (var version in versions.Where(v => v.In(language).Count > 0))
        {
            var heading = version.IsUnreleased
                ? Strings.WhatsNewUnreleased
                : version.Version + (version.Date is { } date ? $" · {date}" : "") + (version.Version == installed ? $"  ({Strings.WhatsNewInstalled})" : "");
            var section = new StackPanel { Spacing = 4 };
            section.Children.Add(new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeight.SemiBold });
            foreach (var change in version.In(language))
                section.Children.Add(new SelectableTextBlock { Text = "•  " + change, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 0, 0) });
            list.Children.Add(section);
            Lines.Add(heading);
            Lines.AddRange(version.In(language));
        }
        if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = Strings.WhatsNewEmpty, Opacity = 0.75 });

        var close = new Button { Content = Strings.Close, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
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

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(close, Avalonia.Controls.Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(full);
        DockPanel.SetDock(bottom, Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children = { bottom, new ScrollViewer { Content = list, Padding = new Thickness(0, 0, 12, 0) } },
        };
    }

    /// <summary>Los títulos y los cambios que muestra (para las pruebas).</summary>
    internal List<string> Lines { get; } = [];
}
