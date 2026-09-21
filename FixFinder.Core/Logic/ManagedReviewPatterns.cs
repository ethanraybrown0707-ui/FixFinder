using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>Mistakes that compile and then give the wrong answer, mostly in Java and C#.</summary>
public static partial class ManagedReviewPatterns
{
    private static readonly IReadOnlySet<string> CSharp = CodePattern.Files(".cs");
    private static readonly IReadOnlySet<string> Managed = CodePattern.Files(".java", ".cs");
    private static readonly IReadOnlySet<string> Objects = CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs");
    private static readonly IReadOnlySet<string> RangeLoops = CodePattern.Files(".java", ".js", ".mjs", ".cjs", ".cpp", ".cc", ".cxx", ".c++");
    private static readonly IReadOnlySet<string> Braced = CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs", ".c", ".cpp", ".cc", ".cxx", ".c++");

    private static CodePattern Pattern(
        string id, IReadOnlySet<string> files, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely, FindingKind kind = FindingKind.Logic) =>
        new(id, files, Syntax.CLike, find, severity, confidence, kind);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-csharp-console-read-number", CSharp, ConsoleReadNumber, Severity.Error),
        Pattern("logic-csharp-throw-ex", CSharp, RethrowLosesTrace, confidence: Confidence.Certain),
        Pattern("logic-csharp-blocking-wait", CSharp, BlockingWait),
        Pattern("logic-csharp-not-disposed", CSharp, NotDisposed, Severity.Suggestion),
        Pattern("logic-case-never-matches", Objects, CaseNeverMatches, Severity.Error, Confidence.Certain),
        Pattern("logic-char-used-as-digit", Managed, CharUsedAsDigit, Severity.Error),
        Pattern("logic-count-from-missing-key", Managed, CountFromMissingKey, Severity.Error),
        Pattern("logic-collection-printed", Managed, CollectionPrinted, confidence: Confidence.Certain),
        Pattern("logic-local-hides-field", Managed, LocalHidesField, Severity.Error),
        Pattern("logic-remove-while-counting-up", Managed, RemoveWhileCountingUp, Severity.Error),
        Pattern("logic-loop-copy-assigned", RangeLoops, LoopCopyAssigned),
        Pattern("logic-loop-steps-away", Braced, LoopStepsAway, Severity.Error, Confidence.Certain),
    ];

    private static LocalFix Replace(string id, string title, string explanation, SourceFile source, int line, string text) =>
        LocalFix.ReplaceLine(id, title, explanation, source.Path, line, text);

    private static bool IsJava(SourceFile source) => Path.GetExtension(source.Path).Equals(".java", StringComparison.OrdinalIgnoreCase);

    private static bool IsCpp(SourceFile source) => Path.GetExtension(source.Path).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c++";

    private static HashSet<string> Declared(IReadOnlyList<string> masked, string pattern) =>
        masked.SelectMany(line => Regex.Matches(line, pattern)).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

    private static string Word(string name) => $@"(?<![\w.]){Regex.Escape(name)}\b";

    [GeneratedRegex(@"^(?<lead>\s*)(?:(?<type>int|long|double|decimal|float|var)\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*(?<call>(?:Convert\.To(?:Int32|Int64|Double|Decimal)\(\s*)?Console\.Read\(\s*\)(?:\s*\))?)\s*;")]
    private static partial Regex ConsoleReadAssignment();

    private static IEnumerable<LogicFinding> ConsoleReadNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var numbers = Declared(masked, @"\b(?:int|long|double|decimal|float)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]");

        for (var i = 0; i < masked.Count; i++)
        {
            var match = ConsoleReadAssignment().Match(masked[i]);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            var type = match.Groups["type"].Value;
            if (type.Length == 0 && !numbers.Contains(name)) continue;

            var usedAsCharacter = $@"{Word(name)}\s*[!=]=\s*'|'\s*[!=]=\s*{Word(name)}|\(\s*char\s*\)\s*{Word(name)}";
            if (masked.Any(line => Regex.IsMatch(line, usedAsCharacter))) continue;

            var parse = type is "long" or "double" or "decimal" or "float" ? type : "int";
            var call = match.Groups["call"];
            var line = source.Lines[i];

            yield return new LogicFinding(id, i + 1,
                $"`Console.Read()` gives the code of one character, not the number typed - typing 7 sets `{name}` to 55",
                Replace(id, $"Read the whole line and turn it into a number: {parse}.Parse(Console.ReadLine())",
                    "`Console.Read()` reads a single character and returns its character code: '7' is 55, and Enter is 13. " +
                    $"`Console.ReadLine()` reads everything typed up to Enter, and `{parse}.Parse` turns that text into the number.",
                    source, i + 1, line[..call.Index] + $"{parse}.Parse(Console.ReadLine())" + line[(call.Index + call.Length)..]));
        }
    }

    private static IEnumerable<LogicFinding> RethrowLosesTrace(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var header = Regex.Match(masked[i], @"\bcatch\s*\(\s*[\w.<>]+\s+(?<name>[A-Za-z_]\w*)\s*\)");
            if (!header.Success || BraceBlocks.Body(masked, i) is not { } body) continue;

            var name = header.Groups["name"].Value;

            for (var j = body.First; j < body.End; j++)
            {
                var rethrow = Regex.Match(masked[j], $@"^(?<lead>\s*)throw\s+{Regex.Escape(name)}\s*;");
                if (!rethrow.Success) continue;

                yield return new LogicFinding(id, j + 1,
                    $"`throw {name};` throws the exception again as if it started here, so its stack trace no longer shows the line that failed",
                    Replace(id, "Rethrow it unchanged: throw;",
                        $"`throw {name};` resets the exception's stack trace to this catch block. `throw;` on its own passes on the same " +
                        "exception with its trace intact, so the error still points at the line that caused it.",
                        source, j + 1, rethrow.Groups["lead"].Value + "throw;" + source.Lines[j][rethrow.Length..]));
            }
        }
    }

    [GeneratedRegex(@"(?<task>(?:[A-Za-z_]\w*\s*\.\s*)*[A-Za-z_]\w*\s*(?:\((?:[^()]|\([^()]*\))*\))?)\s*\.\s*(?<wait>Result\b|Wait\(\s*\)|GetAwaiter\(\s*\)\s*\.\s*GetResult\(\s*\))")]
    private static partial Regex BlockingCall();

    private static IEnumerable<LogicFinding> BlockingWait(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var tasks = Declared(masked, @"\bTask(?:<[^>]*>)?\s+(?<name>[A-Za-z_]\w*)\s*=")
            .Union(Declared(masked, @"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?:[\w.]+\.)?(?:\w+Async|Task\.Run)\s*\("))
            .ToHashSet();

        bool IsTask(string expression) =>
            Regex.IsMatch(expression, @"(?:Async|Task\s*\.\s*Run)\s*\((?:[^()]|\([^()]*\))*\)$") || tasks.Contains(expression.Trim());

        for (var i = 0; i < masked.Count; i++)
        {
            if (!Regex.IsMatch(masked[i], @"\basync\s+(?:void|Task|ValueTask)\b[^;=]*\(") || BraceBlocks.Body(masked, i) is not { } body) continue;

            for (var j = body.First; j < body.End; j++)
            {
                if (masked[j].Contains("=>")) continue;
                if (BlockingCall().Match(masked[j]) is not { Success: true } match) continue;

                var line = source.Lines[j];
                var task = line.Substring(match.Groups["task"].Index, match.Groups["task"].Length).Trim();
                if (!IsTask(task)) continue;

                var alreadyAwaited = Enumerable.Range(body.First, j - body.First)
                    .Any(k => Regex.IsMatch(masked[k], @"\bawait\b") && Regex.IsMatch(masked[k], Word(task)));
                if (alreadyAwaited) continue;

                var endsStatement = masked[j][(match.Index + match.Length)..].TrimStart().StartsWith(';');
                var awaited = endsStatement ? $"await {task}" : $"(await {task})";
                var wait = match.Groups["wait"].Value.StartsWith("Result", StringComparison.Ordinal) ? ".Result" : "." + match.Groups["wait"].Value;

                yield return new LogicFinding(id, j + 1,
                    $"`{wait}` stops the thread until `{task}` finishes, inside a method that can simply await it",
                    Replace(id, $"Await it instead: {awaited}",
                        $"`{wait}` blocks while it waits. In a desktop or web app that can freeze the program for good, because the task needs the " +
                        "very thread that is waiting for it, and any exception arrives wrapped in an AggregateException. `await` waits without " +
                        "blocking and hands back the result - or the exception - as it is.",
                        source, j + 1, line[..match.Index] + awaited + line[(match.Index + match.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"^(?<lead>\s*)(?:var|StreamReader|StreamWriter|FileStream|BinaryReader|BinaryWriter|TextReader|TextWriter)\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?:new\s+(?<type>StreamReader|StreamWriter|FileStream|BinaryReader|BinaryWriter)\s*\(|File\s*\.\s*(?<method>OpenText|CreateText|AppendText|OpenRead|OpenWrite|Create)\s*\()")]
    private static partial Regex OpenedFile();

    private static IEnumerable<LogicFinding> NotDisposed(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = OpenedFile().Match(masked[i]);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            var word = Regex.Escape(name);
            var later = masked.Skip(i + 1).ToList();

            var handedOn = $@"\b{word}\s*\.\s*(?:Close|Dispose)\s*\(|\busing\s*\(\s*{word}\s*\)|\breturn\s+{word}\b|[(,]\s*{word}\s*[,)]|{Word(name)}\s*=[^=]";
            if (later.Any(line => Regex.IsMatch(line, handedOn))) continue;

            var writes = match.Groups["type"].Value is "StreamWriter" or "BinaryWriter" ||
                         match.Groups["method"].Value is "CreateText" or "AppendText" or "OpenWrite" or "Create";
            var flushed = later.Any(line => Regex.IsMatch(line, $@"\b{word}\s*\.\s*(?:Flush\s*\(|AutoFlush\s*=\s*true)"));
            var lead = match.Groups["lead"].Value;
            var fixedLine = lead + "using " + source.Lines[i][lead.Length..];

            if (writes && !flushed)
            {
                yield return new LogicFinding(id, i + 1,
                    $"`{name}` is never closed, so what is written to it may never reach the file - it stays in memory and is lost when the program ends",
                    Replace(id, $"Declare {name} with using, so it is closed at the end of the block",
                        "A StreamWriter collects what it is given in memory and only writes it to the file when it is flushed or closed. Nothing " +
                        $"here does either, so the file can end up empty. `using` closes `{name}` at the end of the block, which writes everything out.",
                        source, i + 1, fixedLine))
                {
                    Severity = Severity.Error,
                    Confidence = Confidence.Certain,
                };
                continue;
            }

            yield return new LogicFinding(id, i + 1,
                $"`{name}` is never closed, so the file stays open - and locked against other programs - until the garbage collector gets to it",
                Replace(id, $"Declare {name} with using, so it is closed at the end of the block",
                    $"An open file is held by the operating system until it is closed. `using` closes `{name}` at the end of the block, even when " +
                    "an exception is thrown on the way.",
                    source, i + 1, fixedLine));
        }
    }

    [GeneratedRegex(@"\.\s*(?<method>toLowerCase|toUpperCase|ToLower|ToUpper|ToLowerInvariant|ToUpperInvariant)\s*\(\s*\)\s*(?:(?:===?|!==?)\s*(?<literal>""[^""]*"")|\.\s*(?:equals|Equals)\s*\(\s*(?<literal>""[^""]*"")\s*\))")]
    private static partial Regex CaseChangedComparison();

    private static IEnumerable<LogicFinding> CaseNeverMatches(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            foreach (Match match in CaseChangedComparison().Matches(masked[i]))
            {
                var line = source.Lines[i];
                var literal = match.Groups["literal"];
                var text = line.Substring(literal.Index + 1, literal.Length - 2);
                if (text.Contains('\\')) continue;

                var lower = match.Groups["method"].Value.Contains("Lower", StringComparison.OrdinalIgnoreCase);
                var expected = lower ? text.ToLowerInvariant() : text.ToUpperInvariant();
                if (expected == text) continue;

                var method = match.Groups["method"].Value;

                yield return new LogicFinding(id, i + 1,
                    $"after `{method}()` the text is all {(lower ? "lower" : "upper")} case, so it can never equal \"{text}\" - the comparison always gives the same answer",
                    Replace(id, $"Compare with \"{expected}\"",
                        $"`{method}()` makes every letter {(lower ? "lower" : "upper")} case, which is what lets \"YES\", \"Yes\" and \"yes\" all match - but " +
                        $"only if the text it is compared with is written in {(lower ? "lower" : "upper")} case too. \"{text}\" is not, so nothing typed can match it.",
                        source, i + 1, line[..(literal.Index + 1)] + expected + line[(literal.Index + literal.Length - 1)..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> CharUsedAsDigit(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var java = IsJava(source);
        var texts = Declared(masked, @"\b(?:string|String)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]")
            .Union(Declared(masked, @"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?:""|Console\s*\.\s*ReadLine\s*\()"))
            .ToHashSet();
        var characters = Declared(masked, @"\bchar\s+(?<name>[A-Za-z_]\w*)(?:\s*[=;,:)]|\s+in\b)")
            .Union(masked
                .SelectMany(line => Regex.Matches(line, @"\bforeach\s*\(\s*var\s+(?<name>[A-Za-z_]\w*)\s+in\s+(?<text>[A-Za-z_]\w*)\s*\)"))
                .Where(match => texts.Contains(match.Groups["text"].Value))
                .Select(match => match.Groups["name"].Value))
            .ToHashSet();
        var wholeNumbers = Declared(masked, @"\b(?:int|long)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]");

        var character = java
            ? @"(?<expression>[A-Za-z_]\w*\s*\.\s*charAt\s*\([^()]*\)|[A-Za-z_]\w*)"
            : @"(?<expression>(?<text>[A-Za-z_]\w*)\s*\[[^\[\]]+\]|[A-Za-z_]\w*)";

        bool IsCharacter(Match match)
        {
            var expression = match.Groups["expression"].Value;
            if (java) return expression.Contains("charAt", StringComparison.Ordinal) || characters.Contains(expression);
            return match.Groups["text"].Success ? texts.Contains(match.Groups["text"].Value) : characters.Contains(expression);
        }

        var converter = java ? @"Integer\s*\.\s*valueOf" : @"Convert\s*\.\s*ToInt(?:32|64)";

        for (var i = 0; i < masked.Count; i++)
        {
            var line = source.Lines[i];

            if (Regex.Match(masked[i], $@"(?<call>{converter}\s*\(\s*{character}\s*\))") is { Success: true } converted && IsCharacter(converted))
            {
                var expression = line.Substring(converted.Groups["expression"].Index, converted.Groups["expression"].Length);
                var call = converted.Groups["call"];

                yield return new LogicFinding(id, i + 1,
                    $"`{expression}` is a character, and converting a character gives its character code - '7' becomes 55, not 7",
                    Replace(id, $"Subtract '0' to get the digit's value: ({expression} - '0')",
                        "Characters are stored as numbers, and the digits '0' to '9' are numbered one after another. So subtracting '0' from a digit " +
                        "character leaves its value: '7' - '0' is 7.",
                        source, i + 1, line[..call.Index] + $"({expression} - '0')" + line[(call.Index + call.Length)..]));
                continue;
            }

            var addition = Regex.Match(masked[i], $@"^\s*(?<total>[A-Za-z_]\w*)\s*\+=\s*{character}\s*;");
            if (!addition.Success || !wholeNumbers.Contains(addition.Groups["total"].Value) || !IsCharacter(addition)) continue;

            var group = addition.Groups["expression"];
            var digit = line.Substring(group.Index, group.Length);

            yield return new LogicFinding(id, i + 1,
                $"`{digit}` is a character, so adding it adds its character code - each '7' adds 55, not 7",
                Replace(id, $"Subtract '0' to add the digit's value: {addition.Groups["total"].Value} += {digit} - '0';",
                    "Characters are stored as numbers, and the digits '0' to '9' are numbered one after another. So subtracting '0' from a digit " +
                    "character leaves its value: '7' - '0' is 7.",
                    source, i + 1, line[..(group.Index + group.Length)] + " - '0'" + line[(group.Index + group.Length)..]));
        }
    }

    private static IEnumerable<LogicFinding> CountFromMissingKey(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var java = IsJava(source);
        var counters = java
            ? Declared(masked, @"\b(?:Map|HashMap|TreeMap|LinkedHashMap)\s*<\s*[\w<>]+\s*,\s*(?:Integer|Long)\s*>\s+(?<name>[A-Za-z_]\w*)")
            : Declared(masked, @"\b(?:Dictionary|SortedDictionary)\s*<\s*[\w<>?]+\s*,\s*(?:int|long)\s*>\s+(?<name>[A-Za-z_]\w*)")
                .Union(Declared(masked, @"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?:Dictionary|SortedDictionary)\s*<\s*[\w<>?]+\s*,\s*(?:int|long)\s*>"))
                .ToHashSet();

        for (var i = 0; i < masked.Count; i++)
        {
            var pattern = java
                ? @"^(?<lead>\s*)(?<map>[A-Za-z_]\w*)\s*\.\s*put\s*\(\s*(?<key>[^,()]+(?:\([^()]*\))?)\s*,\s*\k<map>\s*\.\s*get\s*\(\s*\k<key>\s*\)\s*\+\s*(?<step>[^;]+?)\s*\)\s*;"
                : @"^(?<lead>\s*)(?<map>[A-Za-z_]\w*)\s*\[\s*(?<key>[^\[\]]+?)\s*\]\s*(?:(?<increment>\+\+)|\+=\s*(?<step>[^;]+?)|=\s*\k<map>\s*\[\s*\k<key>\s*\]\s*\+\s*(?<step>[^;]+?))\s*;";

            var match = Regex.Match(masked[i], pattern);
            if (!match.Success || !counters.Contains(match.Groups["map"].Value)) continue;

            var map = match.Groups["map"].Value;
            var guarded = $@"\b{Regex.Escape(map)}\s*\.\s*(?:containsKey|putIfAbsent|getOrDefault|ContainsKey|TryGetValue|TryAdd|GetValueOrDefault)\s*\(";
            if (masked.Skip(Math.Max(0, i - 6)).Take(Math.Min(i, 6) + 1).Any(line => Regex.IsMatch(line, guarded))) continue;

            var line = source.Lines[i];
            var key = line.Substring(match.Groups["key"].Index, match.Groups["key"].Length);
            var step = match.Groups["increment"].Success ? "1" : line.Substring(match.Groups["step"].Index, match.Groups["step"].Length);
            var lead = match.Groups["lead"].Value;
            var rest = line[(match.Index + match.Length)..];

            yield return java
                ? new LogicFinding(id, i + 1,
                    $"the first time a key is counted, `{map}.get({key})` is null, and adding to null throws a NullPointerException",
                    Replace(id, $"Start from 0 for a new key: {map}.getOrDefault({key}, 0)",
                        "A map has no entry for a key until it is put there, so `get` returns null the first time each key turns up, and Java cannot " +
                        "add a number to null. `getOrDefault(key, 0)` gives 0 for a key that is not there yet, so the first one is counted as 1.",
                        source, i + 1, lead + $"{map}.put({key}, {map}.getOrDefault({key}, 0) + {step});" + rest))
                : new LogicFinding(id, i + 1,
                    $"the first time a key is counted it is not in `{map}` yet, so `{map}[{key}]` throws a KeyNotFoundException",
                    Replace(id, $"Start from 0 for a new key: {map}.GetValueOrDefault({key})",
                        "Reading a dictionary with [ ] throws when the key is missing, and every key is missing the first time it is counted. " +
                        "`GetValueOrDefault` gives 0 for a key that is not there yet, and the assignment then adds it.",
                        source, i + 1, lead + $"{map}[{key}] = {map}.GetValueOrDefault({key}) + {step};" + rest));
        }
    }

    private static IEnumerable<LogicFinding> CollectionPrinted(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var java = IsJava(source);
        var arrays = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            foreach (Match m in Regex.Matches(line, @"\b[A-Za-z_][\w<>]*\s*(?<dimensions>(?:\[\s*\])+)\s*(?<name>[A-Za-z_]\w*)\s*[=;,)]"))
                arrays[m.Groups["name"].Value] = m.Groups["dimensions"].Value.Count(c => c == '[');
            foreach (Match m in Regex.Matches(line, @"\b[A-Za-z_]\w*\s+(?<name>[A-Za-z_]\w*)\s*(?<dimensions>(?:\[\s*\])+)\s*[=;,)]"))
                arrays[m.Groups["name"].Value] = m.Groups["dimensions"].Value.Count(c => c == '[');
            if (!java)
            {
                foreach (Match m in Regex.Matches(line, @"\b(?:List|HashSet|Queue|Stack)\s*<[^<>]+>\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]"))
                    arrays[m.Groups["name"].Value] = 1;
                foreach (Match m in Regex.Matches(line, @"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?:(?:List|HashSet|Queue|Stack)\s*<[^<>]+>\s*[({]|[\w]*\s*\[\s*\w*\s*\]\s*[{;(]|\[\s*\]\s*\{)"))
                    arrays[m.Groups["name"].Value] = 1;
            }
        }

        if (arrays.Count == 0) yield break;

        var imported = masked.Any(line => Regex.IsMatch(line, @"^\s*import\s+java\.util\.(?:Arrays|\*)\s*;"));
        var printCall = java ? @"\bSystem\s*\.\s*out\s*\.\s*print(?:ln)?\s*\(" : @"\bConsole\s*\.\s*Write(?:Line)?\s*\(";

        for (var i = 0; i < masked.Count; i++)
        {
            var call = Regex.Match(masked[i], printCall);
            if (!call.Success || Brackets.ClosingParenthesis(masked[i], call.Index + call.Length - 1) is not { } close) continue;

            var line = source.Lines[i];
            var start = call.Index + call.Length;
            var arguments = masked[i][start..close];
            bool TopLevel(int index) => arguments[..index].Count(c => c == '(') == arguments[..index].Count(c => c == ')');

            var printed = Regex.Matches(arguments, @"(?<![\w.""$])(?<name>[A-Za-z_]\w*)(?![\w\[.(])")
                .Where(m => TopLevel(m.Index) && arrays.TryGetValue(m.Groups["name"].Value, out var depth) && (java || depth == 1))
                .ToList();

            var holes = java || !line[start..close].TrimStart().StartsWith("$\"", StringComparison.Ordinal)
                ? []
                : Regex.Matches(line[start..close], @"(?<!\{)\{(?<name>[A-Za-z_]\w*)\}")
                    .Where(m => arrays.TryGetValue(m.Groups["name"].Value, out var depth) && depth == 1)
                    .ToList();

            if (printed.Count == 0 && holes.Count == 0) continue;

            string Shown(string name) =>
                java
                    ? $"{(imported ? "" : "java.util.")}Arrays.{(arrays[name] > 1 ? "deepToString" : "toString")}({name})"
                    : $"string.Join(\", \", {name})";

            var replacements = printed.Select(m => (Index: start + m.Index, m.Length, Text: Shown(m.Groups["name"].Value)))
                .Concat(holes.Select(m => (Index: start + m.Index + 1, Length: m.Length - 2, Text: Shown(m.Groups["name"].Value))))
                .OrderByDescending(r => r.Index)
                .ToList();

            var fixedLine = line;
            foreach (var (index, length, text) in replacements) fixedLine = fixedLine[..index] + text + fixedLine[(index + length)..];

            var name = (printed.Count > 0 ? printed[0] : holes[0]).Groups["name"].Value;
            var shown = Shown(name);

            yield return new LogicFinding(id, i + 1,
                java
                    ? $"printing the array `{name}` prints its type and address - something like [I@1b6d3586 - not what is in it"
                    : $"printing `{name}` prints the name of its type, such as System.Int32[], rather than what is in it",
                Replace(id, $"Print what is in it: {shown}",
                    java
                        ? "An array does not know how to turn itself into text, so Java prints its type code and where it is in memory. " +
                          $"`Arrays.{(arrays[name] > 1 ? "deepToString" : "toString")}` builds text from the items instead: [1, 2, 3]."
                        : "Arrays and lists in C# do not turn their items into text by themselves - printing one prints its type name. " +
                          "`string.Join(\", \", ...)` joins the items with a comma between each: 1, 2, 3.",
                    source, i + 1, fixedLine));
        }
    }

    private static IEnumerable<LogicFinding> LocalHidesField(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var depths = Brackets.BraceDepths(masked);
        var notTypes = new HashSet<string>(StringComparer.Ordinal) { "return", "throw", "new", "else", "case", "yield", "await", "goto", "using" };

        for (var c = 0; c < masked.Count; c++)
        {
            var declaration = Regex.Match(masked[c], @"\bclass\s+(?<name>[A-Z]\w*)");
            if (!declaration.Success || BraceBlocks.Body(masked, c) is not { } classBody || classBody.End <= classBody.First) continue;

            var className = declaration.Groups["name"].Value;
            var memberDepth = depths[classBody.First];
            var members = Enumerable.Range(classBody.First, classBody.End - classBody.First).Where(i => depths[i] == memberDepth).ToList();

            var fields = members
                .Select(i => Regex.Match(masked[i], @"^\s*(?:(?:private|protected|public|internal|static|final|readonly)\s+)*(?<type>[\w<>\[\],.?]+)\s+(?<name>[A-Za-z_]\w*)\s*(?:=[^;]*)?;\s*$"))
                .Where(m => m.Success && !notTypes.Contains(m.Groups["type"].Value))
                .Select(m => m.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);
            if (fields.Count == 0) continue;

            foreach (var header in members.Where(i => Regex.IsMatch(masked[i], $@"^\s*(?:(?:public|private|protected|internal)\s+)?{className}\s*\(")))
            {
                if (BraceBlocks.Body(masked, header) is not { } body) continue;

                for (var j = body.First; j < body.End; j++)
                {
                    var local = Regex.Match(masked[j], @"^(?<lead>\s*)(?:final\s+)?(?<type>[\w<>\[\],.?]+)\s+(?<name>[A-Za-z_]\w*)\s*=[^=]");
                    if (!local.Success || notTypes.Contains(local.Groups["type"].Value) || !fields.Contains(local.Groups["name"].Value)) continue;

                    var name = local.Groups["name"].Value;
                    var setLater = Enumerable.Range(j + 1, body.End - j - 1).Any(k => Regex.IsMatch(masked[k], $@"\bthis\s*\.\s*{Regex.Escape(name)}\s*=[^=]"));
                    if (setLater) continue;

                    var line = source.Lines[j];
                    var typeStart = local.Groups["lead"].Length;

                    yield return new LogicFinding(id, j + 1,
                        $"`{local.Groups["type"].Value} {name} = ...` makes a new local variable called `{name}`, which hides the field - the field this " +
                        "constructor was meant to set keeps its default value",
                        Replace(id, $"Set the field: this.{name} = ...",
                            $"Putting a type in front of `{name}` declares a new variable that only exists inside the constructor. The object's own " +
                            $"`{name}` is never touched, so it stays null, 0 or false. `this.{name}` names the field.",
                            source, j + 1, line[..typeStart] + "this." + line[local.Groups["name"].Index..]));
                }
            }
        }
    }

    private static IEnumerable<LogicFinding> RemoveWhileCountingUp(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var java = IsJava(source);

        for (var i = 0; i < masked.Count; i++)
        {
            var header = Regex.Match(masked[i],
                @"\bfor\s*\(\s*int\s+(?<index>[A-Za-z_]\w*)\s*=\s*0\s*;\s*\k<index>\s*<\s*(?<list>[A-Za-z_]\w*)\s*\.\s*(?:size\s*\(\s*\)|Count)\s*;\s*(?:\k<index>\s*\+\+|\+\+\s*\k<index>|\k<index>\s*\+=\s*1)\s*\)");
            if (!header.Success || BraceBlocks.Body(masked, i) is not { } body) continue;

            var index = Regex.Escape(header.Groups["index"].Value);
            var list = header.Groups["list"].Value;
            var text = BraceBlocks.Text(masked, body.First, body.End);

            var removes = java ? $@"\b{Regex.Escape(list)}\s*\.\s*remove\s*\(\s*{index}\s*\)" : $@"\b{Regex.Escape(list)}\s*\.\s*RemoveAt\s*\(\s*{index}\s*\)";
            if (!Regex.IsMatch(text, removes)) continue;
            if (Regex.IsMatch(text, $@"\b{index}\s*--|--\s*{index}\b|\b{index}\s*-=|\bbreak\s*;|\breturn\b")) continue;

            var line = source.Lines[i];
            var open = masked[i].IndexOf('(', header.Index);
            if (Brackets.ClosingParenthesis(masked[i], open) is not { } close) continue;

            var name = header.Groups["index"].Value;
            var size = java ? $"{list}.size()" : $"{list}.Count";

            yield return new LogicFinding(id, i + 1,
                $"removing position `{name}` moves every later item down one place, and then `{name}` moves up one - so the item just after each one removed is never checked",
                Replace(id, $"Count down from the end: for (int {name} = {size} - 1; {name} >= 0; {name}--)",
                    $"After `{list}` loses the item at `{name}`, the next item slides into position `{name}`, but the loop has already moved on to " +
                    $"`{name} + 1`. Two matching items side by side leave the second one behind. Going from the end towards the start means " +
                    "that removing an item only moves items the loop has already looked at.",
                    source, i + 1, line[..header.Index] + $"for (int {name} = {size} - 1; {name} >= 0; {name}--)" + line[(close + 1)..]));
        }
    }

    private static IEnumerable<LogicFinding> LoopCopyAssigned(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var cpp = IsCpp(source);
        var header = Path.GetExtension(source.Path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs"
            ? @"\bfor\s*\(\s*(?:let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s+of\s+(?<items>[^)]+)\)"
            : @"\bfor\s*\(\s*(?:final\s+|const\s+)?(?<type>[\w:<>\[\],.?]+)\s+(?<name>[A-Za-z_]\w*)\s*:\s*(?<items>[^):][^)]*)\)";

        for (var i = 0; i < masked.Count; i++)
        {
            var loop = Regex.Match(masked[i], header);
            if (!loop.Success || BraceBlocks.Body(masked, i) is not { } body) continue;

            var name = loop.Groups["name"].Value;
            var word = Word(name);

            for (var j = body.First; j < body.End; j++)
            {
                if (!Regex.IsMatch(masked[j], $@"^\s*{Regex.Escape(name)}\s*(?:[+\-*/%]?=(?!=)|\+\+|--)|^\s*(?:\+\+|--)\s*{Regex.Escape(name)}\b")) continue;
                if (Enumerable.Range(j + 1, body.End - j - 1).Any(k => Regex.IsMatch(masked[k], word))) break;

                var readByInnerLoop = Enumerable.Range(body.First, j - body.First).Any(k =>
                    Regex.IsMatch(masked[k], @"\b(?:while|for)\b") && Regex.IsMatch(masked[k], word) &&
                    BraceBlocks.Body(masked, k) is { } inner && inner.First <= j && j < inner.End);
                if (readByInnerLoop) break;

                var items = loop.Groups["items"].Value.Trim();
                var message = $"`{name}` is a copy of each item in `{items}`, so changing `{name}` changes only the copy - `{items}` keeps its old values";

                if (cpp && loop.Groups["type"].Success)
                {
                    var type = loop.Groups["type"];
                    var line = source.Lines[i];
                    yield return new LogicFinding(id, j + 1, message,
                        Replace(id, $"Loop by reference: for ({type.Value}& {name} : {items})",
                            $"Without `&`, `{name}` is a separate copy made for each pass. With `&` it is the item itself, so changing it changes `{items}`.",
                            source, i + 1, line[..(type.Index + type.Length)] + "&" + line[(type.Index + type.Length)..]));
                }
                else
                {
                    yield return new LogicFinding(id, j + 1, message, null);
                }

                break;
            }
        }
    }

    [GeneratedRegex(@"\bfor\s*\(\s*(?:(?:int|long|short|var|let|auto|size_t|unsigned(?:\s+int)?)\s+)?(?<index>[A-Za-z_$][\w$]*)\s*=\s*(?<start>[^;]+?)\s*;\s*\k<index>\s*(?<compare><=?|>=?)\s*(?<end>[^;]+?)\s*;\s*(?<step>\k<index>\s*(?:\+\+|--)|(?:\+\+|--)\s*\k<index>|\k<index>\s*[+-]=\s*\d+)\s*\)")]
    private static partial Regex CountingLoop();

    private static IEnumerable<LogicFinding> LoopStepsAway(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var loop = CountingLoop().Match(masked[i]);
            if (!loop.Success || Regex.IsMatch(loop.Groups["end"].Value, @"&&|\|\|")) continue;

            var step = loop.Groups["step"];
            var up = step.Value.Contains("++", StringComparison.Ordinal) || step.Value.Contains("+=", StringComparison.Ordinal);
            var needsUp = loop.Groups["compare"].Value.StartsWith('<');
            if (up == needsUp) continue;

            var line = source.Lines[i];
            var written = line.Substring(step.Index, step.Length);
            var turned = up
                ? written.Replace("++", "--", StringComparison.Ordinal).Replace("+=", "-=", StringComparison.Ordinal)
                : written.Replace("--", "++", StringComparison.Ordinal).Replace("-=", "+=", StringComparison.Ordinal);
            var index = loop.Groups["index"].Value;

            yield return new LogicFinding(id, i + 1,
                $"`{index}` has to stay {loop.Groups["compare"].Value} {loop.Groups["end"].Value} for the loop to go on, but `{written}` moves it " +
                $"{(up ? "up" : "down")}, away from there - the loop never reaches its end",
                Replace(id, $"Step the other way: {turned}",
                    $"A counting loop ends when its condition turns false. `{index} {loop.Groups["compare"].Value} {loop.Groups["end"].Value}` only turns " +
                    $"false once `{index}` has {(needsUp ? "grown" : "shrunk")} far enough, so the step has to move it {(needsUp ? "up" : "down")}.",
                    source, i + 1, line[..step.Index] + turned + line[(step.Index + step.Length)..]));
        }
    }
}
