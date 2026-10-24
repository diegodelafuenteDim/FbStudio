using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Profiles;
using FbStudio.Workspace;

namespace FbStudio.App.Views;

/// <summary>Lo elegido en «Exportar registros…»: cuáles, si van con usuario, la frase (si van con contraseñas) y si van las áreas.</summary>
public sealed record ProfileExportChoice(IReadOnlyList<Guid> ProfileIds, bool IncludeUser, string? Passphrase, bool IncludeAreas = true);

/// <summary>Lo elegido al importar: si se actualizan los que ya están y el usuario para los que vienen sin él.</summary>
public sealed record ProfileImportChoice(bool UpdateExisting, string? User, string? Password);

/// <summary>Lo común de los diálogos del backup de registros.</summary>
internal static class BackupDialog
{
    public static void Setup(Window w, string title)
    {
        w.Title = title;
        w.Width = 520;
        w.SizeToContent = SizeToContent.Height;
        w.CanResize = false;
        w.CanMinimize = false;
        w.CanMaximize = false;
        w.ShowInTaskbar = false;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    public static TextBlock Text(string text, double opacity = 1) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = opacity };

    public static TextBlock Error() => new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };

    public static void Show(TextBlock error, string message)
    {
        error.Text = message;
        error.IsVisible = true;
    }

    public static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var b in buttons) panel.Children.Add(b);
        return panel;
    }
}

/// <summary>
/// «Exportar registros…» (F-22): qué registros, si van con el usuario y si van con las contraseñas (cifradas con una
/// frase que se escribe dos veces). Devuelve null si se cancela.
/// </summary>
public sealed class ProfileExportDialog : Window
{
    /// <param name="preselected">Las que vienen marcadas (null: todas).</param>
    public ProfileExportDialog(IReadOnlyList<ConnectionProfile> profiles, IReadOnlyCollection<Guid>? preselected = null)
    {
        BackupDialog.Setup(this, Strings.BackupExportTitle);
        var checks = profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(p => new CheckBox
        {
            Content = $"{p.Name}  ·  {(string.IsNullOrWhiteSpace(p.Folder) ? ProfileEnvironments.Label(p.Environment) : p.Folder.Trim())}",
            IsChecked = preselected is null || preselected.Contains(p.Id), Tag = p.Id, Margin = new Thickness(0, 2),
        }).ToList();
        var list = new StackPanel();
        foreach (var c in checks) list.Children.Add(c);
        var all = new Button { Content = Strings.BackupAll };
        var none = new Button { Content = Strings.BackupNone };
        all.Click += (_, _) => checks.ForEach(c => c.IsChecked = true);
        none.Click += (_, _) => checks.ForEach(c => c.IsChecked = false);

        // Con TextBlock adentro para que el texto largo se parta en renglones en vez de cortarse.
        IncludeUser = new CheckBox { Content = BackupDialog.Text(Strings.BackupIncludeUser), IsChecked = true };
        IncludePasswords = new CheckBox { Content = BackupDialog.Text(Strings.BackupIncludePasswords), IsChecked = false };
        IncludeAreas = new CheckBox { Content = BackupDialog.Text(Strings.BackupIncludeAreas), IsChecked = true };
        Phrase = new TextBox { PasswordChar = '●' };
        Repeat = new TextBox { PasswordChar = '●' };
        var phrasePanel = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(24, 0, 0, 0), IsVisible = false,
            Children =
            {
                BackupDialog.Text(string.Format(Strings.BackupPhrase, ProfileBackup.MinPassphraseLength)), Phrase,
                BackupDialog.Text(Strings.BackupPhraseRepeat), Repeat,
            },
        };
        void Refresh()
        {
            IncludePasswords.IsEnabled = IncludeUser.IsChecked == true;   // sin usuario no van contraseñas
            if (IncludeUser.IsChecked != true) IncludePasswords.IsChecked = false;
            phrasePanel.IsVisible = IncludePasswords.IsChecked == true;
        }
        IncludeUser.IsCheckedChanged += (_, _) => Refresh();
        IncludePasswords.IsCheckedChanged += (_, _) => Refresh();

        var error = BackupDialog.Error();
        var ok = new Button { Content = Strings.BackupExportButton, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);

        void Accept()
        {
            var ids = checks.Where(c => c.IsChecked == true).Select(c => (Guid)c.Tag!).ToList();
            string? phrase = null;
            if (ids.Count == 0) { BackupDialog.Show(error, Strings.BackupNoneChosen); return; }
            if (IncludePasswords.IsChecked == true)
            {
                phrase = Phrase.Text ?? "";
                if (phrase.Length < ProfileBackup.MinPassphraseLength)
                {
                    BackupDialog.Show(error, string.Format(Core.Resources.CoreStrings.BackupShortPhrase, ProfileBackup.MinPassphraseLength));
                    return;
                }
                if (phrase != Repeat.Text) { BackupDialog.Show(error, Strings.BackupPhraseMismatch); return; }
            }
            Close(new ProfileExportChoice(ids, IncludeUser.IsChecked == true, phrase, IncludeAreas.IsChecked == true));
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new DockPanel
                {
                    Children = { BackupDialog.Buttons(all, none).Also(p => DockPanel.SetDock(p, Dock.Right)), BackupDialog.Text(Strings.BackupChooseLabel) },
                },
                new ScrollViewer { MaxHeight = 260, Content = list, Padding = new Thickness(4, 0) },
                IncludeUser,
                IncludePasswords,
                phrasePanel,
                IncludeAreas,
                BackupDialog.Text(Strings.BackupExportNote, 0.75),
                error,
                BackupDialog.Buttons(ok, cancel),
            },
        };
    }

    public CheckBox IncludeUser { get; }
    public CheckBox IncludePasswords { get; }
    public CheckBox IncludeAreas { get; }
    public TextBox Phrase { get; }
    public TextBox Repeat { get; }
}

/// <summary>Pide la frase de un archivo cifrado; si no es la correcta lo dice y se puede volver a probar.</summary>
public sealed class ProfilePhraseDialog : Window
{
    public ProfilePhraseDialog(ProfileBackupFile file)
    {
        BackupDialog.Setup(this, Strings.BackupImportTitle);
        Phrase = new TextBox { PasswordChar = '●' };
        var error = BackupDialog.Error();
        var ok = new Button { Content = Strings.Ok, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);
        Phrase.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(); } };
        Opened += (_, _) => Phrase.Focus();

        void Accept()
        {
            try
            {
                Close(ProfileBackup.Open(file, Phrase.Text));
            }
            catch (InvalidOperationException ex)
            {
                BackupDialog.Show(error, ex.Message);
                Phrase.SelectAll();
            }
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children = { BackupDialog.Text(string.Format(Strings.BackupPhrasePrompt, file.Count)), Phrase, error, BackupDialog.Buttons(ok, cancel) },
        };
    }

    public TextBox Phrase { get; }
}

/// <summary>
/// Antes de importar: cuántos son nuevos y cuántos ya están registrados (y qué hacer con ellos), y el usuario y la
/// contraseña para los que vienen sin usuario. Devuelve null si se cancela.
/// </summary>
public sealed class ProfileImportDialog : Window
{
    public ProfileImportDialog(IReadOnlyList<ProfileImportItem> plan, bool savesPasswords)
    {
        BackupDialog.Setup(this, Strings.BackupImportTitle);
        var existing = plan.Count(i => i.Existing is not null);
        var withPassword = plan.Count(i => i.Password is not null);
        var withoutUser = plan.Count(i => string.IsNullOrWhiteSpace(i.Profile.User) && i.Existing is null);

        Skip = new RadioButton { Content = Strings.BackupExistingSkip, IsChecked = true, GroupName = "existing" };
        Update = new RadioButton { Content = Strings.BackupExistingUpdate, GroupName = "existing" };
        User = new TextBox { PlaceholderText = Strings.BackupUser };
        Password = new TextBox { PasswordChar = '●', PlaceholderText = Strings.BackupPassword };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(BackupDialog.Text(string.Format(Strings.BackupImportSummary, plan.Count, plan.Count - existing, existing)));
        if (withPassword > 0) body.Children.Add(BackupDialog.Text(string.Format(Strings.BackupWithPasswords, withPassword), 0.75));
        if (withPassword > 0 && !savesPasswords) body.Children.Add(BackupDialog.Text(Strings.BackupNoKeyring, 0.75));
        if (existing > 0)
        {
            var names = string.Join(", ", plan.Where(i => i.Existing is not null).Select(i => i.Existing!.Name).Take(8));
            body.Children.Add(BackupDialog.Text(string.Format(Strings.BackupExistingList, names), 0.75));
            body.Children.Add(new StackPanel { Spacing = 4, Children = { Skip, Update } });
        }
        if (withoutUser > 0)
        {
            body.Children.Add(BackupDialog.Text(string.Format(Strings.BackupMissingUser, withoutUser)));
            body.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,8,*"),
                Children = { User, Password.Also(p => Grid.SetColumn(p, 2)) },
            });
        }

        var ok = new Button { Content = Strings.BackupImportButton, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) => Close(new ProfileImportChoice(Update.IsChecked == true,
            string.IsNullOrWhiteSpace(User.Text) ? null : User.Text.Trim(), string.IsNullOrEmpty(Password.Text) ? null : Password.Text));
        cancel.Click += (_, _) => Close(null);
        body.Children.Add(BackupDialog.Buttons(ok, cancel));
        Content = new Border { Padding = new Thickness(20), Child = body };
    }

    public RadioButton Skip { get; }
    public RadioButton Update { get; }
    public TextBox User { get; }
    public TextBox Password { get; }
}

internal static class ControlExtensions
{
    /// <summary>Para configurar un control dentro de un inicializador: <c>panel.Also(p => DockPanel.SetDock(p, Dock.Right))</c>.</summary>
    public static T Also<T>(this T control, Action<T> configure)
    {
        configure(control);
        return control;
    }
}
