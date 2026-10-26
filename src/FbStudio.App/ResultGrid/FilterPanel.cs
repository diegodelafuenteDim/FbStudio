using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Results;
using Material.Icons;
using Material.Icons.Avalonia;

namespace FbStudio.App.ResultGrid;

/// <summary>
/// Panel de filtros de la grilla (F-12): todas las columnas en una lista, cada una con su filtro
/// escrito igual que en la fila de filtros y el ▾ de su ventanita. Sirve con muchas columnas: se busca la columna por
/// nombre y se ven juntos los filtros puestos, aunque queden fuera de la vista.
/// </summary>
public sealed class FilterPanel : UserControl
{
    private readonly DrawnGrid _grid;
    private readonly TextBox _search;
    private readonly CheckBox _onlyFiltered;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _clearAll;
    private readonly Dictionary<int, (TextBox Box, TextBlock Name)> _boxes = [];
    private IReadOnlyList<int> _shown = [];
    private ColumnInfo[] _columns = [];

    public FilterPanel(DrawnGrid grid)
    {
        _grid = grid;
        _search = new TextBox { PlaceholderText = Strings.FilterPanelSearch, FontSize = 12 };
        _search.TextChanged += (_, _) => RebuildIfChanged();
        _onlyFiltered = new CheckBox { Content = Strings.FilterPanelOnlyFiltered, FontSize = 12 };
        _onlyFiltered.IsCheckedChanged += (_, _) => RebuildIfChanged();
        _clearAll = new Button { Content = Strings.FilterPanelClearAll, FontSize = 12, Padding = new Thickness(8, 2) };
        _clearAll.Click += (_, _) => _grid.ClearFilters();
        var close = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 14, Height = 14 },
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(close, Strings.FilterPanelClose);
        close.Click += (_, _) => _grid.IsFilterPanelOpen = false;

        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(_title);
        var footer = new DockPanel { LastChildFill = false };
        footer.Children.Add(_clearAll);
        Content = new DockPanel
        {
            Margin = new Thickness(10, 8),
            Children =
            {
                Docked(header, Dock.Top),
                Docked(_search, Dock.Top, new Thickness(0, 8, 0, 4)),
                Docked(_onlyFiltered, Dock.Top, new Thickness(0, 0, 0, 6)),
                Docked(new TextBlock { Text = Strings.FilterPanelHint, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap }, Dock.Bottom, new Thickness(0, 6, 0, 0)),
                Docked(footer, Dock.Bottom, new Thickness(0, 6, 0, 0)),
                new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            },
        };
        Rebuild();
    }

    private static Control Docked(Control control, Dock dock, Thickness? margin = null)
    {
        DockPanel.SetDock(control, dock);
        if (margin is { } m) control.Margin = m;
        return control;
    }

    /// <summary>Las columnas que lista ahora (para las pruebas).</summary>
    public IReadOnlyList<int> ShownColumns => _shown;

    /// <summary>El cuadro del filtro de una columna (para las pruebas y para llevar el foco ahí desde el encabezado).</summary>
    public TextBox? BoxFor(int column) => _boxes.TryGetValue(column, out var b) ? b.Box : null;

    public string SearchText
    {
        get => _search.Text ?? "";
        set
        {
            _search.Text = value;
            RebuildIfChanged(); // TextChanged llega después (Avalonia lo avisa en la próxima vuelta)
        }
    }

    public bool OnlyFiltered
    {
        get => _onlyFiltered.IsChecked == true;
        set => _onlyFiltered.IsChecked = value;
    }

    /// <summary>
    /// Los filtros cambiaron en la grilla (fila de filtros, ventanita, «Quitar filtros»): se copian a los cuadros, salvo
    /// al que se está escribiendo. Con «Solo las que tienen filtro» puede cambiar qué columnas se listan.
    /// </summary>
    public void SyncFromGrid()
    {
        if (!ReferenceEquals(_columns, _grid.Columns))
        {
            Rebuild();
            return;
        }
        if (_onlyFiltered.IsChecked == true && !_boxes.Values.Any(b => b.Box.IsFocused) && !Visible().SequenceEqual(_shown))
        {
            Rebuild();
            return;
        }
        foreach (var (column, (box, name)) in _boxes)
        {
            var text = FilterText(column);
            if (!box.IsFocused && box.Text != text) box.Text = text;
            name.FontWeight = text.Length > 0 ? FontWeight.Bold : FontWeight.Normal;
        }
        UpdateTitle();
    }

    /// <summary>
    /// Vuelve a armar la lista solo si cambian las columnas que muestra: rearmarla saca los cuadros, y con ellos la
    /// ventanita abierta desde uno.
    /// </summary>
    private void RebuildIfChanged()
    {
        if (!ReferenceEquals(_columns, _grid.Columns) || !Visible().SequenceEqual(_shown)) Rebuild();
    }

    /// <summary>Vuelve a armar la lista (otras columnas, otra búsqueda).</summary>
    public void Rebuild()
    {
        _rows.Children.Clear();
        _boxes.Clear();
        _columns = _grid.Columns;
        _shown = Visible();
        foreach (var column in _shown) _rows.Children.Add(Row(column));
        if (_shown.Count == 0)
            _rows.Children.Add(new TextBlock { Text = Strings.FilterPanelNone, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        UpdateTitle();
    }

    private List<int> Visible()
    {
        var search = (_search.Text ?? "").Trim();
        return Enumerable.Range(0, _grid.Columns.Length)
            .Where(c => search.Length == 0 || _grid.Columns[c].Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(c => _onlyFiltered.IsChecked != true || _grid.Spec.Filters.ContainsKey(c))
            .ToList();
    }

    private string FilterText(int column) => _grid.Spec.Filters.TryGetValue(column, out var f) ? f.Text : "";

    private void UpdateTitle()
    {
        var active = _grid.Spec.Filters.Count;
        _title.Text = active == 0 ? Strings.FilterPanelTitle : $"{Strings.FilterPanelTitle} · {string.Format(Strings.FilterPanelActive, active)}";
        _clearAll.IsEnabled = active > 0;
    }

    private Control Row(int column)
    {
        var info = _grid.Columns[column];
        var text = FilterText(column);
        var name = new TextBlock
        {
            Text = info.Name,
            FontWeight = text.Length > 0 ? FontWeight.Bold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var detail = info.DataTypeName + (_grid.IsColumnHidden(column) ? $" · {Strings.FilterPanelHidden}" : "");
        var box = new TextBox { Text = text, FontSize = 12, Padding = new Thickness(6, 3), MinHeight = 0, PlaceholderText = DrawnGrid.FilterPlaceholder(info) };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Apply(column, box); e.Handled = true; }
            else if (e.Key == Key.Escape) { box.Text = FilterText(column); e.Handled = true; }
        };
        box.LostFocus += (_, _) => Apply(column, box);
        var popup = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.ChevronDown, Width = 16, Height = 16 },
            Padding = new Thickness(4, 2),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ToolTip.SetTip(popup, Strings.FilterPanelPopupTip);
        popup.Click += (_, _) => _grid.OpenFilterPopup(column, popup);
        var editor = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(popup, Dock.Right);
        popup.Margin = new Thickness(4, 0, 0, 0);
        editor.Children.Add(popup);
        editor.Children.Add(box);
        _boxes[column] = (box, name);
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new DockPanel
                {
                    LastChildFill = true,
                    Children =
                    {
                        Docked(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center }, Dock.Right, new Thickness(6, 0, 0, 0)),
                        name,
                    },
                },
                editor,
            },
        };
    }

    private void Apply(int column, TextBox box)
    {
        var text = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        if ((text ?? "") == FilterText(column)) return;
        _grid.SetFilter(column, text);
    }
}
