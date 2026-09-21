using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>for n in numbers.sort():</c> - sort changes the list and returns nothing; sorted returns a new one.</summary>
public sealed partial class PythonSortedNotSort : ILocalFixRule
{
    public string Id => "python-sorted-not-sort";

    [GeneratedRegex(@"\.(?<method>sort|reverse)\(")]
    private static partial Regex InPlace();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "'NoneType' object is not iterable") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        var hits = InPlace().Matches(masked);
        if (hits.Count != 1) return null;

        var dot = hits[0].Index;
        var open = dot + hits[0].Length - 1;
        var close = Brackets.Closing(masked, open);
        var start = PythonCode.ReceiverStart(masked, dot);
        if (close < 0 || start < 0) return null;

        var receiver = line[start..dot];
        var arguments = line[(open + 1)..close].Trim();
        var method = hits[0].Groups["method"].Value;

        if (method == "reverse" && arguments.Length > 0) return null;

        var replacement = method == "sort"
            ? $"sorted({receiver}{(arguments.Length > 0 ? ", " + arguments : "")})"
            : $"reversed({receiver})";

        return LocalFix.ReplaceLine(
            Id, $"Use {(method == "sort" ? "sorted" : "reversed")}({receiver})",
            $"`{receiver}.{method}()` changes the list where it is and returns None, so there was nothing to loop over. " +
            $"`{(method == "sort" ? "sorted" : "reversed")}(...)` gives the result back instead.",
            source.Path, number, line[..start] + replacement + line[(close + 1)..]);
    }
}

/// <summary><c>numbers = numbers.append(3)</c>, found from the later line where numbers turned out to be None.</summary>
public sealed partial class PythonInPlaceResult : ILocalFixRule
{
    public string Id => "python-in-place-result";

    [GeneratedRegex(@"^(?:'NoneType' object has no attribute '\w+'|'NoneType' object is not (?:iterable|subscriptable))$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)(?=\s*[.\[])|\bin\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex Used();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "python" || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var names = Used().Matches(masked[number - 1]).Select(m => m.Groups["name"].Value).Distinct().ToList();
        var found = new List<(int Line, string Replacement)>();

        foreach (var name in names)
        {
            var word = Regex.Escape(name);

            for (var k = number - 2; k >= 0; k--)
            {
                if (Regex.Match(masked[k], $@"^(?<indent>\s*){word}\s*=(?!=)\s*(?<rhs>.+?)\s*$") is not { Success: true } assignment) continue;

                if (Regex.IsMatch(assignment.Groups["rhs"].Value, $@"^{word}\.(?:append|extend|insert|sort|reverse|remove|clear|update|add|discard)\(.*\)$"))
                {
                    var rhs = assignment.Groups["rhs"];
                    found.Add((k, assignment.Groups["indent"].Value + source.Lines[k][rhs.Index..(rhs.Index + rhs.Length)] + source.Lines[k][(rhs.Index + rhs.Length)..]));
                }

                break;
            }
        }

        if (found.Count != 1) return null;

        var (index, replacement) = found[0];

        return LocalFix.ReplaceLine(
            Id, "Call it without assigning the result",
            $"Line {index + 1} changes the list where it is - and methods that do that return None, so assigning the result " +
            "replaced the list with None. Calling the method on its own keeps the list.",
            source.Path, index + 1, replacement);
    }
}

/// <summary><c>dictionary changed size during iteration</c> - loop over a copy, and change the original freely.</summary>
public sealed partial class PythonChangedDuringIteration : ILocalFixRule
{
    public string Id => "python-changed-during-iteration";

    [GeneratedRegex(@"^(?:dictionary|set|deque) changed size during iteration$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<head>\s*for\s+.+?\s+in\s+)(?<expr>.+?)(?<colon>\s*:)$")]
    private static partial Regex ForIn();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "RuntimeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (ForIn().Match(code) is not { Success: true } match) return null;

        var expression = match.Groups["expr"].Value;
        if (expression.StartsWith("list(", StringComparison.Ordinal)) return null;

        return LocalFix.ReplaceLine(
            Id, $"Loop over a copy: list({expression})",
            "The loop removes or adds entries in the very collection it is walking through, which Python refuses to keep " +
            "track of. Looping over a copy lets the original change.",
            source.Path, number, $"{match.Groups["head"].Value}list({expression}){match.Groups["colon"].Value}{tail}");
    }
}

/// <summary><c>'set' object has no attribute 'append'</c> - a list's method on a set, which calls it <c>add</c>.</summary>
public sealed partial class PythonSetMethod : ILocalFixRule
{
    public string Id => "python-set-method";

    [GeneratedRegex(@"^'set' object has no attribute '(?<method>append|push|extend)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "AttributeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var method = message.Groups["method"].Value;
        var replacement = method == "extend" ? "update" : "add";

        if (Regex.Matches(CodeText.Mask(line, Syntax.Python), $@"\.{method}\s*\(").ToList() is not [var call]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use a set's own method: {replacement}",
            $"`{method}` is a list's method. A set has no order to add to the end of, so it calls it `{replacement}`.",
            source.Path, number, line[..call.Index] + $".{replacement}(" + line[(call.Index + call.Length)..]);
    }
}

/// <summary><c>deque.pop() takes no arguments (1 given)</c> - <c>pop(0)</c> from a list, where a deque has <c>popleft()</c>.</summary>
public sealed partial class PythonDequePopLeft : ILocalFixRule
{
    public string Id => "python-deque-popleft";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "deque.pop() takes no arguments (1 given)") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Regex.Matches(CodeText.Mask(line, Syntax.Python), @"\.pop\(\s*0\s*\)").ToList() is not [var pop]) return null;

        return LocalFix.ReplaceLine(
            Id, "Take from the front of a deque with popleft()",
            "`pop(0)` takes from the front of a list, but a `deque` takes no position: `pop()` takes from the back and `popleft()` from " +
            "the front. Taking from the front is what a queue in a breadth-first search needs, and it is what `deque` makes fast.",
            source.Path, number, line[..pop.Index] + ".popleft()" + line[(pop.Index + pop.Length)..]);
    }
}

/// <summary><c>sort() got an unexpected keyword argument 'cmp'</c> - Python 2's comparison function, which Python 3 takes as a
/// key.</summary>
public sealed partial class PythonSortCmp : ILocalFixRule
{
    public string Id => "python-sort-cmp";

    [GeneratedRegex(@"^(?:sort\(\) got an unexpected keyword argument 'cmp'|'cmp' is an invalid keyword argument for sort\(\)|sorted\(\) got an unexpected keyword argument 'cmp')$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bcmp\s*=\s*(?<function>[A-Za-z_][\w.]*)(?=\s*[,)])")]
    private static partial Regex Cmp();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Cmp().Matches(CodeText.Mask(line, Syntax.Python)).ToList() is not [var cmp]) return null;

        var function = cmp.Groups["function"].Value;

        return PythonCode.WithImport(
            Id, $"Sort with {function} as a key: cmp_to_key({function})",
            $"Python 3 removed `cmp=`, the comparison function Python 2 took. `functools.cmp_to_key` turns a comparison function like " +
            $"`{function}` into the `key=` that sorting takes now, so the order comes out the same.",
            source, number, "functools", "cmp_to_key",
            written => line[..cmp.Index] + $"key={written}({function})" + line[(cmp.Index + cmp.Length)..]);
    }
}

/// <summary><c>KeyError: 'pear'</c> read with square brackets.</summary>
public sealed partial class PythonMissingKeyGet : ILocalFixRule
{
    public string Id => "python-missing-key-get";

    [GeneratedRegex(@"(?<![\w.])(?<dictionary>[A-Za-z_][\w.]*)\[(?<key>[^\[\]]+)\](?!\s*(?:[-+*/%]|//)?=(?!=))")]
    private static partial Regex Subscript();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "KeyError") || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Regex.IsMatch(masked, @"^\s*del\b")) return null;

        var key = (context.Error.Message ?? "").Trim();
        var reads = Subscript().Matches(masked)
            .Where(m => line.Substring(m.Groups["key"].Index, m.Groups["key"].Length).Trim() == key || Subscript().Matches(masked).Count == 1)
            .ToList();
        if (reads is not [var read]) return null;

        var dictionary = read.Groups["dictionary"].Value;
        var keyText = line.Substring(read.Groups["key"].Index, read.Groups["key"].Length).Trim();

        return LocalFix.ReplaceLine(Id, $"Read it with get, which gives None for a missing key: {dictionary}.get({keyText})",
            $"`{dictionary}[{keyText}]` stops the program when the key is not there. `{dictionary}.get({keyText})` gives None instead - or a default " +
            $"of your choosing, `{dictionary}.get({keyText}, 0)`. If the key should always be there, the real fix is wherever it was meant to be added.",
            source.Path, number, line[..read.Index] + $"{dictionary}.get({keyText})" + line[(read.Index + read.Length)..]);
    }
}
