using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// What Scala says when a program does not build, as Scala 3.8.4 and Scala 2.13.18 said it through Scala CLI 1.15.0 -
/// each sample copied from a real build, with only the folder the file was in changed.
/// </summary>
public class ScalaCompileParserTests
{
    private const string Folder = @"C:\coursework\scala\";

    private static IReadOnlyList<CapturedLine> Lines(string output) =>
        output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')
            .Select((text, index) => new CapturedLine(index, StreamKind.StdErr, text, TimeSpan.Zero))
            .ToList();

    [Fact]
    public void Scala3TypeMismatchesAreEachAnErrorWithTheirLineColumnAndCode()
    {
        var output = $"""
            -- [E007] Type Mismatch Error: {Folder}Total.scala:3:21
            3 |    val count: Int = "three"
              |                     ^^^^^^^
              |                     Found:    ("three" : String)
              |                     Required: Int
              |
              | longer explanation available when compiling with `-explain`
            -- [E007] Type Mismatch Error: {Folder}Total.scala:5:21
            5 |    val total: Int = count * price
              |                     ^^^^^^^^^^^^^
              |                     Found:    Double
              |                     Required: Int
              |
              | longer explanation available when compiling with `-explain`
            2 errors found
            Compilation failed
            """;

        var errors = new ScalaCompileParser().ParseAll(Lines(output));

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(("scala", "compile error", "E007"), (error.LanguageId, error.ExceptionType, error.ErrorCode)));

        Assert.Equal(Folder + "Total.scala", errors[0].Frames[0].File);
        Assert.Equal(3, errors[0].Frames[0].Line);
        Assert.Equal(22, errors[0].Frames[0].Column);
        Assert.Equal("Found:    (\"three\" : String)\nRequired: Int", errors[0].Message);

        Assert.Equal(5, errors[1].Frames[0].Line);
        Assert.Equal("Found:    Double\nRequired: Int", errors[1].Message);
    }

    [Fact]
    public void Scala3ErrorsWithoutACodeAndOverSeveralLinesOfCodeAreRead()
    {
        // Two $ signs, so the braces Scala shows of the code are the code's own.
        var output = $$"""
            -- [E040] Syntax Error: {{Folder}}Semicolon.scala:5:2
            5 |  }
              |  ^
              |  ')' expected, but '}' found
            -- [E008] Not Found Error: {{Folder}}Semicolon.scala:4:4
            3 |    val xs = List(1, 2
            4 |    println(xs)
              |                     ^
              |           value println is not a member of Int.
              |           Note that `println` is treated as an infix operator in Scala 3.
              |           If you do not want that, insert a `;` or empty line in front
              |           or drop any spaces behind the operator.
            -- Error: {{Folder}}Unclosed.scala:3:12
            3 |    println("hi)
              |            ^
              |            unclosed string literal
            3 errors found
            Compilation failed
            """;

        var errors = new ScalaCompileParser().ParseAll(Lines(output));

        Assert.Equal(3, errors.Count);
        Assert.Equal(("E040", "')' expected, but '}' found"), (errors[0].ErrorCode, errors[0].Message));
        Assert.StartsWith("value println is not a member of Int.\nNote that `println` is treated as an infix operator", errors[1].Message);
        Assert.Equal(4, errors[1].Frames[0].Line);
        Assert.Null(errors[2].ErrorCode);
        Assert.Equal(("unclosed string literal", 3, 13), (errors[2].Message, errors[2].Frames[0].Line, errors[2].Frames[0].Column));
    }

    [Fact]
    public void Scala2ErrorsKeepTheirLinesOfMessageAndTakeTheColumnFromTheCaret()
    {
        var output = $"""
            {Folder}Total.scala:3: error: type mismatch;
             found   : String("three")
             required: Int
                val count: Int = "three"
                                 ^
            {Folder}NotMember.scala:4: error: value lenght is not a member of List[String]
            did you mean length? or perhaps lengthIs?
                println(words.lenght)
                              ^
            {Folder}MissingArg.scala:5: error: not enough arguments for method add: (a: Int, b: Int): Int.
            Unspecified value parameter b.
                println(add(1))
                           ^
            3 errors
            Compilation failed
            """;

        var errors = new ScalaCompileParser().ParseAll(Lines(output));

        Assert.Equal(3, errors.Count);
        Assert.Equal("type mismatch;\nfound   : String(\"three\")\nrequired: Int", errors[0].Message);
        Assert.Equal((3, 22), (errors[0].Frames[0].Line!.Value, errors[0].Frames[0].Column!.Value));
        Assert.Equal("value lenght is not a member of List[String]\ndid you mean length? or perhaps lengthIs?", errors[1].Message);
        Assert.Equal(19, errors[1].Frames[0].Column);
        Assert.Equal("not enough arguments for method add: (a: Int, b: Int): Int.\nUnspecified value parameter b.", errors[2].Message);
        Assert.All(errors, error => Assert.Null(error.ErrorCode));
    }

    [Fact]
    public void WarningsAreReadApartFromErrorsInBothScalas()
    {
        var scala3 = $"""
            -- [E198] Unused Symbol Warning: {Folder}Unused.scala:2:20
            2 |  import scala.util.Random
              |                    ^^^^^^
              |                    unused import
            -- [E198] Unused Symbol Warning: {Folder}Unused.scala:4:8
            4 |    val spare = 3
              |        ^^^^^
              |        unused local definition
            2 warnings found
            """;

        var scala2 = $"""
            {Folder}Unused.scala:2: warning: Unused import
              import scala.util.Random
                                ^
            {Folder}Unused.scala:4: warning: local val spare in method main is never used
                val spare = 3
                    ^
            2 warnings
            """;

        Assert.Empty(new ScalaCompileParser().ParseAll(Lines(scala3)));
        Assert.Equal(new[] { "unused import", "unused local definition" }, ScalaCompileParser.ParseWarnings(Lines(scala3)).Select(warning => warning.Message));
        Assert.Equal(new[] { "Unused import", "local val spare in method main is never used" }, ScalaCompileParser.ParseWarnings(Lines(scala2)).Select(warning => warning.Message));
        Assert.All(ScalaCompileParser.ParseWarnings(Lines(scala2)), warning => Assert.Equal("compile warning", warning.ExceptionType));
    }

    [Fact]
    public void AScala2SummaryOfDeprecationsIsNotAWarningOfItsOwn()
    {
        // What Scala 2.13 says when it is not told to name each deprecation: a count, with no file or line to put it at.
        var output = """
            warning: 1 deprecation (since 2.13.0); re-run with -deprecation for details
            1 warning
            """;

        Assert.Empty(ScalaCompileParser.ParseWarnings(Lines(output)));
    }

    [Fact]
    public void WhatScalaCliCouldNotFindInItsCacheIsAMissingDownloadAtTheDirectiveThatAsksForIt()
    {
        // Scala CLI colours its own [error] tags even when its output is not a terminal's.
        var output = $"""
            [{"\u001b"}[31merror{"\u001b"}[0m] {Folder}UsesCats.scala:1:15
            [{"\u001b"}[31merror{"\u001b"}[0m] Error downloading org.typelevel:cats-core_3:2.10.0
            [{"\u001b"}[31merror{"\u001b"}[0m]   not found: C:\Users\student\AppData\Local\Coursier\cache\v1\https\repo1.maven.org\maven2\org\typelevel\cats-core_3\2.10.0\cats-core_3-2.10.0.pom
            [{"\u001b"}[31merror{"\u001b"}[0m] //> using dep org.typelevel::cats-core:2.10.0
            """;

        var error = Assert.Single(new ScalaCompileParser().ParseAll(Lines(output)));

        Assert.Equal(ScalaCompileParser.MissingDownload, error.ExceptionType);
        Assert.Equal("Error downloading org.typelevel:cats-core_3:2.10.0", error.Message);
        Assert.Equal((Folder + "UsesCats.scala", 1, 15), (error.Frames[0].File, error.Frames[0].Line!.Value, error.Frames[0].Column!.Value));
        Assert.DoesNotContain('\u001b', error.RawText);
    }

    [Theory]
    [InlineData("[error]  No main class found", "No main class found")]
    [InlineData("[error]  Found several main classes: First, Second", "Found several main classes: First, Second")]
    [InlineData("[error]  Error downloading org.scala-lang:scala3-compiler_3:3.3.1", "Error downloading org.scala-lang:scala3-compiler_3:3.3.1")]
    public void ScalaCliSaysItsOwnProblemsWithNoPlaceInTheCode(string said, string message)
    {
        var error = Assert.Single(new ScalaCompileParser().ParseAll(Lines(said)));

        Assert.Equal(message, error.Message);
        Assert.Empty(error.Frames);
    }

    [Fact]
    public void TheRegistryPicksScalasParserForScalasOutputAndNotJavasForItsCrash()
    {
        var compile = Lines($"""
            -- [E006] Not Found Error: {Folder}Hello.scala:3:2
            3 |  prinln("Hello, " + name)
              |  ^^^^^^
              |  Not found: prinln - did you mean println?
            1 error found
            Compilation failed
            """);

        var crash = Lines("""
            Exception in thread "main" java.lang.ArithmeticException: / by zero
            	at Marks$.average(Marks.scala:2)
            	at Marks$.main(Marks.scala:6)
            	at Marks.main(Marks.scala)
            """);

        var built = new ParserRegistry().Parse(compile);
        Assert.NotNull(built);
        Assert.Equal(("scala", "compile error"), (built.LanguageId, built.ExceptionType));

        var crashed = new ParserRegistry().Parse(crash);
        Assert.NotNull(crashed);
        Assert.Equal(("scala", "java.lang.ArithmeticException", "/ by zero"), (crashed.LanguageId, crashed.ExceptionType, crashed.Message));
    }
}
