using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>The C rules against gcc as MinGW really prints it on Windows, fix-its included.</summary>
public class GccLocalFixTests : IDisposable
{
    private const string BoolSource =
        "#include <stdio.h>\nint main(void) {\n    bool ready = true;\n    printf(\"%d\\n\", ready);\n    return 0;\n}\n";

    private const string MemberSource =
        "#include <stdio.h>\nstruct parcel { int weight; };\nint main(void) {\n    struct parcel p = { 4 };\n    printf(\"%d\\n\", p.wieght);\n    return 0;\n}\n";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private (ParsedError Error, LocalFixContext Context) Gcc(string fixture, string source)
    {
        var file = Path.Combine(_temp.Path, "app.c");
        File.WriteAllText(file, source);

        var text = File.ReadAllText(Path.Combine(Fixtures.Root, "StackTraces", "gcc", fixture))
            .Replace("{FILE_ESCAPED}", file.Replace("\\", "\\\\"), StringComparison.Ordinal)
            .Replace("{FILE_FORWARD}", file.Replace('\\', '/'), StringComparison.Ordinal)
            .Replace("{FILE}", file, StringComparison.Ordinal);

        var output = text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')
            .Select((line, i) => new CapturedLine(i, StreamKind.StdErr, line, TimeSpan.Zero))
            .ToList();

        var error = new ParserRegistry().Parse(output);
        Assert.NotNull(error);

        return (error!, new LocalFixContext { Error = error!, Output = output, SourceRoot = _temp.Path, FromBuild = true });
    }

    [Fact]
    public void GccIsReadWhenThePathHasADriveLetter()
    {
        var (error, _) = Gcc("live-mingw-member-typo.txt", MemberSource);

        Assert.Equal("gcc", error.LanguageId);
        Assert.Equal("'struct parcel' has no member named 'wieght'; did you mean 'weight'?", error.Message);
        Assert.Equal(5, error.Frames[0].Line);
    }

    [Fact]
    public void GccsOwnFixItAddsTheHeaderItNamed()
    {
        var (_, context) = Gcc("live-mingw-bool-noinclude.txt", BoolSource);

        var fix = new CompilerFixIt().Propose(context)!;

        Assert.Equal(2, fix.StartLine);
        Assert.Equal(["#include <stdbool.h>", "int main(void) {"], fix.NewLines);
    }

    [Fact]
    public void GccsFixItCorrectsTheMember()
    {
        var (_, context) = Gcc("live-mingw-member-typo.txt", MemberSource);

        var fix = new CompilerFixIt().Propose(context)!;

        Assert.Equal("Change wieght to weight", fix.Title);
        Assert.Equal(["    printf(\"%d\\n\", p.weight);"], fix.NewLines);
    }

    [Fact]
    public void AMisspeltCallIsReadFromTheLinkerAndFixedFromTheWarning()
    {
        var (error, context) = Gcc(
            "live-mingw-function-typo.txt",
            "#include <stdio.h>\nint main(void) {\n    prinft(\"hello\\n\");\n    return 0;\n}\n");

        Assert.Equal("link error", error.ExceptionType);
        Assert.Equal("undefined reference to 'prinft'", error.Message);
        Assert.Equal(3, error.Frames[0].Line);

        Assert.Equal(["    printf(\"hello\\n\");"], new CompilerFixIt().Propose(context)!.NewLines);
    }

    [Fact]
    public void GccsMissingSemicolonGoesOnThePreviousStatement()
    {
        var (_, context) = Gcc(
            "live-mingw-missing-semicolon.txt",
            "#include <stdio.h>\nint main(void) {\n    int x = 3\n    printf(\"%d\\n\", x);\n    return 0;\n}\n");

        var fix = new CMissingSemicolon().Propose(context)!;

        Assert.Equal(3, fix.StartLine);
        Assert.Equal("    int x = 3;", fix.NewLines[0]);
    }

    [Fact]
    public void GccsEndOfInputClosesTheBlockThatWasOpened()
    {
        var (_, context) = Gcc(
            "live-mingw-missing-brace.txt",
            "#include <stdio.h>\nint main(void) {\n    printf(\"hello\\n\");\n    return 0;\n");

        var fix = new CMissingClosingBrace().Propose(context)!;

        Assert.Equal("Close the block opened on line 2", fix.Title);
        Assert.Equal(["}"], fix.NewLines);
    }

    [Fact]
    public void GccsMissingHeaderIsCorrectedToTheStandardOne()
    {
        var (_, context) = Gcc(
            "live-mingw-header-typo.txt",
            "#include <stdoi.h>\nint main(void) { printf(\"hi\\n\"); return 0; }\n");

        Assert.Equal("#include <stdio.h>", new CHeaderTypo().Propose(context)!.NewLines[0]);
    }

    [Theory]
    [InlineData("gcc/live-msvc-asan-null.txt", "access-violation", "access-violation on unknown address 0x000000000000 (a null pointer)", 5)]
    [InlineData("gcc/live-msvc-asan-overflow.txt", "stack-buffer-overflow", "stack-buffer-overflow on address 0x00fdc03ff8cc", 4)]
    public void AnAddressSanitizerReportNamesTheLineAndWhatHappened(string fixture, string type, string message, int line)
    {
        var error = new ParserRegistry().Parse(Fixtures.LoadStackTrace(fixture));

        Assert.NotNull(error);
        Assert.Equal(type, error!.ExceptionType);
        Assert.Equal(message, error.Message);
        Assert.Equal("main", error.Frames[0].Symbol);
        Assert.Equal(line, error.Frames[0].Line);
    }

    [Fact]
    public void GccsExpectedErrorsCountAsSyntax()
    {
        var (error, _) = Gcc(
            "live-mingw-missing-semicolon.txt",
            "#include <stdio.h>\nint main(void) {\n    int x = 3\n    printf(\"%d\\n\", x);\n    return 0;\n}\n");

        Assert.True(LocalFixEngine.IsSyntaxPhase(error));
    }
}
