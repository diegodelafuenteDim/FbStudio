using System.Diagnostics;
using System.Globalization;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.App.ResultGrid;
using FbStudio.Core.Data;
using FbStudio.Core.Metadata;
using FbStudio.Core.Results;
using FbStudio.Core.Sql;
using FbStudio.Firebird;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Pestaña «Datos» de una tabla o vista (F-3). Trae de a 500 filas a medida que se baja, con el orden, la fila de
/// filtros y un WHERE libre resueltos en el servidor. Las ediciones y los borrados quedan pendientes (sin bloquear
/// nada en la base) hasta «Aplicar», que los ejecuta juntos en una transacción corta: entran todos o ninguno.
/// </summary>
public sealed partial class TableDataViewModel(EditorTabViewModel tab) : ObservableObject, IGridDataSource, IAsyncDisposable
{
    public const int PageSize = 500;

    private TableDataSession? _session;
    private TableQuery? _query;
    private TableChanges? _changes;
    private IReadOnlyList<DbColumn> _dbColumns = [];
    private int _generation; // cada recarga invalida las páginas que estaban llegando
    private bool _loadingPage;

    public AvaloniaList<object[]> Rows { get; } = [];
    public ColumnInfo[] Columns { get; private set; } = [];

    /// <summary>La vista arma las columnas de la grilla con la primera página.</summary>
    public event Action<ColumnInfo[]>? ColumnsChanged;

    /// <summary>Cambió lo que se ve sin cambiar las filas (cambios aplicados o descartados): redibujar.</summary>
    public event Action? Redraw;

    public bool IsLoaded { get; private set; }
    public bool IsView => tab.Object?.Kind == DbObjectKind.View;
    public string Table => tab.Object?.Name ?? "";

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _extraWhere = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private long? _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending), nameof(PendingText), nameof(ApplyText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DiscardCommand), nameof(ScriptCommand))]
    private int _pendingCount;

    /// <summary>«Aplicar (3)»: cuántos cambios esperan, en el mismo botón.</summary>
    public string ApplyText => PendingCount == 0 ? Strings.DataApply : string.Format(Strings.DataApplyCount, PendingCount);

    public bool HasPending => PendingCount > 0;

    public string PendingText => PendingCount == 0 ? "" : string.Format(Strings.DataPending, _changes?.UpdateCount ?? 0, _changes?.DeleteCount ?? 0, _changes?.InsertCount ?? 0);

    /// <summary>Filas nuevas sin aplicar que están arriba de la grilla (no cuentan para pedir la página siguiente).</summary>
    private int NewRowCount => _changes?.InsertCount ?? 0;

    /// <summary>La tabla de una vista que se puede editar (los cambios van a ella a través de la vista).</summary>
    private string? _viewBase;

    /// <summary>Por qué no se puede editar (solo lectura, sin clave primaria, vista sin la clave de su tabla), o null si se puede.</summary>
    public string? ReadOnlyReason =>
        tab.Profile?.ReadOnly == true ? Strings.DataProfileReadOnly
        : IsLoaded && _changes?.HasKey == false ? (IsView ? Strings.DataViewNoKey : Strings.DataNoKey)
        : null;

    public bool CanEditTable => IsLoaded && ReadOnlyReason is null;

    public string FooterText =>
        (TotalCount is { } total ? string.Format(Strings.DataFooterOf, Rows.Count, total) : string.Format(Strings.DataFooterLoaded, Rows.Count))
        + (HasMore ? Strings.DataFooterMore : "");

    /// <summary>Se llama al elegir la pestaña Datos: la tabla se lee recién ahí (puede ser enorme).</summary>
    public Task EnsureLoadedAsync() => IsLoaded || IsBusy ? Task.CompletedTask : ReloadAsync();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (tab.Profile is not { } profile || tab.Object is not { } obj) return;
        var generation = ++_generation;
        IsBusy = true;
        Status = Strings.Loading;
        var clock = Stopwatch.StartNew();
        try
        {
            if (!IsLoaded)
            {
                _dbColumns = await tab.Owner.WithExplorerAsync(profile, e => e.LoadColumnsAsync(obj.Name));
                if (IsView)
                {
                    // Una vista se edita si sale de una sola tabla y muestra toda su clave: esa es la clave de la fila.
                    var info = await tab.Owner.WithExplorerAsync(profile, e => e.ViewEditInfoAsync(obj.Name));
                    _viewBase = info.IsEditable ? info.BaseTable : null;
                    _dbColumns = _dbColumns.Select(c => c with
                    {
                        IsPrimaryKey = info.KeyColumns.Contains(c.Name),
                        IsComputed = c.IsComputed || info.ExpressionColumns.Contains(c.Name),
                    }).ToList();
                }
                _changes = new TableChanges(_dbColumns);
                _query = new TableQuery(obj.Name, _dbColumns, IsView);
            }
            _query = _query! with { ExtraWhere = string.IsNullOrWhiteSpace(ExtraWhere) ? null : ExtraWhere };
            var page = await Session(profile).ReadPageAsync(_query, 0, PageSize);
            if (generation != _generation) return;
            Rows.Clear();
            if (!IsLoaded || Columns.Length != page.Columns.Length)
            {
                Columns = page.Columns;
                IsLoaded = true;
                ColumnsChanged?.Invoke(Columns);
                OnPropertyChanged(nameof(ReadOnlyReason));
                OnPropertyChanged(nameof(CanEditTable));
            }
            Rows.AddRange(page.Rows);
            // Las filas nuevas sin aplicar siguen arriba aunque cambien el orden o el filtro.
            if (_changes is { InsertCount: > 0 }) Rows.InsertRange(0, _changes.NewRows.Cast<object[]>());
            HasMore = page.HasMore;
            TotalCount = null;
            Status = string.Format(Strings.DataLoaded, page.Rows.Count, clock.Elapsed.TotalSeconds)
                     + (ReadOnlyReason is { } why ? " · " + why : Strings.DataEditHint)
                     + (_viewBase is { } baseTable && ReadOnlyReason is null ? string.Format(Strings.DataViewEditsTable, baseTable) : "");
        }
        catch (Exception ex)
        {
            if (generation == _generation) Status = string.Format(Strings.DataReadFailed, FirstLine(ex.Message));
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
            Redraw?.Invoke();
        }
    }

    /// <summary>La clave primaria de la tabla (o la de la tabla base de una vista editable): el WHERE de «Copiar como UPDATE».</summary>
    public IReadOnlyList<string> KeyColumnNames => _dbColumns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

    /// <summary>Columnas calculadas (y expresiones de una vista): no van en INSERT ni en UPDATE.</summary>
    public IReadOnlySet<string> ComputedColumnNames => _dbColumns.Where(c => c.IsComputed).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Cómo dejó Diego las columnas de esta tabla la última vez (orden y anchos), o null.</summary>
    public GridColumnLayout? SavedLayout() => tab.Profile is { } profile ? tab.Owner.GridLayouts.Get(profile.Id, Table) : null;

    public void SaveLayout(GridColumnLayout layout)
    {
        if (tab.Profile is { } profile && Table.Length > 0) tab.Owner.GridLayouts.Save(profile.Id, Table, layout);
    }

    public bool CanListValues(int column) =>
        _query is not null && column < _dbColumns.Count && !_dbColumns[column].Type.StartsWith("BLOB", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ValueCount>> DistinctValuesAsync(int column, int limit, CancellationToken ct)
    {
        if (tab.Profile is not { } profile || _query is null || !CanListValues(column)) return [];
        return await Session(profile).DistinctAsync(_query, column, limit, ct);
    }

    public void ViewChanged(ResultViewSpec spec)
    {
        if (_query is null) return;
        _query = _query with { Filters = spec.Filters, SortColumn = spec.SortColumn, SortDirection = spec.SortDirection };
        _ = ReloadAsync();
    }

    public void NearEnd()
    {
        if (HasMore && !IsBusy && !_loadingPage && _query is not null) _ = LoadMoreAsync();
    }

    private async Task LoadMoreAsync()
    {
        if (tab.Profile is not { } profile || _query is null) return;
        var generation = _generation;
        _loadingPage = true;
        try
        {
            // «Después de la última fila traída» (por la clave): igual de rápido en cualquier página.
            var last = Rows.LastOrDefault(r => _changes?.IsNew(r) != true);
            var page = await Session(profile).ReadPageAsync(_query, Rows.Count - NewRowCount, PageSize, last);
            if (generation != _generation) return;
            Rows.AddRange(page.Rows);
            HasMore = page.HasMore;
            Status = string.Format(Strings.DataLoadedMore, Rows.Count);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.DataReadFailed, FirstLine(ex.Message));
            HasMore = false;
        }
        finally
        {
            _loadingPage = false;
            Redraw?.Invoke();
        }
    }

    [RelayCommand]
    private async Task CountAsync()
    {
        if (tab.Profile is not { } profile || _query is null) return;
        Status = Strings.DataCounting;
        try
        {
            TotalCount = await Session(profile).CountAsync(_query);
            Status = string.Format(Strings.DataCounted, TotalCount);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.DataReadFailed, FirstLine(ex.Message));
        }
        Redraw?.Invoke();
    }

    // ---------- edición ----------

    public bool CanEdit(int column) =>
        CanEditTable && (column < 0 || (column < _dbColumns.Count && CellValueParser.IsEditable(_dbColumns[column])));

    public bool TryGetPending(object[] row, int column, out object? value)
    {
        value = null;
        return _changes?.TryGetValue(row, column, out value) == true;
    }

    public bool IsDeleted(object[] row) => _changes?.IsDeleted(row) == true;

    public bool IsChanged(object[] row) => _changes?.IsChanged(row) == true;

    public bool IsNew(object[] row) => _changes?.IsNew(row) == true;

    /// <summary>
    /// Insert, «Nueva fila» o clic derecho: una fila vacía arriba de todo, pendiente como cualquier cambio. Lo que no se
    /// complete no va en el INSERT (vale el DEFAULT, el trigger o la identity).
    /// </summary>
    public object[]? AddRow()
    {
        if (!CanEdit(-1) || _changes is null) return null;
        var row = (object[])_changes.AddNew();
        Rows.Insert(0, row);
        Changed();
        Status = Strings.DataNewRowHint;
        return row;
    }

    /// <summary>Saca de la grilla las filas nuevas que dejaron de estar pendientes (borradas o deshechas antes de aplicar).</summary>
    private void DropRemovedNewRows(IEnumerable<object[]> candidates)
    {
        foreach (var row in candidates.Where(r => _changes?.IsNew(r) != true).ToList()) Rows.Remove(row);
    }

    public string EditText(object[] row, int column) =>
        CellValueParser.EditText(TryGetPending(row, column, out var pending) ? pending : row[column], _dbColumns[column], CultureInfo.CurrentCulture);

    public string? SetValue(object[] row, int column, string text)
    {
        if (!CanEdit(column) || _changes is null) return ReadOnlyReason ?? Strings.DataColumnReadOnly;
        if (!CellValueParser.TryParse(text, _dbColumns[column], CultureInfo.CurrentCulture, out var value, out var error))
        {
            Status = error!;
            return error;
        }
        _changes.Set(row, column, value);
        Changed();
        return null;
    }

    public void SetNull(object[] row, int column)
    {
        if (!CanEdit(column) || _changes is null) return;
        _changes.Set(row, column, null);
        Changed();
    }

    public void ToggleDelete(IReadOnlyList<object[]> rows)
    {
        if (!CanEdit(-1) || _changes is null) return;
        var created = rows.Where(r => _changes.IsNew(r)).ToList();
        foreach (var row in rows) _changes.ToggleDeleted(row);
        DropRemovedNewRows(created);
        Changed();
    }

    public void Revert(IReadOnlyList<object[]> rows)
    {
        if (_changes is null) return;
        var created = rows.Where(r => _changes.IsNew(r)).ToList();
        foreach (var row in rows) _changes.Revert(row);
        DropRemovedNewRows(created);
        Changed();
    }

    private void Changed()
    {
        PendingCount = _changes?.Count ?? 0;
        OnPropertyChanged(nameof(PendingText));
        if (PendingCount > 0) Status = Strings.DataPendingHint;
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void Discard()
    {
        var created = _changes?.NewRows.Cast<object[]>().ToList() ?? [];
        _changes?.Clear();
        DropRemovedNewRows(created);
        Changed();
        Status = Strings.DataDiscarded;
        Redraw?.Invoke();
    }

    /// <summary>
    /// Ejecuta los cambios en una transacción corta. Pide confirmación si hay borrados o si la base es de producción.
    /// Si una fila cambió entretanto (control optimista) o está bloqueada, no se guarda nada y los cambios siguen pendientes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPending))]
    public Task ApplyAsync() => ApplyCoreAsync(alwaysPreview: false);

    /// <summary>
    /// «Generar script…»: los comandos de los cambios pendientes, para copiarlos, llevarlos a una consulta (con o sin
    /// COMMIT al final) o ejecutarlos, aunque en Opciones esté elegido aplicar directamente.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPending))]
    public Task ScriptAsync() => ApplyCoreAsync(alwaysPreview: true);

    private async Task ApplyCoreAsync(bool alwaysPreview)
    {
        if (tab.Profile is not { } profile || _changes is null || !CanEditTable) return;
        var statements = _changes.BuildStatements(Table, returning: !IsView);
        if (statements.Count == 0) return;

        var reasons = new List<string>();
        if (profile.IsProduction) reasons.Add(string.Format(Strings.DataApplyProduction, statements.Count, Table, profile.Name));
        if (_changes.DeleteCount > 0) reasons.Add(string.Format(Strings.DataApplyDeletes, _changes.DeleteCount, Table));
        if (alwaysPreview || tab.Owner.Options.PreviewDataChanges)
        {
            // Primero los comandos (con los valores y el control de que nadie cambió la fila); los avisos van arriba.
            var script = TableChanges.Script(Table, statements);
            var choice = await tab.Owner.PreviewScriptAsync(new ScriptPreview(string.Format(Strings.DataPreviewTitle, Table), script, reasons,
                tab.Owner.Options.DataScriptWithCommit));
            if (choice.WithCommit != tab.Owner.Options.DataScriptWithCommit)
                tab.Owner.SaveSettings(tab.Owner.Options with { DataScriptWithCommit = choice.WithCommit }); // se recuerda
            switch (choice.Choice)
            {
                case ScriptPreviewChoice.Cancel:
                    Status = Strings.NotRunCancelledInConfirmation;
                    return;
                case ScriptPreviewChoice.OpenInTab:
                    // A una pestaña de consulta, para revisarlo o cambiarlo y correrlo (sin COMMIT, con Commit o Rollback a mano).
                    tab.Owner.OpenQueryTab(profile, string.Format(Strings.DataScriptTabTitle, Table), TableChanges.Script(Table, statements, choice.WithCommit));
                    var moved = _changes.Count;
                    _changes.Clear();
                    Changed();
                    Status = string.Format(Strings.DataMovedToTab, moved);
                    Redraw?.Invoke();
                    return;
            }
        }
        else if (reasons.Count > 0 && !await tab.Owner.ConfirmAsync(new SafetyDecision(SafetyVerdict.Confirm, reasons)))
        {
            Status = Strings.NotRunCancelledInConfirmation;
            return;
        }

        IsBusy = true;
        Status = Strings.DataApplying;
        var clock = Stopwatch.StartNew();
        try
        {
            var session = Session(profile);
            session.LockTimeout = tab.Owner.LockTimeout;
            var result = await session.ApplyAsync(statements);
            var script = string.Join(Environment.NewLine, statements.Select(s => s.Readable).Append("COMMIT;"));
            var outcome = new QueryOutcome { TotalTime = clock.Elapsed, RecordsAffected = result.Applied, Error = result.Ok ? null : result.Error ?? Strings.DataRowChanged };
            if (!result.Ok)
            {
                var who = result.Failed is { } f ? _changes.Describe(f.RowKey) : "";
                Status = result.Error is { } error
                    ? string.Format(Strings.DataApplyFailed, who, FirstLine(error)) + (await tab.DescribeLockAsync(error) is { } holder ? " · " + holder : "")
                    : result.RowsAffected == 0
                        ? string.Format(Strings.DataApplyConflict, who)
                        : string.Format(Strings.DataApplyNotUnique, who, result.RowsAffected);
                return;
            }
            tab.Owner.Record(profile, script, StatementClassifier.Classify(statements[0].Readable), outcome);
            var (updates, deletes, inserts) = (_changes.UpdateCount, _changes.DeleteCount, _changes.InsertCount);
            ApplyToLoadedRows(result.Inserted);
            _changes.Clear();
            Changed();
            Status = string.Format(Strings.DataApplied, updates, deletes, clock.ElapsedMilliseconds, inserts)
                     + (_dbColumns.Any(c => c.IsComputed) ? Strings.DataComputedRefresh : "");
            // En una vista el INSERT no trae la fila de vuelta: se vuelve a leer para ver lo que puso el servidor.
            if (IsView && inserts > 0) _ = ReloadAfterApplyAsync(Status);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.DataApplyFailed, "", FirstLine(ex.Message));
        }
        finally
        {
            IsBusy = false;
            Redraw?.Invoke();
        }
    }

    private async Task ReloadAfterApplyAsync(string status)
    {
        await ReloadAsync();
        Status = status;
    }

    /// <summary>Lo guardado pasa a las filas cargadas (sin volver a leer: no se pierde dónde estaba uno).</summary>
    private void ApplyToLoadedRows(IReadOnlyDictionary<string, object[]> inserted)
    {
        if (_changes is null) return;
        // Las filas nuevas toman lo que devolvió el INSERT (la clave del trigger o la identity, los valores por defecto).
        foreach (var row in _changes.NewRows.Cast<object[]>())
        {
            if (inserted.TryGetValue(_changes.KeyOf(row), out var values) && values.Length == row.Length) Array.Copy(values, row, row.Length);
            else for (var c = 0; c < row.Length; c++) if (_changes.TryGetValue(row, c, out var v)) row[c] = v ?? DBNull.Value;
        }
        var byKey = _changes.Rows;
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!byKey.TryGetValue(_changes.KeyOf(Rows[i]), out var change)) continue;
            if (change.Deleted)
            {
                Rows.RemoveAt(i);
                continue;
            }
            foreach (var (column, value) in change.Values) Rows[i][column] = value ?? DBNull.Value;
        }
    }

    private TableDataSession Session(Core.Profiles.ConnectionProfile profile)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); // pestaña cerrada: no se abre otra conexión
        if (_session is not null && _session.Profile.Id == profile.Id) return _session;
        var password = tab.Owner.ProfileStore.GetPassword(profile.Id)
                       ?? throw new InvalidOperationException(string.Format(Strings.NoSavedPassword, profile.Name));
        return _session = new TableDataSession(profile, password);
    }

    private static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();

    partial void OnHasMoreChanged(bool value) => Redraw?.Invoke();

    partial void OnTotalCountChanged(long? value) => Redraw?.Invoke();

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _generation++;
        if (_session is not null) await _session.DisposeAsync();
        _session = null;
    }
}
