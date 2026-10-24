using FixFinder.Core;
using FixFinder.Core.Engine;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// Whether a program stopped because it asked for input and none was typed - read from what real programs printed when they
/// were run with nothing typed, in every language that stops when a read finds the end of its input, and from every other
/// captured failure, none of which may be taken for it.
/// </summary>
/// <remarks>
/// A program that asks for input and gets none is told to type the answers into the input box, rather than being reported
/// as a crash to fix. Before these, only Python's EOFError and Java's Scanner were recognised; the rest were reported as
/// crashes. Go, PHP, Perl, JavaScript and PowerShell carry on past a read that finds nothing, so there is nothing of theirs
/// to recognise. Each output is read as the window's run reads it, with no folder named for the program's own files.
/// </remarks>
public class AskedForInputTests
{
    /// <summary>The captured programs that read input and were run with nothing typed: each language, and its case.</summary>
    public static TheoryData<string, string> RunWithNothingTyped => new()
    {
        { "Python", "input-with-nothing-typed" },
        { "Python", "number-typed-as-input-missing" },
        { "Java", "input-with-nothing-typed" },
        { "Java", "line-read-with-nothing-typed" },
        { "C#", "input-with-nothing-typed" },
        { "Ruby", "input-with-nothing-typed" },
        { "Lua", "input-with-nothing-typed" },
        { "Dart", "input-with-nothing-typed" },
        { "Elixir", "input-with-nothing-typed" },
        { "F#", "input-with-nothing-typed" },
        { "Scala", "input-with-nothing-typed" },
        { "Rust", "input-parsed-with-nothing-typed" },
        { "Fortran", "input-ended" },
        { "Fortran", "text-typed-as-a-number-missing" },
    };

    /// <summary>Every other captured program: each failed for a reason of its own, with no input to read.</summary>
    public static TheoryData<string, string> FailedForAnotherReason()
    {
        var askedForInput = RunWithNothingTyped.Select(row => ((string)row[0], (string)row[1])).ToHashSet();
        var others = new TheoryData<string, string>();
        foreach (var row in CapturedErrorTests.CapturedCases())
            if (!askedForInput.Contains(((string)row[0], (string)row[1])))
                others.Add((string)row[0], (string)row[1]);
        return others;
    }

    /// <summary>Whether the session takes the captured output for a program that asked for input, with nothing typed.</summary>
    private static bool TakenForAskingForInput(string language, string caseName)
    {
        var folder = CapturedErrorTests.FolderOf(language);
        var output = Fixtures.LoadCaptured(folder, caseName);
        var error = CodeLanguage.All.Single(each => each.Name == language).Parsers().Parse(output);
        Assert.NotNull(error);

        return FixFinderSession.AskedForInput(error, output, Path.Combine(Fixtures.Root, "Captured", folder, caseName), nothingTyped: true);
    }

    [Theory]
    [MemberData(nameof(RunWithNothingTyped))]
    public void AProgramThatAskedForInputWithNothingTypedIsToldToTypeIt(string language, string caseName) =>
        Assert.True(TakenForAskingForInput(language, caseName), $"{language} {caseName} was not taken for a program that asked for input");

    [Theory]
    [MemberData(nameof(FailedForAnotherReason))]
    public void AProgramThatFailedForAnotherReasonIsNotTakenForOneThatAskedForInput(string language, string caseName) =>
        Assert.False(TakenForAskingForInput(language, caseName), $"{language} {caseName} was taken for a program that asked for input");
}
