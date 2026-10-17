using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Fortran rules share: whether an error is gfortran's, and the line it is on.</summary>
internal static class FortranCode
{
    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "fortran" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!FortranBuild.Extensions.Contains(Path.GetExtension(source.Path)) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static bool IsCompileError(LocalFixContext context) => context.Error.ExceptionType == "compile error";

    public static bool Says(LocalFixContext context, string start) => (context.Error.Message ?? "").StartsWith(start, StringComparison.Ordinal);
}

/// <summary>
/// "Syntax error in IF-expression at (1)" for <c>if (mark = 40) then</c>: = gives a variable a value; == compares, and an if
/// needs a comparison. The one = in the condition becomes ==.
/// </summary>
public sealed partial class FortranCompareWithDoubleEquals : ILocalFixRule
{
    public string Id => "fortran-compare-with-double-equals";

    [GeneratedRegex(@"\bif\s*\(\s*[\w%()]+?\s*(?<assign>=)(?![=>])\s*[^)]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex StoredInCondition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FortranCode.IsCompileError(context) || !FortranCode.Says(context, "Syntax error in IF-expression") || FortranCode.Locate(context) is not { } at) return null;
        if (StoredInCondition().Match(CodeText.Mask(at.Line, Syntax.Fortran)) is not { Success: true } condition) return null;

        var assign = condition.Groups["assign"];

        return LocalFix.ReplaceLine(Id, "Compare with ==",
            "== compares two values; = gives a variable a value, which Fortran will not do inside an if's condition.",
            at.Source.Path, at.Number, at.Line[..assign.Index] + "==" + at.Line[(assign.Index + assign.Length)..]);
    }
}

/// <summary>
/// <c>if (mark != 40) then</c>: Fortran writes "is not equal to" as /=, and ! starts a comment, so gfortran read
/// <c>if (mark</c> and stopped - "Missing ')' in statement". The != becomes /=.
/// </summary>
public sealed class FortranNotEqual : ILocalFixRule
{
    public string Id => "fortran-not-equal";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FortranCode.IsCompileError(context) || !FortranCode.Says(context, "Missing ')' in statement") || FortranCode.Locate(context) is not { } at) return null;

        var (code, comment) = CodeText.SplitComment(at.Line, Syntax.Fortran);
        if (!comment.TrimStart().StartsWith("!=", StringComparison.Ordinal)) return null;

        var place = at.Line.IndexOf("!=", code.Length, StringComparison.Ordinal);

        return LocalFix.ReplaceLine(Id, "Write \"is not equal to\" as /=",
            "Fortran writes \"is not equal to\" as /= - and ! starts a comment, so gfortran read the line only as far as the !.",
            at.Source.Path, at.Number, at.Line[..place] + "/=" + at.Line[(place + 2)..]);
    }
}

/// <summary>
/// "Syntax error in IF-clause after (1)" for an <c>if (mark &gt; 40)</c> line on its own, with an end if below it: a block
/// if's condition ends with then.
/// </summary>
public sealed partial class FortranMissingThen : ILocalFixRule
{
    public string Id => "fortran-missing-then";

    /// <summary>How far below the if its end if is looked for.</summary>
    private const int LinesBelowLooked = 30;

    [GeneratedRegex(@"^\s*(?:else\s*)?if\s*\(.*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BareIf();

    [GeneratedRegex(@"^\s*end\s*if\b", RegexOptions.IgnoreCase)]
    private static partial Regex EndIf();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FortranCode.IsCompileError(context) || !FortranCode.Says(context, "Syntax error in IF-clause") || FortranCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Fortran);
        if (!BareIf().IsMatch(masked[at.Number - 1])) return null;
        if (!Enumerable.Range(at.Number, Math.Min(LinesBelowLooked, masked.Count - at.Number)).Any(index => EndIf().IsMatch(masked[index]))) return null;

        var (code, comment) = CodeText.SplitComment(at.Line, Syntax.Fortran);

        return LocalFix.ReplaceLine(Id, "End the if's condition with then",
            "An if that has lines of its own, closed by end if, ends its condition with then. Without it, gfortran read the line as an " +
            "if with one statement after it - and found none.",
            at.Source.Path, at.Number, code.TrimEnd() + " then" + comment);
    }
}

/// <summary>
/// "Expecting END DO statement at (1)" - or END IF - at a line that ends something else: the do loop or the if above was
/// never closed. Its end goes before that line, indented as its own first line is.
/// </summary>
public sealed partial class FortranCloseBlock : ILocalFixRule
{
    public string Id => "fortran-close-block";

    [GeneratedRegex(@"^Expecting END (?<block>DO|IF) statement")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*(?:\w+\s*:\s*)?do\b", RegexOptions.IgnoreCase)]
    private static partial Regex DoOpening();

    [GeneratedRegex(@"^\s*end\s*do\b", RegexOptions.IgnoreCase)]
    private static partial Regex DoClosing();

    [GeneratedRegex(@"^\s*(?:\w+\s*:\s*)?if\s*\(.*\)\s*then\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IfOpening();

    [GeneratedRegex(@"^\s*end\s*if\b", RegexOptions.IgnoreCase)]
    private static partial Regex IfClosing();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FortranCode.IsCompileError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said || FortranCode.Locate(context) is not { } at) return null;

        var block = said.Groups["block"].Value;
        var (opening, closing) = block == "DO" ? (DoOpening(), DoClosing()) : (IfOpening(), IfClosing());
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Fortran);
        var depth = 0;

        for (var index = at.Number - 2; index >= 0; index--)
        {
            if (closing.IsMatch(masked[index])) depth++;
            else if (opening.IsMatch(masked[index]) && depth-- == 0)
            {
                var word = block == "DO" ? "end do" : "end if";
                var opener = block == "DO" ? "do loop" : "if";

                return LocalFix.Insert(Id, $"Close the {opener} with {word}",
                    $"The {opener} on line {index + 1} has no {word}, so gfortran reached line {at.Number} still inside it. Its {word} goes " +
                    "where its lines finish.",
                    at.Source.Path, at.Number, [CodeText.Indentation(at.Source.Lines[index]) + word]);
            }
        }

        return null;
    }
}

/// <summary>
/// "Dummy argument 'n' with INTENT(IN) in variable definition context": the subroutine changes an argument it promised,
/// with intent(in), only to read. When changing it is the subroutine's job, its promise becomes intent(inout).
/// </summary>
public sealed partial class FortranIntentInOut : ILocalFixRule
{
    public string Id => "fortran-intent-inout";

    [GeneratedRegex(@"^Dummy argument '(?<name>\w+)' with INTENT\(IN\) in variable definition context")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FortranCode.IsCompileError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said || FortranCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        var declaration = new Regex($@"intent\s*\(\s*in\s*\)(?=[^!]*::[^!]*\b{Regex.Escape(name)}\b)", RegexOptions.IgnoreCase);

        for (var index = at.Number - 2; index >= 0; index--)
        {
            if (declaration.Match(CodeText.Mask(at.Source.Lines[index], Syntax.Fortran)) is not { Success: true } promise) continue;

            var line = at.Source.Lines[index];

            return LocalFix.ReplaceLine(Id, $"Let the subroutine change {name}: intent(inout)",
                $"intent(in) promises {name} is only read, and line {at.Number} changes it. intent(inout) says it is read and changed - and the " +
                "change reaches the variable the caller passed.",
                at.Source.Path, index + 1, line[..promise.Index] + "intent(inout)" + line[(promise.Index + promise.Length)..]);
        }

        return null;
    }
}

/// <summary>
/// "Named constant 'pass_mark' in variable definition context": a parameter can never be given another value. When the
/// program does give it one, it is a variable after all, so its declaration loses parameter.
/// </summary>
public sealed partial class FortranParameterChanged : ILocalFixRule
{
    public string Id => "fortran-parameter-changed";

    [GeneratedRegex(@"^Named constant '(?<name>\w+)' in variable definition context")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FortranCode.IsCompileError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said || FortranCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        var declaration = new Regex($@"\s*,\s*parameter\b(?=[^!]*::[^!]*\b{Regex.Escape(name)}\b)", RegexOptions.IgnoreCase);

        for (var index = at.Number - 2; index >= 0; index--)
        {
            if (declaration.Match(CodeText.Mask(at.Source.Lines[index], Syntax.Fortran)) is not { Success: true } parameter) continue;

            var line = at.Source.Lines[index];

            return LocalFix.ReplaceLine(Id, $"Make {name} a variable: take away parameter",
                $"parameter makes {name} a constant, set once and never changed, and line {at.Number} changes it. Without parameter it is a " +
                "variable, which can be given a new value.",
                at.Source.Path, index + 1, line[..parameter.Index] + line[(parameter.Index + parameter.Length)..]);
        }

        return null;
    }
}

/// <summary>
/// An array read outside its bounds by a do loop - gfortran's run-time check, "Index '0' of dimension 1 of array 'scores'
/// below lower bound of 1", or its warning before running, "Array reference at (1) out of bounds (0 &lt; 1) in loop beginning
/// at (2)". The loop is made to count over the array's own positions: from 1, up to size(scores).
/// </summary>
public sealed partial class FortranLoopBounds : ILocalFixRule
{
    public string Id => "fortran-loop-bounds";

    /// <summary>How far above the line the loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 8;

    [GeneratedRegex(@"^Index '-?\d+' of dimension 1 of array '(?<array>\w+)' (?<side>below lower|above upper) bound of (?<bound>-?\d+)")]
    private static partial Regex RuntimeMessage();

    [GeneratedRegex(@"^Array reference at \(1\) out of bounds \((?<index>-?\d+) (?<side>[<>]) (?<bound>-?\d+)\) in loop beginning at \(2\)")]
    private static partial Regex WarningMessage();

    [GeneratedRegex(@"^(?<head>\s*do\s+(?<variable>\w+)\s*=\s*)(?<start>[^,]+?)\s*,\s*(?<end>[^,!]+?)\s*(?<tail>(?:,[^!]*)?(?:!.*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex LoopHeading();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "fortran" || FortranCode.Locate(context) is not { } at) return null;

        bool below;
        if (RuntimeMessage().Match(context.Error.Message ?? "") is { Success: true } stopped) below = stopped.Groups["side"].Value.StartsWith("below", StringComparison.Ordinal);
        else if (WarningMessage().Match(context.Error.Message ?? "") is { Success: true } warned) below = warned.Groups["side"].Value == "<";
        else return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.Fortran);

        for (var index = at.Number - 2; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (LoopHeading().Match(at.Source.Lines[index]) is not { Success: true } loop) continue;

            var variable = loop.Groups["variable"].Value;
            var read = new Regex($@"\b(?<array>\w+)\s*\(\s*{Regex.Escape(variable)}\s*\)", RegexOptions.IgnoreCase).Match(masked[at.Number - 1]);
            if (!read.Success) return null;

            var array = read.Groups["array"].Value;
            var start = below ? "1" : loop.Groups["start"].Value;
            var end = below ? loop.Groups["end"].Value : $"size({array})";

            return LocalFix.ReplaceLine(Id, below ? "Start the loop at 1" : $"End the loop at size({array})",
                $"{array}'s positions run from 1 to size({array}) - Fortran counts from 1 - so the loop counts over those and no further.",
                at.Source.Path, index + 1, $"{loop.Groups["head"].Value}{start}, {end}{loop.Groups["tail"].Value}");
        }

        return null;
    }
}

/// <summary>
/// "Symbol 'totl' at (1) has no IMPLICIT type; did you mean 'total'?": gfortran's own suggestion, made on the line and at the
/// column it names.
/// </summary>
public sealed partial class FortranDidYouMean : ILocalFixRule
{
    public string Id => "fortran-did-you-mean";

    [GeneratedRegex(@"^Symbol '(?<wrong>\w+)' at \(1\) has no IMPLICIT type; did you mean '(?<right>\w+)'\?")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FortranCode.IsCompileError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said || FortranCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["wrong"].Value;
        var right = said.Groups["right"].Value;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.Fortran, context.Frame?.Column - 1) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"{wrong} is declared nowhere, and with implicit none gfortran will not make it a variable of its own. gfortran itself " +
            $"compared it with the names that are declared, and suggested {right}.",
            at.Source.Path, at.Number, corrected);
    }
}
