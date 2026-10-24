using Avalonia.Controls;
using FbStudio.App.Editor;
using FbStudio.App.ViewModels;
using FbStudio.Core.Sql;

namespace FbStudio.App.Views;

/// <summary>Editor de una consulta propia del monitoreo. Devuelve true con «Guardar» (si es válida).</summary>
public partial class MonitorQueryWindow : Window
{
    public MonitorQueryWindow() : this(new MonitorQueryEditorViewModel(null, "", "", _ => throw new InvalidOperationException())) { }

    public MonitorQueryWindow(MonitorQueryEditorViewModel editor)
    {
        InitializeComponent();
        DataContext = editor;
        CanMinimize = false;
        ShowInTaskbar = false;
        SqlEditor.Document = editor.Sql;
        _ = new SqlEditorSetup(SqlEditor, () => MetadataCatalog.Empty); // resaltado y palabras clave
        SaveButton.Click += (_, _) => { if (editor.Validate() is null) Close(true); };
        CancelButton.Click += (_, _) => Close(false);
        Opened += (_, _) => NameBox.Focus();
    }

    public MonitorQueryEditorViewModel Editor => (MonitorQueryEditorViewModel)DataContext!;
}
