using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;

namespace FbStudio.App.Views;

/// <summary>
/// «Datos del servidor…» (F-16): versión, tipo (Classic / SuperClassic / SuperServer) con qué significa y cómo se supo,
/// sistema, datos de la base y caché, sin rutas del servidor. Se abre enseguida y se llena cuando llegan los datos.
/// </summary>
public sealed class ServerDialog : Window
{
    private readonly string _address;
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly Button _copy = new() { Content = Strings.CopyLog, IsEnabled = false };
    private string? _text;

    public ServerDialog(string databaseName, string address, Task<ServerDetails> loading)
    {
        _address = address;
        Title = string.Format(Strings.ServerDialogTitle, databaseName);
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _body.Children.Add(new TextBlock { Text = Strings.ServerReading, Opacity = 0.7 });
        _copy.Click += async (_, _) =>
        {
            if (_text is not null && Clipboard is { } clipboard) await clipboard.SetTextAsync(_text);
        };
        var close = new Button { Content = Strings.Close, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                _body,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _copy, close } },
            },
        };
        Opened += async (_, _) =>
        {
            close.Focus();
            try { Show(await loading); }
            catch (Exception ex) { ShowError(ex.Message.Split('\n', 2)[0].Trim()); }
        };
    }

    /// <summary>Las filas que se ven (para las pruebas).</summary>
    public Grid? Table { get; private set; }

    private void Show(ServerDetails details)
    {
        var rows = details.Rows(_address);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Label, Opacity = 0.7, Margin = new Thickness(0, 3, 16, 3) };
            var value = new SelectableTextBlock { Text = rows[i].Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3) };
            if (rows[i].Label == Core.Resources.CoreStrings.ServerRowMode) value.FontWeight = FontWeight.SemiBold;
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        Table = grid;
        _text = details.Text(_address);
        _copy.IsEnabled = true;
        _body.Children.Clear();
        _body.Children.Add(grid);
    }

    private void ShowError(string message)
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = string.Format(Strings.ServerReadFailed, message), TextWrapping = TextWrapping.Wrap });
    }
}
