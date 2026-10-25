using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// total =+ mark, where total += mark was meant: =+ is = and then a plus sign, so total is given +mark - the mark on its own -
/// and what it held is lost; total =- mark gives it -mark. Each was run with the language's own toolchain, adding 5 and 10, and
/// each printed 10 where 15 was meant, with nothing said by gcc and g++ -Wall -Wextra, javac -Xlint:all, the C# compiler,
/// go vet, ruby -w, Python, PHP, Node.js or PowerShell. Perl warns of it (Reversed += operator), and Scala reads =+ as an
/// operator Int does not have, so neither is here. The change is += (or -=). ReversedAssignmentLiveTests runs each mistake,
/// and its change.
/// </summary>
public static partial class ReversedAssignmentPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        Reversed("logic-plus-equals-reversed", CodePattern.Files(".c", ".cpp", ".cc", ".cxx", ".c++", ".java", ".cs", ".js", ".mjs", ".cjs"), Syntax.CLike,
            @"[A-Za-z_$][\w$]*(?:(?:\.|->)[A-Za-z_$][\w$]*|\[[^\]=]+\])*"),
        Reversed("logic-go-plus-equals-reversed", CodePattern.Files(".go"), Syntax.CLike, @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*|\[[^\]=]+\])*"),
        Reversed("logic-python-plus-equals-reversed", CodePattern.Files(".py", ".pyw"), Syntax.Python, @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*|\[[^\]=]+\])*"),
        Reversed("logic-ruby-plus-equals-reversed", CodePattern.Files(".rb"), Syntax.Python, @"(?:@@?|\$)?[a-z_]\w*(?:\[[^\]=]+\])*"),
        Reversed("logic-php-plus-equals-reversed", CodePattern.Files(".php"), Syntax.CLike, @"\$\w+(?:->\w+|\[[^\]=]*\])*"),
        Reversed("logic-powershell-plus-equals-reversed", CodePattern.Files(".ps1"), Syntax.Python, @"\$[\w:]+(?:\.\w+|\[[^\]=]+\])*"),
    ];

    private static CodePattern Reversed(string id, IReadOnlySet<string> extensions, Syntax syntax, string target) =>
        new(id, extensions, syntax, (patternId, source, masked) => ReversedAssignment(patternId, source, masked, new Regex(
                // =+ with nothing between them, and not =++; =- with a space after it, as total =- mark, and not total =-1, a
                // negative number given.
                $@"^(?<indent>\s*)(?<target>{target})(?<gap>\s*)=(?:(?<sign>\+)(?![+=])|(?<=\s=)(?<sign>-)(?=\s))(?<value>\s*\S.*)$")),
            Severity.Error, Confidence.Likely);

    private static IEnumerable<LogicFinding> ReversedAssignment(string id, SourceFile source, IReadOnlyList<string> masked, Regex reversed)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (reversed.Match(masked[index]) is not { Success: true } assignment) continue;

            var line = source.Lines[index];
            var target = assignment.Groups["target"].Value;
            var sign = assignment.Groups["sign"].Value;
            var valueGroup = assignment.Groups["value"];
            var value = line.Substring(valueGroup.Index, valueGroup.Length).TrimStart();
            var adding = sign == "+";

            yield return new LogicFinding(id, index + 1,
                $"={sign} is = and then a {(adding ? "plus" : "minus")} sign: {target} is given {sign}{value.TrimEnd(';').TrimEnd()} - " +
                $"{(adding ? "the value on its own" : "the value made negative")} - and what {target} held is lost, where {sign}= " +
                $"{(adding ? "adds to it" : "takes away from it")}",
                LocalFix.ReplaceLine(id, $"{(adding ? "Add" : "Take away")} with {sign}=: {target} {sign}= {value.TrimEnd(';').TrimEnd()}",
                    $"{sign}= {(adding ? "adds the value to" : "takes the value away from")} what {target} already holds, so each time " +
                    "round it builds on the last.",
                    source.Path, index + 1, $"{assignment.Groups["indent"].Value}{target} {sign}= {value}"));
        }
    }
}
