using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Measures;

namespace FixFinder.Tests;

/// <summary>
/// McCabe's cyclomatic complexity, counted from each language's own reading of the same function, `grade`: two ifs and
/// an else-if, a counted loop and a while, a switch of two cases and a default, a catch, a conditional expression, and an
/// or and an and - written in each language as far as it has them - so each kind of decision is counted as many times as
/// it is written, and a default and a language's missing kinds not at all.
/// </summary>
public class ComplexityTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The program read by its own language's reader; null when this computer has no Python, JDK or Go to read it with.</summary>
    private async Task<IrProgram?> ReadAsync(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        await File.WriteAllTextAsync(path, code);

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".py" => PythonFrontend.FindInterpreter() is { } python ? await PythonFrontend.ReadAsync([path], python) : null,
            ".java" => JavaFrontend.FindTools() is { } tools ? await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java) : null,
            ".go" => GoFrontend.FindGo(path) is { } go ? await GoFrontend.ReadAsync([path], go) : null,
            ".js" => await JavaScriptFrontend.ReadAsync([path]),
            ".cs" => await CSharpFrontend.ReadAsync([path]),
            _ => await CFrontend.ReadAsync([path]),
        };
    }

    private const string Python = """
        def grade(mark, bonus):
            if mark < 0 or mark > 100:
                return "invalid"
            total = 0
            for step in range(3):
                total += step
            while total > 10 and bonus:
                total -= 1
            try:
                value = 100 // mark
            except ZeroDivisionError:
                value = 0
            label = "pass" if mark >= 40 else "fail"
            if mark > 90:
                return "A"
            elif mark > 70:
                return "B"
            return label
        """;

    private const string Java = """
        class Grades {
            static String grade(int mark, boolean bonus) {
                if (mark < 0 || mark > 100) {
                    return "invalid";
                }
                int total = 0;
                for (int step = 0; step < 3; step++) {
                    total += step;
                }
                while (total > 10 && bonus) {
                    total -= 1;
                }
                int value;
                try {
                    value = 100 / mark;
                } catch (ArithmeticException error) {
                    value = 0;
                }
                String label = mark >= 40 ? "pass" : "fail";
                switch (mark) {
                    case 100: label = "full"; break;
                    case 0: label = "none"; break;
                    default: break;
                }
                if (mark > 90) {
                    return "A";
                } else if (mark > 70) {
                    return "B";
                }
                return label;
            }
        }
        """;

    private const string CSharp = """
        class Grades
        {
            static string Grade(int mark, bool bonus)
            {
                if (mark < 0 || mark > 100)
                {
                    return "invalid";
                }
                int total = 0;
                for (int step = 0; step < 3; step++)
                {
                    total += step;
                }
                while (total > 10 && bonus)
                {
                    total -= 1;
                }
                int value;
                try
                {
                    value = 100 / mark;
                }
                catch (System.DivideByZeroException)
                {
                    value = 0;
                }
                string label = mark >= 40 ? "pass" : "fail";
                switch (mark)
                {
                    case 100: label = "full"; break;
                    case 0: label = "none"; break;
                    default: break;
                }
                if (mark > 90)
                {
                    return "A";
                }
                else if (mark > 70)
                {
                    return "B";
                }
                return label;
            }
        }
        """;

    private const string JavaScript = """
        function grade(mark, bonus) {
            if (mark < 0 || mark > 100) {
                return "invalid";
            }
            let total = 0;
            for (let step = 0; step < 3; step++) {
                total += step;
            }
            while (total > 10 && bonus) {
                total -= 1;
            }
            let value;
            try {
                value = check(mark);
            } catch (error) {
                value = 0;
            }
            let label = mark >= 40 ? "pass" : "fail";
            switch (mark) {
                case 100: label = "full"; break;
                case 0: label = "none"; break;
                default: break;
            }
            if (mark > 90) {
                return "A";
            } else if (mark > 70) {
                return "B";
            }
            return label;
        }
        """;

    // Go has no catch and no conditional expression; its for with only a condition is its while.
    private const string Go = """
        package main

        func grade(mark int, bonus bool) string {
            if mark < 0 || mark > 100 {
                return "invalid"
            }
            total := 0
            for step := 0; step < 3; step++ {
                total += step
            }
            for total > 10 && bonus {
                total -= 1
            }
            label := "fail"
            switch mark {
            case 100:
                label = "full"
            case 0:
                label = "none"
            default:
            }
            if mark > 90 {
                return "A"
            } else if mark > 70 {
                return "B"
            }
            return label
        }

        func main() {
            println(grade(50, true))
        }
        """;

    // C has no catch.
    private const string C = """
        const char *grade(int mark, int bonus) {
            if (mark < 0 || mark > 100) {
                return "invalid";
            }
            int total = 0;
            for (int step = 0; step < 3; step++) {
                total += step;
            }
            while (total > 10 && bonus) {
                total -= 1;
            }
            const char *label = mark >= 40 ? "pass" : "fail";
            switch (mark) {
                case 100: label = "full"; break;
                case 0: label = "none"; break;
                default: break;
            }
            if (mark > 90) {
                return "A";
            } else if (mark > 70) {
                return "B";
            }
            return label;
        }

        int main(void) {
            return grade(50, 1)[0];
        }
        """;

    private const string Cpp = """
        #include <stdexcept>

        const char *grade(int mark, bool bonus) {
            if (mark < 0 || mark > 100) {
                return "invalid";
            }
            int total = 0;
            for (int step = 0; step < 3; step++) {
                total += step;
            }
            while (total > 10 && bonus) {
                total -= 1;
            }
            int value;
            try {
                value = 100 / mark;
            } catch (const std::exception &error) {
                value = 0;
            }
            const char *label = mark >= 40 ? "pass" : "fail";
            switch (mark) {
                case 100: label = "full"; break;
                case 0: label = "none"; break;
                default: break;
            }
            if (mark > 90) {
                return "A";
            } else if (mark > 70) {
                return "B";
            }
            return label;
        }

        int main() {
            return grade(50, true)[0];
        }
        """;

    [Theory]
    [InlineData("grades.py", "grade", 3, 2, 0, 1, 1, 2, 10)]
    [InlineData("Grades.java", "grade", 3, 2, 2, 1, 1, 2, 12)]
    [InlineData("Grades.cs", "Grade", 3, 2, 2, 1, 1, 2, 12)]
    [InlineData("grades.js", "grade", 3, 2, 2, 1, 1, 2, 12)]
    [InlineData("grades.go", "grade", 3, 2, 2, 0, 0, 2, 10)]
    [InlineData("grades.c", "grade", 3, 2, 2, 0, 1, 2, 11)]
    [InlineData("grades.cpp", "grade", 3, 2, 2, 1, 1, 2, 12)]
    public async Task EachDecisionIsCountedAsOftenAsItIsWritten(
        string fileName, string functionName, int ifs, int loops, int cases, int catches, int conditionals, int andsAndOrs, int complexity)
    {
        var code = Path.GetExtension(fileName) switch
        {
            ".py" => Python, ".java" => Java, ".cs" => CSharp, ".js" => JavaScript, ".go" => Go, ".c" => C, _ => Cpp,
        };
        if (await ReadAsync(fileName, code) is not { } program) return;

        var measured = Complexity.Of(Assert.Single(program.AllFunctions, function => function.Name == functionName));

        Assert.Equal((ifs, loops, cases, catches, conditionals, andsAndOrs, complexity),
            (measured.Ifs, measured.Loops, measured.Cases, measured.Catches, measured.Conditionals, measured.AndsAndOrs, measured.Value));
    }

    /// <summary>
    /// The file's most complex functions are said most complex first, a method by its class, the code outside any function
    /// as that, and a function that decides nothing not at all.
    /// </summary>
    [Fact]
    public async Task TheMostComplexFunctionsAreSaidMostComplexFirst()
    {
        const string code = """
            class Marks:
                def best(self, marks):
                    top = 0
                    for mark in marks:
                        if mark > top:
                            top = mark
                    return top

            def report(marks):
                return len(marks)

            marks = [70, 80]
            if marks and len(marks) > 1 and marks[0] > 0:
                print(Marks().best(marks))
            """;
        if (await ReadAsync("marks.py", code) is not { } program) return;

        Assert.Equal(
            ["The code outside any function: 4 - 1 `if` and 2 `and`s and `or`s", "`Marks.best` on line 2: 3 - 1 `if` and 1 loop"],
            Complexity.MostComplex(program, Path.Combine(_temp.Path, "marks.py")));
    }

    /// <summary>
    /// A function written inside another, and a lambda, decide nothing for the function around them: `outer`, whose own
    /// only decision is one if, is 2 however many ifs and ands the function and the lambda inside it hold.
    /// </summary>
    [Theory]
    [InlineData("nested.py", """
        def outer(marks):
            def inner(mark):
                if mark > 50 and mark < 90:
                    return 1
                return 0
            passed = list(filter(lambda mark: mark > 40 and mark < 100, marks))
            if passed:
                return inner(passed[0])
            return 0
        """)]
    [InlineData("nested.js", """
        function outer(marks) {
            function inner(mark) {
                if (mark > 50 && mark < 90) {
                    return 1;
                }
                return 0;
            }
            const passed = marks.filter(mark => mark > 40 && mark < 100 ? true : false);
            if (passed.length > 0) {
                return inner(passed[0]);
            }
            return 0;
        }
        """)]
    [InlineData("Nested.java", """
        import java.util.List;

        class Nested {
            static int outer(List<Integer> marks) {
                List<Integer> passed = marks.stream().filter(mark -> mark > 40 && mark < 100 ? true : false).toList();
                if (!passed.isEmpty()) {
                    return passed.get(0);
                }
                return 0;
            }
        }
        """)]
    [InlineData("Nested.cs", """
        using System.Collections.Generic;
        using System.Linq;

        class Nested
        {
            static int outer(List<int> marks)
            {
                int Inner(int mark)
                {
                    if (mark > 50 && mark < 90)
                    {
                        return 1;
                    }
                    return 0;
                }
                var passed = marks.Where(mark => mark > 40 && mark < 100 ? true : false).ToList();
                if (passed.Count > 0)
                {
                    return Inner(passed[0]);
                }
                return 0;
            }
        }
        """)]
    [InlineData("nested.go", """
        package main

        func outer(marks []int) int {
            passes := func(mark int) bool {
                if mark > 40 && mark < 100 {
                    return true
                }
                return false
            }
            if passes(marks[0]) {
                return marks[0]
            }
            return 0
        }

        func main() {
            println(outer([]int{50}))
        }
        """)]
    [InlineData("nested.cpp", """
        #include <vector>

        int outer(const std::vector<int> &marks) {
            auto passes = [](int mark) {
                if (mark > 40 && mark < 100) {
                    return true;
                }
                return false;
            };
            if (passes(marks[0])) {
                return marks[0];
            }
            return 0;
        }

        int main() {
            return outer({50});
        }
        """)]
    public async Task AFunctionWrittenInsideAnotherAndALambdaAreNotCountedIntoIt(string fileName, string code)
    {
        if (await ReadAsync(fileName, code) is not { } program) return;

        var outer = Complexity.Of(Assert.Single(program.AllFunctions, function => function.Name == "outer"));
        Assert.Equal((1, 0, 0, 2), (outer.Ifs, outer.AndsAndOrs, outer.Conditionals, outer.Value));
    }

    /// <summary>
    /// One conditional expression given to two names at once is written once, and counted once; a chained comparison is
    /// counted as the `and` Python reads it as - 0 <= mark <= 100 is 0 <= mark and mark <= 100, whose second half is
    /// worked out only when the first holds.
    /// </summary>
    [Fact]
    public async Task AValueGivenToTwoNamesIsCountedOnceAndAChainedComparisonIsAnAnd()
    {
        const string code = """
            class Marks:
                def label(self, mark):
                    self.last = shown = "pass" if mark >= 40 else "fail"
                    return shown

                def valid(self, mark):
                    return 0 <= mark <= 100
            """;
        if (await ReadAsync("marks.py", code) is not { } program) return;

        var label = Complexity.Of(Assert.Single(program.AllFunctions, function => function.Name == "label"));
        var valid = Complexity.Of(Assert.Single(program.AllFunctions, function => function.Name == "valid"));
        Assert.Equal((1, 2), (label.Conditionals, label.Value));
        Assert.Equal((1, 2), (valid.AndsAndOrs, valid.Value));
    }

    /// <summary>The Python `grade` is said as the README says it.</summary>
    [Fact]
    public async Task APythonFunctionIsSaidWithEachKindOfDecisionInIt()
    {
        if (await ReadAsync("grades.py", Python) is not { } program) return;

        Assert.Equal("`grade` on line 1: 10 - 3 `if`s, 2 loops, 1 catch, 1 conditional expression and 2 `and`s and `or`s",
            Complexity.Of(Assert.Single(program.AllFunctions, function => function.Name == "grade")).Said);
    }

    /// <summary>The saved report lists them under a heading of their own, their code as code, and has no such heading without them.</summary>
    [Fact]
    public void TheSavedReportListsThemOnlyWhenThereAreAny()
    {
        Core.Reporting.ReportPage Page(IReadOnlyList<string> complexity) => new()
        {
            Program = "marks.py", Language = "Python", CheckedAt = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero),
            SyntaxSummary = "No syntax errors", LogicSummary = "No logic mistakes found", Findings = [], Complexity = complexity,
        };

        var listed = Page(["`Marks.best` on line 2: 3 - 1 `if` and 1 loop"]).ToHtml();
        Assert.Contains("<h2>How complex each function is</h2>", listed, StringComparison.Ordinal);
        Assert.Contains("<li><code>Marks.best</code> on line 2: 3 - 1 <code>if</code> and 1 loop</li>", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("How complex each function is", Page([]).ToHtml(), StringComparison.Ordinal);
    }

    /// <summary>Only the file checked is said; another file's functions are that file's to say.</summary>
    [Fact]
    public async Task OnlyTheFileCheckedIsSaid()
    {
        if (await ReadAsync("grades.js", JavaScript) is not { } program) return;

        Assert.Single(Complexity.MostComplex(program, Path.Combine(_temp.Path, "grades.js")));
        Assert.Empty(Complexity.MostComplex(program, Path.Combine(_temp.Path, "main.js")));
    }
}
