using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using FbStudio.App.Editor;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;
using FbStudio.Core.Sql;

namespace FbStudio.App.Views;

/// <summary>
/// Los comandos que va a ejecutar «Aplicar» en la pestaña Datos, antes de ejecutarlos: Ejecutar, abrirlos en una
/// pestaña de consulta (para revisarlos o cambiarlos y correrlos a mano), copiarlos o cancelar.
/// </summary>
public sealed class ScriptPreviewWindow : Window
{
    public ScriptPreviewWindow(ScriptPreview preview)
    {
        Title = preview.Title;
        Width = 900;
        Height = 560;
        MinWidth = 600;
        MinHeight = 360;
        CanMinimize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var editor = new TextEditor
        {
            Text = preview.Script,
            IsReadOnly = true,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            FontSize = 13,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        _ = new SqlEditorSetup(editor, () => MetadataCatalog.Empty);

        Button Choice(string text, ScriptPreviewChoice choice, bool primary = false)
        {
            var b = new Button { Content = text, IsDefault = primary, IsCancel = choice == ScriptPreviewChoice.Cancel };
            if (primary)
            {
                b.Classes.Add("Primary");
                b[!ThemeProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SolidButton");
            }
            b.Click += (_, _) => Close(choice);
            return b;
        }
        var copy = new Button { Content = Strings.DataPreviewCopy };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(preview.Script);
            copy.Content = Strings.DataPreviewCopied;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(copy);
        buttons.Children.Add(Choice(Strings.DataPreviewOpenTab, ScriptPreviewChoice.OpenInTab));
        buttons.Children.Add(Choice(Strings.DataPreviewRun, ScriptPreviewChoice.Run, primary: true));
        buttons.Children.Add(Choice(Strings.Cancel, ScriptPreviewChoice.Cancel));

        var top = new StackPanel { Spacing = 6 };
        foreach (var warning in preview.Warnings)
            top.Children.Add(new TextBlock { Text = "⚠ " + warning, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, [!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FbsWarningBrush") });
        top.Children.Add(new TextBlock { Text = Strings.DataPreviewNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 });

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        top.Margin = new Thickness(0, 0, 0, 8);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SemiColorBorder"),
            Child = editor,
        });
        Content = root;
    }
}
