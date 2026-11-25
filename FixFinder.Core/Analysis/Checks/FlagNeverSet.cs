using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// A flag a loop never sets: <c>found = False</c> straight before a loop, and an if after the loop that tests it - but
/// nothing in the function ever gives the flag another value, so the test decides nothing: <c>if found</c> is never true,
/// and <c>if not found</c> always is. Most often the loop's if was meant to set it - <c>found = True</c> - where it finds
/// what it looks for.
/// </summary>
/// <remarks>
/// The flag is given false, or 0, once, in the statement just before a for, for-each or while loop with an if in it, and
/// never again anywhere in the function - by an assignment, an increment, a loop's variable, a handler's, or a call it is
/// passed to as C#'s out or ref - and the test is an if after the loop, in the same block, of the flag alone or of not the
/// flag. A parameter, a name an outer scope, a nested function or a lambda shares, and one whose address is taken are left
/// alone. A switch such as <c>debug = False</c>, which the checks of a condition never true or always true leave alone,
/// is not one: nothing is said of it unless it is set just before a loop that searches.
/// </remarks>
internal static partial class FlagNeverSet
{
    public const string CheckId = "analysis-flag-never-set";

    public static IEnumerable<AnalysisFinding> Find(IrProgram program, IEnumerable<IrFunction> functions, SourceText source, string foundBy)
    {
        foreach (var function in functions)
        {
            if (function.Name == IrFunction.ModuleBody) continue;
            var shared = SharedWithOtherCode(program, function, source);

            foreach (var block in Blocks(function.Body))
            {
                for (var index = 0; index + 1 < block.Count; index++)
                {
                    if (SetOff(block[index]) is not var (flag, off)) continue;
                    if (shared.Contains(flag) || function.OuterNames.Contains(flag) || function.AddressTaken.Contains(flag) ||
                        function.Parameters.Any(parameter => parameter.Name == flag)) continue;
                    // C# and Java declare a for-each loop's variable just before the loop, with no value yet.
                    var next = index + 1;
                    while (next < block.Count && block[next] is Declare { Initial: null }) next++;
                    if (next >= block.Count) continue;
                    var loop = block[next];
                    if (loop is not (For or ForEach or While) || !IrWalk.Statements([loop]).Skip(1).Any(statement => statement is If)) continue;
                    if (GivenAnotherValue(function, block[index], flag)) continue;

                    var test = block.Skip(next + 1).OfType<If>().FirstOrDefault(branch => TestOf(branch.Condition, flag) is not null);
                    if (test is null) continue;

                    var asked = TestOf(test.Condition, flag)!.Value ? $"not {flag}" : flag;
                    var outcome = TestOf(test.Condition, flag)!.Value ? "always true" : "never true";
                    yield return new AnalysisFinding(CheckId, test.Span,
                        $"`{flag}` is set to {(off.Value is bool ? "false" : "0")} on line {block[index].Span.Line} and nothing changes it - the loop on line " +
                        $"{loop.Span.Line} never sets it - so the test of `{asked}` on line {test.Span.Line} is {outcome}",
                        Severity.Warning, Confidence.Likely, FindingKind.Logic, foundBy)
                    {
                        Function = function.FullName,
                    };
                }
            }
        }
    }

    /// <summary>Every list of statements in the function, each in its own order: its body, and each branch's, loop's and handler's.</summary>
    private static IEnumerable<IReadOnlyList<Stmt>> Blocks(IReadOnlyList<Stmt> body)
    {
        yield return body;
        foreach (var statement in body)
        {
            IEnumerable<IReadOnlyList<Stmt>> inner = statement switch
            {
                If branch => [branch.Then, branch.Else],
                While loop => [loop.Body, loop.Else],
                For loop => [loop.Setup, loop.Body, loop.Step],
                ForEach loop => [loop.Body, loop.Else],
                Try attempt => [attempt.Body, .. attempt.Handlers.Select(handler => handler.Body), attempt.Else, attempt.Finally],
                Switch choice => choice.Cases.Select(each => each.Body),
                Using used => [used.Body],
                Labeled labeled => [labeled.Body],
                _ => [],
            };
            foreach (var nested in inner.SelectMany(Blocks)) yield return nested;
        }
    }

    /// <summary>A statement setting a plain name to false or 0, with the value.</summary>
    private static (string Flag, Literal Off)? SetOff(Stmt statement) => statement switch
    {
        Assign { Target: Name { Identifier: var name }, Value: Literal literal, Compound: null } when IsOff(literal) => (name, literal),
        Declare { Variable: var name, Initial: Literal literal } when IsOff(literal) => (name, literal),
        _ => null,
    };

    private static bool IsOff(Literal literal) => literal.Value switch
    {
        bool truth => !truth,
        long whole => whole == 0 && literal.Kind == LiteralKind.Integer,
        int whole => whole == 0 && literal.Kind == LiteralKind.Integer,
        _ => false,
    };

    /// <summary>Whether a test is of the flag alone - false - or of not the flag - true; null for any other test.</summary>
    private static bool? TestOf(Expr condition, string flag) => condition switch
    {
        Name { Identifier: var name } when name == flag => false,
        Unary { Operator: UnaryOperator.Not, Operand: Name { Identifier: var name } } when name == flag => true,
        _ => null,
    };

    /// <summary>Whether anything in the function but the statement setting it off gives the flag a value.</summary>
    private static bool GivenAnotherValue(IrFunction function, Stmt setting, string flag)
    {
        foreach (var statement in IrWalk.Statements(function.Body))
        {
            if (ReferenceEquals(statement, setting)) continue;

            var gives = statement switch
            {
                Assign assign => IrWalk.Binds(assign.Target, flag),
                Declare declare => declare.Variable == flag,
                ForEach loop => IrWalk.Binds(loop.Target, flag),
                Try attempt => attempt.Handlers.Any(handler => handler.Variable == flag),
                OpaqueStmt opaque => opaque.MayAssign.Contains(flag),
                _ => false,
            };
            if (gives) return true;

            if (IrWalk.Expressions(statement).SelectMany(IrWalk.Within).Any(expression => expression is AssignValue value && IrWalk.Binds(value.Target, flag)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The names a nested function or a lambda of this function uses, which it may set whenever it is called - a lambda's
    /// taken from its lines as well, as a reader may keep nothing of what is inside one.
    /// </summary>
    private static HashSet<string> SharedWithOtherCode(IrProgram program, IrFunction function, SourceText source)
    {
        var shared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nested in program.AllFunctions.Where(other => !ReferenceEquals(other, function) &&
                                                                 (other.EnclosedBy == function.Name || other.EnclosedBy == function.FullName)))
            shared.UnionWith(IrWalk.Statements(nested.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names));

        foreach (var lambda in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within)
                     .OfType<Opaque>().Where(opaque => opaque.What is "lambda" or "lambda expression" or "function"))
        {
            shared.UnionWith(IrWalk.Names(lambda));
            for (var line = lambda.Span.Line; line <= Math.Max(lambda.Span.Line, lambda.Span.EndLine); line++)
                if (source.Line(lambda.Span.File, line) is { } written) shared.UnionWith(Word().Matches(written).Select(word => word.Value));
        }

        return shared;
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Word();
}
