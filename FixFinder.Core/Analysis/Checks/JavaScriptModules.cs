using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Which function a JavaScript call runs, followed from file to file. A name the caller's module binds at its top level
/// is followed when it is bound once and never set again: a function declared there, a name given a function written in
/// place, or a name imported - by import or require - from another file of the program, whose exports say what it is. A
/// call through a whole module - helpers.total() - is followed the same way. A module that is not one of the program's
/// own files, a name set twice, or an export that could be more than one thing, is left alone.
/// </summary>
internal sealed class JavaScriptModules
{
    /// <summary>How many names and files one call is followed through - a module passing on another's export - before giving up.</summary>
    private const int MostHops = 8;

    /// <summary>How a relative specifier can leave out the end of a file's name, in the order Node tries them.</summary>
    private static readonly string[] Endings = ["", ".js", ".mjs", ".cjs"];

    private static readonly string[] Indexes = ["index.js", "index.mjs", "index.cjs"];

    private readonly Dictionary<string, IrFunction> _moduleOf;
    private readonly ILookup<string, IrFunction> _functionsIn;
    private readonly Dictionary<(string File, string Name), bool> _setAgain = [];

    public JavaScriptModules(IrProgram program)
    {
        _moduleOf = program.Functions
            .Where(function => function.Name == IrFunction.ModuleBody)
            .GroupBy(function => SourceFiles.FullPath(function.Span.File) ?? "", SourceFiles.Comparer)
            .Where(file => file.Key.Length > 0 && file.Count() == 1)
            .ToDictionary(file => file.Key, file => file.Single(), SourceFiles.Comparer);
        _functionsIn = program.AllFunctions.ToLookup(function => SourceFiles.FullPath(function.Span.File) ?? "", SourceFiles.Comparer);
    }

    /// <summary>The function a call by name runs, when the name is one the caller's module binds.</summary>
    public CallTarget? Named(string name, IrFunction caller, SourceSpan calledAt) =>
        SourceFiles.FullPath(caller.Span.File) is { } file && Bound(file, name, hops: 0, TopLevel(caller, calledAt)) is { } function
            ? new CallTarget(function, false)
            : null;

    /// <summary>The function helpers.total() runs, when helpers is a whole module the caller's module imports, or require("./helpers") itself.</summary>
    public CallTarget? Member(Expr holder, string key, IrFunction caller, SourceSpan calledAt)
    {
        if (SourceFiles.FullPath(caller.Span.File) is not { } file) return null;

        var source = holder switch
        {
            Name { Identifier: var local } => WholeModuleNamed(file, local, TopLevel(caller, calledAt)),
            Call { Callee: Name { Identifier: "require" }, Arguments: [{ Name: null, Value: Literal { Value: string specifier } }] } => FileFor(specifier, file),
            _ => null,
        };

        return source is not null && Exported(source, key) is { } value && Given(value, source, hops: 1) is { } function ? new CallTarget(function, false) : null;
    }

    /// <summary>Where a call is made, when it is made by the module's own top-level code - which runs once, in order.</summary>
    private static SourceSpan? TopLevel(IrFunction caller, SourceSpan calledAt) => caller.Name == IrFunction.ModuleBody ? calledAt : null;

    /// <summary>
    /// Whether a name is set by the time top-level code calls through it. That code runs in order, and a name that is not
    /// hoisted - a const, a let, a var, a require - cannot be used above its declaration: the call fails there, before
    /// anything the function would do. A function declaration and an import are ready from the start.
    /// </summary>
    private static bool ReadyBy(Declare declaration, SourceSpan? topLevelCall) =>
        topLevelCall is null || declaration.Type.Name == "function" || declaration.Initial is Opaque { What: "imported" } ||
        (declaration.Span.Line, declaration.Span.Column).CompareTo((topLevelCall.Line, topLevelCall.Column)) < 0;

    /// <summary>What a name bound at a module's top level is, when it is certainly a function.</summary>
    private IrFunction? Bound(string file, string name, int hops, SourceSpan? topLevelCall = null)
    {
        if (hops > MostHops || !_moduleOf.TryGetValue(file, out var module) || SetAgain(file, name)) return null;

        // Declared once, at the top level itself: a declaration inside a block, or a second one, may not be the one in force.
        if (Single(module.Body.OfType<Declare>().Where(declare => declare.Variable == name)) is not { } declaration) return null;
        if (!ReadyBy(declaration, topLevelCall)) return null;

        var imports = module.Imports.Where(import => import.Local == name).ToList();
        if (imports.Count > 0) return imports is [var only] ? Imported(only, file, hops) : null;

        if (declaration.Type.Name == "function") return DeclaredAt(file, declaration.Span);
        return declaration.Initial is { } initial ? Given(initial, file, hops + 1) : null;
    }

    /// <summary>The function a value is: a name bound to one, or a function written in place.</summary>
    private IrFunction? Given(Expr value, string file, int hops) => value switch
    {
        Name { Identifier: var name } => Bound(file, name, hops),
        Opaque { What: "lambda expression" } written => Single(_functionsIn[file].Where(function => function.Span == written.Span)),
        _ => null,
    };

    /// <summary>A function declared at the top level - function total() {} - which starts where its declaration does.</summary>
    private IrFunction? DeclaredAt(string file, SourceSpan declared) =>
        Single(_functionsIn[file].Where(function => function is { Owner: null, EnclosedBy: IrFunction.ModuleBody } &&
                                                     function.Span.Line == declared.Line && function.Span.Column == declared.Column));

    /// <summary>What an import is, found in the exports of the file it names.</summary>
    private IrFunction? Imported(ModuleImport import, string file, int hops)
    {
        // import * as all gives an object holding the exports, which is not itself a function.
        if (import.Key == ModuleImport.Namespace || FileFor(import.Specifier, file) is not { } source) return null;

        var value = import.Key is null
            ? Exported(source, ModuleImport.WholeModule)
            : Exported(source, import.Key) ?? (import.Key == ModuleImport.DefaultExport ? Exported(source, ModuleImport.WholeModule) : null);

        return value is null ? null : Given(value, source, hops + 1);
    }

    private Expr? Exported(string file, string key) => _moduleOf.TryGetValue(file, out var module) ? module.Exports.GetValueOrDefault(key) : null;

    /// <summary>The file a whole module was imported from: import * as all, or a plain require kept in a name.</summary>
    private string? WholeModuleNamed(string file, string local, SourceSpan? topLevelCall)
    {
        if (!_moduleOf.TryGetValue(file, out var module) || SetAgain(file, local)) return null;
        if (Single(module.Body.OfType<Declare>().Where(declare => declare.Variable == local)) is not { } declaration || !ReadyBy(declaration, topLevelCall)) return null;

        return module.Imports.Where(import => import.Local == local).ToList() is [{ Key: null or ModuleImport.Namespace } whole]
            ? FileFor(whole.Specifier, file)
            : null;
    }

    /// <summary>
    /// The program's own file a specifier names: ./helpers as written, or with .js, .mjs or .cjs after it, or the index
    /// file of a folder. A package's name is never one of the program's files.
    /// </summary>
    private string? FileFor(string specifier, string importer)
    {
        if (!(specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal) || specifier is "." or "..")) return null;
        if (Path.GetDirectoryName(importer) is not { } folder || SourceFiles.FullPath(Path.Combine(folder, specifier)) is not { } named) return null;

        return Endings.Select(ending => named + ending).Concat(Indexes.Select(index => Path.Combine(named, index)))
            .FirstOrDefault(_moduleOf.ContainsKey);
    }

    /// <summary>Whether anything in the file sets the name after it is declared - so a call through it may run something else.</summary>
    private bool SetAgain(string file, string name)
    {
        if (_setAgain.TryGetValue((file, name), out var known)) return known;

        var set = _functionsIn[file].SelectMany(function => IrWalk.Statements(function.Body)).Any(statement => statement switch
        {
            Assign assign when IrWalk.Binds(assign.Target, name) => true,
            ForEach loop when IrWalk.Binds(loop.Target, name) => true,
            OpaqueStmt opaque when opaque.MayAssign.Contains(name) => true,
            _ => IrWalk.Expressions(statement).SelectMany(IrWalk.Within).Any(inner => inner is AssignValue assigned && IrWalk.Binds(assigned.Target, name)),
        });

        return _setAgain[(file, name)] = set;
    }

    private static T? Single<T>(IEnumerable<T> candidates) where T : class
    {
        using var each = candidates.GetEnumerator();
        if (!each.MoveNext()) return null;
        var first = each.Current;
        return each.MoveNext() ? null : first;
    }
}
