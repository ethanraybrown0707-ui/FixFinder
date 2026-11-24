using FixFinder.Core.Checking;
using FixFinder.Core.Reporting;

namespace FixFinder.Tests;

/// <summary>
/// How a program's own files use each other: each file found for a Python, JavaScript or TypeScript program, with the others
/// of them it names in an import or require that is code - one in a comment, a docstring or text is not - said a line for
/// each that uses others, named from the chosen file's folder; and nothing for a program of one file, or of another language.
/// </summary>
public class FileMapTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string code)
    {
        var path = Path.Combine(_temp.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code.ReplaceLineEndings("\n"));
        return path;
    }

    private static IReadOnlyList<string> MapOf(string chosen) => ProgramFiles.UsesSaid(ProgramFiles.Uses(ProgramFiles.Read(chosen)), chosen);

    /// <summary>
    /// main.py imports grades and, from the stats package, its module average - naming the package's __init__.py and the
    /// module both; grades.py imports the module by its dotted name, which names the module alone, and names report.py only
    /// inside its docstring, which Python never runs - so report.py, found as one of the program's files, is used by none.
    /// </summary>
    [Fact]
    public void APythonProgramsFilesAreMappedByTheImportsThatAreCode()
    {
        var main = Write("main.py", """
            import grades
            from stats import average
            # import old

            print(grades.letter(average.mean([70, 80])))
            """);
        Write("grades.py", """
            '''Letters for marks.
            import report
            '''
            import stats.average


            def letter(mark):
                return "A" if mark >= stats.average.TOP else "B"
            """);
        Write("stats/__init__.py", "");
        Write("stats/average.py", "TOP = 70\n\n\ndef mean(marks):\n    return sum(marks) / len(marks)\n");
        Write("report.py", "print('report')\n");
        Write("old.py", "print('old')\n");

        Assert.Equal([
            "`main.py` uses `grades.py`, `stats/__init__.py` and `stats/average.py`",
            "`grades.py` uses `stats/average.py`",
        ], MapOf(main));
    }

    /// <summary>An import inside a function is code as much as one at the top, run each time the function is called: the file it names is used.</summary>
    [Fact]
    public void AnImportInsideAFunctionIsMappedLikeOneAtTheTop()
    {
        var main = Write("main.py", """
            def report(mark):
                import grades
                return grades.letter(mark)


            print(report(70))
            """);
        Write("grades.py", "def letter(mark):\n    return 'A' if mark >= 70 else 'B'\n");

        Assert.Equal(["`main.py` uses `grades.py`"], MapOf(main));
    }

    /// <summary>
    /// main.js requires grades and a file in a folder of its own; a require in a comment, and one written inside text the
    /// program prints, name files the program never loads.
    /// </summary>
    [Fact]
    public void AJavaScriptProgramsFilesAreMappedByTheRequiresThatAreCode()
    {
        var main = Write("main.js", """
            const grades = require("./grades");
            const { mean } = require("./lib/format.js");
            // const old = require("./old");
            console.log("loaded with require('./report')");
            console.log(grades.letter(mean([70, 80])));
            """);
        Write("grades.js", "const { mean } = require('./lib/format');\nmodule.exports = { letter: mark => (mark >= 70 ? 'A' : 'B'), mean };\n");
        Write("lib/format.js", "module.exports = { mean: marks => marks.reduce((a, b) => a + b, 0) / marks.length };\n");
        Write("old.js", "console.log('old');\n");
        Write("report.js", "console.log('report');\n");

        Assert.Equal([
            "`main.js` uses `grades.js` and `lib/format.js`",
            "`grades.js` uses `lib/format.js`",
        ], MapOf(main));
    }

    [Fact]
    public void ATypeScriptProgramsFilesAreMappedByItsImports()
    {
        var main = Write("main.ts", "import { mean } from \"./stats.ts\";\n\nconsole.log(mean([70, 80]));\n");
        Write("stats.ts", "export function mean(marks: number[]): number {\n    return marks.reduce((a, b) => a + b, 0) / marks.length;\n}\n");

        Assert.Equal(["`main.ts` uses `stats.ts`"], MapOf(main));
    }

    /// <summary>A program of one file uses no other, and a Java program's files are found another way, with no imports to follow.</summary>
    [Fact]
    public void AProgramOfOneFileAndAJavaProgramHaveNothingToSay()
    {
        var alone = Write("alone.py", "print(sum([1, 2]))\n");
        var java = Write("Main.java", "public class Main {\n    public static void main(String[] args) {\n        System.out.println(Helper.twice(2));\n    }\n}\n");
        Write("Helper.java", "public class Helper {\n    static int twice(int n) {\n        return n * 2;\n    }\n}\n");

        Assert.Empty(MapOf(alone));
        Assert.Empty(MapOf(java));
    }

    [Fact]
    public void TheSavedReportSaysHowTheFilesUseEachOther()
    {
        var page = new ReportPage
        {
            Program = "main.py", Language = "Python", CheckedAt = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero),
            SyntaxSummary = "No syntax errors", LogicSummary = "No logic mistakes found", Findings = [],
            FileMap = ["`main.py` uses `grades.py`"],
        }.ToHtml();

        Assert.Contains("<section class=\"files\">\n<h2>How the files use each other</h2>\n", page, StringComparison.Ordinal);
        Assert.Contains("<li><code>main.py</code> uses <code>grades.py</code></li>", page, StringComparison.Ordinal);
    }
}
