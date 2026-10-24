using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A format written the way C's printf writes one - "Total: %d" - given to a print that fills nothing in, with the values
/// after it. Each was run with the language itself, and its own checks said nothing: Python's print("Total: %d", total)
/// printed "Total: %d 150", even with python -W error; Ruby's puts printed "Total: %d" and 150 on two lines, with ruby -w;
/// PHP's echo and Perl's print, with php -l and perl -w, printed "Total: %d150"; Lua's print put a tab between the two;
/// PowerShell's Write-Host printed "Total: %d 150"; and C#'s Console.WriteLine, which fills in {0} and not %d, printed
/// "Total: %d" and never the total. The change hands the format to the language's own way of filling one in: % in Python,
/// format in Ruby, sprintf in PHP and Perl, string.format in Lua, and {0} in C# and PowerShell. FormatCodeLiveTests runs
/// each mistake, and its change.
/// </summary>
public static partial class FormatCodePatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Printed("logic-python-format-code-printed", CodePattern.Files(".py", ".pyw"), Syntax.Python, PythonPrint),
        Printed("logic-ruby-format-code-printed", CodePattern.Files(".rb"), Syntax.Python, RubyPuts),
        Printed("logic-php-format-code-printed", CodePattern.Files(".php"), Syntax.CLike, PhpEcho),
        Printed("logic-lua-format-code-printed", CodePattern.Files(".lua"), Syntax.Lua, LuaPrint),
        Printed("logic-perl-format-code-printed", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlPrint),
        Printed("logic-powershell-format-code-printed", CodePattern.Files(".ps1"), Syntax.Python, PowerShellWrite),
        Printed("logic-csharp-format-code-printed", CodePattern.Files(".cs"), Syntax.CLike, CSharpWrite),
    ];

    private static CodePattern Printed(string id, IReadOnlySet<string> extensions, Syntax syntax, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find) =>
        new(id, extensions, syntax, find, Severity.Error, Confidence.Likely);

    /// <summary>A code printf fills in - %d, %s, %5.2f - or %%, which is a % sign itself and fills nothing in.</summary>
    [GeneratedRegex(@"%%|%(?<flags>[-+0#]*)(?<width>\d*)(?:\.(?<precision>\d+))?(?<kind>[diouxXeEfgGcs])")]
    internal static partial Regex FormatCode();

    internal static List<string> Codes(string text) => FormatCode().Matches(text).Select(code => code.Value).Where(code => code != "%%").ToList();

    /// <summary>
    /// Where each argument of a call is on the line, from <paramref name="from"/> - just inside the call's bracket, or just after
    /// its name where it has none - to the bracket that closes it, or else a ; or the end of the line: split at the commas
    /// between them, not those inside a bracket of an argument's own, and trimmed of the space around each. Null where the
    /// line ends inside a bracket.
    /// </summary>
    internal static List<(int Start, int End)>? Arguments(string masked, int from, bool bracketed)
    {
        var spans = new List<(int Start, int End)>();
        var depth = 0;
        var start = from;

        void Add(int end)
        {
            var first = start;
            while (first < end && char.IsWhiteSpace(masked[first])) first++;
            var last = end;
            while (last > first && char.IsWhiteSpace(masked[last - 1])) last--;
            spans.Add((first, last));
        }

        for (var position = from; position < masked.Length; position++)
        {
            var character = masked[position];
            if (character is '(' or '[' or '{')
            {
                depth++;
            }
            else if (character is ')' or ']' or '}')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }
                if (!bracketed || character != ')') return null;
                Add(position);
                return spans;
            }
            else if (depth == 0 && character == ',')
            {
                Add(position);
                start = position + 1;
            }
            else if (depth == 0 && character == ';' && !bracketed)
            {
                Add(position);
                return spans;
            }
        }

        if (bracketed || depth != 0) return null;
        Add(masked.Length);
        return spans;
    }

    /// <summary>Whether an argument is one piece of text in plain quotes, and nothing joined to it - its quotes are all masking left.</summary>
    internal static bool IsText(string masked, (int Start, int End) span)
    {
        if (span.End - span.Start < 2) return false;
        var quote = masked[span.Start];
        return quote is '"' or '\'' && masked[span.End - 1] == quote && masked[span.Start..span.End].Count(character => character == quote) == 2;
    }

    private static string Text(string line, (int Start, int End) span) => line[span.Start..span.End];

    private static string Listed(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>The text inside the quotes, as it shows when printed - without the \n it ends with, which starts a new line.</summary>
    private static string Shown(string format) => Regex.Replace(format[1..^1], @"(?:\\r)?\\n$", "");

    /// <summary>"the value of total", or "the values of name and mark".</summary>
    private static string ValuesOf(IReadOnlyList<string> values) => (values.Count == 1 ? "the value of " : "the values of ") + Listed(values);

    /// <summary>
    /// The finding's message: what the print did with the codes, and how the line came out - <paramref name="after"/> says
    /// where the values went, given them as words, whether there is more than one, and whether the text ends with \n, which
    /// puts what follows it on the next line.
    /// </summary>
    private static string Message(string call, string format, IReadOnlyList<string> values, Func<string, bool, bool, string> after) =>
        $"{call} does not fill in {Listed(Codes(format).Distinct().ToList())} - it prints the text as it is, so the line comes out as " +
        $"`{Shown(format)}` {after(ValuesOf(values), values.Count > 1, Shown(format).Length < format.Length - 2)}";

    private static string NextLine(bool endsWithNewLine) => endsWithNewLine ? " on the next line, as the text ends with \\n," : "";

    private static string SpacedOut(string values, bool many, bool endsWithNewLine, string between) =>
        $"followed{NextLine(endsWithNewLine)} by {values}, with {between} before {(many ? "each" : "it")}";

    private static string OnLinesOfTheirOwn(string values, bool many, bool endsWithNewLine) =>
        $"with {values} {(many ? "each on a line of its own" : "on a line of its own")} after it";

    private static string StraightAfter(string values, bool many, bool endsWithNewLine) =>
        endsWithNewLine ? $"followed on the next line, as the text ends with \\n, by {values}" : $"followed straight away by {values}";

    /// <summary>A value that can follow Python's % as it is: a name, a call, a position in a list, a number or a piece of text.</summary>
    [GeneratedRegex(@"^(?:[A-Za-z_][\w.]*(?:\([^()]*\)|\[[^\[\]]*\])*|-?\d+(?:\.\d+)?|""[^""]*""|'[^']*')$")]
    private static partial Regex SimpleValue();

    [GeneratedRegex(@"(?<![\w.])print\s*\(")]
    private static partial Regex PythonPrintCall();

    [GeneratedRegex(@"^[A-Za-z_]\w*\s*=(?!=)")]
    private static partial Regex KeywordArgument();

    /// <summary>
    /// print("Total: %d", total): print writes each thing it is given with a space between, and fills nothing in. The change
    /// fills the text in with %, keeping any of print's own keyword arguments - sep=, end= - after it.
    /// </summary>
    private static IEnumerable<LogicFinding> PythonPrint(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match call in PythonPrintCall().Matches(masked[index]))
            {
                var line = source.Lines[index];
                if (Arguments(masked[index], call.Index + call.Length, bracketed: true) is not [var first, .. var rest] || !IsText(masked[index], first)) continue;

                var keywords = rest.SkipWhile(span => !KeywordArgument().IsMatch(Text(masked[index], span))).ToList();
                var values = rest.Take(rest.Count - keywords.Count).Select(span => Text(line, span)).ToList();
                var format = Text(line, first);
                if (values.Count == 0 || Codes(format).Count != values.Count || keywords.Any(span => !KeywordArgument().IsMatch(Text(masked[index], span)))) continue;

                var filled = values.Count == 1
                    ? SimpleValue().IsMatch(values[0]) ? values[0] : $"({values[0]})"
                    : $"({string.Join(", ", values)})";
                var arguments = $"{format} % {filled}" + string.Concat(keywords.Select(span => ", " + Text(line, span)));
                var end = rest[^1].End;

                yield return new LogicFinding(id, index + 1,
                    Message("print", format, values, (values, many, endsWithNewLine) => SpacedOut(values, many, endsWithNewLine, "a space")),
                    LocalFix.ReplaceLine(id, $"Fill it in with %: {format} % {filled}",
                        "% fills each code in the text with the next value - %d with a whole number, %s with any value - so print is given " +
                        "the text with the values in it.",
                        source.Path, index + 1, line[..first.Start] + arguments + line[end..]));
            }
        }
    }

    /// <summary><c>puts "Total: %d", total</c> or <c>puts("Total: %d", total)</c> - and print - at the start of a statement.</summary>
    [GeneratedRegex(@"^\s*(?<call>puts|print)(?<bracket>\s*\()?\s*(?=[""'])")]
    private static partial Regex RubyPrintCall();

    /// <summary>
    /// Ruby's puts prints each thing it is given on a line of its own, and print side by side - neither fills anything in.
    /// The change fills the text in with format, which works as printf does.
    /// </summary>
    private static IEnumerable<LogicFinding> RubyPuts(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RubyPrintCall().Match(masked[index]) is not { Success: true } call) continue;

            var bracketed = call.Groups["bracket"].Success;
            if (Arguments(masked[index], call.Index + call.Length, bracketed) is not [var first, .. var rest] || !IsText(masked[index], first) || rest.Count == 0) continue;
            // A statement modifier - puts x if ready - belongs to the line, not to the last value.
            if (!bracketed && Regex.IsMatch(Text(masked[index], rest[^1]), @"\s(?:if|unless|while|until)\s")) continue;

            var line = source.Lines[index];
            var format = Text(line, first);
            var values = rest.Select(span => Text(line, span)).ToList();
            if (Codes(format).Count != values.Count) continue;

            var name = call.Groups["call"].Value;
            var formatted = $"format({format}, {string.Join(", ", values)})";
            yield return new LogicFinding(id, index + 1,
                Message(name, format, values, name == "puts" ? OnLinesOfTheirOwn : StraightAfter),
                LocalFix.ReplaceLine(id, $"Fill it in with format: {formatted}",
                    $"format fills each code in the text with the next value, as printf does, and {name} prints the text it gives back.",
                    source.Path, index + 1, line[..first.Start] + formatted + line[rest[^1].End..]));
        }
    }

    [GeneratedRegex(@"^\s*echo\s+(?=[""'])")]
    private static partial Regex PhpEchoCall();

    /// <summary>
    /// PHP's echo writes each thing it is given side by side and fills nothing in. The change fills the text in with sprintf,
    /// given as many of the values after it as the text has codes; any after those are still echoed after it.
    /// </summary>
    private static IEnumerable<LogicFinding> PhpEcho(string id, SourceFile source, IReadOnlyList<string> masked) =>
        SprintfFor(id, source, masked, PhpEchoCall(), "echo", "PHP's");

    [GeneratedRegex(@"^\s*print(?<bracket>\s*\()?\s*(?=[""'])")]
    private static partial Regex PerlPrintCall();

    /// <summary>Perl's print: the same as PHP's echo, and the same change, with Perl's own sprintf.</summary>
    private static IEnumerable<LogicFinding> PerlPrint(string id, SourceFile source, IReadOnlyList<string> masked) =>
        SprintfFor(id, source, masked, PerlPrintCall(), "print", "Perl's");

    private static IEnumerable<LogicFinding> SprintfFor(string id, SourceFile source, IReadOnlyList<string> masked, Regex printCall, string name, string whose)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (printCall.Match(masked[index]) is not { Success: true } call) continue;
            if (Arguments(masked[index], call.Index + call.Length, call.Groups["bracket"].Success) is not [var first, .. var rest] || !IsText(masked[index], first)) continue;

            var line = source.Lines[index];
            var format = Text(line, first);
            var codes = Codes(format).Count;
            if (codes == 0 || rest.Count < codes) continue;

            var values = rest.Take(codes).Select(span => Text(line, span)).ToList();
            var formatted = $"sprintf({format}, {string.Join(", ", values)})";
            yield return new LogicFinding(id, index + 1,
                Message(name, format, values, StraightAfter),
                LocalFix.ReplaceLine(id, $"Fill it in with sprintf: {formatted}",
                    $"{whose} sprintf fills each code in the text with the next value, as printf does, and {name} writes the text it gives back.",
                    source.Path, index + 1, line[..first.Start] + formatted + line[rest[codes - 1].End..]));
        }
    }

    [GeneratedRegex(@"(?<![\w.:])(?<call>print|io\.write)\s*\(")]
    private static partial Regex LuaPrintCall();

    /// <summary>
    /// Lua's print writes each thing with a tab between, and io.write side by side - neither fills anything in. The change
    /// fills the text in with string.format, given as many of the values after it as the text has codes.
    /// </summary>
    private static IEnumerable<LogicFinding> LuaPrint(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match call in LuaPrintCall().Matches(masked[index]))
            {
                if (Arguments(masked[index], call.Index + call.Length, bracketed: true) is not [var first, .. var rest] || !IsText(masked[index], first)) continue;

                var line = source.Lines[index];
                var format = Text(line, first);
                var codes = Codes(format).Count;
                if (codes == 0 || rest.Count < codes) continue;

                var name = call.Groups["call"].Value;
                var values = rest.Take(codes).Select(span => Text(line, span)).ToList();
                var formatted = $"string.format({format}, {string.Join(", ", values)})";
                yield return new LogicFinding(id, index + 1,
                    Message(name, format, values, name == "print" ? (values, many, endsWithNewLine) => SpacedOut(values, many, endsWithNewLine, "a tab") : StraightAfter),
                    LocalFix.ReplaceLine(id, $"Fill it in with string.format: {formatted}",
                        $"string.format fills each code in the text with the next value, as C's printf does, and {name} writes the text it gives back.",
                        source.Path, index + 1, line[..first.Start] + formatted + line[rest[codes - 1].End..]));
            }
        }
    }

    /// <summary>
    /// The text with each printf code written as .NET's {0}, {1} and on: %d, %i, %u and %s as {n}, a width as {n,5} or {n,-5},
    /// and %f as {n:F6} - or {n:F2} for %.2f - the decimal places printf would show; %% is a % sign. Null where a code has no
    /// such form - %x, %e, a + or a 0 flag - or the text already holds a brace, which .NET would read as a place to fill in.
    /// </summary>
    private static string? DotNetPlaceholders(string text)
    {
        if (text.Contains('{') || text.Contains('}')) return null;

        var position = 0;
        var convertible = true;
        var converted = FormatCode().Replace(text, code =>
        {
            if (code.Value == "%%") return "%";

            var flags = code.Groups["flags"].Value;
            var kind = code.Groups["kind"].Value;
            var precision = code.Groups["precision"];
            if (flags is not ("" or "-") || kind is not ("d" or "i" or "u" or "s" or "f") || (precision.Success && kind != "f"))
            {
                convertible = false;
                return code.Value;
            }

            var width = code.Groups["width"].Value;
            var alignment = width.Length > 0 ? $",{flags}{width}" : "";
            var decimals = kind == "f" ? $":F{(precision.Success ? precision.Value : "6")}" : "";
            return $"{{{position++}{alignment}{decimals}}}";
        });

        return convertible ? converted : null;
    }

    /// <summary><c>Write-Host "Total: %d" $total</c> - PowerShell's arguments, after the text, separated by spaces.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<call>Write-Host|Write-Output)\s+(?<format>""[^""]*""|'[^']*')(?<values>(?:\s+(?:\$\w+(?:\.\w+)*|-?\d+(?:\.\d+)?|\([^()]*\)))+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellWriteCall();

    [GeneratedRegex(@"\$\w+(?:\.\w+)*|-?\d+(?:\.\d+)?|\([^()]*\)")]
    private static partial Regex PowerShellValue();

    /// <summary>
    /// PowerShell's Write-Host writes each thing it is given with a space between, and Write-Output each on a line of its own -
    /// neither fills anything in. The change fills the text in with -f, which fills in {0}, {1} and on.
    /// </summary>
    private static IEnumerable<LogicFinding> PowerShellWrite(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PowerShellWriteCall().Match(masked[index]) is not { Success: true } call) continue;

            var line = source.Lines[index];
            var format = line.Substring(call.Groups["format"].Index, call.Groups["format"].Length);
            var values = PowerShellValue().Matches(line.Substring(call.Groups["values"].Index, call.Groups["values"].Length)).Select(value => value.Value).ToList();
            if (Codes(format).Count != values.Count || values.Count == 0) continue;

            var name = call.Groups["call"].Value;
            var message = Message(name, format, values,
                name.Equals("Write-Host", StringComparison.OrdinalIgnoreCase) ? (values, many, endsWithNewLine) => SpacedOut(values, many, endsWithNewLine, "a space") : OnLinesOfTheirOwn);

            if (DotNetPlaceholders(format) is not { } placeholders)
            {
                yield return new LogicFinding(id, index + 1, message, null);
                continue;
            }

            var formatted = $"({placeholders} -f {string.Join(", ", values)})";
            yield return new LogicFinding(id, index + 1, message,
                LocalFix.ReplaceLine(id, $"Fill it in with -f: {formatted}",
                    "-f fills {0} with the first value after it, {1} with the second, and on - so the text is written with the values in it.",
                    source.Path, index + 1, $"{call.Groups["indent"].Value}{name} {formatted}"));
        }
    }

    [GeneratedRegex(@"(?<![\w.])Console\s*\.\s*(?<call>WriteLine|Write)\s*\(")]
    private static partial Regex CSharpWriteCall();

    /// <summary>
    /// C#'s Console.WriteLine fills in {0}, {1} and on, and leaves %d as it is - and the values given for it are never
    /// printed. The change writes {0} and on where the codes are.
    /// </summary>
    private static IEnumerable<LogicFinding> CSharpWrite(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match call in CSharpWriteCall().Matches(masked[index]))
            {
                if (Arguments(masked[index], call.Index + call.Length, bracketed: true) is not [var first, .. var rest] || !IsText(masked[index], first)) continue;

                var line = source.Lines[index];
                var format = Text(line, first);
                var values = rest.Select(span => Text(line, span)).ToList();
                if (values.Count == 0 || Codes(format).Count != values.Count) continue;

                var name = $"Console.{call.Groups["call"].Value}";
                var message = $"{name} fills in {{0}}, {{1}} and on - not {Listed(Codes(format).Distinct().ToList())} - so the line comes out as " +
                              $"`{Shown(format)}`, and {ValuesOf(values)} {(values.Count > 1 ? "are" : "is")} never printed";

                if (DotNetPlaceholders(format) is not { } placeholders)
                {
                    yield return new LogicFinding(id, index + 1, message, null);
                    continue;
                }

                yield return new LogicFinding(id, index + 1, message,
                    LocalFix.ReplaceLine(id, $"Write {{0}} where each code is: {placeholders}",
                        $"{name} fills {{0}} with the first value after the text, {{1}} with the second, and on.",
                        source.Path, index + 1, line[..first.Start] + placeholders + line[first.End..]));
            }
        }
    }
}
