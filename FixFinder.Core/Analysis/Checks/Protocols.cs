using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Temporal logic checking. Two properties about the order things happen in, each checked as a small automaton run over
/// every way through a function:
/// <list type="bullet">
/// <item>□(close → □¬use) - once a file, stream, database connection or socket is closed it is never used again;</item>
/// <item>□(send → connected) - a socket that is made and not yet connected is not sent on or received from;</item>
/// <item>□(acquire → ◇release) - a lock that is taken is always let go before the function is left.</item>
/// </list>
/// Each variable's possible states are carried forward through the graph and joined where ways meet; a violation is
/// reported only where every way into it agrees.
/// </summary>
public sealed class Protocols(
    ControlFlowGraph graph, SourceLanguage language, Func<Expr, string> quote, Action<string, SourceSpan, string, Severity, Confidence> report,
    IReadOnlySet<string>? threadClasses = null)
{
    private enum Phase { Open, Closed, Held, Released, Untracked, ThreadNew, ThreadStarted, Unconnected }

    /// <summary>What a variable that is opened and closed holds: a file or stream, a database connection, or a socket.</summary>
    private enum Resource { File, Database, Socket }

    /// <summary>Where a tracked variable is in its protocol, and the line that put it there.</summary>
    private sealed record Mark(Phase Phase, int Line, SourceSpan At)
    {
        /// <summary>Whether the file was opened for writing, so leaving it open can lose what was written.</summary>
        public bool Writes { get; init; }

        public Resource Kind { get; init; } = Resource.File;
    }

    private sealed class State : Dictionary<string, HashSet<Mark>>
    {
        public State() : base(StringComparer.Ordinal) { }

        public State Copy()
        {
            var copy = new State();
            foreach (var (name, marks) in this) copy[name] = [.. marks];
            return copy;
        }

        /// <summary>Joins another way in; a variable tracked on only one of them may be untracked here.</summary>
        public bool Join(State other)
        {
            var changed = false;
            foreach (var name in Keys.Union(other.Keys).ToList())
            {
                if (!TryGetValue(name, out var mine)) this[name] = mine = [new Mark(Phase.Untracked, 0, SourceSpan.None)];
                var theirs = other.TryGetValue(name, out var found) ? found : [new Mark(Phase.Untracked, 0, SourceSpan.None)];
                foreach (var mark in theirs) changed |= mine.Add(mark);
            }

            return changed;
        }
    }

    private bool IsPython => language == SourceLanguage.Python;

    private readonly HashSet<SourceSpan> _reported = [];

    /// <summary>Locks handed to other code - put in a list, passed to a call, returned - which may well release them.</summary>
    private readonly HashSet<string> _handedOn = HandedOn(graph);

    private static HashSet<string> HandedOn(ControlFlowGraph graph)
    {
        var handed = new HashSet<string>(StringComparer.Ordinal);

        void Visit(Expr expression)
        {
            // Handed to a call, which may close it - or to a new object, such as a BufferedReader around a FileReader,
            // which owns it from then on and closes it when it is itself closed.
            var arguments = expression switch
            {
                Call call => call.Arguments,
                NewObject made => made.Arguments,
                _ => [],
            };

            foreach (var argument in arguments)
                if (argument.Value is Name { Identifier: var passed }) handed.Add(passed);

            foreach (var child in IrWalk.Children(expression)) Visit(child);
        }

        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction is AssignInstruction { Value: Name { Identifier: var copied } }) handed.Add(copied);
                if (instruction is AssignInstruction assign) Visit(assign.Value);
                if (instruction is EvaluateInstruction evaluate) Visit(evaluate.Value);
            }

            if (block.Terminator is Leave { Value: Name { Identifier: var returned } }) handed.Add(returned);
        }

        return handed;
    }

    public void Check()
    {
        var entry = new Dictionary<int, State> { [graph.Entry] = new State() };
        var pending = new Queue<int>([graph.Entry]);

        while (pending.Count > 0)
        {
            var block = graph.Blocks[pending.Dequeue()];
            var state = entry[block.Id].Copy();
            State? thrown = null;

            foreach (var instruction in block.Instructions)
            {
                if (MayThrow(instruction))
                {
                    if (thrown is null) thrown = state.Copy();
                    else thrown.Join(state);
                }

                Step(state, instruction, block, reporting: false);
            }

            Ending(state, block, reporting: false);

            foreach (var next in graph.Successors(block.Id))
            {
                var byException = block.ExceptionTargets.Contains(next) && block.Terminator is not Raise;
                if (byException && thrown is null) continue;
                var carried = byException ? thrown! : state;

                if (!entry.TryGetValue(next, out var known))
                {
                    entry[next] = carried.Copy();
                    pending.Enqueue(next);
                }
                else if (known.Join(carried))
                {
                    pending.Enqueue(next);
                }
            }
        }

        foreach (var block in graph.Blocks)
        {
            if (!entry.TryGetValue(block.Id, out var start)) continue;

            var state = start.Copy();
            foreach (var instruction in block.Instructions) Step(state, instruction, block, reporting: true);
            Ending(state, block, reporting: true);
        }
    }

    private void Step(State state, Instruction instruction, BasicBlock block, bool reporting)
    {
        switch (instruction)
        {
            case AssignInstruction { Target: Name { Identifier: var name }, Value: var value } assign:
                Uses(state, value, reporting);
                if (Opened(value) is { } opened)
                    state[name] = [new Mark(opened.Connected ? Phase.Open : Phase.Unconnected, assign.Span.Line, assign.Span) { Writes = OpensForWriting(value), Kind = opened.Kind }];
                else if (MakesThread(value)) state[name] = [new Mark(Phase.ThreadNew, assign.Span.Line, assign.Span)];
                else state.Remove(name);
                break;

            case AssignInstruction assign:
                Uses(state, assign.Value, reporting);
                Uses(state, assign.Target, reporting);
                break;

            case EvaluateInstruction { Value: var value }:
                if (!Moves(state, value, block, reporting)) Uses(state, value, reporting);
                break;

            // The end of a with block closes a file or a socket - but not a sqlite3 connection: its with block commits, or rolls
            // back, and leaves it open, as Python 3.13 did, reading from it after the block.
            case ReleaseInstruction { Resource: Name { Identifier: var released } } release when state.ContainsKey(released) && KindOf(state, released) != Resource.Database:
                state[released] = [new Mark(Phase.Closed, release.Span.Line, release.Span) { Kind = KindOf(state, released) }];
                break;
        }
    }

    private void Ending(State state, BasicBlock block, bool reporting)
    {
        switch (block.Terminator)
        {
            case Branch branch:
                if (branch.Condition is MoreItems { Items: Name { Identifier: var items } } more) Use(state, items, more.Span, $"the loop over `{items}`", reporting);
                Uses(state, branch.Condition, reporting);
                break;

            case Leave leave:
                if (leave.Value is { } value) Uses(state, value, reporting);
                if (reporting) StillHeld(state, leave);
                if (reporting) StillOpen(state, leave);
                break;
        }
    }

    /// <summary>
    /// Resource ownership: a file or stream this function opened belongs to it until it is closed or handed to other code
    /// - returned, stored, given to a call or to an object that wraps it. One still open and still its own on a way out
    /// of the function is never closed. Python's own top-level code is left out: the interpreter closes its files at exit.
    /// </summary>
    private void StillOpen(State state, Leave leave)
    {
        if (IsPython && graph.Function.Name == IrFunction.ModuleBody) return;

        foreach (var (name, marks) in state)
        {
            if (name.StartsWith("monitor ", StringComparison.Ordinal) || _handedOn.Contains(name)) continue;

            var everyWay = marks.All(m => m.Phase == Phase.Open);
            foreach (var opened in marks.Where(m => m.Phase == Phase.Open))
            {
                if (!_reported.Add(opened.At)) continue;

                var consequence = opened.Kind != Resource.File ? "so the connection stays open until the program ends"
                    : opened.Writes ? "so what was written to it may never reach the file"
                    : "so the file stays open until the program ends";

                report(opened.Kind == Resource.File ? "analysis-resource-not-closed" : "analysis-connection-not-closed", opened.At,
                    $"`{name}`, opened here, is still open {(everyWay ? "" : "on some ways ")}when the function returns on line {leave.Span.Line}, and nothing " +
                    $"else was given it to close - {consequence}. {CloseAdviceFor(opened.Kind)}",
                    IsPython ? Severity.Suggestion : Severity.Warning, Confidence.Likely);
            }
        }
    }

    /// <summary>
    /// A thread's life as a state machine: made, then started, once. start() on a thread already started is an error in
    /// every language; join() on one never started is an error in Python and C#, and in Java returns at once, waiting for
    /// nothing. What makes a thread: new Thread(...), threading.Thread(...), or a class of the program's own that is one.
    /// </summary>
    private bool MakesThread(Expr value) => value switch
    {
        NewObject { Type.Name: var type } when !IsPython => type == "Thread" || threadClasses?.Contains(type) == true,
        Call { CalleeName: "Thread" } when IsPython => true,
        Call { Callee: Name { Identifier: var type } } when IsPython => threadClasses?.Contains(type) == true,
        _ => false,
    };

    /// <summary>Whether a name holds a thread on every way to here - one tracked on some ways only is left alone.</summary>
    private static bool IsThread(State state, string name) =>
        state.TryGetValue(name, out var marks) && marks.Count > 0 && marks.All(m => m.Phase is Phase.ThreadNew or Phase.ThreadStarted);

    private void StartedAgain(State state, string thread, Call start)
    {
        var earlier = state[thread].Where(m => m.Phase == Phase.ThreadStarted).ToList();
        if (earlier.Count == 0 || !_reported.Add(start.Span)) return;

        var lines = string.Join(" or ", earlier.Select(m => m.Line).Distinct().Order());
        var failure = language switch
        {
            SourceLanguage.Python => "RuntimeError: threads can only be started once",
            SourceLanguage.CSharp => "a ThreadStateException",
            _ => "an IllegalThreadStateException",
        };

        if (earlier.Count == state[thread].Count)
        {
            report("analysis-thread-started-twice", start.Span,
                $"`{quote(start)}` starts `{thread}` again, but it was already started on line {lines}, and a thread can only be started once - this fails with {failure}. " +
                "Make a new thread for each piece of work", Severity.Error, Confidence.Certain);
        }
        else
        {
            report("analysis-thread-started-twice", start.Span,
                $"`{quote(start)}` can start `{thread}` a second time - it may already have been started on line {lines}, as on a second time round a loop - and a " +
                $"thread can only be started once: that fails with {failure}. Make a new thread for each piece of work", Severity.Error, Confidence.Likely);
        }
    }

    private void JoinedBeforeStart(State state, string thread, Call join)
    {
        if (!state[thread].All(m => m.Phase == Phase.ThreadNew) || !_reported.Add(join.Span)) return;

        var (what, severity) = language switch
        {
            SourceLanguage.Python => ("this fails with RuntimeError: cannot join thread before it is started", Severity.Error),
            SourceLanguage.CSharp => ("this fails with a ThreadStateException", Severity.Error),
            _ => ("so it returns at once without waiting for anything, and the code after it runs as if the work were done", Severity.Warning),
        };

        report("analysis-join-before-start", join.Span,
            $"`{quote(join)}` waits for `{thread}`, which has not been started - {what}. Call `{thread}.{(language == SourceLanguage.CSharp ? "Start" : "start")}()` first",
            severity, Confidence.Certain);
    }

    /// <summary>
    /// Whether what opens a file opens it for writing: a writer or output stream, File.CreateText and its like, or Python's
    /// open with a mode that writes, appends, creates or updates.
    /// </summary>
    private bool OpensForWriting(Expr value) => value switch
    {
        Call { Callee: Name { Identifier: "open" } } opened when IsPython =>
            (opened.Arguments.FirstOrDefault(a => a.Name == "mode") ?? opened.Arguments.Where(a => a.Name is null).Skip(1).FirstOrDefault()) is
                { Value: Literal { Value: string mode } } && mode.IndexOfAny(['w', 'a', 'x', '+']) >= 0,
        NewObject { Type.Name: "FileWriter" or "BufferedWriter" or "FileOutputStream" or "PrintWriter" or "ObjectOutputStream" or "StreamWriter" or "BinaryWriter" } => true,
        Call { Callee: Member { Target: Name { Identifier: "File" }, MemberName: "OpenWrite" or "CreateText" or "AppendText" or "Create" } } => true,
        _ => false,
    };

    /// <summary>How to close what is left open: a sqlite3 connection's own with block does not close it.</summary>
    private string CloseAdviceFor(Resource kind) => (kind, language) switch
    {
        (Resource.Database, _) => "Close it in a finally, or open it with `with contextlib.closing(sqlite3.connect(...)) as conn:` - " +
                                  "`with sqlite3.connect(...)` on its own commits, but leaves the connection open",
        (Resource.Socket, SourceLanguage.Python) => "Make it with `with socket.socket(...) as s:`, which closes it however the function ends",
        _ => CloseAdvice,
    };

    private string CloseAdvice => language switch
    {
        SourceLanguage.Python => "Open it with `with open(...) as f:`, which closes it however the function ends",
        SourceLanguage.CSharp => "Declare it with `using`, which disposes of it however the method ends",
        _ => "Open it in a try-with-resources - try (var reader = ...) { ... } - which closes it however the method ends",
    };

    /// <summary>Calls that move a variable along its protocol: close, lock and unlock. False when the value is none of them.</summary>
    private bool Moves(State state, Expr value, BasicBlock block, bool reporting)
    {
        switch (value)
        {
            case Call { Callee: Member { Target: var thread, MemberName: "start" or "Start" }, Arguments.Count: 0 } start
                when Owner(thread) is { } started && IsThread(state, started):
                if (reporting) StartedAgain(state, started, start);
                state[started] = [new Mark(Phase.ThreadStarted, start.Span.Line, start.Span)];
                return true;

            case Call { Callee: Member { Target: var thread, MemberName: "join" or "Join" } } join when Owner(thread) is { } joined && IsThread(state, joined):
                if (reporting) JoinedBeforeStart(state, joined, join);
                return true;

            case Call { Callee: Member { Target: var closed, MemberName: "close" or "Close" or "Dispose" } } close when Owner(closed) is { } owner && state.ContainsKey(owner):
                state[owner] = [new Mark(Phase.Closed, close.Span.Line, close.Span) { Kind = KindOf(state, owner) }];
                return true;

            // A socket made and not yet connected is connected - and counts as open from the line that made it.
            case Call { Callee: Member { Target: var socket, MemberName: "connect" or "connect_ex" or "Connect" } } when
                Owner(socket) is { } owner && state.TryGetValue(owner, out var made) && made.Count > 0 && made.All(m => m.Phase == Phase.Unconnected):
                state[owner] = [.. made.Select(m => m with { Phase = Phase.Open })];
                return true;

            case Call { Callee: Member { Target: var taken, MemberName: var method } } call when Acquires(method) && Owner(taken) is { } owner && !_handedOn.Contains(owner):
                state[owner] = [new Mark(Phase.Held, call.Span.Line, call.Span)];
                return true;

            case Call { Callee: Member { Target: Name { Identifier: "Monitor" }, MemberName: "Enter" }, Arguments: [{ Value: Name { Identifier: var guarded } }, ..] } enter:
                state["monitor " + guarded] = [new Mark(Phase.Held, enter.Span.Line, enter.Span)];
                return true;

            case Call { Callee: Member { Target: var released, MemberName: var method } } when Releases(method) && Owner(released) is { } owner && state.ContainsKey(owner):
                state[owner] = [new Mark(Phase.Released, 0, SourceSpan.None)];
                return true;

            case Call { Callee: Member { Target: Name { Identifier: "Monitor" }, MemberName: "Exit" }, Arguments: [{ Value: Name { Identifier: var guarded } }, ..] }:
                state["monitor " + guarded] = [new Mark(Phase.Released, 0, SourceSpan.None)];
                return true;

            case Call call when reporting && block.ExceptionTargets.Count == 0:
                LeftByException(state, call);
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// An instruction an exception can come out of: one that makes a call - other than the protocol's own lock, unlock
    /// and close, which are what is being followed. An exception carries the state from just before it.
    /// </summary>
    private bool MayThrow(Instruction instruction) => instruction switch
    {
        EvaluateInstruction { Value: Call { Callee: Member { MemberName: var method } } } when Acquires(method) || Releases(method) ||
            method is "close" or "Close" or "Dispose" or "Enter" or "Exit" => false,
        AssignInstruction assign => Calls(assign.Value) || Calls(assign.Target),
        EvaluateInstruction evaluate => Calls(evaluate.Value),
        _ => false,
    };

    private static bool Calls(Expr expression) => expression is Call or NewObject || IrWalk.Children(expression).Any(Calls);

    /// <summary>What a lock or file is held under: its own name, or the field's name when it belongs to this object.</summary>
    private static string? Owner(Expr receiver) => receiver switch
    {
        Name name => name.Identifier,
        Member { Target: Name { Identifier: "this" or "self" }, MemberName: var field } => field,
        _ => null,
    };

    /// <summary>
    /// The locks this function releases somewhere. A function that only takes a lock is taking it for whoever called it
    /// - threading's _acquire_restore does exactly that - so being left holding it is not a mistake.
    /// </summary>
    private HashSet<string> Released => _released ??= IrWalk.Statements(graph.Function.Body).SelectMany(IrWalk.Expressions)
        .Select(e => e is Call { Callee: Member { Target: var owner, MemberName: var method } } && Releases(method) ? Owner(owner) : null)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    private HashSet<string>? _released;

    private bool Acquires(string method) => IsPython ? method == "acquire" : method is "lock" or "lockInterruptibly" or "Lock" or "RLock";

    private bool Releases(string method) => IsPython ? method == "release" : method is "unlock" or "Unlock" or "RUnlock";

    /// <summary>What a variable holds once it is closed: what it held before, the same on every way to here, or a file.</summary>
    private static Resource KindOf(State state, string name) =>
        state.TryGetValue(name, out var marks) && marks.Select(m => m.Kind).Distinct().ToList() is [var kind] ? kind : Resource.File;

    /// <summary>
    /// What opens something that has to be closed, and whether it is ready to use when made: a file or stream; a database
    /// connection, sqlite3.connect(...); or a socket - connected when made with where to connect to, socket.create_connection,
    /// new Socket(host, port), new TcpClient(host, port), and not yet connected when made with nothing, socket.socket(),
    /// new Socket(), new TcpClient().
    /// </summary>
    private (Resource Kind, bool Connected)? Opened(Expr value) => value switch
    {
        _ when Opens(value) => (Resource.File, true),
        Call { Callee: Member { Target: Name { Identifier: "sqlite3" }, MemberName: "connect" } } when IsPython => (Resource.Database, true),
        Call { Callee: Member { Target: Name { Identifier: "socket" }, MemberName: "socket" } } when IsPython => (Resource.Socket, false),
        Call { Callee: Member { Target: Name { Identifier: "socket" }, MemberName: "create_connection" } } when IsPython => (Resource.Socket, true),
        NewObject { Type.Name: "Socket", Arguments.Count: var given } when language == SourceLanguage.Java => (Resource.Socket, given > 0),
        NewObject { Type.Name: "TcpClient", Arguments.Count: var given } when language == SourceLanguage.CSharp => (Resource.Socket, given > 0),
        NewObject { Type.Name: "Socket" } when language == SourceLanguage.CSharp => (Resource.Socket, false),
        _ => null,
    };

    /// <summary>What opens a file or stream: open(...), new FileReader(...), File.OpenText(...) and their like.</summary>
    private bool Opens(Expr value) => value switch
    {
        Call { Callee: Name { Identifier: "open" } } => IsPython,
        NewObject { Type.Name: "Scanner", Arguments: [{ Value: Member { Target: Name { Identifier: "System" }, MemberName: "in" } }] } => false,
        NewObject { Type.Name: "FileReader" or "FileWriter" or "BufferedReader" or "BufferedWriter" or "FileInputStream" or "FileOutputStream"
            or "PrintWriter" or "InputStreamReader" or "ObjectInputStream" or "ObjectOutputStream" or "Scanner" or "RandomAccessFile"
            or "StreamReader" or "StreamWriter" or "FileStream" or "BinaryReader" or "BinaryWriter" } => !IsPython,
        Call { Callee: Member { Target: Name { Identifier: "File" }, MemberName: "OpenRead" or "OpenWrite" or "OpenText" or "CreateText" or "AppendText" or "Open" or "Create" } } =>
            language == SourceLanguage.CSharp,
        _ => false,
    };

    /// <summary>Every use of a tracked file inside an expression: a method called on it.</summary>
    private void Uses(State state, Expr expression, bool reporting)
    {
        if (expression is Call { Callee: Member { Target: Name { Identifier: var owner }, MemberName: var method } } call)
            Use(state, owner, call.Span, $"`{quote(call)}`", reporting && method is not ("close" or "Close" or "Dispose" or "closed"), method);

        if (expression is NextItem { Items: Name { Identifier: var items } } next) Use(state, items, next.Span, $"the loop over `{items}`", reporting);

        foreach (var child in IrWalk.Children(expression)) Uses(state, child, reporting);
    }

    private void Use(State state, string name, SourceSpan span, string what, bool reporting, string? method = null)
    {
        if (!reporting || !state.TryGetValue(name, out var marks) || marks.Count == 0) return;

        if (marks.All(m => m.Phase == Phase.Unconnected) && method is { } talking && SendsOrReceives(talking) && _reported.Add(span))
        {
            report("analysis-used-before-connect", span,
                $"`{name}` was made on line {marks.Min(m => m.Line)} and is not connected yet, so {what} fails with {NotConnected(talking)}",
                Severity.Error, Confidence.Certain);
            return;
        }

        if (!marks.All(m => m.Phase == Phase.Closed)) return;
        if (!_reported.Add(span)) return;

        var closedAt = marks.Select(m => m.Line).Distinct().Order().ToList();
        var kind = KindOf(state, name);
        var failure = (kind, language) switch
        {
            (Resource.Database, _) => "sqlite3.ProgrammingError: Cannot operate on a closed database.",
            (Resource.Socket, SourceLanguage.Python) => "OSError",
            (Resource.Socket, SourceLanguage.Java) => "a SocketException (Socket is closed)",
            (_, SourceLanguage.Python) => "ValueError: I/O operation on closed file",
            (_, SourceLanguage.CSharp) => "an ObjectDisposedException",
            _ => "an IOException (Stream closed)",
        };

        report(kind == Resource.File ? "analysis-used-after-close" : "analysis-connection-used-after-close", span,
            $"`{name}` was closed on line {string.Join(" or ", closedAt)}, so {what} fails with {failure}", Severity.Error, Confidence.Certain);
    }

    /// <summary>What sends or receives on a socket - which a socket not yet connected refuses; bind, listen and their like do not.</summary>
    private bool SendsOrReceives(string method) => language switch
    {
        SourceLanguage.Python => method is "send" or "sendall" or "recv" or "recv_into",
        SourceLanguage.Java => method is "getOutputStream" or "getInputStream",
        SourceLanguage.CSharp => method is "Send" or "Receive" or "GetStream",
        _ => false,
    };

    /// <summary>What sending or receiving on a socket not yet connected fails with, as Python 3.13, Java 21 and .NET 10 gave it.</summary>
    private string NotConnected(string method) => language switch
    {
        SourceLanguage.Python => "OSError",
        SourceLanguage.Java => "a SocketException (Socket is not connected)",
        _ when method == "GetStream" => "an InvalidOperationException (The operation is not allowed on non-connected sockets.)",
        _ => "a SocketException",
    };

    private void StillHeld(State state, Leave leave)
    {
        foreach (var (name, marks) in state)
        {
            var lockedFor = name.StartsWith("monitor ", StringComparison.Ordinal) ? name["monitor ".Length..] : name;
            if (!Released.Contains(lockedFor)) continue;

            foreach (var held in marks.Where(m => m.Phase == Phase.Held))
            {
                if (!_reported.Add(held.At)) continue;
                var lockName = name.StartsWith("monitor ", StringComparison.Ordinal) ? name["monitor ".Length..] : name;
                report("analysis-lock-not-released", held.At,
                    $"The lock `{lockName}` taken here is still held when the function returns on line {leave.Span.Line}, so everything else that needs it waits for ever",
                    Severity.Warning, Confidence.Likely);
            }
        }
    }

    /// <summary>A call made while holding a lock, outside any try: if it throws, the unlock after it never runs.</summary>
    private void LeftByException(State state, Call call)
    {
        if (!Failures.CallsCanThrow(language)) return;

        foreach (var (name, marks) in state)
        {
            if (marks.Count == 0 || !marks.All(m => m.Phase == Phase.Held)) continue;

            var held = marks.First();
            if (!_reported.Add(held.At)) continue;

            var lockName = name.StartsWith("monitor ", StringComparison.Ordinal) ? name["monitor ".Length..] : name;
            report("analysis-lock-not-released", held.At,
                $"If `{quote(call)}` throws, the lock `{lockName}` taken here is never released - release it in a finally block",
                Severity.Warning, Confidence.Possible);
        }
    }
}
