using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Parsing;
using FixFinder.Core.Teaching;

namespace FixFinder.Tests.Teaching;

/// <summary>The concepts, the codes that open their lessons, the questions lessons ask, and the problems kept for FixFinder Learn.</summary>
public class TeachingTests
{
    private static Finding Found(string file, string ruleId, ParsedError? error = null, int? line = null) => new()
    {
        Kind = FindingKind.Runtime,
        Severity = Severity.Error,
        Confidence = Confidence.Certain,
        File = file,
        Line = line,
        Title = "Title",
        Explanation = "What is wrong.",
        WhyItMatters = "Why it matters.",
        SuggestedFix = "How to fix it.",
        CorrectedExample = "",
        RuleId = ruleId,
        Error = error,
    };

    private static ParsedError Error(string language, string? type = null, string? code = null, string? message = null) => new()
    {
        LanguageId = language, Confidence = 90, RawText = "", FirstLineSequence = 0, Frames = [],
        ExceptionType = type, ErrorCode = code, Message = message,
    };

    [Fact]
    public void EveryConceptHasACodeThatCanStandInAProblemCode()
    {
        Assert.Equal(Concepts.All.Count, Concepts.All.Select(concept => concept.Code).Distinct().Count());

        foreach (var concept in Concepts.All)
        {
            Assert.Matches("^[A-Z]+$", concept.Code);
            Assert.False(string.IsNullOrWhiteSpace(concept.Title));
            Assert.False(string.IsNullOrWhiteSpace(concept.Idea));
        }
    }

    [Fact]
    public void AProblemCodeSaysTheLanguageAndTheConceptAndReadsBackAsItself()
    {
        var code = ProblemCode.For(CodeLanguage.Python, Concepts.DivisionByZero, "marks.py:2:ZeroDivisionError:Title|average = total / count");

        Assert.Matches("^FF-PY-DIVZERO-[0-9A-HJKMNP-TV-Z]{7}$", code.Text);
        Assert.Equal(code, ProblemCode.Read(code.Text).Code);
    }

    [Fact]
    public void TheSameProblemOnTheSameLineHasTheSameCodeEveryTime()
    {
        var once = ProblemCode.For(CodeLanguage.Java, Concepts.UndefinedName, "Marks.java:7:cannot find symbol|totl += mark;");
        var again = ProblemCode.For(CodeLanguage.Java, Concepts.UndefinedName, "Marks.java:7:cannot find symbol|totl += mark;");
        var changedLine = ProblemCode.For(CodeLanguage.Java, Concepts.UndefinedName, "Marks.java:7:cannot find symbol|total += mark;");

        Assert.Equal(once.Text, again.Text);
        Assert.NotEqual(once.Text, changedLine.Text);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{1}")]
    [InlineData("  {0}  ")]
    [InlineData("{2}")]
    public void ACodeIsReadHoweverItIsTypedIn(string typed)
    {
        var code = ProblemCode.For(CodeLanguage.CSharp, Concepts.NothingThere, "Program.cs:12:NullReferenceException|name.Length");
        var withoutPrefix = code.Text["FF-".Length..];

        var reading = ProblemCode.Read(string.Format(typed, code.Text, code.Text.ToLowerInvariant(), withoutPrefix.Replace('-', ' ')));

        Assert.Null(reading.Problem);
        Assert.Equal(code, reading.Code);
    }

    [Fact]
    public void AChangedCheckLetterIsNoticed()
    {
        var code = ProblemCode.For(CodeLanguage.Go, Concepts.PositionOutOfRange, "main.go:9:index out of range|fmt.Println(items[3])");
        var last = code.Text[^1];
        var slipped = code.Text[..^1] + (last == 'A' ? 'B' : 'A');

        var reading = ProblemCode.Read(slipped);

        Assert.Null(reading.Code);
        Assert.Contains("slip", reading.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FF-RUBY-DIVZERO-K7Q2MXA", "RUBY is not a language FixFinder checks")]
    [InlineData("FF-PY-SPELLING-K7Q2MXA", "SPELLING is not a kind of mistake")]
    [InlineData("FF-PY-DIVZERO", "three parts after FF")]
    [InlineData("FF-PY-DIVZERO-K7Q2", "seven letters and digits")]
    public void ACodeThatCannotBeReadSaysWhy(string typed, string said)
    {
        var reading = ProblemCode.Read(typed);

        Assert.Null(reading.Code);
        Assert.Contains(said, reading.Problem, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string?, string?, string?, string> Mapped => new()
    {
        { "marks.py", "analysis-division-by-zero", null, null, null, "DIVZERO" },
        { "marks.py", "ZeroDivisionError", "ZeroDivisionError", null, "division by zero", "DIVZERO" },
        { "Marks.java", "ArithmeticException", "java.lang.ArithmeticException", null, "/ by zero", "DIVZERO" },
        { "app.py", "TypeError", "TypeError", null, "'NoneType' object is not subscriptable", "NOTHING" },
        { "app.py", "TypeError", "TypeError", null, "area() missing 1 required positional argument: 'height'", "ARGS" },
        { "app.py", "TypeError", "TypeError", null, "can only concatenate str (not \"int\") to str", "TYPES" },
        { "Marks.java", "compile error", "compile error", null, "cannot find symbol", "NAMES" },
        { "Program.cs", "CS0103", "compile error", "CS0103", "The name 'totl' does not exist in the current context", "NAMES" },
        { "app.js", "TypeError", "TypeError", null, "Cannot read properties of undefined (reading 'length')", "NOTHING" },
        { "app.js", "TypeError", "TypeError", null, "Assignment to constant variable.", "CONSTANT" },
        { "main.go", "runtime error", "runtime error", null, "runtime error: index out of range [3] with length 3", "INDEX" },
        { "main.c", "compile error", "compile error", null, "expected ';' before 'return'", "ENDINGS" },
        { "app.py", "ValueError", "ValueError", null, "math domain error", "GENERAL" },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public void AFindingIsTaughtAsTheConceptItIsAnExampleOf(string file, string ruleId, string? type, string? code, string? message, string concept)
    {
        var error = type is null && code is null && message is null ? null : Error("any", type, code, message);

        Assert.Equal(concept, ConceptMap.Of(Found(file, ruleId, error)).Code);
    }

    [Fact]
    public void EveryLessonIsOfAKnownConceptAndEachConceptIsTaughtOnceInEachLanguage()
    {
        Assert.NotEmpty(Lessons.All);
        Assert.Equal(Lessons.All.Count, Lessons.All.Select(lesson => (lesson.Concept.Code, lesson.Language.Name)).Distinct().Count());

        foreach (var lesson in Lessons.All)
        {
            Assert.Contains(lesson.Concept, Concepts.All);
            Assert.True(ProblemCode.Names(lesson.Language), $"{lesson.Language.Name} has no short name for problem codes");
            Assert.NotEmpty(lesson.Examples);
            Assert.False(string.IsNullOrWhiteSpace(lesson.InThisLanguage));
        }
    }

    /// <summary>Every question has exactly one right answer, at least one wrong one, and something to say about each.</summary>
    [Fact]
    public void EveryQuestionHasOneRightAnswerAndSaysWhy()
    {
        foreach (var lesson in Lessons.All)
        {
            foreach (var question in lesson.Questions)
            {
                Assert.Single(question.Answers, answer => answer.IsRight);
                Assert.True(question.Answers.Count >= 2, $"{lesson.Concept.Code} in {lesson.Language.Name}: \"{question.Asks}\" offers one answer");
                Assert.Equal(question.Answers.Count, question.Answers.Select(answer => answer.Text).Distinct().Count());
                Assert.All(question.Answers, answer => Assert.False(string.IsNullOrWhiteSpace(answer.Because)));
            }
        }
    }

    /// <summary>The right answer to "what happens" is what the lesson's running of the program showed, and to "which line", its line.</summary>
    [Fact]
    public void TheRightAnswersAreWhatRunningTheProgramsShowed()
    {
        var lesson = Lessons.For(Concepts.DivisionByZero, CodeLanguage.Python)!;
        var example = lesson.Examples[0];

        var whatHappens = lesson.Questions.First(question => question.Asks == "What happens when this program runs?");
        Assert.Equal(example.BrokenDoes.Described("Python"), whatHappens.Right.Text);

        var whichLine = lesson.Questions.First(question => question.Asks.StartsWith("Which line", StringComparison.Ordinal));
        Assert.StartsWith($"Line {example.BrokenDoes.OnLine}:", whichLine.Right.Text, StringComparison.Ordinal);

        var whichFix = lesson.Questions.First(question => question.Asks.EndsWith("Which of these puts it right?", StringComparison.Ordinal));
        Assert.Equal(example.Fixed, whichFix.Right.Text);
    }

    [Fact]
    public void AProblemIsKeptUnderItsCodeAndFoundAgain()
    {
        using var temp = new TempFolder();
        var program = Path.Combine(temp.Path, "marks.py");
        File.WriteAllLines(program, ["scores = []", "average = sum(scores) / len(scores)", "print(average)"]);

        var store = new ProblemStore(Path.Combine(temp.Path, "store"));
        var finding = Found(program, "ZeroDivisionError", Error("python", "ZeroDivisionError", message: "division by zero"), line: 2);

        Assert.Equal(1, store.Keep([finding], DateTimeOffset.Now));

        var code = ProblemStore.CodeFor(finding)!;
        var kept = Assert.Single(store.Find(code));

        Assert.Equal("average = sum(scores) / len(scores)", kept.LineOfCode);
        Assert.Equal("What is wrong.", kept.Explanation);
        Assert.Equal(code.Text, kept.Code);
    }

    [Fact]
    public void ACodeFromAnotherComputerFindsNothingKeptHere()
    {
        using var temp = new TempFolder();
        var store = new ProblemStore(Path.Combine(temp.Path, "store"));
        var code = ProblemCode.For(CodeLanguage.Python, Concepts.DivisionByZero, "somewhere else");

        Assert.Empty(store.Find(code));
    }

    [Fact]
    public void OnlyTheMostRecentProblemsAreKept()
    {
        using var temp = new TempFolder();
        var store = new ProblemStore(Path.Combine(temp.Path, "store"), mostKept: 3);

        for (var number = 1; number <= 5; number++)
        {
            var program = Path.Combine(temp.Path, $"program{number}.py");
            File.WriteAllText(program, "print(1 / 0)\n");
            store.Keep([Found(program, "ZeroDivisionError", Error("python", "ZeroDivisionError"), line: 1)], DateTimeOffset.Now.AddMinutes(number));
        }

        Assert.Equal(3, Directory.GetFiles(store.Folder, "*.json").Length);
        Assert.Equal(3, store.Recent(10).Count);
    }

    [Fact]
    public void ForgettingDeletesEveryProblemKept()
    {
        using var temp = new TempFolder();
        var store = new ProblemStore(Path.Combine(temp.Path, "store"));
        var program = Path.Combine(temp.Path, "marks.py");
        File.WriteAllText(program, "print(1 / 0)\n");
        store.Keep([Found(program, "ZeroDivisionError", Error("python", "ZeroDivisionError"), line: 1)], DateTimeOffset.Now);

        store.Forget();

        Assert.Empty(store.Recent(10));
        Assert.False(Directory.Exists(store.Folder));
    }
}
