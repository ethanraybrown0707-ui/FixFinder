using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Mistakes PowerShell scripts make that Windows PowerShell 5.1 runs without a word - each run with it: <c>if ($mark &gt; 40)</c>
/// writes $mark to a file named 40, and the condition is then nothing, so false; <c>if ($mark = 40)</c> gives $mark the value
/// and the condition is that value; a loop to <c>$i -le $marks.Count</c> takes one pass too many, reading $null; and a
/// function called as <c>Area(3, 4)</c> is given one value, the list 3, 4, and nothing for its second parameter.
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
    ];

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
}
