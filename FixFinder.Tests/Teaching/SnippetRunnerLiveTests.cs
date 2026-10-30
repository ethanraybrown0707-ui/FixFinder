using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A program that never finishes, given a run far shorter than its compiler takes: the time is the program's, not the
/// compiler's, so it is built in full, and then stopped as never finishing - not called a program that ran to the end and
/// printed nothing, as one was when its build was stopped at the run's ten seconds on a busy computer.
/// </summary>
public class SnippetRunnerLiveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("C", "#include <stdio.h>\n\nint main(void) {\n    volatile long long passes = 0;\n    for (;;) {\n        passes++;\n    }\n    return 0;\n}\n")]
    [InlineData("Java", "public class Main {\n    public static void main(String[] args) {\n        long passes = 0;\n        while (true) {\n            passes++;\n        }\n    }\n}\n")]
    public async Task AProgramGivenLessTimeThanItsCompilerTakesIsBuiltAndThenStoppedAsNeverFinishing(string languageName, string code)
    {
        var language = CodeLanguage.All.Single(each => each.Name == languageName);

        var observed = await SnippetRunner.RunAsync(language, code, TimeSpan.FromMilliseconds(100));
        if (!observed.Ran || ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun))
        {
            output.WriteLine($"Not run on this computer: {observed.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        output.WriteLine(observed.Said(languageName));
        Assert.True(observed.Shows(Behaviour.Running()), observed.Said(languageName));
    }

    /// <summary>
    /// go run and dotnet run build the program before running it, inside the run's time: on a GitHub runner a cold build
    /// used up a lesson's minute, and Go's and C#'s lessons were stopped as still running. Built first, a program that
    /// finishes is timed on itself - here given 5 seconds, less than a cold build took on this computer, 6.2 for Go and
    /// 5.4 for C#.
    /// </summary>
    [Theory]
    [InlineData("Go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tages := map[string]int{\"Ada\": 36}\n\tfmt.Println(ages[\"Bob\"])\n}\n", "0")]
    [InlineData("C#", "var ages = new Dictionary<string, int> { [\"Ada\"] = 36 };\nConsole.WriteLine(ages.GetValueOrDefault(\"Bob\", 0));\n", "0")]
    public async Task AProgramGoRunOrDotnetRunBuildsIsTimedOnItselfNotItsBuild(string languageName, string code, string printed)
    {
        var language = CodeLanguage.All.Single(each => each.Name == languageName);

        var observed = await SnippetRunner.RunAsync(language, code, TimeSpan.FromSeconds(5));
        if (!observed.Ran || ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun))
        {
            output.WriteLine($"Not run on this computer: {observed.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        output.WriteLine(observed.Said(languageName));
        Assert.True(observed.Shows(Behaviour.Printing(printed)), observed.Said(languageName));
    }

    /// <summary>The build each run would do first, as a step of its own - and none for a run that builds nothing, or a project.</summary>
    [Fact]
    public void TheBuildGoRunAndDotnetRunWouldDoIsMadeAStepOfItsOwn()
    {
        static TargetSpec Run(string program, string arguments) => new()
        {
            ExecutablePath = program, Arguments = arguments, WorkingDirectory = @"C:\work", Timeout = TimeSpan.FromSeconds(60),
            ExtraEnvironment = new Dictionary<string, string> { ["GOPROXY"] = "off" },
        };

        var go = SnippetRunner.BuildFirst(Run(@"C:\Go\bin\go.exe", "run \"C:\\work\\main.go\""), @"C:\temp\lesson")!;
        Assert.Equal(("build -o \"C:\\temp\\lesson\\built.exe\" \"C:\\work\\main.go\"", @"C:\work", "off"), (go.Arguments, go.WorkingDirectory, go.ExtraEnvironment["GOPROXY"]));
        Assert.Equal(TargetFactory.FirstRunTimeout, go.Timeout);

        Assert.Equal("build \"C:\\work\\main.cs\"", SnippetRunner.BuildFirst(Run(@"C:\dotnet\dotnet.exe", "run \"C:\\work\\main.cs\""), @"C:\temp")!.Arguments);

        Assert.Null(SnippetRunner.BuildFirst(Run(@"C:\Python\python.exe", "\"C:\\work\\main.py\""), @"C:\temp"));
        Assert.Null(SnippetRunner.BuildFirst(Run(@"C:\dotnet\dotnet.exe", "run --project \"C:\\work\\app.csproj\""), @"C:\temp"));
    }
}
