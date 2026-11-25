using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Security;
using FixFinder.Core.Teaching;

namespace FixFinder.Tests;

/// <summary>
/// Everything FixFinder keeps on this computer is in one folder - the one FIXFINDER_DATA_FOLDER names, when it is set - so
/// FixFinder can be tried without its checks going into the person's own history and list of problems.
/// </summary>
public class DataFolderTests
{
    [Fact]
    public void EveryFileFixFinderKeepsIsInItsDataFolder()
    {
        var kept = new[] { CheckHistory.FilePath, Preferences.FilePath, LearnProgress.DefaultFile, ProblemStore.DefaultFolder, CompiledLanguages.BuildRoot, TokenStore.FilePath };

        Assert.All(kept, path => Assert.StartsWith(DataFolder.Root + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithoutTheSettingTheFolderIsFixFindersOwnInLocalApplicationData()
    {
        if (Environment.GetEnvironmentVariable(DataFolder.Setting) is { Length: > 0 }) return;

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixFinder"), DataFolder.Root);
    }
}
