using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// What the analyses found in each function the last time they looked at it, so that checking again after an edit only
/// looks again at the functions the edit could have changed.
/// </summary>
/// <remarks>
/// A function's findings are reused only when everything they depend on is exactly as it was: the function's own lines
/// and where they are; every function it can call, found by name so that a call through any object still counts, and by
/// following its calls through what its module imports, whatever name the function was imported under; every function
/// those can call in turn; every line of every file that is not inside a function - imports, exports, typedefs,
/// fields, code at the top level; and the list of every function the program has, with what each declares global. Code at
/// the top level depends on every function in its file as well, since any of them may change its variables. Change any
/// of that and the function is
/// analysed again. When the readers' positions cannot be relied on - a function with no end, or two that overlap without
/// one being inside the other - every line of every file counts instead, so only an unchanged program is reused. The
/// checks that look across the whole program at once, and the summaries of what each function returns, are always worked
/// out again.
/// </remarks>
public sealed class AnalysisCache
{
    private readonly ConcurrentDictionary<string, CachedFunction> _functions = new(StringComparer.Ordinal);

    /// <summary>A function's findings, and whether following its paths symbolically had time to refine them.</summary>
    private sealed record CachedFunction(IReadOnlyList<AnalysisFinding> Findings, bool Refined);

    /// <summary>How many functions the last analysis took from here, and how many it analysed afresh.</summary>
    public (int Reused, int Analysed) LastRun { get; private set; }

    /// <summary>
    /// The findings kept under this key. One kept while symbolic execution had run out of time is not reused when there is
    /// time now, since analysing the function again could refine them.
    /// </summary>
    internal bool TryGet(string key, bool canRefine, out IReadOnlyList<AnalysisFinding> findings)
    {
        if (_functions.TryGetValue(key, out var cached) && (cached.Refined || !canRefine))
        {
            findings = cached.Findings;
            return true;
        }

        findings = [];
        return false;
    }

    internal void Store(string key, IReadOnlyList<AnalysisFinding> findings, bool refined) =>
        _functions[key] = new CachedFunction(findings, refined);

    internal void Finished(int reused, int analysed) => LastRun = (reused, analysed);

    /// <summary>The key each function's findings are kept under: a hash of everything the function's analysis depends on.</summary>
    internal static Dictionary<IrFunction, string> KeysFor(IrProgram program, SourceText source)
    {
        var functions = program.AllFunctions.ToList();
        var files = program.Files.Concat(functions.Select(f => f.Span.File)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        var ownLines = PositionsCanBeTrusted(functions) ? functions.Where(HasOwnLines).ToList() : [];

        var context = new StringBuilder("fixfinder-analysis-cache-1\n").Append(program.Language).Append('\n');
        foreach (var file in files) context.Append(file).Append('\n').Append(OutsideFunctions(file, ownLines, source)).Append('\n');
        foreach (var function in functions.OrderBy(f => f.FullName, StringComparer.Ordinal).ThenBy(f => f.Span.Line))
            context.Append(Signature(function)).Append('\n');
        var contextHash = Hash(context.ToString());

        var owning = new HashSet<IrFunction>(ownLines, ReferenceEqualityComparer.Instance);
        var reachable = new Reachable(functions, new CallTargets(program), program.Language == SourceLanguage.Python);
        var keys = new Dictionary<IrFunction, string>(ReferenceEqualityComparer.Instance);

        foreach (var function in functions)
        {
            // Code at the top level treats as unknown any variable a function in its file uses, so it depends on all of them.
            var depends = function.Name == IrFunction.ModuleBody
                ? functions.Where(f => string.Equals(f.Span.File, function.Span.File, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(reachable.From).Distinct(ReferenceEqualityComparer.Instance).Cast<IrFunction>()
                : reachable.From(function);

            var text = new StringBuilder(contextHash).Append('\n');
            foreach (var depended in depends.OrderBy(f => f.Span.File, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Span.Line))
            {
                text.Append(depended.FullName).Append('@').Append(depended.Span.File).Append(':')
                    .Append(depended.Span.Line).Append('-').Append(depended.Span.EndLine).Append('\n');
                if (owning.Contains(depended)) text.Append(LinesOf(depended, source)).Append('\n');
            }

            keys[function] = Hash(text.ToString());
        }

        return keys;
    }

    /// <summary>A function with lines of its own to stand for it; top-level code is the lines outside every function.</summary>
    private static bool HasOwnLines(IrFunction function) => function.Name != IrFunction.ModuleBody;

    /// <summary>
    /// Whether every function says where it starts and ends, and no two overlap unless one is written inside the other.
    /// Only then can a line be said to belong to one function and to nothing else.
    /// </summary>
    private static bool PositionsCanBeTrusted(IReadOnlyList<IrFunction> functions)
    {
        var owning = functions.Where(HasOwnLines).ToList();
        if (owning.Any(f => f.Span.Line < 1 || f.Span.EndLine < f.Span.Line)) return false;

        foreach (var file in owning.GroupBy(f => f.Span.File, StringComparer.OrdinalIgnoreCase))
        {
            var spans = file.ToList();
            for (var first = 0; first < spans.Count; first++)
            {
                for (var second = first + 1; second < spans.Count; second++)
                {
                    var (a, b) = (spans[first].Span, spans[second].Span);
                    var overlap = a.Line <= b.EndLine && b.Line <= a.EndLine;
                    var nested = a.Line <= b.Line && b.EndLine <= a.EndLine || b.Line <= a.Line && a.EndLine <= b.EndLine;
                    if (overlap && !nested) return false;
                }
            }
        }

        return true;
    }

    /// <summary>The file with every line that belongs to a function left blank, so its lines keep their numbers.</summary>
    private static string OutsideFunctions(string file, IReadOnlyList<IrFunction> ownLines, SourceText source)
    {
        var lines = source.Lines(file).ToArray();
        foreach (var function in ownLines.Where(f => string.Equals(f.Span.File, file, StringComparison.OrdinalIgnoreCase)))
        {
            for (var number = function.Span.Line; number <= function.Span.EndLine && number <= lines.Length; number++)
                lines[number - 1] = "";
        }

        return string.Join('\n', lines);
    }

    private static string LinesOf(IrFunction function, SourceText source)
    {
        var lines = source.Lines(function.Span.File);
        var from = Math.Max(function.Span.Line, 1);
        var to = Math.Min(function.Span.EndLine, lines.Count);
        return to < from ? "" : string.Join('\n', lines.Skip(from - 1).Take(to - from + 1));
    }

    /// <summary>What calling code can see of a function without reading its body.</summary>
    private static string Signature(IrFunction function) =>
        $"{function.FullName}({string.Join(", ", function.Parameters.Select(p => $"{p.Name}: {p.Type} {p.Kind}"))}) -> {function.ReturnType} " +
        $"static={function.IsStatic} constructor={function.IsConstructor} async={function.IsAsync} generator={function.IsGenerator} " +
        $"synchronized={function.IsSynchronized} decorated={function.IsDecorated} in={function.EnclosedBy} " +
        $"outer={string.Join(",", function.OuterNames)} address={string.Join(",", function.AddressTaken)}";

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// The functions whose code one function's analysis can depend on: itself, the functions around it, and every function
    /// it names - called, passed or constructed - found by name alone and followed on to what those name in turn.
    /// </summary>
    private sealed class Reachable
    {
        private readonly Dictionary<string, List<IrFunction>> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IrFunction> _byFullName = new(StringComparer.Ordinal);
        private readonly Dictionary<IrFunction, HashSet<string>> _named = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IrFunction, List<IrFunction>> _called = new(ReferenceEqualityComparer.Instance);

        public Reachable(IReadOnlyList<IrFunction> functions, CallTargets targets, bool isPython)
        {
            foreach (var function in functions)
            {
                _byFullName.TryAdd(function.FullName, function);
                Add(function.Name, function);
                if (function.Owner is { } owner) Add(owner[(owner.LastIndexOf('.') + 1)..], function);
                _named[function] = NamesIn(function);
                _called[function] = Called(function, targets, isPython);
            }
        }

        /// <summary>
        /// The functions a function's calls are found to run. A name alone misses one imported under a name of its own -
        /// import { total as sum } - or exported as default, whose code the analysis still depends on.
        /// </summary>
        private static List<IrFunction> Called(IrFunction function, CallTargets targets, bool isPython)
        {
            var locals = IrWalk.LocalNames(function, assigningDeclares: isPython);
            return IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(Effects.CallsIn)
                .Select(call => targets.Resolve(call, function, locals)?.Function)
                .OfType<IrFunction>()
                .ToList();
        }

        private void Add(string name, IrFunction function)
        {
            if (!_byName.TryGetValue(name, out var list)) _byName[name] = list = [];
            list.Add(function);
        }

        public IReadOnlyCollection<IrFunction> From(IrFunction start)
        {
            var found = new HashSet<IrFunction>(ReferenceEqualityComparer.Instance) { start };
            var pending = new Queue<IrFunction>([start]);

            while (pending.Count > 0)
            {
                var function = pending.Dequeue();

                // A function written inside another shares its variables. Code at the top level is in every key already,
                // so it is not followed from here - its calls have nothing to do with this function.
                if (function.EnclosedBy is { } enclosing && _byFullName.TryGetValue(enclosing, out var outer) && HasOwnLines(outer) && found.Add(outer))
                    pending.Enqueue(outer);

                foreach (var name in _named.GetValueOrDefault(function) ?? [])
                {
                    foreach (var named in _byName.GetValueOrDefault(name) ?? [])
                    {
                        if (found.Add(named)) pending.Enqueue(named);
                    }
                }

                foreach (var called in _called.GetValueOrDefault(function) ?? [])
                {
                    if (found.Add(called)) pending.Enqueue(called);
                }
            }

            return found;
        }

        /// <summary>Every name a function's code mentions: variables, members, and the types it constructs.</summary>
        private static HashSet<string> NamesIn(IrFunction function)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            void Visit(Expr expression)
            {
                switch (expression)
                {
                    case Name name:
                        names.Add(name.Identifier);
                        break;
                    case Member member:
                        names.Add(member.MemberName);
                        break;
                    case NewObject made:
                        names.Add(made.Type.Name);
                        break;
                }

                foreach (var child in IrWalk.Children(expression)) Visit(child);
            }

            foreach (var statement in IrWalk.Statements(function.Body))
            {
                foreach (var expression in IrWalk.Expressions(statement)) Visit(expression);
                if (statement is Declare { Type.Name: var type }) names.Add(type);
            }

            return names;
        }
    }
}
