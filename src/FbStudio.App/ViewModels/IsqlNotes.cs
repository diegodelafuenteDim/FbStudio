using FbStudio.App.Resources;
using FbStudio.Core.Profiles;
using FbStudio.Core.Sql;

namespace FbStudio.App.ViewModels;

/// <summary>Qué dice FbStudio de una orden de isql en un script o sola (D-120).</summary>
internal static class IsqlNotes
{
    /// <summary>El renglón del registro: qué hace FbStudio con la orden.</summary>
    /// <param name="bail">Para SET BAIL: si desde ahí el script se detiene ante un error.</param>
    public static string Note(IsqlCommand c, ConnectionProfile profile, bool bail) => c.Kind switch
    {
        IsqlKind.Bail => string.Format(bail ? Strings.IsqlBailOn : Strings.IsqlBailOff, c.Text),
        IsqlKind.End => string.Format(Strings.IsqlEnd, c.Text),
        IsqlKind.Names when SameCharset(c.Argument, profile.Charset) => string.Format(Strings.IsqlNames, c.Text, profile.Charset),
        IsqlKind.Names => string.Format(Strings.IsqlNamesDiffers, c.Text, profile.Charset),
        IsqlKind.Dialect => string.Format(Strings.IsqlDialect, c.Text),
        IsqlKind.AutoDdl => string.Format(Strings.IsqlAutoDdl, c.Text),
        // Sin lo que sigue: la ruta de una DLL no dice nada.
        IsqlKind.ClientLib => string.Format(Strings.IsqlClientLib, c.Name),
        IsqlKind.Show => string.Format(Strings.IsqlShow, c.Text),
        IsqlKind.OutputOff => string.Format(Strings.IsqlOutputOff, c.Text),
        IsqlKind.Shell => string.Format(Strings.IsqlShell, c.Name),
        IsqlKind.Input => string.Format(Strings.IsqlInput, c.Text),
        IsqlKind.Output => string.Format(Strings.IsqlOutput, c.Text),
        // Sin lo que sigue: CONNECT y CREATE DATABASE suelen llevar usuario y contraseña.
        IsqlKind.OtherDatabase => string.Format(Strings.IsqlOtherDatabase, c.Name),
        IsqlKind.OtherDialect => string.Format(Strings.IsqlOtherDialect, c.Text),
        IsqlKind.PlanOnly => string.Format(Strings.IsqlPlanOnly, c.Text),
        IsqlKind.Interactive => string.Format(Strings.IsqlInteractive, c.Name),
        _ => string.Format(Strings.IsqlDisplay, c.Text),
    };

    /// <summary>La orden sola (F9 con una sentencia, Ctrl+Enter): no va al servidor.</summary>
    public static string Alone(IsqlCommand c, ConnectionProfile profile) => c.Kind switch
    {
        IsqlKind.Bail or IsqlKind.End => string.Format(Strings.IsqlOnlyInScript, c.Text),
        _ => Note(c, profile, bail: false),
    };

    /// <summary>UTF8 y UTF-8, ISO8859_1 e iso8859_1: lo mismo. Sin argumento no se compara.</summary>
    private static bool SameCharset(string requested, string connection) =>
        requested.Length == 0
        || string.Equals(Normalize(requested), Normalize(connection), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string charset) => charset.Trim().Trim('\'', '"').Replace("-", "");
}
