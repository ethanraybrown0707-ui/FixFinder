namespace FixFinder.Core.Execution;

/// <summary>A target worked out from a file, or the reason one could not be.</summary>
public sealed record LaunchPlan(TargetSpec? Spec, string? Problem, string Explanation)
{
    public string? ChosenFile { get; init; }

    public TargetSpec? Compile { get; init; }

    public string? SourceFolder { get; init; }

    public bool NeedsCompiling => Compile is not null;

    public bool Ok => Spec is not null;

    public static LaunchPlan Failed(string problem) => new(null, problem, "");
}

/// <summary>Turns a file somebody picked into something that can actually be launched.</summary>
public static class TargetFactory
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>What a language that compiles the world before running is given instead.</summary>
    /// <remarks>
    /// <c>go run</c> compiles the standard library before it compiles the program, which takes minutes on a
    /// machine that has never built Go and seconds on one that has. A timeout tuned to the second case does
    /// not merely make the first slow: the program is killed before it reaches the line that crashes, so
    /// FixFinder reports that it never finished for a program that in fact fails instantly - the wrong answer
    /// rather than a slow one. Waiting longer is only ever paid by a target that has not finished yet, so a
    /// generous allowance costs nothing on the machine where the cache is warm.
    /// </remarks>
    public static readonly TimeSpan FirstRunTimeout = TimeSpan.FromMinutes(6);

    private static readonly HashSet<string> CompileBeforeRunning =
        new(StringComparer.OrdinalIgnoreCase) { ".go" };

    /// <summary>How long a file of this kind is given when the caller has not said.</summary>
    public static TimeSpan TimeoutFor(string extension) =>
        CompileBeforeRunning.Contains(extension) ? FirstRunTimeout : DefaultTimeout;

    private sealed record Runner(
        string? Interpreter, string ArgumentPrefix = "", params string[] Alternatives);

    private static readonly Dictionary<string, Runner> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = new(null),
        [".com"] = new(null),
        [".bat"] = new(null),
        [".cmd"] = new(null),

        [".dll"] = new("dotnet"),

        [".py"] = new("python", "", "py", "python3"),
        [".pyw"] = new("pythonw", "", "python", "py"),

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

        [".dart"] = new("dart", "run"),
        [".exs"] = new("elixir"),
    };

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

        var viaDotnet =
            string.Equals(Path.GetFileNameWithoutExtension(found), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            runner.ArgumentPrefix.Length == 0;

        var arguments = runner.ArgumentPrefix.Length > 0
            ? $"{runner.ArgumentPrefix} \"{full}\""
            : $"\"{full}\"";

        var together = "";

        switch (extension.ToLowerInvariant())
        {
            case ".go" when ProgramLayout.GoPackageOf(full) is { IsSingleFile: false } program:
                arguments = program.Module is not null
                    ? "run ."
                    : "run " + string.Join(" ", program.Files.Select(f => $"\"{f}\""));
                together = program.Module is not null
                    ? " as the module in its folder"
                    : $" together with the rest of its package: {string.Join(", ", program.Files.Skip(1).Select(Path.GetFileName))}";
                break;

            case ".cs" when ProgramLayout.CSharpProject(full) is { } project:
                arguments = $"run --project \"{project}\"";
                workingDirectory = Path.GetDirectoryName(project)!;
                together = $" as part of its project, {Path.GetFileName(project)}";
                break;

            case ".py" when ProgramLayout.PythonModule(full) is { } module:
                arguments = $"-m {module.Module}";
                workingDirectory = module.Folder;
                together = $" as the module {module.Module}, because it imports from its own package";
                break;
        }

        var spec = new TargetSpec
        {
            ExecutablePath = viaDotnet ? full : found,
            Arguments = viaDotnet ? "" : arguments,
            WorkingDirectory = workingDirectory,
            LaunchViaDotnet = viaDotnet,
            Timeout = timeout ?? TimeoutFor(extension),
        };

        var how = $"Running it{together} with {Path.GetFileNameWithoutExtension(found)}.";

        return new LaunchPlan(spec, null, how) { ChosenFile = full, SourceFolder = workingDirectory };
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
        };

    private static string? Resolve(Runner runner)
    {
        foreach (var name in new[] { runner.Interpreter! }.Concat(runner.Alternatives))
        {
            if (FindOnPath(name) is { } found) return found;
        }

        return null;
    }

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
