using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// The two faults the fuzz tests found in FixFinder's own readers, pinned one by one.
/// </summary>
/// <remarks>
/// The fuzz tests found them, but they cannot be trusted to find them again: the second fault first appeared on the
/// two thousand five hundred and sixteenth damaged program of a deep sweep, and the quick sweep the suite runs never
/// gets that far. These say exactly what went wrong, so bringing either fault back fails at once and says why.
/// </remarks>
public class ReaderRobustnessTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> WriteAsync(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        await File.WriteAllTextAsync(path, text);
        return path;
    }

    /// <summary>
    /// A program that stops half way through is what a student hands in mid-edit. The reader keeps going past the end
    /// on purpose so it can read what is there, and one of its reads forgot that and threw.
    /// </summary>
    [Theory]
    [InlineData("function total(items) { let sum = 0; for (const item of")]
    [InlineData("const pick = (a, b = 2) =>")]
    [InlineData("class Till { add(x) { return x?.")]
    [InlineData("if (a) { b = [1, 2,")]
    public async Task AJavaScriptProgramThatStopsHalfWayIsReadNotThrownAt(string source)
    {
        var program = await JavaScriptFrontend.ReadAsync([await WriteAsync("cut.js", source)]);

        Assert.NotNull(program);
    }

    [Theory]
    [InlineData("int main(void) { int *lines = malloc(")]
    [InlineData("struct point { int x; int")]
    [InlineData("static int total_of(const int *lines, int count) { for (int i = 0; i <")]
    [InlineData("typedef int (*compare)(const void *,")]
    public async Task ACProgramThatStopsHalfWayIsReadNotThrownAt(string source)
    {
        var program = await CFrontend.ReadAsync([await WriteAsync("cut.c", source)]);

        Assert.NotNull(program);
    }

    [Theory]
    [InlineData("template <typename T> T biggest(const std::vector<T>& items) { T best =")]
    [InlineData("class Stock { public: int *levels = new int[")]
    public async Task ACppProgramThatStopsHalfWayIsReadNotThrownAt(string source)
    {
        var program = await CFrontend.ReadAsync([await WriteAsync("cut.cpp", source)]);

        Assert.NotNull(program);
    }

    /// <summary>
    /// Escapes JavaScript itself refuses. They were handed straight to a conversion that threw; they are syntax errors,
    /// and are now reported as one, the same way an unclosed string is.
    /// </summary>
    [Theory]
    [InlineData("""const a = "\xzz";""")]
    [InlineData("""const a = "\x4";""")]
    [InlineData("""const a = "\u12zz";""")]
    [InlineData("""const a = "\u{}";""")]
    [InlineData("""const a = "\u{110000}";""")]
    [InlineData("""const a = "\u{zz}";""")]
    [InlineData("""const a = "\u{FFFFFFFFFFFF}";""")]
    public void AnEscapeJavaScriptRefusesIsReportedNotThrown(string source)
    {
        var lexer = new JsLexer(source);

        lexer.Tokens();

        Assert.NotNull(lexer.Problem);
        Assert.Contains("hexadecimal", lexer.Problem, StringComparison.Ordinal);
    }

    /// <summary>The fix must not refuse what JavaScript accepts - including a lone surrogate, which a JS string may hold.</summary>
    [Theory]
    [InlineData("""const a = "\x41";""", "A")]
    [InlineData("""const a = "A";""", "A")]
    [InlineData("""const a = "\u{41}";""", "A")]
    [InlineData("""const a = "\u{000000041}";""", "A")]
    [InlineData("""const a = "\u{1F600}";""", "\U0001F600")]
    public void AnEscapeJavaScriptAcceptsIsReadAsTheCharacterItStandsFor(string source, string expected)
    {
        var lexer = new JsLexer(source);
        var tokens = lexer.Tokens();

        Assert.Null(lexer.Problem);
        Assert.Contains(tokens, token => token.Value as string == expected);
    }

    /// <summary>
    /// A JavaScript string is a sequence of UTF-16 code units, so it may hold half a surrogate pair on its own, and the
    /// reader must not refuse one. Kept out of the theory above: a lone surrogate cannot survive the way test data is
    /// passed to a theory, and arrives as two replacement characters, so the expected value is built here instead.
    /// </summary>
    [Fact]
    public void ALoneSurrogateIsAllowedBecauseJavaScriptAllowsIt()
    {
        var lexer = new JsLexer("""const a = "\u{D800}";""");
        var tokens = lexer.Tokens();

        Assert.Null(lexer.Problem);
        Assert.Contains(tokens, token => token.Value as string == ((char)0xD800).ToString());
    }
}
