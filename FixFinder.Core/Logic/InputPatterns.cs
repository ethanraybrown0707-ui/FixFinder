using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// What a program reads in, used as if it were something else - each run with the language's own toolchain, typing the input,
/// and its own checks said nothing of any: Go's ReadString('\n'), Ruby's gets, PHP's fgets, C's fgets and Rust's read_line
/// keep the newline that ended the line, so "yes\n" never equals "yes" and the program said no to yes; Java's nextInt stops
/// before the newline, so the nextLine after it reads nothing, and the name was empty - as did C++'s getline after cin &gt;&gt;,
/// and C's fgets after scanf, for 21 and then Ada typed; and PowerShell's Read-Host gives text,
/// which -gt compares as text, so "7" -gt 17 was true. InputLiveTests runs each mistake, typing its input, and its change.
/// A name given a new value before it is compared - answer = strings.TrimSpace(answer), answer.chomp!, $age = [int]$age - holds
/// what that line made of it, not what was read: each such program, run with the language typing yes or 7, did what it meant
/// to, and is not found.
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
        new CodePattern("logic-cpp-getline-after-cin", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, CppGetlineAfterCin, Severity.Error),
        new CodePattern("logic-c-fgets-after-scanf", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, CFgetsAfterScanf, Severity.Error),
        new CodePattern("logic-powershell-read-host-compared-as-text", CodePattern.Files(".ps1"), Syntax.Python, PowerShellReadHost, Severity.Error, Confidence.Certain),
    ];

    /// <summary>
    /// Whether a later line compares the name read, as <paramref name="comparison"/> writes it, before a line gives the name a
    /// new value, as <paramref name="givenAgain"/> writes it - after that, the name holds what that line made of it.
    /// </summary>
    private static bool ComparedBeforeGivenAgain(IReadOnlyList<string> masked, int after, string comparison, string givenAgain)
    {
        foreach (var line in masked.Skip(after + 1))
        {
            if (Regex.IsMatch(line, comparison)) return true;
            if (Regex.IsMatch(line, givenAgain)) return false;
        }

        return false;
    }

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
            if (!ComparedBeforeGivenAgain(masked, index, $@"(?<![\w.]){Regex.Escape(name)}\s*[!=]=\s*""|""\s*[!=]=\s*{Regex.Escape(name)}\b",
                    $@"(?<![\w.]){Regex.Escape(name)}\s*:?=(?!=)")) continue;

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
            if (!ComparedBeforeGivenAgain(masked, index, $@"(?<![\w.@$]){Regex.Escape(name)}\s*[!=]=\s*""|""\s*[!=]=\s*{Regex.Escape(name)}\b",
                    $@"(?<![\w.@$]){Regex.Escape(name)}(?:\s*=(?![=~])|\.(?:chomp|chop|strip|rstrip)!)")) continue;

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
            if (!ComparedBeforeGivenAgain(masked, index, $@"\${Regex.Escape(name)}\b\s*[!=]==?\s*[""']|[""']\s*[!=]==?\s*\${Regex.Escape(name)}\b",
                    $@"\${Regex.Escape(name)}\b\s*=(?![=>])")) continue;

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
    /// to the text. The change cuts the line at its newline on the line after - strcspn, from &lt;string.h&gt;, finds it. Text
    /// cut short before it is compared - answer[strcspn(answer, "\n")] = '\0', or strtok(answer, "\n") - is what the change
    /// does, and is not found.
    /// </summary>
    private static IEnumerable<LogicFinding> CFgets(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (CFgetsLine().Match(masked[index]) is not { Success: true } read || masked[index].TrimStart().StartsWith("if", StringComparison.Ordinal)) continue;

            var buffer = read.Groups["buffer"].Value;
            var bufferPattern = Regex.Escape(buffer);
            var comparison = new Regex($@"\bstrcmp\(\s*{bufferPattern}\s*,\s*""|\bstrcmp\(\s*""[^""]*""\s*,\s*{bufferPattern}\s*\)");
            var comparedOn = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(line => comparison.IsMatch(masked[line]), -1);
            if (comparedOn < 0) continue;

            var cutShort = new Regex($@"(?<![\w.]){bufferPattern}\s*\[[^\]]*\]\s*=(?!=)|\bstrtok\s*\(\s*{bufferPattern}\s*,");
            if (Enumerable.Range(index + 1, comparedOn - index - 1).Any(line => cutShort.IsMatch(masked[line]))) continue;

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
    /// compares the trimmed text. A name given a new value first - let answer = answer.trim(); - is compared as that line made
    /// it, and is not found.
    /// </summary>
    private static IEnumerable<LogicFinding> RustReadLine(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (RustReadLineCall().Match(masked[index]) is not { Success: true } read) continue;

            var name = read.Groups["name"].Value;
            var compared = new Regex($@"(?<![\w.]){Regex.Escape(name)}(?<op>\s*[!=]=\s*"")");
            var givenAgain = new Regex($@"\blet\s+(?:mut\s+)?{Regex.Escape(name)}\b|(?<![\w.]){Regex.Escape(name)}\s*=(?!=)");

            for (var later = index + 1; later < masked.Count; later++)
            {
                if (compared.Match(masked[later]) is not { Success: true } comparison)
                {
                    if (givenAgain.IsMatch(masked[later])) break;
                    continue;
                }

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

    /// <summary>
    /// <c>scanner.nextLine();</c> on a line of its own - the rest of a number's line read and thrown away, which is how the
    /// newline a number leaves is put out of the way.
    /// </summary>
    internal static bool RestOfLineThrownAway(string maskedLine, string scanner) =>
        Regex.IsMatch(maskedLine, $@"^\s*{Regex.Escape(scanner)}\s*\.\s*nextLine\s*\(\s*\)\s*;\s*$");

    /// <summary><c>int age = scanner.nextInt();</c> - a number read, and the line it was on left unread.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?:[\w<>\[\]]+\s+)?[A-Za-z_]\w*\s*=\s*(?<scanner>[A-Za-z_]\w*)\.next(?:Int|Double|Long|Float|Short|Byte|Boolean)?\(\)\s*;\s*$")]
    private static partial Regex NumberRead();

    /// <summary>
    /// A Scanner's nextInt - or nextDouble, or next - followed by its nextLine: nextInt stops before the newline that ended the
    /// number's line, so the nextLine after it reads what is left of that line - nothing. The change reads that rest of the
    /// line, and leaves it, before the nextLine - so a nextLine whose text is thrown away, a line of its own, is that change
    /// already made, and is not found.
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
            if (next < 0 || readNext.Match(masked[next]).Groups["what"].Value != "Line" || RestOfLineThrownAway(masked[next], scanner)) continue;

            yield return new LogicFinding(id, next + 1,
                $"{scanner}.next... on line {index + 1} reads the number and stops before the newline after it, so this nextLine reads what is left " +
                "of that line - nothing - and gives back empty text",
                LocalFix.Insert(id, $"Read the rest of the number's line first: {scanner}.nextLine();",
                    $"{scanner}.nextLine() on its own reads the newline the number left, so the next nextLine reads the line typed after it.",
                    source.Path, index + 2, [$"{read.Groups["indent"].Value}{scanner}.nextLine();"]));
        }
    }

    /// <summary><c>std::cin &gt;&gt; age;</c> - a value read with &gt;&gt;, and the line it was on left unread.</summary>
    [GeneratedRegex(@"^\s*(?:std::)?cin\s*>>(?!.*\bgetline\b).*;\s*$")]
    private static partial Regex CinRead();

    /// <summary>A line that reads from cin in any way: &gt;&gt;, getline, ignore, get.</summary>
    [GeneratedRegex(@"\b(?:std::)?cin\b")]
    private static partial Regex ReadsCin();

    /// <summary><c>std::getline(std::cin, name)</c>, with nothing in it that skips the newline first.</summary>
    [GeneratedRegex(@"\bgetline\s*\(\s*(?<stream>(?:std::)?cin)\s*,\s*(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex GetlineFromCin();

    /// <summary>
    /// cin &gt;&gt; followed by getline from cin: &gt;&gt; stops before the newline that ended the value's line, so the getline after
    /// it reads what is left of that line - nothing - and the name read is empty. The change skips that newline, and any spaces,
    /// with std::ws before getline reads.
    /// </summary>
    private static IEnumerable<LogicFinding> CppGetlineAfterCin(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (!CinRead().IsMatch(masked[index])) continue;

            // The next line that reads from cin.
            var next = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(line => ReadsCin().IsMatch(masked[line]), -1);
            if (next < 0 || GetlineFromCin().Match(masked[next]) is not { Success: true } getline) continue;

            var line = source.Lines[next];
            var stream = getline.Groups["stream"];
            var skip = stream.Value.StartsWith("std::", StringComparison.Ordinal) ? "std::ws" : "ws";
            var name = getline.Groups["name"].Value;

            yield return new LogicFinding(id, next + 1,
                $">> on line {index + 1} reads the value and stops before the newline typed after it, so this getline reads what is left of " +
                $"that line - nothing - and {name} is empty",
                LocalFix.ReplaceLine(id, $"Skip the newline first: getline({stream.Value} >> {skip}, {name})",
                    $"{stream.Value} >> {skip} reads past the newline the value left, and any spaces, so getline reads the line typed after it.",
                    source.Path, next + 1, line[..stream.Index] + $"{stream.Value} >> {skip}" + line[(stream.Index + stream.Length)..]));
        }
    }

    /// <summary><c>scanf("%d", &amp;age);</c> - the opening quote of its format, which masking blanks.</summary>
    [GeneratedRegex(@"^\s*scanf\s*\(\s*""")]
    private static partial Regex ScanfCall();

    /// <summary>A line that reads input in any of C's ways.</summary>
    [GeneratedRegex(@"\b(?:scanf|fgets|getchar|gets|getline|fgetc|getc)\s*\(")]
    private static partial Regex ReadsInput();

    /// <summary><c>fgets(name, sizeof name, stdin)</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*).*\bfgets\s*\(\s*(?<buffer>[A-Za-z_]\w*)\s*,.*\bstdin\b")]
    private static partial Regex FgetsFromStdin();

    /// <summary>
    /// scanf followed by fgets from stdin: scanf stops before the newline that ended the number's line - unless its format ends
    /// with a space or \n, which skip it - so the fgets after it reads what is left of that line, the newline alone. The change
    /// reads the rest of that line, up to and with its newline, before fgets - and stops at the end of the input, so it never waits.
    /// </summary>
    private static IEnumerable<LogicFinding> CFgetsAfterScanf(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var namesInCode = masked.SelectMany(code => Regex.Matches(code, @"[A-Za-z_]\w*")).Select(found => found.Value).ToHashSet(StringComparer.Ordinal);
        var rest = new[] { "rest", "leftover", "skipped" }.FirstOrDefault(name => !namesInCode.Contains(name));
        if (rest is null) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (ScanfCall().Match(masked[index]) is not { Success: true } scanf) continue;

            // The format, read from the line as written: one whose end skips the newline leaves nothing for fgets.
            var line = source.Lines[index];
            var close = line.IndexOf('"', scanf.Index + scanf.Length);
            if (close < 0) continue;
            var format = line[(scanf.Index + scanf.Length)..close];
            if (!format.Contains('%') || format.EndsWith(' ') || format.EndsWith("\\n", StringComparison.Ordinal)) continue;

            // The next line that reads input.
            var next = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(later => ReadsInput().IsMatch(masked[later]), -1);
            if (next < 0 || FgetsFromStdin().Match(masked[next]) is not { Success: true } fgets) continue;

            var buffer = fgets.Groups["buffer"].Value;
            var indent = fgets.Groups["indent"].Value;

            yield return new LogicFinding(id, next + 1,
                $"scanf on line {index + 1} reads the number and stops before the newline typed after it, so this fgets reads what is left of " +
                $"that line - the newline alone - and {buffer} holds nothing that was typed",
                LocalFix.Insert(id, "Read the rest of the number's line first",
                    $"The loop reads what is left of the number's line, up to and with its newline - stopping at the end of the input too - so " +
                    "fgets reads the line typed after it.",
                    source.Path, next + 1, [$"{indent}for (int {rest} = getchar(); {rest} != '\\n' && {rest} != EOF; {rest} = getchar()) {{ }}"]));
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
            if (!ComparedBeforeGivenAgain(masked, index, $@"(?i)\${Regex.Escape(name)}\b\s*-(?:gt|lt|ge|le)\s+-?\d",
                    $@"(?i)\${Regex.Escape(name)}\b\s*=")) continue;

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
