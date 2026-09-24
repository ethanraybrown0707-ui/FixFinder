namespace FixFinder.Core.Checking;

/// <summary>What the variables held one time the line was reached.</summary>
/// <param name="Number">Which time this was, counting from 1.</param>
/// <param name="Values">The watched variables, in the same order as <see cref="StateTrace.Columns"/>.</param>
/// <param name="IsWhereItFailed">Whether this is the pass on which the program stopped.</param>
public sealed record StateRow(int Number, IReadOnlyList<string> Values, bool IsWhereItFailed);

/// <summary>
/// What a line actually did, pass by pass: a small table of the variables that decide it, taken from a real run.
/// </summary>
/// <remarks>
/// Every value here was recorded while the program ran, never worked out afterwards and never guessed - which is why
/// there is no trace at all for a program that could not be run, rather than a plausible one. Only the variables the
/// finding is about are kept: a table of everything in scope is a memory dump, and nobody reads it.
/// </remarks>
public sealed record StateTrace
{
    /// <summary>More passes than this and the table stops being something a reader takes in at a glance.</summary>
    public const int MostRowsShown = 25;

    public required int Line { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public required IReadOnlyList<StateRow> Rows { get; init; }

    /// <summary>How many passes there were altogether, which is more than <see cref="Rows"/> when it was cut short.</summary>
    public required int Passes { get; init; }

    public bool WasCut => Passes > Rows.Count;

    public string CutNote => WasCut
        ? Rows.Count == MostRowsShown
            ? $"Showing the first {MostRowsShown} of {Passes} times this line ran."
            : $"Showing {Rows.Count} of {Passes} times this line ran."
        : "";

    /// <summary>
    /// Builds the table from what one run recorded, keeping only the variables asked about, or null when the run
    /// recorded nothing worth a table.
    /// </summary>
    /// <param name="visits">The variables as they stood each time the line was reached, in order.</param>
    /// <param name="about">The variables the finding is about; anything else the run saw is left out.</param>
    /// <param name="failedOnLastPass">Whether the program stopped on this line, which marks the final row.</param>
    public static StateTrace? From(
        int line,
        IReadOnlyList<IReadOnlyDictionary<string, string>> visits,
        IReadOnlyCollection<string> about,
        bool failedOnLastPass,
        bool moreThanWereKept = false)
    {
        if (visits.Count == 0) return null;

        // The columns are the variables the finding is about that the run actually saw. A name the run never had a
        // value for would be an empty column, which says nothing and takes up the width of something that would.
        var columns = about
            .Where(name => visits.Any(visit => visit.ContainsKey(name)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (columns.Count == 0) return null;

        var shown = visits.Take(MostRowsShown).ToList();

        var rows = shown
            .Select((visit, index) => new StateRow(
                index + 1,
                [.. columns.Select(name => visit.GetValueOrDefault(name, ""))],
                failedOnLastPass && index == visits.Count - 1 && !moreThanWereKept))
            .ToList();

        return new StateTrace
        {
            Line = line,
            Columns = columns,
            Rows = rows,
            Passes = moreThanWereKept ? visits.Count + 1 : visits.Count,
        };
    }
}
