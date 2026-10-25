using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// C's and C++'s memory and characters used in a way that builds with nothing said by gcc and g++ -Wall -Wextra, and runs
/// wrong: char word[5] = "hello" leaves no room for the '\0' that ends the text, so printf read on into what came next -
/// [helloabc], strlen 8 (C++ refuses it; C does not); char c = getchar() cannot tell the byte 255 from EOF, so reading
/// "ab", 255, "cd" stopped at the third byte and counted 2; malloc(count) gives count bytes, not count ints - 4 bytes for 4
/// ints of 4 bytes; in C++, "Total: " + count moves along the text by count characters instead of joining - it printed
/// "al: "; and reserve(3) makes room without making items, so marks[0] = 70 wrote past the vector's end and its size
/// stayed 0.
/// </summary>
public static partial class NativeMemoryPatterns
{
    private static readonly IReadOnlySet<string> CFiles = CodePattern.Files(".c");
    private static readonly IReadOnlySet<string> NativeFiles = CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++");
    private static readonly IReadOnlySet<string> CppFiles = CodePattern.Files(".cpp", ".cc", ".cxx", ".c++");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-c-text-without-end", CFiles, Syntax.CLike, TextWithoutEnd, Severity.Error, Confidence.Certain),
        new CodePattern("logic-c-getchar-into-char", NativeFiles, Syntax.CLike, GetcharIntoChar, Severity.Error, Confidence.Certain),
        new CodePattern("logic-c-malloc-without-sizeof", NativeFiles, Syntax.CLike, MallocWithoutSizeof, Severity.Error),
        new CodePattern("logic-cpp-text-plus-number", CppFiles, Syntax.CLike, TextPlusNumber, Severity.Error, Confidence.Certain),
        new CodePattern("logic-cpp-reserve-then-index", CppFiles, Syntax.CLike, ReserveThenIndex, Severity.Error),
    ];

    /// <summary><c>char word[5] = "hello";</c> - a size, and text in quotes.</summary>
    [GeneratedRegex(@"\bchar\s+(?<name>[A-Za-z_]\w*)\s*\[\s*(?<size>\d+)\s*\]\s*=\s*""")]
    private static partial Regex SizedText();

    /// <summary>How many characters text in quotes holds, each escape - \n, \t, \\, \" - one character.</summary>
    internal static int CharactersIn(string quoted) => Regex.Matches(quoted, @"\\(?:[0-7]{1,3}|x[0-9A-Fa-f]+|.)|[^\\]").Count;

    private static IEnumerable<LogicFinding> TextWithoutEnd(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (SizedText().Match(masked[index]) is not { Success: true } sized) continue;

            var line = source.Lines[index];
            var open = sized.Index + sized.Length - 1;
            var close = masked[index].IndexOf('"', open + 1);
            if (close < 0 || masked[index].IndexOf('"', close + 1) >= 0) continue;

            var size = int.Parse(sized.Groups["size"].Value);
            var characters = CharactersIn(line[(open + 1)..close]);
            if (characters != size) continue;

            var name = sized.Groups["name"].Value;
            var sizeGroup = sized.Groups["size"];

            yield return new LogicFinding(id, index + 1,
                $"{name} holds {size} chars, and the text has {characters} - so there is no room for the '\\0' that ends text in C, and printf, " +
                $"strlen and strcpy read on past {name} into whatever comes next: a char[5] given \"hello\" printed helloabc",
                LocalFix.ReplaceLine(id, $"Make room for the end: char {name}[{size + 1}]",
                    $"One char more holds the '\\0' that C puts after the text, so {name} ends where its text does.",
                    source.Path, index + 1, line[..sizeGroup.Index] + (size + 1) + line[(sizeGroup.Index + sizeGroup.Length)..]));
        }
    }

    private static IEnumerable<LogicFinding> GetcharIntoChar(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            // (c = getchar()) != EOF - or getc, fgetc - compared with EOF as it is read.
            var read = Regex.Match(masked[index], @"\(\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?:getchar\s*\(\s*\)|f?getc\s*\([^()]*\))\s*\)\s*[!=]=\s*EOF\b");
            if (!read.Success) continue;

            var name = read.Groups["name"].Value;
            var declaration = Enumerable.Range(0, index + 1).Reverse().Select(line => (Line: line, Match: Regex.Match(masked[line],
                $@"^(?<before>\s*)(?<type>(?:unsigned\s+|signed\s+)?char)(?<after>\s+(?:[A-Za-z_]\w*\s*(?:=[^,;]*)?,\s*)*{Regex.Escape(name)}\s*[;,=])")))
                .FirstOrDefault(each => each.Match.Success);
            if (declaration.Match is not { Success: true } declared) continue;

            var declarationLine = source.Lines[declaration.Line];
            var type = declared.Groups["type"];
            var several = Regex.IsMatch(masked[declaration.Line], $@"\bchar\b[^;]*,") ;

            yield return new LogicFinding(id, index + 1,
                $"getchar gives back an int: every byte, 0 to 255, and EOF, a number apart from them all. Kept in the char {name}, the byte 255 " +
                $"and EOF become one value, so the loop stops at a byte 255 as if the input had ended - reading \"ab\", 255, \"cd\" counted 2",
                several ? null : LocalFix.ReplaceLine(id, $"Keep it in an int: int {name}",
                    $"An int holds every byte and EOF apart, so the loop ends only when the input does.",
                    source.Path, declaration.Line + 1, declarationLine[..type.Index] + "int" + declarationLine[(type.Index + type.Length)..]));
        }
    }

    /// <summary><c>int *marks = malloc(count);</c> - a pointer to a type wider than a char, given bytes counted without sizeof.</summary>
    [GeneratedRegex(@"^(?<before>\s*)(?<type>(?:unsigned\s+)?(?:int|long|short|double|float|long\s+long|size_t|u?int\d+_t|struct\s+[A-Za-z_]\w*))\s*\*\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?:\(\s*[\w\s]+\*\s*\)\s*)?malloc\s*\(\s*(?<count>[^()]*?)\s*\)\s*;")]
    private static partial Regex MallocBytes();

    private static IEnumerable<LogicFinding> MallocWithoutSizeof(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (MallocBytes().Match(masked[index]) is not { Success: true } allocated) continue;

            var count = allocated.Groups["count"].Value;
            if (count.Contains("sizeof", StringComparison.Ordinal) || count.Length == 0) continue;

            var line = source.Lines[index];
            var name = allocated.Groups["name"].Value;
            var type = allocated.Groups["type"].Value;
            var countGroup = allocated.Groups["count"];
            var bytes = Regex.IsMatch(count, @"^[\w.>-]+$") ? count : $"({count})";

            yield return new LogicFinding(id, index + 1,
                $"malloc counts in bytes, and one {type} takes more than one byte - 4 for an int here - so malloc({count}) gives room for only part " +
                $"of {count} of them, and writing the rest goes past the end of what {name} was given: malloc(4) gave 4 bytes for 4 ints",
                LocalFix.ReplaceLine(id, $"Count the bytes: malloc({bytes} * sizeof *{name})",
                    $"sizeof *{name} is the size of one {type}, so {bytes} of them take {bytes} times that many bytes.",
                    source.Path, index + 1, line[..countGroup.Index] + $"{bytes} * sizeof *{name}" + line[(countGroup.Index + countGroup.Length)..]));
        }
    }

    /// <summary><c>"Total: " + count</c> - text in quotes with a number added to it.</summary>
    [GeneratedRegex(@"(?<!\w)(?<text>""(?:[^""\\]|\\.)*"")\s*\+\s*(?<number>[A-Za-z_]\w*|\d+)(?![\w(.\[])")]
    private static partial Regex TextPlus();

    private static IEnumerable<LogicFinding> TextPlusNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            foreach (Match joined in TextPlus().Matches(line))
            {
                // The opening quote must be code, not a quote inside other text - and the text must start the sum: after a
                // std::string and a +, it is joined to that string already.
                if (masked[index].Length <= joined.Index || masked[index][joined.Index] != '"' ||
                    Regex.IsMatch(masked[index][..joined.Index], @"\+\s*$")) continue;

                var number = joined.Groups["number"].Value;
                var isNumber = char.IsDigit(number[0]) ||
                               Regex.IsMatch(code, $@"\b(?:(?:unsigned|signed|long|short)\s+)*(?:int|long|short|size_t|unsigned|double|float|u?int\d+_t)\s+(?:[A-Za-z_]\w*\s*(?:=[^,;]*)?,\s*)*{Regex.Escape(number)}\b(?!\s*\()") ||
                               Regex.IsMatch(code, $@"\bauto\s+{Regex.Escape(number)}\s*=\s*-?\d+\s*;");
                if (!isNumber) continue;

                var numberGroup = joined.Groups["number"];
                var text = joined.Groups["text"].Value;
                yield return new LogicFinding(id, index + 1,
                    $"{text} is not a std::string but the address of its first character, so + {number} moves that address along by {number} " +
                    "characters instead of joining - \"Total: \" + 3 is \"al: \" - and past the end of the text it reads whatever memory comes next",
                    LocalFix.ReplaceLine(id, $"Join it as text: {text} + std::to_string({number})",
                        $"std::to_string writes {number} as text, and a std::string joined to text in quotes makes the two one.",
                        source.Path, index + 1, line[..numberGroup.Index] + $"std::to_string({number})" + line[(numberGroup.Index + numberGroup.Length)..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> ReserveThenIndex(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var reserved = Regex.Match(masked[index], @"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*\.\s*reserve\s*\(");
            if (!reserved.Success) continue;

            var name = reserved.Groups["name"].Value;
            var addsItems = new Regex($@"(?<![\w.]){Regex.Escape(name)}\s*\.\s*(?:push_back|emplace_back|resize|assign|insert|emplace|clear)\s*\(|(?<![\w.]){Regex.Escape(name)}\s*=(?!=)");
            var written = new Regex($@"(?<![\w.]){Regex.Escape(name)}\s*\[[^\]]+\]\s*=(?!=)");
            var firstWrite = -1;
            for (var later = index + 1; later < masked.Count && !addsItems.IsMatch(masked[later]); later++)
                if (written.IsMatch(masked[later])) { firstWrite = later; break; }
            if (firstWrite < 0) continue;

            var line = source.Lines[index];
            var reserve = masked[index].IndexOf("reserve", reserved.Index, StringComparison.Ordinal);

            yield return new LogicFinding(id, firstWrite + 1,
                $"reserve on line {index + 1} makes room in {name} without making any items, so its size is still 0, and {name}[...] = writes past " +
                "its end - nothing is added, and its size stayed 0",
                LocalFix.ReplaceLine(id, $"Make the items: {name}.resize(...)",
                    $"resize makes that many items, each 0, so {name}[...] = gives one of them its value.",
                    source.Path, index + 1, line[..reserve] + "resize" + line[(reserve + "reserve".Length)..]));
        }
    }
}
