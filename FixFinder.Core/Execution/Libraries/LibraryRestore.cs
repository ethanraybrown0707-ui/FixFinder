using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>Whether FixFinder restores the libraries a project names that are not on this computer, before checking it.</summary>
public enum RestoreChoice
{
    /// <summary>Never: the libraries not here are named, and nothing is downloaded - what FixFinder does unless told otherwise.</summary>
    Never,

    /// <summary>Each time, after saying what would be downloaded, from where, and where it would go.</summary>
    Ask,

    /// <summary>Without asking.</summary>
    Automatically,
}

/// <summary>One command a restore runs, in the folder of the file that names the libraries.</summary>
/// <param name="Shown">The command as a person would type it: "pip install -r requirements.txt".</param>
public sealed record RestoreStep(string Program, IReadOnlyList<string> Arguments, string Shown, IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>What restoring a project's libraries would do.</summary>
/// <param name="Program">The program being checked, which the plan was made for.</param>
/// <param name="Manifest">The file that names them: requirements.txt, package.json, go.mod or pubspec.yaml.</param>
/// <param name="Missing">The libraries it names that are not here.</param>
/// <param name="Tool">The tool that restores them: pip, npm, go or dart pub.</param>
/// <param name="From">Where the tool downloads them from: "PyPI (pypi.org)".</param>
/// <param name="Into">Where they go: "the project's own environment, .venv".</param>
public sealed record RestorePlan(string Program, string Manifest, IReadOnlyList<string> Missing, string Tool, string From, string Into, IReadOnlyList<RestoreStep> Steps)
{
    public string ManifestName => Path.GetFileName(Manifest);

    public string Folder => Path.GetDirectoryName(Manifest)!;

    private string Named => $"{ManifestName} names {(Missing.Count == 1 ? "a library" : $"{Missing.Count} libraries")} that " +
                            $"{(Missing.Count == 1 ? "is" : "are")} not on this computer: {string.Join(", ", Missing)}";

    /// <summary>What is asked before a restore: what is missing, what would download it, from where, into where - and the risk.</summary>
    public string Question =>
        $"{Named}.\n\nFixFinder can restore {(Missing.Count == 1 ? "it" : "them")}: {Tool} downloads {(Missing.Count == 1 ? "it" : "them")} from " +
        $"{From} and installs {(Missing.Count == 1 ? "it" : "them")} into {Into}, by running:\n\n" +
        string.Join("\n", Steps.Select(step => step.Shown)) + $"\n\n{LibraryRestore.ScriptsWarning}\n\nRestore {(Missing.Count == 1 ? "it" : "them")} now?";

    /// <summary>The note when nothing was restored, because restoring is off.</summary>
    public string OffNote =>
        $"{Named}. FixFinder downloads nothing unless you let it: restoring libraries is off - Settings, Advanced, Libraries - so the " +
        $"program was checked without {(Missing.Count == 1 ? "it" : "them")}.";

    /// <summary>The note when the person was asked and said no.</summary>
    public string DeclinedNote => $"{Named}. {(Missing.Count == 1 ? "It was" : "They were")} not restored, as you said, so the program was checked without {(Missing.Count == 1 ? "it" : "them")}.";

    /// <summary>The note when there was to be a question, and nobody to answer it - a command run by an editor.</summary>
    public string UnansweredNote =>
        $"{Named}. Nobody was at the terminal to be asked about restoring {(Missing.Count == 1 ? "it" : "them")}, so the program was checked without {(Missing.Count == 1 ? "it" : "them")}.";

    /// <summary>The libraries named in a sentence: "tabulate", "tabulate and requests", "a, b and c".</summary>
    public string MissingSaid => Missing.Count == 1 ? Missing[0] : $"{string.Join(", ", Missing.Take(Missing.Count - 1))} and {Missing[^1]}";

    /// <summary>The note for another program of the same project, when this one's libraries were already asked about or restored.</summary>
    public string StillMissingNote => $"{Named}, so the program was checked without {(Missing.Count == 1 ? "it" : "them")}.";
}

/// <summary>What a restore did, as a note in the report says it.</summary>
public sealed record RestoreOutcome(bool Restored, string Note);

/// <summary>
/// Restores the libraries a project names - its requirements.txt, package.json, go.mod or pubspec.yaml - when they are not
/// on this computer, with the project's own tool, only when the person has said FixFinder may: it is off unless chosen.
/// </summary>
/// <remarks>
/// <para>
/// Each restore puts the libraries where the run FixFinder makes of the program finds them, and was run here to see that it
/// does: pip installs into the project's own environment, and makes one - .venv, beside requirements.txt - when the project
/// has none, which can also use what the Python on this computer has installed; npm installs into node_modules beside
/// package.json - npm ci when there is a package-lock.json, which keeps to it, and otherwise npm install, told not to
/// write one; go mod download all fills Go's module cache, which FixFinder's builds read, and writes the checksums into
/// go.sum; dart pub get fills pub's cache and writes .dart_tool\package_config.json.
/// </para>
/// <para>
/// Ruby's bundle install would put gems into the Ruby installed on this computer, for every program, not into the
/// project; FixFinder builds Rust with rustc and C# with csc, so crates and NuGet packages a restore downloaded would not
/// be used; and Maven, Gradle and Composer projects are not restored, as none of those tools was here to try it with.
/// </para>
/// </remarks>
public static partial class LibraryRestore
{
    public const string ScriptsWarning =
        "A package can run its own install or build scripts on this computer while it is installed, so restore only a project you trust.";

    /// <summary>How long a restore is given before it is stopped: a download of many packages can take minutes.</summary>
    public static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(10);

    /// <summary>
    /// What restoring the libraries the program's project names would do - or null when its project names none, all are
    /// here, or the tool that restores them is not on this computer.
    /// </summary>
    public static async Task<RestorePlan?> PlanForAsync(string programFile, CancellationToken cancellation = default)
    {
        var extension = Path.GetExtension(programFile).ToLowerInvariant();

        try
        {
            return extension switch
            {
                ".py" or ".pyw" or ".ipynb" => await PythonPlanAsync(programFile, cancellation),
                ".js" or ".mjs" or ".cjs" or ".ts" or ".mts" or ".cts" => NodePlan(programFile),
                ".go" => await GoPlanAsync(programFile, cancellation),
                ".dart" => DartPlan(programFile),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a check does about the libraries before it starts, as the person chose: names them when restoring is off,
    /// asks first when it is to ask, restores them otherwise. The note to put in the report, or null when nothing is missing.
    /// </summary>
    /// <param name="restoring">Told just before a restore starts, so what is waited for can be said.</param>
    public static async Task<string?> PrepareAsync(
        string programFile, RestoreChoice choice, Func<RestorePlan, Task<bool>> ask, Action<RestorePlan>? restoring = null,
        CancellationToken cancellation = default) =>
        await PlanForAsync(programFile, cancellation) is { } plan ? await DecideAsync(plan, choice, ask, restoring, cancellation) : null;

    /// <summary>What is done with a plan already made, as the person chose - and the note that says what that was.</summary>
    public static async Task<string> DecideAsync(
        RestorePlan plan, RestoreChoice choice, Func<RestorePlan, Task<bool>> ask, Action<RestorePlan>? restoring = null,
        CancellationToken cancellation = default)
    {
        if (choice is not (RestoreChoice.Ask or RestoreChoice.Automatically)) return plan.OffNote;
        if (choice == RestoreChoice.Ask && !await ask(plan)) return plan.DeclinedNote;

        restoring?.Invoke(plan);
        return (await RunAsync(plan, cancellation)).Note;
    }

    /// <summary>Runs the plan's commands in turn, then looks again to see that what was missing is here.</summary>
    public static async Task<RestoreOutcome> RunAsync(RestorePlan plan, CancellationToken cancellation = default)
    {
        var one = plan.Missing.Count == 1;
        var withoutThem = $"so the program was checked without {(one ? "it" : "them")}";
        var lastSaid = "";

        foreach (var step in plan.Steps)
        {
            var (exitCode, output, errors) = await RunStepAsync(step, plan.Folder, cancellation);

            if (exitCode != 0)
            {
                // What went wrong is said on standard error by pip, npm, go and dart - and on standard output by some.
                var why = exitCode is null
                    ? $"{step.Shown} was still running after {TimeLimit.TotalMinutes:0} minutes, so it was stopped"
                    : $"{step.Shown} stopped, saying \"{Said(Meaningful(errors) ? errors : output)}\"";
                return new RestoreOutcome(false, $"Restoring {plan.MissingSaid}, which {plan.ManifestName} names, did not work: {why} - {withoutThem}.");
            }

            if (Meaningful(output)) lastSaid = Said(output);
        }

        var stillMissing = (await PlanForAsync(plan.Program, cancellation))?.Missing ?? [];
        var restored = plan.Missing.Where(name => !stillMissing.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();

        if (restored.Count == 0)
            return new RestoreOutcome(false, $"{plan.Tool} finished restoring what {plan.ManifestName} names, but {plan.MissingSaid} {(one ? "is" : "are")} still " +
                                             $"not here - {withoutThem}.");

        var note = new StringBuilder($"Restored {plan.MissingSaid}, which {plan.ManifestName} names and {(one ? "was" : "were")} not here, as you said " +
                                     $"FixFinder may: {plan.Tool} installed {(one ? "it" : "them")} into {plan.Into}");
        note.Append(lastSaid.Length > 0 ? $", and said \"{lastSaid}\"." : ".");
        if (stillMissing.Count > 0) note.Append($" Still not here after it: {string.Join(", ", stillMissing)}.");

        return new RestoreOutcome(true, note.ToString());
    }

    private static bool Meaningful(IReadOnlyList<string> lines) => lines.Any(line => line.Trim().Length > 0);

    /// <summary>The last line a tool printed that says something, as a note can quote it.</summary>
    private static string Said(IReadOnlyList<string> lines)
    {
        var meaningful = lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        return meaningful.Count == 0 ? "nothing" : meaningful[^1];
    }

    private static async Task<(int? ExitCode, IReadOnlyList<string> Output, IReadOnlyList<string> Errors)> RunStepAsync(
        RestoreStep step, string folder, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(step.Program)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var argument in step.Arguments) start.ArgumentList.Add(argument);
        foreach (var (name, value) in step.Environment ?? new Dictionary<string, string>()) start.Environment[name] = value;

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return (-1, [], [ex.Message]);
        }

        if (process is null) return (-1, [], ["it did not start"]);

        using (process)
        {
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(TimeLimit);

            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                cancellation.ThrowIfCancellationRequested();
                return (null, [], []);
            }

            // What it printed is all written once it has exited; reading the last of it only needs a thread.
            var printed = await Task.WhenAll(output, errors).WaitAsync(ToolOutput.AfterExit, CancellationToken.None);
            return (process.ExitCode, Lines(printed[0]), Lines(printed[1]));
        }
    }

    private static IReadOnlyList<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    /// <summary>
    /// The nearest folder at or above the program's that holds <paramref name="manifest"/>, looking no further than the
    /// folder that marks the project - one with a .git - or the home folder.
    /// </summary>
    private static string? Nearest(string programFile, string manifest)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);

        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(programFile))!); folder is not null; folder = folder.Parent)
        {
            if (string.Equals(folder.FullName.TrimEnd(Path.DirectorySeparatorChar), home, StringComparison.OrdinalIgnoreCase)) return null;

            var found = Path.Combine(folder.FullName, manifest);
            if (File.Exists(found)) return found;
            if (Path.Exists(Path.Combine(folder.FullName, ".git"))) return null;
        }

        return null;
    }

    // Python: requirements.txt, restored with pip into the project's own environment.

    /// <summary>A requirement's name, at the start of its line: "tabulate" of "tabulate==0.9.0", "requests" of "requests[socks]>=2".</summary>
    [GeneratedRegex(@"^(?<name>[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)")]
    private static partial Regex RequirementName();

    /// <summary>
    /// The names requirements.txt gives. Options (-r, -e, --index-url), paths and links are left to pip, and so is a line
    /// with a marker - "; python_version &lt; '3.8'" - which may not be meant for this Python at all.
    /// </summary>
    internal static IReadOnlyList<string> RequirementsIn(IEnumerable<string> lines) =>
    [
        .. lines
            .Select(line => Regex.Replace(line, @"(^|\s)#.*$", "").Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('-') && !line.StartsWith('.') && !line.Contains("://") && !line.Contains(';') &&
                           !line.Contains('/') && !line.Contains('\\'))
            .Select(line => RequirementName().Match(line))
            .Where(name => name.Success)
            .Select(name => name.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>What Python is asked, to say which of the names given it has not got installed - by their distributions' names, as pip installs them.</summary>
    private const string NotInstalledScript =
        "import importlib.metadata as m, sys\n" +
        "for name in sys.argv[1:]:\n" +
        "    try:\n" +
        "        m.distribution(name)\n" +
        "    except m.PackageNotFoundError:\n" +
        "        print(name)\n";

    private static async Task<RestorePlan?> PythonPlanAsync(string programFile, CancellationToken cancellation)
    {
        if (Nearest(programFile, "requirements.txt") is not { } requirements) return null;

        var names = RequirementsIn(await File.ReadAllLinesAsync(requirements, cancellation));
        if (names.Count == 0) return null;

        var environment = PythonEnvironment.For(programFile);
        if (PythonSetup.For(programFile, environment)?.Interpreter is not { } interpreter) return null;

        var check = new RestoreStep(interpreter, ["-c", NotInstalledScript, .. names], "", new Dictionary<string, string> { ["PYTHONIOENCODING"] = "utf-8" });
        var (exitCode, output, _) = await RunStepAsync(check, Path.GetDirectoryName(requirements)!, cancellation);
        if (exitCode != 0) return null;

        var missing = output.Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (missing.Count == 0) return null;

        var install = new List<string> { "-m", "pip", "install", "--disable-pip-version-check", "--no-input", "-r", "requirements.txt" };
        var pipEnvironment = new Dictionary<string, string> { ["PYTHONIOENCODING"] = "utf-8" };

        if (environment is not null)
            return new RestorePlan(programFile, requirements, missing, "pip", "PyPI (pypi.org)", environment.Described,
                [new RestoreStep(environment.Interpreter, install, "python -m pip install -r requirements.txt", pipEnvironment)]);

        // No environment of its own: one is made beside requirements.txt, where FixFinder - and VS Code and PyCharm - look
        // for one, and it can still use what the Python on this computer has installed, so nothing the program used before goes.
        var made = Path.Combine(Path.GetDirectoryName(requirements)!, ".venv");
        return new RestorePlan(programFile, requirements, missing, "pip", "PyPI (pypi.org)",
            "a new environment for the project, .venv beside requirements.txt, which can also use what the Python on this computer has installed",
            [
                new RestoreStep(interpreter, ["-m", "venv", "--system-site-packages", ".venv"], "python -m venv --system-site-packages .venv", pipEnvironment),
                new RestoreStep(Path.Combine(made, "Scripts", "python.exe"), install, @".venv\Scripts\python -m pip install -r requirements.txt", pipEnvironment),
            ]);
    }

    // JavaScript and TypeScript: package.json, restored with npm into node_modules.

    /// <summary>The packages package.json names, under dependencies and devDependencies.</summary>
    internal static IReadOnlyList<string> PackagesIn(string packageJson)
    {
        using var document = JsonDocument.Parse(packageJson, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

        return
        [
            .. new[] { "dependencies", "devDependencies" }
                .Where(section => document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(section, out var listed) && listed.ValueKind == JsonValueKind.Object)
                .SelectMany(section => document.RootElement.GetProperty(section).EnumerateObject().Select(package => package.Name))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// A node, and npm's own script beside it - npm-cli.js, which npm.cmd runs - so npm is run by node and no command prompt
    /// is started: the usual Node's, else the one beside the npm on PATH.
    /// </summary>
    private static (string Node, string Script)? Npm()
    {
        foreach (var node in new[] { Nodes.Usual?.Program, TargetFactory.FindOnPath("npm") is { } npm ? Path.Combine(Path.GetDirectoryName(npm)!, "node.exe") : null })
        {
            if (node is null) continue;
            var script = Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js");
            if (File.Exists(node) && File.Exists(script)) return (node, script);
        }

        return null;
    }

    private static RestorePlan? NodePlan(string programFile)
    {
        if (Nearest(programFile, "package.json") is not { } packageJson) return null;

        var folder = Path.GetDirectoryName(packageJson)!;
        var missing = PackagesIn(File.ReadAllText(packageJson))
            .Where(package => !File.Exists(Path.Combine(folder, "node_modules", package.Replace('/', Path.DirectorySeparatorChar), "package.json")))
            .ToList();
        if (missing.Count == 0 || Npm() is not var (node, script)) return null;

        var locked = File.Exists(Path.Combine(folder, "package-lock.json"));
        string[] asked = locked ? ["ci", "--no-audit", "--no-fund"] : ["install", "--no-audit", "--no-fund", "--no-package-lock"];

        return new RestorePlan(programFile, packageJson, missing, "npm", "the npm registry (registry.npmjs.org)", "node_modules beside package.json",
            [new RestoreStep(node, [script, .. asked], "npm " + string.Join(" ", asked), new Dictionary<string, string> { ["npm_config_update_notifier"] = "false" })]);
    }

    // Go: go.mod, restored with go mod download into Go's module cache.

    [GeneratedRegex(@"^\s*(?:require\s+)?(?<path>[^\s()]+)\s+(?<version>v[^\s]+)")]
    private static partial Regex Required();

    [GeneratedRegex(@"^\s*(?:replace\s+)?(?<path>[^\s()]+)(?:\s+v[^\s]+)?\s+=>")]
    private static partial Regex Replaced();

    /// <summary>The modules go.mod requires, with their versions, less those it replaces - with a folder of code, or another module.</summary>
    internal static IReadOnlyList<(string Path, string Version)> RequiredIn(IEnumerable<string> goMod)
    {
        var required = new List<(string Path, string Version)>();
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        string? block = null;

        foreach (var written in goMod)
        {
            var line = Regex.Replace(written, @"//.*$", "").Trim();
            if (line.Length == 0) continue;

            if (line.EndsWith('(')) { block = line.Split(' ')[0]; continue; }
            if (line == ")") { block = null; continue; }

            var keyword = block ?? line.Split(' ')[0];
            if (keyword == "require" && Required().Match(line) is { Success: true } module) required.Add((module.Groups["path"].Value, module.Groups["version"].Value));
            else if (keyword == "replace" && Replaced().Match(line) is { Success: true } swapped) replaced.Add(swapped.Groups["path"].Value);
        }

        return [.. required.Where(module => !replaced.Contains(module.Path))];
    }

    /// <summary>A module path or version as Go's module cache writes it: each capital letter as ! and the small letter.</summary>
    internal static string Escaped(string written) => Regex.Replace(written, "[A-Z]", capital => "!" + capital.Value.ToLowerInvariant());

    private static async Task<RestorePlan?> GoPlanAsync(string programFile, CancellationToken cancellation)
    {
        if (Nearest(programFile, "go.mod") is not { } goMod) return null;

        var required = RequiredIn(await File.ReadAllLinesAsync(goMod, cancellation));
        if (required.Count == 0) return null;

        if ((GoSetup.For(programFile)?.Go ?? TargetFactory.FindOnPath("go")) is not { } go) return null;

        var folder = Path.GetDirectoryName(goMod)!;
        var (exitCode, said, _) = await RunStepAsync(new RestoreStep(go, ["env", "GOMODCACHE"], "go env GOMODCACHE", GoSetup.Environment), folder, cancellation);
        if (exitCode != 0 || said.FirstOrDefault(line => line.Trim().Length > 0)?.Trim() is not { } cache) return null;

        var sums = File.Exists(Path.Combine(folder, "go.sum")) ? await File.ReadAllLinesAsync(Path.Combine(folder, "go.sum"), cancellation) : [];
        var missing = required
            .Where(module => !Directory.Exists(Path.Combine(cache, Escaped(module.Path).Replace('/', Path.DirectorySeparatorChar) + "@" + Escaped(module.Version))) ||
                             !sums.Any(line => line.StartsWith($"{module.Path} {module.Version} h1:", StringComparison.Ordinal)))
            .Select(module => $"{module.Path} {module.Version}")
            .ToList();
        if (missing.Count == 0) return null;

        // Its own Go, as every go command FixFinder starts - but with Go's proxy and checksum database, which it downloads from.
        return new RestorePlan(programFile, goMod, missing, "go", "Go's module proxy (proxy.golang.org), checked against its checksum database (sum.golang.org)",
            "Go's module cache, writing the checksums into go.sum",
            [new RestoreStep(go, ["mod", "download", "all"], "go mod download all", new Dictionary<string, string> { ["GOTOOLCHAIN"] = "local" })]);
    }

    // Dart: pubspec.yaml, restored with dart pub get.

    /// <summary>The packages pubspec.yaml names under dependencies and dev_dependencies - each a key two spaces in.</summary>
    internal static IReadOnlyList<string> DartPackagesIn(IEnumerable<string> pubspec)
    {
        var named = new List<string>();
        var inSection = false;

        foreach (var line in pubspec)
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;

            if (!char.IsWhiteSpace(line[0])) { inSection = line.TrimEnd() is "dependencies:" or "dev_dependencies:"; continue; }

            if (inSection && Regex.Match(line, @"^  (?<name>[a-z_][a-z0-9_]*)\s*:") is { Success: true } package) named.Add(package.Groups["name"].Value);
        }

        return named;
    }

    private static RestorePlan? DartPlan(string programFile)
    {
        if (Nearest(programFile, "pubspec.yaml") is not { } pubspec) return null;

        var lines = File.ReadAllLines(pubspec);

        // A Flutter project's packages need the Flutter SDK, which dart alone cannot restore.
        if (lines.Any(line => Regex.IsMatch(line, @"^\s*sdk:\s*flutter\s*$"))) return null;

        var named = DartPackagesIn(lines);
        if (named.Count == 0 || TargetFactory.FindOnPath("dart") is not { } dart) return null;

        var config = Path.Combine(Path.GetDirectoryName(pubspec)!, ".dart_tool", "package_config.json");
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(config))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            if (document.RootElement.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
                foreach (var package in packages.EnumerateArray())
                    if (package.TryGetProperty("name", out var name) && name.GetString() is { } packageName) known.Add(packageName);
        }

        var missing = named.Where(package => !known.Contains(package)).ToList();
        if (missing.Count == 0) return null;

        return new RestorePlan(programFile, pubspec, missing, "dart pub", "pub.dev", "pub's cache, writing .dart_tool\\package_config.json and pubspec.lock",
            [new RestoreStep(dart, ["pub", "get"], "dart pub get")]);
    }
}
