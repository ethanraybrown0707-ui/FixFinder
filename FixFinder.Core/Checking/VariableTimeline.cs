namespace FixFinder.Core.Checking;

/// <summary>One value a variable was given, and the line that gave it.</summary>
/// <param name="Line">The line that gave it, or null for a parameter, which held it as its function began.</param>
/// <param name="Value">The value as the language writes it, cut to 60 characters.</param>
/// <param name="CalledFrom">For a parameter, the line the call that gave it was made from, when that is known.</param>
public sealed record VariableChange(int? Line, string Value, int? CalledFrom)
{
    /// <summary>Where the value came from, in words: "line 5", or "the call on line 11".</summary>
    public string GivenBy => Line is { } line ? $"line {line}" : CalledFrom is { } call ? $"the call on line {call}" : "the call";
}

/// <summary>
/// The values one variable held, in the order it was given them, up to the moment the program stopped - recorded while it
/// ran, never worked out afterwards.
/// </summary>
/// <param name="Changes">Each value it was given: another value, or a list, dictionary or set that grew or shrank.</param>
/// <param name="Cut">Whether it was given more values than were kept, so the first of them are not here.</param>
public sealed record VariableHistory(string Name, IReadOnlyList<VariableChange> Changes, bool Cut)
{
    /// <summary>How many values are kept for each variable. A loop over a long list gives a name more than anyone reads.</summary>
    public const int MostKept = 50;

    public string Summary => Changes.Count == 1 ? "1 value" : Cut ? $"the last {Changes.Count} values" : $"{Changes.Count} values";
}
