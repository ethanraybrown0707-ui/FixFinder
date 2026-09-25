using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Exception flow: what a try statement does to the code around it when something inside it fails.
/// <list type="bullet">
/// <item>A return, break or continue in a finally block runs however the try ends - it replaces the value the try
/// returned, and an exception the try raised is thrown away without a trace.</item>
/// <item>In Python, a variable given its value only inside a try has none if the try failed before that line. A handler
/// or finally block that reads it, or a handler that carries on to code that reads it, raises UnboundLocalError.</item>
/// </list>
/// </summary>
/// <remarks>
/// Java and C# refuse to compile a read of a variable that may not have been given a value, and C# refuses a return in a
/// finally block, so each check runs only where the language lets the mistake through.
/// </remarks>
internal sealed class ExceptionFlow(IrFunction function, SourceLanguage language, Action<AnalysisFinding> report)
{
    public const string FinallyOverridesRule = "analysis-finally-overrides";
    public const string UnassignedRule = "analysis-unassigned-after-error";

    private readonly HashSet<SourceSpan> _reported = [];

    private bool IsPython => language == SourceLanguage.Python;

    /// <summary>What reading a name that was never given a value raises: in a function, and in a module's own code.</summary>
    private string Unbound => function.Name == IrFunction.ModuleBody ? "NameError" : "UnboundLocalError";

    public void Check()
    {
        if (language is SourceLanguage.Java or SourceLanguage.Python or SourceLanguage.JavaScript)
            foreach (var attempt in IrWalk.Statements(function.Body).OfType<Try>()) FinallyOverrides(attempt);

        if (IsPython) Blocks(function.Body);
    }

    /// <summary>return, break or continue inside a finally block - break and continue only when they leave it, not a loop inside it.</summary>
    private void FinallyOverrides(Try attempt)
    {
        void Walk(IEnumerable<Stmt> block, bool inLoop)
        {
            foreach (var statement in block)
            {
                switch (statement)
                {
                    case Return leave when _reported.Add(leave.Span):
                        Report(FinallyOverridesRule, leave.Span,
                            "This return is in a finally block, so it runs however the try block ends: it replaces the value the try block returned, " +
                            "and if the try block raised an error, the error is thrown away without a trace", Confidence.Certain);
                        break;

                    case Break or Continue when !inLoop && _reported.Add(statement.Span):
                        Report(FinallyOverridesRule, statement.Span,
                            $"This {(statement is Break ? "break" : "continue")} leaves a finally block, so if the try block raised an error, the error is thrown away " +
                            "without a trace", Confidence.Certain);
                        break;
                }

                var looping = inLoop || statement is While or For or ForEach;
                foreach (var inner in Children(statement)) Walk(inner, looping);
            }
        }

        Walk(attempt.Finally, inLoop: false);
    }

    private static IEnumerable<IReadOnlyList<Stmt>> Children(Stmt statement) => statement switch
    {
        If branch => [branch.Then, branch.Else],
        While loop => [loop.Body, loop.Else],
        For loop => [loop.Setup, loop.Body, loop.Step],
        ForEach loop => [loop.Body, loop.Else],
        Try attempt => [attempt.Body, .. attempt.Handlers.Select(h => h.Body), attempt.Else, attempt.Finally],
        Switch choice => choice.Cases.Select(c => c.Body),
        Using used => [used.Body],
        Labeled labeled => [labeled.Body],
        _ => [],
    };

    /// <summary>Every block of the function, so each try is seen with the statements that follow it.</summary>
    private void Blocks(IReadOnlyList<Stmt> block)
    {
        for (var index = 0; index < block.Count; index++)
        {
            if (block[index] is Try attempt) Unassigned(attempt, block.Skip(index + 1).ToList());
            foreach (var inner in Children(block[index])) Blocks(inner);
        }
    }

    /// <summary>
    /// Names the try block gives a value that nothing before it did: if the try fails before that line, they have none.
    /// Read in a handler or the finally block, or after a handler that carries on without giving them one, they raise.
    /// </summary>
    private void Unassigned(Try attempt, IReadOnlyList<Stmt> after)
    {
        var givenBefore = GivenBefore(attempt);

        foreach (var (name, givenAt) in FirstGivenIn(attempt.Body))
        {
            if (givenBefore.Contains(name)) continue;

            foreach (var handler in attempt.Handlers)
            {
                if (handler.Variable == name) continue;

                if (FirstRead(handler.Body, name) is { } readInHandler)
                    Report(UnassignedRule, readInHandler,
                        $"`{name}` is only given a value inside the try block (line {givenAt.Line}), so if the error came before that line, `{name}` has no value " +
                        $"here - and reading it raises {Unbound}", Confidence.Likely);
                else if (!Leaves(handler.Body) && !Gives(handler.Body, name) && FirstUnguardedRead(after, name) is { } readAfter)
                    Report(UnassignedRule, readAfter,
                        $"`{name}` has no value here if the try block failed before line {givenAt.Line}: the except block on line {handler.Span.Line} carries on " +
                        $"without giving it one - and reading it raises {Unbound}", Confidence.Likely);
            }

            if (attempt.Finally.Count > 0 && FirstRead(attempt.Finally, name) is { } readInFinally)
                Report(UnassignedRule, readInFinally,
                    $"`{name}` is only given a value inside the try block (line {givenAt.Line}), and the finally block runs even when the try block failed before that " +
                    $"line - then `{name}` has no value here, and reading it raises {Unbound}, hiding the error that stopped the try", Confidence.Likely);
        }
    }

    /// <summary>
    /// The names the try block gives a value, each with where it first does - leaving out those given one by a first step
    /// that cannot fail, which always happens before anything else in the try can go wrong.
    /// </summary>
    private static IEnumerable<(string Name, SourceSpan GivenAt)> FirstGivenIn(IReadOnlyList<Stmt> body)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var anythingCanFail = false;

        foreach (var statement in IrWalk.Statements(body))
        {
            if (Given(statement) is { } name && seen.Add(name) && (anythingCanFail || CanFail(statement))) yield return (name, statement.Span);
            anythingCanFail |= CanFail(statement);
        }
    }

    private static string? Given(Stmt statement) => statement switch
    {
        Assign { Target: Name { Identifier: var name }, Compound: null } => name,
        Declare { Variable: var name } => name,
        Using { Variable: Name { Identifier: var name } } => name,
        ForEach { Target: Name { Identifier: var name } } => name,
        _ => null,
    };

    /// <summary>Whether a statement can raise an error itself: a call, an item looked up, a division, an attribute read.</summary>
    private static bool CanFail(Stmt statement) => IrWalk.Expressions(statement).Any(CanFail) || statement is Throw or AssertThat;

    private static bool CanFail(Expr expression) =>
        expression is Call or NewObject or ElementAccess or Member or Binary { Operator: BinaryOperator.Divide or BinaryOperator.FloorDivide or BinaryOperator.Modulo } ||
        IrWalk.Children(expression).Any(CanFail);

    /// <summary>
    /// Names given a value by anything that comes before the try in the function - its parameters, the names it declares
    /// global, any assignment written above it. Generous on purpose: only a name given its value nowhere but the try is reported.
    /// </summary>
    private HashSet<string> GivenBefore(Try attempt)
    {
        var given = function.Parameters.Select(p => p.Name).Concat(function.OuterNames).ToHashSet(StringComparer.Ordinal);

        foreach (var statement in IrWalk.Statements(function.Body))
        {
            if (ReferenceEquals(statement, attempt)) break;
            if (Given(statement) is { } name) given.Add(name);
            if (statement is Try { Handlers: var handlers }) foreach (var handler in handlers) if (handler.Variable is { } caught) given.Add(caught);
        }

        return given;
    }

    /// <summary>Where a block first reads a name, if it reads it before giving it a value.</summary>
    private static SourceSpan? FirstRead(IReadOnlyList<Stmt> block, string name)
    {
        foreach (var statement in IrWalk.Statements(block))
        {
            var reading = statement switch
            {
                Assign { Target: Name { Identifier: var target }, Compound: not null } assign when target == name => assign.Span,
                Assign assign => IrWalk.Names(assign.Value).Concat(assign.Target is Name ? [] : IrWalk.Names(assign.Target)).Contains(name) ? assign.Span : null,
                _ => IrWalk.Expressions(statement).SelectMany(IrWalk.Names).Contains(name) ? statement.Span : null,
            };

            if (reading is not null) return reading;
            if (Given(statement) == name) return null;
        }

        return null;
    }

    /// <summary>
    /// Where the code after a try first reads a name, counting only reads no condition stands in front of. A read inside
    /// if ok: may be guarded by a flag the try set on success, and whether it is cannot be told from here.
    /// </summary>
    private static SourceSpan? FirstUnguardedRead(IReadOnlyList<Stmt> block, string name)
    {
        foreach (var statement in block)
        {
            if (Given(statement) == name) return null;
            if (IrWalk.Expressions(statement).SelectMany(IrWalk.Names).Contains(name)) return statement.Span;

            // Reads inside the statement's own blocks are guarded; a value it may give the name ends the search.
            if (IrWalk.Statements([statement]).Any(s => Given(s) == name)) return null;
        }

        return null;
    }

    private static bool Gives(IReadOnlyList<Stmt> block, string name) => IrWalk.Statements(block).Any(s => Given(s) == name);

    /// <summary>Whether a handler ends by leaving: returning, raising, or jumping out of the loop it is in.</summary>
    private static bool Leaves(IReadOnlyList<Stmt> block) => block.Count > 0 && block[^1] is Return or Throw or Break or Continue;

    private void Report(string rule, SourceSpan span, string message, Confidence confidence)
    {
        if (rule == UnassignedRule && !_reported.Add(span)) return;
        report(new AnalysisFinding(rule, span, message, Severity.Warning, confidence, FindingKind.Logic, AbstractChecks.FoundBy));
    }
}
