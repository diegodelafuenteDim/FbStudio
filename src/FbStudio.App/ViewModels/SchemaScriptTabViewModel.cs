using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using Material.Icons;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Pestaña «Script de la base» (Extract Metadata): lee todos los objetos en una transacción de solo lectura y arma el
/// script para recrear la base en una vacía. Se guarda o se copia; no se ejecuta sobre la misma base (crearía objetos
/// que ya existen y, siguiendo ante errores, reemplazaría procedimientos por sus firmas vacías).
/// </summary>
public sealed partial class SchemaScriptTabViewModel : EditorTabViewModel
{
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public SchemaScriptTabViewModel(MainWindowViewModel owner, ConnectionProfile profile, SchemaScriptOptions options)
        : base(owner, profile, Strings.SchemaScriptTabTitle)
    {
        Options = options;
        Status = Strings.SchemaScriptIntro;
    }

    public SchemaScriptOptions Options { get; private set; }

    public override MaterialIconKind Icon => MaterialIconKind.ScriptTextOutline;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelGenerateCommand), nameof(OpenWithoutDatabaseCommand))]
    [NotifyPropertyChangedFor(nameof(HasScript))]
    private bool _isGenerating;

    /// <summary>«Generado a las 16:50 · 4.123 objetos».</summary>
    [ObservableProperty] private string _generatedAt = "";

    public bool HasScript => !IsGenerating && Document.TextLength > 0;

    /// <summary>La última generación (para las pruebas).</summary>
    public Task Generating { get; private set; } = Task.CompletedTask;

    public Task GenerateAsync(SchemaScriptOptions? options = null)
    {
        if (IsGenerating) return Generating;
        if (options is not null) Options = options;
        return Generating = GenerateCoreAsync();
    }

    private async Task GenerateCoreAsync()
    {
        if (_disposed || Profile is not { } profile) return;
        if (Owner.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            Status = string.Format(Strings.NoSavedPassword, profile.Name);
            return;
        }
        IsGenerating = true;
        _cts = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var shown = Stopwatch.StartNew();
        Status = Strings.SchemaScriptReading;
        try
        {
            // Un explorador propio: la lectura puede tardar y el del árbol queda libre para lo demás.
            await using var explorer = new MetadataExplorer(profile, password);
            var progress = new Progress<SchemaProgress>(p =>
            {
                if (!IsGenerating || (shown.ElapsedMilliseconds < 150 && p.Done < p.Total)) return;
                shown.Restart();
                Status = string.Format(Strings.SchemaScriptProgress, p.Done, p.Total, p.Current);
            });
            var schema = await explorer.ReadSchemaAsync(Options, progress, _cts.Token);
            if (_disposed) return;
            var header = SchemaScriptWriter.Header(profile.Name, $"{profile.Host}:{profile.Database}", DateTime.Now, schema.Charset);
            Document.Text = SchemaScriptWriter.Write(schema, Options, header);
            var objects = schema.Tables.Count + schema.Views.Count + schema.Procedures.Count + schema.Functions.Count + schema.Packages.Count
                          + schema.Domains.Count + schema.Generators.Count + schema.Exceptions.Count + schema.Roles.Count
                          + schema.Tables.Sum(t => t.Triggers.Count) + schema.Views.Sum(v => v.Triggers.Count) + schema.DatabaseTriggers.Count;
            GeneratedAt = string.Format(Strings.SchemaScriptGeneratedAt, DateTime.Now, objects);
            Status = string.Format(Strings.SchemaScriptDone, objects, Document.LineCount, clock.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            Status = Strings.SchemaScriptCancelled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.SchemaScriptFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        finally
        {
            IsGenerating = false;
            _cts?.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(HasScript));
            OpenWithoutDatabaseCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    private void CancelGenerate() => _cts?.Cancel();

    /// <summary>
    /// El script a una pestaña de consulta SIN base: se elige en el árbol la base vacía donde correrlo (doble clic).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasScript))]
    private void OpenWithoutDatabase()
    {
        var tab = Owner.OpenScriptWithoutDatabase(string.Format(Strings.SchemaScriptQueryTitle, Profile?.Name), Document.Text);
        tab.Status = Strings.SchemaScriptPickTarget;
    }

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        _cts?.Cancel();
        await base.DisposeAsync();
    }
}
