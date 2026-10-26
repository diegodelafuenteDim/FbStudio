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
        DocumentationButton.Click += async (_, _) => await SaveDocumentationAsync();
        RegenerateButton.Click += async (_, _) =>
        {
            if (DataContext is not SchemaScriptTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner) return;
            // Con las áreas de la base, como la primera vez (si no, al volver a generar se perdía el área elegida).
            var areas = tab.Profile is { } profile ? tab.Owner.AreaStore.Areas(profile).Select(a => a.Name).ToList() : [];
            if (await new SchemaScriptDialog(tab.Profile?.Name ?? "", tab.Options, areas).ShowDialog<Core.Metadata.SchemaScriptOptions?>(owner) is { } options)
                await tab.GenerateAsync(options);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SchemaScriptTabViewModel tab) ScriptView.Document = tab.Document;
    }

    /// <summary>
    /// «Documentación HTML…»: con o sin el código PSQL, dónde guardarla, y se abre en el navegador. Sale de lo que ya se
    /// leyó (el área elegida incluida): no vuelve a leer la base.
    /// </summary>
    public async Task SaveDocumentationAsync()
    {
        if (DataContext is not SchemaScriptTabViewModel { Schema: not null } tab || TopLevel.GetTopLevel(this) is not Window owner) return;
        var choice = await new ChoiceDialog(Strings.DocButton.TrimEnd('…'), Strings.DocSourcesQuestion, Strings.DocWithoutSources, Strings.DocWithSources).ShowDialog<int>(owner);
        if (choice < 0) return;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.DocButton.TrimEnd('…'),
            SuggestedFileName = $"{tab.Profile?.Name ?? "base"}-documentacion-{DateTime.Now:yyyyMMdd}.html",
            DefaultExtension = "html",
            FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, tab.DocumentationHtml(includeSources: choice == 1), new System.Text.UTF8Encoding(false));
            tab.Status = string.Format(Strings.DocSaved, path);
            await owner.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tab.Status = string.Format(Strings.ErrorStatus, ex.Message);
        }
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
