using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbStudio.App.Resources;
using FbStudio.Core.Monitoring;
using FbStudio.Core.Sql;
using FbStudio.Firebird;
using FbStudio.Workspace;

namespace FbStudio.App.ViewModels;

/// <summary>
/// Alta o edición de una consulta propia del monitoreo: nombre, SQL y qué columna trae el número de conexión, el de
/// sentencia y el SQL. «Probar» la ejecuta y ofrece sus columnas; las que se llaman como en MON$ se eligen solas.
/// </summary>
public sealed partial class MonitorQueryEditorViewModel : ObservableObject
{
    private readonly Func<string, Task<MonitorResult>> _test;
    private readonly Guid _id;

    public MonitorQueryEditorViewModel(CustomMonitorQuery? existing, string name, string sql, Func<string, Task<MonitorResult>> test)
    {
        _test = test;
        _id = existing?.Id ?? Guid.NewGuid();
        _name = name;
        Sql = new TextDocument(sql);
        _attachmentColumn = existing?.AttachmentColumn ?? "";
        _statementColumn = existing?.StatementColumn ?? "";
        _sqlColumn = existing?.SqlColumn ?? "";
        IsNew = existing is null;
        Columns = new[] { _attachmentColumn, _statementColumn, _sqlColumn }.Where(c => c.Length > 0).Distinct().ToList();
    }

    public bool IsNew { get; }
    public string Title => IsNew ? Strings.MonQueryNewTitle : Strings.MonQueryEditTitle;
    public TextDocument Sql { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _attachmentColumn;
    [ObservableProperty] private string _statementColumn;
    [ObservableProperty] private string _sqlColumn;

    /// <summary>Columnas del último «Probar» (para elegir en las listas).</summary>
    [ObservableProperty] private IReadOnlyList<string> _columns;

    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private bool _testFailed;
    [ObservableProperty] private string? _error;

    [RelayCommand]
    public async Task TestAsync()
    {
        if (ValidateSql() is { } problem)
        {
            (TestResult, TestFailed) = (problem, true);
            return;
        }
        TestResult = Strings.MonQueryTesting;
        try
        {
            var result = await _test(Sql.Text);
            Columns = result.Columns.Select(c => c.Name).ToList();
            // Las que no se eligieron, se detectan por el nombre (MON$ATTACHMENT_ID, "Attachment ID", SQL_TEXT…).
            if (AttachmentColumn.Length == 0 && MonitorColumns.Find(result.Columns, null, MonitorColumns.AttachmentNames) is { } a) AttachmentColumn = Columns[a];
            if (StatementColumn.Length == 0 && MonitorColumns.Find(result.Columns, null, MonitorColumns.StatementNames) is { } s) StatementColumn = Columns[s];
            if (SqlColumn.Length == 0 && MonitorColumns.Find(result.Columns, null, MonitorColumns.SqlNames) is { } q) SqlColumn = Columns[q];
            (TestResult, TestFailed) = (string.Format(Strings.MonQueryTested, result.Rows.Count, string.Join(", ", Columns)), false);
        }
        catch (Exception ex)
        {
            (TestResult, TestFailed) = (string.Format(Strings.MonFailed, ex.Message.Split('\n', 2)[0].Trim()), true);
        }
    }

    private string? ValidateSql()
    {
        var sql = Sql.Text.Trim();
        if (sql.Length == 0) return Strings.MonQueryNoSql;
        // Una sola sentencia y de lectura: el monitoreo la corre cada pocos segundos.
        var statements = SqlScriptSplitter.Split(sql);
        if (statements.Count != 1 || StatementClassifier.Classify(statements[0].Text).Kind != StatementKind.Select) return Strings.MonQueryOnlySelect;
        return null;
    }

    /// <summary>Lo que falta para poder guardar, o null.</summary>
    public string? Validate()
    {
        Error = Name.Trim().Length == 0 ? Strings.MonQueryNoName : ValidateSql();
        return Error;
    }

    public CustomMonitorQuery ToQuery() => new(_id, Name.Trim(), Sql.Text.Trim().TrimEnd(';'),
        Blank(AttachmentColumn), Blank(StatementColumn), Blank(SqlColumn));

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
