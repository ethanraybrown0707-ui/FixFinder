using FixFinder.Core.Analysis.Abstract;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Settles, path by path, what abstract interpretation could only call possible. A line that can fail gets the inputs
/// that make it fail; a line no path can make fail - once every path has been followed - is dropped as a false alarm;
/// and a line that some path is certain to fail on is reported even where the joined-up abstract values could not see it.
/// </summary>
public static class SymbolicChecks
{
    public const string FoundBy = "symbolic execution";

    public const string Confirmed = "abstract interpretation, confirmed by symbolic execution";

    private static readonly HashSet<string> Settled = new(StringComparer.Ordinal)
    {
        "analysis-division-by-zero", "analysis-null-used", "analysis-index-out-of-range", "analysis-empty-collection",
    };

    public static IEnumerable<AnalysisFinding> Refine(ControlFlowGraph graph, Evaluator evaluator, IReadOnlyList<AnalysisFinding> found, SourceText source)
    {
        var report = new SymbolicExecutor(graph, evaluator.Language, evaluator.Locals ?? new HashSet<string>(), evaluator.Volatile, evaluator.DeclaredTypes)
        {
            MayReturnNull = call => evaluator.CallReturns?.Invoke(call) is { MayBeNull: true },
        }.Explore();
        var refined = new List<AnalysisFinding>();

        foreach (var finding in found)
        {
            if (!Settled.Contains(finding.CheckId))
            {
                refined.Add(finding);
                continue;
            }

            var mayDrop = report.Complete && finding.Confidence == Confidence.Possible;

            if (report.Outcomes.TryGetValue((finding.CheckId, finding.Span), out var outcome))
            {
                if (outcome.CanFail)
                    refined.Add(outcome.Witness is { Facts.Count: > 0 } witness
                        ? finding with { Witness = witness.ToString(), WitnessValues = witness.Values, FoundBy = Confirmed }
                        : finding);
                else if (!(mayDrop && !outcome.Unsure))
                    refined.Add(finding);
            }
            else if (!(mayDrop && !report.Evaluated.Contains(finding.Span)))
            {
                refined.Add(finding);
            }
        }

        foreach (var ((check, span), outcome) in report.Outcomes)
        {
            if (!(outcome.Forced || outcome.FromInput) || found.Any(f => f.CheckId == check && f.Span.File == span.File && f.Span.Line == span.Line)) continue;

            refined.Add(new AnalysisFinding(check, span, Message(check, span, outcome, evaluator.Language, source), Severity.Error,
                outcome.Forced ? Confidence.Likely : Confidence.Possible, FindingKind.Runtime, FoundBy)
            {
                Witness = outcome.Witness is { Facts.Count: > 0 } witness ? witness.ToString() : null,
                WitnessValues = outcome.Witness?.Values,
            });
        }

        return refined;
    }

    private static string Message(string check, SourceSpan span, Outcome outcome, SourceLanguage language, SourceText source) =>
        Failing(check, span, outcome, language, source) + SharedWith(outcome);

    /// <summary>
    /// When other names hold the same list on the failing path - b after b = a - a change made through one of them may be
    /// why this one is empty, and saying so turns a puzzling finding into an obvious one.
    /// </summary>
    private static string SharedWith(Outcome outcome)
    {
        if (outcome.Collection is not { } name || outcome.SharedWith.Count == 0) return "";

        var named = string.Join(" and ", outcome.SharedWith.Select(other => $"`{other}`"));
        return outcome.SharedWith.Count == 1
            ? $". {named} is the same list as `{name}`, so a change made through {named} is made to `{name}` too"
            : $". {named} are the same list as `{name}`, so a change made through any of them is made to `{name}` too";
    }

    private static string Failing(string check, SourceSpan span, Outcome outcome, SourceLanguage language, SourceText source)
    {
        string Quote(Expr expression) => source.Of(expression.Span) is { Length: > 0 } text ? text : IrText.Of(expression);

        var whole = source.Of(span) is { Length: > 0 } text ? text : "this";
        var culprit = outcome.Culprit is { } value ? Quote(value) : "the value";
        var none = Failures.Nothing(language);

        if (!outcome.Forced)
        {
            var when = outcome.Witness?.ToString() ?? "for some input";
            return check switch
            {
                "analysis-division-by-zero" => $"`{culprit}` is 0 when {when}, so `{whole}` fails with {Failure(language, "zero")}",
                "analysis-index-out-of-range" => $"`{culprit}` is outside the list when {when}, so `{whole}` fails with {Failure(language, "index")}",
                "analysis-null-used" => $"`{culprit}` is {none} when {when}, so `{whole}` fails with {Failure(language, "null")}",
                _ => $"`{culprit}` is empty when {when}, so `{whole}` fails with {Failure(language, "empty")}",
            };
        }

        return check switch
        {
            "analysis-division-by-zero" => $"On one way through the code `{culprit}` is always 0 here, so `{whole}` fails with {Failure(language, "zero")}",
            "analysis-index-out-of-range" => $"On one way through the code `{culprit}` is always outside the list here, so `{whole}` fails with {Failure(language, "index")}",
            "analysis-null-used" => $"On one way through the code `{culprit}` is always {none} here, so `{whole}` fails with {Failure(language, "null")}",
            _ => $"On one way through the code `{culprit}` is always empty here, so `{whole}` fails with {Failure(language, "empty")}",
        };
    }

    private static string Failure(SourceLanguage language, string kind) => kind switch
    {
        "zero" => Failures.DividingByZero(language),
        "index" => Failures.OutsideTheList(language),
        "null" => language == SourceLanguage.Python ? "AttributeError or TypeError" : Failures.UsingNothing(language),
        _ => Failures.TakingFromEmpty(language),
    };
}
