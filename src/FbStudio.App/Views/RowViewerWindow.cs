using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using FbStudio.App.Resources;
using FbStudio.App.ResultGrid;
using FbStudio.Core.Results;

namespace FbStudio.App.Views;

/// <summary>
/// «Ver la fila en vertical»: una fila de la grilla como lista de columna, tipo y valor (para las tablas anchas, de
/// decenas de columnas), con la fila filtrable y ordenable como cualquier grilla, y ◀ ▶ para pasar a la anterior o la
/// siguiente de lo que se ve. Solo mira.
/// </summary>
public sealed class RowViewerWindow : Window
{
    private readonly DrawnGrid _source;
    private readonly IReadOnlyList<ColumnInfo> _columns;
    private readonly IReadOnlyList<int> _rows;
    private readonly TextBlock _position = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private readonly Button _previous = new() { Content = "◀", Padding = new Thickness(10, 2) };
    private readonly Button _next = new() { Content = "▶", Padding = new Thickness(10, 2) };
    private int _at;

    /// <param name="rowIndex">La fila de <paramref name="source"/> (posición en sus filas cargadas).</param>
    public RowViewerWindow(DrawnGrid source, int rowIndex)
    {
        _source = source;
        _columns = source.DisplayColumns.ToList();
        _rows = source.VisibleRowIndexes();
        _at = Math.Max(0, _rows.ToList().IndexOf(rowIndex));
        Width = 640;
        Height = 620;
        MinWidth = 380;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _previous.Click += (_, _) => Go(-1);
        _next.Click += (_, _) => Go(1);
        var close = new Button { Content = Strings.Close, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Children = { _previous, _position, _next } };
        bar.Children.Add(navigation);

        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(Grid);
        Content = root;
        ShowRow();
    }

    /// <summary>La grilla con columna, tipo y valor.</summary>
    public DrawnGrid Grid { get; } = new();

    /// <summary>Qué fila se muestra (posición en las filas que se ven en la grilla de origen).</summary>
    public int Position => _at;

    public void Go(int delta)
    {
        _at = Math.Clamp(_at + delta, 0, Math.Max(0, _rows.Count - 1));
        ShowRow();
    }

    private void ShowRow()
    {
        if (_rows.Count == 0) return;
        var row = _source.Rows[_rows[_at]];
        ColumnInfo[] columns = [new(0, Strings.RowViewColumn, "VARCHAR"), new(1, Strings.RowViewType, "VARCHAR"), new(2, Strings.RowViewValue, "VARCHAR")];
        Grid.SetData(columns, new AvaloniaList<object[]>(RowRecord.Build(_columns, row)), keepView: true);
        Title = string.Format(Strings.RowViewTitle, _at + 1, _rows.Count);
        _position.Text = string.Format(Strings.RowViewPosition, _at + 1, _rows.Count);
        _previous.IsEnabled = _at > 0;
        _next.IsEnabled = _at < _rows.Count - 1;
    }
}
