namespace FixFinder.Core.Checking;

/// <summary>One step of the way a value went through a program: the function, how many calls deep, the line, and what happened there.</summary>
/// <param name="Depth">How many calls deep, counting from the first function the path shows: 0 for it, 1 for a function it called.</param>
/// <param name="Function">The function the line is in - <c>find_member</c> - or &lt;module&gt; for the file's own top level.</param>
/// <param name="Happened">What the line did, in the program's own terms: "calls `find_member(wanted)`", "gives back None: `return None`".</param>
public sealed record PathStep(int Depth, string Function, int Line, string Happened)
{
    /// <summary>The name of the file the line is in, for a path that goes through more than one of the program's files; null for one that stays in one.</summary>
    public string? File { get; init; }
}

/// <summary>
/// Where the value a program stopped on came from: the line that gave it that value, and how - each part of it seen in a
/// run of the program that recorded it as it happened, never worked out afterwards.
/// </summary>
/// <remarks>
/// A program stops on the line that uses a missing value, which is seldom the line that is wrong. This names the line
/// that is: a function that found nothing and gave back None, a name set to None that nothing set again, a lookup for a
/// key that is not there.
/// </remarks>
public sealed record RootCause
{
    /// <summary>What held the value where the program stopped: <c>member</c>, or <c>find_member(members, 2)</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The value itself, as the language writes it: None.</summary>
    public required string Value { get; init; }

    /// <summary>The line the program stopped on.</summary>
    public required int CrashLine { get; init; }

    /// <summary>The line that gave <see cref="Name"/> its value - where the program went wrong.</summary>
    public required int IntroducedLine { get; init; }

    /// <summary>That line, as it is written.</summary>
    public required string IntroducedCode { get; init; }

    /// <summary>What happened, in the program's own terms and in the order it happened, a sentence each.</summary>
    public required IReadOnlyList<string> Steps { get; init; }

    /// <summary>What the run recorded that shows it: the list under "Why FixFinder thinks this".</summary>
    public required IReadOnlyList<string> Evidence { get; init; }

    /// <summary>
    /// The lines the value came through, the root cause first: the line that gave it, and for a value a function gave back,
    /// that function's first line and the line its call ended on. A finding on one of these is what this one follows from.
    /// </summary>
    public required IReadOnlyList<int> LinesInvolved { get; init; }

    /// <summary>What to do about it, in the program's own terms - said whether or not a change was worked out.</summary>
    public required string WhatToDo { get; init; }

    /// <summary>
    /// Whether the line that gave the value is itself the one to change - <c>items = items.sort()</c> - so that making the
    /// line that stopped skip the value would only hide the mistake.
    /// </summary>
    public bool ChangeTheCause { get; init; }

    /// <summary>
    /// The way the value went, in the order the run took it - each call that made it or passed it on, the line that gave it
    /// back, the line that set it, the line that stopped - or empty when the run did not record enough to say.
    /// </summary>
    public IReadOnlyList<PathStep> Path { get; init; } = [];

    /// <summary>Where it stopped and where it went wrong, in one line.</summary>
    public string Summary => IntroducedLine == CrashLine
        ? $"Crash on line {CrashLine}, where {Name} is {Value}."
        : $"Crash on line {CrashLine}. Root cause introduced on line {IntroducedLine}.";

    /// <summary>The steps as one paragraph.</summary>
    public string Cause => string.Join(" ", Steps);
}
