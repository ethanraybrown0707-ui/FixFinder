using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Reporting;

namespace FixFinder.Core.Teaching;

/// <summary>
/// How to put one finding right, as numbered steps: where to look, what kind of problem it is, how to make the change,
/// and how to see that it worked. Every step is built from the finding itself - its file, its line and the code on it,
/// what kind of finding it is, whether FixFinder has a change for it - so a step never says more than the finding does.
/// The corrected code is not given away here: the steps send the reader to it only once they have tried.
/// </summary>
public static class StepByStep
{
    /// <summary>How long a line of code is shown in a step before it is cut, so a step stays one readable sentence.</summary>
    private const int LongestLineShown = 100;

    public static IReadOnlyList<string> For(Finding finding)
    {
        var language = LanguageName(finding);

        var steps = new List<string> { WhereToLook(finding, language), WhatHappened(finding, language) };

        if (finding.CausedBy is not null)
            steps.Add("This one may follow from another problem in the report: put that one right first - this one may go with it.");

        steps.Add(finding.Fix is not null || finding.ExampleIsFromYourCode
            ? "Work out the change from How to fix it, and make it yourself. When you have tried, Show the corrected code compares your " +
              "change with FixFinder's."
            : "Make the change How to fix it describes.");

        steps.Add(HowToSeeItWorked(finding, language));

        return steps;
    }

    /// <summary>The language's name, or "the language" for a file FixFinder does not check as one.</summary>
    private static string LanguageName(Finding finding) =>
        CodeLanguage.Of(finding.InNotebook?.Notebook ?? finding.File) is { IsAny: false } language ? language.Name : "the language";

    private static string WhereToLook(Finding finding, string language)
    {
        var file = FindingText.FileName(finding);

        if (finding.InNotebook is { } place) return $"Open {file} and go to cell {place.Cell}, line {place.Line}{CodeOn(finding)}.";
        if (finding.Line is { } line) return $"Open {file} and go to line {line}{CodeOn(finding)}.";

        return $"Open {file}. No single line was named for this, so start from what is wrong above.";
    }

    /// <summary>": it reads `total = total / count`" - the code on the finding's line, when the file can be read.</summary>
    private static string CodeOn(Finding finding)
    {
        if (finding.Line is not { } number || SourceFile.Read(finding.File) is not { } source || source.Line(number) is not { } line) return "";

        var code = line.Trim();
        if (code.Length == 0 || code.Contains('`')) return "";
        if (code.Length > LongestLineShown) code = code[..LongestLineShown].TrimEnd() + " ...";

        return $": it reads `{code}`";
    }

    private static string WhatHappened(Finding finding, string language) => (finding.RuleId, finding.Kind) switch
    {
        ("timed-out", _) =>
            "The program was still running when its time ran out, so FixFinder stopped it: nothing went wrong that it printed, but it did not finish.",
        ("wrong-output", _) =>
            "The program ran to the end, but what it printed is not what you said it should print.",
        ("stopped-before-expected-output", _) =>
            "The program stopped before it had printed everything you said it should print.",
        (_, FindingKind.Syntax) when finding.Severity == Severity.Error =>
            $"{Capitalised(language)} could not make sense of this, so the program did not run - none of it, or, for a file another one " +
            "uses, none of this file.",
        (_, FindingKind.Syntax) =>
            $"This is a warning, not an error: the program still runs, and the warning says what about this line may be wrong.",
        (_, FindingKind.Runtime) when finding.Severity != Severity.Error =>
            "Something went wrong while the program ran, but it was not stopped by it - read what is wrong for what it means here.",
        (_, FindingKind.Runtime) when finding.Line is not null =>
            "The program started and ran until this line, and stopped here - everything after it was never reached.",
        (_, FindingKind.Runtime) =>
            "The program started, and stopped part-way while it was running.",
        (_, FindingKind.Logic) when finding.Confidence == Confidence.Possible =>
            "The program runs, but this line may not do what it looks like it does - read what is wrong, and decide whether it is what you meant.",
        (_, FindingKind.Logic) =>
            "The program runs, but this line does not do what it looks like it does, so the answer can come out wrong without any error.",
        (_, FindingKind.Performance) =>
            "The program is right as it is: this is about doing less work as the data it is given grows.",
        _ =>
            "The program works as it is: this is about making it clearer, or safer to change later.",
    };

    private static string HowToSeeItWorked(Finding finding, string language) => (finding.RuleId, finding.Kind) switch
    {
        ("timed-out", _) =>
            "Check it again: once its loops can end - or the input it waits for is given - the program finishes inside its time.",
        ("wrong-output" or "stopped-before-expected-output", _) =>
            "Check it again, with the same expected output: this finding goes when the program prints exactly that.",
        (_, FindingKind.Syntax) when finding.Severity == Severity.Error =>
            $"Check it again. Once this is right, {language} reads on past it, and the next mistake, if there is one, is the one reported.",
        (_, FindingKind.Runtime) when finding.Severity == Severity.Error =>
            "Check it again: the program runs again, and when it gets past this point, this crash is fixed.",
        _ =>
            "Check it again: this finding goes once the code no longer does what it describes.",
    };

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
