using System.Text;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>One statement at the top of a Python file that a first import of it runs.</summary>
/// <param name="Kind">"import", "from" for from ... import, or "gives" for a line that gives names values.</param>
/// <param name="Module">The module an import names, as written: prices, or .prices's prices with its <paramref name="Level"/>.</param>
/// <param name="Level">How many dots a relative import starts with; 0 for an absolute one.</param>
/// <param name="Names">What a from ... import takes out of the module.</param>
/// <param name="Gives">The names the statement gives values to in the module it is in.</param>
/// <param name="OnlyWhenRun">Whether it is inside if __name__ == "__main__", so runs only in the file that is run.</param>
public sealed record TopLevelStatement(
    int Line, string Kind, string Module, int Level, IReadOnlyList<string> Names, IReadOnlyList<string> Gives, bool OnlyWhenRun);

/// <summary>One import on the way to the one that stops the program.</summary>
/// <param name="StartsTheRunFileAgain">
/// Whether the import named the file that is run, which Python then runs again from its top as a module of its own.
/// </param>
public sealed record ImportStep(string File, int Line, string Imports, bool StartsTheRunFileAgain);

/// <summary>
/// Where imports that go round in a circle stop a program: the from ... import that asks a module Python is still part way
/// through for a name the module defines further down.
/// </summary>
/// <param name="Way">Each import from the file that is run to the one that stops it, the last being that one.</param>
/// <param name="DefinedOn">The line of <paramref name="ModuleFile"/> that gives <paramref name="Name"/> its value.</param>
/// <param name="StillOn">The line <paramref name="ModuleFile"/> was on, its own import, when it was asked.</param>
public sealed record CircularImportStop(
    string File, int Line, string Module, string ModuleFile, string Name, int DefinedOn, int StillOn, IReadOnlyList<ImportStep> Way);

/// <summary>
/// Python's imports played out from the file that is run, to find imports that go round in a circle and stop the program
/// before it does anything: shop.py imports prices, prices.py imports shop, and one of them asks the other for a name
/// before the other has reached the line that defines it.
/// </summary>
/// <remarks>
/// Python runs a module's top-level lines the first time it is imported, and a module it is part way through is already
/// there for others to import, holding only what its lines so far have defined; the file that is run is __main__, so a
/// module that imports it by its own name starts it again from its top. This follows the same rules over the statements
/// Python's own parser finds at the top of each of the program's own files, in their order: an import of a module not yet
/// started starts it, and a from ... import of a name the module has not yet reached - though a line further down defines
/// it - is where the program stops with ImportError. Only that is reported. A block whose running depends on values - an
/// if, a for - is not followed into, and every name it could define counts as defined, so what the circle does is never
/// guessed: a name defined nowhere, or a module that is not one of the program's own, ends the playing out with nothing found.
/// </remarks>
public static class PythonImportOrder
{
    public const string CheckId = "analysis-circular-import";

    public const string FoundBy = "following the order Python runs the program's imports in";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Plays the imports out from <paramref name="runFile"/> over the program's <paramref name="files"/>, read with
    /// <paramref name="interpreter"/>'s own parser, and gives back the finding where they stop it, or null when they do not.
    /// </summary>
    public static async Task<AnalysisFinding?> CheckAsync(string runFile, IReadOnlyList<string> files, string interpreter, CancellationToken cancellationToken)
    {
        var pythonFiles = files.Append(runFile).Where(file => file.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var read = await ReadAsync(pythonFiles, interpreter, cancellationToken);
        return Stop(Path.GetFullPath(runFile), read) is { } stop ? FindingFor(stop, Path.GetFullPath(runFile)) : null;
    }

    /// <summary>The top-level statements of each file Python's parser could read, by its whole path.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<TopLevelStatement>>> ReadAsync(
        IReadOnlyList<string> files, string interpreter, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-analysis");
        var id = Guid.NewGuid().ToString("N");
        var script = Path.Combine(folder, $"imports-{id}.py");
        var output = Path.Combine(folder, $"imports-{id}.json");
        var listing = Path.Combine(folder, $"imports-{id}.files");
        var statementsByFile = new Dictionary<string, IReadOnlyList<TopLevelStatement>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(script, PythonImportOrderScript.Source, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllLinesAsync(listing, files, new UTF8Encoding(false), cancellationToken);

            await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = interpreter,
                Arguments = $"-X utf8 \"{script}\" \"{output}\" \"{listing}\"",
                WorkingDirectory = folder,
                Timeout = ReadTimeout,
            }, cancellationToken);

            if (!File.Exists(output)) return statementsByFile;

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken));
            foreach (var entry in document.RootElement.GetProperty("files").EnumerateArray())
            {
                if (!entry.TryGetProperty("statements", out var statements) || entry.GetProperty("path").GetString() is not { } path) continue;
                statementsByFile[Path.GetFullPath(path)] =
                [
                    .. statements.EnumerateArray().Select(statement => new TopLevelStatement(
                        statement.GetProperty("line").GetInt32(), statement.GetProperty("kind").GetString() ?? "",
                        statement.GetProperty("module").GetString() ?? "", statement.GetProperty("level").GetInt32(),
                        [.. statement.GetProperty("names").EnumerateArray().Select(name => name.GetString() ?? "")],
                        [.. statement.GetProperty("gives").EnumerateArray().Select(name => name.GetString() ?? "")],
                        statement.GetProperty("onlyWhenRun").GetBoolean())).OrderBy(statement => statement.Line),
                ];
            }

            return statementsByFile;
        }
        finally
        {
            foreach (var made in new[] { script, output, listing })
            {
                try { File.Delete(made); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Where the imports stop the program when <paramref name="runFile"/> is run, given each file's top-level statements, or
    /// null when they do not stop it in a circle.
    /// </summary>
    public static CircularImportStop? Stop(string runFile, IReadOnlyDictionary<string, IReadOnlyList<TopLevelStatement>> statementsByFile) =>
        statementsByFile.ContainsKey(runFile) ? new ImportPlay(runFile, statementsByFile).PlayFromTheRunFile() : null;

    /// <summary>A module Python has started: what its lines so far have defined, and whether it has reached its end.</summary>
    private sealed class StartedModule(string name, string file)
    {
        public string Name { get; } = name;
        public string File { get; } = file;
        public HashSet<string> Defined { get; } = new(StringComparer.Ordinal);
        public bool Finished { get; set; }
        public int OnLine { get; set; }
    }

    /// <summary>One playing out of the imports, holding the modules started so far as Python's sys.modules does.</summary>
    private sealed class ImportPlay(string runFile, IReadOnlyDictionary<string, IReadOnlyList<TopLevelStatement>> statementsByFile)
    {
        private readonly string _searchedFolder = Path.GetDirectoryName(runFile)!;
        private readonly Dictionary<string, StartedModule> _started = new(StringComparer.Ordinal);
        private readonly List<ImportStep> _way = [];
        private CircularImportStop? _stop;

        /// <summary>Set once the playing out ends early: at the stop, or at anything it cannot follow for certain.</summary>
        private bool _ended;

        public CircularImportStop? PlayFromTheRunFile()
        {
            Run(new StartedModule("__main__", runFile), isTheRunFile: true);
            return _stop;
        }

        private void Run(StartedModule module, bool isTheRunFile)
        {
            foreach (var statement in statementsByFile[module.File])
            {
                if (_ended) return;
                if (statement.OnlyWhenRun && !isTheRunFile) continue;

                module.OnLine = statement.Line;
                if (statement.Kind is "import" or "from") Import(module, statement);
                if (_ended) return;
                module.Defined.UnionWith(statement.Gives);
            }

            module.Finished = true;
        }

        private void Import(StartedModule importer, TopLevelStatement statement)
        {
            if (NameOf(importer, statement) is not { } named) return;

            // A module that is not one of the program's own files - os, a library - is Python's or the library's to import.
            if (Started(named, importer, statement) is not { } imported || _ended) return;
            if (statement.Kind != "from") return;

            foreach (var name in statement.Names)
            {
                if (name == "*" || imported.Defined.Contains(name)) continue;

                // from a package import a module of it: Python imports the module, however far the package has got.
                if (Started($"{imported.Name}.{name}", importer, statement) is not null) continue;
                if (_ended) return;

                // Asked for before its line is reached, though a line further down defines it: the circle stops the program here.
                if (!imported.Finished && LineDefining(imported.File, name) is { } definedOn)
                {
                    _stop = new CircularImportStop(importer.File, statement.Line, imported.Name, imported.File, name, definedOn, imported.OnLine,
                        [.. _way, new ImportStep(importer.File, statement.Line, imported.Name, false)]);
                }

                // Either way the program stops here, and what comes after is not played out: a name no line defines is a
                // mistake of another kind, which is not this check's to report.
                _ended = true;
                return;
            }
        }

        /// <summary>The module a statement imports, its dotted name worked out as Python works it out; null when it cannot be.</summary>
        private static string? NameOf(StartedModule importer, TopLevelStatement statement)
        {
            if (statement.Level == 0) return statement.Module;
            if (importer.Name == "__main__") return null;

            var package = Path.GetFileName(importer.File).Equals("__init__.py", StringComparison.OrdinalIgnoreCase)
                ? importer.Name.Split('.').ToList()
                : importer.Name.Split('.').SkipLast(1).ToList();
            if (statement.Level - 1 > package.Count) return null;

            var parts = package.Take(package.Count - (statement.Level - 1)).ToList();
            if (statement.Module.Length > 0) parts.Add(statement.Module);
            return parts.Count == 0 ? null : string.Join(".", parts);
        }

        /// <summary>
        /// The module of that name, started - and run to its end, or to wherever the imports stop the program - when it had
        /// not been, its packages first, as Python does; null when it is not one of the program's own files.
        /// </summary>
        private StartedModule? Started(string dottedName, StartedModule importer, TopLevelStatement statement)
        {
            var parts = dottedName.Split('.');
            StartedModule? last = null;

            for (var depth = 1; depth <= parts.Length && !_ended; depth++)
            {
                var name = string.Join(".", parts.Take(depth));
                if (_started.TryGetValue(name, out var already))
                {
                    last = already;
                    continue;
                }

                // A folder with no __init__.py of its own is a package with nothing to run; a module of no file of the program's is not its own.
                if (FileOf(parts.Take(depth).ToList(), depth == parts.Length) is not { } file)
                {
                    if (depth == parts.Length) return null;
                    continue;
                }

                var module = new StartedModule(name, file);
                _started[name] = module;
                var startsTheRunFileAgain = string.Equals(file, runFile, StringComparison.OrdinalIgnoreCase);
                _way.Add(new ImportStep(importer.File, statement.Line, name, startsTheRunFileAgain));
                Run(module, isTheRunFile: false);
                if (!_ended) _way.RemoveAt(_way.Count - 1);
                last = module;
            }

            return last;
        }

        /// <summary>The program's own file for a module: a.py, or a package's a/__init__.py, in the folder Python searches first.</summary>
        private string? FileOf(IReadOnlyList<string> parts, bool isTheModuleNamed)
        {
            var folder = Path.Combine([_searchedFolder, .. parts.SkipLast(1)]);
            var asAFile = Path.GetFullPath(Path.Combine(folder, parts[^1] + ".py"));
            var asAPackage = Path.GetFullPath(Path.Combine(folder, parts[^1], "__init__.py"));

            if (statementsByFile.ContainsKey(asAPackage)) return asAPackage;
            if (isTheModuleNamed && statementsByFile.ContainsKey(asAFile)) return asAFile;
            return null;
        }

        /// <summary>The first line at the top of a file that defines the name, when the file is run as a module.</summary>
        private int? LineDefining(string file, string name) =>
            statementsByFile[file].FirstOrDefault(statement => !statement.OnlyWhenRun && statement.Gives.Contains(name))?.Line;
    }

    /// <summary>The finding where the circle stops the program, said with each import on the way.</summary>
    private static AnalysisFinding FindingFor(CircularImportStop stop, string runFile)
    {
        string Named(string file) => Path.GetFileName(file);
        var steps = stop.Way.SkipLast(1).Select(step =>
            $"{Named(step.File)} line {step.Line} imports {step.Imports}" +
            (step.StartsTheRunFileAgain ? $", which starts {Named(runFile)} again from its top as a module of its own, since {Named(runFile)} is the file being run" : ""));
        var asked = $"{Named(stop.File)} line {stop.Line} asks {stop.Module} for {stop.Name}, which {Named(stop.ModuleFile)} has not reached yet: " +
                    $"it defines {stop.Name} on line {stop.DefinedOn}, and is still on line {stop.StillOn}";

        var message = $"Run as it is, {Named(runFile)} stops with ImportError at line {stop.Line} of {Named(stop.File)}, as its imports go round in a circle: " +
                      string.Join("; ", steps) + (steps.Any() ? "; and " : "") + asked;

        return new AnalysisFinding(CheckId, new SourceSpan(stop.File, stop.Line), message, Severity.Error, Confidence.Likely, FindingKind.Logic, FoundBy)
        {
            Function = "<module>",
        };
    }
}
