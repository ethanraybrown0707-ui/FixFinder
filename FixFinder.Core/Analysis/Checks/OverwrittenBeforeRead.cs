using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Abstract;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// A value worked out and then lost: a function's own variable is given a value, and on every way on from there it is given
/// another before anything reads it - <c>area = width * height</c>, then <c>area = width * width</c> - so the first is never
/// used. Most often the second line was meant for another variable, or the first was meant to be used before it.
/// </summary>
/// <remarks>
/// <para>
/// Every way through the function's graph is followed from the line, the ways an exception can go among them, so a handler
/// that reads the variable keeps the value in use; a way that raises an error is not counted either way. A way that reaches
/// the function's end without giving the variable another value means it is a value nothing reads, not one overwritten, and
/// that is not said here.
/// </para>
/// <para>
/// Left alone: a starting value - a literal, None or null, an empty list or a new object made with nothing - which code
/// often gives a variable before it gives it the one it means; each item a for loop gives its variable; a name starting
/// with an underscore, which says its value is not wanted; the top level of a file, whose names any function it calls can
/// read; a variable a nested function or a lambda uses, or one whose address is taken, as either can read it at any time;
/// a C++ reference, and a variable one is bound to, as a value given to either is given to both; and Python's global and
/// nonlocal names.
/// </para>
/// </remarks>
internal static partial class OverwrittenBeforeRead
{
    public const string CheckId = "analysis-overwritten-before-read";

    /// <summary>
    /// A parameter given a value before anything reads the one the caller passed - <c>def area(width, height):</c> and then
    /// <c>width = 5</c> - so the caller's value is never used. A C++ reference and a C# ref or out parameter are left alone,
    /// as giving one a value gives it to the caller's variable, and so is a parameter some way through the function never
    /// gives another value, which is unread rather than replaced.
    /// </summary>
    public const string ParameterCheckId = "analysis-parameter-ignored";

    public static IEnumerable<AnalysisFinding> Find(IrProgram program, IEnumerable<(IrFunction Function, ControlFlowGraph Graph, IReadOnlySet<string> Locals)> functions,
        SourceText source, string foundBy)
    {
        foreach (var (function, graph, locals) in functions)
        {
            if (function.Name == IrFunction.ModuleBody) continue;

            var seenElsewhere = SeenByOtherCode(program, function, source);
            var references = References.Of(function, program.Language);
            bool Followed(string name) => OwnName().IsMatch(name) && !name.StartsWith('_') && !function.OuterNames.Contains(name) &&
                                          !function.AddressTaken.Contains(name) && !seenElsewhere.Contains(name) && !SharedByAReference(references, name);
            var watched = locals.Where(Followed).ToHashSet(StringComparer.Ordinal);
            var parameters = function.Parameters.Where(parameter => !parameter.ByReference && Followed(parameter.Name)).ToList();
            if (watched.Count == 0 && parameters.Count == 0) continue;

            var liveIn = LiveIn(graph);

            // A parameter is given its value by the caller; when every way through the function gives it another before
            // anything reads it, whatever the caller passes is never used.
            foreach (var parameter in parameters)
            {
                if (liveIn[graph.Entry].Contains(parameter.Name) || FirstGivenOnEveryWay(graph, parameter.Name) is not { } replaced) continue;

                var replacedWith = GivenBy(replaced) is var (_, value) ? $"`{IrText.Of(value)}`" : "a new value";
                yield return new AnalysisFinding(ParameterCheckId, replaced.Span,
                    $"`{parameter.Name}` is given {replacedWith} on line {replaced.Span.Line} before anything reads it, so the value the caller " +
                    $"passes for `{parameter.Name}` is never used",
                    Severity.Warning, Confidence.Likely, FindingKind.Logic, foundBy)
                {
                    Function = function.FullName,
                };
            }

            foreach (var block in graph.Blocks)
            {
                for (var index = 0; index < block.Instructions.Count; index++)
                {
                    if (GivenBy(block.Instructions[index]) is not var (name, value) || !watched.Contains(name) || IsAStartingValue(value)) continue;
                    var given = block.Instructions[index];
                    if (ReadAfter(graph, block, index, name, liveIn)) continue;
                    if (GivenAgainOnEveryWay(graph, block, index, name) is not { Count: > 0 } overwrittenOn) continue;

                    var again = overwrittenOn.Count == 1 ? $" on line {overwrittenOn.Single()}" : "";
                    yield return new AnalysisFinding(CheckId, given.Span,
                        $"`{name}` is given `{IrText.Of(value)}` on line {given.Span.Line}, then given another value{again} before anything reads it, " +
                        $"so the value from line {given.Span.Line} is never used",
                        Severity.Warning, Confidence.Likely, FindingKind.Logic, foundBy)
                    {
                        Function = function.FullName,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Whether the name is a C++ reference, or a variable one is bound to: after <c>int&amp; alias = rows</c>, giving alias a
    /// value gives rows it, and reading either reads both, so neither can be followed on its own.
    /// </summary>
    private static bool SharedByAReference(References references, string name) =>
        references.Same(name).Count > 0 || references.Maybe(name).Count > 0 || references.Outside.Contains(name);

    /// <summary>A value code often gives a variable first, before the one it means: 0, "", None, null, [] or new List().</summary>
    private static bool IsAStartingValue(Expr value) => value switch
    {
        Literal => true,
        Unary { Operand: Literal } => true,
        CollectionLiteral { Items.Count: 0 } => true,
        NewObject { Arguments.Count: 0 } => true,
        NextItem => true,
        Opaque or AssignValue => true,
        _ => false,
    };

    /// <summary>
    /// The names a nested function or a lambda of this function uses, which may be read whenever they are called - a lambda's
    /// taken from its lines as well, as a reader may keep nothing of what is inside one.
    /// </summary>
    private static HashSet<string> SeenByOtherCode(IrProgram program, IrFunction function, SourceText source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nested in program.AllFunctions.Where(other => !ReferenceEquals(other, function) &&
                                                                 (other.EnclosedBy == function.Name || other.EnclosedBy == function.FullName)))
            seen.UnionWith(IrWalk.Statements(nested.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names));

        foreach (var lambda in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within)
                     .OfType<Opaque>().Where(opaque => opaque.What is "lambda" or "lambda expression" or "function"))
        {
            seen.UnionWith(IrWalk.Names(lambda));
            for (var line = lambda.Span.Line; line <= Math.Max(lambda.Span.Line, lambda.Span.EndLine); line++)
                if (source.Line(lambda.Span.File, line) is { } written) seen.UnionWith(Word().Matches(written).Select(word => word.Value));
        }

        return seen;
    }

    /// <summary>Whether the value given at that instruction can be read: by a later instruction of the block before the name is given another, by a block after it, or by a handler an exception can reach.</summary>
    private static bool ReadAfter(ControlFlowGraph graph, BasicBlock block, int index, string name, IReadOnlyList<HashSet<string>> liveIn)
    {
        // An exception raised anywhere after the line may take the value to a handler that reads it.
        if (block.ExceptionTargets.Any(handler => liveIn[handler].Contains(name))) return true;

        for (var later = index + 1; later < block.Instructions.Count; later++)
        {
            var (uses, gives) = Effect(block.Instructions[later], name);
            if (uses) return true;
            if (gives) return false;
        }

        if (block.Terminator is { } end && Reads(end, name)) return true;
        return graph.Successors(block.Id).Any(next => liveIn[next].Contains(name));
    }

    /// <summary>
    /// The lines that give the name another value on each way on from the instruction - or null when a way reaches the
    /// function's end without one, which makes the value one nothing reads rather than one overwritten. Ways that raise are
    /// not followed.
    /// </summary>
    private static List<int>? GivenAgainOnEveryWay(ControlFlowGraph graph, BasicBlock block, int index, string name)
    {
        var lines = new SortedSet<int>();

        for (var later = index + 1; later < block.Instructions.Count; later++)
            if (Effect(block.Instructions[later], name).Gives) return [block.Instructions[later].Span.Line];

        var visited = new HashSet<int>();
        var waiting = new Stack<int>(NormalWaysOn(graph, block));

        while (waiting.Count > 0)
        {
            var next = waiting.Pop();
            if (!visited.Add(next)) continue;
            if (next == graph.NormalExit) return null;
            if (next == graph.ErrorExit) continue;

            var node = graph.Blocks[next];
            if (node.Instructions.FirstOrDefault(instruction => Effect(instruction, name).Gives) is { } given)
            {
                lines.Add(given.Span.Line);
                continue;
            }

            if (node.Terminator is Raise) continue;
            foreach (var after in NormalWaysOn(graph, node)) waiting.Push(after);
        }

        return lines.Count > 0 ? [.. lines] : null;
    }

    /// <summary>
    /// The first instruction giving the name a value, when every way from the function's start gives it one before the
    /// function ends; null when a way reaches the end without one, which leaves the parameter unread rather than replaced.
    /// </summary>
    private static Instruction? FirstGivenOnEveryWay(ControlFlowGraph graph, string name)
    {
        if (GivenAgainOnEveryWay(graph, graph.Blocks[graph.Entry], -1, name) is not { Count: > 0 } lines) return null;

        return graph.Blocks.SelectMany(block => block.Instructions)
            .Where(instruction => lines.Contains(instruction.Span.Line) && Effect(instruction, name).Gives)
            .OrderBy(instruction => instruction.Span.Line).ThenBy(instruction => instruction.Span.Column)
            .FirstOrDefault();
    }

    private static IEnumerable<int> NormalWaysOn(ControlFlowGraph graph, BasicBlock block) => block.Terminator switch
    {
        Leave => [graph.NormalExit],
        Raise => [],
        _ => block.NormalSuccessors,
    };

    /// <summary>The names each block needs on entry, worked backwards until nothing changes.</summary>
    private static List<HashSet<string>> LiveIn(ControlFlowGraph graph)
    {
        var liveIn = graph.Blocks.Select(_ => new HashSet<string>(StringComparer.Ordinal)).ToList();
        bool changed;

        do
        {
            changed = false;
            foreach (var block in graph.Blocks.Reverse())
            {
                var live = graph.Successors(block.Id).SelectMany(next => liveIn[next]).ToHashSet(StringComparer.Ordinal);
                if (block.Terminator is { } end) live.UnionWith(Names(end));

                foreach (var instruction in Enumerable.Reverse(block.Instructions))
                {
                    if (GivenBy(instruction) is var (given, value))
                    {
                        live.Remove(given);
                        live.UnionWith(IrWalk.Names(value));
                    }
                    else live.UnionWith(Names(instruction));
                }

                // A handler an exception from the block can reach needs its names whatever the block gave.
                foreach (var handler in block.ExceptionTargets) live.UnionWith(liveIn[handler]);

                if (live.SetEquals(liveIn[block.Id])) continue;
                liveIn[block.Id] = live;
                changed = true;
            }
        } while (changed);

        return liveIn;
    }

    /// <summary>
    /// The name an instruction gives a value to, and the value: an assignment to a plain name - <c>result = width * width</c>,
    /// whether a reader keeps it as an assignment or as an assignment expression standing on its own, as JavaScript's and C's
    /// do. Null for any other instruction.
    /// </summary>
    private static (string Name, Expr Value)? GivenBy(Instruction instruction) => instruction switch
    {
        AssignInstruction { Target: Name { Identifier: var name }, Value: var value } => (name, value),
        EvaluateInstruction { Value: AssignValue { Target: Name { Identifier: var name }, Value: var value, ValueBeforeAssigning: false } } => (name, value),
        _ => null,
    };

    /// <summary>Whether an instruction reads the name, and whether it gives it a value of its own.</summary>
    private static (bool Uses, bool Gives) Effect(Instruction instruction, string name) => instruction switch
    {
        _ when GivenBy(instruction) is var (given, value) && given == name => (IrWalk.Names(value).Contains(name), true),
        AssignInstruction assign when IrWalk.Binds(assign.Target, name) => (IrWalk.Names(assign.Value).Contains(name), true),
        _ => (Names(instruction).Contains(name), false),
    };

    /// <summary>Every name an instruction mentions - read, changed inside, or given - except a plain name it gives a value to.</summary>
    private static IEnumerable<string> Names(Instruction instruction) => instruction switch
    {
        _ when GivenBy(instruction) is var (_, value) => IrWalk.Names(value),
        AssignInstruction assign => IrWalk.Names(assign.Target).Concat(IrWalk.Names(assign.Value)),
        EvaluateInstruction evaluate => IrWalk.Names(evaluate.Value),
        ForgetInstruction forget => forget.Names.Concat(forget.Parts.SelectMany(IrWalk.Names)),
        ReleaseInstruction release => IrWalk.Names(release.Resource),
        _ => [],
    };

    private static IEnumerable<string> Names(Terminator terminator) => terminator switch
    {
        Branch branch => IrWalk.Names(branch.Condition),
        Leave { Value: { } value } => IrWalk.Names(value),
        Raise { Exception: { } exception } => IrWalk.Names(exception),
        _ => [],
    };

    private static bool Reads(Terminator terminator, string name) => Names(terminator).Contains(name);

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Word();

    /// <summary>A name the program wrote, not one a reader made up for itself - $iterator, or a temporary with a number.</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex OwnName();
}
