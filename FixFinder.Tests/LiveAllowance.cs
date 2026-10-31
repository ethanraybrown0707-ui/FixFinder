using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// How long a test lets a real program run: never less than FixFinder itself would give it.
/// </summary>
/// <remarks>
/// A test that allows a program less time than the product does is stricter than the thing it is testing, and fails
/// for reasons that have nothing to do with FixFinder. Go is where that bites. A first Go run on a machine with an empty
/// build cache compiles the standard library before the program, so FixFinder gives it minutes; tests that each chose
/// their own three minutes failed on a cold CI runner at three minutes and one second while FixFinder, left to itself,
/// would have waited long enough (run on d7cef76, 2026-09-25). Go's and C#'s build now has a time of its own, before
/// the run, and the run is given what any program is. One place decides it, so the next live test cannot quietly
/// choose a number of its own.
/// </remarks>
public static class LiveAllowance
{
    /// <summary>
    /// The longer of what FixFinder gives a file of this kind and <paramref name="atLeast"/>, which a test sets from
    /// what its program does beyond running - compiling, building a fix, running again.
    /// </summary>
    public static TimeSpan For(string file, TimeSpan atLeast)
    {
        // Go's and C#'s builds are given their own time before the run; the run is given what any program is.
        var product = TargetFactory.RunTimeLimit;
        return product > atLeast ? product : atLeast;
    }
}
