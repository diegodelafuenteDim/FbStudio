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
    }

    public OptionsViewModel Options => (OptionsViewModel)DataContext!;
}
