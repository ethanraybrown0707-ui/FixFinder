using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Measures;

/// <summary>
/// How complex a function is, as McCabe's cyclomatic complexity counts it: 1 for the function, and 1 more for each place
/// in it where the way it goes is decided - each if, and each else-if, which is an if of its own; each loop; each case of
/// a switch, its default left out; each catch; each conditional expression, a ? b : c or Python's b if a else c; and each
/// &amp;&amp; and || - and and or in Python - which decide whether what is on their right is worked out at all.
/// </summary>
/// <remarks>
/// It is counted from the code as its language's reader reads it. A function written inside another, and a lambda, are
/// counted as functions of their own - or not at all, for a lambda - and not into the one around them; a C-style for
/// with no condition, for (;;), decides nothing and is not counted; a Python comprehension's own for and if clauses are
/// not counted, as the reader keeps only the list a comprehension goes through; a Python chained comparison,
/// 0 &lt;= mark &lt;= 100, is read as the and it is; and a Python match as the ifs it does the work of.
/// </remarks>
public static class Complexity
{
    /// <summary>How many of a file's most complex functions are said.</summary>
    public const int MostSaid = 5;

    /// <summary>A function's complexity, and the decisions in it that make it up.</summary>
    public sealed record Measured(IrFunction Function, int Ifs, int Loops, int Cases, int Catches, int Conditionals, int AndsAndOrs)
    {
        /// <summary>McCabe's cyclomatic complexity: 1, and 1 for each decision.</summary>
        public int Value => 1 + Ifs + Loops + Cases + Catches + Conditionals + AndsAndOrs;

        /// <summary>"`grade` on line 12: 7 - 3 `if`s, 2 loops and 1 `and` or `or`".</summary>
        public string Said
        {
            get
            {
                var decisions = new[]
                {
                    Counted(Ifs, "`if`", "`if`s"), Counted(Loops, "loop", "loops"), Counted(Cases, "case", "cases"),
                    Counted(Catches, "catch", "catches"), Counted(Conditionals, "conditional expression", "conditional expressions"),
                    Counted(AndsAndOrs, "`and` or `or`", "`and`s and `or`s"),
                }.OfType<string>().ToList();

                var listed = decisions.Count == 1 ? decisions[0] : $"{string.Join(", ", decisions[..^1])} and {decisions[^1]}";
                var named = Function.Name == IrFunction.ModuleBody
                    ? "The code outside any function"
                    : $"`{(Function.Owner is { } owner ? $"{owner}.{Function.Name}" : Function.Name)}` on line {Function.Span.Line}";
                return $"{named}: {Value} - {listed}";
            }
        }

        private static string? Counted(int count, string one, string many) => count switch
        {
            0 => null,
            1 => $"1 {one}",
            _ => $"{count} {many}",
        };
    }

    /// <summary>A function's complexity, from the statements and expressions in its own body.</summary>
    public static Measured Of(IrFunction function)
    {
        int ifs = 0, loops = 0, cases = 0, catches = 0, conditionals = 0, andsAndOrs = 0;

        // An expression written once can be held by two statements - Python's self.total = total = a if b else c is two
        // assignments of the one value - so each is counted once, however many statements hold it.
        var countedAlready = new HashSet<Expr>(ReferenceEqualityComparer.Instance);

        foreach (var statement in IrWalk.Statements(function.Body))
        {
            switch (statement)
            {
                case If:
                    ifs++;
                    break;
                case While:
                case ForEach:
                case For { Condition: not null }:
                    loops++;
                    break;
                case Switch choice:
                    cases += choice.Cases.Sum(@case => @case.Labels.Count);
                    break;
                case Try attempt:
                    catches += attempt.Handlers.Count;
                    break;
            }

            foreach (var expression in IrWalk.Expressions(statement).SelectMany(IrWalk.Within))
            {
                if (!countedAlready.Add(expression)) continue;
                if (expression is Conditional) conditionals++;
                else if (expression is Binary { Operator: BinaryOperator.And or BinaryOperator.Or }) andsAndOrs++;
            }
        }

        return new Measured(function, ifs, loops, cases, catches, conditionals, andsAndOrs);
    }

    /// <summary>
    /// The most complex functions in <paramref name="chosenFile"/> - in every file when none is named - most complex first,
    /// at most <see cref="MostSaid"/> of them, each in words; a function that decides nothing is not said.
    /// </summary>
    public static IReadOnlyList<string> MostComplex(IrProgram program, string? chosenFile) =>
        [.. program.AllFunctions
            .Where(function => chosenFile is null || InFile(function, chosenFile))
            .Select(Of)
            .Where(measured => measured.Value > 1)
            .OrderByDescending(measured => measured.Value).ThenBy(measured => measured.Function.Span.Line)
            .Take(MostSaid)
            .Select(measured => measured.Said)];

    /// <summary>Whether a function is written in the file named; one read with no file of its own is in none.</summary>
    private static bool InFile(IrFunction function, string file) =>
        function.Span.File is { Length: > 0 } written &&
        string.Equals(Path.GetFullPath(written), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase);
}
