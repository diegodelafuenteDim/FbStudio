using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;
using FbStudio.Core.Export;

namespace FbStudio.App.Views;

/// <summary>Pestaña de trace: opciones arriba, el análisis por consulta y los eventos en grillas, el detalle abajo.</summary>
public partial class TraceTabView : UserControl
{
    private static readonly FilePickerFileType LogFiles = new(Strings.TraceLogFiles) { Patterns = ["*.log", "*.txt", "*"] };
    private static readonly FilePickerFileType ExcelFiles = new("Excel") { Patterns = ["*.xlsx"] };

    public TraceTabView()
    {
        InitializeComponent();
        AnalysisGrid.SelectionChanged += () => (DataContext as TraceTabViewModel)?.SelectGroup(AnalysisGrid.CurrentRow);
        EventsGrid.SelectionChanged += () => (DataContext as TraceTabViewModel)?.SelectEvent(EventsGrid.CurrentRow);
        OpenLogButton.Click += async (_, _) => await OpenLogAsync();
        SaveLogButton.Click += async (_, _) => await SaveLogAsync();
        ExportAnalysisButton.Click += async (_, _) => await ExportAnalysisAsync();
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

    /// <summary>«Exportar análisis a Excel…»: las consultas como se ven en la grilla (con su orden y sus filtros).</summary>
    private async Task ExportAnalysisAsync()
    {
        if (DataContext is not TraceTabViewModel trace) return;
        if (AnalysisGrid.VisibleRowIndexes().Count == 0)
        {
            trace.Status = Strings.TraceNothingToExport;
            return;
        }
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.TraceExportAnalysis.TrimEnd('…'),
            SuggestedFileName = $"fbstudio-trace-{DateTime.Now:yyyyMMdd-HHmm}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices = [ExcelFiles],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try { ExportAnalysisTo(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { trace.Status = string.Format(Strings.TraceFailed, ex.Message); }
    }

    /// <summary>Escribe el análisis (las filas que se ven, en su orden) en un .xlsx y lo avisa; devuelve cuántas consultas.</summary>
    internal long ExportAnalysisTo(string path)
    {
        var rows = AnalysisGrid.VisibleRowIndexes().Select(i => AnalysisGrid.Rows[i]);
        var count = new XlsxExporter().Write(path, AnalysisGrid.Columns, rows);
        if (DataContext is TraceTabViewModel trace) trace.Status = string.Format(Strings.TraceAnalysisExported, count, path);
        return count;
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
