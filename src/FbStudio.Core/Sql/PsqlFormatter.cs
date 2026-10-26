namespace FbStudio.Core.Sql;

/// <summary>
/// Formatea los bloques PSQL (F-32): CREATE, ALTER y RECREATE de procedimientos, funciones y triggers, y EXECUTE BLOCK.
/// El encabezado queda como se escribió (solo las palabras clave en mayúsculas); el cuerpo va una sentencia por renglón,
/// sangrado por BEGIN … END, IF … THEN … ELSE, WHILE … DO, FOR SELECT … DO y WHEN … DO, y las consultas de adentro se
/// formatean como las demás. Si encuentra algo que no reconoce (un subprocedimiento, un bloque sin cerrar, un cuerpo
/// externo), devuelve null y el bloque queda tal cual: nunca cambia lo que hace el código.
/// </summary>
internal static class PsqlFormatter
{
    private static readonly HashSet<string> HeaderWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE", "ALTER", "RECREATE", "OR", "PROCEDURE", "FUNCTION", "TRIGGER", "RETURNS", "AS", "EXECUTE", "BLOCK", "FOR",
        "ACTIVE", "INACTIVE", "BEFORE", "AFTER", "INSERT", "UPDATE", "DELETE", "POSITION", "ON", "CONNECT", "DISCONNECT",
        "TRANSACTION", "START", "COMMIT", "ROLLBACK", "DETERMINISTIC", "TYPE", "OF", "COLUMN", "DEFAULT", "NOT", "NULL",
        "CHARACTER", "SET", "COLLATE", "DATABASE",
    };

    private sealed class Unparsed : Exception;

    /// <summary>El bloque formateado, o null si no es un bloque PSQL o tiene algo que no se reconoce.</summary>
    public static string? Format(string sql, SqlFormatOptions options)
    {
        var tokens = SqlLexer.Tokenize(sql, comments: true).ToList();
        if (BodyStart(tokens) is not { } body) return null;
        try
        {
            var writer = new Writer(sql, tokens, options, body);
            return writer.Write();
        }
        catch (Unparsed)
        {
            return null;
        }
    }

    /// <summary>Dónde empieza el cuerpo: lo que sigue al AS del encabezado (fuera de paréntesis).</summary>
    private static int? BodyStart(List<SqlToken> tokens)
    {
        var words = tokens.Where(t => t.Kind != SqlTokenKind.Comment).Take(5).Select(t => t.Text.ToUpperInvariant()).ToList();
        var isBlock = words is ["EXECUTE", "BLOCK", ..];
        var verb = words.Count > 0 ? words[0] : "";
        var kindAt = verb == "CREATE" && words.Count > 2 && words[1] == "OR" ? 3 : 1;
        var isRoutine = verb is "CREATE" or "ALTER" or "RECREATE" && words.Count > kindAt && words[kindAt] is "PROCEDURE" or "FUNCTION" or "TRIGGER";
        if (!isBlock && !isRoutine) return null;
        var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == SqlTokenKind.Symbol && t.Text == "(") depth++;
            else if (t.Kind == SqlTokenKind.Symbol && t.Text == ")") depth--;
            else if (depth == 0 && t.Kind == SqlTokenKind.Word && t.Text.Equals("AS", StringComparison.OrdinalIgnoreCase)) return i + 1;
        }
        return null;
    }

    private sealed class Writer(string sql, List<SqlToken> tokens, SqlFormatOptions options, int body)
    {
        private readonly List<string> _lines = [];
        private int _i = body;

        public string Write()
        {
            _lines.Add(Header());
            // Declaraciones (DECLARE VARIABLE, cursores) al nivel del encabezado; después, el BEGIN … END principal.
            while (true)
            {
                Comments(0);
                if (Peek() is not { } t) throw new Unparsed();
                if (IsWord(t, "BEGIN")) break;
                if (!IsWord(t, "DECLARE") || IsWord(Peek(1), "PROCEDURE") || IsWord(Peek(1), "FUNCTION")) throw new Unparsed();
                Simple(0);
            }
            Block(0, "");
            Comments(0);
            if (_i < tokens.Count) throw new Unparsed();
            return string.Join("\n", _lines);
        }

        // ── Encabezado ──

        /// <summary>El encabezado hasta el AS, como se escribió, con las palabras clave en mayúsculas si se pide.</summary>
        private string Header()
        {
            var end = tokens[body - 1].End;
            if (!options.UppercaseKeywords) return sql[..end].TrimEnd();
            var sb = new System.Text.StringBuilder();
            var at = 0;
            foreach (var t in tokens.Take(body))
            {
                sb.Append(sql, at, t.Start - at);
                sb.Append(t.Kind == SqlTokenKind.Word && (HeaderWords.Contains(t.Text) || SqlFormatter.IsKeyword(t.Text)) ? t.Text.ToUpperInvariant() : t.Text);
                at = t.End;
            }
            return sb.ToString().TrimEnd();
        }

        // ── Sentencias ──

        private void Statement(int level, string prefix = "")
        {
            Comments(level);
            var t = Peek() ?? throw new Unparsed();
            // Una etiqueta («SIGUIENTE: WHILE …») va adelante del bucle.
            if (t.Kind == SqlTokenKind.Word && Peek(1) is { Kind: SqlTokenKind.Symbol, Text: ":" } && (IsWord(Peek(2), "WHILE") || IsWord(Peek(2), "FOR")))
            {
                _i += 2;
                Statement(level, prefix + t.Text + ": ");
                return;
            }
            if (IsWord(t, "BEGIN")) Block(level, prefix);
            else if (IsWord(t, "IF")) If(level, prefix);
            else if (IsWord(t, "WHILE")) While(level, prefix);
            else if (IsWord(t, "FOR")) For(level, prefix);
            else if (IsWord(t, "WHEN")) When(level, prefix);
            else if (IsWord(t, "IN") && IsWord(Peek(1), "AUTONOMOUS")) Autonomous(level, prefix);
            else if (IsWord(t, "DECLARE")) throw new Unparsed();   // un DECLARE en el medio del código no es PSQL de Firebird 3
            else Simple(level, prefix);
        }

        private void Block(int level, string prefix)
        {
            Emit(level, prefix + Take("BEGIN"));
            while (true)
            {
                Comments(level + 1);
                if (Peek() is not { } t) throw new Unparsed();
                if (IsWord(t, "END")) break;
                Statement(level + 1);
            }
            var end = Take("END");
            if (Peek() is { Kind: SqlTokenKind.Symbol, Text: ";" })
            {
                end += ";";
                _i++;
            }
            Emit(level, end);
        }

        /// <summary>Lo que va después de THEN, DO o ELSE: un BEGIN … END al mismo nivel, o una sentencia sangrada.</summary>
        private void Branch(int level)
        {
            Comments(level + 1);
            if (IsWord(Peek(), "BEGIN")) Block(level, "");
            else Statement(level + 1);
        }

        private void If(int level, string prefix)
        {
            var keyword = Take("IF");
            var condition = Parenthesized();
            Emit(level, $"{prefix}{keyword} ({condition}) {Take("THEN")}");
            Branch(level);
            if (!IsWord(PeekCode(), "ELSE")) return;
            Comments(level);
            var otherwise = Take("ELSE");
            if (IsWord(Peek(), "IF"))
            {
                If(level, otherwise + " ");
                return;
            }
            Emit(level, otherwise);
            Branch(level);
        }

        private void While(int level, string prefix)
        {
            var keyword = Take("WHILE");
            var condition = Parenthesized();
            Emit(level, $"{prefix}{keyword} ({condition}) {Take("DO")}");
            Branch(level);
        }

        /// <summary>FOR SELECT … INTO … DO, FOR EXECUTE STATEMENT … DO: la consulta formateada, alineada después de «FOR ».</summary>
        private void For(int level, string prefix)
        {
            var first = $"{prefix}{Take("FOR")} ";
            var (start, end) = UntilDo();
            var inner = Format(start, end).Split('\n');
            Emit(level, first + inner[0]);
            foreach (var line in inner.Skip(1)) Emit(level, new string(' ', first.Length) + line);
            Emit(level, Cased(tokens[end]));
            Branch(level);
        }

        /// <summary>WHEN ANY DO, WHEN SQLCODE -803 DO, WHEN EXCEPTION E DO…: los manejadores de errores de un bloque.</summary>
        private void When(int level, string prefix)
        {
            var (start, end) = UntilDo();
            Emit(level, $"{prefix}{Format(start, end)} {Cased(tokens[end])}");
            Branch(level);
        }

        private void Autonomous(int level, string prefix)
        {
            var (start, end) = UntilDo();
            Emit(level, $"{prefix}{Format(start, end)} {Cased(tokens[end])}");
            Branch(level);
        }

        /// <summary>Una sentencia hasta su «;»: asignación, SUSPEND, EXCEPTION, una consulta (formateada)…</summary>
        private void Simple(int level, string prefix = "")
        {
            var start = _i;
            var depth = 0;
            var cases = 0;
            while (true)
            {
                if (Peek() is not { } t) throw new Unparsed();
                if (t.Kind == SqlTokenKind.Symbol && t.Text == "(") depth++;
                else if (t.Kind == SqlTokenKind.Symbol && t.Text == ")") depth--;
                else if (depth == 0 && t.Kind == SqlTokenKind.Symbol && t.Text == ";") break;
                else if (IsWord(t, "CASE")) cases++;
                else if (IsWord(t, "END") && cases > 0) cases--;
                // Un END o un BEGIN sueltos sin «;» antes: el bloque no está como se espera.
                else if (depth == 0 && (IsWord(t, "END") || IsWord(t, "BEGIN"))) throw new Unparsed();
                _i++;
            }
            if (_i == start) throw new Unparsed();
            var text = Format(start, _i);
            _i++; // el «;»
            var lines = text.Split('\n');
            lines[^1] += ";";
            Emit(level, prefix + lines[0]);
            foreach (var line in lines.Skip(1)) Emit(level, line);
        }

        // ── Ayudas ──

        /// <summary>Los comentarios sueltos: en su renglón, o al final del anterior si estaban en el mismo renglón.</summary>
        private void Comments(int level)
        {
            while (Peek() is { Kind: SqlTokenKind.Comment } c)
            {
                var previousEnd = _i > 0 ? tokens[_i - 1].End : 0;
                var sameLine = _lines.Count > 1 && !sql.AsSpan(previousEnd, c.Start - previousEnd).Contains('\n');
                if (sameLine) _lines[^1] += " " + c.Text;
                else Emit(level, c.Text);
                _i++;
            }
        }

        /// <summary>El texto de las sentencias entre <paramref name="start"/> y <paramref name="end"/>, formateado como SQL.</summary>
        private string Format(int start, int end)
        {
            if (end <= start) throw new Unparsed();
            var text = sql[tokens[start].Start..tokens[end - 1].End];
            return SqlFormatter.FormatStatement(text, options).Replace("\r", "");
        }

        /// <summary>«( … )»: lo de adentro, formateado (sin los paréntesis).</summary>
        private string Parenthesized()
        {
            if (Peek() is not { Kind: SqlTokenKind.Symbol, Text: "(" }) throw new Unparsed();
            var start = ++_i;
            var depth = 1;
            while (depth > 0)
            {
                if (Peek() is not { } t) throw new Unparsed();
                if (t.Kind == SqlTokenKind.Symbol && t.Text == "(") depth++;
                else if (t.Kind == SqlTokenKind.Symbol && t.Text == ")") depth--;
                _i++;
            }
            return Format(start, _i - 1);
        }

        /// <summary>Hasta el DO de su nivel (sin incluirlo); deja la posición después del DO.</summary>
        private (int Start, int End) UntilDo()
        {
            var start = _i;
            var depth = 0;
            while (true)
            {
                if (Peek() is not { } t) throw new Unparsed();
                if (t.Kind == SqlTokenKind.Symbol && t.Text == "(") depth++;
                else if (t.Kind == SqlTokenKind.Symbol && t.Text == ")") depth--;
                else if (depth == 0 && IsWord(t, "DO")) break;
                else if (depth == 0 && t.Kind == SqlTokenKind.Symbol && t.Text == ";") throw new Unparsed();
                _i++;
            }
            var end = _i++;
            return (start, end);
        }

        /// <summary>La palabra que sigue, que tiene que ser <paramref name="word"/>, como se escribió (en mayúsculas si se pide).</summary>
        private string Take(string word)
        {
            if (Peek() is not { } t || !IsWord(t, word)) throw new Unparsed();
            _i++;
            return Cased(t);
        }

        private string Cased(SqlToken t) => options.UppercaseKeywords ? t.Text.ToUpperInvariant() : t.Text;

        private SqlToken? Peek(int ahead = 0) => _i + ahead < tokens.Count ? tokens[_i + ahead] : null;

        /// <summary>Lo próximo que no es un comentario (sin avanzar).</summary>
        private SqlToken? PeekCode()
        {
            for (var j = _i; j < tokens.Count; j++)
                if (tokens[j].Kind != SqlTokenKind.Comment) return tokens[j];
            return null;
        }

        private static bool IsWord(SqlToken? t, string word) =>
            t is { Kind: SqlTokenKind.Word } w && w.Text.Equals(word, StringComparison.OrdinalIgnoreCase);

        private void Emit(int level, string text)
        {
            var indent = new string(' ', level * options.IndentSize);
            var lines = text.Split('\n');
            _lines.Add(indent + lines[0]);
            foreach (var line in lines.Skip(1)) _lines.Add(line.Length == 0 ? line : indent + line);
        }
    }
}
