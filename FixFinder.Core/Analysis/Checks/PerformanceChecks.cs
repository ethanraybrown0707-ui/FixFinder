using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Work a program does more times than it needs to.
/// </summary>
/// <remarks>
/// Held to a high bar on purpose. A warning that a program might be slow is worth nothing unless the reader can see
/// why, so only patterns whose cost grows with the data are reported - searching a whole list once per item, and the
/// like - and never anything whose benefit is a matter of taste or of a compiler's mood. A slow program that is right
/// is still right, so none of this is an error: what is reported is a suggestion, and it says what was noticed rather
/// than promising a speed-up nobody has measured.
/// </remarks>
public static class PerformanceChecks
{
    public const string FoundBy = "looking at how the work grows with the data";

    /// <summary>Methods that look through a collection from one end until they find what they were given.</summary>
    private static readonly HashSet<string> SearchesFromTheStart = new(StringComparer.Ordinal)
    {
        "Contains", "IndexOf", "LastIndexOf", "indexOf", "lastIndexOf", "includes", "index", "count", "find", "Find", "remove", "Remove",
    };

    public static IReadOnlyList<AnalysisFinding> Run(IrProgram program, SourceText source)
    {
        var findings = new List<AnalysisFinding>();

        foreach (var function in program.AllFunctions)
        {
            Walk(function.Body, inLoop: null, findings, source);
        }

        return findings
            .GroupBy(finding => (finding.CheckId, finding.Span.File, finding.Span.Line))
            .Select(group => group.First())
            .ToList();
    }

    /// <param name="inLoop">The innermost loop this statement sits in, or null at the top of a function.</param>
    private static void Walk(IReadOnlyList<Stmt> body, Stmt? inLoop, List<AnalysisFinding> findings, SourceText source)
    {
        foreach (var statement in body)
        {
            switch (statement)
            {
                case ForEach loop:
                    Inspect(loop.Body, loop, findings, source);
                    Walk(loop.Body, loop, findings, source);
                    Walk(loop.Else, inLoop, findings, source);
                    break;

                case For loop:
                    Inspect(loop.Body, loop, findings, source);
                    Walk(loop.Body, loop, findings, source);
                    Walk(loop.Setup, inLoop, findings, source);
                    Walk(loop.Step, loop, findings, source);
                    break;

                case While loop:
                    Inspect(loop.Body, loop, findings, source);
                    Walk(loop.Body, loop, findings, source);
                    Walk(loop.Else, inLoop, findings, source);
                    break;

                default:
                    // Anything else may still hold loops - an if, a try, a with - and Statements reaches them all.
                    foreach (var nested in IrWalk.Statements([statement]).Skip(1))
                    {
                        if (nested is ForEach or For or While) Walk([nested], inLoop, findings, source);
                    }
                    break;
            }
        }
    }

    /// <summary>Looks through one loop's body for a search of a collection that the loop itself does not change.</summary>
    private static void Inspect(IReadOnlyList<Stmt> body, Stmt loop, List<AnalysisFinding> findings, SourceText source)
    {
        var changed = Assigned(body);

        // Everything the loop runs, however deeply nested: a search inside an if is still a search on every pass.
        foreach (var statement in IrWalk.Statements(body))
        {
            foreach (var expression in IrWalk.Expressions(statement))
            {
                if (Searched(expression) is not { } search) continue;

                // A collection the loop itself builds or replaces is a different collection each pass, and turning
                // that into a set would change what the program does rather than how long it takes.
                if (changed.Contains(search.Collection)) continue;

                findings.Add(new AnalysisFinding(
                    "analysis-repeated-search",
                    expression.Span,
                    $"`{search.Collection}` is searched from the start on every pass of the loop that begins on line {loop.Span.Line}, " +
                    "so the work grows with both the loop and the collection",
                    Severity.Suggestion,
                    Confidence.Likely,
                    FindingKind.Performance,
                    FoundBy));
            }
        }
    }

    /// <summary>The collection a search looks through, when the expression is a search of one by name.</summary>
    private static (string Collection, Expr Where)? Searched(Expr expression) => expression switch
    {
        // Python's `x in items`, and the same written the other way round.
        Binary { Operator: BinaryOperator.In or BinaryOperator.NotIn, Right: Name collection } => (collection.Identifier, expression),

        // items.Contains(x), items.indexOf(x), items.includes(x) and the rest.
        Call { Callee: Member { Target: Name collection, MemberName: var method }, Arguments.Count: 1 }
            when SearchesFromTheStart.Contains(method) => (collection.Identifier, expression),

        _ => null,
    };

    /// <summary>Names the loop's own body assigns to, which are therefore not the same thing on every pass.</summary>
    private static HashSet<string> Assigned(IReadOnlyList<Stmt> body)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var statement in IrWalk.Statements(body))
        {
            switch (statement)
            {
                case Assign { Target: Name target }:
                    names.Add(target.Identifier);
                    break;

                case Declare declared:
                    names.Add(declared.Variable);
                    break;
            }

            // A collection added to or taken from inside the loop is changing too, even without an assignment.
            foreach (var expression in IrWalk.Expressions(statement))
            {
                if (expression is Call { Callee: Member { Target: Name changed, MemberName: var method } } &&
                    method is "append" or "Add" or "add" or "push" or "insert" or "Insert" or "remove" or "Remove" or "pop" or "clear" or "Clear")
                {
                    names.Add(changed.Identifier);
                }
            }
        }

        return names;
    }
}
