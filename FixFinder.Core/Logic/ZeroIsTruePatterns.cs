using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A number asked as a condition in a language where only nil and false are false - Ruby, Lua and Elixir - so 0 is true, and
/// so is every other number. if n % 2 meant "if n is odd", and if items.length "if there are any", but each is always true:
/// run with each language for n = 4 and no items, each printed odd and has items, with nothing said by ruby -w, Lua or
/// Elixir. The change compares the number with 0 - n % 2 != 0 (~= 0 in Lua), and items.length &gt; 0.
/// </summary>
public static partial class ZeroIsTruePatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-ruby-zero-is-true", CodePattern.Files(".rb"), Syntax.Python,
            (id, source, masked) => AlwaysTrue(id, source, masked, RubyCondition(), RubyNumber(), "!=", "nil and false", "Ruby"), Severity.Error, Confidence.Certain),
        new CodePattern("logic-lua-zero-is-true", CodePattern.Files(".lua"), Syntax.Lua,
            (id, source, masked) => AlwaysTrue(id, source, masked, LuaCondition(), LuaNumber(), "~=", "nil and false", "Lua"), Severity.Error, Confidence.Certain),
        new CodePattern("logic-elixir-zero-is-true", CodePattern.Files(".exs", ".ex"), Syntax.Python,
            (id, source, masked) => AlwaysTrue(id, source, masked, ElixirCondition(), ElixirNumber(), "!=", "nil and false", "Elixir"), Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>if n % 2</c>, <c>elsif</c>, <c>unless</c>, <c>while</c>, <c>until</c> - and the same at the end of a line, <c>puts n if n % 2</c>.</summary>
    [GeneratedRegex(@"(?:^\s*|\s)(?<keyword>if|elsif|unless|while|until)\s+(?<condition>[^#]+?)(?:\s+then)?\s*$")]
    private static partial Regex RubyCondition();

    /// <summary>A remainder - n % 2 - or a length - items.length, .size, .count.</summary>
    [GeneratedRegex(@"^(?:(?<remainder>@{0,2}[a-z_]\w*(?:\.\w+|\[[^\]]+\])*\s*%\s*\w+)|(?<length>@{0,2}[a-z_]\w*(?:\.\w+|\[[^\]]+\])*\.(?:length|size|count)))$")]
    private static partial Regex RubyNumber();

    /// <summary><c>if n % 2 then</c>, <c>elseif</c>, <c>while ... do</c>, <c>until ...</c>.</summary>
    [GeneratedRegex(@"^\s*(?:.*\s)?(?<keyword>if|elseif|while|until)\s+(?<condition>.+?)\s*(?:\bthen\b|\bdo\b|$)")]
    private static partial Regex LuaCondition();

    /// <summary>A remainder - n % 2 - or a length - #items.</summary>
    [GeneratedRegex(@"^(?:(?<remainder>[A-Za-z_]\w*(?:\.\w+|\[[^\]]+\])*\s*%\s*\w+)|(?<length>#\s*[A-Za-z_]\w*(?:\.\w+|\[[^\]]+\])*))$")]
    private static partial Regex LuaNumber();

    /// <summary><c>if rem(n, 2) do</c>, <c>unless ... do</c>, or <c>if ..., do:</c>.</summary>
    [GeneratedRegex(@"(?:^|[\s=(])(?<keyword>if|unless)\s+(?<condition>.+?)\s*(?:\bdo\s*$|,\s*do:)")]
    private static partial Regex ElixirCondition();

    /// <summary>A remainder - rem(n, 2) - or a length - length(items), Enum.count(items), String.length(name), map_size(m).</summary>
    [GeneratedRegex(@"^(?:(?<remainder>rem\([^()]+\))|(?<length>(?:length|Enum\.count|String\.length|map_size|tuple_size|byte_size)\([^()]+\)))$")]
    private static partial Regex ElixirNumber();

    private static IEnumerable<LogicFinding> AlwaysTrue(string id, SourceFile source, IReadOnlyList<string> masked, Regex condition, Regex number,
        string notEqual, string falseValues, string language)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (condition.Match(masked[index]) is not { Success: true } asked) continue;

            var conditionGroup = asked.Groups["condition"];
            if (number.Match(conditionGroup.Value.Trim()) is not { Success: true } value) continue;

            var line = source.Lines[index];
            var written = line.Substring(conditionGroup.Index, conditionGroup.Length).Trim();
            var start = conditionGroup.Index + line.Substring(conditionGroup.Index, conditionGroup.Length).IndexOf(written, StringComparison.Ordinal);
            var isRemainder = value.Groups["remainder"].Success;
            var compared = isRemainder ? $"{written} {notEqual} 0" : $"{written} > 0";
            var keyword = asked.Groups["keyword"].Value;
            var never = keyword is "unless" or "until";

            yield return new LogicFinding(id, index + 1,
                $"In {language} only {falseValues} are false, so every number is true - 0 too - and {keyword} {written} " +
                $"{(never ? "never runs" : "always runs")}{(isRemainder ? ", whatever is left over" : ", even when there are none")}",
                LocalFix.ReplaceLine(id, $"Compare it with 0: {keyword} {compared}",
                    $"{compared} is true only when {(isRemainder ? "something is left over" : "there is at least one")}, and false for 0.",
                    source.Path, index + 1, line[..start] + compared + line[(start + written.Length)..]));
        }
    }
}
