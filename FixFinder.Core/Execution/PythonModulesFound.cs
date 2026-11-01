using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace FixFinder.Core.Execution;

/// <summary>
/// Which modules a Python finds by itself - in its standard library or among the packages installed for it - asked of
/// that Python, so a module of the same name in a program's folders is never put ahead of the one it already has.
/// </summary>
internal static class PythonModulesFound
{
    private static readonly ConcurrentDictionary<(string Interpreter, string Module), bool> FoundBefore = new();

    /// <summary>
    /// Asks Python, from an empty folder, whether it can find each module: importlib's find_spec looks for a top-level
    /// module without running it. Its answers are remembered for each Python. When it gives none - twice, as Python can fail
    /// to start on a busy computer - none are said to be found, and it is asked again the next time.
    /// </summary>
    public static IReadOnlySet<string> Among(string interpreter, IReadOnlyCollection<string> modules)
    {
        var notAskedYet = modules.Where(module => !FoundBefore.ContainsKey((interpreter, module))).ToList();

        if (notAskedYet.Count > 0 && (Ask(interpreter, notAskedYet) ?? Ask(interpreter, notAskedYet)) is { } found)
        {
            foreach (var module in notAskedYet) FoundBefore[(interpreter, module)] = found.Contains(module);
        }

        return modules.Where(module => FoundBefore.TryGetValue((interpreter, module), out var isFound) && isFound).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string>? Ask(string interpreter, IReadOnlyList<string> modules)
    {
        // An empty folder to ask from, as Python looks for modules in the folder it is started in first.
        var emptyFolder = Path.Combine(Path.GetTempPath(), "FixFinder-modules", Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(emptyFolder);
            var start = new ProcessStartInfo(interpreter)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = emptyFolder,
            };

            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import importlib.util, sys; print(*[name for name in sys.argv[1:] if importlib.util.find_spec(name) is not None])");
            foreach (var module in modules) start.ArgumentList.Add(module);

            using var process = Process.Start(start);
            if (process is null) return null;

            process.StandardInput.Close();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return null;
            }

            return process.ExitCode == 0 && output.Wait(ToolOutput.AfterExit)
                ? output.Result.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
                : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try { if (Directory.Exists(emptyFolder)) Directory.Delete(emptyFolder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
