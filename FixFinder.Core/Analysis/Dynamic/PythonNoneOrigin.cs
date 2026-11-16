using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Python program stopping on None: which name held None on the line it stopped on, the line that gave
/// it None, and - when one of the program's own functions gave it - why that function gave back None.
/// </summary>
/// <remarks>
/// Every part of it comes from a second run of the program that records each of those as it happens: what the name held
/// line by line, which calls gave back None, and how many times each line ran. When that run does not stop with the same
/// error on the same line, nothing is said, because it would no longer be about the run that failed.
/// </remarks>
/// <summary>What the second run of a Python program that stopped showed: where its None came from, when it stopped on one, and what each variable held.</summary>
public sealed record PythonTraced(RootCause? Cause, IReadOnlyList<VariableHistory> Timeline);

public static partial class PythonNoneOrigin
{
    /// <summary>More names than this on one line and the line is not one a single missing value explains.</summary>
    private const int MostNamesFollowed = 8;

    /// <summary>How many calls deep a None given back by another call's is followed: more than a program's own chain of helpers.</summary>
    private const int MostCallsFollowed = 8;

    /// <summary>What the line that stopped did with the value, as Python's message says, and where on the line the value can be.</summary>
    /// <param name="Doing">What the line does with it, with {0} where its name goes: "picks an item out of {0} with [ ]".</param>
    /// <param name="Holders">Where a name holding the value can stand on the line, each with the name as its group "name".</param>
    /// <param name="AfterACall">How the line goes on straight after a call whose result is the value: "[" for an item picked out of it.</param>
    private sealed record Use(string Doing, IReadOnlyList<Regex> Holders, Regex? AfterACall = null);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "None", "True", "False", "and", "or", "not", "in", "is", "if", "else", "elif", "for", "while", "return", "lambda", "yield",
        "await", "print", "len",
    };

    /// <summary>Python's own methods that change their list, dictionary or set and give back None, with what each belongs to.</summary>
    private static readonly Dictionary<string, string> ChangesInPlace = new(StringComparer.Ordinal)
    {
        ["sort"] = "list", ["append"] = "list", ["extend"] = "list", ["insert"] = "list", ["reverse"] = "list", ["remove"] = "list",
        ["update"] = "dictionary", ["add"] = "set", ["discard"] = "set", ["clear"] = "list",
    };

    [GeneratedRegex(@"^'NoneType' object has no attribute '(?<attribute>\w+)'$")]
    private static partial Regex NoAttribute();

    [GeneratedRegex(@"^unsupported operand type\(s\) for (?<op>\S+): '(?<left>[\w.]+)' and '(?<right>[\w.]+)'$")]
    private static partial Regex Operand();

    [GeneratedRegex(@"^'(?<op>[<>=!]+)' not supported between instances of '(?<left>[\w.]+)' and '(?<right>[\w.]+)'$")]
    private static partial Regex Comparison();

    [GeneratedRegex(@"^can only concatenate (?:str|list|tuple) \(not ""NoneType""\) to (?:str|list|tuple)$")]
    private static partial Regex Concatenated();

    [GeneratedRegex(@"^(?<type>int|float)\(\) argument must be .*, not 'NoneType'$")]
    private static partial Regex Conversion();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*\[")]
    private static partial Regex PickedFrom();

    [GeneratedRegex(@"\bin\s+(?<name>[A-Za-z_]\w*)\b(?!\s*[.(\[])")]
    private static partial Regex GoneThrough();

    [GeneratedRegex(@"\b(?:list|tuple|set|sorted|sum|max|min|enumerate|zip|any|all)\(\s*(?<name>[A-Za-z_]\w*)\s*[,)]")]
    private static partial Regex GivenToALoop();

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex Called();

    [GeneratedRegex(@"\blen\(\s*(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex Measured();

    [GeneratedRegex(@"=\s*(?<name>[A-Za-z_]\w*)\s*$")]
    private static partial Regex Unpacked();

    [GeneratedRegex(@"\+\s*(?<name>[A-Za-z_]\w*)\b(?!\s*[.(\[])")]
    private static partial Regex AddedOn();

    [GeneratedRegex(@"^\s*\[")]
    private static partial Regex ItemPicked();

    [GeneratedRegex(@"^\s*(?:if|elif)\b.*:\s*$")]
    private static partial Regex IfHeader();

    [GeneratedRegex(@"^\s*return\b")]
    private static partial Regex ReturnLine();

    [GeneratedRegex(@"^\s*(?:return|yield|raise|del|assert|if|elif|else|for|while|with|try|except|finally|def|class|import|from|global|nonlocal|pass|break|continue|async|match|case)\b")]
    private static partial Regex NotAPlainStatement();

    [GeneratedRegex(@"^\s*(?:[A-Za-z_][\w.]*(?:\[[^\]]*\])?\s*,\s*)*[A-Za-z_][\w.]*(?:\[[^\]]*\])?\s*(?::[^=]+)?=(?!=)")]
    private static partial Regex PlainAssignment();

    [GeneratedRegex(@"^(?<receiver>[A-Za-z_][\w.]*)\.get\((?<arguments>[^()]*)\)$")]
    private static partial Regex DictionaryGet();

    [GeneratedRegex(@"^re\.(?<function>match|search|fullmatch)\(")]
    private static partial Regex PatternSearch();

    [GeneratedRegex(@"^(?<receiver>[A-Za-z_]\w*)\.(?<method>\w+)\(.*\)$")]
    private static partial Regex MethodCalled();

    [GeneratedRegex(@"^(?<function>[A-Za-z_]\w*)\s*\(.*\)$")]
    private static partial Regex FunctionCalled();

    /// <summary>
    /// Runs the program again, recording what each variable held where it stopped, line by line - and, when it stopped on
    /// None, following the names the line could have found None in, to say where the None came from. Null when the second run
    /// does not stop with the same error on the same line, since it would no longer be about the run that failed.
    /// </summary>
    /// <param name="type">The error's type: TypeError.</param>
    /// <param name="message">Python's message, after the type: 'NoneType' object is not subscriptable.</param>
    /// <param name="scope">The function the program stopped in, or &lt;module&gt; for the file's own top level.</param>
    public static async Task<PythonTraced?> TraceAsync(string interpreter, string file, string input, string type, string message, int crashLine,
        string scope, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is not { } stoppedOn) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var stoppedMasked = masked[crashLine - 1];
        var use = UseIn(message);

        var holders = use is null ? [] : use.Holders
            .SelectMany(holder => holder.Matches(stoppedMasked))
            .Select(match => match.Groups["name"].Value)
            .Where(name => !Keywords.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var callsUsed = use is null ? [] : CallsUsed(stoppedMasked, stoppedOn, use);

        var trace = await PythonProbe.FollowAsync(interpreter, file, input, holders.Take(MostNamesFollowed).ToList(), scope, crashLine, timeout, cancellationToken);

        // Only a run that stopped the way the first one did, on the same line, says anything about that one.
        if (trace is null || trace.Error != type || trace.Message != message || trace.ErrorLine != crashLine) return null;

        var timeline = trace.Followed
            .Where(each => each.Value.Count > 0)
            .Select(each => new VariableHistory(each.Key, [.. each.Value.Select(change => new VariableChange(change.Line, change.Value, change.CalledFrom))],
                Cut: each.Value.Count >= VariableHistory.MostKept))
            .ToList();

        if (use is null || holders.Count > MostNamesFollowed || holders.Count == 0 && callsUsed.Count == 0) return new PythonTraced(null, timeline);

        var program = new ProgramRead(source.Lines, masked, trace);
        var heldNone = holders
            .Where(name => trace.Followed.TryGetValue(name, out var changes) && changes.Count > 0 && changes[^1].Value == "None")
            .ToList();

        var cause = heldNone is [var name] ? FromName(program, use, name, trace.Followed[name], crashLine, type, scope)
            : heldNone.Count == 0 ? FromCall(program, use, callsUsed, crashLine, type)
            : null;
        return new PythonTraced(cause, timeline);
    }

    /// <summary>
    /// The change that stops the program stopping: line <paramref name="crashLine"/> runs only when the name holds a value.
    /// Null for a line that is not a statement of its own - an assignment, which would leave the name it sets unset, a
    /// return, a block's first line - since skipping one of those changes more than the line.
    /// </summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing)
    {
        if (cause.ChangeTheCause || !Regex.IsMatch(cause.Name, @"^[A-Za-z_]\w*$") || source.Line(cause.CrashLine) is not { } line) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var stopped = masked[cause.CrashLine - 1];
        if (NotAPlainStatement().IsMatch(stopped) || PlainAssignment().IsMatch(stopped)) return null;
        if (stopped.TrimEnd().EndsWith(':') || stopped.TrimEnd().EndsWith('\\')) return null;

        // The line must be a whole statement: nothing left open before it, and nothing it opens left for the next line.
        var openBefore = masked.Take(cause.CrashLine - 1).Sum(Depth);
        if (openBefore != 0 || Depth(stopped) != 0) return null;
        if (cause.CrashLine >= 2 && masked[cause.CrashLine - 2].TrimEnd().EndsWith('\\')) return null;

        var indent = CodeText.Indentation(line);
        var step = indent.Contains('\t') ? "\t" : "    ";

        return new LocalFix
        {
            RuleId = "python-none-guard",
            Title = $"Run line {cause.CrashLine} only when {cause.Name} is not None",
            Explanation = $"Line {cause.CrashLine} {doing}, and that cannot be done to None. With the check, it runs only when {cause.Name} holds a value. " +
                          $"What the program should do instead when {cause.Name} is None - say so, or use a value of its own - is yours to decide.",
            File = source.Path,
            StartLine = cause.CrashLine,
            RemoveCount = 1,
            NewLines = [$"{indent}if {cause.Name} is not None:", $"{indent}{step}{line.TrimStart()}"],
            OnlyStopsTheFailure = true,
        };
    }

    /// <summary>What the line that stopped does with the value, for the guard's explanation.</summary>
    public static string? DoingWith(string message, string name) => UseIn(message) is { } use ? string.Format(use.Doing, name) : null;

    private static int Depth(string masked) =>
        masked.Count(c => c is '(' or '[' or '{') - masked.Count(c => c is ')' or ']' or '}');

    private static Use? UseIn(string message)
    {
        if (message == "'NoneType' object is not subscriptable") return new Use("picks an item out of {0} with [ ]", [PickedFrom()], ItemPicked());

        if (NoAttribute().Match(message) is { Success: true } attribute)
        {
            var name = Regex.Escape(attribute.Groups["attribute"].Value);
            return new Use($"looks up {attribute.Groups["attribute"].Value} on {{0}}", [new Regex($@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*\.\s*{name}\b")],
                new Regex($@"^\s*\.\s*{name}\b"));
        }

        if (message is "'NoneType' object is not iterable" or "argument of type 'NoneType' is not iterable")
            return new Use("goes through {0} one item at a time", [GoneThrough(), GivenToALoop()]);

        if (message == "'NoneType' object is not callable") return new Use("calls {0}", [Called()]);
        if (message == "object of type 'NoneType' has no len()") return new Use("asks for len({0})", [Measured()]);
        if (message == "cannot unpack non-iterable NoneType object") return new Use("unpacks {0} into separate names", [Unpacked()]);
        if (Concatenated().IsMatch(message)) return new Use("adds {0} on with +", [AddedOn()]);

        if (Conversion().Match(message) is { Success: true } conversion)
        {
            var into = conversion.Groups["type"].Value;
            return new Use($"turns {{0}} into {(into == "int" ? "a whole number" : "a decimal")} with {into}()", [new Regex($@"\b{into}\(\s*(?<name>[A-Za-z_]\w*)\s*\)")]);
        }

        foreach (var (pattern, doing) in new[] { (Operand(), "uses {0} with {1}"), (Comparison(), "compares {0} using {1}") })
        {
            if (pattern.Match(message) is not { Success: true } sides) continue;

            var leftIsNone = sides.Groups["left"].Value == "NoneType";
            if (leftIsNone == (sides.Groups["right"].Value == "NoneType")) return null;

            var op = sides.Groups["op"].Value;
            var escaped = Regex.Escape(op.TrimEnd('='));
            var holder = leftIsNone
                ? new Regex($@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*{Regex.Escape(op)}")
                : new Regex($@"{escaped}=?\s*(?<name>[A-Za-z_]\w*)\b(?!\s*[.(\[])");
            return new Use(doing.Replace("{1}", op), [holder]);
        }

        return null;
    }

    /// <summary>Calls on the line whose result the line then uses the way the message says: <c>find(members, 2)[...]</c>.</summary>
    private static List<(string Function, string Text)> CallsUsed(string masked, string line, Use use)
    {
        var used = new List<(string, string)>();
        if (use.AfterACall is null) return used;

        foreach (Match call in Called().Matches(masked))
        {
            var function = call.Groups["name"].Value;
            if (Keywords.Contains(function)) continue;

            var open = call.Index + call.Length - 1;
            var close = Brackets.Closing(masked, open);
            if (close < 0 || !use.AfterACall.IsMatch(masked[(close + 1)..])) continue;

            used.Add((function, line[call.Groups["name"].Index..(close + 1)]));
        }

        return used;
    }

    private static RootCause? FromName(ProgramRead program, Use use, string name, IReadOnlyList<ValueChange> changes, int crashLine, string type, string scope)
    {
        var last = changes[^1];
        var steps = new List<string> { $"The program stopped on line {crashLine}: it {string.Format(use.Doing, Code(name))}, but {Code(name)} is None there." };
        var evidence = new List<string>
        {
            $"A second run, recording each line as it ran, stopped on line {crashLine} with the same {type}.",
            $"{Code(name)} held None when line {crashLine} ran.",
        };

        int introduced;
        var involved = new List<int>();
        var path = new List<PathStep>();
        var checkFirst = $"check {Code(name)} is not None before line {crashLine} uses it";
        string whatToDo;
        var changeTheCause = false;
        var stopped = $"stops: it {string.Format(use.Doing, Code(name))}";

        if (last.Line is not { } made)
        {
            // Held None from the moment its function began: a parameter, given None by the call.
            if (scope == "<module>" || last.CalledFrom is not { } calledFrom || !program.IsParameter(scope, name)) return null;

            introduced = calledFrom;
            involved.Add(calledFrom);
            steps.Add($"{Code(name)} is a parameter of {Code(scope)}, and the call on line {calledFrom}, {Code(program.Text(calledFrom))}, gave it None.");
            evidence.Add($"{Code(name)} already held None as {Code(scope)} began, called from line {calledFrom}.");
            whatToDo = $"Make the call on line {calledFrom} give {Code(scope)} a value for {Code(name)}, or {checkFirst}.";
            var caller = program.FunctionOf(calledFrom);

            if (program.GaveNoneOn(calledFrom, except: scope) is { } given)
            {
                path.Add(new PathStep(0, caller, calledFrom, $"calls {Code(program.CallText(calledFrom, given.Function))}"));
                var ended = program.SayWhyItGaveNone(given, steps, evidence, involved, path, depth: 1);
                whatToDo = $"{ended} Or {checkFirst}.";
            }
            else if (program.PassedFor(calledFrom, scope, name) is { } passed && program.SetBefore(passed, caller, calledFrom) is { } set &&
                     program.AssignedFrom(set, passed) is { } right &&
                     (right == "None" || FunctionCalled().Match(right) is { Success: true } setBy && program.Defines(setBy.Groups["function"].Value) &&
                      program.GaveNoneOn(set, function: setBy.Groups["function"].Value) is not null))
            {
                // The caller's own name was given: the line that set it to None in the caller is where the value went wrong.
                introduced = set;
                involved.Insert(0, set);
                var (inCaller, ofCaller) = caller == "<module>" ? ("At the file's top level", "the file's top level") : ($"In {Code(caller)}", Code(caller));
                evidence.Add($"Line {set} was the one line of {ofCaller} that set {Code(passed)} and ran before line {calledFrom}.");

                if (right == "None")
                {
                    steps.Add($"{inCaller}, {Code(passed)} was set to None on line {set}: {Code(program.Text(set))}.");
                    whatToDo = $"Give {Code(passed)} a value on line {set}, or {checkFirst}.";
                }
                else
                {
                    var setGiven = program.GaveNoneOn(set, function: FunctionCalled().Match(right).Groups["function"].Value)!;
                    steps.Add($"{inCaller}, {Code(passed)} was set on line {set}, {Code(program.Text(set))}, and {Code(setGiven.Function)} gave back None.");
                    path.Add(new PathStep(0, caller, set, $"calls {Code(program.CallText(set, setGiven.Function))}"));
                    var ended = program.SayWhyItGaveNone(setGiven, steps, evidence, involved, path, depth: 1);
                    whatToDo = $"{ended} Or {checkFirst}.";
                }

                path.Add(new PathStep(0, caller, set, $"sets {Code(passed)} to None"));
            }

            path.Add(new PathStep(0, caller, calledFrom, $"calls {Code(program.CallText(calledFrom, scope))}, giving {Code(name)} None"));
            path.Add(new PathStep(1, scope, crashLine, stopped));
        }
        else
        {
            introduced = made;
            involved.Add(made);
            var right = program.AssignedFrom(made, name);
            evidence.Add($"Line {made} was the last line to change {Code(name)} before line {crashLine}.");
            whatToDo = $"Decide what the program should do when {Code(name)} is None: {checkFirst}.";

            if (right == "None")
            {
                steps.Add($"{Code(name)} was set to None on line {made}, {Code(program.Text(made))}, and nothing changed it after that.");
                path.Add(new PathStep(0, scope, made, $"sets {Code(name)} to None"));

                var neverRan = program.OtherLinesSetting(name, made, scope).Where(other => program.Trace.TimesRun.GetValueOrDefault(other) == 0).ToList();
                foreach (var other in neverRan)
                {
                    steps.Add(program.NeverRan(other, program.Trace.TimesRun, inThatCall: false));
                    evidence.Add($"Line {other} did not run at all.");
                }

                if (neverRan.Count > 0)
                    whatToDo = $"Give {Code(name)} a value for when {Lines(neverRan)} {(neverRan.Count == 1 ? "does" : "do")} not run, or {checkFirst}.";
            }
            else
            {
                steps.Add($"{Code(name)} became None on line {made}: {Code(program.Text(made))}.");

                if (right is not null && FunctionCalled().Match(right) is { Success: true } call && program.Defines(call.Groups["function"].Value) &&
                    program.GaveNoneOn(made, function: call.Groups["function"].Value) is { } given)
                {
                    path.Add(new PathStep(0, scope, made, $"calls {Code(program.CallText(made, given.Function))}"));
                    var ended = program.SayWhyItGaveNone(given, steps, evidence, involved, path, depth: 1);
                    path.Add(new PathStep(0, scope, made, $"sets {Code(name)} to None"));
                    whatToDo = $"{ended} Or {checkFirst}.";
                }
                else if (right is not null && Library(right, changes) is { } known)
                {
                    steps.Add(known.Said);
                    whatToDo = known.WhatToDo.Replace("{check}", checkFirst, StringComparison.Ordinal);
                    changeTheCause = known.ChangeTheCause;
                }
            }
        }

        // The line that stopped ends the path; in the parameter case it was put there with the call that gave the value.
        if (path.Count > 0 && path[^1].Line != crashLine) path.Add(new PathStep(0, scope, crashLine, stopped));

        return new RootCause
        {
            Name = name,
            Value = "None",
            CrashLine = crashLine,
            IntroducedLine = introduced,
            IntroducedCode = program.Text(introduced),
            Steps = steps,
            Evidence = evidence,
            LinesInvolved = involved,
            WhatToDo = whatToDo,
            ChangeTheCause = changeTheCause,
            Path = path.Count > 1 ? path : [],
        };
    }

    /// <summary>"line 5", "lines 5 and 8", "lines 5, 8 and 9".</summary>
    private static string Lines(IReadOnlyList<int> lines) => lines.Count == 1 ? $"line {lines[0]}" : $"lines {AndJoined(lines.Select(line => line.ToString()))}";

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string AndJoined(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count <= 1 ? string.Concat(all) : $"{string.Join(", ", all.Take(all.Count - 1))} and {all[^1]}";
    }

    /// <summary>The value was never kept under a name: a call on the line gave it, and the line used it straight away.</summary>
    private static RootCause? FromCall(ProgramRead program, Use use, List<(string Function, string Text)> callsUsed, int crashLine, string type)
    {
        var gave = callsUsed
            .Where(call => program.Defines(call.Function))
            .Select(call => (call.Text, Given: program.GaveNoneOn(crashLine, function: call.Function)))
            .Where(call => call.Given is not null)
            .ToList();
        if (gave is not [var (text, given)]) return null;

        var steps = new List<string> { $"The program stopped on line {crashLine}: it {string.Format(use.Doing, Code(text))}, but {Code(text)} gave back None." };
        var evidence = new List<string> { $"A second run, recording each line as it ran, stopped on line {crashLine} with the same {type}." };
        var involved = new List<int> { crashLine };
        var function = program.FunctionOf(crashLine);
        var path = new List<PathStep> { new(0, function, crashLine, $"calls {Code(program.CallText(crashLine, given!.Function))}") };
        var ended = program.SayWhyItGaveNone(given, steps, evidence, involved, path, depth: 1);
        path.Add(new PathStep(0, function, crashLine, $"stops: it {string.Format(use.Doing, Code(text))}"));

        return new RootCause
        {
            Name = text,
            Value = "None",
            CrashLine = crashLine,
            IntroducedLine = crashLine,
            IntroducedCode = program.Text(crashLine),
            Steps = steps,
            Evidence = evidence,
            LinesInvolved = involved,
            WhatToDo = $"{ended} Or keep what it gives back under a name, and check that is not None before line {crashLine} uses it.",
            Path = path,
        };
    }

    /// <summary>What Python's documentation says of a call that gave None, and what to do about it.</summary>
    /// <param name="WhatToDo">With {check} where "check it is not None before the line uses it" goes, when that is part of it.</param>
    private sealed record Documented(string Said, string WhatToDo, bool ChangeTheCause = false);

    /// <summary>
    /// What Python's own documentation says of the call that gave None, when it is one Python documents as giving None: a
    /// dictionary's get, re's match and search, a list's sort. Null for anything else.
    /// </summary>
    private static Documented? Library(string right, IReadOnlyList<ValueChange> changes)
    {
        if (DictionaryGet().Match(right) is { Success: true } get && !get.Groups["arguments"].Value.Contains(','))
        {
            return new Documented("A dictionary's get gives back None for a key it does not have.",
                "Give get a second value, to give back when the key is not there - or {check}.");
        }

        if (PatternSearch().Match(right) is { Success: true } search)
        {
            var said = search.Groups["function"].Value switch
            {
                "match" => "re.match gives back None when the start of the text does not match the pattern.",
                "search" => "re.search gives back None when the pattern is found nowhere in the text.",
                _ => "re.fullmatch gives back None when the whole text does not match the pattern.",
            };
            return new Documented(said, "Decide what the program should do when nothing matches, and {check}.");
        }

        // Its own method, called on what it held: what it held before says which kind of thing that was.
        if (MethodCalled().Match(right) is { Success: true } method && changes.Count >= 2 &&
            ChangesInPlace.TryGetValue(method.Groups["method"].Value, out var kind))
        {
            var before = changes[^2].Value;
            var name = method.Groups["method"].Value;
            var held = before.StartsWith('[') ? "list" : before.StartsWith('{') && (before == "{}" || before.Contains(':')) ? "dictionary"
                : before.StartsWith('{') || before.StartsWith("set(", StringComparison.Ordinal) ? "set" : null;
            if (held is null || held != kind && !(held == "dictionary" && name == "clear")) return null;

            return new Documented($"Before that it held {before}, and a {held}'s {name} changes the {held} itself and gives back None.",
                $"Call {name} on a line of its own, without keeping what it gives back: {Code(right)}.", ChangeTheCause: true);
        }

        return null;
    }

    private static string Code(string text) => $"`{text}`";

    private static string Times(int times) => times switch
    {
        1 => "once",
        2 => "twice",
        _ => $"{times} times",
    };

    /// <summary>The program's lines, read once, with what the run recorded about them.</summary>
    private sealed class ProgramRead(IReadOnlyList<string> lines, IReadOnlyList<string> masked, Trace trace)
    {
        public Trace Trace => trace;

        public string Text(int line) => lines[line - 1].Trim();

        /// <summary>The line each function of the file is defined on, for the names defined once.</summary>
        private readonly Dictionary<string, int> _defined = Enumerable.Range(0, masked.Count)
            .Select(index => (Index: index, Header: PythonCode.FunctionHeader().Match(masked[index])))
            .Where(each => each.Header.Success)
            .GroupBy(each => each.Header.Groups["name"].Value, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Index, StringComparer.Ordinal);

        public bool Defines(string function) => _defined.ContainsKey(function);

        public bool IsParameter(string function, string name) =>
            _defined.TryGetValue(function, out var header) &&
            PythonCode.SplitParameters(PythonCode.FunctionHeader().Match(masked[header]).Groups["parameters"].Value).Contains(name);

        /// <summary>The right side of the line's assignment to <paramref name="name"/>, masked, or null when the line is not one.</summary>
        public string? AssignedFrom(int line, string name) =>
            Regex.Match(masked[line - 1], $@"^\s*{Regex.Escape(name)}\s*(?::[^=]*)?=(?!=)\s*(?<right>.*\S)\s*$") is { Success: true } assignment
                ? lines[line - 1].Substring(assignment.Groups["right"].Index, assignment.Groups["right"].Length)
                : null;

        /// <summary>The function the line is in, or &lt;module&gt; for the file's own top level.</summary>
        public string FunctionOf(int line)
        {
            var header = PythonCode.EnclosingHeader(masked, line - 1, PythonCode.FunctionKeyword());
            return header < 0 ? "<module>" : PythonCode.FunctionHeader().Match(masked[header]).Groups["name"].Value;
        }

        /// <summary>The call to <paramref name="function"/> on the line, as written - <c>find_member(wanted)</c> - or the line itself when it is not found.</summary>
        public string CallText(int line, string function)
        {
            var call = Regex.Match(masked[line - 1], $@"(?<![\w.]){Regex.Escape(function)}\s*\(");
            if (!call.Success) return Text(line);
            var close = Brackets.Closing(masked[line - 1], call.Index + call.Length - 1);
            return close < 0 ? Text(line) : lines[line - 1][call.Index..(close + 1)];
        }

        /// <summary>
        /// The caller's own name given as <paramref name="parameter"/> in the call to <paramref name="function"/> on the line -
        /// by its place, or by name - or null when what was given there is not a plain name.
        /// </summary>
        public string? PassedFor(int line, string function, string parameter)
        {
            if (!_defined.TryGetValue(function, out var header)) return null;
            var parameters = PythonCode.SplitParameters(PythonCode.FunctionHeader().Match(masked[header]).Groups["parameters"].Value);
            if (parameters.FirstOrDefault() == "self") parameters.RemoveAt(0);
            var place = parameters.IndexOf(parameter);

            var call = Regex.Match(masked[line - 1], $@"(?<![\w])(?:self\.)?{Regex.Escape(function)}\s*\(");
            if (!call.Success || place < 0) return null;
            if (PythonCode.CallArguments(lines[line - 1], masked[line - 1], call.Index + call.Length - 1) is not { } arguments) return null;

            var named = arguments.Select(argument => Regex.Match(argument.Text, $@"^{Regex.Escape(parameter)}\s*=\s*(?<value>[A-Za-z_]\w*)$")).FirstOrDefault(each => each.Success);
            var given = named?.Groups["value"].Value ?? (place < arguments.Count && !arguments[place].Text.Contains('=') ? arguments[place].Text : null);
            return given is not null && Regex.IsMatch(given, @"^[A-Za-z_]\w*$") && !Keywords.Contains(given) ? given : null;
        }

        /// <summary>
        /// The one line of <paramref name="function"/> that sets <paramref name="name"/> before <paramref name="before"/> and
        /// ran - or null when none did, or more than one, and which one gave the value would be a guess.
        /// </summary>
        public int? SetBefore(string name, string function, int before)
        {
            var ran = OtherLinesSetting(name, besides: -1, function).Where(line => line < before && trace.TimesRun.GetValueOrDefault(line) > 0).ToList();
            return ran is [var only] ? only : null;
        }

        /// <summary>
        /// The other call of the file's whose None the call gave back on its return line: return find_member(wanted), or
        /// return found where the one line of the function that set found - and ran in that call - was found =
        /// find_member(wanted). The line it was called from, the name that held it, and the call.
        /// </summary>
        private (int SetLine, string? Held, NoneReturned Inner)? ReturnedFromACall(NoneReturned call)
        {
            var returned = Regex.Match(masked[call.Line - 1], @"^\s*return\s+(?<value>.*\S)\s*$");
            if (!returned.Success) return null;
            var value = lines[call.Line - 1].Substring(returned.Groups["value"].Index, returned.Groups["value"].Length);

            if (FunctionCalled().Match(value) is { Success: true } direct && Defines(direct.Groups["function"].Value) &&
                GaveNoneOn(call.Line, function: direct.Groups["function"].Value) is { } straight)
                return (call.Line, null, straight);

            if (!Regex.IsMatch(value, @"^[A-Za-z_]\w*$")) return null;
            var setting = OtherLinesSetting(value, besides: -1, call.Function)
                .Where(line => line < call.Line && call.TimesRun.GetValueOrDefault(line) > 0)
                .ToList();
            if (setting is not [var set] || AssignedFrom(set, value) is not { } right) return null;

            return FunctionCalled().Match(right) is { Success: true } setBy && Defines(setBy.Groups["function"].Value) &&
                   GaveNoneOn(set, function: setBy.Groups["function"].Value) is { } inner
                ? (set, value, inner)
                : null;
        }

        /// <summary>The last call to one of the file's functions, made from the line, that gave back None.</summary>
        public NoneReturned? GaveNoneOn(int line, string? function = null, string? except = null) =>
            trace.GaveNone.LastOrDefault(call => call.CalledFrom == line && (function is null || call.Function == function) && call.Function != except &&
                                                 Defines(call.Function) && lines[line - 1].Contains(call.Function + "(", StringComparison.Ordinal));

        /// <summary>The other lines in the same function, or at the file's top level, that give <paramref name="name"/> a value.</summary>
        public IEnumerable<int> OtherLinesSetting(string name, int besides, string scope)
        {
            var word = Regex.Escape(name);
            var header = scope == "<module>" ? -1 : _defined.GetValueOrDefault(scope, -2);
            if (header == -2) yield break;

            for (var index = 0; index < masked.Count; index++)
            {
                if (index + 1 == besides) continue;
                if (!Regex.IsMatch(masked[index], $@"^\s*(?:{word}\s*(?::[^=]*)?(?:[-+*/%]|//|\*\*)?=(?!=)|for\s+{word}\s+in\b)")) continue;
                if (PythonCode.EnclosingHeader(masked, index, PythonCode.FunctionKeyword()) == header) yield return index + 1;
            }
        }

        /// <summary>
        /// Says why the call gave back None, from what that call recorded: the line it ended on, and what never ran. Gives back
        /// what to change in the function for it to give back a value.
        /// </summary>
        public string SayWhyItGaveNone(NoneReturned call, List<string> steps, List<string> evidence, List<int> involved, List<PathStep> path, int depth)
        {
            var function = call.Function;
            var header = _defined[function];
            var (first, end) = PythonCode.BlockBody(lines, header);
            var returns = Enumerable.Range(first, end - first)
                .Where(index => ReturnLine().IsMatch(masked[index]) && PythonCode.EnclosingHeader(masked, index, PythonCode.FunctionKeyword()) == header)
                .Select(index => index + 1)
                .ToList();

            involved.Add(header + 1);
            if (!involved.Contains(call.Line)) involved.Add(call.Line);

            string whatToChange;

            if (returns.Count == 0)
            {
                steps.Add($"{Code(function)} has no return, so every call to it gives back None.");
                whatToChange = $"Give {Code(function)} a return for the value it should give back.";
            }
            else if (returns.Contains(call.Line))
            {
                steps.Add($"{Code(function)} gave back None on line {call.Line}: {Code(Text(call.Line))}.");
                whatToChange = $"Decide what {Code(function)} should give back on line {call.Line}, and whether its callers expect None.";
            }
            else
            {
                steps.Add($"{Code(function)} reached its end without a return - the last line it ran was line {call.Line}, {Code(Text(call.Line))} - and a function that reaches its end gives back None.");
                whatToChange = $"Give {Code(function)} a return at its end, for when it gets there.";
            }

            evidence.Add($"{Code(function)}, called from line {call.CalledFrom}, ended on line {call.Line} and gave back None.");

            if (call.Given.Count > 0) steps.Add($"It was called with {AndJoined(call.Given.Select(given => $"{given.Key} = {given.Value}"))}.");

            foreach (var notRun in returns.Where(line => line != call.Line && call.TimesRun.GetValueOrDefault(line) == 0))
            {
                steps.Add(NeverRan(notRun, call.TimesRun, inThatCall: true));
                evidence.Add($"Line {notRun} did not run in that call.");
            }

            // What it gave back was another of the file's calls' None - return found, where found = find_member(wanted), or
            // return find_member(wanted) itself: that call is followed in turn, after this one's own account, and what to
            // change is in it.
            if (returns.Contains(call.Line) && depth <= MostCallsFollowed && ReturnedFromACall(call) is var (setLine, held, inner))
            {
                evidence.Add($"{Code(inner.Function)}, called from line {setLine}, gave back None in that call.");
                steps.Add(held is null
                    ? $"Line {call.Line} gives back what {Code(inner.Function)} gave, and it gave back None."
                    : $"{Code(held)} was set on line {setLine}, {Code(Text(setLine))}, and {Code(inner.Function)} gave back None.");
                if (!involved.Contains(setLine)) involved.Add(setLine);
                path.Add(new PathStep(depth, function, setLine, $"calls {Code(CallText(setLine, inner.Function))}"));
                whatToChange = SayWhyItGaveNone(inner, steps, evidence, involved, path, depth + 1);
                if (held is not null) path.Add(new PathStep(depth, function, setLine, $"sets {Code(held)} to None"));
            }

            path.Add(new PathStep(depth, function, call.Line, returns.Contains(call.Line)
                ? $"gives back None: {Code(Text(call.Line))}"
                : "reaches its end without a return, so gives back None"));

            return whatToChange;
        }

        /// <summary>
        /// That a line never ran - and, when it is the first line under an if that did run, that the if was never true, which
        /// is the only way past it.
        /// </summary>
        public string NeverRan(int line, IReadOnlyDictionary<int, int> timesRun, bool inThatCall)
        {
            var where = inThatCall ? " in that call" : "";
            var said = $"Line {line}, {Code(Text(line))}, never ran{where}";

            if (IfAbove(line) is { } condition && timesRun.GetValueOrDefault(condition) is > 0 and var checks)
            {
                var keyword = Text(condition).StartsWith("elif", StringComparison.Ordinal) ? "elif" : "if";
                return $"{said}: the {keyword} on line {condition}, {Code(Text(condition))}, was checked {Times(checks)} and was never true.";
            }

            return said + ".";
        }

        /// <summary>The if or elif whose first statement the line is, or null.</summary>
        private int? IfAbove(int line)
        {
            var index = line - 1;
            var indent = CodeText.Indentation(masked[index]).Length;

            for (var above = index - 1; above >= 0; above--)
            {
                if (masked[above].Trim().Length == 0) continue;
                if (CodeText.Indentation(masked[above]).Length >= indent) return null;
                return IfHeader().IsMatch(masked[above]) && PythonCode.FirstStatement(lines, above + 1, index + 1) == index ? above + 1 : null;
            }

            return null;
        }
    }
}
