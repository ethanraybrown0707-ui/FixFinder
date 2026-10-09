using System.Diagnostics;
using System.IO;

namespace FixFinder.Gui;

/// <summary>
/// Starts FixFinder Learn with a problem's code, from wherever it is beside FixFinder: the single exe a release ships as,
/// the DLL a build makes, or - while FixFinder is being built from its source - its own project's build.
/// </summary>
/// <remarks>
/// A DLL is started through dotnet.exe, which Windows already trusts, rather than through an exe built on this computer:
/// Smart App Control can refuse a newly built exe it has not seen before, and dotnet.exe is the dependable way in.
/// </remarks>
public static class LearnLauncher
{
    /// <summary>What FixFinder Learn is called beside FixFinder: its release's exe, then a build's DLL and exe.</summary>
    private static readonly string[] Names = ["FixFinderLearn.exe", "FixFinder.Learn.dll", "FixFinder.Learn.exe"];

    /// <summary>Where FixFinder Learn is, or null when it is nowhere FixFinder looks.</summary>
    public static string? Find()
    {
        var here = AppContext.BaseDirectory;

        foreach (var name in Names)
        {
            var beside = Path.Combine(here, name);
            if (File.Exists(beside)) return beside;
        }

        // Built from source, each project builds into its own bin folder: FixFinder.Gui\bin\Debug\net8.0-windows beside
        // FixFinder.Learn\bin\Debug\net8.0-windows, under the same solution folder four levels up.
        var built = new DirectoryInfo(here);
        var configuration = built.Parent?.Name;
        var solution = built.Parent?.Parent?.Parent?.Parent;
        if (solution is null || configuration is null) return null;

        var learnBuild = Path.Combine(solution.FullName, "FixFinder.Learn", "bin", configuration, built.Name, "FixFinder.Learn.dll");
        return File.Exists(learnBuild) ? learnBuild : null;
    }

    /// <summary>Starts FixFinder Learn on a problem's code, or says why it could not be.</summary>
    public static string? Open(string problemCode)
    {
        if (Find() is not { } learn)
            return "FixFinder Learn was not found beside FixFinder. Its code is copied, ready to paste into FixFinder Learn when it is opened.";

        try
        {
            var start = learn.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("dotnet", $"\"{learn}\" {problemCode}") { UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo(learn, problemCode) { UseShellExecute = false };

            start.WorkingDirectory = Path.GetDirectoryName(learn)!;
            Process.Start(start);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return $"FixFinder Learn could not be started: {ex.Message}";
        }
    }
}
