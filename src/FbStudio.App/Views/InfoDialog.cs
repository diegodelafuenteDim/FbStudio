using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;

namespace FbStudio.App.Views;

/// <summary>Aviso con un solo botón («Aceptar»): para confirmar que algo se hizo.</summary>
public sealed class InfoDialog : Window
{
    public InfoDialog(string title, string message)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button { Content = Strings.Ok, IsDefault = true, IsCancel = true, Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, ok },
        };
        Opened += (_, _) => ok.Focus();
    }
}
