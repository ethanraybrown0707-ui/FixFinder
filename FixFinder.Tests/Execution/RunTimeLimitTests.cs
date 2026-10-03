using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Tests that change the time a run is given, which every run shares. xUnit runs this collection on its own, after everything
/// else, so no other test's program is ever stopped sooner, or let run longer, than it expects.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedRunTimeLimit
{
    public const string Name = "the time every run shares";
}

[Collection(SharedRunTimeLimit.Name)]
public class RunTimeLimitTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly TimeSpan _before = TargetFactory.RunTimeLimit;

    public void Dispose()
    {
        TargetFactory.RunTimeLimit = _before;
        _temp.Dispose();
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void ARunIsGivenTheTimeChosen()
    {
        var program = Write("marks.py", "print('hi')\n");

        TargetFactory.RunTimeLimit = TimeSpan.FromMinutes(5);

        Assert.Equal(TimeSpan.FromMinutes(5), TargetFactory.FromFile(program).Spec!.Timeout);
    }

    /// <summary>Go's first build compiles its standard library, so a Go program is never given less than the six minutes that takes.</summary>
    [Fact]
    public void GoIsNeverGivenLessThanItsFirstBuildTakes()
    {
        TargetFactory.RunTimeLimit = TimeSpan.FromSeconds(10);
        Assert.Equal(TargetFactory.FirstRunTimeout, TargetFactory.TimeoutFor(".go"));
        Assert.Equal(TimeSpan.FromSeconds(10), TargetFactory.TimeoutFor(".py"));

        TargetFactory.RunTimeLimit = TimeSpan.FromMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(10), TargetFactory.TimeoutFor(".go"));
    }

    /// <summary>The time comes from a preferences file anybody can edit, so one outside a second to an hour is the default.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3601)]
    [InlineData(int.MaxValue)]
    public void ATimeOutsideASecondToAnHourIsTheDefault(int seconds)
    {
        TargetFactory.RunTimeLimit = TimeSpan.FromSeconds(seconds);
        Assert.Equal(TargetFactory.DefaultTimeout, TargetFactory.RunTimeLimit);

        Assert.Equal(TargetFactory.DefaultTimeout, new Preferences { RunSeconds = seconds }.RunTimeLimit);
    }

    [Fact]
    public void TheTimeChosenAndTheWindowChoiceAreKeptBetweenSessions()
    {
        var file = Path.Combine(_temp.Path, "preferences.json");

        new Preferences { RunSeconds = 300, WindowsRunUntilClosed = true }.Save(file);
        var read = Preferences.Load(file);

        Assert.Equal(TimeSpan.FromMinutes(5), read.RunTimeLimit);
        Assert.True(read.WindowsRunUntilClosed);
    }

    /// <summary>
    /// Only a program with a window runs until it is closed, and only its own run: a server is not closed by anybody, and the
    /// copies a change is tried in keep the time a run is given.
    /// </summary>
    [Fact]
    public void OnlyAProgramWithAWindowRunsUntilItIsClosed()
    {
        var window = TargetFactory.FromFile(Write("draw.py", "import turtle\nturtle.forward(10)\nturtle.done()\n"));
        var server = TargetFactory.FromFile(Write("serve.py", "import http.server\nhttp.server.test()\n"));
        var plain = TargetFactory.FromFile(Write("add.py", "print(1 + 1)\n"));

        var (untilClosed, note) = ProgramChecker.RunFor(window, windowsRunUntilClosed: true);
        Assert.Equal(Timeout.InfiniteTimeSpan, untilClosed.Timeout);
        Assert.StartsWith("draw.py is a program with a window - it uses turtle, so it ran until its window was closed, as chosen", note, StringComparison.Ordinal);

        Assert.Equal(window.Spec!.Timeout, ProgramChecker.RunFor(window, windowsRunUntilClosed: false).Spec.Timeout);
        Assert.Null(ProgramChecker.RunFor(server, windowsRunUntilClosed: true).WindowNote);
        Assert.Equal(server.Spec!.Timeout, ProgramChecker.RunFor(server, windowsRunUntilClosed: true).Spec.Timeout);
        Assert.Null(ProgramChecker.RunFor(plain, windowsRunUntilClosed: true).WindowNote);
    }
}
