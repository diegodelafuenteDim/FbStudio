using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.App.ViewModels;
using FbStudio.Core.Security;

namespace FbStudio.App.Views;

/// <summary>
/// Crear o cambiar un usuario del servidor (F-28): nombre (solo al crear), contraseña dos veces (al cambiar, vacía no se
/// toca), nombre y apellido, descripción, activo, si administra usuarios y en qué administrador de usuarios va.
/// </summary>
public sealed class UserDialog : Window
{
    public UserDialog(UserDialogRequest request)
    {
        var user = request.User;
        Title = user is null ? Strings.SecUserTitleNew : string.Format(Strings.SecUserTitleEdit, user.Name);
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        UserName = new TextBox { Text = user?.Name ?? "", IsEnabled = user is null };
        Password = new TextBox { PasswordChar = '•', PlaceholderText = user is null ? "" : Strings.SecUserPasswordKeep };
        Repeat = new TextBox { PasswordChar = '•' };
        FirstName = new TextBox { Text = user?.FirstName ?? "" };
        MiddleName = new TextBox { Text = user?.MiddleName ?? "" };
        LastName = new TextBox { Text = user?.LastName ?? "" };
        Description = new TextBox { Text = user?.Description ?? "" };
        Active = new CheckBox { Content = Strings.SecUserActive, IsChecked = user?.Active ?? true };
        Admin = new CheckBox { Content = Strings.SecUserAdmin, IsChecked = user?.Admin ?? false };
        // El administrador de usuarios solo se elige al crear, y solo si el servidor tiene más de uno.
        Plugin = new ComboBox
        {
            ItemsSource = new[] { Strings.SecUserPluginDefault }.Concat(request.Plugins).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = user is null && request.Plugins.Count > 1,
        };
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        var ok = new Button { Content = Strings.SecUserSave, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (Validate(user is null) is { } message)
            {
                error.Text = message;
                error.IsVisible = true;
                return;
            }
            Close(Result(user));
        };
        cancel.Click += (_, _) => Close(null);
        Opened += (_, _) => (user is null ? UserName : Password).Focus();

        static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 4, 0, 0) };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 4,
            Children =
            {
                Label(Strings.SecUserName), UserName,
                Label(Strings.SecUserPassword), Password,
                Label(Strings.SecUserPasswordRepeat), Repeat,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*"),
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    Children =
                    {
                        At(Label(Strings.SecUserFirstName), 0, 0), At(FirstName, 1, 0),
                        At(Label(Strings.SecUserMiddleName), 0, 2), At(MiddleName, 1, 2),
                        At(Label(Strings.SecUserLastName), 0, 4), At(LastName, 1, 4),
                    },
                },
                Label(Strings.SecUserDescription), Description,
                new TextBlock { Text = Strings.SecUserPlugin, Margin = new Thickness(0, 4, 0, 0), IsVisible = Plugin.IsVisible }, Plugin,
                Active, Admin,
                new TextBlock { Text = Strings.SecUserServerNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 4) },
                error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };
    }

    // Para las pruebas.
    public TextBox UserName { get; }
    public TextBox Password { get; }
    public TextBox Repeat { get; }
    public TextBox FirstName { get; }
    public TextBox MiddleName { get; }
    public TextBox LastName { get; }
    public TextBox Description { get; }
    public CheckBox Active { get; }
    public CheckBox Admin { get; }
    public ComboBox Plugin { get; }

    private static Control At(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    /// <summary>Lo que falta o no coincide; null si está todo bien.</summary>
    public string? Validate(bool isNew)
    {
        if (string.IsNullOrWhiteSpace(UserName.Text)) return Strings.SecUserNameRequired;
        if (isNew && string.IsNullOrEmpty(Password.Text)) return Strings.SecUserPasswordRequired;
        if ((Password.Text ?? "") != (Repeat.Text ?? "")) return Strings.SecUserPasswordMismatch;
        return null;
    }

    private UserEdit Result(DbUser? user) => new(
        UserName.Text!.Trim(),
        string.IsNullOrEmpty(Password.Text) ? null : Password.Text,
        FirstName.Text, MiddleName.Text, LastName.Text,
        Active.IsChecked == true, Admin.IsChecked == true,
        Description.Text,
        user is null && Plugin.SelectedIndex > 0 ? Plugin.SelectedItem as string : null);
}
