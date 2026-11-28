using FixFinder.Core.Checking;
using FixFinder.Core.Parsing;
using FixFinder.Core.Teaching;

namespace FixFinder.Tests.Teaching;

/// <summary>The ideas behind the mistakes FixFinder finds, each finding's idea, and a run judged against what it should show.</summary>
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
    public void EveryConceptHasACodeOfItsOwnATitleAndAnIdea()
    {
        Assert.Equal(Concepts.All.Count, Concepts.All.Select(concept => concept.Code).Distinct().Count());

        foreach (var concept in Concepts.All)
        {
            Assert.Matches("^[A-Z]+$", concept.Code);
            Assert.False(string.IsNullOrWhiteSpace(concept.Title));
            Assert.False(string.IsNullOrWhiteSpace(concept.Idea));
        }
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
        { "sum.ml", "compile error", "compile error", null, "The value count has type int but an expression was expected of type\nfloat", "TYPES" },
        { "sum.ml", "compile error", "compile error", null, "The constant 2.5 has type float but an expression was expected of type\nint", "TYPES" },
        { "app.py", "ValueError", "ValueError", null, "math domain error", "GENERAL" },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public void AFindingIsTaughtAsTheConceptItIsAnExampleOf(string file, string ruleId, string? type, string? code, string? message, string concept)
    {
        var error = type is null && code is null && message is null ? null : Error("any", type, code, message);

        Assert.Equal(concept, ConceptMap.Of(Found(file, ruleId, error)).Code);
    }

    /// <summary>An error the language names no line for is said to have none, and only a run that names none shows it.</summary>
    [Fact]
    public void AnErrorWithNoLineIsSaidAndCheckedAsHavingNone()
    {
        var expected = Behaviour.StoppingOnNoLine("Division_by_zero");
        var error = new ParsedError { LanguageId = "ocaml", Confidence = 90, RawText = "", FirstLineSequence = 0, ExceptionType = "Division_by_zero", Frames = [] };

        Assert.Equal("It stops with Division_by_zero, and OCaml does not say on which line", expected.Described("OCaml"));
        Assert.True(new Observation { Ran = true, Error = error }.Shows(expected));
        Assert.False(new Observation { Ran = true, Error = error, Line = 3 }.Shows(expected));
        Assert.False(new Observation { Ran = true, Error = error }.Shows(Behaviour.Stopping("Division_by_zero", 3)));
    }
}
