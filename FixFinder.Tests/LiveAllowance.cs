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
/// would have waited long enough (run on d7cef76, 2026-09-25). One place decides it, so the next live test cannot
/// quietly choose a number of its own.
/// </remarks>
public static class LiveAllowance
{
    /// <summary>
    /// The longer of what FixFinder gives a file of this kind and <paramref name="atLeast"/>, which a test sets from
    /// what its program does beyond running - compiling, building a fix, running again.
    /// </summary>
    public static TimeSpan For(string file, TimeSpan atLeast)
    {
        var product = TargetFactory.TimeoutFor(Path.GetExtension(file));
        return product > atLeast ? product : atLeast;
    }
}
