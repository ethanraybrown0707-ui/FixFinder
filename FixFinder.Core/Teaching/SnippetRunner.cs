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
    /// or the start of its message - on the same line, or on none when the lesson says the language names none, refused
    /// before running or not, just as the lesson has it.
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
            if (Line != expected.OnLine) return false;

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

    /// <summary>How long code a lesson says never finishes is given before it is stopped - far longer than its fixed version takes.</summary>
    public static readonly TimeSpan NeverFinishingTimeLimit = TimeSpan.FromSeconds(10);

    /// <summary>The time a lesson's program is given: less when the lesson says it never finishes, so nobody waits a minute to see it.</summary>
    public static TimeSpan TimeLimitFor(Behaviour lessonSays) => lessonSays.NeverFinishes ? NeverFinishingTimeLimit : UsualTimeLimit;

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
                // The time given is the program's, for running it. Building it is given at least the usual time: a compiler on
                // a busy computer took longer than the ten seconds a program that never finishes is given, and the build stopped
                // for time was said to be a program that ran to the end and printed nothing.
                var buildTime = compile.Timeout < UsualTimeLimit ? UsualTimeLimit : compile.Timeout;
                var build = await runner.RunAsync(compile.WithTimeout(buildTime), cancellationToken);
                output.AddRange(build.Lines.Select(line => line.Text));

                if (build.Outcome == RunOutcome.LaunchFailed)
                    return new Observation { Ran = false, WhyNotRun = build.LaunchError ?? "The compiler could not be started.", Output = output };

                if (build.Outcome == RunOutcome.TimedOut)
                    return new Observation { Ran = false, WhyNotRun = $"Building it took longer than {buildTime.TotalSeconds:0} seconds, so it was stopped before it ran.", Output = output };

                if (build.Outcome != RunOutcome.ExitedClean)
                {
                    var refusal = build.Error ?? language.Parsers().Parse(build.Lines);
                    return new Observation
                    {
                        Ran = true, Error = refusal, Line = LineIn(refusal, file), BeforeRunning = true, Output = output,
                    };
                }
            }

            // go run and dotnet run build the program before running it, inside the time the run is given: built first, on its
            // own, the run finds it built and is timed on the program alone.
            if (BuildFirst(run, folder) is { } prebuild)
            {
                var built = await runner.RunAsync(prebuild, cancellationToken);
                if (built.Outcome == RunOutcome.TimedOut)
                {
                    output.AddRange(built.Lines.Select(line => line.Text));
                    return new Observation { Ran = false, WhyNotRun = $"Building it took longer than {prebuild.Timeout.TotalSeconds:0} seconds, so it was stopped before it ran.", Output = output };
                }
            }

            var result = await runner.RunAsync(run, cancellationToken);
            output.AddRange(result.Lines.Select(line => line.Text));

            if (result.Outcome == RunOutcome.LaunchFailed)
                return new Observation { Ran = false, WhyNotRun = result.LaunchError ?? "The program could not be started.", Output = output };

            if (result.Outcome == RunOutcome.TimedOut)
                return new Observation { Ran = true, NeverFinished = true, Output = output };

            // A warning PHP or Perl printed and carried on past is not what the code stopped with: it ran to the end.
            if (result.Error is { } error && result.Outcome != RunOutcome.ExitedClean && !(result.ExitCode == 0 && CarriedOnPast.WhatItWas(error) is not null))
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
    /// The build go run or dotnet run would do first, as a step of its own, given the time FixFinder gives a first Go build -
    /// six minutes, paid only by a build that needs it - or null for any other run. Inside the run, a cold build used up a lesson's minute on a busy GitHub runner: Go's and C#'s lessons were
    /// stopped at 60 seconds, still running, where every other language's build had a time of its own. Built here, go run
    /// took 0.8 seconds after a 6.2-second go build with an empty cache, and dotnet run 1.7 after a 5.4-second dotnet build.
    /// What the build says is not kept: when it fails, the run fails the same way, and says so as it always did.
    /// </summary>
    internal static TargetSpec? BuildFirst(TargetSpec run, string folder)
    {
        if (run.LaunchViaDotnet || !run.Arguments.StartsWith("run \"", StringComparison.Ordinal)) return null;

        var file = run.Arguments["run ".Length..];
        var arguments = Path.GetFileNameWithoutExtension(run.ExecutablePath).ToLowerInvariant() switch
        {
            "go" => $"build -o \"{Path.Combine(folder, "built.exe")}\" {file}",
            "dotnet" => $"build {file}",
            _ => null,
        };

        return arguments is null ? null : new TargetSpec
        {
            ExecutablePath = run.ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = run.WorkingDirectory,
            ExtraEnvironment = run.ExtraEnvironment,
            Timeout = run.Timeout < TargetFactory.FirstRunTimeout ? TargetFactory.FirstRunTimeout : run.Timeout,
        };
    }

    /// <summary>
    /// Whether an error is the language refusing the code before any of it ran: a compiler's error, the SyntaxError
    /// Python and JavaScript give for code they cannot read at all, or PHP's ParseError, Lua's syntax error, Perl's
    /// compilation error or Elixir's syntax and compile errors - PHP, Lua, Perl and Elixir read a whole file before running
    /// any of it.
    /// </summary>
    private static bool IsRefusal(ParsedError error) =>
        error.ExceptionType is "compile error" or "link error" ||
        error.ShortExceptionType is "SyntaxError" or "IndentationError" or "TabError" or "ParseError" ||
        error.ExceptionType is Parsing.Parsers.LuaParser.SyntaxError or Parsing.Parsers.PerlParser.CompilationError or Parsing.Parsers.PowerShellParser.ParserError ||
        error.LanguageId == "elixir" && Parsing.Parsers.ElixirParser.SyntaxErrors.Contains(error.ExceptionType ?? "");

    /// <summary>The line of the saved code an error is on: the frame in that file, the one the error's own code is in.</summary>
    private static int? LineIn(ParsedError? error, string file)
    {
        if (error is null) return null;

        var inTheFile = error.Frames.FirstOrDefault(frame => frame.File is { } named &&
            string.Equals(Path.GetFileName(named), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase) && frame.Line is > 0);

        return inTheFile?.Line ?? LocalFixContext.OwnFrame(error)?.Line ?? error.Frames.FirstOrDefault(frame => frame.Line is > 0)?.Line;
    }
}
