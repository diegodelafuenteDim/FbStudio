using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FbStudio.Core.Profiles;

namespace FbStudio.App.Views;

public static class Converters
{
    /// <summary>Lista de registros: «Clinica · Producción» si tiene carpeta propia, si no solo el ambiente.</summary>
    public static readonly IValueConverter ProfileGroup = new FuncValueConverter<ConnectionProfile?, string>(p =>
        p is null ? "" : string.IsNullOrWhiteSpace(p.Folder)
            ? ProfileEnvironments.Label(p.Environment)
            : $"{p.Folder.Trim()} · {ProfileEnvironments.Label(p.Environment)}");

    /// <summary>Triggers e índices inactivos, bases desconectadas: atenuados.</summary>
    public static readonly IValueConverter InactiveOpacity = new FuncValueConverter<bool, double>(i => i ? 0.55 : 1.0);

    public static readonly IValueConverter BoldIfTrue = new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.Bold : FontWeight.Normal);

    /// <summary>Desarrollo / Prueba / Producción en el idioma de la interfaz.</summary>
    public static readonly IValueConverter EnvironmentLabel = new FuncValueConverter<ProfileEnvironment, string>(e => ProfileEnvironments.Label(e));

    /// <summary>Sangría del árbol aplanado.</summary>
    public static readonly IValueConverter IndentMargin = new FuncValueConverter<double, Thickness>(i => new Thickness(i, 0, 0, 0));
}
