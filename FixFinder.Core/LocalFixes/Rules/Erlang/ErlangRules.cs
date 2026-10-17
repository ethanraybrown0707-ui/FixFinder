using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Erlang rules share: whether an error is Erlang's, the line it is on, and where a function ends.</summary>
internal static partial class ErlangCode
{
    public static bool IsErlang(SourceFile source) => Path.GetExtension(source.Path).Equals(".erl", StringComparison.OrdinalIgnoreCase);

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "erlang" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsErlang(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>Whether Erlang gave this error, of this kind.</summary>
    public static bool Is(LocalFixContext context, string kind) => context.Error.LanguageId == "erlang" && context.Error.ExceptionType == kind;

    /// <summary>The code of a line with its comments and the insides of its strings and quoted atoms blanked out.</summary>
    public static string Masked(string line) => CodeText.Mask(line, Syntax.Erlang);

    /// <summary>The index of the line that ends the function a line is in - the first from it whose code ends with a full stop.</summary>
    public static int FunctionEnd(IReadOnlyList<string> lines, int from)
    {
        for (var index = from; index < lines.Count; index++)
            if (Masked(lines[index]).TrimEnd().EndsWith('.')) return index;

        return lines.Count - 1;
    }

    /// <summary>
    /// The lines from <paramref name="from"/> to the end of its function with a name changed wherever it stands as a name of
    /// its own - not a function's or a module's, and not inside text.
    /// </summary>
    public static IReadOnlyList<string> Renamed(IReadOnlyList<string> lines, int from, int to, string name, string newName)
    {
        var standing = new Regex($@"(?<![\w@:#?'$]){Regex.Escape(name)}(?![\w@(:])");
        var renamed = new List<string>();

        for (var index = from; index <= to; index++)
        {
            var line = lines[index];
            var masked = Masked(line);
            var changed = line;

            foreach (var found in standing.Matches(masked).Reverse())
                changed = changed[..found.Index] + newName + changed[(found.Index + found.Length)..];

            renamed.Add(changed);
        }

        return renamed;
    }
}

/// <summary>
/// "variable 'Totl' is unbound, did you mean 'Total'?" and "function avrage/1 undefined, did you mean average/1?": erlc's own
/// suggestion, made on the line and at the column it names.
/// </summary>
public sealed partial class ErlangDidYouMean : ILocalFixRule
{
    public string Id => "erlang-did-you-mean";

    [GeneratedRegex(@"^(?:variable '(?<wrong>\w+)' is unbound, did you mean '(?<right>\w+)'\?|function (?<wrong>[a-z]\w*)/\d+ undefined, did you mean (?<right>[a-z]\w*)/\d+\?)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ErlangCode.Is(context, ErlangParser.CompileError) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (ErlangCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["wrong"].Value;
        var right = said.Groups["right"].Value;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.Erlang, context.Frame?.Column - 1) is not { } corrected) return null;

        var what = (context.Error.Message ?? "").StartsWith("variable", StringComparison.Ordinal)
            ? $"No variable called {wrong} is given a value before this line, so erlc would not compile the program. erlc itself compared it with the variables it knew here"
            : $"The module has no function called {wrong} taking that many arguments, so erlc would not compile the program. erlc itself compared it with the functions the module has";

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}", $"{what}, and suggested {right}.", at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "syntax error before: '&lt;'", on a line that joins two comparisons with and or or: and and or bind more tightly than the
/// comparisons, so Mark &gt; 40 and Mark &lt; 70 is read as Mark &gt; (40 and Mark) &lt; 70. They become andalso and orelse.
/// </summary>
public sealed partial class ErlangAndAlso : ILocalFixRule
{
    public string Id => "erlang-andalso";

    [GeneratedRegex(@"^syntax error before: '(?:<|>|=<|>=|==|/=|=:=|=/=)'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<=\s)(?<word>and|or)(?=\s)")]
    private static partial Regex WordOperator();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ErlangCode.Is(context, ErlangParser.CompileError) || !Message().IsMatch(context.Error.Message ?? "") || ErlangCode.Locate(context) is not { } at) return null;

        var found = WordOperator().Matches(ErlangCode.Masked(at.Line));
        if (found.Count == 0) return null;

        var corrected = at.Line;
        foreach (var word in found.Reverse())
            corrected = corrected[..word.Index] + (word.Value == "and" ? "andalso" : "orelse") + corrected[(word.Index + word.Length)..];

        var words = string.Join(" and ", found.Select(word => word.Value == "and" ? "andalso" : "orelse").Distinct());
        return LocalFix.ReplaceLine(Id, $"Join the comparisons with {words}",
            "In Erlang and and or bind more tightly than < and >, so Mark > 40 and Mark < 70 is read as Mark > (40 and Mark) < 70, which " +
            "Erlang cannot make sense of. andalso and orelse join two comparisons, and stop as soon as the answer is known.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "syntax error before: main", at the start of a line that starts a function: the function above it has no full stop at
/// its end, so erlc read on into this one. The full stop goes at the end of the line above.
/// </summary>
public sealed partial class ErlangMissingPeriod : ILocalFixRule
{
    public string Id => "erlang-missing-period";

    [GeneratedRegex(@"^\s*[a-z][\w@]*\s*\(")]
    private static partial Regex FunctionHead();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ErlangCode.Is(context, ErlangParser.CompileError) || !(context.Error.Message ?? "").StartsWith("syntax error before: ", StringComparison.Ordinal)) return null;
        if (context.Frame?.Column != 1 || ErlangCode.Locate(context) is not { } at || !FunctionHead().IsMatch(ErlangCode.Masked(at.Line))) return null;
        if (MissingSeparator.LineBefore(at.Source.Lines, at.Number - 1) is not { } before) return null;

        var (code, tail) = CodeText.SplitComment(at.Source.Lines[before], Syntax.Erlang);
        return LocalFix.ReplaceLine(Id, "End the function above with a full stop",
            "Each Erlang function ends with a full stop. The one above has none, so erlc read this function as more of it, and stopped " +
            "at this function's name.",
            at.Source.Path, before + 1, code + "." + tail);
    }
}

/// <summary>Where a separator is missing: the code line before an error, when it ends as an expression does - with no , ; . or -&gt;.</summary>
internal static partial class MissingSeparator
{
    [GeneratedRegex(@"[\w)\]}""']$")]
    private static partial Regex EndsAnExpression();

    /// <summary>The index of the last line with code before <paramref name="lineIndex"/>, when that code ends an expression - else null.</summary>
    public static int? LineBefore(IReadOnlyList<string> lines, int lineIndex)
    {
        for (var index = lineIndex - 1; index >= 0; index--)
        {
            var code = ErlangCode.Masked(lines[index]).TrimEnd();
            if (code.Length == 0) continue;

            return EndsAnExpression().IsMatch(code) ? index : null;
        }

        return null;
    }
}

/// <summary>
/// "syntax error before: io", inside a function: the expression on the line above has no comma after it, so erlc read on
/// into this line and stopped at its first word. Between two clauses of a case or an if - each with its own -&gt; - the
/// separator is a semicolon instead.
/// </summary>
public sealed class ErlangMissingComma : ILocalFixRule
{
    public string Id => "erlang-missing-comma";

    public LocalFix? Propose(LocalFixContext context) => MissingInside(context, Id, clauses: false);

    internal static LocalFix? MissingInside(LocalFixContext context, string id, bool clauses)
    {
        if (!ErlangCode.Is(context, ErlangParser.CompileError) || !(context.Error.Message ?? "").StartsWith("syntax error before: ", StringComparison.Ordinal)) return null;
        if (context.Frame?.Column is not > 1 || ErlangCode.Locate(context) is not { } at) return null;
        if (MissingSeparator.LineBefore(at.Source.Lines, at.Number - 1) is not { } before) return null;

        var betweenClauses = ErlangCode.Masked(at.Source.Lines[before]).Contains("->") && ErlangCode.Masked(at.Line).Contains("->");
        if (betweenClauses != clauses) return null;

        var (code, tail) = CodeText.SplitComment(at.Source.Lines[before], Syntax.Erlang);
        return clauses
            ? LocalFix.ReplaceLine(id, "Separate the clauses with a semicolon",
                "The clauses of a case or an if are separated by semicolons, the last one with none. The clause above has none after it, " +
                "so erlc read on into this one and stopped at its start.",
                at.Source.Path, before + 1, code + ";" + tail)
            : LocalFix.ReplaceLine(id, "Separate the expressions with a comma",
                "Inside a function, Erlang separates one expression from the next with a comma. The line above has none, so erlc read on " +
                "into this line and stopped at its first word.",
                at.Source.Path, before + 1, code + "," + tail);
    }
}

/// <summary>The semicolon between two clauses of a case or an if - see <see cref="ErlangMissingComma"/>.</summary>
public sealed class ErlangMissingSemicolon : ILocalFixRule
{
    public string Id => "erlang-missing-semicolon";

    public LocalFix? Propose(LocalFixContext context) => ErlangMissingComma.MissingInside(context, Id, clauses: true);
}

/// <summary>
/// "bad argument ... called as list_to_integer("12\n")": text with its newline still on the end, as io:get_line gives it.
/// The text on the line is trimmed before it is read as a number.
/// </summary>
public sealed partial class ErlangTrimInput : ILocalFixRule
{
    public string Id => "erlang-trim-input";

    [GeneratedRegex(@"called as list_to_(?:integer|float)\("".*\\n""\)")]
    private static partial Regex CalledWithNewline();

    [GeneratedRegex(@"\blist_to_(?<kind>integer|float)\((?<text>io:get_line\((?:[^()]|\([^()]*\))*\)|[A-Z_][\w@]*)\)")]
    private static partial Regex Conversion();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ErlangCode.Is(context, "badarg") || !CalledWithNewline().IsMatch(context.Error.RawText) || ErlangCode.Locate(context) is not { } at) return null;
        if (Conversion().Matches(ErlangCode.Masked(at.Line)) is not [var conversion]) return null;

        var text = at.Line.Substring(conversion.Groups["text"].Index, conversion.Groups["text"].Length);
        var kind = conversion.Groups["kind"].Value;

        return LocalFix.ReplaceLine(Id, $"Trim the text first: list_to_{kind}(string:trim(...))",
            $"A line read with io:get_line still has its newline on the end - \"12\\n\" - and list_to_{kind} will not read a newline. " +
            "string:trim takes it off first.",
            at.Source.Path, at.Number, at.Line[..conversion.Index] + $"list_to_{kind}(string:trim({text}))" + at.Line[(conversion.Index + conversion.Length)..]);
    }
}

/// <summary>
/// "an error occurred when evaluating an arithmetic expression", from dividing by zero: the one division on the line - /,
/// div or rem - is given an answer, 0, for when what it divides by is zero.
/// </summary>
public sealed partial class ErlangDivisionGuard : ILocalFixRule
{
    public string Id => "erlang-division-guard";

    private const string Operand = @"(?:[A-Za-z_][\w@]*(?::[a-z_][\w@]*)?(?:\((?:[^()]|\([^()]*\))*\))?|\d+(?:\.\d+)?)";

    [GeneratedRegex(@"(?<![\w@:])(?<dividend>" + Operand + @")\s*(?<op>/(?![=/])|\bdiv\b|\brem\b)\s*(?<divisor>" + Operand + ")")]
    private static partial Regex Division();

    /// <summary>What an operator was called with, when Erlang says: by zero, or something else.</summary>
    [GeneratedRegex(@"called as .+ (?:/|div|rem) 0\s*$", RegexOptions.Multiline)]
    private static partial Regex CalledWithZero();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!ErlangCode.Is(context, "badarith") || ErlangCode.Locate(context) is not { } at) return null;
        if (context.Error.RawText.Contains("called as", StringComparison.Ordinal) && !CalledWithZero().IsMatch(context.Error.RawText)) return null;
        if (Division().Matches(ErlangCode.Masked(at.Line)) is not [var division]) return null;

        var written = at.Line.Substring(division.Index, division.Length);
        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length).Trim();

        return LocalFix.ReplaceLine(Id, $"Give an answer for when {divisor} is zero",
            $"Dividing by zero stops an Erlang program with badarith. The change works {written} out only when {divisor} is not zero, " +
            "and gives 0 when it is.",
            at.Source.Path, at.Number, at.Line[..division.Index] + $"(case {divisor} of 0 -> 0; _ -> {written} end)" + at.Line[(division.Index + division.Length)..]);
    }
}

/// <summary>
/// "no clause will ever match", and the badmatch that follows, on a line such as total = 10: a name that starts with a small
/// letter is an atom - a fixed value - so the line asks whether the atom matches 10. Written with a capital letter it is a
/// variable, from that line to the end of the function.
/// </summary>
public sealed partial class ErlangCapitalVariable : ILocalFixRule
{
    public string Id => "erlang-capital-variable";

    [GeneratedRegex(@"^\s*(?<atom>[a-z][\w@]*)\s*=(?![=:<])")]
    private static partial Regex AtomGivenAValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        var warned = ErlangCode.Is(context, ErlangParser.CompileWarning) && context.Error.Message == "no clause will ever match";
        if (!warned && !ErlangCode.Is(context, "badmatch")) return null;
        if (ErlangCode.Locate(context) is not { } at || AtomGivenAValue().Match(ErlangCode.Masked(at.Line)) is not { Success: true } given) return null;

        var atom = given.Groups["atom"].Value;
        var variable = char.ToUpperInvariant(atom[0]) + atom[1..];
        var from = at.Number - 1;
        var to = ErlangCode.FunctionEnd(at.Source.Lines, from);

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Make {atom} a variable: {variable}",
            Explanation = $"A name that starts with a small letter is an atom in Erlang - a fixed value, like a word - and a variable's name " +
                          $"starts with a capital letter. {atom} = ... asks whether the atom {atom} matches the value, which it never does. " +
                          $"Written {variable}, it is a variable, given the value - and it is read as {variable} to the end of the function.",
            File = at.Source.Path,
            StartLine = at.Number,
            RemoveCount = to - from + 1,
            NewLines = ErlangCode.Renamed(at.Source.Lines, from, to, atom, variable),
        };
    }
}
