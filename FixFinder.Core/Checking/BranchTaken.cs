namespace FixFinder.Core.Checking;

/// <summary>What one name held when a line was reached, as the language writes it.</summary>
public sealed record HeldValue(string Name, string Value);

/// <summary>The kind of line that decided whether the lines under it ran.</summary>
public enum BranchKind
{
    If,

    /// <summary>Python's elif, or another language's else if.</summary>
    ElseIf,

    /// <summary>The branch every condition above it in the same chain was false for; it has no condition of its own.</summary>
    Else,

    While,
}

/// <summary>
/// One decision on the way to the line a program stopped on: an if, elif or while around that line - or the else of such a
/// chain - whether its condition was true or false the last time the call that stopped reached it, and what the names in the
/// condition held then. Whether it was true is what the line it stopped on running means for it; the values are recorded by
/// a run as the condition was reached, never worked out afterwards.
/// </summary>
/// <param name="Code">The line as it is written: <c>if left == 0:</c>.</param>
/// <param name="WasTrue">Whether the condition was true; for an else, whether it ran, which it did.</param>
/// <param name="Values">What each name the condition reads held then, in the order the condition reads them.</param>
public sealed record BranchTaken(int Line, string Code, BranchKind Kind, bool WasTrue, IReadOnlyList<HeldValue> Values);
