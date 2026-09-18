using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using static FixFinder.Core.Logic.PythonBlocks;

namespace FixFinder.Core.Logic;

/// <summary>More of the mistakes Python programs make without failing: conditions that are always true, results thrown away, loops that skip items.</summary>
public static partial class PythonReviewPatterns
{
    private static readonly IReadOnlySet<string> Python = CodePattern.Files(".py", ".pyw");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely, FindingKind kind = FindingKind.Logic) =>
        new(id, Python, Syntax.Python, find, severity, confidence, kind);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-python-or-constant", OrConstant, Severity.Error, Confidence.Certain),
        Pattern("logic-python-none-returned-assigned", NoneReturnedAssigned, Severity.Error, Confidence.Certain),
        Pattern("logic-python-missing-f-prefix", MissingFPrefix),
        Pattern("logic-python-method-not-called", MethodNotCalled, Severity.Error),
        Pattern("logic-python-modified-while-looping", ModifiedWhileLooping, Severity.Error),
        Pattern("logic-python-input-used-as-number", InputUsedAsNumber, Severity.Error),
        Pattern("logic-python-print-instead-of-return", PrintInsteadOfReturn, Severity.Error),
        Pattern("logic-python-returns-nothing-sometimes", ReturnsNothingSometimes),
        Pattern("logic-python-floor-division-average", FloorDivisionAverage),
        Pattern("logic-python-attribute-not-set", AttributeNotSet),
        Pattern("logic-python-return-print", ReturnPrint),
        Pattern("logic-python-statement-has-no-effect", StatementHasNoEffect, confidence: Confidence.Certain),
        Pattern("logic-python-shadowed-builtin", ShadowedBuiltin),
        Pattern("logic-python-unreachable-code", UnreachableCode, confidence: Confidence.Certain),
        Pattern("logic-python-duplicate-condition", DuplicateCondition),
        Pattern("logic-python-endless-while-true", EndlessWhileTrue, confidence: Confidence.Possible),
        Pattern("logic-python-range-skips-last", RangeSkipsLast, confidence: Confidence.Possible),
        Pattern("logic-python-bare-except", BareExcept),
        Pattern("logic-python-silent-except", SilentExcept, confidence: Confidence.Possible),
        Pattern("logic-python-shared-class-list", SharedClassList),
        Pattern("logic-python-none-comparison", NoneComparison, Severity.Suggestion, Confidence.Certain, FindingKind.Style),
        Pattern("logic-python-bool-comparison", BoolComparison, Severity.Suggestion, Confidence.Likely, FindingKind.Style),
        Pattern("logic-python-type-comparison", TypeComparison, Severity.Suggestion, Confidence.Likely, FindingKind.Style),
        Pattern("logic-python-range-len-loop", RangeLenLoop, Severity.Suggestion, Confidence.Likely, FindingKind.Style),
        Pattern("logic-python-file-not-closed", FileNotClosed, Severity.Suggestion, Confidence.Likely, FindingKind.Style),
    ];

    private static LocalFix Replace(string id, string title, string explanation, SourceFile source, int line, string text) =>
        LocalFix.ReplaceLine(id, title, explanation, source.Path, line, text);

    private static string Original(SourceFile source, int line, Group group) => source.Lines[line].Substring(group.Index, group.Length);

    // ------------------------------------------------------------------ if answer == "yes" or "y":

    private const string Literal = @"(?:""[^""]*""|'[^']*'|-?\d+(?:\.\d+)?)";

    [GeneratedRegex(@"(?<![\w.])(?<left>[A-Za-z_][\w.]*(?:\[[^\]]*\])?(?:\(\))?)\s*(?<op>==|!=)\s*(?<first>" + Literal + @")(?<rest>(?:\s+(?<join>or|and)\s+" + Literal + @")+)(?=\s*(?:[:)]|$|\s+(?:or|and|if|else)\b))")]
    private static partial Regex OrConstantRegex();

    private static IEnumerable<LogicFinding> OrConstant(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (OrConstantRegex().Match(masked[i]) is not { Success: true } match) continue;

            var joins = match.Groups["join"].Captures.Select(c => c.Value).Distinct().ToList();
            var op = match.Groups["op"].Value;
            if (joins.Count != 1 || (op == "==" && joins[0] == "and")) continue;

            var line = source.Lines[i];
            var left = Original(source, i, match.Groups["left"]);
            var restText = Original(source, i, match.Groups["rest"]);
            var values = new[] { Original(source, i, match.Groups["first"]) }
                .Concat(Regex.Matches(restText, @"(?:or|and)\s+(?<value>" + Literal + ")").Select(m => m.Groups["value"].Value))
                .ToList();

            var membership = op == "==" ? "in" : "not in";
            var replacement = $"{left} {membership} ({string.Join(", ", values)})";
            var written = line.Substring(match.Index, match.Length);
            var second = values[1];

            var consequence = joins[0] == "or"
                ? $"{second} on its own is a value that always counts as true, so the whole condition is always true"
                : $"{second} on its own always counts as true, so `{left}` is never compared with it";

            yield return new LogicFinding(id, i + 1,
                $"`{written}` does not compare `{left}` with {second} - {consequence}",
                Replace(id, $"Compare {left} with every value: {replacement}",
                    $"Python reads `{written}` as `({left} {op} {values[0]}) {joins[0]} {second}`, and a non-empty string or a non-zero " +
                    $"number on its own is always true. `{left} {membership} (...)` checks `{left}` against every value in the brackets.",
                    source, i + 1, line[..match.Index] + replacement + line[(match.Index + match.Length)..]));
        }
    }

    // ------------------------------------------------------------------ names = names.sort()

    [GeneratedRegex(@"^(?<lead>\s*)(?<target>[A-Za-z_][\w.]*)\s*=\s*(?<object>[A-Za-z_][\w.]*)\.(?<method>sort|reverse|append|extend|insert|remove|clear|add|update)\((?<arguments>.*)\)\s*$")]
    private static partial Regex InPlaceAssigned();

    [GeneratedRegex(@"^(?<lead>\s*)(?<target>[A-Za-z_][\w.]*)\s*=\s*random\.shuffle\((?<object>[A-Za-z_][\w.]*)\)\s*$")]
    private static partial Regex ShuffleAssigned();

    private static IEnumerable<LogicFinding> NoneReturnedAssigned(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var ownMethods = masked.SelectMany(l => Regex.Matches(l, @"\bdef\s+(?<name>\w+)")).Select(m => m.Groups["name"].Value).ToHashSet();
        var setsOrDicts = masked.SelectMany(l => Regex.Matches(l, @"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?:set\(|dict\(|\{)")).Select(m => m.Groups["name"].Value).ToHashSet();

        for (var i = 0; i < masked.Count; i++)
        {
            var line = source.Lines[i];
            string lead, target, obj, method, arguments;

            if (InPlaceAssigned().Match(masked[i]) is { Success: true } call)
            {
                (lead, target, obj, method) = (call.Groups["lead"].Value, call.Groups["target"].Value, call.Groups["object"].Value, call.Groups["method"].Value);
                arguments = Original(source, i, call.Groups["arguments"]);

                if (ownMethods.Contains(method)) continue;
                if (method is "add" or "update" && !setsOrDicts.Contains(obj)) continue;
            }
            else if (ShuffleAssigned().Match(masked[i]) is { Success: true } shuffle)
            {
                (lead, target, obj, method, arguments) = (shuffle.Groups["lead"].Value, shuffle.Groups["target"].Value, shuffle.Groups["object"].Value, "shuffle", "");
            }
            else
            {
                continue;
            }

            var callText = method == "shuffle" ? $"random.shuffle({obj})" : $"{obj}.{method}({arguments})";

            IReadOnlyList<string> corrected = (method, target == obj) switch
            {
                (_, true) => [$"{lead}{callText}"],
                ("sort", false) => [$"{lead}{target} = sorted({obj}{(arguments.Length > 0 ? ", " + arguments : "")})"],
                ("reverse", false) => [$"{lead}{target} = list(reversed({obj}))"],
                _ => [$"{lead}{callText}", $"{lead}{target} = {obj}"],
            };

            yield return new LogicFinding(id, i + 1,
                $"`{callText}` changes `{obj}` where it is and gives back None, so `{target}` is set to None",
                new LocalFix
                {
                    RuleId = id,
                    Title = target == obj ? $"Call {method} on its own line, without assigning the result" : $"Keep the change and set {target} to the list itself",
                    Explanation =
                        $"Methods that change a list, set or dictionary in place - sort, append, remove and the rest - return None rather than the " +
                        $"changed collection. Assigning that result throws away the only name for it and leaves `{target}` holding None, so the " +
                        $"next line that uses `{target}` fails or prints None.",
                    File = source.Path, StartLine = i + 1, RemoveCount = 1, NewLines = corrected,
                });
        }
    }

    // ------------------------------------------------------------------ print("Hello {name}")

    [GeneratedRegex(@"\{(?<name>[A-Za-z_]\w*)(?:\.\w+|\[[^\]{}'""\s]+\]|\(\))*(?:![rsa])?(?::[^{}'""\s]*)?\}")]
    private static partial Regex Placeholder();

    private static IEnumerable<LogicFinding> MissingFPrefix(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var defined = DefinedNames(masked);
        var formatted = masked.SelectMany(l => Regex.Matches(l, @"(?<name>[A-Za-z_]\w*)\.format(?:_map)?\(")).Select(m => m.Groups["name"].Value).ToHashSet();

        for (var i = 0; i < masked.Count; i++)
        {
            var line = source.Lines[i];
            var code = masked[i];

            for (var at = 0; at < code.Length; at++)
            {
                if (code[at] is not ('"' or '\'')) continue;

                var quote = code[at];
                var close = code.IndexOf(quote, at + 1);
                if (close < 0) break;

                var start = at;
                at = close;

                var prefixStart = start;
                while (prefixStart > 0 && char.IsLetter(code[prefixStart - 1])) prefixStart--;
                var prefix = code[prefixStart..start];

                if (prefix.Contains('f', StringComparison.OrdinalIgnoreCase) || prefix.Contains('b', StringComparison.OrdinalIgnoreCase) || prefix.Length > 2) continue;
                if (prefixStart > 0 && CodeText.IsWordChar(code[prefixStart - 1])) continue;

                var text = line[(start + 1)..close];
                var names = Placeholder().Matches(text).Select(m => m.Groups["name"].Value).ToList();
                if (names.Count == 0 || !names.All(defined.Contains)) continue;

                var after = code[(close + 1)..].TrimStart();
                if (after.StartsWith(".format", StringComparison.Ordinal) || after.StartsWith('%')) continue;

                if (Regex.Match(code, @"^\s*(?<name>[A-Za-z_]\w*)\s*=") is { Success: true } assigned && formatted.Contains(assigned.Groups["name"].Value)) continue;

                yield return new LogicFinding(id, i + 1,
                    $"the string {quote}{Shorten(text)}{quote} has `{{{names[0]}}}` in it but no `f` in front, so it prints the braces and the name instead of the value",
                    Replace(id, "Make it an f-string: put f before the quote",
                        $"Only an f-string fills in `{{...}}` with the value of what is inside. Without the `f`, `{{{names[0]}}}` is just text and is " +
                        "printed exactly as it is written.",
                        source, i + 1, line[..prefixStart] + "f" + line[prefixStart..]));

                break;
            }
        }
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..37] + "...";

    // ------------------------------------------------------------------ if answer.isdigit:

    [GeneratedRegex(@"(?<![\w.])(?<object>(?!(?:str|bytes|dict|list|set|self)\b(?!\())[A-Za-z_]\w*(?:\[[^\]]*\]|\([^()]*\))?)\.(?<method>upper|lower|strip|lstrip|rstrip|title|capitalize|swapcase|split|isdigit|isalpha|isalnum|isupper|islower|isspace|isnumeric|isdecimal|istitle)\b(?!\s*\()")]
    private static partial Regex UncalledMethod();

    private static IEnumerable<LogicFinding> MethodNotCalled(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            foreach (Match call in UncalledMethod().Matches(masked[i]))
            {
                var before = masked[i][..call.Index];
                if (Regex.IsMatch(before, @"(?:key\s*=|map\(|filter\(|sorted\([^)]*,)\s*$")) continue;

                var line = source.Lines[i];
                var end = call.Index + call.Length;
                var name = $"{call.Groups["object"].Value}.{call.Groups["method"].Value}";
                var inCondition = Regex.IsMatch(masked[i], @"^\s*(?:if|elif|while)\b");

                yield return new LogicFinding(id, i + 1,
                    inCondition
                        ? $"`{name}` without brackets is the method itself, not its answer, and a method always counts as true - so this condition is always true"
                        : $"`{name}` without brackets is the method itself, not the result of calling it",
                    Replace(id, $"Call it: {name}()",
                        $"A method only runs when it is called with brackets. `{name}` names the method; `{name}()` runs it and gives its result.",
                        source, i + 1, line[..end] + "()" + line[end..]))
                {
                    Confidence = inCondition ? Confidence.Certain : Confidence.Likely,
                };

                break;
            }
        }
    }

    // ------------------------------------------------------------------ for item in items: items.remove(item)

    [GeneratedRegex(@"^(?<lead>\s*)for\s+(?<variables>[\w\s,()]+?)\s+in\s+(?<collection>[A-Za-z_][\w.]*)\s*:\s*$")]
    private static partial Regex ForOverName();

    private static IEnumerable<LogicFinding> ModifiedWhileLooping(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (ForOverName().Match(masked[header]) is not { Success: true } loop) continue;

            var collection = loop.Groups["collection"].Value;
            var c = Regex.Escape(collection);
            var (first, end) = Body(lines, header);
            var statements = Statements(masked, first, end).ToList();

            var changing = statements.FirstOrDefault(s => Regex.IsMatch(masked[s], $@"(?<![\w.]){c}\s*\.\s*(?:remove|pop|append|insert|extend|clear|popitem)\s*\(|^\s*del\s+{c}\s*\["), -1);
            if (changing < 0) continue;

            var nextStatement = statements.SkipWhile(s => s <= changing).FirstOrDefault(-1);
            if (nextStatement >= 0 && Indent(lines[nextStatement]).Length >= Indent(lines[changing]).Length &&
                Regex.IsMatch(masked[nextStatement], @"^\s*(?:break|return)\b")) continue;

            var growing = Regex.IsMatch(masked[changing], @"\.\s*(?:append|insert|extend)\s*\(");
            var line = lines[header];
            var at = loop.Groups["collection"].Index;

            yield return new LogicFinding(id, changing + 1,
                growing
                    ? $"the loop adds to `{collection}` while it is looping over it, so it keeps finding new items and may never finish"
                    : $"the loop removes from `{collection}` while it is looping over it, so the item after each one removed is skipped",
                Replace(id, $"Loop over a copy: for ... in list({collection}):",
                    $"A for loop walks through `{collection}` by position. Removing an item moves every later item back one place, so the loop " +
                    $"steps over the one that moved into the gap; adding items gives it more to walk through. Looping over `list({collection})`, " +
                    $"a copy made before the loop starts, lets the loop change `{collection}` itself safely.",
                    source, header + 1, line[..at] + $"list({collection})" + line[(at + collection.Length)..]));
        }
    }

    // ------------------------------------------------------------------ age = input(); if age > 18

    [GeneratedRegex(@"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*=\s*(?<call>input\s*\((?<prompt>.*)\))\s*$")]
    private static partial Regex InputAssignment();

    private static IEnumerable<LogicFinding> InputUsedAsNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (InputAssignment().Match(masked[i]) is not { Success: true } input) continue;

            var name = input.Groups["name"].Value;
            var n = Regex.Escape(name);
            var number = @"-?\d+(?:\.\d+)?";
            var numeric = new Regex(
                $@"(?<![\w.""']){n}\s*(?:[-*/%]|\*\*|//|[<>]=?)\s*(?:{number}|[A-Za-z_]\w*\b(?!\s*\())|(?<![\w.]){number}\s*(?:[-+*/%]|\*\*|//|[<>]=?)\s*{n}(?![\w.])|" +
                $@"\brange\(\s*(?:[^,()]*,\s*)?{n}\s*[,)]|(?<![\w.]){n}\s*\+\s*{number}(?![\w.])|(?<![\w.]){n}\s*==\s*{number}(?![\w.])");

            int? use = null;

            for (var k = i + 1; k < masked.Count; k++)
            {
                if (Regex.IsMatch(masked[k], $@"^\s*{n}\s*=(?!=)")) break;
                if (Regex.IsMatch(masked[k], $@"\b(?:int|float)\s*\(\s*{n}\s*\)")) break;

                if (numeric.IsMatch(masked[k]))
                {
                    use = k;
                    break;
                }
            }

            if (use is not { } at) continue;

            var real = Regex.IsMatch(masked[at], $@"\d+\.\d+") || Regex.IsMatch(masked[at], $@"(?<![\w.]){n}\s*/(?!/)|/(?!/)\s*{n}(?![\w.])");
            var type = real ? "float" : "int";
            var line = source.Lines[i];
            var call = input.Groups["call"];

            yield return new LogicFinding(id, i + 1,
                $"`input()` always gives back text, and line {at + 1} uses `{name}` as a number - so it either crashes or, for `*`, repeats the text",
                Replace(id, $"Turn the answer into a number: {type}(input(...))",
                    $"Whatever is typed, `input()` returns it as a string: typing 5 gives \"5\", not 5. Comparing \"5\" with a number or adding a " +
                    $"number to it raises a TypeError, and `\"5\" * 2` is \"55\". Wrapping the call in `{type}()` turns the text into a number " +
                    "as soon as it is read.",
                    source, i + 1, line[..call.Index] + $"{type}({line.Substring(call.Index, call.Length)})" + line[(call.Index + call.Length)..]));
        }
    }

    // ------------------------------------------------------------------ def area(): print(w * h)   ...   total = area()

    [GeneratedRegex(@"^(?<lead>\s*)def\s+(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex FunctionHeader();

    private static IEnumerable<LogicFinding> PrintInsteadOfReturn(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (FunctionHeader().Match(masked[header]) is not { Success: true } def) continue;

            var name = def.Groups["name"].Value;
            if (name.StartsWith("__", StringComparison.Ordinal)) continue;

            var (first, end) = Body(lines, header);
            var body = Statements(masked, first, end).ToList();

            if (body.Any(s => Regex.IsMatch(masked[s], @"^\s*(?:return\s+\S|yield\b)|\bawait\b"))) continue;

            var prints = body.Where(s => Regex.IsMatch(masked[s], @"^\s*print\s*\(")).ToList();
            if (prints.Count == 0) continue;

            var f = Regex.Escape(name);
            var usedAsValue = masked.Select((text, index) => (text, index))
                .FirstOrDefault(x => x.index != header &&
                                     Regex.IsMatch(x.text, $@"(?:[=+\-*/%<>,(\[]|\breturn|\bif|\bwhile|\band|\bor|\bnot|\bin)\s*(?<![\w.]){f}\s*\(") &&
                                     !Regex.IsMatch(x.text, $@"^\s*def\s"), (text: "", index: -1)).index;

            if (usedAsValue < 0) continue;

            LocalFix? fix = null;

            if (prints.Count == 1 && prints[0] == body[^1] && Regex.Match(lines[prints[0]], @"^(?<lead>\s*)print\((?<value>[^,]*)\)\s*$") is { Success: true } single &&
                !Regex.IsMatch(masked[prints[0]], @"\bsep\s*=|\bend\s*="))
            {
                fix = Replace(id, $"Return the value instead of printing it: return {single.Groups["value"].Value.Trim()}",
                    $"`{name}` prints its answer and then returns nothing, which in Python means it returns None. The code that calls it and " +
                    $"uses the result gets None. Returning the value hands it back to the caller, which can print it or use it.",
                    source, prints[0] + 1, $"{single.Groups["lead"].Value}return {single.Groups["value"].Value.Trim()}");
            }

            yield return new LogicFinding(id, header + 1,
                $"`{name}` prints its answer but never returns it, and line {usedAsValue + 1} uses what it returns - which is None",
                fix);
        }
    }

    // ------------------------------------------------------------------ def grade(s): if s >= 50: return "pass"   (and nothing else)

    private static IEnumerable<LogicFinding> ReturnsNothingSometimes(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (FunctionHeader().Match(masked[header]) is not { Success: true } def) continue;

            var (first, end) = Body(lines, header);
            var body = Statements(masked, first, end).ToList();
            if (body.Count == 0) continue;

            if (body.Any(k => Regex.IsMatch(masked[k], @"^\s*(?:async\s+)?def\s|\byield\b"))) continue;
            if (!body.Any(k => Regex.IsMatch(masked[k], @"^\s*return\s+\S"))) continue;

            var indent = Indent(lines[body[0]]).Length;
            var top = body.Where(k => Indent(lines[k]).Length == indent).ToList();
            var last = top[^1];
            var text = masked[last];

            if (Regex.IsMatch(text, @"^\s*(?:return|raise)\b|^\s*(?:sys\.)?exit\s*\(|^\s*while\s+(?:True|1)\s*:")) continue;
            if (Regex.IsMatch(text, @"^\s*(?:try|except|finally|with|match|case)\b")) continue;

            string when;

            if (Regex.IsMatch(text, @"^\s*(?:if|elif|else)\b"))
            {
                var start = top.FindLastIndex(k => Regex.IsMatch(masked[k], @"^\s*if\b"));
                if (start < 0) continue;

                var branches = top.Skip(start).ToList();
                var hasElse = Regex.IsMatch(masked[branches[^1]], @"^\s*else\s*:");
                var allReturn = branches.All(b => EndsInReturn(lines, masked, b));

                if (hasElse && allReturn) continue;
                when = hasElse ? "one of its branches finishes without a return" : "none of the `if` branches matches";
            }
            else if (Regex.IsMatch(text, @"^\s*(?:for|while)\b"))
            {
                when = "the loop finishes without returning";
            }
            else
            {
                when = "it reaches its last line";
            }

            yield return new LogicFinding(id, header + 1,
                $"`{def.Groups["name"].Value}` returns a value on some paths, but when {when} it gives back None",
                null)
            {
                Confidence = when.StartsWith("the loop", StringComparison.Ordinal) ? Confidence.Possible : Confidence.Likely,
            };
        }
    }

    private static bool EndsInReturn(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int branch)
    {
        var (first, end) = Body(lines, branch);
        var inner = Statements(masked, first, end).ToList();
        if (inner.Count == 0) return false;

        var indent = Indent(lines[inner[0]]).Length;
        var last = inner.Last(k => Indent(lines[k]).Length == indent);

        return Regex.IsMatch(masked[last], @"^\s*(?:return|raise)\b|^\s*(?:sys\.)?exit\s*\(");
    }

    // ------------------------------------------------------------------ average = total // count

    private static IEnumerable<LogicFinding> FloorDivisionAverage(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        string? function = null;

        for (var i = 0; i < masked.Count; i++)
        {
            if (FunctionHeader().Match(masked[i]) is { Success: true } def) function = def.Groups["name"].Value;
            else if (Indent(masked[i]).Length == 0 && masked[i].Trim().Length > 0) function = null;

            var at = masked[i].IndexOf("//", StringComparison.Ordinal);
            if (at < 0) continue;

            var assigned = Regex.Match(masked[i], @"^\s*(?<target>[A-Za-z_]\w*)\s*=");
            var named = assigned.Success ? assigned.Groups["target"].Value : Regex.IsMatch(masked[i], @"^\s*return\b") ? function ?? "" : "";

            if (!Regex.IsMatch(named, @"average|mean|avg", RegexOptions.IgnoreCase)) continue;

            var line = source.Lines[i];

            yield return new LogicFinding(id, i + 1,
                "`//` divides and rounds down to a whole number, so the average loses its fraction - 7 // 2 is 3, not 3.5",
                Replace(id, "Divide with / to keep the fraction",
                    "In Python 3 `/` always gives the exact answer, fraction and all, and `//` rounds it down. An average usually needs the fraction.",
                    source, i + 1, line[..at] + "/" + line[(at + 2)..]));
        }
    }

    // ------------------------------------------------------------------ def add(self): count = self.count + 1

    private static IEnumerable<LogicFinding> AttributeNotSet(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;
        var attributes = masked.SelectMany(l => Regex.Matches(l, @"\bself\.(?<name>[A-Za-z_]\w*)\s*(?:[-+*/]|//)?=(?!=)")).Select(m => m.Groups["name"].Value).ToHashSet();

        for (var header = 0; header < masked.Count; header++)
        {
            if (!Regex.IsMatch(masked[header], @"^\s*def\s+\w+\s*\(\s*self\b")) continue;

            var (first, end) = Body(lines, header);

            for (var k = first; k < end; k++)
            {
                var assignment = Regex.Match(masked[k], @"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*=(?!=)\s*(?<value>.*\bself\.\k<name>\b.*)$");
                if (!assignment.Success) continue;

                var name = assignment.Groups["name"].Value;
                if (!attributes.Contains(name)) continue;

                var n = Regex.Escape(name);
                if (Enumerable.Range(k + 1, end - k - 1).Any(later => Regex.IsMatch(masked[later], $@"(?<![\w.]){n}(?!\w)"))) continue;

                var line = lines[k];
                var at = assignment.Groups["name"].Index;

                yield return new LogicFinding(id, k + 1,
                    $"`{name} = ...` makes a new local variable that is thrown away when the method returns, so `self.{name}` is never changed",
                    Replace(id, $"Store it on the object: self.{name} = ...",
                        $"Inside a method, a bare name is a local variable. `self.{name}` is the object's own value, and only assigning to `self.{name}` " +
                        "changes it.",
                        source, k + 1, line[..at] + "self." + line[at..]));
            }
        }
    }

    // ------------------------------------------------------------------ return print(total)

    private static IEnumerable<LogicFinding> ReturnPrint(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(source.Lines[i], @"^(?<lead>\s*)return\s+print\((?<value>[^,]+)\)\s*$") is not { Success: true } match) continue;
            if (!Regex.IsMatch(masked[i], @"^\s*return\s+print\(")) continue;

            var lead = match.Groups["lead"].Value;
            var value = match.Groups["value"].Value.Trim();

            yield return new LogicFinding(id, i + 1,
                "`return print(...)` returns what print gives back, which is always None - not the value printed",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Print the value, then return it: return {value}",
                    Explanation = "print shows a value and returns None. Returning its result hands None to the caller. Printing and returning as two " +
                                  "statements does both.",
                    File = source.Path, StartLine = i + 1, RemoveCount = 1,
                    NewLines = [$"{lead}print({value})", $"{lead}return {value}"],
                });
        }
    }

    // ------------------------------------------------------------------ count + 1 on a line of its own

    private static IEnumerable<LogicFinding> StatementHasNoEffect(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var open = OpenBrackets(masked);

        for (var i = 0; i < masked.Count; i++)
        {
            if (open[i] != 0 || (i > 0 && source.Lines[i - 1].TrimEnd().EndsWith('\\'))) continue;
            if (Regex.Match(masked[i], @"^(?<lead>\s*)(?!(?:return|yield|await|raise|del|assert|not|lambda|print|else|elif|if|while|for|in|is|and|or)\b)(?<target>[A-Za-z_][\w.]*(?:\[[^\]]*\])?)\s*(?<op>[-+*/]|//)\s*(?<value>[\w.]+)\s*$") is not { Success: true } statement) continue;

            var line = source.Lines[i];
            var target = statement.Groups["target"].Value;
            var op = statement.Groups["op"].Value;
            var value = Original(source, i, statement.Groups["value"]);

            yield return new LogicFinding(id, i + 1,
                $"`{target} {op} {value}` works out a new value and throws it away, so `{target}` does not change",
                Replace(id, $"Store the result: {target} {op}= {value}",
                    $"On a line of its own `{target} {op} {value}` calculates an answer that nothing keeps. `{target} {op}= {value}` changes " +
                    $"`{target}` itself.",
                    source, i + 1, $"{statement.Groups["lead"].Value}{target} {op}= {value}{CodeText.SplitComment(line, Syntax.Python).Tail}"));
        }
    }

    // ------------------------------------------------------------------ sum = 0 ... sum(values)

    private static readonly string[] Builtins =
    [
        "list", "dict", "str", "int", "float", "sum", "max", "min", "len", "input", "print", "set", "tuple", "type", "range",
        "sorted", "open", "id", "map", "filter", "zip", "any", "all", "abs", "round", "next", "iter", "format", "bool", "chr", "ord",
    ];

    private static IEnumerable<LogicFinding> ShadowedBuiltin(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var builtins = Builtins.ToHashSet(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < masked.Count; i++)
        {
            var named = new List<string>();

            if (Regex.Match(masked[i], @"^\s*(?<name>[A-Za-z_]\w*)\s*(?:[-+*/]|//)?=(?!=)|^\s*for\s+(?<name>[A-Za-z_]\w*)\s+in\b") is { Success: true } assigned &&
                builtins.Contains(assigned.Groups["name"].Value))
                named.Add(assigned.Groups["name"].Value);

            var isParameter = false;

            if (Regex.Match(masked[i], @"^\s*(?:async\s+)?def\s+\w+\s*\((?<parameters>[^)]*)\)") is { Success: true } def)
            {
                isParameter = true;
                named.AddRange(def.Groups["parameters"].Value.Split(',')
                    .Select(p => p.Split('=', ':')[0].Trim().TrimStart('*'))
                    .Where(builtins.Contains));
            }

            foreach (var name in named)
            {
                if (!reported.Add(name)) continue;

                var (from, to) = isParameter ? Body(source.Lines, i) : (i + 1, masked.Count);
                var called = Enumerable.Range(from, Math.Max(0, to - from))
                    .FirstOrDefault(k => Regex.IsMatch(masked[k], $@"(?<![\w.]){name}\s*\(") && !Regex.IsMatch(masked[k], @"^\s*def\s"), -1);

                if (called < 0 && isParameter) continue;

                yield return new LogicFinding(id, i + 1,
                    called >= 0
                        ? $"`{name}` is also the name of Python's own `{name}()`, and this line replaces it - so calling `{name}(...)` on line {called + 1} fails"
                        : $"`{name}` is also the name of Python's own `{name}()`, and this line hides it for the rest of the program",
                    isParameter ? null : Rename(id, source, masked, name, i))
                {
                    Severity = called >= 0 ? Severity.Error : Severity.Suggestion,
                    Confidence = called >= 0 ? Confidence.Certain : Confidence.Likely,
                    Kind = called >= 0 ? FindingKind.Logic : FindingKind.Style,
                };
            }
        }
    }

    private static readonly Dictionary<string, string> NewNames = new()
    {
        ["list"] = "items", ["dict"] = "table", ["str"] = "text", ["sum"] = "total", ["max"] = "largest", ["min"] = "smallest",
        ["input"] = "answer", ["len"] = "length", ["type"] = "kind", ["id"] = "identifier", ["set"] = "values", ["tuple"] = "pair",
        ["map"] = "mapping", ["filter"] = "chosen", ["open"] = "opened", ["format"] = "layout", ["next"] = "following",
        ["iter"] = "iterator", ["round"] = "rounded", ["abs"] = "size", ["all"] = "everything", ["any"] = "something", ["zip"] = "pairs",
        ["chr"] = "character", ["ord"] = "code", ["bool"] = "flag", ["int"] = "number", ["float"] = "real", ["print"] = "printer",
        ["range"] = "span", ["sorted"] = "ordered",
    };

    /// <summary>The variable renamed from the line that names it to its last use, leaving every call of the built-in alone.</summary>
    private static LocalFix? Rename(string id, SourceFile source, IReadOnlyList<string> masked, string name, int from)
    {
        if (!NewNames.TryGetValue(name, out var replacement) || DefinedNames(masked).Contains(replacement)) return null;

        var use = new Regex($@"(?<![\w.]){Regex.Escape(name)}(?![\w(])(?!\s*\()");
        var last = Enumerable.Range(from, masked.Count - from).LastOrDefault(k => use.IsMatch(masked[k]), -1);
        if (last < 0) return null;

        var renamed = Enumerable.Range(from, last - from + 1).Select(k =>
        {
            var line = source.Lines[k];
            foreach (var hit in use.Matches(masked[k]).Reverse()) line = line[..hit.Index] + replacement + line[(hit.Index + hit.Length)..];
            return line;
        }).ToList();

        return new LocalFix
        {
            RuleId = id,
            Title = $"Rename the variable to {replacement}",
            Explanation = $"Giving the variable a name of its own - `{replacement}` - leaves Python's `{name}()` working for the rest of the program. " +
                          $"Every use of the variable is renamed; every call of `{name}(...)` is left as it was.",
            File = source.Path, StartLine = from + 1, RemoveCount = last - from + 1, NewLines = renamed,
        };
    }

    // ------------------------------------------------------------------ code after return

    private static IEnumerable<LogicFinding> UnreachableCode(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;
        var open = OpenBrackets(masked);

        for (var i = 0; i < masked.Count; i++)
        {
            if (open[i] != 0 || !Regex.IsMatch(masked[i], @"^\s*(?:return\b|break\s*$|continue\s*$|raise\b|sys\.exit\s*\(|exit\s*\(|quit\s*\()")) continue;
            if (Indent(lines[i]).Length == 0 && Regex.IsMatch(masked[i], @"^(?:return|break|continue)\b")) continue;
            if (masked[i].TrimEnd().EndsWith('\\') || masked[i].Count(c => c is '(' or '[' or '{') > masked[i].Count(c => c is ')' or ']' or '}')) continue;

            var next = i + 1;
            while (next < masked.Count && masked[next].Trim().Length == 0) next++;
            if (next >= masked.Count) continue;

            if (Indent(lines[next]).Length != Indent(lines[i]).Length) continue;
            if (Regex.IsMatch(masked[next], @"^\s*(?:else|elif|except|finally|case)\b|^\s*(?:def|class|@)")) continue;

            yield return new LogicFinding(id, next + 1,
                $"this line comes straight after `{lines[i].Trim()}` in the same block, so it can never run",
                null);
        }
    }

    // ------------------------------------------------------------------ if x > 5: ... elif x > 5:

    private static IEnumerable<LogicFinding> DuplicateCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^(?<lead>\s*)if\s+(?<condition>.+):\s*$") is not { Success: true } first) continue;

            var indent = first.Groups["lead"].Value.Length;
            var seen = new Dictionary<string, int> { [Normalise(lines[i], first.Groups["condition"])] = i };

            for (var k = Body(lines, i).End; k < masked.Count; k = Body(lines, k).End)
            {
                while (k < masked.Count && masked[k].Trim().Length == 0) k++;
                if (k >= masked.Count || Indent(lines[k]).Length != indent) break;

                if (Regex.Match(masked[k], @"^\s*elif\s+(?<condition>.+):\s*$") is not { Success: true } elif) break;

                var condition = Normalise(lines[k], elif.Groups["condition"]);

                if (seen.TryGetValue(condition, out var earlier))
                {
                    yield return new LogicFinding(id, k + 1,
                        $"this `elif` checks the same thing as line {earlier + 1}, so whenever it is true the earlier branch has already run and this one never does",
                        null);
                }
                else
                {
                    seen[condition] = k;
                }
            }
        }
    }

    private static string Normalise(string line, Group condition) =>
        Regex.Replace(line.Substring(condition.Index, condition.Length), @"\s+", "");

    // ------------------------------------------------------------------ while True: with no way out

    private static IEnumerable<LogicFinding> EndlessWhileTrue(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var header = 0; header < masked.Count; header++)
        {
            if (!Regex.IsMatch(masked[header], @"^\s*while\s+(?:True|1)\s*:\s*$")) continue;

            var (first, end) = Body(source.Lines, header);
            var body = Enumerable.Range(first, Math.Max(0, end - first)).Select(k => masked[k]);

            if (body.Any(l => Regex.IsMatch(l, @"\b(?:break|return|raise|exit|quit|os\._exit)\b"))) continue;

            yield return new LogicFinding(id, header + 1,
                "nothing inside this `while True` loop can end it - there is no break, return, raise or exit - so the program never gets past it",
                null);
        }
    }

    // ------------------------------------------------------------------ for i in range(len(xs) - 1): print(xs[i])

    private static IEnumerable<LogicFinding> RangeSkipsLast(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var header = 0; header < masked.Count; header++)
        {
            var loop = Regex.Match(masked[header], @"^\s*for\s+(?<index>[A-Za-z_]\w*)\s+in\s+range\(\s*len\(\s*(?<collection>[A-Za-z_][\w.]*)\s*\)\s*(?<minus>-\s*1)\s*\)\s*:\s*$");
            if (!loop.Success) continue;

            var index = Regex.Escape(loop.Groups["index"].Value);
            var collection = Regex.Escape(loop.Groups["collection"].Value);
            var (first, end) = Body(source.Lines, header);
            var body = string.Join("\n", Enumerable.Range(first, Math.Max(0, end - first)).Select(k => masked[k]));

            if (!Regex.IsMatch(body, $@"{collection}\s*\[\s*{index}\s*\]")) continue;
            if (Regex.IsMatch(body, $@"\[\s*{index}\s*[-+]\s*1\s*\]|\[\s*1\s*\+\s*{index}\s*\]")) continue;

            var line = source.Lines[header];
            var minus = loop.Groups["minus"];

            yield return new LogicFinding(id, header + 1,
                $"`range(len({loop.Groups["collection"].Value}) - 1)` stops one short, so the last item is never looked at",
                Replace(id, "Loop over every index: remove the - 1",
                    "`range(n)` already stops before n, so `range(len(items))` gives every index from the first to the last. Taking one more " +
                    "away skips the last item - only needed when the loop also reads the item after the current one.",
                    source, header + 1, (line[..minus.Index].TrimEnd() + line[(minus.Index + minus.Length)..])));
        }
    }

    // ------------------------------------------------------------------ except:

    private static IEnumerable<LogicFinding> BareExcept(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^(?<lead>\s*)except\s*:") is not { Success: true } match) continue;

            var line = source.Lines[i];
            var colon = line.IndexOf(':', match.Groups["lead"].Length);

            yield return new LogicFinding(id, i + 1,
                "a bare `except:` catches every error - a misspelt name, a wrong type, even Ctrl+C - so real mistakes are hidden behind whatever the handler does",
                Replace(id, "Catch only real errors: except Exception:",
                    "`except:` with nothing after it also catches KeyboardInterrupt and SystemExit, and every programming mistake in the try " +
                    "block. `except Exception:` still catches errors but lets the program be stopped; naming the error you expect, such as " +
                    "`except ValueError:`, is better still.",
                    source, i + 1, line[..colon].TrimEnd() + " Exception" + line[colon..]));
        }
    }

    // ------------------------------------------------------------------ except ValueError: pass

    private static IEnumerable<LogicFinding> SilentExcept(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^(?<lead>\s*)except\b(?<what>[^:]*):\s*$") is not { Success: true } handler) continue;

            var (first, end) = Body(lines, i);
            var body = Statements(masked, first, end).ToList();
            if (body is not [var only] || masked[only].Trim() != "pass") continue;

            var what = handler.Groups["what"].Value.Trim();
            var variable = Regex.Match(what, @"\bas\s+(?<name>\w+)$") is { Success: true } named ? named.Groups["name"].Value : "error";
            var header = Regex.IsMatch(what, @"\bas\s+\w+$") ? lines[i] : $"{handler.Groups["lead"].Value}except {(what.Length > 0 ? what : "Exception")} as {variable}:";

            yield return new LogicFinding(id, i + 1,
                "this handler catches an error and does nothing with it, so when something goes wrong the program carries on as if it had not",
                new LocalFix
                {
                    RuleId = id,
                    Title = "Say what went wrong instead of passing",
                    Explanation = "An `except` block that only says `pass` hides the error completely - the program goes on with whatever values it " +
                                  "had, and nothing tells you why the result is wrong. Printing the error at least shows that it happened and what it was.",
                    File = source.Path, StartLine = i + 1, RemoveCount = only - i + 1,
                    NewLines = [header, .. lines.Skip(i + 1).Take(only - i - 1), $"{Indent(lines[only])}print(\"Something went wrong:\", {variable})"],
                });
        }
    }

    // ------------------------------------------------------------------ class Basket: items = []

    private static IEnumerable<LogicFinding> SharedClassList(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;

        for (var header = 0; header < masked.Count; header++)
        {
            if (!Regex.IsMatch(masked[header], @"^\s*class\s+\w+")) continue;

            var (first, end) = Body(lines, header);
            var memberIndent = first < end ? Indent(lines[Statements(masked, first, end).FirstOrDefault(first)]).Length : -1;
            var body = string.Join("\n", Enumerable.Range(first, Math.Max(0, end - first)).Select(k => masked[k]));

            foreach (var k in Statements(masked, first, end))
            {
                if (Indent(lines[k]).Length != memberIndent) continue;
                if (Regex.Match(masked[k], @"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?:\[\s*\]|\{\s*\}|list\(\s*\)|dict\(\s*\)|set\(\s*\))\s*$") is not { Success: true } shared) continue;

                var name = Regex.Escape(shared.Groups["name"].Value);
                if (!Regex.IsMatch(body, $@"self\.{name}\s*(?:\.\s*(?:append|extend|insert|add|update|pop|remove|clear|setdefault)\s*\(|\[[^\]]*\]\s*=(?!=))")) continue;
                if (Regex.IsMatch(body, $@"self\.{name}\s*=(?!=)")) continue;

                var hasInit = Regex.IsMatch(body, @"\bdef\s+__init__\s*\(");
                var lead = Indent(lines[k]);
                var step = PythonCode.IndentUnit(lines);

                yield return new LogicFinding(id, k + 1,
                    $"`{shared.Groups["name"].Value}` belongs to the class, not to each object, so every object adds to the same one",
                    hasInit
                        ? null
                        : new LocalFix
                        {
                            RuleId = id,
                            Title = $"Give each object its own: self.{shared.Groups["name"].Value} in __init__",
                            Explanation = "A value written in the class body is made once and shared by every object of the class. Made in " +
                                          "`__init__`, which runs once for each new object, every object gets a list of its own.",
                            File = source.Path, StartLine = k + 1, RemoveCount = 1,
                            NewLines = [$"{lead}def __init__(self):", $"{lead}{step}self.{lines[k].Trim()}"],
                        });
            }
        }
    }

    // ------------------------------------------------------------------ x == None

    private static IEnumerable<LogicFinding> NoneComparison(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var matches = Regex.Matches(masked[i], @"\s*(?<op>==|!=)\s*(?=None\b)").ToList();
            if (matches.Count == 0) continue;

            var line = source.Lines[i];
            var corrected = matches.OrderByDescending(m => m.Index)
                .Aggregate(line, (text, m) => text[..m.Index] + (m.Groups["op"].Value == "==" ? " is " : " is not ") + text[(m.Index + m.Length)..]);

            yield return new LogicFinding(id, i + 1,
                "`None` is compared with `==`, which a class can change the meaning of; `is None` checks for None itself",
                Replace(id, "Compare with None using is", "There is only one None, so `is None` asks exactly the right question and cannot be fooled by a class's own `==`.",
                    source, i + 1, corrected));
        }
    }

    // ------------------------------------------------------------------ if done == True:

    private static IEnumerable<LogicFinding> BoolComparison(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"^(?<lead>\s*(?:if|elif|while)\s+)(?<value>[^=!:]+?)\s*(?<op>==|!=)\s*(?<bool>True|False)\s*:");
            if (!match.Success) continue;

            var line = source.Lines[i];
            var value = Original(source, i, match.Groups["value"]).Trim();
            var positive = (match.Groups["op"].Value == "==") == (match.Groups["bool"].Value == "True");
            var condition = positive ? value : $"not {value}";
            var colon = line.IndexOf(':', match.Groups["bool"].Index);

            yield return new LogicFinding(id, i + 1,
                $"comparing with `{match.Groups["bool"].Value}` is not needed - `{value}` is already true or false",
                Replace(id, $"Use the value itself: {match.Groups["lead"].Value.Trim()} {condition}:",
                    $"`if {value}:` already asks whether `{value}` is true. Adding `== True` repeats the question, and fails for values that " +
                    "count as true without being exactly True, such as a non-empty list.",
                    source, i + 1, match.Groups["lead"].Value + condition + line[colon..]));
        }
    }

    // ------------------------------------------------------------------ type(x) == int

    private static IEnumerable<LogicFinding> TypeComparison(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            var match = Regex.Match(masked[i], @"\btype\((?<value>[^()]+)\)\s*(?<op>==|!=|is not|is)\s*(?<type>int|str|float|list|dict|tuple|set|bool)\b");
            if (!match.Success) continue;

            var line = source.Lines[i];
            var value = Original(source, i, match.Groups["value"]).Trim();
            var type = match.Groups["type"].Value;
            var negated = match.Groups["op"].Value is "!=" or "is not";
            var check = $"{(negated ? "not " : "")}isinstance({value}, {type})";

            yield return new LogicFinding(id, i + 1,
                $"`type({value}) {match.Groups["op"].Value} {type}` is false for anything built on {type}; `isinstance` is the usual check",
                Replace(id, $"Check the type with isinstance: {check}",
                    $"`isinstance({value}, {type})` is true for a {type} and for anything that is a kind of {type} - True is an int, for example " +
                    "- and reads more plainly.",
                    source, i + 1, line[..match.Index] + check + line[(match.Index + match.Length)..]));
        }
    }

    // ------------------------------------------------------------------ for i in range(len(names)): print(names[i])

    private static IEnumerable<LogicFinding> RangeLenLoop(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var lines = source.Lines;
        var defined = DefinedNames(masked);

        for (var header = 0; header < masked.Count; header++)
        {
            var loop = Regex.Match(masked[header], @"^(?<lead>\s*)for\s+(?<index>[A-Za-z_]\w*)\s+in\s+range\(\s*len\(\s*(?<collection>[A-Za-z_]\w*)\s*\)\s*\)\s*:\s*$");
            if (!loop.Success) continue;

            var index = loop.Groups["index"].Value;
            var collection = loop.Groups["collection"].Value;
            var (first, end) = Body(lines, header);
            if (end <= first) continue;

            var bodyText = string.Join("\n", Enumerable.Range(first, end - first).Select(k => masked[k]));
            var i = Regex.Escape(index);
            var c = Regex.Escape(collection);

            var uses = Regex.Matches(bodyText, $@"(?<![\w.]){i}(?!\w)").Count;
            var itemUses = Regex.Matches(bodyText, $@"(?<![\w.]){c}\s*\[\s*{i}\s*\]").Count;

            if (uses == 0 || uses != itemUses || Regex.IsMatch(bodyText, $@"{c}\s*\[\s*{i}\s*\]\s*(?:[-+*/]|//)?=(?!=)")) continue;

            var item = Singular(collection);
            if (item == collection || defined.Contains(item)) item = "item";
            if (defined.Contains(item)) continue;

            var rewritten = Enumerable.Range(first, end - first)
                .Select(k => Regex.Replace(lines[k], $@"(?<![\w.]){c}\s*\[\s*{i}\s*\]", item))
                .ToList();

            yield return new LogicFinding(id, header + 1,
                $"the loop counts through the positions of `{collection}` only to read `{collection}[{index}]` - it can loop over the items themselves",
                new LocalFix
                {
                    RuleId = id,
                    Title = $"Loop over the items: for {item} in {collection}:",
                    Explanation = $"`for {item} in {collection}:` gives each item in turn, so there is no index to get wrong and no `{collection}[{index}]` to " +
                                  "read. It is the usual way to loop in Python.",
                    File = source.Path, StartLine = header + 1, RemoveCount = end - header,
                    NewLines = [$"{loop.Groups["lead"].Value}for {item} in {collection}:", .. rewritten],
                });
        }
    }

    private static string Singular(string name) =>
        name.EndsWith("ies", StringComparison.Ordinal) && name.Length > 4 ? name[..^3] + "y"
        : name.EndsWith('s') && !name.EndsWith("ss", StringComparison.Ordinal) && name.Length > 3 ? name[..^1]
        : name;

    // ------------------------------------------------------------------ f = open(...) with no close

    private static IEnumerable<LogicFinding> FileNotClosed(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^\s*(?<name>[A-Za-z_]\w*)\s*=\s*open\(") is not { Success: true } opened) continue;

            var open = masked[i].IndexOf("open(", StringComparison.Ordinal) + 4;
            if (LocalFixes.Rules.Brackets.ClosingParenthesis(masked[i], open) is not { } close || masked[i][(close + 1)..].Trim().Length > 0) continue;

            var name = Regex.Escape(opened.Groups["name"].Value);
            if (masked.Any(l => Regex.IsMatch(l, $@"(?<![\w.]){name}\s*\.\s*close\s*\(|\bwith\s+{name}\b"))) continue;

            yield return new LogicFinding(id, i + 1,
                $"`{opened.Groups["name"].Value}` is opened and never closed, so what was written may not be saved and the file stays locked",
                null);
        }
    }
}
