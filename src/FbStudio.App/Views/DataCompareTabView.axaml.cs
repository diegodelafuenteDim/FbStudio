using System.ComponentModel;
using Avalonia.Controls;
using FbStudio.App.ViewModels;

namespace FbStudio.App.Views;

/// <summary>Pestaña «Comparar datos»: las filas distintas a la izquierda y el script para igualar el destino a la derecha.</summary>
public partial class DataCompareTabView : UserControl
{
    private DataCompareTabViewModel? _wired;

    public DataCompareTabView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_wired is not null) _wired.PropertyChanged -= OnTabChanged;
        _wired = DataContext as DataCompareTabViewModel;
        if (_wired is null) return;
        _wired.PropertyChanged += OnTabChanged;
        ScriptView.Text = _wired.Script;
    }

    // El editor de AvaloniaEdit no se enlaza por texto: se carga cuando cambia el script.
    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DataCompareTabViewModel.Script) && _wired is not null) ScriptView.Text = _wired.Script;
    }
}
