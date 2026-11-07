using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Recursion whose stopping case is there but is never reached, and recursion that stops with the wrong value - in a
/// function of one parameter, n, that tests n against a number once and calls itself with n changed by a number:
/// <list type="bullet">
/// <item>the call moves n away from where the test stops it - if n &lt;= 0 with f(n + 1) - or does not move it - f(n) - so
/// from any other n each call makes another, until the language runs out of room for them;</item>
/// <item>the call takes 2 or more off n each time, and the test is n == 0: an odd n goes past 0 and never equals it;</item>
/// <item>the function gives back n times what the next call gives back, and the stopping case gives back 0 - so every
/// answer has 0 in its product, and is 0.</item>
/// </list>
/// A function with no way to stop at all is a different mistake, found by analysis-recursion-never-ends.
/// </summary>
/// <remarks>
/// Only what the code shows is said: one parameter, one test of it against a number in the function, and every call the
/// function makes to itself giving it n, or n plus or minus a number. A call written any other way - f(n // 2), f(n - k) -
/// leaves the function alone.
/// </remarks>
public static partial class RecursionPatterns
{
    /// <summary>How a function's body is found: by indentation, between braces, or up to the end that closes it.</summary>
    private enum Body { Indented, Braces, Ends, BracesOrIndented }

    /// <summary>How one language writes a function of one parameter, and how its comparisons are written.</summary>
    /// <param name="Parameter">The parameter's name in what the brackets hold: the last word (int n), the first (n: u32), or the $ name.</param>
    /// <param name="Copied">Perl's: the line that copies the parameter out of @_, my ($n) = @_; - where the brackets hold none.</param>
    /// <param name="Words">Whether comparisons are written as words - -eq, -le - as PowerShell's are.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Header, Body Body, string Parameter,
        Regex? Copied = null, bool Words = false);

    private const string LastWord = @"(?<name>[A-Za-z_]\w*)\s*(?:=.*)?$";
    private const string FirstWord = @"^\s*(?:mut\s+)?(?<name>[A-Za-z_]\w*)";
    private const string DollarName = @"(?<name>\$[A-Za-z_]\w*)";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-recursion-never-stops", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new(@"^(?<indent>\s*)def\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Indented, FirstWord),
        new("logic-ruby-recursion-never-stops", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^(?<indent>\s*)def\s+(?:self\.)?(?<function>[a-z_]\w*[?!]?)\s*\((?<parameters>[^)]*)\)"), Body.Ends, FirstWord),
        new("logic-lua-recursion-never-stops", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^(?<indent>\s*)(?:local\s+)?function\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Ends, FirstWord),
        new("logic-go-recursion-never-stops", CodePattern.Files(".go"), Syntax.CLike,
            new(@"^(?<indent>\s*)func\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, FirstWord),
        new("logic-rust-recursion-never-stops", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:pub\s+)?fn\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, FirstWord),
        new("logic-js-recursion-never-stops", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:export\s+)?function\s+(?<function>[A-Za-z_$][\w$]*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, FirstWord),
        new("logic-java-recursion-never-stops", CodePattern.Files(".java"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:public|private|protected|static|final)\s+)*[\w<>\[\]]+\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord),
        new("logic-csharp-recursion-never-stops", CodePattern.Files(".cs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:public|private|protected|internal|static)\s+)*[\w<>\[\]?]+\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord),
        new("logic-c-recursion-never-stops", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:static|inline|unsigned|long|const)\s+)*[A-Za-z_]\w*[\s*&]+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord),
        new("logic-dart-recursion-never-stops", CodePattern.Files(".dart"), Syntax.CLike,
            new(@"^(?<indent>\s*)[\w<>?]+\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*\{?\s*$"), Body.Braces, LastWord),
        new("logic-php-recursion-never-stops", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?:(?:public|private|protected|static)\s+)*function\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.Braces, DollarName),
        new("logic-perl-recursion-never-stops", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^(?<indent>\s*)sub\s+(?<function>[A-Za-z_]\w*)\s*(?<parameters>)\{?\s*$"), Body.Braces, DollarName,
            Copied: new(@"^\s*my\s*(?:\(\s*(?<name>\$[A-Za-z_]\w*)\s*\)\s*=\s*@_|(?<name>\$[A-Za-z_]\w*)\s*=\s*shift\b)")),
        new("logic-powershell-recursion-never-stops", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^(?<indent>\s*)function\s+(?<function>[A-Za-z_][\w-]*)\s*\((?<parameters>[^)]*)\)", RegexOptions.IgnoreCase), Body.Braces, DollarName, Words: true),
        new("logic-scala-recursion-never-stops", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"^(?<indent>\s*)def\s+(?<function>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)"), Body.BracesOrIndented, FirstWord),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => Recursions(id, source, masked, dialect), Severity.Error, Confidence.Likely))];

    /// <summary>A line starting with a word that opens a block closed by end, in Ruby and Lua - an if after a statement opens none.</summary>
    [GeneratedRegex(@"^\s*(?:[a-z_]\w*\s*=\s*|local\s+)?(?<word>if|unless|while|until|case|begin|def|class|module|for|function|repeat)\b")]
    private static partial Regex OpeningWord();

    /// <summary>A do, or a function written inside a line - each opens a block of its own.</summary>
    [GeneratedRegex(@"\bdo\b|\bfunction\s*\(")]
    private static partial Regex OpensInside();

    [GeneratedRegex(@"\b(?:end|until)\b")]
    private static partial Regex Closes();

    private static readonly Dictionary<string, string> WordComparisons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["-eq"] = "==", ["-le"] = "<=", ["-lt"] = "<", ["-ge"] = ">=", ["-gt"] = ">",
    };

    private static IEnumerable<LogicFinding> Recursions(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        for (var header = 0; header < masked.Count; header++)
        {
            if (dialect.Header.Match(masked[header]) is not { Success: true } function) continue;
            if (Lines(masked, header, dialect.Body, function.Groups["indent"].Value) is not { Count: > 0 } body) continue;
            if (Parameter(dialect, function.Groups["parameters"].Value, masked, body) is not { } parameter) continue;
            if (Read(id, source, masked, dialect, function.Groups["function"].Value, parameter, body) is { } finding) yield return finding;
        }
    }

    /// <summary>The function's lines after its header - or, for one written on a single line, its header's line.</summary>
    private static List<int>? Lines(IReadOnlyList<string> masked, int header, Body body, string indent)
    {
        switch (body)
        {
            case Body.Indented:
                return Indented(masked, header, indent);
            case Body.Braces:
            case Body.BracesOrIndented when masked[header].Contains('{') || BraceBlocks.NextCode(masked, header + 1) is var next && next >= 0 && masked[next].TrimStart().StartsWith('{'):
                if (BraceBlocks.Body(masked, header) is { } lines) return [.. Enumerable.Range(lines.First, lines.End - lines.First)];
                return masked[header].Contains('{') && masked[header].Contains('}') ? [header] : null;
            case Body.BracesOrIndented:
                return Indented(masked, header, indent);
            default:
                var depth = 0;
                var inside = new List<int>();
                for (var line = header; line < masked.Count; line++)
                {
                    // A while's, an until's or a for's do is the one block its word opens.
                    var opening = OpeningWord().Match(masked[line]);
                    var opensInside = opening.Success && opening.Groups["word"].Value is "while" or "until" or "for"
                        ? 0
                        : OpensInside().Matches(masked[line]).Count;
                    depth += (opening.Success ? 1 : 0) + opensInside - Closes().Matches(masked[line]).Count;
                    if (line > header) inside.Add(line);
                    if (depth <= 0) return line == header ? [header] : inside.Take(inside.Count - 1).ToList();
                }

                return null;
        }
    }

    private static List<int> Indented(IReadOnlyList<string> masked, int header, string indent)
    {
        var lines = new List<int>();
        for (var line = header + 1; line < masked.Count; line++)
        {
            if (masked[line].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[line]).Length <= indent.Length) break;
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>The function's one parameter's name, or null for a function of none or of more than one.</summary>
    private static string? Parameter(Dialect dialect, string parameters, IReadOnlyList<string> masked, List<int> body)
    {
        if (dialect.Copied is { } copied)
            return body.Count > 0 && copied.Match(masked[body[0]]) is { Success: true } line ? line.Groups["name"].Value : null;

        var pieces = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces is not [var only] || only is "self" or "this") return null;

        var name = Regex.Match(only.Replace("[int]", "", StringComparison.OrdinalIgnoreCase).Trim(), dialect.Parameter);
        return name.Success && name.Groups["name"].Value is not ("self" or "mut" or "const" or "final") ? name.Groups["name"].Value : null;
    }

    /// <summary>One self-call: the line it is on, where its argument starts and ends there, and how it changes n: +2, -1, 0.</summary>
    private sealed record SelfCall(int Line, int Start, int Length, int Step, string Written);

    private static LogicFinding? Read(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, string function, string parameter, List<int> body)
    {
        var name = Regex.Escape(parameter);
        var called = new Regex($@"(?<![\w.$:-]){Regex.Escape(function)}\s*\(");

        // Every call the function makes to itself, each giving it n or n plus or minus a number - or none of this is said.
        var calls = new List<SelfCall>();
        foreach (var line in body)
        {
            foreach (Match call in called.Matches(masked[line]))
            {
                var open = call.Index + call.Length - 1;
                if (Brackets.ClosingParenthesis(masked[line], open) is not { } close) return null;

                var argument = masked[line][(open + 1)..close];
                if (Regex.Match(argument, $@"^\s*{name}\s*(?:(?<sign>[-+])\s*(?<step>\d+))?\s*$") is not { Success: true } given) return null;

                var step = given.Groups["step"].Success ? int.Parse(given.Groups["step"].Value) * (given.Groups["sign"].Value == "-" ? -1 : 1) : 0;
                calls.Add(new SelfCall(line, open + 1, close - open - 1, step, source.Lines[line].Substring(open + 1, close - open - 1).Trim()));
            }
        }

        if (calls.Count == 0) return null;

        // The one test of n against a number: n <= 0, 0 == n, $n -le 1.
        var comparison = dialect.Words ? @"-(?:eq|le|lt|ge|gt)\b" : @"===|==|<=|>=|<(?!<)|>(?!>)";
        var tests = body.SelectMany(line => Regex.Matches(masked[line], $@"(?<![\w$]){name}\s*(?<op>{comparison})\s*(?<limit>-?\d+)\b|\b(?<limit>-?\d+)\s*(?<op>{comparison})\s*{name}(?![\w$])")
            .Select(test => (Line: line, Test: test))).ToList();
        if (tests is not [var only]) return null;

        var op = dialect.Words ? WordComparisons[only.Test.Groups["op"].Value] : only.Test.Groups["op"].Value.Replace("===", "==", StringComparison.Ordinal);
        if (only.Test.Value.TrimStart().StartsWith(only.Test.Groups["limit"].Value, StringComparison.Ordinal))
            op = op switch { "<=" => ">=", "<" => ">", ">=" => "<=", ">" => "<", _ => op };

        var limit = int.Parse(only.Test.Groups["limit"].Value);
        var testLine = only.Line;
        var test = source.Lines[testLine].Substring(only.Test.Index, only.Test.Length);
        var stopsLow = op is "<=" or "<";
        var stopsHigh = op is ">=" or ">";

        // The call moves n away from the stop, or keeps it where it is.
        if (calls.All(call => call.Step == 0) && (stopsLow || stopsHigh))
        {
            var callLine = calls[0].Line;
            var towards = stopsLow ? $"{parameter} - 1" : $"{parameter} + 1";
            return Finding(id, source, callLine,
                $"Line {callLine + 1} calls {function} with {parameter} as it is, and line {testLine + 1} stops only when `{test}`: from any other " +
                $"{parameter} each call makes the same call again, until the language runs out of room for them.",
                calls.All(call => call.Line == callLine)
                    ? Changed(id, source, callLine, calls, towards, $"Call {function} with {towards}", $"Each call then moves {parameter} a step towards {limit}, where line {testLine + 1} stops it.")
                    : null);
        }

        if (calls.All(call => stopsLow ? call.Step > 0 : stopsHigh && call.Step < 0))
        {
            var callLine = calls[0].Line;
            var away = stopsLow ? "up" : "down";
            return Finding(id, source, callLine,
                $"Line {callLine + 1} calls {function} with `{calls[0].Written}`, which moves {parameter} {away}, away from where line {testLine + 1} " +
                $"stops it, `{test}`: from any {parameter} not already stopped there each call makes another, until the language runs out of room for them.",
                calls.All(call => call.Line == callLine)
                    ? Changed(id, source, callLine, calls, null, $"Move {parameter} towards {limit}: {(stopsLow ? "-" : "+")} in each call",
                        $"Each call then moves {parameter} towards {limit}, where line {testLine + 1} stops it.")
                    : null);
        }

        // n == 0 with 2 or more taken off each time: an odd n passes 0.
        if (op == "==" && calls.All(call => call.Step <= -2))
        {
            var instead = dialect.Words ? "-le" : "<=";
            var written = only.Test.Groups["op"];
            var fixedLine = source.Lines[testLine][..written.Index] + instead + source.Lines[testLine][(written.Index + written.Length)..];
            return Finding(id, source, testLine,
                $"Line {testLine + 1} stops only when `{test}`, and line {calls[0].Line + 1} takes {-calls[0].Step} off {parameter} each call: a {parameter} " +
                $"that is not {limit} plus a number of steps of {-calls[0].Step} goes past {limit}, never equals it, and the calls never stop.",
                LocalFix.ReplaceLine(id, $"Stop at {limit} or below it: {instead}",
                    $"With {instead}, a {parameter} that steps past {limit} stops too.", source.Path, testLine + 1, fixedLine), Confidence.Possible, Severity.Warning);
        }

        // n times the next call's answer, with the stop giving back 0.
        // PowerShell's call is in brackets of its own: $n * (Get-Factorial ($n - 1)).
        if (calls.All(call => call.Step < 0) && body.Any(line => Regex.IsMatch(masked[line], $@"(?<![\w$]){name}\s*\*\s*\(?\s*{Regex.Escape(function)}\s*\(|{Regex.Escape(function)}\s*\([^()]*\)\s*\)?\s*\*\s*{name}(?![\w$])")) &&
            ReturnsZero(masked, testLine, body) is { } zeroLine)
        {
            var zero = Regex.Match(masked[zeroLine], @"\breturn\s+(?<zero>0)\b");
            var fixedLine = source.Lines[zeroLine][..zero.Groups["zero"].Index] + "1" + source.Lines[zeroLine][(zero.Groups["zero"].Index + 1)..];
            return Finding(id, source, zeroLine,
                $"{function} gives back {parameter} times what the next call gives back, and line {zeroLine + 1} gives back 0 when it stops: every " +
                "answer then has 0 in its product, and is 0.",
                LocalFix.ReplaceLine(id, "Give back 1 when it stops", "1 leaves a product as it is, where 0 makes it 0.", source.Path, zeroLine + 1, fixedLine));
        }

        return null;
    }

    /// <summary>The line of the stop's return 0 - on the test's line, or the two after it - or null.</summary>
    private static int? ReturnsZero(IReadOnlyList<string> masked, int testLine, List<int> body) =>
        body.Where(line => line >= testLine && line <= testLine + 2).Cast<int?>()
            .FirstOrDefault(line => Regex.IsMatch(masked[line!.Value], @"\breturn\s+0\s*;?\s*(?:$|\bif\b|\bend\b|\})"));

    /// <summary>The self-calls' line with each argument changed: to <paramref name="to"/>, or with its + and - turned round.</summary>
    private static LocalFix Changed(string id, SourceFile source, int line, List<SelfCall> calls, string? to, string title, string explanation)
    {
        var text = source.Lines[line];
        foreach (var call in calls.OrderByDescending(call => call.Start))
        {
            var argument = text.Substring(call.Start, call.Length);
            var changed = to ?? Regex.Replace(argument, @"(?<sign>[-+])(?=\s*\d+\s*$)", sign => sign.Value == "+" ? "-" : "+");
            text = text[..call.Start] + changed + text[(call.Start + call.Length)..];
        }

        return LocalFix.ReplaceLine(id, title, explanation, source.Path, line + 1, text);
    }

    private static LogicFinding Finding(string id, SourceFile source, int line, string said, LocalFix? fix, Confidence confidence = Confidence.Likely, Severity severity = Severity.Error) =>
        new(id, line + 1, said, fix) { Severity = severity, Confidence = confidence };
}
