namespace FbStudio.Core.Sql;

/// <summary>Qué es una orden de isql y qué hace FbStudio con ella en un script (D-120).</summary>
public enum IsqlKind
{
    // Se interpretan.

    /// <summary>SET BAIL [ON|OFF]: desde esa línea, el script se detiene en el primer error o sigue.</summary>
    Bail,

    /// <summary>QUIT o EXIT sueltos: el script termina ahí, sin confirmar ni deshacer nada.</summary>
    End,

    // Se ignoran, con un renglón en el registro.

    /// <summary>Cambian cómo muestra isql los resultados (ECHO, LIST, HEADING, COUNT, STATS, PLAN, WIDTH…).</summary>
    Display,

    /// <summary>SET NAMES: la conexión usa el charset del registro.</summary>
    Names,

    /// <summary>SET SQL DIALECT 3: FbStudio ya trabaja en dialecto 3.</summary>
    Dialect,

    /// <summary>SET AUTODDL: FbStudio trabaja siempre como AUTODDL OFF (el DDL queda pendiente hasta el COMMIT).</summary>
    AutoDdl,

    /// <summary>SET CLIENTLIB: de los scripts que arman otras herramientas (la biblioteca cliente a usar).</summary>
    ClientLib,

    /// <summary>SHOW … y HELP: listan cosas en la consola de isql.</summary>
    Show,

    /// <summary>OUTPUT sin archivo: vuelve la salida a la pantalla.</summary>
    OutputOff,

    // Se rechazan: el script no corre.

    /// <summary>SHELL: un comando del sistema.</summary>
    Shell,

    /// <summary>INPUT: corre otro archivo.</summary>
    Input,

    /// <summary>OUTPUT con un archivo: escribe la salida ahí.</summary>
    Output,

    /// <summary>CONNECT, CREATE DATABASE o DROP DATABASE: otra base, no la de la pestaña.</summary>
    OtherDatabase,

    /// <summary>SET SQL DIALECT 1 o 2: el script se escribió para otro dialecto.</summary>
    OtherDialect,

    /// <summary>SET PLANONLY ON: isql solo prepararía las sentencias; ignorarlo las ejecutaría.</summary>
    PlanOnly,

    /// <summary>EDIT, BLOBDUMP, BLOBVIEW, COPY o ADD: interactivas o que escriben archivos.</summary>
    Interactive,
}

/// <param name="Kind">Qué orden es.</param>
/// <param name="Name">La orden con su nombre completo, en mayúsculas («SET AUTODDL» aunque diga «SET AUTO»).</param>
/// <param name="Argument">Lo que viene después, tal como se escribió («UTF8», «ON», «/db/x.sql»); "" si nada.</param>
public sealed record IsqlCommand(IsqlKind Kind, string Name, string Argument)
{
    /// <summary>Lo rechazado frena el script antes de la primera sentencia.</summary>
    public bool IsRejected => Kind >= IsqlKind.Shell;

    /// <summary>Lo ignorado no se ejecuta y queda un renglón en el registro.</summary>
    public bool IsIgnored => Kind is >= IsqlKind.Display and < IsqlKind.Shell;

    /// <summary>ON = true, OFF = false; null si no dice (SET BAIL sin argumento alterna, como en isql).</summary>
    public bool? Switch => Argument.ToUpperInvariant() switch
    {
        "ON" => true,
        "OFF" => false,
        _ => null,
    };

    /// <summary>«SET NAMES UTF8», «SHELL», «INPUT /db/x.sql».</summary>
    public string Text => Argument.Length == 0 ? Name : $"{Name} {Argument}";
}

/// <summary>
/// Reconoce las órdenes de isql (el cliente de consola de Firebird) que aparecen en los scripts: no son SQL, así que
/// el servidor no las conoce. Lo que también es SQL de Firebird (SET GENERATOR, SET STATISTICS INDEX, SET TRANSACTION,
/// SET ROLE, SET TIME ZONE…) no se toma como orden de isql. SET TERM lo resuelve <see cref="SqlScriptSplitter"/>.
/// </summary>
public static class IsqlCommands
{
    /// <summary>Las de SET … que solo cambian cómo muestra isql los resultados: nombre completo y abreviatura mínima.</summary>
    private static readonly (string Name, int Min)[] DisplaySettings =
    [
        ("ECHO", 4), ("LIST", 4), ("HEADING", 4), ("COUNT", 5), ("STATS", 4), ("PLAN", 4), ("WIDTH", 5),
        ("BLOBDISPLAY", 4), ("WARNINGS", 4), ("WNG", 3), ("ROWCOUNT", 4), ("EXPLAIN", 7), ("SQLDA_DISPLAY", 5),
        ("PER_TABLE_STATS", 7), ("KEEP_TRAN_PARAMS", 9), ("MAXROWS", 7), ("EXEC_PATH_DISPLAY", 9),
        ("LOCAL_TIMEOUT", 7), ("AUTOTERM", 8), ("TERM", 4),
    ];

    /// <summary>La orden de isql de la sentencia, o null si es SQL (va al servidor).</summary>
    public static IsqlCommand? Parse(string statement) => Parse(statement, SqlLexer.Tokenize(statement).ToList());

    internal static IsqlCommand? Parse(string statement, IReadOnlyList<SqlToken> tokens)
    {
        if (tokens.Count == 0 || tokens[0].Kind != SqlTokenKind.Word) return null;
        var first = tokens[0].Text.ToUpperInvariant();
        // Sin el «;» del final, si vino con él (el separador del script ya lo saca).
        string Rest(int from) => from < tokens.Count ? statement[tokens[from].Start..].Trim().TrimEnd(';').TrimEnd() : "";
        IsqlCommand Make(IsqlKind kind, string name, int argumentFrom) => new(kind, name, Rest(argumentFrom));

        switch (first)
        {
            case "SET":
                return ParseSet(tokens, Rest);
            case "QUIT" or "EXIT" when tokens.Count == 1:
                return Make(IsqlKind.End, first, 1);
            case "SHOW" or "HELP":
                return Make(IsqlKind.Show, first, 1);
            case "SHELL":
                return Make(IsqlKind.Shell, first, 1);
            case "INPUT":
                return Make(IsqlKind.Input, first, 1);
            case "OUTPUT" or "OUT":
                return Make(tokens.Count == 1 ? IsqlKind.OutputOff : IsqlKind.Output, "OUTPUT", 1);
            case "CONNECT":
                return Make(IsqlKind.OtherDatabase, first, 1);
            case "CREATE" when tokens.Count > 1 && (tokens[1].IsWord("DATABASE") || tokens[1].IsWord("SCHEMA")):
                return Make(IsqlKind.OtherDatabase, "CREATE " + tokens[1].Text.ToUpperInvariant(), 2);
            case "DROP" when tokens.Count > 1 && tokens[1].IsWord("DATABASE"):
                return Make(IsqlKind.OtherDatabase, "DROP DATABASE", 2);
            case "EDIT" or "BLOBDUMP" or "BLOBVIEW" or "COPY" or "ADD":
                return Make(IsqlKind.Interactive, first, 1);
            default:
                return null;
        }
    }

    private static IsqlCommand? ParseSet(IReadOnlyList<SqlToken> tokens, Func<int, string> rest)
    {
        if (tokens.Count == 1) return new IsqlCommand(IsqlKind.Display, "SET", "");
        if (tokens[1].Kind != SqlTokenKind.Word) return null;
        var word = tokens[1].Text.ToUpperInvariant();
        var argument = rest(2);
        var onOff = argument.Length == 0 || argument.Equals("ON", StringComparison.OrdinalIgnoreCase) || argument.Equals("OFF", StringComparison.OrdinalIgnoreCase);

        switch (word)
        {
            case "BAIL":
                return new IsqlCommand(IsqlKind.Bail, "SET BAIL", argument);
            case "NAMES":
                return new IsqlCommand(IsqlKind.Names, "SET NAMES", argument);
            case "SQL" when tokens.Count > 2 && tokens[2].IsWord("DIALECT"):
                var dialect = rest(3);
                return new IsqlCommand(dialect == "3" ? IsqlKind.Dialect : IsqlKind.OtherDialect, "SET SQL DIALECT", dialect);
            case "CLIENTLIB":
                return new IsqlCommand(IsqlKind.ClientLib, "SET CLIENTLIB", argument);
            case "PLANONLY":
                return new IsqlCommand(argument.Equals("OFF", StringComparison.OrdinalIgnoreCase) ? IsqlKind.Display : IsqlKind.PlanOnly, "SET PLANONLY", argument);
            // SET TIME ZONE es SQL de Firebird 4; el SET TIME de isql va solo o con ON/OFF.
            case "TIME":
                return onOff ? new IsqlCommand(IsqlKind.Display, "SET TIME", argument) : null;
        }
        if (word.Length >= 4 && "AUTODDL".StartsWith(word, StringComparison.Ordinal))
            return new IsqlCommand(IsqlKind.AutoDdl, "SET AUTODDL", argument);
        // Las abreviaturas de isql («SET STAT», «SET HEAD»); SET STATISTICS INDEX no es prefijo de STATS: va al servidor.
        foreach (var (name, min) in DisplaySettings)
            if (word.Length >= min && name.StartsWith(word, StringComparison.Ordinal))
                return new IsqlCommand(IsqlKind.Display, "SET " + name, argument);
        return null;
    }
}
