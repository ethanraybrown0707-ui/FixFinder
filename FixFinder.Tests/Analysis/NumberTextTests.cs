using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Tests;

/// <summary>
/// Text read as a number by each language's own rules. A finding that says a text is not a number is only true if the
/// language would refuse it - so Python's float("inf"), Java's Double.parseDouble("1.5f") and C#'s
/// double.Parse("1,000.5") must all read, and a refusal must say why: no number, a fraction where a whole number was
/// asked for, or a whole number written in a way the conversion does not take.
/// </summary>
public class NumberTextTests
{
    [Theory]
    [InlineData("inf", "Reads")]
    [InlineData("-Infinity", "Reads")]
    [InlineData("nan", "Reads")]
    [InlineData(" 1_000.5 ", "Reads")]
    [InlineData("1e-3", "Reads")]
    [InlineData(".5", "Reads")]
    [InlineData("5.", "Reads")]
    [InlineData("1__0", "NotANumber")]
    [InlineData("_1", "NotANumber")]
    [InlineData("1_", "NotANumber")]
    [InlineData(".", "NotANumber")]
    [InlineData("1e", "NotANumber")]
    [InlineData("0x10", "NotANumber")]
    [InlineData("infinit", "NotANumber")]
    [InlineData("abc", "NotANumber")]
    public void PythonFloatReadsWhatPythonReads(string text, string reading) =>
        Assert.Equal(Enum.Parse<NumberText.Reading>(reading), NumberText.Read(SourceLanguage.Python, text, whole: false));

    [Theory]
    [InlineData(" 42 ", "Reads")]
    [InlineData("+1_000", "Reads")]
    [InlineData("007", "Reads")]
    [InlineData("١٢", "Reads")]
    [InlineData("1.5", "NotWhole")]
    [InlineData("inf", "NotWhole")]
    [InlineData("12.0", "WrittenOtherwise")]
    [InlineData("1e3", "WrittenOtherwise")]
    [InlineData("25e-1", "NotWhole")]
    [InlineData("250e-1", "WrittenOtherwise")]
    [InlineData("twelve", "NotANumber")]
    public void PythonIntReadsWhatPythonReads(string text, string reading) =>
        Assert.Equal(Enum.Parse<NumberText.Reading>(reading), NumberText.Read(SourceLanguage.Python, text, whole: true));

    [Theory]
    [InlineData("1.5f", "Reads")]
    [InlineData("2D", "Reads")]
    [InlineData("0x1p3", "Reads")]
    [InlineData("0x1.8p1d", "Reads")]
    [InlineData(" -Infinity ", "Reads")]
    [InlineData("NaN", "Reads")]
    [InlineData("inf", "NotANumber")]
    [InlineData("nan", "NotANumber")]
    [InlineData("0xfd", "NotANumber")]
    [InlineData("1_000", "NotANumber")]
    [InlineData("1,5", "NotANumber")]
    public void JavaParseDoubleReadsWhatJavaReads(string text, string reading) =>
        Assert.Equal(Enum.Parse<NumberText.Reading>(reading), NumberText.Read(SourceLanguage.Java, text, whole: false));

    [Theory]
    [InlineData("+12", "Reads")]
    [InlineData("-7", "Reads")]
    [InlineData(" 12", "WrittenOtherwise")]
    [InlineData("12.5", "NotWhole")]
    [InlineData("0x1.8p0", "NotWhole")]
    [InlineData("0x1p3", "WrittenOtherwise")]
    [InlineData("twelve", "NotANumber")]
    public void JavaParseIntReadsWhatJavaReads(string text, string reading) =>
        Assert.Equal(Enum.Parse<NumberText.Reading>(reading), NumberText.Read(SourceLanguage.Java, text, whole: true));

    /// <summary>
    /// "12.5" is a fraction written the English way and a hundred and twenty-five written the German way; the culture
    /// the program runs in decides, so int.Parse("12.5") is only said to be written the wrong way, not to be a fraction.
    /// </summary>
    [Theory]
    [InlineData("1,000.5", false, "Reads")]
    [InlineData("1.234,5", false, "Reads")]
    [InlineData("1.5f", false, "NotANumber")]
    [InlineData("twelve", false, "NotANumber")]
    [InlineData(" 42 ", true, "Reads")]
    [InlineData("1,000", true, "WrittenOtherwise")]
    [InlineData("12.5", true, "WrittenOtherwise")]
    [InlineData("5e-1", true, "NotWhole")]
    [InlineData("twelve", true, "NotANumber")]
    public void CSharpParseReadsWhatCSharpReads(string text, bool whole, string reading) =>
        Assert.Equal(Enum.Parse<NumberText.Reading>(reading), NumberText.Read(SourceLanguage.CSharp, text, whole));
}
