using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Results;
using Material.Icons;
using Material.Icons.Avalonia;

namespace FbStudio.App.ResultGrid;

/// <summary>
/// Ventanita de filtro de una columna (F-12), según su tipo: texto (contiene, no contiene, empieza, termina, igual…),
/// números (mayor, menor, entre…), fechas (ese día, un período como «este mes», entre, desde, hasta, con calendario),
/// horas y BOOLEAN; y la lista de valores para tildar. Escribe el mismo texto que se puede tipear en la fila de filtros
/// (se ve abajo antes de aplicar), así lo elegido queda a la vista y se puede corregir a mano.
/// </summary>
public sealed class FilterPopup : UserControl
{
    public const int ValuesLimit = 500;

    private readonly ColumnInfo _column;
    private readonly FilterKind _kind;
    private readonly IReadOnlyList<FilterOperator> _operators;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ValueCount>>>? _loadValues;
    private readonly CancellationTokenSource _cts = new();
    private readonly IReadOnlySet<string> _initialChecked;
    private readonly List<(ValueCount Value, CheckBox Box, string Label)> _values = [];
    private readonly StackPanel _valueRow, _valuesPanel;
    private readonly TextBlock _and, _valuesStatus, _preview;
    private readonly Button _calendar1, _calendar2, _loadButton;
    private readonly TextBox _search;
    private readonly ScrollViewer _valuesScroll;
    private static readonly (string Label, string Text)[] Periods =
    [
        (Strings.FilterRelToday, Strings.FilterRelToday), (Strings.FilterRelYesterday, Strings.FilterRelYesterday),
        (Strings.FilterRelThisWeek, Strings.FilterRelThisWeek), (Strings.FilterRelLastWeek, Strings.FilterRelLastWeek),
        (Strings.FilterRelThisMonth, Strings.FilterRelThisMonth), (Strings.FilterRelLastMonth, Strings.FilterRelLastMonth),
        (Strings.FilterRelThisYear, Strings.FilterRelThisYear), (Strings.FilterRelLast7, Strings.FilterRelLast7),
        (Strings.FilterRelLast30, Strings.FilterRelLast30),
    ];

    /// <param name="loadValues">Trae los valores para tildar; null si la columna no se puede listar.</param>
    /// <param name="valuesOnDemand">Del servidor: recién con el botón (recorre la tabla). Si no, al abrir.</param>
    public FilterPopup(ColumnInfo column, string? current, Func<CancellationToken, Task<IReadOnlyList<ValueCount>>>? loadValues, bool valuesOnDemand)
    {
        _column = column;
        _kind = FilterBuilder.KindOf(column);
        _operators = FilterBuilder.OperatorsFor(_kind);
        _loadValues = loadValues;
        _initialChecked = FilterBuilder.ListItems(current);

        OperatorBox = new ComboBox { ItemsSource = _operators.Select(Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        Value1 = new TextBox { PlaceholderText = Placeholder(), MinWidth = 0 };
        Value2 = new TextBox { PlaceholderText = Placeholder(), MinWidth = 0 };
        PeriodBox = new ComboBox { ItemsSource = Periods.Select(p => p.Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0 };
        _calendar1 = CalendarButton(Value1);
        _calendar2 = CalendarButton(Value2);
        _and = new TextBlock { Text = Strings.FilterAnd, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        _valueRow = new StackPanel { Spacing = 6 };
        var first = Row(Value1, _calendar1);
        var second = Row(Value2, _calendar2);
        _valueRow.Children.Add(first);
        _valueRow.Children.Add(_and);
        _valueRow.Children.Add(second);
        _valueRow.Children.Add(PeriodBox);

        _search = new TextBox { PlaceholderText = Strings.FilterValuesSearch, IsVisible = false };
        _search.TextChanged += (_, _) => FilterValueList();
        _valuesPanel = new StackPanel { Spacing = 2 };
        _valuesScroll = new ScrollViewer { Content = _valuesPanel, MaxHeight = 220, IsVisible = false };
        _valuesStatus = new TextBlock { Opacity = 0.65, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _loadButton = new Button { Content = Strings.FilterValuesLoad, IsVisible = loadValues is not null && valuesOnDemand, FontSize = 12, Padding = new Thickness(8, 3) };
        ToolTip.SetTip(_loadButton, string.Format(Strings.FilterValuesLoadTip, ValuesLimit));
        _loadButton.Click += async (_, _) => await LoadValuesAsync();
        _preview = new TextBlock { Opacity = 0.75, FontSize = 12, TextWrapping = TextWrapping.Wrap };

        var apply = new Button { Content = Strings.FilterApply, Classes = { "accent" }, IsDefault = true };
        var clear = new Button { Content = Strings.FilterClear };
        var cancel = new Button { Content = Strings.Cancel };
        apply.Click += (_, _) => Apply();
        clear.Click += (_, _) => Applied?.Invoke(null);
        cancel.Click += (_, _) => Cancelled?.Invoke();

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = string.Format(Strings.FilterTitle, column.Name), FontWeight = FontWeight.SemiBold },
                OperatorBox,
                _valueRow,
            },
        };
        if (loadValues is not null)
        {
            panel.Children.Add(new Separator { Margin = new Thickness(0, 2) });
            panel.Children.Add(new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = column.DataTypeName == "TIMESTAMP" ? Strings.FilterValuesByDay : Strings.FilterValues,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            });
            panel.Children.Add(_loadButton);
            panel.Children.Add(_search);
            panel.Children.Add(_valuesScroll);
            panel.Children.Add(_valuesStatus);
        }
        panel.Children.Add(new Separator { Margin = new Thickness(0, 2) });
        panel.Children.Add(_preview);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Children = { clear, cancel, apply },
        });

        var frame = new Border
        {
            Padding = new Thickness(12),
            Width = 330,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 4 16 0 #33000000"),
            Child = panel,
        };
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("SemiColorBackground2"));
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("SemiColorBorder"));
        Content = frame;

        // Lo que ya había escrito en la fila de filtros.
        var (op, v1, v2) = FilterBuilder.Describe(current, column);
        OperatorBox.SelectedIndex = Math.Max(0, IndexOf(op));
        if (op == FilterOperator.Relative)
            PeriodBox.SelectedIndex = Math.Max(0, Array.FindIndex(Periods, p => string.Equals(p.Text, v1, StringComparison.OrdinalIgnoreCase)));
        else
            Value1.Text = v1;
        Value2.Text = v2;

        OperatorBox.SelectionChanged += (_, _) => Refresh();
        Value1.TextChanged += (_, _) => UpdatePreview();
        Value2.TextChanged += (_, _) => UpdatePreview();
        PeriodBox.SelectionChanged += (_, _) => UpdatePreview();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancelled?.Invoke(); e.Handled = true; }
            else if (e.Key == Key.Enter && e.Source is TextBox) { Apply(); e.Handled = true; }
        };
        AttachedToVisualTree += (_, _) => (OperatorBox.SelectedItem is not null && FilterBuilder.NeedsValue(SelectedOperator) && SelectedOperator != FilterOperator.Relative ? Value1 : (Control)OperatorBox).Focus();
        DetachedFromVisualTree += (_, _) => _cts.Cancel();
        Refresh();
        if (loadValues is not null && !valuesOnDemand) _ = LoadValuesAsync();
    }

    /// <summary>Lo elegido: el texto del filtro (null = quitarlo).</summary>
    public event Action<string?>? Applied;

    public event Action? Cancelled;

    public ComboBox OperatorBox { get; }
    public TextBox Value1 { get; }
    public TextBox Value2 { get; }
    public ComboBox PeriodBox { get; }

    public FilterOperator SelectedOperator => _operators[Math.Clamp(OperatorBox.SelectedIndex, 0, _operators.Count - 1)];

    public IReadOnlyList<FilterOperator> Operators => _operators;

    public void SelectOperator(FilterOperator op) => OperatorBox.SelectedIndex = Math.Max(0, IndexOf(op));

    /// <summary>Los valores leídos para tildar.</summary>
    public IReadOnlyList<ValueCount> Values => _values.Select(v => v.Value).ToList();

    /// <summary>Tilda (o destilda) un valor de la lista.</summary>
    public void Check(object? value, bool isChecked = true)
    {
        var literal = FilterBuilder.Literal(value, _column);
        foreach (var (v, box, _) in _values)
            if (string.Equals(FilterBuilder.Literal(v.Value, _column), literal, StringComparison.OrdinalIgnoreCase)) box.IsChecked = isChecked;
    }

    /// <summary>El texto que quedaría en la fila de filtros: los valores tildados, o el operador con su valor.</summary>
    public string? FilterText
    {
        get
        {
            var checkedValues = _values.Where(v => v.Box.IsChecked == true).Select(v => v.Value.Value).OrderBy(v => v is null ? 1 : 0).ToList(); // el vacío al final
            if (checkedValues.Count > 0) return FilterBuilder.Values(checkedValues, _column);
            var op = SelectedOperator;
            var first = op == FilterOperator.Relative ? Periods[Math.Clamp(PeriodBox.SelectedIndex, 0, Periods.Length - 1)].Text : Value1.Text;
            return FilterBuilder.Build(op, first, Value2.Text);
        }
    }

    public void Apply() => Applied?.Invoke(FilterText);

    /// <summary>Los valores de la columna, para tildar (sin valores repetidos; vacío primero, después en orden).</summary>
    public async Task LoadValuesAsync()
    {
        if (_loadValues is null) return;
        _loadButton.IsEnabled = false;
        _valuesStatus.IsVisible = true;
        _valuesStatus.Text = Strings.FilterValuesLoading;
        try
        {
            var values = await _loadValues(_cts.Token);
            ShowValues(values);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _valuesStatus.Text = string.Format(Strings.FilterValuesFailed, ex.Message.Split('\n', 2)[0].Trim());
            _loadButton.IsEnabled = true;
        }
    }

    private void ShowValues(IReadOnlyList<ValueCount> values)
    {
        _values.Clear();
        _valuesPanel.Children.Clear();
        foreach (var v in values.OrderBy(v => v.Value is null ? 0 : 1).ThenBy(v => v.Value, ValueComparer.Default))
        {
            var label = v.Value switch
            {
                null => Strings.FilterNullValue,
                DateTime d when _column.DataTypeName == "TIMESTAMP" => FilterBuilder.Literal(d, _column), // por día
                _ => CellFormatter.Format(v.Value, _column.DataTypeName).TrimEnd(),
            };
            var box = new CheckBox
            {
                // Texto aparte: en el Content de un CheckBox, «_» se toma como tecla de acceso.
                Content = new TextBlock { Text = $"{label}   ({v.Count.ToString("N0", CultureInfo.CurrentCulture)})", TextTrimming = TextTrimming.CharacterEllipsis },
                IsChecked = _initialChecked.Contains(FilterBuilder.Literal(v.Value, _column)),
            };
            box.IsCheckedChanged += (_, _) => UpdatePreview();
            _values.Add((v, box, label));
            _valuesPanel.Children.Add(box);
        }
        _loadButton.IsVisible = false;
        _valuesScroll.IsVisible = _values.Count > 0;
        _search.IsVisible = _values.Count > 10;
        _valuesStatus.IsVisible = _values.Count == 0 || values.Count >= ValuesLimit;
        _valuesStatus.Text = _values.Count == 0 ? Strings.FilterValuesNone : string.Format(Strings.FilterValuesMore, ValuesLimit);
        UpdatePreview();
    }

    private void FilterValueList()
    {
        var text = _search.Text?.Trim() ?? "";
        foreach (var (_, box, label) in _values)
            box.IsVisible = text.Length == 0 || box.IsChecked == true || label.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Refresh()
    {
        var op = SelectedOperator;
        var needs = FilterBuilder.NeedsValue(op);
        var period = op == FilterOperator.Relative;
        _valueRow.IsVisible = needs;
        ((Control)Value1.Parent!).IsVisible = needs && !period;
        _and.IsVisible = ((Control)Value2.Parent!).IsVisible = op == FilterOperator.Between;
        PeriodBox.IsVisible = period;
        _calendar1.IsVisible = _calendar2.IsVisible = _kind == FilterKind.Date;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var text = FilterText;
        _preview.Text = text is null ? Strings.FilterPreviewNone : string.Format(Strings.FilterPreview, text);
        if (text is not null && ColumnFilter.Parse(text, _column)?.Error is { } error) _preview.Text += " · " + error;
    }

    private int IndexOf(FilterOperator op)
    {
        for (var i = 0; i < _operators.Count; i++) if (_operators[i] == op) return i;
        return -1;
    }

    private string Placeholder() => _kind switch
    {
        FilterKind.Number => "1500,50",
        FilterKind.Date => _column.DataTypeName == "DATE" ? "2026-07-01" : "2026-07-01 08:30",
        FilterKind.Time => "08:30",
        _ => Strings.FilterTextPlaceholder,
    };

    private string Label(FilterOperator op) => op switch
    {
        FilterOperator.Contains => Strings.FilterOpContains,
        FilterOperator.NotContains => Strings.FilterOpNotContains,
        FilterOperator.StartsWith => Strings.FilterOpStartsWith,
        FilterOperator.EndsWith => Strings.FilterOpEndsWith,
        FilterOperator.Equals => Strings.FilterOpEquals,
        FilterOperator.NotEquals => _kind == FilterKind.Date ? Strings.FilterOpOtherDay : Strings.FilterOpNotEquals,
        FilterOperator.Greater => _kind == FilterKind.Date ? Strings.FilterOpAfter : Strings.FilterOpGreater,
        FilterOperator.GreaterOrEqual => _kind is FilterKind.Date or FilterKind.Time ? Strings.FilterOpFrom : Strings.FilterOpGreaterOrEqual,
        FilterOperator.Less => _kind == FilterKind.Date ? Strings.FilterOpBefore : Strings.FilterOpLess,
        FilterOperator.LessOrEqual => _kind is FilterKind.Date or FilterKind.Time ? Strings.FilterOpUntil : Strings.FilterOpLessOrEqual,
        FilterOperator.Between => Strings.FilterOpBetween,
        FilterOperator.Day => Strings.FilterOpDay,
        FilterOperator.Relative => Strings.FilterOpPeriod,
        FilterOperator.Yes => Strings.FilterOpYes,
        FilterOperator.No => Strings.FilterOpNo,
        FilterOperator.IsNull => Strings.FilterOpIsNull,
        _ => Strings.FilterOpIsNotNull,
    };

    private static Control Row(TextBox box, Button calendar)
    {
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(calendar, Dock.Right);
        calendar.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(calendar);
        row.Children.Add(box);
        return row;
    }

    /// <summary>Botón con un calendario: el día elegido se escribe en el cuadro (que también se puede tipear).</summary>
    private static Button CalendarButton(TextBox target)
    {
        var calendar = new Avalonia.Controls.Calendar { SelectionMode = CalendarSelectionMode.SingleDate };
        var flyout = new Flyout { Content = calendar, Placement = PlacementMode.BottomEdgeAlignedRight };
        var button = new Button { Content = new MaterialIcon { Kind = MaterialIconKind.CalendarMonthOutline, Width = 16, Height = 16 }, Padding = new Thickness(6, 4), Flyout = flyout };
        ToolTip.SetTip(button, Strings.FilterPickDate);
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is not { } day) return;
            target.Text = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            flyout.Hide();
        };
        return button;
    }
}
