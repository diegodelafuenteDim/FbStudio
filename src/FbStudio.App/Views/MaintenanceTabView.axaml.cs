using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de mantenimiento: la tarea a la izquierda, su formulario arriba y el registro de lo que va pasando abajo.</summary>
public partial class MaintenanceTabView : UserControl
{
    public MaintenanceTabView()
    {
        InitializeComponent();
        CopyButton.Click += async (_, _) =>
        {
            if (DataContext is not MaintenanceTabViewModel tab || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(tab.Document.Text);
            tab.Status = Strings.MaintLogCopied;
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MaintenanceTabViewModel tab) return;
        LogView.Document = tab.Document;
        // Lo último a la vista mientras llega la salida del servidor (después de medir: antes no sabe la altura).
        var queued = false;
        tab.Document.Changed += (_, _) =>
        {
            if (queued) return;
            queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                queued = false;
                LogView.ScrollToLine(LogView.Document.LineCount);
            }, DispatcherPriority.Background);
        };
    }
}
