using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Covers turning a file somebody picked into something that can be launched.
/// </summary>
/// <remarks>
/// This is what makes "just add a file" true. If picking <c>crash.py</c> does not become
/// <c>python crash.py</c>, the person is back to filling in a program box and an arguments box
/// and knowing which goes where - which is the complication the simplified window exists to
/// remove.
/// </remarks>
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

    // ------------------------------------------------------------------ interpreters

    /// <summary>A script is launched through its interpreter, with the script as the argument.</summary>
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

    /// <summary>
    /// A managed .dll goes through the dotnet host, and must not name the file twice.
    /// </summary>
    /// <remarks>
    /// <c>TargetSpec</c> quotes the assembly path itself when LaunchViaDotnet is set, so putting
    /// it in the arguments as well produces <c>dotnet "app.dll" "app.dll"</c>.
    /// </remarks>
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

    /// <summary>An argument prefix such as -jar goes in unquoted, or it becomes a file name.</summary>
    [Fact]
    public void AJarKeepsItsUnquotedArgumentPrefix()
    {
        if (TargetFactory.FindOnPath("java") is null) return;

        var jar = Make("app.jar", "PK");
        var plan = TargetFactory.FromFile(jar);

        Assert.True(plan.Ok, plan.Problem);
        Assert.StartsWith("-jar \"", plan.Spec!.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing interpreter is named, rather than surfacing later as a Win32 error.
    /// </summary>
    /// <remarks>
    /// Launching anyway would produce a crash report about FixFinder rather than about the
    /// program, which is the least useful answer available.
    /// </remarks>
    [Fact]
    public void AMissingInterpreterIsReportedByName()
    {
        var script = Make("thing.lua", "print('hi')");
        var plan = TargetFactory.FromFile(script);

        if (TargetFactory.FindOnPath("lua") is not null) return;   // actually installed here

        Assert.False(plan.Ok);
        Assert.Contains("lua", plan.Problem!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not installed", plan.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An unfamiliar extension is attempted rather than refused.</summary>
    [Fact]
    public void AnUnknownExtensionIsRunDirectlyAndSaysSo()
    {
        var odd = Make("program.weird", "whatever");
        var plan = TargetFactory.FromFile(odd);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Equal(odd, plan.Spec!.ExecutablePath);
        Assert.Contains("recognises", plan.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ the defaults

    [Fact]
    public void TheWorkingDirectoryIsTheFilesOwnFolder()
    {
        var script = Make(Path.Combine("nested", "deep", "run.py"));
        var plan = TargetFactory.FromFile(script);

        if (!plan.Ok) return;   // no python on this machine

        Assert.Equal(Path.GetDirectoryName(script), plan.Spec!.WorkingDirectory);
    }

    [Fact]
    public void ATimeoutIsAlwaysSet()
    {
        var plan = TargetFactory.FromFile(Make("thing.exe", "MZ"));

        Assert.True(plan.Ok);
        Assert.Equal(TargetFactory.DefaultTimeout, plan.Spec!.Timeout);
    }

    // ------------------------------------------------------------------ refusals

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

    // ------------------------------------------------------------------ finding things

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
