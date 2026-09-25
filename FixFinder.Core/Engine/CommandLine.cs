using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;

namespace FixFinder.Core.Engine;

/// <summary>
/// FixFinder from a terminal or an editor's task: check one program and print what was found, one line each.
/// </summary>
/// <remarks>
/// The same check the window runs, so it compiles and runs the program just as pressing a language does there - which
/// is why it only ever runs the program named on its own command line. What it prints on standard output is findings and
/// nothing else, because an editor reads that stream line by line for problems; anything said about the run goes to
/// standard error, where it is shown without being mistaken for one.
/// </remarks>
public static class CommandLine
{
    /// <summary>Nothing wrong was found.</summary>
    public const int NothingWrong = 0;

    /// <summary>At least one error was found, so a build step running this can stop.</summary>
    public const int FoundErrors = 1;

    /// <summary>The program could not be checked at all - the file is missing, or nothing on this machine runs it.</summary>
    public const int CouldNotCheck = 2;

    public const string Usage = """
        fixfinder - check a program's syntax and logic, and print what is wrong with it

        usage: fixfinder <file> [options]

          --format msbuild|gcc|json   msbuild (the default) is read by Visual Studio, Rider and
                                      VS Code's $msCompile; gcc suits most other tools
          --level beginner|student|technical
                                      how much each finding explains (default: your setting)
          --language <name>           Python, Java, C#, C, C++, JavaScript or Go
                                      (default: worked out from the file)
          --expect <text>             what the program should print, to catch wrong answers

        The program is compiled and run, exactly as the FixFinder window does it.
        Exit code: 0 nothing wrong, 1 at least one error, 2 it could not be checked.
        """;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, TextWriter output, TextWriter errors, CancellationToken cancellationToken = default)
    {
        if (Read(args) is not { } asked)
        {
            await errors.WriteLineAsync(Usage);
            return CouldNotCheck;
        }

        if (asked.Problem is { } problem)
        {
            await errors.WriteLineAsync($"fixfinder: {problem}");
            await errors.WriteLineAsync("Run fixfinder --help for how to use it.");
            return CouldNotCheck;
        }

        var preferences = Preferences.Load();

        // The course's language versions apply to this check, and the shared setting is put back afterwards: as a
        // command it hardly matters, but anything that calls this in-process would otherwise be left compiling under
        // somebody's saved Java 8 for good.
        var before = LanguageStandards.Current;
        LanguageStandards.Current = preferences.Standards;

        try
        {
            return await CheckAsync(asked, preferences, output, errors, cancellationToken);
        }
        finally
        {
            LanguageStandards.Current = before;
        }
    }

    private static async Task<int> CheckAsync(
        Asked asked, Preferences preferences, TextWriter output, TextWriter errors, CancellationToken cancellationToken)
    {
        var launch = TargetFactory.FromFile(asked.File!);
        if (!launch.Ok)
        {
            await errors.WriteLineAsync($"fixfinder: {launch.Problem}");
            return CouldNotCheck;
        }

        var expected = asked.Expect is { } text ? ExpectedBehaviour.From([new ExpectedRun(null, text)], "") : null;

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = asked.Language ?? CodeLanguage.Of(asked.File!) ?? CodeLanguage.Any,
            Expected = expected is { IsEmpty: false } ? expected : null,
        };

        var report = await checker.CheckAsync(launch, cancellationToken);

        foreach (var note in report.Notes) await errors.WriteLineAsync($"note: {note}");
        await errors.WriteLineAsync($"syntax: {report.SyntaxSummary}");
        await errors.WriteLineAsync($"logic:  {report.LogicSummary}");

        var level = asked.Level ?? preferences.Explanations;

        if (asked.Format == DiagnosticFormat.Json)
        {
            await output.WriteLineAsync(DiagnosticLines.Json(report.Findings, level));
        }
        else
        {
            foreach (var finding in report.Findings) await output.WriteLineAsync(DiagnosticLines.Line(finding, asked.Format, level));
        }

        return report.Findings.Any(f => f.Severity == Severity.Error) ? FoundErrors : NothingWrong;
    }

    /// <summary>What was asked for on the command line.</summary>
    private sealed record Asked(
        string? File, DiagnosticFormat Format, ExplanationLevel? Level, CodeLanguage? Language, string? Expect, string? Problem);

    /// <summary>The command line read, or null when it asked for help rather than a check.</summary>
    private static Asked? Read(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Any(a => a is "--help" or "-h" or "/?")) return null;

        string? file = null, expect = null;
        var format = DiagnosticFormat.MsBuild;
        ExplanationLevel? level = null;
        CodeLanguage? language = null;

        Asked Refused(string why) => new(null, format, level, language, expect, why);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (file is not null) return Refused($"only one program can be checked at a time, but both {file} and {arg} were given");
                file = arg;
                continue;
            }

            if (i + 1 >= args.Count) return Refused($"{arg} needs a value after it");
            var value = args[++i];

            switch (arg)
            {
                case "--format":
                    if (!Enum.TryParse(value, ignoreCase: true, out format) || !Enum.IsDefined(format))
                        return Refused($"there is no format called '{value}' - use msbuild, gcc or json");
                    break;

                case "--level":
                    if (!Enum.TryParse<ExplanationLevel>(value, ignoreCase: true, out var chosen) || !Enum.IsDefined(chosen))
                        return Refused($"there is no level called '{value}' - use beginner, student or technical");
                    level = chosen;
                    break;

                case "--language":
                    language = Named(value);
                    if (language is null) return Refused($"FixFinder does not check a language called '{value}'");
                    break;

                case "--expect":
                    expect = value;
                    break;

                default:
                    return Refused($"there is no option called {arg}");
            }
        }

        return file is null ? Refused("no program was named") : new Asked(Path.GetFullPath(file), format, level, language, expect, null);
    }

    /// <summary>A language by the name somebody would type for it.</summary>
    private static CodeLanguage? Named(string name) => name.ToLowerInvariant() switch
    {
        "c#" or "csharp" or "cs" => CodeLanguage.CSharp,
        "c++" or "cpp" or "cxx" => CodeLanguage.Cpp,
        "js" or "javascript" or "node" => CodeLanguage.JavaScript,
        "golang" => CodeLanguage.Go,
        "any" or "auto" => CodeLanguage.Any,
        var other => CodeLanguage.All.FirstOrDefault(l => string.Equals(l.Name, other, StringComparison.OrdinalIgnoreCase)),
    };
}
