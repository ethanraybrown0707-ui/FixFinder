using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// <c>cannot find symbol (symbol: variable args)</c> in a program whose main takes nothing - Java 25's <c>void main()</c> - and
/// still reads the command-line arguments: main is given them again as <c>String[] args</c>.
/// </summary>
public sealed partial class JavaMainArgs : ILocalFixRule
{
    public string Id => "java-main-args";

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+variable args\b")]
    private static partial Regex ArgsNotFound();

    [GeneratedRegex(@"^\s*(?:(?:public|static|final)\s+)*void\s+main\s*(?<open>\()\s*\)\s*(?:throws\s+[\w$., ]+)?\s*\{?\s*$")]
    private static partial Regex MainWithoutParameters();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || !ArgsNotFound().IsMatch(context.Error.Message ?? "")) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var mains = Enumerable.Range(0, masked.Count).Where(lineIndex => MainWithoutParameters().IsMatch(masked[lineIndex])).ToList();
        if (mains is not [var mainLine]) return null;

        var line = source.Lines[mainLine];
        var afterOpen = MainWithoutParameters().Match(masked[mainLine]).Groups["open"].Index + 1;
        var corrected = line[..afterOpen] + "String[] args" + line[afterOpen..].TrimStart();

        return LocalFix.ReplaceLine(
            Id, "Give main its arguments: String[] args",
            "Since Java 25 main can be written without its parameter - `void main()` - but then the command-line arguments have no " +
            $"name inside it, so `args` on line {at.Number} is a name javac does not know. Given `String[] args` again, main has them.",
            source.Path, mainLine + 1, corrected);
    }
}

/// <summary>
/// <c>not a statement</c> on the last line of a case's block in a switch that gives a value: the block hands its answer back
/// with <c>yield</c>, as a method does with <c>return</c>.
/// </summary>
public sealed partial class JavaMissingYield : ILocalFixRule
{
    public string Id => "java-missing-yield";

    [GeneratedRegex(@"^(?<indent>\s*)(?<value>[^;{}]+?)\s*;\s*$")]
    private static partial Regex ValueOnItsOwn();

    [GeneratedRegex(@"^\s*(?:case\b.+|default\s*)->\s*\{\s*$")]
    private static partial Regex CaseBlockOpens();

    [GeneratedRegex(@"(?:=|\breturn|\(|,|->)\s*switch\s*\(")]
    private static partial Regex SwitchGivesAValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "not a statement" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (ValueOnItsOwn().Match(masked[number - 1]) is not { Success: true } value) return null;

        var indent = value.Groups["indent"].Value.Length;

        // The block the line is the last of: the case above it that opens it, and its closing brace straight below.
        if (NearestLessIndented(masked, number - 1, indent, upwards: true) is not { } caseLine || !CaseBlockOpens().IsMatch(masked[caseLine])) return null;
        if (NearestLessIndented(masked, number - 1, indent, upwards: false) is not { } closeLine || masked[closeLine].Trim() != "}") return null;

        // The switch the case belongs to must give a value: int days = switch (month) {.
        var caseIndent = CodeText.Indentation(masked[caseLine]).Length;
        if (NearestLessIndented(masked, caseLine, caseIndent, upwards: true) is not { } switchLine || !SwitchGivesAValue().IsMatch(masked[switchLine])) return null;

        var answer = line.Substring(value.Groups["value"].Index, value.Groups["value"].Length).Trim();
        var corrected = value.Groups["indent"].Value + "yield " + answer + ";" + CodeText.SplitComment(line, Syntax.CLike).Tail;

        return LocalFix.ReplaceLine(
            Id, $"Hand the case's answer back: yield {answer};",
            "In a switch that gives a value, a case whose answer takes a block of lines hands it back with `yield` - the way a " +
            $"method does with `return`. Without it, `{answer};` works the answer out and keeps it nowhere, which javac calls not a statement.",
            source.Path, number, corrected);
    }

    /// <summary>The nearest line above or below that is not blank and is indented less than <paramref name="indent"/>.</summary>
    private static int? NearestLessIndented(IReadOnlyList<string> masked, int from, int indent, bool upwards)
    {
        for (var lineIndex = upwards ? from - 1 : from + 1; lineIndex >= 0 && lineIndex < masked.Count; lineIndex += upwards ? -1 : 1)
        {
            if (masked[lineIndex].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[lineIndex]).Length < indent) return lineIndex;
        }

        return null;
    }
}

/// <summary>
/// <c>different case kinds used in the switch</c> - a <c>case 2:</c> among cases written <c>case 1 -&gt;</c>. A one-line
/// case is written the way the switch's first case is: with an arrow.
/// </summary>
public sealed partial class JavaMixedCaseKinds : ILocalFixRule
{
    public string Id => "java-case-kinds";

    [GeneratedRegex(@"^(?<indent>\s*)(?<label>case\b[^:]+?|default)\s*:\s*(?<statement>[^;{}]+;)\s*(?:break\s*;)?\s*$")]
    private static partial Regex OneLineColonCase();

    [GeneratedRegex(@"^\s*(?:case\b.+?|default\s*)->")]
    private static partial Regex ArrowCase();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "different case kinds used in the switch" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (OneLineColonCase().Match(masked[number - 1]) is not { Success: true } colonCase) return null;

        // The switch's cases above it, up to the switch itself, are written with arrows.
        var arrowsAbove = false;
        for (var lineAbove = number - 2; lineAbove >= 0 && !Regex.IsMatch(masked[lineAbove], @"\bswitch\s*\("); lineAbove--)
            arrowsAbove |= ArrowCase().IsMatch(masked[lineAbove]);
        if (!arrowsAbove) return null;

        string Part(string group) => line.Substring(colonCase.Groups[group].Index, colonCase.Groups[group].Length);

        // After an arrow a case's value is written on its own: case 2: yield 28; becomes case 2 -> 28;.
        var statement = Regex.Replace(Part("statement"), @"^yield\s+", "");
        var corrected = $"{Part("indent")}{Part("label").TrimEnd()} -> {statement}";

        return LocalFix.ReplaceLine(
            Id, $"Write {Part("label").TrimEnd()} with an arrow, as the switch's other cases are",
            "A switch's cases are all written one way: with `->`, each runs only its own code; with `:`, each runs on into the next " +
            "unless it breaks. This switch's cases are written with `->`, so this one is too - and needs no `break`.",
            source.Path, number, corrected);
    }
}

/// <summary>
/// <c>this case label is dominated by a preceding case label</c> - <c>case String s</c> below <c>case Object o</c>, which
/// matches everything it would. The particular case is moved above the general one, as a switch tries its cases from the top.
/// </summary>
public sealed partial class JavaDominatedCase : ILocalFixRule
{
    public string Id => "java-dominated-case";

    [GeneratedRegex(@"^\s*case\s+(?<type>[A-Z][\w$.]*)\s+[\w$]+(?<guard>\s+when\s+.+?)?\s*->.*[;}]\s*$")]
    private static partial Regex OneLineTypeCase();

    /// <summary>The JDK's own types that a beginner's case puts above one of its subtypes.</summary>
    private static readonly Dictionary<string, string[]> KnownSubtypes = new(StringComparer.Ordinal)
    {
        ["CharSequence"] = ["String", "StringBuilder", "StringBuffer"],
        ["Number"] = ["Integer", "Long", "Double", "Float", "Short", "Byte"],
        ["Exception"] = ["RuntimeException", "IllegalArgumentException", "IllegalStateException", "NumberFormatException", "ArithmeticException"],
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "this case label is dominated by a preceding case label" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (OneLineTypeCase().Match(masked[number - 1]) is not { Success: true } dominated) return null;

        var type = dominated.Groups["type"].Value;
        var guarded = dominated.Groups["guard"].Success;

        // The case above it, in the same switch, that matches everything it does.
        int? dominatingLine = null;
        for (var lineAbove = number - 2; lineAbove >= 0 && !Regex.IsMatch(masked[lineAbove], @"\bswitch\s*\("); lineAbove--)
        {
            if (OneLineTypeCase().Match(masked[lineAbove]) is not { Success: true } above || above.Groups["guard"].Success) continue;

            var general = above.Groups["type"].Value;
            if (general == "Object" || (general == type && guarded) || IsSupertype(general, type, masked)) dominatingLine = lineAbove;
        }

        if (dominatingLine is not { } first) return null;

        var moved = new List<string> { line };
        moved.AddRange(source.Lines.Skip(first).Take(number - 1 - first));
        var generalType = OneLineTypeCase().Match(masked[first]).Groups["type"].Value;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Put case {type} above case {generalType}",
            Explanation = $"A switch tries its cases from the top, and `case {generalType}` matches everything `case {type}` would - so " +
                          $"`case {type}` could never be reached. The more particular case goes first.",
            File = source.Path,
            StartLine = first + 1,
            RemoveCount = number - first,
            NewLines = moved,
        };
    }

    /// <summary>Whether <paramref name="general"/> is above <paramref name="particular"/>: one of the JDK's, or by this file's own extends and implements.</summary>
    private static bool IsSupertype(string general, string particular, IReadOnlyList<string> masked)
    {
        if (KnownSubtypes.TryGetValue(general, out var subtypes) && subtypes.Contains(particular)) return true;

        // record Circle(double r) implements Shape, class Dog extends Animal - followed up the file's own types.
        var reached = new HashSet<string>(StringComparer.Ordinal) { particular };
        var toFollow = new Queue<string>([particular]);

        while (toFollow.TryDequeue(out var below))
        {
            foreach (var text in masked)
            {
                if (Regex.Match(text, $@"\b(?:class|record|interface|enum)\s+{Regex.Escape(below)}\b[^{{]*?\b(?:extends|implements)\s+(?<parents>[\w$.,<>\s]+?)\s*(?:\{{|permits\b|$)")
                    is not { Success: true } declared) continue;

                foreach (var parent in Regex.Split(declared.Groups["parents"].Value, @"\s*(?:,|\bimplements\b|\bextends\b)\s*").Select(name => name.Trim()))
                {
                    var bare = Regex.Replace(parent, @"<.*$", "");
                    if (bare == general) return true;
                    if (bare.Length > 0 && reached.Add(bare)) toFollow.Enqueue(bare);
                }
            }
        }

        return false;
    }
}

/// <summary>
/// <c>illegal text block open delimiter sequence, missing line terminator</c> - a text block on one line, <c>"""Roses are
/// red"""</c>. A text block's text starts on the line after its opening quotes; text on one line is an ordinary string.
/// </summary>
public sealed partial class JavaOneLineTextBlock : ILocalFixRule
{
    public string Id => "java-one-line-text-block";

    [GeneratedRegex("\"\"\"(?<text>[^\"\\\\]*)\"\"\"")]
    private static partial Regex TextBlockOnOneLine();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "illegal text block open delimiter sequence, missing line terminator" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var blocks = TextBlockOnOneLine().Matches(line);
        if (blocks.Count == 0) return null;

        var corrected = TextBlockOnOneLine().Replace(line, block => "\"" + block.Groups["text"].Value + "\"");

        return LocalFix.ReplaceLine(
            Id, "Write the text as an ordinary string",
            "A text block - three quotes - is for text over several lines, and its text starts on the line after the opening " +
            "quotes. Text that fits on one line is an ordinary string, in one pair of quotes.",
            source.Path, number, corrected);
    }
}

/// <summary>
/// <c>cannot infer type for local variable total</c> at <c>var total;</c> with its value given on the next line: <c>var</c>
/// takes its type from the value it is declared with, so the two lines become one.
/// </summary>
public sealed partial class JavaVarWithoutValue : ILocalFixRule
{
    public string Id => "java-var-without-value";

    [GeneratedRegex(@"^cannot infer type for local variable (?<name>[\w$]+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var name = Regex.Escape(message.Groups["name"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Regex.Match(masked[number - 1], $@"^(?<indent>\s*)(?<final>final\s+)?var\s+{name}\s*;\s*$") is not { Success: true } declared) return null;
        if (number >= masked.Count || Regex.Match(masked[number], $@"^(?<indent>\s*){name}\s*=(?!=)\s*(?<value>.+?)\s*;\s*$") is not { Success: true } given) return null;
        if (given.Groups["indent"].Value != declared.Groups["indent"].Value) return null;

        var nextLine = source.Lines[number];
        var value = nextLine.Substring(given.Groups["value"].Index, given.Groups["value"].Length);
        var joined = $"{declared.Groups["indent"].Value}{declared.Groups["final"].Value}var {message.Groups["name"].Value} = {value};";

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Give {message.Groups["name"].Value} its value where it is declared",
            Explanation = "`var` takes a variable's type from the value it is given where it is declared, so it needs one there - " +
                          "with none, javac cannot tell what type it is. The value from the next line goes on the declaration.",
            File = source.Path,
            StartLine = number,
            RemoveCount = 2,
            NewLines = [joined],
        };
    }
}

/// <summary>
/// <c>the switch expression does not cover all possible input values</c> for a switch over numbers, text or an enum's
/// constants: a <c>default</c> that says which value it was not ready for. A switch over a sealed type, whose missing case
/// is a type of its own, is left alone.
/// </summary>
public sealed partial class JavaSwitchDefault : ILocalFixRule
{
    public string Id => "java-switch-default";

    [GeneratedRegex(@"\bswitch\s*\(\s*(?<selector>[\w$.]+(?:\(\))?)\s*\)\s*\{\s*$")]
    private static partial Regex SwitchOpens();

    [GeneratedRegex(@"^(?<indent>\s*)case\s+(?<labels>.+?)\s*->")]
    private static partial Regex ArrowCase();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "the switch expression does not cover all possible input values" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (SwitchOpens().Match(masked[number - 1]) is not { Success: true } opens) return null;

        // The switch's cases, to the brace that closes it.
        var depth = 0;
        int? closeLine = null;
        string? caseIndent = null;

        for (var lineIndex = number - 1; lineIndex < masked.Count && closeLine is null; lineIndex++)
        {
            if (lineIndex > number - 1 && depth == 1 && ArrowCase().Match(masked[lineIndex]) is { Success: true } arrowCase)
            {
                // A case of a type - case Circle c, case Point(var x, var y) - is a sealed type's, whose missing case is a type.
                if (Regex.IsMatch(arrowCase.Groups["labels"].Value, @"^[A-Z][\w$.<>]*\s*(?:\(|[\w$]+\b)") && !Regex.IsMatch(arrowCase.Groups["labels"].Value, @"^[A-Z][A-Z0-9_]*\s*(?:,|$)"))
                    return null;
                caseIndent ??= arrowCase.Groups["indent"].Value;
            }

            if (lineIndex > number - 1 && depth == 1 && Regex.IsMatch(masked[lineIndex], @"^\s*(?:case\b.*?|default\s*):")) return null;

            foreach (var character in masked[lineIndex])
            {
                if (character == '{') depth++;
                else if (character == '}' && --depth == 0) { closeLine = lineIndex; break; }
            }
        }

        if (closeLine is not { } close || caseIndent is null || !masked[close].TrimStart().StartsWith('}')) return null;

        var selector = opens.Groups["selector"].Value;

        return LocalFix.Insert(
            Id, "Add a default for the values no case is for",
            $"A switch that gives a value has to give one for every value it can be handed, and `{selector}` can be values none of " +
            "these cases is for. A `default` covers the rest: here it stops the program and says which value came. If another " +
            "value has an answer of its own, give it its own case instead.",
            source.Path, close + 1, [$"{caseIndent}default -> throw new IllegalArgumentException(\"Unexpected value: \" + {selector});"]);
    }
}
