namespace FixFinder.Core.Teaching;

/// <summary>
/// What a piece of code does when it is run, as running it showed: what it printed, or the error it stopped with and the
/// line it stopped on. Every behaviour a lesson states is one its tests saw happen, with the language's own toolchain.
/// </summary>
public sealed record Behaviour
{
    /// <summary>Everything it printed, line by line, when it ran to the end; null when it stopped with an error.</summary>
    public string? Prints { get; init; }

    /// <summary>
    /// The error it stopped with, as the language names it: an exception's type (ZeroDivisionError), a compiler's code
    /// (CS0103), or how the compiler's message starts (cannot find symbol). Null when it ran to the end.
    /// </summary>
    public string? StopsWith { get; init; }

    /// <summary>The line of the example the error is on; null when the language names no line for it.</summary>
    public int? OnLine { get; init; }

    /// <summary>Whether the language refused the code before any of it ran - a compiler's error, or a SyntaxError.</summary>
    public bool BeforeRunning { get; init; }

    /// <summary>Whether it was still running when its time ran out - a loop that never ends.</summary>
    public bool NeverFinishes { get; init; }

    /// <summary>
    /// What Windows said when it stopped the program with no error of the language's own - "integer divide by zero" - for C
    /// and C++, whose mistakes often end that way.
    /// </summary>
    public string? CrashesWith { get; init; }

    public static Behaviour Printing(string printed) => new() { Prints = printed };

    public static Behaviour Stopping(string error, int line) => new() { StopsWith = error, OnLine = line };

    /// <summary>
    /// Stopping with an error the language names no line for - as OCaml's bytecode names none for a whole number divided by
    /// zero outside any function.
    /// </summary>
    public static Behaviour StoppingOnNoLine(string error) => new() { StopsWith = error };

    public static Behaviour Refused(string error, int line) => new() { StopsWith = error, OnLine = line, BeforeRunning = true };

    public static Behaviour Running() => new() { NeverFinishes = true };

    public static Behaviour Crashing(string meaning) => new() { CrashesWith = meaning };

    /// <summary>Whether the code does what it is for: runs to the end, rather than stopping, crashing or running for ever.</summary>
    public bool Finishes => StopsWith is null && CrashesWith is null && !NeverFinishes;

    /// <summary>
    /// What it does, as a sentence a quiz option or an explanation can use: "It prints 2.5", "It stops with ZeroDivisionError
    /// on line 2", "The compiler refuses it: cannot find symbol on line 3".
    /// </summary>
    public string Described(string language) => this switch
    {
        { NeverFinishes: true } => "It never finishes - it is still running when it is stopped",
        { CrashesWith: { } meaning } => $"It crashes: Windows stops it with {meaning}",
        { StopsWith: { } error, BeforeRunning: true } => $"{RefusedBy(language)} refuses it before it runs: {error} on line {OnLine}",
        { StopsWith: { } error, OnLine: null } => $"It stops with {error}, and {language} does not say on which line",
        { StopsWith: { } error } => $"It stops with {error} on line {OnLine}",
        { Prints: { Length: > 0 } printed } => printed.Contains('\n') ? $"It prints:\n{printed}" : $"It prints {printed}",
        _ => "It runs to the end and prints nothing",
    };

    /// <summary>Who refuses code before it runs: the compiler, or - for Python and JavaScript, which have none - the language.</summary>
    private static string RefusedBy(string language) => language is "Python" or "JavaScript" ? language : $"The {language} compiler";
}
