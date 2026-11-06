using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A value worked out under its own name and then never used, while a value it was worked out from is given back or printed
/// in its place: <c>average = total / len(marks)</c> then <c>return total</c> in a function called average, or then
/// <c>print("Average:", total)</c>. The function's name, or the words printed beside it, say which was meant - and the value
/// worked out for it is the one nothing reads. The change gives back, or prints, that value.
/// </summary>
/// <remarks>
/// Said only with both halves of the evidence: the name worked out is read nowhere else, so it was worked out for nothing as
/// the code stands; and the function, or the label printed, is named for it. A total that is meant to be returned, from a
/// function called total, is never taken for one. Go is left out: its compiler refuses a name that is never used.
/// </remarks>
public static partial class WrongValuePatterns
{
    /// <summary>How one language writes what this check reads.</summary>
    /// <param name="Function">A function's first line: its name as the group "name".</param>
    /// <param name="Assignment">A name given a value worked out from others: the name as "target", the rest as "right".</param>
    /// <param name="Return">A function giving back one name: the name as "value".</param>
    /// <param name="LastValue">A function's last line that gives back the name on it without a return, or null for a language that needs one.</param>
    /// <param name="Print">A line that prints.</param>
    /// <param name="Sigil">What every variable's name starts with in the language: $ for PHP, Perl and PowerShell.</param>
    private sealed record Dialect(
        string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex? Function, Regex Assignment, Regex? Return, Regex? LastValue, Regex Print, string Sigil = "");

    private const string BraceFunction =
        @"^\s*(?:(?:public|private|protected|internal|static|final|async|export|virtual|override|inline)\s+)*" +
        @"(?:function\s+|(?:[A-Za-z_][\w<>\[\],.?*&]*\s+)+)(?<name>[A-Za-z_$][\w$]*)\s*\([^;]*\)\s*(?::\s*[\w<>\[\]|]+\s*)?\{?\s*$";

    private const string BraceAssignment =
        @"^\s*(?:(?:final|const|let|var|val|auto)\s+)?(?:[A-Za-z_][\w<>\[\],.?*&]*\s+(?=[A-Za-z_$][\w$]*\s*=))?(?<target>[A-Za-z_$][\w$]*)\s*=(?![=>])\s*(?<right>[^;]+?)\s*;?\s*$";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-wrong-value-used", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            new(@"^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\("),
            new(@"^\s*(?<target>[A-Za-z_]\w*)\s*=(?!=)\s*(?<right>.+?)\s*$"),
            new(@"^\s*return\s+(?<value>[A-Za-z_]\w*)\s*$"), null,
            new(@"^\s*print\s*\(")),
        new("logic-java-wrong-value-used", CodePattern.Files(".java"), Syntax.CLike,
            new(BraceFunction), new(BraceAssignment), new(@"^\s*return\s+(?<value>[A-Za-z_]\w*)\s*;\s*$"), null,
            new(@"\bSystem\.out\.print(?:ln|f)?\s*\(")),
        new("logic-wrong-value-used", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike,
            new(BraceFunction), new(BraceAssignment), new(@"^\s*return\s+(?<value>[A-Za-z_$][\w$]*)\s*;?\s*$"), null,
            new(@"\b(?:printf|console\.log|Console\.Write(?:Line)?|std::cout|cout)\b")),
        new("logic-rust-wrong-value-used", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^\s*(?:pub\s+)?fn\s+(?<name>[A-Za-z_]\w*)\s*[(<]"),
            new(@"^\s*let\s+(?:mut\s+)?(?<target>[A-Za-z_]\w*)(?:\s*:\s*[^=]+)?\s*=(?![=>])\s*(?<right>.+?)\s*;\s*$"),
            new(@"^\s*return\s+(?<value>[A-Za-z_]\w*)\s*;\s*$"), new(@"^\s*(?<value>[A-Za-z_]\w*)\s*$"),
            new(@"\b(?:println|print)!\s*\(")),
        new("logic-ruby-wrong-value-used", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^\s*def\s+(?<name>[A-Za-z_]\w*[?!]?)"),
            new(@"^\s*(?<target>[a-z_]\w*)\s*=(?![=~>])\s*(?<right>.+?)\s*$"),
            new(@"^\s*return\s+(?<value>[a-z_]\w*)\s*$"), new(@"^\s*(?<value>[a-z_]\w*)\s*$"),
            new(@"^\s*(?:puts|print|p)\b")),
        new("logic-php-wrong-value-used", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^\s*(?:(?:public|private|protected|static)\s+)*function\s+(?<name>[A-Za-z_]\w*)\s*\("),
            new(@"^\s*(?<target>\$[A-Za-z_]\w*)\s*=(?![=>])\s*(?<right>.+?)\s*;\s*$"),
            new(@"^\s*return\s+(?<value>\$[A-Za-z_]\w*)\s*;\s*$"), null,
            new(@"^\s*(?:echo|print|printf)\b"), "$"),
        new("logic-lua-wrong-value-used", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^\s*(?:local\s+)?function\s+(?:[\w.]+[.:])?(?<name>[A-Za-z_]\w*)\s*\("),
            new(@"^\s*(?:local\s+)?(?<target>[A-Za-z_]\w*)\s*=(?!=)\s*(?<right>.+?)\s*$"),
            new(@"^\s*return\s+(?<value>[A-Za-z_]\w*)\s*$"), null,
            new(@"^\s*print\s*\(")),
        new("logic-perl-wrong-value-used", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^\s*sub\s+(?<name>[A-Za-z_]\w*)"),
            new(@"^\s*(?:my\s+)?(?<target>\$[A-Za-z_]\w*)\s*=(?![=~>])\s*(?<right>.+?)\s*;\s*$"),
            new(@"^\s*return\s+(?<value>\$[A-Za-z_]\w*)\s*;\s*$"), null,
            new(@"^\s*(?:print|say|printf)\b"), "$"),
        new("logic-dart-wrong-value-used", CodePattern.Files(".dart"), Syntax.CLike,
            new(BraceFunction), new(BraceAssignment), new(@"^\s*return\s+(?<value>[A-Za-z_]\w*)\s*;\s*$"), null,
            new(@"^\s*print\s*\(")),
        new("logic-fortran-wrong-value-used", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran,
            null, new(@"^\s*(?<target>[A-Za-z_]\w*)\s*=(?![=>])\s*(?<right>.+?)\s*$"), null, null,
            new(@"^\s*(?:print\s*\*\s*,|write\s*\()", RegexOptions.IgnoreCase)),
        new("logic-powershell-wrong-value-used", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^\s*function\s+(?<name>[A-Za-z_][\w-]*)", RegexOptions.IgnoreCase),
            new(@"^\s*(?<target>\$[A-Za-z_]\w*)\s*=(?!=)\s*(?<right>.+?)\s*$"),
            new(@"^\s*return\s+(?<value>\$[A-Za-z_]\w*)\s*$", RegexOptions.IgnoreCase), new(@"^\s*(?<value>\$[A-Za-z_]\w*)\s*$"),
            new(@"^\s*(?:Write-Output|Write-Host)\b", RegexOptions.IgnoreCase), "$"),
        new("logic-fsharp-wrong-value-used", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            new(@"^\s*let\s+(?:rec\s+)?(?<name>[A-Za-z_]\w*)\s+[A-Za-z_(][^=]*=\s*$"),
            new(@"^\s*let\s+(?:mutable\s+)?(?<target>[A-Za-z_]\w*)\s*=(?!=)\s*(?<right>.+?)\s*$"),
            null, new(@"^\s*(?<value>[A-Za-z_]\w*)\s*$"),
            new(@"^\s*printfn?\b")),
        new("logic-scala-wrong-value-used", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"^\s*def\s+(?<name>[A-Za-z_]\w*)\s*[(\[]"),
            new(@"^\s*(?:val|var)\s+(?<target>[A-Za-z_]\w*)(?:\s*:\s*[^=]+)?\s*=(?![=>])\s*(?<right>.+?)\s*$"),
            new(@"^\s*return\s+(?<value>[A-Za-z_]\w*)\s*$"), new(@"^\s*(?<value>[A-Za-z_]\w*)\s*$"),
            new(@"^\s*println\s*\(")),
        new("logic-elixir-wrong-value-used", CodePattern.Files(".ex", ".exs"), Syntax.Python,
            new(@"^\s*defp?\s+(?<name>[a-z_]\w*[?!]?)"),
            new(@"^\s*(?<target>[a-z_]\w*)\s*=(?![=~>])\s*(?<right>.+?)\s*$"),
            null, new(@"^\s*(?<value>[a-z_]\w*)\s*$"),
            new(@"^\s*IO\.(?:puts|inspect)\b")),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => WrongValues(id, source, masked, dialect), Severity.Warning, Confidence.Likely))];

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"[$@]?[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Name();

    [GeneratedRegex(@"[-_]+|(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex PartBreak();

    /// <summary>A Fortran declaration - integer :: total, average - which names a variable without reading it.</summary>
    [GeneratedRegex(@"::")]
    private static partial Regex Declares();

    private static IEnumerable<LogicFinding> WrongValues(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        foreach (var (name, first, end) in Scopes(masked, dialect))
        {
            var lines = Enumerable.Range(first, end - first).ToList();

            foreach (var at in lines)
            {
                if (dialect.Assignment.Match(masked[at]) is not { Success: true } worked) continue;

                var target = worked.Groups["target"].Value;
                var from = Names(worked.Groups["right"].Value).Where(other => other != target).ToHashSet(StringComparer.Ordinal);
                if (from.Count == 0) continue;

                // Worked out for nothing as the code stands: the name is read nowhere in its function but on its own line - a
                // declaration that only names it does not read it.
                if (lines.Any(line => line != at && Mentions(masked[line], target) && !(dialect.Syntax == Syntax.Fortran && Declares().IsMatch(masked[line])))) continue;

                foreach (var later in lines.Where(line => line > at))
                {
                    if (name is not null && GivesBack(dialect, masked, later, end) is { } given && from.Contains(given.Value) && NamedFor(name, target))
                    {
                        yield return Returned(id, source, at, later, given, target, name);
                        break;
                    }

                    if (dialect.Print.IsMatch(masked[later]) && Printed(source.Lines[later], masked[later], target, from) is { } printed)
                    {
                        yield return Printing(id, source, at, later, printed, target);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>The functions of the file - each with its name and the lines of its body, by indentation - and the file's own top level.</summary>
    private static IEnumerable<(string? Name, int First, int End)> Scopes(IReadOnlyList<string> masked, Dialect dialect)
    {
        var inFunctions = new bool[masked.Count];

        if (dialect.Function is not null)
        {
            for (var header = 0; header < masked.Count; header++)
            {
                if (dialect.Function.Match(masked[header]) is not { Success: true } function || function.Groups["name"].Value is "if" or "for" or "while" or "switch" or "catch") continue;

                var indent = CodeText.Indentation(masked[header]).Length;
                var end = header + 1;
                while (end < masked.Count && (masked[end].Trim().Length == 0 || CodeText.Indentation(masked[end]).Length > indent)) end++;
                if (end == header + 1) continue;

                for (var line = header; line < end; line++) inFunctions[line] = true;
                yield return (function.Groups["name"].Value, header + 1, end);
            }
        }

        // The top level, for what it prints: the lines no function holds, as one scope.
        var topLevel = Enumerable.Range(0, masked.Count).Where(line => !inFunctions[line]).ToList();
        if (topLevel.Count > 0 && topLevel.Count == topLevel[^1] - topLevel[0] + 1) yield return (null, topLevel[0], topLevel[^1] + 1);
        else foreach (var (first, end) in Runs(topLevel)) yield return (null, first, end);
    }

    private static IEnumerable<(int First, int End)> Runs(List<int> lines)
    {
        for (var start = 0; start < lines.Count;)
        {
            var stop = start;
            while (stop + 1 < lines.Count && lines[stop + 1] == lines[stop] + 1) stop++;
            yield return (lines[start], lines[stop] + 1);
            start = stop + 1;
        }
    }

    /// <summary>What a function gives back on the line: the name after return, or - for a language that gives back its last line - the name alone on its last line.</summary>
    private static Group? GivesBack(Dialect dialect, IReadOnlyList<string> masked, int line, int end)
    {
        if (dialect.Return?.Match(masked[line]) is { Success: true } returned) return returned.Groups["value"];

        // The last line with code on it, before the function's end.
        var last = Enumerable.Range(line, end - line).LastOrDefault(each => masked[each].Trim().Length > 0 && !Regex.IsMatch(masked[each], @"^\s*(?:end|\})\s*$"));
        return last == line && dialect.LastValue?.Match(masked[line]) is { Success: true } alone ? alone.Groups["value"] : null;
    }

    private static HashSet<string> Names(string code) => [.. Name().Matches(code).Select(name => name.Value)];

    private static bool Mentions(string code, string name) =>
        Regex.IsMatch(code, $@"(?<![\w$@]){Regex.Escape(name)}(?!\w)");

    /// <summary>Whether a function's name is the name worked out, or holds it: average, get_average and averageOf for average.</summary>
    private static bool NamedFor(string function, string target)
    {
        var functionParts = Parts(function);
        var targetParts = Parts(target.TrimStart('$', '@'));
        return targetParts.Count > 0 && targetParts.All(part => part.Length >= 3 && functionParts.Contains(part));
    }

    private static List<string> Parts(string name) =>
        [.. PartBreak().Split(name).Where(part => part.Length > 0).Select(part => part.ToLowerInvariant())];

    /// <summary>
    /// On a line that prints, the name printed in place of the one worked out: a label in quotes on the line names the one
    /// worked out - "Average:" for average - while the line prints a name it was worked out from, and not it. Null otherwise.
    /// </summary>
    private static Match? Printed(string line, string masked, string target, HashSet<string> from)
    {
        var labels = Quoted().Matches(line).Select(quoted => quoted.Value.ToLowerInvariant()).ToList();
        var words = Parts(target.TrimStart('$', '@'));
        if (words.Count == 0 || words.Any(word => word.Length < 3) || !labels.Any(label => words.All(label.Contains))) return null;
        if (Mentions(masked, target)) return null;

        // The names printed, outside the quotes: exactly one of those it was worked out from.
        var outside = Quoted().Replace(line, match => new string(' ', match.Length));
        var printed = Name().Matches(outside).Where(name => from.Contains(name.Value)).ToList();
        return printed is [var only] ? only : null;
    }

    private static LogicFinding Returned(string id, SourceFile source, int at, int line, Group given, string target, string function)
    {
        var text = source.Lines[line];
        var changed = text[..given.Index] + target + text[(given.Index + given.Length)..];

        return new LogicFinding(id, line + 1,
            $"`{function}` works out `{target}` on line {at + 1} and then gives back `{given.Value}` on line {line + 1}, which `{target}` was worked out " +
            $"from - `{target}` itself is used nowhere. A function named {function} most likely means to give back `{target}`.",
            LocalFix.ReplaceLine(id, $"Give back {target}, the value {function} works out",
                $"Line {at + 1} works out {target} and nothing reads it; line {line + 1} gives back {given.Value} instead. Giving back {target} makes the " +
                $"function return what its name says.",
                source.Path, line + 1, changed))
        {
            Severity = Severity.Warning,
            Confidence = Confidence.Likely,
        };
    }

    private static LogicFinding Printing(string id, SourceFile source, int at, int line, Match printed, string target)
    {
        var text = source.Lines[line];
        var changed = text[..printed.Index] + target + text[(printed.Index + printed.Length)..];

        return new LogicFinding(id, line + 1,
            $"Line {line + 1} prints `{printed.Value}` beside words that name `{target}`, which line {at + 1} works out from `{printed.Value}` - and " +
            $"`{target}` itself is used nowhere. It most likely means to print `{target}`.",
            LocalFix.ReplaceLine(id, $"Print {target}, the value the words beside it name",
                $"Line {at + 1} works out {target} and nothing reads it; line {line + 1} labels a value as {target} but prints {printed.Value}. Printing " +
                $"{target} makes it say what its label says.",
                source.Path, line + 1, changed))
        {
            Severity = Severity.Warning,
            Confidence = Confidence.Likely,
        };
    }
}
