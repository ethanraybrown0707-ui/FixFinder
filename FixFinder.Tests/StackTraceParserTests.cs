using FixFinder.Core.Parsing;
using FixFinder.Core.Fingerprinting;

namespace FixFinder.Tests;

/// <summary>
/// Drives every parser through <see cref="ParserRegistry"/> against captured crash output, so
/// the tests cover detection and parsing together - picking the wrong parser is just as much a
/// failure as parsing badly.
/// </summary>
public class StackTraceParserTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed;
    }

    // ------------------------------------------------------------------ language routing

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

    // ------------------------------------------------------------------ .NET

    /// <summary>
    /// The chain is the whole point of the .NET parser: frames printed before the
    /// end-of-inner marker belong to the inner exception, not the wrapper.
    /// </summary>
    // ------------------------------------------------------------------ Python, unparseable

    /// <summary>
    /// A file that will not parse prints no traceback at all, and used to be lost entirely.
    /// </summary>
    /// <remarks>
    /// There is no call stack because nothing was ever called, so the "Traceback (most recent
    /// call last):" header this parser anchors on is simply absent. The generic reader picked it
    /// up instead and kept only the message: no type, no file, no line, confidence 20. For what
    /// is probably the most common error anybody writing Python ever meets, the search query came
    /// out as three loose words and matched strangers' unrelated questions.
    /// </remarks>
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

    /// <summary>IndentationError and TabError are SyntaxError, and print the same shape.</summary>
    [Fact]
    public void Python_AnIndentationErrorIsReadTheSameWay()
    {
        var error = Parse("python/indentation-error.txt");

        Assert.Equal("IndentationError", error.ExceptionType);
        Assert.Equal("unexpected indent", error.Message);
        Assert.Equal(12, Assert.Single(error.Frames).Line);
    }

    /// <summary>
    /// The file is taken from just above the error, not from anywhere in the output.
    /// </summary>
    /// <remarks>
    /// Programs print before they die. Pairing an error with the first file name anywhere in the
    /// output would attribute a syntax error to whatever a log line happened to mention.
    /// </remarks>
    [Fact]
    public void Python_ASyntaxErrorIsFoundAfterOrdinaryOutput()
    {
        var error = Parse("python/syntax-error-after-output.txt");

        Assert.Equal("SyntaxError", error.ExceptionType);
        Assert.StartsWith("invalid syntax.", error.Message!, StringComparison.Ordinal);
        Assert.EndsWith("worker.py", Assert.Single(error.Frames).File!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A syntax error is in the file being read, by definition - so it is always your own.
    /// </summary>
    /// <remarks>
    /// This is the part that makes the answer useful. With a first-party culprit FixFinder says
    /// the true and helpful thing - no published issue exists for a typo only your file has - 
    /// instead of presenting the best of forty weak matches as though it were relevant.
    /// </remarks>
    [Fact]
    public void Python_ASyntaxErrorIsAlwaysFirstParty()
    {
        var error = Parse("python/syntax-error.txt");
        var fingerprint = FingerprintBuilder.Build(error);

        Assert.True(fingerprint.CulpritIsFirstParty);
        Assert.Equal("SyntaxError", fingerprint.ShortExceptionType);

        // The type has to reach the query, or the search is three loose words again.
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

        // Two frames before the marker belong to the inner exception; two after belong to
        // the wrapper. Attributing all four to either one is the classic mistake.
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

    /// <summary>
    /// Frames after the async resume boundary are the awaiting caller's own code. They must
    /// survive as ordinary frames, or the culprit lands in HttpConnectionPool instead of in
    /// the user's FeedClient.
    /// </summary>
    [Fact]
    public void DotNet_KeepsFramesAfterTheAsyncResumeBoundary()
    {
        var error = Parse("csharp/async-previous-location.txt");

        Assert.Equal(3, error.Frames.Count);
        Assert.Contains(error.Frames, f => f.File is not null && f.File.EndsWith("FeedClient.cs"));
    }

    // ------------------------------------------------------------------ Python

    /// <summary>
    /// Python prints frames outermost-first. If the reversal is ever dropped, this test fails
    /// with the culprit sitting on <c>&lt;module&gt;</c> - the entry point - on every crash.
    /// </summary>
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

        // Only "File ..." lines are frames. The echoed source and the 3.11+ ~~~^^^ pointer
        // sit between them and must not be counted.
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

    /// <summary>4300 lines of ordinary output before the traceback must not hide it.</summary>
    [Fact]
    public void Python_FindsATracebackAfterThousandsOfNoiseLines()
    {
        var error = Parse("adversarial/noise-then-traceback.txt");

        Assert.Equal("sqlite3.OperationalError", error.ExceptionType);
        Assert.Equal("OperationalError", error.ShortExceptionType);
    }

    /// <summary>
    /// When a program recovers from one error and then dies of another, the fatal one is last -
    /// and searching for the recovered one would be searching for a problem already solved.
    /// </summary>
    [Fact]
    public void Python_PicksTheLastTracebackWhenTwoAreUnrelated()
    {
        var error = Parse("adversarial/two-python-tracebacks.txt");

        Assert.Equal("ZeroDivisionError", error.ExceptionType);
    }

    // ------------------------------------------------------------------ Node

    [Fact]
    public void Node_ReadsFramesWithColumnsAndMarksRuntimeInternals()
    {
        var error = Parse("node/typeerror.txt");

        Assert.Equal("TypeError", error.ExceptionType);
        Assert.Equal(4, error.Frames.Count);
        Assert.Equal("renderUser", error.Frames[0].Symbol);
        Assert.Equal(12, error.Frames[0].Line);
        Assert.Equal(29, error.Frames[0].Column);

        // "at async Module._load (node:internal/...)" - both the async keyword and the
        // node: pseudo-path have to be handled.
        Assert.Equal(FrameOrigin.Runtime, error.Frames[^1].Origin);
    }

    [Fact]
    public void Node_ClassifiesNodeModulesAsThirdParty()
    {
        var error = Parse("node/typeerror.txt");

        Assert.Contains(error.Frames, f => f.Origin == FrameOrigin.ThirdParty);
        Assert.Equal("express", CulpritFrameSelector.NearestThirdPartyModule(error));
    }

    // ------------------------------------------------------------------ Java

    [Fact]
    public void Java_ReadsCausedByAndRecordsElidedFrames()
    {
        var error = Parse("java/caused-by.txt");

        Assert.Equal("java.lang.IllegalStateException", error.ExceptionType);
        Assert.Equal("java.io.IOException", error.RootCause.ExceptionType);

        // "... 3 more" is kept as a visible marker rather than dropped silently.
        Assert.Contains(error.RootCause.Frames, f => f.Symbol is not null && f.Symbol.Contains("3 frames identical"));
    }

    /// <summary>Java frames carry a bare file name, which later stages must not mistake for a path.</summary>
    [Fact]
    public void Java_RecordsFileNamesWithoutDirectories()
    {
        var error = Parse("java/caused-by.txt");

        Assert.Equal("Config.java", error.RootCause.Frames[1].File);
        Assert.Equal(27, error.RootCause.Frames[1].Line);
    }

    // ------------------------------------------------------------------ Go

    /// <summary>Go frames span two lines; a one-line parser finds nothing at all here.</summary>
    [Fact]
    public void Go_PairsSymbolAndLocationLinesIntoOneFrame()
    {
        var error = Parse("go/panic.txt");

        Assert.Equal(2, error.Frames.Count);
        Assert.Equal("main.process", error.Frames[0].Symbol);
        Assert.Equal(14, error.Frames[0].Line);
        Assert.EndsWith("main.go", error.Frames[0].File);
    }

    /// <summary>
    /// A panic dumps every live goroutine. Only the first is the failure; the rest are
    /// unrelated stacks that would poison both the frame list and the search query.
    /// </summary>
    [Fact]
    public void Go_ParsesOnlyTheFirstGoroutineBlock()
    {
        var error = Parse("go/panic.txt");

        Assert.DoesNotContain(error.Frames, f => f.Symbol is not null && f.Symbol.Contains("persistConn"));
    }

    // ------------------------------------------------------------------ Rust

    [Fact]
    public void Rust_ModernFormatTakesTheMessageFromTheFollowingLine()
    {
        var error = Parse("rust/modern-with-backtrace.txt");

        Assert.Equal("index out of bounds: the len is 3 but the index is 10", error.Message);
        Assert.Equal("main.rs:4", error.Frames[0].Location);
        Assert.True(error.Frames.Count > 1, "the backtrace frames should have been read");
    }

    /// <summary>Pre-1.72 output quotes the message inline; still very much in the wild.</summary>
    [Fact]
    public void Rust_LegacyFormatTakesTheMessageFromTheHeaderLine()
    {
        var error = Parse("rust/legacy-no-backtrace.txt");

        Assert.Contains("Option::unwrap()", error.Message);
        Assert.Equal("config.rs:17", error.Frames[0].Location);
    }

    // ------------------------------------------------------------------ gcc / MSVC

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

    /// <summary>
    /// A bare segfault has no detail at all, so it is reported with low confidence rather than
    /// not reported - "a crash we cannot locate" beats silence.
    /// </summary>
    [Fact]
    public void Gcc_ReportsABareSegfaultWithLowConfidence()
    {
        var error = Parse("gcc/segfault.txt");

        Assert.Contains("Segmentation fault", error.ExceptionType);
        Assert.Empty(error.Frames);
        Assert.InRange(error.Confidence, 1, 60);
    }

    /// <summary>The compiler error code is the best search term available, so it is lifted out.</summary>
    [Fact]
    public void Msvc_LiftsTheErrorCodeIntoItsOwnField()
    {
        var error = Parse("msvc/cs0103.txt");

        Assert.Equal("CS0103", error.ErrorCode);
        Assert.Equal(12, error.Frames[0].Line);
        Assert.Equal(17, error.Frames[0].Column);
        Assert.DoesNotContain("MyApp.csproj", error.Message);
    }

    // ------------------------------------------------------------------ Ruby

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

    // ------------------------------------------------------------------ generic fallback

    /// <summary>
    /// Perl has no parser here, which is exactly the point: the fallback still finds the file
    /// and line, so "works with any language" holds.
    /// </summary>
    [Fact]
    public void Generic_HarvestsFileAndLineFromAnUnsupportedLanguage()
    {
        var error = Parse("generic/perl.txt");

        Assert.Equal("generic", error.LanguageId);
        Assert.Null(error.ExceptionType);
        Assert.Contains(error.Frames, f => f.File == "lib/Builder.pm" && f.Line == 214);
    }

    /// <summary>
    /// The fallback matches something in almost any noisy output, so its score must stay below
    /// every real parser's. Otherwise a log line mentioning "error" outranks a real traceback.
    /// </summary>
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

    /// <summary>
    /// Cross-language false positives are the failure mode score-based selection exists to
    /// prevent, so no specific parser may be confident about another language's output.
    /// </summary>
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

    // ------------------------------------------------------------------ registry behaviour

    /// <summary>
    /// stdout is full of things that look like errors. Restricting the first pass to stderr is
    /// what stops a log line quoting a traceback from being treated as this run's crash.
    /// </summary>
    [Fact]
    public void PrefersTheErrorOnStdErrOverALookalikeOnStdOut()
    {
        // The program logs a failure it recovered from to stdout, then dies of something else.
        // Reading stdout would send the whole search after a problem already solved.
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

    /// <summary>
    /// The other half of the stderr-first rule: a program that writes its crash to stdout must
    /// still be read, rather than falling through to a vague generic match on stderr alone.
    /// </summary>
    [Fact]
    public void StillFindsATraceThatWasWrittenEntirelyToStdOut()
    {
        var lines = Fixtures.LoadSplit("python/keyerror.txt", stdOutPredicate: _ => true);

        var parsed = Registry.Parse(lines);

        Assert.NotNull(parsed);
        Assert.Equal("python", parsed.LanguageId);
        Assert.Equal("KeyError", parsed.ExceptionType);
    }

    /// <summary>
    /// Regression guard for a real bug: the fallback used to take the last line containing a
    /// severity keyword, which on this fixture is the bland "build aborted" closing line - so
    /// it reported that and discarded the only file and line in the whole run.
    /// </summary>
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

    /// <summary>
    /// An exception type that does not end in Error or Exception is still an exception.
    /// </summary>
    /// <remarks>
    /// Found by pointing FixFinder at a crash inside requests. The terminator pattern used to
    /// demand one of a handful of suffixes, so <c>requests.exceptions.InvalidSchema</c> was not
    /// recognised: the frames parsed, the type and message were dropped, and the search query
    /// came out empty - failing hardest on exactly the third-party errors the tool is best at.
    /// </remarks>
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

    /// <summary>A traceback's own log noise must still not be mistaken for the terminator.</summary>
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

    /// <summary>
    /// A javac diagnostic is a Java error, and belongs to the Java parser.
    /// </summary>
    /// <remarks>
    /// It has a line number and no column, so the gcc/clang parser - which requires
    /// <c>file:line:col:</c> - does not match it. Before this it fell through to the generic
    /// fallback and produced a query of loose words with none of the error text in it, which is
    /// a poor showing for the commonest javac error there is.
    /// </remarks>
    [Fact]
    public void AJavacDiagnosticIsReadByTheJavaParser()
    {
        var parsed = new ParserRegistry().Parse(
            Fixtures.LoadStackTrace("java/javac-cannot-find-symbol.txt"), []);

        Assert.NotNull(parsed);
        Assert.Equal("java", parsed!.LanguageId);
        Assert.Equal("compile error", parsed.ExceptionType);
        Assert.Contains("cannot find symbol", parsed.Message!, StringComparison.Ordinal);

        // The symbol javac names is the most useful term in the whole diagnostic.
        Assert.Contains("variable avg", parsed.Message!, StringComparison.Ordinal);
        Assert.Contains("class Main", parsed.Message!, StringComparison.Ordinal);

        var frame = Assert.Single(parsed.Frames);
        Assert.Equal(18, frame.Line);
        Assert.EndsWith("Main.java", frame.File!, StringComparison.Ordinal);
    }

    /// <summary>
    /// An absolute Windows path must not break the match.
    /// </summary>
    /// <remarks>
    /// The bug this was written for. javac echoes the path exactly as given, so on Windows the
    /// line begins <c>C:\src\Main.java:18:</c> - and a pattern that assumes the first colon is
    /// the one before the line number matches nothing at all.
    /// </remarks>
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

    /// <summary>A javac error must not be mistaken for a stack trace, or the reverse.</summary>
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
