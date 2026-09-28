using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>
/// The calls that end the whole program - sys.exit, System.exit, Environment.Exit, os.Exit, process.exit - which never
/// return to the code that makes them, so nothing written after one runs.
/// </summary>
/// <remarks>
/// Each becomes a throw of the call itself, as C's exit already is in the C reader, so every analysis ends the path
/// there. Without it, a function that checks its list, says why it cannot go on and calls sys.exit(1) was read as
/// carrying on to divide by the list's length - and was reported as dividing by something that can be zero.
///
/// A bare exit or quit in Python is only taken as the built-in when the program defines no function of that name.
/// Go's os.Exit and log.Fatal the Go reader turns into throws itself, as C's reader does C's exit.
/// </remarks>
public static class ProgramStops
{
    /// <summary>The name each language's program-ending calls are known by, qualified as they are written.</summary>
    private static readonly IReadOnlyDictionary<SourceLanguage, IReadOnlySet<string>> ByLanguage = new Dictionary<SourceLanguage, IReadOnlySet<string>>
    {
        [SourceLanguage.Python] = new HashSet<string>(StringComparer.Ordinal) { "sys.exit", "os._exit", "exit", "quit" },
        [SourceLanguage.Java] = new HashSet<string>(StringComparer.Ordinal) { "System.exit" },
        [SourceLanguage.CSharp] = new HashSet<string>(StringComparer.Ordinal) { "Environment.Exit", "System.Environment.Exit", "Environment.FailFast", "System.Environment.FailFast" },
        [SourceLanguage.JavaScript] = new HashSet<string>(StringComparer.Ordinal) { "process.exit" },
    };

    /// <summary>
    /// The ones the C and Go readers turn into throws themselves, named as they name them: C's exit and abort, and Go's
    /// os.Exit and log.Fatal. Go's log.Panic is left out, because a panic is an exception a deferred recover can catch.
    /// </summary>
    private static readonly IReadOnlySet<string> StopsReadAlready = new HashSet<string>(StringComparer.Ordinal)
    {
        "exit", "_Exit", "quick_exit", "abort", "terminate", "os.Exit", "log.Fatal", "log.Fatalf", "log.Fatalln",
    };

    /// <summary>Whether a throw is really one of these calls: the program stopping, not an exception anything could catch.</summary>
    public static bool Stops(Throw thrown) =>
        thrown.Exception is NewObject { Type.Name: var name } &&
        (StopsReadAlready.Contains(name) || ByLanguage.Values.Any(names => names.Contains(name)));

    public static IrProgram Lower(IrProgram program)
    {
        if (!ByLanguage.TryGetValue(program.Language, out var stops)) return program;

        var defined = program.AllFunctions.Select(function => function.Name).ToHashSet(StringComparer.Ordinal);
        var lowering = new Lowering(stops, defined);

        return program with
        {
            Functions = program.Functions.Select(lowering.Function).ToList(),
            Classes = program.Classes.Select(type => type with { Methods = type.Methods.Select(lowering.Function).ToList() }).ToList(),
        };
    }

    private sealed class Lowering(IReadOnlySet<string> stops, IReadOnlySet<string> defined)
    {
        public IrFunction Function(IrFunction function) => function with { Body = Block(function.Body) };

        private IReadOnlyList<Stmt> Block(IReadOnlyList<Stmt> block) => block.Select(Statement).ToList();

        private Stmt Statement(Stmt statement) => statement switch
        {
            Evaluate { Value: Call call } when Stopping(call) is { } name =>
                new Throw(statement.Span, new NewObject(call.Span, IrType.Named(name), call.Arguments)),

            If branch => branch with { Then = Block(branch.Then), Else = Block(branch.Else) },
            While loop => loop with { Body = Block(loop.Body), Else = Block(loop.Else) },
            For loop => loop with { Setup = Block(loop.Setup), Step = Block(loop.Step), Body = Block(loop.Body) },
            ForEach loop => loop with { Body = Block(loop.Body), Else = Block(loop.Else) },
            Try attempt => attempt with
            {
                Body = Block(attempt.Body),
                Handlers = attempt.Handlers.Select(handler => handler with { Body = Block(handler.Body) }).ToList(),
                Else = Block(attempt.Else),
                Finally = Block(attempt.Finally),
            },
            Switch choice => choice with { Cases = choice.Cases.Select(option => option with { Body = Block(option.Body) }).ToList() },
            Using used => used with { Body = Block(used.Body) },
            Labeled labeled => labeled with { Body = Block(labeled.Body) },
            _ => statement,
        };

        /// <summary>The call's name as written - sys.exit, System.exit - when it is one that ends the program.</summary>
        private string? Stopping(Call call)
        {
            var written = call.Callee switch
            {
                Name name => defined.Contains(name.Identifier) ? null : name.Identifier,
                Member { Target: Name owner } member => $"{owner.Identifier}.{member.MemberName}",
                Member { Target: Member { Target: Name root } owner } member => $"{root.Identifier}.{owner.MemberName}.{member.MemberName}",
                _ => null,
            };

            return written is not null && stops.Contains(written) ? written : null;
        }
    }
}
