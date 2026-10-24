namespace FbStudio.Core.Metadata;

/// <summary>Traduce RDB$TRIGGER_TYPE al texto del DDL ("BEFORE INSERT OR UPDATE", "ON CONNECT", …).</summary>
public static class TriggerTypeFormatter
{
    private const long DatabaseTrigger = 8192;
    private const long DdlTrigger = 16384;

    public static string Describe(long type)
    {
        if ((type & DdlTrigger) != 0)
        {
            // Triggers DDL (Firebird 3): bit 0 = AFTER, el resto son los eventos. Se muestra la forma general.
            return ((type & 1) == 1 ? "AFTER" : "BEFORE") + " ANY DDL STATEMENT";
        }
        if (type >= DatabaseTrigger)
        {
            return (type - DatabaseTrigger) switch
            {
                0 => "ON CONNECT",
                1 => "ON DISCONNECT",
                2 => "ON TRANSACTION START",
                3 => "ON TRANSACTION COMMIT",
                4 => "ON TRANSACTION ROLLBACK",
                var n => $"ON (tipo {n + DatabaseTrigger})",
            };
        }

        // DML: el bit menos significativo de (tipo + 1) es BEFORE/AFTER y cada par de bits siguiente es una acción.
        var prefix = ((type + 1) & 1) == 0 ? "BEFORE" : "AFTER";
        var actions = new List<string>(3);
        for (var slot = 1; slot <= 3; slot++)
        {
            var action = ((type + 1) >> (slot * 2 - 1)) & 3;
            if (action == 0) break;
            actions.Add(action switch { 1 => "INSERT", 2 => "UPDATE", _ => "DELETE" });
        }
        return actions.Count == 0 ? $"{prefix} (tipo {type})" : $"{prefix} {string.Join(" OR ", actions)}";
    }

    public static bool IsDatabaseTrigger(long type) => type >= DatabaseTrigger;
}
