using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// JavaScript's own library used in a way that runs and does something else - each run with Node.js, which said nothing of
/// any: new Array(3).map(...) maps nothing, as the array has three holes and no items - [ &lt;3 empty items&gt; ];
/// "a-b-c".replace("-", "") replaces the first "-" only - "ab-c"; an async function called without await gives back a
/// Promise, which printed as Promise { 3 } and [object Promise] where 3 was meant, and a map with an async function gave a
/// list of Promises; setTimeout(greet(), 1000) called greet straight away and gave setTimeout what it gave back - Node
/// stopped with ERR_INVALID_ARG_TYPE; getYear() gives the year less 1900 - 124 for 2024; and getDay() gives the day of the
/// week - 1 for Monday 5 February - not the day of the month, which is getDate().
/// </summary>
public static partial class ScriptLibraryPatterns
{
    private static readonly IReadOnlySet<string> ScriptFiles = CodePattern.Files(".js", ".mjs", ".cjs");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-js-array-holes-mapped", ScriptFiles, Syntax.CLike, ArrayHolesMapped, Severity.Error, Confidence.Certain),
        new CodePattern("logic-js-replace-first-only", ScriptFiles, Syntax.CLike, ReplaceFirstOnly, Severity.Warning, Confidence.Possible),
        new CodePattern("logic-js-promise-used-as-value", ScriptFiles, Syntax.CLike, PromiseUsedAsValue, Severity.Error),
        new CodePattern("logic-js-timer-given-a-call", ScriptFiles, Syntax.CLike, TimerGivenACall, Severity.Error),
        new CodePattern("logic-js-get-year", ScriptFiles, Syntax.CLike, GetYear, Severity.Error, Confidence.Certain),
        new CodePattern("logic-js-get-day-for-date", ScriptFiles, Syntax.CLike, GetDayForDate, Severity.Error),
    ];

    /// <summary><c>new Array(3).map(</c> - an array of holes, gone over by a method that passes holes by.</summary>
    [GeneratedRegex(@"(?<made>(?:\bnew\s+)?\bArray\s*\(\s*(?<length>[^(),]+)\s*\))\s*\.\s*(?<method>map|forEach|filter|reduce|some|every|flatMap)\s*\(")]
    private static partial Regex HolesMapped();

    private static IEnumerable<LogicFinding> ArrayHolesMapped(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match mapped in HolesMapped().Matches(masked[index]))
            {
                // A length - a number, a name, a count - not one value in quotes, which makes an array holding it.
                var length = mapped.Groups["length"].Value.Trim();
                if (length.StartsWith('"') || length.StartsWith('\'') || length.StartsWith('`')) continue;

                var line = source.Lines[index];
                var made = mapped.Groups["made"];
                var method = mapped.Groups["method"].Value;
                var changed = $"Array.from({{ length: {line.Substring(mapped.Groups["length"].Index, mapped.Groups["length"].Length).Trim()} }})";

                yield return new LogicFinding(id, index + 1,
                    $"Array({length}) makes an array of {length} holes - no items at all, not even undefined - and {method} passes holes by, so its " +
                    $"function never runs: new Array(3).map(...) is [ <3 empty items> ]",
                    LocalFix.ReplaceLine(id, $"Make an array with items: {changed}",
                        "Array.from({ length }) makes an array whose items are undefined, which map and the others go over.",
                        source.Path, index + 1, line[..made.Index] + changed + line[(made.Index + made.Length)..]));
            }
        }
    }

    /// <summary><c>phone.replace("-", "")</c> - one character given as text, as a separator is.</summary>
    [GeneratedRegex(@"\.\s*replace\s*\(\s*(?<quote>[""'])(?<text>[-,\s_./:|;])\k<quote>\s*,")]
    private static partial Regex ReplaceOneCharacter();

    private static IEnumerable<LogicFinding> ReplaceFirstOnly(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            foreach (Match replaced in ReplaceOneCharacter().Matches(line))
            {
                if (masked[index].Length <= replaced.Index || masked[index][replaced.Index] != '.') continue;

                var text = replaced.Groups["text"].Value;
                var replace = line.IndexOf("replace", replaced.Index, StringComparison.Ordinal);

                yield return new LogicFinding(id, index + 1,
                    $"replace given text replaces only the first \"{text}\" it finds: \"a-b-c\".replace(\"-\", \"\") is \"ab-c\"",
                    LocalFix.ReplaceLine(id, $"Replace every one: .replaceAll(\"{text}\", ...)",
                        $"replaceAll replaces every \"{text}\" in the text.",
                        source.Path, index + 1, line[..replace] + "replaceAll" + line[(replace + "replace".Length)..]));
            }
        }
    }

    /// <summary><c>async function count(</c>, <c>const count = async (</c> - an async function, by its name.</summary>
    [GeneratedRegex(@"\basync\s+function\s*\*?\s*(?<name>[A-Za-z_$][\w$]*)\s*\(|\b(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*async\b")]
    private static partial Regex AsyncFunction();

    /// <summary>
    /// Whether await can be written at the top of a file, outside every function: in a module - an .mjs file, or a .js file
    /// Node takes for one. Node 24 refused a .js file with both require and an await at its top ("Cannot determine intended
    /// module format"), and a .cjs file's top-level await is a SyntaxError.
    /// </summary>
    private static bool TopLevelAwaitAllowed(SourceFile source, string code) =>
        Path.GetExtension(source.Path).ToLowerInvariant() is ".mjs" or ".mts" ||
        (Path.GetExtension(source.Path).ToLowerInvariant() is ".js" or ".ts" && !Regex.IsMatch(code, @"\brequire\s*\(|\bmodule\s*\.\s*exports\b|\bexports\s*\."));

    /// <summary>Whether await can be written on a line: inside a function marked async, or at the top of a file that allows it.</summary>
    private static bool AwaitAllowed(IReadOnlyList<string> masked, int index, bool atTopLevel)
    {
        var depth = 0;
        for (var above = index; above >= 0; above--)
        {
            var line = masked[above];
            for (var position = (above == index ? 0 : line.Length) - 1; position >= 0; position--)
            {
                if (line[position] == '}') depth++;
                else if (line[position] == '{' && --depth < 0)
                    return Regex.IsMatch(line[..position], @"\basync\b") && !Regex.IsMatch(line[..position], @"\bfunction\b[^{]*\bfunction\b");
            }
        }

        return atTopLevel;
    }

    private static IEnumerable<LogicFinding> PromiseUsedAsValue(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        var asyncNames = AsyncFunction().Matches(code).Select(function => function.Groups["name"].Value).ToHashSet();
        var atTopLevel = TopLevelAwaitAllowed(source, code);

        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];

            // const result = count(); - kept without await, and then printed or put into text.
            var kept = Regex.Match(masked[index], @"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?<call>(?<function>[A-Za-z_$][\w$]*)\s*\([^()]*\))\s*;?\s*$");
            if (kept.Success && asyncNames.Contains(kept.Groups["function"].Value))
            {
                var name = kept.Groups["name"].Value;
                var used = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(later =>
                    Regex.IsMatch(masked[later], $@"\bconsole\s*\.\s*log\s*\((?:[^()]*,)?\s*{Regex.Escape(name)}\s*[,)]") ||
                    Regex.IsMatch(source.Lines[later], $@"\$\{{\s*{Regex.Escape(name)}\s*\}}") ||
                    Regex.IsMatch(masked[later], $@"(?<![\w$.]){Regex.Escape(name)}\s*[-+*/]\s*[\w(]|(?<![\w$.]){Regex.Escape(name)}\s*\.\s*(?!then\b|catch\b|finally\b)[A-Za-z_$]"), -1);
                if (used < 0) continue;

                var call = kept.Groups["call"];
                var function = kept.Groups["function"].Value;
                yield return new LogicFinding(id, index + 1,
                    $"{function} is async, and called without await it gives back a Promise of its value, not the value - so {name} on line " +
                    $"{used + 1} is the Promise: it printed as Promise {{ 3 }}, and in text as [object Promise]",
                    AwaitAllowed(masked, index, atTopLevel)
                        ? LocalFix.ReplaceLine(id, $"Wait for its value: await {function}(...)",
                            $"await waits for {function} to finish and gives its value, so {name} is that value.",
                            source.Path, index + 1, line[..call.Index] + "await " + line[call.Index..])
                        : null);
                continue;
            }

            // const doubled = marks.map(async (mark) => ...); - a list of Promises.
            var mapped = Regex.Match(masked[index], @"^\s*(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?<mapped>(?:[A-Za-z_$][\w$.]*|\[[^\]]*\])\s*\.\s*map\s*\(\s*async\b.*\))\s*;?\s*$");
            if (mapped.Success)
            {
                var name = mapped.Groups["name"].Value;
                var mappedGroup = mapped.Groups["mapped"];
                var all = line.Substring(mappedGroup.Index, mappedGroup.Length);
                yield return new LogicFinding(id, index + 1,
                    $"map with an async function gives back a list of Promises, one for each item, not their values - so {name} printed as " +
                    "[ Promise { 2 }, Promise { 4 } ]",
                    AwaitAllowed(masked, index, atTopLevel)
                        ? LocalFix.ReplaceLine(id, "Wait for every value: await Promise.all(...)",
                            "Promise.all waits for every Promise in the list, and await gives the list of their values.",
                            source.Path, index + 1, line[..mappedGroup.Index] + $"await Promise.all({all})" + line[(mappedGroup.Index + mappedGroup.Length)..])
                        : null);
            }
        }
    }

    /// <summary><c>setTimeout(greet(), 1000)</c> - a call given where the function itself was meant.</summary>
    [GeneratedRegex(@"\b(?<timer>setTimeout|setInterval)\s*\(\s*(?<function>[A-Za-z_$][\w$]*)\s*\((?<arguments>[^()]*)\)\s*,")]
    private static partial Regex TimerCall();

    private static IEnumerable<LogicFinding> TimerGivenACall(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match timer in TimerCall().Matches(masked[index]))
            {
                var function = timer.Groups["function"].Value;

                // A function of the file's own that gives back no function - nothing but a function is any use to a timer.
                var declared = Regex.Match(code, $@"\bfunction\s+{Regex.Escape(function)}\s*\([^)]*\)\s*\{{");
                if (!declared.Success) continue;
                var close = Brackets.Closing(code, declared.Index + declared.Length - 1);
                var body = code[declared.Index..(close < 0 ? code.Length : close)];
                if (Regex.IsMatch(body, @"\breturn\s+(?:function\b|\(|[A-Za-z_$][\w$]*\s*=>|async\b)")) continue;

                var line = source.Lines[index];
                var functionGroup = timer.Groups["function"];
                var callEnd = masked[index].IndexOf(')', timer.Groups["arguments"].Index + timer.Groups["arguments"].Length) + 1;
                var arguments = timer.Groups["arguments"].Value.Trim();
                var given = arguments.Length == 0 ? function : $"() => {line[functionGroup.Index..callEnd]}";
                var timerName = timer.Groups["timer"].Value;

                yield return new LogicFinding(id, index + 1,
                    $"{function}(...) is called here, straight away, and {timerName} is given what it gives back - undefined - not the function to call " +
                    $"later: {function} runs at once, and Node stops with ERR_INVALID_ARG_TYPE",
                    LocalFix.ReplaceLine(id, $"Give the function itself: {timerName}({given}, ...)",
                        arguments.Length == 0 ? $"Without the brackets, {function} is the function, which {timerName} calls when its time comes."
                            : "An arrow function holds the call, with its values, until the timer runs it.",
                        source.Path, index + 1, line[..functionGroup.Index] + given + line[callEnd..]));
            }
        }
    }

    [GeneratedRegex(@"\.\s*getYear\s*\(\s*\)")]
    private static partial Regex GetYearCall();

    private static IEnumerable<LogicFinding> GetYear(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match year in GetYearCall().Matches(masked[index]))
            {
                var line = source.Lines[index];
                yield return new LogicFinding(id, index + 1,
                    "getYear() gives the year less 1900 - 124 for 2024 - and is kept only so old pages still run",
                    LocalFix.ReplaceLine(id, "Ask for the whole year: .getFullYear()",
                        "getFullYear() gives the year as it is written - 2024.",
                        source.Path, index + 1, line[..year.Index] + ".getFullYear()" + line[(year.Index + year.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"\.\s*getDay\s*\(\s*\)")]
    private static partial Regex GetDayCall();

    private static IEnumerable<LogicFinding> GetDayForDate(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            // Written beside the month or the year, it is meant as the day of the month - a date.
            if (!Regex.IsMatch(masked[index], @"\.\s*get(?:Month|FullYear|UTCMonth|UTCFullYear)\s*\(")) continue;

            foreach (Match day in GetDayCall().Matches(masked[index]))
            {
                var line = source.Lines[index];
                yield return new LogicFinding(id, index + 1,
                    "getDay() gives the day of the week - 0 for Sunday to 6 for Saturday - not the day of the month: for Monday 5 February it is 1",
                    LocalFix.ReplaceLine(id, "Ask for the day of the month: .getDate()",
                        "getDate() gives the day of the month - 5 for 5 February.",
                        source.Path, index + 1, line[..day.Index] + ".getDate()" + line[(day.Index + day.Length)..]));
            }
        }
    }
}
