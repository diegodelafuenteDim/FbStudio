using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de importación: el archivo arriba, qué columna va a cada una a la izquierda, la vista previa a la derecha.</summary>
public partial class ImportTabView : UserControl
{
    private static readonly FilePickerFileType Files = new(Strings.ImportFileTypes) { Patterns = ["*.csv", "*.txt", "*.xlsx"] };

    public ImportTabView()
    {
        InitializeComponent();
        PreviewTitle.Text = string.Format(Strings.ImportPreview, ImportTabViewModel.PreviewRows);
        ChooseFileButton.Click += async (_, _) => await ChooseFileAsync();
    }

    public ResultGrid.DrawnGrid Preview => PreviewGrid;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ImportTabViewModel import) return;
        LogView.Document = import.Document;
        import.PreviewChanged += (columns, rows) => PreviewGrid.SetData(columns, rows);
        // Lo último del registro a la vista.
        import.Document.Changed += (_, _) => Dispatcher.UIThread.Post(() => LogView.ScrollToLine(LogView.Document.LineCount), DispatcherPriority.Background);
    }

    private async Task ChooseFileAsync()
    {
        if (DataContext is not ImportTabViewModel import || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Strings.ImportChooseFile.TrimEnd('…'), FileTypeFilter = [Files] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) import.OpenFile(path);
    }
}
