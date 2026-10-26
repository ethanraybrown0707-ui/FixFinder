using System.Text.Json;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// TypeScript's own compiler, tsc, when this computer has it already - the one a project installed in a node_modules above the
/// program, or else one npm installed for every project - run to check a program's types, which Node.js takes out without
/// checking. Nothing is downloaded: with no tsc here, the types are not checked, and the report says so.
/// </summary>
/// <remarks>
/// <para>
/// tsc is run as node &lt;the typescript package&gt;/bin/tsc, the command npm itself makes tsc run. In TypeScript 7.0.2, the
/// version this was written against, it is a launcher that starts the compiler written in Go, tsc.exe, which Microsoft signs.
/// </para>
/// <para>
/// A program with a tsconfig.json - in its own folder or one above it, up to the folder of its package.json - is checked as
/// that project says, with tsc -p. A file on its own is checked as Node.js runs it, as TypeScript 7.0.2 was seen to need:
/// strict, as tsc --init sets it; an import of ./helpers.ts with its extension allowed, as Node needs the extension;
/// modules read as ESnext ones, as --module nodenext called a top-level await an error in a file Node ran, printing 5; and
/// every file a module, as Node runs each - as a script, a file with no import had its const top called a second top, the
/// browser's own window.top. And TypeScript 7 takes in no type definitions it is not named - Node's own, @types/node, are
/// named when they are here.
/// </para>
/// </remarks>
public static partial class TypeScriptCompiler
{
    /// <param name="Package">The typescript package's folder.</param>
    /// <param name="Version">Its version, as its own package.json gives it: 7.0.2.</param>
    /// <param name="ForTheProject">Whether the project installed it, in a node_modules above the program, rather than npm for every project.</param>
    public sealed record Compiler(string Package, string Version, bool ForTheProject)
    {
        /// <summary>What Node.js is given to run tsc: the package's own launcher.</summary>
        public string Launcher => Path.Combine(Package, "bin", "tsc");

        /// <summary>Whether this TypeScript can check a file as Node.js runs it - an import's .ts allowed - which 5.0 brought in.</summary>
        public bool ChecksAsNodeRuns => int.TryParse(Version.Split('.')[0], out var major) && major >= 5;

        public string Description => $"TypeScript {Version}, {(ForTheProject ? "the project's own" : "installed with npm")}";
    }

    /// <summary>How tsc is run on a program, and whether Node's own type definitions were found to name to it.</summary>
    /// <param name="Project">The tsconfig.json the program is checked as, or null for a file checked on its own.</param>
    public sealed record TypeCheck(TargetSpec Spec, string? Project, bool NodeTypesFound);

    /// <summary>
    /// The tsc for a program: the project's, in a node_modules in the program's folder or one above it - else the one npm
    /// installed for every project, beside the tsc command on PATH or in npm's own folder. Null when there is none.
    /// </summary>
    public static Compiler? For(string file)
    {
        foreach (var folder in FoldersUp(Path.GetDirectoryName(Path.GetFullPath(file))!))
        {
            if (Read(Path.Combine(folder, "node_modules", "typescript"), forTheProject: true) is { } project) return project;
        }

        return InstalledWithNpm().Select(package => Read(package, forTheProject: false)).FirstOrDefault(found => found is not null);
    }

    /// <summary>Where npm puts the typescript package it installs for every project: beside the tsc on PATH, and in npm's own folder.</summary>
    private static IEnumerable<string> InstalledWithNpm()
    {
        if (TargetFactory.FindOnPath("tsc") is { } command)
            yield return Path.Combine(Path.GetDirectoryName(command)!, "node_modules", "typescript");

        if (Environment.GetEnvironmentVariable("APPDATA") is { Length: > 0 } roaming)
            yield return Path.Combine(roaming, "npm", "node_modules", "typescript");
    }

    private static Compiler? Read(string package, bool forTheProject)
    {
        try
        {
            var manifest = Path.Combine(package, "package.json");
            if (!File.Exists(manifest) || !File.Exists(Path.Combine(package, "bin", "tsc"))) return null;

            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            return json.RootElement.TryGetProperty("name", out var name) && name.GetString() == "typescript" &&
                   json.RootElement.TryGetProperty("version", out var version) && version.GetString() is { Length: > 0 } written
                ? new Compiler(package, written, forTheProject)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>A folder and each one above it, up to and including the first that holds a package.json - the project's own.</summary>
    private static IEnumerable<string> FoldersUp(string start)
    {
        for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
        {
            yield return folder.FullName;
            if (File.Exists(Path.Combine(folder.FullName, "package.json"))) yield break;
        }
    }

    /// <summary>The tsconfig.json a program is checked as: in its own folder, or one above it up to its project's.</summary>
    public static string? ProjectOf(string file) =>
        FoldersUp(Path.GetDirectoryName(Path.GetFullPath(file))!).Select(folder => Path.Combine(folder, "tsconfig.json")).FirstOrDefault(File.Exists);

    /// <summary>
    /// How tsc checks a program, run by <paramref name="node"/>: as its project's tsconfig.json says, or the file on its own as
    /// Node.js runs it - each time with --noEmit, so nothing is written, and --pretty false, so each message is one line.
    /// </summary>
    public static TypeCheck CheckOf(Compiler compiler, string node, string file, TimeSpan timeout)
    {
        var full = Path.GetFullPath(file);
        var common = $"\"{compiler.Launcher}\" --noEmit --pretty false";

        if (ProjectOf(full) is { } project)
        {
            return new TypeCheck(new TargetSpec
            {
                ExecutablePath = node, Arguments = $"{common} -p \"{project}\"", WorkingDirectory = Path.GetDirectoryName(project)!, Timeout = timeout,
            }, project, NodeTypesFound: true);
        }

        var folder = Path.GetDirectoryName(full)!;
        var nodeTypes = NodeTypesFor(full, compiler);
        var types = nodeTypes switch
        {
            null => "",
            { RootToName: null } => " --types node",
            { RootToName: { } root } => $" --typeRoots \"{root}\" --types node",
        };

        return new TypeCheck(new TargetSpec
        {
            ExecutablePath = node,
            Arguments = $"{common} --strict --target esnext --module esnext --moduleResolution bundler --moduleDetection force " +
                        $"--allowImportingTsExtensions --skipLibCheck{types} " +
                        $"\"{Path.GetFileName(full)}\"",
            WorkingDirectory = folder,
            Timeout = timeout,
        }, null, nodeTypes is not null);
    }

    /// <summary>Node's own type definitions, found: with the folder tsc has to be told of, when it would not find them itself.</summary>
    private sealed record NodeTypes(string? RootToName);

    /// <summary>
    /// Where Node's own type definitions, @types/node, are: in a node_modules above the program, which tsc finds itself - or
    /// beside the tsc npm installed for every project, whose folder tsc is told of. Null when they are in neither.
    /// </summary>
    private static NodeTypes? NodeTypesFor(string file, Compiler compiler)
    {
        foreach (var folder in FoldersUp(Path.GetDirectoryName(file)!))
        {
            if (File.Exists(Path.Combine(folder, "node_modules", "@types", "node", "package.json"))) return new NodeTypes(null);
        }

        var besideTheCompiler = Path.Combine(Path.GetDirectoryName(compiler.Package)!, "@types");
        return File.Exists(Path.Combine(besideTheCompiler, "node", "package.json")) ? new NodeTypes(besideTheCompiler) : null;
    }

    /// <summary>The modules Node.js has of its own, which only Node's type definitions describe - and node:fs, node:readline.</summary>
    private static readonly HashSet<string> NodeModules = new(StringComparer.Ordinal)
    {
        "assert", "async_hooks", "buffer", "child_process", "cluster", "console", "crypto", "dgram", "dns", "events", "fs", "fs/promises", "http",
        "http2", "https", "inspector", "module", "net", "os", "path", "perf_hooks", "process", "querystring", "readline", "readline/promises",
        "repl", "stream", "string_decoder", "timers", "tls", "tty", "url", "util", "v8", "vm", "worker_threads", "zlib",
    };

    /// <summary>The names Node.js gives a program that TypeScript says only "Cannot find name" of, with no word of Node's types.</summary>
    private static readonly HashSet<string> NodeNames = new(StringComparer.Ordinal) { "__dirname", "__filename", "global", "exports", "setImmediate", "clearImmediate" };

    [GeneratedRegex(@"^Cannot find (?:name|module) '(?<name>[^']+)'")]
    private static partial Regex CannotFind();

    /// <summary>The name a "Cannot find name 'process'" or "Cannot find module 'fs'" message says was not found, or null.</summary>
    public static string? NameNotFound(string? message) => CannotFind().Match(message ?? "") is { Success: true } found ? found.Groups["name"].Value : null;

    /// <summary>
    /// Whether an error only says Node's own type definitions are not here - "Cannot find name 'process'. Do you need to install
    /// type definitions for node?" (TS2580 and TS2591), "Cannot find name '__dirname'" (TS2304), or a module of Node's own that
    /// cannot be found (TS2307) - which says nothing of the program, as Node.js gives it every one of those names.
    /// </summary>
    public static bool OnlyMissingNodeTypes(string? code, string? message)
    {
        if (code is "TS2580" or "TS2591") return true;
        if (code is not ("TS2304" or "TS2307") || CannotFind().Match(message ?? "") is not { Success: true } found) return false;

        var name = found.Groups["name"].Value;
        return code == "TS2304" ? NodeNames.Contains(name) : name.StartsWith("node:", StringComparison.Ordinal) || NodeModules.Contains(name);
    }
}
