using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Two values swapped without keeping one of them: <c>arr[j] = arr[j + 1]</c> and then <c>arr[j + 1] = arr[j]</c>. The first
/// line gives arr[j] what arr[j + 1] holds, so by the second arr[j] holds it too, and arr[j + 1] is given its own value back
/// - the two are not swapped, and what arr[j] held is lost. It is the swap in a bubble sort, written as two lines. The
/// change swaps them the language's own way - a, b = b, a where the language has it - or, when the line above kept the first
/// value - temp = arr[j] - gives the second line that value.
/// </summary>
/// <remarks>
/// Always a mistake, as the second line can only give the value back that it already holds. Said only for two lines that do
/// nothing else, neither of them a declaration, with blank lines at most between them.
/// </remarks>
public static partial class SwapPatterns
{
    /// <summary>What a swap needs to know of the two lines: their indentation, the two places, and the ; the language ends them with.</summary>
    private sealed record Swapped(string Indent, string First, string Second, string End, SourceFile Source, IReadOnlyList<string> Masked, int Line);

    /// <summary>
    /// How one language writes a place a value is kept in - a name, a field, an item - and how it swaps two of them: the lines
    /// of the change, or null where it cannot be written without more than the file says.
    /// </summary>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, string Place, Func<Swapped, IReadOnlyList<string>?> Swap);

    private const string BracePlace = @"[A-Za-z_$][\w$]*(?:(?:\.|->)[A-Za-z_$][\w$]*|\[[^\[\]=;]+\])*";
    private const string NamePlace = @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*|\[[^\[\]=]+\])*";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-swap-loses-value", CodePattern.Files(".py", ".pyw"), Syntax.Python, NamePlace, Tuple),
        new("logic-ruby-swap-loses-value", CodePattern.Files(".rb"), Syntax.Python, @"@{0,2}[a-z_]\w*(?:\.\w+|\[[^\[\]=]+\])*", Tuple),
        new("logic-lua-swap-loses-value", CodePattern.Files(".lua"), Syntax.Lua, NamePlace, Tuple),
        new("logic-go-swap-loses-value", CodePattern.Files(".go"), Syntax.CLike, NamePlace, Tuple),
        new("logic-powershell-swap-loses-value", CodePattern.Files(".ps1"), Syntax.Python, @"\$[\w:]+(?:\.\w+|\[[^\[\]=]+\])*", Tuple),
        new("logic-php-swap-loses-value", CodePattern.Files(".php"), Syntax.CLike, @"\$\w+(?:->\w+|\[[^\[\]=]*\])*",
            swapped => [$"{swapped.Indent}[{swapped.First}, {swapped.Second}] = [{swapped.Second}, {swapped.First}];"]),
        new("logic-perl-swap-loses-value", CodePattern.Files(".pl", ".pm"), Syntax.Perl, @"\$\w+(?:\[[^\[\]=]+\]|\{[^{}=]+\}|->(?:\[[^\[\]=]+\]|\{[^{}=]+\}))*",
            swapped => [$"{swapped.Indent}({swapped.First}, {swapped.Second}) = ({swapped.Second}, {swapped.First});"]),
        new("logic-js-swap-loses-value", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, BracePlace, JavaScriptSwap),
        new("logic-csharp-swap-loses-value", CodePattern.Files(".cs"), Syntax.CLike, BracePlace,
            swapped => [$"{swapped.Indent}({swapped.First}, {swapped.Second}) = ({swapped.Second}, {swapped.First});"]),
        new("logic-java-swap-loses-value", CodePattern.Files(".java"), Syntax.CLike, BracePlace, swapped => Held(swapped, "var")),
        new("logic-cpp-swap-loses-value", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, BracePlace, swapped => Held(swapped, "auto")),
        new("logic-c-swap-loses-value", CodePattern.Files(".c"), Syntax.CLike, BracePlace, swapped => CType(swapped) is { } type ? Held(swapped, type) : null),
        new("logic-dart-swap-loses-value", CodePattern.Files(".dart"), Syntax.CLike, BracePlace, swapped => Held(swapped, "final")),
        new("logic-rust-swap-loses-value", CodePattern.Files(".rs"), Syntax.CLike, NamePlace, RustSwap),
        new("logic-scala-swap-loses-value", CodePattern.Files(".scala", ".sc"), Syntax.Scala, @"[A-Za-z_]\w*(?:\.\w+|\([^()=]+\))*", swapped => Held(swapped, "val")),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => Swaps(id, source, masked, dialect), Severity.Error, Confidence.Likely))];

    private static IEnumerable<LogicFinding> Swaps(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        // One place given another, and nothing else on the line: not a declaration, not :=, +=, == or =>.
        var given = new Regex($@"^(?<indent>\s*)(?<first>{dialect.Place})\s*(?<![:+\-*/%&|^<>!=.])=(?![=>~])\s*(?<second>{dialect.Place})\s*(?<end>;?)\s*$");

        for (var line = 0; line + 1 < masked.Count; line++)
        {
            if (given.Match(masked[line]) is not { Success: true } first) continue;

            var next = line + 1;
            while (next < masked.Count && masked[next].Trim().Length == 0) next++;
            if (next >= masked.Count || given.Match(masked[next]) is not { Success: true } second) continue;

            // Read from the lines as written, with each place's spaces taken out to compare them: arr[j+1] is arr[j + 1].
            var a = Written(source, line, first.Groups["first"]);
            var b = Written(source, line, first.Groups["second"]);
            if (Squeezed(a) == Squeezed(b) || Squeezed(Written(source, next, second.Groups["first"])) != Squeezed(b) ||
                Squeezed(Written(source, next, second.Groups["second"])) != Squeezed(a)) continue;

            var indent = second.Groups["indent"].Value;
            var end = second.Groups["end"].Value;
            var said = $"Line {line + 1} gives `{a}` what `{b}` holds, so by line {next + 1} `{a}` holds it too: line {next + 1} gives `{b}` " +
                       $"its own value back, and what `{a}` held before is lost - the two are not swapped.";

            // temp = a just above: the second line should give b that.
            if (Kept(source, masked, line, a) is { } kept)
            {
                yield return new LogicFinding(id, next + 1, said + $" Line {kept.Line + 1} kept it in `{kept.Name}`.",
                    LocalFix.ReplaceLine(id, $"Give {b} what {kept.Name} kept: {b} = {kept.Name}",
                        $"{kept.Name} holds what {a} held before line {line + 1} changed it, so {b} gets that value and the two are swapped.",
                        source.Path, next + 1, $"{indent}{b} = {kept.Name}{end}"));
                continue;
            }

            var lines = dialect.Swap(new Swapped(indent, a, b, end, source, masked, line));
            yield return new LogicFinding(id, next + 1, said,
                lines is null ? null : new LocalFix
                {
                    RuleId = id,
                    Title = $"Swap {a} and {b}",
                    Explanation = $"{(lines.Count == 1 ? "The line" : "The lines")} {(lines.Count == 1 ? "gives" : "give")} each place what the other held, " +
                                  $"so neither value is lost.",
                    File = source.Path,
                    StartLine = line + 1,
                    RemoveCount = next - line + 1,
                    NewLines = lines,
                });
        }
    }

    private static string Written(SourceFile source, int line, Group group) => source.Lines[line].Substring(group.Index, group.Length);

    private static string Squeezed(string place) => Regex.Replace(place, @"\s+", "");

    /// <summary>The line just above the first, when it keeps that place's value in a name of its own - temp = arr[j] - with the name.</summary>
    private static (int Line, string Name)? Kept(SourceFile source, IReadOnlyList<string> masked, int line, string place)
    {
        var above = line - 1;
        while (above >= 0 && masked[above].Trim().Length == 0) above--;
        if (above < 0) return null;

        var placeWritten = string.Join(@"\s*", Squeezed(place).Select(c => Regex.Escape(c.ToString())));
        return Regex.Match(source.Lines[above], $@"^\s*(?:[\w<>\[\]?*&:]+\s+)*(?<name>[$@%]?[A-Za-z_]\w*)\s*(?::[^=]*)?(?<![:+\-*/%&|^<>!=])=(?![=>~])\s*{placeWritten}\s*;?\s*$") is { Success: true } kept
            ? (above, kept.Groups["name"].Value)
            : null;
    }

    /// <summary>a, b = b, a - Python's, Ruby's, Lua's, Go's and PowerShell's swap of two places in one line.</summary>
    private static IReadOnlyList<string> Tuple(Swapped swapped) =>
        [$"{swapped.Indent}{swapped.First}, {swapped.Second} = {swapped.Second}, {swapped.First}{swapped.End}"];

    /// <summary>
    /// [a, b] = [b, a]; - begun with a ; when the line above ends without one, as JavaScript would otherwise read the [ as
    /// part of that line.
    /// </summary>
    private static IReadOnlyList<string> JavaScriptSwap(Swapped swapped)
    {
        var above = swapped.Line - 1;
        while (above >= 0 && swapped.Masked[above].Trim().Length == 0) above--;
        var endsAbove = above < 0 || swapped.Masked[above].TrimEnd() is var code && (code.EndsWith(';') || code.EndsWith('{') || code.EndsWith('}'));
        return [$"{swapped.Indent}{(endsAbove ? "" : ";")}[{swapped.First}, {swapped.Second}] = [{swapped.Second}, {swapped.First}]{swapped.End}"];
    }

    /// <summary>Rust's: v.swap(i, j) for two items of one list, std::mem::swap for two names - or null for anything else.</summary>
    private static IReadOnlyList<string>? RustSwap(Swapped swapped)
    {
        var items = Regex.Match(swapped.First, @"^(?<list>[A-Za-z_]\w*)\[(?<first>[^\[\]]+)\]$");
        var others = Regex.Match(swapped.Second, @"^(?<list>[A-Za-z_]\w*)\[(?<second>[^\[\]]+)\]$");
        if (items.Success && others.Success && items.Groups["list"].Value == others.Groups["list"].Value)
            return [$"{swapped.Indent}{items.Groups["list"].Value}.swap({items.Groups["first"].Value}, {others.Groups["second"].Value});"];

        return Regex.IsMatch(swapped.First, @"^[A-Za-z_]\w*$") && Regex.IsMatch(swapped.Second, @"^[A-Za-z_]\w*$")
            ? [$"{swapped.Indent}std::mem::swap(&mut {swapped.First}, &mut {swapped.Second});"]
            : null;
    }

    /// <summary>The first value kept in a name of its own while the first place is given the second: held = a; a = b; b = held.</summary>
    private static IReadOnlyList<string> Held(Swapped swapped, string declared)
    {
        var held = FreeName(swapped.Source, "held");
        return
        [
            $"{swapped.Indent}{declared} {held} = {swapped.First}{swapped.End}",
            $"{swapped.Indent}{swapped.First} = {swapped.Second}{swapped.End}",
            $"{swapped.Indent}{swapped.Second} = {held}{swapped.End}",
        ];
    }

    /// <summary>held, or held2, held3 and on - the first the file does not already use as a word.</summary>
    private static string FreeName(SourceFile source, string name)
    {
        var words = new HashSet<string>(source.Lines.SelectMany(line => Regex.Matches(line, @"[A-Za-z_]\w*").Select(word => word.Value)), StringComparer.Ordinal);
        if (!words.Contains(name)) return name;
        for (var number = 2; ; number++)
            if (!words.Contains(name + number)) return name + number;
    }

    /// <summary>
    /// The type C needs for the kept value: what the file declares the place's name as - int marks[5], double *values - for a
    /// name or an item of it, or null for a field or a name it does not declare.
    /// </summary>
    private static string? CType(Swapped swapped)
    {
        if (Regex.Match(swapped.First, @"^(?<name>[A-Za-z_]\w*)(?:\[[^\[\]]+\])?$") is not { Success: true } place) return null;

        var name = Regex.Escape(place.Groups["name"].Value);
        return swapped.Masked
            .Select(line => Regex.Match(line, $@"(?:^\s*|[;{{(,]\s*)(?<type>(?:(?:unsigned|signed|long|short|const)\s+)*[A-Za-z_]\w*)\s+\*?\s*{name}\s*(?:\[|=|;|,|\))"))
            .FirstOrDefault(match => match.Success && match.Groups["type"].Value is not ("return" or "else" or "case" or "goto")) is { } declared
            ? Regex.Replace(declared.Groups["type"].Value, @"^const\s+", "")
            : null;
    }
}
