using FixFinder.Core.Execution;
using FixFinder.Core.Verification;

namespace FixFinder.Tests;

/// <summary>
/// Covers building C, C++, C# and Java before running them.
/// </summary>
/// <remarks>
/// These languages need a step the scripting ones do not, and the step earns its keep twice
/// over: it makes them runnable at all, and it makes <b>compiler errors searchable</b>.
/// <c>C2065</c> and <c>CS0103</c> are globally unique strings that everyone who hits them pastes
/// verbatim into a search box, and FixFinder already had parsers that lift the code out - there
/// was simply nothing able to produce one.
/// <para>
/// Tests that need a compiler check for it and return early rather than failing. Which
/// toolchains exist is a property of the machine, and a suite that goes red on a laptop without
/// a JDK is reporting on the laptop rather than on the code.
/// </para>
/// </remarks>
public class CompiledLanguageTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    // ------------------------------------------------------------------ which languages

    [Theory]
    [InlineData(".c")]
    [InlineData(".cpp")]
    [InlineData(".cc")]
    [InlineData(".cxx")]
    [InlineData(".java")]
    [InlineData(".C")]
    public void TheLanguagesThatNeedBuildingAreRecognised(string extension) =>
        Assert.True(CompiledLanguages.Handles(extension));

    [Theory]
    [InlineData(".py")]
    [InlineData(".js")]
    [InlineData(".cs")]    // the .NET SDK builds and runs it in one command, so no separate step
    [InlineData(".exe")]
    public void TheOthersAreLeftAlone(string extension) =>
        Assert.False(CompiledLanguages.Handles(extension));

    [Fact]
    public void EveryCompiledLanguageIsOfferedByTheFilePicker()
    {
        var filter = TargetFactory.FileDialogFilter;

        Assert.Contains("*.c;", filter, StringComparison.Ordinal);
        Assert.Contains("*.cpp", filter, StringComparison.Ordinal);
        Assert.Contains("*.cs", filter, StringComparison.Ordinal);
        Assert.Contains("*.java", filter, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ C#

    /// <summary>
    /// A loose .cs file runs through the SDK, which builds and runs it in one command.
    /// </summary>
    /// <remarks>
    /// Both kinds of failure then arrive the same way: a compile error prints CS#### and exits
    /// non-zero, a runtime crash prints a stack trace. No project has to be set up either way.
    /// </remarks>
    [Fact]
    public void ALooseCSharpFileIsRunByTheDotnetSdk()
    {
        if (TargetFactory.FindOnPath("dotnet") is null) return;

        var plan = TargetFactory.FromFile(Write("thing.cs", "Console.WriteLine(1);"));

        Assert.True(plan.Ok, plan.Problem);
        Assert.False(plan.NeedsCompiling);
        Assert.Contains("dotnet", plan.Spec!.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("run ", plan.Spec.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void ACsprojIsRunWithTheProjectSwitch()
    {
        if (TargetFactory.FindOnPath("dotnet") is null) return;

        var plan = TargetFactory.FromFile(Write("App.csproj", "<Project />"));

        Assert.True(plan.Ok, plan.Problem);
        Assert.Contains("--project", plan.Spec!.Arguments, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ C and C++

    [Fact]
    public void ACFileGetsABuildStepAndRunsWhatItProduces()
    {
        if (Toolchains.FindGnu(false) is null && Toolchains.FindMsvc() is null) return;

        var source = Write("main.c", "int main(void){return 0;}");
        var plan = TargetFactory.FromFile(source);

        Assert.True(plan.Ok, plan.Problem);
        Assert.True(plan.NeedsCompiling);
        Assert.NotNull(plan.Compile);

        // The thing that gets run is the built binary, not the source.
        Assert.EndsWith(".exe", plan.Spec!.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source, plan.ChosenFile);
    }

    /// <summary>
    /// The build step is also the rebuild command, or nothing compiled can ever be verified.
    /// </summary>
    /// <remarks>
    /// The verifier refuses to re-run a binary it knows is stale, so without this every compiled
    /// language reaches <c>Inconclusive</c> however good the patch was - and the loop, which only
    /// continues on a verdict, never gets a second round for C, C++ or Java at all. The command
    /// is derived from the compile step rather than guessed again, so the two cannot disagree.
    /// </remarks>
    [Fact]
    public void TheBuildStepIsAlsoTheRebuildCommand()
    {
        if (Toolchains.FindGnu(false) is null && Toolchains.FindMsvc() is null) return;

        var plan = TargetFactory.FromFile(Write("rebuild.c", "int main(void){return 0;}"));

        Assert.True(plan.Ok, plan.Problem);
        Assert.NotNull(plan.Spec!.BuildCommand);
        Assert.Equal(plan.Compile!.DisplayCommandLine, plan.Spec.BuildCommand);
        Assert.Equal(plan.Compile.WorkingDirectory, plan.Spec.BuildWorkingDirectory);

        // The verifier splits it back into a program and arguments, so that has to survive the
        // round trip - the MSVC route is "cmd.exe /c <batch>", with quoting either side of it.
        var (program, arguments) = FixVerifier.SplitCommand(plan.Spec.BuildCommand!);

        // Either a real path, or a name the shell can find - the MSVC route uses a bare "cmd.exe"
        // and leaves resolving it to the process start, exactly as typing it would.
        Assert.True(File.Exists(program) || TargetFactory.FindOnPath(program) is not null, program);

        // Whatever is being built has to survive the split, or the rebuild compiles nothing.
        Assert.NotEmpty(arguments);
    }

    /// <summary>
    /// Build output goes to its own folder, never beside the source.
    /// </summary>
    /// <remarks>
    /// Someone's project directory is not FixFinder's to litter with .obj and .pdb files, and a
    /// build folder inside a source tree is one more thing the patch mapper would have to learn
    /// to ignore.
    /// </remarks>
    [Fact]
    public void BuildOutputNeverLandsBesideTheSource()
    {
        if (Toolchains.FindGnu(false) is null && Toolchains.FindMsvc() is null) return;

        var source = Write("main.c", "int main(void){return 0;}");
        var plan = TargetFactory.FromFile(source);

        Assert.True(plan.Ok, plan.Problem);
        Assert.DoesNotContain(_temp.Path, plan.Spec!.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(CompiledLanguages.BuildRoot, plan.Spec.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoFilesOfTheSameNameDoNotShareABuildFolder()
    {
        var one = Path.Combine(_temp.Path, "a", "main.c");
        var two = Path.Combine(_temp.Path, "b", "main.c");

        Assert.NotEqual(CompiledLanguages.OutputDirectory(one), CompiledLanguages.OutputDirectory(two));
    }

    /// <summary>
    /// The generated build script must not put a backslash immediately before a closing quote.
    /// </summary>
    /// <remarks>
    /// The bug this was written for, and it is invisible on inspection. A quoted Windows path
    /// ending in a separator - <c>"C:\out\obj\"</c> - has a backslash right before the closing
    /// quote, which the command-line parser reads as an <i>escaped quote</i>. The argument then
    /// swallows the next token, and cl reports "Command line error D8003: missing source
    /// filename" while pointing at a command line that looks perfectly correct.
    /// </remarks>
    [Fact]
    public void TheGeneratedBuildScriptHasNoTrailingBackslashBeforeAQuote()
    {
        if (Toolchains.FindMsvc() is null) return;
        if (Toolchains.FindGnu(false) is not null) return;   // gcc needs no script

        var source = Write("main.c", "int main(void){return 0;}");
        var plan = TargetFactory.FromFile(source);

        Assert.True(plan.Ok, plan.Problem);

        var batch = Path.Combine(CompiledLanguages.OutputDirectory(source), "build.cmd");
        Assert.True(File.Exists(batch), "the build script should have been written");

        var text = File.ReadAllText(batch);

        Assert.DoesNotContain("\\\"", text, StringComparison.Ordinal);
        Assert.Contains(source, text, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ Java

    /// <summary>
    /// The class to launch is qualified by whatever package the file declares.
    /// </summary>
    /// <remarks>
    /// Running <c>Main</c> when the file says <c>package app;</c> fails with
    /// NoClassDefFoundError - an error about FixFinder rather than about the program, which is
    /// the least useful thing it could report.
    /// </remarks>
    [Fact]
    public void AJavaPackageDeclarationQualifiesTheClassThatIsLaunched()
    {
        if (Toolchains.FindJavac() is null) return;

        var source = Write("Main.java", """
            package com.example.app;

            public class Main {
                public static void main(String[] args) { }
            }
            """);

        var plan = TargetFactory.FromFile(source);

        Assert.True(plan.Ok, plan.Problem);
        Assert.Contains("com.example.app.Main", plan.Spec!.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void AJavaFileWithNoPackageLaunchesItsBareClassName()
    {
        if (Toolchains.FindJavac() is null) return;

        var source = Write("Solo.java", "public class Solo { public static void main(String[] a) { } }");
        var plan = TargetFactory.FromFile(source);

        Assert.True(plan.Ok, plan.Problem);
        Assert.EndsWith(" Solo", plan.Spec!.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    /// A missing toolchain is named, with the one command that would install it.
    /// </summary>
    /// <remarks>
    /// "Cannot run this file" is a dead end. Saying which compiler is missing, and how to get
    /// it, is the difference between a refusal and an answer.
    /// </remarks>
    [Fact]
    public void AMissingToolchainSaysWhatToInstall()
    {
        var plan = TargetFactory.FromFile(Write("Thing.java", "public class Thing { }"));

        if (Toolchains.FindJavac() is not null) return;   // a JDK is present here

        Assert.False(plan.Ok);
        Assert.Contains("JDK", plan.Problem!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("winget install", plan.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// MSVC is found by its setup script, not by cl.exe.
    /// </summary>
    /// <remarks>
    /// Launching the compiler directly fails with missing DLLs or a flood of "cannot open
    /// include file", because it depends entirely on the environment vcvarsall sets up - which
    /// is also why it is almost never on PATH.
    /// </remarks>
    [Fact]
    public void MsvcIsFoundThroughItsSetupScript()
    {
        var msvc = Toolchains.FindMsvc();
        if (msvc is null) return;

        Assert.NotNull(msvc.SetupScript);
        Assert.EndsWith("vcvarsall.bat", msvc.SetupScript!, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(msvc.SetupScript));
    }

    [Fact]
    public void TheToolchainSummaryNamesEveryLanguage()
    {
        var described = string.Join("\n", Toolchains.Describe());

        Assert.Contains("C ", described, StringComparison.Ordinal);
        Assert.Contains("C++", described, StringComparison.Ordinal);
        Assert.Contains("C#", described, StringComparison.Ordinal);
        Assert.Contains("Java", described, StringComparison.Ordinal);
    }
}
