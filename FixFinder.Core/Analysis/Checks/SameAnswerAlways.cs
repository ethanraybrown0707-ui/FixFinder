using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// A yes-or-no function that gives the same answer whatever it is asked about: named as a question - is_prime, hasSpace,
/// IsValid - with two or more returns, every one of them gives back the same true, or the same false, and it cannot
/// finish any other way. Asking it tells the caller nothing; most often one of its returns was meant to give the other
/// answer, as in an is_prime that returns True when it finds a divisor as well as at its end.
/// </summary>
/// <remarks>
/// A function whose only return is a constant is a placeholder, or a default a class is meant to override - readable()
/// returning False in a base class - and is left alone: it takes two returns, both reached by different ways through the
/// function, for the sameness to be a mistake rather than a choice. A function that can reach its end without a return -
/// giving back None, or undefined - gives a second answer, and is not one; so its last statement has to be a return or a
/// throw. Returns of a nested function, or a lambda, are that function's own.
/// </remarks>
internal static partial class SameAnswerAlways
{
    public const string CheckId = "analysis-same-answer-always";

    /// <summary>A name that asks a yes-or-no question: is_ or isX or IsX, and the like - has, can, should, contains.</summary>
    [GeneratedRegex(@"^(?:(?:is|has|can|should|contains|was|are|does|needs|allows|matches)_|(?:is|has|can|should|contains|was|are|does|needs|allows|matches)[A-Z0-9]|(?:Is|Has|Can|Should|Contains|Was|Are|Does|Needs|Allows|Matches)[A-Z0-9])")]
    private static partial Regex AsksAQuestion();

    public static IEnumerable<AnalysisFinding> Find(IrProgram program, IEnumerable<IrFunction> functions, string foundBy)
    {
        foreach (var function in functions)
        {
            if (function.Name == IrFunction.ModuleBody || !AsksAQuestion().IsMatch(function.Name)) continue;
            if (function.Body.Count == 0 || function.Body[^1] is not (Return or Throw)) continue;

            var returns = IrWalk.Statements(function.Body).OfType<Return>().ToList();
            if (returns.Count < 2) continue;
            if (returns.Select(Answer).Distinct().ToList() is not [bool answer]) continue;

            var word = Written(answer, program.Language);
            var lines = returns.Select(each => each.Span.Line).Distinct().OrderBy(line => line).ToList();
            var where = lines.Count switch
            {
                1 => $"line {lines[0]}",
                2 => $"lines {lines[0]} and {lines[1]}",
                _ => $"lines {string.Join(", ", lines.SkipLast(1))} and {lines[^1]}",
            };

            yield return new AnalysisFinding(CheckId, function.Span,
                $"`{function.Name}` always says {word}, whatever it is asked about: each of its {returns.Count} returns gives back {word} - on {where} - " +
                "so asking it tells the caller nothing",
                Severity.Warning, Confidence.Likely, FindingKind.Logic, foundBy)
            {
                Function = function.Name,
            };
        }
    }

    /// <summary>What a return gives back, when it is true or false written out; null for anything else, which ends the matter.</summary>
    private static bool? Answer(Return leave) => leave.Value is Literal { Kind: LiteralKind.Boolean, Value: bool answer } ? answer : null;

    /// <summary>True or false as the language writes it: True in Python, true in the rest.</summary>
    private static string Written(bool answer, SourceLanguage language) => language == SourceLanguage.Python
        ? (answer ? "True" : "False")
        : (answer ? "true" : "false");
}
