using Avalonia.Controls;
using Avalonia.Input.Platform;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>
/// Pestaña de estadísticas: tarjetas con lo importante, todos los datos del encabezado, las tablas e índices, la
/// comparación con otra base y el texto de gstat.
/// </summary>
public partial class StatisticsTabView : UserControl
{
    public StatisticsTabView()
    {
        InitializeComponent();
        CopyButton.Click += async (_, _) =>
        {
            if (DataContext is not StatisticsTabViewModel stats || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(stats.Document.Text);
            stats.Status = Strings.StatsCopied;
        };
        // Las tablas de la base se leen la primera vez que se abre la lista.
        TablesButton.Flyout!.Opened += (_, _) => _ = (DataContext as StatisticsTabViewModel)?.LoadTablesAsync();
    }

    public ResultGrid.DrawnGrid Tables => TablesGrid;
    public ResultGrid.DrawnGrid Indexes => IndexesGrid;
    public ResultGrid.DrawnGrid CompareTables => CompareTablesGrid;
    public ResultGrid.DrawnGrid CompareIndexes => CompareIndexesGrid;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not StatisticsTabViewModel stats) return;
        OutputView.Document = stats.Document;
        stats.AnalysisChanged += (tableColumns, tableRows, indexColumns, indexRows) =>
        {
            TablesGrid.SetData(tableColumns, tableRows);
            IndexesGrid.SetData(indexColumns, indexRows);
        };
        stats.ComparisonChanged += (tableColumns, tableRows, indexColumns, indexRows) =>
        {
            CompareTablesGrid.SetData(tableColumns, tableRows);
            CompareIndexesGrid.SetData(indexColumns, indexRows);
        };
    }
}
