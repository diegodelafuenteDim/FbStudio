using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Profiles;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Una base conectada que no es la de la pestaña elegida (F-21): en la franja, al lado de la base de la pestaña, con el
/// color de su entorno. Un clic lleva a su pestaña (o abre una consulta en ella), sin cambiar la base de la actual.
/// </summary>
public sealed class ConnectedChip(ConnectionProfile profile, Action<ConnectionProfile> goTo)
{
    public ConnectionProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Tip => string.Format(Strings.BannerOtherTip, ProfileEnvironments.Label(Profile.Environment), Profile.Display);
    public bool IsDev => Profile.Environment == ProfileEnvironment.Desarrollo;
    public bool IsTest => Profile.Environment == ProfileEnvironment.Prueba;
    public bool IsProd => Profile.Environment == ProfileEnvironment.Produccion;
    public IRelayCommand GoTo { get; } = new RelayCommand(() => goTo(profile));
}
