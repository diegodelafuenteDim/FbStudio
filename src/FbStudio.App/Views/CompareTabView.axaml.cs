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

    // Lo que cambia, renglón por renglón: verde lo que el origen agrega, rojo lo que el destino pierde.
    private readonly Editor.LineHighlighter _sourceChanges = new(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(70, 46, 160, 67)));
    private readonly Editor.LineHighlighter _targetChanges = new(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(70, 218, 54, 51)));

    /// <summary>Los renglones marcados en el DDL del origen y del destino (desde 1; para las pruebas).</summary>
    public IReadOnlyCollection<int> SourceChangedLines => _sourceChanges.Lines;
    public IReadOnlyCollection<int> TargetChangedLines => _targetChanges.Lines;

    public CompareTabView()
    {
        InitializeComponent();
        SourceView.TextArea.TextView.BackgroundRenderers.Add(_sourceChanges);
        TargetView.TextArea.TextView.BackgroundRenderers.Add(_targetChanges);
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
        // Solo si está en las dos (lo nuevo o lo que sobra es todo distinto: no hace falta marcarlo).
        var both = _wired.SelectedItem?.Difference is { SourceDdl: not null, TargetDdl: not null };
        var diff = both ? Core.Metadata.LineDiff.Compute(SourceView.Text, TargetView.Text) : [];
        _sourceChanges.SetLines(SourceView.TextArea.TextView, diff.Where(d => d.Kind == Core.Metadata.DiffKind.OnlyInA).Select(d => d.LineA + 1));
        _targetChanges.SetLines(TargetView.TextArea.TextView, diff.Where(d => d.Kind == Core.Metadata.DiffKind.OnlyInB).Select(d => d.LineB + 1));
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
