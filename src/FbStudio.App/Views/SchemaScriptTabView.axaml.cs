using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña del script de la base: el script (solo lectura) para guardarlo, copiarlo o llevarlo a una pestaña sin base.</summary>
public partial class SchemaScriptTabView : UserControl
{
    public SchemaScriptTabView()
    {
        InitializeComponent();
        CopyButton.Click += async (_, _) =>
        {
            if (DataContext is not SchemaScriptTabViewModel tab || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(tab.Document.Text);
            tab.Status = Strings.SchemaScriptCopied;
        };
        SaveButton.Click += async (_, _) => await SaveAsync();
        RegenerateButton.Click += async (_, _) =>
        {
            if (DataContext is not SchemaScriptTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner) return;
            if (await new SchemaScriptDialog(tab.Profile?.Name ?? "", tab.Options).ShowDialog<Core.Metadata.SchemaScriptOptions?>(owner) is { } options)
                await tab.GenerateAsync(options);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SchemaScriptTabViewModel tab) ScriptView.Document = tab.Document;
    }

    /// <summary>«Guardar como…»: el script en un .sql (UTF-8).</summary>
    private async Task SaveAsync()
    {
        if (DataContext is not SchemaScriptTabViewModel tab || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SchemaScriptSaveTitle,
            SuggestedFileName = $"{tab.Profile?.Name ?? "base"}-{DateTime.Now:yyyyMMdd-HHmm}.sql",
            DefaultExtension = "sql",
            FileTypeChoices = [new FilePickerFileType(Strings.ExportSqlScript) { Patterns = ["*.sql"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, tab.Document.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            tab.Status = string.Format(Strings.SchemaScriptSaved, path);
        }
        catch (Exception ex)
        {
            tab.Status = string.Format(Strings.ErrorStatus, ex.Message);
        }
    }
}
