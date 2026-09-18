using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing;

/// <summary>Recognises one language's crash output.</summary>
public interface IStackTraceParser
{
    string LanguageId { get; }

    string DisplayName { get; }

    int Detect(IReadOnlyList<string> lines);

    ParsedError? Parse(IReadOnlyList<CapturedLine> lines);
}
