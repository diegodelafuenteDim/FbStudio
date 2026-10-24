using Avalonia;
using FbStudio.App.Resources;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FbStudio.App.Views;

/// <summary>Pregunta con varias opciones; devuelve el índice elegido o -1 si se cierra/cancela.</summary>
public sealed class ChoiceDialog : Window
{
    public ChoiceDialog(string title, string message, params string[] options)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var b = new Button { Content = options[i], Classes = { i == 0 ? "accent" : "" } };
            b.Click += (_, _) => Close(index);
            buttons.Children.Add(b);
        }
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        cancel.Click += (_, _) => Close(-1);
        buttons.Children.Add(cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons },
        };
    }
}
