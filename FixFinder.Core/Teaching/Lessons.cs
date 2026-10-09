using FixFinder.Core.Teaching.LessonsByConcept;

namespace FixFinder.Core.Teaching;

/// <summary>Every lesson FixFinder Learn has: one for each concept in each language it is taught in.</summary>
public static class Lessons
{
    public static IReadOnlyList<Lesson> All { get; } =
    [
        .. DivisionByZeroLessons.All,
        .. NothingThereLessons.All,
        .. PositionOutOfRangeLessons.All,
        .. MissingKeyLessons.All,
        .. UndefinedNameLessons.All,
        .. WrongTypeLessons.All,
        .. AssignOrCompareLessons.All,
        .. SameValueOrSameObjectLessons.All,
        .. WholeNumberDivisionLessons.All,
        .. OffByOneLessons.All,
        .. TextToNumberLessons.All,
        .. FunctionLessons.All,
        .. MissedCaseLessons.All,
        .. ReadBeforeSetLessons.All,
        .. BlocksLessons.All,
        .. UnclosedPairLessons.All,
        .. StatementEndLessons.All,
        .. EndlessLoopLessons.All,
        .. FallingThroughLessons.All,
        .. ChangingAConstantLessons.All,
        .. ChangingWhileLoopingLessons.All,
        .. NeverUsedLessons.All,
    ];

    /// <summary>The lesson on a concept in a language, or null when that one has not been written.</summary>
    public static Lesson? For(Concept concept, CodeLanguage language) =>
        All.FirstOrDefault(lesson => lesson.Concept.Code == concept.Code && lesson.Language.Name == language.Name);

    /// <summary>Every language a concept is taught in.</summary>
    public static IReadOnlyList<Lesson> Of(Concept concept) => All.Where(lesson => lesson.Concept.Code == concept.Code).ToList();
}
