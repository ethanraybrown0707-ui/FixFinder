using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Parsing;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests.Teaching;

/// <summary>
/// The further explanation each finding is given: its steps, built from the finding alone; the words it explains; the
/// habit for its idea; and the language's own message, shown only where a language said one.
/// </summary>
public class FurtherExplanationTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Finding Found(string file, FindingKind kind, Severity severity, int? line, ParsedError? error = null, string ruleId = "rule",
        string title = "Title", string explanation = "What is wrong.") => new()
    {
        Kind = kind,
        Severity = severity,
        Confidence = Confidence.Certain,
        File = file,
        Line = line,
        Title = title,
        Explanation = explanation,
        WhyItMatters = "Why it matters.",
        SuggestedFix = "How to fix it.",
        CorrectedExample = "",
        RuleId = ruleId,
        Error = error,
    };

    private static ParsedError Said(string languageId, string raw, string? type = null) => new()
    {
        LanguageId = languageId, Confidence = 90, RawText = raw, FirstLineSequence = 0, ExceptionType = type, Frames = [],
    };

    [Fact]
    public void TheFirstStepNamesTheFileTheLineAndTheCodeOnIt()
    {
        var file = Path.Combine(_temp.Path, "marks.py");
        File.WriteAllText(file, "scores = []\naverage = sum(scores) / len(scores)\nprint(average)\n");

        var steps = StepByStep.For(Found(file, FindingKind.Runtime, Severity.Error, 2));

        Assert.Equal("Open marks.py and go to line 2: it reads `average = sum(scores) / len(scores)`.", steps[0]);
        Assert.Contains("ran until this line, and stopped here", steps[1]);
        Assert.Contains("when it gets past this point, this crash is fixed", steps[^1]);
    }

    [Fact]
    public void EachKindOfFindingIsSaidToHaveHappenedAsItDid()
    {
        var file = Path.Combine(_temp.Path, "Main.java");

        Assert.Contains("could not make sense of this, so the program did not run", StepByStep.For(Found(file, FindingKind.Syntax, Severity.Error, 3))[1]);
        Assert.Contains("This is a warning, not an error", StepByStep.For(Found(file, FindingKind.Syntax, Severity.Warning, 3))[1]);
        Assert.Contains("was not stopped by it", StepByStep.For(Found(file, FindingKind.Runtime, Severity.Warning, 3))[1]);
        Assert.Contains("still running when its time ran out", StepByStep.For(Found(file, FindingKind.Runtime, Severity.Warning, null, ruleId: "timed-out"))[1]);
        Assert.Contains("not what you said it should print", StepByStep.For(Found(file, FindingKind.Logic, Severity.Error, 3, ruleId: "wrong-output"))[1]);
        Assert.Contains("No single line was named", StepByStep.For(Found(file, FindingKind.Runtime, Severity.Error, null))[0]);
    }

    [Fact]
    public void WordsAreExplainedInTheOrderTheyAppearAndAtMostFive()
    {
        var words = Glossary.In("The loop reads an index one past the end of the list, so the exception stops the program; a stack trace shows the method and its variable and parameter.");

        Assert.Equal(new[] { "loop", "index", "list", "exception", "stack trace" }, words.Select(word => word.Word));
    }

    [Fact]
    public void AWordThatIsAlsoEnglishIsOnlyTakenInTheFormALanguageWritesIt()
    {
        Assert.DoesNotContain(Glossary.In("none of the marks was missing"), word => word.Word == "None");
        Assert.Contains(Glossary.In("the function gave back None"), word => word.Word == "None");
        Assert.DoesNotContain(Glossary.In("the keyword list"), word => word.Word == "key");
    }

    [Fact]
    public void EveryMeaningIsASentenceOfItsOwn()
    {
        Assert.All(Glossary.All, word =>
        {
            Assert.True(word.Meaning.Length > 10, word.Word);
            Assert.True(char.IsUpper(word.Meaning[0]) || word.Meaning[0] == '"', word.Word);
            Assert.EndsWith(".", word.Meaning);
        });
    }

    [Fact]
    public void EveryIdeaHasAHabitOfItsOwn()
    {
        Assert.All(Concepts.All, concept => Assert.True(Habits.HasOwn(concept), concept.Code));
        Assert.Equal(Concepts.All.Count, Concepts.All.Select(Habits.For).Distinct().Count());
    }

    [Fact]
    public void TheLanguagesOwnMessageIsShownOnlyWhereALanguageSaidOne()
    {
        var python = Said("python", "Traceback (most recent call last):\n  File \"marks.py\", line 2, in <module>\nZeroDivisionError: division by zero");
        var crash = MessageReading.For(Found("marks.py", FindingKind.Runtime, Severity.Error, 2, python));

        Assert.NotNull(crash);
        Assert.Equal(("Python", MessagePhase.WhileRunning), (crash.SaidBy, crash.Phase));
        Assert.StartsWith("Traceback", crash.Message);

        Assert.Null(MessageReading.For(Found("marks.py", FindingKind.Logic, Severity.Warning, 2)));
        Assert.Null(MessageReading.For(Found("main.c", FindingKind.Runtime, Severity.Error, 4, Said("generic", "integer divide by zero (0xC0000094)"))));
        Assert.Null(MessageReading.For(Found("main.c", FindingKind.Syntax, Severity.Error, 4, Said("msvc", "main.c(4): error C2065: 'x': undeclared identifier", "compile error"))));
    }

    [Fact]
    public void ALongMessageIsShortenedAndSaysSo()
    {
        var raw = string.Join("\n", Enumerable.Range(1, 30).Select(number => $"line {number}"));
        var read = MessageReading.For(Found("Main.java", FindingKind.Runtime, Severity.Error, 1, Said("java", raw)));

        Assert.NotNull(read);
        Assert.True(read.WasShortened);
        Assert.Equal(MessageReading.MostLinesShown, read.Message.Split('\n').Length);
    }
}

/// <summary>
/// Each language's message, as its own tools print it, has the parts its description says it has: a crash and a mistake
/// that stops it building, run with the language's toolchain the way FixFinder Learn runs a lesson. A language this
/// computer cannot run is passed over, and said to be.
/// </summary>
public class MessageReadingLiveTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string[]> Messages => new()
    {
        { "Python", "x = (1, 2\nprint(x)\n", ["^", "SyntaxError"] },
        { "Python", "print(1 / 0)\n", ["Traceback (most recent call last)", "line 1", "ZeroDivisionError"] },
        { "Java", "public class Main {\n    public static void main(String[] args) {\n        int x = \"a\";\n    }\n}\n", [".java:3: error:"] },
        { "Java", "public class Main {\n    public static void main(String[] args) {\n        System.out.println(1 / 0);\n    }\n}\n", ["java.lang.ArithmeticException", "\tat Main.main(", ".java:3)"] },
        { "C#", "int x = \"a\";\n", ["(1,9): error CS0029"] },
        { "C#", "int zero = 0;\nConsole.WriteLine(1 / zero);\n", ["Unhandled exception. System.DivideByZeroException", "   at ", ":line 2"] },
        { "C", "int main(void) {\n    int x = 1\n    return x;\n}\n", [":3:5: error:"] },
        { "JavaScript", "const x = (1, 2;\n", ["SyntaxError", "node:"] },
        { "JavaScript", "const nothing = undefined;\nconsole.log(nothing.length);\n", ["TypeError", "    at "] },
        { "Go", "package main\n\nfunc main() {\n\tx := \"a\" + 1\n}\n", [".go:4:2:"] },
        { "Go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{1}\n\tfmt.Println(marks[len(marks)])\n}\n", ["panic: runtime error: index out of range", "goroutine 1 [running]:", "+0x"] },
        { "Scala", "object Main {\n  def main(args: Array[String]): Unit = {\n    val x: Int = \"a\"\n  }\n}\n", ["[E007]", ":3:"] },
        { "Scala", "object Main {\n  def main(args: Array[String]): Unit = {\n    val zero = 0\n    println(1 / zero)\n  }\n}\n", ["java.lang.ArithmeticException", "\tat Main$.main(", ".scala:4)"] },
        { "Rust", "fn main() {\n    let total = 10;\n    println!(\"{}\", totl);\n}\n", ["error[E0425]", "--> ", ":3:20", "^", "help:"] },
        { "Rust", "fn main() {\n    let marks = vec![70];\n    println!(\"{}\", marks[marks.len()]);\n}\n", ["thread 'main'", "panicked at ", ":3:", "index out of bounds"] },
        { "Ruby", "def total(marks)\n  marks.sum\n\nputs total([1, 2])\n", ["pasted.rb:4:", "(SyntaxError)", "^"] },
        { "Ruby", "total = 10\nputs totl\n", [":2:in ", "(NameError)", "^^^^", "Did you mean?"] },
        { "OCaml", "let value1 = 3\n\nlet () = print_int (valeu1 + 1)\n", ["line 3, characters", "^", "Error:"] },
        { "OCaml", "let () = failwith \"stop\"\n", ["Fatal error: exception Failure", "Raised at"] },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public async Task TheMessageHasThePartsItsDescriptionNames(string language, string code, string[] parts)
    {
        var codeLanguage = CodeLanguage.All.Single(each => each.Name == language);
        var observed = await SnippetRunner.RunAsync(codeLanguage, code);

        if (!observed.Ran)
        {
            output.WriteLine($"Not run on this computer: {observed.WhyNotRun}");
            return;
        }

        if (observed.Crash is { } windows && windows.Contains("Application Control", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine($"Windows refused to start the program: {windows}");
            return;
        }

        Assert.NotNull(observed.Error);
        output.WriteLine(observed.Error.RawText);

        var phase = observed.BeforeRunning ? MessagePhase.BeforeRunning : MessagePhase.WhileRunning;
        Assert.True(MessageReading.Describes(language, phase), $"{language} {phase} has no description");
        Assert.All(parts, part => Assert.Contains(part, observed.Error.RawText));
    }
}
