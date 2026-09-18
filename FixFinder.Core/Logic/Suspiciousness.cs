namespace FixFinder.Core.Logic;

/// <summary>How suspicious one line is, from which runs went through it.</summary>
/// <param name="Line">The 1-based line.</param>
/// <param name="FailedThrough">How many runs with the wrong output executed it.</param>
/// <param name="PassedThrough">How many runs with the right output executed it.</param>
public sealed record LineSuspicion(int Line, int FailedThrough, int PassedThrough, double Ochiai, double Tarantula, double DStar);

/// <summary>
/// Spectrum-based fault localisation: which lines a failing run went through that a passing run did not.
/// </summary>
/// <remarks>
/// The idea is simple and old. A line every wrong run executes and no right run does is more likely to hold the mistake than a
/// line every run executes. Three standard formulas put a number on that, from the same four counts per line - failed runs
/// that executed it (ef), passed runs that executed it (ep), and the totals of each (F, P):
/// <list type="bullet">
/// <item><b>Ochiai</b> = ef / √(F × (ef + ep)) - the one studies most often find best, and the one lines are ordered by.</item>
/// <item><b>DStar</b> (*=2) = ef² / (ep + (F − ef)) - breaks Ochiai's ties by punishing lines that failed runs skipped.</item>
/// <item><b>Tarantula</b> = (ef/F) / (ef/F + ep/P) - the original, shown for comparison.</item>
/// </list>
/// With a single failing run and no passing one, every executed line scores the same, and the order falls back to how likely
/// each line is to hold the kind of mistake a small change fixes. More runs, some right and some wrong, sharpen it.
/// </remarks>
public static class Suspiciousness
{
    public static double Ochiai(int ef, int ep, int totalFailed) =>
        ef == 0 || totalFailed == 0 ? 0 : ef / Math.Sqrt((double)totalFailed * (ef + ep));

    public static double Tarantula(int ef, int ep, int totalFailed, int totalPassed)
    {
        if (ef == 0 || totalFailed == 0) return 0;

        var failed = (double)ef / totalFailed;
        var passed = totalPassed == 0 ? 0 : (double)ep / totalPassed;

        return failed / (failed + passed);
    }

    public static double DStar(int ef, int ep, int totalFailed, int star = 2)
    {
        if (ef == 0) return 0;

        var denominator = ep + (totalFailed - ef);
        return denominator == 0 ? double.PositiveInfinity : Math.Pow(ef, star) / denominator;
    }

    /// <summary>Every line any run executed, most suspicious first; ties keep the order the lines appear in.</summary>
    /// <param name="coverage">For each run, whether its output was right, and the lines it executed.</param>
    public static IReadOnlyList<LineSuspicion> Rank(IReadOnlyList<(bool Passed, IReadOnlySet<int> Lines)> coverage)
    {
        var failed = coverage.Count(c => !c.Passed);
        var passed = coverage.Count - failed;

        return coverage.SelectMany(c => c.Lines).Distinct()
            .Select(line =>
            {
                var ef = coverage.Count(c => !c.Passed && c.Lines.Contains(line));
                var ep = coverage.Count(c => c.Passed && c.Lines.Contains(line));

                return new LineSuspicion(line, ef, ep, Ochiai(ef, ep, failed), Tarantula(ef, ep, failed, passed), DStar(ef, ep, failed));
            })
            .Where(s => s.FailedThrough > 0)
            .OrderByDescending(s => s.Ochiai)
            .ThenByDescending(s => s.DStar)
            .ThenBy(s => s.Line)
            .ToList();
    }
}
