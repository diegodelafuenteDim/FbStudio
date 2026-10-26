using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Firebird;

namespace FbStudio.App.Views;

/// <summary>
/// «¿Dónde se usa?» de una columna: lo que la usa (vistas, procedimientos, triggers, índices, FK de otras tablas, FK
/// lógicas) y a qué apunta ella. Doble clic abre el objeto.
/// </summary>
public sealed class ColumnUsageWindow : Window
{
    public ColumnUsageWindow(string table, string column, IReadOnlyList<DbDependency> usage, Func<DbDependency, Task> open)
    {
        Title = string.Format(Strings.ColumnUsageTitle, table, column);
        Width = 620;
        Height = 460;
        MinWidth = 360;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Usage = usage;

        Lines = usage.Select(d => $"{(d.Direction == DependencyDirection.UsedBy ? Strings.ColumnUsageUsedBy : Strings.ColumnUsageUses)} · "
                                  + $"{DbObjectKinds.Singular(d.Kind)} {d.Name}{(d.Detail is { } detail ? " — " + detail : "")}").ToList();
        var list = new ListBox { ItemsSource = Lines };
        list.DoubleTapped += async (_, _) => { if (list.SelectedIndex >= 0) await open(usage[list.SelectedIndex]); };
        var summary = new TextBlock
        {
            Text = usage.Count == 0 ? Strings.ColumnUsageNone : string.Format(Strings.ColumnUsageSummary, usage.Count),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var close = new Button { Content = Strings.Close, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        close.Click += (_, _) => Close();
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(summary, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(summary);
        root.Children.Add(close);
        root.Children.Add(new Border { BorderThickness = new Thickness(1), BorderBrush = Avalonia.Media.Brushes.Gray, CornerRadius = new CornerRadius(4), Child = list });
        Content = root;
    }

    public IReadOnlyList<DbDependency> Usage { get; }

    /// <summary>Lo que se muestra, un renglón por objeto.</summary>
    public IReadOnlyList<string> Lines { get; }
}
