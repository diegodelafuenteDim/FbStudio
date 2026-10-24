using Avalonia.Controls;
using FbStudio.App.Resources;
using FbStudio.Core.Profiles;
using FbStudio.Firebird;
using FbStudio.Workspace;

namespace FbStudio.App.Views;

/// <summary>Alta, edición y baja de perfiles. La contraseña se guarda cifrada y nunca se vuelve a mostrar.</summary>
public partial class ProfilesWindow : Window
{
    private static readonly string[] Charsets = ["ISO8859_1", "WIN1252", "UTF8", "NONE"];
    private readonly ProfileStore _store;
    private readonly string? _newFolder;
    private ConnectionProfile? _editing;
    private Guid? _editingId;

    // Constructor sin parámetros para el diseñador de XAML.
    public ProfilesWindow() : this(AppServices.CreateProfileStore(), null) { }

    /// <param name="folder">Carpeta propuesta para un registro nuevo (al registrar desde una carpeta del árbol).</param>
    public ProfilesWindow(ProfileStore store, Guid? selectId, bool startWithNew = false, string? folder = null)
    {
        _store = store;
        _newFolder = folder;
        InitializeComponent();
        FolderBox.InnerRightContent = FolderPicker();
        CharsetBox.ItemsSource = Charsets;
        EnvironmentBox.ItemsSource = Enum.GetValues<ProfileEnvironment>();
        EnvironmentBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProfileEnvironment>((e, _) => new TextBlock { Text = ProfileEnvironments.Label(e) });
        WireCryptBox.ItemsSource = Enum.GetValues<WireCryptMode>();
        WireCryptBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<WireCryptMode>((m, _) => new TextBlock { Text = WireCryptLabel(m) });
        // Producción: el cifrado pasa a obligatorio (se ve y se puede cambiar). Solo cuando lo elige el usuario, no al mostrar un registro.
        EnvironmentBox.SelectionChanged += (_, _) =>
        {
            if (_filling || EnvironmentBox.SelectedItem is not ProfileEnvironment.Produccion || WireCryptBox.SelectedItem is not WireCryptMode.Enabled) return;
            WireCryptBox.SelectedItem = WireCryptMode.Required;
            TestResult.Text = Strings.ProfileProductionRequiresCrypt;
        };

        List.SelectionChanged += (_, _) => { if (List.SelectedItem is ConnectionProfile p) Show(p); };
        NewButton.Click += (_, _) => { List.SelectedItem = null; ShowNew(); };
        DeleteButton.Click += (_, _) => Delete();
        SaveButton.Click += async (_, _) => await SaveAsync();
        TestButton.Click += async (_, _) => await TestAsync();
        CloseButton.Click += (_, _) => Close();

        Reload(selectId);
        if (startWithNew) { List.SelectedItem = null; ShowNew(); }
    }

    private void Reload(Guid? selectId)
    {
        var all = _store.Load();
        List.ItemsSource = all;
        var selected = all.FirstOrDefault(p => p.Id == selectId) ?? all.FirstOrDefault();
        if (selected is null) ShowNew();
        else List.SelectedItem = selected;
    }

    /// <summary>Botón ▾ al lado de «Carpeta» con las carpetas propias que ya existen.</summary>
    private Button FolderPicker()
    {
        var button = new Button { Content = "▾", Padding = new Avalonia.Thickness(8, 0), Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0) };
        ToolTip.SetTip(button, Strings.ProfileFolderPick);
        var flyout = new MenuFlyout();
        button.Flyout = flyout;
        flyout.Opening += (_, _) =>
        {
            var folders = ProfileFolders.All(_store.Load());
            var items = new List<MenuItem>();
            foreach (var f in folders)
            {
                var item = new MenuItem { Header = f };
                item.Click += (_, _) => FolderBox.Text = f;
                items.Add(item);
            }
            var none = new MenuItem { Header = Strings.ProfileFolderNone };
            none.Click += (_, _) => FolderBox.Text = "";
            items.Add(none);
            flyout.ItemsSource = items;
        };
        return button;
    }

    private void ShowNew()
    {
        _filling = true;
        _editingId = null;
        _editing = null;
        FolderBox.Text = _newFolder ?? "";
        NameBox.Text = HostBox.Text = DatabaseBox.Text = UserBox.Text = RoleBox.Text = PasswordBox.Text = "";
        PasswordBox.PlaceholderText = "";
        PortBox.Value = 3050;
        CharsetBox.SelectedItem = "ISO8859_1";
        EnvironmentBox.SelectedItem = ProfileEnvironment.Desarrollo;
        WireCryptBox.SelectedItem = WireCryptMode.Enabled;
        ReadOnlyBox.IsChecked = false;
        SaveHistoryBox.IsChecked = true;
        TestResult.Text = "";
        _filling = false;
    }

    private void Show(ConnectionProfile p)
    {
        _filling = true;
        _editingId = p.Id;
        _editing = p;
        FolderBox.Text = p.Folder ?? "";
        NameBox.Text = p.Name;
        HostBox.Text = p.Host;
        PortBox.Value = p.Port;
        DatabaseBox.Text = p.Database;
        UserBox.Text = p.User;
        RoleBox.Text = p.Role ?? "";
        PasswordBox.Text = "";
        PasswordBox.PlaceholderText = _store.GetPassword(p.Id) is null ? Strings.ProfilePasswordMissing : Strings.ProfilePasswordUnchanged;
        CharsetBox.SelectedItem = Charsets.Contains(p.Charset) ? p.Charset : "ISO8859_1";
        EnvironmentBox.SelectedItem = p.Environment;
        WireCryptBox.SelectedItem = p.WireCrypt;
        ReadOnlyBox.IsChecked = p.ReadOnly;
        SaveHistoryBox.IsChecked = p.SaveHistory;
        TestResult.Text = "";
        _filling = false;
    }

    /// <summary>Mientras se cargan los campos de un registro (no es una elección del usuario).</summary>
    private bool _filling;

    internal static string WireCryptLabel(WireCryptMode mode) => mode switch
    {
        WireCryptMode.Required => Strings.WireCryptRequired,
        WireCryptMode.Disabled => Strings.WireCryptDisabled,
        _ => Strings.WireCryptEnabled,
    };

    private ConnectionProfile? FromForm()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(HostBox.Text)
            || string.IsNullOrWhiteSpace(DatabaseBox.Text) || string.IsNullOrWhiteSpace(UserBox.Text))
        {
            TestResult.Text = Strings.ProfileMissingData;
            return null;
        }
        // Se parte del registro que se edita: lo que no está en el formulario no se pierde.
        var all = _store.Load();
        var basis = _editing ?? new ConnectionProfile { Name = "", Host = "", Database = "", User = "" };
        return basis with
        {
            Id = _editingId ?? Guid.NewGuid(),
            Folder = ProfileFolders.Normalize(FolderBox.Text, all.Where(p => p.Id != _editingId)),
            Name = NameBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            Port = (int)(PortBox.Value ?? 3050),
            Database = DatabaseBox.Text.Trim(),
            User = UserBox.Text.Trim(),
            Role = string.IsNullOrWhiteSpace(RoleBox.Text) ? null : RoleBox.Text.Trim(),
            Charset = CharsetBox.SelectedItem as string ?? "ISO8859_1",
            Environment = EnvironmentBox.SelectedItem is ProfileEnvironment env ? env : ProfileEnvironment.Desarrollo,
            WireCrypt = WireCryptBox.SelectedItem is WireCryptMode wc ? wc : WireCryptMode.Enabled,
            ReadOnly = ReadOnlyBox.IsChecked == true,
            SaveHistory = SaveHistoryBox.IsChecked == true,
        };
    }

    private async Task SaveAsync()
    {
        if (FromForm() is not { } profile) return;
        var password = string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text;
        if (password is null && _store.GetPassword(profile.Id) is null)
        {
            TestResult.Text = Strings.ProfileNoPassword;
            return;
        }
        _store.Upsert(profile, password);
        Reload(profile.Id);
        TestResult.Text = Strings.ProfileSaved;
        await new InfoDialog(Strings.ProfileSavedTitle, string.Format(Strings.ProfileSavedMessage, profile.Name)).ShowDialog(this);
    }

    private void Delete()
    {
        if (List.SelectedItem is not ConnectionProfile p) return;
        _store.Delete(p.Id);
        Reload(null);
    }

    private async Task TestAsync()
    {
        if (FromForm() is not { } profile) return;
        var password = string.IsNullOrEmpty(PasswordBox.Text) ? _store.GetPassword(profile.Id) : PasswordBox.Text;
        if (password is null) { TestResult.Text = Strings.ProfileNoPassword; return; }
        TestResult.Text = Strings.ProfileTesting;
        try
        {
            await using var session = new QuerySession(profile, password);
            await session.OpenAsync();
            var wire = await session.WireEncryptedAsync() switch { true => Strings.BannerEncrypted, false => Strings.BannerNotEncrypted, null => "" };
            TestResult.Text = $"OK · {session.ServerVersion}{wire}";
        }
        catch (Exception ex)
        {
            TestResult.Text = string.Format(Strings.ErrorStatus, ex.Message.Split('\n')[0]);
        }
    }
}
