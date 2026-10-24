using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña «Comparar bases»: diferencias a la izquierda; a la derecha, la elegida (origen y destino) y el script.</summary>
public partial class CompareTabView : UserControl
{
    private CompareTabViewModel? _wired;

    public CompareTabView()
    {
        InitializeComponent();
        SaveButton.Click += async (_, _) => await SaveAsync();
        RecompareButton.Click += async (_, _) =>
        {
            if (DataContext is CompareTabViewModel tab) await tab.CompareAsync();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_wired is not null) _wired.PropertyChanged -= OnTabChanged;
        _wired = DataContext as CompareTabViewModel;
        if (_wired is null) return;
        _wired.PropertyChanged += OnTabChanged;
        ShowSelected();
    }

    // Los editores de AvaloniaEdit no se enlazan por texto: se cargan cuando cambia la diferencia elegida o el script.
    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CompareTabViewModel.SelectedItem)) ShowSelected();
        else if (e.PropertyName is nameof(CompareTabViewModel.Script) && _wired is not null) ScriptView.Text = _wired.Script;
    }

    private void ShowSelected()
    {
        if (_wired is null) return;
        SourceView.Text = _wired.SelectedSourceDdl;
        TargetView.Text = _wired.SelectedTargetDdl;
    }

    private async Task SaveAsync()
    {
        if (_wired is not { Script.Length: > 0 } tab || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.CompareSaveTitle,
            SuggestedFileName = $"igualar-{tab.Target.Name}-{DateTime.Now:yyyyMMdd-HHmm}.sql",
            DefaultExtension = "sql",
            FileTypeChoices = [new FilePickerFileType(Strings.ExportSqlScript) { Patterns = ["*.sql"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, tab.Script.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            tab.Status = string.Format(Strings.SchemaScriptSaved, path);
        }
        catch (Exception ex)
        {
            tab.Status = string.Format(Strings.ErrorStatus, ex.Message);
        }
    }
}
