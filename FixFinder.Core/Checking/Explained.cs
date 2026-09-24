namespace FixFinder.Core.Checking;

/// <summary>How much a reader already knows, which decides only how a finding is worded.</summary>
public enum ExplanationLevel
{
    /// <summary>Plain words, with the ideas behind the mistake spelled out.</summary>
    Beginner,

    /// <summary>The middle, and what FixFinder says when nobody has chosen: some programming known, the reasoning shown.</summary>
    Student,

    /// <summary>Short and exact, in the terms someone who works on this every day would use.</summary>
    Technical,
}

/// <summary>
/// One explanation written at three depths.
/// </summary>
/// <remarks>
/// The same facts each time: what is wrong, where, and why. Only the words change, because the level a reader chooses
/// says something about the reader and nothing about the program. Where a guide has only one wording, that wording is
/// given at every level rather than being padded out or cut down into something FixFinder cannot stand behind.
/// </remarks>
public sealed record Explained(string Beginner, string Student, string Technical)
{
    public string At(ExplanationLevel level) => level switch
    {
        ExplanationLevel.Beginner => Beginner,
        ExplanationLevel.Technical => Technical,
        _ => Student,
    };

    /// <summary>Whether the wording actually differs by level, or the one explanation is all there is.</summary>
    public bool VariesByLevel => !(Beginner == Student && Student == Technical);

    /// <summary>The middle wording, with the other two where they have been written and the middle where they have not.</summary>
    public static Explained Of(string student, string? beginner = null, string? technical = null) =>
        new(Blank(beginner) ? student : beginner!, student, Blank(technical) ? student : technical!);

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);
}
