using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FbStudio.App.ViewModels;
using FbStudio.App.Views;
using FbStudio.Core.Profiles;
using FbStudio.Firebird.Tests;
using FbStudio.Workspace;

namespace FbStudio.App.Tests;

/// <summary>«Exportar registros…» e «Importar registros…» (F-22).</summary>
[Collection(nameof(UiCollection))]
public sealed class ProfileBackupDialogTests(FirebirdFixture fb, HeadlessFixture ui) : IDisposable
{
    private const string Phrase = "frase de prueba larga";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-bkpui-" + Guid.NewGuid().ToString("N"));

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Control root, Func<T, bool> match) where T : Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().First(match);

    private static void Click(Window w, string text) =>
        Find<Button>(w, b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<string?> Texts(Control root) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<TextBlock>().Select(t => t.Text).ToList();

    private MainWindowViewModel Vm(string name, params ConnectionProfile[] profiles)
    {
        var folder = Path.Combine(_folder, name);
        var store = AppServices.CreateProfileStore(folder);
        foreach (var p in profiles) store.Upsert(p, "clave-" + p.Name);
        return new MainWindowViewModel(store, null, AppServices.CreateSettingsStore(folder));
    }

    private static ConnectionProfile P(string name, string folder) => new()
    {
        Name = name, Host = "srv.example", Database = name.ToLowerInvariant(), User = "SYSDBA", Folder = folder, Environment = ProfileEnvironment.Prueba,
    };

    [Fact]
    public Task Exportar_valida_la_frase_y_sin_usuario_no_ofrece_contrasenas() => ui.Session.DispatchAsync(async () =>
    {
        _ = fb;
        var owner = new Window();
        owner.Show();
        var dialog = new ProfileExportDialog([P("Clinica", "Clientes"), P("Stock", "Clientes")]);
        var result = dialog.ShowDialog<ProfileExportChoice?>(owner);
        Pump();
        Assert.Equal("Exportar registros", dialog.Title);

        Click(dialog, "Ninguno");
        Click(dialog, "Exportar…");
        Pump();
        Assert.Contains("Elegí al menos un registro.", Texts(dialog));
        Click(dialog, "Todos");

        dialog.IncludePasswords.IsChecked = true;
        dialog.Phrase.Text = "corta";
        Click(dialog, "Exportar…");
        Pump();
        Assert.Contains(Texts(dialog), t => t?.Contains("al menos 10 caracteres") == true);
        dialog.Phrase.Text = Phrase;
        dialog.Repeat.Text = Phrase + "x";
        Click(dialog, "Exportar…");
        Pump();
        Assert.Contains("Las dos frases no coinciden.", Texts(dialog));

        dialog.IncludeUser.IsChecked = false;                                  // sin usuario no van contraseñas
        Pump();
        Assert.False(dialog.IncludePasswords.IsEnabled);
        Assert.False(dialog.IncludePasswords.IsChecked);
        dialog.IncludeUser.IsChecked = true;
        dialog.IncludePasswords.IsChecked = true;
        dialog.Repeat.Text = Phrase;
        Assert.True(dialog.IncludeAreas.IsChecked);                            // las áreas van por defecto
        dialog.IncludeAreas.IsChecked = false;
        Click(dialog, "Exportar…");
        var choice = await result;
        Assert.NotNull(choice);
        Assert.Equal((2, true, Phrase, false), (choice.ProfileIds.Count, choice.IncludeUser, choice.Passphrase, choice.IncludeAreas));
        owner.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Desde_una_carpeta_vienen_marcadas_solo_las_suyas() => ui.Session.DispatchAsync(() =>
    {
        var (clinica, stock) = (P("Clinica", "Clientes"), P("Stock", "Otras"));
        var dialog = new ProfileExportDialog([clinica, stock], [clinica.Id]);
        dialog.Show();
        Pump();
        var checks = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<CheckBox>().Where(c => c.Tag is Guid).ToList();
        Assert.Equal([true, false], checks.Select(c => c.IsChecked == true));
        Assert.Contains(Texts(dialog), t => t?.StartsWith("Incluir mi usuario de cada conexión") == true);
        dialog.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);

    [Fact]
    public Task Exporta_con_contrasenas_e_importa_en_otra_PC_con_la_frase() => ui.Session.DispatchAsync(async () =>
    {
        var origin = Vm("origen", P("Clinica", "Clientes"), P("Stock", "Clientes"));
        var path = Path.Combine(_folder, "registros.json");
        origin.ExportProfiles(path, origin.Profiles.Select(p => p.Id).ToList(), includeUser: true, Phrase);
        Assert.StartsWith("Registros exportados: 2 (2 con contraseña, cifrada)", origin.Tree.Status);

        var file = ProfileBackup.Read(path);
        var owner = new Window();
        owner.Show();
        var ask = new ProfilePhraseDialog(file);
        var opened = ask.ShowDialog<ProfileBackupContent?>(owner);
        Pump();
        ask.Phrase.Text = "no es esta frase";
        Click(ask, "Aceptar");
        Pump();
        Assert.Contains("La frase no es correcta, o el archivo fue modificado.", Texts(ask));
        ask.Phrase.Text = Phrase;
        Click(ask, "Aceptar");
        var content = await opened;
        Assert.NotNull(content);

        // La otra PC ya tiene «Stock»: se avisa y por defecto queda como estaba.
        var target = Vm("destino", P("Stock", "Mío"));
        var plan = target.PlanImport(content);
        var confirm = new ProfileImportDialog(plan, savesPasswords: true);
        var chosen = confirm.ShowDialog<ProfileImportChoice?>(owner);
        Pump();
        Assert.Contains("2 registros en el archivo · nuevos: 1 · ya registrados: 1.", Texts(confirm));
        Assert.Contains("Ya registrados: Stock.", Texts(confirm));
        Assert.True(confirm.Skip.IsChecked);
        Click(confirm, "Importar");
        var choice = await chosen;
        Assert.NotNull(choice);
        target.ImportProfiles(plan, choice.UpdateExisting, choice.User, choice.Password);

        Assert.Equal("Registros importados · nuevos: 1 · actualizados: 0 · sin tocar: 1.", target.Tree.Status);
        var clinica = Assert.Single(target.Profiles, p => p.Name == "Clinica");
        Assert.Equal(("Clientes", ProfileEnvironment.Prueba), (clinica.Folder, clinica.Environment));
        Assert.Equal("clave-Clinica", target.ProfileStore.GetPassword(clinica.Id));
        Assert.Equal("Mío", target.Profiles.Single(p => p.Name == "Stock").Folder);
        owner.Close();
        await origin.DisposeAsync();
        await target.DisposeAsync();
    }, CancellationToken.None);

    [Fact]
    public Task Sin_usuario_pide_usuario_y_contrasena_para_los_nuevos() => ui.Session.DispatchAsync(async () =>
    {
        var origin = Vm("origen2", P("Clinica", "Clientes"));
        var path = Path.Combine(_folder, "sin-usuario.json");
        origin.ExportProfiles(path, origin.Profiles.Select(p => p.Id).ToList(), includeUser: false, null);
        var content = ProfileBackup.Open(ProfileBackup.Read(path), null);

        var target = Vm("colega");
        var plan = target.PlanImport(content);
        var owner = new Window();
        owner.Show();
        var confirm = new ProfileImportDialog(plan, savesPasswords: true);
        var chosen = confirm.ShowDialog<ProfileImportChoice?>(owner);
        Pump();
        Assert.Contains(Texts(confirm), t => t?.StartsWith("Sin usuario: 1.") == true);
        confirm.User.Text = "JPEREZ";
        confirm.Password.Text = "suya";
        Click(confirm, "Importar");
        var choice = (await chosen)!;
        target.ImportProfiles(plan, choice.UpdateExisting, choice.User, choice.Password);

        var p = Assert.Single(target.Profiles);
        Assert.Equal("JPEREZ", p.User);
        Assert.Equal("suya", target.ProfileStore.GetPassword(p.Id));
        owner.Close();
        await origin.DisposeAsync();
        await target.DisposeAsync();
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
