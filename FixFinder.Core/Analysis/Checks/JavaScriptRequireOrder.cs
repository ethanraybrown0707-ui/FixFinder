using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Where requires that go round in a circle leave a JavaScript module holding nothing: the require that takes a name from a
/// module Node is still part way through, before that module exports it - or keeps the module's exports object, which the
/// module then replaces with module.exports = ... - and where the name is used.
/// </summary>
/// <param name="Local">The name the requiring file holds it under.</param>
/// <param name="Key">
/// The name it asks the module for: the key taken, or read from the object it keeps; null when what is used is the object
/// kept itself, which the module replaces with something whose names cannot be told - a class, a function.
/// </param>
/// <param name="ExportedOn">The line of <paramref name="ModuleFile"/> that exports <paramref name="Key"/>, or replaces its exports with an object that has it.</param>
/// <param name="StillOn">The line <paramref name="ModuleFile"/> was on, its own require, when it was asked.</param>
/// <param name="KeptTheOldObject">Whether the file keeps the exports object, which the module replaces, rather than taking a name out of it.</param>
/// <param name="UsedOn">A line of the requiring file that reads the name.</param>
public sealed record CircularRequireGap(
    string File, int Line, string Local, string? Key, string ModuleFile, int ExportedOn, int StillOn, bool KeptTheOldObject, int UsedOn,
    IReadOnlyList<ImportStep> Way);

/// <summary>
/// A CommonJS program's requires played out from the file that is run, as Node runs them, to find a circle of them that
/// leaves a module holding undefined where it expects a name another module exports.
/// </summary>
/// <remarks>
/// Node runs a module's top level the first time it is required, and a module part way through is already there for
/// others to require - handing over its exports object as it stands, with only what its lines so far have put in it. The
/// file that is run is one of them, and is never started again. A const that takes a name out of that object - const
/// { TAX_RATE } = require("./shop"), or require("./shop").TAX_RATE - holds undefined for as long as the program runs when
/// the module exports the name only further down; a const that keeps the whole object holds what the module later puts
/// in it, unless the module replaces it with module.exports = ..., after which it never sees the new one. This follows
/// those rules over the top level of each of the program's own files, read with FixFinder's own JavaScript reader, and
/// says the first such name the file goes on to use. A file of ES modules, a require or an export inside a block or a
/// function, or anything else it cannot follow for certain ends the playing out with nothing said.
/// </remarks>
public static partial class JavaScriptRequireOrder
{
    public const string FoundBy = "following the order Node runs the program's requires in";

    [GeneratedRegex(@"(?m)^\s*(?:import\s*[\w{*""'(]|export\s)")]
    private static partial Regex EsModuleSyntax();

    [GeneratedRegex(@"""type""\s*:\s*""module""")]
    private static partial Regex TypeModule();

    /// <summary>One thing a module's top level does that its requires and its exports depend on, in the order it does them.</summary>
    private abstract record TopLevelStep(int Line);

    /// <summary>require(...), kept whole under <paramref name="Local"/>, a <paramref name="Key"/> taken out of it, or nothing kept.</summary>
    private sealed record RequireStep(int Line, string Specifier, string? Local, string? Key) : TopLevelStep(Line);

    /// <summary>module.exports = ...: the names the new object has, or null when they cannot be told from the code.</summary>
    private sealed record ReplaceExportsStep(int Line, IReadOnlySet<string>? Gives) : TopLevelStep(Line);

    /// <summary>module.exports.key = ... or exports.key = ...: one name put in an exports object.</summary>
    private sealed record ExportStep(int Line, string Key, bool ThroughExports) : TopLevelStep(Line);

    /// <summary>
    /// Plays the requires out from <paramref name="runFile"/> over the program's own <paramref name="files"/> and gives back
    /// the finding where a circle of them leaves a name undefined, or null when none does.
    /// </summary>
    public static async Task<AnalysisFinding?> CheckAsync(string runFile, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        var javaScriptFiles = files.Append(runFile)
            .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".js" or ".cjs")
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (InAnEsModulePackage(runFile)) return null;

        return Find(runFile, javaScriptFiles, await JavaScriptFrontend.ReadAsync(javaScriptFiles, cancellationToken));
    }

    /// <summary>What each file of a program already read does at its top, worked out once for every playing out over it.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IrProgram, Dictionary<string, IReadOnlyList<TopLevelStep>?>> StepsRead = new();

    /// <summary>The same, over files already read: the playing out from each of many files reads them once.</summary>
    public static AnalysisFinding? Find(string runFile, IReadOnlyList<string> javaScriptFiles, IrProgram program)
    {
        var stepsByFile = StepsRead.GetValue(program, read =>
        {
            var functionsByFile = read.Functions.Concat(read.Classes.SelectMany(type => type.Methods))
                .ToLookup(function => Path.GetFullPath(function.Span.File), StringComparer.OrdinalIgnoreCase);
            var worked = new Dictionary<string, IReadOnlyList<TopLevelStep>?>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in javaScriptFiles.Select(Path.GetFullPath)) worked[file] = StepsOf(file, functionsByFile[file].ToList());
            return worked;
        });

        return Gap(Path.GetFullPath(runFile), stepsByFile, program) is { } gap ? FindingFor(gap, Path.GetFullPath(runFile)) : null;
    }

    /// <summary>Whether the nearest package.json above the file says "type": "module", making its .js files ES modules.</summary>
    private static bool InAnEsModulePackage(string file)
    {
        for (var folder = Path.GetDirectoryName(Path.GetFullPath(file)); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            var package = Path.Combine(folder, "package.json");
            if (!File.Exists(package)) continue;
            try
            {
                return TypeModule().IsMatch(File.ReadAllText(package));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What a file's top level does that matters here, in order; null when it does any of it in a way this cannot follow
    /// for certain - an ES module, a require or an export inside a block, an export made inside a function, exports replaced.
    /// </summary>
    private static IReadOnlyList<TopLevelStep>? StepsOf(string file, IReadOnlyList<IrFunction> functionsOfTheFile)
    {
        if (Path.GetExtension(file).Equals(".mjs", StringComparison.OrdinalIgnoreCase)) return null;
        if (SourceFileText(file) is not { } text || EsModuleSyntax().IsMatch(text)) return null;
        if (functionsOfTheFile.FirstOrDefault(function => function.Name == IrFunction.ModuleBody) is not { } module) return null;

        var steps = new List<TopLevelStep>();
        var followed = new HashSet<Expr>(ReferenceEqualityComparer.Instance);

        foreach (var statement in module.Body)
        {
            switch (statement)
            {
                case Declare { Initial: { } initial } declared when Required(initial) is (var specifier, var key, var partOfAValue):
                    followed.UnionWith(IrWalk.Within(initial));
                    var named = module.Imports.FirstOrDefault(taken => taken.Local == declared.Variable && taken.Specifier == specifier);
                    if (named is null) return null;
                    steps.Add(new RequireStep(statement.Span.Line, specifier, declared.Variable, partOfAValue ? named.Key : key));
                    break;

                case Evaluate { Value: var value } when Required(value) is (var specifier, null, false):
                    followed.UnionWith(IrWalk.Within(value));
                    steps.Add(new RequireStep(statement.Span.Line, specifier, null, null));
                    break;

                case Evaluate { Value: AssignValue assigned } when ExportsBy(assigned) is { } exported:
                    followed.UnionWith(IrWalk.Within(assigned));
                    steps.Add(exported with { Line = statement.Span.Line });
                    break;
            }
        }

        // A require or an export anywhere else in the top level - in an if, a loop, a block - may or may not happen as the
        // module is first run, and an export made in a function happens whenever it is called: neither can be followed.
        foreach (var function in functionsOfTheFile)
        {
            foreach (var expression in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within))
            {
                if (followed.Contains(expression)) continue;
                if (expression is AssignValue assigned && ExportsBy(assigned) is not null) return null;
                if (expression is AssignValue { Target: Name { Identifier: "exports" } }) return null;
                if (function.Name == IrFunction.ModuleBody && Required(expression) is not null) return null;
            }
        }

        return steps;
    }

    /// <summary>The file's text, or null when it cannot be read.</summary>
    private static string? SourceFileText(string file)
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

    /// <summary>
    /// require("./prices") - kept whole - require("./prices").key, one name taken out of it, or the value a destructuring
    /// pattern takes its names from, which the reader wraps as part of a value.
    /// </summary>
    private static (string Specifier, string? Key, bool PartOfAValue)? Required(Expr value) => value switch
    {
        Call { Callee: Name { Identifier: "require" }, Arguments: [{ Name: null, Value: Literal { Kind: LiteralKind.Text, Value: string specifier } }] } =>
            (specifier, null, false),
        Member { Target: Call { Callee: Name { Identifier: "require" }, Arguments: [{ Name: null, Value: Literal { Kind: LiteralKind.Text, Value: string specifier } }] }, MemberName: var key } =>
            (specifier, key, false),
        Opaque { What: "part of a value", Parts: [var whole] } when Required(whole) is (var specifier, null, false) => (specifier, null, true),
        _ => null,
    };

    /// <summary>What an assignment does to the module's exports, if it is one of the plain ways of making them.</summary>
    private static TopLevelStep? ExportsBy(AssignValue assigned) => assigned.Target switch
    {
        Member { Target: Name { Identifier: "module" }, MemberName: "exports" } => new ReplaceExportsStep(0, NamesOf(assigned.Value)),
        Member { Target: Member { Target: Name { Identifier: "module" }, MemberName: "exports" }, MemberName: var key } => new ExportStep(0, key, false),
        Member { Target: Name { Identifier: "exports" }, MemberName: var key } => new ExportStep(0, key, true),
        _ => null,
    };

    /// <summary>The names an object written out in place has; null for any other value, whose names cannot be told.</summary>
    private static IReadOnlySet<string>? NamesOf(Expr value) =>
        value is CollectionLiteral { Kind: CollectionKind.Dictionary, Keys: { } keys } && keys.All(key => key is Literal { Value: string })
            ? keys.Select(key => (string)((Literal)key).Value!).ToHashSet(StringComparer.Ordinal)
            : null;

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>An exports object as far as the playing out knows it: the names put in it, or that they cannot be told.</summary>
    private sealed class ExportsObject
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        /// <summary>Set for what module.exports = Library, a class or a function, gives: whatever names it has are its own.</summary>
        public bool NamesUnknown { get; init; }
    }

    /// <summary>A module Node has started: the exports object it hands over now, and the one exports still names.</summary>
    private sealed class StartedModule(string file)
    {
        public string File { get; } = file;
        public ExportsObject FirstObject { get; } = new();
        public ExportsObject HandedOver { get; set; } = null!;
        public bool Finished { get; set; }
        public int OnLine { get; set; }
    }

    /// <summary>The first name a circle of requires leaves undefined in a file that uses it, or null when there is none to say for certain.</summary>
    private static CircularRequireGap? Gap(string runFile, IReadOnlyDictionary<string, IReadOnlyList<TopLevelStep>?> stepsByFile, IrProgram program)
    {
        if (stepsByFile.GetValueOrDefault(runFile) is null) return null;

        var started = new Dictionary<string, StartedModule>(StringComparer.OrdinalIgnoreCase);
        var way = new List<ImportStep>();
        CircularRequireGap? gap = null;
        var ended = false;

        void Run(StartedModule module)
        {
            module.HandedOver = module.FirstObject;
            foreach (var step in stepsByFile[module.File]!)
            {
                if (ended) return;
                module.OnLine = step.Line;
                switch (step)
                {
                    case ReplaceExportsStep replaced:
                        module.HandedOver = new ExportsObject { NamesUnknown = replaced.Gives is null };
                        if (replaced.Gives is { } named) module.HandedOver.Names.UnionWith(named);
                        break;
                    case ExportStep exported:
                        (exported.ThroughExports ? module.FirstObject : module.HandedOver).Names.Add(exported.Key);
                        break;
                    case RequireStep required:
                        Require(module, required);
                        break;
                }
            }

            module.Finished = true;
        }

        void Require(StartedModule importer, RequireStep step)
        {
            if (FileRequired(importer.File, step.Specifier, stepsByFile) is not { } file) return;
            if (stepsByFile[file] is null)
            {
                ended = true;
                return;
            }

            if (!started.TryGetValue(file, out var required))
            {
                required = new StartedModule(file);
                started[file] = required;
                way.Add(new ImportStep(importer.File, step.Line, step.Specifier, false));
                Run(required);
                if (ended) return;
                way.RemoveAt(way.Count - 1);
            }

            if (required.Finished || step.Local is null) return;

            // A name taken now is what the object holds now; an object kept is the one handed over now, which a later
            // module.exports = ... leaves behind.
            var laterSteps = stepsByFile[file]!.Where(later => later.Line > required.OnLine).ToList();
            if (step.Key is { } key)
            {
                // From exports whose names cannot be told, whether the name is there cannot be told either.
                if (required.HandedOver.NamesUnknown || required.HandedOver.Names.Contains(key)) return;
                if (LineExporting(laterSteps, key, keptTheOldObject: false) is not { } exportedOn) return;
                if (LineUsing(importer.File, step.Local, step.Line, null, program) is { } usedOn)
                    gap = new CircularRequireGap(importer.File, step.Line, step.Local, key, file, exportedOn, required.OnLine, false, usedOn,
                        [.. way, new ImportStep(importer.File, step.Line, step.Specifier, false)]);
                ended = gap is not null;
                return;
            }

            if (laterSteps.OfType<ReplaceExportsStep>().FirstOrDefault() is not { } replacement) return;

            // Replaced with a class or a function: the file keeps the exports object from before, and a use of it is not a
            // use of what the module goes on to be.
            if (replacement.Gives is not { } replacedWith)
            {
                if (LineUsing(importer.File, step.Local, step.Line, null, program) is { } usedAtAll)
                    gap = new CircularRequireGap(importer.File, step.Line, step.Local, null, file, replacement.Line, required.OnLine, true, usedAtAll,
                        [.. way, new ImportStep(importer.File, step.Line, step.Specifier, false)]);
                ended = gap is not null;
                return;
            }

            // exports.name = ... still puts the name in the first object, which is the one kept when that was what was handed over.
            var keptGetsLater = ReferenceEquals(required.HandedOver, required.FirstObject)
                ? laterSteps.OfType<ExportStep>().Where(exported => exported.ThroughExports).Select(exported => exported.Key).ToHashSet(StringComparer.Ordinal)
                : [];
            foreach (var asked in replacedWith.Where(name => !required.HandedOver.Names.Contains(name) && !keptGetsLater.Contains(name)).OrderBy(name => name, StringComparer.Ordinal))
            {
                if (LineUsing(importer.File, step.Local, step.Line, asked, program) is not { } usedOn) continue;
                gap = new CircularRequireGap(importer.File, step.Line, step.Local, asked, file, replacement.Line, required.OnLine, true, usedOn,
                    [.. way, new ImportStep(importer.File, step.Line, step.Specifier, false)]);
                ended = true;
                return;
            }
        }

        var runModule = new StartedModule(runFile);
        started[runFile] = runModule;
        Run(runModule);
        return gap;
    }

    /// <summary>The line of the steps that exports the name: put into the exports, or in the object they are replaced with.</summary>
    private static int? LineExporting(IEnumerable<TopLevelStep> steps, string key, bool keptTheOldObject) => steps.FirstOrDefault(step => step switch
    {
        ExportStep exported => exported.Key == key,
        ReplaceExportsStep { Gives: { } gives } => !keptTheOldObject && gives.Contains(key),
        _ => false,
    })?.Line;

    /// <summary>
    /// The first line of the file after its require that reads the name - or, with <paramref name="member"/>, reads that
    /// member of it, as shop.TAX_RATE - anywhere in the file, its functions too; null when nothing does.
    /// </summary>
    private static int? LineUsing(string file, string local, int requiredOn, string? member, IrProgram program)
    {
        var lines = program.Functions.Where(function => SamePath(function.Span.File, file))
            .Concat(program.Classes.SelectMany(type => type.Methods).Where(method => SamePath(method.Span.File, file)))
            .SelectMany(function => IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within))
            .Where(expression => member is null
                ? expression is Name { Identifier: var named } && named == local
                : expression is Member { Target: Name { Identifier: var holder }, MemberName: var asked } && holder == local && asked == member)
            .Select(expression => expression.Span.Line)
            .Where(line => line != requiredOn)
            .ToList();
        return lines.Count == 0 ? null : lines.Min();
    }

    /// <summary>The program's own file a relative require names, tried as Node tries it: as written, then .js, .cjs, and index.js in the folder.</summary>
    private static string? FileRequired(string importer, string specifier, IReadOnlyDictionary<string, IReadOnlyList<TopLevelStep>?> stepsByFile)
    {
        if (!specifier.StartsWith("./", StringComparison.Ordinal) && !specifier.StartsWith("../", StringComparison.Ordinal)) return null;

        var written = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(importer)!, specifier));
        return new[] { written, written + ".js", written + ".cjs", Path.Combine(written, "index.js") }.FirstOrDefault(stepsByFile.ContainsKey);
    }

    /// <summary>The finding where the circle leaves the name undefined, said with each require on the way.</summary>
    private static AnalysisFinding FindingFor(CircularRequireGap gap, string runFile)
    {
        string Named(string file) => SamePath(file, runFile) ? $"{Path.GetFileName(file)} - the file being run, which Node does not start again -" : Path.GetFileName(file);
        static string Plainly(string file) => Path.GetFileName(file);
        var steps = gap.Way.SkipLast(1).Select(step => $"{Plainly(step.File)} line {step.Line} requires {step.Imports}").ToList();
        var required = gap.Way[^1].Imports;
        var held = gap.KeptTheOldObject && gap.Key is { } member ? $"{gap.Local}.{member}" : gap.Local;
        var circle = "because the requires go round in a circle: " + string.Join("; ", steps) + (steps.Count > 0 ? "; and " : "");

        // What is kept is the exports object from before module.exports = Library: not undefined, but not the class either.
        if (gap is { KeptTheOldObject: true, Key: null })
        {
            var kept = $"Run as it is, {gap.Local} in {Plainly(gap.File)} is the exports object {Plainly(gap.ModuleFile)} had before its line {gap.ExportedOn} " +
                       $"replaces it with module.exports = ..., for as long as the program runs, and its line {gap.UsedOn} uses it, {circle}" +
                       $"{Plainly(gap.File)} line {gap.Line} requires {required} and keeps what {Named(gap.ModuleFile)} has exported so far, while it is still on line {gap.StillOn}";
            return new AnalysisFinding(PythonImportOrder.CheckId, new SourceSpan(gap.File, gap.Line), kept, Severity.Error, Confidence.Likely, FindingKind.Logic, FoundBy)
            {
                Function = IrFunction.ModuleBody,
            };
        }

        var taken = gap.KeptTheOldObject
            ? $"{Plainly(gap.File)} line {gap.Line} requires {required} and keeps what {Named(gap.ModuleFile)} has exported so far, while it is " +
              $"still on line {gap.StillOn} - and its line {gap.ExportedOn} then replaces that with module.exports = ..., which {Plainly(gap.File)} never sees"
            : $"{Plainly(gap.File)} line {gap.Line} requires {required} and takes {gap.Key} out of what {Plainly(gap.ModuleFile)} has exported so far, " +
              $"which is not it yet: {Named(gap.ModuleFile)} exports {gap.Key} on line {gap.ExportedOn}, and is still on line {gap.StillOn}";

        var message = $"Run as it is, {held} is undefined in {Plainly(gap.File)} for as long as the program runs, and its line {gap.UsedOn} reads it, " +
                      circle + taken;

        return new AnalysisFinding(PythonImportOrder.CheckId, new SourceSpan(gap.File, gap.Line), message, Severity.Error, Confidence.Likely, FindingKind.Logic, FoundBy)
        {
            Function = IrFunction.ModuleBody,
        };
    }
}
