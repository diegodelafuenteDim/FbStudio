using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de trace: opciones arriba, el análisis por consulta y los eventos en grillas, el detalle abajo.</summary>
public partial class TraceTabView : UserControl
{
    private static readonly FilePickerFileType LogFiles = new(Strings.TraceLogFiles) { Patterns = ["*.log", "*.txt", "*"] };

    public TraceTabView()
    {
        InitializeComponent();
        AnalysisGrid.SelectionChanged += () => (DataContext as TraceTabViewModel)?.SelectGroup(AnalysisGrid.CurrentRow);
        EventsGrid.SelectionChanged += () => (DataContext as TraceTabViewModel)?.SelectEvent(EventsGrid.CurrentRow);
        OpenLogButton.Click += async (_, _) => await OpenLogAsync();
        SaveLogButton.Click += async (_, _) => await SaveLogAsync();
    }

    public ResultGrid.DrawnGrid Analysis => AnalysisGrid;
    public ResultGrid.DrawnGrid EventsList => EventsGrid;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not TraceTabViewModel trace) return;
        // Cada tanda trae todo de nuevo: se conservan orden, filtros, anchos y la fila elegida.
        trace.GridsChanged += (groupColumns, groupRows, eventColumns, eventRows) =>
        {
            AnalysisGrid.SetData(groupColumns, groupRows, keepView: true);
            EventsGrid.SetData(eventColumns, eventRows, keepView: true);
        };
    }

    private async Task OpenLogAsync()
    {
        if (DataContext is not TraceTabViewModel trace || trace.IsTracing || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Strings.TraceOpenLog.TrimEnd('…'), FileTypeFilter = [LogFiles] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) trace.OpenLog(path);
    }

    private async Task SaveLogAsync()
    {
        if (DataContext is not TraceTabViewModel trace) return;
        if (!trace.HasLog)
        {
            trace.Status = Strings.TraceNothingToSave;
            return;
        }
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.TraceSaveLog.TrimEnd('…'),
            SuggestedFileName = $"fbstudio-trace-{DateTime.Now:yyyyMMdd-HHmm}.log",
            DefaultExtension = "log",
            FileTypeChoices = [LogFiles],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            try { trace.SaveLog(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { trace.Status = string.Format(Strings.TraceFailed, ex.Message); }
        }
    }
}
