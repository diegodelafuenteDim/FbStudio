using FbStudio.App.Views;
using FbStudio.Core.Profiles;

namespace FbStudio.App.Tests;

/// <summary>El formulario de registros: el cifrado de la conexión (D-73).</summary>
[Collection(nameof(UiCollection))]
public sealed class ProfilesWindowTests(HeadlessFixture ui) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fbstudio-profiles-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public Task Elegir_produccion_pasa_el_cifrado_a_obligatorio() => ui.Session.DispatchAsync(() =>
    {
        var store = AppServices.CreateProfileStore(_folder);
        var w = new ProfilesWindow(store, null, startWithNew: true);
        w.Show();
        Assert.Equal(WireCryptMode.Enabled, w.WireCryptBox.SelectedItem);
        Assert.Equal("Obligatorio (Required)", ProfilesWindow.WireCryptLabel(WireCryptMode.Required));

        w.EnvironmentBox.SelectedItem = ProfileEnvironment.Produccion;
        Assert.Equal(WireCryptMode.Required, w.WireCryptBox.SelectedItem);
        Assert.StartsWith("En producción el cifrado pasa a «Obligatorio (Required)»", w.TestResult.Text);
        w.Close();

        // Un registro de producción guardado con «Enabled» se muestra como está: no se cambia solo al abrirlo.
        var old = new ConnectionProfile { Name = "Vieja", Host = "srv", Database = "base", User = "U", Environment = ProfileEnvironment.Produccion };
        store.Upsert(old, null);
        var again = new ProfilesWindow(store, old.Id);
        again.Show();
        Assert.Equal(ProfileEnvironment.Produccion, again.EnvironmentBox.SelectedItem);
        Assert.Equal(WireCryptMode.Enabled, again.WireCryptBox.SelectedItem);
        again.Close();
        return Task.CompletedTask;
    }, CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }
}
