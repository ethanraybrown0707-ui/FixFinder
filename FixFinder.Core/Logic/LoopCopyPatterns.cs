using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// What a loop seems to change, or give back, and does not - each run with the language's own toolchain, and its own checks
/// said nothing of any. Python's for mark in marks, JavaScript's for (let mark of marks), PHP's foreach ($marks as $mark) and
/// PowerShell's foreach ($mark in $marks) give each item to a variable of the loop's own, so mark += 5 left 70, 80 and 90 as
/// they were. Ruby's each gives back the list it went over, not what its block worked out, so doubled was [70, 80] - and
/// Elixir's Enum.each gives back :ok, so doubled was :ok, and JavaScript's forEach undefined. Go's range with one variable gives each position, so the total of
/// 70, 80 and 90 came out as 3. And a PowerShell function gives back everything it outputs, so Write-Output's text came back
/// with the total. LoopCopyLiveTests runs each mistake, and its change.
/// </summary>
public static partial class LoopCopyPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-loop-variable-changed", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonLoopVariable, Severity.Error),
        new CodePattern("logic-js-loop-copy-changed", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, ScriptLoopCopy, Severity.Error),
        new CodePattern("logic-php-foreach-copy-changed", CodePattern.Files(".php"), Syntax.CLike, PhpForeachCopy, Severity.Error),
        new CodePattern("logic-powershell-foreach-copy-changed", CodePattern.Files(".ps1"), Syntax.Python, PowerShellForeachCopy, Severity.Error),
        new CodePattern("logic-ruby-each-for-map", CodePattern.Files(".rb"), Syntax.Python, RubyEachForMap, Severity.Error, Confidence.Certain),
        new CodePattern("logic-elixir-each-for-map", CodePattern.Files(".ex", ".exs"), Syntax.Python, ElixirEachForMap, Severity.Error, Confidence.Certain),
        new CodePattern("logic-js-foreach-for-map", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, ScriptForEachForMap, Severity.Error, Confidence.Certain),
        new CodePattern("logic-go-range-index-as-value", CodePattern.Files(".go"), Syntax.CLike, GoRangeIndex, Severity.Error),
        new CodePattern("logic-js-for-in-over-array", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, ScriptForInOverArray, Severity.Error),
        new CodePattern("logic-powershell-function-output-returned", CodePattern.Files(".ps1"), Syntax.Python, PowerShellOutputReturned, Severity.Error),
    ];

    /// <summary>Whether a later line than <paramref name="after"/> reads <paramref name="name"/>.</summary>
    private static bool ReadAfter(IReadOnlyList<string> lines, int after, string name, string prefix = "") =>
        lines.Skip(after + 1).Any(line => Regex.IsMatch(line, $@"(?<![\w.$]){Regex.Escape(prefix + name)}\b"));

    /// <summary>A name for the position that the file does not use already: index, or position.</summary>
    private static string PositionName(IReadOnlyList<string> masked, string prefix = "") =>
        masked.Any(line => Regex.IsMatch(line, $@"(?<![\w$]){Regex.Escape(prefix)}index\b")) ? "position" : "index";

    /// <summary><c>for mark in marks:</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)for\s+(?<variable>[a-z_]\w*)\s+in\s+(?<list>[a-z_]\w*)\s*:\s*$")]
    private static partial Regex PythonForHeading();

    /// <summary><c>mark += 5</c>, <c>mark = mark * 2</c> - the loop's variable given a new value.</summary>
    private static Regex GivenANewValue(string variable, string prefix = "") =>
        new($@"^(?<indent>\s*){Regex.Escape(prefix + variable)}\s*(?<op>[-+*/]?=)(?!=)\s*(?<value>[^;]+?)\s*;?\s*$");

    /// <summary>
    /// The loop's variable given a new value, as the loop's one line: the list's item is not changed, only the variable, and
    /// the list read after the loop is as it was. The change goes over the positions and changes the item at each.
    /// </summary>
    private static IEnumerable<LogicFinding> PythonLoopVariable(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count - 1; index++)
        {
            if (PythonForHeading().Match(masked[index]) is not { Success: true } heading) continue;

            var (first, end) = PythonBlocks.Body(masked, index);
            if (end - first != 1) continue;

            var variable = heading.Groups["variable"].Value;
            var list = heading.Groups["list"].Value;
            if (GivenANewValue(variable).Match(masked[first]) is not { Success: true } given || !ReadAfter(masked, end - 1, list)) continue;

            var position = PositionName(masked);
            var value = source.Lines[first].Substring(given.Groups["value"].Index, given.Groups["value"].Length);

            yield return new LogicFinding(id, first + 1,
                $"for gives each item of {list} to {variable}, a variable of the loop's own - so giving {variable} a new value leaves the item " +
                $"in {list} as it was, and {list} is unchanged after the loop",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Change the item itself: {list}[{position}] {given.Groups["op"].Value} ...",
                    Explanation = $"enumerate gives each item's position with it, so the line changes the item at that position in {list}.",
                    File = source.Path,
                    StartLine = index + 1,
                    RemoveCount = 2,
                    NewLines =
                    [
                        $"{heading.Groups["indent"].Value}for {position}, {variable} in enumerate({list}):",
                        $"{given.Groups["indent"].Value}{list}[{position}] {given.Groups["op"].Value} {value}",
                    ],
                });
        }
    }

    /// <summary><c>for (let mark of marks) {</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)for\s*\(\s*let\s+(?<variable>[A-Za-z_$][\w$]*)\s+of\s+(?<list>[A-Za-z_$][\w$.]*)\s*\)\s*\{\s*$")]
    private static partial Regex ScriptForOfHeading();

    /// <summary>
    /// JavaScript's for...of: the same - the loop's variable given a new value, as the loop's one statement, leaves the array's
    /// item as it was. The change goes over the positions and changes the item at each.
    /// </summary>
    private static IEnumerable<LogicFinding> ScriptLoopCopy(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index + 2 < masked.Count; index++)
        {
            if (ScriptLoopCopyAt(masked, index) is not var (heading, given)) continue;

            var variable = heading.Groups["variable"].Value;
            var list = heading.Groups["list"].Value;
            var position = PositionName(masked);

            // The new loop has no variable of the item's own, so a value that reads it - item = item.trim() - reads the item at its
            // position instead. The names are found in the code, not inside its text.
            var valueGroup = given.Groups["value"];
            var value = source.Lines[index + 1].Substring(valueGroup.Index, valueGroup.Length);
            foreach (var read in new Regex($@"(?<![\w$.]){Regex.Escape(variable)}(?![\w$])").Matches(masked[index + 1].Substring(valueGroup.Index, valueGroup.Length)).Reverse())
                value = value[..read.Index] + $"{list}[{position}]" + value[(read.Index + read.Length)..];

            yield return new LogicFinding(id, index + 2,
                $"for...of gives each item of {list} to {variable}, a variable of the loop's own - so giving {variable} a new value leaves the " +
                $"item in {list} as it was, and {list} is unchanged after the loop",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Change the item itself: {list}[{position}] {given.Groups["op"].Value} ...",
                    Explanation = $"A loop over the positions reaches each item in {list} itself, so the line changes the item at that position.",
                    File = source.Path,
                    StartLine = index + 1,
                    RemoveCount = 2,
                    NewLines =
                    [
                        $"{heading.Groups["indent"].Value}for (let {position} = 0; {position} < {list}.length; {position}++) {{",
                        $"{given.Groups["indent"].Value}{list}[{position}] {given.Groups["op"].Value} {value};",
                    ],
                });
        }
    }

    /// <summary>
    /// Whether the line starts a for...of loop that this check changes: one statement, giving the loop's variable a new value,
    /// and the array read after the loop. The check on loop copies in general leaves such a loop to this one.
    /// </summary>
    internal static bool FindsScriptLoop(IReadOnlyList<string> masked, int index) => ScriptLoopCopyAt(masked, index) is not null;

    private static (Match Heading, Match Given)? ScriptLoopCopyAt(IReadOnlyList<string> masked, int index)
    {
        if (index + 2 >= masked.Count || ScriptForOfHeading().Match(masked[index]) is not { Success: true } heading || masked[index + 2].Trim() != "}") return null;

        var variable = heading.Groups["variable"].Value;
        return GivenANewValue(variable).Match(masked[index + 1]) is { Success: true } given && ReadAfter(masked, index + 2, heading.Groups["list"].Value)
            ? (heading, given)
            : null;
    }

    /// <summary><c>foreach ($marks as $mark) {</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)foreach\s*\(\s*\$(?<list>\w+)\s+as\s+\$(?<variable>\w+)\s*\)\s*\{\s*$")]
    private static partial Regex PhpForeachHeading();

    /// <summary>
    /// PHP's foreach: the same - the loop's variable given a new value, as the loop's one statement, leaves the array's item as
    /// it was. The change takes each item's key with it, and changes the item under that key.
    /// </summary>
    private static IEnumerable<LogicFinding> PhpForeachCopy(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index + 2 < masked.Count; index++)
        {
            if (PhpForeachHeading().Match(masked[index]) is not { Success: true } heading || masked[index + 2].Trim() != "}") continue;

            var variable = heading.Groups["variable"].Value;
            var list = heading.Groups["list"].Value;
            if (GivenANewValue(variable, "$").Match(masked[index + 1]) is not { Success: true } given || !ReadAfter(masked, index + 2, list, "$")) continue;

            var key = masked.Any(line => line.Contains("$key", StringComparison.Ordinal)) ? "position" : "key";
            var value = source.Lines[index + 1].Substring(given.Groups["value"].Index, given.Groups["value"].Length);

            yield return new LogicFinding(id, index + 2,
                $"foreach gives each item of ${list} to ${variable}, a copy of its own - so giving ${variable} a new value leaves the item in " +
                $"${list} as it was, and ${list} is unchanged after the loop",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Change the item itself: ${list}[${key}] {given.Groups["op"].Value} ...",
                    Explanation = $"foreach gives each item's key with it as well, so the line changes the item under that key in ${list}.",
                    File = source.Path,
                    StartLine = index + 1,
                    RemoveCount = 2,
                    NewLines =
                    [
                        $"{heading.Groups["indent"].Value}foreach (${list} as ${key} => ${variable}) {{",
                        $"{given.Groups["indent"].Value}${list}[${key}] {given.Groups["op"].Value} {value};",
                    ],
                });
        }
    }

    /// <summary><c>foreach ($mark in $marks) {</c> - PowerShell's words, in any case.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)foreach\s*\(\s*\$(?<variable>\w+)\s+in\s+\$(?<list>\w+)\s*\)\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellForeachHeading();

    /// <summary><c>$marks = @(70, 80, 90)</c> or <c>$marks = 70, 80, 90</c> - an array, made where it is named.</summary>
    private static Regex ArrayMade(string list) => new($@"^\s*\${Regex.Escape(list)}\s*=\s*(?:@\(|[^=,]+,)", RegexOptions.IgnoreCase);

    /// <summary>
    /// PowerShell's foreach: the same - the loop's variable given a new value, as the loop's one statement, leaves the array's
    /// item as it was. Only an array the file makes is changed, as a position in it is then an item. The change goes over the
    /// positions and changes the item at each.
    /// </summary>
    private static IEnumerable<LogicFinding> PowerShellForeachCopy(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index + 2 < masked.Count; index++)
        {
            if (PowerShellForeachHeading().Match(masked[index]) is not { Success: true } heading || masked[index + 2].Trim() != "}") continue;

            var variable = heading.Groups["variable"].Value;
            var list = heading.Groups["list"].Value;
            if (GivenANewValue(variable, "$").Match(masked[index + 1]) is not { Success: true } given || !ReadAfter(masked, index + 2, list, "$")) continue;
            if (!masked.Take(index).Any(line => ArrayMade(list).IsMatch(line))) continue;

            var position = PositionName(masked, "$");
            var valueGroup = given.Groups["value"];
            var value = source.Lines[index + 1].Substring(valueGroup.Index, valueGroup.Length);
            foreach (var read in new Regex($@"\${Regex.Escape(variable)}(?!\w)", RegexOptions.IgnoreCase).Matches(masked[index + 1].Substring(valueGroup.Index, valueGroup.Length)).Reverse())
                value = value[..read.Index] + $"${list}[${position}]" + value[(read.Index + read.Length)..];

            yield return new LogicFinding(id, index + 2,
                $"foreach gives each item of ${list} to ${variable}, a copy of its own - so giving ${variable} a new value leaves the item in " +
                $"${list} as it was, and ${list} is unchanged after the loop",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Change the item itself: ${list}[${position}] {given.Groups["op"].Value} ...",
                    Explanation = $"A loop over the positions reaches each item in ${list} itself, so the line changes the item at that position.",
                    File = source.Path,
                    StartLine = index + 1,
                    RemoveCount = 2,
                    NewLines =
                    [
                        $"{heading.Groups["indent"].Value}for (${position} = 0; ${position} -lt ${list}.Count; ${position}++) {{",
                        $"{given.Groups["indent"].Value}${list}[${position}] {given.Groups["op"].Value} {value}",
                    ],
                });
        }
    }

    /// <summary><c>doubled = marks.each { |mark| mark * 2 }</c> - what each gives back, kept.</summary>
    [GeneratedRegex(@"^\s*[a-z_]\w*\s*=\s*[a-z_][\w.]*?(?<each>\.each)(?=\s*(?:\{|do\b)\s*\|)")]
    private static partial Regex EachKept();

    /// <summary>
    /// What each gives back, given to a name: each gives back the list it went over, not what its block worked out - map does.
    /// </summary>
    private static IEnumerable<LogicFinding> RubyEachForMap(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (EachKept().Match(masked[index]) is not { Success: true } kept) continue;

            var each = kept.Groups["each"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                "each gives back the list it went over, as it was - not what its block worked out for each item - so the name is given " +
                "the same list again",
                LocalFix.ReplaceLine(id, "Keep what the block works out: map",
                    "map gives back a new list of what its block worked out for each item.",
                    source.Path, index + 1, line[..each.Index] + ".map" + line[(each.Index + each.Length)..]));
        }
    }

    /// <summary>
    /// <c>doubled = Enum.each(marks, fn mark -> mark * 2 end)</c>, or <c>doubled = marks |> Enum.each(...)</c> - what
    /// Enum.each gives back, given to a name that does not begin with _, which Elixir keeps for values thrown away.
    /// </summary>
    [GeneratedRegex(@"^\s*(?<name>[a-z]\w*)\s*=\s*(?:[a-z_][\w.]*\s*\|>\s*)?Enum(?<each>\.each)\s*\(")]
    private static partial Regex EnumEachKept();

    /// <summary>
    /// What Enum.each gives back, given to a name read later: Enum.each runs its function for each item and gives back :ok,
    /// not what the function worked out - Enum.map does.
    /// </summary>
    /// <summary><c>const doubled = marks.forEach(</c> - what forEach gives back, given to a name.</summary>
    [GeneratedRegex(@"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*[A-Za-z_$][\w$.]*?(?<each>\.forEach)\s*\(")]
    private static partial Regex ForEachKept();

    /// <summary>
    /// What forEach gives back, given to a name read later: forEach runs its function for each item and gives back undefined,
    /// not what the function worked out - map does.
    /// </summary>
    private static IEnumerable<LogicFinding> ScriptForEachForMap(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ForEachKept().Match(masked[index]) is not { Success: true } kept || !ReadAfter(masked, index, kept.Groups["name"].Value)) continue;

            var name = kept.Groups["name"].Value;
            var each = kept.Groups["each"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"forEach runs its function for each item and gives back undefined - not what the function worked out - so {name} is undefined",
                LocalFix.ReplaceLine(id, "Keep what the function works out: map",
                    "map gives back a new array of what its function worked out for each item.",
                    source.Path, index + 1, line[..each.Index] + ".map" + line[(each.Index + each.Length)..]));
        }
    }

    private static IEnumerable<LogicFinding> ElixirEachForMap(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (EnumEachKept().Match(masked[index]) is not { Success: true } kept || !ReadAfter(masked, index, kept.Groups["name"].Value)) continue;

            var name = kept.Groups["name"].Value;
            var each = kept.Groups["each"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"Enum.each runs its function for each item and gives back :ok - not what the function worked out - so {name} is :ok",
                LocalFix.ReplaceLine(id, "Keep what the function works out: Enum.map",
                    "Enum.map gives back a new list of what its function worked out for each item.",
                    source.Path, index + 1, line[..each.Index] + ".map" + line[(each.Index + each.Length)..]));
        }
    }

    /// <summary><c>for mark := range marks {</c> - range with one variable.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)for\s+(?<variable>[a-z_]\w*)\s*:=\s*range\s+(?<slice>[a-zA-Z_]\w*)\s*\{\s*$")]
    private static partial Regex GoRangeOneVariable();

    /// <summary>
    /// range with one variable over a slice, whose variable is used as an item - added up, printed - rather than as a position
    /// in the slice: range gives each position, 0, 1, 2. The change takes the item as range's second variable.
    /// </summary>
    private static IEnumerable<LogicFinding> GoRangeIndex(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            if (GoRangeOneVariable().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var slice = loop.Groups["slice"].Value;

            // A slice - made with []T{...} or declared []T - not a map, whose range gives its keys.
            if (!Regex.IsMatch(code, $@"\b{Regex.Escape(slice)}\s*:?=\s*\[\]|\b{Regex.Escape(slice)}\s+\[\]")) continue;

            var asPosition = new Regex($@"\[\s*{Regex.Escape(variable)}\s*\]");
            var used = new Regex($@"(?<![\w.]){Regex.Escape(variable)}\b");
            var lines = Enumerable.Range(body.First, body.End - body.First).Select(line => masked[line]).ToList();
            if (!lines.Any(line => used.IsMatch(asPosition.Replace(line, "")))) continue;

            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"range with one variable gives each position in {slice} - 0, 1, 2 - not the item there, so {variable} is a position, and the " +
                "loop works with positions where it means items",
                LocalFix.ReplaceLine(id, $"Take the item as range's second variable: for _, {variable} := range {slice}",
                    $"range gives the position and then the item; _ leaves the position unused, and {variable} is the item.",
                    source.Path, index + 1, $"{loop.Groups["indent"].Value}for _, {variable} := range {slice} {{"));
        }
    }

    /// <summary><c>for (const mark in marks) {</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)for\s*\(\s*(?<kind>const|let|var)\s+(?<variable>[A-Za-z_$][\w$]*)\s+in\s+(?<list>[A-Za-z_$][\w$]*)\s*\)\s*\{\s*$")]
    private static partial Regex ScriptForInHeading();

    /// <summary>
    /// JavaScript's for...in over an array, whose variable is used as an item - added up, printed - and never as a position in
    /// the array: for...in gives each position, as text - "0", "1", "2" - so total += mark joins text, and Node printed Total: 0012
    /// for marks of 70, 80 and 90. The change goes over the items with for...of.
    /// </summary>
    private static IEnumerable<LogicFinding> ScriptForInOverArray(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            if (ScriptForInHeading().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var list = loop.Groups["list"].Value;

            // An array - made with [...], Array.from, new Array, Object.keys or values, or a method that gives one - not an object,
            // whose for...in gives its keys.
            if (!Regex.IsMatch(code,
                    $@"(?<![\w$.]){Regex.Escape(list)}\s*=\s*(?:\[|Array\.from\(|new Array\(|Object\.(?:keys|values)\(|[^;\n]*?\.(?:split|map|filter|slice|concat)\()")) continue;

            var asPosition = new Regex($@"\[\s*{Regex.Escape(variable)}\s*\]");
            var used = new Regex($@"(?<![\w$.]){Regex.Escape(variable)}(?![\w$])");
            var lines = Enumerable.Range(body.First, body.End - body.First).Select(line => masked[line]).ToList();
            if (lines.Any(asPosition.IsMatch) || !lines.Any(used.IsMatch)) continue;

            var line = source.Lines[index];
            var heading = Regex.Match(line, $@"\b{loop.Groups["kind"].Value}\s+{Regex.Escape(variable)}\s+in\b");

            yield return new LogicFinding(id, index + 1,
                $"for...in goes over the positions in {list} - \"0\", \"1\", \"2\", as text - not its items, so {variable} is a position " +
                $"written as text, and the loop works with positions where it means items: adding {variable} to a number joins the two as text",
                LocalFix.ReplaceLine(id, $"Go over the items with for...of: for ({loop.Groups["kind"].Value} {variable} of {list})",
                    $"for...of gives each item in {list} in turn, so {variable} is the item itself, not its position.",
                    source.Path, index + 1, line[..heading.Index] + heading.Value[..^2] + "of" + line[(heading.Index + heading.Length)..]));
        }
    }

    /// <summary><c>function Get-Total($marks) {</c>.</summary>
    [GeneratedRegex(@"^\s*function\s+(?<name>[\w-]+)\s*(?:\([^)]*\))?\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellFunctionHeading();

    [GeneratedRegex(@"^(?<indent>\s*)Write-Output\b", RegexOptions.IgnoreCase)]
    private static partial Regex WriteOutputLine();

    [GeneratedRegex(@"^\s*return\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex ReturnLine();

    /// <summary>
    /// A function that writes with Write-Output and returns a value, whose result is kept: PowerShell gives back everything a
    /// function outputs, so the text comes back with the value. The change writes the text to the screen with Write-Host.
    /// </summary>
    private static IEnumerable<LogicFinding> PowerShellOutputReturned(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PowerShellFunctionHeading().Match(masked[index]) is not { Success: true } function || BraceBlocks.Body(masked, index) is not { } body) continue;

            var name = function.Groups["name"].Value;
            var written = Enumerable.Range(body.First, body.End - body.First).Where(line => WriteOutputLine().IsMatch(masked[line])).ToList();
            if (written.Count == 0 || !Enumerable.Range(body.First, body.End - body.First).Any(line => ReturnLine().IsMatch(masked[line]))) continue;
            if (!masked.Any(line => Regex.IsMatch(line, $@"^\s*\$\w+\s*=\s*{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))) continue;

            var from = written[0];
            var to = written[^1];

            yield return new LogicFinding(id, from + 1,
                $"PowerShell gives back everything a function outputs, not only what return names - so the text Write-Output writes in {name} " +
                "comes back as part of its result, and the value kept from it holds the text too",
                new LocalFix
                {
                    RuleId = id,
                    Title = "Write the text to the screen: Write-Host",
                    Explanation = "Write-Host shows the text on the screen and outputs nothing, so the function gives back only what return names.",
                    File = source.Path,
                    StartLine = from + 1,
                    RemoveCount = to - from + 1,
                    NewLines = Enumerable.Range(from, to - from + 1)
                        .Select(line => written.Contains(line) ? WriteOutputLine().Replace(source.Lines[line], "${indent}Write-Host", 1) : source.Lines[line])
                        .ToList(),
                });
        }
    }
}
