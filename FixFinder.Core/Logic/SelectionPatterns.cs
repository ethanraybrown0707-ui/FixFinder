using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// A quickselect looking for the item at a place in the part above the pivot with the same place it was given. The place
/// counts from the start of the whole list, and everything up to the pivot - num_lessoreq items - comes before that part, so
/// within it the place is k - num_lessoreq. Given k, it looks for a later item than the one wanted, and can look past the
/// part's end: Python's IndexError, Java's IndexOutOfBoundsException - or calls that go on with nothing. QuixBugs' kth is this
/// mistake, in Python and in Java. The change takes those items away: kth(above, k - num_lessoreq), as QuixBugs' correct
/// program has it.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a function calls itself with a part of its list in the list's place - a name made
/// from the items above something, above = [x for x in arr if x &gt; pivot], above.add(x) under if x &gt; pivot, pack(items,
/// items &gt; pivot) - and its place parameter in the place's own, unchanged, in a branch taken when the place is at or past a
/// count: k &gt;= num_lessoreq, k &gt; less_or_equal. A call into the part below, which starts where the whole does, keeps k, and
/// is left alone.
/// </remarks>
public static partial class SelectionPatterns
{
    /// <summary>How one language is read, and whether its functions are called as commands - Get-Kth $above $k - as PowerShell's are.</summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, bool ColonEnds = false, bool IgnoreCase = false, bool Commands = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, ColonEnds: true),
        new("java", CodePattern.Files(".java"), Syntax.CLike),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike),
        new("c", CodePattern.Files(".c"), Syntax.CLike),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike),
        new("go", CodePattern.Files(".go"), Syntax.CLike),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua),
        new("php", CodePattern.Files(".php"), Syntax.CLike),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, IgnoreCase: true, Commands: true),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, IgnoreCase: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-place-not-taken-away", dialect.Files, dialect.Syntax,
            (id, source, masked) => PlacesNotTakenAway(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>A test's word: if, elif, else if, elsif, elseif.</summary>
    [GeneratedRegex(@"(?<![\w$.@-])(?:else\s+if|elseif|elsif|elif|if)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWord();

    /// <summary>Something above something else: &gt;, &gt;=, -gt, -ge, .gt. - not -&gt;, =&gt; or &gt;&gt;.</summary>
    [GeneratedRegex(@"(?<![-=>])>(?![>])|-g[te]\b|\.g[te]\.", RegexOptions.IgnoreCase)]
    private static partial Regex Above();

    /// <summary>A comparison of something above something else, as code spaces it - x &gt; pivot, $_ -gt $pivot, items .gt. pivot - not a type's &lt;int&gt;.</summary>
    [GeneratedRegex(@"\s>=?\s|-g[te]\b|\.g[te]\.", RegexOptions.IgnoreCase)]
    private static partial Regex SpacedAbove();

    /// <summary>
    /// A collection made or filled: filter, where, select, grep, pack, append, add, push, insert, &lt;&lt;, a comprehension's for - or
    /// an array filled by its next place: above[num_above++] = x, above[#above + 1] = x.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:filter|where|select|grep|pack|append|add|push|push_back|insert|Where-Object)\b|<<|\bfor\b|\[\s*#?[$@]?\w+\s*(?:\+\+|\+\s*1)\s*\]\s*=(?!=)", RegexOptions.IgnoreCase)]
    private static partial Regex Collected();

    /// <summary>Whether the file makes a part from the items above something anywhere - the comparison and a collection made on one line or the next.</summary>
    private static bool MakesAnAbovePart(IReadOnlyList<string> masked)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            if (!SpacedAbove().IsMatch(masked[line])) continue;
            if (Collected().IsMatch(masked[line]) || line + 1 < masked.Count && Collected().IsMatch(masked[line + 1])) return true;
        }

        return false;
    }

    private static IEnumerable<LogicFinding> PlacesNotTakenAway(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (!MakesAnAbovePart(masked)) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var names = dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var function in SortingPatterns.FunctionsIn(masked, Path.GetExtension(source.Path).ToLowerInvariant()))
        {
            if (function.Parameters.Count < 2) continue;

            // Made only for a function whose body names it, as one calling itself does.
            Regex? callsItself = null;
            foreach (var line in function.Body)
            {
                if (!masked[line].Contains(function.Name, dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                callsItself ??= new Regex($@"(?<![\w$.@-])(?:(?:self|this)\s*\.\s*)?{Regex.Escape(function.Name)}(?<open>\s*\(|\s+(?=[$(@\w\\-]))", options);
                foreach (var call in SelfCalls(masked[line], callsItself, dialect))
                {
                    if (call.Arguments.Count != function.Parameters.Count) continue;
                    if (Read(id, source, masked, dialect, function, line, call, names, options) is { } finding)
                    {
                        yield return finding;
                        break;
                    }
                }
            }
        }
    }

    private static LogicFinding? Read(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect,
        (int Header, string Name, List<string> Parameters, List<int> Body) function, int line, SelfCall call, StringComparer names, RegexOptions options)
    {
        for (var listAt = 0; listAt < function.Parameters.Count; listAt++)
        {
            var list = function.Parameters[listAt];
            var part = Unwrapped(call.Arguments[listAt].Text);
            if (names.Equals(part, list) || !UsedAsList(masked, function.Body, list, options)) continue;

            for (var placeAt = 0; placeAt < function.Parameters.Count; placeAt++)
            {
                if (placeAt == listAt) continue;
                var place = function.Parameters[placeAt];
                if (!names.Equals(Unwrapped(call.Arguments[placeAt].Text), place)) continue;

                // The part is the items above something: written so in the argument itself - pack(items, items > pivot) - or on
                // a line making or filling it, or in the test that line is under.
                if (!IsAbovePart(masked, function.Body, part, options)) continue;

                // In a branch taken when the place is at or past a count: k >= num_lessoreq, k > less_or_equal.
                if (BranchTest(masked, line, dialect) is not { } branch) continue;
                if (PastCount(branch, place, options) is not { } count) continue;

                var told = $"Line {line + 1} looks in `{part}`, the part above the pivot, for the item at place `{place}` - but `{place}` counts from the " +
                           $"start of the whole list, and the `{count}` items up to the pivot come before `{part}`. Within `{part}` the place is " +
                           $"`{place} - {count}`; as written it looks for a later item than the one wanted, and can look past the end of `{part}`.";
                return new LogicFinding(id, line + 1, told, TakeAway(id, source, line, call.Arguments[placeAt], place, count))
                {
                    Severity = Severity.Error,
                    Confidence = Confidence.Likely,
                };
            }
        }

        return null;
    }

    /// <summary>An argument without brackets around all of it, and without Perl's \ in front: (k) is k, \@above is @above.</summary>
    private static string Unwrapped(string argument)
    {
        var text = argument.Trim();
        while (text.StartsWith('(') && Brackets.ClosingParenthesis(text, 0) == text.Length - 1) text = text[1..^1].Trim();
        return text.TrimStart('\\', '&').Trim();
    }

    /// <summary>
    /// Whether the part is the items above something: the argument is written with &gt; in it - pack(items, items &gt; pivot) - or a
    /// line of the function naming the part has &gt; in it, or the test just above that line does: above.add(x) under
    /// } else if (x &gt; pivot) {.
    /// </summary>
    private static bool IsAbovePart(IReadOnlyList<string> masked, List<int> body, string part, RegexOptions options)
    {
        if (!Regex.IsMatch(part, @"^[$@]?[A-Za-z_]\w*$")) return Above().IsMatch(part);
        var named = new Regex($@"(?<![\w$@])[$@]*{Regex.Escape(part.TrimStart('@', '$'))}(?!\w)", options);
        foreach (var line in body)
        {
            if (!named.IsMatch(masked[line])) continue;
            if (Above().IsMatch(masked[line])) return true;
            var above = line - 1;
            while (above >= 0 && masked[above].Trim().Length == 0) above--;
            if (above >= 0 && TestWord().IsMatch(masked[above]) && Above().IsMatch(masked[above]) &&
                CodeText.Indentation(masked[above]).Length < CodeText.Indentation(masked[line]).Length)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the parameter is used as a list in the function: indexed, its length or size read, its first item taken -
    /// arr[0], len(arr), arr.size(), arr.length, #items, count($items), @$items, items.head, items(1) - not a number such as a
    /// binary search's start.
    /// </summary>
    private static bool UsedAsList(IReadOnlyList<string> masked, List<int> body, string list, RegexOptions options)
    {
        var name = Regex.Escape(list.TrimStart('$', '@'));
        var asList = new Regex($@"(?<![\w$@])[$@]?{name}\s*(?:->\s*)?\[|\b(?:len|size|count|length)\s*\(\s*[$@]?{name}\b|(?<![\w$@])[$@]?{name}\s*\.\s*(?:size|length|Length|Count|count|get|head|isEmpty|iter|filter|where|select|Where|len)\b|#\s*{name}\b|@\s*\$?{name}\b|\$\s*{name}\s*\.\s*Count\b|(?<![\w$@]){name}\s*\(\s*1\s*\)", options);
        return body.Any(line => asList.IsMatch(masked[line]));
    }

    /// <summary>The condition of the test the line is under - on the line itself, or the nearest less indented line above that tests.</summary>
    private static string? BranchTest(IReadOnlyList<string> masked, int line, Dialect dialect)
    {
        if (TestWord().Match(masked[line]) is { Success: true } own && Reaching.Written(masked[line], own.Index + own.Length, dialect.ColonEnds) is { } written)
            return written.Condition;

        var indent = CodeText.Indentation(masked[line]).Length;
        for (var above = line - 1; above >= 0 && indent > 0; above--)
        {
            if (masked[above].Trim().Length == 0 || masked[above].Trim() == "{") continue;
            var width = CodeText.Indentation(masked[above]).Length;
            if (width >= indent) continue;
            if (TestWord().Match(masked[above]) is { Success: true } test && Reaching.Written(masked[above], test.Index + test.Length, dialect.ColonEnds) is { } wording)
                return wording.Condition;
            return null;
        }

        return null;
    }

    /// <summary>The count the place is asked to be at or past - k &gt;= C, k &gt; C, C &lt;= k, C &lt; k, $k -ge C, k .gt. C - when C is not a number alone.</summary>
    private static string? PastCount(string condition, string place, RegexOptions options)
    {
        var name = Regex.Escape(place);
        var asked = condition.Trim();
        while (asked.StartsWith('(') && Brackets.ClosingParenthesis(asked, 0) == asked.Length - 1) asked = asked[1..^1].Trim();
        var found = Regex.Match(asked, $@"^{name}\s*(?:>=?|-g[te]\b|\.g[te]\.)\s*(?<count>.+)$", options);
        if (!found.Success) found = Regex.Match(asked, $@"^(?<count>.+?)\s*(?:<=?|-l[te]\b|\.l[te]\.)\s*{name}$", options);
        if (!found.Success) return null;
        var count = found.Groups["count"].Value.Trim();
        return Regex.IsMatch(count, @"^\d+$") || Regex.IsMatch(count, @"&&|\|\||\band\b|\bor\b|-and\b|-or\b", RegexOptions.IgnoreCase) ? null : count;
    }

    /// <summary>The change: the items up to the pivot taken away from the place - kth(above, k - num_lessoreq).</summary>
    private static LocalFix? TakeAway(string id, SourceFile source, int line, (string Text, int Start) argument, string place, string count)
    {
        var text = source.Lines[line];
        var written = argument.Text.Trim();
        var at = argument.Start + argument.Text.IndexOf(written, StringComparison.Ordinal);
        if (at < 0 || at + written.Length > text.Length) return null;
        var inner = Unwrapped(written);
        var innerAt = at + written.IndexOf(inner, StringComparison.Ordinal);
        var counted = Regex.IsMatch(count, @"^[$@]?[A-Za-z_][\w.]*(?:\(\))?$") ? count : $"({count})";
        var takenAway = $"{inner} - {counted}";
        var changed = text[..innerAt] + takenAway + text[(innerAt + inner.Length)..];
        return LocalFix.ReplaceLine(id, $"Take away what comes before the part: {takenAway}",
            $"Everything up to the pivot - `{count}` items - comes before the part above it, so the place within that part is `{takenAway}`.",
            source.Path, line + 1, changed);
    }

    /// <summary>A call the function makes of itself: each argument with where on the line it starts.</summary>
    private sealed record SelfCall(List<(string Text, int Start)> Arguments);

    private static IEnumerable<SelfCall> SelfCalls(string text, Regex callsItself, Dialect dialect)
    {
        foreach (Match call in callsItself.Matches(text))
        {
            var start = call.Index + call.Length;
            if (call.Groups["open"].Value.Trim() == "(")
            {
                if (Brackets.ClosingParenthesis(text, start - 1) is not { } close) continue;
                yield return new SelfCall(Split(text, start, close));
            }
            else if (dialect.Commands)
            {
                yield return new SelfCall(CommandArguments(text, start));
            }
        }
    }

    /// <summary>The arguments between a call's brackets, split at its commas at the top level, each with where it starts.</summary>
    private static List<(string Text, int Start)> Split(string text, int start, int end)
    {
        var arguments = new List<(string, int)>();
        var depth = 0;
        var from = start;
        for (var at = start; at < end; at++)
        {
            if (text[at] is '(' or '[' or '{') depth++;
            else if (text[at] is ')' or ']' or '}') depth--;
            else if (text[at] == ',' && depth == 0)
            {
                arguments.Add((text[from..at], from));
                from = at + 1;
            }
        }

        if (text[from..end].Trim().Length > 0 || arguments.Count > 0) arguments.Add((text[from..end], from));
        return arguments;
    }

    /// <summary>A command's arguments, one after another: each a word or a bracketed piece, up to a ) it did not open, a |, a ; or the end.</summary>
    private static List<(string Text, int Start)> CommandArguments(string text, int from)
    {
        var arguments = new List<(string, int)>();
        var at = from;
        while (at < text.Length)
        {
            while (at < text.Length && text[at] == ' ') at++;
            if (at >= text.Length || text[at] is ')' or '|' or ';' or '}') break;
            var start = at;
            var depth = 0;
            while (at < text.Length && (depth > 0 || text[at] != ' ' && text[at] is not (')' or '|' or ';' or '}')))
            {
                if (text[at] is '(' or '[' or '{') depth++;
                else if (text[at] is ')' or ']' or '}') depth--;
                at++;
            }

            arguments.Add((text[start..at], start));
        }

        return arguments;
    }
}
