using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing;

/// <summary>
/// Recognises one language's crash output.
/// </summary>
/// <remarks>
/// Split into a cheap <see cref="Detect"/> and a full <see cref="Parse"/> so that
/// <see cref="ParserRegistry"/> can ask every parser about every run without doing ten full
/// parses. Detect is called on the hot path; Parse is called at most three times.
/// </remarks>
public interface IStackTraceParser
{
    /// <summary>Stable id used in fixtures, logs and search queries. Never localise it.</summary>
    string LanguageId { get; }

    /// <summary>Human-readable name for the UI.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Cheap 0-100 guess that this parser owns <paramref name="lines"/>.
    /// </summary>
    /// <remarks>
    /// Must not throw and must stay cheap - it runs for every parser on every run. Returning a
    /// confident score is fine; being wrong is recoverable, because the registry falls through
    /// to the next-best parser when <see cref="Parse"/> then returns null.
    /// </remarks>
    int Detect(IReadOnlyList<string> lines);

    /// <summary>
    /// Full parse, or null when <see cref="Detect"/> was optimistic and the structure is not there.
    /// </summary>
    ParsedError? Parse(IReadOnlyList<CapturedLine> lines);
}
