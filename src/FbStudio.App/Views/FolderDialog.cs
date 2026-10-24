using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Profiles;

namespace FbStudio.App.Views;

/// <summary>Lo elegido en «Nueva carpeta…»: el nombre y las bases que van adentro.</summary>
public sealed record FolderChoice(string Name, IReadOnlyList<Guid> ProfileIds);

/// <summary>
/// «Nueva carpeta…»: nombre y qué bases van adentro, en un solo paso (una carpeta existe mientras tenga alguna base).
/// Devuelve null si se cancela.
/// </summary>
public sealed class FolderDialog : Window
{
    public FolderDialog(IReadOnlyList<ConnectionProfile> profiles, IEnumerable<Guid> preselected)
    {
        Title = Strings.FolderNewTitle;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var chosen = preselected.ToHashSet();
        var name = new TextBox { Name = "FolderName", PlaceholderText = Strings.FolderNamePlaceholder };
        var checks = profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(p => new CheckBox
        {
            Content = $"{p.Name}  ·  {(string.IsNullOrWhiteSpace(p.Folder) ? ProfileEnvironments.Label(p.Environment) : p.Folder.Trim())}",
            IsChecked = chosen.Contains(p.Id),
            Tag = p.Id,
            Margin = new Thickness(0, 2),
        }).ToList();
        var list = new StackPanel { Name = "FolderDatabases" };
        foreach (var c in checks) list.Children.Add(c);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        var ok = new Button { Content = Strings.FolderCreate, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
        Opened += (_, _) => name.Focus();

        void Accept()
        {
            var ids = checks.Where(c => c.IsChecked == true).Select(c => (Guid)c.Tag!).ToList();
            var problem = string.IsNullOrWhiteSpace(name.Text) ? Strings.FolderNeedsName : ids.Count == 0 ? Strings.FolderNeedsDatabase : null;
            if (problem is not null)
            {
                error.Text = problem;
                error.IsVisible = true;
                return;
            }
            Close(new FolderChoice(name.Text!.Trim(), ids));
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = Strings.FolderNameLabel },
                name,
                new TextBlock { Text = Strings.FolderDatabasesLabel, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap },
                new ScrollViewer { MaxHeight = 320, Content = list, Padding = new Thickness(4, 0) },
                error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };
    }
}
