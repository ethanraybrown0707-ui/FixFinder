using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Whether a function gets as far as one of its own lines in a case it is given - each test on the way read with numbers
/// in place of what it tests. With lo and hi both 3, <c>if lo &gt; hi: return</c> is 3 &gt; 3, false, so a call after it is
/// reached; with hi one more than lo, <c>if hi - lo &lt;= 1: return</c> is 1 &lt;= 1, true, so it is not.
/// </summary>
/// <remarks>
/// Blocks are read by indentation, as code is laid out in every language here: a test's block is the lines further in than
/// it, and an else, an elif or a } else at its own indentation goes on from it. A test written on the line it guards -
/// <c>if (lo &gt; hi) return;</c>, <c>return arr if arr.empty?</c> - guards only what is on that line.
/// </remarks>
internal static partial class Reaching
{
    /// <summary>
    /// How a test stands to the line: the line is in its block; in an else, or an elif, after it; after it, past a block
    /// that leaves the function; or the test is written in a way not read here - a test inside a line, x if c else y.
    /// </summary>
    internal enum Role { Inside, InElse, Leaves, Unread }

    /// <summary>
    /// One test on the way to the line: its line, where its condition is on that line and what it says, whether it says
    /// unless - the line runs when the condition is false - how it stands to the line, and what its own block says.
    /// </summary>
    internal sealed record Test(int Line, int Start, string Condition, bool Unless, Role Role, string Then);

    [GeneratedRegex(@"(?<![\w$.@-])(?<word>else\s+if|elseif|elsif|elif|if|unless)\b")]
    private static partial Regex TestWord();

    [GeneratedRegex(@"(?<![\w$.@-])(?<word>else\s+if|elseif|elsif|elif|if|unless)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TestWordAnyCase();

    [GeneratedRegex(@"^\s*\}?\s*(?:else\s+if|elseif|elsif|elif|else)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GoesOn();

    [GeneratedRegex(@"^\s*(?:end\b|\}\s*$|\)\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex Closes();

    [GeneratedRegex(@"(?<![\w$.])(?:return|raise|throw|die|exit|panic!?|abort|error)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeavesTheFunction();

    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:for|foreach|while|until|loop|do|repeat)\b|\.(?:each|forEach|times|map)\b|\bForEach-Object\b", RegexOptions.IgnoreCase)]
    private static partial Regex Loop();

    /// <summary>What may come straight after a test's closing bracket when the bracket holds the whole test.</summary>
    [GeneratedRegex(@"^(?:$|\{|:|;|=>|then\b|do\b|(?!(?:and|or|andalso|orelse|div|mod)\b)[A-Za-z_$@])", RegexOptions.IgnoreCase)]
    private static partial Regex AfterAWholeTest();

    /// <summary>
    /// Every test from <paramref name="first"/> up to <paramref name="target"/> that decides whether the target line runs -
    /// in its own block, the block it is in, or a block before it that leaves - each with how it stands to it.
    /// </summary>
    /// <param name="skipped">Lines that are not the function's own: a function written inside it.</param>
    /// <param name="colonEnds">Whether a colon ends a test, as Python's does.</param>
    /// <param name="modifiers">Whether a test may follow what it guards on the line, as Ruby's and Perl's may.</param>
    internal static List<Test> Tests(IReadOnlyList<string> masked, int first, int target, ISet<int> skipped, bool colonEnds, bool modifiers, bool ignoreCase)
    {
        var tests = new List<Test>();
        for (var line = first; line <= target && line < masked.Count; line++)
        {
            if (skipped.Contains(line)) continue;
            var text = masked[line];
            foreach (Match word in (ignoreCase ? TestWordAnyCase() : TestWord()).Matches(text))
            {
                var unless = word.Groups["word"].Value.Equals("unless", StringComparison.OrdinalIgnoreCase);
                var guarded = text[..word.Index].Trim();

                if (guarded.Length > 0 && guarded != "}")
                {
                    // A test after what it guards - return arr if arr.empty? - or one inside a line, x if c else y, not read here.
                    if (modifiers && !Regex.IsMatch(guarded, @"(?:[=(,\[{+\-*/<>!&|?:]|\b(?:and|or|not|then|else|do|return))$"))
                    {
                        var after = word.Index + word.Length;
                        var start = after + (text[after..].Length - text[after..].TrimStart().Length);
                        var condition = text[start..].TrimEnd().TrimEnd(';').TrimEnd();
                        if (line == target) tests.Add(new Test(line, start, condition, unless, Role.Inside, guarded));
                        else if (LeavesTheFunction().IsMatch(guarded)) tests.Add(new Test(line, start, condition, unless, Role.Leaves, guarded));
                        continue;
                    }

                    tests.Add(new Test(line, word.Index, text[word.Index..].Trim(), unless, Role.Unread, ""));
                    continue;
                }

                if (Written(text, word.Index + word.Length, colonEnds) is not { } wording)
                {
                    tests.Add(new Test(line, word.Index, text[word.Index..].Trim(), unless, Role.Unread, ""));
                    continue;
                }

                if (Stands(masked, line, wording, target, skipped) is { } role)
                    tests.Add(new Test(line, wording.Start, wording.Condition, unless, role.Role, role.Then));
            }
        }

        return tests;
    }

    /// <summary>A test's condition, where it starts, and what follows it on the line.</summary>
    internal sealed record Wording(int Start, string Condition, string Rest, int RestStart);

    internal static Wording? Written(string text, int from, bool colonEnds)
    {
        var at = from;
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        if (at >= text.Length) return null;

        if (text[at] == '(' && Brackets.ClosingParenthesis(text, at) is { } close)
        {
            var after = text[(close + 1)..].TrimStart();
            if (AfterAWholeTest().IsMatch(after))
                return new Wording(at + 1, text[(at + 1)..close].Trim(), Opening(after), close + 1 + (text[(close + 1)..].Length - after.Length));
        }

        // Up to what ends a test written without brackets around it: Python's colon, then, do, or the { of a block.
        var depth = 0;
        for (var end = at; end < text.Length; end++)
        {
            var c = text[end];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (depth == 0 && (c == '{' || colonEnds && c == ':' ||
                                    Regex.IsMatch(text[end..], @"^(?:then|do)\b") && end > at && char.IsWhiteSpace(text[end - 1]) ||
                                    c == ',' && Regex.IsMatch(text[end..], @"^,\s*do:")))
            {
                return new Wording(at, text[at..end].Trim(), Opening(text[end..]), end);
            }
        }

        return new Wording(at, text[at..].Trim().TrimEnd(';').TrimEnd(), "", text.Length);
    }

    /// <summary>What a block's first line holds after what opens it: { : then do.</summary>
    private static string Opening(string rest) =>
        Regex.Replace(rest, @"^\s*(?:\{|:|then\b|do:?|,\s*do:)\s*", "").Trim();

    /// <summary>How the test on <paramref name="line"/> stands to the target line, and what its own block holds - or null when it does not decide it.</summary>
    private static (Role Role, string Then)? Stands(IReadOnlyList<string> masked, int line, Wording wording, int target, ISet<int> skipped)
    {
        if (line == target)
        {
            // On its own line: the target is what the test guards, or what its else does.
            return (Regex.IsMatch(wording.Rest, @"\belse\b") ? Role.InElse : Role.Inside, wording.Rest);
        }

        var indent = CodeText.Indentation(masked[line]).Length;
        var inline = wording.Rest.Length > 0;
        var then = inline ? [wording.Rest] : new List<string>();
        var at = line + 1;

        if (inline && Regex.IsMatch(wording.Rest, @"\belse\b"))
        {
            // if v.is_empty() { v } else { - the lines further in are the else's.
            var (elseLines, afterElse) = Block(masked, at, indent);
            if (elseLines.Contains(target)) return (Role.InElse, wording.Rest);
            at = afterElse;
        }
        else if (!inline)
        {
            var (thenLines, afterThen) = Block(masked, at, indent);
            if (thenLines.Contains(target)) return (Role.Inside, string.Join("\n", then));
            then.AddRange(thenLines.Where(each => !skipped.Contains(each)).Select(each => masked[each].Trim()));
            at = afterThen;
        }

        // The else and elif that go on from it, at its own indentation.
        while (at < masked.Count && at <= target)
        {
            if (masked[at].Trim().Length == 0) { at++; continue; }
            if (CodeText.Indentation(masked[at]).Length != indent) break;
            if (GoesOn().IsMatch(masked[at]))
            {
                if (at == target) return (Role.InElse, string.Join("\n", then));
                var (branchLines, afterBranch) = Block(masked, at + 1, indent);
                if (branchLines.Contains(target)) return (Role.InElse, string.Join("\n", then));
                at = afterBranch;
                continue;
            }

            if (Closes().IsMatch(masked[at]) && masked[at].Trim() is "}" or "end" or "end if" or "endif" or ")")
            {
                at++;
                continue;
            }

            break;
        }

        return LeavesTheFunction().IsMatch(string.Join("\n", then)) ? (Role.Leaves, string.Join("\n", then)) : null;
    }

    /// <summary>
    /// The lines of the block below a line indented <paramref name="indent"/>: the lines further in - after a { on a line of its
    /// own at that indentation, as C# writes it, up to the } that closes it - and the line after the block.
    /// </summary>
    private static (List<int> Lines, int After) Block(IReadOnlyList<string> masked, int from, int indent)
    {
        var lines = new List<int>();
        var at = from;
        while (at < masked.Count && masked[at].Trim().Length == 0) at++;
        var braced = at < masked.Count && masked[at].Trim() == "{" && CodeText.Indentation(masked[at]).Length == indent;
        if (braced) at++;

        for (; at < masked.Count; at++)
        {
            if (masked[at].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[at]).Length <= indent) break;
            lines.Add(at);
        }

        if (braced && at < masked.Count && masked[at].Trim() == "}" && CodeText.Indentation(masked[at]).Length == indent) at++;
        return (lines, at);
    }

    /// <summary>Whether the line is in a loop of the function's - for, while, each - by the lines heading the blocks it is in.</summary>
    internal static bool InALoop(IReadOnlyList<string> masked, int first, int line)
    {
        var indent = CodeText.Indentation(masked[line]).Length;
        for (var above = line - 1; above >= first && indent > 0; above--)
        {
            if (masked[above].Trim().Length == 0) continue;
            var width = CodeText.Indentation(masked[above]).Length;
            if (width >= indent) continue;
            indent = width;

            // A { on a line of its own belongs to the line above it, as C# writes it.
            var header = masked[above].Trim() == "{" && above > first ? masked[above - 1] : masked[above];
            if (Loop().IsMatch(header)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a condition holds once what it tests has been put as numbers, true or false - 1 - 0 &lt;= 1, not true, 3 &gt;= 3 -
    /// or null when anything else is left in it.
    /// </summary>
    /// <param name="equalsCompares">Whether a single = compares, as F#'s, OCaml's and Fortran's tests may.</param>
    internal static bool? Holds(string condition, bool equalsCompares = false)
    {
        var tokens = new List<string>();
        var at = 0;
        while (at < condition.Length)
        {
            if (char.IsWhiteSpace(condition[at])) { at++; continue; }
            var token = Token().Match(condition, at);
            if (!token.Success || token.Index != at) return null;
            tokens.Add(token.Value.ToLowerInvariant());
            at += token.Length;
        }

        var reader = new Reader(tokens, equalsCompares);
        var value = reader.Or();
        return value is not null && reader.Done ? Truth(value) : null;
    }

    [GeneratedRegex(@"\d+|\.(?:and|or|not|eq|ne|lt|le|gt|ge|true|false)\.|-(?:and|or|not|eq|ne|lt|le|gt|ge)\b|\$?(?:true|false)\b|(?:andalso|orelse|and|or|not)\b|===|!==|==|!=|<>|~=|=/=|/=|=:=|<=|>=|=<|&&|\|\||[<>!+\-*=()]", RegexOptions.IgnoreCase)]
    private static partial Regex Token();

    private static bool? Truth(object? value) => value switch { bool truth => truth, long number => number != 0, _ => null };

    /// <summary>Reads the tokens of a condition: or, and, not, a comparison, then sums of numbers.</summary>
    private sealed class Reader(List<string> tokens, bool equalsCompares)
    {
        private int at;

        public bool Done => at == tokens.Count;

        private string? Next => at < tokens.Count ? tokens[at] : null;

        public object? Or()
        {
            var left = And();
            while (left is not null && Next is "or" or "||" or "-or" or ".or." or "orelse")
            {
                at++;
                var right = And();
                if (Truth(left) is not { } a || Truth(right) is not { } b) return null;
                left = a || b;
            }

            return left;
        }

        private object? And()
        {
            var left = Not();
            while (left is not null && Next is "and" or "&&" or "-and" or ".and." or "andalso")
            {
                at++;
                var right = Not();
                if (Truth(left) is not { } a || Truth(right) is not { } b) return null;
                left = a && b;
            }

            return left;
        }

        private object? Not()
        {
            if (Next is "not" or "!" or "-not" or ".not.")
            {
                at++;
                return Truth(Not()) is { } value ? !value : null;
            }

            return Comparison();
        }

        private object? Comparison()
        {
            var left = Sum();
            if (left is null || Next is not { } op) return left;

            var compares = op is "==" or "===" or "!=" or "!==" or "<>" or "~=" or "/=" or "=/=" or "=:=" or "<" or "<=" or "=<" or ">" or ">=" ||
                           op is "-eq" or "-ne" or "-lt" or "-le" or "-gt" or "-ge" or ".eq." or ".ne." or ".lt." or ".le." or ".gt." or ".ge." ||
                           op == "=" && equalsCompares;
            if (!compares) return left;

            at++;
            var right = Sum();
            if (right is null) return null;

            if (left is bool || right is bool)
            {
                if (Truth(left) is not { } a || Truth(right) is not { } b) return null;
                return op switch
                {
                    "==" or "===" or "-eq" or ".eq." or "=:=" or "=" => a == b,
                    "!=" or "!==" or "<>" or "~=" or "/=" or "=/=" or "-ne" or ".ne." => a != b,
                    _ => null,
                };
            }

            var (x, y) = ((long)left, (long)right);
            return op switch
            {
                "==" or "===" or "-eq" or ".eq." or "=:=" or "=" => x == y,
                "!=" or "!==" or "<>" or "~=" or "/=" or "=/=" or "-ne" or ".ne." => x != y,
                "<" or "-lt" or ".lt." => x < y,
                "<=" or "=<" or "-le" or ".le." => x <= y,
                ">" or "-gt" or ".gt." => x > y,
                _ => x >= y,
            };
        }

        private object? Sum()
        {
            var left = Product();
            while (left is long && Next is "+" or "-")
            {
                var op = tokens[at++];
                if (Product() is not long right) return null;
                left = op == "+" ? (long)left + right : (long)left - right;
            }

            return left;
        }

        private object? Product()
        {
            var left = Single();
            while (left is long && Next is "*")
            {
                at++;
                if (Single() is not long right) return null;
                left = (long)left * right;
            }

            return left;
        }

        private object? Single()
        {
            switch (Next)
            {
                case null:
                    return null;
                case "-":
                    at++;
                    return Single() is long number ? -number : null;
                case "(":
                    at++;
                    var inside = Or();
                    if (Next != ")") return null;
                    at++;
                    return inside;
                case "true" or "$true" or ".true.":
                    at++;
                    return true;
                case "false" or "$false" or ".false.":
                    at++;
                    return false;
            }

            if (long.TryParse(Next, out var value))
            {
                at++;
                return value;
            }

            return null;
        }
    }
}
