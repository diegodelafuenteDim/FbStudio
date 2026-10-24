using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de áreas: la lista a la izquierda; las reglas del área elegida y lo que junta a la derecha; el archivo abajo.</summary>
public partial class AreasTabView : UserControl
{
    private static readonly FilePickerFileType Files = new(Strings.AreasFileTypes) { Patterns = ["*.json"] };

    public AreasTabView()
    {
        InitializeComponent();
        UseFileButton.Click += async (_, _) => await UseFileAsync();
        SaveAsButton.Click += async (_, _) => await SaveAsAsync();
        PreviewList.DoubleTapped += async (_, _) =>
        {
            if (Tab is { } tab && PreviewList.SelectedItem is AreaObjectRow row) await tab.OpenObjectAsync(row);
        };
        PreviewList.ContextRequested += (_, e) =>
        {
            if (BuildPreviewMenu(e.Source) is { } menu)
            {
                menu.Open(PreviewList);
                e.Handled = true;
            }
        };
        PreviewList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Tab is { } tab && PreviewList.SelectedItem is AreaObjectRow row)
            {
                e.Handled = true;
                await tab.OpenObjectAsync(row);
            }
        };
    }

    private AreasTabViewModel? Tab => DataContext as AreasTabViewModel;

    public ListBox AreaListBox => AreaList;
    public ListBox PreviewListBox => PreviewList;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Tab is not { } tab) return;
        // Un área nueva se nombra enseguida.
        tab.NameRequested += () => Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    /// <summary>Menú de los objetos elegidos: abrir, agregarlos a un área, sacarlos de la que se está viendo.</summary>
    internal ContextMenu? BuildPreviewMenu(object? source)
    {
        if (Tab is not { } tab) return null;
        // El clic derecho sobre uno que no estaba elegido lo elige (y solo a él).
        if ((source as Control)?.DataContext is AreaObjectRow clicked && !PreviewList.SelectedItems!.Contains(clicked)) PreviewList.SelectedItem = clicked;
        var rows = PreviewList.SelectedItems!.OfType<AreaObjectRow>().ToList();
        if (rows.Count == 0) return null;
        var items = new List<Control>();
        if (rows.Count == 1)
        {
            var open = new MenuItem { Header = Strings.AreasOpenObject };
            open.Click += async (_, _) => await tab.OpenObjectAsync(rows[0]);
            items.Add(open);
        }
        var targets = tab.AddTargets;
        if (targets.Count > 0)
        {
            var add = new MenuItem { Header = Strings.AreasAddTo };
            add.ItemsSource = targets.Select(target =>
            {
                var mi = new MenuItem { Header = target.Name };
                mi.Click += (_, _) => tab.AddTo(target, rows);
                return mi;
            }).ToList();
            items.Add(add);
        }
        if (tab.HasEditableArea)
        {
            var remove = new MenuItem { Header = tab.RemoveFromCaption };
            remove.Click += (_, _) => tab.RemoveFromSelected(rows);
            items.Add(remove);
        }
        return new ContextMenu { ItemsSource = items };
    }

    private async Task UseFileAsync()
    {
        if (Tab is not { } tab || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Strings.AreasUseFile.TrimEnd('…'), FileTypeFilter = [Files] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await tab.UseFileAsync(path);
    }

    private async Task SaveAsAsync()
    {
        if (Tab is not { } tab || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.AreasSaveAs.TrimEnd('…'),
            SuggestedFileName = string.Format(Strings.AreasSuggestedFileName, tab.Profile?.Name),
            DefaultExtension = "json",
            FileTypeChoices = [Files],
        });
        if (file?.TryGetLocalPath() is { } path) await tab.SaveAsAsync(path);
    }
}
