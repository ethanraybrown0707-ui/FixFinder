namespace FixFinder.Core.Execution;

/// <summary>A target worked out from a file, or the reason one could not be.</summary>
/// <param name="Spec">Ready to run, or null.</param>
/// <param name="Problem">What stopped it, in words meant for the person who picked the file.</param>
/// <param name="Explanation">How it will be launched, shown so the choice is never a surprise.</param>
public sealed record LaunchPlan(TargetSpec? Spec, string? Problem, string Explanation)
{
    /// <summary>
    /// The file the person actually chose.
    /// </summary>
    /// <remarks>
    /// Not the same as the executable path, and the window has to show this one. A script's
    /// executable is its interpreter, so displaying that back turns "reader.py" into
    /// "python.EXE" - which is true, and is not the answer to "what did I just pick".
    /// </remarks>
    public string? ChosenFile { get; init; }

    /// <summary>
    /// A build step to run before the program, for languages that must be compiled.
    /// </summary>
    /// <remarks>
    /// When this fails, its output <i>is</i> the error worth looking up - a compiler diagnostic
    /// carries a globally unique code that thousands of people have searched for, which is a far
    /// better search term than most runtime messages.
    /// </remarks>
    public TargetSpec? Compile { get; init; }

    /// <summary>
    /// The folder holding the source, for when the program is built somewhere else.
    /// </summary>
    /// <remarks>
    /// A compiled program runs from a build directory, so nothing about the running process
    /// points back at the code. Without this, a C program that crashes without a stack trace
    /// would resolve its source root to the folder full of object files.
    /// </remarks>
    public string? SourceFolder { get; init; }

    public bool NeedsCompiling => Compile is not null;

    public bool Ok => Spec is not null;

    public static LaunchPlan Failed(string problem) => new(null, problem, "");
}

/// <summary>
/// Turns a file somebody picked into something that can actually be launched.
/// </summary>
/// <remarks>
/// The piece that makes "just add a file" true rather than nearly true. Picking
/// <c>crash.py</c> has to mean <c>python crash.py</c>, and a <c>.jar</c> has to mean
/// <c>java -jar</c>; without that the person is back to filling in a program box and an
/// arguments box and knowing which goes where.
/// <para>
/// When the interpreter for a file is not installed, that is said plainly and by name. The
/// alternative - launching anyway and reporting a Win32 error - produces a crash report about
/// FixFinder rather than about the program, which is the least useful possible answer.
/// </para>
/// </remarks>
public static class TargetFactory
{
    /// <summary>How long a target gets before it is assumed to be running happily.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <param name="Interpreter">Command that runs the file, or null when it runs itself.</param>
    /// <param name="ArgumentPrefix">Anything that goes before the file, such as <c>-jar</c>.</param>
    /// <param name="Alternatives">Other names the interpreter is known by, tried in order.</param>
    private sealed record Runner(
        string? Interpreter, string ArgumentPrefix = "", params string[] Alternatives);

    private static readonly Dictionary<string, Runner> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = new(null),
        [".com"] = new(null),
        [".bat"] = new(null),
        [".cmd"] = new(null),

        // A managed .dll is not executable, and on this machine the dotnet host is also the way
        // past the Application Control policy that blocks freshly-built binaries.
        [".dll"] = new("dotnet"),

        // "py" is the Windows launcher and is usually the more reliable of the two, because a
        // bare "python" on PATH is often the Store alias stub.
        [".py"] = new("python", "", "py", "python3"),
        [".pyw"] = new("pythonw", "", "python", "py"),

        // The .NET SDK builds and runs a loose .cs file in one command, so a compile error and
        // a runtime crash both arrive through the same run - and the MSVC parser already lifts
        // the CS#### code out of the first kind.
        [".cs"] = new("dotnet", "run"),
        [".csproj"] = new("dotnet", "run --project"),
        [".fsproj"] = new("dotnet", "run --project"),
        [".sln"] = new("dotnet", "run --project"),

        [".js"] = new("node"),
        [".mjs"] = new("node"),
        [".cjs"] = new("node"),
        [".ts"] = new("npx", "tsx", "ts-node"),

        [".jar"] = new("java", "-jar"),
        [".rb"] = new("ruby"),
        [".pl"] = new("perl"),
        [".php"] = new("php"),
        [".lua"] = new("lua"),
        [".sh"] = new("bash", "", "sh"),
        [".ps1"] = new("powershell", "-NoProfile -ExecutionPolicy Bypass -File"),
        [".go"] = new("go", "run"),

        // Added alongside their parsers. Without an entry here the parser is unreachable: the
        // file cannot be launched, so nothing ever produces output for it to read.
        [".dart"] = new("dart", "run"),
        [".exs"] = new("elixir"),
    };

    /// <summary>Project files that tell us how to rebuild after a patch.</summary>
    private static readonly string[] BuildMarkers = ["*.sln", "*.csproj", "*.fsproj", "*.vbproj"];

    /// <summary>Everything the file picker should offer, built from what can actually be run.</summary>
    public static string FileDialogFilter
    {
        get
        {
            var all = ByExtension.Keys
                .Concat([".c", ".cpp", ".cc", ".cxx", ".java"])
                .OrderBy(e => e, StringComparer.Ordinal);

            var extensions = string.Join(";", all.Select(e => "*" + e));
            return $"Programs and scripts ({extensions})|{extensions}|All files (*.*)|*.*";
        }
    }

    /// <summary>
    /// Works out how to launch a file, filling in everything the person did not have to say.
    /// </summary>
    public static LaunchPlan FromFile(string path, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return LaunchPlan.Failed("No file was chosen.");

        string full;
        try { full = Path.GetFullPath(path.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return LaunchPlan.Failed($"That is not a usable file path:\n\n{path}");
        }

        if (!File.Exists(full)) return LaunchPlan.Failed($"There is no file at:\n\n{full}");

        var workingDirectory = Path.GetDirectoryName(full)!;
        var extension = Path.GetExtension(full);

        // C, C++ and Java have to be built first, and the build is a target in its own right.
        if (CompiledLanguages.Handles(extension))
        {
            var (built, problem) = CompiledLanguages.Prepare(full, timeout ?? DefaultTimeout);

            if (built is null) return LaunchPlan.Failed(problem!);

            return new LaunchPlan(built.Runnable, null, built.Explanation)
            {
                ChosenFile = full,
                Compile = built.Compile,
                SourceFolder = workingDirectory,
            };
        }

        if (!ByExtension.TryGetValue(extension, out var runner))
        {
            // Unknown extensions are attempted directly rather than refused: plenty of things
            // are executable without a familiar suffix, and the run itself will say if it is not.
            return new LaunchPlan(
                Build(full, "", workingDirectory, launchViaDotnet: false, timeout),
                null,
                $"Running {Path.GetFileName(full)} directly - its extension is not one FixFinder recognises.")
            { ChosenFile = full };
        }

        if (runner.Interpreter is null)
        {
            return new LaunchPlan(
                Build(full, "", workingDirectory, launchViaDotnet: false, timeout),
                null,
                $"Running {Path.GetFileName(full)} directly.")
            { ChosenFile = full };
        }

        var found = Resolve(runner);

        if (found is null)
        {
            var names = string.Join(" or ", new[] { runner.Interpreter }.Concat(runner.Alternatives).Distinct());

            return LaunchPlan.Failed(
                $"{Path.GetFileName(full)} needs {names} to run it, and that is not installed - " +
                $"or at least not on this account's PATH.\n\n" +
                $"Install it, or pick a program that runs on its own such as an .exe.");
        }

        // The dotnet host is a special case, and getting it wrong names the file twice.
        // TargetSpec already quotes the assembly path itself when LaunchViaDotnet is set, so
        // that route passes the .dll as the executable and leaves the arguments empty.
        //
        // Only when there is no prefix, though. "dotnet run file.cs" is an ordinary command with
        // a verb in it, and routing that through the host shortcut silently drops the "run" -
        // leaving dotnet to be handed a .cs file as though it were an assembly.
        var viaDotnet =
            string.Equals(Path.GetFileNameWithoutExtension(found), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            runner.ArgumentPrefix.Length == 0;

        // Only the file is quoted. The prefix is a literal fragment of the command line, and
        // quoting it would turn "-jar" into an argument nobody asked for.
        var arguments = runner.ArgumentPrefix.Length > 0
            ? $"{runner.ArgumentPrefix} \"{full}\""
            : $"\"{full}\"";

        var spec = new TargetSpec
        {
            ExecutablePath = viaDotnet ? full : found,
            Arguments = viaDotnet ? "" : arguments,
            WorkingDirectory = workingDirectory,
            LaunchViaDotnet = viaDotnet,
            Timeout = timeout ?? DefaultTimeout,
            BuildCommand = GuessBuildCommand(full, workingDirectory),
            BuildWorkingDirectory = workingDirectory,
        };

        var how = $"Running it with {Path.GetFileNameWithoutExtension(found)}.";
        if (spec.BuildCommand is { Length: > 0 } build) how += $" Rebuilding with: {build}";

        return new LaunchPlan(spec, null, how) { ChosenFile = full, SourceFolder = workingDirectory };
    }

    /// <summary>
    /// Looks for a project to rebuild, so a patched compiled program is not re-run stale.
    /// </summary>
    /// <remarks>
    /// Worth guessing rather than asking. Without a build command the verifier has to report
    /// Inconclusive for every compiled language, because re-running would run the binary from
    /// before the patch - and a person who just wanted to pick a file will not know that.
    /// </remarks>
    private static string? GuessBuildCommand(string target, string workingDirectory)
    {
        if (!target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = new DirectoryInfo(workingDirectory);

        // bin\Debug\net8.0\app.dll sits a few levels below the project file.
        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            foreach (var marker in BuildMarkers)
            {
                try
                {
                    var match = directory.GetFiles(marker).FirstOrDefault();
                    if (match is not null) return $"dotnet build \"{match.FullName}\"";
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // An unreadable folder simply yields no guess.
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static TargetSpec Build(
        string path, string arguments, string workingDirectory, bool launchViaDotnet, TimeSpan? timeout) =>
        new()
        {
            ExecutablePath = path,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            LaunchViaDotnet = launchViaDotnet,
            Timeout = timeout ?? DefaultTimeout,
            BuildCommand = GuessBuildCommand(path, workingDirectory),
            BuildWorkingDirectory = workingDirectory,
        };

    /// <summary>Returns the first of an interpreter's names that exists, or null.</summary>
    private static string? Resolve(Runner runner)
    {
        foreach (var name in new[] { runner.Interpreter! }.Concat(runner.Alternatives))
        {
            if (FindOnPath(name) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// Finds an executable on PATH, the way the shell would.
    /// </summary>
    /// <remarks>
    /// Done here rather than left to <c>Process.Start</c> so that a missing interpreter is a
    /// sentence about the interpreter instead of a Win32 error code, and so the window can say
    /// which program it is about to use before anything is launched.
    /// </remarks>
    public static string? FindOnPath(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var directory in directories)
        {
            string folder;
            try { folder = directory.Trim().Trim('"'); }
            catch (ArgumentException) { continue; }

            if (folder.Length == 0) continue;

            foreach (var candidate in Candidates(folder, name, extensions))
            {
                try { if (File.Exists(candidate)) return candidate; }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string folder, string name, string[] extensions)
    {
        if (Path.HasExtension(name))
        {
            yield return Path.Combine(folder, name);
            yield break;
        }

        foreach (var extension in extensions) yield return Path.Combine(folder, name + extension);
    }
}
