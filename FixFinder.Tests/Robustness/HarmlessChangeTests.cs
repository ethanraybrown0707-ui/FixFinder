using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;

namespace FixFinder.Tests;

/// <summary>
/// Every program FixFinder holds - each lesson's broken and fixed example, each logic guide's example, and each program captured
/// from a real run - read again after a change that leaves what it does as it was: saved with Windows line endings, with a
/// byte-order mark, with spaces at the ends of its lines, with blank lines after its end, and with a comment line above its
/// first. The logic checks find the same mistakes in it every time, on the same lines - each a line further down where a comment
/// was put above it - and with Windows line endings and a byte-order mark, the same changes.
/// </summary>
public class HarmlessChangeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The changes, by name - none of which changes what a program does.</summary>
    public static readonly string[] Changes =
        ["Windows line endings", "a byte-order mark", "spaces at the ends of lines", "blank lines after the end", "a comment line at the top", "tabs for indentation"];

    /// <summary>Every program, by a name that says where it is from, with the kind of file it is saved as and its text.</summary>
    internal static IEnumerable<(string Name, string Extension, string Code)> Programs()
    {
        foreach (var lesson in Lessons.All)
        {
            for (var number = 0; number < lesson.Examples.Count; number++)
            {
                var example = lesson.Examples[number];
                var extension = lesson.Language.Extensions[0];
                yield return ($"lesson {lesson.Language.Name} {lesson.Concept.Code} {number + 1} broken", extension, example.Broken);
                yield return ($"lesson {lesson.Language.Name} {lesson.Concept.Code} {number + 1} fixed", extension, example.Fixed);
            }
        }

        foreach (var row in GuideExampleTests.EveryCheckOnItsOwnGuidesExample())
        {
            var (checkId, extension) = ((string)row[0], (string)row[1]);
            yield return ($"guide {checkId} {extension}", extension, Guidebook.For("example" + extension, FindingKind.Logic, checkId).Example ?? "");
        }

        foreach (var row in CapturedErrorTests.CapturedPrograms())
        {
            var (language, caseName, file) = ((string)row[0], (string)row[1], (string)row[2]);
            var path = Path.Combine(Fixtures.Root, "Captured", CapturedErrorTests.FolderOf(language), caseName, file);
            if (File.Exists(path)) yield return ($"captured {language} {caseName} {file}", Path.GetExtension(file), File.ReadAllText(path));
        }

        foreach (var snippet in SnippetsTheTestsUse()) yield return snippet;
    }

    /// <summary>
    /// The code every other test of FixFinder's is given - each row of an InlineData, or of a TheoryData member, that names a
    /// kind of file, by its file's name or its language's, and holds text of more than one line: the mistakes the checks are
    /// written to find, in every language, as well as the code they leave alone.
    /// </summary>
    private static IEnumerable<(string Name, string Extension, string Code)> SnippetsTheTestsUse()
    {
        var languages = CodeLanguage.All.Where(language => !language.IsAny).ToList();
        var extensions = languages.SelectMany(language => language.Extensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byName = languages.ToDictionary(language => language.Name, language => language.Extensions[0], StringComparer.Ordinal);
        const BindingFlags everyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        string? ExtensionIn(IReadOnlyList<string> values) =>
            values.Where(value => Regex.IsMatch(value, @"^[\w.-]+\.\w+$")).Select(Path.GetExtension).FirstOrDefault(extension => extension is not null && extensions.Contains(extension))
            ?? values.Select(value => byName.GetValueOrDefault(value)).FirstOrDefault(extension => extension is not null);

        // A test class named for a language - ErlangLiveTests, CSharpLiveTests - whose rows name no file: its language's kind of
        // file. The longest name is tried first, so JavaScript is not taken for Java, nor C++ for C.
        var spelled = languages.Select(language => (Spelled: language.Name.Replace("#", "Sharp").Replace("++", "pp"), Extension: language.Extensions[0]))
            .OrderByDescending(language => language.Spelled.Length).ToList();
        string? ExtensionOfClass(Type type) => spelled
            .Where(language => type.Name.Length > language.Spelled.Length && type.Name.StartsWith(language.Spelled, StringComparison.OrdinalIgnoreCase) &&
                               char.IsUpper(type.Name[language.Spelled.Length]))
            .Select(language => language.Extension).FirstOrDefault();

        IEnumerable<(string Name, string Extension, string Code)> FromRow(Type type, string source, object?[]? row)
        {
            // [InlineData(null)] gives a row that is itself null.
            var values = row?.OfType<string>().ToList() ?? [];
            if ((ExtensionIn(values) ?? ExtensionOfClass(type)) is not { } extension) yield break;
            foreach (var code in values.Where(value => value.Contains('\n'))) yield return ($"{source} row", extension, code);
        }

        foreach (var type in typeof(HarmlessChangeTests).Assembly.GetTypes().Where(type => type != typeof(HarmlessChangeTests)))
        {
            foreach (var method in type.GetMethods(everyMember))
                foreach (var data in method.GetCustomAttributes<InlineDataAttribute>())
                    foreach (var row in data.GetData(method))
                        foreach (var snippet in FromRow(type, $"{type.Name}.{method.Name}", row)) yield return snippet;

            foreach (var member in type.GetProperties(everyMember).Where(property => property.GetMethod?.IsStatic == true && typeof(IEnumerable<object[]>).IsAssignableFrom(property.PropertyType)))
                if (member.GetValue(null) is IEnumerable<object[]> rows)
                    foreach (var row in rows)
                        foreach (var snippet in FromRow(type, $"{type.Name}.{member.Name}", row)) yield return snippet;
        }
    }

    /// <summary>Each program, once for each change.</summary>
    public static TheoryData<string, string, string, string> ProgramsAndChanges()
    {
        var cases = new TheoryData<string, string, string, string>();
        foreach (var (name, extension, code) in Programs().DistinctBy(program => (program.Extension, program.Code)))
            foreach (var change in Changes.Where(change => change != "tabs for indentation" || IndentsByFours(code, extension)))
                cases.Add(name, extension, code, change);
        return cases;
    }

    /// <summary>
    /// Whether a program indents in steps of four spaces and nothing else, so tabs in their place keep its shape - and its
    /// language takes a tab as indentation: F# refuses tabs, and so does Fortran written by most compilers' rules.
    /// </summary>
    private static bool IndentsByFours(string code, string extension)
    {
        if (extension.ToLowerInvariant() is ".fs" or ".fsx" or ".f90" or ".f95" or ".f03" or ".f08") return false;

        var indents = code.ReplaceLineEndings("\n").Split('\n').Where(line => line.Trim().Length > 0).Select(line => line[..(line.Length - line.TrimStart().Length)]).ToList();
        return indents.Any(indent => indent.Length > 0) && indents.All(indent => indent.All(character => character == ' ') && indent.Length % 4 == 0);
    }

    /// <summary>The comment a line is written as in a kind of file, or null for one with no comment that runs to the end of a line.</summary>
    private static string CommentLine(string extension) => extension.ToLowerInvariant() switch
    {
        ".py" or ".pyw" or ".rb" or ".pl" or ".pm" or ".ps1" or ".exs" or ".ex" => "# a comment added above the program",
        ".lua" => "-- a comment added above the program",
        ".f90" or ".f95" or ".f03" or ".f08" => "! a comment added above the program",
        ".erl" => "% a comment added above the program",
        ".ml" => "(* a comment added above the program *)",
        _ => "// a comment added above the program",
    };

    /// <summary>
    /// The program with the change made, and the line the comment went in above - its first, or, in PHP, the line after its
    /// opening tag - or 0 where no line was put in.
    /// </summary>
    private static (byte[] Bytes, int InsertedAt) Changed(string code, string extension, string change)
    {
        var lines = code.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').ToList();
        var utf8 = new UTF8Encoding(false);

        switch (change)
        {
            case "Windows line endings":
                return (utf8.GetBytes(string.Join("\r\n", lines) + "\r\n"), 0);
            case "a byte-order mark":
                return ([.. new UTF8Encoding(true).GetPreamble(), .. utf8.GetBytes(string.Join("\n", lines) + "\n")], 0);
            case "spaces at the ends of lines":
                // Not after a \, which in Python and C carries the line on to the next only when nothing follows it.
                return (utf8.GetBytes(string.Join("\n", lines.Select(line => line.EndsWith('\\') ? line : line + "  ")) + "\n"), 0);
            case "blank lines after the end":
                return (utf8.GetBytes(string.Join("\n", lines) + "\n\n\n\n"), 0);
            case "tabs for indentation":
                return (utf8.GetBytes(string.Join("\n", lines.Select(line => new string('\t', (line.Length - line.TrimStart(' ').Length) / 4) + line.TrimStart(' '))) + "\n"), 0);
            default:
                var at = extension == ".php" && lines.Count > 0 && lines[0].TrimStart().StartsWith("<?php", StringComparison.Ordinal) ? 1 : 0;
                lines.Insert(at, CommentLine(extension));
                return (utf8.GetBytes(string.Join("\n", lines) + "\n"), at + 1);
        }
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, byte[] bytes)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllBytes(path, bytes);
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    private static string Listed(IEnumerable<string> findings) => string.Join("\n", findings.DefaultIfEmpty("(none)"));

    /// <summary>
    /// The programs give the checks something to find, in every language a check reads: the test above compares findings, and
    /// would pass on programs that held none.
    /// </summary>
    [Fact]
    public void TheProgramsHoldMistakesInEveryLanguageTheChecksRead()
    {
        var withFindings = Programs().DistinctBy(program => (program.Extension, program.Code))
            .Where(program => Scan((program.Extension == ".java" ? "Main" : "main") + program.Extension, Encoding.UTF8.GetBytes(program.Code)).Count > 0)
            .GroupBy(program => CodeLanguage.Of("main" + program.Extension)?.Name ?? program.Extension)
            .ToDictionary(group => group.Key, group => group.Count());

        var read = LogicPatterns.All.SelectMany(check => check.Extensions)
            .Select(extension => CodeLanguage.Of("main" + extension)?.Name).OfType<string>().Distinct().Order().ToList();

        Assert.All(read, language => Assert.True(withFindings.GetValueOrDefault(language) > 0, $"no program in {language} holds a mistake a check finds"));
        Assert.True(withFindings.Values.Sum() >= 600, string.Join(", ", withFindings.Select(pair => $"{pair.Key} {pair.Value}")));
    }

    [Theory]
    [MemberData(nameof(ProgramsAndChanges))]
    public void AChangeThatLeavesWhatAProgramDoesAsItWasLeavesWhatTheChecksFindAsItWas(string name, string extension, string code, string change)
    {
        var fileName = (extension == ".java" ? "Main" : "main") + extension;
        var original = Scan(fileName, Encoding.UTF8.GetBytes(code.ReplaceLineEndings("\n").TrimEnd('\n') + "\n"));
        var (bytes, insertedAt) = Changed(code, extension, change);
        var changed = Scan(fileName, bytes);

        int Moved(int line) => insertedAt > 0 && line >= insertedAt ? line + 1 : line;
        var expected = original.Select(finding => $"{finding.PatternId} line {Moved(finding.Line)}").Order(StringComparer.Ordinal).ToList();
        var found = changed.Select(finding => $"{finding.PatternId} line {finding.Line}").Order(StringComparer.Ordinal).ToList();

        Assert.True(expected.SequenceEqual(found), $"{name}, with {change}:\nexpected\n{Listed(expected)}\nfound\n{Listed(found)}");

        if (change is not ("Windows line endings" or "a byte-order mark")) return;

        static string Changes(IEnumerable<LogicFinding> findings) =>
            Listed(findings.Where(finding => finding.Fix is not null).Select(finding => $"{finding.PatternId} {finding.Fix!.StartLine}+{finding.Fix.RemoveCount}: {string.Join(" / ", finding.Fix.NewLines)}").Order(StringComparer.Ordinal));
        Assert.Equal(Changes(original), Changes(changed));
    }
}
