using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Elixir's and Erlang's own library used in a way that runs and does something else - each run with Elixir 1.20 and
/// erlc +warn_all, none of which said anything: IO.puts of a list of whole numbers writes the characters they stand for -
/// [72, 105] was "Hi", and [7, 8, 9] a bell, a backspace and a tab - and IO.inspect showed them as ~c"Hi" and ~c"	" too,
/// until it was given charlists: :as_lists; String.split(text, "") gives an empty piece at each end -
/// ["", "a", "b", "c", ""]; a map made with text keys and read with an atom - ages[:Ada] of %{"Ada" =&gt; 36} - gives nil; and
/// lists:nth counts from 1, so positions from lists:seq(0, ...) stopped the program with function_clause at the first.
/// </summary>
public static partial class BeamLibraryPatterns
{
    private static readonly IReadOnlySet<string> ElixirFiles = CodePattern.Files(".exs", ".ex");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-elixir-numbers-printed-as-text", ElixirFiles, Syntax.Python, ElixirNumbersPrintedAsText, Severity.Error),
        new CodePattern("logic-elixir-split-on-nothing", ElixirFiles, Syntax.Python, ElixirSplitOnNothing),
        new CodePattern("logic-elixir-atom-for-text-key", ElixirFiles, Syntax.Python, ElixirAtomForTextKey, Severity.Error, Confidence.Certain),
        new CodePattern("logic-erlang-nth-from-zero", CodePattern.Files(".erl"), Syntax.Erlang, ErlangNthFromZero, Severity.Error, Confidence.Certain),
    ];

    /// <summary>A list of whole numbers - [7, 8, 9] - or a list worked out from one by Enum.</summary>
    private static bool HoldsWholeNumbers(string name, IReadOnlyList<string> masked, int before) =>
        Enumerable.Range(0, before).Reverse().Select(line => Regex.Match(masked[line], $@"^\s*{Regex.Escape(name)}\s*=\s*(?<value>.+?)\s*$"))
            .FirstOrDefault(given => given.Success) is { } latest &&
        (Regex.IsMatch(latest.Groups["value"].Value, @"^\[\s*-?\d+(?:\s*,\s*-?\d+)*\s*\]$") ||
         Regex.IsMatch(latest.Groups["value"].Value, @"^Enum\.(?:sort|reverse|take|drop|filter|uniq|shuffle)\(\s*\[\s*-?\d+(?:\s*,\s*-?\d+)*\s*\]"));

    private static IEnumerable<LogicFinding> ElixirNumbersPrintedAsText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var printed = Regex.Match(masked[index], @"\bIO\.puts\(\s*(?<value>\[\s*-?\d+(?:\s*,\s*-?\d+)*\s*\]|[a-z_]\w*)\s*\)");
            if (!printed.Success) continue;

            var value = printed.Groups["value"].Value;
            if (!value.StartsWith('[') && !HoldsWholeNumbers(value, masked, index)) continue;

            var line = source.Lines[index];
            var valueGroup = printed.Groups["value"];
            var written = line.Substring(valueGroup.Index, valueGroup.Length);

            yield return new LogicFinding(id, index + 1,
                $"IO.puts takes a list of whole numbers as text - each number the character it stands for - so {value} is written as characters, " +
                "not as numbers: [72, 105] was \"Hi\", and [7, 8, 9] a bell, a backspace and a tab",
                LocalFix.ReplaceLine(id, $"Print the list as a list of numbers: IO.inspect({value}, charlists: :as_lists)",
                    "IO.inspect writes the list as Elixir writes it in code, and charlists: :as_lists keeps it numbers - without it, a list of " +
                    "codes for printable characters is shown as text too, ~c\"Hi\".",
                    source.Path, index + 1, line[..printed.Index] + $"IO.inspect({written}, charlists: :as_lists)" + line[(printed.Index + printed.Length)..]));
        }
    }

    [GeneratedRegex(@"\bString\.split\(\s*(?<text>[^,()]+(?:\([^()]*\))?)\s*,\s*""""\s*\)")]
    private static partial Regex SplitOnNothing();

    private static IEnumerable<LogicFinding> ElixirSplitOnNothing(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match split in SplitOnNothing().Matches(masked[index]))
            {
                var line = source.Lines[index];
                var text = line.Substring(split.Groups["text"].Index, split.Groups["text"].Length).Trim();
                var changed = $"String.graphemes({text})";

                yield return new LogicFinding(id, index + 1,
                    $"String.split with \"\" splits before the first character and after the last as well, so it gives an empty piece at each end - " +
                    "String.split(\"abc\", \"\") is [\"\", \"a\", \"b\", \"c\", \"\"] - and a count of the pieces is two too many",
                    LocalFix.ReplaceLine(id, $"Take the characters: {changed}",
                        "String.graphemes gives each character of the text, and nothing else.",
                        source.Path, index + 1, line[..split.Index] + changed + line[(split.Index + split.Length)..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> ElixirAtomForTextKey(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var made = Regex.Match(masked[index], @"^\s*(?<name>[a-z_]\w*)\s*=\s*%\{\s*""");
            if (!made.Success) continue;

            var name = made.Groups["name"].Value;
            // The map's text keys, read from the line as written: masking blanks the inside of the quotes.
            var keys = Regex.Matches(source.Lines[index], @"""(?<key>[A-Za-z_]\w*)""\s*=>").Select(key => key.Groups["key"].Value).ToHashSet();
            if (keys.Count == 0 || Regex.IsMatch(source.Lines[index], @"\b[a-z_]\w*:\s|:\w+\s*=>")) continue;

            var givenAgain = new Regex($@"^\s*{Regex.Escape(name)}\s*=(?!=)");
            for (var later = index + 1; later < masked.Count && !givenAgain.IsMatch(masked[later]); later++)
            {
                foreach (Match read in Regex.Matches(masked[later], $@"(?<![\w.]){Regex.Escape(name)}\[:(?<key>[A-Za-z_]\w*)\]"))
                {
                    var key = read.Groups["key"].Value;
                    if (!keys.Contains(key)) continue;

                    var line = source.Lines[later];
                    var keyStart = read.Groups["key"].Index - 1;
                    var keyEnd = read.Groups["key"].Index + read.Groups["key"].Length;

                    yield return new LogicFinding(id, later + 1,
                        $"{name} was made on line {index + 1} with the text \"{key}\" as a key, and the atom :{key} is another key altogether - so " +
                        $"{name}[:{key}] finds nothing, and gives nil",
                        LocalFix.ReplaceLine(id, $"Read it by its text: {name}[\"{key}\"]",
                            $"\"{key}\" is the key the map was made with, so it finds the value stored under it.",
                            source.Path, later + 1, line[..keyStart] + $"\"{key}\"" + line[keyEnd..]));
                }
            }
        }
    }

    private static IEnumerable<LogicFinding> ErlangNthFromZero(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];

            // lists:nth(0, List) - the first position written as 0.
            foreach (Match zero in Regex.Matches(masked[index], @"\blists:nth\(\s*(?<zero>0)\s*,"))
            {
                var at = zero.Groups["zero"].Index;
                yield return new LogicFinding(id, index + 1,
                    "lists:nth counts from 1 - the first item is at 1 - so lists:nth(0, ...) stops the program with function_clause",
                    LocalFix.ReplaceLine(id, "Ask for the first item at 1: lists:nth(1, ...)",
                        "Position 1 is the first item of the list.",
                        source.Path, index + 1, line[..at] + "1" + line[(at + 1)..]));
            }

            // [lists:nth(Position, Marks) || Position <- lists:seq(0, length(Marks) - 1)] - positions from 0.
            foreach (Match nth in Regex.Matches(masked[index], @"\blists:nth\(\s*(?<position>[A-Z_]\w*)\s*,"))
            {
                var position = nth.Groups["position"].Value;
                var seq = Regex.Match(masked[index], $@"\b{Regex.Escape(position)}\s*<-\s*lists:seq\(\s*(?<zero>0)\s*,\s*(?<end>.+?)\s*-\s*1\s*\)");
                if (!seq.Success) continue;

                var zeroAt = seq.Groups["zero"].Index;
                var endGroup = seq.Groups["end"];
                var endWritten = line.Substring(endGroup.Index, endGroup.Length);
                var closeAt = seq.Index + seq.Length - 1;

                yield return new LogicFinding(id, index + 1,
                    $"lists:nth counts from 1 - the first item is at 1 - and {position} starts at 0, so the first lists:nth stops the program with " +
                    "function_clause",
                    LocalFix.ReplaceLine(id, $"Count the positions from 1: lists:seq(1, {endWritten})",
                        "From 1 to the length is every position in the list, as lists:nth counts them.",
                        source.Path, index + 1, line[..zeroAt] + "1" + line[(zeroAt + 1)..endGroup.Index] + endWritten + line[closeAt..]));
            }
        }
    }
}
