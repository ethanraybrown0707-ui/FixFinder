using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A value worked out and then lost, in the languages whose values FixFinder does not follow through the code: a variable is
/// given a value, and the very next line gives it another without reading it - <c>$area = $width * $height;</c>, then
/// <c>$area = $width * $width;</c> - so the first is never used. Python, Java, C#, JavaScript, Go, C and C++ have the same
/// mistake found by following every way through the function, analysis-overwritten-before-read; Rust's compiler, Elixir's and
/// OCaml's and F#'s say it themselves, and Erlang does not let a variable be given a second value.
/// </summary>
/// <remarks>
/// Only the plainest case is said, as nothing here follows the ways through the code: two lines one after the other, at the
/// same indentation, each the whole of a statement that gives the same plain variable a value - not a field, not Ruby's
/// @count or $count - the second neither declaring it again, nor reading it, nor given only under a modifier such as
/// <c>if ready</c>. Left alone: a first value that is a starting value - a number, text, nil or null, true or false, an empty
/// list or a new object made with nothing; a second value that names any function or method the file defines, which may
/// read the variable from where it is; and lines inside a block whose rescue, catch or except reads the variable, which an
/// error on the second line would take the first value to.
/// </remarks>
public static partial class OverwrittenPatterns
{
    /// <summary>
    /// How one language gives a variable a value: the words that declare one, what its names look like, and whether case
    /// matters. TypeScript is read with JavaScript's files, as every TypeScript file is, but said only of a TypeScript file:
    /// a JavaScript file's values are followed through the code instead.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string Declares, string NamePattern,
        string Modifiers = "", bool IgnoreCase = false, bool OnlyTypeScript = false);

    private static readonly Dialect[] Dialects =
    [
        new("typescript", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, @"(?:let|var)\s+", @"[A-Za-z_$][\w$]*", OnlyTypeScript: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, "", @"[a-z_]\w*", Modifiers: @"\s(?:if|unless|while|until|rescue)\s"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, "", @"\$[A-Za-z_]\w*"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, @"local\s+", @"[A-Za-z_]\w*"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, @"(?:my|our|local)\s+", @"\$[A-Za-z_]\w*", Modifiers: @"\s(?:if|unless|while|until|for|foreach)\s"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, @"(?:var|late\s+var|int|double|num|bool|String|dynamic|Object|(?:List|Map|Set)<[^=;]*>)\??\s+", @"[A-Za-z_$][\w$]*"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, @"\[[\w.\[\]]+\]\s*", @"\$[A-Za-z_]\w*", IgnoreCase: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, @"var\s+", @"[a-z_]\w*"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, "", @"[A-Za-z]\w*", IgnoreCase: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-overwritten-before-read", dialect.Files, dialect.Syntax,
            (id, source, masked) => Overwritten(id, source, masked, dialect))),
    ];

    private static IEnumerable<LogicFinding> Overwritten(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (dialect.OnlyTypeScript && !TypeScriptCheck.IsTypeScript(source.Path)) yield break;

        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        // The name is taken whole, never given back a letter at a time, a type written after it is at most a line's worth, and
        // the value is the rest of the line, its end trimmed afterwards: else a line of one 100,000-letter word, or of text
        // blanked to 5,000 spaces, would be read again for every letter.
        var given = new Regex($@"^\s*(?<declared>{(dialect.Declares.Length > 0 ? dialect.Declares : "(?!)")})?(?<name>(?>{dialect.NamePattern}))(?![\w$])\s*(?::[^=;]{{1,200}}?)?\s*=(?![=~>])\s*(?<value>\S.*)$",
            options, TimeSpan.FromSeconds(1));
        var ownFunctions = OwnFunctions(masked);
        var comparer = dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        for (var first = 0; first < masked.Count; first++)
        {
            if (given.Match(masked[first]) is not { Success: true } before || !WholeStatement(masked, first)) continue;
            if (NextWithCode(masked, first) is not { } second || Indentation(masked[second]) != Indentation(masked[first])) continue;
            if (given.Match(masked[second]) is not { Success: true } after || !WholeStatement(masked, second)) continue;

            var name = before.Groups["name"].Value;
            if (!comparer.Equals(name, after.Groups["name"].Value) || after.Groups["declared"].Success) continue;

            // Read as written, not blanked: a value can read the variable inside its text - "#{total}", "$total".
            var firstValue = Written(source, first, before.Groups["value"]);
            var secondValue = Written(source, second, after.Groups["value"]);
            if (IsAStartingValue(firstValue)) continue;
            if (Mentions(secondValue, name, dialect.IgnoreCase) || ownFunctions.Any(function => Mentions(secondValue, function, dialect.IgnoreCase))) continue;
            if (dialect.Modifiers.Length > 0 && Regex.IsMatch(" " + secondValue + " ", dialect.Modifiers, options)) continue;
            if (ReadWhereAnErrorWouldGo(source, masked, second, name, dialect.IgnoreCase)) continue;

            yield return new LogicFinding(id, first + 1,
                $"`{name}` is given `{firstValue}` on line {first + 1}, then given another value on line {second + 1} before anything reads it, " +
                $"so the value from line {first + 1} is never used", null);
        }
    }

    /// <summary>The value as the line writes it - its text and all - from where the blanked line says it starts.</summary>
    private static string Written(SourceFile source, int line, Group value) =>
        (source.Line(line + 1) is { } text && value.Index + value.Length <= text.Length ? text.Substring(value.Index, value.Length) : value.Value)
            .TrimEnd().TrimEnd(';').Trim();

    /// <summary>
    /// Whether the line is the whole of one statement: its brackets close on it, it holds no second statement, it does not
    /// end on something that carries on to the next line, and the next line does not carry it on.
    /// </summary>
    private static bool WholeStatement(IReadOnlyList<string> masked, int line)
    {
        var code = masked[line].Trim().TrimEnd(';').TrimEnd();
        if (code.Contains(';') || Depth(code) != 0 || CarriesOn().IsMatch(code)) return false;
        return NextWithCode(masked, line) is not { } next || !CarriedOnFrom().IsMatch(masked[next]);
    }

    private static int Depth(string code) => code.Count(c => c is '(' or '[' or '{') - code.Count(c => c is ')' or ']' or '}');

    private static int? NextWithCode(IReadOnlyList<string> masked, int line)
    {
        for (var next = line + 1; next < masked.Count; next++)
            if (masked[next].Trim().Length > 0) return next;
        return null;
    }

    private static int Indentation(string line) => line.Length - line.TrimStart().Length;

    /// <summary>A value code often gives a variable first, before the one it means: 0, "", nil, null, true, [], {} or new List().</summary>
    private static bool IsAStartingValue(string value) => StartingValue().IsMatch(value.Trim());

    private static bool Mentions(string code, string name, bool ignoreCase) =>
        Regex.IsMatch(code, $@"(?<![\w$@]){Regex.Escape(name)}(?![\w-])", ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);

    /// <summary>The names of the functions and methods the file defines, any of which may read a variable from where it is.</summary>
    private static HashSet<string> OwnFunctions(IReadOnlyList<string> masked) =>
        masked.SelectMany(line => FunctionDefined().Matches(line)).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether the lines are inside a block whose rescue, catch or except reads the name - in its code or inside its text,
    /// "its length is $value" - as an error on the second line takes the first value there. The handler is the first line
    /// after them, less indented, that starts one.
    /// </summary>
    private static bool ReadWhereAnErrorWouldGo(SourceFile source, IReadOnlyList<string> masked, int second, string name, bool ignoreCase)
    {
        var indent = Indentation(masked[second]);
        for (var line = second + 1; line < masked.Count; line++)
        {
            if (masked[line].Trim().Length == 0 || Indentation(masked[line]) >= indent) continue;
            if (!Handler().IsMatch(masked[line])) return false;

            var handlerIndent = Indentation(masked[line]);
            for (var inside = line; inside < masked.Count; inside++)
            {
                if (inside > line && masked[inside].Trim().Length > 0 && Indentation(masked[inside]) <= handlerIndent && !Handler().IsMatch(masked[inside])) return false;
                if (Mentions(source.Line(inside + 1) ?? masked[inside], name, ignoreCase)) return true;
            }

            return false;
        }

        return false;
    }

    [GeneratedRegex(@"^(?:-?\d[\d_.]*(?:[eEdD][-+]?\d+)?(?:_\w+|[a-zA-Z]{0,2})|""[^""]*""|'[^']*'|`[^`]*`|nil|null|NULL|None|undef|true|false|\$null|\$true|\$false|\.true\.|\.false\.|Nil|None|\[\s*\]|\{\s*\}|\(\s*\)|@\(\s*\)|@\{\s*\}|array\(\s*\)|<[^<>]*>\[\s*\]|new\s+[\w.]+(?:<[^<>]*>)?\(\s*\)|[A-Z]\w*(?:<[^<>]*>|\[[^\[\]]*\])?\(\s*\)|Array\.new|Hash\.new|List\(\s*\)|Map\(\s*\)|Set\(\s*\))$")]
    private static partial Regex StartingValue();

    /// <summary>A line ending on something that carries the statement on: an operator, a comma, an opening bracket, a backslash, then or do.</summary>
    [GeneratedRegex(@"(?:[+\-*/%&|^<>=,.(\[{\\?:]|\b(?:and|or|not|then|do|&&|\|\|))\s*$")]
    private static partial Regex CarriesOn();

    /// <summary>A line carrying on the one above it: starting with a dot, an operator or a closing word of an expression.</summary>
    [GeneratedRegex(@"^\s*(?:\.(?!\.)|[+\-*/%&|^?:]|\b(?:and|or)\b)")]
    private static partial Regex CarriedOnFrom();

    /// <summary>
    /// A function or method the file defines: def total, function total, sub total, func total, fun total, function Get-Total;
    /// one written with its type first and its body after, int total(List&lt;int&gt; values) { or =&gt; - or a name given a
    /// function: total = function, total = (x) =&gt;, total = lambda.
    /// </summary>
    [GeneratedRegex(@"\b(?:def|function|sub|func|fun|fn|subroutine)\s+(?:self\.)?(?<name>(?>[A-Za-z_][\w-]*))|^\s*(?:(?>[\w<>\[\]?,.]+)\s+)+(?<name>(?>[A-Za-z_]\w*))\s*\([^()]*\)\s*(?:\{|=>|async\b)|(?<![\w$])(?<name>(?>[A-Za-z_$][\w$]*))\s*=\s*(?:function\b|\([^()]*\)\s*(?::\s*[^=]+)?=>|[A-Za-z_]\w*\s*=>|lambda\b|->|proc\b)", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionDefined();

    /// <summary>A line that starts handling an error: rescue, catch, except, } catch.</summary>
    [GeneratedRegex(@"^\s*\}?\s*(?:rescue|catch|except)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Handler();
}
