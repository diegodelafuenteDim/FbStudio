using FbStudio.Core.Profiles;

namespace FbStudio.Core.Sql;

public enum SafetyVerdict
{
    Allow,
    Confirm,
    Block,
}

public sealed record SafetyDecision(SafetyVerdict Verdict, IReadOnlyList<string> Reasons)
{
    public static SafetyDecision Allow { get; } = new(SafetyVerdict.Allow, []);
}

/// <summary>
/// Lo que la sentencia sola no dice: si va a correr dentro de una transacción de escritura abierta y qué procedimientos
/// o funciones usa (<see cref="SqlRoutines"/>).
/// </summary>
public sealed record SafetyContext(bool WriteTransactionOpen, IReadOnlyList<string> Routines)
{
    public static SafetyContext None { get; } = new(false, []);
}

/// <summary>
/// Reglas que se evalúan ANTES de mandar una sentencia al servidor.
/// Es una red de seguridad de la herramienta, no reemplaza los permisos de la base.
/// </summary>
public static class SafetyPolicy
{
    public static SafetyDecision Evaluate(ConnectionProfile profile, StatementInfo statement, SafetyContext? context = null)
    {
        if (statement.Kind == StatementKind.Empty)
            return new SafetyDecision(SafetyVerdict.Block, [Resources.CoreStrings.SafetyNothingToRun]);

        if (profile.ReadOnly && statement.Modifies)
            return new SafetyDecision(SafetyVerdict.Block,
                [string.Format(Resources.CoreStrings.SafetyReadOnly, profile.Name, statement.Keyword)]);

        // Un generador se mueve aunque la transacción sea de solo lectura: un registro de solo lectura no lo toca.
        if (profile.ReadOnly && statement.ChangesSequences)
            return new SafetyDecision(SafetyVerdict.Block, [string.Format(Resources.CoreStrings.SafetyReadOnlySequence, profile.Name)]);

        if (statement.Kind == StatementKind.Transaction && statement.Keyword is "SET TRANSACTION" or "SAVEPOINT" or "RELEASE")
            return new SafetyDecision(SafetyVerdict.Block,
                [string.Format(Resources.CoreStrings.SafetyNotSupported, statement.Keyword)]);

        var reasons = new List<string>();
        if (profile.IsProduction && statement.Modifies)
            reasons.Add(string.Format(Resources.CoreStrings.SafetyProduction, statement.Keyword, profile.Name));
        if (statement.AffectsAllRows)
            reasons.Add(string.Format(Resources.CoreStrings.SafetyNoWhere, statement.Keyword));
        // Sin transacción abierta un SELECT corre en una de solo lectura y un procedimiento que escribe falla; dentro de
        // la de escritura lo que haga queda con lo pendiente y se graba con el Commit (D-73).
        if (profile.IsProduction && statement.Kind == StatementKind.Select && context is { WriteTransactionOpen: true, Routines.Count: > 0 })
            reasons.Add(string.Format(Resources.CoreStrings.SafetyRoutineInTransaction, string.Join(", ", context.Routines), profile.Name));

        return reasons.Count == 0 ? SafetyDecision.Allow : new SafetyDecision(SafetyVerdict.Confirm, reasons);
    }
}
