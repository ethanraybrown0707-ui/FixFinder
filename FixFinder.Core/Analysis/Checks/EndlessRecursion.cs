using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Recursion that never ends: a function that cannot return - nor raise, nor end the program - without calling itself, or
/// without calling the next of a ring of functions each of which cannot return without calling the next. Every way from the
/// function's start to a way out is followed; when each one passes a call to the same function of the program's own, the
/// function never gets out, and each call makes another until the language runs out of room for them.
/// </summary>
/// <remarks>
/// <para>
/// Only a call made on the way through counts: one inside a conditional expression's branches - return 0 if n == 0 else
/// f(n - 1) - after an and or an or, or inside a lambda or a comprehension, may not be made, and is not counted. A call to a
/// method a subclass can replace may not run this one, and is not counted either; nor is a call to a generator or an async
/// function, which makes a generator or a coroutine without running any of it. A call that ends the program - sys.exit,
/// System.exit, Environment.Exit, process.exit, os.Exit - is a way out.
/// </para>
/// <para>
/// What each language then does, as it was run here: Python 3.13 stops with RecursionError: maximum recursion depth exceeded,
/// Java 21 with StackOverflowError, .NET 10 ends the whole program with "Stack overflow.", Node.js with RangeError: Maximum
/// call stack size exceeded, and Go with fatal error: stack overflow. C and C++ are not here: gcc -Wall already warns of it,
/// "infinite recursion detected".
/// </para>
/// </remarks>
internal static class EndlessRecursion
{
    public const string CheckId = "analysis-recursion-never-ends";

    public static IEnumerable<AnalysisFinding> Find(
        IEnumerable<(IrFunction Function, ControlFlowGraph Graph, IReadOnlySet<string> Locals)> functions, CallTargets targets, SourceLanguage language,
        SourceText source, string foundBy)
    {
        if (Failure(language) is not { } failure) yield break;

        // For each function, the functions of the program's own it cannot get out without calling - each with its first call.
        var mustCall = new Dictionary<IrFunction, List<(IrFunction Callee, Call At)>>(ReferenceEqualityComparer.Instance);

        foreach (var (function, graph, locals) in functions)
        {
            if (function.IsGenerator || function.IsAsync || function.Name == IrFunction.ModuleBody) continue;
            if (!WayOut(graph, _ => false)) continue;

            var made = graph.Blocks.ToDictionary(block => block.Id, block => CallsMade(block)
                .Select(call => (Call: call, Target: targets.Resolve(call, function, locals)))
                .Where(call => call.Target is { Overridable: false, Function: { IsGenerator: false, IsAsync: false } })
                .Select(call => (call.Call, Callee: call.Target!.Function))
                .ToList());

            foreach (var callee in made.Values.SelectMany(calls => calls).Select(call => call.Callee).Distinct(ReferenceEqualityComparer.Instance).Cast<IrFunction>())
            {
                if (WayOut(graph, block => made[block].Any(call => ReferenceEquals(call.Callee, callee)))) continue;

                var first = made.Values.SelectMany(calls => calls).Where(call => ReferenceEquals(call.Callee, callee))
                    .OrderBy(call => call.Call.Span.Line).ThenBy(call => call.Call.Span.Column).First();
                (mustCall.TryGetValue(function, out var known) ? known : mustCall[function] = []).Add((callee, first.Call));
            }
        }

        // A ring of functions is one mistake, said once: at the first of them in the file.
        var said = new HashSet<IrFunction>(ReferenceEqualityComparer.Instance);

        foreach (var (function, calls) in mustCall.OrderBy(each => each.Value.Min(call => call.At.Span.Line)))
        {
            if (said.Contains(function) || RingThrough(function, mustCall) is not { } ring) continue;
            said.UnionWith(ring);

            var next = calls.First(call => ReferenceEquals(call.Callee, ring[1 % ring.Count]));
            var called = source.Of(next.At.Span) is { Length: > 0 } text ? text : IrText.Of(next.At);
            var message = ring.Count == 1
                ? $"`{function.Name}` cannot return without calling itself - every way through it reaches `{called}` - so each call makes " +
                  $"another, and they never end: {failure}"
                : $"`{function.Name}` cannot return without calling `{ring[1].Name}`, " + string.Join(", ", ring.Skip(1).Select((each, place) =>
                      $"`{each.Name}` without calling `{ring[(place + 2) % ring.Count].Name}`")) +
                  $" - so the calls go round and never end: {failure}";

            yield return new AnalysisFinding(CheckId, next.At.Span, message, Severity.Error, Confidence.Likely, FindingKind.Logic, foundBy);
        }
    }

    /// <summary>The functions round a ring of calls none of them can get out without, starting at this one - or null when it is on none.</summary>
    private static List<IrFunction>? RingThrough(IrFunction start, Dictionary<IrFunction, List<(IrFunction Callee, Call At)>> mustCall)
    {
        var path = new List<IrFunction> { start };
        var onPath = new HashSet<IrFunction>(ReferenceEqualityComparer.Instance) { start };

        List<IrFunction>? Walk(IrFunction at)
        {
            if (!mustCall.TryGetValue(at, out var calls)) return null;

            foreach (var (callee, _) in calls.OrderBy(call => call.At.Span.Line))
            {
                if (ReferenceEquals(callee, start)) return [.. path];
                if (!onPath.Add(callee)) continue;

                path.Add(callee);
                if (Walk(callee) is { } ring) return ring;
                path.RemoveAt(path.Count - 1);
            }

            return null;
        }

        return Walk(start);
    }

    /// <summary>Whether a way out of the function - a return, a raise, or a call that ends the program - is reached from its start without a blocked block.</summary>
    private static bool WayOut(ControlFlowGraph graph, Func<int, bool> blocked)
    {
        var seen = new HashSet<int>();
        var pending = new Queue<int>([graph.Entry]);

        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!seen.Add(id)) continue;
            if (id == graph.NormalExit || id == graph.ErrorExit) return true;
            if (blocked(id)) continue;

            var block = graph.Blocks[id];
            if (CallsMade(block).Any(EndsTheProgram)) return true;

            IEnumerable<int> next = block.Terminator switch
            {
                Leave => [graph.NormalExit],
                Raise => block.ExceptionTargets.Count > 0 ? block.ExceptionTargets : [graph.ErrorExit],
                _ => block.NormalSuccessors,
            };
            foreach (var successor in next) pending.Enqueue(successor);
        }

        return false;
    }

    /// <summary>sys.exit(...), exit(), quit(), os._exit(...), System.exit(...), Environment.Exit(...), process.exit(...), os.Exit(...).</summary>
    private static bool EndsTheProgram(Call call) => IrText.Of(call.Callee) is "sys.exit" or "exit" or "quit" or "os._exit" or "System.exit" or
        "Environment.Exit" or "process.exit" or "os.Exit" or "log.Fatal" or "log.Fatalf";

    /// <summary>The calls a block certainly makes, in the order it makes them: in its instructions, and in what its ending tests or returns.</summary>
    private static IEnumerable<Call> CallsMade(BasicBlock block)
    {
        foreach (var instruction in block.Instructions)
        {
            var made = instruction switch
            {
                AssignInstruction assign => Made(assign.Target).Concat(Made(assign.Value)),
                EvaluateInstruction evaluate => Made(evaluate.Value),
                _ => [],
            };
            foreach (var call in made) yield return call;
        }

        var ending = block.Terminator switch
        {
            Branch branch => Made(branch.Condition),
            Leave { Value: { } value } => Made(value),
            Raise { Exception: { } raised } => Made(raised),
            _ => [],
        };
        foreach (var call in ending) yield return call;
    }

    /// <summary>
    /// The calls an expression certainly makes when it is worked out: a call's callee and arguments, then the call; only the
    /// test of a conditional expression; only the left of an and or an or; nothing inside a lambda or a comprehension.
    /// </summary>
    private static IEnumerable<Call> Made(Expr expression) => expression switch
    {
        Call call => Made(call.Callee).Concat(call.Arguments.SelectMany(argument => Made(argument.Value))).Append(call),
        Conditional conditional => Made(conditional.Test),
        Binary { Operator: BinaryOperator.And or BinaryOperator.Or } either => Made(either.Left),
        Opaque => [],
        _ => IrWalk.Children(expression).SelectMany(Made),
    };

    /// <summary>What running out of room for calls does in each language, as each was run here - or null for one not checked here.</summary>
    private static string? Failure(SourceLanguage language) => language switch
    {
        SourceLanguage.Python => "Python stops it with RecursionError: maximum recursion depth exceeded",
        SourceLanguage.Java => "Java stops it with a StackOverflowError",
        SourceLanguage.CSharp => ".NET ends the whole program with Stack overflow.",
        SourceLanguage.JavaScript => "Node.js stops it with RangeError: Maximum call stack size exceeded",
        SourceLanguage.Go => "Go stops it with fatal error: stack overflow",
        _ => null,
    };
}
