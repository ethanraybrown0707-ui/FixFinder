using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes PowerShell scripts make that Windows PowerShell 5.1 runs without a word - each run with it: <c>if ($mark &gt; 40)</c>
/// writes $mark to a file named 40, and the condition is then nothing, so false; <c>if ($mark = 40)</c> gives $mark the value
/// and the condition is that value; a loop to <c>$i -le $marks.Count</c> takes one pass too many, reading $null; a
/// function called as <c>Area(3, 4)</c> is given one value, the list 3, 4, and nothing for its second parameter; and
/// <c>Write-Host "Total: " + $total</c>, which wrote "Total:  + 150", as each word - the + too - is a thing to write.
/// </summary>
public static partial class PowerShellLogicPatterns
{
    private static readonly IReadOnlySet<string> PowerShellFiles = CodePattern.Files(".ps1");

    private static CodePattern Pattern(
        string id, Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
        Severity severity = Severity.Warning, Confidence confidence = Confidence.Likely) =>
        new(id, PowerShellFiles, Syntax.Python, find, severity, confidence);

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Pattern("logic-powershell-greater-than-writes-a-file", GreaterThanWritesAFile, Severity.Error, Confidence.Certain),
        Pattern("logic-powershell-assignment-in-condition", AssignmentInCondition, Severity.Error, Confidence.Certain),
        Pattern("logic-powershell-loop-to-count", LoopToCount, Severity.Error),
        Pattern("logic-powershell-function-called-with-brackets", FunctionCalledWithBrackets, Severity.Error, Confidence.Certain),
        Pattern("logic-powershell-property-in-text", PropertyInText, Severity.Error, Confidence.Certain),
        Pattern("logic-powershell-variable-in-single-quotes", VariableInSingleQuotes, Severity.Error),
        Pattern("logic-powershell-plus-outside-brackets", PlusOutsideBrackets, Severity.Error, Confidence.Certain),
    ];

    /// <summary>The text on a line - between double quotes, with ` escaping, or single ones, with '' for a quote - each with its quote.</summary>
    private static IEnumerable<(char Quote, int Start, int End)> TextIn(string line)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '#') yield break;
            if (line[index] is not ('"' or '\'')) continue;

            var quote = line[index];
            var end = index + 1;
            while (end < line.Length)
            {
                if (quote == '"' && line[end] == '`') end += 2;
                else if (line[end] == quote && end + 1 < line.Length && line[end + 1] == quote) end += 2;
                else if (line[end] == quote) break;
                else end++;
            }

            if (end >= line.Length) yield break;
            yield return (quote, index + 1, end);
            index = end;
        }
    }

    /// <summary><c>$marks.Count</c> or <c>$marks[0]</c> - a variable with a property or a position after it - not already in $( ).</summary>
    [GeneratedRegex(@"(?<!\$\()(?<!`)\$(?<name>[A-Za-z_]\w*)(?<after>\.[A-Za-z_]\w*|\[[^\]]+\])")]
    private static partial Regex VariableWithMore();

    /// <summary>
    /// "$marks.Count" in double quotes: PowerShell puts a variable's value into text, but only the variable - $marks - so the .Count
    /// after it stays as it is written, and 70 80 90.Count is printed. $( ) puts the whole expression's value in.
    /// </summary>
    private static IEnumerable<LogicFinding> PropertyInText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < source.Lines.Count; index++)
        {
            var line = source.Lines[index];
            var inText = TextIn(line).Where(text => text.Quote == '"')
                .SelectMany(text => VariableWithMore().Matches(line[text.Start..text.End]).Select(found => (Found: found, At: text.Start + found.Index)))
                .ToList();
            if (inText.Count == 0) continue;

            var changed = line;
            foreach (var (found, at) in Enumerable.Reverse(inText))
                changed = changed[..at] + $"$({found.Value})" + changed[(at + found.Length)..];

            var first = inText[0].Found;
            yield return new LogicFinding(id, index + 1,
                $"in double quotes PowerShell puts in the value of ${first.Groups["name"].Value} alone, and writes the {first.Groups["after"].Value} " +
                "after it as it is - so the text holds the whole of the variable, and then the words",
                LocalFix.ReplaceLine(id, $"Put the whole expression in: $({first.Value})",
                    $"$( ) inside double quotes puts in the value of everything between its brackets - {first.Value}, not ${first.Groups["name"].Value} alone.",
                    source.Path, index + 1, changed));
        }
    }

    /// <summary>
    /// 'Hello, $name' in single quotes, where the script gives $name a value: PowerShell puts variables' values only into text in
    /// double quotes - text in single quotes is written exactly as it is, $name and all. The quotes become double ones.
    /// </summary>
    private static IEnumerable<LogicFinding> VariableInSingleQuotes(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var given = masked
            .Select(line => Regex.Match(line, @"^\s*\$(?<name>[A-Za-z_]\w*)\s*=(?!=)"))
            .Where(found => found.Success)
            .Select(found => found.Groups["name"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < source.Lines.Count; index++)
        {
            var line = source.Lines[index];

            foreach (var (quote, start, end) in TextIn(line))
            {
                var text = line[start..end];
                if (quote != '\'' || text.Contains('"') || text.Contains("''", StringComparison.Ordinal) || text.Contains('`')) continue;

                var named = Regex.Matches(text, @"\$(?<name>[A-Za-z_]\w*)").Select(found => found.Groups["name"].Value).Where(given.Contains).ToList();
                if (named.Count == 0) continue;

                yield return new LogicFinding(id, index + 1,
                    $"text in single quotes is written exactly as it is, so ${named[0]} is printed as the word ${named[0]} - not its value",
                    LocalFix.ReplaceLine(id, "Put the text in double quotes",
                        $"PowerShell puts a variable's value into text only in double quotes: \"{text}\" holds the value of ${named[0]}.",
                        source.Path, index + 1, line[..(start - 1)] + "\"" + text + "\"" + line[(end + 1)..]));
                break;
            }
        }
    }

    /// <summary>An if, elseif or while whose condition has a &gt; of its own - not -gt, not 2&gt;, not &gt;&gt;.</summary>
    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:if|elseif|while)\s*\((?<left>[^()>]*?)\s*(?<symbol>(?<![-\w>])>(?![>=]))\s*(?<right>[^()]*?)\)", RegexOptions.IgnoreCase)]
    private static partial Regex GreaterThanInCondition();

    private static IEnumerable<LogicFinding> GreaterThanWritesAFile(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (GreaterThanInCondition().Match(masked[index]) is not { Success: true } condition) continue;

            var symbol = condition.Groups["symbol"];
            var right = source.Lines[index].Substring(condition.Groups["right"].Index, condition.Groups["right"].Length).Trim();
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"> in PowerShell sends output to a file, so this writes {condition.Groups["left"].Value.Trim()} to a file named {right} in the " +
                "folder the script runs in, and the condition is then nothing - false every time",
                LocalFix.ReplaceLine(id, "Compare with -gt",
                    "-gt is PowerShell's \"is greater than\"; > sends what is on its left to the file named on its right.",
                    source.Path, index + 1, line[..symbol.Index] + "-gt" + line[(symbol.Index + symbol.Length)..]));
        }
    }

    /// <summary><c>if ($mark = 40)</c>: a value stored, not compared, with a literal - so the condition is the same every time.</summary>
    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:if|elseif|while)\s*\(\s*\$(?<name>\w+)\s*(?<assign>=)(?!=)\s*(?<value>-?\d+(?:\.\d+)?|'[^']*'|""[^""]*""|\$true|\$false)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex StoredInCondition();

    private static IEnumerable<LogicFinding> AssignmentInCondition(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (StoredInCondition().Match(source.Lines[index]) is not { Success: true } condition) continue;
            if (masked[index][condition.Groups["assign"].Index] != '=') continue;

            var name = condition.Groups["name"].Value;
            var value = condition.Groups["value"].Value;
            var assign = condition.Groups["assign"];
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"`${name} = {value}` gives ${name} the value {value} rather than comparing them, so the condition is that value - the same every " +
                $"time - and ${name}'s own value is lost",
                LocalFix.ReplaceLine(id, $"Compare with -eq: ${name} -eq {value}",
                    "-eq is PowerShell's \"is equal to\"; = gives the variable on its left the value on its right.",
                    source.Path, index + 1, line[..assign.Index] + "-eq" + line[(assign.Index + assign.Length)..]));
        }
    }

    [GeneratedRegex(@"\bfor\s*\(\s*\$(?<variable>\w+)\s*=\s*0\s*;\s*\$\k<variable>\s*(?<comparison>-le)\s*\$(?<array>\w+)\.(?:Count|Length)\s*;", RegexOptions.IgnoreCase)]
    private static partial Regex LoopToCountHeading();

    private static IEnumerable<LogicFinding> LoopToCount(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (LoopToCountHeading().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var variable = loop.Groups["variable"].Value;
            var array = loop.Groups["array"].Value;
            var position = new Regex($@"\${Regex.Escape(array)}\s*\[\s*\${Regex.Escape(variable)}\s*\]", RegexOptions.IgnoreCase);
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => position.IsMatch(masked[line]))) continue;

            var line = source.Lines[index];
            var comparison = loop.Groups["comparison"];

            yield return new LogicFinding(id, index + 1,
                $"`${variable} -le ${array}.Count` lets ${variable} reach the count, so on the loop's last pass `${array}[${variable}]` is one past " +
                "the end - which PowerShell gives as $null, with no error",
                LocalFix.ReplaceLine(id, "Stop the loop below the count: -lt",
                    $"${array}'s positions run from 0 to its count less one, so the loop has to stop while ${variable} is still below the count.",
                    source.Path, index + 1, line[..comparison.Index] + "-lt" + line[(comparison.Index + comparison.Length)..]));
        }
    }

    [GeneratedRegex(@"^\s*function\s+(?<name>[\w-]+)\s*(?:\((?<parameters>[^)]*)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionDeclared();

    private static IEnumerable<LogicFinding> FunctionCalledWithBrackets(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var functions = masked.Select(line => FunctionDeclared().Match(line)).Where(declared => declared.Success)
            .Select(declared => declared.Groups["name"].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var function in functions)
        {
            var called = new Regex($@"(?<![\w-]){Regex.Escape(function)}\((?<arguments>[^()]*,[^()]*)\)", RegexOptions.IgnoreCase);

            for (var index = 0; index < masked.Count; index++)
            {
                if (FunctionDeclared().IsMatch(masked[index]) || called.Match(masked[index]) is not { Success: true } call) continue;

                var line = source.Lines[index];
                var arguments = line.Substring(call.Groups["arguments"].Index, call.Groups["arguments"].Length);
                var spaced = string.Join(" ", arguments.Split(',').Select(argument => argument.Trim()));

                yield return new LogicFinding(id, index + 1,
                    $"`{function}({arguments})` gives {function} one value - the list {arguments} - for its first parameter, and nothing for the " +
                    "rest: PowerShell calls a function with its values after its name, apart, with no brackets or commas",
                    LocalFix.ReplaceLine(id, $"Call it as PowerShell calls a function: {function} {spaced}",
                        $"{function} {spaced} gives each parameter a value of its own, in order.",
                        source.Path, index + 1, line[..call.Index] + $"{function} {spaced}" + line[(call.Index + call.Length)..]));
            }
        }
    }

    /// <summary>
    /// <c>Write-Host "Total: " + $total</c> - a + between the words given to Write-Host or Write-Output, with no brackets round
    /// them, and none of the command's own parameters after.
    /// </summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<call>Write-Host|Write-Output)\s+(?<words>(?:""[^""]*""|'[^']*'|\$\w+(?:\.\w+)*|-?\d+(?:\.\d+)?)(?:\s+\+\s+(?:""[^""]*""|'[^']*'|\$\w+(?:\.\w+)*|-?\d+(?:\.\d+)?))+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PlusOutsideBrackets();

    /// <summary>
    /// Write-Host and Write-Output take each word after them as a thing to write, the + as well - so "Total: " + $total is
    /// three things, written with a space between by Write-Host, and each on a line of its own by Write-Output. Brackets make
    /// it one sum, worked out first.
    /// </summary>
    private static IEnumerable<LogicFinding> PlusOutsideBrackets(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (PlusOutsideBrackets().Match(masked[index]) is not { Success: true } call) continue;

            var line = source.Lines[index];
            var name = call.Groups["call"].Value;
            var words = line.Substring(call.Groups["words"].Index, call.Groups["words"].Length);
            var host = name.Equals("Write-Host", StringComparison.OrdinalIgnoreCase);

            yield return new LogicFinding(id, index + 1,
                $"{name} takes each word after it as a thing to write - the + as well - so {words} is not joined: {name} writes " +
                (host ? "each part with a space between, + and all" : "each part on a line of its own, + and all"),
                LocalFix.ReplaceLine(id, $"Put the sum in brackets: {name} ({words})",
                    "Brackets are worked out first, so the parts are joined - or added - into one value, and that one value is written.",
                    source.Path, index + 1, $"{call.Groups["indent"].Value}{name} ({words})"));
        }
    }
}
