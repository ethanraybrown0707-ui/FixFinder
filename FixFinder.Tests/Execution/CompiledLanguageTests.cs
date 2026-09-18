using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Covers building C, C++, C# and Java before running them.</summary>
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
    [InlineData(".cs")]
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

    [Fact]
    public void ACFileGetsABuildStepAndRunsWhatItProduces()
    {
        if (Toolchains.FindGnu(false) is null && Toolchains.FindMsvc() is null) return;

        var source = Write("main.c", "int main(void){return 0;}");
        var plan = TargetFactory.FromFile(source);

        Assert.True(plan.Ok, plan.Problem);
        Assert.True(plan.NeedsCompiling);
        Assert.NotNull(plan.Compile);

        Assert.EndsWith(".exe", plan.Spec!.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source, plan.ChosenFile);
    }

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

    [Fact]
    public void TheGeneratedBuildScriptHasNoTrailingBackslashBeforeAQuote()
    {
        if (Toolchains.FindMsvc() is null) return;
        if (Toolchains.FindGnu(false) is not null) return;

        var source = Write("main.c", "int main(void){return 0;}");
        var plan = TargetFactory.FromFile(source);

        Assert.True(plan.Ok, plan.Problem);

        var batch = Path.Combine(CompiledLanguages.OutputDirectory(source), "build.cmd");
        Assert.True(File.Exists(batch), "the build script should have been written");

        var text = File.ReadAllText(batch);

        Assert.DoesNotContain("\\\"", text, StringComparison.Ordinal);
        Assert.Contains(source, text, StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public void AMissingToolchainSaysWhatToInstall()
    {
        var plan = TargetFactory.FromFile(Write("Thing.java", "public class Thing { }"));

        if (Toolchains.FindJavac() is not null) return;

        Assert.False(plan.Ok);
        Assert.Contains("JDK", plan.Problem!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("winget install", plan.Problem!, StringComparison.OrdinalIgnoreCase);
    }

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
