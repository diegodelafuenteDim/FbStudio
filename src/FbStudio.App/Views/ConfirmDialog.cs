using Avalonia;
using FbStudio.App.Resources;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FbStudio.App.Views;

/// <summary>Confirmación explícita para sentencias riesgosas. El botón por defecto es "No ejecutar".</summary>
public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(IReadOnlyList<string> reasons, string? runText = null, string? cancelText = null, string? title = null)
    {
        runText ??= Strings.RunAnyway;
        cancelText ??= Strings.DontRun;
        Title = title ?? Strings.ConfirmRunTitle;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var run = new Button { Content = runText, Classes = { "danger" } };
        var cancel = new Button { Content = cancelText, IsDefault = true, IsCancel = true, Classes = { "accent" } };
        run.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);

        var list = new StackPanel { Spacing = 6 };
        foreach (var r in reasons)
            list.Children.Add(new TextBlock { Text = "⚠ " + r, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                list,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { run, cancel },
                },
            },
        };
        Opened += (_, _) => cancel.Focus();
    }
}
