using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A loop that stops after its first item, in the languages the C-like check and Python's do not read: the last thing in the
/// loop is an if that returns when the item matches and an else that returns when it does not, so whichever way the first item
/// goes the loop ends there and nothing after it is looked at - <c>if item == wanted { return true } else { return false }</c>.
/// Go, Rust, Dart, PHP, Perl, Scala, PowerShell, Ruby, Lua and Fortran.
/// </summary>
/// <remarks>
/// The if has to end with a return, and the else hold nothing but one - in Fortran, after the value it gives the function's
/// result - each a whole line, never under a modifier such as <c>return 1 if found</c>; the if must start a chain, not be an
/// else-if, and the loop has to end straight after the else does. An else that gives back the loop's own variable - the first
/// item itself - is left alone, as giving back the first item may be what was meant. The change takes the else out: where
/// the line after the loop already gives back what the else did, that is all; where the loop is the last thing in a function
/// or an if, the else's return goes after the loop, reached only when no item matched. Where anything else follows, or the
/// loop is inside another loop, no change is offered, as moving the return there would change what that code does.
/// </remarks>
public static partial class ReturnInLoopPatterns
{
    /// <summary>
    /// How one language writes a loop: its header, with the names it gives each item in the group <c>variables</c>; whether
    /// its blocks end with a word - end, end do, end if - rather than a brace; and whether its words ignore case.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex LoopHeader, bool BlocksEndWithAWord = false,
        bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("go", CodePattern.Files(".go"), Syntax.CLike, GoLoop()),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, RustLoop()),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, DartLoop()),
        new("php", CodePattern.Files(".php"), Syntax.CLike, PhpLoop(), IgnoreCase: true),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlLoop()),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaLoop()),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, PowerShellLoop(), IgnoreCase: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, RubyLoop(), BlocksEndWithAWord: true),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, LuaLoop(), BlocksEndWithAWord: true),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, FortranLoop(), BlocksEndWithAWord: true, IgnoreCase: true),
    ];

    /// <summary>Words a loop header uses beside the names it gives each item: Rust's mut, Perl's my, Dart's final.</summary>
    private static readonly HashSet<string> NotItemNames = ["mut", "my", "our", "var", "final", "const", "ref", "_"];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-return-in-loop", dialect.Files, dialect.Syntax,
            (id, source, masked) => StopsAfterTheFirstItem(id, source, masked, dialect))),
    ];

    /// <summary>The lines of one loop whose last statement is an if and an else that both return.</summary>
    private sealed record ReturningBranches(int Loop, int ElseLine, int ResultLine, int ReturnLine, int CloseElse, int CloseLoop);

    private static IEnumerable<LogicFinding> StopsAfterTheFirstItem(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var lines = source.Lines;
        var elseHeader = dialect.BlocksEndWithAWord ? ElseWord() : ElseBetweenBraces();

        for (var elseLine = 0; elseLine < masked.Count; elseLine++)
        {
            if (!elseHeader.IsMatch(masked[elseLine])) continue;
            if (Branches(masked, dialect, elseLine) is not { } branches) continue;

            // What the else gives back - or, in Fortran, the value it gives the result - is the first item itself when it
            // names the loop's own variable, and that may be what was meant.
            var elseGives = ElseGives(lines, branches);
            var itemNames = dialect.LoopHeader.Match(masked[branches.Loop]).Groups["variables"].Captures
                .SelectMany(capture => NameInHeader().Matches(capture.Value).Select(name => name.Value))
                .Where(name => !NotItemNames.Contains(name));
            var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            if (itemNames.Any(name => Regex.IsMatch(elseGives, $@"(?<![\w$@]){Regex.Escape(name)}(?!\w)", options))) continue;

            yield return new LogicFinding(id, elseLine + 1,
                "the loop returns on its first pass whichever way the `if` goes, so it never looks past the first item",
                Change(id, source, masked, dialect, branches, elseGives));
        }
    }

    /// <summary>
    /// The loop around an else at <paramref name="elseLine"/>, when the else holds only a return and closes straight before
    /// the loop does, and the if before it starts a chain and ends with a return of its own; null otherwise.
    /// </summary>
    private static ReturningBranches? Branches(IReadOnlyList<string> masked, Dialect dialect, int elseLine)
    {
        // The else's body: its return - in Fortran, after the value it gives the function's result.
        var returnLine = NextCode(masked, elseLine + 1);
        var resultLine = -1;
        if (returnLine < 0) return null;
        if (dialect.Language == "fortran" && FortranAssignment().IsMatch(masked[returnLine]))
        {
            resultLine = returnLine;
            returnLine = NextCode(masked, resultLine + 1);
            if (returnLine < 0) return null;
        }
        if (!IsWholeReturn(masked[returnLine])) return null;

        var closeElse = NextCode(masked, returnLine + 1);
        var closeLoop = closeElse < 0 ? -1 : NextCode(masked, closeElse + 1);
        if (closeLoop < 0 || !ClosesABlock(masked[closeElse], dialect) || !ClosesABlock(masked[closeLoop], dialect)) return null;

        var elseIndent = CodeText.Indentation(masked[elseLine]).Length;
        var loopIndent = CodeText.Indentation(masked[closeLoop]).Length;
        if (CodeText.Indentation(masked[closeElse]).Length != elseIndent || loopIndent >= elseIndent) return null;

        // The loop is the header just above at the indentation of its end, with everything between deeper than it.
        var loop = PreviousCodeAt(masked, elseLine - 1, loopIndent);
        if (loop < 0 || !dialect.LoopHeader.IsMatch(masked[loop])) return null;
        if (!Enumerable.Range(loop + 1, closeLoop - loop - 1).All(line => IsBlank(masked[line]) || CodeText.Indentation(masked[line]).Length > loopIndent)) return null;

        // The if is the header just above the else at its indentation, with everything between deeper - its own branch.
        var ifLine = PreviousCodeAt(masked, elseLine - 1, elseIndent);
        if (ifLine <= loop || !(dialect.BlocksEndWithAWord ? IfWord() : IfBrace()).IsMatch(masked[ifLine])) return null;
        if (!Enumerable.Range(ifLine + 1, elseLine - ifLine - 1).All(line => IsBlank(masked[line]) || CodeText.Indentation(masked[line]).Length > elseIndent)) return null;

        // The if's branch ends with a return at the same depth as the else's - not one inside a further block.
        var lastOfIf = PreviousCode(masked, elseLine - 1);
        if (lastOfIf <= ifLine || !IsWholeReturn(masked[lastOfIf]) ||
            CodeText.Indentation(masked[lastOfIf]).Length != CodeText.Indentation(masked[returnLine]).Length) return null;

        return new ReturningBranches(loop, elseLine, resultLine, returnLine, closeElse, closeLoop);
    }

    /// <summary>What the else gives back, as written: its return's value - or, in Fortran, the line giving the result its value.</summary>
    private static string ElseGives(IReadOnlyList<string> lines, ReturningBranches branches) =>
        branches.ResultLine >= 0
            ? lines[branches.ResultLine].Trim()
            : ReturnValue().Match(lines[branches.ReturnLine]) is { Success: true } returned ? returned.Groups["value"].Value.Trim() : "";

    /// <summary>
    /// The else taken out: just that, where the line after the loop already gives back what the else did; the else's return
    /// put after the loop, where the loop is the last thing in a function or an if; null otherwise.
    /// </summary>
    private static LocalFix? Change(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, ReturningBranches branches, string elseGives)
    {
        var lines = source.Lines;
        var after = NextCode(masked, branches.CloseLoop + 1);
        if (after < 0) return null;

        var comparison = dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var elseStatement = Statement(lines[branches.ResultLine >= 0 ? branches.ResultLine : branches.ReturnLine]);
        var afterStatement = Statement(lines[after]);
        var whatTheElseDoes = branches.ResultLine >= 0
            ? $"`{elseStatement}` and `{Statement(lines[branches.ReturnLine])}` end the search"
            : $"`{elseStatement}` ends the search";

        // The if's end: a brace at the else's indentation, or the word that closed the else, now closing the if.
        var ifEnd = dialect.BlocksEndWithAWord ? lines[branches.CloseElse] : $"{CodeText.Indentation(lines[branches.ElseLine])}}}";

        if (string.Equals(afterStatement, elseStatement, comparison) || (elseGives.Length > 0 && string.Equals(afterStatement, elseGives, comparison)))
        {
            return new LocalFix
            {
                RuleId = id,
                Title = "Take out the else, so every item is looked at",
                Explanation =
                    $"Both branches return, so the loop stops on its first pass: if the first item does not match, {whatTheElseDoes} before any " +
                    $"other item is looked at. The line after the loop, `{afterStatement}`, already does the same once every item has been " +
                    "checked, so the `else` can go.",
                File = source.Path, StartLine = branches.ElseLine + 1, RemoveCount = branches.CloseLoop - branches.ElseLine + 1,
                NewLines = [ifEnd, lines[branches.CloseLoop]],
            };
        }

        // Moving the return after the loop is right only where nothing else would run there: the end of a function or an if.
        if (branches.ResultLine >= 0 || !ClosesABlock(masked[after], dialect)) return null;
        var afterIndent = CodeText.Indentation(masked[after]).Length;
        var opener = PreviousCodeAt(masked, branches.Loop - 1, afterIndent);
        if (afterIndent >= CodeText.Indentation(masked[branches.Loop]).Length || opener < 0 || dialect.LoopHeader.IsMatch(masked[opener])) return null;

        return new LocalFix
        {
            RuleId = id,
            Title = $"Only {elseStatement} once every item has been checked",
            Explanation =
                $"Both branches return, so the loop stops on its first pass: if the first item does not match, {whatTheElseDoes} before any " +
                "other item is looked at. The `else` belongs after the loop - reached only when no item matched.",
            File = source.Path, StartLine = branches.ElseLine + 1, RemoveCount = branches.CloseLoop - branches.ElseLine + 1,
            NewLines = [ifEnd, lines[branches.CloseLoop], $"{CodeText.Indentation(lines[branches.Loop])}{lines[branches.ReturnLine].Trim()}"],
        };
    }

    /// <summary>A line's statement as written, without its indentation or a closing semicolon.</summary>
    private static string Statement(string line) => line.Trim().TrimEnd(';').TrimEnd();

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static bool IsWholeReturn(string masked) =>
        ReturnValue().Match(masked) is { Success: true } returned && !Modifier().IsMatch(returned.Groups["value"].Value);

    private static bool ClosesABlock(string masked, Dialect dialect) =>
        dialect.BlocksEndWithAWord ? EndWord().IsMatch(masked) : masked.Trim() == "}";

    private static int NextCode(IReadOnlyList<string> masked, int from)
    {
        for (var line = from; line < masked.Count; line++)
            if (!IsBlank(masked[line])) return line;
        return -1;
    }

    private static int PreviousCode(IReadOnlyList<string> masked, int from)
    {
        for (var line = from; line >= 0; line--)
            if (!IsBlank(masked[line])) return line;
        return -1;
    }

    /// <summary>The nearest line at or above <paramref name="from"/> indented by exactly <paramref name="indent"/> characters.</summary>
    private static int PreviousCodeAt(IReadOnlyList<string> masked, int from, int indent)
    {
        for (var line = from; line >= 0; line--)
            if (!IsBlank(masked[line]) && CodeText.Indentation(masked[line]).Length == indent) return line;
        return -1;
    }

    [GeneratedRegex(@"^\s*\}\s*else\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ElseBetweenBraces();

    [GeneratedRegex(@"^\s*else\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ElseWord();

    [GeneratedRegex(@"^\s*(?:end(?:\s*(?:if|do))?|enddo|endif)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EndWord();

    /// <summary>An if that starts a chain and opens its branch with a brace - not an else-if.</summary>
    [GeneratedRegex(@"^\s*(?:if|unless)\b.*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IfBrace();

    /// <summary>An if that starts a chain whose blocks end with a word: Ruby's if and unless, Lua's and Fortran's if.</summary>
    [GeneratedRegex(@"^\s*(?:if|unless)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IfWord();

    /// <summary>A line that is a return and nothing more, with what it gives back.</summary>
    [GeneratedRegex(@"^\s*return\b(?<value>[^;]*);?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReturnValue();

    /// <summary>A modifier after a statement, as in Ruby's and Perl's return 1 if found: the return happens only sometimes.</summary>
    [GeneratedRegex(@"\b(?:if|unless|when)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Modifier();

    /// <summary>A Fortran line giving a name a value: has_item = .false.</summary>
    [GeneratedRegex(@"^\s*[A-Za-z]\w*\s*=(?!=)")]
    private static partial Regex FortranAssignment();

    /// <summary>A name in a loop header, with PHP's, Perl's and PowerShell's $ kept.</summary>
    [GeneratedRegex(@"[$@]?[A-Za-z_]\w*")]
    private static partial Regex NameInHeader();

    /// <summary>for _, item := range items {, for index := 0; ... {, for condition {, for {.</summary>
    [GeneratedRegex(@"^\s*for(?:\s+(?<variables>(?>\w+(?:\s*,\s*\w+)*))\s*:?=.*|\b.*)\{\s*$")]
    private static partial Regex GoLoop();

    /// <summary>for item in items {, for (index, item) in ... {, while ... {, loop {.</summary>
    [GeneratedRegex(@"^\s*(?:for\s+(?<variables>&?\s*(?:mut\s+)?\w+|\([^)]*\))\s+in\b.*|while\b.*|loop\s*)\{\s*$")]
    private static partial Regex RustLoop();

    /// <summary>for (var item in items) {, for (int index = 0; ...) {, while (...) {.</summary>
    [GeneratedRegex(@"^\s*(?:for\s*\(\s*(?:(?:final|var|const)\s+)?(?:[A-Za-z_]\w*(?:<[^>]*>)?\??\s+)?(?<variables>[A-Za-z_]\w*)\s+in\b.*|for\s*\(\s*(?:(?:final|var)\s+)?(?:[A-Za-z_]\w*\s+)?(?<variables>[A-Za-z_]\w*)\s*=.*|for\s*\(.*|while\s*\(.*)\)\s*\{\s*$")]
    private static partial Regex DartLoop();

    /// <summary>foreach ($items as $item) {, foreach ($items as $key => $item) {, for ($index = 0; ...) {, while (...) {.</summary>
    [GeneratedRegex(@"^\s*(?:foreach\s*\(.*\bas\s+(?<variables>(?:&?\$\w+\s*=>\s*)?&?\$\w+)\s*|for\s*\((?:\s*(?<variables>\$\w+)\s*=)?.*|while\s*\(.*)\)\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PhpLoop();

    /// <summary>foreach my $item (@items) {, for (my $index = 0; ...) {, while (...) {.</summary>
    [GeneratedRegex(@"^\s*(?:(?:foreach|for)\s+(?:(?:my|our)\s+)?(?<variables>\$\w+)\s*\(.*\)|(?:foreach|for)\s*\((?:\s*(?:my\s+)?(?<variables>\$\w+)\s*=)?.*\)|(?:while|until)\s*\(.*\))\s*\{\s*$")]
    private static partial Regex PerlLoop();

    /// <summary>for (item &lt;- items) {, for ((key, value) &lt;- pairs) {, while (...) {.</summary>
    [GeneratedRegex(@"^\s*(?:for\s*\(\s*(?<variables>\w+|\([^)]*\))\s*<-.*\)|while\s*\(.*\))\s*\{\s*$")]
    private static partial Regex ScalaLoop();

    /// <summary>foreach ($item in $items) {, for ($index = 0; ...) {, while (...) {. Not ForEach-Object, whose return goes on to the next item.</summary>
    [GeneratedRegex(@"^\s*(?:foreach\s*\(\s*(?<variables>\$\w+)\s+in\b.*|for\s*\((?:\s*(?<variables>\$\w+)\s*=)?.*|while\s*\(.*)\)\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellLoop();

    /// <summary>items.each do |item|, 3.times do |index|, for item in items, while ..., until ..., loop do.</summary>
    [GeneratedRegex(@"^\s*(?:\S.*\.(?:each\w*|times|upto|downto|step)\b.*\bdo\s*\|(?<variables>[^|]*)\||for\s+(?<variables>(?>\w+(?:\s*,\s*\w+)*))\s+in\b.*|(?:while|until)\b.*|loop\s+do)\s*$")]
    private static partial Regex RubyLoop();

    /// <summary>for _, item in ipairs(items) do, for index = 1, #items do, while ... do.</summary>
    [GeneratedRegex(@"^\s*(?:for\s+(?<variables>(?>\w+(?:\s*,\s*\w+)*))\s+in\b.*|for\s+(?<variables>(?>\w+))\s*=.*|while\b.*)\bdo\s*$")]
    private static partial Regex LuaLoop();

    /// <summary>do index = 1, size(items), do while (...), a named loop's search: do.</summary>
    [GeneratedRegex(@"^\s*(?:\w+\s*:\s*)?do\b(?:\s+(?<variables>(?>\w+))\s*=)?", RegexOptions.IgnoreCase)]
    private static partial Regex FortranLoop();
}
