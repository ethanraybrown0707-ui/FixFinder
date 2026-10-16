namespace FixFinder.Tests;

/// <summary>
/// Live tests heavy enough - a compiler, or a new PowerShell, started again and again for each check - to starve the tests
/// running beside them on a small machine, so that a one-line program elsewhere was still running when its time ran out.
/// They run after every other test, one at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BuildsRunAlone
{
    public const string Name = "Builds that run alone";
}
