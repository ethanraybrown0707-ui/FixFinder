using System.Globalization;
using System.Text;

namespace FixFinder.Core.Analysis.Frontends;

internal enum JsTokenKind { Identifier, Keyword, Number, Text, Template, Regex, Punctuator, End }

/// <summary>
/// One piece of JavaScript source. <paramref name="NewlineBefore"/> is what automatic semicolon insertion needs, and
/// <paramref name="Parts"/> holds the pieces a template literal is built from.
/// </summary>
internal sealed record JsToken(
    JsTokenKind Kind,
    string Text,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    bool NewlineBefore,
    object? Value = null,
    IReadOnlyList<string>? Parts = null);

/// <summary>
/// Turns JavaScript source into tokens. Whether a slash starts a regular expression or divides is decided the way every
/// JavaScript reader decides it: by what came before it - a value can be divided, anything else starts a pattern.
/// </summary>
internal sealed class JsLexer(string source)
{
    private static readonly string[] Punctuators =
    [
        ">>>=", "...", "===", "!==", "**=", "<<=", ">>=", ">>>", "&&=", "||=", "??=",
        "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "**",
        "{", "}", "(", ")", "[", "]", ";", ",", "<", ">", "+", "-", "*", "/", "%", "&", "|", "^", "!", "~", "?", ":", "=", ".", "#", "@",
    ];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "enum", "export",
        "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "null", "return", "static",
        "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while", "with", "yield", "async", "of", "get", "set",
        "undefined",
    };

    private int _at;
    private int _line = 1;
    private int _lineStart;
    private JsToken? _previous;

    public string? Problem { get; private set; }

    public List<JsToken> Tokens()
    {
        var tokens = new List<JsToken>();
        while (true)
        {
            var token = Next();
            tokens.Add(token);
            if (token.Kind == JsTokenKind.End || Problem is not null) break;
        }

        return tokens;
    }

    private int Column => _at - _lineStart;

    private char At(int ahead = 0) => _at + ahead < source.Length ? source[_at + ahead] : '\0';

    private JsToken Next()
    {
        var newline = SkipSpace();
        var line = _line;
        var column = Column;

        if (_at >= source.Length) return Made(JsTokenKind.End, "", line, column, newline);

        var c = At();

        if (char.IsDigit(c) || c == '.' && char.IsDigit(At(1))) return Number(line, column, newline);
        if (c is '"' or '\'') return Text(line, column, newline);
        if (c == '`') return Template(line, column, newline);
        if (c == '/' && StartsPattern()) return Regex(line, column, newline);
        if (IsNameStart(c)) return Word(line, column, newline);

        foreach (var punctuator in Punctuators)
        {
            if (_at + punctuator.Length > source.Length || string.CompareOrdinal(source, _at, punctuator, 0, punctuator.Length) != 0) continue;

            // `?.5` is a question mark then a number, not optional chaining.
            if (punctuator == "?." && char.IsDigit(At(2))) continue;

            _at += punctuator.Length;
            return Made(JsTokenKind.Punctuator, punctuator, line, column, newline);
        }

        Problem = $"line {line}: FixFinder cannot read the character '{c}'";
        _at++;
        return Made(JsTokenKind.End, "", line, column, newline);
    }

    private JsToken Made(JsTokenKind kind, string text, int line, int column, bool newline, object? value = null, IReadOnlyList<string>? parts = null) =>
        _previous = new JsToken(kind, text, line, column, _line, Column, newline, value, parts);

    private bool SkipSpace()
    {
        var newline = false;

        while (_at < source.Length)
        {
            var c = At();

            if (c == '\n')
            {
                newline = true;
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

            if (c == '/' && At(1) == '/')
            {
                while (_at < source.Length && At() != '\n') _at++;
                continue;
            }

            if (c == '/' && At(1) == '*')
            {
                _at += 2;
                while (_at < source.Length && !(At() == '*' && At(1) == '/'))
                {
                    if (At() == '\n')
                    {
                        newline = true;
                        _line++;
                        _lineStart = _at + 1;
                    }

                    _at++;
                }

                _at += 2;
                continue;
            }

            // A script can start with #!/usr/bin/env node.
            if (c == '#' && At(1) == '!' && _at == 0)
            {
                while (_at < source.Length && At() != '\n') _at++;
                continue;
            }

            break;
        }

        return newline;
    }

    /// <summary>Whether a slash here starts a pattern: it does unless what came before it is a value that can be divided.</summary>
    private bool StartsPattern() => _previous switch
    {
        null => true,
        { Kind: JsTokenKind.Number or JsTokenKind.Text or JsTokenKind.Template or JsTokenKind.Regex } => false,
        { Kind: JsTokenKind.Identifier } => false,
        { Kind: JsTokenKind.Keyword, Text: "this" or "super" or "true" or "false" or "null" or "undefined" } => false,
        { Kind: JsTokenKind.Keyword } => true,
        { Kind: JsTokenKind.Punctuator, Text: ")" or "]" or "}" or "++" or "--" } => false,
        _ => true,
    };

    private static bool IsNameStart(char c) => char.IsLetter(c) || c is '_' or '$';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    private JsToken Word(int line, int column, bool newline)
    {
        var start = _at;
        while (_at < source.Length && IsNamePart(At())) _at++;
        var text = source[start.._at];
        return Made(Keywords.Contains(text) ? JsTokenKind.Keyword : JsTokenKind.Identifier, text, line, column, newline);
    }

    private JsToken Number(int line, int column, bool newline)
    {
        var start = _at;

        if (At() == '0' && char.ToLowerInvariant(At(1)) is 'x' or 'b' or 'o')
        {
            var radix = char.ToLowerInvariant(At(1)) switch { 'x' => 16, 'b' => 2, _ => 8 };
            _at += 2;
            while (_at < source.Length && (IsNamePart(At()) || At() == '_')) _at++;
            var digits = source[(start + 2).._at].Replace("_", "").TrimEnd('n');
            var whole = long.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) || radix != 16
                ? Try(() => Convert.ToInt64(digits, radix))
                : null;
            return Made(JsTokenKind.Number, source[start.._at], line, column, newline, whole);
        }

        while (_at < source.Length && (char.IsDigit(At()) || At() is '.' or '_')) _at++;
        if (char.ToLowerInvariant(At()) == 'e')
        {
            _at++;
            if (At() is '+' or '-') _at++;
            while (_at < source.Length && char.IsDigit(At())) _at++;
        }

        if (At() == 'n') _at++;

        var text = source[start.._at];
        var cleaned = text.Replace("_", "").TrimEnd('n');
        object? value = long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) ? real : null;

        return Made(JsTokenKind.Number, text, line, column, newline, value);
    }

    private static T? Try<T>(Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    private JsToken Text(int line, int column, bool newline)
    {
        var quote = At();
        var start = _at++;
        var value = new StringBuilder();

        while (_at < source.Length && At() != quote)
        {
            if (At() == '\n')
            {
                Problem = $"line {_line}: a text that is not closed";
                break;
            }

            if (At() == '\\') Escape(value);
            else value.Append(source[_at++]);
        }

        _at++;
        return Made(JsTokenKind.Text, source[start..Math.Min(_at, source.Length)], line, column, newline, value.ToString());
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
            case 'b': value.Append('\b'); break;
            case 'f': value.Append('\f'); break;
            case 'v': value.Append('\v'); break;
            case '0' when !char.IsDigit(At()): value.Append('\0'); break;
            case '\n':
                _line++;
                _lineStart = _at;
                break;
            // The digits after \x, \u and \u{ are read only when they really are hexadecimal. JavaScript itself rejects
            // "\xzz" or "\u{}" as a syntax error, so that is what is reported - the same way an unclosed string is -
            // rather than letting the conversion throw on a program somebody is half way through typing.
            case 'x':
                if (Hex(2) is { } code) value.Append((char)code);
                else Malformed(@"\x has to be followed by two hexadecimal digits");
                break;
            case 'u' when At() == '{':
                var close = source.IndexOf('}', _at);
                if (close > _at && CodePoint(source.AsSpan(_at + 1, close - _at - 1)) is { } point)
                {
                    // A lone surrogate is allowed in a JavaScript string, which is a sequence of UTF-16 code units, so a
                    // point that fits in one is written as one rather than refused.
                    if (point <= 0xFFFF) value.Append((char)point);
                    else value.Append(char.ConvertFromUtf32(point));
                    _at = close + 1;
                }
                else Malformed(@"\u{...} has to hold a code point of up to 10FFFF in hexadecimal");
                break;
            case 'u':
                if (Hex(4) is { } unit) value.Append((char)unit);
                else Malformed(@"\u has to be followed by four hexadecimal digits, or by a code point in braces");
                break;
            default:
                value.Append(c);
                break;
        }
    }

    /// <summary>The next few characters as a hexadecimal number, taken off the input only when that is what they are.</summary>
    private int? Hex(int digits)
    {
        if (_at + digits > source.Length) return null;

        var number = 0;
        for (var i = 0; i < digits; i++)
        {
            if (Digit(source[_at + i]) is not { } value) return null;
            number = number * 16 + value;
        }

        _at += digits;
        return number;
    }

    /// <summary>
    /// The inside of \u{...} as a code point, or null when it is not one: empty, not hexadecimal, or beyond the last
    /// code point there is. Leading zeros are allowed, as JavaScript allows them, so the length alone decides nothing.
    /// </summary>
    private static int? CodePoint(ReadOnlySpan<char> digits)
    {
        if (digits.IsEmpty) return null;

        var point = 0;
        foreach (var character in digits)
        {
            if (Digit(character) is not { } value) return null;

            point = point * 16 + value;
            if (point > 0x10FFFF) return null;
        }

        return point;
    }

    private static int? Digit(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => null,
    };

    /// <summary>An escape JavaScript would refuse: reported like any other problem, keeping the first one found.</summary>
    private void Malformed(string what) => Problem ??= $"line {_line}: {what}";

    /// <summary>A template literal, with the code inside ${...} kept as text for the parser to read on its own.</summary>
    private JsToken Template(int line, int column, bool newline)
    {
        var start = _at++;
        var parts = new List<string>();
        var value = new StringBuilder();

        while (_at < source.Length && At() != '`')
        {
            if (At() == '\\')
            {
                Escape(value);
                continue;
            }

            if (At() == '$' && At(1) == '{')
            {
                _at += 2;
                var depth = 1;
                var from = _at;
                while (_at < source.Length && depth > 0)
                {
                    if (At() == '{') depth++;
                    else if (At() == '}') depth--;
                    else if (At() == '\n')
                    {
                        _line++;
                        _lineStart = _at + 1;
                    }

                    if (depth > 0) _at++;
                }

                parts.Add(source[from.._at]);
                _at++;
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
        return Made(JsTokenKind.Template, source[start..Math.Min(_at, source.Length)], line, column, newline, value.ToString(), parts);
    }

    private JsToken Regex(int line, int column, bool newline)
    {
        var start = _at++;
        var inClass = false;

        while (_at < source.Length)
        {
            var c = At();
            if (c == '\\') { _at += 2; continue; }
            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;
            else if (c == '/' && !inClass) break;
            else if (c == '\n') break;
            _at++;
        }

        _at++;
        while (_at < source.Length && IsNamePart(At())) _at++;
        return Made(JsTokenKind.Regex, source[start..Math.Min(_at, source.Length)], line, column, newline);
    }
}
