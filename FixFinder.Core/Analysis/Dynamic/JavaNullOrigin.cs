using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Java program stopping on null: the variable Java's own message names, the line that gave it null,
/// and - when one of the program's own methods gave it - which return gave back null and why the others never ran.
/// </summary>
/// <remarks>
/// Java's message already names what was null, since FixFinder builds with -g; what it does not say is where that null came
/// from. A copy of the program is run with a line printed at each place the answer could be - after each line that sets
/// the variable, at each return and if of the method it came from, as that method begins - each put on the line it is
/// about, so every line keeps its number. Only a copy that stops with the same NullPointerException on the same line is
/// read, and nothing is said of a run that did not.
/// </remarks>
public static partial class JavaNullOrigin
{
    /// <summary>What every line the copy prints for FixFinder starts with, so the program's own output is never read as one.</summary>
    private const string Marker = "@@FixFinder@@";

    /// <summary>Between one parameter's value and the next on a line the copy prints.</summary>
    private const string Between = " @@ ";

    [GeneratedRegex(@"because ""(?<name>[A-Za-z_]\w*)"" is null$")]
    private static partial Regex NamedNull();

    [GeneratedRegex(@"because the return value of ""(?<owner>[\w.$]+)\.(?<method>[A-Za-z_]\w*)\((?<types>[^)]*)\)"" is null$")]
    private static partial Regex ReturnedNull();

    [GeneratedRegex(@"^Cannot invoke ""(?<type>[\w.$]+)\.(?<method>\w+)\((?<types>[^)]*)\)""")]
    private static partial Regex Invoking();

    [GeneratedRegex(@"^Cannot (?<what>read|assign) field ""(?<field>\w+)""")]
    private static partial Regex Field();

    [GeneratedRegex(@"^Cannot (?<what>load from|store to) \w+ array")]
    private static partial Regex ArrayItem();

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final|synchronized|abstract|native)\s+)*(?:<[^>]+>\s+)?[\w.$<>\[\],? ]+\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*(?:throws\s+[\w.,\s]+)?\{?\s*$")]
    private static partial Regex MethodHeader();

    [GeneratedRegex(@"^\s*return\b[^;]*;\s*$")]
    private static partial Regex ReturnStatement();

    [GeneratedRegex(@"^\s*if\s*\(")]
    private static partial Regex IfStatement();

    [GeneratedRegex(@"^\s*\}?\s*else\s+if\s*\(")]
    private static partial Regex ElseIf();

    [GeneratedRegex(@"^\s*(?:return|throw|break|continue|if|else|for|while|do|switch|case|default|try|catch|finally|synchronized|yield)\b")]
    private static partial Regex NotAPlainStatement();

    [GeneratedRegex(@"^\s*(?:final\s+)?[\w.$]+(?:<[^;=]*>)?(?:\[\])*\s+[A-Za-z_]\w*\s*=")]
    private static partial Regex Declaration();

    /// <summary>
    /// Runs a copy of the program with the places the null could have come from marked, and says where it came from - or
    /// null when the copy does not stop the same way, or does not show it.
    /// </summary>
    /// <param name="program">The file the program is run from.</param>
    /// <param name="file">The file it stopped in.</param>
    /// <param name="message">Java's message: Cannot invoke "java.util.Map.get(Object)" because "member" is null.</param>
    /// <param name="calledFrom">The line of the same file the method it stopped in was called from, when Java's trace says so.</param>
    public static async Task<RootCause?> TraceAsync(string program, string file, string? typed, string message, int crashLine, string method,
        int? calledFrom, CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is null) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = new JavaCode(source.Lines, masked);
        if (code.MethodAround(crashLine, method) is not { } stoppedIn) return null;

        var doing = DoingIn(message);
        var named = NamedNull().Match(message);
        var returned = ReturnedNull().Match(message);
        if (!named.Success && !returned.Success) return null;

        var marks = new Marks(source.Lines);

        // The lines that set the variable in the method it stopped in, and where each of the program's methods is: any of
        // them may be the one the null came from.
        var name = named.Success ? named.Groups["name"].Value : null;
        var setting = name is null ? [] : code.LinesSetting(name, stoppedIn).ToList();
        foreach (var line in setting) marks.After(line, $"System.err.println(\"{Marker} set {line} \" + ({name} == null));");

        var followed = code.Methods.Values.Where(each => !code.CallsItself(each)).ToList();
        foreach (var each in followed)
        {
            marks.AfterOpening(each.Header, $"System.err.println(\"{Marker} call {each.Name}{Between}\" + {code.ParametersShown(each)});");
            foreach (var line in code.Returns(each)) marks.WrapReturn(line, $"System.err.println(\"{Marker} return {each.Name} {line}\");");
        }

        // Each if once, whichever method it is in, so how many times it was checked is counted once.
        foreach (var line in followed.Append(stoppedIn).SelectMany(code.Ifs).Distinct()) marks.Before(line, $"System.err.println(\"{Marker} if {line}\");");

        if (await FixRun.RunChangedAsync(source, marks.Lines, program, typed, cancellationToken) is not { } run) return null;

        // Only a copy that stopped the way the program did, on the same line, says anything about it.
        if (run.Error is not { } error || error.ShortExceptionType != "NullPointerException" || error.Message != message ||
            LocalFixContext.OwnFrame(error)?.Line != crashLine)
        {
            return null;
        }

        var events = run.Lines.Select(line => line.Text.TrimEnd('\r', '\n')).Where(text => text.StartsWith(Marker + " ", StringComparison.Ordinal))
            .Select(text => text[(Marker.Length + 1)..]).ToList();
        var said = new Story(code, events, crashLine);

        return name is not null
            ? said.FromName(name, doing, stoppedIn, calledFrom)
            : said.FromCall(returned.Groups["method"].Value, returned.Groups["owner"].Value, doing);
    }

    /// <summary>
    /// The change that stops the program stopping: line <paramref name="crashLine"/> runs only when the variable is not null.
    /// Null for a line that is not a statement of its own - a declaration, whose name the lines after it use, a return, a
    /// block's first line - since skipping one of those changes more than the line.
    /// </summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing)
    {
        if (cause.ChangeTheCause || !Regex.IsMatch(cause.Name, @"^[A-Za-z_]\w*$") || source.Line(cause.CrashLine) is not { } line) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var stopped = masked[cause.CrashLine - 1].TrimEnd();
        if (NotAPlainStatement().IsMatch(stopped) || Declaration().IsMatch(stopped) || !stopped.EndsWith(';')) return null;
        if (stopped.Count(c => c is '(') != stopped.Count(c => c is ')') || stopped.Contains('{') || stopped.Contains('}')) return null;

        // Nothing before it left open: the line is the whole statement.
        var before = cause.CrashLine >= 2 ? masked[cause.CrashLine - 2].TrimEnd() : "{";
        if (before.Length > 0 && !before.EndsWith(';') && !before.EndsWith('{') && !before.EndsWith('}')) return null;

        var indent = CodeText.Indentation(line);
        var step = indent.Contains('\t') ? "\t" : "    ";

        return new LocalFix
        {
            RuleId = "java-null-guard",
            Title = $"Run line {cause.CrashLine} only when {cause.Name} is not null",
            Explanation = $"Line {cause.CrashLine} {doing}, and that cannot be done to null. With the check, it runs only when {cause.Name} refers to an " +
                          $"object. What the program should do instead when {cause.Name} is null - say so, or use a value of its own - is yours to decide.",
            File = source.Path,
            StartLine = cause.CrashLine,
            RemoveCount = 1,
            NewLines = [$"{indent}if ({cause.Name} != null) {{", $"{indent}{step}{line.TrimStart()}", $"{indent}}}"],
            OnlyStopsTheFailure = true,
        };
    }

    /// <summary>What the line that stopped did with the null, from the start of Java's message, with {0} where its name goes.</summary>
    public static string DoingIn(string message)
    {
        if (Invoking().Match(message) is { Success: true } invoking)
        {
            var called = invoking.Groups["method"].Value;
            return invoking.Groups["type"].Value.StartsWith("java.lang.", StringComparison.Ordinal) && called.EndsWith("Value", StringComparison.Ordinal)
                ? "uses {0} as a plain " + called[..^"Value".Length]
                : $"calls {called} on {{0}}";
        }

        if (Field().Match(message) is { Success: true } field)
            return field.Groups["what"].Value == "read" ? $"reads the field {field.Groups["field"].Value} of {{0}}" : $"sets the field {field.Groups["field"].Value} of {{0}}";

        if (ArrayItem().Match(message) is { Success: true } item)
            return item.Groups["what"].Value == "load from" ? "reads an item of {0}" : "stores an item in {0}";

        if (message.StartsWith("Cannot read the array length", StringComparison.Ordinal)) return "asks for the length of {0}";
        return "uses {0}";
    }

    private static string Code(string text) => $"`{text}`";

    private static string Times(int times) => times switch
    {
        1 => "once",
        2 => "twice",
        _ => $"{times} times",
    };

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string AndJoined(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count <= 1 ? string.Concat(all) : $"{string.Join(", ", all.Take(all.Count - 1))} and {all[^1]}";
    }

    /// <summary>One method of the file: its name, the line its header is on and the lines its body runs over, 1-based.</summary>
    private sealed record Method(string Name, int Header, int End, IReadOnlyList<(string Type, string Name)> Parameters);

    /// <summary>The file's lines, read once: where its methods are, and what sets, returns and decides in each.</summary>
    private sealed class JavaCode(IReadOnlyList<string> lines, IReadOnlyList<string> masked)
    {
        public IReadOnlyList<string> Lines => lines;

        public string Text(int line) => lines[line - 1].Trim();

        public IReadOnlyDictionary<string, Method> Methods { get; } = Read(masked);

        private readonly List<Method> _all = ReadAll(masked);

        private static List<Method> ReadAll(IReadOnlyList<string> masked)
        {
            var found = new List<Method>();

            for (var index = 0; index < masked.Count; index++)
            {
                if (MethodHeader().Match(masked[index]) is not { Success: true } header || header.Groups["name"].Value is "if" or "for" or "while" or "switch" or "catch") continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;

                var parameters = SplitParameters(header.Groups["parameters"].Value);
                found.Add(new Method(header.Groups["name"].Value, index + 1, end + 1, parameters));
            }

            return found;
        }

        private static Dictionary<string, Method> Read(IReadOnlyList<string> masked) =>
            ReadAll(masked).GroupBy(method => method.Name, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        private static List<(string Type, string Name)> SplitParameters(string parameters)
        {
            var split = new List<(string, string)>();
            var depth = 0;
            var start = 0;

            for (var i = 0; i <= parameters.Length; i++)
            {
                if (i < parameters.Length && parameters[i] == '<') depth++;
                else if (i < parameters.Length && parameters[i] == '>') depth--;
                else if (i == parameters.Length || parameters[i] == ',' && depth == 0)
                {
                    var one = Regex.Replace(parameters[start..i].Trim(), @"^(?:final\s+|@\w+\s+)*", "");
                    if (Regex.Match(one, @"^(?<type>.+?)\s+(?<name>[A-Za-z_]\w*)$") is { Success: true } parameter)
                        split.Add((parameter.Groups["type"].Value.Trim(), parameter.Groups["name"].Value));
                    start = i + 1;
                }
            }

            return split;
        }

        /// <summary>The method the line is in, of the name Java's trace gives - Members.main, or main.</summary>
        public Method? MethodAround(int line, string traced)
        {
            var name = traced[(traced.LastIndexOf('.') + 1)..];
            return _all.Where(method => method.Name == name && method.Header <= line && line <= method.End).OrderByDescending(method => method.Header).FirstOrDefault();
        }

        /// <summary>The lines in the method that give the variable a value and hold nothing else: <c>member = findMember(members, 2);</c>.</summary>
        public IEnumerable<int> LinesSetting(string name, Method method)
        {
            var word = Regex.Escape(name);

            for (var line = method.Header + 1; line < method.End; line++)
            {
                var text = masked[line - 1];
                if (!Regex.IsMatch(text, $@"^\s*(?:final\s+)?(?:[\w.$]+(?:<[^;=]*>)?(?:\[\])*\s+)?{word}\s*=(?!=)[^;]*;\s*$")) continue;
                if (text.Count(c => c == '(') != text.Count(c => c == ')')) continue;
                yield return line;
            }
        }

        /// <summary>The right side of the line that sets the variable, as written, or null.</summary>
        public string? AssignedFrom(int line, string name) =>
            Regex.Match(masked[line - 1], $@"^\s*(?:final\s+)?(?:[\w.$]+(?:<[^;=]*>)?(?:\[\])*\s+)?{Regex.Escape(name)}\s*=(?!=)\s*(?<right>[^;]*?)\s*;\s*$") is { Success: true } set
                ? lines[line - 1].Substring(set.Groups["right"].Index, set.Groups["right"].Length)
                : null;

        public bool IsParameter(Method method, string name) => method.Parameters.Any(parameter => parameter.Name == name);

        /// <summary>Whether the method calls itself, which would mix one call's lines up with another's.</summary>
        public bool CallsItself(Method method) =>
            Enumerable.Range(method.Header + 1, Math.Max(0, method.End - method.Header - 1))
                .Any(line => Regex.IsMatch(masked[line - 1], $@"(?<![\w.]){Regex.Escape(method.Name)}\s*\("));

        public IEnumerable<int> Returns(Method method) =>
            Enumerable.Range(method.Header + 1, Math.Max(0, method.End - method.Header - 1)).Where(line => ReturnStatement().IsMatch(masked[line - 1]));

        /// <summary>The if statements of the method that start a line after a finished statement, so a line put before one runs where it does.</summary>
        public IEnumerable<int> Ifs(Method method) =>
            Enumerable.Range(method.Header + 1, Math.Max(0, method.End - method.Header - 1))
                .Where(line => IfStatement().IsMatch(masked[line - 1]) && EndsAStatement(line - 1));

        private bool EndsAStatement(int line)
        {
            for (var above = line - 1; above >= 0; above--)
            {
                var text = masked[above].TrimEnd();
                if (text.Trim().Length == 0) continue;
                return text.EndsWith(';') || text.EndsWith('{') || text.EndsWith('}');
            }

            return true;
        }

        /// <summary>The first statement under an if on the line above, when there is one: the line that runs only when it is true.</summary>
        public int? IfAbove(int line)
        {
            for (var above = line - 1; above >= 1; above--)
            {
                var text = masked[above - 1].Trim();
                if (text.Length == 0) continue;
                return (IfStatement().IsMatch(masked[above - 1]) || ElseIf().IsMatch(masked[above - 1])) && text.EndsWith('{') ? above : null;
            }

            return null;
        }

        /// <summary>Each parameter as "name = value", in Java, for the copy to print as the method begins.</summary>
        public string ParametersShown(Method method)
        {
            if (method.Parameters.Count == 0) return "\"\"";

            return string.Join($" + \"{Between}\" + ", method.Parameters.Select(parameter =>
            {
                var value = parameter.Type.EndsWith("[][]", StringComparison.Ordinal) ? $"java.util.Arrays.deepToString({parameter.Name})"
                    : parameter.Type.EndsWith("[]", StringComparison.Ordinal) || parameter.Type.EndsWith("...", StringComparison.Ordinal) ? $"java.util.Arrays.toString({parameter.Name})"
                    : parameter.Type is "String" or "java.lang.String" ? $"({parameter.Name} == null ? \"null\" : \"\\\"\" + {parameter.Name} + \"\\\"\")"
                    : $"String.valueOf({parameter.Name})";
                return $"\"{parameter.Name} = \" + {value}";
            }));
        }

        /// <summary>The declared type of a name in the file, as its declaration writes it, or null.</summary>
        public string? DeclaredType(string name) =>
            masked.Select(text => Regex.Match(text, $@"(?<type>\b[\w.$]+(?:<[^;=()]*>)?)\s+{Regex.Escape(name)}\s*[=;,)]")).FirstOrDefault(match => match.Success)?.Groups["type"].Value;
    }

    /// <summary>The copy's lines, with what it prints put on the lines it is about, so no line changes its number.</summary>
    private sealed class Marks(IReadOnlyList<string> original)
    {
        private readonly string[] _lines = [.. original];

        public IReadOnlyList<string> Lines => _lines;

        public void After(int line, string statement) => _lines[line - 1] = _lines[line - 1].TrimEnd() + " " + statement;

        public void Before(int line, string statement)
        {
            var text = _lines[line - 1];
            var indent = CodeText.Indentation(text);
            _lines[line - 1] = indent + statement + " " + text.TrimStart();
        }

        /// <summary>Puts the statement just inside the method's opening brace, on the line the brace is on.</summary>
        public void AfterOpening(int header, string statement)
        {
            for (var line = header; line <= Math.Min(header + 1, _lines.Length); line++)
            {
                var brace = _lines[line - 1].IndexOf('{');
                if (brace < 0) continue;
                _lines[line - 1] = _lines[line - 1][..(brace + 1)] + " " + statement + _lines[line - 1][(brace + 1)..];
                return;
            }
        }

        /// <summary><c>return x;</c> becomes <c>{ print; return x; }</c>, which still works where a lone statement has to be one.</summary>
        public void WrapReturn(int line, string statement)
        {
            var text = _lines[line - 1];
            var at = text.IndexOf("return", StringComparison.Ordinal);
            var end = text.LastIndexOf(';');
            if (at < 0 || end < at) return;
            _lines[line - 1] = text[..at] + "{ " + statement + " " + text[at..(end + 1)] + " }" + text[(end + 1)..];
        }
    }

    /// <summary>What the copy printed, read into what happened, in the program's own terms.</summary>
    private sealed class Story(JavaCode code, List<string> events, int crashLine)
    {
        public RootCause? FromName(string name, string doing, Method stoppedIn, int? calledFrom)
        {
            var steps = new List<string> { $"The program stopped on line {crashLine}: it {string.Format(doing, Code(name))}, but {Code(name)} is null there." };
            var evidence = new List<string>
            {
                $"A copy of the program, printing a line wherever the null could come from, stopped on line {crashLine} with the same NullPointerException.",
                $"{Code(name)} was null when line {crashLine} ran.",
            };
            var involved = new List<int>();
            var checkFirst = $"check {Code(name)} is not null before line {crashLine} uses it";

            var lastSet = events.Select((said, index) => (Said: said.Split(' '), Index: index))
                .LastOrDefault(each => each.Said is ["set", _, _]);

            int introduced;
            string whatToDo;
            var changeTheCause = false;

            if (lastSet.Said is null)
            {
                // Nothing in the method set it: a parameter, given null by the call.
                if (!code.IsParameter(stoppedIn, name) || calledFrom is not { } from) return null;

                introduced = from;
                involved.Add(from);
                steps.Add($"{Code(name)} is a parameter of {Code(stoppedIn.Name)}, and the call on line {from}, {Code(code.Text(from))}, gave it null.");
                evidence.Add($"No line of {Code(stoppedIn.Name)} set {Code(name)} before line {crashLine}: it held what the call on line {from} gave it.");
                whatToDo = $"Make the call on line {from} give {Code(stoppedIn.Name)} an object for {Code(name)}, or {checkFirst}.";

                // What gave the call its null, when one of the program's methods on that line did, just before this one began.
                var began = events.FindLastIndex(said => said.StartsWith($"call {stoppedIn.Name}{Between}", StringComparison.Ordinal));
                var giver = code.Methods.Values.FirstOrDefault(other => other.Name != stoppedIn.Name &&
                                                                         Regex.IsMatch(code.Text(from), $@"(?<![\w.]){Regex.Escape(other.Name)}\s*\(") &&
                                                                         began > 0 && CallBefore(began, other.Name) is { Returned: not null });
                if (giver is not null && CallBefore(began, giver.Name) is { } given)
                    whatToDo = $"{SayWhyItGaveNull(giver, given, steps, evidence, involved, from)} Or {checkFirst}.";
            }
            else
            {
                if (lastSet.Said[2] != "true" || !int.TryParse(lastSet.Said[1], out var made)) return null;

                introduced = made;
                involved.Add(made);
                var right = code.AssignedFrom(made, name);
                evidence.Add($"Line {made} was the last line to set {Code(name)} before line {crashLine}, and it set it to null.");
                whatToDo = $"Decide what the program should do when {Code(name)} is null: {checkFirst}.";

                if (right == "null")
                {
                    steps.Add($"{Code(name)} was set to null on line {made}, {Code(code.Text(made))}, and nothing changed it after that.");

                    var neverRan = code.LinesSetting(name, stoppedIn).Where(line => line != made && !events.Contains($"set {line} true") && !events.Contains($"set {line} false")).ToList();
                    foreach (var line in neverRan)
                    {
                        steps.Add(NeverRan(line, events, inThatCall: null));
                        evidence.Add($"Line {line} did not run at all.");
                    }

                    if (neverRan.Count > 0)
                        whatToDo = $"Give {Code(name)} an object for when {AndJoined(neverRan.Select(line => $"line {line}"))} {(neverRan.Count == 1 ? "does" : "do")} not run, or {checkFirst}.";
                }
                else
                {
                    steps.Add($"{Code(name)} became null on line {made}: {Code(code.Text(made))}.");

                    if (right is not null && CalledMethod(right) is { } called && code.Methods.TryGetValue(called, out var method) &&
                        CallBefore(lastSet.Index, method.Name) is { } call)
                    {
                        whatToDo = $"{SayWhyItGaveNull(method, call, steps, evidence, involved, made)} Or {checkFirst}.";
                    }
                    else if (right is not null && MapGet(right) is { } map)
                    {
                        steps.Add($"{Code(map)} is declared a Map, and a Map's get gives back null for a key it does not have.");
                        whatToDo = $"Use getOrDefault, to give back a value of your own when the key is not there - or {checkFirst}.";
                    }
                }
            }

            return new RootCause
            {
                Name = name,
                Value = "null",
                CrashLine = crashLine,
                IntroducedLine = introduced,
                IntroducedCode = code.Text(introduced),
                Steps = steps,
                Evidence = evidence,
                LinesInvolved = involved,
                WhatToDo = whatToDo,
                ChangeTheCause = changeTheCause,
            };
        }

        /// <summary>The value was never kept under a name: a method of the program gave back null, and the line used it straight away.</summary>
        public RootCause? FromCall(string methodName, string owner, string doing)
        {
            if (!code.Methods.TryGetValue(methodName, out var method) || CallBefore(events.Count, methodName) is not { } call) return null;

            var text = $"{methodName}(...)";
            var steps = new List<string> { $"The program stopped on line {crashLine}: it {string.Format(doing, $"what {Code(methodName)} gave back")}, but {Code(methodName)} gave back null." };
            var evidence = new List<string> { $"A copy of the program, printing a line wherever the null could come from, stopped on line {crashLine} with the same NullPointerException." };
            var involved = new List<int> { crashLine };
            var whatToChange = SayWhyItGaveNull(method, call, steps, evidence, involved, crashLine);

            return new RootCause
            {
                Name = text,
                Value = "null",
                CrashLine = crashLine,
                IntroducedLine = crashLine,
                IntroducedCode = code.Text(crashLine),
                Steps = steps,
                Evidence = evidence,
                LinesInvolved = involved,
                WhatToDo = $"{whatToChange} Or keep what it gives back under a name, and check that is not null before line {crashLine} uses it.",
            };
        }

        /// <summary>The last call to the method before the event at <paramref name="before"/>, with the events of that call.</summary>
        private (string Given, IReadOnlyList<string> During, int? Returned)? CallBefore(int before, string method)
        {
            var start = -1;
            for (var index = Math.Min(before, events.Count) - 1; index >= 0; index--)
            {
                if (events[index].StartsWith($"call {method}{Between}", StringComparison.Ordinal))
                {
                    start = index;
                    break;
                }
            }

            if (start < 0) return null;

            var during = new List<string>();
            int? returned = null;
            for (var index = start + 1; index < before && index < events.Count; index++)
            {
                if (events[index].StartsWith("call ", StringComparison.Ordinal)) break;
                if (events[index].StartsWith($"return {method} ", StringComparison.Ordinal) && int.TryParse(events[index].Split(' ')[2], out var line))
                {
                    returned = line;
                    break;
                }

                during.Add(events[index]);
            }

            return (events[start][$"call {method}{Between}".Length..], during, returned);
        }

        /// <summary>Says why the call gave back null, from what that call printed: the return it ended on, and what never ran.</summary>
        private string SayWhyItGaveNull(Method method, (string Given, IReadOnlyList<string> During, int? Returned) call, List<string> steps, List<string> evidence,
            List<int> involved, int calledFrom)
        {
            involved.Add(method.Header);

            if (call.Returned is not { } returned)
            {
                steps.Add($"{Code(method.Name)} gave back null.");
                return $"Decide what {Code(method.Name)} should give back.";
            }

            if (!involved.Contains(returned)) involved.Add(returned);
            steps.Add($"{Code(method.Name)} gave back null on line {returned}: {Code(code.Text(returned))}.");
            evidence.Add($"{Code(method.Name)}, called from line {calledFrom}, ended on line {returned}, and what it gave back was null.");

            var given = call.Given.Split(Between, StringSplitOptions.None).Where(part => part.Length > 0).Select(part => part.Length > 70 ? part[..70] + "..." : part).ToList();
            if (given.Count > 0) steps.Add($"It was called with {AndJoined(given)}.");

            foreach (var line in code.Returns(method).Where(line => line != returned))
            {
                steps.Add(NeverRan(line, call.During, inThatCall: true));
                evidence.Add($"Line {line} did not run in that call.");
            }

            return $"Decide what {Code(method.Name)} should give back on line {returned}, and whether its callers expect null.";
        }

        /// <summary>
        /// That a line never ran - and, when it is the first line under an if that was checked, that the if was never true, which
        /// is the only way past it.
        /// </summary>
        private string NeverRan(int line, IReadOnlyList<string> during, bool? inThatCall)
        {
            var where = inThatCall == true ? " in that call" : "";
            var said = $"Line {line}, {Code(code.Text(line))}, never ran{where}";

            if (code.IfAbove(line) is { } condition && during.Count(each => each == $"if {condition}") is > 0 and var checks)
                return $"{said}: the if on line {condition}, {Code(code.Text(condition))}, was checked {Times(checks)} and was never true.";

            return said + ".";
        }

        private static string? CalledMethod(string right) =>
            Regex.Match(right, @"^(?:(?:this|[A-Z][\w$]*)\.)?(?<method>[a-z_]\w*)\s*\(.*\)$") is { Success: true } call ? call.Groups["method"].Value : null;

        /// <summary>The name a get is called on, when the file declares that name as a Map.</summary>
        private string? MapGet(string right) =>
            Regex.Match(right, @"^(?<receiver>[A-Za-z_]\w*)\.get\([^,()]*\)$") is { Success: true } get &&
            code.DeclaredType(get.Groups["receiver"].Value) is { } type && Regex.IsMatch(type, @"^(?:java\.util\.)?(?:Map|HashMap|TreeMap|LinkedHashMap)\b")
                ? get.Groups["receiver"].Value
                : null;
    }
}
