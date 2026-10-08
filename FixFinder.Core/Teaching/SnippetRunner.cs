using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Teaching;

/// <summary>What running a piece of code did, as FixFinder saw it - the same way it sees any program it checks.</summary>
public sealed record Observation
{
    /// <summary>Whether it was run at all; false when this computer has nothing that runs its language.</summary>
    public required bool Ran { get; init; }

    /// <summary>Why it was not run, when it was not.</summary>
    public string? WhyNotRun { get; init; }

    /// <summary>Everything it printed to standard output, when it ran to the end.</summary>
    public string? Printed { get; init; }

    /// <summary>The error it stopped with, as the language reported it, when it stopped with one.</summary>
    public ParsedError? Error { get; init; }

    /// <summary>The line of the code the error is on.</summary>
    public int? Line { get; init; }

    /// <summary>Whether the language refused the code before any of it ran.</summary>
    public bool BeforeRunning { get; init; }

    /// <summary>Whether it was still running when its time ran out.</summary>
    public bool NeverFinished { get; init; }

    /// <summary>What Windows stopped it with, when it crashed with no error of the language's own: "integer divide by zero (0xC0000094)".</summary>
    public string? Crash { get; init; }

    /// <summary>Everything the compiler and the program printed, in order, as it would be seen in a terminal.</summary>
    public IReadOnlyList<string> Output { get; init; } = [];

    /// <summary>
    /// Whether this is what a lesson says the code does: the same printing, or the same error - named by its type, its code
    /// or the start of its message - on the same line, refused before running or not, just as the lesson has it.
    /// </summary>
    public bool Shows(Behaviour expected)
    {
        if (!Ran) return false;
        if (expected.NeverFinishes) return NeverFinished;
        if (NeverFinished) return false;

        if (expected.CrashesWith is { } meaning) return Crash is { } crash && crash.StartsWith(meaning, StringComparison.Ordinal);
        if (Crash is not null) return false;

        if (expected.StopsWith is { } stopsWith)
        {
            if (Error is not { } error || BeforeRunning != expected.BeforeRunning) return false;
            if (expected.OnLine is { } line && Line != line) return false;

            return string.Equals(error.ShortExceptionType, stopsWith, StringComparison.Ordinal) ||
                   string.Equals(error.ErrorCode, stopsWith, StringComparison.OrdinalIgnoreCase) ||
                   (error.Message ?? "").StartsWith(stopsWith, StringComparison.Ordinal);
        }

        return Error is null && Printed is { } printed && printed == (expected.Prints ?? "");
    }

    /// <summary>What it did, said as a lesson says it: what it printed, or what it stopped with and where.</summary>
    public string Said(string language) => this switch
    {
        { Ran: false } => WhyNotRun ?? "It could not be run on this computer.",
        { NeverFinished: true } => "It was still running when its time ran out, so it was stopped.",
        { Crash: { } crash } => $"It crashed: Windows stopped it with {crash}.",
        { Error: { } error } => (BeforeRunning ? $"{language} refused it before it ran: " : "It stopped with ") + NameOf(error) +
                                (error.Message is { Length: > 0 } message ? $" - {message}" : "") +
                                (Line is { } at ? $", on line {at}" : ""),
        { Printed: { Length: > 0 } printed } => $"It ran to the end and printed:\n{printed}",
        _ => "It ran to the end and printed nothing.",
    };

    /// <summary>What an error is called: its exception type, or for a compiler's error, its code when it has one.</summary>
    private static string NameOf(ParsedError error) =>
        error.ShortExceptionType is { } type && type is not ("compile error" or "link error") ? type : error.ErrorCode ?? "an error";
}

/// <summary>
/// Runs a piece of code the way FixFinder runs a program - saved as its language needs, built if it has to be, then run -
/// and says what it did. Lessons are checked with it, and FixFinder Learn runs examples and learners' own fixes with it.
/// </summary>
public static class SnippetRunner
{
    /// <summary>How long a piece of code is given when nothing else is said: enough for any example a lesson has.</summary>
    public static readonly TimeSpan UsualTimeLimit = TimeSpan.FromSeconds(60);

    public static async Task<Observation> RunAsync(CodeLanguage language, string code, TimeSpan? timeLimit = null, CancellationToken cancellationToken = default)
    {
        var folder = PastedCode.NewFolder();

        try
        {
            var file = PastedCode.Save(code, language, folder);
            var launch = TargetFactory.FromFile(file, timeLimit);

            if (!launch.Ok || launch.Spec is not { } run)
                return new Observation { Ran = false, WhyNotRun = launch.Problem ?? $"Nothing on this computer runs {language.Name}." };

            var runner = new TargetRunner(language.Parsers());
            var output = new List<string>();

            if (launch.Compile is { } compile)
            {
                var build = await runner.RunAsync(compile, cancellationToken);
                output.AddRange(build.Lines.Select(line => line.Text));

                if (build.Outcome == RunOutcome.LaunchFailed)
                    return new Observation { Ran = false, WhyNotRun = build.LaunchError ?? "The compiler could not be started.", Output = output };

                if (build.Outcome != RunOutcome.ExitedClean)
                {
                    var refusal = build.Error ?? language.Parsers().Parse(build.Lines);
                    return new Observation
                    {
                        Ran = true, Error = refusal, Line = LineIn(refusal, file), BeforeRunning = true, Output = output,
                    };
                }
            }

            var result = await runner.RunAsync(run, cancellationToken);
            output.AddRange(result.Lines.Select(line => line.Text));

            if (result.Outcome == RunOutcome.LaunchFailed)
                return new Observation { Ran = false, WhyNotRun = result.LaunchError ?? "The program could not be started.", Output = output };

            if (result.Outcome == RunOutcome.TimedOut)
                return new Observation { Ran = true, NeverFinished = true, Output = output };

            if (result.Error is { } error && result.Outcome != RunOutcome.ExitedClean)
            {
                return new Observation
                {
                    Ran = true, Error = error, Line = LineIn(error, file), BeforeRunning = IsRefusal(error), Output = output,
                };
            }

            if (result.Outcome != RunOutcome.ExitedClean && result.ExitCode is { } exitCode && RunClassifier.MeaningOf(exitCode) is { } meaning)
                return new Observation { Ran = true, Crash = meaning, Output = output };

            var printed = string.Join("\n", result.Lines.Where(line => line.Stream == StreamKind.StdOut).Select(line => line.Text.TrimEnd())).TrimEnd('\n');
            return new Observation { Ran = true, Printed = printed, Output = output };
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Whether an error is the language refusing the code before any of it ran: a compiler's error, or the SyntaxError
    /// Python and JavaScript give for code they cannot read at all.
    /// </summary>
    private static bool IsRefusal(ParsedError error) =>
        error.ExceptionType is "compile error" or "link error" ||
        error.ShortExceptionType is "SyntaxError" or "IndentationError" or "TabError";

    /// <summary>The line of the saved code an error is on: the frame in that file, the one the error's own code is in.</summary>
    private static int? LineIn(ParsedError? error, string file)
    {
        if (error is null) return null;

        var inTheFile = error.Frames.FirstOrDefault(frame => frame.File is { } named &&
            string.Equals(Path.GetFileName(named), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase) && frame.Line is > 0);

        return inTheFile?.Line ?? LocalFixContext.OwnFrame(error)?.Line ?? error.Frames.FirstOrDefault(frame => frame.Line is > 0)?.Line;
    }
}
