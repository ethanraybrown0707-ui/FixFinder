using FixFinder.Core.Parsing;
using FixFinder.Core.Fingerprinting;

namespace FixFinder.Tests;

/// <summary>Drives every parser through <c>ParserRegistry</c> against captured crash output, so the tests cover detection and
/// parsing together - picking the wrong parser is just as much a failure as parsing badly.</summary>
public class StackTraceParserTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed;
    }

    [Theory]
    [InlineData("csharp/inner-exception.txt", "csharp")]
    [InlineData("csharp/aggregate.txt", "csharp")]
    [InlineData("csharp/async-previous-location.txt", "csharp")]
    [InlineData("python/keyerror.txt", "python")]
    [InlineData("python/chained.txt", "python")]
    [InlineData("python/inside-json-logs.txt", "python")]
    [InlineData("python/syntax-error.txt", "python")]
    [InlineData("python/indentation-error.txt", "python")]
    [InlineData("python/syntax-error-after-output.txt", "python")]
    [InlineData("node/typeerror.txt", "node")]
    [InlineData("java/caused-by.txt", "java")]
    [InlineData("go/panic.txt", "go")]
    [InlineData("rust/modern-with-backtrace.txt", "rust")]
    [InlineData("rust/legacy-no-backtrace.txt", "rust")]
    [InlineData("gcc/asan.txt", "gcc")]
    [InlineData("gcc/compile-error.txt", "gcc")]
    [InlineData("gcc/segfault.txt", "gcc")]
    [InlineData("msvc/cs0103.txt", "msvc")]
    [InlineData("ruby/zerodivision.txt", "ruby")]
    [InlineData("ruby/ruby34-quotes.txt", "ruby")]
    [InlineData("php/uncaught-typeerror.txt", "php")]
    [InlineData("php/parse-error.txt", "php")]
    [InlineData("php/display-errors.txt", "php")]
    [InlineData("powershell/cmdlet-error.txt", "powershell")]
    [InlineData("powershell/after-output.txt", "powershell")]
    [InlineData("dart/rangeerror.txt", "dart")]
    [InlineData("elixir/arithmetic.txt", "elixir")]
    [InlineData("perl/carp.txt", "perl")]
    [InlineData("lua/nil-index.txt", "lua")]
    [InlineData("generic/perl.txt", "generic")]
    public void RoutesEachFixtureToTheRightParser(string fixture, string expectedLanguage)
    {
        Assert.Equal(expectedLanguage, Parse(fixture).LanguageId);
    }

    [Fact]
    public void Python_ASyntaxErrorKeepsItsTypeFileAndLine()
    {
        var error = Parse("python/syntax-error.txt");

        Assert.Equal("python", error.LanguageId);
        Assert.Equal("SyntaxError", error.ExceptionType);
        Assert.Equal("'(' was never closed", error.Message);
        Assert.True(error.Confidence >= 90);

        var frame = Assert.Single(error.Frames);
        Assert.Equal(4, frame.Line);
        Assert.EndsWith("reader.py", frame.File!, StringComparison.Ordinal);
    }

    [Fact]
    public void Python_AnIndentationErrorIsReadTheSameWay()
    {
        var error = Parse("python/indentation-error.txt");

        Assert.Equal("IndentationError", error.ExceptionType);
        Assert.Equal("unexpected indent", error.Message);
        Assert.Equal(12, Assert.Single(error.Frames).Line);
    }

    [Fact]
    public void Python_ASyntaxErrorIsFoundAfterOrdinaryOutput()
    {
        var error = Parse("python/syntax-error-after-output.txt");

        Assert.Equal("SyntaxError", error.ExceptionType);
        Assert.StartsWith("invalid syntax.", error.Message!, StringComparison.Ordinal);
        Assert.EndsWith("worker.py", Assert.Single(error.Frames).File!, StringComparison.Ordinal);
    }

    [Fact]
    public void Python_ASyntaxErrorIsAlwaysFirstParty()
    {
        var error = Parse("python/syntax-error.txt");
        var fingerprint = FingerprintBuilder.Build(error);

        Assert.True(fingerprint.CulpritIsFirstParty);
        Assert.Equal("SyntaxError", fingerprint.ShortExceptionType);

        Assert.Contains("SyntaxError", fingerprint.Tight.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void DotNet_WalksTheInnerExceptionChain()
    {
        var error = Parse("csharp/inner-exception.txt");

        Assert.Equal("System.InvalidOperationException", error.ExceptionType);
        Assert.Equal("Could not load the basket from the store.", error.Message);

        var cause = Assert.Single(error.Causes);
        Assert.Equal("System.NullReferenceException", cause.ExceptionType);
        Assert.Same(cause, error.RootCause);

        Assert.Equal(2, cause.Frames.Count);
        Assert.Equal(2, error.Frames.Count);
        Assert.Equal("CrashDotNet.Program.ReadFromStore()", cause.Frames[0].Symbol);
        Assert.Equal(53, cause.Frames[0].Line);
    }

    [Fact]
    public void DotNet_CulpritIsWhereItThrewNotWhereItWasWrapped()
    {
        var error = Parse("csharp/inner-exception.txt");

        Assert.NotNull(error.CulpritFrame);
        Assert.Equal("Program.cs:53", error.CulpritFrame.Location);
    }

    [Fact]
    public void DotNet_ReadsAggregateExceptionSiblings()
    {
        var error = Parse("csharp/aggregate.txt");

        Assert.Equal("System.AggregateException", error.ExceptionType);
        Assert.Contains("ArgumentException", error.RootCause.ExceptionType);
    }

    [Fact]
    public void DotNet_KeepsFramesAfterTheAsyncResumeBoundary()
    {
        var error = Parse("csharp/async-previous-location.txt");

        Assert.Equal(3, error.Frames.Count);
        Assert.Contains(error.Frames, f => f.File is not null && f.File.EndsWith("FeedClient.cs"));
    }

    [Fact]
    public void Python_ReversesFramesSoOrderZeroIsWhereItThrew()
    {
        var error = Parse("python/keyerror.txt");

        Assert.Equal("KeyError", error.ExceptionType);
        Assert.Equal("'user_id'", error.Message);
        Assert.Equal(3, error.Frames.Count);
        Assert.Equal("connect", error.Frames[0].Symbol);
        Assert.Equal(23, error.Frames[0].Line);
        Assert.Equal("<module>", error.Frames[^1].Symbol);
    }

    [Fact]
    public void Python_SkipsTheSourceEchoAndCaretLines()
    {
        var error = Parse("python/keyerror.txt");

        Assert.All(error.Frames, f => Assert.NotNull(f.File));
        Assert.Equal(3, error.Frames.Count);
    }

    [Fact]
    public void Python_ChainsBlocksSoTheLastOneIsTheEscapingException()
    {
        var error = Parse("python/chained.txt");

        Assert.Equal("RuntimeError", error.ExceptionType);
        Assert.Equal("KeyError", error.RootCause.ExceptionType);
    }

    [Fact]
    public void Python_FindsATracebackBuriedInStructuredLogLines()
    {
        var error = Parse("python/inside-json-logs.txt");

        Assert.Equal("IndexError", error.ExceptionType);
        Assert.Equal("batch.py:88", error.CulpritFrame?.Location);
    }

    [Fact]
    public void Python_FindsATracebackAfterThousandsOfNoiseLines()
    {
        var error = Parse("adversarial/noise-then-traceback.txt");

        Assert.Equal("sqlite3.OperationalError", error.ExceptionType);
        Assert.Equal("OperationalError", error.ShortExceptionType);
    }

    [Fact]
    public void Python_PicksTheLastTracebackWhenTwoAreUnrelated()
    {
        var error = Parse("adversarial/two-python-tracebacks.txt");

        Assert.Equal("ZeroDivisionError", error.ExceptionType);
    }

    [Fact]
    public void Node_ReadsFramesWithColumnsAndMarksRuntimeInternals()
    {
        var error = Parse("node/typeerror.txt");

        Assert.Equal("TypeError", error.ExceptionType);
        Assert.Equal(4, error.Frames.Count);
        Assert.Equal("renderUser", error.Frames[0].Symbol);
        Assert.Equal(12, error.Frames[0].Line);
        Assert.Equal(29, error.Frames[0].Column);

        Assert.Equal(FrameOrigin.Runtime, error.Frames[^1].Origin);
    }

    [Fact]
    public void Node_ClassifiesNodeModulesAsThirdParty()
    {
        var error = Parse("node/typeerror.txt");

        Assert.Contains(error.Frames, f => f.Origin == FrameOrigin.ThirdParty);
        Assert.Equal("express", CulpritFrameSelector.NearestThirdPartyModule(error));
    }

    [Fact]
    public void Java_ReadsCausedByAndRecordsElidedFrames()
    {
        var error = Parse("java/caused-by.txt");

        Assert.Equal("java.lang.IllegalStateException", error.ExceptionType);
        Assert.Equal("java.io.IOException", error.RootCause.ExceptionType);

        Assert.Contains(error.RootCause.Frames, f => f.Symbol is not null && f.Symbol.Contains("3 frames identical"));
    }

    [Fact]
    public void Java_RecordsFileNamesWithoutDirectories()
    {
        var error = Parse("java/caused-by.txt");

        Assert.Equal("Config.java", error.RootCause.Frames[1].File);
        Assert.Equal(27, error.RootCause.Frames[1].Line);
    }

    [Fact]
    public void Go_PairsSymbolAndLocationLinesIntoOneFrame()
    {
        var error = Parse("go/panic.txt");

        Assert.Equal(2, error.Frames.Count);
        Assert.Equal("main.process", error.Frames[0].Symbol);
        Assert.Equal(14, error.Frames[0].Line);
        Assert.EndsWith("main.go", error.Frames[0].File);
    }

    [Fact]
    public void Go_ParsesOnlyTheFirstGoroutineBlock()
    {
        var error = Parse("go/panic.txt");

        Assert.DoesNotContain(error.Frames, f => f.Symbol is not null && f.Symbol.Contains("persistConn"));
    }

    [Fact]
    public void Rust_ModernFormatTakesTheMessageFromTheFollowingLine()
    {
        var error = Parse("rust/modern-with-backtrace.txt");

        Assert.Equal("index out of bounds: the len is 3 but the index is 10", error.Message);
        Assert.Equal("main.rs:4", error.Frames[0].Location);
        Assert.True(error.Frames.Count > 1, "the backtrace frames should have been read");
    }

    [Fact]
    public void Rust_LegacyFormatTakesTheMessageFromTheHeaderLine()
    {
        var error = Parse("rust/legacy-no-backtrace.txt");

        Assert.Contains("Option::unwrap()", error.Message);
        Assert.Equal("config.rs:17", error.Frames[0].Location);
    }

    [Fact]
    public void Gcc_ReadsSanitizerReportsWithFrames()
    {
        var error = Parse("gcc/asan.txt");

        Assert.Equal("heap-buffer-overflow", error.ExceptionType);
        Assert.Equal("write_row", error.Frames[0].Symbol);
        Assert.Equal(88, error.Frames[0].Line);
    }

    [Fact]
    public void Gcc_ReadsCompilerErrorsButNotWarnings()
    {
        var error = Parse("gcc/compile-error.txt");

        Assert.Equal("compile error", error.ExceptionType);
        Assert.Contains("row_count", error.Message);
        Assert.Equal(88, error.Frames[0].Line);
    }

    [Fact]
    public void Gcc_ReportsABareSegfaultWithLowConfidence()
    {
        var error = Parse("gcc/segfault.txt");

        Assert.Contains("Segmentation fault", error.ExceptionType);
        Assert.Empty(error.Frames);
        Assert.InRange(error.Confidence, 1, 60);
    }

    [Fact]
    public void Msvc_LiftsTheErrorCodeIntoItsOwnField()
    {
        var error = Parse("msvc/cs0103.txt");

        Assert.Equal("CS0103", error.ErrorCode);
        Assert.Equal(12, error.Frames[0].Line);
        Assert.Equal(17, error.Frames[0].Column);
        Assert.DoesNotContain("MyApp.csproj", error.Message);
    }

    [Theory]
    [InlineData("ruby/zerodivision.txt")]
    [InlineData("ruby/ruby34-quotes.txt")]
    public void Ruby_AcceptsBothTheOldAndNewBacktraceQuoting(string fixture)
    {
        var error = Parse(fixture);

        Assert.Equal("ZeroDivisionError", error.ExceptionType);
        Assert.Equal("divided by 0", error.Message);
        Assert.Equal(3, error.Frames.Count);
        Assert.Equal("divide", error.Frames[0].Symbol);
    }

    [Fact]
    public void Generic_HarvestsFileAndLineFromAnUnsupportedLanguage()
    {
        var error = Parse("generic/perl.txt");

        Assert.Equal("generic", error.LanguageId);
        Assert.Null(error.ExceptionType);
        Assert.Contains(error.Frames, f => f.File == "lib/Builder.pm" && f.Line == 214);
    }

    [Fact]
    public void Generic_NeverOutscoresARealParser()
    {
        foreach (var fixture in new[]
                 {
                     "python/keyerror.txt", "csharp/inner-exception.txt", "node/typeerror.txt",
                     "java/caused-by.txt", "go/panic.txt", "ruby/zerodivision.txt",
                 })
        {
            var scores = Registry.DetectionScores(Fixtures.LoadStackTrace(fixture));
            var generic = scores.Single(s => s.LanguageId == "generic");

            Assert.True(scores[0].LanguageId != "generic",
                $"the generic parser won on {fixture} (scored {generic.Score})");
        }
    }

    [Fact]
    public void NoSpecificParserIsConfidentAboutAnotherLanguage()
    {
        var expected = new Dictionary<string, string>
        {
            ["python/keyerror.txt"] = "python",
            ["csharp/inner-exception.txt"] = "csharp",
            ["node/typeerror.txt"] = "node",
            ["java/caused-by.txt"] = "java",
            ["go/panic.txt"] = "go",
            ["rust/modern-with-backtrace.txt"] = "rust",
            ["ruby/zerodivision.txt"] = "ruby",
            ["msvc/cs0103.txt"] = "msvc",
        };

        foreach (var (fixture, owner) in expected)
        {
            var scores = Registry.DetectionScores(Fixtures.LoadStackTrace(fixture));
            var winner = scores[0];

            Assert.True(winner.LanguageId == owner,
                $"{fixture} should be owned by '{owner}' but '{winner.LanguageId}' scored highest " +
                $"({string.Join(", ", scores.Take(3).Select(s => $"{s.LanguageId}={s.Score}"))})");
        }
    }

    [Fact]
    public void PrefersTheErrorOnStdErrOverALookalikeOnStdOut()
    {
        var lines = Fixtures.Combine(
            [
                "INFO  connecting to cache",
                "ERROR ConnectionRefusedError: [Errno 111] Connection refused - retrying",
                "INFO  recovered, continuing",
            ],
            "python/keyerror.txt");

        var parsed = Registry.Parse(lines);

        Assert.NotNull(parsed);
        Assert.Equal("python", parsed.LanguageId);
        Assert.Equal("KeyError", parsed.ExceptionType);
    }

    [Fact]
    public void StillFindsATraceThatWasWrittenEntirelyToStdOut()
    {
        var lines = Fixtures.LoadSplit("python/keyerror.txt", stdOutPredicate: _ => true);

        var parsed = Registry.Parse(lines);

        Assert.NotNull(parsed);
        Assert.Equal("python", parsed.LanguageId);
        Assert.Equal("KeyError", parsed.ExceptionType);
    }

    [Fact]
    public void Generic_SkipsABlandClosingLineInFavourOfOneWithALocation()
    {
        var error = Parse("generic/perl.txt");

        Assert.NotEmpty(error.Frames);
        Assert.DoesNotContain("build aborted", error.Message);
    }

    [Fact]
    public void ReturnsNullForOutputWithNoErrorInIt()
    {
        var parsed = Registry.Parse(Fixtures.FromText(
            "starting up\nloaded 12 records\nwrote report.csv\ndone in 1.4s"));

        Assert.Null(parsed);
    }

    [Fact]
    public void ReturnsNullForEmptyOutput()
    {
        Assert.Null(Registry.Parse([]));
    }

    [Theory]
    [InlineData("requests.exceptions.InvalidSchema: No connection adapters were found for 'htp://x'",
                "requests.exceptions.InvalidSchema", "InvalidSchema")]
    [InlineData("requests.exceptions.MissingSchema: Invalid URL 'example.com'",
                "requests.exceptions.MissingSchema", "MissingSchema")]
    [InlineData("requests.exceptions.TooManyRedirects: Exceeded 30 redirects.",
                "requests.exceptions.TooManyRedirects", "TooManyRedirects")]
    [InlineData("socket.timeout: timed out", "socket.timeout", "timeout")]
    [InlineData("KeyError: 'user_id'", "KeyError", "KeyError")]
    public void AnExceptionTypeWithAnUnusualNameIsStillRead(string terminator, string expected, string shortName)
    {
        var parsed = new ParserRegistry().Parse(Fixtures.FromText($"""
            Traceback (most recent call last):
              File "C:\app\main.py", line 12, in <module>
                response = fetch(url)
              File "C:\app\net.py", line 4, in fetch
                raise Boom()
            {terminator}
            """), []);

        Assert.NotNull(parsed);
        Assert.Equal("python", parsed!.LanguageId);
        Assert.Equal(expected, parsed.ExceptionType);
        Assert.Equal(shortName, parsed.ShortExceptionType);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Message), "the message must survive too");
    }

    [Fact]
    public void ALineWithSpacesBeforeItsColonIsNotATerminator()
    {
        var parsed = new ParserRegistry().Parse(Fixtures.FromText("""
            Traceback (most recent call last):
              File "C:\app\main.py", line 12, in <module>
                boom()
            ValueError: the real one
            2026-09-11 08:00:00 INFO: something logged afterwards
            """), []);

        Assert.NotNull(parsed);
        Assert.Equal("ValueError", parsed!.ExceptionType);
        Assert.Equal("the real one", parsed.Message);
    }

    [Fact]
    public void AJavacDiagnosticIsReadByTheJavaParser()
    {
        var parsed = new ParserRegistry().Parse(
            Fixtures.LoadStackTrace("java/javac-cannot-find-symbol.txt"), []);

        Assert.NotNull(parsed);
        Assert.Equal("java", parsed!.LanguageId);
        Assert.Equal("compile error", parsed.ExceptionType);
        Assert.Contains("cannot find symbol", parsed.Message!, StringComparison.Ordinal);

        Assert.Contains("variable avg", parsed.Message!, StringComparison.Ordinal);
        Assert.Contains("class Main", parsed.Message!, StringComparison.Ordinal);

        var frame = Assert.Single(parsed.Frames);
        Assert.Equal(18, frame.Line);
        Assert.EndsWith("Main.java", frame.File!, StringComparison.Ordinal);
    }

    [Fact]
    public void AJavacDiagnosticSurvivesADriveLetterInThePath()
    {
        var absolute = new ParserRegistry().Parse(
            Fixtures.LoadStackTrace("java/javac-cannot-find-symbol.txt"), []);

        var relative = new ParserRegistry().Parse(
            Fixtures.LoadStackTrace("java/javac-relative-path.txt"), []);

        Assert.Equal("java", absolute!.LanguageId);
        Assert.Equal("java", relative!.LanguageId);
        Assert.Equal(7, relative.Frames[0].Line);
        Assert.Contains("incompatible types", relative.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuntimeStackTraceIsStillReadAsOne()
    {
        var parsed = new ParserRegistry().Parse(Fixtures.LoadStackTrace("java/caused-by.txt"), []);

        Assert.NotNull(parsed);
        Assert.Equal("java", parsed!.LanguageId);
        Assert.NotEqual("compile error", parsed.ExceptionType);
        Assert.True(parsed.Frames.Count > 1, "a stack trace has frames; a diagnostic has one");
    }
}
