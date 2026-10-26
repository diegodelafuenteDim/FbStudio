using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using FbStudio.App.Resources;
using FbStudio.App.ResultGrid;
using FbStudio.App.ViewModels;
using FbStudio.Core.Export;
using FbStudio.Core.Metadata;
using FbStudio.Core.Sql;
using FbStudio.Workspace;

namespace FbStudio.App.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<EditorTabViewModel, Control> _views = [];
    private MainWindowViewModel? _wired;
    private bool _closeConfirmed;
    private bool _restartAfterClose;

    // Arrastrar en el árbol: dónde se apretó (el arrastre arranca recién al moverse unos píxeles) y qué se arrastra.
    private (PointerPressedEventArgs Pressed, Avalonia.Point Point, TreeNode Node)? _dragStart;
    private TreeNode? _dragging;

    /// <summary>Una base o una carpeta que se mueve dentro del árbol: no es texto, así el editor no escribe su nombre.</summary>
    private static readonly DataFormat<string> TreeMoveFormat = DataFormat.CreateStringApplicationFormat("fbstudio-tree-move");

    public MainWindow()
    {
        InitializeComponent();

        RunButton.Click += async (_, _) => await RunAsync();
        MenuRun.Click += async (_, _) => await RunAsync();
        MenuRunStatement.Click += async (_, _) => await RunCurrentStatementAsync();
        MenuPlanOnly.Click += async (_, _) => await ShowPlanOnlyAsync();
        MenuRunScript.Click += async (_, _) => await RunAsScriptAsync();
        MenuRunFile.Click += async (_, _) => await RunFileAsync();
        MenuOpenFile.Click += async (_, _) => await OpenSqlFileAsync();
        MenuFind.Click += (_, _) => CurrentEditorView?.OpenFind(replace: false);
        MenuReplace.Click += (_, _) => CurrentEditorView?.OpenFind(replace: true);
        MenuOpenDiagram.Click += async (_, _) => { if (Vm is { } vm) await OpenDiagramFileAsync(vm, SelectedDatabase()?.Profile); };
        MenuSaveFile.Click += async (_, _) => await SaveSqlFileAsync(saveAs: false);
        MenuSaveFileAs.Click += async (_, _) => await SaveSqlFileAsync(saveAs: true);
        MenuRecentFiles.SubmenuOpened += (_, _) => FillRecentFiles();
        FillRecentFiles();
        MenuFormatSql.Click += (_, _) => CurrentEditorView?.FormatSql();
        MenuInsertTemplate.Click += (_, _) => CurrentEditorView?.ShowTemplates();
        MenuToggleComment.Click += (_, _) => CurrentEditorView?.ToggleComment();
        MenuDuplicateLine.Click += (_, _) => CurrentEditorView?.DuplicateLine();
        MenuMoveLineUp.Click += (_, _) => CurrentEditorView?.MoveLines(up: true);
        MenuMoveLineDown.Click += (_, _) => CurrentEditorView?.MoveLines(up: false);
        MenuUppercase.Click += (_, _) => CurrentEditorView?.ChangeCase(upper: true);
        MenuLowercase.Click += (_, _) => CurrentEditorView?.ChangeCase(upper: false);
        MenuGoToLine.Click += async (_, _) => await GoToLineAsync();
        MenuGoToDefinition.Click += async (_, _) => { if (CurrentEditorView is { } view) await view.GoToDefinitionAtCaretAsync(); };
        MenuExport.Click += async (_, _) => await ExportAsync(ExportFormat.Csv);
        MenuExportXlsx.Click += async (_, _) => await ExportAsync(ExportFormat.Xlsx);
        MenuExportInsert.Click += async (_, _) => await ExportAsync(ExportFormat.Insert);
        MenuExportJson.Click += async (_, _) => await ExportAsync(ExportFormat.Json);
        MenuExportMarkdown.Click += async (_, _) => await ExportAsync(ExportFormat.Markdown);
        FlyoutExportCsv.Click += async (_, _) => await ExportAsync(ExportFormat.Csv);
        FlyoutExportXlsx.Click += async (_, _) => await ExportAsync(ExportFormat.Xlsx);
        FlyoutExportInsert.Click += async (_, _) => await ExportAsync(ExportFormat.Insert);
        FlyoutExportJson.Click += async (_, _) => await ExportAsync(ExportFormat.Json);
        FlyoutExportMarkdown.Click += async (_, _) => await ExportAsync(ExportFormat.Markdown);
        ToolRegister.Click += async (_, _) => await OpenProfilesAsync(newRegistration: true);
        MenuRegister.Click += async (_, _) => await OpenProfilesAsync(newRegistration: true);
        WelcomeRegister.Click += async (_, _) => await OpenProfilesAsync(newRegistration: true);
        WelcomeImport.Click += async (_, _) => { if (Vm is { } vm) await ImportProfilesAsync(vm); };
        MenuEditRegistration.Click += async (_, _) => await OpenProfilesAsync(newRegistration: false);
        MenuNewFolder.Click += async (_, _) => { if (Vm is { } vm) await NewFolderAsync(vm, SelectedDatabase()?.Profile); };
        MenuExportProfiles.Click += async (_, _) => { if (Vm is { } vm) await ExportProfilesAsync(vm); };
        MenuImportProfiles.Click += async (_, _) => { if (Vm is { } vm) await ImportProfilesAsync(vm); };
        ToolConnect.Click += async (_, _) => await ConnectSelectedAsync();
        MenuConnect.Click += async (_, _) => await ConnectSelectedAsync();
        MenuDisconnect.Click += async (_, _) =>
        {
            if (Vm is not { } vm) return;
            if ((SelectedDatabase()?.Profile ?? vm.SelectedProfile) is { } profile) await vm.DisconnectAsync(profile);
            else vm.Notify(Strings.PickDatabaseInTree);
        };
        MenuExit.Click += (_, _) => Close();
        MenuShortcuts.Click += async (_, _) => await ShowShortcutsAsync();
        MenuAbout.Click += async (_, _) => await new AboutDialog().ShowDialog(this);
        MenuWhatsNew.Click += async (_, _) => await new WhatsNewDialog().ShowDialog(this);
        MenuManual.Click += async (_, _) => await ManualService.OpenAsync(this);
        MenuOptions.Click += async (_, _) => await OpenOptionsAsync();
        MenuMonitor.Click += (_, _) => Vm?.OpenMonitor(SelectedDatabase()?.Profile);
        MenuStatistics.Click += (_, _) => Vm?.OpenStatistics(SelectedDatabase()?.Profile);
        MenuMaintenance.Click += (_, _) => Vm?.OpenMaintenance(SelectedDatabase()?.Profile);
        MenuTrace.Click += (_, _) => Vm?.OpenTrace(SelectedDatabase()?.Profile);
        MenuSecurity.Click += (_, _) => Vm?.OpenSecurity(SelectedDatabase()?.Profile);
        MenuAreas.Click += (_, _) => Vm?.OpenAreas(SelectedDatabase()?.Profile);
        MenuSchemaScript.Click += async (_, _) => { if (Vm is { } vm) await OpenSchemaScriptAsync(vm, SelectedDatabase()?.Profile ?? vm.SelectedProfile); };
        MenuCompare.Click += async (_, _) => { if (Vm is { } vm) await OpenCompareAsync(vm, SelectedDatabase()?.Profile ?? vm.SelectedProfile); };
        MenuNewTab.Click += (_, _) => NewTab();
        NewTabButton.Click += (_, _) => NewTab();
        MenuCloseTab.Click += async (_, _) => await CloseCurrentTabAsync();
        MenuSearch.Click += (_, _) => OpenSearch();
        ToolSearch.Click += (_, _) => OpenSearch();
        MenuViewResults.Click += (_, _) => SetTab(0);
        MenuViewPlan.Click += (_, _) => SetTab(1);
        MenuViewStats.Click += (_, _) => SetTab(2);
        MenuViewHistory.Click += (_, _) => SetTab(3);
        MenuViewMessages.Click += (_, _) => SetTab(4);

        Tree.DoubleTapped += async (_, _) => { if (Vm?.Tree.SelectedNode is { } n) await Vm.Tree.ActivateAsync(n); };
        Tree.KeyDown += async (_, e) =>
        {
            if (Vm?.Tree.SelectedNode is not { } n) return;
            if (e.Key == Key.Enter) { e.Handled = true; await Vm.Tree.ActivateAsync(n); }
            else if (e.Key == Key.Right && !n.IsExpanded && n.IsExpandable) { e.Handled = true; await Vm.Tree.ToggleAsync(n); }
            else if (e.Key == Key.Left && n.IsExpanded) { e.Handled = true; await Vm.Tree.ToggleAsync(n); }
        };
        // Clic en el triángulo abre o cierra sin activar.
        Tree.AddHandler(PointerPressedEvent, async (_, e) =>
        {
            if (e.Source is TextBlock { Text: "▸" or "▾", DataContext: TreeNode n } && Vm is { } vm)
            {
                e.Handled = true;
                await vm.Tree.ToggleAsync(n);
            }
        }, RoutingStrategies.Tunnel);
        // Arrastrar una base o una carpeta propia: a otra carpeta, adentro de otra carpeta o a la raíz. Un objeto o una
        // columna, al editor: escribe su nombre donde se suelta.
        Tree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            _dragStart = e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed
                         && (e.Source as Control)?.DataContext is TreeNode node
                         && (node.Kind == TreeNodeKind.Database || node.IsCustomFolder || DatabaseTreeViewModel.EditorText(node) is not null)
                ? (e, e.GetPosition(Tree), node)
                : null;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tree.AddHandler(PointerReleasedEvent, (_, _) => _dragStart = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tree.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (_dragStart is not { } start || !e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed) return;
            var moved = e.GetPosition(Tree) - start.Point;
            if (Math.Abs(moved.X) + Math.Abs(moved.Y) < 8) return;
            _dragStart = null;
            if (DatabaseTreeViewModel.EditorText(start.Node) is { } name)
            {
                var text = new DataTransfer();
                text.Add(DataTransferItem.CreateText(name));
                await DragDrop.DoDragDropAsync(start.Pressed, text, DragDropEffects.Copy);
                return;
            }
            _dragging = start.Node;
            try
            {
                var data = new DataTransfer();
                data.Add(DataTransferItem.Create(TreeMoveFormat, start.Node.Text));
                await DragDrop.DoDragDropAsync(start.Pressed, data, DragDropEffects.Move);
            }
            finally
            {
                _dragging = null;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(Tree, true);
        Tree.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = _dragging is { } dragged && Vm is { } vm && vm.CanDrop(dragged, (e.Source as Control)?.DataContext as TreeNode)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        });
        Tree.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_dragging is { } dragged && Vm is { } vm) vm.Drop(dragged, (e.Source as Control)?.DataContext as TreeNode);
            e.Handled = true;
        });

        // La consulta nueva va a la base de lo último que se tocó (D-88): un clic en el árbol, aunque sea en la fila que ya
        // estaba elegida, o volver al editor de la pestaña.
        Tree.AddHandler(PointerPressedEvent, (_, _) => Vm?.TreeTouched(), RoutingStrategies.Tunnel, handledEventsToo: true);
        TabHost.AddHandler(GotFocusEvent, (_, _) => Vm?.TabTouched(), RoutingStrategies.Bubble, handledEventsToo: true);

        // Clic derecho: el menú de la fila de abajo del puntero (y la elige); en un lugar vacío, registrar o crear carpeta.
        Tree.ContextRequested += (_, e) =>
        {
            if (Vm is not { } vm) return;
            var node = (e.Source as Control)?.DataContext as TreeNode;
            if (node is not null) vm.Tree.SelectedNode = node;
            if (BuildTreeMenu(node) is { } menu) { menu.Open(Tree); e.Handled = true; }
        };

        // Clic del medio sobre una pestaña la cierra.
        TabStrip.AddHandler(PointerReleasedEvent, async (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Middle && (e.Source as Control)?.DataContext is EditorTabViewModel tab && Vm is { } vm)
            {
                e.Handled = true;
                await vm.CloseTabAsync(tab);
            }
        }, RoutingStrategies.Tunnel);

        TabStrip.ContextRequested += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is not EditorTabViewModel tab || Vm is not { } vm) return;
            e.Handled = true;
            BuildTabMenu(vm, tab).Open(TabStrip);
        };

        KeyDown += OnWindowKeyDown;
        // Las de pestañas van en túnel: el editor se queda con Ctrl+Tab si le llega primero.
        AddHandler(KeyDownEvent, OnTabShortcut, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    /// <summary>Vista (editor + resultados) de la pestaña elegida; falla si es la de búsqueda.</summary>
    public EditorTabView CurrentView => CurrentEditorView ?? throw new InvalidOperationException(Strings.TabHasNoEditor);

    /// <summary>Vista de editor de la pestaña elegida (null en la pestaña de búsqueda).</summary>
    public EditorTabView? CurrentEditorView => Vm?.SelectedTab is { } tab ? _views.GetValueOrDefault(tab) as EditorTabView : null;

    public TextEditor Editor => CurrentView.Editor;
    public DrawnGrid ResultsGrid => CurrentView.Results;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is not { } vm || ReferenceEquals(vm, _wired)) return;
        _wired = vm;
        vm.ConfirmAsync = decision => new ConfirmDialog(decision.Reasons).ShowDialog<bool>(this);
        vm.ConfirmCloseAsync = message => new ConfirmDialog([message], Strings.CloseAndUndo, Strings.DontClose, Strings.OpenTransactionTitle).ShowDialog<bool>(this);
        vm.EditMonitorQueryAsync = editor => new MonitorQueryWindow(editor).ShowDialog<bool>(this);
        vm.PreviewScriptAsync = preview => new ScriptPreviewWindow(preview).ShowDialog<ScriptPreviewResult>(this);
        vm.ConfirmActionAsync = prompt => new ConfirmDialog(prompt.Reasons, prompt.RunText, prompt.CancelText, prompt.Title).ShowDialog<bool>(this);
        vm.ConfirmTypedAsync = prompt => new TypedConfirmDialog(prompt).ShowDialog<bool>(this);
        vm.AskParametersAsync = entries => new ParameterDialog(entries).ShowDialog<IReadOnlyList<ParameterEntry>?>(this);
        vm.EditUserAsync = request => new UserDialog(request).ShowDialog<FbStudio.Core.Security.UserEdit?>(this);
        vm.AskTextAsync = prompt => new InputDialog(prompt.Title, prompt.Message, prompt.Initial).ShowDialog<string?>(this);
        vm.ConfirmDiscardAsync = prompt => new ConfirmDialog([prompt.Message], prompt.DiscardLabel, Strings.KeepEditing, Strings.UncompiledChangesTitle).ShowDialog<bool>(this);
        vm.Tabs.CollectionChanged += OnTabsChanged;
        vm.SettingsChanged += settings =>
        {
            App.ApplyTheme(settings.Theme);
            App.ApplyEnvironmentColors(settings);
        };
        foreach (var tab in vm.Tabs) AddView(tab);
        vm.Tree.InsertRequested += text => CurrentEditorView?.InsertAtCaret(text);
        vm.Tree.QueryRequested += async (profile, title, sql) =>
        {
            var tab = vm.OpenQueryTab(profile, title, sql);
            await tab.RunAsync(sql);
        };
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (EditorTabViewModel tab in e.OldItems ?? Array.Empty<EditorTabViewModel>())
        {
            if (_views.Remove(tab, out var view)) TabHost.Children.Remove(view);
        }
        foreach (EditorTabViewModel tab in e.NewItems ?? Array.Empty<EditorTabViewModel>()) AddView(tab);
    }

    private void AddView(EditorTabViewModel tab)
    {
        Control view = tab switch
        {
            SearchTabViewModel => new SearchTabView { DataContext = tab },
            MonitorTabViewModel => new MonitorTabView { DataContext = tab },
            StatisticsTabViewModel => new StatisticsTabView { DataContext = tab },
            SchemaScriptTabViewModel => new SchemaScriptTabView { DataContext = tab },
            CompareTabViewModel => new CompareTabView { DataContext = tab },
            MaintenanceTabViewModel => new MaintenanceTabView { DataContext = tab },
            TraceTabViewModel => new TraceTabView { DataContext = tab },
            ImportTabViewModel => new ImportTabView { DataContext = tab },
            SecurityTabViewModel => new SecurityTabView { DataContext = tab },
            AreasTabViewModel => new AreasTabView { DataContext = tab },
            DiagramTabViewModel => new DiagramTabView { DataContext = tab },
            TestDataTabViewModel => new TestDataTabView { DataContext = tab },
            DataCompareTabViewModel => new DataCompareTabView { DataContext = tab },
            _ => new EditorTabView { DataContext = tab },
        };
        _views[tab] = view;
        TabHost.Children.Add(view);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || Vm is not { } vm) return;
        e.Cancel = true;
        var warnings = vm.ExitWarnings();
        if (warnings.Count > 0)
        {
            var undo = vm.Tabs.Any(t => t.InTransaction || t.Data?.HasPending == true);
            if (!await new ConfirmDialog(warnings, undo ? Strings.ExitAndUndo : Strings.ExitAndDiscard, Strings.DontExit,
                    undo ? Strings.OpenTransactionsTitle : Strings.ExitPendingTitle).ShowDialog<bool>(this))
            {
                _restartAfterClose = false; // eligió no salir: tampoco se reinicia
                return;
            }
        }
        // Las conexiones se cierran antes de salir, con lo abierto deshecho. Si el programa terminara primero, el
        // servidor igual las corta al cerrarse el socket, pero sin despedirse. Como mucho 5 s, por si el servidor no responde.
        IsEnabled = false;
        try { vm.SaveSession(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex); }
        await Task.WhenAny(vm.DisposeAsync().AsTask(), Task.Delay(TimeSpan.FromSeconds(5)));
        _closeConfirmed = true;
        Close();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_restartAfterClose) StartNewInstance();
        if (Vm is { } vm) await vm.DisposeAsync();
    }

    /// <summary>Al arrancar una versión nueva, las novedades una vez (D-85). Lo llama App al abrir la ventana.</summary>
    internal async Task ShowWhatsNewIfUpdatedAsync()
    {
        if (Vm?.ShouldShowWhatsNew(AppInfo.VersionNumber, AppInfo.ChangeLog) == true) await new WhatsNewDialog().ShowDialog(this);
    }

    /// <summary>Abre otra vez FbStudio (el mismo ejecutable, o «dotnet FbStudio.dll» si se corrió así).</summary>
    private static void StartNewInstance()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && System.Reflection.Assembly.GetEntryAssembly()?.Location is { Length: > 0 } dll)
            start.ArgumentList.Add(dll);
        System.Diagnostics.Process.Start(start);
    }

    /// <summary>
    /// Los textos se leen al armar las ventanas: el idioma nuevo se ve al reiniciar. Pregunta si reiniciar ya; si hay
    /// transacciones abiertas, la salida pregunta como siempre (y si se elige no salir, no se reinicia).
    /// </summary>
    private async Task OfferRestartAsync(MainWindowViewModel vm)
    {
        var prompt = ConfirmPrompt.Restart(vm.Options.Language, vm.Tabs.Count);
        if (!await new ConfirmDialog(prompt.Reasons, prompt.RunText, prompt.CancelText, prompt.Title).ShowDialog<bool>(this)) return;
        _restartAfterClose = true;
        Close();
    }

    private void SetTab(int index)
    {
        if (Vm?.SelectedTab is { } tab) tab.SelectedResultTab = index;
    }

    private void NewTab()
    {
        if (Vm?.NewQueryTabFor(SelectedDatabase()?.Profile) is not null) FocusEditorLater();
    }

    private void OpenSearch()
    {
        if (Vm?.NewSearchTabFor(SelectedDatabase()?.Profile) is not null) FocusEditorLater();
    }

    /// <summary>El capítulo del manual que corresponde a lo que se está usando (F1): la grilla, la pestaña o el árbol.</summary>
    internal string ManualChapterHere()
    {
        var focused = FocusManager?.GetFocusedElement() as Avalonia.Visual;
        if (focused is not null && Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(focused).Contains(Tree)) return "05-tree";
        if (FocusedGrid() is not null)
            return Vm?.SelectedTab is { Data: not null } dataTab && dataTab.SelectedResultTab == EditorTabViewModel.DataTab ? "04-data" : "03-results";
        return Vm?.SelectedTab switch
        {
            null => "README",
            MonitorTabViewModel or TraceTabViewModel or StatisticsTabViewModel => "06-monitoring",
            MaintenanceTabViewModel or SecurityTabViewModel => "07-maintenance",
            SchemaScriptTabViewModel or CompareTabViewModel or DataCompareTabViewModel or ImportTabViewModel or TestDataTabViewModel => "08-scripts",
            AreasTabViewModel or DiagramTabViewModel or SearchTabViewModel => "05-tree",
            { Data: not null, SelectedResultTab: EditorTabViewModel.DataTab } => "04-data",
            _ => "02-editor",
        };
    }

    /// <summary>La grilla de resultados que tiene el foco (o donde está el foco), si hay una.</summary>
    private ResultGrid.DrawnGrid? FocusedGrid() =>
        Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ResultGrid.DrawnGrid>(FocusManager?.GetFocusedElement() as Avalonia.Visual, includeSelf: true);

    /// <summary>«¿Dónde se usa?» de una columna del árbol: la lista, y doble clic abre cada objeto.</summary>
    internal async Task<ColumnUsageWindow?> ShowColumnUsageAsync(MainWindowViewModel vm, Core.Profiles.ConnectionProfile profile, string table, string column)
    {
        try
        {
            var usage = await vm.ColumnUsageAsync(profile, table, column);
            var window = new ColumnUsageWindow(table, column, usage, d =>
                d.Kind == DbObjectKind.Index ? Task.CompletedTask : vm.OpenObjectAsync(profile, new DbObject(d.Kind, d.Name)));
            window.Show(this);
            return window;
        }
        catch (Exception ex)
        {
            vm.Notify(string.Format(Strings.ErrorStatus, ex.Message.Split('\n', 2)[0].Trim()));
            return null;
        }
    }

    /// <summary>Ctrl+G: pide el número de renglón y lleva el cursor ahí.</summary>
    private async Task GoToLineAsync()
    {
        if (CurrentEditorView is not { } view) return;
        var editor = view.Editor;
        var current = editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber;
        var text = await new InputDialog(Strings.GoToLineTitle, string.Format(Strings.GoToLinePrompt, editor.Document.LineCount),
            current.ToString(System.Globalization.CultureInfo.CurrentCulture)).ShowDialog<string?>(this);
        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out var line)) view.GoTo(line, 1);
    }

    private async Task CloseCurrentTabAsync()
    {
        if (Vm is { SelectedTab: { } tab } vm) await vm.CloseTabAsync(tab);
    }

    private void FocusEditorLater() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (Vm?.SelectedTab is not { } t || !_views.TryGetValue(t, out var v)) return;
        if (v is EditorTabView editor) editor.Editor.Focus();
        else if (v is SearchTabView search) search.FocusSearchBox();
    }, Avalonia.Threading.DispatcherPriority.Background);

    private async void OnTabShortcut(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && CurrentEditorView is { } formatView)
        {
            e.Handled = true;
            formatView.FormatSql();
        }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            OpenSearch();
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && FocusedGrid() is { } grid)
        {
            // Con una grilla elegida, Ctrl+F busca en sus resultados (en el editor, en el texto).
            e.Handled = true;
            grid.OpenSearch();
        }
        else if (e.Key is Key.F or Key.H && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && CurrentEditorView is { } findView)
        {
            e.Handled = true;
            findView.OpenFind(replace: e.Key == Key.H);
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true;
            vm.SelectNextTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            FocusEditorLater();
        }
        else if (e.Key == Key.O && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            await OpenSqlFileAsync();
        }
        else if (e.Key == Key.S && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            await SaveSqlFileAsync(saveAs: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
        else if (e.Key == Key.T && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            e.Handled = true;
            if (vm.ReopenClosedTab() is not null) FocusEditorLater();
        }
        else if (e.Key == Key.T && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            NewTab();
        }
        else if (e.Key == Key.E && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && CurrentEditorView is not null)
        {
            e.Handled = true;
            await ShowPlanOnlyAsync();
        }
        else if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control && CurrentEditorView is not null)
        {
            e.Handled = true;
            await GoToLineAsync();
        }
        else if (e.Key is Key.W or Key.F4)
        {
            e.Handled = true;
            await CloseCurrentTabAsync();
        }
        else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            // En túnel: si no, el editor se queda con el Enter y agrega un salto de línea.
            e.Handled = true;
            await RunCurrentStatementAsync();
        }
    }

    /// <summary>Atajos: F9 (o F5) ejecuta, Ctrl+Alt+C/R confirman o deshacen. Ctrl+Enter va en túnel.</summary>
    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var tab = Vm?.SelectedTab;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        {
            // El capítulo del manual de lo que se está usando.
            e.Handled = true;
            await ManualService.OpenAsync(this, ManualChapterHere());
        }
        else if (e.Key == Key.F9 && ctrl)
        {
            e.Handled = true;
            await RunAsScriptAsync();
        }
        else if (e.Key is Key.F9 or Key.F5)
        {
            e.Handled = true;
            await RunAsync();
        }
        else if (e.Key == Key.Escape && tab?.CancelCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            tab.CancelCommand.Execute(null);
        }
        else if (ctrl && alt && e.Key == Key.C && tab?.CommitCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            await tab.CommitCommand.ExecuteAsync(null);
        }
        else if (ctrl && alt && e.Key == Key.R && tab?.RollbackCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            await tab.RollbackCommand.ExecuteAsync(null);
        }
        else if (ctrl && (e.Key is >= Key.D1 and <= Key.D5 || (e.Key == Key.D6 && tab?.IsObjectTab == true) || (e.Key == Key.D7 && tab?.HasDataTab == true)))
        {
            e.Handled = true;
            SetTab(e.Key - Key.D1);
        }
    }

    /// <summary>
    /// F9: con selección ejecuta lo seleccionado; sin selección, todo el editor (varias sentencias van
    /// como script, en orden). En la pestaña de un procedimiento, trigger, vista o función, sin selección, compila.
    /// </summary>
    public Task RunAsync()
    {
        if (Vm?.SelectedTab is not { } tab || tab is SchemaScriptTabViewModel or CompareTabViewModel or MaintenanceTabViewModel or TraceTabViewModel or ImportTabViewModel or TestDataTabViewModel
            or DataCompareTabViewModel) return Task.CompletedTask;
        if (tab is MonitorTabViewModel monitor) return monitor.RefreshAsync();
        if (tab is SecurityTabViewModel security) return security.RefreshAsync();
        if (tab is StatisticsTabViewModel stats) return stats.ReadHeaderAsync();
        if (tab is SearchTabViewModel search) return search.SearchAsync();
        var editor = CurrentView.Editor;
        if (editor.SelectionLength > 0) return tab.RunTextAsync(editor.SelectedText, editor.SelectionStart, ExecutionScope.Selection);
        if (tab.IsCompilable) return tab.CompileAsync();
        return tab.RunTextAsync(editor.Text, 0, ExecutionScope.All);
    }

    /// <summary>
    /// Ctrl+F9: la selección o todo el editor como script sin grilla (scripts largos: sin plan ni estadísticas por
    /// sentencia, y una sola entrada en el historial). En las pestañas sin editor hace lo mismo que F9.
    /// </summary>
    public Task RunAsScriptAsync()
    {
        if (Vm?.SelectedTab is not { } tab || CurrentEditorView is not { } view) return RunAsync();
        var editor = view.Editor;
        return editor.SelectionLength > 0
            ? tab.RunTextAsync(editor.SelectedText, editor.SelectionStart, ExecutionScope.Selection, asScript: true)
            : tab.RunTextAsync(editor.Text, 0, ExecutionScope.All, asScript: true);
    }

    private static readonly FilePickerFileType SqlFiles = new(Strings.ExportSqlScript) { Patterns = ["*.sql", "*.txt"] };

    /// <summary>Herramientas → «Abrir diagrama guardado…»: en la base elegida (la del árbol o la de la pestaña).</summary>
    private async Task OpenDiagramFileAsync(MainWindowViewModel vm, FbStudio.Core.Profiles.ConnectionProfile? profile)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.MenuOpenDiagram.TrimEnd('…'),
            FileTypeFilter = [new FilePickerFileType(Strings.DiagramFileSaved) { Patterns = ["*" + Core.Diagrams.DiagramFile.Extension] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) vm.OpenDiagramFile(path, profile);
    }

    /// <summary>Ctrl+O o Consulta → «Abrir archivo .sql…».</summary>
    private async Task OpenSqlFileAsync()
    {
        if (Vm is not { } vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.MenuOpenFile.TrimEnd('…'),
            FileTypeFilter = [SqlFiles, FilePickerFileTypes.All],
            AllowMultiple = true,
        });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path) vm.OpenSqlFile(path, SelectedDatabase()?.Profile);
        if (files.Count > 0) FocusEditorLater();
    }

    /// <summary>
    /// Ctrl+S o «Guardar»: en su archivo, o pregunta dónde si todavía no tiene (o con «Guardar como»). En la pestaña de un
    /// objeto guarda una copia del DDL.
    /// </summary>
    internal async Task SaveSqlFileAsync(bool saveAs)
    {
        if (Vm is not { } vm || vm.SelectedTab is not { } tab || CurrentEditorView is null) return;
        var path = saveAs || tab.Kind == EditorTabKind.Object ? null : tab.FilePath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = (saveAs ? Strings.MenuSaveFileAs : Strings.MenuSaveFile).TrimEnd('…'),
                SuggestedFileName = tab.FilePath is { } current ? Path.GetFileName(current) : SuggestedFileName(tab),
                DefaultExtension = "sql",
                FileTypeChoices = [SqlFiles],
            });
            if (file?.TryGetLocalPath() is not { } chosen) return;
            path = chosen;
        }
        SaveCurrentTabTo(path);
    }

    /// <summary>Guarda la pestaña elegida en ese archivo y lo suma a los recientes (si es una consulta).</summary>
    internal void SaveCurrentTabTo(string path)
    {
        if (Vm is not { SelectedTab: { } tab } vm) return;
        try
        {
            tab.SaveFile(path);
            if (tab.Kind == EditorTabKind.Sql) vm.RememberFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tab.Status = string.Format(Strings.FileSaveFailed, Path.GetFileName(path), ex.Message);
        }
    }

    /// <summary>«Consulta 3.sql», «TURNOS.sql»: el título sin caracteres que no van en un nombre de archivo.</summary>
    private static string SuggestedFileName(EditorTabViewModel tab) =>
        string.Concat((tab.Object?.Name ?? tab.BaseTitle).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".sql";

    /// <summary>«Abrir reciente»: los últimos archivos, con su carpeta de tooltip.</summary>
    private void FillRecentFiles()
    {
        var items = new List<Control>();
        foreach (var path in Vm?.RecentFiles ?? [])
        {
            var item = new MenuItem { Header = Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) =>
            {
                if (Vm?.OpenSqlFile(path, SelectedDatabase()?.Profile) is not null) FocusEditorLater();
            };
            items.Add(item);
        }
        MenuRecentFiles.ItemsSource = items.Count > 0 ? items : [new MenuItem { Header = Strings.MenuRecentNone, IsEnabled = false }];
    }

    /// <summary>
    /// Consulta → «Ejecutar script desde archivo…»: en la pestaña de consulta elegida o, si es otra clase de pestaña o
    /// está ejecutando, en una consulta nueva de la base de lo último que se tocó.
    /// </summary>
    private async Task RunFileAsync()
    {
        if (Vm is not { } vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.MenuRunFile.TrimEnd('…'),
            FileTypeFilter = [new FilePickerFileType(Strings.ExportSqlScript) { Patterns = ["*.sql", "*.txt"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        var tab = vm.SelectedTab is { Kind: EditorTabKind.Sql, IsRunning: false, Profile: not null } current
            ? current
            : vm.NewQueryTabFor(SelectedDatabase()?.Profile);
        if (tab is not null) await tab.RunFileAsync(path);
    }

    /// <summary>Ctrl+Shift+E: el plan de la sentencia donde está el cursor (o de la selección), sin ejecutarla.</summary>
    public Task ShowPlanOnlyAsync()
    {
        if (CurrentEditorView is not { Tab: { } tab } view) return Task.CompletedTask;
        var editor = view.Editor;
        if (editor.SelectionLength > 0) return tab.ShowPlanOnlyAsync(editor.SelectedText);
        if (SqlScriptSplitter.AtCaret(editor.Text, editor.CaretOffset) is not { } statement)
        {
            tab.Status = Strings.NoStatementAtCursor;
            return Task.CompletedTask;
        }
        return tab.ShowPlanOnlyAsync(statement.Text);
    }

    /// <summary>Ctrl+Enter: solo la sentencia donde está el cursor (o la selección, si hay).</summary>
    public Task RunCurrentStatementAsync()
    {
        if (Vm?.SelectedTab is not { } tab || tab is SchemaScriptTabViewModel or CompareTabViewModel or MaintenanceTabViewModel or TraceTabViewModel or ImportTabViewModel or TestDataTabViewModel
            or DataCompareTabViewModel) return Task.CompletedTask;
        if (tab is MonitorTabViewModel monitor) return monitor.RefreshAsync();
        if (tab is SecurityTabViewModel security) return security.RefreshAsync();
        if (tab is StatisticsTabViewModel stats) return stats.ReadHeaderAsync();
        if (tab is SearchTabViewModel search) return search.SearchAsync();
        var editor = CurrentView.Editor;
        if (editor.SelectionLength > 0) return tab.RunTextAsync(editor.SelectedText, editor.SelectionStart, ExecutionScope.Selection);
        if (tab.IsCompilable) return tab.CompileAsync();
        if (SqlScriptSplitter.AtCaret(editor.Text, editor.CaretOffset) is not { } statement)
        {
            tab.Status = Strings.NoStatementAtCursor;
            return Task.CompletedTask;
        }
        return tab.RunTextAsync(statement.Text, statement.Start, ExecutionScope.Statement);
    }

    private TreeNode? SelectedDatabase() => Vm?.Tree.SelectedNode?.DatabaseNode;

    /// <summary>Los objetos que reciben permisos («Permisos…» en el árbol), con su tipo para la pestaña de seguridad.</summary>
    private static FbStudio.Core.Security.GrantObjectKind? GrantKind(DbObjectKind kind) => kind switch
    {
        DbObjectKind.Table => FbStudio.Core.Security.GrantObjectKind.Table,
        DbObjectKind.View => FbStudio.Core.Security.GrantObjectKind.View,
        DbObjectKind.Procedure => FbStudio.Core.Security.GrantObjectKind.Procedure,
        DbObjectKind.Function => FbStudio.Core.Security.GrantObjectKind.Function,
        DbObjectKind.Package => FbStudio.Core.Security.GrantObjectKind.Package,
        DbObjectKind.Generator => FbStudio.Core.Security.GrantObjectKind.Generator,
        DbObjectKind.Exception => FbStudio.Core.Security.GrantObjectKind.Exception,
        _ => null,
    };

    private async Task ConnectSelectedAsync()
    {
        if (Vm is not { } vm) return;
        if (SelectedDatabase() is { } db) await vm.Tree.ConnectAsync(db);
        else vm.Notify(vm.Tree.IsEmpty ? Strings.RegisterFirst : Strings.PickDatabaseInTree);
    }

    internal ContextMenu BuildTabMenu(MainWindowViewModel vm, EditorTabViewModel tab)
    {
        MenuItem Item(string header, Func<Task> action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += async (_, _) => await action();
            return mi;
        }
        var index = vm.Tabs.IndexOf(tab);
        var others = vm.Tabs.Where(t => t != tab).ToList();
        var right = vm.Tabs.Skip(index + 1).ToList();
        var isQuery = tab.Kind == EditorTabKind.Sql && tab is not SearchTabViewModel;
        return new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = new List<Control>
            {
                Item(Strings.TabRename, async () =>
                {
                    if (TopLevel.GetTopLevel(this) is Window owner
                        && await new InputDialog(Strings.TabRename.TrimEnd('…'), Strings.TabRenamePrompt, tab.BaseTitle).ShowDialog<string?>(owner) is { } name)
                        tab.Rename(name);
                }, tab.CanRename),
                Item(Strings.TabDuplicate, () => { vm.DuplicateTab(tab); return Task.CompletedTask; }, isQuery),
                Item(Strings.TabReopenClosed, () => { vm.ReopenClosedTab(); return Task.CompletedTask; }, vm.CanReopenClosedTab),
                new Separator(),
                Item(Strings.Close, () => vm.CloseTabAsync(tab)),
                Item(Strings.CloseOthers, () => vm.CloseTabsAsync(others), others.Count > 0),
                Item(Strings.CloseToTheRight, () => vm.CloseTabsAsync(right), right.Count > 0),
                new Separator(),
                Item(Strings.CloseAll, () => vm.CloseTabsAsync(vm.Tabs.ToList())),
            },
        };
    }

    internal ContextMenu? BuildTreeMenu(TreeNode? node)
    {
        if (Vm is not { } vm) return null;
        var tree = vm.Tree;
        var items = new List<Control>();
        MenuItem Item(string header, Func<Task> action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += async (_, _) => await action();
            return mi;
        }
        static MenuItem Group(string header, params Control[] children) => new() { Header = header, ItemsSource = children };
        Func<Task> Run(Action action) => () =>
        {
            action();
            return Task.CompletedTask;
        };
        if (node is null)
        {
            items.Add(Item(Strings.TreeRegister, () => OpenProfilesAsync(newRegistration: true)));
            items.Add(Item(Strings.TreeNewFolder, () => NewFolderAsync(vm, null), !tree.IsEmpty));
            items.Add(new Separator());
            items.Add(Item(Strings.MenuExportProfiles, () => ExportProfilesAsync(vm), !tree.IsEmpty));
            items.Add(Item(Strings.MenuImportProfiles, () => ImportProfilesAsync(vm)));
            return new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        }

        switch (node.Kind)
        {
            case TreeNodeKind.Database:
            {
                // Agrupado (pedido de Diego): lo de todos los días arriba, las herramientas en submenús y lo que no es de
                // esta base (nueva carpeta, registrar otra, importar) en el menú de las carpetas y en «Base de datos».
                // Lo que no aplica no se muestra. Las herramientas abren su propia conexión: no hace falta conectar antes.
                var profile = node.Profile!;
                var connected = tree.IsConnected(profile.Id);
                items.Add(Item(connected ? Strings.TreeUseInTab : Strings.ToolConnect, () => tree.ConnectAsync(node)));
                if (vm.CanDisconnect(profile.Id)) items.Add(Item(Strings.TreeDisconnect, () => vm.DisconnectAsync(profile)));
                items.Add(Item(Strings.TreeNewQuery, Run(() =>
                {
                    vm.NewQueryTab(profile);
                    FocusEditorLater();
                })));
                items.Add(Item(Strings.TreeSearchMetadata, Run(() =>
                {
                    vm.NewSearchTab(profile);
                    FocusEditorLater();
                })));
                if (connected)
                    items.Add(Item(Strings.TreeReloadMetadata, async () =>
                    {
                        await tree.RefreshDatabaseAsync(node);
                        vm.InvalidateCatalog(profile.Id);
                    }));
                if (!profile.ReadOnly) items.Add(Item(Strings.TreeImportNewTable, Run(() => vm.OpenImport(profile, null))));
                items.Add(new Separator());
                items.Add(Group(Strings.TreeGroupTools,
                    Item(Strings.TreeMonitor, Run(() => vm.OpenMonitor(profile))),
                    Item(Strings.TreeTrace, Run(() => vm.OpenTrace(profile))),
                    Item(Strings.TreeStatistics, Run(() => vm.OpenStatistics(profile))),
                    Item(Strings.TreeSecurity, Run(() => vm.OpenSecurity(profile))),
                    Item(Strings.TreeAreas, Run(() => vm.OpenAreas(profile))),
                    Item(Strings.MenuOpenDiagram, () => OpenDiagramFileAsync(vm, profile)),
                    Item(Strings.TreeServerDetails, () => ShowServerDetailsAsync(vm, profile))));
                items.Add(Group(Strings.TreeGroupMaintenance,
                    Item(Strings.MaintTaskBackup, Run(() => vm.OpenMaintenance(profile, MaintenanceTask.Backup))),
                    Item(Strings.MaintTaskRestore, Run(() => vm.OpenMaintenance(profile, MaintenanceTask.Restore))),
                    Item(Strings.MaintTaskNBackup, Run(() => vm.OpenMaintenance(profile, MaintenanceTask.NBackup))),
                    Item(Strings.MaintTaskValidate, Run(() => vm.OpenMaintenance(profile, MaintenanceTask.Validate))),
                    Item(Strings.MaintTaskSweep, Run(() => vm.OpenMaintenance(profile, MaintenanceTask.Sweep))),
                    Item(Strings.MaintTaskEmptyCopy, Run(() => vm.OpenMaintenance(profile, MaintenanceTask.EmptyCopy))),
                    Item(Strings.MaintTaskProperties, Run(() => vm.OpenMaintenance(profile, MaintenanceTask.Properties)))));
                var script = new List<Control>
                {
                    Item(Strings.TreeSchemaScript, () => OpenSchemaScriptAsync(vm, profile)),
                    Item(Strings.TreeDocumentation, () => OpenSchemaScriptAsync(vm, profile, documentation: true)),
                };
                if (vm.Profiles.Count > 1) script.Add(Item(Strings.TreeCompare, () => OpenCompareAsync(vm, profile)));
                items.Add(Group(Strings.TreeGroupScript, [.. script]));
                items.Add(new Separator());
                items.Add(Group(Strings.TreeGroupRegistration,
                    Item(Strings.TreeEditRegistration, () => OpenProfilesAsync(newRegistration: false, select: profile.Id)),
                    Item(Strings.TreeCloneRegistration, () => OpenProfilesAsync(newRegistration: false, select: vm.CloneProfile(profile).Id)),
                    Item(Strings.TreeMoveToFolder, () => MoveToFolderAsync(vm, profile)),
                    Item(Strings.TreeExportThis, () => ExportProfilesAsync(vm, [profile.Id]))));
                break;
            }
            case TreeNodeKind.Category when node.Category == DbObjectKind.Table && node.DatabaseNode?.Profile is { ReadOnly: false } tablesOf:
                items.Add(Item(Strings.TreeImportNewTable, Run(() => vm.OpenImport(tablesOf, null))));
                break;
            case TreeNodeKind.Object when node.Object is { } o:
                // La pestaña del objeto abre con el DDL arriba y las dependencias abajo: un solo «Abrir».
                items.Add(Item(DbObjectKinds.IsCompilable(o.Kind) ? Strings.TreeOpenCompilable : Strings.TreeViewDdl,
                    () => vm.OpenObjectAsync(node.DatabaseNode!.Profile!, o)));
                items.Add(Item(Strings.TreeViewData, async () =>
                {
                    if (await vm.OpenObjectAsync(node.DatabaseNode!.Profile!, o) is { } dataTab)
                        dataTab.SelectedResultTab = EditorTabViewModel.DataTab;
                }, DbObjectKinds.IsRelation(o.Kind)));
                items.Add(Item("SELECT FIRST 100 *", () => { tree.RequestSelectFirst100(node); return Task.CompletedTask; },
                    DbObjectKinds.IsRelation(o.Kind)));
                if (DbObjectKinds.IsRelation(o.Kind))
                {
                    // La sentencia en una consulta nueva, sin ejecutar; de una vista, solo el SELECT.
                    var sqlProfile = node.DatabaseNode!.Profile!;
                    MenuItem Sql(string header, TableSqlKind kind) => Item(header, async () =>
                    {
                        if (await vm.GenerateTableSqlAsync(sqlProfile, o, kind) is not null) FocusEditorLater();
                    });
                    var sql = new List<Control> { Sql(Strings.TreeSqlSelect, TableSqlKind.Select) };
                    if (o.Kind == DbObjectKind.Table)
                        sql.AddRange([Sql("INSERT", TableSqlKind.Insert), Sql(Strings.TreeSqlUpdate, TableSqlKind.Update),
                            Sql(Strings.TreeSqlDelete, TableSqlKind.Delete), Sql("UPDATE OR INSERT", TableSqlKind.UpdateOrInsert)]);
                    items.Add(Group(Strings.TreeGenerateSql, [.. sql]));
                    items.Add(Item(Strings.TreeDiagram, Run(() => vm.OpenDiagram(sqlProfile, new DiagramSource(o.Name)))));
                }
                if (o.Kind == DbObjectKind.Table)
                    items.Add(Item(Strings.TreeImportData, () =>
                    {
                        vm.OpenImport(node.DatabaseNode!.Profile!, o.Name);
                        return Task.CompletedTask;
                    }, !node.DatabaseNode!.Profile!.ReadOnly));
                if (o.Kind is DbObjectKind.Procedure or DbObjectKind.Function)
                {
                    // La llamada con sus parámetros, en una consulta nueva sin ejecutar.
                    var routineProfile = node.DatabaseNode!.Profile!;
                    MenuItem Call(string header, bool select) => Item(header, async () =>
                    {
                        if (await vm.GenerateRoutineSqlAsync(routineProfile, o, select) is not null) FocusEditorLater();
                    });
                    var calls = new List<Control>();
                    if (o.Kind == DbObjectKind.Function) calls.Add(Call("SELECT … FROM RDB$DATABASE", true));
                    else calls.AddRange([Call("SELECT * FROM …", true), Call("EXECUTE PROCEDURE …", false)]);
                    items.Add(Group(Strings.TreeGenerateSql, [.. calls]));
                }
                if (o.Kind == DbObjectKind.Table && vm.Profiles.Count > 1)
                    items.Add(Item(Strings.TreeDataCompare, Run(() => vm.OpenDataCompare(node.DatabaseNode!.Profile!, o.Name))));
                if (o.Kind == DbObjectKind.Table && node.DatabaseNode!.Profile! is { ReadOnly: false, IsProduction: false } testProfile)
                    items.Add(Item(Strings.TreeTestData, Run(() => vm.OpenTestData(testProfile, o.Name))));
                if (GrantKind(o.Kind) is { } grantKind)
                    items.Add(Item(Strings.TreePermissions, async () =>
                    {
                        if (vm.OpenSecurity(node.DatabaseNode!.Profile!) is not { } security) return;
                        await security.Working;
                        security.ShowObject(grantKind, o.Name);
                    }));
                items.Add(Item(Strings.TreeInsertName, () => { CurrentEditorView?.InsertAtCaret(DatabaseTreeViewModel.QuoteIdentifier(o.Name)); return Task.CompletedTask; }));
                items.Add(Item(Strings.TreeCopyName, async () => { if (Clipboard is { } c) await c.SetTextAsync(o.Name); }));
                break;
            case TreeNodeKind.Areas or TreeNodeKind.Area:
            {
                var profile = node.DatabaseNode!.Profile!;
                if (node.Kind == TreeNodeKind.Area && vm.AreaStore.Areas(profile).FirstOrDefault(a => a.Name == node.Text) is { } area)
                    items.Add(Item(Strings.TreeAreaDiagram, Run(() => vm.OpenDiagram(profile, new DiagramSource(area.Name, area)))));
                items.Add(Item(Strings.TreeEditAreas, Run(() => vm.OpenAreas(profile))));
                break;
            }
            case TreeNodeKind.Column when node.Column is { } col:
                if (node.Parent?.Object is { } owner && node.DatabaseNode?.Profile is { } columnProfile)
                    items.Add(Item(Strings.TreeColumnUsage, () => ShowColumnUsageAsync(vm, columnProfile, owner.Name, col.Name)));
                items.Add(Item(Strings.TreeInsertName, () => { CurrentEditorView?.InsertAtCaret(DatabaseTreeViewModel.QuoteIdentifier(col.Name)); return Task.CompletedTask; }));
                items.Add(Item(Strings.TreeCopyName, async () => { if (Clipboard is { } c) await c.SetTextAsync(col.Name); }));
                break;
            case TreeNodeKind.Folder:
                items.Add(Item(Strings.TreeRegister, () => OpenProfilesAsync(newRegistration: true, folder: node.FolderPath)));
                items.Add(Item(Strings.TreeNewFolder, () => NewFolderAsync(vm, null)));
                items.Add(Item(Strings.TreeExportFolder, () => ExportProfilesAsync(vm, DatabasesIn(node))));
                items.Add(Item(Strings.MenuImportProfiles, () => ImportProfilesAsync(vm)));
                if (node.IsCustomFolder)
                {
                    items.Add(new Separator());
                    items.Add(FolderColorMenu(vm, node.FolderPath!));
                    items.Add(Item(Strings.TreeRenameFolder, async () =>
                    {
                        var name = await new InputDialog(Strings.TreeRenameFolder.TrimEnd('…'), string.Format(Strings.FolderRenamePrompt, node.FolderPath),
                            node.FolderPath!, allowEmpty: true).ShowDialog<string?>(this);
                        if (name is not null) vm.RenameFolder(node.FolderPath!, name);
                    }));
                }
                break;
            default:
                return null;
        }
        return new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
    }

    /// <summary>«Color de la carpeta» (F-20): los de la paleta (sin rojos, que son de producción) y «Sin color propio».</summary>
    private static MenuItem FolderColorMenu(MainWindowViewModel vm, string folder)
    {
        var current = vm.FolderColor(folder);
        var menu = new MenuItem { Header = Strings.TreeFolderColor };
        foreach (var choice in ColorChoice.For(null))
        {
            var item = new MenuItem
            {
                Header = (string.Equals(choice.Hex, current, StringComparison.OrdinalIgnoreCase) ? "● " : "") + choice.Name,
                Icon = new Border { Width = 14, Height = 14, CornerRadius = new Avalonia.CornerRadius(3), Background = choice.Brush },
            };
            item.Click += (_, _) => vm.SetFolderColor(folder, choice.Hex);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var none = new MenuItem { Header = Strings.TreeFolderNoColor, IsEnabled = current is not null };
        none.Click += (_, _) => vm.SetFolderColor(folder, null);
        menu.Items.Add(none);
        return menu;
    }

    private enum ExportFormat
    {
        Csv,
        Xlsx,
        Insert,
        Json,
        Markdown,
    }

    /// <summary>Exporta el resultado de la pestaña: lo cargado en la grilla o, si la consulta trae más, todo (re-ejecutándola).</summary>
    private async Task ExportAsync(ExportFormat format)
    {
        if (Vm?.SelectedTab is not { } tab) return;
        if (tab.Rows.Count == 0)
        {
            tab.Status = Strings.NothingToExport;
            return;
        }

        IResultExporter exporter;
        string fileType;
        string suggested;
        switch (format)
        {
            case ExportFormat.Xlsx:
                (exporter, fileType, suggested) = (new XlsxExporter(), "Excel", Strings.ExportFileName);
                break;
            case ExportFormat.Insert:
                var table = await new InputDialog(Strings.ExportInsertTitle, Strings.ExportInsertPrompt,
                    StatementClassifier.MainTable(tab.LastSql) ?? Strings.ExportInsertDefaultTable).ShowDialog<string?>(this);
                if (table is null) return;
                var insert = new InsertScriptExporter(table);
                (exporter, fileType, suggested) = (insert, Strings.ExportSqlScript, "insert-" + insert.Table.Trim('"').ToLowerInvariant());
                break;
            case ExportFormat.Json:
                (exporter, fileType, suggested) = (new JsonExporter(), "JSON", Strings.ExportFileName);
                break;
            case ExportFormat.Markdown:
                (exporter, fileType, suggested) = (new MarkdownExporter(), "Markdown", Strings.ExportFileName);
                break;
            default:
                (exporter, fileType, suggested) = (new CsvExporter(CsvOptions.ExcelAr), "CSV (Excel)", Strings.ExportFileName);
                break;
        }

        var full = false;
        if (tab.CanExportFull)
        {
            var choice = await new ChoiceDialog(string.Format(Strings.ExportTitle, exporter.Name),
                string.Format(Strings.ExportPartialQuestion, tab.Rows.Count),
                Strings.ExportAll, string.Format(Strings.ExportOnlyLoaded, tab.Rows.Count)).ShowDialog<int>(this);
            if (choice < 0) return;
            full = choice == 0;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = string.Format(Strings.ExportPickerTitle, exporter.Name),
            SuggestedFileName = $"{suggested}-{DateTime.Now:yyyyMMdd-HHmm}.{exporter.Extension}",
            DefaultExtension = exporter.Extension,
            FileTypeChoices = [new FilePickerFileType(fileType) { Patterns = [$"*.{exporter.Extension}"] }],
        });
        if (file?.TryGetLocalPath() is { } path) await tab.ExportAsync(path, full, exporter);
    }

    /// <summary>«Comparar bases…»: origen, destino y opciones; después, la pestaña con las diferencias.</summary>
    private async Task OpenCompareAsync(MainWindowViewModel vm, Core.Profiles.ConnectionProfile? source)
    {
        if (vm.Profiles.Count < 2)
        {
            vm.Notify(Strings.CompareNeedsTwo);
            return;
        }
        if (await new CompareDialog(vm.Profiles, source, p => vm.AreaStore.Areas(p).Select(a => a.Name).ToList()).ShowDialog<CompareChoice?>(this) is { } choice)
            vm.OpenCompare(choice.Source, choice.Target, choice.Options);
    }

    private static readonly FilePickerFileType ProfileFiles = new(Strings.BackupFileType) { Patterns = ["*.json"] };

    /// <summary>Las bases que hay dentro de una carpeta del árbol (y de sus subcarpetas).</summary>
    private static List<Guid> DatabasesIn(TreeNode folder) =>
        (folder.Children ?? []).SelectMany(n => n.Kind == TreeNodeKind.Database ? [n.Profile!.Id] : DatabasesIn(n)).ToList();

    /// <summary>«Exportar registros…» (F-22): cuáles y cómo, y después dónde. Desde una carpeta vienen marcadas las suyas.</summary>
    private async Task ExportProfilesAsync(MainWindowViewModel vm, IReadOnlyCollection<Guid>? preselected = null)
    {
        if (vm.Profiles.Count == 0)
        {
            vm.Notify(Strings.BackupNothing);
            return;
        }
        if (await new ProfileExportDialog(vm.Profiles.ToList(), preselected).ShowDialog<ProfileExportChoice?>(this) is not { } choice) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.BackupExportTitle,
            SuggestedFileName = $"fbstudio-registros-{DateTime.Now:yyyyMMdd}.json",
            DefaultExtension = "json",
            FileTypeChoices = [ProfileFiles],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try { vm.ExportProfiles(path, choice.ProfileIds, choice.IncludeUser, choice.Passphrase, choice.IncludeAreas); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { vm.Notify(string.Format(Strings.BackupFailed, ex.Message)); }
    }

    /// <summary>«Importar registros…»: el archivo, la frase si está cifrado, y qué hacer con los que ya están.</summary>
    private async Task ImportProfilesAsync(MainWindowViewModel vm)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Strings.BackupImportTitle, FileTypeFilter = [ProfileFiles] });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            var file = ProfileBackup.Read(path);
            var content = file.IsEncrypted ? await new ProfilePhraseDialog(file).ShowDialog<ProfileBackupContent?>(this) : ProfileBackup.Open(file, null);
            if (content is null) return;
            var plan = vm.PlanImport(content);
            if (await new ProfileImportDialog(plan, vm.ProfileStore.SavesPasswords).ShowDialog<ProfileImportChoice?>(this) is not { } choice) return;
            vm.ImportProfiles(plan, choice.UpdateExisting, choice.User, choice.Password);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            vm.Notify(string.Format(Strings.BackupFailed, ex.Message));
        }
    }

    /// <summary>«Datos del servidor…»: la ventana se abre enseguida y se llena cuando contesta el servidor (F-16).</summary>
    private async Task ShowServerDetailsAsync(MainWindowViewModel vm, Core.Profiles.ConnectionProfile profile)
    {
        if (vm.ProfileStore.GetPassword(profile.Id) is not { } password)
        {
            vm.Notify(string.Format(Strings.NoSavedPassword, profile.Name));
            return;
        }
        var loading = Firebird.ServerProbe.DetailsAsync(profile, password);
        await new ServerDialog(profile.Name, $"{profile.Host}:{profile.Port}", loading).ShowDialog(this);
    }

    /// <summary>«Script de la base…»: qué incluir y, con «Generar», la pestaña con el script.</summary>
    /// <param name="documentation">Después de leer la base, ofrecer guardar la documentación HTML.</param>
    private async Task OpenSchemaScriptAsync(MainWindowViewModel vm, Core.Profiles.ConnectionProfile? profile, bool documentation = false)
    {
        if (profile is null)
        {
            vm.Notify(Strings.SchemaScriptPickDatabase);
            return;
        }
        var areas = vm.AreaStore.Areas(profile).Select(a => a.Name).ToList();
        if (await new SchemaScriptDialog(profile.Name, new Core.Metadata.SchemaScriptOptions(), areas).ShowDialog<Core.Metadata.SchemaScriptOptions?>(this) is not { } options
            || vm.OpenSchemaScript(profile, options) is not { } tab || !documentation) return;
        await tab.Generating;
        if (tab.HasScript && _views.TryGetValue(tab, out var view) && view is SchemaScriptTabView scriptView) await scriptView.SaveDocumentationAsync();
    }

    /// <summary>«Nueva carpeta…»: nombre y bases que van adentro (viene marcada la base elegida en el árbol).</summary>
    private async Task NewFolderAsync(MainWindowViewModel vm, Core.Profiles.ConnectionProfile? selected)
    {
        if (vm.Profiles.Count == 0)
        {
            vm.Notify(Strings.RegisterFirst);
            return;
        }
        var choice = await new FolderDialog(vm.Profiles, selected is null ? [] : [selected.Id]).ShowDialog<FolderChoice?>(this);
        if (choice is not null) vm.CreateFolder(choice.Name, choice.ProfileIds);
    }

    /// <summary>«Mover a carpeta…»: una carpeta existente o una nueva; vacía, vuelve al grupo de su entorno.</summary>
    private async Task MoveToFolderAsync(MainWindowViewModel vm, Core.Profiles.ConnectionProfile profile)
    {
        var folder = await new InputDialog(Strings.TreeMoveToFolder.TrimEnd('…'), string.Format(Strings.FolderMovePrompt, profile.Name),
            profile.Folder ?? "", allowEmpty: true, suggestions: vm.Folders).ShowDialog<string?>(this);
        if (folder is not null) vm.MoveToFolder(profile, folder);
    }

    private async Task OpenProfilesAsync(bool newRegistration, Guid? select = null, string? folder = null)
    {
        if (Vm is not { } vm) return;
        var window = new ProfilesWindow(vm.ProfileStore, select ?? vm.SelectedProfile?.Id, startWithNew: newRegistration, folder: folder);
        await window.ShowDialog(this);
        vm.ReloadProfiles();
    }

    /// <summary>Herramientas → Opciones: se guarda y se aplica solo con «Aceptar».</summary>
    public async Task OpenOptionsAsync()
    {
        if (Vm is not { } vm) return;
        var languageBefore = vm.Options.Language;
        var result = await new OptionsWindow(new OptionsViewModel(vm.Options)).ShowDialog<AppSettings?>(this);
        if (result is null) return;
        vm.SaveSettings(result);
        if (vm.Options.Language != languageBefore) await OfferRestartAsync(vm);
    }

    private Task ShowShortcutsAsync() => ShortcutsWindow().ShowDialog(this);

    /// <summary>Ayuda de atajos: letra de ancho fijo (el texto va en columnas), sin cortes de línea y un solo botón.</summary>
    internal static Window ShortcutsWindow()
    {
        var close = new Button { Content = Strings.Close, IsDefault = true, IsCancel = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = Strings.ShortcutsTitle,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new SelectableTextBlock { Text = Strings.ShortcutsHelp, FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), FontSize = 13 },
                    close,
                },
            },
        };
        close.Click += (_, _) => dialog.Close();
        return dialog;
    }
}
