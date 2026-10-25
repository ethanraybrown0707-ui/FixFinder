using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Code no run of the program can reach, so nothing in it is ever tested - each run, and none of its tools said anything:
/// a line straight after return in the same block never ran in C, C++ (gcc and g++ -Wall -Wextra), JavaScript, PHP, Perl
/// (perl -w), PowerShell and Fortran (gfortran -Wall) - "counted" was never printed - and in Ruby, whose ruby -w warns
/// "statement not reached": the warning and this check are one finding. A second case with the same value as one before it in
/// a switch was never matched in JavaScript and PHP; a function defined twice in one place in Python, JavaScript, Lua,
/// PowerShell and Ruby is replaced by the second before anything calls it - area(3) gave the second's answer, and JavaScript,
/// which sets every function up before the code runs, gave it even to calls before the second. Ruby warns of a method redefined
/// only while the program runs with -w, which is not how it is run, so its warning never reached the report (perl -w warns of a
/// sub redefined as it compiles). Python's if __name__ == "main": never runs its block, as a program run directly is named
/// "__main__".
/// </summary>
public static partial class DeadCodePatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        AfterLeaving("logic-code-after-return", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".js", ".mjs", ".cjs"), Syntax.CLike,
            @"^\s*(?<left>(?:return\b[^;{}]*|break|continue|throw\b[^;{}]*|exit\s*\([^;]*\)|goto\s+\w+)\s*;)\s*$", @"^\s*(?:\}|case\b|default\b|[A-Za-z_]\w*\s*:(?!:)|#|function\b|else\b)"),
        AfterLeaving("logic-php-code-after-return", CodePattern.Files(".php"), Syntax.CLike,
            @"^\s*(?<left>(?:return\b[^;{}]*|break|continue|throw\b[^;{}]*|exit\b[^;]*|die\b[^;]*)\s*;)\s*$", @"^\s*(?:\}|case\b|default\b|function\b|else\b|\?>)"),
        AfterLeaving("logic-perl-code-after-return", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            @"^\s*(?<left>(?:return\b|die\b|exit\b|last\b|next\b)(?:(?!\b(?:if|unless|or|and)\b)[^;{}])*;)\s*$", @"^\s*(?:\}|sub\b|__END__|__DATA__)"),
        AfterLeaving("logic-powershell-code-after-return", CodePattern.Files(".ps1"), Syntax.Python,
            @"^\s*(?<left>(?:return|break|continue|exit|throw)\b[^{};]*)\s*$", @"^\s*(?:\}|function\b|else\b|elseif\b|catch\b|finally\b)"),
        AfterLeaving("logic-fortran-code-after-return", CodePattern.Files(".f90", ".f95", ".f03", ".f08"), Syntax.Fortran,
            @"^\s*(?<left>(?:return|stop(?:\s+\S.*)?|exit|cycle))\s*$", @"^\s*(?:end\b|else\b|contains\b|case\b|\d+\s+continue\b)", RegexOptions.IgnoreCase),
        AfterLeaving("logic-ruby-code-after-return", CodePattern.Files(".rb"), Syntax.Python,
            @"^\s*(?<left>(?:return|break|next|redo|retry|raise|exit|abort)\b(?:(?!\b(?:if|unless|while|until|do)\b|\{).)*)\s*$",
            @"^\s*(?:end|else|elsif|rescue|ensure|when|in)\b"),
        new CodePattern("logic-js-duplicate-case", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, DuplicateCase, Severity.Error, Confidence.Certain),
        new CodePattern("logic-php-duplicate-case", CodePattern.Files(".php"), Syntax.CLike, DuplicateCase, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-defined-twice", CodePattern.Files(".py", ".pyw"), Syntax.Python,
            (id, source, masked) => DefinedTwice(id, source, masked, PythonDefinition(), false, StringComparison.Ordinal), Severity.Error, Confidence.Certain),
        new CodePattern("logic-js-defined-twice", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike,
            (id, source, masked) => DefinedTwice(id, source, masked, ScriptDefinition(), true, StringComparison.Ordinal), Severity.Error, Confidence.Certain),
        new CodePattern("logic-lua-defined-twice", CodePattern.Files(".lua"), Syntax.Lua,
            (id, source, masked) => DefinedTwice(id, source, masked, LuaDefinition(), false, StringComparison.Ordinal), Severity.Error, Confidence.Certain),
        new CodePattern("logic-powershell-defined-twice", CodePattern.Files(".ps1"), Syntax.Python,
            (id, source, masked) => DefinedTwice(id, source, masked, PowerShellDefinition(), false, StringComparison.OrdinalIgnoreCase), Severity.Error, Confidence.Certain),
        new CodePattern("logic-ruby-defined-twice", CodePattern.Files(".rb"), Syntax.Python,
            (id, source, masked) => DefinedTwice(id, source, masked, RubyDefinition(), false, StringComparison.Ordinal, decorators: false), Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-main-guard-misspelt", CodePattern.Files(".py", ".pyw"), Syntax.Python, MainGuardMisspelt, Severity.Error, Confidence.Certain),
    ];

    private static int IndentOf(string line) => line.Length - line.TrimStart().Length;

    private static CodePattern AfterLeaving(string id, IReadOnlySet<string> extensions, Syntax syntax, string leaves, string reachable, RegexOptions options = RegexOptions.None) =>
        new(id, extensions, syntax, (patternId, source, masked) => CodeAfterLeaving(patternId, source, masked, new Regex(leaves, options), new Regex(reachable, options)),
            Severity.Warning, Confidence.Certain);

    /// <summary>
    /// A statement that leaves the block - return, break, throw, exit - whole on its line, and a line of code straight after it at
    /// the same indentation: not the block's end, a case or a label another path jumps to, or a function set up before the code runs.
    /// </summary>
    private static IEnumerable<LogicFinding> CodeAfterLeaving(string id, SourceFile source, IReadOnlyList<string> masked, Regex leaves, Regex reachable)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (leaves.Match(masked[index]) is not { Success: true } left) continue;
            if (left.Groups["left"].Value.Count(c => c == '(') != left.Groups["left"].Value.Count(c => c == ')')) continue;

            var next = index + 1;
            while (next < masked.Count && masked[next].Trim().Length == 0) next++;
            if (next >= masked.Count || IndentOf(masked[next]) != IndentOf(masked[index]) || reachable.IsMatch(masked[next])) continue;

            var statement = source.Lines[index].Trim();
            yield return new LogicFinding(id, next + 1,
                $"This line comes straight after {statement} on line {index + 1}, in the same block, so the program has always left by the time it " +
                "would get here - it never runs, and no run of the program can test it",
                null);
        }
    }

    [GeneratedRegex(@"^(?<indent>\s*)switch\s*\(.*\)\s*\{\s*$")]
    private static partial Regex SwitchHeader();

    private static IEnumerable<LogicFinding> DuplicateCase(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (!SwitchHeader().IsMatch(masked[index]) || BraceBlocks.Body(masked, index) is not { } body) continue;

            // The cases of this switch: lines at the first case's indentation, its value read from the line as written.
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var caseIndent = -1;
            for (var line = body.First; line < body.End; line++)
            {
                var label = Regex.Match(masked[line], @"^(?<indent>\s*)case\s+(?<value>[^:]+?)\s*:");
                if (!label.Success) continue;
                if (caseIndent < 0) caseIndent = label.Groups["indent"].Length;
                if (label.Groups["indent"].Length != caseIndent) continue;

                var valueGroup = label.Groups["value"];
                var value = source.Lines[line].Substring(valueGroup.Index, valueGroup.Length).Trim();
                if (seen.TryGetValue(value, out var first))
                {
                    yield return new LogicFinding(id, line + 1,
                        $"case {value}: is the same as the case on line {first + 1}, which comes first - a switch takes the first case that matches, so " +
                        "this one is never matched, and what it does can never run",
                        null);
                    continue;
                }
                seen[value] = line;
            }
        }
    }

    [GeneratedRegex(@"^(?<indent>\s*)(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex PythonDefinition();

    [GeneratedRegex(@"^(?<indent>\s*)(?:async\s+)?function\s*\*?\s*(?<name>[A-Za-z_$][\w$]*)\s*\(")]
    private static partial Regex ScriptDefinition();

    [GeneratedRegex(@"^(?<indent>\s*)(?:local\s+)?function\s+(?<name>[A-Za-z_][\w.:]*)\s*\(")]
    private static partial Regex LuaDefinition();

    [GeneratedRegex(@"^(?<indent>\s*)function\s+(?<name>[A-Za-z_][\w-]*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellDefinition();

    [GeneratedRegex(@"^(?<indent>\s*)def\s+(?<name>(?:self\.)?[A-Za-z_]\w*[?!]?)(?=[\s(;]|$)")]
    private static partial Regex RubyDefinition();

    /// <summary>The line that holds a line - the nearest line above it with less indentation - or -1 at the top of the file.</summary>
    private static int Holder(IReadOnlyList<string> masked, int line)
    {
        var indent = IndentOf(masked[line]);
        for (var above = line - 1; above >= 0; above--)
            if (masked[above].Trim().Length > 0 && IndentOf(masked[above]) < indent) return above;
        return -1;
    }

    /// <summary>
    /// A function defined twice in one place - the same name, at the same indentation, held by the same line - with nothing in
    /// between that uses it, or, in JavaScript, which sets every function up before the code runs, whatever is in between. A
    /// decorated Python function - a property's setter, an overload - is left alone.
    /// </summary>
    private static IEnumerable<LogicFinding> DefinedTwice(string id, SourceFile source, IReadOnlyList<string> masked, Regex definition, bool setUpFirst, StringComparison comparison,
        bool decorators = true)
    {
        var definitions = masked.Select((line, index) => (Index: index, Match: definition.Match(line))).Where(each => each.Match.Success).ToList();
        foreach (var (first, firstMatch) in definitions)
        {
            var name = firstMatch.Groups["name"].Value;
            if (decorators && Decorated(masked, first)) continue;

            var second = definitions.FirstOrDefault(each => each.Index > first && string.Equals(each.Match.Groups["name"].Value, name, comparison) &&
                                                            each.Match.Groups["indent"].Length == firstMatch.Groups["indent"].Length);
            if (second.Match is null || (decorators && Decorated(masked, second.Index)) || Holder(masked, second.Index) != Holder(masked, first)) continue;

            // Nothing between them at less indentation - they are in one block - and, unless every function is set up first, no use of the name.
            var between = Enumerable.Range(first + 1, second.Index - first - 1).ToList();
            if (between.Any(line => masked[line].Trim().Length > 0 && IndentOf(masked[line]) < firstMatch.Groups["indent"].Length)) continue;
            var used = new Regex($@"(?<![\w$.-]){Regex.Escape(name)}(?![\w$-])", comparison == StringComparison.OrdinalIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            if (!setUpFirst && between.Any(line => used.IsMatch(masked[line]))) continue;

            yield return new LogicFinding(id, first + 1,
                $"{name} is defined again on line {second.Index + 1}, and that definition takes this one's place " +
                (setUpFirst ? "- JavaScript sets every function up before the code runs, so even a call before line " + (second.Index + 1) + " gets the second -"
                    : "before anything here uses it -") +
                " so this one can never run, and no call can test it",
                null);
        }
    }

    /// <summary>Whether a Python definition has a decorator on the line above it - @property's setter, @overload, @register.</summary>
    private static bool Decorated(IReadOnlyList<string> masked, int line)
    {
        var above = line - 1;
        while (above >= 0 && masked[above].Trim().Length == 0) above--;
        return above >= 0 && masked[above].TrimStart().StartsWith('@');
    }

    [GeneratedRegex(@"\b__name__\s*==\s*(?<quote>[""'])|(?<quote>[""'])\s*==\s*__name__\b")]
    private static partial Regex NameCompared();

    private static IEnumerable<LogicFinding> MainGuardMisspelt(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (!masked[index].TrimStart().StartsWith("if", StringComparison.Ordinal) || NameCompared().Match(masked[index]) is not { Success: true }) continue;

            var line = source.Lines[index];
            // The text compared, read from the line as written: masking blanks the inside of the quotes.
            var text = Regex.Match(line, @"(?<quote>[""'])(?<text>[^""']*)\k<quote>");
            if (!text.Success) continue;
            var written = text.Groups["text"].Value;
            if (written == "__main__" || !written.Contains("main", StringComparison.OrdinalIgnoreCase)) continue;

            var textGroup = text.Groups["text"];
            yield return new LogicFinding(id, index + 1,
                $"A program run directly is named \"__main__\" - with two underscores either side - so __name__ is never \"{written}\", and nothing " +
                "under this if ever runs: the program does nothing, and nothing in its main part can be tested",
                LocalFix.ReplaceLine(id, "Name it as Python does: \"__main__\"",
                    "__name__ is \"__main__\" when the file is run directly, so the block runs then, and not when the file is imported.",
                    source.Path, index + 1, line[..textGroup.Index] + "__main__" + line[(textGroup.Index + textGroup.Length)..]));
        }
    }
}
