using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing;

/// <summary>
/// Implemented by parsers whose output can hold several errors that are independent of each other.
/// </summary>
/// <remarks>
/// Only compilers, and the distinction is not a technicality. A program that crashes has exactly
/// one error, because the first one ended it - anything behind it is unreachable until that one
/// is fixed, and no amount of parsing will reveal it. A compiler does not stop: it reports
/// everything it found and exits, so the second diagnostic is genuinely there to be looked up
/// while the first is still unfixed.
/// <para>
/// That is what makes skipping possible for one and impossible for the other, which is why the
/// capability is declared by the parsers that really have it rather than assumed of all of them.
/// A window offering to move past an error it cannot move past would be worse than not offering.
/// </para>
/// <para>
/// Chained exceptions are not this. A <c>Caused by</c> or <c>__cause__</c> chain is one failure
/// described at several depths, and it belongs in <see cref="ParsedError.Causes"/> where the
/// fingerprinter can pick the root; these are separate failures that happen to share a run.
/// </para>
/// </remarks>
public interface IMultiErrorParser
{
    /// <summary>
    /// Every independent error in the output, in the order the tool reported them.
    /// </summary>
    /// <remarks>
    /// The first entry must be what <see cref="IStackTraceParser.Parse"/> returns, so that
    /// skipping starts from the error the user was already shown.
    /// </remarks>
    IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines);
}
