namespace FixFinder.Core.Execution;

/// <summary>A target worked out from a file, or the reason one could not be.</summary>
public sealed record LaunchPlan(TargetSpec? Spec, string? Problem, string Explanation)
{
    public string? ChosenFile { get; init; }

    public TargetSpec? Compile { get; init; }

    public string? SourceFolder { get; init; }

    /// <summary>
    /// The file as the person picked it, where that is not <see cref="ChosenFile"/>: a Jupyter notebook, whose code is
    /// checked as the script FixFinder writes it out to.
    /// </summary>
    public string? PickedFile { get; init; }

    /// <summary>The file to name to the person: the one they picked, rather than a script made from it.</summary>
    public string? ShownFile => PickedFile ?? ChosenFile;

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
                .Concat([".c", ".cpp", ".cc", ".cxx", ".java", ".ipynb"])
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

        if (extension.Equals(".ipynb", StringComparison.OrdinalIgnoreCase)) return Notebook(full, timeout);

        // A notebook's code - the script FixFinder wrote it to, or a copy of that with a change in it - runs as the notebook does.
        if (NotebookScript.IsCodeOfANotebook(full)) return NotebookCode(full, timeout);

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

        // A Python project with an environment of its own runs in it, with what is installed there, as its IDE runs it - and
        // so does a copy of the project made to try a change in, which leaves the environment behind.
        var environment = extension.ToLowerInvariant() is ".py" or ".pyw"
            ? PythonEnvironment.For(ProgramCopy.OriginalOf(full), windowed: extension.Equals(".pyw", StringComparison.OrdinalIgnoreCase))
            : null;
        var found = environment?.Interpreter ?? Resolve(runner);

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
        var testsRunBy = (string?)null;

        switch (extension.ToLowerInvariant())
        {
            case ".py" when PythonTests.FrameworkOf(full) == PythonTests.Framework.Unittest:
            {
                // Its tests are run with unittest, one by one, from where running the file - or its package - would start.
                var package = ProgramLayout.PythonModule(full);
                var testModule = package?.Module ?? Path.GetFileNameWithoutExtension(full);
                workingDirectory = package?.Folder ?? workingDirectory;
                arguments = $"-X utf8 \"{PythonTests.WriteLauncher()}\" {testModule} \"{workingDirectory}\" \"{full}\"";
                testsRunBy = "unittest";
                break;
            }

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

        var interpreterNamed = environment?.Described ?? Path.GetFileNameWithoutExtension(found);
        var how = testsRunBy is not null
            ? $"Running its tests with {testsRunBy}, test by test, using {interpreterNamed}."
            : $"Running it{together} with {interpreterNamed}.";

        return new LaunchPlan(spec, null, how) { ChosenFile = full, SourceFolder = workingDirectory };
    }

    /// <summary>The warning filter, in Python's own form, for the warning matplotlib gives when a plot is made with no window.</summary>
    private const string PlotNotShown = "ignore:FigureCanvasAgg is non-interactive:UserWarning";

    /// <summary>A Jupyter notebook, checked as the script its code cells are written out to.</summary>
    private static LaunchPlan Notebook(string notebook, TimeSpan? timeout)
    {
        var (written, problem) = NotebookScript.Write(notebook);
        if (written is null) return LaunchPlan.Failed(problem!);

        var plan = NotebookCode(written.Script, timeout);
        return plan.Ok ? plan with { PickedFile = notebook } : plan;
    }

    /// <summary>
    /// A notebook's code cells, run in order as a script, as Run All runs them: from the notebook's own folder - where
    /// Jupyter starts its kernel - with that folder among the places Python looks for modules, so the notebook's own
    /// modules and data files are found, and with the project's own Python when it has one. A copy of the script, put
    /// beside a copy of the notebook's files to try a change in, runs from that copy in the same way.
    /// </summary>
    private static LaunchPlan NotebookCode(string script, TimeSpan? timeout)
    {
        var notebookName = Path.GetFileNameWithoutExtension(script);
        var folder = NotebookScript.FolderOfCode(script);

        // The notebook itself: the one the script was written from, or - for a copy made to try a change in - the one it
        // was copied from. Its own Jupyter kernel decides its Python, when it was saved with one; its project's otherwise.
        var original = ProgramCopy.OriginalOf(script);
        var notebook = NotebookScript.Of(script)?.Notebook ?? (original.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ? original[..^3] : original);
        var environment = JupyterKernels.Named(NotebookScript.KernelNameIn(notebook), PythonEnvironment.Current) ?? PythonEnvironment.For(notebook);
        var python = environment?.Interpreter ?? Resolve(ByExtension[".py"]);

        if (python is null)
        {
            return LaunchPlan.Failed(
                $"{notebookName} is a notebook of Python, which needs python to run it, and that is not installed - " +
                "or at least not on this account's PATH.");
        }

        var modulePath = Environment.GetEnvironmentVariable("PYTHONPATH") is { Length: > 0 } modulesAlready
            ? folder + Path.PathSeparator + modulesAlready
            : folder;

        // Plots are made as Jupyter makes them, with no window, so show() carries on to the next cell rather than waiting
        // for a window to be closed. matplotlib warns that such a plot cannot be shown; that is FixFinder's doing, not the
        // notebook's, so that one warning is left out.
        var warnings = Environment.GetEnvironmentVariable("PYTHONWARNINGS") is { Length: > 0 } warningsAlready
            ? warningsAlready + "," + PlotNotShown
            : PlotNotShown;

        // Code that awaits outside a function, as a cell can, is run as Jupyter runs it; any other runs as it is.
        var awaitsOutsideAFunction = NotebookScript.AwaitsOutsideAFunction(File.ReadAllLines(script));

        var spec = new TargetSpec
        {
            ExecutablePath = python,
            Arguments = awaitsOutsideAFunction ? $"\"{NotebookScript.WriteRunner()}\" \"{script}\"" : $"\"{script}\"",
            WorkingDirectory = folder,
            ExtraEnvironment = new Dictionary<string, string>
            {
                ["PYTHONPATH"] = modulePath,
                ["MPLBACKEND"] = "Agg",
                ["PYTHONWARNINGS"] = warnings,
            },
            Timeout = timeout ?? DefaultTimeout,
        };

        var how = $"Running the code cells of {notebookName} in order, as Jupyter's Run All does, with " +
                  $"{environment?.Described ?? Path.GetFileNameWithoutExtension(python)}. Plots are made without opening a window.";

        return new LaunchPlan(spec, null, how) { ChosenFile = script, SourceFolder = folder };
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
