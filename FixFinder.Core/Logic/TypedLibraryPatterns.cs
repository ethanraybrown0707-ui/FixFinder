using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Rust's, F#'s, Scala's and Dart's own library used in a way that builds with nothing said by rustc, F# Interactive, Scala
/// CLI or dart analyze, and runs wrong: let sorted = marks.sort() keeps what sort gives back, which is () - and so did
/// reverse, dedup and push; Array.create 3 row, and Scala's Array.fill(2)(row), fill the grid with one row three times, so
/// changing one cell changed every row; int of a char, and Scala's toInt of one, give its character code - "123" added up
/// to 150, not 6; name.trim() or name.toUpperCase() on a line of their own give back a changed copy that is lost - the name
/// printed as it was; and an async function's Future printed - Instance of 'Future&lt;int&gt;' - where its value was meant.
/// </summary>
public static partial class TypedLibraryPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-rust-unit-kept", CodePattern.Files(".rs"), Syntax.CLike, RustUnitKept, Severity.Error, Confidence.Certain),
        new CodePattern("logic-fsharp-rows-shared", CodePattern.Files(".fsx"), Syntax.FSharp, FSharpRowsShared, Severity.Error, Confidence.Certain),
        new CodePattern("logic-fsharp-char-as-number", CodePattern.Files(".fsx"), Syntax.FSharp, FSharpCharAsNumber, Severity.Error),
        new CodePattern("logic-scala-char-as-number", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaCharAsNumber, Severity.Error),
        new CodePattern("logic-scala-rows-shared", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaRowsShared, Severity.Error, Confidence.Certain),
        new CodePattern("logic-dart-result-discarded", CodePattern.Files(".dart"), Syntax.CLike, DartResultDiscarded, Severity.Error, Confidence.Certain),
        new CodePattern("logic-dart-future-used-as-value", CodePattern.Files(".dart"), Syntax.CLike, DartFutureUsedAsValue, Severity.Error),
    ];

    /// <summary><c>let sorted = marks.sort();</c> - a method that changes a Vec or a String in place, and gives back ().</summary>
    [GeneratedRegex(@"^(?<indent>\s*)let\s+(?:mut\s+)?(?<name>[a-z_]\w*)\s*=\s*(?<receiver>[a-z_][\w.]*)\s*\.\s*(?<method>sort|sort_unstable|sort_by|sort_by_key|sort_unstable_by|sort_unstable_by_key|reverse|dedup|dedup_by_key|push|push_str|clear|truncate|extend|retain|rotate_left|rotate_right)\s*\((?<arguments>.*)\)\s*;\s*$")]
    private static partial Regex UnitMethodKept();

    private static IEnumerable<LogicFinding> RustUnitKept(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (UnitMethodKept().Match(masked[index]) is not { Success: true } kept) continue;

            var line = source.Lines[index];
            var indent = kept.Groups["indent"].Value;
            var name = kept.Groups["name"].Value;
            var receiver = kept.Groups["receiver"].Value;
            var method = kept.Groups["method"].Value;
            var call = line.Substring(kept.Groups["receiver"].Index, line.LastIndexOf(')') + 1 - kept.Groups["receiver"].Index);

            yield return new LogicFinding(id, index + 1,
                $"{method} changes {receiver} itself and gives back () - nothing - so {name} is (): let sorted = marks.sort() printed ()",
                new LocalFix
                {
                    RuleId = id, Title = $"Change {receiver}, then copy it: {receiver}.{method}(...); let {name} = {receiver}.clone();",
                    Explanation = $"{receiver}.{method}(...) on a line of its own changes {receiver}, and clone gives {name} a copy of it as it now is.",
                    File = source.Path, StartLine = index + 1, RemoveCount = 1, NewLines = [$"{indent}{call};", $"{indent}let {name} = {receiver}.clone();"],
                });
        }
    }

    /// <summary><c>Array.create 3 (Array.zeroCreate 3)</c> or <c>Array.create 3 row</c> - one array given as every item.</summary>
    [GeneratedRegex(@"\bArray\.create\s+(?<count>\w+)\s+(?<row>\((?<inner>Array\.(?:zeroCreate|create)\b[^()]*|\[\|[^()]*\|\])\)|(?<inner>\[\|[^\]]*\|\])|(?<name>[a-z_]\w*))")]
    private static partial Regex ArrayCreateOfArray();

    private static IEnumerable<LogicFinding> FSharpRowsShared(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match created in ArrayCreateOfArray().Matches(masked[index]))
            {
                var name = created.Groups["name"].Value;
                if (created.Groups["name"].Success && !Regex.IsMatch(code, $@"\blet\s+(?:mutable\s+)?{Regex.Escape(name)}\s*=\s*(?:\[\||Array\.)")) continue;

                var line = source.Lines[index];
                var count = created.Groups["count"].Value;
                var inner = created.Groups["inner"].Success ? line.Substring(created.Groups["inner"].Index, created.Groups["inner"].Length) : $"Array.copy {name}";
                var changed = $"Array.init {count} (fun _ -> {inner})";

                yield return new LogicFinding(id, index + 1,
                    $"Array.create puts the one value it is given in every place - and an array is one array wherever it is put, so every row is " +
                    "the same row: setting grid.[0].[0] changed the first cell of all three",
                    LocalFix.ReplaceLine(id, $"Make each row its own: {changed}",
                        "Array.init runs the function once for each row, so each row is a new array.",
                        source.Path, index + 1, line[..created.Index] + changed + line[(created.Index + created.Length)..]));
            }
        }
    }

    /// <summary>The names that hold a char: a loop over text, a char read from text by position, or a char in quotes.</summary>
    private static HashSet<string> CharNames(string code, string loop, string fromText, string quoted) =>
        Regex.Matches(code, loop).Concat(Regex.Matches(code, fromText)).Concat(Regex.Matches(code, quoted))
            .Select(match => match.Groups["name"].Value).ToHashSet();

    private static IEnumerable<LogicFinding> FSharpCharAsNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        var chars = CharNames(code, @"\bfor\s+(?<name>[a-z_]\w*)\s+in\s+(?:""|[a-z_]\w*\s+do\b)", @"\blet\s+(?<name>[a-z_]\w*)\s*=\s*(?:[a-z_]\w*|""[^""]*"")\.\[[^\]]+\]", @"\blet\s+(?<name>[a-z_]\w*)\s*=\s*'.'");
        // for c in name do - a loop over a name only when that name holds text.
        chars.RemoveWhere(name => Regex.Match(code, $@"\bfor\s+{Regex.Escape(name)}\s+in\s+(?<over>[a-z_]\w*)\s+do\b") is { Success: true } over &&
                                  !Regex.IsMatch(code, $@"\blet\s+{Regex.Escape(over.Groups["over"].Value)}\s*=\s*"""));

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match converted in Regex.Matches(masked[index], @"(?<![\w.])int\s+(?<value>(?<name>[a-z_]\w*)|'.')(?![\w.])"))
            {
                var name = converted.Groups["name"].Value;
                var quoted = !converted.Groups["name"].Success;
                if (!quoted && !chars.Contains(name)) continue;
                // The digit's value worked out already: int c - int '0', the code of '0' taken away.
                if (Regex.IsMatch(masked[index][(converted.Index + converted.Length)..], @"^\s*-\s*int\s+'.'") ||
                    Regex.IsMatch(masked[index][..converted.Index], @"-\s*$")) continue;

                var line = source.Lines[index];
                var value = line.Substring(converted.Groups["value"].Index, converted.Groups["value"].Length);
                // Already in brackets of its own - (int c) - the brackets are kept as the sum's.
                var bracketed = converted.Index > 0 && masked[index][converted.Index - 1] == '(' &&
                                converted.Index + converted.Length < masked[index].Length && masked[index][converted.Index + converted.Length] == ')';
                var start = bracketed ? converted.Index - 1 : converted.Index;
                var end = bracketed ? converted.Index + converted.Length + 1 : converted.Index + converted.Length;
                var changed = $"(int {value} - int '0')";

                yield return new LogicFinding(id, index + 1,
                    $"int of a char gives its character code, not the digit it shows - int '7' is 55 - so adding up the digits of \"123\" this way gave " +
                    "150, not 6",
                    LocalFix.ReplaceLine(id, $"Take the digit's value: {changed}",
                        "The digits' codes are in order, '0' to '9', so a digit's code less the code of '0' is its value - '7' gives 7.",
                        source.Path, index + 1, line[..start] + changed + line[end..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> ScalaCharAsNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        var chars = CharNames(code, @"\bfor\s*\(?\s*(?<name>[a-z_]\w*)\s*<-\s*""", @"(?m)\b(?:val|var)\s+(?<name>[a-z_]\w*)\s*=\s*(?:[a-z_]\w*|""[^""]*"")(?:\(\s*[^()]+\)|\.charAt\([^()]+\)|\.head|\.last)\s*$", @"\b(?:val|var)\s+(?<name>[a-z_]\w*)\s*=\s*'.'");

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match converted in Regex.Matches(masked[index], @"(?<value>(?<![\w.])(?<name>[a-z_]\w*)|'.')\s*\.\s*(?<method>toInt)\b(?!\s*\()"))
            {
                var name = converted.Groups["name"].Value;
                if (converted.Groups["name"].Success && !chars.Contains(name)) continue;
                // The digit's value worked out already: c.toInt - '0', or - '0'.toInt, the code of '0' taken away.
                if (Regex.IsMatch(masked[index][(converted.Index + converted.Length)..], @"^\s*-\s*'.'") ||
                    Regex.IsMatch(masked[index][..converted.Index], @"-\s*$")) continue;

                var line = source.Lines[index];
                var method = converted.Groups["method"];

                yield return new LogicFinding(id, index + 1,
                    $"toInt of a Char gives its character code, not the digit it shows - '7'.toInt is 55 - so adding up the digits of \"123\" this way " +
                    "gave 150, not 6",
                    LocalFix.ReplaceLine(id, "Take the digit's value: .asDigit",
                        "asDigit gives the value of the digit a Char shows - '7'.asDigit is 7.",
                        source.Path, index + 1, line[..method.Index] + "asDigit" + line[(method.Index + method.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"\bArray\.fill\s*\((?<count>[^()]+)\)\s*\(\s*(?<row>[a-z_]\w*)\s*\)")]
    private static partial Regex ArrayFillOfName();

    private static IEnumerable<LogicFinding> ScalaRowsShared(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match filled in ArrayFillOfName().Matches(masked[index]))
            {
                var row = filled.Groups["row"].Value;
                if (!Regex.IsMatch(code, $@"\b(?:val|var)\s+{Regex.Escape(row)}\s*(?::[^=]+)?=\s*(?:Array\b|new\s+Array\b|Array\.(?:fill|ofDim))")) continue;

                var line = source.Lines[index];
                var rowGroup = filled.Groups["row"];

                yield return new LogicFinding(id, index + 1,
                    $"Array.fill works out {row} once for each place, and {row} is one array, so each place holds that same array - every row is " +
                    "one row: setting grid(0)(0) changed the first cell of both",
                    LocalFix.ReplaceLine(id, $"Make each row its own copy: {row}.clone()",
                        $"clone makes a new array with {row}'s items, and Array.fill makes one for each place.",
                        source.Path, index + 1, line[..rowGroup.Index] + $"{row}.clone()" + line[(rowGroup.Index + rowGroup.Length)..]));
            }
        }
    }

    /// <summary><c>name.trim();</c> - a String method that gives back a changed copy, called on a line of its own.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<receiver>[a-z_]\w*)\s*\.\s*(?<method>trim|trimLeft|trimRight|toUpperCase|toLowerCase|replaceAll|replaceFirst|substring|padLeft|padRight)\s*\((?<arguments>.*)\)\s*;\s*$")]
    private static partial Regex StringMethodAlone();

    private static IEnumerable<LogicFinding> DartResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            if (StringMethodAlone().Match(masked[index]) is not { Success: true } alone) continue;

            var receiver = alone.Groups["receiver"].Value;
            // Text: a String, or a var given text in quotes. final and const names cannot be given the copy, and get no change.
            var declared = Regex.Match(code, $@"\b(?<kind>var|String|final|const|final\s+String|const\s+String)\s+{Regex.Escape(receiver)}\s*=\s*(?<value>['""]|[a-z_]\w*)");
            if (!declared.Success || (declared.Groups["kind"].Value == "var" && declared.Groups["value"].Value is not ("'" or "\""))) continue;

            var line = source.Lines[index];
            var method = alone.Groups["method"].Value;
            var unchangeable = declared.Groups["kind"].Value.StartsWith("final", StringComparison.Ordinal) || declared.Groups["kind"].Value.StartsWith("const", StringComparison.Ordinal);
            var call = line[(alone.Groups["receiver"].Index)..(line.LastIndexOf(')') + 1)];

            yield return new LogicFinding(id, index + 1,
                $"Text in Dart never changes: {method} gives back a new, changed copy, and leaves {receiver} as it was - so on a line of its own the " +
                "copy is lost: the name printed as it was, and dart analyze said nothing",
                unchangeable ? null : LocalFix.ReplaceLine(id, $"Keep the copy: {receiver} = {receiver}.{method}(...);",
                    $"Giving the copy back to {receiver} makes {receiver} the changed text.",
                    source.Path, index + 1, $"{alone.Groups["indent"].Value}{receiver} = {call};"));
        }
    }

    [GeneratedRegex(@"\bFuture\s*<[^>]+>\s+(?<name>[a-z_]\w*)\s*\([^)]*\)\s*async\b")]
    private static partial Regex AsyncFunctionWithValue();

    private static IEnumerable<LogicFinding> DartFutureUsedAsValue(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        var asyncNames = AsyncFunctionWithValue().Matches(code).Select(function => function.Groups["name"].Value).ToHashSet();
        if (asyncNames.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            var kept = Regex.Match(masked[index], @"^\s*(?:var|final)\s+(?<name>[a-z_]\w*)\s*=\s*(?<call>(?<function>[a-z_]\w*)\s*\([^()]*\))\s*;\s*$");
            if (!kept.Success || !asyncNames.Contains(kept.Groups["function"].Value)) continue;

            var name = kept.Groups["name"].Value;
            var used = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(later =>
                Regex.IsMatch(masked[later], $@"\bprint\s*\(\s*{Regex.Escape(name)}\s*\)") || Regex.IsMatch(source.Lines[later], $@"\${Regex.Escape(name)}\b|\$\{{\s*{Regex.Escape(name)}\s*\}}"), -1);
            if (used < 0) continue;

            // await can be written only inside a function marked async - main() async { ... } among them.
            var enclosing = Enumerable.Range(0, index).Reverse().FirstOrDefault(above => Regex.IsMatch(masked[above], @"\)\s*(?:async\s*)?\{\s*$"), -1);
            var awaitAllowed = enclosing >= 0 && Regex.IsMatch(masked[enclosing], @"\basync\s*\{\s*$");

            var line = source.Lines[index];
            var call = kept.Groups["call"];
            var function = kept.Groups["function"].Value;

            yield return new LogicFinding(id, index + 1,
                $"{function} is async, and called without await it gives back a Future of its value, not the value - so {name} on line {used + 1} " +
                "is the Future: it printed as Instance of 'Future<int>'",
                awaitAllowed
                    ? LocalFix.ReplaceLine(id, $"Wait for its value: await {function}(...)",
                        $"await waits for {function} to finish and gives its value, so {name} is that value.",
                        source.Path, index + 1, line[..call.Index] + "await " + line[call.Index..])
                    : null);
        }
    }
}
