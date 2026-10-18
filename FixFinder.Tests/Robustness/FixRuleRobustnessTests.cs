using System.Collections.Concurrent;
using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// The files the odd reports point at, written once for each kind of file and shared by every test: a three-line program, an
/// empty file, and a file whose second line is one 100,000-character word.
/// </summary>
public sealed class OddReportFiles : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ConcurrentDictionary<string, string> _written = new(StringComparer.Ordinal);

    public string Program(string extension) => Written("program" + extension, "x = 1\ny = 2\nz = 3\n");

    public string Empty(string extension) => Written("empty" + extension, "");

    public string OneLongWord(string extension) => Written("wide" + extension, "x = 1\n" + new string('a', 100_000) + "\nz = 3\n");

    public string Missing(string extension) => Path.Combine(_temp.Path, "missing" + extension);

    public string Folder => _temp.Path;

    private string Written(string name, string text) => _written.GetOrAdd(name, _ =>
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, text);
        return path;
    });

    public void Dispose() => _temp.Dispose();
}

/// <summary>
/// Every fix rule, given oddly shaped reports in each language it reads: no place at all, a file that is not there, line 0, line
/// 99,999 of a three-line file, a line of an empty file, a 10,000-character message, a line that is one 100,000-character word,
/// a column past the end of its line, and empty text for the error's type and message. Each rule must answer - a change or
/// nothing - and throw nothing.
/// </summary>
/// <remarks>
/// These reports seldom get past a rule's first questions - is this its language, its error, its message - so they test that no
/// rule takes a report's place or wording for granted, not the whole of what it does. The engine would catch a rule that threw,
/// but the change it might have offered would be lost without a word.
/// </remarks>
public class FixRuleRobustnessTests(OddReportFiles files) : IClassFixture<OddReportFiles>
{
    private static readonly string[] OddReports =
    [
        "no place", "a file that is not there", "line 0", "line 99,999 of three", "a line of an empty file", "a 10,000-character message",
        "a line that is one 100,000-character word", "a column past the end of the line", "empty text for type and message",
    ];

    /// <summary>The languages a rule reads, by the prefix of its name - every language for a rule that names none.</summary>
    private static IEnumerable<CodeLanguage> LanguagesOf(ILocalFixRule rule)
    {
        var named = CodeLanguage.All.Where(language => !language.IsAny && language.RulePrefixes.Any(prefix => rule.Id.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        return named.Count > 0 ? named : CodeLanguage.All.Where(language => !language.IsAny);
    }

    /// <summary>Each rule, each name of an error reader of each language it reads, and each odd report.</summary>
    public static TheoryData<string, string, string, string> EveryRuleGivenEveryOddReport()
    {
        var cases = new TheoryData<string, string, string, string>();
        foreach (var rule in LocalFixEngine.Rules.DistinctBy(rule => rule.Id))
            foreach (var language in LanguagesOf(rule))
                foreach (var parserId in language.ParserIds)
                    foreach (var report in OddReports)
                        cases.Add(rule.Id, language.Name, parserId, report);
        return cases;
    }

    private static ErrorFrame FrameAt(string? file, int? line, int? column = null) => new() { Order = 0, File = file, Line = line, Column = column, RawLine = "" };

    private (string? Type, string? Message, IReadOnlyList<ErrorFrame> Frames) Report(string report, string extension) => report switch
    {
        "no place" => (null, null, []),
        "a file that is not there" => ("Error", "something went wrong", [FrameAt(files.Missing(extension), 1)]),
        "line 0" => ("Error", "something went wrong", [FrameAt(files.Program(extension), 0)]),
        "line 99,999 of three" => ("Error", "something went wrong", [FrameAt(files.Program(extension), 99_999)]),
        "a line of an empty file" => ("Error", "something went wrong", [FrameAt(files.Empty(extension), 1)]),
        "a 10,000-character message" => ("Error", new string('x', 10_000), [FrameAt(files.Program(extension), 2)]),
        "a line that is one 100,000-character word" => ("Error", "something went wrong", [FrameAt(files.OneLongWord(extension), 2)]),
        "a column past the end of the line" => ("Error", "something went wrong", [FrameAt(files.Program(extension), 2, 500)]),
        _ => ("", "", [FrameAt(files.Program(extension), 2)]),
    };

    [Theory]
    [MemberData(nameof(EveryRuleGivenEveryOddReport))]
    public void AFixRuleAnswersAnOddlyShapedReportWithoutThrowing(string ruleId, string languageName, string parserId, string report)
    {
        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var (type, message, frames) = Report(report, language.Extensions[0]);
        var error = new ParsedError
        {
            LanguageId = parserId, Confidence = 90, RawText = message ?? "", FirstLineSequence = 0, ExceptionType = type, Message = message, Frames = frames,
        };
        var context = new LocalFixContext { Error = error, SourceRoot = files.Folder, Language = language };

        foreach (var rule in LocalFixEngine.Rules.Where(each => each.Id == ruleId))
        {
            var answering = Task.Run(() => rule.Propose(context));
            Assert.True(answering.Wait(TimeSpan.FromMinutes(1)), $"{ruleId} was still answering after a minute");
        }
    }
}
