namespace FixFinder.Core.Teaching;

/// <summary>
/// A change that looks as if it fixes an example and does not, with what it does instead - for a quiz to offer beside the
/// change that does. What it does is checked by running it, like everything else a lesson says code does.
/// </summary>
/// <param name="Code">The whole example with the change made.</param>
/// <param name="Does">What it does when it runs.</param>
/// <param name="WhyNot">Why it is not the fix, in plain words, beyond what running it shows.</param>
public sealed record WrongFix(string Code, Behaviour Does, string WhyNot);

/// <summary>
/// A small program with the mistake in it, the same program put right, and what each does when it runs.
/// </summary>
/// <param name="Title">What it shows: "An average of an empty list".</param>
/// <param name="Broken">The program with the mistake.</param>
/// <param name="BrokenDoes">What it does when it runs, as running it showed.</param>
/// <param name="Fixed">The same program put right.</param>
/// <param name="FixedDoes">What that does when it runs.</param>
/// <param name="WhatChanged">The change, and why it puts the mistake right, in plain words.</param>
public sealed record WorkedExample(string Title, string Broken, Behaviour BrokenDoes, string Fixed, Behaviour FixedDoes, string WhatChanged)
{
    /// <summary>Changes that look like fixes and are not, for a quiz to offer beside the real one.</summary>
    public IReadOnlyList<WrongFix> WrongFixes { get; init; } = [];

    /// <summary>The broken program's lines, numbered from 1 as the language numbers them.</summary>
    public IReadOnlyList<string> BrokenLines => Broken.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}

/// <summary>
/// One concept taught in one language: what that language does about it, examples that show it happening, and questions
/// that check it has been understood.
/// </summary>
/// <param name="Concept">The idea it teaches.</param>
/// <param name="Language">The language it is taught in.</param>
/// <param name="InThisLanguage">What this language does about it, in plain words - each claim one an example here shows.</param>
/// <param name="Examples">Programs that show it, each run by the tests to check what the lesson says of it.</param>
public sealed record Lesson(Concept Concept, CodeLanguage Language, string InThisLanguage, IReadOnlyList<WorkedExample> Examples)
{
    /// <summary>
    /// The questions that check this lesson has been understood - every answer worked out from what running its examples
    /// showed, so a question can never mark a true answer wrong.
    /// </summary>
    public IReadOnlyList<Question> Questions => Quiz.For(this);
}
