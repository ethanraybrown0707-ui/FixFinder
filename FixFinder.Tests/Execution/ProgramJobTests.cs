using System.Diagnostics;
using FixFinder.Core.Execution;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Each program FixFinder starts is put in a Windows job of its own, which ends it - with whatever it started - when the job is
/// closed: as FixFinder closes it when the run is done, and as Windows does when FixFinder ends, however it ends. Python from
/// the Microsoft Store, already in a job of its own, is put in one too. A computer without the program a row starts passes it
/// over.
/// </summary>
public class ProgramJobTests(ITestOutputHelper output)
{
    /// <summary>Started as TargetRunner starts a program: no shell, no window, every stream read by FixFinder.</summary>
    private static Process? Start(string program, string arguments)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Programs that wait half a minute, so each is still running when it is put in its job and the job closed.</summary>
    public static TheoryData<string, string> Programs => new()
    {
        { "python", "-c \"import time; time.sleep(30)\"" },
        { "node", "-e \"setTimeout(() => {}, 30000)\"" },
        { "powershell", "-NoProfile -Command \"Start-Sleep -Seconds 30\"" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public void EachProgramIsPutInAJobOfItsOwn(string program, string arguments)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Start(program, arguments);
        if (process is null)
        {
            output.WriteLine($"{program} is not on this computer.");
            return;
        }

        // A second program's job first, so the first program's is not the only job: one job for them all was refused for
        // Python from the Microsoft Store.
        using var other = Start("powershell", "-NoProfile -Command \"Start-Sleep -Seconds 30\"");
        using var otherJob = other is null ? null : ProgramJob.Hold(other);

        using var job = ProgramJob.Hold(process);
        try
        {
            Assert.NotNull(job);
            Assert.True(job!.Holds(process));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (other is { HasExited: false }) other.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void ClosingTheJobEndsTheProgramInIt(string program, string arguments)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Start(program, arguments);
        if (process is null)
        {
            output.WriteLine($"{program} is not on this computer.");
            return;
        }

        try
        {
            var job = ProgramJob.Hold(process);
            Assert.NotNull(job);
            job!.Dispose();

            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(10)), $"{program} was still running 10 seconds after its job was closed");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
