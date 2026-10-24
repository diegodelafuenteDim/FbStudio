using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>
/// Confirmación fuerte: el botón que hace la acción se habilita recién cuando se escribe el nombre pedido (sin
/// importar mayúsculas). El botón por defecto es «Cancelar».
/// </summary>
public sealed class TypedConfirmDialog : Window
{
    public TypedConfirmDialog(TypedConfirm prompt)
    {
        Title = prompt.Title;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox();
        var run = new Button { Content = prompt.RunText, Classes = { "danger" }, IsEnabled = false };
        var cancel = new Button { Content = Strings.Cancel, IsDefault = true, IsCancel = true, Classes = { "accent" } };
        box.TextChanged += (_, _) => run.IsEnabled = Matches(box.Text, prompt.Expected);
        run.Click += (_, _) => { if (Matches(box.Text, prompt.Expected)) Close(true); };
        cancel.Click += (_, _) => Close(false);

        var list = new StackPanel { Spacing = 6 };
        foreach (var r in prompt.Reasons)
            list.Children.Add(new TextBlock { Text = "⚠ " + r, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                list,
                new TextBlock { Text = string.Format(Strings.TypedConfirmPrompt, prompt.Expected), TextWrapping = TextWrapping.Wrap },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 4, 0, 0),
                    Children = { run, cancel },
                },
            },
        };
        Opened += (_, _) => box.Focus();
    }

    internal static bool Matches(string? typed, string expected) => string.Equals(typed?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
