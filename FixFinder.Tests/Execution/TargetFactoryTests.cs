using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Covers turning a file somebody picked into something that can be launched.</summary>
public class TargetFactoryTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Make(string name, string content = "print('hi')\n")
    {
        var path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void APythonScriptIsRunThroughPython()
    {
        if (TargetFactory.FindOnPath("python") is null && TargetFactory.FindOnPath("py") is null) return;

        var script = Make("crash.py");
        var plan = TargetFactory.FromFile(script);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Contains("python", Path.GetFileName(plan.Spec!.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(script, plan.Spec.Arguments, StringComparison.Ordinal);
        Assert.False(plan.Spec.LaunchViaDotnet);
        Assert.Contains("python", plan.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AManagedDllGoesThroughTheDotnetHostExactlyOnce()
    {
        var dll = Make("App.dll", "not really an assembly");
        var plan = TargetFactory.FromFile(dll);

        Assert.True(plan.Ok, plan.Problem);
        Assert.True(plan.Spec!.LaunchViaDotnet);
        Assert.Equal(dll, plan.Spec.ExecutablePath);
        Assert.Equal("", plan.Spec.Arguments);

        var commandLine = plan.Spec.DisplayCommandLine;
        var firstMention = commandLine.IndexOf("App.dll", StringComparison.Ordinal);

        Assert.True(firstMention >= 0, commandLine);
        Assert.Equal(firstMention, commandLine.LastIndexOf("App.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExecutableIsRunDirectly()
    {
        var exe = Make("thing.exe", "MZ");
        var plan = TargetFactory.FromFile(exe);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Equal(exe, plan.Spec!.ExecutablePath);
        Assert.False(plan.Spec.LaunchViaDotnet);
        Assert.Equal("", plan.Spec.Arguments);
    }

    [Fact]
    public void AJarKeepsItsUnquotedArgumentPrefix()
    {
        if (TargetFactory.FindOnPath("java") is null) return;

        var jar = Make("app.jar", "PK");
        var plan = TargetFactory.FromFile(jar);

        Assert.True(plan.Ok, plan.Problem);
        Assert.StartsWith("-jar \"", plan.Spec!.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingInterpreterIsReportedByName()
    {
        var script = Make("thing.lua", "print('hi')");
        var plan = TargetFactory.FromFile(script);

        if (TargetFactory.FindOnPath("lua") is not null) return;

        Assert.False(plan.Ok);
        Assert.Contains("lua", plan.Problem!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not installed", plan.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnknownExtensionIsRunDirectlyAndSaysSo()
    {
        var odd = Make("program.weird", "whatever");
        var plan = TargetFactory.FromFile(odd);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Equal(odd, plan.Spec!.ExecutablePath);
        Assert.Contains("recognises", plan.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheWorkingDirectoryIsTheFilesOwnFolder()
    {
        var script = Make(Path.Combine("nested", "deep", "run.py"));
        var plan = TargetFactory.FromFile(script);

        if (!plan.Ok) return;

        Assert.Equal(Path.GetDirectoryName(script), plan.Spec!.WorkingDirectory);
    }

    [Fact]
    public void ATimeoutIsAlwaysSet()
    {
        var plan = TargetFactory.FromFile(Make("thing.exe", "MZ"));

        Assert.True(plan.Ok);
        Assert.Equal(TargetFactory.DefaultTimeout, plan.Spec!.Timeout);
    }

    /// <summary>
    /// Go compiles the standard library before it runs anything on a machine that has never built Go, which outlasts the
    /// ordinary wait - and go run does it inside the run. Killing it there does not make FixFinder slow, it makes it wrong:
    /// the program is stopped before it reaches the line that crashes, and a program that fails at once is reported as one
    /// that never finished. So go build runs first, writing nothing beside the program, given a first build's time, with
    /// the run's own environment - its proxy off - and the run is timed on the program alone.
    /// </summary>
    [Fact]
    public void GoIsBuiltFirstGivenTimeToCompileTheStandardLibrary()
    {
        var file = Make("crash.go", "package main");
        var plan = TargetFactory.FromFile(file);

        if (!plan.Ok) return;

        var build = plan.Spec!.BuildFirst!;
        Assert.Equal(($"build -o NUL \"{file}\"", TargetFactory.FirstRunTimeout, "off"), (build.Arguments, build.Timeout, build.ExtraEnvironment["GOPROXY"]));
        Assert.Equal(plan.Spec.ExecutablePath, build.ExecutablePath);
        Assert.Equal(TargetFactory.RunTimeLimit, plan.Spec.Timeout);
    }

    /// <summary>dotnet run builds the file, or the project, first: dotnet build does it on its own. Nothing else is built first.</summary>
    [Fact]
    public void CSharpIsBuiltFirstAndNothingElseIs()
    {
        var file = Make("Program.cs", "System.Console.WriteLine(1);\n");
        if (TargetFactory.FromFile(file) is { Ok: true } single)
            Assert.Equal($"build \"{file}\"", single.Spec!.BuildFirst?.Arguments);

        var project = Make(Path.Combine("app", "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>\n");
        Make(Path.Combine("app", "Program.cs"), "System.Console.WriteLine(1);\n");
        if (TargetFactory.FromFile(project) is { Ok: true } built)
            Assert.Equal($"build \"{project}\"", built.Spec!.BuildFirst?.Arguments);

        Assert.Null(TargetFactory.FromFile(Make("main.py")).Spec?.BuildFirst);
        Assert.Null(TargetFactory.FromFile(Make("main.js", "console.log(1);\n")).Spec?.BuildFirst);
    }

    /// <summary>What the program is given - its arguments, what is typed in, its time - does not lose the build done first.</summary>
    [Fact]
    public void TheBuildDoneFirstStaysWithTheRunWhateverItIsGiven()
    {
        if (TargetFactory.FromFile(Make("main.go", "package main\n\nfunc main() {}\n")) is not { Ok: true } plan) return;

        var given = plan.Spec!.WithArguments("marks.txt").WithInput("5\n").WithTimeout(TimeSpan.FromSeconds(3));
        Assert.Same(plan.Spec.BuildFirst, given.BuildFirst);
        Assert.DoesNotContain("marks.txt", given.BuildFirst!.Arguments, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingChosenIsSaidPlainly(string path) =>
        Assert.Contains("No file", TargetFactory.FromFile(path).Problem!, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void AFileThatIsNotThereIsSaidPlainly()
    {
        var plan = TargetFactory.FromFile(Path.Combine(_temp.Path, "absent.py"));

        Assert.False(plan.Ok);
        Assert.Contains("no file at", plan.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnInterpreterOnPathIsFound() =>
        Assert.NotNull(TargetFactory.FindOnPath("cmd"));

    [Fact]
    public void SomethingThatIsNotOnPathIsNotInvented() =>
        Assert.Null(TargetFactory.FindOnPath("definitely-not-a-real-program-xyzzy"));

    [Fact]
    public void TheFilePickerOffersEverythingThatCanBeRun()
    {
        var filter = TargetFactory.FileDialogFilter;

        Assert.Contains("*.py", filter, StringComparison.Ordinal);
        Assert.Contains("*.dll", filter, StringComparison.Ordinal);
        Assert.Contains("*.jar", filter, StringComparison.Ordinal);
        Assert.Contains("All files", filter, StringComparison.Ordinal);
    }
}
