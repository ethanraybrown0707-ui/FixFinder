using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Races, from happens-before and locksets. <see cref="HappensBefore"/> says which code can run at the same time as which
/// thread. The lockset of an access is every lock held at it: taken by synchronized, lock or with, taken by lock() or
/// acquire() and let go in a finally, or held by the code that called the function it is in. Two accesses to the same
/// data race when they can happen at the same time, at least one changes it, and no lock is held at both.
/// </summary>
/// <remarks>
/// <para>
/// Two things are reported. Code that reads what a thread it started may still be changing - a total printed before the
/// join() that waits for the threads adding to it. And data kept under a lock in one place, but used without that lock -
/// or under a different one - in another: a lock only protects data if everything using the data holds it.
/// </para>
/// <para>
/// Data no lock guards anywhere is left to the lost-update and stale-read checks, which say what goes wrong in each case.
/// In Python one read or one assignment is never torn, so only a read-modify-write racing with a change counts there.
/// </para>
/// <para>
/// Latches, events, blocking queues and futures order threads too, but what they order is not followed here - so
/// wherever one is used, no race is claimed on either side of it.
/// </para>
/// </remarks>
internal sealed class Races(IrProgram program, ProgramNames names)
{
    public const string ReadBeforeJoinRule = "analysis-read-before-join";
    public const string DataRaceRule = "analysis-data-race";

    /// <summary>How deep calls are followed from a thread's code; what happens further down is not attributed to it.</summary>
    private const int MostCallDepth = 4;

    private enum AccessKind { Read, Write, Update }

    /// <summary>
    /// One read or change of shared data, the locks held at it, and the thread it happens on - null for the code that
    /// starts the threads, which also has its place in happens-before, whether it is waiting in a loop, and the call it is
    /// reached through, if it is not in that code itself.
    /// </summary>
    private sealed record Access(DataPlace Place, string Shown, AccessKind Kind, SourceSpan At, IReadOnlyList<SharedName> Locks,
        ThreadStart? On, HappensBefore? Order, int Position, bool Polling, Call? Via);

    /// <summary>
    /// A function being walked: the object it runs on, what its parameters were given, the thread it runs on, and - for
    /// code reached from the function that starts the threads - the call it is reached through and that call's position.
    /// </summary>
    private sealed record Frame(IrFunction Function, string? Receiver, IReadOnlyDictionary<int, Given> Parameters, int Depth, ThreadStart? On,
        HappensBefore? Order, int Position, bool Polling, Call? Via, IReadOnlyList<IrFunction> Calling);

    /// <summary>What a call gave one parameter: the data it names, and the lock, if it is one.</summary>
    private sealed record Given(DataPlace? Data, SharedName? Lock);

    /// <summary>
    /// Types whose methods order threads without a lock - latches, barriers, semaphores, events, blocking queues, futures.
    /// Happens-before through them is not followed, so wherever one is used, no race is claimed on either side of it.
    /// </summary>
    private static readonly HashSet<string> Synchronisers = new(StringComparer.Ordinal)
    {
        "CountDownLatch", "CyclicBarrier", "Semaphore", "Phaser", "Exchanger", "BlockingQueue", "ArrayBlockingQueue", "LinkedBlockingQueue",
        "PriorityBlockingQueue", "SynchronousQueue", "LinkedBlockingDeque", "BlockingDeque", "DelayQueue", "LinkedTransferQueue", "TransferQueue",
        "Future", "CompletableFuture", "FutureTask", "Condition",
        "Event", "BoundedSemaphore", "Barrier", "Queue", "SimpleQueue", "LifoQueue", "PriorityQueue", "JoinableQueue",
        "ManualResetEvent", "AutoResetEvent", "ManualResetEventSlim", "CountdownEvent", "SemaphoreSlim", "BlockingCollection",
        "TaskCompletionSource", "Channel", "EventWaitHandle",
    };

    private readonly List<Access> _accesses = [];
    private readonly List<AnalysisFinding> _findings = [];
    private readonly HashSet<(string Rule, string File, int Line)> _reported = [];

    /// <summary>Threads whose code uses a synchroniser, and so may be ordered against others in ways not followed here.</summary>
    private readonly HashSet<ThreadStart> _synchronisedOtherwise = new(ReferenceEqualityComparer.Instance);

    /// <summary>Where the code that starts threads uses a synchroniser - waiting on a latch, taking from a queue.</summary>
    private readonly Dictionary<HappensBefore, List<int>> _launcherWaits = new(ReferenceEqualityComparer.Instance);

    private bool IsPython => program.Language == SourceLanguage.Python;

    public IReadOnlyList<AnalysisFinding> Check()
    {
        var orders = program.AllFunctions.Select(f => HappensBefore.Of(f, names)).OfType<HappensBefore>().ToList();
        if (orders.Count == 0) return [];

        foreach (var order in orders)
        {
            var launcher = order.Launcher;
            Walk(new Frame(launcher, names.ThisOf(launcher), new Dictionary<int, Given>(), 0, null, order, 0, false, null, [launcher]),
                launcher.Body, Synchronised(launcher));

            foreach (var thread in order.Threads)
            {
                var given = new Dictionary<int, Given>();
                for (var index = 0; index < thread.Body.Parameters.Count; index++)
                {
                    var position = thread.Bound ? index - 1 : index;
                    if (position < 0 || position >= thread.Arguments.Count) continue;

                    var passed = thread.Arguments[position].Value;
                    given[index] = new Given(names.PlaceOf(passed, launcher, names.ThisOf(launcher)), names.Named(passed, launcher));
                }

                Walk(new Frame(thread.Body, thread.Receiver, given, 0, thread, null, 0, false, null, [thread.Body]), thread.Body.Body, Synchronised(thread.Body));
            }
        }

        ReadsBeforeJoin();
        InconsistentLocking();
        return _findings;
    }

    /// <summary>The lock a synchronized method holds the whole time it runs: its object's, or its class's if it is static.</summary>
    private IReadOnlyList<SharedName> Synchronised(IrFunction function) =>
        function.IsSynchronized && names.Targets.ClassOf(function) is { } owner
            ? [function.IsStatic ? new SharedName($"{owner}.class", $"{owner}.class") : new SharedName($"{owner}.this", "this")]
            : [];

    private void Walk(Frame frame, IEnumerable<Stmt> block, IReadOnlyList<SharedName> held)
    {
        var holding = held;

        foreach (var statement in block)
        {
            if (Taken(statement, frame) is { } acquired)
            {
                holding = [.. holding, acquired];
                continue;
            }

            if (Released(statement, frame) is { } released)
            {
                holding = holding.Where(l => l.Identity != released.Identity).ToList();
                continue;
            }

            var here = frame.Depth == 0 && frame.Order is { } order ? frame with { Position = order.PositionOf(statement) } : frame;

            // A while loop in the code that starts the threads - its condition and body - is waiting for them by checking on them.
            if (statement is While && here.On is null && here.Depth == 0) here = here with { Polling = true };

            Accesses(here, statement, holding);
            Calls(here, statement, holding);

            if (statement is Using used && LockOrder.IsLockRegion(used) && LockIn(used, here) is { } locked)
            {
                Walk(here, used.Body, [.. holding, locked]);
            }
            else
            {
                foreach (var inner in Children(statement)) Walk(here, inner, holding);
            }

            // lock(); try { ... } finally { unlock(); } - the finally lets the lock go on every way out of the try.
            if (statement is Try { Finally: var cleanup })
            {
                foreach (var let in cleanup.Select(s => Released(s, frame)).OfType<SharedName>())
                    holding = holding.Where(l => l.Identity != let.Identity).ToList();
            }
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

    /// <summary>A statement that takes a lock and holds it for the statements after it: lock.lock(), lock.acquire(), Monitor.Enter(x).</summary>
    private SharedName? Taken(Stmt statement, Frame frame) => statement switch
    {
        Evaluate { Value: Call { Callee: Member { Target: var locked, MemberName: "lock" or "lockInterruptibly" or "Lock" or "EnterWriteLock" or "EnterReadLock" }, Arguments.Count: 0 } } =>
            LockOf(locked, frame),
        Evaluate { Value: Call { Callee: Member { Target: var locked, MemberName: "acquire" }, Arguments.Count: 0 } } when IsPython => LockOf(locked, frame),
        Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: "Monitor" }, MemberName: "Enter" }, Arguments: [{ Value: var locked }, ..] } } =>
            LockOf(locked, frame),
        _ => null,
    };

    private SharedName? Released(Stmt statement, Frame frame) => statement switch
    {
        Evaluate { Value: Call { Callee: Member { Target: var locked, MemberName: "unlock" or "Unlock" or "ExitWriteLock" or "ExitReadLock" }, Arguments.Count: 0 } } =>
            LockOf(locked, frame),
        Evaluate { Value: Call { Callee: Member { Target: var locked, MemberName: "release" }, Arguments.Count: 0 } } when IsPython => LockOf(locked, frame),
        Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: "Monitor" }, MemberName: "Exit" }, Arguments: [{ Value: var locked }, ..] } } =>
            LockOf(locked, frame),
        _ => null,
    };

    /// <summary>
    /// The lock a lock expression names, in the terms of the whole program - a parameter's, as its caller named it. The two
    /// halves of a read-write lock, rw.readLock() and rw.writeLock(), keep each other out, so both are rw.
    /// </summary>
    private SharedName LockOf(Expr expression, Frame frame)
    {
        if (expression is Call { Callee: Member { Target: var whole, MemberName: "readLock" or "writeLock" }, Arguments.Count: 0 }) return LockOf(whole, frame);

        var named = names.Named(expression, frame.Function);
        if (named is null || !named.IsKnown) return SharedName.Unnamed;
        if (named.IsConcrete) return named;

        return frame.Parameters.GetValueOrDefault(named.Parameter)?.Lock is { IsKnown: true, IsConcrete: true } passed
            ? new SharedName(passed.Identity + named.Path, passed.Shown + named.Path)
            : SharedName.Unnamed;
    }

    /// <summary>What a lock region holds; Python's with on something made on the spot - with open(f):, suppress() - is no lock.</summary>
    private SharedName? LockIn(Using used, Frame frame) =>
        used.Purpose == UsingPurpose.Either && used.Resource is Call ? null : LockOf(used.Resource, frame);

    /// <summary>The reads and changes of shared data one statement makes itself.</summary>
    private void Accesses(Frame frame, Stmt statement, IReadOnlyList<SharedName> held)
    {
        switch (statement)
        {
            case Assign { Target: var target, Value: var value, Compound: var compound }:
                var readBack = value is Binary { Left: var left } && Same(left, target) ? left : null;
                Record(frame, target, compound is not null || readBack is not null ? AccessKind.Update : AccessKind.Write, held);
                ReadsInside(frame, target, held);
                Reads(frame, value, held, readBack);
                break;

            default:
                foreach (var expression in IrWalk.Expressions(statement)) Reads(frame, expression, held, null);
                break;
        }
    }

    /// <summary>What reading a target to assign to it reads: the object a field is on, the list and index an item is at.</summary>
    private void ReadsInside(Frame frame, Expr target, IReadOnlyList<SharedName> held)
    {
        switch (target)
        {
            case ElementAccess element:
                Reads(frame, element.Target, held, null);
                Reads(frame, element.Key, held, null);
                break;
            case Member { Target: not Name { Identifier: "this" or "self" } } member:
                Reads(frame, member.Target, held, null);
                break;
        }
    }

    /// <summary>Every read of shared data in an expression - except <paramref name="skipped"/>, which is part of an update.</summary>
    private void Reads(Frame frame, Expr expression, IReadOnlyList<SharedName> held, Expr? skipped)
    {
        if (ReferenceEquals(expression, skipped)) return;

        switch (expression)
        {
            case Name or Member when Resolve(expression, frame) is not null:
                Record(frame, expression, AccessKind.Read, held);
                return;

            case Call call:
                if (call.Callee is Member { Target: var receiver }) Reads(frame, receiver, held, skipped);
                foreach (var argument in call.Arguments) Reads(frame, argument.Value, held, skipped);
                return;

            case AssignValue assigned:
                var readBack = assigned.Value is Binary { Left: var left } && Same(left, assigned.Target) ? left : null;
                Record(frame, assigned.Target, readBack is not null ? AccessKind.Update : AccessKind.Write, held);
                Reads(frame, assigned.Value, held, readBack);
                return;

            case Opaque { What: "lambda expression" or "lambda" }:
                return;
        }

        foreach (var child in IrWalk.Children(expression)) Reads(frame, child, held, skipped);
    }

    private static bool Same(Expr first, Expr second) => (first, second) switch
    {
        (Name a, Name b) => a.Identifier == b.Identifier,
        (Member a, Member b) => a.MemberName == b.MemberName && Same(a.Target, b.Target),
        _ => false,
    };

    /// <summary>The data an expression names, in the whole program's terms - a parameter's, as the call that gave it named it.</summary>
    private DataPlace? Resolve(Expr expression, Frame frame)
    {
        if (names.PlaceOf(expression, frame.Function, frame.Receiver) is not { } place) return null;
        if (place.IsConcrete) return place;

        return frame.Parameters.GetValueOrDefault(place.Parameter)?.Data is { IsConcrete: true } passed
            ? new DataPlace(passed.Identity + place.Path, place.Field ?? passed.Field)
            : null;
    }

    private void Record(Frame frame, Expr expression, AccessKind kind, IReadOnlyList<SharedName> held)
    {
        if (Resolve(expression, frame) is not { } place) return;

        _accesses.Add(new Access(place, names.Shown(expression), kind, expression.Span, held, frame.On, frame.Order, frame.Position, frame.Polling, frame.Via));
    }

    /// <summary>
    /// Calls to the program's own code: what the callee does happens on the same thread, holding the same locks, with its
    /// parameters given what the call passes and this standing for the object the call is made on.
    /// </summary>
    private void Calls(Frame frame, Stmt statement, IReadOnlyList<SharedName> held)
    {
        foreach (var call in IrWalk.Expressions(statement).SelectMany(CallsIn))
        {
            if (call.Callee is Member { Target: var used } && names.TypeNameOf(used, frame.Function) is { } type && Synchronisers.Contains(type))
            {
                if (frame.On is { } thread) _synchronisedOtherwise.Add(thread);
                else if (frame.Order is { } order) (_launcherWaits.TryGetValue(order, out var waits) ? waits : _launcherWaits[order] = []).Add(frame.Position);
            }

            if (frame.Depth >= MostCallDepth || Callee(call, frame) is not { } resolved) continue;

            var (target, receiver) = resolved;
            if (frame.Calling.Contains(target.Function, ReferenceEqualityComparer.Instance)) continue;

            var given = new Dictionary<int, Given>();
            for (var index = 0; index < target.Function.Parameters.Count; index++)
            {
                if (ArgumentFor(call, target, index) is not { } argument) continue;
                given[index] = new Given(Resolve(argument, frame), LockOf(argument, frame) is { IsKnown: true } passedLock ? passedLock : null);
            }

            var inside = frame with
            {
                Function = target.Function,
                Receiver = receiver,
                Parameters = given,
                Depth = frame.Depth + 1,
                Via = frame.Via ?? call,
                Calling = [.. frame.Calling, target.Function],
            };

            Walk(inside, target.Function.Body, [.. held, .. Synchronised(target.Function)]);
        }
    }

    private static IEnumerable<Call> CallsIn(Expr expression)
    {
        if (expression is Opaque { What: "lambda expression" or "lambda" }) yield break;
        if (expression is Call call) yield return call;

        foreach (var child in IrWalk.Children(expression))
            foreach (var inner in CallsIn(child)) yield return inner;
    }

    /// <summary>
    /// The function a call runs and the object it runs on: a method of this object, a static method, or - where the code
    /// says what class an object is - a method called on another object.
    /// </summary>
    private (CallTarget Target, string? Receiver)? Callee(Call call, Frame frame)
    {
        if (names.Targets.Resolve(call, frame.Function, names.LocalsOf(frame.Function)) is { } target)
            return (target, target.Function.IsStatic || target.Function.Owner is null ? null : frame.Receiver);

        if (call.Callee is not Member { Target: var owner, MemberName: var method } || names.ClassOfValue(owner, frame.Function) is not { } type) return null;

        var arguments = call.Arguments.Count;
        var matching = type.Methods.Where(m => m.Name == method && m.Parameters.Count(p => p.Name != "self") == arguments).ToList();
        if (matching.Count != 1) return null;

        return (new CallTarget(matching[0], Bound: IsPython), Resolve(owner, frame)?.Identity);
    }

    private static Expr? ArgumentFor(Call call, CallTarget target, int parameterIndex)
    {
        var parameter = target.Function.Parameters[parameterIndex];
        if (call.Arguments.FirstOrDefault(a => a.Name == parameter.Name) is { } named) return named.Value;
        if (parameter.Kind != ParameterKind.Normal) return null;

        var position = target.Bound ? parameterIndex - 1 : parameterIndex;
        var positional = call.Arguments.Where(a => a.Name is null).ToList();
        return position >= 0 && position < positional.Count ? positional[position].Value : null;
    }

    /// <summary>
    /// The code that starts threads reading what one of them may still be changing. Only a thread whose start and join are
    /// both in that code counts, and not a read in a loop - one that waits for the thread by checking on it.
    /// </summary>
    private void ReadsBeforeJoin()
    {
        var changes = _accesses.Where(a => a.On is not null && a.Kind != AccessKind.Read && !a.Place.Synchronises).ToLookup(a => a.Place.Identity, StringComparer.Ordinal);

        foreach (var read in _accesses.Where(a => a.On is null && a.Kind == AccessKind.Read && !a.Polling && !a.Place.Synchronises))
        {
            if (read.Order is not { } order) continue;

            var waits = _launcherWaits.GetValueOrDefault(order) ?? [];
            var change = changes[read.Place.Identity].FirstOrDefault(c =>
                ReferenceEquals(c.On!.Launcher, order.Launcher) && HappensBefore.MayOverlap(c.On, read.Position) &&
                !_synchronisedOtherwise.Contains(c.On) && !waits.Any(w => w > c.On.Started && w <= read.Position));
            if (change is null) continue;

            var thread = change.On!;
            var reportAt = read.Via?.Span ?? read.At;
            if (!_reported.Add((ReadBeforeJoinRule, reportAt.File, reportAt.Line))) continue;

            var what = read.Via is { } via
                ? $"`{names.Shown(via)}` reads `{read.Shown}` (line {read.At.Line})"
                : $"`{read.Shown}` is read here";

            var wait = thread.JoinedAt is { } joined
                ? $"it has only certainly finished after {Waiting(thread)} on line {joined.Line}, so read it after that"
                : $"and nothing here waits for it to finish - {WaitAdvice}";

            _findings.Add(new AnalysisFinding(ReadBeforeJoinRule, reportAt,
                $"{what} while the thread started on line {thread.StartedAt.Line} may still be changing it at line {change.At.Line} - {wait}",
                Severity.Warning, Confidence.Likely, FindingKind.Logic, Concurrency.FoundBy));
        }
    }

    private string Waiting(ThreadStart thread) => thread.JoinedBy is { } by ? $"`{names.Shown(by)}`" : "the end of the block that closes its pool";

    private string WaitAdvice => program.Language switch
    {
        SourceLanguage.CSharp => "Join() the thread, or Wait() for the task, before reading it",
        _ => "call join() on the thread before reading it",
    };

    /// <summary>
    /// Data used under a lock in one place and without it, or under a different lock, in another, by threads that can run
    /// at the same time. Reported at the access holding fewer locks, which is where a lock is missing.
    /// </summary>
    private void InconsistentLocking()
    {
        foreach (var uses in _accesses.Where(a => a.On is not null && !a.Place.Synchronises).GroupBy(a => a.Place.Identity, StringComparer.Ordinal))
        {
            var list = uses.ToList();

            for (var first = 0; first < list.Count; first++)
            {
                for (var second = first; second < list.Count; second++)
                {
                    var (one, other) = (list[first], list[second]);
                    if (!Conflict(one, other) || !HappensBefore.MayOverlap(one.On!, other.On!)) continue;
                    if (_synchronisedOtherwise.Contains(one.On!) || _synchronisedOtherwise.Contains(other.On!)) continue;
                    if (one.Locks.Count + other.Locks.Count == 0 || SharesALock(one, other)) continue;

                    var (missing, kept) = one.Locks.Count < other.Locks.Count || one.Locks.Count == other.Locks.Count && one.At.Line >= other.At.Line
                        ? (one, other)
                        : (other, one);

                    Report(missing, kept);
                }
            }
        }
    }

    /// <summary>Whether two accesses can clash: at least one changes the data - and in Python, one reads it and writes it back.</summary>
    private bool Conflict(Access one, Access other)
    {
        if (ReferenceEquals(one, other)) return one.Kind != AccessKind.Read && one.On!.Several;
        if (one.Kind == AccessKind.Read && other.Kind == AccessKind.Read) return false;
        if (!IsPython) return true;

        return (one.Kind == AccessKind.Update && other.Kind != AccessKind.Read) || (other.Kind == AccessKind.Update && one.Kind != AccessKind.Read);
    }

    /// <summary>Whether some lock is held at both; one that cannot be named might be the same lock at both, and is given the benefit of the doubt.</summary>
    private static bool SharesALock(Access one, Access other) =>
        one.Locks.Any(mine => other.Locks.Any(theirs => theirs.Identity == mine.Identity));

    private void Report(Access missing, Access kept)
    {
        if (!_reported.Add((DataRaceRule, missing.At.File, missing.At.Line))) return;

        var name = missing.Shown;
        var here = missing.Locks.Count == 0 ? "with no lock held" : $"holding {LocksShown(missing)}";
        var there = kept.Locks.Count == 0 ? "with no lock held" : $"while holding {LocksShown(kept)}";
        var where = kept.At.File == missing.At.File ? $"line {kept.At.Line}" : $"line {kept.At.Line} of {Path.GetFileName(kept.At.File)}";
        var why = missing.Locks.Count == 0
            ? $"a lock only protects `{name}` if everything that uses it holds the lock"
            : "different locks do not keep each other out";

        var outcome = (missing.Kind, kept.Kind) switch
        {
            (AccessKind.Read, _) => "this read can see an out-of-date value, or one in the middle of being changed",
            (_, AccessKind.Read) => $"the read there can see an out-of-date value, or one in the middle of being changed",
            _ => "both changes can happen at the same time, and one of them can be lost",
        };

        _findings.Add(new AnalysisFinding(DataRaceRule, missing.At,
            $"`{name}` is {Done(missing)} here {here}, but {where} {Does(kept)} it {there} on another thread - {why}, so {outcome}",
            Severity.Warning, Confidence.Likely, FindingKind.Logic, Concurrency.FoundBy));
    }

    private static string Done(Access access) => access.Kind == AccessKind.Read ? "read" : "changed";

    private static string Does(Access access) => access.Kind == AccessKind.Read ? "reads" : "changes";

    private static string LocksShown(Access access) => string.Join(" and ", access.Locks.Select(l => $"`{l.Shown}`").Distinct());
}
