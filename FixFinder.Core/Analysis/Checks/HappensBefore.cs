using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// A thread a function starts: the code it runs, the object that code runs on, what its parameters are given, where the
/// function starts it and where it waits for it, and whether several copies of it run at once.
/// </summary>
/// <param name="Receiver">The program-wide name of the object the code runs on, or null when each thread has its own.</param>
/// <param name="Arguments">What the thread's code is given: Python's Thread(target=work, args=(a, b)) gives work a and b.</param>
/// <param name="Bound">Whether the code is a method whose first parameter, self, is the receiver rather than an argument.</param>
/// <param name="Started">The position of the statement that starts it, in the order of <see cref="HappensBefore"/>.</param>
/// <param name="Joined">The position of the first statement after that which waits for it to finish, or null when none does.</param>
/// <param name="JoinedBy">What waits for it - t.join(), task.Wait() - or null for the end of the block that closes its pool.</param>
/// <param name="Followed">Whether every use of the thread's handle is in the function, so it cannot be joined anywhere else.</param>
internal sealed record ThreadStart(
    IrFunction Body,
    IrFunction Launcher,
    string? Receiver,
    IReadOnlyList<Argument> Arguments,
    bool Bound,
    int Started,
    SourceSpan StartedAt,
    int? Joined,
    SourceSpan? JoinedAt,
    Expr? JoinedBy,
    bool Several,
    bool Followed);

/// <summary>
/// Happens-before between a function and the threads it starts, built from the operations that order them. Each
/// statement of the function happens before the next; start() happens before everything the thread does; everything the
/// thread does happens before the join() that waits for it. Two things neither of which happens before the other can
/// happen at the same time.
/// </summary>
/// <remarks>
/// Statements are numbered in the order they are written, a nested statement after the one it is in. Only a join with
/// no time limit orders anything - join(1.0) can return with the thread still running - and a thread whose handle leaves
/// the function, returned or passed on, may be joined anywhere, so nothing is claimed about when it ends.
/// </remarks>
internal sealed class HappensBefore
{
    private readonly Dictionary<Stmt, int> _positions = new(ReferenceEqualityComparer.Instance);

    private HappensBefore(IrFunction launcher) => Launcher = launcher;

    public IrFunction Launcher { get; }

    public IReadOnlyList<ThreadStart> Threads { get; private set; } = [];

    /// <summary>A statement's place in the function's order, or -1 for one that is not the function's own.</summary>
    public int PositionOf(Stmt statement) => _positions.GetValueOrDefault(statement, -1);

    /// <summary>Whether the statement at a position happens before everything the thread does.</summary>
    public static bool Before(int position, ThreadStart thread) => position < thread.Started;

    /// <summary>Whether everything the thread does happens before the statement at a position.</summary>
    public static bool Before(ThreadStart thread, int position) => thread.Joined < position;

    /// <summary>Whether everything one thread does happens before everything another does: the first is joined before the second starts.</summary>
    public static bool Before(ThreadStart first, ThreadStart second) =>
        ReferenceEquals(first.Launcher, second.Launcher) && first.Followed && second.Followed && first.Joined < second.Started;

    /// <summary>Whether the statement at a position may run while the thread does: after it is started, before it is joined.</summary>
    public static bool MayOverlap(ThreadStart thread, int position) =>
        thread.Followed && !Before(position, thread) && !Before(thread, position);

    /// <summary>Whether two threads may run at once - one thread with itself only when several copies of it run.</summary>
    public static bool MayOverlap(ThreadStart first, ThreadStart second) =>
        ReferenceEquals(first, second) ? first.Several : !Before(first, second) && !Before(second, first);

    /// <summary>The threads a function starts, in the order the relation needs; null when it starts none.</summary>
    public static HappensBefore? Of(IrFunction launcher, ProgramNames names)
    {
        var relation = new HappensBefore(launcher);
        var discovery = new Discovery(relation, names);
        discovery.Walk(launcher.Body, []);

        relation.Threads = discovery.Threads();
        return relation.Threads.Count == 0 ? null : relation;
    }

    /// <summary>How an expression that makes a thread starts it.</summary>
    private enum Starting
    {
        /// <summary>new Thread(...) and threading.Thread(...): not running until start() is called on it.</summary>
        WhenStarted,

        /// <summary>Task.Run, an executor's submit: running from the moment it is made.</summary>
        AtOnce,

        /// <summary>Parallel.For: running from the moment it is made, and finished when the call returns.</summary>
        AndWaitedFor,
    }

    /// <summary>One expression that makes a thread, the code the thread runs, and where it is in the function.</summary>
    private sealed record Made(Expr Expression, IrFunction Body, string? Receiver, IReadOnlyList<Argument> Arguments, bool Bound, Starting Starting,
        bool Several, int Position, SourceSpan At, IReadOnlyList<(int First, int Last)> Loops)
    {
        /// <summary>Whether the thread is an object of the program's own Thread class, whose run() works on that same object.</summary>
        public bool RunsOnItself { get; init; }
    }

    /// <summary>
    /// A variable holding threads - one, or a list or array of them - with what is done to it: started, joined, or
    /// handed to code elsewhere, which could do anything with it.
    /// </summary>
    private sealed class Handle
    {
        public List<Made> Threads { get; } = [];

        public List<(int Position, SourceSpan At)> Starts { get; } = [];

        public List<(int Position, SourceSpan At, Expr? By)> Joins { get; } = [];

        public bool Escaped { get; set; }
    }

    /// <summary>Reads a function once, in order, finding the threads it makes and what it does with them.</summary>
    private sealed class Discovery(HappensBefore relation, ProgramNames names)
    {
        private readonly IrFunction _launcher = relation.Launcher;
        private readonly Dictionary<string, Handle> _handles = new(StringComparer.Ordinal);
        private readonly List<(Made Thread, Handle? Handle)> _made = [];
        private readonly List<int> _startedInPlace = [];
        private int _next = 2;

        private SourceLanguage Language => names.Program.Language;

        public void Walk(IEnumerable<Stmt> block, IReadOnlyList<(int First, int Last)> loops)
        {
            foreach (var statement in block)
            {
                var position = _next;
                _next += 2;
                relation._positions[statement] = position;

                if (statement is ForEach { Target: Name { Identifier: var each }, Items: var items } && HandleOf(items) is { } walked)
                    _handles[each] = walked;

                Made(statement, position, loops);
                Events(statement, position);

                var loopStart = _next;
                var inLoop = statement is While or For or ForEach;
                foreach (var inner in Children(statement))
                    Walk(inner, inLoop ? [.. loops, (loopStart, int.MaxValue)] : loops);

                if (inLoop) Close(loops.Count, loopStart);

                if (statement is Using { Variable: Name { Identifier: var pool } } && _handles.TryGetValue(pool, out var waited))
                    waited.Joins.Add((_next - 1, statement.Span, null));
            }
        }

        /// <summary>Records where a loop ends, now that its body has been numbered.</summary>
        private void Close(int depth, int loopStart)
        {
            for (var index = 0; index < _made.Count; index++)
            {
                var (made, handle) = _made[index];
                if (made.Loops.Count <= depth || made.Loops[depth].First != loopStart) continue;

                var loops = made.Loops.ToList();
                loops[depth] = (loopStart, _next - 1);
                _made[index] = (made with { Loops = loops }, handle);
            }
        }

        private static IEnumerable<IReadOnlyList<Stmt>> Children(Stmt statement) => statement switch
        {
            If branch => [branch.Then, branch.Else],
            While loop => [loop.Body, loop.Else],
            For loop => [loop.Setup, loop.Body, loop.Step],
            ForEach loop => [loop.Body, loop.Else],
            Try attempt => [attempt.Body, .. attempt.Handlers.Select(h => h.Body), attempt.Else, attempt.Finally],
            Switch choice => choice.Cases.Select(c => c.Body),
            Using used => [used.Body],
            Labeled labeled => [labeled.Body],
            _ => [],
        };

        /// <summary>The threads a statement makes, and the variable - if any - that keeps hold of them.</summary>
        private void Made(Stmt statement, int position, IReadOnlyList<(int First, int Last)> loops)
        {
            foreach (var expression in IrWalk.Expressions(statement))
            {
                foreach (var (made, many) in ThreadsMadeIn(expression, many: false))
                {
                    if (Resolve(made, many, position, statement.Span, loops) is not { } thread) continue;

                    var holder = HolderOf(statement, made);
                    var handle = holder is not null ? HandleNamed(holder) : null;

                    // w = Worker(): what run() does to self is done to w - unless each time round a loop makes a different w.
                    if (thread.RunsOnItself && holder is not null && !many && loops.Count == 0)
                        thread = thread with { Receiver = names.PlaceOf(new Name(statement.Span, holder), _launcher, names.ThisOf(_launcher))?.Identity };
                    handle?.Threads.Add(thread);
                    _made.Add((thread, handle));

                    if (statement is Evaluate { Value: Call { Callee: Member { Target: var started, MemberName: "start" or "Start" }, Arguments.Count: 0 } } &&
                        ReferenceEquals(started, made))
                    {
                        _startedInPlace.Add(_made.Count - 1);
                    }
                }
            }
        }

        private Handle HandleNamed(string name)
        {
            if (!_handles.TryGetValue(name, out var handle)) _handles[name] = handle = new Handle();
            return handle;
        }

        /// <summary>
        /// The variable a statement puts a new thread in: t = Thread(...), threads[i] = new Thread(...), a list made of
        /// them, or a list the thread is added to.
        /// </summary>
        private static string? HolderOf(Stmt statement, Expr made) => statement switch
        {
            Declare { Variable: var name, Initial: { } value } when Holds(value, made) => name,
            Assign { Target: Name { Identifier: var name }, Value: var value } when Holds(value, made) => name,
            Assign { Target: ElementAccess { Target: Name { Identifier: var name } }, Value: var value } when Holds(value, made) => name,
            Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: var list }, MemberName: "add" or "append" or "Add" }, Arguments: [{ Value: var value }] } }
                when Holds(value, made) => list,
            _ => null,
        };

        /// <summary>Whether a value is the new thread itself, or a list or comprehension of new threads made from it.</summary>
        private static bool Holds(Expr value, Expr made) => ReferenceEquals(value, made) || value switch
        {
            CollectionLiteral items => items.Items.Any(item => Holds(item, made)),
            Opaque { What: var what } opaque when what.Contains("comprehension", StringComparison.Ordinal) => opaque.Parts.Any(part => Holds(part, made)),
            _ => false,
        };

        /// <summary>The expressions that make threads, and whether each is made once or many times - in a comprehension.</summary>
        private IEnumerable<(Expr Made, bool Many)> ThreadsMadeIn(Expr expression, bool many)
        {
            if (Starts(expression) is not null) yield return (expression, many);

            var inside = many || expression is Opaque { What: var what } && (what.Contains("comprehension", StringComparison.Ordinal) || what == "generator");
            foreach (var child in IrWalk.Children(expression))
                foreach (var found in ThreadsMadeIn(child, inside)) yield return found;
        }

        /// <summary>How an expression starts a thread, or null when it makes none.</summary>
        private Starting? Starts(Expr expression) => expression switch
        {
            NewObject { Type.Name: "Thread", Arguments.Count: > 0 } when Language is SourceLanguage.Java or SourceLanguage.CSharp => Starting.WhenStarted,
            NewObject made when ThreadClass(made.Type.Name) is not null => Starting.WhenStarted,
            Call { Callee: Name { Identifier: var type } } when Language == SourceLanguage.Python && ThreadClass(type) is not null => Starting.WhenStarted,
            Call { CalleeName: "Thread" } call when Language == SourceLanguage.Python && call.Arguments.Any(a => a.Name == "target") => Starting.WhenStarted,
            Call { Callee: Member { Target: Name { Identifier: "Task" } or Member { MemberName: "Factory" }, MemberName: "Run" or "StartNew" }, Arguments.Count: > 0 } => Starting.AtOnce,
            Call { Callee: Member { Target: Name { Identifier: "ThreadPool" }, MemberName: "QueueUserWorkItem" }, Arguments.Count: > 0 } => Starting.AtOnce,
            Call { Callee: Member { Target: Name { Identifier: "CompletableFuture" }, MemberName: "runAsync" or "supplyAsync" }, Arguments.Count: > 0 } => Starting.AtOnce,
            Call { Callee: Member { MemberName: "submit" or "execute" }, Arguments.Count: > 0 } when Language is SourceLanguage.Java or SourceLanguage.Python => Starting.AtOnce,
            Call { Callee: Member { Target: Name { Identifier: "Parallel" }, MemberName: "For" or "ForEach" or "Invoke" } } => Starting.AndWaitedFor,
            _ => null,
        };

        /// <summary>A class of the program's own that is a thread - class Worker extends Thread, class Worker(threading.Thread).</summary>
        private IrClass? ThreadClass(string name) =>
            names.Program.Classes.FirstOrDefault(c => c.Name == name && c.Bases.Any(b => b is "Thread" or "threading.Thread"));

        /// <summary>The code a new thread runs and the object it runs on, or null when that cannot be told.</summary>
        private Made? Resolve(Expr made, bool many, int position, SourceSpan at, IReadOnlyList<(int First, int Last)> loops)
        {
            var starting = Starts(made)!.Value;
            var several = many || starting == Starting.AndWaitedFor;

            switch (made)
            {
                // A thread of a class of the program's own runs on itself: its run() uses the fields of the object that is the thread.
                case NewObject created when ThreadClass(created.Type.Name) is { } type && names.RunOf(type.Name) is { } run:
                    return new Made(made, run, null, [], Bound: false, starting, several, position, at, loops) { RunsOnItself = true };

                case Call { Callee: Name { Identifier: var name } } when Language == SourceLanguage.Python && ThreadClass(name) is { } type && names.RunOf(type.Name) is { } run:
                    return new Made(made, run, null, [], Bound: true, starting, several, position, at, loops) { RunsOnItself = true };

                case Call { CalleeName: "Thread" } python when Language == SourceLanguage.Python:
                    var target = python.Arguments.First(a => a.Name == "target").Value;
                    IReadOnlyList<Argument> passed = python.Arguments.FirstOrDefault(a => a.Name == "args") is { Value: CollectionLiteral given }
                        ? given.Items.Select(item => new Argument(null, item)).ToList()
                        : [];
                    return Code(target, passed, made, starting, several, position, at, loops);

                case Call { Callee: Member { MemberName: "submit" or "execute" } } submitted when Language == SourceLanguage.Python:
                    return Code(submitted.Arguments[0].Value, submitted.Arguments.Skip(1).Where(a => a.Name is null).ToList(), made, starting, several, position, at, loops);

                case Call { Callee: Member { Target: Name { Identifier: "Parallel" } } } parallel:
                    // Parallel.For(0, n, body): the code is the argument that is a lambda or a method.
                    var code = parallel.Arguments.Select(a => a.Value).LastOrDefault(v => v is Opaque { What: "lambda expression" } or Name);
                    return code is null ? null : Code(code, [], made, starting, several, position, at, loops);

                case Call call:
                    return Code(call.Arguments[0].Value, [], made, starting, several, position, at, loops);

                case NewObject created:
                    return Code(created.Arguments[0].Value, [], made, starting, several, position, at, loops);

                default:
                    return null;
            }
        }

        /// <summary>The function a thread is given - a lambda, a Runnable, a method or function by name - and the object it runs on.</summary>
        private Made? Code(Expr code, IReadOnlyList<Argument> passed, Expr made, Starting starting, bool several, int position, SourceSpan at,
            IReadOnlyList<(int First, int Last)> loops)
        {
            var self = names.ThisOf(_launcher);

            switch (code)
            {
                case Opaque { What: "lambda expression" } lambda:
                    return names.Program.AllFunctions.FirstOrDefault(f => f.Span == lambda.Span) is { } body
                        ? new Made(made, body, self, passed, Bound: false, starting, several, position, at, loops)
                        : null;

                case NewObject created when names.RunOf(created.Type.Name) is { } run:
                    return new Made(made, run, null, passed, Bound: false, starting, several, position, at, loops);

                case Name { Identifier: var name } when names.ClassOfValue(code, _launcher) is { } type && names.RunOf(type.Name) is { } run:
                    // A Runnable in a variable: threads given the same one share its fields - unless it is made anew each time round the loop.
                    var shared = MadeInLoop(name, loops) ? null : names.PlaceOf(code, _launcher, self)?.Identity;
                    return new Made(made, run, shared, passed, Bound: false, starting, several, position, at, loops);

                case Name or Member { Target: Name { Identifier: "self" or "this" } }:
                    return names.Targets.Resolve(new Call(code.Span, code, passed), _launcher, names.LocalsOf(_launcher)) is { } target
                        ? new Made(made, target.Function, target.Function.IsStatic ? null : self, passed, target.Bound, starting, several, position, at, loops)
                        : null;

                default:
                    return null;
            }
        }

        /// <summary>Whether a variable is given a new object inside the innermost loop the thread is made in.</summary>
        private bool MadeInLoop(string name, IReadOnlyList<(int First, int Last)> loops)
        {
            if (loops.Count == 0) return false;

            var (first, _) = loops[^1];
            return relation._positions.Any(entry => entry.Value >= first && entry.Key switch
            {
                Declare { Variable: var declared, Initial: NewObject or Call } => declared == name,
                Assign { Target: Name { Identifier: var assigned }, Value: NewObject or Call } => assigned == name,
                _ => false,
            });
        }

        /// <summary>What a statement does to the threads already made: starts them, waits for them, or hands them on.</summary>
        private void Events(Stmt statement, int position)
        {
            switch (statement)
            {
                // Thread other = first; - a second name for the same thread, not a way out of the function.
                case Declare { Variable: var alias, Initial: Name copied } when HandleOf(copied) is { } same:
                    _handles[alias] = same;
                    return;
                case Assign { Target: Name { Identifier: var alias }, Value: Name copied } when HandleOf(copied) is { } same:
                    _handles[alias] = same;
                    return;
            }

            foreach (var expression in IrWalk.Expressions(statement)) Event(expression, position, statement);

            switch (statement)
            {
                case Return { Value: { } returned }:
                    foreach (var name in IrWalk.Names(returned)) Escape(name);
                    break;
                case Assign { Target: not Name, Value: var stored } when stored is Name:
                    foreach (var name in IrWalk.Names(stored)) Escape(name);
                    break;
            }
        }

        private void Event(Expr expression, int position, Stmt statement)
        {
            switch (expression)
            {
                case Call { Callee: Member { Target: var thread, MemberName: "start" or "Start" }, Arguments.Count: 0 } start when HandleOf(thread) is { } started:
                    started.Starts.Add((position, start.Span));
                    return;

                case Call { Callee: Member { Target: var thread, MemberName: "join" or "Join" or "Wait" or "get" or "result" }, Arguments.Count: 0 } call
                    when HandleOf(thread) is { } joined:
                    joined.Joins.Add((position, call.Span, call));
                    return;

                case Member { Target: var task, MemberName: "Result" } result when Language == SourceLanguage.CSharp && HandleOf(task) is { } finished:
                    finished.Joins.Add((position, result.Span, result));
                    return;

                case Opaque { What: "await", Parts: [var awaited] } await:
                    foreach (var handle in Awaited(awaited)) handle.Joins.Add((position, await.Span, await));
                    return;

                case Call { Callee: Member { Target: Name { Identifier: "Task" }, MemberName: "WaitAll" } } all:
                    foreach (var handle in all.Arguments.Select(a => HandleOf(a.Value)).OfType<Handle>()) handle.Joins.Add((position, all.Span, all));
                    return;

                case Call { Callee: Member { Target: Name { Identifier: var pool }, MemberName: "awaitTermination" or "close" or "invokeAll" } } call
                    when _handles.TryGetValue(pool, out var waited):
                    waited.Joins.Add((position, call.Span, call));
                    return;

                case Call { Callee: Member { Target: Name { Identifier: var list }, MemberName: "add" or "append" or "Add" }, Arguments: [{ Value: var added }] }
                    when HandleOf(added) is { } joining:
                    // The list now holds the same threads: starting or joining everything in it starts or joins them.
                    _handles[list] = Merge(_handles.GetValueOrDefault(list), joining);
                    return;

                case Call { Callee: Member { Target: Name { Identifier: var pool }, MemberName: "submit" or "execute" } } submitted
                    when Language is SourceLanguage.Java or SourceLanguage.Python:
                    // Everything submitted to a pool is waited for when the pool is: pool.awaitTermination(), or the end of with ... as pool.
                    var pooled = HandleNamed(pool);
                    foreach (var (made, _) in _made.Where(m => ReferenceEquals(m.Thread.Expression, submitted)).ToList())
                        if (!pooled.Threads.Contains(made)) pooled.Threads.Add(made);
                    break;

                case Call call:
                    foreach (var argument in call.Arguments)
                        foreach (var name in IrWalk.Names(argument.Value)) Escape(name);
                    break;
            }

            foreach (var child in IrWalk.Children(expression)) Event(child, position, statement);
        }

        /// <summary>The threads an await waits for: one task, or all of those given to Task.WhenAll.</summary>
        private IEnumerable<Handle> Awaited(Expr awaited) => awaited switch
        {
            Call { Callee: Member { Target: Name { Identifier: "Task" }, MemberName: "WhenAll" } } all => all.Arguments.Select(a => HandleOf(a.Value)).OfType<Handle>(),
            _ => HandleOf(awaited) is { } one ? [one] : [],
        };

        private static Handle Merge(Handle? into, Handle from)
        {
            if (into is null || ReferenceEquals(into, from)) return from;

            foreach (var thread in from.Threads.Where(t => !into.Threads.Contains(t))) into.Threads.Add(thread);
            into.Starts.AddRange(from.Starts);
            into.Joins.AddRange(from.Joins);
            into.Escaped |= from.Escaped;
            return into;
        }

        private Handle? HandleOf(Expr expression) => expression switch
        {
            Name { Identifier: var name } => _handles.GetValueOrDefault(name),
            ElementAccess { Target: Name { Identifier: var name } } => _handles.GetValueOrDefault(name),
            _ => null,
        };

        private void Escape(string name)
        {
            if (_handles.TryGetValue(name, out var handle)) handle.Escaped = true;
        }

        /// <summary>Each thread made, with where it started and was waited for, now the whole function has been read.</summary>
        public IReadOnlyList<ThreadStart> Threads()
        {
            // A thread's handle captured by a lambda or nested function can be joined from there.
            foreach (var nested in names.NestedIn(_launcher))
                foreach (var name in IrWalk.FreeNames(nested)) Escape(name);

            var threads = new List<ThreadStart>();

            for (var index = 0; index < _made.Count; index++)
            {
                var made = _made[index].Thread;
                var holders = _handles.Values.Where(h => h.Threads.Contains(made)).ToList();
                var followed = holders.All(h => !h.Escaped);

                var start = made.Starting switch
                {
                    Starting.WhenStarted when _startedInPlace.Contains(index) => (made.Position, made.At),
                    Starting.WhenStarted => holders.SelectMany(h => h.Starts).Where(s => s.Position >= made.Position).OrderBy(s => s.Position)
                        .Cast<(int Position, SourceSpan At)?>().FirstOrDefault(),
                    _ => (made.Position, made.At),
                };

                if (start is null)
                {
                    // Never started here: either nothing runs it, or it is started somewhere this function hands it to.
                    if (followed) continue;
                    start = (made.Position, made.At);
                }

                var started = start.Value.Position;

                var joins = made.Starting == Starting.AndWaitedFor
                    ? [(made.Position, made.At, (Expr?)made.Expression)]
                    : holders.SelectMany(h => h.Joins).Where(j => j.Position > started).OrderBy(j => j.Position).ToList();
                var (joined, joinedAt, joinedBy) = joins.Count > 0 ? ((int?)joins[0].Position, (SourceSpan?)joins[0].At, joins[0].By) : (null, null, null);

                // Made in a loop and not waited for before the loop goes round again: the copies run at the same time.
                var inLoop = made.Loops.Count > 0 && made.Starting != Starting.AndWaitedFor;
                var waitedInLoop = inLoop && joined is { } end && end >= made.Loops[^1].First && end <= made.Loops[^1].Last;
                var several = made.Several || inLoop && !waitedInLoop;

                threads.Add(new ThreadStart(made.Body, _launcher, made.Receiver, made.Arguments, made.Bound, started,
                    start.Value.At, joined, joinedAt, joinedBy, several, followed));
            }

            return threads;
        }
    }
}
