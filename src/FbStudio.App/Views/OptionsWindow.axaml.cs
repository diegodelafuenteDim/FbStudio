using Avalonia.Controls;
using FbStudio.App.ViewModels;
using FbStudio.Workspace;

namespace FbStudio.App.Views;

/// <summary>Opciones del sistema. Devuelve las opciones nuevas con «Aceptar», o null.</summary>
public partial class OptionsWindow : Window
{
    public OptionsWindow() : this(new OptionsViewModel(new AppSettings())) { }

    public OptionsWindow(OptionsViewModel options)
    {
        InitializeComponent();
        DataContext = options;
        CanMinimize = false;
        ShowInTaskbar = false;
        OkButton.Click += (_, _) => Close(options.ToSettings());
        CancelButton.Click += (_, _) => Close(null);
        CopyAssistantsConfigButton.Click += async (_, _) =>
        {
            if (GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, options.AssistantsConfig);
            AssistantsCopied.Text = FbStudio.App.Resources.Strings.AssistantsCopied;
        };
    }

    public OptionsViewModel Options => (OptionsViewModel)DataContext!;
}
