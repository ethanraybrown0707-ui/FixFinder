using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The runtime's own correction, read out of a real crash and applied to a real file.</summary>
public class RuntimeSuggestionTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private static readonly string? Python =
        TargetFactory.FindOnPath("python") ??
        TargetFactory.FindOnPath("py") ??
        TargetFactory.FindOnPath("python3");

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<ParsedError> CrashOf(string script)
    {
        var spec = new TargetSpec
        {
            ExecutablePath = Python!,
            Arguments = $"\"{script}\"",
            WorkingDirectory = _temp.Path,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, CancellationToken.None);

        Assert.NotNull(run.Error);
        return run.Error!;
    }

    [Fact]
    public async Task AMisspeltAttributeIsReadFromWhatPythonSaid()
    {
        if (Python is null) return;

        var script = Write("attr.py", """
            class Supply:
                def __init__(self):
                    self.heavy = True

            s = Supply()
            print(s.heavey)
            """);

        var correction = RuntimeSuggestion.Read(await CrashOf(script));

        Assert.NotNull(correction);
        Assert.Equal("heavey", correction!.Wrong);
        Assert.Equal("heavy", correction.Right);
        Assert.Equal(6, correction.Line);
    }

    [Fact]
    public async Task TheClassNameIsNotMistakenForTheMisspeltName()
    {
        if (Python is null) return;

        var script = Write("attr2.py", """
            class Supply:
                def __init__(self):
                    self.heavy = True

            print(Supply().heavey)
            """);

        var correction = RuntimeSuggestion.Read(await CrashOf(script));

        Assert.Equal("heavey", correction!.Wrong);
        Assert.NotEqual("Supply", correction.Wrong);
    }

    [Fact]
    public async Task AMisspeltNameIsRead()
    {
        if (Python is null) return;

        var script = Write("name.py", """
            average = 10
            print(avarage)
            """);

        var correction = RuntimeSuggestion.Read(await CrashOf(script));

        Assert.Equal("avarage", correction!.Wrong);
        Assert.Equal("average", correction.Right);
    }

    [Fact]
    public async Task AnErrorWithNoSuggestionProducesNothing()
    {
        if (Python is null) return;

        var script = Write("keyerr.py", """
            data = {}
            print(data["user_id"])
            """);

        Assert.Null(RuntimeSuggestion.Read(await CrashOf(script)));
    }

    private static ParsedError Captured(string language, string type, string message, string? raw = null) =>
        new()
        {
            LanguageId = language,
            Confidence = 90,
            RawText = raw ?? $"{type}: {message}",
            FirstLineSequence = 0,
            ExceptionType = type,
            Message = message,
            Frames = [new ErrorFrame { Order = 0, File = "main.c", Line = 4, RawLine = "" }],
        };

    [Theory]
    [InlineData("'avarage' undeclared (first use in this function); did you mean 'average'?")]
    [InlineData("use of undeclared identifier 'avarage'; did you mean 'average'?")]
    public void BothCCompilersAreRead(string message)
    {
        var correction = RuntimeSuggestion.Read(Captured("gcc", "compile error", message));

        Assert.NotNull(correction);
        Assert.Equal("avarage", correction!.Wrong);
        Assert.Equal("average", correction.Right);
    }

    [Theory]
    [InlineData("undefined local variable or method 'avarage' for main", "avarage", "average")]
    [InlineData("undefined method 'heavey' for an instance of Supply", "heavey", "heavy")]
    public void RubyIsReadFromTheLineUnderTheMessage(string message, string wrong, string right)
    {
        var raw = $"main.rb:3:in '<main>': {message} (NameError)\nDid you mean?  {right}";

        var correction = RuntimeSuggestion.Read(Captured("ruby", "NameError", message, raw));

        Assert.NotNull(correction);
        Assert.Equal(wrong, correction!.Wrong);
        Assert.Equal(right, correction.Right);
    }

    [Fact]
    public void APhraseInTheProgramsOwnOutputIsNotASuggestion()
    {
        var raw = "checking spelling... did you mean 'banana'?\nZeroDivisionError: division by zero";

        Assert.Null(RuntimeSuggestion.Read(
            Captured("python", "ZeroDivisionError", "division by zero", raw)));
    }

    [Fact]
    public async Task TwoOccurrencesOnOneLineAreRefusedRatherThanGuessedAt()
    {
        if (Python is null) return;

        var script = Write("twice.py", """
            average = 10
            print(avarage + avarage)
            """);

        var error = await CrashOf(script);

        Assert.NotNull(RuntimeSuggestion.Read(error));
        Assert.Null(RuntimeSuggestion.For(error, _temp.Path));
    }

    [Fact]
    public async Task AMissingFileProducesNothing()
    {
        if (Python is null) return;

        var script = Write("gone.py", """
            average = 10
            print(avarage)
            """);

        var error = await CrashOf(script);
        File.Delete(script);

        Assert.Null(RuntimeSuggestion.For(error, _temp.Path));
    }
}
