using System.Globalization;
using Usharp.Syntax;

namespace Usharp.Compute;

public enum ComputeLiteralKind
{
    Int,
    Decimal,
    Bool,
    String
}

public abstract record ComputeExpression(int Line);

public sealed record ComputeLiteralExpression(ComputeLiteralKind Kind, string Text, int Line)
    : ComputeExpression(Line);

public sealed record ComputeIdentifierExpression(string Name, int Line) : ComputeExpression(Line);

public sealed record ComputeKeyReferenceExpression(string SpaceId, string Coordinate, int Line)
    : ComputeExpression(Line);

public sealed record ComputeCallExpression(string Name, IReadOnlyList<ComputeExpression> Arguments, int Line)
    : ComputeExpression(Line);

public sealed record ComputeUnaryExpression(string Operator, ComputeExpression Operand, int Line)
    : ComputeExpression(Line);

public sealed record ComputeBinaryExpression(string Operator, ComputeExpression Left,
    ComputeExpression Right, int Line) : ComputeExpression(Line);

public sealed record ComputeListExpression(IReadOnlyList<ComputeExpression> Items, int Line)
    : ComputeExpression(Line);

public sealed record ComputeMapExpression(IReadOnlyList<KeyValuePair<string, ComputeExpression>> Entries, int Line)
    : ComputeExpression(Line);

public abstract record ComputeStatement(int Line);

public sealed record ComputeLetStatement(string Name, bool Mutable, ComputeExpression Value, int Line)
    : ComputeStatement(Line);

public sealed record ComputeSetStatement(string Name, ComputeExpression Value, int Line)
    : ComputeStatement(Line);

public sealed record ComputeIfStatement(ComputeExpression Condition, IReadOnlyList<ComputeStatement> Then,
    IReadOnlyList<ComputeStatement>? Else, int Line) : ComputeStatement(Line);

public sealed record ComputeLoopStatement(string Variable, ComputeExpression From, ComputeExpression To,
    long Step, bool StepDeclared, IReadOnlyList<ComputeStatement> Body, int Line) : ComputeStatement(Line);

public sealed record ComputeReturnStatement(ComputeExpression? Value, int Line) : ComputeStatement(Line);

public sealed record ComputeCallStatement(ComputeCallExpression Call, int Line) : ComputeStatement(Line);

internal enum ComputeTokenKind
{
    Ident,
    Int,
    Decimal,
    String,
    Symbol,
    Newline,
    End
}

internal sealed record ComputeToken(ComputeTokenKind Kind, string Text, int Line);

/// <summary>
/// 语句通道：只读 <see cref="Plate.DslMethod.Model.DslDeclaration.Continuation"/>（缩进续行），
/// 表达式/语句 grammar 与声明头完全分离。
/// </summary>
public static class ComputeBodyParser
{
    public static bool TryParse(string file, IReadOnlyList<string> lines, int firstLine,
        out IReadOnlyList<ComputeStatement> statements, out IReadOnlyList<ComputeDiagnostic> diagnostics)
    {
        statements = Array.Empty<ComputeStatement>();
        var errors = new List<ComputeDiagnostic>();
        var tokens = new List<ComputeToken>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineNumber = firstLine + i;
            if (!TryTokenizeLine(file, line, lineNumber, tokens, out var error))
            {
                errors.Add(error!);
                diagnostics = errors;
                return false;
            }

            tokens.Add(new ComputeToken(ComputeTokenKind.Newline, "", lineNumber));
        }

        var lastLine = lines.Count == 0 ? firstLine : firstLine + lines.Count - 1;
        tokens.Add(new ComputeToken(ComputeTokenKind.End, "", lastLine));

        var parser = new Parser(file, tokens, errors);
        var parsed = parser.ParseProgram();
        if (errors.Count > 0)
        {
            diagnostics = errors;
            return false;
        }

        statements = parsed;
        diagnostics = Array.Empty<ComputeDiagnostic>();
        return true;
    }

    private static bool TryTokenizeLine(string file, string line, int lineNumber, List<ComputeToken> tokens,
        out ComputeDiagnostic? error)
    {
        error = null;
        var hash = IndexOfComment(line);
        var text = hash >= 0 ? line[..hash] : line;
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '"')
            {
                if (!TryReadString(file, text, ref i, lineNumber, out var literal, out error))
                {
                    return false;
                }

                tokens.Add(new ComputeToken(ComputeTokenKind.String, literal, lineNumber));
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                if (!TryReadNumber(file, text, ref i, lineNumber, tokens, out error))
                {
                    return false;
                }

                continue;
            }

            if (UsharpIdentifier.TryRead(text, ref i, out var identifier))
            {
                tokens.Add(new ComputeToken(ComputeTokenKind.Ident, identifier, lineNumber));
                continue;
            }

            var two = i + 1 < text.Length ? text.Substring(i, 2) : "";
            if (two is "==" or "!=" or "<=" or ">=")
            {
                tokens.Add(new ComputeToken(ComputeTokenKind.Symbol, two, lineNumber));
                i += 2;
                continue;
            }

            if ("+-*/%()[]{}.,:$=<>".Contains(c))
            {
                tokens.Add(new ComputeToken(ComputeTokenKind.Symbol, c.ToString(), lineNumber));
                i++;
                continue;
            }

            error = new ComputeDiagnostic(ComputeCodes.SyntaxInvalid,
                $"非法字符 '{c}'", file, lineNumber);
            return false;
        }

        return true;
    }

    private static int IndexOfComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
            if (c == '#' && !inString) return i;
        }

        return -1;
    }

    private static bool TryReadString(string file, string text, ref int index, int line,
        out string value, out ComputeDiagnostic? error)
    {
        error = null;
        value = "";
        var builder = new System.Text.StringBuilder();
        index++;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == '"')
            {
                index++;
                value = builder.ToString();
                return true;
            }

            if (c == '\\' && index + 1 < text.Length)
            {
                var escape = text[index + 1];
                builder.Append(escape switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => escape
                });
                index += 2;
                continue;
            }

            builder.Append(c);
            index++;
        }

        error = new ComputeDiagnostic(ComputeCodes.SyntaxInvalid, "字符串未闭合", file, line);
        return false;
    }

    private static bool TryReadNumber(string file, string text, ref int index, int line,
        List<ComputeToken> tokens, out ComputeDiagnostic? error)
    {
        error = null;
        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
        var isDecimal = false;
        if (index < text.Length && text[index] == '.')
        {
            isDecimal = true;
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
        }

        var literal = text[start..index];
        if (isDecimal && literal.EndsWith(".", StringComparison.Ordinal))
        {
            error = new ComputeDiagnostic(ComputeCodes.SyntaxInvalid, $"小数 '{literal}' 缺少小数位", file, line);
            return false;
        }

        tokens.Add(new ComputeToken(isDecimal ? ComputeTokenKind.Decimal : ComputeTokenKind.Int, literal, line));
        return true;
    }

    internal sealed class Parser
    {
        private readonly string _file;
        private readonly List<ComputeToken> _tokens;
        private readonly List<ComputeDiagnostic> _errors;
        private int _index;

        public Parser(string file, List<ComputeToken> tokens, List<ComputeDiagnostic> errors)
        {
            _file = file;
            _tokens = tokens;
            _errors = errors;
        }

        private ComputeToken Current => _tokens[Math.Min(_index, _tokens.Count - 1)];

        public IReadOnlyList<ComputeStatement> ParseProgram()
        {
            var statements = ParseStatements(null);
            if (Current.Kind != ComputeTokenKind.End)
            {
                Error(Current, Current.Text == "end" ? "多余的 end" : $"多余 token '{Current.Text}'");
            }

            return statements;
        }

        private List<ComputeStatement> ParseStatements(ISet<string>? stopWords)
        {
            var statements = new List<ComputeStatement>();
            while (true)
            {
                SkipNewlines();
                var token = Current;
                if (token.Kind == ComputeTokenKind.End)
                {
                    if (stopWords is not null)
                    {
                        Error(token, "缺少 end（块未闭合）");
                        _index++;
                    }

                    return statements;
                }

                if (stopWords is not null && token.Kind == ComputeTokenKind.Ident
                    && stopWords.Contains(token.Text))
                {
                    return statements;
                }

                var statement = ParseStatement();
                if (statement is null) return statements;
                statements.Add(statement);
            }
        }

        private ComputeStatement? ParseStatement()
        {
            var token = Current;
            if (token.Kind == ComputeTokenKind.Ident)
            {
                switch (token.Text)
                {
                    case "let":
                        _index++;
                        return ParseBinding(false, token);
                    case "var":
                        _index++;
                        return ParseBinding(true, token);
                    case "set":
                        _index++;
                        return ParseSet(token);
                    case "if":
                        _index++;
                        return ParseIf(token);
                    case "loop":
                        _index++;
                        return ParseLoop(token);
                    case "while":
                        _index++;
                        Error(token, "while 属于第 4 步（递归/图灵完备层），本阶段未启用");
                        return null;
                    case "return":
                        _index++;
                        return ParseReturn(token);
                    case "end":
                        Error(token, "多余的 end");
                        _index++;
                        return null;
                    case "else":
                        Error(token, "else 没有匹配的 if");
                        _index++;
                        return null;
                    case "then":
                    case "do":
                        Error(token, $"多余的 {token.Text}");
                        _index++;
                        return null;
                }
            }

            var expression = ParseExpression();
            if (expression is null) return null;
            if (expression is not ComputeCallExpression call)
            {
                Error(token, "语句位置只允许效果调用或声明语句（表达式结果不可丢弃）");
                return null;
            }

            return new ComputeCallStatement(call, token.Line);
        }

        private ComputeStatement? ParseBinding(bool mutable, ComputeToken keyword)
        {
            var name = ExpectIdentifier("let/var 名称");
            if (name is null) return null;
            if (!ExpectSymbol("=", "let/var 需要 '='")) return null;
            var value = ParseExpression();
            if (value is null) return null;
            return mutable
                ? new ComputeLetStatement(name.Text, true, value, keyword.Line)
                : new ComputeLetStatement(name.Text, false, value, keyword.Line);
        }

        private ComputeStatement? ParseSet(ComputeToken keyword)
        {
            var name = ExpectIdentifier("set 目标");
            if (name is null) return null;
            if (!ExpectSymbol("=", "set 需要 '='")) return null;
            var value = ParseExpression();
            return value is null ? null : new ComputeSetStatement(name.Text, value, keyword.Line);
        }

        private ComputeStatement? ParseIf(ComputeToken keyword)
        {
            var condition = ParseExpression();
            if (condition is null) return null;
            if (!ExpectWord("then", "if 条件后需要 then")) return null;
            var then = ParseStatements(new HashSet<string>(StringComparer.Ordinal) { "else", "end" });
            IReadOnlyList<ComputeStatement>? otherwise = null;
            if (Current.Kind == ComputeTokenKind.Ident && Current.Text == "else")
            {
                _index++;
                otherwise = ParseStatements(new HashSet<string>(StringComparer.Ordinal) { "end" });
            }

            if (!ExpectWord("end", "if 缺少 end")) return null;
            return new ComputeIfStatement(condition, then, otherwise, keyword.Line);
        }

        private ComputeStatement? ParseLoop(ComputeToken keyword)
        {
            var name = ExpectIdentifier("loop 迭代变量");
            if (name is null) return null;
            if (!ExpectWord("from", "loop 需要 from")) return null;
            var from = ParseExpression();
            if (from is null) return null;
            if (!ExpectWord("to", "loop 需要 to")) return null;
            var to = ParseExpression();
            if (to is null) return null;
            var step = 1L;
            var stepDeclared = false;
            if (Current.Kind == ComputeTokenKind.Ident && Current.Text == "step")
            {
                stepDeclared = true;
                _index++;
                var stepToken = Current;
                if (stepToken.Kind != ComputeTokenKind.Int)
                {
                    Error(stepToken, "loop step 必须是正整数字面量");
                    return null;
                }

                _index++;
                if (!long.TryParse(stepToken.Text, NumberStyles.None, CultureInfo.InvariantCulture, out step)
                    || step <= 0)
                {
                    Error(stepToken, $"loop step='{stepToken.Text}' 必须为正整数");
                    return null;
                }
            }

            if (!ExpectWord("do", "loop 需要 do")) return null;
            var body = ParseStatements(new HashSet<string>(StringComparer.Ordinal) { "end" });
            if (!ExpectWord("end", "loop 缺少 end")) return null;
            return new ComputeLoopStatement(name.Text, from, to, step, stepDeclared, body, keyword.Line);
        }

        private ComputeStatement? ParseReturn(ComputeToken keyword)
        {
            if (Current.Kind is ComputeTokenKind.Newline or ComputeTokenKind.End)
            {
                return new ComputeReturnStatement(null, keyword.Line);
            }

            if (Current.Kind == ComputeTokenKind.Ident && Current.Text is "end" or "else")
            {
                return new ComputeReturnStatement(null, keyword.Line);
            }

            var value = ParseExpression();
            return value is null ? null : new ComputeReturnStatement(value, keyword.Line);
        }

        private ComputeExpression? ParseExpression() => ParseOr();

        private ComputeExpression? ParseOr()
        {
            var left = ParseAnd();
            while (left is not null && IsWord("or"))
            {
                var op = Current;
                _index++;
                var right = ParseAnd();
                if (right is null) return null;
                left = new ComputeBinaryExpression("or", left, right, op.Line);
            }

            return left;
        }

        private ComputeExpression? ParseAnd()
        {
            var left = ParseEquality();
            while (left is not null && IsWord("and"))
            {
                var op = Current;
                _index++;
                var right = ParseEquality();
                if (right is null) return null;
                left = new ComputeBinaryExpression("and", left, right, op.Line);
            }

            return left;
        }

        private ComputeExpression? ParseEquality()
        {
            var left = ParseComparison();
            while (left is not null && IsSymbol("==", "!="))
            {
                var op = Current;
                _index++;
                var right = ParseComparison();
                if (right is null) return null;
                left = new ComputeBinaryExpression(op.Text, left, right, op.Line);
            }

            return left;
        }

        private ComputeExpression? ParseComparison()
        {
            var left = ParseAdditive();
            while (left is not null && IsSymbol("<", "<=", ">", ">="))
            {
                var op = Current;
                _index++;
                var right = ParseAdditive();
                if (right is null) return null;
                left = new ComputeBinaryExpression(op.Text, left, right, op.Line);
            }

            return left;
        }

        private ComputeExpression? ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (left is not null && IsSymbol("+", "-"))
            {
                var op = Current;
                _index++;
                var right = ParseMultiplicative();
                if (right is null) return null;
                left = new ComputeBinaryExpression(op.Text, left, right, op.Line);
            }

            return left;
        }

        private ComputeExpression? ParseMultiplicative()
        {
            var left = ParseUnary();
            while (left is not null && IsSymbol("*", "/", "%"))
            {
                var op = Current;
                _index++;
                var right = ParseUnary();
                if (right is null) return null;
                left = new ComputeBinaryExpression(op.Text, left, right, op.Line);
            }

            return left;
        }

        private ComputeExpression? ParseUnary()
        {
            if (IsSymbol("-"))
            {
                var op = Current;
                _index++;
                var operand = ParseUnary();
                return operand is null ? null : new ComputeUnaryExpression("-", operand, op.Line);
            }

            if (IsWord("not"))
            {
                var op = Current;
                _index++;
                var operand = ParseUnary();
                return operand is null ? null : new ComputeUnaryExpression("not", operand, op.Line);
            }

            return ParsePrimary();
        }

        private ComputeExpression? ParsePrimary()
        {
            var token = Current;
            if (token.Kind is ComputeTokenKind.Int or ComputeTokenKind.Decimal or ComputeTokenKind.String)
            {
                _index++;
                var kind = token.Kind switch
                {
                    ComputeTokenKind.Int => ComputeLiteralKind.Int,
                    ComputeTokenKind.Decimal => ComputeLiteralKind.Decimal,
                    _ => ComputeLiteralKind.String
                };
                return new ComputeLiteralExpression(kind, token.Text, token.Line);
            }

            if (token.Kind == ComputeTokenKind.Ident)
            {
                if (token.Text is "true" or "false")
                {
                    _index++;
                    return new ComputeLiteralExpression(ComputeLiteralKind.Bool, token.Text, token.Line);
                }

                _index++;
                if (IsSymbol("("))
                {
                    _index++;
                    var args = new List<ComputeExpression>();
                    if (!IsSymbol(")"))
                    {
                        while (true)
                        {
                            var arg = ParseExpression();
                            if (arg is null) return null;
                            args.Add(arg);
                            if (IsSymbol(","))
                            {
                                _index++;
                                continue;
                            }

                            break;
                        }
                    }

                    if (!ExpectSymbol(")", "调用缺少 ')'")) return null;
                    return new ComputeCallExpression(token.Text, args, token.Line);
                }

                return new ComputeIdentifierExpression(token.Text, token.Line);
            }

            if (IsSymbol("$"))
            {
                _index++;
                var space = ExpectIdentifier("$ 后需要空间名");
                if (space is null) return null;
                if (!ExpectSymbol(".", "$空间后需要 '.坐标'")) return null;
                var coordinate = ExpectIdentifier("'.' 后需要坐标");
                if (coordinate is null) return null;
                return new ComputeKeyReferenceExpression(space.Text, coordinate.Text, token.Line);
            }

            if (IsSymbol("("))
            {
                _index++;
                var inner = ParseExpression();
                if (inner is null) return null;
                if (!ExpectSymbol(")", "缺少 ')'")) return null;
                return inner;
            }

            if (IsSymbol("["))
            {
                _index++;
                var items = new List<ComputeExpression>();
                if (!IsSymbol("]"))
                {
                    while (true)
                    {
                        var item = ParseExpression();
                        if (item is null) return null;
                        items.Add(item);
                        if (IsSymbol(","))
                        {
                            _index++;
                            continue;
                        }

                        break;
                    }
                }

                if (!ExpectSymbol("]", "列表缺少 ']'")) return null;
                return new ComputeListExpression(items, token.Line);
            }

            if (IsSymbol("{"))
            {
                _index++;
                var entries = new List<KeyValuePair<string, ComputeExpression>>();
                if (!IsSymbol("}"))
                {
                    while (true)
                    {
                        if (Current.Kind != ComputeTokenKind.String)
                        {
                            Error(Current, "map 的键必须是字符串字面量");
                            return null;
                        }

                        var key = Current;
                        _index++;
                        if (!ExpectSymbol(":", "map 键后需要 ':'")) return null;
                        var value = ParseExpression();
                        if (value is null) return null;
                        entries.Add(new KeyValuePair<string, ComputeExpression>(key.Text, value));
                        if (IsSymbol(","))
                        {
                            _index++;
                            continue;
                        }

                        break;
                    }
                }

                if (!ExpectSymbol("}", "map 缺少 '}'")) return null;
                return new ComputeMapExpression(entries, token.Line);
            }

            Error(token, $"无法解析表达式起始 token '{token.Text}'");
            return null;
        }

        private ComputeToken? ExpectIdentifier(string what)
        {
            if (Current.Kind != ComputeTokenKind.Ident)
            {
                Error(Current, $"期望{what}，实际 '{Current.Text}'");
                return null;
            }

            var token = Current;
            _index++;
            return token;
        }

        private bool ExpectSymbol(string symbol, string message)
        {
            if (!IsSymbol(symbol))
            {
                Error(Current, message);
                return false;
            }

            _index++;
            return true;
        }

        private bool ExpectWord(string word, string message)
        {
            if (!IsWord(word))
            {
                Error(Current, message);
                return false;
            }

            _index++;
            return true;
        }

        private bool IsSymbol(params string[] symbols)
            => Current.Kind == ComputeTokenKind.Symbol && symbols.Contains(Current.Text, StringComparer.Ordinal);

        private bool IsWord(string word)
            => Current.Kind == ComputeTokenKind.Ident && string.Equals(Current.Text, word, StringComparison.Ordinal);

        private void SkipNewlines()
        {
            while (Current.Kind == ComputeTokenKind.Newline) _index++;
        }

        private void Error(ComputeToken token, string message)
        {
            if (_errors.Any(e => e.Code == ComputeCodes.SyntaxInvalid && e.Line == token.Line)) return;
            _errors.Add(new ComputeDiagnostic(ComputeCodes.SyntaxInvalid, message, _file, token.Line));
        }
    }
}
