using Avalonia.Controls;
using Avalonia.Threading;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de datos de prueba: qué va en cada columna a la izquierda, la muestra a la derecha, el registro abajo.</summary>
public partial class TestDataTabView : UserControl
{
    public TestDataTabView()
    {
        InitializeComponent();
        PreviewTitle.Text = string.Format(Strings.TestDataPreviewTitle, TestDataTabViewModel.PreviewRows);
    }

    public ResultGrid.DrawnGrid Preview => PreviewGrid;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not TestDataTabViewModel tab) return;
        LogView.Document = tab.Document;
        tab.PreviewChanged += (columns, rows) => PreviewGrid.SetData(columns, rows);
        tab.Document.Changed += (_, _) => Dispatcher.UIThread.Post(() => LogView.ScrollToLine(LogView.Document.LineCount), DispatcherPriority.Background);
    }
}
