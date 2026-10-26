using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.Core.Results;

namespace FbStudio.App.Views;

/// <summary>
/// «Ver el valor» de una celda de la grilla: el texto entero (con ajuste de renglones), el JSON o XML con sangría, el
/// texto de un RTF, una imagen o un binario en hexadecimal; copiarlo o guardarlo tal cual en un archivo. Solo mira: no
/// edita nada, y se pueden tener varias abiertas.
/// </summary>
public sealed class ValueViewerWindow : Window
{
    private readonly CellContent _content;
    private readonly string _column;
    private readonly TextBox _text;
    private readonly ContentControl _body = new();
    private readonly CheckBox _wrap;
    private readonly Button _copy;
    private readonly TextBlock _info;
    private readonly List<(string Name, Func<Control> Build, string? Text)> _views = [];

    public ValueViewerWindow(string column, string dataType, object? value)
    {
        _column = column;
        _content = CellContents.Of(value, dataType);
        Title = string.Format(Strings.ValueViewerTitle, column);
        Width = 820;
        Height = 560;
        MinWidth = 420;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _text = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_text, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        _wrap = new CheckBox { Content = Strings.ValueViewerWrap, IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        _wrap.IsCheckedChanged += (_, _) => _text.TextWrapping = _wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;

        // Las formas de verlo, según lo que es.
        switch (_content.Kind)
        {
            case CellContentKind.Null:
                AddText(Strings.ValueViewerText, "NULL");
                break;
            case CellContentKind.Image:
                _views.Add((Strings.ValueViewerImage, BuildImage, null));
                AddText(Strings.ValueViewerHex, _content.Text);
                break;
            case CellContentKind.Binary:
                AddText(Strings.ValueViewerHex, _content.Text);
                break;
            default:
                if (_content.Formatted is { } formatted)
                    AddText(_content.Format == "RTF" ? Strings.ValueViewerRtfText : Strings.ValueViewerFormatted, formatted);
                AddText(_content.Format == "RTF" ? Strings.ValueViewerRtfSource : Strings.ValueViewerText, _content.Text);
                if (_content.Bytes is { } raw) AddText(Strings.ValueViewerHex, CellContents.HexDump(raw));
                break;
        }

        var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (name, _, _) in _views)
        {
            var radio = new RadioButton { Content = name, GroupName = "vista-" + GetHashCode(), Margin = new Thickness(0, 0, 8, 0) };
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) SelectView(name); };
            choices.Children.Add(radio);
        }

        var info = _info = new TextBlock
        {
            Text = Describe(dataType),
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_wrap, Dock.Right);
        top.Children.Add(_wrap);
        DockPanel.SetDock(choices, Dock.Left);
        top.Children.Add(choices);
        top.Children.Add(info);

        _copy = new Button { Content = Strings.ValueViewerCopy };
        _copy.Click += async (_, _) => { if (ShownText is { } t && Clipboard is { } c) await c.SetTextAsync(t); };
        var save = new Button { Content = Strings.ValueViewerSave, IsEnabled = _content.Kind != CellContentKind.Null };
        save.Click += async (_, _) => await SaveAsync();
        var close = new Button { Content = Strings.Close, IsCancel = true };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_copy);
        buttons.Children.Add(save);
        buttons.Children.Add(close);

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(_body);
        Content = root;
        ((RadioButton)choices.Children[0]).IsChecked = true; // la primera forma de verlo
    }

    /// <summary>Las formas de ver el valor, en el orden en que se ofrecen (la primera es la elegida al abrir).</summary>
    public IReadOnlyList<string> Views => _views.Select(v => v.Name).ToList();

    /// <summary>La forma elegida.</summary>
    public string? CurrentView { get; private set; }

    /// <summary>El texto que se está mostrando; null si se muestra una imagen.</summary>
    public string? ShownText { get; private set; }

    public void SelectView(string name)
    {
        var view = _views.First(v => v.Name == name);
        CurrentView = name;
        ShownText = view.Text;
        _body.Content = view.Build();
        _wrap.IsVisible = view.Text is not null;
        _copy.IsEnabled = view.Text is not null;
    }

    private void AddText(string name, string text) => _views.Add((name, () =>
    {
        _text.Text = text;
        return _text;
    }, text));

    private Control BuildImage()
    {
        try
        {
            using var stream = new MemoryStream(_content.Bytes!);
            var bitmap = new Bitmap(stream);
            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return new TextBlock { Text = Strings.ValueViewerBadImage, Margin = new Thickness(4) };
        }
    }

    private string Describe(string dataType)
    {
        if (_content.Kind == CellContentKind.Null) return $"{dataType} · NULL";
        var size = _content.Bytes is not null
            ? string.Format(Strings.ValueViewerBytes, _content.Size)
            : string.Format(Strings.ValueViewerChars, _content.Size);
        return string.Join(" · ", new[] { dataType, _content.Format, size }.Where(s => !string.IsNullOrEmpty(s)));
    }

    /// <summary>Guarda el valor tal cual (los bytes de un binario; un texto en UTF-8).</summary>
    private async Task SaveAsync()
    {
        var name = string.Concat(_column.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.ValueViewerSave.TrimEnd('…'),
            SuggestedFileName = (name.Length > 0 ? name : "valor") + _content.Extension,
            DefaultExtension = _content.Extension.TrimStart('.'),
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(_content.Bytes ?? Encoding.UTF8.GetBytes(_content.Text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _info.Text = string.Format(Strings.FileSaveFailed, file.Name, ex.Message);
        }
    }
}
