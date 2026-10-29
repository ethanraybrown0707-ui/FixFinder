using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Python's own library used in a way that runs, says nothing, and does something else - each run with python -W error -X dev,
/// which said nothing of any: a Thread's run() called where start() was meant ran its work on the main thread - "worker on
/// MainThread"; Decimal(0.1) holds 0.1000000000000000055511151231257827021181583404541015625, the float 0.1 is;
/// groups.get(team, []).append(name) appends to a new list that get hands back and nothing keeps, so groups stayed {};
/// numbers = map(int, ...) read twice gives nothing the second time - sum(numbers) was 7 and len(list(numbers)) 0;
/// "test.txt".rstrip(".txt") takes off every t, x and . from the end - "tes" - as its argument is a set of characters, not
/// an ending; and a lambda made in a loop keeps the loop's variable, not its value then, so [lambda x: x + step for step in
/// range(3)] all added 2 - [12, 12, 12].
/// </summary>
public static partial class PythonLibraryPatterns
{
    private static readonly IReadOnlySet<string> PythonFiles = CodePattern.Files(".py", ".pyw");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-thread-run", PythonFiles, Syntax.Python, ThreadRun),
        new CodePattern("logic-python-decimal-from-float", PythonFiles, Syntax.Python, DecimalFromFloat, Severity.Warning, Confidence.Certain),
        new CodePattern("logic-python-get-default-changed", PythonFiles, Syntax.Python, GetDefaultChanged, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-iterator-used-twice", PythonFiles, Syntax.Python, IteratorUsedTwice, Severity.Error),
        new CodePattern("logic-python-strip-word", PythonFiles, Syntax.Python, StripWord),
        new CodePattern("logic-python-lambda-in-loop", PythonFiles, Syntax.Python, LambdaInLoop, Severity.Error),
        new CodePattern("logic-python-asyncio-run-in-async", PythonFiles, Syntax.Python, AsyncioRunInAsync, Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>asyncio.run(</c> - which starts an event loop of its own.</summary>
    [GeneratedRegex(@"(?<![\w.])asyncio\s*\.\s*run\s*\(")]
    private static partial Regex AsyncioRun();

    [GeneratedRegex(@"^\s*async\s+def\b")]
    private static partial Regex AsyncDef();

    [GeneratedRegex(@"^\s*(?:def|class|lambda)\b")]
    private static partial Regex OtherScope();

    /// <summary>Whether a line is inside an async def: the nearest def or class it is written under is one.</summary>
    private static bool InsideAsyncDef(IReadOnlyList<string> masked, int index)
    {
        var indent = masked[index].Length - masked[index].TrimStart().Length;
        for (var above = index - 1; above >= 0 && indent > 0; above--)
        {
            if (masked[above].Trim().Length == 0) continue;
            var outer = masked[above].Length - masked[above].TrimStart().Length;
            if (outer >= indent) continue;

            indent = outer;
            if (AsyncDef().IsMatch(masked[above])) return true;
            if (OtherScope().IsMatch(masked[above])) return false;
        }

        return false;
    }

    /// <summary>
    /// asyncio.run(...) inside an async def: the async def is run by the event loop asyncio.run started, and asyncio.run will
    /// not start a second inside it - Python 3.13 stopped with RuntimeError: asyncio.run() cannot be called from a running
    /// event loop, and warned that the coroutine it was given was never awaited. Inside an async def, a coroutine is awaited:
    /// await fetch() printed 42.
    /// </summary>
    private static IEnumerable<LogicFinding> AsyncioRunInAsync(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (AsyncioRun().Match(masked[index]) is not { Success: true } run || !InsideAsyncDef(masked, index)) continue;

            var open = run.Index + run.Length - 1;
            var close = Brackets.Closing(masked[index], open);
            if (close < 0) continue;

            var line = source.Lines[index];
            var awaited = line[(open + 1)..close].Trim();
            if (awaited.Length == 0 || awaited.Contains(',')) continue;

            yield return new LogicFinding(id, index + 1,
                $"`{line[run.Index..(close + 1)].Trim()}` starts an event loop of its own, but this line is inside an async def, which an " +
                "event loop is already running - so Python stops with RuntimeError: asyncio.run() cannot be called from a running event loop",
                LocalFix.ReplaceLine(id, $"Await it: await {awaited}",
                    "Inside an async def, the loop that runs it is already there: await runs the coroutine in it, and gives back its value.",
                    source.Path, index + 1, line[..run.Index] + "await " + awaited + line[(close + 1)..]));
        }
    }

    /// <summary><c>worker.run()</c>, or <c>threading.Thread(...).run()</c> on one line.</summary>
    [GeneratedRegex(@"(?:(?<![\w.])(?<name>[A-Za-z_]\w*)|(?<made>\b(?:threading\s*\.\s*)?Thread\s*\(.*\)))\s*\.\s*run\s*\(\s*\)")]
    private static partial Regex RunCalled();

    private static IEnumerable<LogicFinding> ThreadRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            if (RunCalled().Match(masked[index]) is not { Success: true } called) continue;

            var name = called.Groups["name"].Value;
            if (!called.Groups["made"].Success &&
                !Regex.IsMatch(code, $@"(?m)^\s*{Regex.Escape(name)}\s*=\s*(?:threading\s*\.\s*)?Thread\s*\(")) continue;

            var line = source.Lines[index];
            var run = line.LastIndexOf("run", called.Index + called.Length, StringComparison.Ordinal);

            yield return new LogicFinding(id, index + 1,
                $"run() runs {(called.Groups["made"].Success ? "the thread" : name)}'s work here, on the thread that calls it - nothing runs " +
                "alongside it - where start() starts it on a thread of its own",
                LocalFix.ReplaceLine(id, "Start the thread: .start()",
                    "start() runs the work on the new thread, alongside the code that started it.",
                    source.Path, index + 1, line[..run] + "start" + line[(run + 3)..]));
        }
    }

    /// <summary><c>Decimal(0.1)</c> - a decimal written as a float.</summary>
    [GeneratedRegex(@"(?<![\w.])(?:decimal\s*\.\s*)?Decimal\s*\(\s*(?<value>-?\d+\.\d+)\s*\)")]
    private static partial Regex DecimalOfFloat();

    private static IEnumerable<LogicFinding> DecimalFromFloat(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match made in DecimalOfFloat().Matches(masked[index]))
            {
                var value = made.Groups["value"].Value;
                if (ManagedLibraryPatterns.IsExactInBinary(value)) continue;

                var line = source.Lines[index];
                var number = made.Groups["value"];
                var changed = line[..number.Index] + $"\"{value}\"" + line[(number.Index + number.Length)..];

                yield return new LogicFinding(id, index + 1,
                    $"{value} is a float before Decimal sees it, and a float cannot hold most decimals exactly - Decimal(0.1) is " +
                    "0.1000000000000000055511151231257827021181583404541015625 - so the Decimal starts with that error in it",
                    LocalFix.ReplaceLine(id, $"Make it from text: Decimal(\"{value}\")",
                        $"Made from the text \"{value}\", the Decimal holds exactly that decimal.",
                        source.Path, index + 1, changed));
            }
        }
    }

    /// <summary><c>groups.get(team, []).append(name)</c> - a default list, set or dict changed and then lost.</summary>
    [GeneratedRegex(@"(?<![\w.])(?<mapping>[A-Za-z_][\w.]*)\s*\.\s*get\s*\(\s*(?<key>[^,()]+(?:\([^()]*\))?)\s*,\s*(?<default>\[\s*\]|\{\s*\}|set\(\s*\)|list\(\s*\)|dict\(\s*\))\s*\)\s*\.\s*(?<method>append|extend|add|update|insert)\s*\(")]
    private static partial Regex GetThenChanged();

    private static IEnumerable<LogicFinding> GetDefaultChanged(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match changedDefault in GetThenChanged().Matches(masked[index]))
            {
                var line = source.Lines[index];
                var get = masked[index].IndexOf("get", changedDefault.Groups["mapping"].Index + changedDefault.Groups["mapping"].Length, StringComparison.Ordinal);
                var mapping = changedDefault.Groups["mapping"].Value;
                var method = changedDefault.Groups["method"].Value;

                yield return new LogicFinding(id, index + 1,
                    $"get gives back the default when the key is missing - a new {changedDefault.Groups["default"].Value} - without putting it in " +
                    $"{mapping}, so .{method} changes that new one, which nothing keeps, and {mapping} is left as it was",
                    LocalFix.ReplaceLine(id, $"Put the default in first: {mapping}.setdefault(...).{method}(...)",
                        $"setdefault puts the default in {mapping} when the key is missing, and gives back what is there, so .{method} changes what " +
                        $"{mapping} holds.",
                        source.Path, index + 1, line[..get] + "setdefault" + line[(get + 3)..]));
            }
        }
    }

    /// <summary><c>numbers = map(int, words)</c> - an iterator, read once.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*(?<made>(?:map|filter|zip|reversed|enumerate)\s*\(.*\)|\(.*\bfor\b.+\bin\b.*\))\s*$")]
    private static partial Regex IteratorMade();

    /// <summary>Whether a line reads all of an iterator: a for over it, or a function that goes through it - sum, list, max, sorted, in.</summary>
    private static Regex ReadsAll(string name) =>
        new($@"\bfor\s+.+\s+in\s+{Regex.Escape(name)}\s*:|\b(?:sum|list|tuple|set|max|min|sorted|any|all|dict|frozenset)\s*\(\s*{Regex.Escape(name)}\s*[,)]|\.join\s*\(\s*{Regex.Escape(name)}\s*\)|\bin\s+{Regex.Escape(name)}\b(?!\s*:)");

    private static IEnumerable<LogicFinding> IteratorUsedTwice(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (IteratorMade().Match(masked[index]) is not { Success: true } made) continue;

            var name = made.Groups["name"].Value;
            var readsAll = ReadsAll(name);
            var givenAgain = new Regex($@"^\s*{Regex.Escape(name)}\s*=(?!=)");
            var reads = new List<int>();
            for (var later = index + 1; later < masked.Count && !givenAgain.IsMatch(masked[later]); later++)
                reads.AddRange(readsAll.Matches(masked[later]).Select(_ => later));

            if (reads.Count < 2) continue;

            var line = source.Lines[index];
            var madeGroup = made.Groups["made"];
            var kept = line.Substring(madeGroup.Index, madeGroup.Length);

            yield return new LogicFinding(id, index + 1,
                $"{name} is an iterator: it gives each item once, and then it is used up - so line {reads[0] + 1} reads all of it, and " +
                (reads[1] == reads[0] ? "the second read on that line" : $"line {reads[1] + 1}") +
                " gets nothing: sum(numbers) was 7, and len(list(numbers)) after it 0",
                LocalFix.ReplaceLine(id, $"Keep the items in a list: {name} = list(...)",
                    "A list keeps its items, so it can be gone through as many times as the program needs.",
                    source.Path, index + 1, line[..madeGroup.Index] + (kept.StartsWith('(') ? "list" + kept : $"list({kept})") +
                                            line[(madeGroup.Index + madeGroup.Length)..]));
        }
    }

    /// <summary><c>name.rstrip(".txt")</c> or <c>name.lstrip("www.")</c> - text that reads as an ending or a beginning, given as characters.</summary>
    [GeneratedRegex(@"\.\s*(?<method>rstrip|lstrip)\s*\(\s*(?<quote>[""'])(?<text>[^""'\\]{2,})\k<quote>\s*\)")]
    private static partial Regex StripText();

    private static IEnumerable<LogicFinding> StripWord(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            // The argument is in quotes, which masking blanks, so it is read from the line as written.
            foreach (Match stripped in StripText().Matches(line))
            {
                if (masked[index].Length < stripped.Index + 1 || masked[index][stripped.Index] != '.') continue;

                var text = stripped.Groups["text"].Value;
                // A word, or an ending such as .txt - at least two letters together - not a set like " \n" or ",.;".
                if (!Regex.IsMatch(text, "[A-Za-z]{2}")) continue;

                var method = stripped.Groups["method"].Value;
                var ending = method == "rstrip";
                var instead = ending ? "removesuffix" : "removeprefix";
                var methodGroup = stripped.Groups["method"];

                yield return new LogicFinding(id, index + 1,
                    $"{method} takes away characters, not text: \"{text}\" is the set of characters {string.Join(", ", text.Distinct().Select(character => $"'{character}'"))}, " +
                    $"and every one of them is taken off the {(ending ? "end" : "start")} until another is met - \"test.txt\".rstrip(\".txt\") is \"tes\"",
                    LocalFix.ReplaceLine(id, $"Take off that text: .{instead}(\"{text}\")",
                        $"{instead} takes off \"{text}\" itself, once, when the text {(ending ? "ends" : "starts")} with it, and nothing else.",
                        source.Path, index + 1, line[..methodGroup.Index] + instead + line[(methodGroup.Index + methodGroup.Length)..]));
            }
        }
    }

    /// <summary><c>lambda x: x + step</c> - a lambda, its parameters, and what it works out.</summary>
    [GeneratedRegex(@"\blambda\b(?<parameters>[^:]*):(?<body>(?:(?!\bfor\b)[^,\]\)])*)")]
    private static partial Regex Lambda();

    [GeneratedRegex(@"^(?<indent>\s*)for\s+(?<variable>[A-Za-z_]\w*)\s+in\b.*:\s*$")]
    private static partial Regex ForHeader();

    private static IEnumerable<LogicFinding> LambdaInLoop(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match lambda in Lambda().Matches(masked[index]))
            {
                // The loop's variable: a comprehension's own for, after the lambda, or a for loop the line is inside and that keeps it.
                var comprehension = Regex.Match(masked[index][(lambda.Index + lambda.Length)..], @"^\s*for\s+(?<variable>[A-Za-z_]\w*)\s+in\b");
                var variable = comprehension.Success ? comprehension.Groups["variable"].Value : EnclosingLoopVariable(masked, index);
                if (variable is null) continue;
                if (!comprehension.Success && !Regex.IsMatch(masked[index], @"\.\s*(?:append|insert|add)\s*\(\s*lambda\b|\[[^\]]*\]\s*=\s*lambda\b|=\s*lambda\b")) continue;

                var parameters = lambda.Groups["parameters"].Value;
                if (Regex.IsMatch(parameters, $@"(?<![\w.]){Regex.Escape(variable)}\b") ||
                    !Regex.IsMatch(lambda.Groups["body"].Value, $@"(?<![\w.]){Regex.Escape(variable)}\b")) continue;

                var line = source.Lines[index];
                var parametersGroup = lambda.Groups["parameters"];
                var written = parameters.Trim();
                var withDefault = (written.Length > 0 ? $" {written}, " : " ") + $"{variable}={variable}";

                yield return new LogicFinding(id, index + 1,
                    $"The lambda keeps {variable} itself, not its value when the lambda was made, so when it runs - after the loop - {variable} is the " +
                    $"value that ended the loop, the same for every lambda: [lambda x: x + step for step in range(3)] all added 2",
                    LocalFix.ReplaceLine(id, $"Keep each pass's value as a default: lambda{withDefault}:",
                        $"A default is worked out when the lambda is made, so each lambda keeps the {variable} of its own pass.",
                        source.Path, index + 1, line[..parametersGroup.Index] + withDefault + line[(parametersGroup.Index + parametersGroup.Length)..]));
            }
        }
    }

    /// <summary>The variable of the nearest for loop a line is inside, by its indentation - or null when it is inside none.</summary>
    private static string? EnclosingLoopVariable(IReadOnlyList<string> masked, int index)
    {
        var indent = masked[index].Length - masked[index].TrimStart().Length;
        for (var above = index - 1; above >= 0; above--)
        {
            if (masked[above].Trim().Length == 0) continue;
            var aboveIndent = masked[above].Length - masked[above].TrimStart().Length;
            if (aboveIndent >= indent) continue;
            if (ForHeader().Match(masked[above]) is { Success: true } loop) return loop.Groups["variable"].Value;
            if (Regex.IsMatch(masked[above], @"^\s*(?:def|class)\b")) return null;
            indent = aboveIndent;
        }

        return null;
    }
}
