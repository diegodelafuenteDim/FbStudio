using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>
/// Los valores de los parámetros de una consulta (F-25): uno por renglón, con lo último que se usó para cada nombre y un
/// «NULL» para mandarlo vacío de verdad. Enter ejecuta; Esc cancela (devuelve null).
/// </summary>
public sealed class ParameterDialog : Window
{
    private readonly List<(ParameterEntry Entry, TextBox Text, CheckBox Null)> _rows = [];

    public ParameterDialog(IReadOnlyList<ParameterEntry> entries)
    {
        Title = Strings.ParamsTitle;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions() };
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock
            {
                Text = e.Name.StartsWith('#') ? string.Format(Strings.ParamsUnnamed, e.Name[1..]) : ":" + e.Name,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4), FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            };
            var text = new TextBox { Text = e.Text, Margin = new Thickness(0, 4), IsEnabled = !e.IsNull };
            var isNull = new CheckBox { Content = Strings.ParamsNull, IsChecked = e.IsNull, Margin = new Thickness(10, 4, 0, 4) };
            isNull.IsCheckedChanged += (_, _) => text.IsEnabled = isNull.IsChecked != true;
            text.KeyDown += (_, k) => { if (k.Key == Key.Enter) { k.Handled = true; Accept(); } };
            Grid.SetRow(label, i);
            Grid.SetRow(text, i);
            Grid.SetColumn(text, 1);
            Grid.SetRow(isNull, i);
            Grid.SetColumn(isNull, 2);
            grid.Children.Add(label);
            grid.Children.Add(text);
            grid.Children.Add(isNull);
            _rows.Add((e, text, isNull));
        }

        var run = new Button { Content = Strings.ParamsRun, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        run.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            if (_rows.FirstOrDefault(r => r.Text.IsEnabled).Text is { } first)
            {
                first.Focus();
                first.SelectAll();
            }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new ScrollViewer { MaxHeight = 420, Content = grid },
                new TextBlock { Text = Strings.ParamsHint, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { run, cancel } },
            },
        };
    }

    /// <summary>Lo que se va a devolver con «Ejecutar» (para las pruebas).</summary>
    public IReadOnlyList<ParameterEntry> Values => _rows.Select(r => r.Entry with { Text = r.Text.Text ?? "", IsNull = r.Null.IsChecked == true }).ToList();

    public IReadOnlyList<TextBox> Boxes => _rows.Select(r => r.Text).ToList();

    private void Accept() => Close(Values);
}
