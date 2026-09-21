namespace FixFinder.Core.Logic;

/// <summary>How suspicious one line is, from which runs went through it.</summary>
public sealed record LineSuspicion(int Line, int FailedThrough, int PassedThrough, double Ochiai, double Tarantula, double DStar);

/// <summary>Spectrum-based fault localisation: which lines a failing run went through that a passing run did not.</summary>
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
