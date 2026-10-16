using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// What a program reads in, used as if it were something else - each run with the language's own toolchain, typing the input,
/// and its own checks said nothing of any: Go's ReadString('\n'), Ruby's gets, PHP's fgets, C's fgets and Rust's read_line
/// keep the newline that ended the line, so "yes\n" never equals "yes" and the program said no to yes; Java's nextInt stops
/// before the newline, so the nextLine after it reads nothing, and the name was empty; and PowerShell's Read-Host gives text,
/// which -gt compares as text, so "7" -gt 17 was true. InputLiveTests runs each mistake, typing its input, and its change.
/// </summary>
public static partial class InputPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-go-input-not-trimmed", CodePattern.Files(".go"), Syntax.CLike, GoReadString, Severity.Error, Confidence.Certain),
        new CodePattern("logic-ruby-input-not-chomped", CodePattern.Files(".rb"), Syntax.Python, RubyGets, Severity.Error, Confidence.Certain),
        new CodePattern("logic-php-input-not-trimmed", CodePattern.Files(".php"), Syntax.CLike, PhpFgets, Severity.Error, Confidence.Certain),
        new CodePattern("logic-c-fgets-newline", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, CFgets, Severity.Error, Confidence.Certain),
        new CodePattern("logic-rust-input-not-trimmed", CodePattern.Files(".rs"), Syntax.CLike, RustReadLine, Severity.Error, Confidence.Certain),
        new CodePattern("logic-java-nextline-after-nextint", CodePattern.Files(".java"), Syntax.CLike, JavaNextLineAfterNumber, Severity.Error),
        new CodePattern("logic-powershell-read-host-compared-as-text", CodePattern.Files(".ps1"), Syntax.Python, PowerShellReadHost, Severity.Error, Confidence.Certain),
    ];

    /// <summary>Whether a later line compares <paramref name="name"/> with text in quotes, as <paramref name="comparison"/> writes it.</summary>
    private static bool ComparedWithTextLater(IReadOnlyList<string> masked, int after, string comparison) =>
        masked.Skip(after + 1).Any(line => Regex.IsMatch(line, comparison));

    /// <summary><c>answer, _ := reader.ReadString('\n')</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)\s*,\s*[A-Za-z_]\w*\s*:?=\s*[A-Za-z_][\w.]*\.ReadString\(\s*'\\n'\s*\)\s*$")]
    private static partial Regex GoReadStringLine();

    [GeneratedRegex(@"^\s*import\s*(?:\(\s*[^)]*""strings""|""strings"")", RegexOptions.Multiline)]
    private static partial Regex GoImportsStrings();

    /// <summary>
    /// A line read with ReadString('\n') compared with text: it ends with the newline it stopped at. The change trims it on the
    /// line after - with strings.TrimSpace, when the file imports strings.
    /// </summary>
    private static IEnumerable<LogicFinding> GoReadString(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            // Read from the line as written: masking blanks the inside of the '\n' it is given.
            if (GoReadStringLine().Match(source.Lines[index]) is not { Success: true } read || masked[index].Trim().Length == 0) continue;

            var name = read.Groups["name"].Value;
            if (!ComparedWithTextLater(masked, index, $@"(?<![\w.]){Regex.Escape(name)}\s*[!=]=\s*""|""\s*[!=]=\s*{Regex.Escape(name)}\b")) continue;

            // The import as written - masking blanks the "strings" it names.
            var fix = GoImportsStrings().IsMatch(string.Join("\n", source.Lines))
                ? LocalFix.Insert(id, $"Trim the line as it is read: {name} = strings.TrimSpace({name})",
                    "strings.TrimSpace takes the newline - and any spaces - off both ends, so the text is what was typed.",
                    source.Path, index + 2, [$"{read.Groups["indent"].Value}{name} = strings.TrimSpace({name})"])
                : null;

            yield return new LogicFinding(id, index + 1,
                $"ReadString('\\n') gives back the line with the newline it stopped at still on the end - \"yes\\n\", not \"yes\" - so comparing " +
                $"{name} with text is never true for what was typed",
                fix);
        }
    }

    /// <summary><c>answer = gets</c>, as it is read.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[a-z_]\w*)\s*=\s*(?<read>(?:\$stdin\.|STDIN\.)?gets)\s*$")]
    private static partial Regex RubyGetsLine();

    /// <summary>A line read with gets compared with text: it keeps its newline until chomp takes it off. The change chomps it as it is read.</summary>
    private static IEnumerable<LogicFinding> RubyGets(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RubyGetsLine().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            if (!ComparedWithTextLater(masked, index, $@"(?<![\w.@$]){Regex.Escape(name)}\s*[!=]=\s*""|""\s*[!=]=\s*{Regex.Escape(name)}\b")) continue;

            yield return new LogicFinding(id, index + 1,
                $"gets gives back the line with its newline still on the end - \"yes\\n\", not \"yes\" - so comparing {name} with text is never " +
                "true for what was typed",
                LocalFix.ReplaceLine(id, $"Take the newline off as it is read: {name} = {read.Groups["read"].Value}.chomp",
                    "chomp takes the newline off the end of the line, so the text is what was typed.",
                    source.Path, index + 1, $"{read.Groups["indent"].Value}{name} = {read.Groups["read"].Value}.chomp"));
        }
    }

    /// <summary><c>$answer = fgets(STDIN);</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)\$(?<name>\w+)\s*=\s*(?<read>fgets\(\s*(?:STDIN|\$\w+)\s*\))\s*;\s*$")]
    private static partial Regex PhpFgetsLine();

    /// <summary>A line read with fgets compared with text: it ends with its newline. The change trims it as it is read.</summary>
    private static IEnumerable<LogicFinding> PhpFgets(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PhpFgetsLine().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            if (!ComparedWithTextLater(masked, index, $@"\${Regex.Escape(name)}\b\s*[!=]==?\s*[""']|[""']\s*[!=]==?\s*\${Regex.Escape(name)}\b")) continue;

            var line = source.Lines[index];
            var readCall = line.Substring(read.Groups["read"].Index, read.Groups["read"].Length);

            yield return new LogicFinding(id, index + 1,
                $"fgets gives back the line with its newline still on the end - \"yes\\n\", not \"yes\" - so comparing ${name} with text is never " +
                "true for what was typed",
                LocalFix.ReplaceLine(id, $"Trim the line as it is read: ${name} = trim({readCall});",
                    "trim takes the newline - and any spaces - off both ends, so the text is what was typed.",
                    source.Path, index + 1, $"{read.Groups["indent"].Value}${name} = trim({readCall});"));
        }
    }

    /// <summary><c>fgets(answer, sizeof answer, stdin);</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?:if\s*\(\s*)?fgets\(\s*(?<buffer>[A-Za-z_]\w*)\s*,.*\)\s*;\s*$")]
    private static partial Regex CFgetsLine();

    [GeneratedRegex(@"^\s*#\s*include\s*<(?:string\.h|cstring)>", RegexOptions.Multiline)]
    private static partial Regex IncludesString();

    /// <summary>
    /// A line read with fgets and compared with strcmp: fgets keeps the newline in the buffer, so strcmp never finds it equal
    /// to the text. The change cuts the line at its newline on the line after - strcspn, from &lt;string.h&gt;, finds it.
    /// </summary>
    private static IEnumerable<LogicFinding> CFgets(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CFgetsLine().Match(masked[index]) is not { Success: true } read || masked[index].TrimStart().StartsWith("if", StringComparison.Ordinal)) continue;

            var buffer = read.Groups["buffer"].Value;
            if (!ComparedWithTextLater(masked, index, $@"\bstrcmp\(\s*{Regex.Escape(buffer)}\s*,\s*""|\bstrcmp\(\s*""[^""]*""\s*,\s*{Regex.Escape(buffer)}\s*\)")) continue;

            var fix = IncludesString().IsMatch(string.Join("\n", source.Lines))
                ? LocalFix.Insert(id, $"Cut the newline off: {buffer}[strcspn({buffer}, \"\\n\")] = '\\0';",
                    $"strcspn finds where the newline is in {buffer}, and putting '\\0' there ends the text before it.",
                    source.Path, index + 2, [$"{read.Groups["indent"].Value}{buffer}[strcspn({buffer}, \"\\n\")] = '\\0';"])
                : null;

            yield return new LogicFinding(id, index + 1,
                $"fgets keeps the newline at the end of the line it reads - {buffer} holds \"yes\\n\", not \"yes\" - so strcmp never finds it " +
                "equal to the text",
                fix);
        }
    }

    /// <summary><c>io::stdin().read_line(&amp;mut answer)</c>.</summary>
    [GeneratedRegex(@"\.read_line\(\s*&mut\s+(?<name>[a-z_]\w*)\s*\)")]
    private static partial Regex RustReadLineCall();

    /// <summary>
    /// A line read with read_line compared with text: read_line adds the line to the string with its newline. The change
    /// compares the trimmed text.
    /// </summary>
    private static IEnumerable<LogicFinding> RustReadLine(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RustReadLineCall().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            var compared = new Regex($@"(?<![\w.]){Regex.Escape(name)}(?<op>\s*[!=]=\s*"")");

            for (var later = index + 1; later < masked.Count; later++)
            {
                if (compared.Match(masked[later]) is not { Success: true } comparison) continue;

                var line = source.Lines[later];
                var nameEnd = comparison.Index + name.Length;

                yield return new LogicFinding(id, later + 1,
                    $"read_line adds the line to {name} with its newline still on the end - \"yes\\n\", not \"yes\" - so comparing {name} with text " +
                    "is never true for what was typed",
                    LocalFix.ReplaceLine(id, $"Compare the trimmed text: {name}.trim()",
                        $"trim gives {name} without the newline - and any spaces - at either end, so it is what was typed.",
                        source.Path, later + 1, line[..nameEnd] + ".trim()" + line[nameEnd..]));
                break;
            }
        }
    }

    /// <summary><c>int age = scanner.nextInt();</c> - a number read, and the line it was on left unread.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?:[\w<>\[\]]+\s+)?[A-Za-z_]\w*\s*=\s*(?<scanner>[A-Za-z_]\w*)\.next(?:Int|Double|Long|Float|Short|Byte|Boolean)?\(\)\s*;\s*$")]
    private static partial Regex NumberRead();

    /// <summary>
    /// A Scanner's nextInt - or nextDouble, or next - followed by its nextLine: nextInt stops before the newline that ended the
    /// number's line, so the nextLine after it reads what is left of that line - nothing. The change reads that rest of the
    /// line, and leaves it, before the nextLine.
    /// </summary>
    private static IEnumerable<LogicFinding> JavaNextLineAfterNumber(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (NumberRead().Match(masked[index]) is not { Success: true } read) continue;

            var scanner = read.Groups["scanner"].Value;
            var readNext = new Regex($@"\b{Regex.Escape(scanner)}\.next(?<what>\w*)\(\)");

            // The next line that reads from the same scanner.
            var next = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(line => readNext.IsMatch(masked[line]), -1);
            if (next < 0 || readNext.Match(masked[next]).Groups["what"].Value != "Line") continue;

            yield return new LogicFinding(id, next + 1,
                $"{scanner}.next... on line {index + 1} reads the number and stops before the newline after it, so this nextLine reads what is left " +
                "of that line - nothing - and gives back empty text",
                LocalFix.Insert(id, $"Read the rest of the number's line first: {scanner}.nextLine();",
                    $"{scanner}.nextLine() on its own reads the newline the number left, so the next nextLine reads the line typed after it.",
                    source.Path, index + 2, [$"{read.Groups["indent"].Value}{scanner}.nextLine();"]));
        }
    }

    /// <summary><c>$age = Read-Host "Age"</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)\$(?<name>\w+)\s*=\s*(?<read>Read-Host\b.*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadHostLine();

    /// <summary>
    /// Text from Read-Host compared with a number by -gt, -lt, -ge or -le: PowerShell compares as the left side is - text,
    /// letter by letter - so "7" -gt 17 is true, as "7" comes after "1". The change reads the text as a whole number.
    /// </summary>
    private static IEnumerable<LogicFinding> PowerShellReadHost(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ReadHostLine().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            if (!ComparedWithTextLater(masked, index, $@"(?i)\${Regex.Escape(name)}\b\s*-(?:gt|lt|ge|le)\s+-?\d")) continue;

            var line = source.Lines[index];
            var readCall = line.Substring(read.Groups["read"].Index, read.Groups["read"].Length);

            yield return new LogicFinding(id, index + 1,
                $"Read-Host gives back text, and PowerShell compares text with -gt and -lt as text, character by character - so \"7\" -gt 17 is " +
                $"true, as \"7\" comes after \"1\" - and ${name} is compared with numbers",
                LocalFix.ReplaceLine(id, $"Read it as a whole number: ${name} = [int]({readCall})",
                    "[int] turns the text typed into a whole number, so -gt compares it as one.",
                    source.Path, index + 1, $"{read.Groups["indent"].Value}${name} = [int]({readCall})"));
        }
    }
}
