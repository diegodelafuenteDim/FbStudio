using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FbStudio.App.Views;
using FbStudio.App.Resources;
using FbStudio.Core.Results;

namespace FbStudio.App.ResultGrid;

/// <summary>
/// Grilla de resultados dibujada a mano (no crea un control por celda: virtualiza filas Y columnas).
/// Funciones al estilo DevExpress: clic en el encabezado ordena, fila de filtros, agrupar arrastrando el
/// encabezado al panel superior, totales al pie (clic derecho), redimensionar columnas, navegar con teclado.
/// La lógica de orden/filtro/grupos/totales vive en <see cref="ResultViewBuilder"/> (núcleo, con tests).
/// </summary>
public sealed class DrawnGrid : Avalonia.Controls.Grid
{
    private readonly GridCanvas _canvas;
    private readonly ScrollBar _v = new() { Orientation = Orientation.Vertical };
    private readonly ScrollBar _h = new() { Orientation = Orientation.Horizontal };
    private readonly Canvas _overlay = new() { IsHitTestVisible = true };
    private readonly WrapPanel _groupChips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _groupPanel;
    private readonly TextBlock _groupHint = new()
    {
        Text = Strings.GridGroupHint,
        Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _viewStatus = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private TextBox? _filterEditor;
    private Popup? _filterPopup;
    private TextBox? _cellEditor;
    private CancellationTokenSource? _buildCts;
    private readonly DispatcherTimer _rebuildTimer;

    public DrawnGrid()
    {
        RowDefinitions = new RowDefinitions("Auto,*");
        _canvas = new GridCanvas(this);

        // Panel de agrupado (arriba)
        var clearFilters = new Button { Content = Strings.GridClearFilters, FontSize = 12, Padding = new Thickness(8, 2), IsVisible = false };
        clearFilters.Click += (_, _) => ApplySpec(Spec with { Filters = ImmutableDictionary<int, ColumnFilter>.Empty });
        var expandAll = new Button { Content = Strings.GridExpandAll, FontSize = 12, Padding = new Thickness(8, 2), IsVisible = false };
        expandAll.Click += (_, _) => ApplySpec(Spec with { ExpandAll = true, ExpandedGroups = [] });
        var collapseAll = new Button { Content = Strings.GridCollapseAll, FontSize = 12, Padding = new Thickness(8, 2), IsVisible = false };
        collapseAll.Click += (_, _) => ApplySpec(Spec with { ExpandAll = false, ExpandedGroups = [] });
        var panelContent = new DockPanel { LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, [DockPanel.DockProperty] = Dock.Right };
        right.Children.Add(_viewStatus);
        right.Children.Add(expandAll);
        right.Children.Add(collapseAll);
        right.Children.Add(clearFilters);
        panelContent.Children.Add(right);
        var chipsHost = new Panel();
        chipsHost.Children.Add(_groupHint);
        chipsHost.Children.Add(_groupChips);
        panelContent.Children.Add(chipsHost);
        _groupPanel = new Border { Padding = new Thickness(8, 4), MinHeight = 34, Child = panelContent, BorderThickness = new Thickness(0, 0, 0, 1) };
        ViewChanged += () =>
        {
            clearFilters.IsVisible = !Spec.Filters.IsEmpty;
            expandAll.IsVisible = collapseAll.IsVisible = !Spec.GroupBy.IsEmpty;
        };
        Children.Add(_groupPanel);

        // Área de datos: canvas + scrollbars + capa para el editor de filtros
        var area = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("*,Auto") };
        SetRow(area, 1);
        area.Children.Add(_canvas);
        area.Children.Add(_overlay);
        SetColumn(_v, 1);
        SetRow(_h, 1);
        area.Children.Add(_v);
        area.Children.Add(_h);
        Children.Add(area);

        _v.ValueChanged += (_, e) => _canvas.SetOffset(_canvas.OffsetX, e.NewValue);
        _h.ValueChanged += (_, e) => _canvas.SetOffset(e.NewValue, _canvas.OffsetY);
        _canvas.SizeChanged += (_, _) => UpdateScrollBars();
        _canvas.PointerWheelChanged += OnWheel;
        _rebuildTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            _rebuildTimer!.Stop();
            ApplySpec(Spec);
        });
        _rebuildTimer.Stop(); // ese constructor lo deja andando: rearmaba la vista una vez sin motivo a los 400 ms
        ActualThemeVariantChanged += (_, _) => UpdatePanelColors();
        UpdatePanelColors();
        RefreshGroupPanel();
        // Clic o teclado pueden cambiar la fila elegida: se avisa después de que el canvas la actualizó.
        _canvas.AddHandler(PointerPressedEvent, (_, _) => Dispatcher.UIThread.Post(CheckSelection), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _canvas.AddHandler(KeyDownEvent, (_, _) => Dispatcher.UIThread.Post(CheckSelection), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Cambió la fila de la celda elegida (clic, teclado o al volver a elegirla después de refrescar).</summary>
    public event Action? SelectionChanged;

    /// <summary>Valores de la fila de la celda elegida, o null.</summary>
    public object[]? CurrentRow => _canvas.CurrentRow();

    private object[]? _lastRow;

    private void CheckSelection()
    {
        var row = CurrentRow;
        if (ReferenceEquals(row, _lastRow)) return;
        _lastRow = row;
        SelectionChanged?.Invoke();
    }

    // ---------- Estado y API pública ----------

    public ColumnInfo[] Columns { get; private set; } = [];
    public AvaloniaList<object[]> Rows { get; private set; } = [];
    public ResultViewSpec Spec { get; private set; } = ResultViewSpec.Empty;
    public ResultViewSnapshot? Snapshot { get; private set; }
    public IReadOnlyDictionary<int, AggregateKind> Aggregates => _aggregates;
    private readonly Dictionary<int, AggregateKind> _aggregates = [];

    /// <summary>Se dispara cuando cambia la vista (orden, filtros, grupos, totales) o llegan filas.</summary>
    public event Action? ViewChanged;

    /// <summary>Tarea de la última reconstrucción (para tests y para mostrar "ordenando…").</summary>
    public Task PendingBuild { get; private set; } = Task.CompletedTask;

    public int DisplayCount => Snapshot?.Count ?? Rows.Count;
    public int VisibleRowCount => Snapshot?.VisibleRowCount ?? Rows.Count;

    public Vector Offset
    {
        get => new(_h.Value, _v.Value);
        set { _h.Value = Math.Clamp(value.X, 0, _h.Maximum); _v.Value = Math.Clamp(value.Y, 0, _v.Maximum); }
    }

    public Size Extent => _canvas.Extent;
    public Size Viewport => _canvas.Bounds.Size;
    public double RowHeight => GridCanvas.RowHeight;
    public int CellsDrawnLastFrame => _canvas.CellsDrawn;

    /// <summary>
    /// Con una fuente (pestaña Datos), el orden y los filtros los resuelve el servidor, no se agrupa y las celdas se
    /// pueden editar (ver <see cref="IGridDataSource"/>).
    /// </summary>
    public IGridDataSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            RefreshGroupPanel();
            _canvas.InvalidateVisual();
        }
    }

    private IGridDataSource? _source;

    /// <summary>Redibuja las celdas (después de aplicar o descartar cambios, que modifican lo que se ve).</summary>
    public void RefreshCells() => _canvas.ClearCacheAndRedraw();

    /// <summary>Hay una celda en edición (para las pruebas).</summary>
    public bool IsEditingCell => _cellEditor is not null;

    /// <summary>Si el último cuadro se dibujó con la paleta oscura (para las pruebas del cambio de tema).</summary>
    public bool LastFrameDark => _canvas.DrewDark;
    public int TextLayoutsCreatedLastFrame => _canvas.LayoutsCreated;

    /// <param name="keepView">
    /// Mismo resultado vuelto a leer (refresco del monitoreo): si las columnas son las mismas se conservan el orden,
    /// los filtros, los grupos, los totales, los anchos, el desplazamiento y la fila elegida (por el valor de la 1.ª columna).
    /// </param>
    public void SetData(ColumnInfo[] columns, AvaloniaList<object[]> rows, bool keepView = false)
    {
        CloseFilterEditor(apply: false);
        CloseCellEditor(commit: false);
        keepView &= columns.Select(c => c.Name).SequenceEqual(Columns.Select(c => c.Name));
        object? selectedKey = keepView ? _canvas.SelectedKey() : null;
        var offset = Offset;
        Rows.CollectionChanged -= OnRowsChanged;
        Columns = columns;
        Rows = rows;
        Rows.CollectionChanged += OnRowsChanged;
        if (!keepView)
        {
            Spec = ResultViewSpec.Empty;
            Snapshot = null;
            _aggregates.Clear();
            _canvas.Reset();
            RefreshGroupPanel();
            UpdateScrollBars();
            RaiseViewChanged();
            return;
        }
        // Los filtros se vuelven a interpretar con las columnas nuevas (mismos nombres y tipos).
        var filters = Spec.Filters.ToImmutableDictionary(f => f.Key, f => ColumnFilter.Parse(f.Value.Text, Columns[f.Key]) ?? f.Value);
        _reselectKey = selectedKey;
        _reselectOffset = offset;
        ApplySpec(Spec with { Filters = filters }, keepSelection: true);
    }

    private object? _reselectKey;
    private Vector _reselectOffset;

    /// <summary>En qué orden se ven las columnas: índices de <see cref="Columns"/>, de izquierda a derecha.</summary>
    public IReadOnlyList<int> ColumnOrder => _canvas.Order;

    /// <summary>Las columnas que se ven, en su orden (sin las ocultas).</summary>
    public IEnumerable<ColumnInfo> DisplayColumns => _canvas.Order.Where(i => !_canvas.IsHidden(i)).Select(i => Columns[i]);

    /// <summary>Columnas ocultas (índices de <see cref="Columns"/>): siguen en el orden, con ancho 0.</summary>
    public IReadOnlyCollection<int> HiddenColumns => _canvas.Hidden;

    public bool IsColumnHidden(int column) => _canvas.IsHidden(column);

    /// <summary>
    /// Oculta una columna (o la vuelve a mostrar). Es solo la vista: sigue en los filtros, el orden y los totales, en
    /// «Copiar como INSERT / UPDATE» y en las exportaciones; no se copia como texto ni se elige con el teclado. La última
    /// que se ve no se puede ocultar.
    /// </summary>
    public void SetColumnHidden(int column, bool hidden)
    {
        if (!_canvas.SetHidden(column, hidden)) return;
        UpdateScrollBars();
        RaiseColumnLayoutChanged();
    }

    public void ShowAllColumns()
    {
        if (!_canvas.ShowAll()) return;
        UpdateScrollBars();
        RaiseColumnLayoutChanged();
    }

    /// <summary>Se movió, se ensanchó o se restauró una columna a mano (para recordarlo).</summary>
    public event Action? ColumnLayoutChanged;

    internal void RaiseColumnLayoutChanged() => ColumnLayoutChanged?.Invoke();

    /// <summary>
    /// Lleva una columna a otra posición (la de la columna antes de la que queda; la cantidad de columnas = al final).
    /// Es solo la vista: el orden de las filas, los filtros y los totales siguen con su columna.
    /// </summary>
    public void MoveColumn(int column, int position)
    {
        if (_canvas.Move(column, position)) RaiseColumnLayoutChanged();
    }

    public double ColumnWidth(int column) => _canvas.ColumnWidth(column);

    /// <summary>Orden, anchos y columnas ocultas, por nombre de columna.</summary>
    public GridColumnLayout ColumnLayout() =>
        new([.. _canvas.Order.Select(i => Columns[i].Name)],
            Columns.Select((c, i) => (c.Name, Width: _canvas.ColumnWidth(i))).DistinctBy(x => x.Name).ToDictionary(x => x.Name, x => x.Width),
            _canvas.Hidden.Count > 0 ? [.. _canvas.Order.Where(_canvas.IsHidden).Select(i => Columns[i].Name)] : null);

    /// <summary>
    /// Pone el orden y los anchos guardados. Las columnas que no figuran (nuevas en la tabla) van al final, en su orden;
    /// las guardadas que ya no existen se ignoran.
    /// </summary>
    public void ApplyColumnLayout(GridColumnLayout? layout)
    {
        if (layout is null || Columns.Length == 0) return;
        var byName = Columns.Select((c, i) => (c.Name, i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
        var order = layout.Order.Where(byName.ContainsKey).Select(n => byName[n]).Distinct().ToList();
        order.AddRange(Enumerable.Range(0, Columns.Length).Except(order));
        var widths = layout.Widths.Where(w => byName.ContainsKey(w.Key)).ToDictionary(w => byName[w.Key], w => w.Value);
        var hidden = (layout.Hidden ?? []).Where(byName.ContainsKey).Select(n => byName[n]);
        _canvas.SetLayout([.. order], widths, hidden);
        UpdateScrollBars();
    }

    /// <summary>Vuelve al orden de la consulta y a los anchos según el contenido, con todas las columnas a la vista.</summary>
    public void ResetColumnLayout()
    {
        _canvas.ResetLayout();
        UpdateScrollBars();
        RaiseColumnLayoutChanged();
    }

    internal void ScrollHorizontallyBy(double dx) => _h.Value = Math.Clamp(_h.Value + dx, 0, _h.Maximum);

    /// <summary>Índices (en <see cref="Rows"/>) de las filas que muestra la grilla, en su orden (respeta filtros y orden).</summary>
    public IReadOnlyList<int> VisibleRowIndexes() => Snapshot is { } s ? s.VisibleRows().ToList() : Enumerable.Range(0, Rows.Count).ToList();

    /// <summary>Filas seleccionadas (Shift+clic, Ctrl+clic, Ctrl+A); sin selección de filas, la de la celda actual.</summary>
    public IReadOnlyList<int> SelectedRowIndexes() => _canvas.SelectedRowIndexes();

    public void SelectAllRows() => _canvas.SelectAll();

    public void SelectRows(int fromDisplay, int toDisplay) => _canvas.SelectRange(fromDisplay, toDisplay);

    /// <summary>
    /// Texto para pegar en Excel: encabezados y filas separadas por tabulaciones, NULL vacío, números con el separador
    /// decimal del sistema y sin tabulaciones ni saltos de línea adentro de los valores.
    /// </summary>
    public string ClipboardText(IEnumerable<int> rowIndexes, bool header = true)
    {
        var sb = new System.Text.StringBuilder();
        var columns = DisplayColumns.ToList(); // en el orden en que se ven
        if (header) sb.Append(string.Join('\t', columns.Select(c => c.Name))).Append(Environment.NewLine);
        foreach (var r in rowIndexes)
        {
            var row = Rows[r];
            sb.Append(string.Join('\t', columns.Select(c => ClipboardValue(row[c.Ordinal], c)))).Append(Environment.NewLine);
        }
        return sb.ToString();
    }

    /// <summary>
    /// «Copiar como INSERT / UPDATE» con las filas elegidas: lo arma la vista, que sabe de qué tabla son. Sin esto no se
    /// ofrece en el menú.
    /// </summary>
    public Func<IReadOnlyList<int>, bool, Task>? CopyAsScript { get; set; }

    /// <summary>
    /// Las filas como se ven: las columnas en el orden de la pantalla (también las ocultas: un INSERT sin ellas perdería
    /// datos) y, en Datos, con los cambios sin aplicar.
    /// </summary>
    public (IReadOnlyList<ColumnInfo> Columns, IReadOnlyList<object?[]> Rows) DisplayedRows(IReadOnlyList<int> rowIndexes)
    {
        var columns = _canvas.Order.Select(i => Columns[i]).ToList();
        var rows = rowIndexes.Where(r => r >= 0 && r < Rows.Count).Select(r =>
        {
            var row = Rows[r];
            return columns.Select(c => Source is { } source && source.TryGetPending(row, c.Ordinal, out var pending) ? pending : row[c.Ordinal]).ToArray();
        }).ToList();
        return (columns, rows);
    }

    /// <summary>Los valores de una columna (filas visibles), uno por línea: sirve para armar un IN (…).</summary>
    public string ColumnClipboardText(int column) =>
        string.Join(Environment.NewLine, VisibleRowIndexes().Select(r => ClipboardValue(Rows[r][column], Columns[column])));

    private static string ClipboardValue(object? value, ColumnInfo column) => value switch
    {
        null or DBNull => "",
        decimal or double or float or int or long or short or byte => ((IFormattable)value).ToString(null, CultureInfo.CurrentCulture),
        _ => CellFormatter.Format(value, column.DataTypeName).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '),
    };

    public void SortBy(int column, SortDirection direction) =>
        ApplySpec(Spec with { SortColumn = direction == SortDirection.None ? null : column, SortDirection = direction });

    public void SetFilter(int column, string? text)
    {
        var parsed = column < Columns.Length ? ColumnFilter.Parse(text, Columns[column]) : null;
        ApplySpec(Spec with { Filters = parsed is null ? Spec.Filters.Remove(column) : Spec.Filters.SetItem(column, parsed) });
    }

    public void SetGroupBy(IEnumerable<int> columns) => ApplySpec(Spec with { GroupBy = [.. columns.Distinct()], ExpandedGroups = [] });

    public void AddGroup(int column)
    {
        if (!Spec.GroupBy.Contains(column)) SetGroupBy(Spec.GroupBy.Append(column));
    }

    public void RemoveGroup(int column) => SetGroupBy(Spec.GroupBy.Where(c => c != column));

    public void ToggleGroupAt(int displayIndex)
    {
        if (Snapshot is null || displayIndex >= Snapshot.Count || !Snapshot[displayIndex].IsGroup) return;
        var key = Snapshot.Groups[Snapshot[displayIndex].GroupIndex].Key;
        ApplySpec(Spec with { ExpandedGroups = Spec.ExpandedGroups.Contains(key) ? Spec.ExpandedGroups.Remove(key) : Spec.ExpandedGroups.Add(key) });
    }

    public void SetAggregate(int column, AggregateKind kind)
    {
        if (kind == AggregateKind.None) _aggregates.Remove(column); else _aggregates[column] = kind;
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>Texto del pie para una columna (lo que se ve dibujado).</summary>
    public string AggregateText(int column)
    {
        if (!_aggregates.TryGetValue(column, out var kind) || column >= Columns.Length) return "";
        var snap = Snapshot ?? ResultViewBuilder.Build(Rows, Rows.Count, ResultViewSpec.Empty);
        var value = snap.Aggregate(column, kind, Columns[column]);
        var prefix = kind switch
        {
            AggregateKind.Count => "n = ",
            AggregateKind.Sum => "Σ ",
            AggregateKind.Average => Strings.GridAvgPrefix,
            AggregateKind.Min => Strings.GridMinPrefix,
            AggregateKind.Max => Strings.GridMaxPrefix,
            _ => "",
        };
        return value switch
        {
            null => prefix + "—",
            long n => prefix + n.ToString("N0", CultureInfo.CurrentCulture),
            decimal d => prefix + d.ToString(d == decimal.Truncate(d) ? "N0" : "N2", CultureInfo.CurrentCulture),
            _ => prefix + CellFormatter.Format(value, Columns[column].DataTypeName),
        };
    }

    // ---------- Reconstrucción de la vista ----------

    private void ApplySpec(ResultViewSpec spec, bool keepSelection = false)
    {
        Spec = spec;
        _buildCts?.Cancel();
        if (Source is { } source)
        {
            // En el servidor: se guarda lo elegido (para dibujar la flecha y los filtros) y la fuente vuelve a leer.
            Spec = spec with { GroupBy = [], ExpandedGroups = [] };
            Snapshot = null;
            PendingBuild = Task.CompletedTask;
            AfterViewChange();
            if (!keepSelection) source.ViewChanged(Spec);
            return;
        }
        if (spec.IsIdentity)
        {
            Snapshot = null;
            PendingBuild = Task.CompletedTask;
            AfterViewChange(keepSelection);
            return;
        }
        var cts = _buildCts = new CancellationTokenSource();
        var rows = Rows;
        var count = rows.Count;
        _viewStatus.Text = count > 200_000 ? Strings.GridProcessing : _viewStatus.Text;
        PendingBuild = Task.Run(() => ResultViewBuilder.Build(rows, count, spec, cts.Token), cts.Token)
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && !cts.IsCancellationRequested && ReferenceEquals(rows, Rows))
                {
                    Snapshot = t.Result;
                    AfterViewChange(keepSelection);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void AfterViewChange(bool keepSelection = false)
    {
        _canvas.Reset(keepWidths: true);
        RefreshGroupPanel();
        UpdateScrollBars();
        if (keepSelection)
        {
            Offset = _reselectOffset;
            _canvas.Reselect(_reselectKey);
            _reselectKey = null;
        }
        RaiseViewChanged();
        CheckSelection();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Spec.IsIdentity || Source is not null)
        {
            _canvas.OnRowsAppended(e);
            UpdateScrollBars();
            RaiseViewChanged();
        }
        else if (!_rebuildTimer.IsEnabled)
        {
            _rebuildTimer.Start(); // con filtros/orden/grupos: se rearma la vista cada tanto mientras llegan filas
        }
    }

    private void RaiseViewChanged()
    {
        _viewStatus.Text = Spec.Filters.IsEmpty
            ? ""
            : Source is not null ? Strings.GridFilteredOnServer
            : string.Format(Strings.GridFiltered, VisibleRowCount, Rows.Count)
              + (Spec.Filters.Values.FirstOrDefault(f => f.Error is not null) is { } bad ? $" · {bad.Error}" : "");
        ViewChanged?.Invoke();
    }

    // ---------- Panel de agrupado ----------

    private void RefreshGroupPanel()
    {
        _groupChips.Children.Clear();
        foreach (var col in Spec.GroupBy)
        {
            if (col >= Columns.Length) continue;
            var chip = new Button
            {
                Content = $"{Columns[col].Name}  ✕",
                FontSize = 12,
                Padding = new Thickness(8, 2),
                Margin = new Thickness(0, 0, 6, 0),
            };
            ToolTip.SetTip(chip, Strings.GridRemoveGroup);
            var c = col;
            chip.Click += (_, _) => RemoveGroup(c);
            _groupChips.Children.Add(chip);
        }
        _groupHint.IsVisible = Spec.GroupBy.IsEmpty && Source is null; // con fuente no se agrupa
        // Con fuente el panel solo hace falta para «Quitar filtros».
        _groupPanel.IsVisible = Columns.Length > 0 && (Source is null || !Spec.Filters.IsEmpty);
    }

    internal void HighlightGroupPanel(bool on) =>
        _groupPanel.Background = on ? new SolidColorBrush(Color.FromArgb(60, 0, 120, 215)) : PanelBackground();

    internal bool IsOverGroupPanel(PointerEventArgs e)
    {
        var p = e.GetPosition(_groupPanel);
        return p.X >= 0 && p.Y >= 0 && p.X <= _groupPanel.Bounds.Width && p.Y <= _groupPanel.Bounds.Height;
    }

    private IBrush PanelBackground() => ActualThemeVariant == ThemeVariant.Dark
        ? new SolidColorBrush(Color.FromRgb(37, 37, 40)) : new SolidColorBrush(Color.FromRgb(246, 246, 248));

    private void UpdatePanelColors()
    {
        _groupPanel.Background = PanelBackground();
        _groupPanel.BorderBrush = ActualThemeVariant == ThemeVariant.Dark
            ? new SolidColorBrush(Color.FromRgb(60, 60, 64)) : new SolidColorBrush(Color.FromRgb(225, 225, 230));
    }

    // ---------- Editor de filtros (TextBox real sobre la celda de la fila de filtros) ----------

    internal void OpenFilterEditor(int column, Rect cellRect)
    {
        CloseFilterEditor(apply: true);
        var editor = new TextBox
        {
            Text = Spec.Filters.TryGetValue(column, out var f) ? f.Text : "",
            Width = Math.Max(80, cellRect.Width),
            Height = cellRect.Height,
            MinHeight = 0,
            Padding = new Thickness(4, 2),
            FontSize = 12,
            PlaceholderText = Columns[column].IsNumeric ? ">100, <=5, =3" : Columns[column].DataTypeName is "DATE" or "TIMESTAMP" ? "2026-07-01" : Strings.GridFilterTextPlaceholder,
            Tag = column,
        };
        Canvas.SetLeft(editor, cellRect.X);
        Canvas.SetTop(editor, cellRect.Y);
        var debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            if (_filterEditor == editor) SetFilter(column, editor.Text);
        });
        debounce.Stop(); // arranca recién al escribir
        editor.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CloseFilterEditor(apply: true); _canvas.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseFilterEditor(apply: false); _canvas.Focus(); e.Handled = true; }
        };
        editor.LostFocus += (_, _) => CloseFilterEditor(apply: true);
        _overlay.Children.Add(editor);
        _filterEditor = editor;
        editor.Focus();
        editor.SelectAll();
    }

    // ---------- Ventanita de filtro según el tipo de la columna (F-12) ----------

    /// <summary>La ventanita de filtro abierta (para las pruebas).</summary>
    internal FilterPopup? CurrentFilterPopup => _filterPopup?.Child as FilterPopup;

    /// <summary>Abre la ventanita de filtro de una columna (el ▾ de la fila de filtros o «Filtrar…» en el encabezado).</summary>
    public void OpenFilterPopup(int column)
    {
        if (column < 0 || column >= Columns.Length) return;
        ScrollIntoView(Math.Max(0, (int)(_v.Value / GridCanvas.RowHeight)), column);
        _canvas.OpenFilterPopupFor(column);
    }

    internal void OpenFilterPopup(int column, Rect cellRect)
    {
        CloseFilterEditor(apply: true);
        CloseFilterPopup();
        var current = Spec.Filters.TryGetValue(column, out var f) ? f.Text : null;
        Func<CancellationToken, Task<IReadOnlyList<ValueCount>>>? load = null;
        var onDemand = false;
        if (Source is { } source)
        {
            // Del servidor recorre la tabla: recién cuando se pide.
            if (source.CanListValues(column)) load = ct => source.DistinctValuesAsync(column, FilterPopup.ValuesLimit, ct);
            onDemand = true;
        }
        else if (Columns[column].DataTypeName is not "BLOB")
        {
            load = _ => Task.FromResult(LocalValues(column));
        }
        var content = new FilterPopup(Columns[column], current, load, onDemand);
        var popup = new Popup
        {
            Child = content,
            PlacementTarget = _canvas,
            Placement = PlacementMode.Bottom,
            PlacementRect = cellRect,
            IsLightDismissEnabled = true,
        };
        content.Applied += text =>
        {
            CloseFilterPopup();
            SetFilter(column, text);
            _canvas.Focus();
        };
        content.Cancelled += () =>
        {
            CloseFilterPopup();
            _canvas.Focus();
        };
        popup.Closed += (_, _) =>
        {
            if (_filterPopup == popup) _filterPopup = null;
            Children.Remove(popup);
        };
        Children.Add(popup);
        _filterPopup = popup;
        popup.IsOpen = true;
    }

    private void CloseFilterPopup()
    {
        if (_filterPopup is not { } popup) return;
        _filterPopup = null;
        popup.IsOpen = false;
        Children.Remove(popup);
    }

    /// <summary>
    /// Los valores de una columna en lo cargado, con cuántas filas tiene cada uno, respetando los filtros de las demás
    /// columnas (no el suyo: es el que se elige). Los más repetidos, hasta el límite.
    /// </summary>
    private IReadOnlyList<ValueCount> LocalValues(int column)
    {
        var others = Spec.Filters.Where(x => x.Key != column && x.Key < Columns.Length).ToList();
        var counts = new Dictionary<object, long>();
        var byDay = Columns[column].DataTypeName == "TIMESTAMP";
        long nulls = 0;
        foreach (var row in Rows)
        {
            if (others.Any(x => !x.Value.Matches(row[x.Key]))) continue;
            var value = row[column];
            if (CellFormatter.IsNull(value)) { nulls++; continue; }
            if (value is byte[]) continue;
            if (value is string s) value = s.TrimEnd();
            if (value is DateTime dt && byDay) value = dt.Date; // por día: por minuto casi no se repiten
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }
        var list = counts.Select(x => new ValueCount(x.Key, x.Value)).OrderByDescending(x => x.Count).Take(FilterPopup.ValuesLimit).ToList();
        if (nulls > 0) list.Add(new ValueCount(null, nulls));
        return list;
    }

    private void CloseFilterEditor(bool apply)
    {
        if (_filterEditor is not { } editor) return;
        _filterEditor = null;
        _overlay.Children.Remove(editor);
        if (apply && editor.Tag is int column) SetFilter(column, editor.Text);
    }

    // ---------- Editor de celdas (solo con fuente: pestaña Datos) ----------

    /// <summary>Abre un TextBox sobre la celda; Enter guarda y baja, Tab guarda y pasa a la derecha, Esc cancela.</summary>
    internal void OpenCellEditor(int displayIndex, int column, Rect cellRect, string? typed = null)
    {
        if (Source is not { } source || !source.CanEdit(column) || displayIndex >= Rows.Count) return;
        CloseCellEditor(commit: true);
        var row = Rows[displayIndex];
        var editor = new TextBox
        {
            Text = typed ?? source.EditText(row, column),
            Width = Math.Max(120, cellRect.Width),
            Height = cellRect.Height,
            MinHeight = 0,
            Padding = new Thickness(4, 1),
            FontSize = 13,
            Tag = (row, column, displayIndex),
        };
        Canvas.SetLeft(editor, cellRect.X);
        Canvas.SetTop(editor, cellRect.Y);
        editor.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Tab)
            {
                e.Handled = true;
                if (!CloseCellEditor(commit: true)) return; // no se entendió: queda abierto con el error
                _canvas.MoveSelection(e.Key == Key.Enter ? 1 : 0, e.Key == Key.Tab ? (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1) : 0);
                _canvas.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseCellEditor(commit: false);
                _canvas.Focus();
            }
        };
        editor.LostFocus += (_, _) => { if (_cellEditor == editor) CloseCellEditor(commit: true, force: true); };
        _overlay.Children.Add(editor);
        _cellEditor = editor;
        editor.Focus();
        if (typed is null) editor.SelectAll();
        else editor.CaretIndex = typed.Length;
    }

    /// <summary>Cierra el editor; con <paramref name="commit"/> pasa el texto a la fuente. Devuelve false si quedó abierto por un error.</summary>
    private bool CloseCellEditor(bool commit, bool force = false)
    {
        if (_cellEditor is not { Tag: (object[] row, int column, int _) } editor) return true;
        if (commit && Source is { } source && source.SetValue(row, column, editor.Text ?? "") is { } error)
        {
            if (!force)
            {
                ToolTip.SetTip(editor, error);
                ToolTip.SetIsOpen(editor, true);
                editor.BorderBrush = Brushes.IndianRed;
                return false;
            }
        }
        _cellEditor = null;
        _overlay.Children.Remove(editor);
        _canvas.ClearCacheAndRedraw();
        return true;
    }

    /// <summary>Elige una celda (como un clic).</summary>
    internal void SelectCell(int displayIndex, int column)
    {
        _canvas.SelectCell(displayIndex, column);
        CheckSelection();
    }

    /// <summary>
    /// Insert, «Nueva fila» o el menú: la fuente agrega una fila arriba de todo y se abre el editor en su primera columna
    /// que se puede escribir. Devuelve false si la fuente no deja agregar.
    /// </summary>
    public bool AddRowAndEdit()
    {
        if (Source?.AddRow() is not { } row) return false;
        var display = Rows.IndexOf(row);
        if (display < 0) return false;
        var column = ColumnOrder.Where(c => !_canvas.IsHidden(c)).FirstOrDefault(Source.CanEdit); // la primera que se ve y se puede escribir
        Offset = new Vector(0, 0);
        SelectCell(display, column);
        _canvas.Focus();
        _canvas.OpenEditorAtSelection();
        return true;
    }

    /// <summary>Para las pruebas: la columna de la celda elegida (−1 si no hay).</summary>
    internal int SelectedColumn => _canvas.SelectedColumn;

    /// <summary>Para las pruebas: escribe en la celda elegida como si se tipeara y se apretara Enter.</summary>
    internal bool EditSelectedCell(string text)
    {
        if (!_canvas.OpenEditorAtSelection(text) || _cellEditor is null) return false;
        return CloseCellEditor(commit: true);
    }

    // ---------- Scroll ----------

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0)
            _h.Value = Math.Clamp(_h.Value - (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 120, 0, _h.Maximum);
        else
            _v.Value = Math.Clamp(_v.Value - e.Delta.Y * 3 * GridCanvas.RowHeight, 0, _v.Maximum);
        e.Handled = true;
    }

    internal void UpdateScrollBars()
    {
        var view = _canvas.Bounds.Size;
        var dataHeight = Math.Max(0, view.Height - GridCanvas.TopHeight - GridCanvas.FooterHeight);
        var ext = _canvas.Extent;
        _v.Maximum = Math.Max(0, DisplayCount * GridCanvas.RowHeight - dataHeight);
        _v.ViewportSize = dataHeight;
        _v.LargeChange = Math.Max(1, dataHeight - GridCanvas.RowHeight);
        _v.SmallChange = GridCanvas.RowHeight;
        _h.Maximum = Math.Max(0, ext.Width - view.Width);
        _h.ViewportSize = view.Width;
        _h.LargeChange = Math.Max(1, view.Width);
        _h.SmallChange = 40;
    }

    internal void ScrollIntoView(int displayIndex, int column)
    {
        var dataHeight = Math.Max(0, _canvas.Bounds.Height - GridCanvas.TopHeight - GridCanvas.FooterHeight);
        var top = displayIndex * GridCanvas.RowHeight;
        if (top < _v.Value) _v.Value = top;
        else if (top + GridCanvas.RowHeight > _v.Value + dataHeight) _v.Value = Math.Min(_v.Maximum, top + GridCanvas.RowHeight - dataHeight);
        var (left, right) = _canvas.ColumnSpan(column);
        var visibleWidth = _canvas.Bounds.Width - _canvas.GutterWidth;
        if (left < _h.Value) _h.Value = left;
        else if (right > _h.Value + visibleWidth) _h.Value = Math.Min(_h.Maximum, right - visibleWidth);
    }

    // =====================================================================================
    // Canvas: dibuja encabezado, fila de filtros, filas/grupos y pie; maneja mouse y teclado.
    // =====================================================================================
    private sealed class GridCanvas : Control
    {
        public const double RowHeight = 24, HeaderHeight = 28, FilterHeight = 26, FooterHeight = 26, CellPad = 6;
        public const double TopHeight = HeaderHeight + FilterHeight;
        private const double ResizeGrip = 4, GroupIndent = 18, FilterButtonWidth = 18;

        private readonly DrawnGrid _owner;
        private double[] _widths = [], _lefts = []; // ancho por columna; borde izquierdo por posición en pantalla
        private int[] _order = [], _pos = [];       // posición → columna y columna → posición (reordenar arrastrando)
        private readonly HashSet<int> _hidden = [];  // columnas ocultas: siguen en el orden, con ancho 0
        private int _dropPosition = -1;             // dónde caería la columna que se arrastra (−1: no cae en el encabezado)
        private bool _autoFitPending; // se ajustan los anchos con las primeras filas de cada resultado
        private Dictionary<(int, int), FormattedText> _cache = new(), _next = new();
        private (int Display, int Column) _selected = (-1, -1);
        private readonly HashSet<int> _rows = []; // filas seleccionadas (índices de la vista), además de la celda actual
        private int _anchor = -1;

        // arrastre / redimensionado
        private int _resizeColumn = -1;
        private double _resizeStartX, _resizeStartWidth;
        private int _pressedHeader = -1;
        private Point _pressPoint;
        private bool _dragging;
        private Point _dragPoint;

        private static readonly Typeface Face = new("Segoe UI, Inter, sans-serif");
        private static readonly Typeface FaceItalic = new("Segoe UI, Inter, sans-serif", FontStyle.Italic);
        private static readonly Typeface FaceBold = new("Segoe UI, Inter, sans-serif", FontStyle.Normal, FontWeight.SemiBold);

        public GridCanvas(DrawnGrid owner)
        {
            _owner = owner;
            ClipToBounds = true;
            Focusable = true;
            // La paleta se elige al dibujar, pero un cambio de tema no pide redibujar, y los textos de las celdas
            // guardados en caché tienen el color de letra del tema anterior.
            ActualThemeVariantChanged += (_, _) =>
            {
                _cache.Clear();
                _next.Clear();
                InvalidateVisual();
            };
        }

        public bool DrewDark { get; private set; }

        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public int CellsDrawn { get; private set; }
        public int LayoutsCreated { get; private set; }
        public double GutterWidth { get; private set; } = 48;

        public Size Extent => new(GutterWidth + (_lefts.Length > 0 ? _lefts[^1] : 0), TopHeight + _owner.DisplayCount * RowHeight + FooterHeight);

        private ColumnInfo[] Columns => _owner.Columns;

        public IReadOnlyList<int> Order => _order;

        public IReadOnlyCollection<int> Hidden => _hidden;

        public int SelectedColumn => _selected.Column;

        public bool IsHidden(int column) => _hidden.Contains(column);

        /// <summary>Oculta o muestra una columna; false si no cambió nada (o si es la última que se ve).</summary>
        public bool SetHidden(int column, bool hidden)
        {
            if (column < 0 || column >= Columns.Length) return false;
            if (hidden && (_hidden.Contains(column) || _hidden.Count >= Columns.Length - 1)) return false;
            if (!hidden && !_hidden.Remove(column)) return false;
            if (hidden)
            {
                _hidden.Add(column);
                if (_selected.Column == column) _selected = (_selected.Display, _order[VisibleAt(_pos[column], 1)]);
            }
            AfterLayoutChange();
            return true;
        }

        public bool ShowAll()
        {
            if (_hidden.Count == 0) return false;
            _hidden.Clear();
            AfterLayoutChange();
            return true;
        }

        /// <summary>La posición visible más cercana a <paramref name="position"/>, yendo en <paramref name="direction"/> (y si no hay, al revés).</summary>
        private int VisibleAt(int position, int direction)
        {
            if (_order.Length == 0) return 0;
            position = Math.Clamp(position, 0, _order.Length - 1);
            var step = direction < 0 ? -1 : 1;
            for (var p = position; p >= 0 && p < _order.Length; p += step) if (!_hidden.Contains(_order[p])) return p;
            for (var p = position; p >= 0 && p < _order.Length; p -= step) if (!_hidden.Contains(_order[p])) return p;
            return position;
        }

        public double ColumnWidth(int column) => column >= 0 && column < _widths.Length ? _widths[column] : 0;

        public void Reset(bool keepWidths = false)
        {
            if (!keepWidths || _widths.Length != Columns.Length)
            {
                _widths = DefaultWidths();
                _order = [.. Enumerable.Range(0, Columns.Length)];
                _hidden.Clear();
                RecomputeLefts();
                OffsetX = OffsetY = 0;
                _autoFitPending = true;
            }
            _cache.Clear();
            _selected = (-1, -1);
            _rows.Clear();
            _anchor = -1;
            InvalidateVisual();
        }

        private double[] DefaultWidths() => Columns.Select(c => Math.Max(
            c.DataTypeName switch { "DATE" => 100.0, "TIMESTAMP" => 160, "CHAR" or "VARCHAR" => 170, _ => 95 },
            Math.Min(320, Measure(c.Name, FaceBold) + 2 * CellPad + 18))).ToArray();

        /// <summary>Lleva la columna a la posición (la de la que queda a su derecha); false si no cambió nada.</summary>
        public bool Move(int column, int position)
        {
            if (column < 0 || column >= _order.Length) return false;
            var from = _pos[column];
            position = Math.Clamp(position, 0, _order.Length);
            if (position > from) position--;
            if (position == from) return false;
            var list = _order.ToList();
            list.RemoveAt(from);
            list.Insert(position, column);
            _order = [.. list];
            AfterLayoutChange();
            return true;
        }

        /// <summary>Orden, anchos y ocultas guardados; con anchos guardados no se ajustan solos con las primeras filas.</summary>
        public void SetLayout(int[] order, IReadOnlyDictionary<int, double> widths, IEnumerable<int>? hidden = null)
        {
            if (order.Length != Columns.Length) return;
            _order = order;
            _hidden.Clear();
            foreach (var c in hidden ?? []) if (c >= 0 && c < Columns.Length) _hidden.Add(c);
            if (_hidden.Count >= Columns.Length) _hidden.Clear(); // al menos una a la vista
            foreach (var (column, width) in widths) if (column < _widths.Length) _widths[column] = Math.Clamp(width, 30, 2000);
            if (widths.Count > 0) _autoFitPending = false;
            AfterLayoutChange();
        }

        public void ResetLayout()
        {
            _order = [.. Enumerable.Range(0, Columns.Length)];
            _hidden.Clear();
            _widths = DefaultWidths();
            if (_owner.Rows.Count > 0) AutoFitAll(); else _autoFitPending = true;
            AfterLayoutChange();
        }

        private void AfterLayoutChange()
        {
            RecomputeLefts();
            _cache.Clear();
            _owner.UpdateScrollBars();
            InvalidateVisual();
        }

        public void OpenFilterPopupFor(int column) => _owner.OpenFilterPopup(column, CellRect(column, HeaderHeight, FilterHeight));

        public object[]? CurrentRow() =>
            _selected.Display >= 0 && _selected.Display < _owner.DisplayCount && !Entry(_selected.Display).IsGroup
                ? _owner.Rows[Entry(_selected.Display).RowIndex]
                : null;

        public void SelectCell(int display, int column)
        {
            _selected = (display, column);
            _rows.Clear();
            _anchor = display;
            InvalidateVisual();
        }

        public void ClearCacheAndRedraw()
        {
            _cache.Clear();
            InvalidateVisual();
        }

        /// <summary>Valor de la primera columna de la fila elegida (para volver a elegirla después de refrescar).</summary>
        public object? SelectedKey() =>
            _selected.Display >= 0 && _selected.Display < _owner.DisplayCount && !Entry(_selected.Display).IsGroup && Columns.Length > 0
                ? _owner.Rows[Entry(_selected.Display).RowIndex][0]
                : null;

        public void Reselect(object? key)
        {
            if (key is null) return;
            for (var d = 0; d < _owner.DisplayCount; d++)
            {
                var entry = Entry(d);
                if (entry.IsGroup || !Equals(_owner.Rows[entry.RowIndex][0], key)) continue;
                _selected = (d, 0);
                InvalidateVisual();
                return;
            }
        }

        /// <summary>Mueve la celda elegida (después de editar: Enter baja, Tab va a la derecha).</summary>
        public void MoveSelection(int rows, int columns)
        {
            if (_selected.Display < 0 || Columns.Length == 0) return;
            _selected = (Math.Clamp(_selected.Display + rows, 0, Math.Max(0, _owner.DisplayCount - 1)), _order[VisibleAt(_pos[_selected.Column] + columns, columns)]);
            _rows.Clear();
            _anchor = _selected.Display;
            _owner.ScrollIntoView(_selected.Display, _selected.Column);
            InvalidateVisual();
        }

        public bool OpenEditorAtSelection(string? typed = null)
        {
            if (_owner.Source is null || _selected.Display < 0 || _selected.Column < 0 || _selected.Column >= Columns.Length) return false;
            _owner.ScrollIntoView(_selected.Display, _selected.Column);
            var y = TopHeight + _selected.Display * RowHeight - OffsetY;
            _owner.OpenCellEditor(_selected.Display, _selected.Column, CellRect(_selected.Column, y, RowHeight), typed);
            return true;
        }

        private IReadOnlyList<object[]> SelectedRows() => SelectedRowIndexes().Select(i => _owner.Rows[i]).ToList();

        public IReadOnlyList<int> SelectedRowIndexes()
        {
            IEnumerable<int> displays = _rows.Count > 0 ? _rows.Order() : _selected.Display >= 0 ? [_selected.Display] : [];
            return displays.Where(d => d < _owner.DisplayCount).Select(Entry).Where(e => !e.IsGroup).Select(e => e.RowIndex).ToList();
        }

        public void SelectAll()
        {
            _rows.Clear();
            for (var d = 0; d < _owner.DisplayCount; d++) if (!Entry(d).IsGroup) _rows.Add(d);
            InvalidateVisual();
        }

        public void SelectRange(int from, int to)
        {
            _rows.Clear();
            for (var d = Math.Max(0, Math.Min(from, to)); d <= Math.Min(_owner.DisplayCount - 1, Math.Max(from, to)); d++)
                if (!Entry(d).IsGroup) _rows.Add(d);
            InvalidateVisual();
        }

        public void OnRowsAppended(NotifyCollectionChangedEventArgs e)
        {
            if (_autoFitPending && _owner.Rows.Count > 0)
            {
                _autoFitPending = false;
                AutoFitAll();
            }
            // Una fila que entra en el medio o arriba (fila nueva) corre los índices: lo dibujado ya no sirve.
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex + (e.NewItems?.Count ?? 0) < _owner.Rows.Count) _cache.Clear();
            var lastVisible = (int)((OffsetY + Bounds.Height) / RowHeight) + 1;
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex <= lastVisible) InvalidateVisual();
        }

        public void SetOffset(double x, double y)
        {
            if (x == OffsetX && y == OffsetY) return;
            OffsetX = x;
            OffsetY = y;
            InvalidateVisual();
        }

        public (double Left, double Right) ColumnSpan(int c) => c >= 0 && c < _widths.Length ? (_lefts[_pos[c]], _lefts[_pos[c] + 1]) : (0, 0);

        private void RecomputeLefts()
        {
            if (_order.Length != _widths.Length) _order = [.. Enumerable.Range(0, _widths.Length)];
            _pos = new int[_order.Length];
            for (var p = 0; p < _order.Length; p++) _pos[_order[p]] = p;
            _lefts = new double[_widths.Length + 1];
            for (var p = 0; p < _order.Length; p++) _lefts[p + 1] = _lefts[p] + (_hidden.Contains(_order[p]) ? 0 : _widths[_order[p]]);
        }

        private static double Measure(string text, Typeface face, double size = 13) =>
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, Brushes.Black).Width;

        private DisplayEntry Entry(int display) => _owner.Snapshot?[display] ?? new DisplayEntry(display, -1);

        // ---------- geometría ----------

        private int ColumnAt(double x)
        {
            var dx = x - GutterWidth + OffsetX;
            if (x < GutterWidth) return -1;
            for (var p = 0; p < _order.Length; p++) if (dx >= _lefts[p] && dx < _lefts[p + 1]) return _order[p];
            return -1;
        }

        private int ResizeEdgeAt(double x)
        {
            if (x < GutterWidth) return -1;
            var dx = x - GutterWidth + OffsetX;
            for (var p = 0; p < _order.Length; p++) if (!_hidden.Contains(_order[p]) && Math.Abs(dx - _lefts[p + 1]) <= ResizeGrip) return _order[p];
            return -1;
        }

        /// <summary>Dónde caería una columna soltada en x: la posición de la columna que quedaría a su derecha (0..n).</summary>
        internal int DropPosition(double x)
        {
            var dx = x - GutterWidth + OffsetX;
            for (var p = 0; p < _order.Length; p++)
                if (dx < (_lefts[p] + _lefts[p + 1]) / 2) return p;
            return _order.Length;
        }

        private double DataBottom => Bounds.Height - FooterHeight;

        private int DisplayAt(double y)
        {
            if (y < TopHeight || y >= DataBottom) return -1;
            var d = (int)((y - TopHeight + OffsetY) / RowHeight);
            return d < _owner.DisplayCount ? d : -1;
        }

        private Rect CellRect(int column, double y, double height)
        {
            var x = GutterWidth + _lefts[_pos[column]] - OffsetX;
            return new Rect(x, y, _widths[column], height);
        }

        // ---------- mouse ----------

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (_resizeColumn >= 0)
            {
                _widths[_resizeColumn] = Math.Max(30, _resizeStartWidth + p.X - _resizeStartX);
                RecomputeLefts();
                _cache.Clear();
                _owner.UpdateScrollBars();
                InvalidateVisual();
                return;
            }
            if (_pressedHeader >= 0 && !_dragging && Math.Abs(p.X - _pressPoint.X) + Math.Abs(p.Y - _pressPoint.Y) > 8) _dragging = true;
            if (_dragging)
            {
                _dragPoint = p;
                var overPanel = _owner.Source is null && _owner.IsOverGroupPanel(e);
                _owner.HighlightGroupPanel(overPanel);
                // Sobre el encabezado (o cerca): se marca entre qué columnas caería; cerca de los bordes, se corre.
                _dropPosition = !overPanel && p.Y > -8 && p.Y < TopHeight + RowHeight ? DropPosition(p.X) : -1;
                if (_dropPosition >= 0 && p.X > Bounds.Width - 24) _owner.ScrollHorizontallyBy(30);
                else if (_dropPosition >= 0 && p.X < GutterWidth + 24) _owner.ScrollHorizontallyBy(-30);
                InvalidateVisual();
                return;
            }
            Cursor = p.Y < HeaderHeight && ResizeEdgeAt(p.X) >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            var p = e.GetPosition(this);
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) { ShowContextMenu(p); e.Handled = true; return; }
            if (!point.Properties.IsLeftButtonPressed) return;

            if (p.Y < HeaderHeight)
            {
                var edge = ResizeEdgeAt(p.X);
                if (edge >= 0 && e.ClickCount == 2) { AutoFit(edge); _owner.RaiseColumnLayoutChanged(); return; }
                if (edge >= 0) { _resizeColumn = edge; _resizeStartX = p.X; _resizeStartWidth = _widths[edge]; e.Pointer.Capture(this); return; }
                _pressedHeader = ColumnAt(p.X);
                _pressPoint = p;
                if (_pressedHeader >= 0) e.Pointer.Capture(this);
                return;
            }
            if (p.Y < TopHeight)
            {
                var col = ColumnAt(p.X);
                if (col < 0) return;
                var cell = CellRect(col, HeaderHeight, FilterHeight);
                if (p.X >= cell.Right - FilterButtonWidth) _owner.OpenFilterPopup(col, cell);
                else _owner.OpenFilterEditor(col, cell);
                return;
            }
            var d = DisplayAt(p.Y);
            if (d < 0) return;
            if (Entry(d).IsGroup) { _owner.ToggleGroupAt(d); return; }
            var c = ColumnAt(p.X);
            if (e.ClickCount == 2 && _owner.Source is not null && c >= 0)
            {
                _selected = (d, c);
                OpenEditorAtSelection();
                e.Handled = true;
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _anchor >= 0)
            {
                SelectRange(_anchor, d);
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (_rows.Count == 0 && _selected.Display >= 0) _rows.Add(_selected.Display);
                if (!_rows.Add(d)) _rows.Remove(d);
                _anchor = d;
            }
            else
            {
                _rows.Clear();
                _anchor = d;
            }
            _selected = (d, c >= 0 ? c : _order.Length > 0 ? _order[VisibleAt(0, 1)] : 0);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_resizeColumn >= 0)
            {
                _resizeColumn = -1;
                e.Pointer.Capture(null);
                _owner.RaiseColumnLayoutChanged();
                return;
            }
            if (_pressedHeader < 0) return;
            var col = _pressedHeader;
            _pressedHeader = -1;
            e.Pointer.Capture(null);
            if (_dragging)
            {
                _dragging = false;
                var over = _owner.Source is null && _owner.IsOverGroupPanel(e);
                var drop = _dropPosition;
                _dropPosition = -1;
                _owner.HighlightGroupPanel(false);
                InvalidateVisual();
                if (over) _owner.AddGroup(col);
                else if (drop >= 0) _owner.MoveColumn(col, drop);
                return;
            }
            // Clic en el encabezado: ascendente ↔ descendente; con Ctrl, quita el orden.
            var spec = _owner.Spec;
            var dir = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? SortDirection.None
                : spec.SortColumn == col && spec.SortDirection == SortDirection.Ascending ? SortDirection.Descending
                : SortDirection.Ascending;
            _owner.SortBy(col, dir);
        }

        /// <summary>Ancho de cada columna según el encabezado y las primeras 100 filas, entre 50 y 400 px.</summary>
        private void AutoFitAll()
        {
            var sample = Math.Min(_owner.Rows.Count, 100);
            for (var c = 0; c < _widths.Length && c < Columns.Length; c++)
            {
                var max = Measure(Columns[c].Name, FaceBold) + 18;
                for (var r = 0; r < sample; r++)
                {
                    var value = _owner.Rows[r][c];
                    if (value is null or DBNull) continue;
                    var text = CellFormatter.Format(value, Columns[c].DataTypeName);
                    if (text.Length > 80) text = text[..80];
                    max = Math.Max(max, Measure(text, Face));
                }
                _widths[c] = Math.Clamp(max + 2 * CellPad + 2, 50, 400);
            }
            RecomputeLefts();
            _cache.Clear();
            _owner.UpdateScrollBars();
            InvalidateVisual();
        }

        private void AutoFit(int column)
        {
            var first = Math.Max(0, (int)(OffsetY / RowHeight));
            var last = Math.Min(_owner.DisplayCount - 1, first + 200);
            var max = Measure(Columns[column].Name, FaceBold) + 18;
            for (var d = first; d <= last; d++)
            {
                var entry = Entry(d);
                if (entry.IsGroup) continue;
                max = Math.Max(max, Measure(CellFormatter.Format(_owner.Rows[entry.RowIndex][column], Columns[column].DataTypeName), Face));
            }
            _widths[column] = Math.Min(600, max + 2 * CellPad + 2);
            RecomputeLefts();
            _cache.Clear();
            _owner.UpdateScrollBars();
            InvalidateVisual();
        }

        // ---------- menús contextuales ----------

        private void ShowContextMenu(Point p)
        {
            var items = new List<Control>();
            MenuItem Item(string header, Action action, bool enabled = true)
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                mi.Click += (_, _) => action();
                return mi;
            }

            var col = ColumnAt(p.X);
            if (p.Y < TopHeight && col >= 0)
            {
                var grouped = _owner.Spec.GroupBy.Contains(col);
                items.Add(Item(Strings.GridSortAscending, () => _owner.SortBy(col, SortDirection.Ascending)));
                items.Add(Item(Strings.GridSortDescending, () => _owner.SortBy(col, SortDirection.Descending)));
                items.Add(Item(Strings.GridRemoveSort, () => _owner.SortBy(col, SortDirection.None), _owner.Spec.SortColumn == col));
                items.Add(new Separator());
                if (_owner.Source is null)
                    items.Add(grouped ? Item(Strings.GridUngroupColumn, () => _owner.RemoveGroup(col)) : Item(Strings.GridGroupByColumn, () => _owner.AddGroup(col)));
                items.Add(Item(Strings.GridAutoFit, () => { AutoFit(col); _owner.RaiseColumnLayoutChanged(); }));
                items.Add(Item(Strings.GridResetColumns, () => _owner.ResetColumnLayout()));
                items.Add(Item(Strings.GridHideColumn, () => _owner.SetColumnHidden(col, true), _hidden.Count < Columns.Length - 1));
                if (_hidden.Count > 0)
                {
                    var show = new MenuItem { Header = string.Format(Strings.GridShowHidden, _hidden.Count) };
                    foreach (var c in _order.Where(_hidden.Contains))
                    {
                        var column = c;
                        show.Items.Add(Item(Columns[column].Name, () => _owner.SetColumnHidden(column, false)));
                    }
                    show.Items.Add(new Separator());
                    show.Items.Add(Item(Strings.GridShowAllColumns, () => _owner.ShowAllColumns()));
                    items.Add(show);
                }
                items.Add(Item(string.Format(Strings.GridCopyColumn, _owner.VisibleRowIndexes().Count), () => _ = CopyTextAsync(_owner.ColumnClipboardText(col))));
                items.Add(new Separator());
                items.Add(Item(Strings.GridFilterMenu, () => _owner.OpenFilterPopup(col)));
                items.Add(Item(Strings.GridRemoveColumnFilter, () => _owner.SetFilter(col, null), _owner.Spec.Filters.ContainsKey(col)));
                items.Add(Item(Strings.GridRemoveAllFilters, () => _owner.ApplySpec(_owner.Spec with { Filters = ImmutableDictionary<int, ColumnFilter>.Empty }), !_owner.Spec.Filters.IsEmpty));
            }
            else if (p.Y >= DataBottom && col >= 0 && _owner.Source is null)
            {
                var numeric = Columns[col].IsNumeric;
                foreach (var (label, kind) in new[]
                {
                    (Strings.GridNoTotal, AggregateKind.None), (Strings.GridCount, AggregateKind.Count), (Strings.GridSum, AggregateKind.Sum),
                    (Strings.GridAverage, AggregateKind.Average), (Strings.GridMinimum, AggregateKind.Min), (Strings.GridMaximum, AggregateKind.Max),
                })
                {
                    var current = _owner._aggregates.GetValueOrDefault(col) == kind;
                    items.Add(Item((current ? "● " : "    ") + label, () => _owner.SetAggregate(col, kind),
                        numeric || kind is not (AggregateKind.Sum or AggregateKind.Average)));
                }
            }
            else
            {
                var d = DisplayAt(p.Y);
                if (d >= 0 && !Entry(d).IsGroup && col >= 0)
                {
                    _selected = (d, col);
                    InvalidateVisual();
                    var value = _owner.Rows[Entry(d).RowIndex][col];
                    if (!_rows.Contains(d)) { _rows.Clear(); _anchor = d; } // clic derecho fuera de la selección: esa fila
                    var selected = SelectedRowIndexes();
                    var visible = _owner.VisibleRowIndexes();
                    items.Add(Item(Strings.GridCopyCell, () => _ = CopyAsync(false)));
                    items.Add(Item(selected.Count > 1 ? string.Format(Strings.GridCopySelectedRows, selected.Count) : Strings.GridCopyRow,
                        () => _ = CopyRowsAsync(selected)));
                    items.Add(Item(string.Format(Strings.GridCopyAllRows, visible.Count), () => _ = CopyRowsAsync(visible)));
                    if (_owner.CopyAsScript is { } copyAs)
                    {
                        // Las filas elegidas como sentencias para pegar en otra base o en un script.
                        items.Add(Item(Strings.GridCopyAsInsert, () => _ = copyAs(selected, false)));
                        items.Add(Item(Strings.GridCopyAsUpdate, () => _ = copyAs(selected, true)));
                    }
                    items.Add(new Separator());
                    items.Add(Item(Strings.GridFilterByValue, () => _owner.SetFilter(col, FilterFor(value, Columns[col], "="))));
                    items.Add(Item(Strings.GridExcludeValue, () => _owner.SetFilter(col, CellFormatter.IsNull(value) ? "!null" : FilterFor(value, Columns[col], "<>"))));
                    if (_owner.Source is { } source)
                    {
                        var row = _owner.Rows[Entry(d).RowIndex];
                        var rowsSel = SelectedRows();
                        items.Add(new Separator());
                        items.Add(Item(Strings.GridEditCell, () => OpenEditorAtSelection(), source.CanEdit(col)));
                        items.Add(Item(Strings.GridSetNull, () => { source.SetNull(row, col); ClearCacheAndRedraw(); }, source.CanEdit(col)));
                        items.Add(Item(rowsSel.All(source.IsDeleted) ? Strings.GridUndelete : string.Format(Strings.GridDeleteRows, rowsSel.Count),
                            () => { source.ToggleDelete(rowsSel); ClearCacheAndRedraw(); }, source.CanEdit(-1)));
                        items.Add(Item(Strings.GridRevertRows, () => { source.Revert(rowsSel); ClearCacheAndRedraw(); }, rowsSel.Any(source.IsChanged)));
                        items.Add(Item(Strings.GridNewRow, () => _owner.AddRowAndEdit(), source.CanEdit(-1)));
                    }
                }
            }
            if (items.Count == 0) return;
            var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
            menu.Open(this);
        }

        private static string FilterFor(object? value, ColumnInfo col, string op) => value switch
        {
            null or DBNull => "null",
            DateTime d => op + d.ToString(col.DataTypeName == "DATE" ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable f when col.IsNumeric => op + f.ToString(null, CultureInfo.InvariantCulture),
            _ => op + CellFormatter.Format(value, col.DataTypeName),
        };

        // ---------- teclado ----------

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                // Con varias filas seleccionadas se copian todas; si no, la celda (o la fila con Ctrl+Shift+C).
                if (_rows.Count > 1 || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) await CopyRowsAsync(SelectedRowIndexes());
                else await CopyAsync(false);
                return;
            }
            if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                SelectAll();
                return;
            }
            if (_owner.Source is not null && e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = _owner.AddRowAndEdit(); // también con la tabla vacía
                return;
            }
            var count = _owner.DisplayCount;
            if (count == 0 || Columns.Length == 0) return;
            if (_owner.Source is { } src && _selected.Display >= 0)
            {
                if (e.Key is Key.F2 or Key.Enter && e.KeyModifiers == KeyModifiers.None)
                {
                    e.Handled = OpenEditorAtSelection();
                    return;
                }
                if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && src.CanEdit(-1))
                {
                    src.ToggleDelete(SelectedRows());
                    ClearCacheAndRedraw();
                    e.Handled = true;
                    return;
                }
            }
            if (_owner.Source is not null && e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = _owner.AddRowAndEdit();
                return;
            }
            // Izquierda y derecha van por la posición en pantalla (las columnas se pueden reordenar).
            var (d, c) = _selected.Display < 0 ? (0, 0) : (_selected.Display, _pos[_selected.Column]);
            var page = Math.Max(1, (int)((DataBottom - TopHeight) / RowHeight) - 1);
            switch (e.Key)
            {
                case Key.Down: d++; break;
                case Key.Up: d--; break;
                case Key.Right: c++; break;
                case Key.Left: c--; break;
                case Key.PageDown: d += page; break;
                case Key.PageUp: d -= page; break;
                case Key.Home: if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) d = 0; c = 0; break;
                case Key.End: if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) d = count - 1; c = Columns.Length - 1; break;
                case Key.Enter or Key.Space when Entry(Math.Clamp(d, 0, count - 1)).IsGroup:
                    _owner.ToggleGroupAt(Math.Clamp(d, 0, count - 1));
                    e.Handled = true;
                    return;
                default: return;
            }
            var previous = _selected.Display < 0 ? 0 : _selected.Display;
            _selected = (Math.Clamp(d, 0, count - 1), _order[VisibleAt(c, e.Key is Key.Left or Key.End ? -1 : 1)]);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
            {
                if (_anchor < 0) _anchor = previous;
                SelectRange(_anchor, _selected.Display);
            }
            else
            {
                _rows.Clear();
                _anchor = _selected.Display;
            }
            _owner.ScrollIntoView(_selected.Display, _selected.Column);
            InvalidateVisual();
            e.Handled = true;
        }

        /// <summary>Con fuente: escribir sobre una celda la empieza a editar con esa letra (como en Excel).</summary>
        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            if (_owner.Source is null || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
            e.Handled = OpenEditorAtSelection(e.Text);
        }

        /// <summary>Copia filas con encabezados; con muchas, pregunta antes (y sugiere Exportar).</summary>
        private async Task CopyRowsAsync(IReadOnlyList<int> rows)
        {
            if (rows.Count == 0) return;
            if (rows.Count > 100_000 && TopLevel.GetTopLevel(this) is Window window
                && !await new ConfirmDialog([string.Format(Strings.GridCopyManyWarning, rows.Count)],
                    Strings.GridCopyAnyway, Strings.Cancel, Strings.GridCopyManyTitle).ShowDialog<bool>(window))
                return;
            await CopyTextAsync(_owner.ClipboardText(rows));
        }

        private async Task CopyTextAsync(string text)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }

        private async Task CopyAsync(bool wholeRow)
        {
            if (_selected.Display < 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            var entry = Entry(_selected.Display);
            if (entry.IsGroup) return;
            var row = _owner.Rows[entry.RowIndex];
            var text = wholeRow
                ? string.Join('\t', _owner.DisplayColumns.Select(c => c.Name)) + Environment.NewLine
                  + string.Join('\t', _owner.DisplayColumns.Select(c => CellFormatter.Format(row[c.Ordinal], c.DataTypeName)))
                : CellFormatter.Format(row[_selected.Column], Columns[_selected.Column].DataTypeName);
            await clipboard.SetTextAsync(text);
        }

        // ---------- dibujo ----------

        private sealed record Palette(IBrush Background, IBrush Foreground, IBrush Muted, IBrush HeaderBackground,
            IBrush AlternateBackground, Pen Line, IBrush Selection, IBrush GroupBackground, IBrush Accent, IBrush Error, IBrush FooterBackground,
            IBrush Edited, IBrush Deleted);

        private static readonly Palette Light = new(Brushes.White, Brushes.Black, new SolidColorBrush(Color.FromRgb(120, 120, 120)),
            new SolidColorBrush(Color.FromRgb(240, 240, 242)), new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            new Pen(new SolidColorBrush(Color.FromRgb(225, 225, 230)), 1), new SolidColorBrush(Color.FromArgb(90, 0, 120, 215)),
            new SolidColorBrush(Color.FromRgb(232, 238, 246)), new SolidColorBrush(Color.FromRgb(0, 95, 184)),
            new SolidColorBrush(Color.FromRgb(198, 40, 40)), new SolidColorBrush(Color.FromRgb(244, 244, 246)),
            new SolidColorBrush(Color.FromArgb(110, 255, 193, 7)), new SolidColorBrush(Color.FromArgb(60, 229, 57, 53)));

        private static readonly Palette Dark = new(new SolidColorBrush(Color.FromRgb(24, 24, 26)), Brushes.WhiteSmoke,
            new SolidColorBrush(Color.FromRgb(150, 150, 150)), new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            new SolidColorBrush(Color.FromRgb(30, 30, 33)), new Pen(new SolidColorBrush(Color.FromRgb(60, 60, 64)), 1),
            new SolidColorBrush(Color.FromArgb(110, 0, 120, 215)), new SolidColorBrush(Color.FromRgb(36, 44, 56)),
            new SolidColorBrush(Color.FromRgb(96, 165, 250)), new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            new SolidColorBrush(Color.FromRgb(38, 38, 41)),
            new SolidColorBrush(Color.FromArgb(90, 255, 193, 7)), new SolidColorBrush(Color.FromArgb(70, 239, 83, 80)));

        private FormattedText Text(string s, Typeface face, IBrush brush, double maxWidth, TextAlignment align = TextAlignment.Left, double size = 13) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush)
            {
                MaxTextWidth = Math.Max(1, maxWidth),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = align,
            };

        public override void Render(DrawingContext ctx)
        {
            DrewDark = ActualThemeVariant == ThemeVariant.Dark;
            var pal = DrewDark ? Dark : Light;
            var w = Bounds.Width;
            var h = Bounds.Height;
            ctx.FillRectangle(pal.Background, new Rect(0, 0, w, h));
            if (Columns.Length == 0)
            {
                ctx.DrawText(Text(Strings.GridEmpty, Face, pal.Muted, w - 24), new Point(12, 12));
                return;
            }

            var count = _owner.DisplayCount;
            GutterWidth = Math.Max(48, Measure(count.ToString("N0", CultureInfo.CurrentCulture), Face, 12) + 2 * CellPad + 4);
            var firstCol = 0;
            while (firstCol < Columns.Length - 1 && _lefts[firstCol + 1] <= OffsetX) firstCol++;
            var lastCol = firstCol;
            while (lastCol < Columns.Length - 1 && GutterWidth + _lefts[lastCol + 1] - OffsetX < w) lastCol++;

            // Filas (con clip al área de datos)
            var drawn = 0;
            var created = 0;
            _next.Clear();
            using (ctx.PushClip(new Rect(0, TopHeight, w, Math.Max(0, DataBottom - TopHeight))))
            {
                var firstRow = Math.Max(0, (int)(OffsetY / RowHeight));
                var lastRow = Math.Min(count - 1, (int)((OffsetY + DataBottom - TopHeight) / RowHeight) + 1);
                var dataNumber = _owner.Spec.GroupBy.IsEmpty;
                for (var d = firstRow; d <= lastRow; d++)
                {
                    var y = TopHeight + d * RowHeight - OffsetY;
                    var entry = Entry(d);
                    if (entry.IsGroup)
                    {
                        DrawGroupRow(ctx, pal, _owner.Snapshot!.Groups[entry.GroupIndex], y, w, d == _selected.Display);
                        continue;
                    }
                    if (_rows.Contains(d)) ctx.FillRectangle(pal.Selection, new Rect(0, y, w, RowHeight));
                    else if (d % 2 == 1) ctx.FillRectangle(pal.AlternateBackground, new Rect(0, y, w, RowHeight));
                    var row = _owner.Rows[entry.RowIndex];
                    var source = _owner.Source;
                    var deleted = source?.IsDeleted(row) == true;
                    var changed = !deleted && source?.IsChanged(row) == true;
                    if (deleted) ctx.FillRectangle(pal.Deleted, new Rect(0, y, w, RowHeight));
                    using (ctx.PushClip(new Rect(GutterWidth, y, w - GutterWidth, RowHeight)))
                    {
                        for (var p = firstCol; p <= lastCol; p++)
                        {
                            var c = _order[p];
                            if (_hidden.Contains(c)) continue;
                            var x = GutterWidth + _lefts[p] - OffsetX;
                            if (_selected == (d, c)) ctx.FillRectangle(pal.Selection, new Rect(x, y, _widths[c], RowHeight));
                            object? pending = null;
                            if (changed && source!.TryGetPending(row, c, out pending))
                            {
                                // Celda cambiada y sin aplicar: fondo marcado y el valor nuevo (sin caché: son pocas).
                                ctx.FillRectangle(pal.Edited, new Rect(x, y, _widths[c], RowHeight));
                                var pt = Text(CellFormatter.Format(pending, Columns[c].DataTypeName), CellFormatter.IsNull(pending) ? FaceItalic : FaceBold,
                                    pal.Foreground, _widths[c] - 2 * CellPad, Columns[c].IsNumeric ? TextAlignment.Right : TextAlignment.Left);
                                ctx.DrawText(pt, new Point(x + CellPad, y + (RowHeight - pt.Height) / 2));
                                drawn++;
                                continue;
                            }
                            if (!_cache.TryGetValue((entry.RowIndex, c), out var ft))
                            {
                                var v = row[c];
                                var isNull = CellFormatter.IsNull(v);
                                ft = Text(CellFormatter.Format(v, Columns[c].DataTypeName), isNull ? FaceItalic : Face,
                                    isNull ? pal.Muted : pal.Foreground, _widths[c] - 2 * CellPad, Columns[c].IsNumeric ? TextAlignment.Right : TextAlignment.Left);
                                created++;
                            }
                            _next[(entry.RowIndex, c)] = ft;
                            ctx.DrawText(ft, new Point(x + CellPad, y + (RowHeight - ft.Height) / 2));
                            drawn++;
                        }
                    }
                    for (var p = firstCol; p <= lastCol; p++)
                    {
                        var c = _order[p];
                        if (_hidden.Contains(c)) continue;
                        var lx = GutterWidth + _lefts[p + 1] - OffsetX - 0.5;
                        if (lx > GutterWidth) ctx.DrawLine(pal.Line, new Point(lx, y), new Point(lx, y + RowHeight));
                    }
                    // número de fila (columna fija); una fila nueva sin aplicar lleva «+» sobre el color de lo editado
                    ctx.FillRectangle(pal.HeaderBackground, new Rect(0, y, GutterWidth, RowHeight));
                    if (source?.IsNew(row) == true)
                    {
                        ctx.FillRectangle(pal.Edited, new Rect(0, y, GutterWidth, RowHeight));
                        ctx.DrawText(Text("+", FaceBold, pal.Foreground, GutterWidth - 2 * CellPad, TextAlignment.Right, 12), new Point(CellPad, y + 5));
                    }
                    else if (dataNumber)
                        ctx.DrawText(Text((d + 1).ToString("N0", CultureInfo.CurrentCulture), Face, pal.Muted, GutterWidth - 2 * CellPad, TextAlignment.Right, 12),
                            new Point(CellPad, y + 5));
                    if (deleted) ctx.DrawLine(new Pen(pal.Error, 1), new Point(GutterWidth, y + RowHeight / 2), new Point(w, y + RowHeight / 2));
                    ctx.DrawLine(pal.Line, new Point(0, y + RowHeight - 0.5), new Point(w, y + RowHeight - 0.5));
                }
                // Con fuente: al ver las últimas filas cargadas, que traiga más (una vez por cuadro, fuera del dibujo).
                if (_owner.Source is { } more && count > 0 && lastRow >= count - 40)
                    Dispatcher.UIThread.Post(more.NearEnd, DispatcherPriority.Background);
            }
            (_cache, _next) = (_next, _cache);

            DrawHeader(ctx, pal, firstCol, lastCol, w);
            DrawFilterRow(ctx, pal, firstCol, lastCol, w);
            DrawFooter(ctx, pal, firstCol, lastCol, w, h);

            // líneas verticales en encabezado, filtros y pie (en las filas las dibuja cada fila de datos)
            foreach (var (top, bottom) in new[] { (0.0, TopHeight), (h - FooterHeight, h) })
                for (var p = firstCol; p <= lastCol; p++)
                {
                    var c = _order[p];
                    if (_hidden.Contains(c)) continue;
                    var x = GutterWidth + _lefts[p + 1] - OffsetX - 0.5;
                    if (x > GutterWidth) ctx.DrawLine(pal.Line, new Point(x, top), new Point(x, bottom));
                }
            ctx.DrawLine(pal.Line, new Point(GutterWidth - 0.5, 0), new Point(GutterWidth - 0.5, h));

            if (_dragging && _dropPosition >= 0 && _dropPosition < _lefts.Length)
            {
                var dropX = Math.Max(GutterWidth + 1, GutterWidth + _lefts[_dropPosition] - OffsetX);
                ctx.DrawLine(new Pen(pal.Accent, 3), new Point(dropX, 0), new Point(dropX, TopHeight));
                ctx.DrawLine(new Pen(pal.Accent, 1), new Point(dropX, TopHeight), new Point(dropX, DataBottom));
            }
            if (_dragging && _pressedHeader >= 0) DrawDragGhost(ctx, pal);
            CellsDrawn = drawn;
            LayoutsCreated = created;
        }

        private void DrawGroupRow(DrawingContext ctx, Palette pal, GroupInfo g, double y, double w, bool selected)
        {
            ctx.FillRectangle(selected ? pal.Selection : pal.GroupBackground, new Rect(0, y, w, RowHeight));
            var x = GutterWidth + g.Level * GroupIndent + 6;
            var glyph = g.Expanded ? "▾" : "▸";
            ctx.DrawText(Text(glyph, FaceBold, pal.Accent, 14), new Point(x, y + 3));
            var col = Columns[g.Column];
            var label = $"{col.Name}: {CellFormatter.Format(g.Value, col.DataTypeName)}   ({(g.RowCount == 1 ? Strings.GridOneRow : string.Format(Strings.RowCountFormat, g.RowCount))})";
            ctx.DrawText(Text(label, FaceBold, pal.Foreground, w - x - 20), new Point(x + 16, y + (RowHeight - 17) / 2));
            ctx.DrawLine(pal.Line, new Point(0, y + RowHeight - 0.5), new Point(w, y + RowHeight - 0.5));
        }

        private void DrawHeader(DrawingContext ctx, Palette pal, int firstCol, int lastCol, double w)
        {
            ctx.FillRectangle(pal.HeaderBackground, new Rect(0, 0, w, HeaderHeight));
            ctx.DrawText(Text("#", FaceBold, pal.Muted, GutterWidth - 2 * CellPad, TextAlignment.Right), new Point(CellPad, 6));
            var spec = _owner.Spec;
            using (ctx.PushClip(new Rect(GutterWidth, 0, w - GutterWidth, HeaderHeight)))
            {
                for (var p = firstCol; p <= lastCol; p++)
                {
                    var c = _order[p];
                    if (_hidden.Contains(c)) continue;
                    var x = GutterWidth + _lefts[p] - OffsetX;
                    var sorted = spec.SortColumn == c && spec.SortDirection != SortDirection.None;
                    var grouped = spec.GroupBy.Contains(c);
                    var name = Text(Columns[c].Name, FaceBold, grouped ? pal.Accent : pal.Foreground, _widths[c] - 2 * CellPad - (sorted ? 14 : 0));
                    ctx.DrawText(name, new Point(x + CellPad, (HeaderHeight - name.Height) / 2));
                    if (sorted)
                        ctx.DrawText(Text(spec.SortDirection == SortDirection.Ascending ? "▲" : "▼", Face, pal.Accent, 14, size: 10),
                            new Point(x + _widths[c] - 16, (HeaderHeight - 13) / 2));
                }
            }
            ctx.DrawLine(pal.Line, new Point(0, HeaderHeight - 0.5), new Point(w, HeaderHeight - 0.5));
        }

        private void DrawFilterRow(DrawingContext ctx, Palette pal, int firstCol, int lastCol, double w)
        {
            ctx.FillRectangle(pal.Background, new Rect(0, HeaderHeight, w, FilterHeight));
            ctx.FillRectangle(pal.HeaderBackground, new Rect(0, HeaderHeight, GutterWidth, FilterHeight));
            ctx.DrawText(Text(Strings.GridFilterRow, Face, pal.Muted, GutterWidth - 2 * CellPad, TextAlignment.Right, 11), new Point(CellPad, HeaderHeight + 6));
            using (ctx.PushClip(new Rect(GutterWidth, HeaderHeight, w - GutterWidth, FilterHeight)))
            {
                for (var p = firstCol; p <= lastCol; p++)
                {
                    var c = _order[p];
                    if (_hidden.Contains(c)) continue;
                    var x = GutterWidth + _lefts[p] - OffsetX;
                    var filtered = _owner.Spec.Filters.TryGetValue(c, out var f);
                    // ▾ a la derecha: abre la ventanita de filtro según el tipo de la columna.
                    var arrow = Text("▾", Face, filtered ? pal.Accent : pal.Muted, FilterButtonWidth, TextAlignment.Center, 12);
                    ctx.DrawText(arrow, new Point(x + _widths[c] - FilterButtonWidth, HeaderHeight + (FilterHeight - arrow.Height) / 2));
                    if (!filtered) continue;
                    var t = Text(f!.Text, Face, f.Error is null ? pal.Accent : pal.Error, _widths[c] - 2 * CellPad - FilterButtonWidth + CellPad, size: 12);
                    ctx.DrawText(t, new Point(x + CellPad, HeaderHeight + (FilterHeight - t.Height) / 2));
                }
            }
            ctx.DrawLine(pal.Line, new Point(0, TopHeight - 0.5), new Point(w, TopHeight - 0.5));
        }

        private void DrawFooter(DrawingContext ctx, Palette pal, int firstCol, int lastCol, double w, double h)
        {
            var y = h - FooterHeight;
            ctx.FillRectangle(pal.FooterBackground, new Rect(0, y, w, FooterHeight));
            ctx.DrawLine(pal.Line, new Point(0, y + 0.5), new Point(w, y + 0.5));
            var visible = _owner.VisibleRowCount;
            ctx.DrawText(Text(visible.ToString("N0", CultureInfo.CurrentCulture), FaceBold, pal.Muted, GutterWidth - 2 * CellPad, TextAlignment.Right, 12),
                new Point(CellPad, y + 5));
            using (ctx.PushClip(new Rect(GutterWidth, y, w - GutterWidth, FooterHeight)))
            {
                if (_owner.Source is { } source)
                {
                    ctx.DrawText(Text(source.FooterText, Face, pal.Muted, w - GutterWidth - 12, size: 11), new Point(GutterWidth + CellPad, y + 6));
                    return;
                }
                if (_owner._aggregates.Count == 0)
                {
                    ctx.DrawText(Text(Strings.GridTotalsHint, FaceItalic, pal.Muted, w - GutterWidth - 12, size: 11),
                        new Point(GutterWidth + CellPad, y + 6));
                    return;
                }
                for (var p = firstCol; p <= lastCol; p++)
                {
                    var c = _order[p];
                    if (_hidden.Contains(c)) continue;
                    var text = _owner.AggregateText(c);
                    if (text.Length == 0) continue;
                    var x = GutterWidth + _lefts[p] - OffsetX;
                    var t = Text(text, FaceBold, pal.Foreground, _widths[c] - 2 * CellPad, Columns[c].IsNumeric ? TextAlignment.Right : TextAlignment.Left, 12);
                    ctx.DrawText(t, new Point(x + CellPad, y + (FooterHeight - t.Height) / 2));
                }
            }
        }

        private void DrawDragGhost(DrawingContext ctx, Palette pal)
        {
            var t = Text(Columns[_pressedHeader].Name, FaceBold, pal.Foreground, 240);
            var rect = new Rect(_dragPoint.X + 8, Math.Max(2, _dragPoint.Y - 10), t.Width + 16, 24);
            ctx.FillRectangle(pal.HeaderBackground, rect);
            ctx.DrawRectangle(null, new Pen(pal.Accent, 1), rect);
            ctx.DrawText(t, new Point(rect.X + 8, rect.Y + 3));
        }
    }
}
