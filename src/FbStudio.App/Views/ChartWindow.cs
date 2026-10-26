using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.App.ResultGrid;
using FbStudio.Core.Results;

namespace FbStudio.App.Views;

/// <summary>
/// «Gráfico…» de un resultado: elegir la columna de las etiquetas (X), la del valor (Y, o la cantidad de filas) y si van
/// barras o línea; usa las filas que se ven en la grilla (con sus filtros). Se puede guardar como PNG.
/// </summary>
public sealed class ChartWindow : Window
{
    private readonly DrawnGrid _source;
    private readonly IReadOnlyList<ColumnInfo> _columns;
    private readonly List<ColumnInfo?> _values;
    private readonly ComboBox _x = new() { MinWidth = 160 };
    private readonly ComboBox _y = new() { MinWidth = 160 };
    private readonly ComboBox _kind = new() { MinWidth = 110 };
    private readonly TextBlock _info = new() { Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

    /// <param name="preferred">La columna desde la que se pidió (encabezado), si hay: va como X o como Y según su tipo.</param>
    public ChartWindow(DrawnGrid source, int? preferred = null)
    {
        _source = source;
        _columns = source.DisplayColumns.ToList();
        _values = [null, .. _columns.Where(c => c.IsNumeric)];
        Title = Strings.ChartTitle;
        Width = 900;
        Height = 560;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _x.ItemsSource = _columns.Select(c => c.Name).ToList();
        _y.ItemsSource = _values.Select(c => c?.Name ?? Strings.ChartRowCount).ToList();
        _kind.ItemsSource = new[] { Strings.ChartBars, Strings.ChartLine };
        // Lo propuesto: X la primera que no es número (o la pedida), Y la primera numérica (o la pedida si es número).
        var asked = preferred is { } p ? _columns.FirstOrDefault(c => c.Ordinal == p) : null;
        var x = asked is { IsNumeric: false } ? asked : _columns.FirstOrDefault(c => !c.IsNumeric) ?? _columns.FirstOrDefault();
        var y = asked is { IsNumeric: true } && asked != x ? asked : _columns.FirstOrDefault(c => c.IsNumeric && c != x);
        _x.SelectedIndex = x is null ? -1 : _columns.ToList().IndexOf(x);
        _y.SelectedIndex = y is null ? 0 : _values.IndexOf(y);
        _kind.SelectedIndex = x is not null && ChartData.IsTemporal(x) ? 1 : 0;
        _x.SelectionChanged += (_, _) => Refresh();
        _y.SelectionChanged += (_, _) => Refresh();
        _kind.SelectionChanged += (_, _) => Chart.Kind = _kind.SelectedIndex == 1 ? ChartKind.Line : ChartKind.Bars;

        var save = new Button { Content = Strings.ChartSave };
        save.Click += async (_, _) => await SaveAsync();
        var close = new Button { Content = Strings.Close, IsCancel = true };
        close.Click += (_, _) => Close();
        static TextBlock Label(string text) => new() { Text = text, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var c in new Control[] { Label(Strings.ChartLabels), _x, Label(Strings.ChartValues), _y, _kind, _info })
        {
            c.Margin = c.Margin == default ? new Thickness(0, 0, 12, 4) : c.Margin;
            top.Children.Add(c);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { save, close } };
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = Chart });
        Content = root;
        Chart.Kind = _kind.SelectedIndex == 1 ? ChartKind.Line : ChartKind.Bars;
        Refresh();
    }

    public ChartControl Chart { get; } = new();

    /// <summary>«24 barras · suma de IMPORTE por MEDICO» (y si se cortó).</summary>
    public string Info => _info.Text ?? "";

    public void Choose(string x, string? y)
    {
        _x.SelectedIndex = _columns.ToList().FindIndex(c => c.Name == x);
        _y.SelectedIndex = y is null ? 0 : _values.FindIndex(c => c?.Name == y);
        Refresh();
    }

    private void Refresh()
    {
        if (_x.SelectedIndex < 0) return;
        var x = _columns[_x.SelectedIndex];
        var y = _y.SelectedIndex > 0 ? _values[_y.SelectedIndex] : null;
        var rows = _source.VisibleRowIndexes().Select(r => (IReadOnlyList<object?>)_source.Rows[r]);
        var (points, truncated) = ChartData.Build(x, y, rows);
        Chart.Points = points;
        Title = y is null ? string.Format(Strings.ChartTitleCount, x.Name) : string.Format(Strings.ChartTitleSum, y.Name, x.Name);
        _info.Text = string.Format(Strings.ChartPoints, points.Count) + (truncated ? " · " + string.Format(Strings.ChartTruncated, ChartData.MaxPoints) : "");
    }

    private async Task SaveAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.ChartSave.TrimEnd('…'),
            SuggestedFileName = $"grafico-{DateTime.Now:yyyyMMdd-HHmm}.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var size = new PixelSize(Math.Max(1, (int)Chart.Bounds.Width), Math.Max(1, (int)Chart.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(Chart);
        try
        {
            bitmap.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _info.Text = string.Format(Strings.ErrorStatus, ex.Message);
        }
    }
}
