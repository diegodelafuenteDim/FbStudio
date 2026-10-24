using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de diagrama: las opciones arriba, el lienzo al centro, la tabla elegida y las escondidas a la derecha.</summary>
public partial class DiagramTabView : UserControl
{
    private static readonly FilePickerFileType Png = new(Strings.DiagramFilePng) { Patterns = ["*.png"] };
    private static readonly FilePickerFileType Svg = new(Strings.DiagramFileSvg) { Patterns = ["*.svg"] };
    private static readonly FilePickerFileType Drawio = new(Strings.DiagramFileDrawio) { Patterns = ["*.drawio"] };

    public DiagramTabView()
    {
        InitializeComponent();
        FitButton.Click += (_, _) => Canvas.FitToView();
        ExportButton.Click += async (_, _) => await ExportAsync();
        Canvas.SelectionChanged += node =>
        {
            if (Tab is { } tab) tab.SelectedNode = node;
        };
        Canvas.NodeActivated += async node =>
        {
            if (Tab is { } tab) await tab.OpenAsync(node);
        };
    }

    private DiagramTabViewModel? Tab => DataContext as DiagramTabViewModel;

    public DiagramCanvas DiagramCanvas => Canvas;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Tab is not { } tab) return;
        Canvas.Scene = tab.Scene;
        tab.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DiagramTabViewModel.Scene)) Canvas.Scene = tab.Scene;
        };
        // Después de acomodar, todo a la vista (cuando el lienzo ya tiene tamaño).
        tab.Rearranged += () => Dispatcher.UIThread.Post(() =>
        {
            Canvas.InvalidateVisual();
            Canvas.FitToView();
        }, DispatcherPriority.Background);
    }

    private void ShowHub_Click(object? sender, RoutedEventArgs e)
    {
        if (Tab is { } tab && (sender as Control)?.Tag is HiddenHubRow row) tab.ShowHub(row);
    }

    private async Task ExportAsync()
    {
        if (Tab is not { } tab || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.DiagramExport.TrimEnd('…'),
            SuggestedFileName = string.Format(Strings.DiagramFileName, tab.Source.Name),
            DefaultExtension = "png",
            FileTypeChoices = [Png, Svg, Drawio],
        });
        if (file?.TryGetLocalPath() is { } path) tab.Export(path);
    }
}
