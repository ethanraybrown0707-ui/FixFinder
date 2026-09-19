using FixFinder.Core.Analysis.Solver;

namespace FixFinder.Tests;

/// <summary>The solver's answers, checked by hand on known cases and against trying every value on random small systems.</summary>
public class ConstraintSolverTests
{
    private static readonly ConstraintSolver Solver = new();

    private static LinearTerm X => LinearTerm.Symbol(0);
    private static LinearTerm Y => LinearTerm.Symbol(1);
    private static LinearTerm Z => LinearTerm.Symbol(2);

    private static SolverResult Whole(params Constraint[] constraints) => Solver.Check(constraints, _ => true);

    private static SolverResult Real(params Constraint[] constraints) => Solver.Check(constraints, _ => false);

    [Fact]
    public void ASimpleRangeHasAValueInIt()
    {
        var result = Whole(Constraint.AtLeast(X, 3), Constraint.AtMost(X, 7));

        Assert.True(result.IsSatisfiable);
        Assert.True(result.Model![0] >= 3 && result.Model[0] <= 7);
    }

    [Fact]
    public void ContradictoryBoundsAreUnsatisfiable() =>
        Assert.True(Whole(Constraint.Above(X, 5), Constraint.Below(X, 3)).IsUnsatisfiable);

    [Fact]
    public void TwoEquationsAreSolvedTogether()
    {
        var result = Whole(Constraint.Same(X + Y, 10), Constraint.Same(X - Y, 2));

        Assert.Equal(((Rational)6, (Rational)4), (result.Model![0], result.Model[1]));
    }

    [Fact]
    public void AGapBetweenWholeNumbersIsFound()
    {
        Assert.True(Whole(Constraint.Above(X, 0), Constraint.Below(X, 1)).IsUnsatisfiable);
        Assert.True(Real(Constraint.Above(X, 0), Constraint.Below(X, 1)).IsSatisfiable);
    }

    [Fact]
    public void AnEquationWithNoWholeSolutionIsUnsatisfiableAtOnce()
    {
        Assert.True(Whole(Constraint.Same(X * 2 - Y * 2, 1)).IsUnsatisfiable);
        Assert.True(Real(Constraint.Same(X * 2 - Y * 2, 1)).IsSatisfiable);
    }

    [Fact]
    public void NotEqualIsSplitIntoBelowOrAbove()
    {
        Assert.True(Whole(Constraint.AtLeast(X, 0), Constraint.AtMost(X, 0), Constraint.Different(X, 0)).IsUnsatisfiable);

        var result = Whole(Constraint.AtLeast(X, 0), Constraint.AtMost(X, 1), Constraint.Different(X, 0));
        Assert.Equal((Rational)1, result.Model![0]);
    }

    [Fact]
    public void AStrictBoundOnRealsGivesAValueStrictlyInside()
    {
        var result = Real(Constraint.Above(X, 1), Constraint.Below(X + Y, 2), Constraint.Same(Y, 0));

        Assert.True(result.IsSatisfiable);
        Assert.True(result.Model![0] > 1 && result.Model[0] < 2);
    }

    [Fact]
    public void AThreeVariableSystemNeedingPivotsIsSolved()
    {
        var result = Whole(
            Constraint.AtMost(X + Y + Z, 10),
            Constraint.AtLeast(X - Y, 3),
            Constraint.AtLeast(Y + Z * 2, 7),
            Constraint.AtLeast(X, 0), Constraint.AtLeast(Y, 0), Constraint.AtLeast(Z, 0));

        Assert.True(result.IsSatisfiable);
        var model = result.Model!;
        Assert.True(model.Values.All(v => v.IsInteger));
        Assert.True(model[0] + model[1] + model[2] <= 10 && model[0] - model[1] >= 3 && model[1] + model[2] * 2 >= 7);
    }

    [Fact]
    public void ExactFractionsSurviveRoundTrips()
    {
        Assert.Equal(new Rational(1, 10) + new Rational(2, 10), new Rational(3, 10));
        Assert.Equal((Rational)(-2), new Rational(-3, 2).Floor());
        Assert.Equal((Rational)(-1), new Rational(-3, 2).Ceiling());
        Assert.Equal(new Rational(1, 2), Rational.FromDouble(0.5));
        Assert.Equal((Rational)12, Rational.FromDouble(12.0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomSmallSystemsAgreeWithTryingEveryValue(int seed)
    {
        var random = new Random(seed);
        const int Low = -4, High = 4;

        for (var round = 0; round < 150; round++)
        {
            var symbols = random.Next(1, 4);
            var constraints = new List<Constraint>();

            for (var s = 0; s < symbols; s++)
            {
                constraints.Add(Constraint.AtLeast(LinearTerm.Symbol(s), Low));
                constraints.Add(Constraint.AtMost(LinearTerm.Symbol(s), High));
            }

            for (var c = random.Next(1, 5); c > 0; c--)
            {
                var term = LinearTerm.Of(random.Next(-6, 7));
                for (var s = 0; s < symbols; s++) term += LinearTerm.Symbol(s) * random.Next(-3, 4);
                constraints.Add(new Constraint(term, (Relation)random.Next(4)));
            }

            var expected = Enumerate(symbols, Low, High).Any(values => constraints.All(c => c.HoldsFor(values)));
            var result = Whole([.. constraints]);

            Assert.NotEqual(Verdict.Unknown, result.Verdict);
            Assert.True(expected == result.IsSatisfiable, $"seed {seed} round {round}: expected {expected}, got {result.Verdict} for {string.Join(" and ", constraints)}");

            if (result.IsSatisfiable)
                Assert.True(constraints.All(c => c.HoldsFor(result.Model!)) && result.Model!.Values.All(v => v.IsInteger),
                    $"seed {seed} round {round}: the model breaks a constraint");
        }
    }

    private static IEnumerable<Dictionary<int, Rational>> Enumerate(int symbols, int low, int high)
    {
        IEnumerable<Dictionary<int, Rational>> all = [new Dictionary<int, Rational>()];

        for (var s = 0; s < symbols; s++)
        {
            var symbol = s;
            all = all.SelectMany(partial => Enumerable.Range(low, high - low + 1)
                .Select(v => new Dictionary<int, Rational>(partial) { [symbol] = v }));
        }

        return all;
    }
}
