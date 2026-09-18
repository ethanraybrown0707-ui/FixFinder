using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing;

/// <summary>Implemented by parsers whose output can hold several errors that are independent of each other.</summary>
public interface IMultiErrorParser
{
    IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines);
}
