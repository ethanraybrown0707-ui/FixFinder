using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>More of the mistakes Java, C#, JavaScript, C and C++ programs make without failing to compile.</summary>
public static partial class CLikeReviewPatterns
{
    private static readonly IReadOnlySet<string> Java = CodePattern.Files(".java");
    private static readonly IReadOnlySet<string> CSharp = CodePattern.Files(".cs");
    private static readonly IReadOnlySet<string> Script = CodePattern.Files(".js", ".mjs", ".cjs");
    private static readonly IReadOnlySet<string> Native = CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++");
    private static readonly IReadOnlySet<string> Objects = CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs", ".cpp", ".cc", ".cxx", ".c++");
    private static readonly IReadOnlySet<string> Braced = CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs", ".c", ".cpp", ".cc", ".cxx", ".c++");
    private static readonly IReadOnlySet<string> Loosely = CodePattern.Files(".js", ".mjs", ".cjs", ".c", ".cpp", ".cc", ".cxx", ".c++");
    private static readonly IReadOnlySet<string> Managed = CodePattern.Files(".java", ".cs");

    private static CodePattern Pattern(
        string id, IReadOnlySet<string> files, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely, FindingKind kind = FindingKind.Logic) =>
        new(id, files, Syntax.CLike, find, severity, confidence, kind);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-self-assignment", Objects, SelfAssignment, confidence: Confidence.Certain),
        Pattern("logic-lost-increment", Braced, LostIncrement, confidence: Confidence.Certain),
        Pattern("logic-off-by-one-length", Braced, OffByOneLength, Severity.Error),
        Pattern("logic-or-constant", Loosely, OrConstant, Severity.Error, Confidence.Certain),
        Pattern("logic-duplicate-condition", Braced, DuplicateCondition),
        Pattern("logic-empty-catch", CodePattern.Files(".java", ".cs", ".js", ".mjs", ".cjs", ".cpp", ".cc", ".cxx", ".c++"), EmptyCatch),
        Pattern("logic-modified-while-looping", Managed, ModifiedWhileLooping, Severity.Error),
        Pattern("logic-float-equality", CodePattern.Files(".java", ".cs", ".c", ".cpp", ".cc", ".cxx", ".c++"), FloatEquality, Severity.Suggestion, Confidence.Possible),
        Pattern("logic-java-scanner-skips-line", Java, ScannerSkipsLine),
        Pattern("logic-java-random-always-zero", Java, RandomAlwaysZero, Severity.Error, Confidence.Certain),
        Pattern("logic-java-wrapper-equality", Java, WrapperEquality),
        Pattern("logic-java-misspelt-override", Java, MisspeltObjectMethod),
        Pattern("logic-java-equals-overload", Java, EqualsOverload),
        Pattern("logic-java-chars-added", Java, CharsAdded),
        Pattern("logic-string-built-in-loop", Managed, StringBuiltInLoop, Severity.Suggestion, Confidence.Possible, FindingKind.Style),
        Pattern("logic-csharp-async-void", CSharp, AsyncVoid),
        Pattern("logic-csharp-property-calls-itself", CSharp, PropertyCallsItself, Severity.Error, Confidence.Certain),
        Pattern("logic-csharp-parse-unchecked", CSharp, ParseUnchecked, Severity.Suggestion, Confidence.Likely, FindingKind.Style),
        Pattern("logic-js-loose-equality", Script, LooseEquality, Severity.Suggestion, Confidence.Likely, FindingKind.Style),
        Pattern("logic-js-template-in-quotes", Script, TemplateInQuotes),
        Pattern("logic-js-compare-with-new-array", Script, CompareWithNewArray, Severity.Error, Confidence.Certain),
        Pattern("logic-nan-comparison", CodePattern.Files(".js", ".mjs", ".cjs", ".java", ".cs"), NaNComparison, Severity.Error, Confidence.Certain),
    ];

    private static LocalFix Replace(string id, string title, string explanation, SourceFile source, int line, string text) =>
        LocalFix.ReplaceLine(id, title, explanation, source.Path, line, text);

    private static string Extension(SourceFile source) => Path.GetExtension(source.Path).ToLowerInvariant();

    private static bool IsScript(SourceFile source) => Extension(source) is ".js" or ".mjs" or ".cjs";

    // ------------------------------------------------------------------ name = name;

    private static IEnumerable<LogicFinding> SelfAssignment(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var self = Extension(source) is ".cpp" or ".cc" or ".cxx" or ".c++" ? "this->" : "this.";

        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*\k<name>\s*;\s*$") is not { Success: true } match) continue;

            var name = match.Groups["name"].Value;
            var parameters = BraceBlocks.EnclosingParameters(masked, i);
            var line = source.Lines[i];

            yield return new LogicFinding(id, i + 1,
                $"`{name} = {name};` sets `{name}` to itself, so nothing changes" +
                (parameters.Contains(name) ? $" - the parameter `{name}` hides the field of the same name, and the field is never set" : ""),
                parameters.Contains(name)
                    ? Replace(id, $"Set the field from the parameter: {self}{name} = {name};",
                        $"Inside this method `{name}` means the parameter, which hides the field with the same name, so `{name} = {name};` copies " +
                        $"the parameter onto itself. `{self}{name}` names the field, so `{self}{name} = {name};` stores the value where it was meant to go.",
                        source, i + 1, $"{match.Groups["lead"].Value}{self}{name} = {name};{CodeText.SplitComment(line, Syntax.CLike).Tail}")
                    : null);
        }
    }

    // ------------------------------------------------------------------ i = i++;

    private static IEnumerable<LogicFinding> LostIncrement(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^(?<lead>\s*)(?<name>[A-Za-z_][\w.\[\]]*)\s*=\s*\k<name>\s*(?<op>\+\+|--)\s*;\s*$") is not { Success: true } match) continue;

            var name = match.Groups["name"].Value;
            var op = match.Groups["op"].Value;

            yield return new LogicFinding(id, i + 1,
                $"`{name} = {name}{op};` works out the old value, changes `{name}`, then stores the old value back - so `{name}` never changes",
                Replace(id, $"Just change it: {name}{op};",
                    $"`{name}{op}` gives back the value from before the change. Assigning that to `{name}` undoes the change it just made. `{name}{op};` " +
                    "on its own is all that is needed.",
                    source, i + 1, $"{match.Groups["lead"].Value}{name}{op};"));
        }
    }

    // ------------------------------------------------------------------ for (i = 0; i <= items.length; i++)

    [GeneratedRegex(@"^(?<head>\s*for\s*\([^;]*;\s*(?<index>[A-Za-z_]\w*)\s*)(?<op><=)(?<bound>\s*(?<collection>[A-Za-z_][\w.]*?)\s*\.\s*(?<size>length|Length|Count|size\s*\(\s*\)|count\s*\(\s*\))\s*;)")]
    private static partial Regex LengthLoop();

    [GeneratedRegex(@"^(?<head>\s*for\s*\([^;]*;\s*(?<index>[A-Za-z_]\w*)\s*)(?<op><=)(?<bound>\s*(?<size>[A-Za-z_]\w*|\d+)\s*;)")]
    private static partial Regex NumberLoop();

    private static IEnumerable<LogicFinding> OffByOneLength(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var arraySizes = masked
            .SelectMany(l => Regex.Matches(l, @"\b(?<name>[A-Za-z_]\w*)\s*\[\s*(?<size>[A-Za-z_]\w*|\d+)\s*\]\s*(?:=|;|,)"))
            .GroupBy(m => m.Groups["name"].Value)
            .ToDictionary(g => g.Key, g => g.First().Groups["size"].Value);

        for (var i = 0; i < masked.Count; i++)
        {
            var loop = LengthLoop().Match(masked[i]);
            string collection;

            if (loop.Success)
            {
                collection = loop.Groups["collection"].Value;
            }
            else if (NumberLoop().Match(masked[i]) is { Success: true } numbered &&
                     arraySizes.FirstOrDefault(pair => pair.Value == numbered.Groups["size"].Value).Key is { } array)
            {
                loop = numbered;
                collection = array;
            }
            else
            {
                continue;
            }

            if (BraceBlocks.Body(masked, i) is not var (first, end)) continue;

            var index = Regex.Escape(loop.Groups["index"].Value);
            var c = Regex.Escape(collection);
            var body = BraceBlocks.Text(masked, first, end) + "\n" + masked[i][(loop.Index + loop.Length)..];

            if (!Regex.IsMatch(body, $@"{c}\s*(?:\[\s*{index}\s*\]|\.\s*(?:get|charAt|at|ElementAt)\s*\(\s*{index}\s*\))")) continue;

            var line = source.Lines[i];
            var op = loop.Groups["op"];

            yield return new LogicFinding(id, i + 1,
                $"the loop runs while `{loop.Groups["index"].Value} <=` the size of `{collection}`, so its last pass reads one past the end",
                Replace(id, "Stop before the size: < instead of <=",
                    $"Positions run from 0 to one less than the size, so a loop that goes up to and including the size reads a position that does " +
                    $"not exist on its last pass - which crashes in Java and C#, gives undefined in JavaScript, and reads other memory in C.",
                    source, i + 1, line[..op.Index] + "<" + line[(op.Index + op.Length)..]));
        }
    }

    // ------------------------------------------------------------------ if (c == 'y' || 'Y')

    private const string Literal = @"(?:""[^""]*""|'[^']*'|-?\d+(?:\.\d+)?)";

    [GeneratedRegex(@"(?<![\w.])(?<left>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\s*(?<op>===|==|!==|!=)\s*(?<first>" + Literal + @")(?<rest>(?:\s*(?<join>\|\||&&)\s*" + Literal + @")+)(?=\s*[)?;,]|\s*(?:\|\||&&))")]
    private static partial Regex OrConstantRegex();

    private static IEnumerable<LogicFinding> OrConstant(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (OrConstantRegex().Match(masked[i]) is not { Success: true } match) continue;

            var joins = match.Groups["join"].Captures.Select(c => c.Value).Distinct().ToList();
            var op = match.Groups["op"].Value;
            if (joins.Count != 1 || (op.StartsWith('=') && joins[0] == "&&")) continue;

            var line = source.Lines[i];
            var left = line.Substring(match.Groups["left"].Index, match.Groups["left"].Length);
            var restText = line.Substring(match.Groups["rest"].Index, match.Groups["rest"].Length);
            var values = new[] { line.Substring(match.Groups["first"].Index, match.Groups["first"].Length) }
                .Concat(Regex.Matches(restText, @"(?:\|\||&&)\s*(?<value>" + Literal + ")").Select(m => m.Groups["value"].Value))
                .ToList();

            var join = op.StartsWith('=') ? "||" : "&&";
            var replacement = string.Join($" {join} ", values.Select(v => $"{left} {op} {v}"));
            var written = line.Substring(match.Index, match.Length);

            yield return new LogicFinding(id, i + 1,
                $"`{written}` does not compare `{left}` with {values[1]} - {values[1]} on its own always counts as true, so the condition is always " +
                (joins[0] == "||" ? "true" : "decided by the first comparison alone"),
                Replace(id, $"Compare {left} with each value: {replacement}",
                    $"`{written}` is read as `({left} {op} {values[0]}) {joins[0]} {values[1]}`, and a non-zero number or a string on its own is " +
                    $"true. Each value needs its own comparison with `{left}`.",
                    source, i + 1, line[..match.Index] + replacement + line[(match.Index + match.Length)..]));
        }
    }

    // ------------------------------------------------------------------ if (x > 5) ... else if (x > 5)

    private static IEnumerable<LogicFinding> DuplicateCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var chains = new Dictionary<int, Dictionary<string, int>>();

        for (var i = 0; i < masked.Count; i++)
        {
            var text = masked[i];
            var elseIf = Regex.Match(text, @"^\s*\}?\s*else\s+if\s*\(");
            var plainIf = Regex.Match(text, @"^\s*if\s*\(");
            var indent = CodeText.Indentation(text).Length;

            if (!elseIf.Success && !plainIf.Success)
            {
                if (text.Trim().Length > 0 && !Regex.IsMatch(text, @"^\s*\}?\s*else\b") && !text.TrimStart().StartsWith('}'))
                    chains.Remove(indent);

                continue;
            }

            var open = text.IndexOf('(', (elseIf.Success ? elseIf : plainIf).Index + (elseIf.Success ? elseIf : plainIf).Length - 1);
            if (Brackets.ClosingParenthesis(text, open) is not { } close) continue;

            var condition = Regex.Replace(source.Lines[i][(open + 1)..close], @"\s+", "");

            if (plainIf.Success)
            {
                chains[indent] = new Dictionary<string, int> { [condition] = i };
                continue;
            }

            if (!chains.TryGetValue(indent, out var seen)) continue;

            if (seen.TryGetValue(condition, out var earlier))
            {
                yield return new LogicFinding(id, i + 1,
                    $"this `else if` checks the same thing as line {earlier + 1}, so whenever it is true the earlier branch has already run and this one never does",
                    null);
            }
            else
            {
                seen[condition] = i;
            }
        }
    }

    // ------------------------------------------------------------------ catch (Exception e) { }

    private static IEnumerable<LogicFinding> EmptyCatch(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var language = Extension(source);

        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"^(?<lead>\s*\}?\s*)catch\s*(?:\((?<what>[^)]*)\))?\s*(?<open>\{)?\s*(?<closed>\})?\s*$");
            if (!match.Success) continue;

            var opening = i;
            if (!match.Groups["open"].Success)
            {
                opening = BraceBlocks.NextCode(masked, i + 1);
                if (opening < 0 || !Regex.IsMatch(masked[opening], @"^\s*\{\s*\}?\s*$")) continue;
            }

            int closing;
            if (match.Groups["closed"].Success || masked[opening].Trim() == "{}" || Regex.IsMatch(masked[opening], @"^\s*\{\s*\}\s*$"))
            {
                closing = opening;
            }
            else
            {
                closing = BraceBlocks.NextCode(masked, opening + 1);
                if (closing < 0 || masked[closing].Trim() != "}") continue;
                if (Enumerable.Range(opening + 1, closing - opening - 1).Any(k => source.Lines[k].Trim().Length > 0)) continue;
            }

            var what = match.Groups["what"].Success ? source.Lines[i].Substring(match.Groups["what"].Index, match.Groups["what"].Length).Trim() : "";
            var inner = CodeText.Indentation(source.Lines[i]) + BraceBlocks.IndentStep(source.Lines);
            var fix = CatchBody(language, what, match.Groups["lead"].Value, inner);

            if (fix is var (allmanHeader, _) && opening != i)
                fix = fix.Value with { Header = allmanHeader.TrimEnd('{').TrimEnd() + "\n" + CodeText.Indentation(source.Lines[i]) + "{" };

            var anything = what.Length == 0 || Regex.IsMatch(what, @"^(?:System\.)?(?:Exception|Throwable|Error)\b|^\.\.\.$") ||
                           (IsScript(source) && Regex.IsMatch(what, @"^[A-Za-z_$]\w*$"));

            yield return new LogicFinding(id, i + 1,
                "this `catch` catches an error and does nothing with it, so when something goes wrong the program carries on as if it had not",
                fix is var (header, body)
                    ? new LocalFix
                    {
                        RuleId = id,
                        Title = "Say what went wrong instead of ignoring it",
                        Explanation = "An empty catch block hides the error completely - the program goes on with whatever values it had, and nothing " +
                                      "tells you why the result is wrong. Printing the error at least shows that it happened and what it was.",
                        File = source.Path, StartLine = i + 1, RemoveCount = closing - i + 1,
                        NewLines = [.. header.Split('\n'), body, CodeText.Indentation(source.Lines[i]) + "}"],
                    }
                    : null)
            {
                Confidence = anything ? Confidence.Likely : Confidence.Possible,
            };
        }
    }

    private static (string Header, string Body)? CatchBody(string language, string what, string lead, string inner)
    {
        var variable = Regex.Match(what, @"\b(?<name>[A-Za-z_]\w*)\s*$") is { Success: true } named && what.Contains(' ') ? named.Groups["name"].Value : null;

        switch (language)
        {
            case ".java" when variable is not null:
                return ($"{lead}catch ({what}) {{", $"{inner}System.err.println(\"Something went wrong: \" + {variable}.getMessage());");

            case ".cs":
            {
                var type = what.Length == 0 ? "Exception" : what.Split(' ')[0];
                var name = variable ?? "ex";
                return ($"{lead}catch ({type} {name}) {{", $"{inner}Console.Error.WriteLine($\"Something went wrong: {{{name}.Message}}\");");
            }

            case ".js" or ".mjs" or ".cjs":
            {
                var name = what.Length > 0 ? what : "error";
                return ($"{lead}catch ({name}) {{", $"{inner}console.error(\"Something went wrong:\", {name});");
            }

            default:
                return null;
        }
    }

    // ------------------------------------------------------------------ for (String name : names) names.remove(name);

    private static IEnumerable<LogicFinding> ModifiedWhileLooping(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var csharp = Extension(source) == ".cs";

        for (var i = 0; i < masked.Count; i++)
        {
            var loop = csharp
                ? Regex.Match(masked[i], @"\bforeach\s*\(\s*[\w<>\[\],?\s]+?\s+(?<item>\w+)\s+in\s+(?<collection>[A-Za-z_][\w.]*)\s*\)")
                : Regex.Match(masked[i], @"\bfor\s*\(\s*(?:final\s+)?[\w<>\[\],?\s]+?\s+(?<item>\w+)\s*:\s*(?<collection>[A-Za-z_][\w.]*)\s*\)");
            if (!loop.Success || BraceBlocks.Body(masked, i) is not var (first, end)) continue;

            var c = Regex.Escape(loop.Groups["collection"].Value);
            var change = csharp ? @"(?:Remove|RemoveAt|Add|Insert|Clear|AddRange)" : @"(?:remove|add|clear|addAll|removeAll)";

            var changing = Enumerable.Range(first, Math.Max(0, end - first)).FirstOrDefault(k => Regex.IsMatch(masked[k], $@"(?<![\w.]){c}\s*\.\s*{change}\s*\("), -1);
            if (changing < 0) continue;

            var next = BraceBlocks.NextCode(masked, changing + 1);
            if (next >= 0 && next < end && Regex.IsMatch(masked[next], @"^\s*(?:break|return)\b")) continue;

            var collection = loop.Groups["collection"];
            var line = source.Lines[i];
            var copy = csharp ? $"{collection.Value}.ToList()" : $"new java.util.ArrayList<>({collection.Value})";

            yield return new LogicFinding(id, changing + 1,
                $"the loop changes `{collection.Value}` while it is looping over it, which {(csharp ? "throws InvalidOperationException" : "throws ConcurrentModificationException")} on the next pass",
                Replace(id, $"Loop over a copy: {copy}",
                    $"A {(csharp ? "foreach" : "for-each")} loop checks that the collection has not changed since it started, and stops with an exception when " +
                    $"it has. Looping over a copy made before the loop starts lets the loop change `{collection.Value}` itself.",
                    source, i + 1, line[..collection.Index] + copy + line[(collection.Index + collection.Length)..]));
        }
    }

    // ------------------------------------------------------------------ if (total == 0.3)

    private static IEnumerable<LogicFinding> FloatEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var reals = masked
            .SelectMany(l => Regex.Matches(l, @"\b(?:double|float|Double|Float)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]"))
            .Select(m => m.Groups["name"].Value)
            .ToHashSet();
        if (reals.Count == 0) yield break;

        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*(?<op>==|!=)\s*(?<value>\d*\.\d*[1-9]\d*)[fFdD]?(?![\w.])");
            if (!match.Success || !reals.Contains(match.Groups["name"].Value)) continue;

            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value;
            var abs = Extension(source) switch { ".java" => "Math.abs", ".cs" => "Math.Abs", _ => "fabs" };
            var close = match.Groups["op"].Value == "==" ? $"{abs}({name} - {value}) < 1e-9" : $"{abs}({name} - {value}) >= 1e-9";
            var line = source.Lines[i];

            yield return new LogicFinding(id, i + 1,
                $"`{name}` is a decimal number compared exactly with {value}, and decimal arithmetic is rarely exact - 0.1 + 0.2 is 0.30000000000000004",
                Extension(source) is ".java" or ".cs"
                    ? Replace(id, $"Compare within a small tolerance: {close}",
                        "Most decimal fractions cannot be stored exactly in binary, so sums and products land a tiny amount away from the value you " +
                        "expect. Checking that the difference is tiny, rather than zero, compares them the way you meant.",
                        source, i + 1, line[..match.Index] + close + line[(match.Index + match.Length)..])
                    : null);
        }
    }

    // ------------------------------------------------------------------ Java: nextInt() then nextLine()

    private static IEnumerable<LogicFinding> ScannerSkipsLine(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var scanners = masked.SelectMany(l => Regex.Matches(l, @"\b(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?:java\.util\.)?Scanner\s*\(\s*System\.in\s*\)"))
            .Select(m => m.Groups["name"].Value)
            .ToHashSet();

        foreach (var scanner in scanners)
        {
            var s = Regex.Escape(scanner);
            var reads = new List<(int Line, bool WholeLine)>();

            for (var i = 0; i < masked.Count; i++)
            {
                foreach (Match read in Regex.Matches(masked[i], $@"(?<![\w.]){s}\s*\.\s*(?<method>next(?:Int|Double|Long|Float|Short|Byte|Boolean|BigDecimal|BigInteger)?|nextLine)\s*\(\s*\)"))
                    reads.Add((i, read.Groups["method"].Value == "nextLine"));
            }

            for (var k = 1; k < reads.Count; k++)
            {
                var (before, beforeIsLine) = reads[k - 1];
                var (after, afterIsLine) = reads[k];

                if (beforeIsLine || !afterIsLine || before == after) continue;

                var lead = CodeText.Indentation(source.Lines[before]);

                yield return new LogicFinding(id, after + 1,
                    $"`{scanner}.nextLine()` straight after reading a number returns the rest of the number's line - usually nothing - instead of waiting for the next line",
                    LocalFix.Insert(id, $"Finish the number's line first: {scanner}.nextLine();",
                        $"Reading a number leaves the Enter typed after it waiting in the input. The next `nextLine()` reads up to that Enter, gets an " +
                        $"empty string, and the question it was meant to answer is skipped. Reading and throwing away the rest of the line straight " +
                        "after the number puts the next `nextLine()` at the start of the next line.",
                        source.Path, before + 2, [$"{lead}{scanner}.nextLine();"]));
            }
        }
    }

    // ------------------------------------------------------------------ Java: (int) Math.random() * 10

    private static IEnumerable<LogicFinding> RandomAlwaysZero(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"\(\s*int\s*\)\s*Math\.random\(\s*\)\s*\*\s*(?<scale>[\w.]+(?:\(\s*\))?)");
            if (!match.Success) continue;

            var line = source.Lines[i];
            var scale = match.Groups["scale"].Value;

            yield return new LogicFinding(id, i + 1,
                $"the cast turns `Math.random()` into 0 before it is multiplied, so the result is always 0",
                Replace(id, $"Multiply first, then cast: (int) (Math.random() * {scale})",
                    "A cast applies to the value straight after it. `(int) Math.random()` cuts a number between 0 and 1 down to 0, and 0 times anything " +
                    "is 0. Brackets around the multiplication make the cast apply to the result.",
                    source, i + 1, line[..match.Index] + $"(int) (Math.random() * {scale})" + line[(match.Index + match.Length)..]));
        }
    }

    // ------------------------------------------------------------------ Java: Integer a, b; a == b

    private static IEnumerable<LogicFinding> WrapperEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var wrappers = masked
            .SelectMany(l => Regex.Matches(l, @"\b(?:Integer|Long|Short|Byte|Character|Double|Float|Boolean)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]"))
            .Select(m => m.Groups["name"].Value)
            .ToHashSet();
        if (wrappers.Count < 2) yield break;

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (Match match in Regex.Matches(masked[i], @"(?<![\w.])(?<a>[A-Za-z_]\w*)\s*(?<op>==|!=)\s*(?<b>[A-Za-z_]\w*)(?![\w.(])"))
            {
                var (a, b) = (match.Groups["a"].Value, match.Groups["b"].Value);
                if (!wrappers.Contains(a) || !wrappers.Contains(b)) continue;

                var line = source.Lines[i];
                var equals = match.Groups["op"].Value == "==" ? $"{a}.equals({b})" : $"!{a}.equals({b})";

                yield return new LogicFinding(id, i + 1,
                    $"`{a}` and `{b}` are objects, so `{match.Value.Trim()}` compares whether they are the same object - which is only reliable for small numbers",
                    Replace(id, $"Compare the values: {equals}",
                        "Java keeps one shared object for each Integer from -128 to 127, so == happens to work for those and then fails for 128 and " +
                        "above. equals compares the numbers themselves.",
                        source, i + 1, line[..match.Index] + equals + line[(match.Index + match.Length)..]));

                break;
            }
        }
    }

    // ------------------------------------------------------------------ Java: public String tostring()

    private static readonly (string Right, string Pattern)[] ObjectMethods =
    [
        ("toString", @"public\s+String\s+(?<name>(?!toString\b)to_?[Ss]tring)\s*\(\s*\)"),
        ("hashCode", @"public\s+int\s+(?<name>(?!hashCode\b)hash_?[Cc]ode)\s*\(\s*\)"),
        ("equals", @"public\s+boolean\s+(?<name>(?!equals\b)[Ee]quals?)\s*\(\s*Object\b"),
    ];

    private static IEnumerable<LogicFinding> MisspeltObjectMethod(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var (right, pattern) in ObjectMethods)
            {
                if (Regex.Match(masked[i], pattern) is not { Success: true } match) continue;

                var line = source.Lines[i];
                var name = match.Groups["name"];

                yield return new LogicFinding(id, i + 1,
                    $"`{name.Value}` is not `{right}`, so Java never calls it - printing the object, or putting it in a HashMap, still uses the original `{right}`",
                    Replace(id, $"Name it {right} exactly",
                        $"Java calls `{right}` by that exact name, capitals included. A method with a different spelling is simply a new method nothing " +
                        $"calls. Adding `@Override` above it makes the compiler check the name.",
                        source, i + 1, line[..name.Index] + right + line[(name.Index + name.Length)..]));
            }
        }
    }

    // ------------------------------------------------------------------ Java: public boolean equals(Point other)

    private static IEnumerable<LogicFinding> EqualsOverload(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"^(?<lead>\s*)public\s+boolean\s+equals\s*\(\s*(?:final\s+)?(?<type>(?!Object\b)[A-Z][\w<>]*)\s+(?<name>[A-Za-z_]\w*)\s*\)\s*\{\s*$");
            if (!match.Success) continue;

            var type = match.Groups["type"].Value;
            var name = match.Groups["name"].Value;
            var lead = match.Groups["lead"].Value;
            var inner = lead + BraceBlocks.IndentStep(source.Lines);

            yield return new LogicFinding(id, i + 1,
                $"`equals({type} {name})` does not override Java's `equals(Object)` - it is a second method beside it, so HashSet, HashMap and List.contains never call it",
                new LocalFix
                {
                    RuleId = id,
                    Title = "Take an Object, and check its type inside",
                    Explanation = $"Java's collections call `equals(Object)`. A method taking a `{type}` has a different signature, so it is an overload that only " +
                                  $"runs when the compiler can see both sides are `{type}`. Taking an `Object` and checking it is a `{type}` makes it the real override.",
                    File = source.Path, StartLine = i + 1, RemoveCount = 1,
                    NewLines = [$"{lead}@Override", $"{lead}public boolean equals(Object object) {{", $"{inner}if (!(object instanceof {type} {name})) return false;"],
                });
        }
    }

    // ------------------------------------------------------------------ Java: System.out.println(first + second) with two chars

    private static IEnumerable<LogicFinding> CharsAdded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var chars = masked.SelectMany(l => Regex.Matches(l, @"\bchar\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]")).Select(m => m.Groups["name"].Value).ToHashSet();
        if (chars.Count == 0) yield break;

        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"System\.out\.print(?:ln)?\(\s*(?<a>[A-Za-z_]\w*|'[^']*')\s*\+\s*(?<b>[A-Za-z_]\w*|'[^']*')(?![\w.(])");
            if (!match.Success) continue;

            bool IsChar(Group g) => g.Value.StartsWith('\'') || chars.Contains(g.Value);
            if (!IsChar(match.Groups["a"]) || !IsChar(match.Groups["b"])) continue;

            var line = source.Lines[i];
            var at = match.Groups["a"].Index;

            yield return new LogicFinding(id, i + 1,
                $"adding two `char`s adds their character codes, so this prints a number instead of the two letters",
                Replace(id, "Start from a string so + joins them: \"\" + ...",
                    "A `char` is a number underneath - 'A' is 65 - and `+` between two of them adds the numbers. Starting the expression with a " +
                    "string makes every `+` after it join text instead.",
                    source, i + 1, line[..at] + "\"\" + " + line[at..]));
        }
    }

    // ------------------------------------------------------------------ result += text inside a loop

    private static IEnumerable<LogicFinding> StringBuiltInLoop(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var strings = masked.SelectMany(l => Regex.Matches(l, @"\b(?:String|string)\s+(?<name>[A-Za-z_]\w*)\s*=")).Select(m => m.Groups["name"].Value).ToHashSet();
        if (strings.Count == 0) yield break;

        var reported = new HashSet<string>();

        for (var i = 0; i < masked.Count; i++)
        {
            if (!Regex.IsMatch(masked[i], @"^\s*(?:for|while|foreach)\s*\(") || BraceBlocks.Body(masked, i) is not var (first, end)) continue;

            for (var k = first; k < end; k++)
            {
                var build = Regex.Match(masked[k], @"^\s*(?<name>[A-Za-z_]\w*)\s*(?:\+=|=\s*\k<name>\s*\+)");
                if (!build.Success || !strings.Contains(build.Groups["name"].Value) || !reported.Add(build.Groups["name"].Value)) continue;

                yield return new LogicFinding(id, k + 1,
                    $"`{build.Groups["name"].Value}` is built up with + inside a loop, which copies the whole text again on every pass",
                    null);
            }
        }
    }

    // ------------------------------------------------------------------ C#: async void Save()

    private static IEnumerable<LogicFinding> AsyncVoid(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"\basync\s+(?<void>void)\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)");
            if (!match.Success || Regex.IsMatch(match.Groups["parameters"].Value, @"\bobject\s+\w+\s*,\s*\w*EventArgs\b")) continue;

            var line = source.Lines[i];
            var at = match.Groups["void"];

            yield return new LogicFinding(id, i + 1,
                $"`{match.Groups["name"].Value}` is `async void`, so nothing can wait for it to finish and an exception inside it crashes the program",
                Replace(id, "Return a Task: async Task",
                    "An async method that returns Task can be awaited, so the caller knows when it has finished and sees any exception it throws. " +
                    "`async void` is only for event handlers, where nothing can await it anyway.",
                    source, i + 1, line[..at.Index] + "Task" + line[(at.Index + at.Length)..]));
        }
    }

    // ------------------------------------------------------------------ C#: public int Age { get { return Age; } }

    private static IEnumerable<LogicFinding> PropertyCallsItself(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i],
                @"^(?<head>\s*(?:(?:public|private|protected|internal|static|virtual|override)\s+)*[\w<>\[\],?.]+\s+(?<name>[A-Z]\w*)\s*)(?<accessors>\{\s*get\s*(?:\{\s*return\s+(?:this\.)?\k<name>\s*;\s*\}|=>\s*(?:this\.)?\k<name>\s*;)\s*(?:set\s*(?:\{\s*(?:this\.)?\k<name>\s*=\s*value\s*;\s*\}|=>\s*(?:this\.)?\k<name>\s*=\s*value\s*;)\s*)?\})\s*$");
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            var line = source.Lines[i];
            var accessors = match.Groups["accessors"];

            yield return new LogicFinding(id, i + 1,
                $"the property `{name}` reads itself to get its own value, which calls the property again, forever - the program crashes with a StackOverflowException",
                Replace(id, $"Let C# store it: {{ get; set; }}",
                    $"Inside the getter, `{name}` is the property itself, so getting it gets it again without end. An auto-property, `{{ get; set; }}`, " +
                    "has a hidden field of its own to keep the value in.",
                    source, i + 1, line[..accessors.Index] + "{ get; set; }" + line[(accessors.Index + accessors.Length)..]));
        }
    }

    // ------------------------------------------------------------------ C#: int.Parse(Console.ReadLine())

    private static IEnumerable<LogicFinding> ParseUnchecked(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"\b(?<type>int|double|decimal|long|float)\.Parse\(\s*Console\.ReadLine\(\)\s*!?\s*\)");
            if (!match.Success) continue;

            yield return new LogicFinding(id, i + 1,
                $"`{match.Groups["type"].Value}.Parse` crashes with a FormatException as soon as someone types something that is not a number",
                null);
        }
    }

    // ------------------------------------------------------------------ JavaScript: ==

    private static IEnumerable<LogicFinding> LooseEquality(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var matches = Regex.Matches(masked[i], @"(?<![=!<>])(?<op>==|!=)(?!=)(?!\s*(?:null|undefined)\b)").Where(m => !Regex.IsMatch(masked[i][..m.Index], @"(?:null|undefined)\s*$")).ToList();
            if (matches.Count == 0) continue;

            var line = source.Lines[i];
            var corrected = matches.OrderByDescending(m => m.Index).Aggregate(line, (text, m) => text[..m.Index] + m.Groups["op"].Value + "=" + text[(m.Index + m.Length)..]);

            yield return new LogicFinding(id, i + 1,
                "`==` converts the two sides to the same type before comparing, so \"1\" == 1 and \"\" == 0 are both true",
                Replace(id, "Compare without conversion: === and !==",
                    "`===` is true only when both sides are the same type and value, which is almost always what a comparison means. `==` quietly " +
                    "converts text to numbers and back, and the rules for when it does are hard to remember.",
                    source, i + 1, corrected));
        }
    }

    // ------------------------------------------------------------------ JavaScript: "Hello ${name}"

    private static IEnumerable<LogicFinding> TemplateInQuotes(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var code = masked[i];
            var line = source.Lines[i];

            for (var at = 0; at < code.Length; at++)
            {
                if (code[at] is not ('"' or '\'')) continue;

                var close = code.IndexOf(code[at], at + 1);
                if (close < 0) break;

                var text = line[(at + 1)..close];
                var start = at;
                at = close;

                if (!Regex.IsMatch(text, @"\$\{[^}]+\}") || text.Contains('`')) continue;

                yield return new LogicFinding(id, i + 1,
                    $"`${{...}}` only fills in a value inside backticks, so this string prints `{Regex.Match(text, @"\$\{[^}]+\}").Value}` as it is written",
                    Replace(id, "Use backticks for a template string",
                        "JavaScript fills in `${...}` only in a template literal, written with backticks. In ordinary quotes it is just text.",
                        source, i + 1, line[..start] + "`" + text + "`" + line[(close + 1)..]));

                break;
            }
        }
    }

    // ------------------------------------------------------------------ JavaScript: items === []

    private static IEnumerable<LogicFinding> CompareWithNewArray(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"(?<![\w.])(?<name>[A-Za-z_$][\w$.]*)\s*(?<op>===|==|!==|!=)\s*\[\s*\]");
            if (!match.Success) continue;

            var line = source.Lines[i];
            var name = match.Groups["name"].Value;
            var empty = match.Groups["op"].Value.StartsWith('=') ? $"{name}.length === 0" : $"{name}.length > 0";

            yield return new LogicFinding(id, i + 1,
                $"`[]` makes a brand-new array, and an array is only ever equal to itself - so `{match.Value.Trim()}` is never true",
                Replace(id, $"Check the length: {empty}",
                    "Arrays are compared by identity, not by what they contain, so no array is ever equal to a new empty one. Its length says " +
                    "whether it is empty.",
                    source, i + 1, line[..match.Index] + empty + line[(match.Index + match.Length)..]));
        }
    }

    // ------------------------------------------------------------------ x === NaN

    private static IEnumerable<LogicFinding> NaNComparison(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var (nan, test) = Extension(source) switch
        {
            ".java" => (@"(?:Double|Float)\.NaN", "Double.isNaN({0})"),
            ".cs" => (@"(?:double|float|Double|Single)\.NaN", "double.IsNaN({0})"),
            _ => ("NaN", "Number.isNaN({0})"),
        };

        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], $@"(?<![\w.])(?<name>[A-Za-z_$][\w$.]*)\s*(?<op>===|==|!==|!=)\s*{nan}(?![\w.])");
            if (!match.Success) continue;

            var line = source.Lines[i];
            var name = match.Groups["name"].Value;
            var check = string.Format(test, name);
            if (match.Groups["op"].Value.StartsWith('!')) check = "!" + check;

            yield return new LogicFinding(id, i + 1,
                "NaN is not equal to anything, not even itself, so comparing with it is always false",
                Replace(id, $"Test for NaN: {check}",
                    "By definition NaN is unequal to every value, including another NaN, so `== NaN` can never be true. The language's own NaN test " +
                    "is the only reliable check.",
                    source, i + 1, line[..match.Index] + check + line[(match.Index + match.Length)..]));
        }
    }
}
