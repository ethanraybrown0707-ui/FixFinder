using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// What a method that changes a value in place gives back, kept as if it were the changed value - each run with the
/// language itself, and neither php -l nor ruby -w said anything of either. PHP's sort($marks) puts $marks itself in order
/// and gives back true, so $sorted = sort($marks) printed true - and so did rsort, usort, uasort, uksort, asort, arsort, ksort,
/// krsort, shuffle, natsort and natcasesort with PHP 8.4. Ruby's ! methods change the value itself and give back nil when
/// there is nothing to change, so fixed = name.strip! was nil for "Ada" - and so were uniq!, compact!, flatten!, reject!,
/// select!, filter!, lstrip!, rstrip!, chomp!, squeeze!, sub!, gsub!, capitalize!, downcase!, upcase!, swapcase!, tr! and
/// delete! with Ruby 3.3, each given a value it had nothing to change in. And PHP's array_push gives back the count, so
/// $marks = array_push($marks, 90) made $marks int(3). ResultKeptLiveTests runs each mistake, and its change.
/// </summary>
public static partial class ResultKeptPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-php-sort-result-kept", CodePattern.Files(".php"), Syntax.CLike, PhpSortResultKept, Severity.Error, Confidence.Certain),
        new CodePattern("logic-ruby-bang-result-kept", CodePattern.Files(".rb"), Syntax.Python, RubyBangResultKept, Severity.Error),
        new CodePattern("logic-php-push-result-kept", CodePattern.Files(".php"), Syntax.CLike, PhpPushResultKept, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>$marks = array_push($marks, 90);</c> - the array given what array_push gives back.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)\$(?<name>\w+)\s*=\s*(?<call>(?:array_push|array_unshift)\s*\(\s*\$(?<array>\w+)\s*,.*\))\s*;\s*$")]
    private static partial Regex PhpPushKept();

    /// <summary>
    /// array_push and array_unshift add to the array itself and give back how many items it now has, so giving the array what
    /// they give back replaces its items with a number - $marks was int(3). The change only adds; a different name given the
    /// count is left alone, as the count is what array_push is for there.
    /// </summary>
    private static IEnumerable<LogicFinding> PhpPushResultKept(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PhpPushKept().Match(masked[index]) is not { Success: true } kept || kept.Groups["name"].Value != kept.Groups["array"].Value) continue;

            var array = kept.Groups["array"].Value;
            var call = source.Lines[index].Substring(kept.Groups["call"].Index, kept.Groups["call"].Length);
            var function = call[..call.IndexOf('(')].Trim();

            yield return new LogicFinding(id, index + 1,
                $"{function} adds to ${array} itself and gives back how many items it now has - so ${array} is given that count, a number, " +
                "and its items are lost",
                LocalFix.ReplaceLine(id, $"Only add: {call};",
                    $"{function} changes ${array} itself, so the line needs nothing more.",
                    source.Path, index + 1, $"{kept.Groups["indent"].Value}{call};"));
        }
    }

    /// <summary>
    /// Whether a later line than <paramref name="after"/> reads <paramref name="name"/> - in the lines as written, so a name
    /// put into text, as in "$sorted" or "#{fixed}", counts.
    /// </summary>
    private static bool ReadAfter(SourceFile source, int after, string name) =>
        source.Lines.Skip(after + 1).Any(line => Regex.IsMatch(line, $@"(?<![\w.$@]){Regex.Escape(name)}(?![\w?!])"));

    /// <summary><c>$sorted = sort($marks);</c> - what one of PHP's sorting functions gives back, given to a name.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)\$(?<name>\w+)\s*=\s*(?<call>(?<function>sort|rsort|usort|uasort|uksort|asort|arsort|ksort|krsort|shuffle|natsort|natcasesort)\s*\(\s*\$(?<array>\w+)\s*(?:,.*)?\))\s*;\s*$")]
    private static partial Regex PhpSortKept();

    /// <summary>
    /// PHP's sorting functions put the array itself in order and give back true, so the name is given true, not the array.
    /// The change sorts the array on a line of its own, then gives the name the sorted array - or, where the name is the array's
    /// own, only sorts it.
    /// </summary>
    private static IEnumerable<LogicFinding> PhpSortResultKept(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PhpSortKept().Match(masked[index]) is not { Success: true } kept) continue;

            var name = kept.Groups["name"].Value;
            var array = kept.Groups["array"].Value;
            var function = kept.Groups["function"].Value;
            var indent = kept.Groups["indent"].Value;
            var call = source.Lines[index].Substring(kept.Groups["call"].Index, kept.Groups["call"].Length);
            var sameArray = name == array;
            if (!sameArray && !ReadAfter(source, index, "$" + name)) continue;

            var sortItself = LocalFix.ReplaceLine(id,
                sameArray ? $"Sort it on a line of its own: {call};" : $"Sort ${array}, then keep it: {call}; ${name} = ${array};",
                sameArray
                    ? $"{function} puts ${array} itself in order, so the line needs nothing more - giving ${array} what {function} gives back makes it true."
                    : $"{function} puts ${array} itself in order, so once it has, ${array} is the sorted array to give ${name}.",
                source.Path, index + 1, $"{indent}{call};");

            yield return new LogicFinding(id, index + 1,
                $"{function} puts ${array} itself in {(function == "shuffle" ? "a random order" : "order")} and gives back true - not the " +
                $"array - so ${name} is true{(sameArray ? ", and the sorted array is lost" : "")}",
                sameArray ? sortItself : sortItself with { NewLines = [$"{indent}{call};", $"{indent}${name} = ${array};"] });
        }
    }

    /// <summary>When each ! method has nothing to change, and gives back nil: the value it is called on already...</summary>
    private static readonly IReadOnlyDictionary<string, string> NothingToChange = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["uniq"] = "has no item twice", ["compact"] = "holds no nil", ["flatten"] = "holds no list inside it",
        ["reject"] = "has no item the block rejects", ["select"] = "has only items the block keeps", ["filter"] = "has only items the block keeps",
        ["strip"] = "has no space at either end", ["lstrip"] = "has no space at the start", ["rstrip"] = "has no space at the end",
        ["chomp"] = "does not end with a new line", ["squeeze"] = "has no character twice in a row",
        ["sub"] = "holds nothing to replace", ["gsub"] = "holds nothing to replace", ["tr"] = "holds none of the characters to change",
        ["delete"] = "holds none of the characters to delete", ["capitalize"] = "is capitalized", ["downcase"] = "has no capital letter",
        ["upcase"] = "has no small letter", ["swapcase"] = "has no letters",
    };

    /// <summary>
    /// <c>fixed = name.strip!</c> - what a ! method gives back, given to a name: the value it is called on is a name, or a call
    /// such as gets, and whatever follows the ! is the method's own arguments or block.
    /// </summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[a-z_]\w*)\s*=\s*(?<receiver>@?[a-z_]\w*(?:\.[a-z_]\w*[?!]?)*?)\.(?<method>uniq|compact|flatten|reject|select|filter|strip|lstrip|rstrip|chomp|squeeze|sub|gsub|tr|delete|capitalize|downcase|upcase|swapcase)(?<bang>!)(?![\w=?!])(?<rest>.*)$")]
    private static partial Regex RubyBangKept();

    /// <summary>What may follow the !: nothing, the method's arguments in brackets, and a block on the same line - with no further method called.</summary>
    [GeneratedRegex(@"^\s*(?:\([^()]*\))?\s*(?:\{[^{}]*\})?\s*(?:#.*)?$")]
    private static partial Regex ArgumentsAndBlockOnly();

    /// <summary>
    /// Whether what a ! method is called on is a variable - an @ name, or a name given a value, or named as a block's or a
    /// method's parameter, before the line - rather than a call such as gets, which a later line would call again.
    /// </summary>
    private static bool IsVariable(IReadOnlyList<string> masked, int before, string receiver)
    {
        if (receiver.StartsWith('@')) return true;
        if (!Regex.IsMatch(receiver, @"^[a-z_]\w*$")) return false;

        var named = Regex.Escape(receiver);
        var givenOrNamed = new Regex($@"^\s*{named}\s*(?:[-+*/]|\|\||&&)?=(?![=~>])|\|[^|]*(?<![\w.]){named}\b[^|]*\||\bdef\s+[\w.]+[?!]?\s*\(?[^)#]*(?<![\w.]){named}\b");
        return masked.Take(before).Any(line => givenOrNamed.IsMatch(line));
    }

    /// <summary>
    /// What a ! method gives back, given to a name read later: the method changes the value itself and gives back nil when it
    /// has nothing to change - so the name is nil for any value already as the method would leave it. Where the value is not
    /// read again, the change calls the method without its !, which gives back a changed copy; where it is, the change calls
    /// the ! method on a line of its own, then gives the name the value it changed.
    /// </summary>
    private static IEnumerable<LogicFinding> RubyBangResultKept(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RubyBangKept().Match(masked[index]) is not { Success: true } kept || !ArgumentsAndBlockOnly().IsMatch(kept.Groups["rest"].Value)) continue;

            var name = kept.Groups["name"].Value;
            var receiver = kept.Groups["receiver"].Value;
            var method = kept.Groups["method"].Value;
            if (name == receiver || !ReadAfter(source, index, name)) continue;

            var line = source.Lines[index];
            var bang = kept.Groups["bang"];
            var variable = IsVariable(masked, index, receiver);
            var value = variable ? receiver : $"what {receiver} gives back";
            var message = $"{method}! changes {value} itself and gives back nil when there is nothing to change - so {name} is nil " +
                          $"whenever {value} {NothingToChange[method]}";

            if (!variable || !ReadAfter(source, index, receiver))
            {
                yield return new LogicFinding(id, index + 1, message,
                    LocalFix.ReplaceLine(id, $"Keep a changed copy: {receiver}.{method}, without the !",
                        $"{method} without the ! gives back a changed copy every time - even when there was nothing to change.",
                        source.Path, index + 1, line[..bang.Index] + line[(bang.Index + bang.Length)..]));
                continue;
            }

            var indent = kept.Groups["indent"].Value;
            var callFrom = kept.Groups["receiver"].Index;
            var changeItself = LocalFix.ReplaceLine(id, $"Change {receiver}, then keep it: {receiver}.{method}! on its own line, then {name} = {receiver}",
                $"{method}! changes {receiver} itself, so once it has, {receiver} holds the changed value - whether or not there was " +
                $"anything to change - and {name} is given that.",
                source.Path, index + 1, indent + line[callFrom..].TrimEnd());

            yield return new LogicFinding(id, index + 1, message,
                changeItself with { NewLines = [indent + line[callFrom..].TrimEnd(), $"{indent}{name} = {receiver}"] });
        }
    }
}
