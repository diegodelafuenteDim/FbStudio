using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FbStudio.App.Resources;
using FbStudio.Core.Sql;
using TextSearch = FbStudio.Core.Sql.TextSearch;
using Material.Icons;
using Material.Icons.Avalonia;

namespace FbStudio.App.Views;

/// <summary>
/// Buscar y reemplazar en el editor (Ctrl+F, Ctrl+H): todas las coincidencias marcadas, «3 de 12», F3 y Mayús+F3 para la
/// siguiente y la anterior, mayúsculas, palabra entera y expresión regular. «Reemplazar todo» es un solo paso de Ctrl+Z.
/// </summary>
public sealed class FindReplaceBar : UserControl
{
    private readonly TextEditor _editor;
    private readonly TextBox _find = new() { PlaceholderText = Strings.FindPlaceholder, MinWidth = 260, FontSize = 12, Padding = new Thickness(6, 3), MinHeight = 0 };
    private readonly TextBox _replace = new() { PlaceholderText = Strings.ReplacePlaceholder, MinWidth = 260, FontSize = 12, Padding = new Thickness(6, 3), MinHeight = 0 };
    private readonly ToggleButton _matchCase = Toggle("Aa", Strings.FindMatchCase);
    private readonly ToggleButton _wholeWord = Toggle("ab|", Strings.FindWholeWord);
    private readonly ToggleButton _regex = Toggle(".*", Strings.FindRegex);
    private readonly TextBlock _count = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 };
    private readonly StackPanel _replaceRow;
    private readonly Highlighter _highlighter = new();
    private IReadOnlyList<TextMatch> _matches = [];

    public FindReplaceBar(TextEditor editor)
    {
        _editor = editor;
        IsVisible = false;
        _editor.TextArea.TextView.BackgroundRenderers.Add(_highlighter);
        _editor.Document.TextChanged += (_, _) => { if (IsVisible) Refresh(); };
        _editor.DocumentChanged += (_, _) =>
        {
            _editor.Document.TextChanged += (_, _) => { if (IsVisible) Refresh(); };
            if (IsVisible) Refresh();
        };
        _find.TextChanged += (_, _) => Refresh(select: true);
        foreach (var t in new[] { _matchCase, _wholeWord, _regex }) t.IsCheckedChanged += (_, _) => Refresh(select: true);

        _find.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Go(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; }
        };
        _replace.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ReplaceNext(); e.Handled = true; }
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.F3) { Go(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; }
            // Tab y Mayús+Tab van y vienen entre buscar y reemplazar (sin pasar por los botones).
            else if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && _replaceRow?.IsVisible == true
                     && (_find.IsFocused || _replace.IsFocused))
            {
                var other = _find.IsFocused ? _replace : _find;
                other.Focus();
                other.SelectAll();
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var previous = IconButton(MaterialIconKind.ArrowUp, Strings.FindPrevious, () => Go(-1));
        var next = IconButton(MaterialIconKind.ArrowDown, Strings.FindNext, () => Go(1));
        var close = IconButton(MaterialIconKind.Close, Strings.FindClose, Close);
        var replaceOne = new Button { Content = Strings.ReplaceOne, FontSize = 12, Padding = new Thickness(8, 2) };
        replaceOne.Click += (_, _) => ReplaceNext();
        var replaceAll = new Button { Content = Strings.ReplaceAll, FontSize = 12, Padding = new Thickness(8, 2) };
        replaceAll.Click += (_, _) => ReplaceAll();
        _replaceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _replace, replaceOne, replaceAll } };

        Content = new Border
        {
            Padding = new Thickness(6, 4),
            BorderThickness = new Thickness(0, 0, 0, 1),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SemiColorBorder"),
            [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SemiColorFill0"),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _find, previous, next, _matchCase, _wholeWord, _regex, _count, close } },
                    _replaceRow,
                },
            },
        };
    }

    /// <summary>Las coincidencias de ahora (para las pruebas).</summary>
    public IReadOnlyList<TextMatch> Matches => _matches;

    public string CountText => _count.Text ?? "";

    /// <summary>Dónde está el foco dentro de la barra (para las pruebas).</summary>
    internal bool FindHasFocus => _find.IsFocused;
    internal bool ReplaceHasFocus => _replace.IsFocused;

    public string FindText
    {
        get => _find.Text ?? "";
        set
        {
            _find.Text = value;
            Refresh(select: true); // TextChanged llega después (Avalonia lo avisa en la próxima vuelta)
        }
    }

    public string ReplaceText
    {
        get => _replace.Text ?? "";
        set => _replace.Text = value;
    }

    public bool MatchCase { get => _matchCase.IsChecked == true; set => _matchCase.IsChecked = value; }
    public bool WholeWord { get => _wholeWord.IsChecked == true; set => _wholeWord.IsChecked = value; }
    public bool UseRegex { get => _regex.IsChecked == true; set => _regex.IsChecked = value; }

    private TextSearchOptions Options => new(MatchCase, WholeWord, UseRegex);

    /// <summary>Ctrl+F (o Ctrl+H con <paramref name="replace"/>): con lo elegido en el editor (si es de un renglón) como texto a buscar.</summary>
    public void Open(bool replace)
    {
        IsVisible = true;
        _replaceRow.IsVisible = replace && !_editor.IsReadOnly;
        var selected = _editor.SelectedText;
        if (selected.Length > 0 && !selected.Contains('\n')) _find.Text = selected;
        Refresh();
        var box = replace && _find.Text?.Length > 0 && _replaceRow.IsVisible ? _replace : _find;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    public void Close()
    {
        IsVisible = false;
        _matches = [];
        _highlighter.Matches = [];
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        _editor.Focus();
    }

    /// <summary>F3 / Mayús+F3 (o Enter / Mayús+Enter en el cuadro): la siguiente o la anterior, dando la vuelta.</summary>
    public void Go(int direction)
    {
        if (!IsVisible) Open(false);
        if (_matches.Count == 0) return;
        var match = direction > 0
            ? TextSearch.Next(_matches, _editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.CaretOffset - 1)
            : TextSearch.Previous(_matches, _editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.CaretOffset);
        if (match is { } m) Select(m);
    }

    /// <summary>Reemplaza la coincidencia elegida (si lo elegido es una) y va a la siguiente.</summary>
    public void ReplaceNext()
    {
        Refresh();
        if (_editor.IsReadOnly || _matches.Count == 0) return;
        var current = _matches.FirstOrDefault(m => m.Start == _editor.SelectionStart && m.Length == _editor.SelectionLength);
        if (current.Length > 0)
        {
            var replacement = TextSearch.Replacement(_editor.Text, current, FindText, ReplaceText, Options);
            _editor.Document.Replace(current.Start, current.Length, replacement);
            _editor.CaretOffset = current.Start + replacement.Length;
            Refresh();
        }
        Go(1);
    }

    /// <summary>Reemplaza todas de una vez (un solo Ctrl+Z las vuelve atrás).</summary>
    public int ReplaceAll()
    {
        Refresh();
        if (_editor.IsReadOnly || _matches.Count == 0) return 0;
        var text = _editor.Text;
        var matches = _matches.ToList();
        _editor.Document.BeginUpdate();
        try
        {
            foreach (var m in matches.AsEnumerable().Reverse())
                _editor.Document.Replace(m.Start, m.Length, TextSearch.Replacement(text, m, FindText, ReplaceText, Options));
        }
        finally
        {
            _editor.Document.EndUpdate();
        }
        Refresh();
        _count.Text = string.Format(Strings.ReplacedCount, matches.Count);
        return matches.Count;
    }

    private void Select(TextMatch m)
    {
        _editor.Select(m.Start, m.Length);
        var location = _editor.Document.GetLocation(m.Start);
        _editor.ScrollTo(location.Line, location.Column);
        UpdateCount();
    }

    /// <param name="select">El texto o las opciones cambiaron: se elige la primera coincidencia desde donde está el cursor.</param>
    private void Refresh(bool select = false)
    {
        _matches = TextSearch.FindAll(_editor.Text, FindText, Options, out var error);
        _highlighter.Matches = _matches;
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        if (error is not null)
        {
            _count.Text = Strings.FindInvalid;
            return;
        }
        if (select && _matches.Count > 0)
        {
            var from = _editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.CaretOffset;
            Select(_matches.FirstOrDefault(m => m.Start >= from) is { Length: > 0 } after ? after : _matches[0]);
            return;
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (FindText.Length == 0) _count.Text = "";
        else if (_matches.Count == 0) _count.Text = Strings.FindNone;
        else
        {
            var index = _matches.ToList().FindIndex(m => m.Start == _editor.SelectionStart && m.Length == _editor.SelectionLength);
            _count.Text = index >= 0 ? string.Format(Strings.FindPosition, index + 1, _matches.Count)
                : _matches.Count == 1 ? Strings.FindTotalOne : string.Format(Strings.FindTotal, _matches.Count);
        }
    }

    private static ToggleButton Toggle(string text, string tip)
    {
        var toggle = new ToggleButton { Content = text, FontSize = 12, Padding = new Thickness(6, 2), FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace") };
        ToolTip.SetTip(toggle, tip);
        return toggle;
    }

    private static Button IconButton(MaterialIconKind icon, string tip, Action click)
    {
        var button = new Button { Content = new MaterialIcon { Kind = icon, Width = 14, Height = 14 }, Padding = new Thickness(5, 3) };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Pinta de fondo todas las coincidencias que están a la vista.</summary>
    private sealed class Highlighter : IBackgroundRenderer
    {
        private static readonly IBrush Brush = new SolidColorBrush(Color.FromArgb(90, 255, 193, 7));

        public IReadOnlyList<TextMatch> Matches { get; set; } = [];

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Matches.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
            var start = textView.VisualLines[0].FirstDocumentLine.Offset;
            var end = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
            var any = false;
            foreach (var m in Matches)
            {
                if (m.End < start) continue;
                if (m.Start > end) break;
                builder.AddSegment(textView, new TextSegment { StartOffset = m.Start, Length = m.Length });
                any = true;
            }
            if (any && builder.CreateGeometry() is { } geometry) drawingContext.DrawGeometry(Brush, null, geometry);
        }
    }
}
