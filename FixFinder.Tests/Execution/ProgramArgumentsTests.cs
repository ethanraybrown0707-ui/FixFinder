using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>Passing a program the arguments it reads, so a program that needs them gets past its first line.</summary>
public class ProgramArgumentsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static TargetSpec Spec(string program, string arguments, bool viaDotnet = false) => new()
    {
        ExecutablePath = program,
        Arguments = arguments,
        WorkingDirectory = @"C:\work",
        LaunchViaDotnet = viaDotnet,
    };

    [Fact]
    public void ArgumentsGoAfterEverythingFixFinderPutOnTheCommandLine() =>
        Assert.Equal("\"C:\\work\\app.py\" 21 \"two words\"",
            Spec(@"C:\Python\python.exe", "\"C:\\work\\app.py\"").WithArguments("21 \"two words\"").Arguments);

    [Fact]
    public void DotnetRunIsToldWhereItsOwnArgumentsEnd() =>
        Assert.Equal("run \"C:\\work\\app.cs\" -- --verbose 3",
            Spec(@"C:\dotnet\dotnet.exe", "run \"C:\\work\\app.cs\"").WithArguments("--verbose 3").Arguments);

    [Fact]
    public void AnAssemblyRunThroughTheHostTakesThemDirectly()
    {
        var spec = Spec(@"C:\work\app.dll", "", viaDotnet: true).WithArguments("a b");

        Assert.Equal("a b", spec.Arguments);
        Assert.Equal("dotnet \"C:\\work\\app.dll\" a b", spec.DisplayCommandLine);
    }

    [Fact]
    public void NoArgumentsChangesNothingAndTypedInputIsKept()
    {
        var spec = Spec(@"C:\Python\python.exe", "\"app.py\"").WithInput("yes");

        Assert.Same(spec, spec.WithArguments("   "));
        Assert.Equal("yes", spec.WithArguments("1").StandardInput);
    }

    private async Task<SessionOutcome> Run(string file, string? arguments)
    {
        var plan = TargetFactory.FromFile(file);
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var session = new FixFinderSession(http, new FixSourceRegistry());

        return await session.RunAsync(plan with { Spec = plan.Spec!.WithArguments(arguments) },
            new SearchBudget(Cache: CacheMode.CacheOnly), CancellationToken.None);
    }

    [Fact]
    public async Task APythonProgramGetsItsArguments()
    {
        if (TargetFactory.FindOnPath("python") is null && TargetFactory.FindOnPath("py") is null) return;

        var file = Path.Combine(_temp.Path, "double.py");
        await File.WriteAllTextAsync(file, "import sys\nprint(int(sys.argv[1]) * 2)\n");

        var without = await Run(file, null);
        var with = await Run(file, "21");

        Assert.Equal("IndexError", without.Error?.ExceptionType);
        if (ApplicationControl.Refused(with)) return;
        Assert.Equal(SessionResult.RanFine, with.Result);
        Assert.Contains(with.Run!.Lines, line => line.Text == "42");
    }

    [Fact]
    public async Task ACSharpProgramRunByTheSdkGetsItsArgumentsRatherThanTheSdk()
    {
        if (TargetFactory.FindOnPath("dotnet") is null) return;

        var file = Path.Combine(_temp.Path, "Double.cs");
        await File.WriteAllTextAsync(file, "Console.WriteLine(int.Parse(args[0]) * 2);\n");

        var with = await Run(file, "21 --help");

        if (with.Result == SessionResult.CouldNotRun) return;

        if (ApplicationControl.Refused(with)) return;
        Assert.Equal(SessionResult.RanFine, with.Result);
        Assert.Contains(with.Run!.Lines, line => line.Text == "42");
    }
}
