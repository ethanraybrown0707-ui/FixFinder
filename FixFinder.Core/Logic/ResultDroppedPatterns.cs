using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A function that gives back a changed copy, called on a line of its own as if it changed the value itself: C's
/// toupper(letter);, PHP's strtoupper($name);, Lua's string.upper(name), Perl's uc($name); and Rust's name.to_uppercase();.
/// Each leaves the value as it was and throws the copy away, and the program prints the old value - "ada" where "ADA" was
/// meant. The change keeps the copy in the name it was made from: a new let of the same name in Rust, which shadows the old.
/// </summary>
public static partial class ResultDroppedPatterns
{
    /// <summary>How one language writes this mistake and its change.</summary>
    /// <param name="Id">The check's id for the language.</param>
    /// <param name="Dropped">The call on a line of its own: its indent, the call and the name it is made from.</param>
    /// <param name="Kept">The change, from the indent, the name and the call: letter = toupper(letter);.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Regex Dropped, Func<string, string, string, string> Kept);

    private static readonly Dialect[] Dialects =
    [
        new("logic-c-result-discarded", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?<call>(?:std::)?(?:toupper|tolower|abs|fabs|sqrt|pow|round|floor|ceil)\(\s*(?<name>[A-Za-z_]\w*)\s*(?:,[^;]*)?\))\s*;\s*$"),
            (indent, name, call) => $"{indent}{name} = {call};"),
        new("logic-php-result-discarded", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?<call>(?:strtoupper|strtolower|ucfirst|lcfirst|ucwords|trim|ltrim|rtrim|strrev|str_pad|round|abs|floor|ceil|array_reverse|array_unique|array_values|array_slice|array_filter)\(\s*(?<name>\$[A-Za-z_]\w*)\s*(?:,[^;]*)?\))\s*;\s*$"),
            (indent, name, call) => $"{indent}{name} = {call};"),
        new("logic-lua-result-discarded", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^(?<indent>\s*)(?<call>string\.(?:upper|lower|reverse|rep|gsub|sub)\(\s*(?<name>[A-Za-z_]\w*)\s*(?:,[^)]*)?\)|(?<name>[A-Za-z_]\w*):(?:upper|lower|reverse|rep|gsub|sub)\([^)]*\))\s*$"),
            (indent, name, call) => $"{indent}{name} = {call}"),
        new("logic-perl-result-discarded", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^(?<indent>\s*)(?<call>(?:uc|lc|ucfirst|lcfirst|abs|int)\s*\(\s*(?<name>\$[A-Za-z_]\w*)\s*\))\s*;\s*$"),
            (indent, name, call) => $"{indent}{name} = {call};"),
        new("logic-rust-result-discarded", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^(?<indent>\s*)(?<call>(?<name>[a-z_]\w*)\.(?:to_uppercase|to_lowercase|trim|trim_start|trim_end|replace|repeat|abs|round|floor|ceil|sqrt)\([^;]*\))\s*;\s*$"),
            (indent, name, call) => $"{indent}let {name} = {call};"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax, (id, source, masked) => ResultDropped(id, source, masked, dialect)))];

    private static IEnumerable<LogicFinding> ResultDropped(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        for (var lineIndex = 0; lineIndex < masked.Count; lineIndex++)
        {
            if (dialect.Dropped.Match(masked[lineIndex]) is not { Success: true } dropped) continue;

            var name = dropped.Groups["name"].Value;
            var line = source.Lines[lineIndex];
            var call = line.Substring(dropped.Groups["call"].Index, dropped.Groups["call"].Length);
            var kept = dialect.Kept(dropped.Groups["indent"].Value, name, call);

            yield return new LogicFinding(id, lineIndex + 1,
                $"`{call}` gives back a changed copy and leaves `{name}` as it was - on a line of its own, the copy is thrown away",
                LocalFix.ReplaceLine(id, $"Keep what it gives back: {kept.Trim()}",
                    $"`{call}` does not change `{name}`: it works out a new value and gives it back. Kept in `{name}`, the new value is " +
                    "what the lines after it read.",
                    source.Path, lineIndex + 1, kept + CodeText.SplitComment(line, dialect.Syntax).Tail));
        }
    }
}
