using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>Logic mistakes in C, C++, Java, C# and JavaScript that the code itself shows.</summary>
public static partial class CLikeLogicPatterns
{
    private static IReadOnlySet<string> Set(params string[] extensions) => new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> Native = Set(".c", ".cpp", ".cc", ".cxx", ".c++");
    private static readonly IReadOnlySet<string> Typed = Set(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs");
    private static readonly IReadOnlySet<string> Braced = Set(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs");
    private static readonly IReadOnlySet<string> Loose = Set(".c", ".cpp", ".cc", ".cxx", ".c++", ".js", ".mjs", ".cjs");
    private static readonly IReadOnlySet<string> Fallthrough = Set(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".js", ".mjs", ".cjs");
    private static readonly IReadOnlySet<string> Discarding = Set(".java", ".cs", ".js", ".mjs", ".cjs");
    private static readonly IReadOnlySet<string> Script = Set(".js", ".mjs", ".cjs");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new Pattern("logic-integer-division", Typed, IntegerDivision),
        new Pattern("logic-empty-loop-body", Braced, EmptyLoopBody),
        new Pattern("logic-empty-if-body", Braced, EmptyIfBody),
        new Pattern("logic-java-string-equals", Set(".java"), JavaStringEquals),
        new Pattern("logic-result-discarded", Discarding, ResultDiscarded),
        new Pattern("logic-assignment-in-condition", Loose, AssignmentInCondition),
        new Pattern("logic-bitwise-precedence", Loose, BitwisePrecedence),
        new Pattern("logic-switch-fallthrough", Fallthrough, SwitchFallthrough, confidence: Confidence.Possible),
        new Pattern("logic-uninitialised-total", Native, UninitialisedTotal),
        new Pattern("logic-string-literal-modified", Native, StringLiteralModified, Severity.Error),
        new Pattern("logic-c-string-equals", Native, NativeStringEquals),
        new Pattern("logic-cpp-catch-by-value", Set(".cpp", ".cc", ".cxx", ".c++"), CatchByValue, Severity.Suggestion),
        new Pattern("logic-cpp-non-virtual-destructor", Set(".cpp", ".cc", ".cxx", ".c++"), NonVirtualDestructor, confidence: Confidence.Possible),
        new Pattern("logic-js-var-in-closure", Script, VarInClosure),
        new Pattern("logic-js-numeric-sort", Script, NumericSort),
        new Pattern("logic-js-map-parseint", Script, MapParseInt),
        new Pattern("logic-return-in-loop", Braced, ReturnInLoop),
        new Pattern("logic-reset-in-loop", Braced, ResetInLoop),
        new Pattern("logic-loop-never-advances", Braced, LoopNeverAdvances, Severity.Error),
    ];

    private sealed class Pattern(
        string id,
        IReadOnlySet<string> extensions,
        Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning,
        Confidence confidence = Confidence.Likely,
        FindingKind kind = FindingKind.Logic) : ILogicPattern
    {
        public string Id => id;
        public IReadOnlySet<string> Extensions => extensions;

        public IEnumerable<LogicFinding> Find(SourceFile source) =>
            find(id, source, CodeText.MaskAll(source.Lines, Syntax.CLike))
                .Select(finding => finding with { Severity = severity, Confidence = confidence, Kind = kind });
    }

    private static string Indent(string line) => CodeText.Indentation(line);

    private static bool Is(SourceFile source, params string[] extensions) =>
        extensions.Contains(Path.GetExtension(source.Path).ToLowerInvariant());

    private static LocalFix Replace(string id, string title, string explanation, SourceFile source, int line, string text) =>
        LocalFix.ReplaceLine(id, title, explanation, source.Path, line, text);

    private static int NextCode(IReadOnlyList<string> masked, int from)
    {
        for (var k = from; k < masked.Count; k++) if (masked[k].Trim().Length > 0) return k;
        return -1;
    }

    private static int PreviousCode(IReadOnlyList<string> masked, int from)
    {
        for (var k = from; k >= 0; k--) if (masked[k].Trim().Length > 0) return k;
        return -1;
    }

    // ------------------------------------------------------------------ double average = sum / count;

    [GeneratedRegex(@"\b(?:int|long|short|unsigned(?:\s+int)?|size_t|byte|Integer|Long)\s+(?<rest>[^;(){}]+)")]
    private static partial Regex WholeDeclaration();

    [GeneratedRegex(@"\b(?:double|float|Double|Float|decimal)\s+(?<rest>[^;(){}]+)")]
    private static partial Regex RealDeclaration();

    private static HashSet<string> Declared(IReadOnlyList<string> masked, Regex declaration) =>
        masked.SelectMany(l => declaration.Matches(l))
            .SelectMany(m => m.Groups["rest"].Value.Split(','))
            .Select(part => Regex.Match(part.Trim(), @"^(?:\*\s*)?(?<name>[A-Za-z_]\w*)").Groups["name"].Value)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<LogicFinding> IntegerDivision(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var whole = Declared(masked, WholeDeclaration());
        var real = Declared(masked, RealDeclaration());
        var operand = @"[A-Za-z_]\w*(?:\s*\.\s*(?:size\(\s*\)|length\(\s*\)|length|Length|Count)(?![\w(]))?|\d+(?![.\w])";

        bool IsWhole(string text) =>
            Regex.IsMatch(text, @"^\d+$|\.\s*(?:size\(\s*\)|length\(\s*\)|length|Length|Count)$") || (whole.Contains(text) && !real.Contains(text));

        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], $@"\b(?:double|float|Double|Float)\s+(?<target>[A-Za-z_]\w*)\s*=\s*(?<a>{operand})\s*/\s*(?<b>{operand})\s*;");
            if (!match.Success) continue;

            var (a, b) = (match.Groups["a"], match.Groups["b"]);
            if (!IsWhole(a.Value) || !IsWhole(b.Value) || Regex.IsMatch(a.Value + b.Value, @"^\d+$")) continue;

            var line = source.Lines[i];
            var target = match.Groups["target"].Value;

            yield return new LogicFinding(id, i + 1,
                $"`{a.Value} / {b.Value}` divides two whole numbers, which throws the fraction away before the result is stored in `{target}`",
                Replace(id, $"Divide as real numbers: (double) {a.Value} / {b.Value}",
                    $"Both `{a.Value}` and `{b.Value}` are whole numbers, so `/` does whole-number division: 7 / 2 is 3, not 3.5. Storing that in a " +
                    $"`double` cannot bring the .5 back - it was lost first. Making one side a `double` makes the division itself keep the fraction.",
                    source, i + 1, line[..a.Index] + $"(double) {a.Value}" + line[(a.Index + a.Length)..]));
        }
    }

    // ------------------------------------------------------------------ for (...); { ... }

    private static IEnumerable<LogicFinding> EmptyLoopBody(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"^(?<lead>\s*)(?<keyword>for|while)\s*\((?<inside>.*)\)\s*(?<semicolon>;)\s*(?<brace>\{)?\s*$");
            if (!match.Success || CCode.Matching(masked[i], masked[i].IndexOf('(')) != masked[i].LastIndexOf(')')) continue;

            // `} while (x);` ends a do-while, and so does `while (x);` straight after a closing brace.
            if (match.Groups["keyword"].Value == "while" && PreviousCode(masked, i - 1) is var previous and >= 0 && masked[previous].TrimEnd().EndsWith('}')) continue;

            var next = NextCode(masked, i + 1);
            var opensBlock = match.Groups["brace"].Success || (next >= 0 && (masked[next].TrimStart().StartsWith('{') || Indent(masked[next]).Length > Indent(masked[i]).Length));
            if (!opensBlock) continue;

            var semicolon = match.Groups["semicolon"].Index;
            var line = source.Lines[i];
            var keyword = match.Groups["keyword"].Value;

            yield return new LogicFinding(id, i + 1,
                $"the `;` straight after the `{keyword}` is the loop's whole body, so the block below it runs once, after the loop, instead of on every pass",
                Replace(id, $"Remove the ; that ends the {keyword} loop early",
                    $"A `;` on its own is an empty statement, and straight after `{keyword} (...)` it becomes the loop's body: the loop runs, doing " +
                    "nothing, and the block underneath - meant to be the body - runs once when it has finished. Without the `;` the block is the body.",
                    source, i + 1, (line[..semicolon] + line[(semicolon + 1)..]).TrimEnd()));
        }
    }

    // ------------------------------------------------------------------ if (...); { ... }

    private static IEnumerable<LogicFinding> EmptyIfBody(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"^\s*if\s*\((?<inside>.*)\)\s*(?<semicolon>;)\s*(?<brace>\{)?\s*$");
            if (!match.Success || CCode.Matching(masked[i], masked[i].IndexOf('(')) != masked[i].LastIndexOf(')')) continue;

            var next = NextCode(masked, i + 1);
            int blockEnd;

            if (match.Groups["brace"].Success)
                blockEnd = NativeCourse.BlockEnd(masked, i) ?? -1;
            else if (next >= 0 && masked[next].TrimStart().StartsWith('{'))
                blockEnd = NativeCourse.BlockEnd(masked, next) ?? -1;
            else if (next >= 0 && Indent(masked[next]).Length > Indent(masked[i]).Length)
                blockEnd = next;
            else
                continue;

            if (blockEnd < 0) continue;

            // With an else after the block it does not compile, and another rule answers that.
            var after = NextCode(masked, blockEnd + 1);
            if (after >= 0 && Regex.IsMatch(masked[after], @"^\s*else\b")) continue;
            if (Regex.IsMatch(masked[blockEnd], @"\}\s*else\b")) continue;

            var semicolon = match.Groups["semicolon"].Index;
            var line = source.Lines[i];

            yield return new LogicFinding(id, i + 1,
                "the `;` straight after the `if` is everything the condition controls, so the block below it runs whether the condition is true or not",
                Replace(id, "Remove the ; that ends the if early",
                    "A `;` on its own is an empty statement, and straight after `if (...)` it is what the condition controls - so the `if` guards " +
                    "nothing, and the block underneath always runs. Without the `;` the block runs only when the condition is true.",
                    source, i + 1, (line[..semicolon] + line[(semicolon + 1)..]).TrimEnd()));
        }
    }

    // ------------------------------------------------------------------ Java: name == "admin"

    private static IEnumerable<LogicFinding> JavaStringEquals(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var strings = masked.SelectMany(l => Regex.Matches(l, @"\bString\s+(?<name>[A-Za-z_$][\w$]*)\s*[=;,)]")).Select(m => m.Groups["name"].Value).ToHashSet();
        if (strings.Count == 0) yield break;

        var comparison = new Regex(@"(?<left>(?<![\w$.])[A-Za-z_$][\w$]*|""[^""]*"")\s*(?<op>==|!=)\s*(?<right>""[^""]*""|[A-Za-z_$][\w$]*(?![\w$.(]))");

        for (var i = 0; i < masked.Count; i++)
        {
            var hits = comparison.Matches(masked[i])
                .Where(m =>
                {
                    var (left, right) = (m.Groups["left"].Value, m.Groups["right"].Value);
                    var leftText = left.StartsWith('"');
                    var rightText = right.StartsWith('"');

                    return (strings.Contains(left) || leftText) && (strings.Contains(right) || rightText) && !(leftText && rightText) && right != "null";
                })
                .ToList();

            if (hits.Count == 0) continue;

            var line = source.Lines[i];
            var corrected = CCode.Replace(line, hits, m =>
            {
                // "yes".equals(answer) when the literal is on the left: it reads the same, and cannot fail on a null answer.
                var left = line.Substring(m.Groups["left"].Index, m.Groups["left"].Length);
                var right = line.Substring(m.Groups["right"].Index, m.Groups["right"].Length);

                return (m.Groups["op"].Value == "!=" ? "!" : "") + $"{left}.equals({right})";
            });

            yield return new LogicFinding(id, i + 1,
                "`==` on two Strings compares whether they are the same object, not whether they hold the same text",
                Replace(id, "Compare the text with equals, not ==",
                    "In Java, `==` on objects asks whether both sides are the very same object. Two Strings with the same text are often different " +
                    "objects - one typed in, one written in the code - so the comparison is false even when the text matches. `equals` compares " +
                    "the characters.",
                    source, i + 1, corrected));
        }
    }

    // ------------------------------------------------------------------ name.toUpperCase(); on its own

    private static IEnumerable<LogicFinding> ResultDiscarded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var java = Is(source, ".java");
        var csharp = Is(source, ".cs");

        var methods = java
            ? "toUpperCase|toLowerCase|trim|strip|stripLeading|stripTrailing|replace|replaceAll|replaceFirst|substring|concat|repeat"
            : csharp
                ? "ToUpper|ToLower|ToUpperInvariant|ToLowerInvariant|Trim|TrimStart|TrimEnd|Replace|Substring|Insert|Remove|PadLeft|PadRight"
                : "toUpperCase|toLowerCase|trim|trimStart|trimEnd|replace|replaceAll|slice|substring|padStart|padEnd|repeat|concat|map|filter|toSorted|toReversed";

        HashSet<string> Names(string pattern) => masked.SelectMany(l => Regex.Matches(l, pattern)).Select(m => m.Groups["name"].Value).ToHashSet();

        var variables = java
            ? Names(@"\bString\s+(?<name>[A-Za-z_$][\w$]*)\s*[=;,)]")
            : csharp
                ? Names(@"\b(?:string|String)\s+(?<name>[A-Za-z_]\w*)\s*[=;,)]").Union(Names(@"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?:""|Console\.ReadLine\(\))")).ToHashSet()
                : Names(@"\b(?:let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=");

        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], $@"^(?<lead>\s*)(?<name>[A-Za-z_$][\w$]*)\.(?<method>{methods})\s*\((?<arguments>.*)\)\s*;?\s*$");
            if (!match.Success || !variables.Contains(match.Groups["name"].Value)) continue;
            if (CCode.Matching(masked[i], masked[i].IndexOf('(', match.Groups["method"].Index)) != masked[i].TrimEnd().TrimEnd(';').TrimEnd().Length - 1) continue;

            var name = match.Groups["name"].Value;
            var method = match.Groups["method"].Value;
            var line = source.Lines[i];
            var lead = match.Groups["lead"].Value;

            yield return new LogicFinding(id, i + 1,
                $"`{name}.{method}(...)` returns a new value and nothing keeps it, so `{name}` does not change",
                Replace(id, $"Keep the result: {name} = {name}.{method}(...)",
                    $"{(java ? "Java Strings" : csharp ? "C# strings" : "JavaScript strings and these array methods")} never change the value they are " +
                    $"called on. `{method}` returns a new one, and on a line of its own it is thrown away. Storing it back in `{name}` keeps it.",
                    source, i + 1, $"{lead}{name} = {line.TrimStart()}"));
        }
    }

    // ------------------------------------------------------------------ if (x = 5)

    private static IEnumerable<LogicFinding> AssignmentInCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var script = Is(source, ".js", ".mjs", ".cjs");

        for (var i = 0; i < masked.Count; i++)
        {
            var hits = Regex.Matches(masked[i], @"\b(?:if|while)\s*\(\s*(?<target>[A-Za-z_$][\w$.]*(?:\[[^\]]*\])?)\s*(?<op>=)(?![=])\s*(?<value>-?\d+(?:\.\d+)?|'.'|""[^""]*""|true|false|NULL|nullptr|null|undefined)\s*\)").ToList();
            if (hits.Count == 0) continue;

            var line = source.Lines[i];
            var corrected = hits.OrderByDescending(h => h.Groups["op"].Index)
                .Aggregate(line, (text, hit) => text[..hit.Groups["op"].Index] + (script ? "===" : "==") + text[(hit.Groups["op"].Index + 1)..]);

            yield return new LogicFinding(id, i + 1,
                "`=` inside the condition stores the value instead of comparing with it, so the condition is decided by the value itself",
                Replace(id, $"Compare with {(script ? "===" : "==")}, not =",
                    $"`=` assigns: the variable is overwritten, and the condition is then true for any value that is not zero. `{(script ? "===" : "==")}` " +
                    "compares, which is what a condition means.",
                    source, i + 1, corrected));
        }
    }

    // ------------------------------------------------------------------ x & 1 == 0

    private static IEnumerable<LogicFinding> BitwisePrecedence(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var pattern = new Regex(@"(?<![&|^\w$.)\]])(?<expression>(?<a>[A-Za-z_$][\w$.]*)\s*(?<op>(?<!&)&(?!&)|(?<!\|)\|(?!\|)|\^)\s*(?<b>[A-Za-z_$][\w$]*|0[xX][0-9a-fA-F]+|\d+))\s*(?<compare>===|!==|==|!=)\s*(?<c>[A-Za-z_$][\w$]*|0[xX][0-9a-fA-F]+|\d+)");

        for (var i = 0; i < masked.Count; i++)
        {
            var hits = pattern.Matches(masked[i]).ToList();
            if (hits.Count == 0) continue;

            var line = source.Lines[i];
            var corrected = hits.OrderByDescending(h => h.Groups["expression"].Index).Aggregate(line, (text, hit) =>
            {
                var e = hit.Groups["expression"];
                return text[..e.Index] + "(" + text.Substring(e.Index, e.Length) + ")" + text[(e.Index + e.Length)..];
            });

            var first = hits[0];

            yield return new LogicFinding(id, i + 1,
                $"`{first.Groups["compare"].Value}` is worked out before `{first.Groups["op"].Value}`, so this compares `{first.Groups["b"].Value}` with `{first.Groups["c"].Value}` first",
                Replace(id, $"Bracket the {first.Groups["op"].Value} before comparing",
                    $"Comparison binds tighter than `{first.Groups["op"].Value}`, so `{first.Value.Trim()}` means `{first.Groups["a"].Value} {first.Groups["op"].Value} " +
                    $"({first.Groups["b"].Value} {first.Groups["compare"].Value} {first.Groups["c"].Value})` - a true or false combined with a number. The brackets " +
                    "make the bits be combined first, then compared.",
                    source, i + 1, corrected));
        }
    }

    // ------------------------------------------------------------------ a case that falls into the next one

    private static IEnumerable<LogicFinding> SwitchFallthrough(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var depths = CCode.DepthAtStart(masked);
        var labels = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], @"^\s*(?:case\b[^:]*|default\s*):\s*(?:\{\s*)?$")).ToList();
        var ends = new Regex(@"^\s*(?:break|return|continue|throw|goto|yield)\b|\bexit\s*\(|System\.exit\s*\(|\[\[fallthrough\]\]");

        for (var k = 0; k + 1 < labels.Count; k++)
        {
            var (label, nextLabel) = (labels[k], labels[k + 1]);
            if (depths[label] != depths[nextLabel]) continue;

            var body = Enumerable.Range(label + 1, nextLabel - label - 1).Where(i => masked[i].Trim().Length > 0 && depths[i] == depths[label]).ToList();
            if (body.Count == 0 || ends.IsMatch(masked[body[^1]])) continue;
            if (Enumerable.Range(label, nextLabel - label + 1).Any(i => Regex.IsMatch(source.Lines[i], @"(?://|/\*).*fall", RegexOptions.IgnoreCase))) continue;

            var assigned = body.Select(i => Regex.Match(masked[i], @"^\s*(?<target>[A-Za-z_$][\w$.]*)\s*(?:[+\-*/]?=)(?!=)")).Where(m => m.Success).Select(m => m.Groups["target"].Value).ToHashSet();
            var nextFirst = NextCode(masked, nextLabel + 1);
            if (nextFirst < 0 || Regex.Match(masked[nextFirst], @"^\s*(?<target>[A-Za-z_$][\w$.]*)\s*=(?!=)") is not { Success: true } overwritten) continue;
            if (!assigned.Contains(overwritten.Groups["target"].Value)) continue;

            var target = overwritten.Groups["target"].Value;

            yield return new LogicFinding(id, body[^1] + 1,
                $"this case has no `break`, so it runs straight on into the next case, which sets `{target}` again and overwrites it",
                LocalFix.Insert(id, "End the case with break",
                    $"A `case` without `break` carries on into the next one. Here the next case sets `{target}` again, so whatever this case set " +
                    "is always overwritten before anything uses it. `break;` stops at the end of this case.",
                    source.Path, body[^1] + 2, [$"{Indent(source.Lines[body[^1]])}break;"]));
        }
    }

    // ------------------------------------------------------------------ int sum; sum += ...

    private static IEnumerable<LogicFinding> UninitialisedTotal(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var depths = CCode.DepthAtStart(masked);

        for (var i = 0; i < masked.Count; i++)
        {
            if (depths[i] == 0) continue;

            var declaration = Regex.Match(source.Lines[i], @"^(?<head>\s*(?:(?:unsigned|signed|long|short)\s+)*(?:int|long|double|float|short)\s+(?<name>[A-Za-z_]\w*))(?<tail>\s*;.*)$");
            if (!declaration.Success) continue;

            var name = declaration.Groups["name"].Value;
            var n = Regex.Escape(name);
            var (_, end) = CCode.EnclosingFunction(masked, i);

            var use = Enumerable.Range(i + 1, Math.Max(0, end - i)).FirstOrDefault(k => Regex.IsMatch(masked[k], $@"(?<![\w.>]){n}(?!\w)"), -1);
            if (use < 0) continue;

            var text = masked[use];
            if (Regex.IsMatch(text, $@"&\s*{n}\b|\b{n}\s*=(?!=)(?!\s*{n}\b)")) continue;

            var start = Regex.IsMatch(text, $@"(?<![\w.>]){n}\s*(?:\+=|-=|\+\+|--)|(?:\+\+|--)\s*{n}\b|\b{n}\s*=\s*{n}\s*[+-]")
                ? "0"
                : Regex.IsMatch(text, $@"(?<![\w.>]){n}\s*\*=|\b{n}\s*=\s*{n}\s*\*") ? "1" : null;

            if (start is null) continue;

            yield return new LogicFinding(id, i + 1,
                $"`{name}` is added to before it is ever given a value, so the {(start == "0" ? "total" : "product")} starts from whatever was in memory",
                Replace(id, $"Start {name} from {start}",
                    $"A local variable in C is not set to anything when it is declared - it holds whatever that memory held before. `{name}` is " +
                    $"built on top of that, so the answer can be right on one run and wrong on the next. A running " +
                    $"{(start == "0" ? "total starts from 0" : "product starts from 1")}.",
                    source, i + 1, declaration.Groups["head"].Value + " = " + start + declaration.Groups["tail"].Value));
        }
    }

    // ------------------------------------------------------------------ char *name = "hello"; name[0] = 'H';

    private static IEnumerable<LogicFinding> StringLiteralModified(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var declaration = Regex.Match(source.Lines[i], @"^(?<lead>\s*)char\s*\*\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?<literal>""(?:[^""\\]|\\.)*"")\s*;(?<tail>.*)$");
            if (!declaration.Success) continue;

            var name = declaration.Groups["name"].Value;
            var n = Regex.Escape(name);
            var (_, end) = CCode.EnclosingFunction(masked, i);
            var later = Enumerable.Range(i + 1, Math.Max(0, end - i)).ToList();

            if (later.Any(k => Regex.IsMatch(masked[k], $@"(?<![\w.>*]){n}\s*=(?!=)"))) continue;

            var write = later.FirstOrDefault(k => Regex.IsMatch(masked[k], $@"(?<![\w.>]){n}\s*\[[^\]]+\]\s*=(?!=)|\*\s*{n}\s*=(?!=)|\*\s*\(\s*{n}\s*\+[^)]*\)\s*=(?!=)"), -1);
            if (write < 0) continue;

            yield return new LogicFinding(id, i + 1,
                $"`{name}` points at a string literal, which is read-only, and line {write + 1} writes into it",
                Replace(id, $"Make {name} an array holding its own copy: char {name}[]",
                    $"A string literal lives in memory the program may not change, and `char *{name}` only points at it - so writing into it " +
                    $"crashes on some systems and silently does something else on others. `char {name}[] = ...` makes an array with its own copy " +
                    "of the text, which can be changed.",
                    source, i + 1, $"{declaration.Groups["lead"].Value}char {name}[] = {declaration.Groups["literal"].Value};{declaration.Groups["tail"].Value}"));
        }
    }

    // ------------------------------------------------------------------ C: answer == "yes" on a char array

    private static IEnumerable<LogicFinding> NativeStringEquals(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        // Text held as C text - a char array or a char pointer - which == compares by address. A std::string compares its text.
        var texts = masked.SelectMany(l => Regex.Matches(l, @"\bchar\s*(?:\*\s*|const\s*\*\s*)?(?<name>[A-Za-z_]\w*)\s*(?:\[[^\]]*\])?\s*[=;,)]"))
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        if (texts.Count == 0) yield break;

        for (var i = 0; i < masked.Count; i++)
        {
            var hits = Regex.Matches(masked[i], @"(?<![\w.>\]])(?<name>[A-Za-z_]\w*)\s*(?<op>==|!=)\s*""[^""]*""|""[^""]*""\s*(?<op>==|!=)\s*(?<name>[A-Za-z_]\w*)(?![\w(\[])")
                .Where(m => texts.Contains(m.Groups["name"].Value))
                .ToList();

            if (hits is not [var hit]) continue;

            var line = source.Lines[i];
            var name = hit.Groups["name"].Value;
            var op = hit.Groups["op"].Value;
            var literal = Regex.Match(line.Substring(hit.Index, hit.Length), @"""(?:[^""\\]|\\.)*""").Value;
            var fix = NativeCourse.WithHeader(
                id, $"Compare the text with strcmp: strcmp({name}, {literal}) {op} 0",
                $"`==` on C strings compares where they are in memory, not what they say, and `{name}` and the literal `{literal}` are never " +
                $"in the same place - so this is {(op == "==" ? "false" : "true")} even when the text matches. `strcmp` compares the characters " +
                "and returns 0 when they are the same.",
                source, i + 1, Cpp.IsCpp(source) ? "cstring" : "string.h",
                line[..hit.Index] + $"strcmp({name}, {literal}) {op} 0" + line[(hit.Index + hit.Length)..]);

            yield return new LogicFinding(id, i + 1, $"`{name} {op} {literal}` compares where the text is in memory, not what it says", fix);
        }
    }

    // ------------------------------------------------------------------ C++: catch (std::exception e)

    private static IEnumerable<LogicFinding> CatchByValue(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var clause = Regex.Match(masked[i], @"\bcatch\s*\(\s*(?:const\s+)?(?<type>(?:std::)?(?:exception|runtime_error|logic_error|out_of_range|invalid_argument|length_error|domain_error|range_error|overflow_error|underflow_error|bad_alloc|system_error|ios_base::failure|[A-Z]\w*(?:Error|Exception)))\s+(?<name>[A-Za-z_]\w*)\s*\)");
            if (!clause.Success) continue;

            var type = clause.Groups["type"].Value;
            var name = clause.Groups["name"].Value;
            var line = source.Lines[i];

            yield return new LogicFinding(id, i + 1,
                $"catching `{type}` by value copies just that part of what was thrown, so `{name}` loses the real exception",
                Replace(id, $"Catch by reference: const {type}& {name}",
                    $"Catching `{type}` by value copies only the `{type}` part of whatever was thrown - so `{name}.what()` can answer for a plain " +
                    $"`{type}`, and the real message and type are lost. Some compilers keep the message anyway, which is why it can look fine. " +
                    "A reference is the thrown object itself.",
                    source, i + 1, line[..clause.Index] + $"catch (const {type}& {name})" + line[(clause.Index + clause.Length)..]));
        }
    }

    // ------------------------------------------------------------------ C++: delete through a base with no virtual destructor

    private static IEnumerable<LogicFinding> NonVirtualDestructor(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var deletes = Enumerable.Range(0, masked.Count)
            .Select(i => (Index: i, Match: Regex.Match(masked[i], @"\bdelete\s+(?<name>[A-Za-z_]\w*)\s*;")))
            .Where(x => x.Match.Success)
            .ToList();

        foreach (var (index, match) in deletes)
        {
            var name = match.Groups["name"].Value;

            // Made as one class and held as a pointer to another: Base* pet = new Dog();
            var made = Enumerable.Range(0, index).Select(k => Regex.Match(masked[k], $@"\b(?<base>[A-Z]\w*)\s*\*\s*{Regex.Escape(name)}\s*=\s*new\s+(?<derived>[A-Z]\w*)\b")).LastOrDefault(m => m.Success);
            if (made is null || made.Groups["base"].Value == made.Groups["derived"].Value) continue;

            var baseName = made.Groups["base"].Value;
            var header = CppClass.Header(masked, baseName);
            if (header < 0 || CppClass.Body(masked, header) is not { } body) continue;

            var members = CppClass.Members(masked, header);
            if (!members.Any(k => Regex.IsMatch(masked[k], @"\bvirtual\b"))) continue;

            var destructor = members.Where(k => Regex.IsMatch(masked[k], $@"(?<![\w:])~{Regex.Escape(baseName)}\s*\(")).ToList();
            if (destructor.Any(k => Regex.IsMatch(masked[k], @"\bvirtual\b"))) continue;

            const string Explanation =
                "The object is deleted through a pointer to its base class, and the base class's destructor is not `virtual` - so only the base " +
                "part is destroyed, the derived class's destructor never runs, and whatever it would have released leaks. A class used through " +
                "a base pointer needs a `virtual` destructor, which makes `delete` find the right one.";

            LocalFix fix;

            if (destructor is [var line])
            {
                var text = source.Lines[line];
                var at = Regex.Match(text, $@"(?<![\w:])~{Regex.Escape(baseName)}\s*\(").Index;
                fix = Replace(id, $"Make ~{baseName}() virtual", Explanation, source, line + 1, text[..at] + "virtual " + text[at..]);
            }
            else if (destructor.Count == 0)
            {
                var publicLine = members.FirstOrDefault(k => Regex.IsMatch(masked[k], @"^\s*public\s*:"), -1);
                var isStruct = Regex.IsMatch(masked[header], @"^\s*struct\b");
                var indent = members.Select(k => source.Lines[k]).FirstOrDefault(l => l.Trim().Length > 0 && !l.Trim().EndsWith(':')) is { } memberLine ? Indent(memberLine) : "    ";

                IReadOnlyList<string> added = publicLine >= 0 || isStruct ? [$"{indent}virtual ~{baseName}() = default;"] : ["public:", $"{indent}virtual ~{baseName}() = default;"];
                fix = LocalFix.Insert(id, $"Give {baseName} a virtual destructor", Explanation, source.Path, publicLine >= 0 ? publicLine + 2 : body.Open + 2, added);
            }
            else
            {
                continue;
            }

            yield return new LogicFinding(id, index + 1,
                $"`{name}` is a `{made.Groups["derived"].Value}` deleted as a `{baseName}`, whose destructor is not virtual, so `~{made.Groups["derived"].Value}` never runs",
                fix);
        }
    }

    // ------------------------------------------------------------------ JavaScript: for (var i ...) with a callback

    private static IEnumerable<LogicFinding> VarInClosure(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var header = Regex.Match(masked[i], @"^(?<lead>\s*for\s*\(\s*)var(?<rest>\s+(?<name>[A-Za-z_$][\w$]*)\s*=)");
            if (!header.Success || NativeCourse.BlockEnd(masked, i) is not { } close) continue;

            var name = header.Groups["name"].Value;
            var body = string.Join("\n", Enumerable.Range(i, close - i + 1).Select(k => masked[k]));
            var callback = Regex.Match(body, @"(?:setTimeout|setInterval|\.push|\.then|addEventListener|\.on)\s*\(\s*(?:[^,]*,\s*)?(?:\([^)]*\)\s*=>|[A-Za-z_$][\w$]*\s*=>|function\b)");
            if (!callback.Success || !Regex.IsMatch(body[callback.Index..], $@"(?<![\w$.]){Regex.Escape(name)}(?![\w$])")) continue;

            var at = header.Groups["lead"].Length;
            var line = source.Lines[i];

            yield return new LogicFinding(id, i + 1,
                $"every callback made in the loop shares the one `var {name}`, so by the time they run they all see its last value",
                Replace(id, $"Give each pass its own {name}: let",
                    $"`var` makes one `{name}` for the whole function, and the callbacks run after the loop has finished - so they all read the " +
                    $"value `{name}` ended on. `let` gives every pass of the loop its own `{name}`, which is what each callback captures.",
                    source, i + 1, line[..at] + "let" + line[(at + 3)..]));
        }
    }

    // ------------------------------------------------------------------ JavaScript: numbers.sort()

    private static IEnumerable<LogicFinding> NumericSort(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var numeric = masked.SelectMany(l => Regex.Matches(l, @"\b(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*\[\s*-?\d[\d\s.,\-]*\]")).Select(m => m.Groups["name"].Value).ToHashSet();

        for (var i = 0; i < masked.Count; i++)
        {
            var hits = Regex.Matches(masked[i], @"(?<![\w$.])(?<name>[A-Za-z_$][\w$]*)\.sort\(\s*\)").Where(m => numeric.Contains(m.Groups["name"].Value)).ToList();
            if (hits.Count == 0) continue;

            var line = source.Lines[i];
            var name = hits[0].Groups["name"].Value;

            yield return new LogicFinding(id, i + 1,
                $"`sort()` with nothing given sorts `{name}` as text, so 10 comes before 9",
                Replace(id, "Sort numbers as numbers: sort((a, b) => a - b)",
                    "Without a comparison, `sort` turns every element into text and orders them alphabetically - \"10\" before \"9\", because \"1\" " +
                    "comes before \"9\". `(a, b) => a - b` compares them as numbers.",
                    source, i + 1, CCode.Replace(line, hits, m => $"{m.Groups["name"].Value}.sort((a, b) => a - b)")));
        }
    }

    // ------------------------------------------------------------------ JavaScript: ["1", "2"].map(parseInt)

    private static IEnumerable<LogicFinding> MapParseInt(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var hits = Regex.Matches(masked[i], @"\.map\(\s*parseInt\s*\)").ToList();
            if (hits.Count == 0) continue;

            yield return new LogicFinding(id, i + 1,
                "`map(parseInt)` passes each element's position as `parseInt`'s base, so every number after the first comes out wrong",
                Replace(id, "Convert with Number, not parseInt",
                    "`map` calls its function with the element and its position, and `parseInt` reads a second argument as the base to count in - " +
                    "so \"2\" at position 1 is read in base 1, and gives NaN. `Number` takes only the value.",
                    source, i + 1, CCode.Replace(source.Lines[i], hits, _ => ".map(Number)")));
        }
    }

    // ------------------------------------------------------------------ return in both branches inside a loop

    private static IEnumerable<LogicFinding> ReturnInLoop(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var e = 0; e < masked.Count; e++)
        {
            // } else {  /  return B;  /  }  /  }  - the last closing the loop.
            if (!Regex.IsMatch(masked[e], @"^\s*\}\s*else\s*\{\s*$")) continue;

            var r = NextCode(masked, e + 1);
            var closeElse = r < 0 ? -1 : NextCode(masked, r + 1);
            var closeLoop = closeElse < 0 ? -1 : NextCode(masked, closeElse + 1);
            if (closeLoop < 0) continue;

            if (Regex.Match(masked[r], @"^\s*return\b(?<value>[^;]*);\s*$") is not { Success: true } returned) continue;
            if (masked[closeElse].Trim() != "}" || masked[closeLoop].Trim() != "}") continue;

            // The loop the last brace closes, and the if the else belongs to - which must return too.
            var depths = CCode.DepthAtStart(masked);
            var loop = Enumerable.Range(0, closeLoop).LastOrDefault(k => depths[k] == depths[closeLoop] - 1 && masked[k].TrimEnd().EndsWith('{'), -1);
            if (loop < 0 || Regex.Match(masked[loop], @"^\s*(?:for|while)\s*\((?<header>.*)\)\s*\{\s*$") is not { Success: true } header) continue;

            var ifLine = Enumerable.Range(loop + 1, e - loop - 1).LastOrDefault(k => depths[k] == depths[e] - 1 && Regex.IsMatch(masked[k], @"^\s*if\s*\(.*\)\s*\{\s*$"), -1);
            if (ifLine < 0 || !Enumerable.Range(ifLine + 1, e - ifLine - 1).Any(k => Regex.IsMatch(masked[k], @"^\s*return\b"))) continue;

            var variables = Regex.Matches(header.Groups["header"].Value, @"(?:\b(?:int|let|var|const|auto|size_t|long|String|string|char|double)\s+|,\s*|:\s*)(?<name>[A-Za-z_$][\w$]*)\s*(?:=|:|\bof\b|\bin\b)").Select(m => m.Groups["name"].Value).ToList();
            if (variables.Any(v => Regex.IsMatch(returned.Groups["value"].Value, $@"(?<![\w$.]){Regex.Escape(v)}(?![\w$])"))) continue;

            var statement = lines[r].Trim();

            yield return new LogicFinding(id, e + 1,
                "the loop returns on its first pass whichever way the `if` goes, so it never looks past the first item",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Only {statement.TrimEnd(';')} once every item has been checked",
                    Explanation =
                        $"Both branches return, so the loop stops on its first pass: if the first item does not match, `{statement}` ends the " +
                        "search before any other item is looked at. The `else` belongs after the loop - reached only when no item matched.",
                    File = source.Path, StartLine = e + 1, RemoveCount = closeLoop - e + 1,
                    NewLines = [$"{Indent(lines[e])}}}", lines[closeLoop], $"{Indent(lines[loop])}{statement}"],
                });
        }
    }

    // ------------------------------------------------------------------ total = 0; inside the loop that adds to it

    private static IEnumerable<LogicFinding> ResetInLoop(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;
        var depths = CCode.DepthAtStart(masked);
        int depthsAt(int line) => depths[line];

        for (var header = 0; header < masked.Count; header++)
        {
            if (!Regex.IsMatch(masked[header], @"^\s*(?:for|while)\s*\(.*\)\s*\{\s*$") || NativeCourse.BlockEnd(masked, header) is not { } close) continue;

            var first = NextCode(masked, header + 1);
            if (first < 0 || first >= close) continue;
            if (Regex.Match(masked[first], @"^\s*(?<name>[A-Za-z_$][\w$]*)\s*=\s*(?<value>0|0\.0|1|""\s*"")\s*;\s*$") is not { Success: true } reset) continue;

            var name = reset.Groups["name"].Value;
            var n = Regex.Escape(name);
            var body = Enumerable.Range(first + 1, close - first - 1).Select(k => masked[k]).ToList();

            if (!body.Any(l => Regex.IsMatch(l, $@"(?<![\w$.]){n}\s*(?:\+=|-=|\*=|\+\+|--)|(?<![\w$.]){n}\s*=\s*{n}\s*[+\-*]"))) continue;
            if (body.Any(l => Regex.IsMatch(l, $@"(?<![\w$.]){n}\s*=(?!=)(?!\s*{n}\b)"))) continue;

            // The rest of the function - or of the file, for a script's top-level code.
            var end = depthsAt(header) == 0 ? masked.Count - 1 : CCode.EnclosingFunction(masked, header).End;
            if (!Enumerable.Range(close + 1, Math.Max(0, end - close)).Any(k => Regex.IsMatch(masked[k], $@"(?<![\w$.]){n}(?![\w$])"))) continue;

            yield return new LogicFinding(id, first + 1,
                $"`{name}` is set back to {reset.Groups["value"].Value} at the start of every pass, so after the loop it only holds the last item's part",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Start {name} once, before the loop",
                    Explanation =
                        $"`{lines[first].Trim()}` is inside the loop, so every pass throws away what the passes before it added and starts again. " +
                        $"Set once before the loop, `{name}` keeps building up across every pass.",
                    File = source.Path, StartLine = header + 1, RemoveCount = first - header + 1,
                    NewLines = [Indent(lines[header]) + lines[first].Trim(), lines[header], .. lines.Skip(header + 1).Take(first - header - 1)],
                });
        }
    }

    // ------------------------------------------------------------------ while (i < n) { ... } without i changing

    private static IEnumerable<LogicFinding> LoopNeverAdvances(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var header = 0; header < masked.Count; header++)
        {
            var loop = Regex.Match(masked[header], @"^\s*while\s*\(\s*(?<name>[A-Za-z_$][\w$]*)\s*(?<op><=|<|>=|>)\s*(?<bound>[^)]+)\)\s*\{\s*$");
            if (!loop.Success || NativeCourse.BlockEnd(masked, header) is not { } close || close <= header + 1) continue;

            var name = loop.Groups["name"].Value;
            var n = Regex.Escape(name);
            var body = Enumerable.Range(header + 1, close - header - 1).Select(k => masked[k]).ToList();

            if (body.Any(l => Regex.IsMatch(l, $@"(?<![\w$.]){n}\s*(?:[+\-*/%]?=)(?!=)|(?:\+\+|--)\s*{n}\b|(?<![\w$.]){n}\s*(?:\+\+|--)|&\s*{n}\b|\b(?:break|return|goto|throw|exit)\b"))) continue;

            var bounds = Regex.Matches(loop.Groups["bound"].Value, @"(?<![\w$.])[A-Za-z_$][\w$]*").Select(m => m.Value).ToList();
            if (bounds.Any(b => body.Any(l => Regex.IsMatch(l, $@"(?<![\w$.]){Regex.Escape(b)}\s*(?:[+\-*/%]?=)(?!=)|(?:\+\+|--)\s*{Regex.Escape(b)}\b|(?<![\w$.]){Regex.Escape(b)}\s*(?:\+\+|--)|&\s*{Regex.Escape(b)}\b")))) continue;

            if (!Enumerable.Range(0, header).Any(k => Regex.IsMatch(masked[k], $@"(?<![\w$.]){n}\s*=\s*-?\d+\s*[;,]"))) continue;

            var step = loop.Groups["op"].Value.StartsWith('<') ? "++" : "--";
            var inner = Enumerable.Range(header + 1, close - header - 1).Select(k => source.Lines[k]).FirstOrDefault(l => l.Trim().Length > 0) is { } first ? Indent(first) : Indent(source.Lines[header]) + "    ";

            yield return new LogicFinding(id, header + 1,
                $"nothing in the loop changes `{name}`, so once its condition is true it stays true and the loop never ends",
                LocalFix.Insert(id, $"Move {name} on at the end of each pass: {name}{step}",
                    $"The loop keeps going while `{name} {loop.Groups["op"].Value} {loop.Groups["bound"].Value.Trim()}`, and nothing inside it changes " +
                    $"`{name}` or the bound, so it can only stop by never starting. `{name}{step};` at the end of each pass moves it towards the end.",
                    source.Path, close + 1, [$"{inner}{name}{step};"]));
        }
    }
}
