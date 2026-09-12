using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// The six languages added after the original nine: PHP, PowerShell, Dart, Elixir, Perl and Lua.
/// </summary>
/// <remarks>
/// None of these runtimes is installed on the machine this was written on, so every case here runs
/// against captured output rather than a live crash. That is the same standard the other six
/// absent runtimes were held to - the fixture is the test - and it is why each one is a real
/// transcript rather than a line invented to match the pattern.
/// </remarks>
public class NewLanguageParserTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed;
    }

    // ------------------------------------------------------------------ php

    /// <summary>The uncaught form, where the location is written <c>file:line</c>.</summary>
    [Fact]
    public void PhpReadsAnUncaughtException()
    {
        var error = Parse("php/uncaught-typeerror.txt");

        Assert.Equal("TypeError", error.ExceptionType);
        Assert.Equal("Unsupported operand types: string + int", error.Message);
        Assert.Equal("/var/www/app/Calculator.php", error.Frames[0].File);
        Assert.Equal(18, error.Frames[0].Line);
    }

    /// <summary>
    /// The trace under it, and the <c>{main}</c> sentinel treated as an end rather than a frame.
    /// </summary>
    [Fact]
    public void PhpReadsTheStackTraceUnderIt()
    {
        var error = Parse("php/uncaught-typeerror.txt");

        Assert.Equal(3, error.Frames.Count);
        Assert.Equal("/var/www/app/Report.php", error.Frames[1].File);
        Assert.Equal(42, error.Frames[1].Line);
        Assert.Equal("/var/www/public/index.php", error.Frames[2].File);
        Assert.DoesNotContain(error.Frames, frame => frame.Symbol?.Contains("main}", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// The other location form, which is the one that breaks a parser that only knows the first.
    /// </summary>
    /// <remarks>
    /// Reading <c>in /var/www/app/Router.php on line 57</c> with the <c>file:line</c> pattern
    /// yields a file path with " on line 57" stuck to the end of it, which then resolves to
    /// nothing - so the crash is reported with no location at all.
    /// </remarks>
    [Fact]
    public void PhpReadsAParseErrorsLocationWithoutTheWordsAroundIt()
    {
        var error = Parse("php/parse-error.txt");

        Assert.Equal("ParseError", error.ExceptionType);
        Assert.Equal("/var/www/app/Router.php", error.Frames[0].File);
        Assert.Equal(57, error.Frames[0].Line);
    }

    /// <summary>Without the <c>PHP </c> prefix, which is how a web server shows it.</summary>
    [Fact]
    public void PhpReadsTheUnprefixedForm()
    {
        var error = Parse("php/display-errors.txt");

        Assert.Equal("Error", error.ExceptionType);
        Assert.Contains("undefined function", error.Message!, StringComparison.Ordinal);
        Assert.Equal(31, error.Frames[0].Line);
    }

    // ------------------------------------------------------------------ powershell

    /// <summary>
    /// The FullyQualifiedErrorId, which is the best search term PowerShell gives you.
    /// </summary>
    /// <remarks>
    /// The message is full of the user's own paths and searches badly; <c>PathNotFound</c> is
    /// stable across every machine that ever hit this. That is the same argument that makes MSVC's
    /// <c>C2065</c> a first-class field, so it lands in the same place.
    /// </remarks>
    [Fact]
    public void PowerShellKeepsTheErrorIdAsACode()
    {
        var error = Parse("powershell/cmdlet-error.txt");

        Assert.Equal("PathNotFound", error.ErrorCode);
        Assert.Equal("ItemNotFoundException", error.ExceptionType);
        Assert.Equal(@"C:\scripts\Import-Report.ps1", error.Frames[0].File);
        Assert.Equal(12, error.Frames[0].Line);
    }

    /// <summary>
    /// The message is the record's own first line, not whatever the script printed before it.
    /// </summary>
    /// <remarks>
    /// The walk up out of the record stops at the first line that is not part of it. An
    /// off-by-one here does not fail loudly - it reports "Reading configuration" as the error,
    /// which looks like an answer and sends the whole search somewhere unrelated.
    /// </remarks>
    [Fact]
    public void PowerShellDoesNotMistakePriorOutputForTheMessage()
    {
        var error = Parse("powershell/after-output.txt");

        Assert.StartsWith("A parameter cannot be found", error.Message!, StringComparison.Ordinal);
        Assert.Equal("NamedParameterNotFound", error.ErrorCode);
        Assert.Equal(20, error.Frames[0].Line);
    }

    // ------------------------------------------------------------------ dart

    /// <summary>The parenthesised subtype belongs to the type, not the message.</summary>
    [Fact]
    public void DartKeepsTheSubtypeWithTheType()
    {
        var error = Parse("dart/rangeerror.txt");

        Assert.Equal("RangeError (index)", error.ExceptionType);
        Assert.StartsWith("Invalid value", error.Message!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every frame is kept, including the ones that name no file on this disk.
    /// </summary>
    /// <remarks>
    /// Only the <c>file:///</c> frame can be opened. Dropping the <c>dart:</c> and
    /// <c>package:</c> ones would renumber the trace and lose the ordering that says which call
    /// led to which.
    /// </remarks>
    [Fact]
    public void DartKeepsSdkAndPackageFramesInOrder()
    {
        var error = Parse("dart/rangeerror.txt");

        Assert.Equal(4, error.Frames.Count);
        Assert.Equal("Basket.itemAt", error.Frames[1].Symbol);
        Assert.Equal("main", error.Frames[2].Symbol);
        Assert.Equal(12, error.Frames[2].Line);
    }

    // ------------------------------------------------------------------ elixir

    [Fact]
    public void ElixirReadsTheTypeAndTheApplicationFrames()
    {
        var error = Parse("elixir/arithmetic.txt");

        Assert.Equal("ArithmeticError", error.ExceptionType);
        Assert.Equal("bad argument in arithmetic expression", error.Message);
        Assert.Equal("lib/my_app/ledger.ex", error.Frames[1].File);
        Assert.Equal(42, error.Frames[1].Line);
        Assert.Equal("my_app 0.1.0", error.Frames[1].Module);
    }

    /// <summary>
    /// The Erlang built-in has no file, and is the frame that actually failed.
    /// </summary>
    /// <remarks>
    /// <c>:erlang./(1, 0)</c> is the division that raised. A parser that only took lines with a
    /// <c>file:line</c> on them would drop it and report the caller as the culprit instead.
    /// </remarks>
    [Fact]
    public void ElixirKeepsTheErlangBuiltInThatActuallyFailed()
    {
        var error = Parse("elixir/arithmetic.txt");

        Assert.Equal(":erlang./", error.Frames[0].Symbol);
        Assert.Null(error.Frames[0].File);
    }

    // ------------------------------------------------------------------ perl

    [Fact]
    public void PerlReadsTheDieLineAndItsCallers()
    {
        var error = Parse("perl/carp.txt");

        Assert.StartsWith("Can't locate object method", error.Message!, StringComparison.Ordinal);
        Assert.Equal("/opt/build/lib/Builder.pm", error.Frames[0].File);
        Assert.Equal(214, error.Frames[0].Line);
        Assert.Equal(3, error.Frames.Count);
        Assert.Equal("main::run", error.Frames[2].Symbol);
    }

    /// <summary>
    /// Perl invents no exception type, because it has none.
    /// </summary>
    /// <remarks>
    /// A made-up type would be carried into the search query as a term that appears in nobody's
    /// answer, which is worse than the field being empty.
    /// </remarks>
    [Fact]
    public void PerlDoesNotInventAnExceptionType() =>
        Assert.Null(Parse("perl/carp.txt").ExceptionType);

    // ------------------------------------------------------------------ lua

    [Fact]
    public void LuaReadsTheErrorAndItsTraceback()
    {
        var error = Parse("lua/nil-index.txt");

        Assert.Equal("attempt to index a nil value (global 'settings')", error.Message);
        Assert.Equal("config.lua", error.Frames[0].File);
        Assert.Equal(14, error.Frames[0].Line);
        Assert.Contains(error.Frames, frame => frame.Symbol == "main chunk");
        Assert.Contains(error.Frames, frame => frame.Symbol == "function 'load_settings'");
    }

    /// <summary>
    /// Without the traceback banner, Lua's error line is not claimed at all.
    /// </summary>
    /// <remarks>
    /// <c>file:line: message</c> is the same shape as a gcc diagnostic and as half the log lines
    /// ever formatted. Claiming it on sight would route C compile errors to a Lua parser, so the
    /// banner is required and the generic parser handles the rest.
    /// </remarks>
    [Fact]
    public void LuaDoesNotClaimABareFileLineMessage()
    {
        var parsed = Registry.Parse(Fixtures.FromText("config.lua:14: attempt to index a nil value"));

        Assert.NotEqual("lua", parsed?.LanguageId);
    }

    // ------------------------------------------------------------------ they stay out of each other's way

    /// <summary>
    /// Every fixture's own parser must beat every other specific parser on it, outright.
    /// </summary>
    /// <remarks>
    /// This is the test the original plan called for and never got, and it matters far more at
    /// fifteen parsers than it did at nine: PHP and Dart both number their frames <c>#0</c>, Lua
    /// and gcc share <c>file:line: message</c>, and Perl's <c>at FILE line N</c> is loose enough
    /// to match ordinary prose. A tie is treated as a failure - the registry only tries the top
    /// three, so a parser that draws with the right one is a parser that can displace it on a
    /// slightly different transcript.
    /// <para>
    /// The generic parser is excluded because it is meant to score on everything; that is its job.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("php/uncaught-typeerror.txt", "php")]
    [InlineData("powershell/cmdlet-error.txt", "powershell")]
    [InlineData("dart/rangeerror.txt", "dart")]
    [InlineData("elixir/arithmetic.txt", "elixir")]
    [InlineData("perl/carp.txt", "perl")]
    [InlineData("lua/nil-index.txt", "lua")]
    [InlineData("python/keyerror.txt", "python")]
    [InlineData("go/panic.txt", "go")]
    [InlineData("java/caused-by.txt", "java")]
    [InlineData("csharp/inner-exception.txt", "csharp")]
    [InlineData("ruby/zerodivision.txt", "ruby")]
    [InlineData("node/typeerror.txt", "node")]
    public void NoParserOutscoresTheRightOneOnItsOwnLanguage(string fixture, string expected)
    {
        var text = Fixtures.LoadStackTrace(fixture).Select(line => line.Text).ToArray();

        var scores = Registry.Parsers
            .Where(parser => parser.LanguageId != "generic")
            .ToDictionary(parser => parser.LanguageId, parser => parser.Detect(text));

        var winner = scores[expected];

        foreach (var (language, score) in scores)
        {
            if (language == expected) continue;

            Assert.True(
                score < winner,
                $"{fixture}: {language} scored {score} against {expected}'s {winner}");
        }
    }
}
