using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Builds a Fortran program with gfortran and runs what it built. The program is the chosen file and the files beside it
/// that define the modules it uses - each module's file before the files that use it, as gfortran needs - written into
/// FixFinder's own build folder with the .mod files gfortran makes, so nothing is written into the program's. It is built
/// with debugging information, every warning -Wall gives, and -fcheck=all, so a position outside an array, among other
/// slips, stops the program at its line rather than reading whatever is there.
/// </summary>
public static partial class FortranBuild
{
    /// <summary>The program gfortran builds, in the build folder.</summary>
    public const string Program = "program.exe";

    /// <summary>The extensions gfortran reads as free-form Fortran source - the form a program is written in now.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>([".f90", ".f95", ".f03", ".f08"], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\s*module\s+(?!procedure\b)(?<name>\w+)\s*(?:!.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ModuleDeclaration();

    [GeneratedRegex(@"^\s*use\s*(?:,\s*\w+\s*)?(?:::\s*)?(?<name>\w+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ModuleUsed();

    public static string? Gfortran() => TargetFactory.FindOnPath("gfortran");

    private static readonly Lazy<string?> AskedVersion = new(() => Gfortran() is { } gfortran ? FirstLine(gfortran, "--version") : null);

    /// <summary>What gfortran says it is - "GNU Fortran (MinGW-W64 ...) 13.2.0" - asked once; null when there is none.</summary>
    public static string? Version() => AskedVersion.Value;

    private static string? FirstLine(string program, string arguments)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            });
            if (process is null) return null;

            var said = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(30_000) ? said.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    public static (BuildAndRun? Plan, string? Problem) Prepare(string source, string output, TimeSpan timeout)
    {
        if (Gfortran() is not { } gfortran)
        {
            return (null,
                $"{Path.GetFileName(source)} is Fortran, which has to be compiled before it can run, and no gfortran was found on PATH.\n\n" +
                "Install gfortran - it comes with MinGW-w64, and with Strawberry Perl.");
        }

        var files = Files(source);
        var folder = Path.GetDirectoryName(Path.GetFullPath(source))!;

        var compile = new TargetSpec
        {
            ExecutablePath = gfortran,
            Arguments = CompileArguments(output, Path.Combine(output, Program), files.Select(file => Path.GetRelativePath(folder, file))),
            WorkingDirectory = folder,
            Timeout = timeout,
        };

        var run = new TargetSpec
        {
            ExecutablePath = Path.Combine(output, Program),
            Arguments = "",
            WorkingDirectory = folder,
            Timeout = timeout,
        };

        var together = files.Count > 1 ? $" with {string.Join(", ", files.Where(file => !file.Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName))}, whose modules it uses," : "";
        return (new BuildAndRun(compile, run, $"Building {Path.GetFileName(source)}{together} with gfortran, checking each array position and more as it runs, then running it."), null);
    }

    /// <summary>gfortran's arguments: debugging information, -Wall, the run-time checks, where the .mod files and the program go, and the files.</summary>
    public static string CompileArguments(string moduleFolder, string outputFile, IEnumerable<string> files) =>
        $"-g -Wall -fcheck=all -fbacktrace -J \"{moduleFolder}\" -o \"{outputFile}\" " + string.Join(" ", files.Select(file => $"\"{file}\""));

    /// <summary>The arguments that check files without building them, for a change tried in a copy.</summary>
    public static string CheckArguments(string moduleFolder, IEnumerable<string> files) =>
        $"-fsyntax-only -Wall -J \"{moduleFolder}\" " + string.Join(" ", files.Select(file => $"\"{file}\""));

    /// <summary>
    /// The chosen file and the files beside it that define the modules it uses, and the ones those use - each module's
    /// file before every file that uses it, the chosen file last.
    /// </summary>
    public static IReadOnlyList<string> Files(string chosen)
    {
        var whole = Path.GetFullPath(chosen);
        var folder = Path.GetDirectoryName(whole)!;
        var definedIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(folder).Where(file => Extensions.Contains(Path.GetExtension(file))))
        {
            if (TextOf(file) is not { } text) continue;
            foreach (Match module in ModuleDeclaration().Matches(text)) definedIn.TryAdd(module.Groups["name"].Value, file);
        }

        var ordered = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string file)
        {
            if (ordered.Contains(file, StringComparer.OrdinalIgnoreCase) || !visiting.Add(file)) return;

            foreach (Match used in ModuleUsed().Matches(TextOf(file) ?? ""))
                if (definedIn.TryGetValue(used.Groups["name"].Value, out var definer) && !definer.Equals(file, StringComparison.OrdinalIgnoreCase))
                    Visit(definer);

            ordered.Add(file);
        }

        Visit(whole);
        return ordered;
    }

    private static string? TextOf(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
