using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Deadlock as a graph. Every lock the program takes is a node, and there is an edge from one lock to another wherever
/// the second is taken while the first is held - in one function, or in a function called while the first is held, with
/// the callee's parameters replaced by what each call passes it. A cycle, of two locks or of ten, is a set of places
/// that can leave a thread at each one holding the lock the next one is waiting for.
/// </summary>
/// <remarks>
/// <para>
/// A cycle only counts when threads could be at all of its places at once: no lock held at two of them - a lock around
/// both lets only one thread in at a time - and no more than one of them in the program's main code, which only ever
/// runs on one thread.
/// </para>
/// <para>
/// Taking a lock the thread already holds succeeds at once where the language's locks allow it, as Java's and C#'s
/// always do. Where they do not - a Python threading.Lock - the thread waits for itself for ever, which is a deadlock
/// with no second thread at all.
/// </para>
/// </remarks>
internal sealed class LockOrder(IrProgram program, ProgramNames names, IReadOnlySet<IrFunction> threadBodies)
{
    /// <summary>The most locks in a cycle looked for. Real deadlocks seldom involve more than four; this only bounds the search.</summary>
    private const int MostLocksInACycle = 12;

    /// <summary>How many steps the search for cycles may take, so a program with hundreds of locks cannot stall a check.</summary>
    private const int MostSearchSteps = 100_000;

    /// <summary>How many edges may be tried when choosing one for each step of a cycle.</summary>
    private const int MostChoices = 10_000;

    /// <summary>How much one function's summary keeps; a long chain of calls repeats the same locks many times over.</summary>
    private const int MostPerSummary = 256;

    private static readonly Summary NothingTaken = new([], []);

    private static readonly string[] CountWords = ["", "", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    private readonly Dictionary<IrFunction, Summary> _summaries = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IrFunction> _summarising = new(ReferenceEqualityComparer.Instance);
    private readonly List<Edge> _edges = [];
    private readonly List<AnalysisFinding> _findings = [];
    private readonly HashSet<(string File, int Line)> _reportedAt = [];
    private Dictionary<string, LockSort>? _pythonSorts;

    /// <summary>A lock the thread holds, where it took it, and inside how many branches and loops of the function.</summary>
    private sealed record Holding(SharedName Lock, SourceSpan TakenAt, int Branches);

    /// <summary>
    /// A lock a function takes, the locks it already holds by then, and where - in the function's own terms.
    /// <paramref name="Always"/> when every run of the function takes it: it sits in no branch or loop, after no return.
    /// </summary>
    private sealed record Taking(SharedName Lock, IReadOnlyList<Holding> Held, SourceSpan At, bool Always);

    /// <summary>
    /// A lock taken while another is held, with every lock the thread holds as it waits - what says whether two such
    /// places can be busy at the same time. <paramref name="Started"/> when it happens on a thread the code starts, which
    /// holds none of its starter's locks.
    /// </summary>
    private sealed record Edge(SharedName From, SharedName To, IReadOnlyList<SharedName> Guards, SourceSpan At, string How, IrFunction In, bool Started);

    /// <summary>What calling a function does with locks: the locks it takes, and its edges still waiting on what callers pass it.</summary>
    private sealed record Summary(IReadOnlyList<Taking> Takings, IReadOnlyList<Edge> Pending);

    /// <summary>One function being summarised: what it takes, its pending edges, and whether a return has been passed.</summary>
    private sealed class Scope(IrFunction function)
    {
        public IrFunction Function { get; } = function;

        public List<Taking> Takings { get; } = [];

        public List<Edge> Pending { get; } = [];

        public bool MayHaveReturned { get; set; }
    }

    /// <summary>Whether taking a lock the thread already holds is allowed - and whether, in Python, it is a lock at all.</summary>
    private enum LockSort { Reentrant, NotReentrant, Unknown, NotALock }

    /// <summary>synchronized and lock always lock; Python's with locks only when it names no variable - with lock:, not with open(f) as f:.</summary>
    public static bool IsLockRegion(Using used) =>
        used.Purpose == UsingPurpose.Lock || used.Purpose == UsingPurpose.Either && used.Variable is null;

    public IReadOnlyList<AnalysisFinding> Check()
    {
        var anyLocks = program.AllFunctions.Any(f => f.IsSynchronized || IrWalk.Statements(f.Body).Any(s => s is Using used && IsLockRegion(used)));
        if (!anyLocks) return [];

        foreach (var function in program.AllFunctions) Summarise(function);
        ReportCycles();
        return _findings;
    }

    private Summary Summarise(IrFunction function)
    {
        if (_summaries.TryGetValue(function, out var known)) return known;

        // A call back into a function still being summarised - recursion - adds nothing the first visit will not find.
        if (!_summarising.Add(function)) return NothingTaken;

        var scope = new Scope(function);
        IReadOnlyList<Holding> held = function.IsSynchronized && names.Targets.ClassOf(function) is { } owner
            ? [new Holding(function.IsStatic ? new SharedName($"{owner}.class", $"{owner}.class") : new SharedName($"{owner}.this", "this"), function.Span, 0)]
            : [];

        Visit(scope, function.Body, held, 0);

        _summarising.Remove(function);
        return _summaries[function] = new Summary(scope.Takings, scope.Pending);
    }

    private void Visit(Scope scope, IEnumerable<Stmt> block, IReadOnlyList<Holding> held, int branches)
    {
        foreach (var statement in block)
        {
            foreach (var (call, target, launch) in IrWalk.Expressions(statement).SelectMany(e => CallsIn(e, scope.Function)))
                Called(scope, call, target, launch, held, branches);

            if (statement is Using used && IsLockRegion(used) && LockIn(used, scope.Function) is { } taken)
            {
                IReadOnlyList<Holding> inside = held;

                if (!taken.IsKnown) inside = [.. held, new Holding(taken, used.Span, branches)];
                else if (Take(scope, taken, held, used.Span, branches)) inside = [.. held, new Holding(taken, used.Span, branches)];

                Visit(scope, used.Body, inside, branches);
            }
            else
            {
                foreach (var (inner, conditional) in Blocks(statement))
                    Visit(scope, inner, held, conditional ? branches + 1 : branches);
            }

            if (IrWalk.Statements([statement]).Any(s => s is Return or Throw)) scope.MayHaveReturned = true;
        }
    }

    /// <summary>The blocks a statement holds, each with whether it may not run every time the statement does.</summary>
    private static IEnumerable<(IReadOnlyList<Stmt> Block, bool Conditional)> Blocks(Stmt statement) => statement switch
    {
        If branch => [(branch.Then, true), (branch.Else, true)],
        While loop => [(loop.Body, true), (loop.Else, true)],
        For loop => [(loop.Setup, false), (loop.Body, true), (loop.Step, true)],
        ForEach loop => [(loop.Body, true), (loop.Else, true)],
        Try attempt => [(attempt.Body, false), .. attempt.Handlers.Select(h => (h.Body, true)), (attempt.Else, true), (attempt.Finally, false)],
        Switch choice => choice.Cases.Select(c => (c.Body, true)),
        Using used => [(used.Body, false)],
        Labeled labeled => [(labeled.Body, false)],
        _ => [],
    };

    /// <summary>
    /// A lock the function takes itself. Returns whether the thread holds one more lock after it - not so when it
    /// already held this one, which is either taken again at once or, for a lock that cannot be, the thread's end.
    /// </summary>
    private bool Take(Scope scope, SharedName taken, IReadOnlyList<Holding> held, SourceSpan at, int branches)
    {
        if (held.FirstOrDefault(h => h.Lock.Identity == taken.Identity) is { } already)
        {
            if (SortOf(taken) == LockSort.NotReentrant)
            {
                Reacquired(at, Confidence.Certain,
                    $"This takes `{taken.Shown}` again while this thread already holds it from {Places.Line(already.TakenAt, at)}, and a Lock cannot be taken " +
                    "twice by one thread - it waits for itself for ever. Make it an RLock if it has to be taken again");
            }

            return false;
        }

        if (scope.Takings.Count < MostPerSummary)
            scope.Takings.Add(new Taking(taken, held, at, branches == 0 && !scope.MayHaveReturned));

        foreach (var holding in held.Where(h => h.Lock.IsKnown))
        {
            Record(scope, new Edge(holding.Lock, taken, [.. held.Select(h => h.Lock)], at,
                $"takes `{taken.Shown}` while holding `{holding.Lock.Shown}`", scope.Function, Started: false));
        }

        return true;
    }

    /// <summary>
    /// A call to one of the program's own functions: every lock it takes is taken while the caller's are held, and its
    /// edges that waited on its parameters now know what they are. A thread started with a function runs it holding
    /// none of the starter's locks, and on a thread of its own.
    /// </summary>
    private void Called(Scope scope, Call call, CallTarget target, bool launch, IReadOnlyList<Holding> held, int branches)
    {
        var callee = Summarise(target.Function);

        if (!launch)
        {
            foreach (var taking in callee.Takings)
            {
                if (Translate(taking.Lock, call, target, scope.Function) is not { IsKnown: true } taken) continue;

                var alsoHeld = taking.Held.Select(h => h with { Lock = Translate(h.Lock, call, target, scope.Function) ?? SharedName.Unnamed, Branches = branches }).ToList();
                var always = branches == 0 && !scope.MayHaveReturned && taking.Always;

                if (held.FirstOrDefault(h => h.Lock.Identity == taken.Identity) is { } already)
                {
                    if (SortOf(taken) == LockSort.NotReentrant && taking.Always)
                    {
                        Reacquired(call.Span, Confidence.Likely,
                            $"This calls `{Quote(call)}` while holding `{already.Lock.Shown}`, and that call takes `{taken.Shown}` again at {Places.Line(taking.At, call.Span)} - " +
                            "a Lock cannot be taken twice by one thread, so it waits for itself for ever. Make it an RLock, or have the call run without taking it");
                    }

                    continue;
                }

                if (scope.Takings.Count < MostPerSummary)
                    scope.Takings.Add(new Taking(taken, [.. held, .. alsoHeld], taking.At, always));

                foreach (var holding in held.Where(h => h.Lock.IsKnown))
                {
                    Record(scope, new Edge(holding.Lock, taken, [.. held.Select(h => h.Lock), .. alsoHeld.Select(h => h.Lock)], call.Span,
                        $"calls `{Quote(call)}` while holding `{holding.Lock.Shown}`, and that call takes `{taken.Shown}` at {Places.Line(taking.At, call.Span)}",
                        scope.Function, Started: false));
                }
            }
        }

        foreach (var edge in callee.Pending)
        {
            if (Translate(edge.From, call, target, scope.Function) is not { IsKnown: true } from ||
                Translate(edge.To, call, target, scope.Function) is not { IsKnown: true } to ||
                from.Identity == to.Identity)
            {
                continue;
            }

            var onItsOwn = launch || edge.Started;
            IReadOnlyList<SharedName> guards = [.. (onItsOwn ? [] : held).Select(h => h.Lock), .. edge.Guards.Select(g => Translate(g, call, target, scope.Function) ?? SharedName.Unnamed)];
            var how = launch
                ? $"starts a thread running `{target.Function.Name}`, which takes `{to.Shown}` while holding `{from.Shown}` at {Places.Line(edge.At, call.Span)}"
                : $"calls `{Quote(call)}`, which takes `{to.Shown}` while holding `{from.Shown}` at {Places.Line(edge.At, call.Span)}";

            Record(scope, new Edge(from, to, guards, call.Span, how, launch ? target.Function : edge.Started ? edge.In : scope.Function, onItsOwn));
        }
    }

    /// <summary>An edge between two locks the whole program can name goes in the graph; one that still names a parameter waits for the callers.</summary>
    private void Record(Scope scope, Edge edge)
    {
        if (edge.From.IsConcrete && edge.To.IsConcrete) _edges.Add(edge);
        else if (scope.Pending.Count < MostPerSummary) scope.Pending.Add(edge);
    }

    /// <summary>
    /// The calls an expression makes to the program's own functions, and the threads it starts running one - Python's
    /// Thread(target=work, args=(a, b)) and pool.submit(work, a, b) are calls of work(a, b) made on another thread.
    /// </summary>
    private IEnumerable<(Call Call, CallTarget Target, bool Launch)> CallsIn(Expr expression, IrFunction function)
    {
        if (expression is Call call)
        {
            if (names.Targets.Resolve(call, function, names.LocalsOf(function)) is { } target) yield return (call, target, false);
            if (Started(call) is { } started && names.Targets.Resolve(started, function, names.LocalsOf(function)) is { } runs) yield return (started, runs, true);
        }

        foreach (var child in IrWalk.Children(expression))
            foreach (var inner in CallsIn(child, function)) yield return inner;
    }

    private Call? Started(Call call)
    {
        if (program.Language != SourceLanguage.Python) return null;

        switch (call)
        {
            case { CalleeName: "Thread" } when call.Arguments.FirstOrDefault(a => a.Name == "target") is { Value: Name or Member } target:
                IReadOnlyList<Expr> passed = call.Arguments.FirstOrDefault(a => a.Name == "args") is { Value: CollectionLiteral items } ? items.Items : [];
                return new Call(call.Span, target.Value, passed.Select(value => new Argument(null, value)).ToList());

            case { Callee: Member { MemberName: "submit" }, Arguments: [{ Name: null, Value: Name or Member } work, ..] }:
                return new Call(call.Span, work.Value, call.Arguments.Skip(1).Where(a => a.Name is null).ToList());

            default:
                return null;
        }
    }

    /// <summary>A lock the callee names by one of its parameters, as the call names it - or the callee's lock unchanged.</summary>
    private SharedName? Translate(SharedName lockName, Call call, CallTarget target, IrFunction caller)
    {
        if (lockName.IsConcrete || !lockName.IsKnown) return lockName;
        if (ArgumentFor(call, target, lockName.Parameter) is not { } argument || LockIn(argument, caller) is not { IsKnown: true } passed) return null;

        return new SharedName(passed.Identity + lockName.Path, passed.Shown + lockName.Path, passed.Parameter, passed.Path + lockName.Path);
    }

    private static Expr? ArgumentFor(Call call, CallTarget target, int parameterIndex)
    {
        if (parameterIndex < 0 || parameterIndex >= target.Function.Parameters.Count) return null;

        var parameter = target.Function.Parameters[parameterIndex];
        if (call.Arguments.FirstOrDefault(a => a.Name == parameter.Name) is { } named) return named.Value;
        if (parameter.Kind != ParameterKind.Normal) return null;

        var position = target.Bound ? parameterIndex - 1 : parameterIndex;
        var positional = call.Arguments.Where(a => a.Name is null).ToList();
        return position >= 0 && position < positional.Count ? positional[position].Value : null;
    }

    /// <summary>
    /// The lock a lock region takes, or null when it is not a lock anything else could hold: a Python with on something
    /// made on the spot, something made as a file or a connection rather than a lock, or a lock new to this call.
    /// </summary>
    private SharedName? LockIn(Using used, IrFunction function) =>
        used.Purpose == UsingPurpose.Either && used.Resource is Call ? null : LockIn(used.Resource, function);

    private SharedName? LockIn(Expr expression, IrFunction function)
    {
        var named = names.Named(expression, function);
        return named is { IsKnown: true, IsConcrete: true } && SortOf(named) == LockSort.NotALock ? null : named;
    }

    private LockSort SortOf(SharedName lockName)
    {
        if (!lockName.IsKnown || !lockName.IsConcrete) return LockSort.Unknown;
        if (program.Language != SourceLanguage.Python) return LockSort.Reentrant;

        _pythonSorts ??= PythonSorts();
        return _pythonSorts.GetValueOrDefault(lockName.Identity, LockSort.Unknown);
    }

    /// <summary>
    /// What each Python lock was made as. A Lock is taken once per thread; an RLock, and a Condition made without a lock
    /// of its own, which uses an RLock, can be taken again. Anything made some other way - a file, a connection, a
    /// Semaphore - is not treated as a lock here, however much a with around it looks like one.
    /// </summary>
    private Dictionary<string, LockSort> PythonSorts()
    {
        var made = new Dictionary<string, HashSet<LockSort>>(StringComparer.Ordinal);

        void Made(SharedName? named, Expr? value)
        {
            if (named is not { IsKnown: true, IsConcrete: true } || value is not Call creation) return;
            if (!made.TryGetValue(named.Identity, out var sorts)) made[named.Identity] = sorts = [];
            sorts.Add(SortMadeBy(creation));
        }

        foreach (var function in program.AllFunctions)
        {
            foreach (var statement in IrWalk.Statements(function.Body))
            {
                switch (statement)
                {
                    case Assign assign:
                        Made(names.Named(assign.Target, function), assign.Value);
                        break;
                    case Declare declare:
                        Made(names.Variable(declare.Variable, function, declare.Variable), declare.Initial);
                        break;
                }
            }
        }

        foreach (var type in program.Classes)
            foreach (var field in type.Fields)
                Made(new SharedName($"{type.Name}.{field.Name}", field.Name), field.Initial);

        return made.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Contains(LockSort.NotALock) ? LockSort.NotALock : entry.Value.Count == 1 ? entry.Value.Single() : LockSort.Unknown,
            StringComparer.Ordinal);
    }

    private static LockSort SortMadeBy(Call creation) => creation.CalleeName switch
    {
        "Lock" => LockSort.NotReentrant,
        "RLock" => LockSort.Reentrant,
        "Condition" when creation.Arguments.Count == 0 => LockSort.Reentrant,
        _ => LockSort.NotALock,
    };

    /// <summary>
    /// Every cycle in the graph, shortest first, and each edge on one that threads could be at together. An edge is
    /// reported once, for the shortest cycle it closes.
    /// </summary>
    private void ReportCycles()
    {
        var steps = _edges
            .Where(e => e.From.IsKnown && e.To.IsKnown && e.From.Identity != e.To.Identity)
            .GroupBy(e => (From: e.From.Identity, To: e.To.Identity))
            .ToDictionary(g => g.Key, g => g.ToList());

        var onward = steps.Keys
            .GroupBy(step => step.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(step => step.To).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        foreach (var cycle in Cycles(onward).OrderBy(c => c.Count))
        {
            var choices = cycle.Select((lockName, index) => steps[(lockName, cycle[(index + 1) % cycle.Count])]).ToList();

            for (var index = 0; index < choices.Count; index++)
            {
                foreach (var edge in choices[index])
                {
                    if (_reportedAt.Contains((edge.At.File, edge.At.Line)) || Together(choices, index, edge) is not { } chosen) continue;

                    _reportedAt.Add((edge.At.File, edge.At.Line));
                    ReportCycle(chosen, index);
                }
            }
        }
    }

    /// <summary>Every cycle of locks, each once: found from its first lock in name order, going only through locks after it.</summary>
    private static List<List<string>> Cycles(Dictionary<string, List<string>> onward)
    {
        var found = new List<List<string>>();
        var path = new List<string>();
        var onPath = new HashSet<string>(StringComparer.Ordinal);
        var stepsTaken = 0;

        void Extend(string start, string from)
        {
            if (++stepsTaken > MostSearchSteps || !onward.TryGetValue(from, out var nextLocks)) return;

            foreach (var next in nextLocks)
            {
                if (next == start)
                {
                    if (path.Count >= 2) found.Add([.. path]);
                    continue;
                }

                if (path.Count >= MostLocksInACycle || onPath.Contains(next) || string.CompareOrdinal(next, start) < 0) continue;

                path.Add(next);
                onPath.Add(next);
                Extend(start, next);
                path.RemoveAt(path.Count - 1);
                onPath.Remove(next);
            }
        }

        foreach (var start in onward.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            path.Add(start);
            onPath.Add(start);
            Extend(start, start);
            path.Clear();
            onPath.Clear();
        }

        return found;
    }

    /// <summary>
    /// One edge for each step of a cycle, the given edge among them, such that threads could be at all of them at once:
    /// no lock held at two of the places, and at most one place in main code. Null when no such choice exists.
    /// </summary>
    private List<Edge>? Together(IReadOnlyList<List<Edge>> choices, int fixedIndex, Edge fixedEdge)
    {
        var chosen = new Edge?[choices.Count];
        chosen[fixedIndex] = fixedEdge;
        var order = Enumerable.Range(0, choices.Count).Where(step => step != fixedIndex).ToList();
        var triesLeft = MostChoices;

        bool Choose(int position)
        {
            if (position == order.Count) return true;

            var step = order[position];
            foreach (var edge in choices[step])
            {
                if (--triesLeft < 0) return false;
                if (!FitsWith(edge, chosen)) continue;

                chosen[step] = edge;
                if (Choose(position + 1)) return true;
                chosen[step] = null;
            }

            return false;
        }

        return Choose(0) ? chosen.Select(edge => edge!).ToList() : null;
    }

    private bool FitsWith(Edge edge, IEnumerable<Edge?> chosen)
    {
        var guards = edge.Guards.Select(g => g.Identity).ToHashSet(StringComparer.Ordinal);
        var inMain = IsMainCode(edge.In) ? 1 : 0;

        foreach (var other in chosen.OfType<Edge>())
        {
            if (other.Guards.Any(g => guards.Contains(g.Identity))) return false;
            if (IsMainCode(other.In)) inMain++;
        }

        return inMain <= 1;
    }

    /// <summary>
    /// Code only the program's main thread runs: the module's own code, main, and a lambda written in main that no thread
    /// is started with - which runs on the main thread too. Two places in it are never busy at the same time.
    /// </summary>
    private bool IsMainCode(IrFunction function, int depth = 0)
    {
        if (function.Name == IrFunction.ModuleBody) return true;
        if (function is { IsStatic: true, Owner: not null, Name: "main" or "Main" } && program.Language is SourceLanguage.Java or SourceLanguage.CSharp) return true;

        return depth < ProgramNames.MostNesting && function.Name.StartsWith("lambda at line ", StringComparison.Ordinal) && !threadBodies.Contains(function) &&
            names.Targets.Enclosing(function) is { } outer && IsMainCode(outer, depth + 1);
    }

    private void ReportCycle(IReadOnlyList<Edge> cycle, int index)
    {
        var edge = cycle[index];
        var others = Enumerable.Range(1, cycle.Count - 1).Select(offset => cycle[(index + offset) % cycle.Count]).ToList();

        if (others.Count == 1)
        {
            var other = others[0];
            _findings.Add(new AnalysisFinding("analysis-lock-order", edge.At,
                $"This {edge.How}, but {Places.Line(other.At, edge.At)} {other.How} - two threads doing both can wait for each other for ever",
                Severity.Warning, Confidence.Likely, FindingKind.Logic, Concurrency.FoundBy));
            return;
        }

        var between = string.Join(", ", others.Take(others.Count - 1).Select(o => $"{Places.Line(o.At, edge.At)} {o.How}"));
        var last = others[^1];
        _findings.Add(new AnalysisFinding("analysis-lock-cycle", edge.At,
            $"This {edge.How}, {between}, and {Places.Line(last.At, edge.At)} {last.How} - {CountOf(cycle.Count)} threads, one at each of these places, " +
            "can each hold the lock the next one is waiting for, and then none of them can go on",
            Severity.Warning, Confidence.Likely, FindingKind.Logic, Concurrency.FoundBy));
    }

    private void Reacquired(SourceSpan at, Confidence confidence, string message)
    {
        if (!_reportedAt.Add((at.File, at.Line))) return;
        _findings.Add(new AnalysisFinding("analysis-lock-reacquired", at, message, Severity.Error, confidence, FindingKind.Logic, Concurrency.FoundBy));
    }

    private static string CountOf(int threads) => threads < CountWords.Length ? CountWords[threads] : threads.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private string Quote(Call call) => names.Shown(call);
}
