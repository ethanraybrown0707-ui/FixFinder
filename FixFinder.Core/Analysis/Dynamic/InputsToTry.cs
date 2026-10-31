using System.Globalization;
using FixFinder.Core.Analysis.Abstract;
using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Solver;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>One input tried, and what the program did with it.</summary>
/// <param name="Typed">The answers typed, one to each question the program asked.</param>
/// <param name="Printed">What it printed, as it printed it.</param>
/// <param name="StoppedWith">What stopped it, when something did: "ZeroDivisionError: division by zero".</param>
public sealed record InputTried(IReadOnlyList<string> Typed, IReadOnlyList<string> Printed, string? StoppedWith)
{
    /// <summary>The answers as a sentence says them: "70", "0 then 0", "an empty line".</summary>
    public string TypedSaid => string.Join(" then ", Typed.Select(answer => answer.Length == 0 ? "an empty line" : answer));
}

/// <summary>
/// Inputs to try, to see what each way through a program does: for a Python program whose top-level code goes different
/// ways on what is typed, one set of answers for each way - worked out from the code, by following each way and solving
/// for what takes it - and the program run with each. For score = int(input()) and if score &gt;= 70 ... elif score &gt;= 50,
/// the answers are 70, 50 and 0, and the three runs print what each grade does.
/// </summary>
/// <remarks>
/// Only for a program that asks all its questions in straight-line top-level code - not in a loop, a branch or a function -
/// so every way asks the same questions in the same order, and each answer goes to the question it was worked out for.
/// A way that the answers alone do not decide, one that goes through code FixFinder cannot see, and a run that asks for
/// more than it was given are left out.
/// </remarks>
public static class InputsToTry
{
    private const int MostTries = 6;

    /// <summary>How long one try is given: these are small runs, and there can be several.</summary>
    private static readonly TimeSpan TryTimeLimit = TimeSpan.FromSeconds(10);

    private static readonly ConstraintSolver Solver = new() { MostBranches = 60, MostPivots = 300 };

    /// <summary>The answers to type for each way through the file's top-level code that what is typed decides.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> AnswersFor(IrProgram program, string file)
    {
        if (program.Language != SourceLanguage.Python) return [];

        var body = program.Functions.FirstOrDefault(function => function.Name == IrFunction.ModuleBody &&
                                                                string.Equals(Path.GetFullPath(function.Span.File), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        if (body is null || !AsksOnlyInStraightLine(program, body)) return [];

        var report = new SymbolicExecutor(CfgBuilder.Build(body), program.Language, IrWalk.LocalNames(body, true), Scopes.Volatile(program, body),
            new Dictionary<string, IrType>())
        {
            KeepEnds = true,
            MostTime = TimeSpan.FromMilliseconds(800),
            OwnTypes = program.Classes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal),
        }.Explore();
        if (report.Symbols is not { } symbols) return [];

        var answerSets = new List<IReadOnlyList<string>>();
        foreach (var end in report.Ends)
        {
            if (answerSets.Count >= MostTries) break;
            if (end.Approximated || end.OutsideDecided) continue;
            // A way chosen by what is typed - at a branch, or at a line that fails for some answers and not others.
            if (!end.Constraints.SelectMany(constraint => constraint.Term.Symbols).Any(id => symbols[id].TypedAt is not null)) continue;

            var solved = Solver.Check(end.Constraints, symbols.IsWhole);
            if (!solved.IsSatisfiable || solved.Model is not { } model) continue;

            var answers = Answers(symbols, model);
            if (answers.Count > 0 && !answerSets.Any(known => known.SequenceEqual(answers))) answerSets.Add(answers);
        }

        return answerSets.Count > 1 ? answerSets : [];
    }

    /// <summary>
    /// Runs the program once with each set of answers, as the check ran it - its own Python, folder and environment - and
    /// keeps what each printed. A try that ran out of answers, or out of time, is left out: it shows nothing about its way.
    /// </summary>
    public static async Task<IReadOnlyList<InputTried>> TryAsync(TargetSpec spec, IReadOnlyList<IReadOnlyList<string>> answerSets, CancellationToken cancellation)
    {
        var tried = new List<InputTried>();

        foreach (var answers in answerSets)
        {
            var run = await new TargetRunner().RunAsync(spec.WithInput(string.Concat(answers.Select(answer => answer + "\n"))).WithTimeout(TryTimeLimit), cancellation);
            if (run.Outcome == RunOutcome.TimedOut || run.Error?.ShortExceptionType == "EOFError") continue;

            var printed = run.Lines.Where(line => line.Stream == StreamKind.StdOut).Select(line => line.Text).ToList();
            var stopped = run.Error is { } error ? (error.Message is { Length: > 0 } message ? $"{error.ShortExceptionType ?? error.ExceptionType}: {message}" : error.Summary) : null;
            tried.Add(new InputTried(answers, printed, stopped));
        }

        // Inputs that all did the same show nothing a reader could compare.
        return tried.Select(each => (string.Join("\n", each.Printed), each.StoppedWith)).Distinct().Count() > 1 ? tried : [];
    }

    /// <summary>
    /// Whether every question the program asks - every input() - is asked in its top-level code, in a statement that runs
    /// once: not in a loop, a branch's body, or a function, where how many are asked, and in what order, depends on the way.
    /// </summary>
    private static bool AsksOnlyInStraightLine(IrProgram program, IrFunction body)
    {
        static int Asked(IEnumerable<Expr> expressions) =>
            expressions.SelectMany(IrWalk.Within).Count(expression => expression is Call { Callee: Name { Identifier: "input" } });

        var everywhere = Asked(IrWalk.Statements(body.Body).SelectMany(IrWalk.Expressions));
        var straight = Asked(body.Body.Where(statement => statement is Evaluate or Assign or Declare or If).SelectMany(IrWalk.Expressions));
        var elsewhere = program.AllFunctions.Where(function => function != body)
            .Sum(function => Asked(IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions)));

        return everywhere > 0 && everywhere == straight && elsewhere == 0;
    }

    /// <summary>
    /// One answer to each question, in the order they are asked: the number a question was turned into when there is one -
    /// int(input()) - else text of the length worked out, else an empty line.
    /// </summary>
    private static List<string> Answers(SymbolTable symbols, IReadOnlyDictionary<int, Rational> model)
    {
        var typed = Enumerable.Range(0, symbols.Count).Select(id => symbols[id]).Where(symbol => symbol.TypedAt is not null)
            .GroupBy(symbol => (Line: symbol.TypedAt!.Value, symbol.Occurrence))
            .OrderBy(question => question.Key.Line).ThenBy(question => question.Key.Occurrence);

        var answers = new List<string>();
        foreach (var question in typed)
        {
            var number = question.FirstOrDefault(symbol => symbol.Origin == SymbolOrigin.Input);
            var length = question.FirstOrDefault(symbol => symbol.Origin == SymbolOrigin.TextLength);

            if (number is not null)
            {
                var value = model.GetValueOrDefault(number.Id, Rational.Zero);
                answers.Add(number.IsWhole || value.IsInteger
                    ? value.Numerator.ToString(CultureInfo.InvariantCulture)
                    : value.ToDouble().ToString("R", CultureInfo.InvariantCulture));
            }
            else
            {
                var characters = length is null ? 0 : (int)Math.Min(20, Math.Max(0, model.GetValueOrDefault(length.Id, Rational.Zero).ToDouble()));
                answers.Add(new string('x', characters));
            }
        }

        return answers;
    }
}
