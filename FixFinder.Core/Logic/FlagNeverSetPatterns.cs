using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A flag a loop never sets, in the languages whose values FixFinder does not follow through the code: <c>found = false</c>
/// on the line before a loop with an if in it, nothing in the loop or after it changing the flag, and an if after the loop
/// testing it - so <c>if found</c> is never true and <c>if not found</c> always is. TypeScript, Ruby, PHP, Lua, Perl, Dart,
/// PowerShell, Scala, Fortran, Rust, F# and OCaml; Python, Java, C#, JavaScript, Go, C and C++ have it found by following
/// the code, analysis-flag-never-set.
/// </summary>
/// <remarks>
/// The flag is set off - false, or 0 in Perl - on the line straight before the loop's header, at the same indentation; no
/// line from there to the end of the block names it but the test, which is the first line after the loop to name it, at
/// the flag's indentation, and tests the flag alone or not the flag - <c>if found</c>, <c>unless found</c>,
/// <c>if (-not $found)</c>, <c>if (.not. found) then</c>, OCaml's <c>if !found</c>. Any other line naming the flag - one
/// passing it to a function, or reading it in the loop - leaves it alone. Elixir and Erlang do not let a loop change a
/// variable outside it.
/// </remarks>
public static partial class FlagNeverSetPatterns
{
    /// <summary>
    /// How one language sets a flag off, starts a loop and tests a flag: <c>SetOff</c> with the flag in <c>flag</c> and the
    /// value in <c>off</c>; <c>Test</c>, with the flag's name <c>{F}</c>, an if of the flag - <c>negated</c> set where it asks
    /// for not the flag.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex SetOff, Regex LoopHeader, string Test,
        bool IgnoreCase = false, bool OnlyTypeScript = false);

    private static readonly Dialect[] Dialects =
    [
        new("typescript", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike,
            new(@"^\s*(?:let|var)\s+(?<flag>(?>[A-Za-z_$][\w$]*))\s*=\s*(?<off>false)\s*;?\s*$"), BraceLoop(),
            @"^\s*if\s*\(\s*(?<negated>!\s*)?{F}\s*\)\s*\{?", OnlyTypeScript: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^\s*(?<flag>(?>[a-z_]\w*))\s*=\s*(?<off>false)\s*$"), RubyLoop(),
            @"^\s*(?:if\s+(?:(?<negated>!|not\s+))?{F}|(?<negated>unless)\s+{F})\s*(?:then\s*)?$"),
        new("php", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^\s*(?<flag>\$(?>\w+))\s*=\s*(?<off>false)\s*;\s*$", RegexOptions.IgnoreCase), BraceLoop(),
            @"^\s*if\s*\(\s*(?<negated>!\s*)?{F}\s*\)\s*\{?\s*$", IgnoreCase: true),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^\s*local\s+(?<flag>(?>\w+))\s*=\s*(?<off>false)\s*$"), LuaLoop(),
            @"^\s*if\s+(?<negated>not\s+)?{F}\s+then\s*$"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^\s*my\s+(?<flag>\$(?>\w+))\s*=\s*(?<off>0)\s*;\s*$"), PerlLoop(),
            @"^\s*(?:if\s*\(\s*(?<negated>!\s*)?{F}\s*\)|(?<negated>unless)\s*\(\s*{F}\s*\))\s*\{?\s*$"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike,
            new(@"^\s*(?:var|bool)\s+(?<flag>(?>\w+))\s*=\s*(?<off>false)\s*;\s*$"), BraceLoop(),
            @"^\s*if\s*\(\s*(?<negated>!\s*)?{F}\s*\)\s*\{?\s*$"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^\s*(?<flag>\$(?>\w+))\s*=\s*(?<off>\$false)\s*$", RegexOptions.IgnoreCase), PowerShellLoop(),
            @"^\s*if\s*\(\s*(?<negated>-not\s+|!\s*)?{F}\s*\)\s*\{?\s*$", IgnoreCase: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"^\s*var\s+(?<flag>(?>\w+))\s*(?::\s*Boolean\s*)?=\s*(?<off>false)\s*$"), ScalaLoop(),
            @"^\s*if\s*\(\s*(?<negated>!\s*)?{F}\s*\)\s*\{?\s*$"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran,
            new(@"^\s*(?<flag>(?>[A-Za-z]\w*))\s*=\s*(?<off>\.false\.)\s*$", RegexOptions.IgnoreCase), FortranLoop(),
            @"^\s*if\s*\(\s*(?<negated>\.not\.\s*)?{F}\s*\)\s*then\s*$", IgnoreCase: true),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^\s*let\s+(?:mut\s+)?(?<flag>(?>\w+))\s*(?::\s*bool\s*)?=\s*(?<off>false)\s*;\s*$"), RustLoop(),
            @"^\s*if\s+(?<negated>!\s*)?{F}\s*\{\s*$"),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            new(@"^\s*let\s+mutable\s+(?<flag>(?>\w+))\s*=\s*(?<off>false)\s*$"), MlLoop(),
            @"^\s*if\s+(?<negated>not\s+)?\(?\s*{F}\s*\)?\s+then\b"),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml,
            new(@"^\s*let\s+(?<flag>(?>\w+))\s*=\s*ref\s+(?<off>false)\s+in\s*$"), MlLoop(),
            @"^\s*if\s+(?<negated>not\s+)?\(?\s*!{F}\s*\)?\s+then\b"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-flag-never-set", dialect.Files, dialect.Syntax,
            (id, source, masked) => NeverSet(id, source, masked, dialect))),
    ];

    private static IEnumerable<LogicFinding> NeverSet(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (dialect.OnlyTypeScript && !TypeScriptCheck.IsTypeScript(source.Path)) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

        for (var set = 0; set < masked.Count; set++)
        {
            if (dialect.SetOff.Match(masked[set]) is not { Success: true } setOff) continue;

            var indent = CodeText.Indentation(masked[set]).Length;
            var loop = NextCode(masked, set + 1);
            if (loop < 0 || CodeText.Indentation(masked[loop]).Length != indent || !dialect.LoopHeader.IsMatch(masked[loop])) continue;

            // The loop's lines: deeper than its header, with its own end at the header's indentation.
            var loopEnd = loop + 1;
            while (loopEnd < masked.Count && (IsBlank(masked[loopEnd]) || CodeText.Indentation(masked[loopEnd]).Length > indent)) loopEnd++;
            if (!Enumerable.Range(loop + 1, loopEnd - loop - 1).Any(line => AnIf().IsMatch(masked[line]))) continue;

            // The block the flag is set in ends at the first line indented less than it.
            var blockEnd = loopEnd;
            while (blockEnd < masked.Count && (IsBlank(masked[blockEnd]) || CodeText.Indentation(masked[blockEnd]).Length >= indent)) blockEnd++;

            // Mentions in code, not in text - "found it" names nothing - and none of them may give the flag a value or hand it
            // to a call that may; a condition before the test may read it.
            var flag = setOff.Groups["flag"].Value;
            var mention = new Regex($@"(?<![\w$]){Regex.Escape(flag)}(?![\w$])", options);
            var mentions = Enumerable.Range(set + 1, blockEnd - set - 1).Where(line => mention.IsMatch(masked[line])).ToList();
            if (mentions.Any(line => Gives(masked[line], flag, options))) continue;

            // The test is the first line naming it from the loop's end on, at the flag's indentation.
            var tested = mentions.FirstOrDefault(line => line >= loopEnd, -1);
            if (tested < 0 || CodeText.Indentation(masked[tested]).Length != indent) continue;
            if (mentions.Any(line => line < tested && !ACondition().IsMatch(masked[line]))) continue;
            var test = new Regex(dialect.Test.Replace("{F}", Regex.Escape(flag)), options).Match(masked[tested]);
            if (!test.Success) continue;

            var negated = test.Groups["negated"].Success;
            var off = setOff.Groups["off"].Value;
            yield return new LogicFinding(id, tested + 1,
                $"`{flag}` is set to `{off}` on line {set + 1} and nothing changes it - the loop on line {loop + 1} never sets it - so the test of " +
                $"`{(negated ? "not " : "")}{flag}` on line {tested + 1} is {(negated ? "always" : "never")} true",
                null);
        }
    }

    /// <summary>
    /// Whether a line gives the flag a value - =, +=, :=, &lt;-, ++ - or hands it to a call that may: an argument of its own,
    /// as PHP's references, Perl's \$flag, PowerShell's [ref] and Fortran's arguments let a call set it.
    /// </summary>
    private static bool Gives(string line, string flag, RegexOptions options)
    {
        var named = Regex.Escape(flag);
        return Regex.IsMatch(line,
            $@"(?<![\w$]){named}\s*(?:[-+*/|&]?=(?!=)|:=|<-|\+\+|--)|(?:\+\+|--|\\)\s*{named}(?![\w$])|" +
            $@"(?<![\w$.-])(?!(?:if|elsif|elseif|unless|while|until|for|foreach|switch|return|not|and|or)\b)[A-Za-z_][\w.:-]*\s*\((?:[^()]*,)?\s*(?:\[ref\]\s*)?&?\s*{named}\s*[,)]",
            options);
    }

    /// <summary>A line that only tests a condition: if, elsif, unless, while, until.</summary>
    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:if|unless|elsif|elseif|else\s+if|while|until)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ACondition();

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static int NextCode(IReadOnlyList<string> masked, int from)
    {
        for (var line = from; line < masked.Count; line++)
            if (!IsBlank(masked[line])) return line;
        return -1;
    }

    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:if|unless|elsif|elseif|else\s+if)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AnIf();

    [GeneratedRegex(@"^\s*(?:for|foreach|while|do)\b.*\{\s*$")]
    private static partial Regex BraceLoop();

    [GeneratedRegex(@"^\s*(?:\S.*\.(?:each\w*|times|upto|downto|step)\b.*\bdo\b.*|for\s+.+\s+in\s+.+|(?:while|until)\s+.+|loop\s+do)\s*$")]
    private static partial Regex RubyLoop();

    [GeneratedRegex(@"^\s*(?:for|while)\b.*\bdo\s*$|^\s*repeat\s*$")]
    private static partial Regex LuaLoop();

    [GeneratedRegex(@"^\s*(?:for|foreach|while|until)\b.*\{\s*$")]
    private static partial Regex PerlLoop();

    [GeneratedRegex(@"^\s*(?:for|foreach|while|do)\b.*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellLoop();

    [GeneratedRegex(@"^\s*(?:for|while)\s*\(.*\)\s*\{\s*$|^\s*\S.*\.foreach\s*\{.*$")]
    private static partial Regex ScalaLoop();

    [GeneratedRegex(@"^\s*(?:\w+\s*:\s*)?do\b", RegexOptions.IgnoreCase)]
    private static partial Regex FortranLoop();

    [GeneratedRegex(@"^\s*(?:for|while)\b.*\{\s*$|^\s*loop\s*\{\s*$")]
    private static partial Regex RustLoop();

    [GeneratedRegex(@"^\s*(?:for|while)\b.*\bdo\s*$")]
    private static partial Regex MlLoop();
}
