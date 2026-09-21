using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>The parsers for PHP, PowerShell, Dart, Elixir, Perl and Lua.</summary>
public class OtherLanguageParserTests
{
    private static readonly ParserRegistry Registry = new();

    private static ParsedError Parse(string fixture)
    {
        var parsed = Registry.Parse(Fixtures.LoadStackTrace(fixture));
        Assert.NotNull(parsed);
        return parsed;
    }

    [Fact]
    public void PhpReadsAnUncaughtException()
    {
        var error = Parse("php/uncaught-typeerror.txt");

        Assert.Equal("TypeError", error.ExceptionType);
        Assert.Equal("Unsupported operand types: string + int", error.Message);
        Assert.Equal("/var/www/app/Calculator.php", error.Frames[0].File);
        Assert.Equal(18, error.Frames[0].Line);
    }

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

    [Fact]
    public void PhpReadsAParseErrorsLocationWithoutTheWordsAroundIt()
    {
        var error = Parse("php/parse-error.txt");

        Assert.Equal("ParseError", error.ExceptionType);
        Assert.Equal("/var/www/app/Router.php", error.Frames[0].File);
        Assert.Equal(57, error.Frames[0].Line);
    }

    [Fact]
    public void PhpReadsTheUnprefixedForm()
    {
        var error = Parse("php/display-errors.txt");

        Assert.Equal("Error", error.ExceptionType);
        Assert.Contains("undefined function", error.Message!, StringComparison.Ordinal);
        Assert.Equal(31, error.Frames[0].Line);
    }

    [Fact]
    public void PowerShellKeepsTheErrorIdAsACode()
    {
        var error = Parse("powershell/cmdlet-error.txt");

        Assert.Equal("PathNotFound", error.ErrorCode);
        Assert.Equal("ItemNotFoundException", error.ExceptionType);
        Assert.Equal(@"C:\scripts\Import-Report.ps1", error.Frames[0].File);
        Assert.Equal(12, error.Frames[0].Line);
    }

    [Fact]
    public void PowerShellDoesNotMistakePriorOutputForTheMessage()
    {
        var error = Parse("powershell/after-output.txt");

        Assert.StartsWith("A parameter cannot be found", error.Message!, StringComparison.Ordinal);
        Assert.Equal("NamedParameterNotFound", error.ErrorCode);
        Assert.Equal(20, error.Frames[0].Line);
    }

    [Fact]
    public void DartKeepsTheSubtypeWithTheType()
    {
        var error = Parse("dart/rangeerror.txt");

        Assert.Equal("RangeError (index)", error.ExceptionType);
        Assert.StartsWith("Invalid value", error.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void DartKeepsSdkAndPackageFramesInOrder()
    {
        var error = Parse("dart/rangeerror.txt");

        Assert.Equal(4, error.Frames.Count);
        Assert.Equal("Basket.itemAt", error.Frames[1].Symbol);
        Assert.Equal("main", error.Frames[2].Symbol);
        Assert.Equal(12, error.Frames[2].Line);
    }

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

    [Fact]
    public void ElixirKeepsTheErlangBuiltInThatActuallyFailed()
    {
        var error = Parse("elixir/arithmetic.txt");

        Assert.Equal(":erlang./", error.Frames[0].Symbol);
        Assert.Null(error.Frames[0].File);
    }

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

    [Fact]
    public void PerlDoesNotInventAnExceptionType() =>
        Assert.Null(Parse("perl/carp.txt").ExceptionType);

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

    [Fact]
    public void LuaDoesNotClaimABareFileLineMessage()
    {
        var parsed = Registry.Parse(Fixtures.FromText("config.lua:14: attempt to index a nil value"));

        Assert.NotEqual("lua", parsed?.LanguageId);
    }

    [Theory]
    [InlineData("php/live-typeerror.txt", "php")]
    [InlineData("powershell/live-pathnotfound.txt", "powershell")]
    [InlineData("dart/live-rangeerror.txt", "dart")]
    [InlineData("elixir/live-arithmetic.txt", "elixir")]
    [InlineData("perl/live-method.txt", "perl")]
    [InlineData("lua/live-nil-index.txt", "lua")]
    public void RealCapturedOutputRoutesToTheRightParser(string fixture, string expected) =>
        Assert.Equal(expected, Parse(fixture).LanguageId);

    [Fact]
    public void AWrappedPowerShellLocationIsPutBackTogether()
    {
        var error = Parse("powershell/live-pathnotfound.txt");

        Assert.Equal("PathNotFound", error.ErrorCode);
        Assert.StartsWith("Cannot find path", error.Message!, StringComparison.Ordinal);
        Assert.Single(error.Frames);
        Assert.EndsWith("crash.ps1", error.Frames[0].File!, StringComparison.Ordinal);
        Assert.Equal(2, error.Frames[0].Line);
    }

    [Fact]
    public void DartFramesWithoutALineNumberDoNotStopTheTrace()
    {
        var error = Parse("dart/live-rangeerror.txt");

        Assert.Equal(4, error.Frames.Count);
        Assert.Null(error.Frames[0].Line);

        var mine = error.Frames.First(frame => frame.File?.EndsWith("crash.dart", StringComparison.Ordinal) == true);
        Assert.Equal(4, mine.Line);
    }

    [Fact]
    public void LuaIsRecognisedWhenItNamesItselfByFullPath()
    {
        var error = Parse("lua/live-nil-index.txt");

        Assert.Equal("lua", error.LanguageId);
        Assert.Equal("attempt to index a nil value (local 'settings')", error.Message);
        Assert.Equal("crash.lua", error.Frames[0].File);
        Assert.Equal(3, error.Frames[0].Line);
    }

    [Fact]
    public void PhpIsStillFoundWhenItWritesToStandardOutput()
    {
        var parsed = Registry.Parse(Fixtures.LoadSplit("php/live-typeerror.txt", _ => true));

        Assert.NotNull(parsed);
        Assert.Equal("php", parsed!.LanguageId);
        Assert.Equal("TypeError", parsed.ExceptionType);
        Assert.Equal("Unsupported operand types: string + int", parsed.Message);
    }

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
            .GroupBy(parser => parser.LanguageId)
            .ToDictionary(group => group.Key, group => group.Max(parser => parser.Detect(text)));

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
