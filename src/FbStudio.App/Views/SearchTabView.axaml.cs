using Avalonia.Controls;
using Avalonia.Input;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

public partial class SearchTabView : UserControl
{
    public SearchTabView()
    {
        InitializeComponent();
        SearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || Tab is not { } tab) return;
            e.Handled = true;
            await tab.SearchAsync();
        };
        HitsList.DoubleTapped += async (_, _) => await ActivateSelectedAsync();
        HitsList.KeyDown += async (_, e) =>
        {
            if (HitsList.SelectedItem is not SearchListItem item || Tab is not { } tab) return;
            if (e.Key == Key.Enter) { e.Handled = true; await tab.ActivateAsync(item); }
            else if (e.Key == Key.Right && !item.IsExpanded && item.Children.Count > 0) { e.Handled = true; tab.Toggle(item); }
            else if (e.Key == Key.Left && item.IsExpanded) { e.Handled = true; tab.Toggle(item); }
        };
        // Clic en el triángulo: despliega o pliega sin abrir.
        HitsList.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is TextBlock { Text: "▸" or "▾", DataContext: SearchListItem item } && Tab is { } tab)
            {
                e.Handled = true;
                tab.Toggle(item);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public SearchTabViewModel? Tab => DataContext as SearchTabViewModel;

    public void FocusSearchBox() => SearchBox.Focus();

    private Task ActivateSelectedAsync() =>
        HitsList.SelectedItem is SearchListItem item && Tab is { } tab ? tab.ActivateAsync(item) : Task.CompletedTask;
}
