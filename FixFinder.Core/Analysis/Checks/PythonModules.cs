using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Which function a Python call runs, followed from module to module. A module's top level binds a name by def, class,
/// import or assignment, and the name is followed only when it is bound exactly once: to a function or class defined in
/// that module, or by from-import to a name of another module of the program, followed there in turn. A star import is
/// followed for a name the module does not bind itself, and a module imported whole for helpers.total(). A name bound
/// twice, bound by a function through global, or that could come from a module outside the program, is left alone.
/// </summary>
internal sealed class PythonModules
{
    /// <summary>How many modules one call is followed through - a package passing on a submodule's function - before giving up.</summary>
    private const int MostHops = 8;

    private const string StarImportEnd = " import *";

    private readonly IrProgram _program;
    private readonly Dictionary<string, IrFunction> _moduleOf;
    private readonly ILookup<string, IrFunction> _functionsIn;
    private readonly Dictionary<(string File, string Name), IReadOnlyList<Binding>> _bindings = [];
    private readonly Dictionary<string, List<(string? Source, int Line)>> _starImports = new(SourceFiles.Comparer);

    /// <summary>One way a module binds a name: what it assigns, or null when that cannot be read - a loop, a with, a global.</summary>
    private sealed record Binding(Expr? Value, int Line);

    public PythonModules(IrProgram program)
    {
        _program = program;
        _moduleOf = program.Functions
            .Where(function => function.Name == IrFunction.ModuleBody)
            .GroupBy(function => SourceFiles.FullPath(function.Span.File) ?? "", SourceFiles.Comparer)
            .Where(file => file.Key.Length > 0 && file.Count() == 1)
            .ToDictionary(file => file.Key, file => file.Single(), SourceFiles.Comparer);
        _functionsIn = program.AllFunctions.ToLookup(function => SourceFiles.FullPath(function.Span.File) ?? "", SourceFiles.Comparer);
    }

    /// <summary>The function a call by name runs, when the name is one the caller's module binds or star-imports.</summary>
    public CallTarget? Named(string name, IrFunction caller, SourceSpan calledAt) =>
        SourceFiles.FullPath(caller.Span.File) is { } file ? Named(name, file, hops: 0, TopLevelLine(caller, calledAt)) : null;

    /// <summary>The function helpers.total() runs, when helpers - or a.b in a.b.total() - is a module of the program.</summary>
    public CallTarget? Member(Expr holder, string key, IrFunction caller, SourceSpan calledAt) =>
        SourceFiles.FullPath(caller.Span.File) is { } file && DottedModule(holder, file, TopLevelLine(caller, calledAt)) is { } module &&
        FileFor(module, file) is { } source
            ? Named(key, source, hops: 1)
            : null;

    /// <summary>
    /// The line of a call made by the module's own top-level code. That code runs once, in order, so a name it uses before
    /// the line that binds it does not exist yet - the call fails with a NameError, before anything the function would do.
    /// </summary>
    private static int? TopLevelLine(IrFunction caller, SourceSpan calledAt) => caller.Name == IrFunction.ModuleBody ? calledAt.Line : null;

    private CallTarget? Named(string name, string file, int hops, int? topLevelLine = null)
    {
        if (hops > MostHops || !_moduleOf.ContainsKey(file)) return null;

        var bindings = Bindings(file, name);
        var stars = StarImports(file).Where(star => topLevelLine is not { } line || star.Line <= line).ToList();

        if (bindings.Count == 0)
        {
            // Names starting with _ are left out of a star import, and a module outside the program could give any name.
            if (name.StartsWith('_') || stars.Any(star => star.Source is null)) return null;
            return stars.Where(star => GivesByStar(star.Source!, name)).Select(star => star.Source!).Distinct(SourceFiles.Comparer).ToList() is [var source]
                ? Named(name, source, hops + 1)
                : null;
        }

        if (bindings is not [{ Value: { } value } binding] || binding.Line > topLevelLine) return null;

        // A star import made after the binding replaces it with the other module's, if that module has the name.
        if (stars.Any(star => star.Line > binding.Line && (star.Source is null || Bindings(star.Source, name).Count > 0))) return null;

        return value switch
        {
            Opaque { What: "function" } => Single(_functionsIn[file].Where(function =>
                function.Name == name && function.Owner is null && function.EnclosedBy is null or IrFunction.ModuleBody)) is { } defined
                ? new CallTarget(defined, false)
                : null,

            Opaque { What: "class" } => Single(_program.Classes.Where(type => type.Name == name && SourceFiles.Comparer.Equals(SourceFiles.FullPath(type.Span.File), file))) is { } made &&
                                        Single(made.Methods.Where(method => method.Name == "__init__")) is { } init
                ? new CallTarget(init, true)
                : null,

            Opaque { What: var what } when FromImport(what) is (var module, var imported) =>
                FileFor(module, file) is { } source ? Named(imported, source, hops + 1) : null,

            _ => null,
        };
    }

    /// <summary>Whether a star import of the module gives the name: it binds it, and does not say with __all__ which names it gives.</summary>
    private bool GivesByStar(string source, string name) => Bindings(source, "__all__").Count == 0 && Bindings(source, name).Count > 0;

    /// <summary>
    /// The dotted name of the module an expression stands for: a module imported whole, a package's submodule reached
    /// through it - a.b after import a.b - or a submodule taken by from-import. Null for anything else.
    /// </summary>
    private string? DottedModule(Expr holder, string file, int? topLevelLine)
    {
        switch (holder)
        {
            case Name { Identifier: var name }:
                if (Bindings(file, name) is not [{ Value: Opaque { What: var what } } binding] || binding.Line > topLevelLine || StarImports(file).Count > 0) return null;

                if (what.StartsWith("module ", StringComparison.Ordinal))
                {
                    // import a.b binds a, the package; import a.b as c binds c, which is a.b itself.
                    var imported = what["module ".Length..];
                    return imported.StartsWith(name + ".", StringComparison.Ordinal) ? name : imported;
                }

                if (FromImport(what) is not (var package, var member)) return null;

                // from package import name takes what the package itself binds under that name, before any submodule.
                if (FileFor(package, file) is { } packageFile && Bindings(packageFile, member).Count > 0) return null;
                return package.EndsWith('.') ? package + member : $"{package}.{member}";

            case Member { Target: var outer, MemberName: var part }:
                if (DottedModule(outer, file, topLevelLine) is not { } parent) return null;

                // a.b is only a module here when an import of it, or of something inside it, was made.
                var dotted = $"{parent}.{part}";
                return WholeImports(file).Any(imported => imported == dotted || imported.StartsWith(dotted + ".", StringComparison.Ordinal)) ? dotted : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// The program's own file for a module's dotted name. A relative name - .helpers, ..shared.tools - is found from the
    /// importing file's package; any other must end the path of exactly one of the program's files, as helpers.py or
    /// helpers/__init__.py, or it is not followed.
    /// </summary>
    private string? FileFor(string dotted, string importer)
    {
        var level = dotted.TakeWhile(c => c == '.').Count();
        var parts = dotted[level..].Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (level > 0)
        {
            var folder = Path.GetDirectoryName(importer);
            for (var climbed = 1; climbed < level && folder is not null; climbed++) folder = Path.GetDirectoryName(folder);
            if (folder is null) return null;

            var path = Path.Combine([folder, .. parts]);
            return new[] { path + ".py", Path.Combine(path, "__init__.py") }.FirstOrDefault(_moduleOf.ContainsKey);
        }

        if (parts.Length == 0) return null;

        var ending = string.Join('/', parts);
        var matches = _moduleOf.Keys.Where(file => EndsWith(file, ending + ".py") || EndsWith(file, ending + "/__init__.py")).ToList();
        return matches is [var only] ? only : null;
    }

    private static bool EndsWith(string file, string ending)
    {
        var forward = file.Replace('\\', '/');
        return forward.EndsWith("/" + ending, StringComparison.OrdinalIgnoreCase) || forward.Equals(ending, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The modules a file star-imports, with the line each is imported on; a module outside the program has no file.</summary>
    private List<(string? Source, int Line)> StarImports(string file)
    {
        if (_starImports.TryGetValue(file, out var known)) return known;

        return _starImports[file] = IrWalk.Statements(_moduleOf[file].Body)
            .OfType<OpaqueStmt>()
            .Where(statement => statement.What.StartsWith("from ", StringComparison.Ordinal) && statement.What.EndsWith(StarImportEnd, StringComparison.Ordinal))
            .Select(statement => (FileFor(statement.What["from ".Length..^StarImportEnd.Length], file), statement.Span.Line))
            .ToList();
    }

    /// <summary>The dotted names of the modules a file imports whole, by import a.b.</summary>
    private IEnumerable<string> WholeImports(string file) =>
        IrWalk.Statements(_moduleOf[file].Body)
            .OfType<Assign>()
            .Select(assign => assign.Value)
            .OfType<Opaque>()
            .Where(opaque => opaque.What.StartsWith("module ", StringComparison.Ordinal))
            .Select(opaque => opaque.What["module ".Length..]);

    /// <summary>from package import name, as the reader writes it: the package with a dot for each level it climbs, and the name.</summary>
    private static (string Module, string Name)? FromImport(string what)
    {
        const string From = "from ", Import = " import ";
        if (!what.StartsWith(From, StringComparison.Ordinal)) return null;

        var at = what.IndexOf(Import, StringComparison.Ordinal);
        return at > From.Length ? (what[From.Length..at], what[(at + Import.Length)..]) : null;
    }

    /// <summary>
    /// Every way a module binds a name, anywhere in its top-level code: an assignment - def, class and import are
    /// assignments too - with its value, and each other way with none: a loop, a with, an except, a walrus, a function
    /// that names it global.
    /// </summary>
    private IReadOnlyList<Binding> Bindings(string file, string name)
    {
        if (_bindings.TryGetValue((file, name), out var known)) return known;

        var found = new List<Binding>();
        foreach (var statement in IrWalk.Statements(_moduleOf[file].Body))
        {
            var line = statement.Span.Line;
            switch (statement)
            {
                case Assign { Target: Name { Identifier: var bound }, Value: var value } when bound == name:
                    found.Add(new Binding(value, line));
                    break;
                case Assign assign when IrWalk.Binds(assign.Target, name):
                case ForEach loop when IrWalk.Binds(loop.Target, name):
                case Using { Variable: { } variable } when IrWalk.Binds(variable, name):
                case Try attempt when attempt.Handlers.Any(handler => handler.Variable == name):
                case OpaqueStmt opaque when opaque.MayAssign.Contains(name):
                    found.Add(new Binding(null, line));
                    break;
                case Declare { Initial: { } initial } declare when declare.Variable == name:
                    found.Add(new Binding(initial, line));
                    break;
            }

            if (IrWalk.Expressions(statement).SelectMany(IrWalk.Within).Any(inner => inner is AssignValue assigned && IrWalk.Binds(assigned.Target, name)))
                found.Add(new Binding(null, line));
        }

        found.AddRange(_functionsIn[file].Where(function => function.OuterNames.Contains(name)).Select(function => new Binding(null, function.Span.Line)));
        return _bindings[(file, name)] = found;
    }

    private static T? Single<T>(IEnumerable<T> candidates) where T : class
    {
        using var each = candidates.GetEnumerator();
        if (!each.MoveNext()) return null;
        var first = each.Current;
        return each.MoveNext() ? null : first;
    }
}
