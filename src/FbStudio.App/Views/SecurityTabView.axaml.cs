using Avalonia.Controls;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña de usuarios y permisos: la lista a la izquierda, roles y permisos del elegido a la derecha, el SQL abajo.</summary>
public partial class SecurityTabView : UserControl
{
    public SecurityTabView()
    {
        InitializeComponent();
    }

    public ListBox Permissions => PermissionList;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SecurityTabViewModel security) SqlView.Document = security.Document;
    }
}
