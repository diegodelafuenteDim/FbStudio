using Avalonia;
using FbStudio.App.Resources;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace FbStudio.App.Views;

/// <summary>
/// Pide un texto; devuelve null si se cancela. Con <paramref name="allowEmpty"/> acepta vacío (devuelve ""); con
/// sugerencias, muestra cada una como botón que la pone en el cuadro.
/// </summary>
public sealed class InputDialog : Window
{
    private readonly bool _allowEmpty;

    public InputDialog(string title, string message, string initial = "", bool allowEmpty = false, IReadOnlyList<string>? suggestions = null)
    {
        _allowEmpty = allowEmpty;
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox { Text = initial };
        var ok = new Button { Content = Strings.Ok, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) => Accept(box.Text);
        cancel.Click += (_, _) => Close(null);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(box.Text); } };
        Opened += (_, _) => { box.Focus(); box.SelectAll(); };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                box,
            },
        };
        if (suggestions is { Count: > 0 })
        {
            var chips = new WrapPanel { Name = "Suggestions" };
            foreach (var s in suggestions)
            {
                var chip = new Button { Content = s, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2) };
                chip.Click += (_, _) => { box.Text = s; box.Focus(); box.CaretIndex = s.Length; };
                chips.Children.Add(chip);
            }
            panel.Children.Add(chips);
        }
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } });
        Content = panel;
    }

    private void Accept(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) Close(text.Trim());
        else if (_allowEmpty) Close("");
    }
}
