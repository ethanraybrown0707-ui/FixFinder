using System.Globalization;
using System.Text;

namespace FixFinder.Core.Analysis.Frontends;

internal enum CTokenKind { Identifier, Keyword, Number, Text, Character, Punctuator, End }

internal sealed record CToken(CTokenKind Kind, string Text, int Line, int Column, int EndLine, int EndColumn, object? Value = null);

/// <summary>
/// Turns C or C++ source into tokens, and deals with the preprocessor as far as reading the code needs: comments and
/// line continuations go, <c>#define NAME 10</c> stands for its value wherever the name is used, and only the first
/// branch of an <c>#if</c> is read - the code FixFinder checks is the code as one build of it sees it.
/// </summary>
internal sealed class CLexer(string source, bool cpp)
{
    private static readonly string[] Punctuators =
    [
        "<<=", ">>=", "...", "->*", ".*", "::", "->", "++", "--", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=",
        "%=", "&=", "|=", "^=", "#", "{", "}", "(", ")", "[", "]", ";", ",", ":", "?", ".", "+", "-", "*", "/", "%", "&", "|", "^", "~",
        "!", "<", ">", "=",
    ];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float", "for", "goto",
        "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed", "sizeof", "static", "struct", "switch",
        "typedef", "union", "unsigned", "void", "volatile", "while", "_Bool", "_Atomic", "_Thread_local", "alignas", "alignof",
        "bool", "true", "false", "nullptr", "class", "public", "private", "protected", "virtual", "override", "final", "new", "delete",
        "this", "namespace", "using", "template", "typename", "operator", "try", "catch", "throw", "friend", "explicit", "mutable",
        "constexpr", "noexcept", "static_cast", "dynamic_cast", "const_cast", "reinterpret_cast", "decltype", "nullptr_t", "NULL",
    };

    private readonly Dictionary<string, CToken> _macros = new(StringComparer.Ordinal);
    private int _at;
    private int _line = 1;
    private int _lineStart;

    public string? Problem { get; private set; }

    public List<CToken> Tokens()
    {
        var tokens = new List<CToken>();
        var skipping = 0;
        var depth = 0;

        while (true)
        {
            SkipSpace();
            if (_at >= source.Length) break;

            if (At() == '#' && StartOfLine())
            {
                var directive = Directive();
                switch (directive.Name)
                {
                    case "if" or "ifdef" or "ifndef":
                        depth++;
                        if (skipping == 0 && directive.Name == "if" && directive.Tail.Trim() == "0") skipping = depth;
                        break;
                    case "else" or "elif":
                        // Only the first way through an #if is read; the rest is skipped.
                        if (skipping == 0) skipping = depth;
                        else if (skipping == depth) skipping = 0;
                        break;
                    case "endif":
                        if (skipping == depth) skipping = 0;
                        depth = Math.Max(0, depth - 1);
                        break;
                    case "define" when skipping == 0:
                        Define(directive.Tail);
                        break;
                }

                continue;
            }

            var token = Next();
            if (token.Kind == CTokenKind.End) break;
            if (skipping > 0) continue;

            if (token.Kind == CTokenKind.Identifier && token.Text is "__attribute__" or "__extension__" or "__asm__" or "__declspec")
            {
                SkipSpace();
                if (At() == '(') Balanced();
                continue;
            }

            if (token.Kind == CTokenKind.Identifier && _macros.TryGetValue(token.Text, out var stood))
                token = stood with { Line = token.Line, Column = token.Column, EndLine = token.EndLine, EndColumn = token.EndColumn };

            tokens.Add(token);
            if (tokens.Count > 400_000)
            {
                Problem = "the file is too large to read";
                break;
            }
        }

        tokens.Add(new CToken(CTokenKind.End, "", _line, Column, _line, Column));
        return tokens;
    }

    private int Column => _at - _lineStart;

    private char At(int ahead = 0) => _at + ahead < source.Length ? source[_at + ahead] : '\0';

    private bool StartOfLine() => source[.._at].AsSpan().TrimEnd(' ').TrimEnd('\t').Length == _lineStart || AllSpaceBefore();

    private bool AllSpaceBefore()
    {
        for (var i = _lineStart; i < _at; i++)
            if (!char.IsWhiteSpace(source[i])) return false;
        return true;
    }

    private (string Name, string Tail) Directive()
    {
        _at++;
        while (_at < source.Length && (At() == ' ' || At() == '\t')) _at++;

        var start = _at;
        while (_at < source.Length && char.IsLetter(At())) _at++;
        var name = source[start.._at];

        var rest = new StringBuilder();
        while (_at < source.Length && At() != '\n')
        {
            if (At() == '\\' && (At(1) == '\n' || At(1) == '\r'))
            {
                while (_at < source.Length && At() != '\n') _at++;
                _at++;
                _line++;
                _lineStart = _at;
                continue;
            }

            if (At() == '/' && At(1) is '/' or '*') break;
            rest.Append(source[_at++]);
        }

        return (name, rest.ToString());
    }

    /// <summary>A name that stands for one number, character or text keeps its value; anything else is left alone.</summary>
    private void Define(string rest)
    {
        var text = rest.Trim();
        var space = text.IndexOfAny([' ', '\t', '(']);
        if (space <= 0 || text[space] == '(') return;

        var name = text[..space];
        var value = text[space..].Trim();
        if (value.Length == 0) return;

        var inner = new CLexer(value, cpp);
        var tokens = inner.Tokens();
        if (tokens.Count == 2 && tokens[0].Kind is CTokenKind.Number or CTokenKind.Text or CTokenKind.Character) _macros[name] = tokens[0];
    }

    private void SkipSpace()
    {
        while (_at < source.Length)
        {
            var c = At();

            if (c == '\n')
            {
                _at++;
                _line++;
                _lineStart = _at;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                _at++;
                continue;
            }

            if (c == '\\' && (At(1) == '\n' || At(1) == '\r' && At(2) == '\n'))
            {
                _at += At(1) == '\n' ? 2 : 3;
                _line++;
                _lineStart = _at;
                continue;
            }

            if (c == '/' && At(1) == '/')
            {
                while (_at < source.Length && At() != '\n')
                {
                    if (At() == '\\' && At(1) == '\n')
                    {
                        _at++;
                        _line++;
                    }

                    _at++;
                }

                continue;
            }

            if (c == '/' && At(1) == '*')
            {
                _at += 2;
                while (_at < source.Length && !(At() == '*' && At(1) == '/'))
                {
                    if (At() == '\n')
                    {
                        _line++;
                        _lineStart = _at + 1;
                    }

                    _at++;
                }

                _at += 2;
                continue;
            }

            break;
        }
    }

    private CToken Next()
    {
        var line = _line;
        var column = Column;

        if (_at >= source.Length) return new CToken(CTokenKind.End, "", line, column, line, column);

        var c = At();

        if (char.IsDigit(c) || c == '.' && char.IsDigit(At(1))) return Number(line, column);
        if (c is '"') return Text(line, column);
        if (c is '\'') return Character(line, column);
        if (char.IsLetter(c) || c is '_' or '$') return Word(line, column);

        // A raw string, a wide string or a character with a prefix.
        if (c is 'L' or 'u' or 'U' or 'R' && At(1) is '"' or '\'')
        {
            _at++;
            return At() == '"' ? Text(line, column) : Character(line, column);
        }

        foreach (var punctuator in Punctuators)
        {
            if (_at + punctuator.Length > source.Length || string.CompareOrdinal(source, _at, punctuator, 0, punctuator.Length) != 0) continue;
            if (punctuator == "::" && !cpp) continue;

            _at += punctuator.Length;
            return new CToken(CTokenKind.Punctuator, punctuator, line, column, _line, Column);
        }

        Problem ??= $"line {line}: FixFinder cannot read the character '{c}'";
        _at++;
        return new CToken(CTokenKind.Punctuator, c.ToString(), line, column, _line, Column);
    }

    /// <summary>Skips a bracketed group, brackets inside it and all: what __attribute__ and its like are given.</summary>
    private void Balanced()
    {
        var depth = 0;
        while (_at < source.Length)
        {
            if (At() == '(') depth++;
            else if (At() == ')')
            {
                depth--;
                _at++;
                if (depth == 0) return;
                continue;
            }
            else if (At() == '\n')
            {
                _line++;
                _lineStart = _at + 1;
            }

            _at++;
        }
    }

    private CToken Word(int line, int column)
    {
        var start = _at;
        while (_at < source.Length && (char.IsLetterOrDigit(At()) || At() is '_' or '$')) _at++;
        var text = source[start.._at];
        return new CToken(Keywords.Contains(text) ? CTokenKind.Keyword : CTokenKind.Identifier, text, line, column, _line, Column);
    }

    private CToken Number(int line, int column)
    {
        var start = _at;
        var hex = At() == '0' && char.ToLowerInvariant(At(1)) == 'x';
        if (hex) _at += 2;

        while (_at < source.Length && (char.IsLetterOrDigit(At()) || At() == '\'' && char.IsLetterOrDigit(At(1)) ||
                                       At() == '.' || (At() is '+' or '-') && char.ToLowerInvariant(At(-1)) is 'e' or 'p'))
            _at++;

        var text = source[start.._at];
        var cleaned = text.Replace("'", "").TrimEnd('u', 'U', 'l', 'L', 'f', 'F');
        object? value = null;

        try
        {
            if (hex) value = Convert.ToInt64(cleaned[2..], 16);
            else if (cleaned.Length > 1 && cleaned[0] == '0' && cleaned.All(char.IsDigit)) value = Convert.ToInt64(cleaned, 8);
            else if (long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)) value = whole;
            else if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) value = real;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            value = null;
        }

        return new CToken(CTokenKind.Number, text, line, column, _line, Column, value);
    }

    private CToken Text(int line, int column)
    {
        var start = _at++;
        var value = new StringBuilder();

        while (_at < source.Length && At() != '"')
        {
            if (At() == '\\')
            {
                Escape(value);
                continue;
            }

            if (At() == '\n')
            {
                _line++;
                _lineStart = _at + 1;
            }

            value.Append(source[_at++]);
        }

        _at++;

        // Texts written next to each other are one text.
        var text = source[start..Math.Min(_at, source.Length)];
        return new CToken(CTokenKind.Text, text, line, column, _line, Column, value.ToString());
    }

    private CToken Character(int line, int column)
    {
        var start = _at++;
        var value = new StringBuilder();

        while (_at < source.Length && At() != '\'')
        {
            if (At() == '\\') Escape(value);
            else value.Append(source[_at++]);
        }

        _at++;
        var text = source[start..Math.Min(_at, source.Length)];
        return new CToken(CTokenKind.Character, text, line, column, _line, Column, value.Length > 0 ? (long)value[0] : 0L);
    }

    private void Escape(StringBuilder value)
    {
        _at++;
        var c = At();
        _at++;

        switch (c)
        {
            case 'n': value.Append('\n'); break;
            case 't': value.Append('\t'); break;
            case 'r': value.Append('\r'); break;
            case '0': value.Append('\0'); break;
            case 'a': value.Append('\a'); break;
            case 'b': value.Append('\b'); break;
            case 'f': value.Append('\f'); break;
            case 'v': value.Append('\v'); break;
            case '\n':
                _line++;
                _lineStart = _at;
                break;
            case 'x' when _at + 1 < source.Length:
                var hex = 0;
                while (_at < source.Length && Uri.IsHexDigit(At()))
                {
                    hex = hex * 16 + Convert.ToInt32(At().ToString(), 16);
                    _at++;
                }

                value.Append((char)hex);
                break;
            default:
                value.Append(c);
                break;
        }
    }
}
