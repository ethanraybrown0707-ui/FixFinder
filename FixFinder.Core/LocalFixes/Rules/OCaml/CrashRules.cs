using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// <c>Division_by_zero</c>: a whole number divided by zero. The division is given an answer for when the divisor is zero,
/// as an if, which in OCaml gives a value either way.
/// </summary>
/// <remarks>
/// OCaml 5.5's bytecode does not say where the division is: its backtrace starts "Raised by primitive operation at unknown
/// location", and the first place it names is the call of the function that divided. Then the function called there - in
/// the characters OCaml names - is looked up in the program, and when it divides exactly once, that division is the one
/// given an answer.
/// </remarks>
public sealed partial class OCamlDivisionGuard : ILocalFixRule
{
    public string Id => "ocaml-division-guard";

    private const string Advice =
        "Dividing a whole number by zero has no answer, so OCaml stops the program with Division_by_zero. Checking the divisor first " +
        "and giving 0 in that case keeps the program going - change the 0 to whatever an empty case should give.";

    /// <summary>
    /// A whole-number division - / with no dot after it - of a name, a number or a bracketed expression, by a name, a
    /// bracketed expression or a function given one argument, as in <c>total / List.length marks</c>.
    /// </summary>
    [GeneratedRegex(@"(?<dividend>[A-Za-z_][\w.']*|\d+|\([^()]*\))\s*/(?!\.)\s*(?<divisor>\([^()]*\)|[A-Za-z_][\w.']*(?:\s+[a-z_][\w']*)?)")]
    private static partial Regex Division();

    [GeneratedRegex(@", characters (?<start>\d+)-(?<end>\d+)")]
    private static partial Regex Characters();

    /// <summary>A call, as OCaml points at it: <c>(average 10 0)</c> or <c>Helper.divide 10 0</c>.</summary>
    [GeneratedRegex(@"^[\s(]*(?:(?<module>[A-Z][\w']*)\.)?(?<function>[a-z_][\w']*)\s")]
    private static partial Regex Call();

    /// <summary>A line that starts something new at the top of a file, so ends the definition above it.</summary>
    [GeneratedRegex(@"^(?:let|type|module|exception|open|include)\b")]
    private static partial Regex TopLevelStart();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!OCamlCode.Raised(context.Error, "Division_by_zero") || OCamlCode.Locate(context) is not { } at) return null;

        if (OnlyDivisionIn(at.Line) is { } division) return Guarded(at.Source, at.Number, division, Advice);

        if (!context.Error.RawText.Contains("unknown location", StringComparison.Ordinal)) return null;
        if (CalledAt(context, at.Line) is not { } called || DefinitionOf(context, at.Source, called) is not { } definition) return null;

        var divisionsInBody = Enumerable.Range(definition.FirstLine, definition.LastLine - definition.FirstLine + 1)
            .Select(number => (Number: number, Divisions: Division().Matches(CodeText.Mask(definition.Source.Lines[number - 1], Syntax.OCaml))))
            .Where(line => line.Divisions.Count > 0)
            .ToList();
        if (divisionsInBody is not [{ Divisions.Count: 1 } dividingLine]) return null;

        var where = definition.Source.Path == at.Source.Path ? $"line {dividingLine.Number}" : $"line {dividingLine.Number} of {Path.GetFileName(definition.Source.Path)}";

        return Guarded(definition.Source, dividingLine.Number, dividingLine.Divisions[0],
            $"OCaml did not say where the division is - only that Division_by_zero came from the call of {called.Name} on line {at.Number}. " +
            $"{called.Name} divides once, on {where}, so that is the division given an answer. " + Advice);
    }

    private static Match? OnlyDivisionIn(string line) => Division().Matches(CodeText.Mask(line, Syntax.OCaml)) is [var only] ? only : null;

    private LocalFix Guarded(SourceFile source, int number, Match division, string explanation)
    {
        var line = source.Lines[number - 1];
        var divisor = line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = line.Substring(division.Index, division.Length);
        var corrected = line[..division.Index] + $"(if {divisor} = 0 then 0 else {expression})" + line[(division.Index + division.Length)..];

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero", explanation, source.Path, number, corrected);
    }

    /// <summary>The function called in the characters OCaml's frame names, with the module it is in when one is written.</summary>
    private static (string? Module, string Name)? CalledAt(LocalFixContext context, string line)
    {
        if (context.Frame is not { } frame || Characters().Match(frame.RawLine) is not { Success: true } characters) return null;

        var start = int.Parse(characters.Groups["start"].Value);
        var end = int.Parse(characters.Groups["end"].Value);
        if (start >= end || end > line.Length || Call().Match(line[start..end] + " ") is not { Success: true } call) return null;

        return (call.Groups["module"].Success ? call.Groups["module"].Value : null, call.Groups["function"].Value);
    }

    /// <summary>
    /// The lines of a function defined at the top of the file it is in - this file, or the program's file of the module
    /// named - from its let to the line before the next thing defined; null when it is not defined in the program.
    /// </summary>
    private static (SourceFile Source, int FirstLine, int LastLine)? DefinitionOf(LocalFixContext context, SourceFile calling, (string? Module, string Name) called)
    {
        var source = called.Module is null ? calling : ModuleFile(context, calling, called.Module);
        if (source is null) return null;

        var heading = new Regex($@"^let\s+(?:rec\s+)?{Regex.Escape(called.Name)}\b");
        var first = source.Lines.ToList().FindIndex(line => heading.IsMatch(line));
        if (first < 0) return null;

        var last = first;
        while (last + 1 < source.Lines.Count && !TopLevelStart().IsMatch(source.Lines[last + 1])) last++;

        return (source, first + 1, last + 1);
    }

    private static SourceFile? ModuleFile(LocalFixContext context, SourceFile calling, string module)
    {
        if (Path.GetDirectoryName(calling.Path) is not { } folder) return null;

        var file = Directory.EnumerateFiles(folder, "*.ml").FirstOrDefault(candidate => OCamlProgram.ModuleOf(candidate) == module);
        return file is null ? null : context.Read(file);
    }
}

/// <summary>
/// <c>Invalid_argument("index out of bounds")</c> from a loop <c>for i = 0 to Array.length a do</c>: OCaml's for counts
/// up to and including its last number, so the loop's last pass is one past the end.
/// </summary>
public sealed class OCamlForUpperBound : ILocalFixRule
{
    public string Id => "ocaml-for-upper-bound";

    /// <summary>How far above the failing line a loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 6;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!OCamlCode.Raised(context.Error, "Invalid_argument") || context.Error.Message != "index out of bounds") return null;
        if (OCamlCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.OCaml);

        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (OCamlLoops.ForToLength().Match(masked[index]) is { Success: true } loop) return OCamlLoops.UpperBoundFix(Id, at.Source, index + 1, loop);
        }

        return null;
    }
}

/// <summary>The change the crash rule and the logic check both make to a for loop that runs one past the end, so the two are one fix.</summary>
internal static partial class OCamlLoops
{
    /// <summary><c>for i = 0 to Array.length marks do</c> - with no - 1 after the length.</summary>
    [GeneratedRegex(@"\bfor\s+(?<variable>[a-z_][\w']*)\s*=\s*0\s+to\s+(?<length>(?<module>Array|String|Bytes|List)\.length\s+(?<collection>[a-z_][\w']*))\s+do\b")]
    public static partial Regex ForToLength();

    public static LocalFix UpperBoundFix(string id, SourceFile source, int lineNumber, Match loop)
    {
        var line = source.Lines[lineNumber - 1];
        var length = loop.Groups["length"];
        var corrected = line[..(length.Index + length.Length)] + " - 1" + line[(length.Index + length.Length)..];

        return LocalFix.ReplaceLine(id, $"Stop at {length.Value} - 1",
            $"OCaml's for counts up to and including its last number, so for {loop.Groups["variable"].Value} = 0 to {length.Value} takes one step " +
            $"more than {loop.Groups["collection"].Value} has positions - they run from 0 to one less than its length.",
            source.Path, lineNumber, corrected);
    }
}
