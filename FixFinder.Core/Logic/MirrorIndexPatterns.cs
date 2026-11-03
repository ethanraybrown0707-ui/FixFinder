using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// The item matching a position from the other end, written word[len(word) - i] in a loop whose i starts at 0. Positions run
/// from 0 to one less than the length, so on the first pass it asks for the position the length itself names - one past the
/// end - and every pass after is one out: a palindrome check finds "level" is not one. Python, C#, Java, Go, Rust and Dart
/// stop with an index error; C reads the 0 that ends the text; JavaScript, Ruby, PHP, PowerShell and Perl carry on with
/// nothing there. The change counts from the other end with len(word) - 1 - i. Lua and Fortran count positions from 1, where
/// len - i is a different mistake, and are not read here.
/// </summary>
public static partial class MirrorIndexPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Mirror("logic-mirror-index", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike),
        Mirror("logic-python-mirror-index", CodePattern.Files(".py", ".pyw"), Syntax.Python),
        Mirror("logic-go-mirror-index", CodePattern.Files(".go"), Syntax.CLike),
        Mirror("logic-rust-mirror-index", CodePattern.Files(".rs"), Syntax.CLike),
        Mirror("logic-ruby-mirror-index", CodePattern.Files(".rb"), Syntax.Python),
        Mirror("logic-php-mirror-index", CodePattern.Files(".php"), Syntax.CLike),
        Mirror("logic-perl-mirror-index", CodePattern.Files(".pl", ".pm"), Syntax.Perl),
        Mirror("logic-dart-mirror-index", CodePattern.Files(".dart"), Syntax.CLike),
        Mirror("logic-powershell-mirror-index", CodePattern.Files(".ps1"), Syntax.Python),
        Mirror("logic-fsharp-mirror-index", CodePattern.Files(".fsx", ".fs"), Syntax.FSharp),
        Mirror("logic-scala-mirror-index", CodePattern.Files(".scala", ".sc"), Syntax.Scala),
        Mirror("logic-elixir-mirror-index", CodePattern.Files(".exs", ".ex"), Syntax.Python),
    ];

    private static CodePattern Mirror(string id, IReadOnlySet<string> files, Syntax syntax) =>
        new(id, files, syntax, MirrorIndex, Severity.Warning, Confidence.Likely);

    /// <summary>How many lines above the item the loop that gives its position is looked for.</summary>
    private const int MostLinesToTheLoop = 10;

    /// <summary>
    /// An item taken from the other end: word[len(word) - i], word.charAt(word.length() - i), $word[strlen($word) - $i],
    /// word.[word.Length - i], word(word.length - i) - the length of the same list, or a name that holds it, then - and the position.
    /// </summary>
    [GeneratedRegex(@"(?<![\w$.])(?<items>\$?[A-Za-z_]\w*)(?:\.?\[|\(|\.charAt\(|\.at\(|\.get\()\s*(?<length>len\(\s*\k<items>\s*\)|\k<items>\.(?:len\(\)|length\(\)|size\(\)|(?:length|size|Length|Count)\b)|(?:strlen|count|length)\(\s*\k<items>\s*\)|(?<lengthName>[A-Za-z_]\w*))\s*-\s*(?<index>\$?[A-Za-z_]\w*)\s*[\])]")]
    private static partial Regex FromTheOtherEnd();

    /// <summary>Perl's substr($word, length($word) - $i, 1) and Elixir's String.at(word, String.length(word) - i).</summary>
    [GeneratedRegex(@"(?:substr|String\.at)\(\s*(?<items>\$?[A-Za-z_]\w*)\s*,\s*(?<length>(?:length|String\.length)\(\s*\k<items>\s*\))\s*-\s*(?<index>\$?[A-Za-z_]\w*)\s*[,)]")]
    private static partial Regex TextFromTheOtherEnd();

    private static IEnumerable<LogicFinding> MirrorIndex(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var lineIndex = 0; lineIndex < masked.Count; lineIndex++)
        {
            var mirrored = FromTheOtherEnd().Matches(masked[lineIndex]).Concat(TextFromTheOtherEnd().Matches(masked[lineIndex])).ToList();

            foreach (var item in mirrored)
            {
                var (items, length, index) = (item.Groups["items"].Value, item.Groups["length"], item.Groups["index"].Value);

                // A name in place of the length has to hold the same list's length: int length = strlen(word);, n = len(word).
                if (item.Groups["lengthName"].Success && !HoldsLengthOf(masked, lineIndex, length.Value, items)) continue;
                if (!CountsFromZero(masked, lineIndex, index)) continue;

                var line = source.Lines[lineIndex];
                var afterLength = length.Index + length.Length;

                yield return new LogicFinding(id, lineIndex + 1,
                    $"`{length.Value} - {index}` is meant to be the position matching `{index}` from the other end of `{items}`, but positions " +
                    $"run from 0 to one less than the length - so on the first pass, when `{index}` is 0, it is `{length.Value}` itself: one past the end",
                    LocalFix.ReplaceLine(id, $"Count from the other end with {length.Value} - 1 - {index}",
                        $"The last position is {length.Value} - 1, so the position matching `{index}` from the other end is {length.Value} - 1 - {index}: " +
                        $"the last when `{index}` is 0, and the first when `{index}` is the last.",
                        source.Path, lineIndex + 1, line[..afterLength] + " - 1" + line[afterLength..]));
                break;
            }
        }
    }

    /// <summary>Whether a name was given the list's length above the line: int length = strlen(word);, n := len(word), my $n = length($word);.</summary>
    private static bool HoldsLengthOf(IReadOnlyList<string> masked, int usedOn, string name, string items)
    {
        var given = $@"(?<![\w$.]){Regex.Escape(name)}\s*(?::=|=)(?!=)\s*(?:\(\w+\)\s*)?(?:len\(\s*{Regex.Escape(items)}\s*\)|{Regex.Escape(items)}\.(?:len\(\)|length\(\)|size\(\)|(?:length|size|Length|Count)\b)|(?:strlen|count|length)\(\s*{Regex.Escape(items)}\s*\))";

        return Enumerable.Range(0, usedOn).Any(lineIndex => Regex.IsMatch(masked[lineIndex], given));
    }

    /// <summary>
    /// Whether the position is a loop's, counted from 0, in a loop that starts above the line: for (int i = 0; ...), for i
    /// in range(len(word)), for i := 0; ..., for i in 0..n, (0...n).each do |i|, for my $i (0 .. ...), for i &lt;- 0 until n,
    /// Enum.all?(0..n, fn i -&gt;).
    /// </summary>
    private static bool CountsFromZero(IReadOnlyList<string> masked, int usedOn, string index)
    {
        var i = Regex.Escape(index);
        var fromZero = $@"(?<![\w$.]){i}\s*(?::=|=)\s*0\s*[;,]|(?<![\w$.]){i}\s+in\s+(?:range\(\s*(?:0\s*,\s*)?[^,)]*\)|0\s*\.\.)|(?<![\w$.]){i}\s*<-\s*0\s+(?:until|to)\b" +
                       $@"|\(\s*0\s*\.{{2,3}}[^)]*\)\.each\s+do\s*\|\s*{i}\s*\||(?:for|foreach)\s+my\s+{i}\s*\(\s*0\s*\.\.|\(\s*0\s*\.\..*fn\s+{i}\s*->";

        return Enumerable.Range(Math.Max(0, usedOn - MostLinesToTheLoop), Math.Min(usedOn + 1, MostLinesToTheLoop + 1))
            .Any(lineIndex => Regex.IsMatch(masked[lineIndex], fromZero));
    }
}
