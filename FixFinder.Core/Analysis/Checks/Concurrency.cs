using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Concurrency, as the memory model sees it: the code that threads run, found from where the program starts them, and
/// what that code does to state other threads share. It finds updates two threads can lose, flags a thread may never see
/// change, locks taken in orders that go round in a circle (see <see cref="LockOrder"/>), wait and notify without the
/// lock they need, and run() called where start() was meant.
/// </summary>
public sealed class Concurrency(IrProgram program, SourceText source)
{
    public const string FoundBy = "concurrency analysis";

    /// <summary>Code a thread runs; <paramref name="Several"/> when more than one copy of it can run at once, sharing its state.</summary>
    private sealed record Body(IrFunction Function, bool Several, bool SharesInstance);

    private readonly List<AnalysisFinding> _findings = [];

    private bool IsPython => program.Language == SourceLanguage.Python;

    private string Quote(Expr expression) => source.Of(expression.Span) is { Length: > 0 } text ? text : IrText.Of(expression);

    private string Said(SourceSpan span) => source.Of(span) is { Length: > 0 } text ? text : "this";

    private void Report(string id, SourceSpan span, string message, Severity severity, Confidence confidence) =>
        _findings.Add(new AnalysisFinding(id, span, message, severity, confidence, FindingKind.Logic, FoundBy));

    public IReadOnlyList<AnalysisFinding> Check()
    {
        var bodies = Bodies();
        foreach (var body in bodies) LostUpdates(body);
        foreach (var body in bodies) StaleReads(body);
        _findings.AddRange(new LockOrder(program, source, new HashSet<IrFunction>(bodies.Select(b => b.Function), ReferenceEqualityComparer.Instance)).Check());
        var underLock = CalledUnderLock();
        foreach (var function in program.AllFunctions.Where(f => !underLock.Contains(f.FullName) && f.Name is not ("wait" or "notify" or "notifyAll")))
            WaitAndNotify(function);
        foreach (var function in program.AllFunctions) RunInsteadOfStart(function);
        return _findings;
    }

    /// <summary>A statement with how many loops it sits in and the locks held around it.</summary>
    private sealed record Place(Stmt Statement, int Loops, IReadOnlyList<string> Locks);

    /// <summary>Every statement of a function, with the loops and lock regions around it.</summary>
    private static IEnumerable<Place> Places(IrFunction function)
    {
        IEnumerable<Place> Walk(IEnumerable<Stmt> block, int loops, IReadOnlyList<string> locks)
        {
            foreach (var statement in block)
            {
                yield return new Place(statement, loops, locks);

                var (inner, innerLocks) = statement switch
                {
                    While loop => ((IEnumerable<Stmt>)loop.Body, locks),
                    For loop => (loop.Body.Concat(loop.Step), locks),
                    ForEach loop => (loop.Body, locks),
                    Using used when LockOrder.IsLockRegion(used) && LockKey(used.Resource) is { } key => (used.Body, (IReadOnlyList<string>)[.. locks, key]),
                    _ => (null, locks),
                };

                if (inner is not null)
                {
                    foreach (var place in Walk(inner, statement is While or For or ForEach ? loops + 1 : loops, innerLocks)) yield return place;
                    continue;
                }

                var children = statement switch
                {
                    If branch => branch.Then.Concat(branch.Else),
                    Try attempt => attempt.Body.Concat(attempt.Handlers.SelectMany(h => h.Body)).Concat(attempt.Else).Concat(attempt.Finally),
                    Switch choice => choice.Cases.SelectMany(c => c.Body),
                    Using used => used.Body,
                    Labeled labeled => labeled.Body,
                    _ => [],
                };

                foreach (var place in Walk(children, loops, locks)) yield return place;
            }
        }

        IReadOnlyList<string> held = function.IsSynchronized ? ["this"] : [];
        return Walk(function.Body, 0, held).ToList();
    }

    /// <summary>What a lock region locks: synchronized (x), lock (x), with lock - a variable, a field or this, never a new object.</summary>
    private static string? LockKey(Expr resource) => resource switch
    {
        Name name => name.Identifier,
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var field } => field,
        Member member => IrText.Of(member),
        _ => null,
    };

    private IReadOnlyList<Body> Bodies()
    {
        var launches = new List<(IrFunction Body, bool Several, string? Shared)>();

        foreach (var function in program.AllFunctions)
        {
            var places = Places(function);
            var created = places.Select(p => p.Statement).SelectMany(s => s switch
            {
                Declare { Initial: NewObject made } declare => [(declare.Variable, made.Type.Name)],
                Assign { Target: Name target, Value: NewObject made } => new[] { (target.Identifier, made.Type.Name) },
                _ => [],
            }).GroupBy(p => p.Item1).ToDictionary(g => g.Key, g => g.Last().Item2, StringComparer.Ordinal);

            foreach (var place in places)
            {
                foreach (var launch in IrWalk.Expressions(place.Statement).SelectMany(Launches))
                {
                    var (body, parallel, variable) = Resolve(launch, function, created);
                    if (body is null) continue;
                    launches.Add((body, parallel || place.Loops > 0, variable));
                }
            }
        }

        return launches.GroupBy(l => l.Body, ReferenceEqualityComparer.Instance).Select(g =>
        {
            var list = g.ToList();
            var several = list.Count > 1 || list.Any(l => l.Several);
            var shared = list.Where(l => l.Shared is not null and not Fresh).GroupBy(l => l.Shared).Any(same => same.Count() > 1 || same.Any(l => l.Several));
            return new Body((IrFunction)g.Key!, several, shared || list.Any(l => l.Shared is null));
        }).ToList();
    }

    /// <summary>The expressions that start a thread, each with what the thread runs.</summary>
    private IEnumerable<(Expr Runs, bool Parallel)> Launches(Expr expression)
    {
        switch (expression)
        {
            case NewObject { Type.Name: "Thread", Arguments: [var runs, ..] }:
                yield return (runs.Value, false);
                break;
            case Call { Callee: Member { Target: Name { Identifier: "Task" or "Factory" }, MemberName: "Run" or "StartNew" }, Arguments: [var runs, ..] }:
                yield return (runs.Value, false);
                break;
            case Call { Callee: Member { Target: Name { Identifier: "ThreadPool" }, MemberName: "QueueUserWorkItem" }, Arguments: [var runs, ..] }:
                yield return (runs.Value, false);
                break;
            case Call { Callee: Member { Target: Name { Identifier: "Parallel" }, MemberName: "For" or "ForEach" or "Invoke" } } parallel:
                foreach (var argument in parallel.Arguments) yield return (argument.Value, true);
                break;
            case Call { Callee: var callee } started when IsPython && callee is Name { Identifier: "Thread" } or Member { MemberName: "Thread" }:
                if (started.Arguments.FirstOrDefault(a => a.Name == "target") is { } target) yield return (target.Value, false);
                break;
            case Call { Callee: Member { MemberName: "submit" or "execute" or "map" or "runAsync" or "supplyAsync" } method, Arguments: [var runs, ..] }:
                yield return (runs.Value, method.MemberName == "map");
                break;
            case Opaque { What: "go", Parts: [Call { Callee: var goroutine }] }:
                yield return (goroutine, false);
                break;
        }

        var many = expression is Opaque { What: var what } && (what.Contains("comprehension", StringComparison.Ordinal) || what == "generator");
        foreach (var child in IrWalk.Children(expression))
            foreach (var inner in Launches(child)) yield return many ? inner with { Parallel = true } : inner;
    }

    /// <summary>
    /// The function a thread runs, whether it runs in parallel with itself, and the variable holding the object it runs
    /// on when that object is created in the same function - a Runnable given to two threads shares its fields.
    /// </summary>
    private (IrFunction? Body, bool Parallel, string? Variable) Resolve((Expr Runs, bool Parallel) launch, IrFunction function, Dictionary<string, string> created)
    {
        var (runs, parallel) = launch;

        switch (runs)
        {
            case Opaque { What: "lambda expression" } lambda:
                return (program.AllFunctions.FirstOrDefault(f => f.Span == lambda.Span), parallel, null);

            case Name { Identifier: var name } when created.TryGetValue(name, out var type):
                return (RunOf(type), parallel, name);

            case NewObject made:
                return (RunOf(made.Type.Name), parallel, Fresh);

            case Name { Identifier: var name }:
                var top = program.Functions.FirstOrDefault(f => f.Name == name && f.Owner is null);
                var method = function.Owner is { } within ? MethodsOf(within).FirstOrDefault(m => m.Name == name) : null;
                return (top ?? method, parallel, null);

            case Member { Target: Name { Identifier: "self" or "this" }, MemberName: var name } when function.Owner is { } owner:
                return (MethodsOf(owner).FirstOrDefault(m => m.Name == name), parallel, null);

            default:
                return (null, parallel, null);
        }
    }

    /// <summary>A launch that makes its own new object, which no other thread shares.</summary>
    private const string Fresh = "<a new object>";

    private IEnumerable<IrFunction> MethodsOf(string owner) => program.Classes.Where(c => c.Name == owner).SelectMany(c => c.Methods);

    private IrFunction? RunOf(string type) =>
        program.Classes.FirstOrDefault(c => c.Name == type && c.Bases.Any(b => b is "Runnable" or "Thread" or "Callable"))?.Methods
            .FirstOrDefault(m => m.Name is "run" or "call" && m.Parameters.Count == 0);

    private IrClass? ClassOf(IrFunction function) => function.Owner is { } owner ? program.Classes.FirstOrDefault(c => c.Name == owner) : null;

    /// <summary>
    /// Lost updates: x += 1 reads x, adds and writes it back, and another thread doing the same in between is lost. Only
    /// shared state counts - a field, a captured variable, a global - and only where more than one copy runs at once
    /// with no lock around the update.
    /// </summary>
    private void LostUpdates(Body body)
    {
        if (!body.Several || Locks(body.Function)) return;

        var type = ClassOf(body.Function);

        foreach (var place in Places(body.Function).Where(p => p.Locks.Count == 0))
        {
            if (Updated(place.Statement) is not { } target) continue;

            var shared = target switch
            {
                Name { Identifier: var name } when IsPython => body.Function.OuterNames.Contains(name),
                Name { Identifier: var name } when type?.Fields.FirstOrDefault(f => f.Name == name) is { } field =>
                    !Atomic(field) && (field.IsStatic || body.SharesInstance) && !body.Function.Parameters.Any(p => p.Name == name),
                Name { Identifier: var name } => body.Function.OuterNames.Contains(name),
                Member { Target: Name { Identifier: "self" or "this" }, MemberName: var attribute } =>
                    body.SharesInstance && !(type?.Fields.FirstOrDefault(f => f.Name == attribute) is { } declared && Atomic(declared)),
                _ => false,
            };

            if (!shared) continue;

            Report("analysis-lost-update", place.Statement.Span,
                $"`{Said(place.Statement.Span)}` runs on several threads at once with no lock, so two of them can read the same old value of `{IrText.Of(target)}` and one update is lost",
                Severity.Warning, Confidence.Likely);
            return;
        }
    }

    private static bool Atomic(IrField field) => field.Type.Name.StartsWith("Atomic", StringComparison.Ordinal) || field.Type.Name.StartsWith("Concurrent", StringComparison.Ordinal);

    /// <summary>Whether a function takes a lock some other way: lock(), acquire(), Interlocked, Monitor.</summary>
    private static bool Locks(IrFunction function) =>
        IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).Any(Locking);

    private static bool Locking(Expr expression) =>
        expression is Call { CalleeName: "lock" or "acquire" or "Enter" or "TryEnter" or "lockInterruptibly" or "Lock" or "RLock" } or
            Call { Callee: Member { Target: Name { Identifier: "Interlocked" or "atomic" } } } ||
        IrWalk.Children(expression).Any(Locking);

    /// <summary>The variable a statement reads, changes and writes back: x += 1, x = x + 1, x++.</summary>
    private static Expr? Updated(Stmt statement) => statement switch
    {
        Assign { Compound: not null, Target: Name or Member } update => update.Target,
        Assign { Target: var target, Value: Binary { Left: var read } } when Same(target, read) => target,
        Evaluate { Value: AssignValue { Target: var target, Value: Binary { Left: var read } } } when Same(target, read) => target,
        _ => null,
    };

    private static bool Same(Expr a, Expr b) => (a, b) switch
    {
        (Name x, Name y) => x.Identifier == y.Identifier,
        (Member x, Member y) => x.MemberName == y.MemberName && Same(x.Target, y.Target),
        _ => false,
    };

    /// <summary>
    /// Visibility in the Java and C# memory models: a thread that loops on a field another method sets, with nothing in
    /// the loop that synchronises, may keep reading its old value for ever unless the field is volatile.
    /// </summary>
    private void StaleReads(Body body)
    {
        if (IsPython || ClassOf(body.Function) is not { } type) return;

        foreach (var place in Places(body.Function).Where(p => p.Locks.Count == 0))
        {
            var (condition, inside) = place.Statement switch
            {
                While loop => (loop.Condition, loop.Body),
                For { Condition: { } test } loop => (test, loop.Body),
                _ => (null, []),
            };

            if (condition is null || IrWalk.Statements(inside).Any(s => s is Using || IrWalk.Expressions(s).Any(Calls))) continue;

            foreach (var name in IrWalk.Names(condition).Distinct())
            {
                if (type.Fields.FirstOrDefault(f => f.Name == name) is not { IsVolatile: false } field || Atomic(field)) continue;
                if (!type.Methods.Any(m => !ReferenceEquals(m, body.Function) && IrWalk.Statements(m.Body).Any(s => Assigns(s, name)))) continue;

                Report("analysis-stale-read", condition.Span,
                    $"`{name}` is not volatile and nothing in this loop synchronises, so the thread may keep seeing its old value and never stop - declare it volatile",
                    Severity.Warning, Confidence.Likely);
                return;
            }
        }
    }

    private static bool Calls(Expr expression) => expression is Call or NewObject || IrWalk.Children(expression).Any(Calls);

    private static bool Assigns(Stmt statement, string name) => statement switch
    {
        Assign { Target: Name { Identifier: var target } } => target == name,
        Assign { Target: Member { Target: Name { Identifier: "this" }, MemberName: var field } } => field == name,
        _ => false,
    };

    /// <summary>wait and notify need the lock of the object they are called on; wait belongs in a loop that checks the condition again.</summary>
    private void WaitAndNotify(IrFunction function)
    {
        if (program.Language == SourceLanguage.Python) return;

        foreach (var place in Places(function))
        {
            foreach (var call in IrWalk.Expressions(place.Statement).SelectMany(Calls2))
            {
                var (on, method) = call switch
                {
                    { Callee: Name { Identifier: var name } } when program.Language == SourceLanguage.Java => ("this", name),
                    { Callee: Member { Target: Name { Identifier: "this" }, MemberName: var name } } when program.Language == SourceLanguage.Java => ("this", name),
                    { Callee: Member { Target: Name { Identifier: "Monitor" }, MemberName: var name }, Arguments: [{ Value: var locked }, ..] } when LockKey(locked) is { } key => (key, name),
                    { Callee: Member { Target: var target, MemberName: var name } } when program.Language == SourceLanguage.Java && LockKey(target) is { } key => (key, name),
                    _ => (null, ""),
                };

                if (on is null || method is not ("wait" or "notify" or "notifyAll" or "Wait" or "Pulse" or "PulseAll")) continue;

                if (!place.Locks.Contains(on))
                {
                    var failure = program.Language == SourceLanguage.Java ? "an IllegalMonitorStateException" : "a SynchronizationLockException";
                    Report("analysis-wait-without-lock", call.Span,
                        $"`{Quote(call)}` needs the lock of `{on}`, and it is not held here, so it fails with {failure}", Severity.Error, Confidence.Certain);
                }
                else if (method is "wait" or "Wait" && place.Loops == 0)
                {
                    Report("analysis-wait-not-in-loop", call.Span,
                        $"`{Quote(call)}` can wake up before the condition it waits for is true, so check the condition again in a while loop around it",
                        Severity.Warning, Confidence.Likely);
                }
            }
        }
    }

    /// <summary>Methods called while their object's lock is held - from a synchronized method or block - so they hold it too.</summary>
    private HashSet<string> CalledUnderLock()
    {
        var called = new HashSet<string>(StringComparer.Ordinal);

        foreach (var function in program.AllFunctions.Where(f => f.Owner is not null))
            foreach (var place in Places(function).Where(p => p.Locks.Contains("this")))
                foreach (var call in IrWalk.Expressions(place.Statement).SelectMany(Calls2))
                    if (call.Callee switch { Name plain => plain.Identifier, Member { Target: Name { Identifier: "this" }, MemberName: var method } => method, _ => null } is { } name)
                        called.Add($"{function.Owner}.{name}");

        return called;
    }

    private static IEnumerable<Call> Calls2(Expr expression) =>
        (expression is Call call ? [call] : Enumerable.Empty<Call>()).Concat(IrWalk.Children(expression).SelectMany(Calls2));

    /// <summary>run() on a thread runs its code on the thread that calls it; start() is what runs it on the new one.</summary>
    private void RunInsteadOfStart(IrFunction function)
    {
        var threads = IrWalk.Statements(function.Body).SelectMany(s => s switch
        {
            Declare { Type.Name: "Thread" } declare => [declare.Variable],
            Declare { Initial: NewObject { Type.Name: "Thread" } } declare => [declare.Variable],
            Assign { Target: Name target, Value: NewObject { Type.Name: "Thread" } } => new[] { target.Identifier },
            Assign { Target: Name target, Value: Call { Callee: Name { Identifier: "Thread" } or Member { MemberName: "Thread" } } } when IsPython => new[] { target.Identifier },
            _ => [],
        }).ToHashSet(StringComparer.Ordinal);

        if (threads.Count == 0) return;

        foreach (var call in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(Calls2))
        {
            if (call is not { Callee: Member { Target: Name { Identifier: var thread }, MemberName: "run" }, Arguments.Count: 0 } || !threads.Contains(thread)) continue;

            Report("analysis-run-not-start", call.Span,
                $"`{Quote(call)}` runs the thread's code here, on this thread, and waits for it - `{thread}.start()` runs it on the new thread",
                Severity.Warning, Confidence.Likely);
        }
    }
}
