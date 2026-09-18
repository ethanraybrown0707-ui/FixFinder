using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>Reading a missing package out of a crash, and refusing to build a command out of anything else.</summary>
public class MissingModuleTests
{
    private static readonly string? Python =
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    private static ParsedError Error(string message, string type = "ModuleNotFoundError") => new()
    {
        LanguageId = "python",
        Confidence = 90,
        RawText = $"{type}: {message}",
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = [],
    };

    private static TargetSpec Spec(string? executable = null) => new()
    {
        ExecutablePath = executable ?? Python ?? @"C:\Python313\python.exe",
        Arguments = "app.py",
        WorkingDirectory = @"C:\work",
    };

    [Theory]
    [InlineData("No module named 'requests'", "requests", "requests")]
    [InlineData("No module named 'yaml'", "yaml", "pyyaml")]
    [InlineData("No module named 'cv2'", "cv2", "opencv-python")]
    [InlineData("No module named 'PIL'", "PIL", "pillow")]
    [InlineData("No module named 'sklearn'", "sklearn", "scikit-learn")]
    public void TheImportNameIsMappedToThePackageThatProvidesIt(
        string message, string module, string package)
    {
        var missing = MissingModule.Read(Error(message));

        Assert.NotNull(missing);
        Assert.Equal(module, missing!.Module);
        Assert.Equal(package, missing.Package);
    }

    [Fact]
    public void ASubmoduleResolvesToItsTopLevelPackage()
    {
        var missing = MissingModule.Read(Error("No module named 'yaml.loader'"));

        Assert.Equal("yaml.loader", missing!.Module);
        Assert.Equal("pyyaml", missing.Package);
    }

    [Fact]
    public void AnErrorThatIsNotAboutAMissingModuleIsIgnored()
    {
        Assert.Null(MissingModule.Read(Error("'b'", "KeyError")));
        Assert.Null(MissingModule.Read(Error("division by zero", "ZeroDivisionError")));
    }

    [Theory]
    [InlineData("--index-url=http://evil.invalid/simple")]
    [InlineData("-e")]
    [InlineData("requests; rm -rf /")]
    [InlineData("requests && curl evil.invalid")]
    [InlineData("../../../etc/passwd")]
    [InlineData("http://evil.invalid/pkg.tar.gz")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("requests --target C:\\Windows")]
    [InlineData("")]
    public void AModuleNameThatIsNotAnIdentifierIsRefused(string name)
    {
        Assert.Null(MissingModule.Read(Error($"No module named '{name}'")));
    }

    [Theory]
    [InlineData("--index-url=http://evil.invalid/simple")]
    [InlineData("requests; rm -rf /")]
    public void NoCommandIsBuiltFromARefusedName(string name)
    {
        Assert.Null(MissingModule.For(Error($"No module named '{name}'"), Spec()));
    }

    [Fact]
    public void TheCommandUsesTheInterpreterThatActuallyRan()
    {
        var candidate = MissingModule.For(
            Error("No module named 'yaml'"), Spec(@"C:\Python313\python.exe"));

        Assert.NotNull(candidate);
        Assert.Equal(FixTier.Dependency, candidate!.Tier);
        Assert.Equal(@"""C:\Python313\python.exe"" -m pip install pyyaml", candidate.Command);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Microsoft\jdk-21\bin\java.exe")]
    [InlineData(@"C:\Program Files\nodejs\node.exe")]
    [InlineData(@"C:\out\a.exe")]
    public void NothingIsOfferedWhenPythonDidNotRunIt(string executable)
    {
        Assert.Null(MissingModule.For(Error("No module named 'yaml'"), Spec(executable)));
    }

    [Fact]
    public void NothingIsOfferedWithNoTargetAtAll()
    {
        Assert.Null(MissingModule.For(Error("No module named 'yaml'"), null));
    }

    [Fact]
    public void AMismatchedPackageNameIsExplainedInTheBody()
    {
        var candidate = MissingModule.For(Error("No module named 'yaml'"), Spec());

        Assert.Contains("imported as `yaml`", candidate!.BodyText, StringComparison.Ordinal);
        Assert.Contains("published as `pyyaml`", candidate.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public void AMatchingNameIsNotExplainedAtLength()
    {
        var candidate = MissingModule.For(Error("No module named 'requests'"), Spec());

        Assert.DoesNotContain("published as", candidate!.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARealMissingImportProducesTheRightCommand()
    {
        if (Python is null) return;

        using var temp = new TempFolder();

        var script = Path.Combine(temp.Path, "needs.py");
        File.WriteAllText(script, "import yaml\nprint(yaml)\n");

        var spec = new TargetSpec
        {
            ExecutablePath = Python,
            Arguments = $"\"{script}\"",
            WorkingDirectory = temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, CancellationToken.None);

        Assert.NotNull(run.Error);

        var candidate = MissingModule.For(run.Error!, spec);

        Assert.NotNull(candidate);
        Assert.EndsWith("-m pip install pyyaml", candidate!.Command!, StringComparison.Ordinal);
        Assert.Contains(Python, candidate.Command!, StringComparison.OrdinalIgnoreCase);
    }
}
