using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Elixir rules share: whether an error is Elixir's, and the line it is on.</summary>
internal static class ElixirCode
{
    public static bool IsElixir(SourceFile source) => Path.GetExtension(source.Path).ToLowerInvariant() is ".exs" or ".ex";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "elixir" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsElixir(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>Whether Elixir gave this error, of this kind.</summary>
    public static bool Is(LocalFixContext context, string kind) => context.Error.LanguageId == "elixir" && context.Error.ExceptionType == kind;

    /// <summary>The code of a line with its comments and the insides of its strings blanked out.</summary>
    public static string Masked(string line) => CodeText.Mask(line, Syntax.Python);
}

/// <summary>
/// "undefined variable "totl"": a name the code never gives a value to, within two letters of one it does - and of only one
/// - becomes that one, on the line and at the column Elixir names.
/// </summary>
public sealed partial class ElixirNearestVariable : ILocalFixRule
{
    public string Id => "elixir-nearest-variable";

    [GeneratedRegex(@"^undefined variable ""(?<name>\w+)""$")]
    private static partial Regex Message();

    /// <summary>
    /// A name the code gives a value to: on the left of an =, a function's or an fn's parameters, and a for's variable.
    /// </summary>
    [GeneratedRegex(@"^\s*(?<assigned>[a-z_]\w*)\s*=(?![=~>])|\bdefp?\s+[a-z_]\w*[?!]?\((?<parameters>[^)]*)\)|\bfn\s+(?<fnParameters>[a-z_][\w\s,]*?)\s*->|\b(?<generated>[a-z_]\w*)\s*<-", RegexOptions.Multiline)]
    private static partial Regex GivenAValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, ElixirParser.CompileError) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (ElixirCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match given in GivenAValue().Matches(string.Join("\n", CodeText.MaskAll(at.Source.Lines, Syntax.Python))))
        {
            foreach (var group in new[] { "assigned", "generated" })
                if (given.Groups[group].Success) named.Add(given.Groups[group].Value);

            foreach (var group in new[] { "parameters", "fnParameters" })
                foreach (var parameter in given.Groups[group].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (Regex.Match(parameter, @"^[a-z_]\w*") is { Success: true } name) named.Add(name.Value);
        }

        if (CodeText.Nearest(wrong, named) is not { } right) return null;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.Python, context.Frame?.Column - 1) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"Elixir knows no variable called {wrong} here, so it would not compile the program. {right}, a letter or two apart, is " +
            "a name the code does give a value to.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "undefined function avrage/1": a function the file does not define, within two letters of one it defines that takes as
/// many arguments - and of only one - becomes that one, on the line and at the column Elixir names.
/// </summary>
public sealed partial class ElixirNearestFunction : ILocalFixRule
{
    public string Id => "elixir-nearest-function";

    [GeneratedRegex(@"^undefined function (?<name>[a-z_]\w*[?!]?)/(?<arity>\d+) \(")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bdefp?\s+(?<name>[a-z_]\w*[?!]?)(?:\((?<parameters>[^)]*)\))?")]
    private static partial Regex Definition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, ElixirParser.CompileError) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (ElixirCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var arity = int.Parse(said.Groups["arity"].Value);

        var defined = CodeText.MaskAll(at.Source.Lines, Syntax.Python)
            .SelectMany(line => Definition().Matches(line))
            .Where(definition => ArityOf(definition.Groups["parameters"].Value) == arity)
            .Select(definition => definition.Groups["name"].Value);

        if (CodeText.Nearest(wrong, defined) is not { } right) return null;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.Python, context.Frame?.Column - 1) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"There is no function called {wrong} taking {arity} argument{(arity == 1 ? "" : "s")} here, so Elixir would not compile the " +
            $"program. {right}, a letter or two apart, is a function the file defines.",
            at.Source.Path, at.Number, corrected);
    }

    private static int ArityOf(string parameters) => parameters.Trim().Length == 0 ? 0 : parameters.Split(',').Length;
}

/// <summary>
/// "function Enum.summ/1 is undefined or private. Did you mean: * sum/1": Elixir's own suggestion - when it makes only one
/// - for the call on the line it stopped at.
/// </summary>
public sealed partial class ElixirDidYouMeanFunction : ILocalFixRule
{
    public string Id => "elixir-did-you-mean-function";

    [GeneratedRegex(@"^function (?<module>[A-Z][\w.]*)\.(?<name>[a-z_]\w*[?!]?)/(?<arity>\d+) is undefined or private\. Did you mean:")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*\* (?<name>[a-z_]\w*[?!]?)/\d+\s*$", RegexOptions.Multiline)]
    private static partial Regex Suggestion();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "UndefinedFunctionError") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (Suggestion().Matches(context.Error.RawText) is not [var suggested] || ElixirCode.Locate(context) is not { } at) return null;

        var module = said.Groups["module"].Value;
        var wrong = said.Groups["name"].Value;
        var right = suggested.Groups["name"].Value;

        if (new Regex($@"\b{Regex.Escape(module)}\.{Regex.Escape(wrong)}(?=\s*\()").Matches(ElixirCode.Masked(at.Line)) is not [var call]) return null;

        var nameAt = call.Index + module.Length + 1;
        return LocalFix.ReplaceLine(Id, $"Change {module}.{wrong} to {module}.{right}",
            $"{module} has no function called {wrong}. Elixir itself compared it with the functions {module} has, and suggested {right}.",
            at.Source.Path, at.Number, at.Line[..nameAt] + right + at.Line[(nameAt + wrong.Length)..]);
    }
}

/// <summary>
/// "key :agee not found in: ... Did you mean: * :age": Elixir's own suggestion - when it makes only one - for the key on
/// the line it stopped at.
/// </summary>
public sealed partial class ElixirDidYouMeanKey : ILocalFixRule
{
    public string Id => "elixir-did-you-mean-key";

    [GeneratedRegex(@"^key :(?<name>\w+) not found")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*\* :(?<name>\w+)\s*$", RegexOptions.Multiline)]
    private static partial Regex Suggestion();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "KeyError") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (Suggestion().Matches(context.Error.RawText) is not [var suggested] || ElixirCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var right = suggested.Groups["name"].Value;

        // The key as the line writes it: person.agee, or :agee.
        if (new Regex($@"(?<=[\w)\]]\.|:){Regex.Escape(wrong)}\b(?!\s*\()").Matches(ElixirCode.Masked(at.Line)) is not [var key]) return null;

        return LocalFix.ReplaceLine(Id, $"Change the key {wrong} to {right}",
            $"The map has no key :{wrong}. Elixir itself compared it with the keys the map has, and suggested :{right}.",
            at.Source.Path, at.Number, at.Line[..key.Index] + right + at.Line[(key.Index + key.Length)..]);
    }
}

/// <summary>
/// "bad argument in arithmetic expression: "Total: " + 10": + adds numbers only, and text is joined with &lt;&gt; - the one +
/// beside text on the line becomes &lt;&gt;, with the other side turned into text.
/// </summary>
public sealed partial class ElixirJoinText : ILocalFixRule
{
    public string Id => "elixir-join-text";

    [GeneratedRegex(@"^bad argument in arithmetic expression: (?:"".*"" \+ .+|.+ \+ "".*"")$")]
    private static partial Regex Message();

    private const string Text = @"""(?:[^""\\]|\\.)*""";
    private const string Value = @"[a-z_][\w.]*(?:\([^()]*\))?|\d+(?:\.\d+)?";

    [GeneratedRegex(@"(?<text>" + Text + @")\s*\+\s*(?<value>" + Value + ")")]
    private static partial Regex TextPlusValue();

    [GeneratedRegex(@"(?<value>" + Value + @")\s*\+\s*(?<text>" + Text + ")")]
    private static partial Regex ValuePlusText();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "ArithmeticError") || !Message().IsMatch(context.Error.Message ?? "") || ElixirCode.Locate(context) is not { } at) return null;

        var textFirst = TextPlusValue().Matches(at.Line);
        var valueFirst = ValuePlusText().Matches(at.Line);
        if (textFirst.Count + valueFirst.Count != 1) return null;

        var (joined, found) = textFirst.Count == 1
            ? ($"{textFirst[0].Groups["text"].Value} <> to_string({textFirst[0].Groups["value"].Value})", textFirst[0])
            : ($"to_string({valueFirst[0].Groups["value"].Value}) <> {valueFirst[0].Groups["text"].Value}", valueFirst[0]);

        return LocalFix.ReplaceLine(Id, "Join the text with <>",
            "In Elixir + adds numbers only, and text is joined with <>. to_string turns the other side into text first, as <> joins " +
            "text only.",
            at.Source.Path, at.Number, at.Line[..found.Index] + joined + at.Line[(found.Index + found.Length)..]);
    }
}

/// <summary>
/// "bad argument in arithmetic expression", from dividing by zero: Elixir says no more for /, and names the call for div
/// and rem - div(10, 0). The one division on the line is given an answer, 0, for when what it divides by is zero.
/// </summary>
public sealed partial class ElixirDivisionGuard : ILocalFixRule
{
    public string Id => "elixir-division-guard";

    [GeneratedRegex(@"^bad argument in arithmetic expression(?:: (?:div|rem)\(.+, 0\))?$")]
    private static partial Regex Message();

    private const string Operand = @"(?:[A-Za-z_][\w.]*(?:\((?:[^()]|\([^()]*\))*\))?|\d+(?:\.\d+)?)";

    [GeneratedRegex(@"(?<![\w.])(?<dividend>" + Operand + @")\s*/(?!/)\s*(?<divisor>" + Operand + ")")]
    private static partial Regex Division();

    [GeneratedRegex(@"\b(?:div|rem)\((?<dividend>(?:[^(),]|\([^()]*\))+),\s*(?<divisor>(?:[^(),]|\([^()]*\))+)\)")]
    private static partial Regex WholeDivision();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "ArithmeticError") || !Message().IsMatch(context.Error.Message ?? "") || ElixirCode.Locate(context) is not { } at) return null;

        var masked = ElixirCode.Masked(at.Line);
        var divisions = Division().Matches(masked).Concat(WholeDivision().Matches(masked)).ToList();
        if (divisions is not [var division]) return null;

        var written = at.Line.Substring(division.Index, division.Length);
        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length).Trim();

        return LocalFix.ReplaceLine(Id, $"Give an answer for when {divisor} is zero",
            $"Dividing by zero stops an Elixir program with ArithmeticError. The change works {written} out only when {divisor} is not " +
            "zero, and gives 0 when it is.",
            at.Source.Path, at.Number, at.Line[..division.Index] + $"(if {divisor} == 0, do: 0, else: {written})" + at.Line[(division.Index + division.Length)..]);
    }
}

/// <summary>
/// "unexpected reserved word: end", with Elixir's hint that the end on a line "may not have a matching do": the block that
/// end closes - the nearest line above it, at its indentation, that starts a def, an if or another block - has no do,
/// and gets one.
/// </summary>
public sealed partial class ElixirMissingDo : ILocalFixRule
{
    public string Id => "elixir-missing-do";

    [GeneratedRegex(@"the ""end"" on line (?<line>\d+) may not have a matching ""do""")]
    private static partial Regex Hint();

    [GeneratedRegex(@"^\s*(?:defmodule|defp?|defmacrop?|if|unless|case|cond|for|with|receive|try)\b")]
    private static partial Regex Opener();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "SyntaxError") || context.Error.Message != "unexpected reserved word: end") return null;
        if (Hint().Match(context.Error.RawText) is not { Success: true } hint || ElixirCode.Locate(context) is not { } at) return null;

        var lines = at.Source.Lines;
        var endIndex = int.Parse(hint.Groups["line"].Value) - 1;
        if (endIndex <= 0 || endIndex >= lines.Count) return null;

        var indent = CodeText.Indentation(lines[endIndex]).Length;

        for (var index = endIndex - 1; index >= 0; index--)
        {
            var masked = ElixirCode.Masked(lines[index]);
            if (masked.Trim().Length == 0 || CodeText.Indentation(masked).Length != indent) continue;
            if (!Opener().IsMatch(masked) || Regex.IsMatch(masked, @"\bdo\b")) return null;

            return LocalFix.ReplaceLine(Id, "Start the block with do",
                "In Elixir a def, an if and every other block starts with do and ends with end. This line has no do, so Elixir " +
                "found the end below it with nothing to close.",
                at.Source.Path, index + 1, lines[index].TrimEnd() + " do");
        }

        return null;
    }
}

/// <summary>
/// "missing terminator: end": a block whose end is missing. Read by its indentation - the lines inside a block are indented
/// further than the line that starts it, and its end lines up with that line - the last block left open gets its end after
/// its last line.
/// </summary>
public sealed partial class ElixirMissingEnd : ILocalFixRule
{
    public string Id => "elixir-missing-end";

    /// <summary>A line that starts a block: one ending in do, or an fn whose body starts on the next line.</summary>
    [GeneratedRegex(@"(?:\bdo|\bfn\b[^\n]*->)\s*$")]
    private static partial Regex Opener();

    /// <summary>A line that carries a block on: else, rescue, catch, after.</summary>
    [GeneratedRegex(@"^\s*(?:else|rescue|catch|after)\b")]
    private static partial Regex Continuation();

    [GeneratedRegex(@"^\s*end\b")]
    private static partial Regex Closer();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "TokenMissingError") || context.Error.Message != "missing terminator: end" || ElixirCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Python);
        (int Opening, int InsertBefore)? open = null;

        for (var index = 0; index < masked.Count; index++)
        {
            if (!Opener().IsMatch(masked[index])) continue;
            if (Unclosed(masked, index) is { } insertBefore) open = (index, insertBefore);
        }

        if (open is not { } block) return null;

        var indent = CodeText.Indentation(at.Source.Lines[block.Opening]);
        return LocalFix.Insert(Id, "Close the block with end",
            "Every do in Elixir needs an end. The block this change closes has none, so Elixir read on to the end of the file " +
            "looking for it. The end goes after the block's last line, lined up with the line that starts the block.",
            at.Source.Path, block.InsertBefore + 1, [indent + "end"]);
    }

    /// <summary>Where an end belongs for the block opened at <paramref name="opening"/> when it has none - or null when it has one.</summary>
    private static int? Unclosed(IReadOnlyList<string> masked, int opening)
    {
        var indent = CodeText.Indentation(masked[opening]).Length;
        var lastInside = opening;

        for (var index = opening + 1; index < masked.Count; index++)
        {
            if (masked[index].Trim().Length == 0) continue;

            var lineIndent = CodeText.Indentation(masked[index]).Length;
            if (lineIndent > indent)
            {
                lastInside = index;
                continue;
            }

            if (lineIndent == indent && Closer().IsMatch(masked[index])) return null;
            if (lineIndent == indent && Continuation().IsMatch(masked[index]))
            {
                lastInside = index;
                continue;
            }

            return lastInside + 1;
        }

        return lastInside + 1;
    }
}

/// <summary>
/// "variable "total" is unused (there is a variable with the same name in the context ...)": a variable given a new value
/// inside fn, for or if, which makes a new variable that ends with the block and leaves the one outside as it was. The block's
/// own result is given to the variable instead - see <see cref="ElixirBlocks"/>.
/// </summary>
public sealed partial class ElixirOuterVariable : ILocalFixRule
{
    public string Id => "elixir-outer-variable";

    [GeneratedRegex(@"^variable ""(?<name>\w+)"" is unused \(there is a variable with the same name in the context")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, ElixirParser.Warning) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (ElixirCode.Locate(context) is not { } at) return null;

        return ElixirBlocks.KeptFix(Id, at.Source, at.Number, said.Groups["name"].Value);
    }
}

/// <summary>
/// The change the warning's rule and the logic check both make to a variable given a new value inside a block, so the two
/// are one fix. Elixir's variables never change: = inside fn, for or if makes a new variable that lasts until the block ends.
/// What the block works out is given to the variable outside instead - by Enum.reduce for Enum.each, by for's reduce: for a
/// for, and by an if's own value for an if.
/// </summary>
internal static partial class ElixirBlocks
{
    /// <summary>Enum.each(marks, fn mark -> total = total + mark end), on one line.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)Enum\.each\((?<list>.+?),\s*fn\s+(?<item>[a-z_]\w*)\s*->\s*(?<name>[a-z_]\w*)\s*=(?![=~>])\s*(?<value>.+?)\s+end\)\s*$")]
    public static partial Regex EachOnOneLine();

    [GeneratedRegex(@"^(?<indent>\s*)Enum\.each\((?<list>.+?),\s*fn\s+(?<item>[a-z_]\w*)\s*->\s*$")]
    public static partial Regex EachOpening();

    [GeneratedRegex(@"^(?<indent>\s*)for\s+(?<item>[a-z_]\w*)\s*<-\s*(?<list>.+?)\s+do\s*$")]
    public static partial Regex ForOpening();

    [GeneratedRegex(@"^(?<indent>\s*)(?<keyword>if|unless)\s+(?<condition>.+?)\s+do\s*$")]
    public static partial Regex IfOpening();

    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[a-z_]\w*)\s*=(?![=~>])\s*(?<value>.+?)\s*$")]
    public static partial Regex Assignment();

    /// <summary>
    /// The change for <paramref name="name"/> given a new value on <paramref name="lineNumber"/>, inside one of the four blocks
    /// it knows - or null when the line is in none of them, or the block holds more than that one line.
    /// </summary>
    public static LocalFix? KeptFix(string id, SourceFile source, int lineNumber, string name)
    {
        var lines = source.Lines;
        var index = lineNumber - 1;
        var masked = ElixirCode.Masked(lines[index]);

        if (EachOnOneLine().Match(masked) is { Success: true } each && each.Groups["name"].Value == name)
        {
            var line = lines[index];
            var list = Written(line, each.Groups["list"]);
            var item = each.Groups["item"].Value;
            var value = Written(line, each.Groups["value"]);

            return LocalFix.ReplaceLine(id, $"Keep the total with Enum.reduce: {name} = Enum.reduce(...)",
                $"Enum.reduce passes {name} on from one item to the next, starting from the {name} before it, and gives back the last " +
                $"one - which is given to {name}.",
                source.Path, lineNumber, $"{each.Groups["indent"].Value}{name} = Enum.reduce({list}, {name}, fn {item}, {name} -> {value} end)");
        }

        if (index < 1 || index + 1 >= lines.Count || Assignment().Match(masked) is not { Success: true } assigned || assigned.Groups["name"].Value != name) return null;

        var above = ElixirCode.Masked(lines[index - 1]);
        var below = ElixirCode.Masked(lines[index + 1]).Trim();
        var newValue = Written(lines[index], assigned.Groups["value"]);
        var bodyIndent = assigned.Groups["indent"].Value;

        if (EachOpening().Match(above) is { Success: true } eachOpening && below == "end)")
        {
            var indent = eachOpening.Groups["indent"].Value;
            return new LocalFix
            {
                RuleId = id,
                Title = $"Keep the total with Enum.reduce: {name} = Enum.reduce(...)",
                Explanation = $"Enum.reduce passes {name} on from one item to the next, starting from the {name} before it, and gives back " +
                              $"the last one - which is given to {name}.",
                File = source.Path,
                StartLine = lineNumber - 1,
                RemoveCount = 2,
                NewLines =
                [
                    $"{indent}{name} = Enum.reduce({Written(lines[index - 1], eachOpening.Groups["list"])}, {name}, fn {eachOpening.Groups["item"].Value}, {name} ->",
                    $"{bodyIndent}{newValue}",
                ],
            };
        }

        if (ForOpening().Match(above) is { Success: true } forOpening && below == "end")
        {
            var indent = forOpening.Groups["indent"].Value;
            return new LocalFix
            {
                RuleId = id,
                Title = $"Keep the total with for's reduce: {name} = for ..., reduce: {name}",
                Explanation = $"With reduce:, for passes {name} on from one item to the next, starting from the {name} before it, and " +
                              $"gives back the last one - which is given to {name}.",
                File = source.Path,
                StartLine = lineNumber - 1,
                RemoveCount = 2,
                NewLines =
                [
                    $"{indent}{name} = for {forOpening.Groups["item"].Value} <- {Written(lines[index - 1], forOpening.Groups["list"])}, reduce: {name} do",
                    $"{bodyIndent}{name} -> {newValue}",
                ],
            };
        }

        if (IfOpening().Match(above) is { Success: true } ifOpening && below == "end")
        {
            var keyword = ifOpening.Groups["keyword"].Value;
            return new LocalFix
            {
                RuleId = id,
                Title = $"Give the {keyword}'s answer to {name}",
                Explanation = $"An {keyword} gives back a value of its own. {name} is given it: the new value when the condition says so, " +
                              $"and the {name} it already had otherwise.",
                File = source.Path,
                StartLine = lineNumber - 1,
                RemoveCount = 3,
                NewLines = [$"{ifOpening.Groups["indent"].Value}{name} = {keyword} {Written(lines[index - 1], ifOpening.Groups["condition"])}, do: {newValue}, else: {name}"],
            };
        }

        return null;
    }

    /// <summary>A part of a line as it is written - strings and all - where a match on its masked copy found it.</summary>
    private static string Written(string line, Group part) => line.Substring(part.Index, part.Length);
}

/// <summary>
/// "the Access module does not support accessing lists by index": marks[0] reads a map by its key, and a list has no keys -
/// the one name[...] on the line becomes Enum.at, which Elixir's own message names.
/// </summary>
public sealed partial class ElixirListIndex : ILocalFixRule
{
    public string Id => "elixir-list-index";

    [GeneratedRegex(@"(?<![\w.@])(?<name>[a-z_]\w*)\[(?<position>[^\[\]]+)\]")]
    private static partial Regex Indexed();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "ArgumentError") || !(context.Error.Message ?? "").StartsWith("the Access module does not support accessing lists by index", StringComparison.Ordinal)) return null;
        if (ElixirCode.Locate(context) is not { } at || Indexed().Matches(ElixirCode.Masked(at.Line)) is not [var indexed]) return null;

        var name = indexed.Groups["name"].Value;
        var position = at.Line.Substring(indexed.Groups["position"].Index, indexed.Groups["position"].Length).Trim();

        return LocalFix.ReplaceLine(Id, $"Read the list with Enum.at({name}, {position})",
            $"In Elixir [ ] reads a map by its key, and a list has no keys. Enum.at({name}, {position}) gives the item at that position, " +
            "counting from 0 - and nil past the end.",
            at.Source.Path, at.Number, at.Line[..indexed.Index] + $"Enum.at({name}, {position})" + at.Line[(indexed.Index + indexed.Length)..]);
    }
}

/// <summary>
/// "errors were found at the given arguments: * 1st argument: not a textual representation of an integer", for text with a
/// newline on its end - "12\n", as IO.gets gives it: the text on the line is trimmed before it is read as a number.
/// </summary>
public sealed partial class ElixirTrimInput : ILocalFixRule
{
    public string Id => "elixir-trim-input";

    [GeneratedRegex(@"binary_to_(?:integer|float)\("".*\\n""\)")]
    private static partial Regex ReadWithNewline();

    [GeneratedRegex(@"\bString\.to_(?<kind>integer|float)\((?<text>IO\.gets\((?:[^()]|\([^()]*\))*\)|[a-z_]\w*)\)")]
    private static partial Regex Conversion();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ElixirCode.Is(context, "ArgumentError") || !ReadWithNewline().IsMatch(context.Error.RawText) || ElixirCode.Locate(context) is not { } at) return null;
        if (Conversion().Matches(ElixirCode.Masked(at.Line)) is not [var conversion]) return null;

        var text = at.Line.Substring(conversion.Groups["text"].Index, conversion.Groups["text"].Length);
        var kind = conversion.Groups["kind"].Value;

        return LocalFix.ReplaceLine(Id, $"Trim the text first: String.to_{kind}(String.trim(...))",
            $"A line read with IO.gets still has its newline on the end - \"12\\n\" - and String.to_{kind} will not read a newline. " +
            "String.trim takes it off first.",
            at.Source.Path, at.Number,
            at.Line[..conversion.Index] + $"String.to_{kind}(String.trim({text}))" + at.Line[(conversion.Index + conversion.Length)..]);
    }
}
