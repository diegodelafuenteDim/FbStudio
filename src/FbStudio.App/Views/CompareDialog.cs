using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;

namespace FbStudio.App.Views;

/// <summary>Lo elegido en «Comparar bases…».</summary>
public sealed record CompareChoice(ConnectionProfile Source, ConnectionProfile Target, SchemaCompareOptions Options);

/// <summary>«Comparar bases…»: origen (el modelo, p. ej. desarrollo), destino (el que se iguala) y opciones.</summary>
public sealed class CompareDialog : Window
{
    /// <param name="areasOf">Las áreas de una base: se puede comparar solo un área del origen.</param>
    public CompareDialog(IReadOnlyList<ConnectionProfile> profiles, ConnectionProfile? source, Func<ConnectionProfile, IReadOnlyList<string>>? areasOf = null)
    {
        Title = Strings.CompareDialogTitle;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        static string Label(ConnectionProfile p) => $"{p.Name}  ·  {ProfileEnvironments.Label(p.Environment)}";
        var sorted = profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var from = new ComboBox { ItemsSource = sorted.Select(Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var to = new ComboBox { ItemsSource = sorted.Select(Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        from.SelectedIndex = source is null ? 0 : Math.Max(0, sorted.FindIndex(p => p.Id == source.Id));
        // Destino propuesto: otra base, preferentemente de producción si el origen no lo es.
        to.SelectedIndex = sorted.FindIndex(p => p.Id != sorted[from.SelectedIndex].Id && p.IsProduction) is var prod and >= 0
            ? prod
            : sorted.FindIndex(p => p.Id != sorted[from.SelectedIndex].Id);
        // Toda la base o un área del origen (sus reglas se aplican también al destino).
        IReadOnlyList<string> areas = [];
        var scope = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(scope, Strings.CompareAreaTip);
        void FillAreas()
        {
            areas = from.SelectedIndex >= 0 && areasOf is not null ? areasOf(sorted[from.SelectedIndex]) : [];
            scope.ItemsSource = new[] { Strings.SchemaScriptWholeDatabase }.Concat(areas.Select(a => string.Format(Strings.SchemaScriptOnlyArea, a))).ToList();
            scope.SelectedIndex = 0;
            scope.IsVisible = areas.Count > 0;
        }
        FillAreas();
        from.SelectionChanged += (_, _) => FillAreas();
        var drop = new CheckBox { Content = Strings.CompareDropExtra };
        var privileges = new CheckBox { Content = Strings.ComparePrivileges, IsChecked = true };
        var positions = new CheckBox { Content = Strings.ComparePositions, IsChecked = true };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        var ok = new Button { Content = Strings.CompareRun, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (from.SelectedIndex < 0 || to.SelectedIndex < 0 || from.SelectedIndex == to.SelectedIndex)
            {
                error.Text = Strings.CompareSameDatabase;
                error.IsVisible = true;
                return;
            }
            Close(new CompareChoice(sorted[from.SelectedIndex], sorted[to.SelectedIndex], new SchemaCompareOptions
            {
                DropExtra = drop.IsChecked == true,
                Privileges = privileges.IsChecked == true,
                ColumnPositions = positions.IsChecked == true,
                Area = scope.SelectedIndex > 0 ? areas[scope.SelectedIndex - 1] : null,
            }));
        };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Strings.CompareSourceLabel },
                from,
                new TextBlock { Text = Strings.CompareTargetLabel, Margin = new Thickness(0, 6, 0, 0) },
                to,
                scope,
                new TextBlock { Text = Strings.CompareDialogNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 4) },
                privileges,
                positions,
                drop,
                error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };
    }
}
