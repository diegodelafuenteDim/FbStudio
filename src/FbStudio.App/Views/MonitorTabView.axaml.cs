using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de monitoreo: contadores arriba, consulta elegida en la grilla y el SQL de la sentencia elegida abajo.</summary>
public partial class MonitorTabView : UserControl
{
    public MonitorTabView()
    {
        InitializeComponent();
        MonitorGrid.SelectionChanged += () => { if (DataContext is MonitorTabViewModel m) m.SelectedRow = MonitorGrid.CurrentRow; };
        QueryList.DoubleTapped += async (_, _) => { if (DataContext is MonitorTabViewModel m && m.EditQueryCommand.CanExecute(null)) await m.EditQueryCommand.ExecuteAsync(null); };
        QueryList.ContextRequested += OnQueryListContext;
        // Clic en una tarjeta: la consulta que explica ese número.
        OatCard.PointerPressed += (_, _) => (DataContext as MonitorTabViewModel)?.ShowPreset("transactions");
        LocksCard.PointerPressed += (_, _) => (DataContext as MonitorTabViewModel)?.ShowPreset("locks");
        ExportQueries.Click += async (_, _) => await ExportAsync();
        ImportQueries.Click += async (_, _) => await ImportAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MonitorTabViewModel m) return;
        // Cada refresco trae filas nuevas: con la misma consulta se conservan orden, filtros, anchos y la fila elegida.
        m.ResultsChanged += (columns, rows, sameQuery) =>
        {
            MonitorGrid.SetData(columns, rows, keepView: sameQuery);
            m.SelectedRow = MonitorGrid.CurrentRow;
        };
    }

    public ResultGrid.DrawnGrid Grid => MonitorGrid;

    /// <summary>Clic derecho en la lista de consultas: elige la de abajo del puntero y ofrece lo mismo que los botones.</summary>
    private void OnQueryListContext(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MonitorTabViewModel m) return;
        if ((e.Source as Avalonia.StyledElement)?.DataContext is MonitorQueryItem { Preset: not null } item) m.SelectedItem = item;
        var name = m.SelectedPreset.Name;
        MenuItem Item(string header, Func<Task> action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += async (_, _) => await action();
            return mi;
        }
        var items = new List<Control>
        {
            Item(Strings.MonNewQuery, () => m.NewQueryCommand.ExecuteAsync(false)),
            Item(string.Format(Strings.MonCloneQuery, name), () => m.NewQueryCommand.ExecuteAsync(true)),
            Item(string.Format(Strings.MonEditQueryNamed, name), () => m.EditQueryCommand.ExecuteAsync(null), m.EditQueryCommand.CanExecute(null)),
            Item(string.Format(Strings.MonDeleteQueryNamed, name), () => m.DeleteQueryCommand.ExecuteAsync(null), m.DeleteQueryCommand.CanExecute(null)),
            new Separator(),
            Item(Strings.MonExportQueries, ExportAsync),
            Item(Strings.MonImportQueries, ImportAsync),
        };
        new ContextMenu { ItemsSource = items }.Open(QueryList);
        e.Handled = true;
    }

    private static readonly FilePickerFileType JsonFiles = new("JSON") { Patterns = ["*.json"] };

    private async Task ExportAsync()
    {
        if (DataContext is not MonitorTabViewModel m || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.MonExportQueries,
            SuggestedFileName = "fbstudio-monitoreo.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonFiles],
        });
        if (file?.TryGetLocalPath() is { } path) m.ExportQueries(path);
    }

    private async Task ImportAsync()
    {
        if (DataContext is not MonitorTabViewModel m || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Strings.MonImportQueries, FileTypeFilter = [JsonFiles] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) m.ImportQueries(path);
    }
}
