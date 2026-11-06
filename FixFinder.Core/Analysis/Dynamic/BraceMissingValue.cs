using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>One function or method of a file: its name, the line its header is on and the line its body ends on, 1-based.</summary>
internal sealed record BraceFunction(string Name, int Header, int End, IReadOnlyList<(string Type, string Name)> Parameters);

/// <summary>What a documented call says of the missing value it gave, and what to do about it.</summary>
/// <param name="WhatToDo">With {check} where "check it is there before the line uses it" goes.</param>
internal sealed record Documented(string Said, string WhatToDo);

/// <summary>What one language written in braces writes and says, for following a missing value through a copy of a program in it.</summary>
internal abstract partial class BraceDialect
{
    /// <summary>The error the language stops with: NullPointerException, TypeError.</summary>
    public abstract string ErrorName { get; }

    /// <summary>The language's words for a missing value: null; undefined and null.</summary>
    public abstract IReadOnlyList<string> MissingWords { get; }

    /// <summary>What a variable that is not missing holds, in words: "an object", "a value".</summary>
    public abstract string SomethingThere { get; }

    /// <summary>What a function that reaches its end without a return gives back, or null where that cannot happen.</summary>
    public abstract string? ReachingTheEndGives { get; }

    /// <summary>Whether a statement always ends in a semicolon, so a line without one is not a whole statement.</summary>
    public abstract bool StatementsEndInSemicolons { get; }

    /// <summary>The file's functions and methods, with the lines their bodies run over.</summary>
    public abstract IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked);

    /// <summary>A line that gives <paramref name="name"/> a value and holds nothing else, with what it gives as the group "right".</summary>
    public abstract Regex Sets(string name);

    /// <summary>What a line gives the name without saying so - <c>let best;</c> gives undefined - or null.</summary>
    public virtual string? SetsWithoutSaying(string masked, string name) => null;

    /// <summary>A statement that prints <paramref name="expression"/> where the program's own output will not be mistaken for it.</summary>
    public abstract string Print(string expression);

    /// <summary>An expression giving the missing value's word - null, undefined - or "value", for what the name holds.</summary>
    public abstract string MissingOrValue(string name);

    /// <summary>An expression giving a parameter's value as text.</summary>
    public abstract string Shown(string type, string name);

    /// <summary>A line no change should make run only sometimes: a return, a declaration, a block's first line.</summary>
    public abstract Regex NotAStatementToSkip { get; }

    /// <summary>What the language's documentation says of the call on the right of the line that gave the missing value, or null.</summary>
    public abstract Documented? Library(string right, BraceCode code);

    /// <summary>
    /// A line that gives a name a value and holds nothing else - x = 1;, total += mark;, i++; - with the name as "name" and, for
    /// a declaration, its type as "type": what the variable timeline prints a value after.
    /// </summary>
    public abstract Regex SetsAnyName { get; }

    /// <summary>A line that starts an if: <c>if (x)</c>, or Go's <c>if x {</c>.</summary>
    public virtual Regex IfLine => IfInBrackets();

    /// <summary>A line that starts an else if: <c>} else if (x)</c>, or Go's <c>} else if x {</c>.</summary>
    public virtual Regex ElseIfLine => ElseIfInBrackets();

    /// <summary>Whether a line of code, trimmed, ends a statement, so a line put after it runs after it.</summary>
    public virtual bool EndsAStatement(string code) => code.EndsWith(';') || code.EndsWith('{') || code.EndsWith('}');

    /// <summary>The first line of the check that runs a line only when the name holds a value.</summary>
    public virtual string GuardOpening(string name) => $"if ({name} != null) {{";

    /// <summary>What the copy needs put at the end of a line before it can print - Go's imports, on its package line - or nothing.</summary>
    public virtual IEnumerable<(int Line, string Added)> Additions(IReadOnlyList<string> masked) => [];

    /// <summary>How the language's comments and text are blanked when its lines are read: C's, or Lua's.</summary>
    public virtual Syntax Syntax => Syntax.CLike;

    /// <summary>Whether a line of code, trimmed, opens a block: it ends with {, or Lua's then.</summary>
    public virtual bool OpensABlock(string code) => code.EndsWith('{');

    /// <summary>Where a function's body starts on its header line, blanked - after its { - or null when it starts on the next.</summary>
    public virtual int? BodyStartsAfter(string masked) => masked.LastIndexOf('{') is var brace and >= 0 ? brace + 1 : null;

    /// <summary>A statement as it is put where a function's body starts: as it is, or after Ruby's ; when the header has no brackets.</summary>
    public virtual string OpeningStatement(string statement) => statement;

    /// <summary>A return with a print before it that is still one statement, where only one may stand: { print return x; }.</summary>
    public virtual string WrappedReturn(string print, string returned) => "{ " + print + " " + returned + " }";

    /// <summary>The line that ends the check that runs a line only when its value is there: }, or Lua's end.</summary>
    public virtual string GuardClosing => "}";

    /// <summary>What the copy puts before the program's first line - Lua's function that writes a value - or null.</summary>
    public virtual string? BeforeTheFirstLine => null;

    /// <summary>Text as the copy's prints write it: "text", or PHP's 'text', in which a $ is only a $.</summary>
    public virtual string Quoted(string text) => "\"" + text + "\"";

    /// <summary>What joins two pieces of text in the copy's prints: +, or PHP's dot.</summary>
    public virtual string Joining => " + ";

    /// <summary>
    /// Whether a line gives a name a function, whose value says nothing - what follows the = starts with function, (x) =&gt;,
    /// x =&gt; or Java's x -&gt; - and not a value a function worked out, as in members.find(member =&gt; member.id === 2).
    /// </summary>
    public virtual bool GivesAFunction(string masked) => FunctionAfterEquals().IsMatch(masked);

    [GeneratedRegex(@"(?<![=!<>])=(?![=>])\s*(?:async\s+)?(?:function\b|\([^()]*\)\s*(?::[^=]*)?(?:=>|->)|[A-Za-z_$][\w$]*\s*(?:=>|->))")]
    private static partial Regex FunctionAfterEquals();

    [GeneratedRegex(@"^\s*if\s*\(")]
    private static partial Regex IfInBrackets();

    [GeneratedRegex(@"^\s*\}?\s*else\s+if\s*\(")]
    private static partial Regex ElseIfInBrackets();
}

/// <summary>
/// Follows a missing value back through a copy of a program written in braces - Java, JavaScript - with a line printed
/// wherever the value could have come from: after each line that sets the variable, as each of the program's functions
/// begins, and at each of their returns and ifs, every one put on the line it is about, so no line moves. Only a copy that
/// stops the way the program did, on the same line, is read; nothing is said of one that did not.
/// </summary>
/// <summary>What a copy of a program that stopped on a missing value showed: where the value came from, and what each variable held.</summary>
public sealed record BraceTraced(RootCause? Cause, IReadOnlyList<VariableHistory> Timeline);

internal static class BraceMissingValue
{
    /// <summary>What every line the copy prints for FixFinder starts with, so the program's own output is never read as one.</summary>
    private const string Marker = "@@FixFinder@@";

    /// <summary>Between one parameter's value and the next on a line the copy prints.</summary>
    public const string Between = " @@ ";

    /// <param name="value">The missing value's word, as the error said it: null, undefined - or null when it did not say, and what the copy printed says.</param>
    /// <param name="name">The variable the error says was missing, or null when it was what a call gave back.</param>
    /// <param name="calledFunction">The function whose result was used straight away, when <paramref name="name"/> is null.</param>
    /// <param name="calledFrom">The line of the same file the function it stopped in was called from, when the trace says so.</param>
    /// <param name="stoppedTheSameWay">Whether the copy's error is the program's: same kind, same message.</param>
    public static async Task<BraceTraced?> TraceAsync(BraceDialect dialect, string program, SourceFile source, string? typed, int crashLine, int? calledFrom,
        string doing, string? value, string? name, string? calledFunction, Func<ParsedError, bool> stoppedTheSameWay, CancellationToken cancellationToken)
    {
        if (source.Line(crashLine) is null || name is null && (calledFunction is null || value is null)) return null;

        var code = new BraceCode(dialect, source.Lines, CodeText.MaskAll(source.Lines, dialect.Syntax));
        var stoppedIn = code.FunctionAround(crashLine);

        // A line inside the function it stopped in is not the call that began it - only a function that calls itself has one
        // there, and then which call gave the value cannot be told - so nothing is said of that call.
        if (calledFrom is { } from && stoppedIn is { } inside && inside.Header <= from && from <= inside.End) calledFrom = null;
        var marks = new Marks(dialect, source.Lines, code.Masked);
        foreach (var (line, added) in dialect.Additions(code.Masked)) marks.AtTheEnd(line, added);

        // The lines that set the variable where it stopped, and each of the program's functions: any of them may be the one
        // the missing value came from.
        foreach (var line in name is null ? [] : code.LinesSetting(name, stoppedIn))
            marks.After(line, dialect.Print(dialect.Quoted($"{Marker} set {line} ") + dialect.Joining + dialect.MissingOrValue(name!)), dialect.StatementsEndInSemicolons);

        var followed = code.Unique.Values.Where(each => !code.CallsItself(each)).ToList();
        foreach (var each in followed)
        {
            marks.AfterOpening(each.Header, dialect.Print(dialect.Quoted($"{Marker} call {each.Name}{Between}") + dialect.Joining + code.ParametersShown(each)));
            foreach (var line in code.Returns(each)) marks.WrapReturn(line, dialect.Print(dialect.Quoted($"{Marker} return {each.Name} {line}")));

            // A function that gives back a variable - return best; - says what each line setting it gave it, so where the
            // missing value it gave back came from can be told. Written as any value is, since its type may hold no null.
            foreach (var (line, variable) in code.LinesSettingWhatItReturns(each))
                marks.After(line, dialect.Print(dialect.Quoted($"{Marker} inner {line} ") + dialect.Joining + dialect.Shown(code.TypeOf(variable, "", each), variable)), dialect.StatementsEndInSemicolons);
        }

        // Each if once, whichever function it is in, so how many times it was checked is counted once.
        foreach (var line in followed.Select(each => (BraceFunction?)each).Append(stoppedIn).SelectMany(code.Ifs).Distinct())
            marks.Before(line, dialect.Print(dialect.Quoted($"{Marker} if {line}")));

        // The variable timeline: each value a variable is given where it stopped, printed after the line that gives it.
        foreach (var (line, variable, type) in code.LinesSettingAny(stoppedIn).Take(MostLinesFollowed))
            marks.After(line, dialect.Print(dialect.Quoted($"{Marker} value {line} {variable}{Between}") + dialect.Joining + dialect.Shown(code.TypeOf(variable, type, stoppedIn), variable)),
                dialect.StatementsEndInSemicolons);

        // Before everything else on its line, so it is there before any of the program runs: not on a #! line, which has to stay first.
        if (dialect.BeforeTheFirstLine is { } first && Enumerable.Range(1, source.Lines.Count).FirstOrDefault(line => !source.Lines[line - 1].StartsWith("#!", StringComparison.Ordinal)) is > 0 and var firstLine)
            marks.AtTheStart(firstLine, first);

        if (await Checking.FixRun.RunChangedAsync(source, marks.Lines, program, typed, cancellationToken) is not { } run) return null;
        if (run.Error is not { } error || !stoppedTheSameWay(error) || LocalFixContext.OwnFrame(error)?.Line != crashLine) return null;

        var events = run.Lines.Select(line => line.Text.TrimEnd('\r', '\n'))
            .Where(text => text.StartsWith(Marker + " ", StringComparison.Ordinal))
            .Select(text => text[(Marker.Length + 1)..])
            .ToList();
        var story = new Story(dialect, code, events, crashLine, value);
        var cause = name is not null ? story.FromName(name, doing, stoppedIn, calledFrom) : story.FromCall(calledFunction!, doing, value!);

        return new BraceTraced(cause, Timeline(events, stoppedIn, calledFrom));
    }

    /// <summary>How many lines of where it stopped have their value printed: enough for a beginner's program, not a long file's.</summary>
    private const int MostLinesFollowed = 60;

    /// <summary>
    /// What each variable held where the program stopped, in the order the copy printed it - from the call that began the
    /// function it stopped in, its parameters first - with the last <see cref="VariableHistory.MostKept"/> values of each.
    /// </summary>
    private static IReadOnlyList<VariableHistory> Timeline(List<string> events, BraceFunction? stoppedIn, int? calledFrom)
    {
        var start = stoppedIn is null ? 0 : events.FindLastIndex(said => said.StartsWith($"call {stoppedIn.Name}{Between}", StringComparison.Ordinal));
        if (start < 0) return [];

        var held = new Dictionary<string, List<VariableChange>>(StringComparer.Ordinal);
        var order = new List<string>();
        void Add(string variable, VariableChange change)
        {
            if (!held.TryGetValue(variable, out var changes))
            {
                held[variable] = changes = [];
                order.Add(variable);
            }

            changes.Add(change);
        }

        if (stoppedIn is not null)
        {
            foreach (var given in events[start][$"call {stoppedIn.Name}{Between}".Length..].Split(Between, StringSplitOptions.None))
                if (given.IndexOf(" = ", StringComparison.Ordinal) is > 0 and var equals)
                    Add(given[..equals], new VariableChange(null, Cut(given[(equals + 3)..]), calledFrom));
        }

        for (var index = stoppedIn is null ? 0 : start + 1; index < events.Count; index++)
        {
            if (stoppedIn is not null && events[index].StartsWith($"call {stoppedIn.Name}{Between}", StringComparison.Ordinal)) break;

            var said = events[index];
            if (!said.StartsWith("value ", StringComparison.Ordinal) || said.IndexOf(Between, StringComparison.Ordinal) is not (> 0 and var between)) continue;

            var parts = said[..between].Split(' ');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var line)) continue;
            Add(parts[2], new VariableChange(line, Cut(said[(between + Between.Length)..]), null));
        }

        return [.. order.Select(variable => held[variable] is var changes && changes.Count > VariableHistory.MostKept
            ? new VariableHistory(variable, changes[^VariableHistory.MostKept..], Cut: true)
            : new VariableHistory(variable, held[variable], Cut: false))];
    }

    private static string Cut(string value) => value.Length > 60 ? value[..60] : value;

    /// <summary>
    /// The change that stops the program stopping: line <paramref name="cause"/>.CrashLine runs only when the variable holds
    /// something. Null for a line that is not a statement of its own - a declaration, whose name the lines after it use, a
    /// return, a block's first line - since skipping one of those changes more than the line.
    /// </summary>
    public static LocalFix? GuardFor(BraceDialect dialect, string ruleId, RootCause cause, SourceFile source, string doing)
    {
        if (cause.ChangeTheCause || !Regex.IsMatch(cause.Name, @"^[A-Za-z_$][\w$]*$") || source.Line(cause.CrashLine) is not { } line) return null;

        var masked = CodeText.MaskAll(source.Lines, dialect.Syntax);
        var stopped = masked[cause.CrashLine - 1].TrimEnd();
        if (dialect.NotAStatementToSkip.IsMatch(stopped) || dialect.StatementsEndInSemicolons && !stopped.EndsWith(';')) return null;
        if (stopped.Count(c => c is '(') != stopped.Count(c => c is ')') || stopped.Count(c => c is '[') != stopped.Count(c => c is ']') ||
            stopped.Contains('{') || stopped.Contains('}') || stopped.EndsWith(',') || stopped.EndsWith('+')) return null;

        // Nothing before it left open: the line is the whole statement.
        var before = Enumerable.Range(0, cause.CrashLine - 1).Reverse().Select(index => masked[index].TrimEnd()).FirstOrDefault(text => text.Trim().Length > 0) ?? "{";
        if (!before.EndsWith(';') && !before.EndsWith('{') && !before.EndsWith('}') && (dialect.StatementsEndInSemicolons || Regex.IsMatch(before, @"[-+*/%=&|<>?:,(\[.]$"))) return null;

        var indent = CodeText.Indentation(line);
        var step = indent.Contains('\t') ? "\t" : new string(' ', IndentStep(source.Lines));

        return new LocalFix
        {
            RuleId = ruleId,
            Title = $"Run line {cause.CrashLine} only when {cause.Name} is not {cause.Value}",
            Explanation = $"Line {cause.CrashLine} {doing}, and that cannot be done to {cause.Value}. With the check, it runs only when {cause.Name} holds " +
                          $"{dialect.SomethingThere}. What the program should do instead when {cause.Name} is {cause.Value} - say so, or use a value of its own - is yours to decide.",
            File = source.Path,
            StartLine = cause.CrashLine,
            RemoveCount = 1,
            NewLines = [$"{indent}{dialect.GuardOpening(cause.Name)}", $"{indent}{step}{line.TrimStart()}", $"{indent}{dialect.GuardClosing}"],
            OnlyStopsTheFailure = true,
        };
    }

    /// <summary>
    /// How many spaces the file indents a block by: the least any of its lines is indented with spaces - two in a file written
    /// as Dart's guide writes it, four in most - or four for a file with none.
    /// </summary>
    private static int IndentStep(IReadOnlyList<string> lines) =>
        lines.Where(line => line.Trim().Length > 0 && line.StartsWith(' '))
            .Select(line => line.Length - line.TrimStart(' ').Length)
            .DefaultIfEmpty(4)
            .Min() is var least && least is >= 2 and <= 8 ? least : 4;

    public static string Code(string text) => $"`{text}`";

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

    /// <summary>
    /// The copy's lines, with what it prints put on the lines it is about, so no line changes its number - and before a comment
    /// at the end of a line, where it would not run.
    /// </summary>
    /// <param name="masked">The lines with their text and comments blanked, which say where each line's code ends.</param>
    private sealed class Marks(BraceDialect dialect, IReadOnlyList<string> original, IReadOnlyList<string> masked)
    {
        private readonly string[] _lines = [.. original];

        public IReadOnlyList<string> Lines => _lines;

        /// <summary>After the line's own statement; a semicolon first where the language lets a line end without one.</summary>
        public void After(int line, string statement, bool endsInSemicolon)
        {
            var (code, comment) = Split(line);
            _lines[line - 1] = code + (endsInSemicolon || code.EndsWith(';') ? " " : "; ") + statement + comment;
        }

        /// <summary>Puts the text before everything on the line, as it is.</summary>
        public void AtTheStart(int line, string added) => _lines[line - 1] = added + " " + _lines[line - 1];

        /// <summary>Puts the text straight after the line's code, as it is.</summary>
        public void AtTheEnd(int line, string added)
        {
            var (code, comment) = Split(line);
            _lines[line - 1] = code + added + comment;
        }

        /// <summary>
        /// The line as the copy has it so far, cut where its code ends: what is put after its statement goes before the comment
        /// the line ends with, which is as long as the original line is longer than its code.
        /// </summary>
        private (string Code, string Comment) Split(int line)
        {
            var text = _lines[line - 1].TrimEnd();
            var commentLength = original[line - 1].TrimEnd().Length - masked[line - 1].TrimEnd().Length;
            if (commentLength <= 0 || commentLength > text.Length) return (text, "");
            return (text[..^commentLength].TrimEnd(), " " + text[^commentLength..].TrimStart());
        }

        public void Before(int line, string statement)
        {
            var text = _lines[line - 1];
            _lines[line - 1] = CodeText.Indentation(text) + statement + " " + text.TrimStart();
        }

        /// <summary>
        /// Puts the statement where the function's body starts - just inside its opening brace, on the header's line or the
        /// next, or after Lua's header - found in the blanked line, so a brace in a comment or in text is passed over.
        /// </summary>
        public void AfterOpening(int header, string statement)
        {
            for (var line = header; line <= Math.Min(header + 1, _lines.Length); line++)
            {
                var text = _lines[line - 1];
                if ((text == original[line - 1] ? dialect.BodyStartsAfter(masked[line - 1]) : dialect.BodyStartsAfter(text)) is not { } at) continue;
                _lines[line - 1] = text[..at] + " " + dialect.OpeningStatement(statement) + text[at..];
                return;
            }
        }

        /// <summary><c>return x;</c> becomes <c>{ print; return x; }</c>, which still works where a lone statement has to be one.</summary>
        public void WrapReturn(int line, string statement)
        {
            var text = _lines[line - 1];
            if (text != original[line - 1]) return;

            // Found in the masked line, so a ; or a return in a comment or in text is passed over.
            var code = masked[line - 1];
            var at = code.IndexOf("return", StringComparison.Ordinal);
            var end = code.LastIndexOf(';');
            if (at < 0) return;
            if (end < at) end = code.TrimEnd().Length - 1;
            _lines[line - 1] = text[..at] + dialect.WrappedReturn(statement, text[at..(end + 1)]) + text[(end + 1)..];
        }
    }

    /// <summary>What the copy printed, read into what happened, in the program's own terms.</summary>
    private sealed class Story(BraceDialect dialect, BraceCode code, List<string> events, int crashLine, string? wordTheErrorSaid)
    {
        /// <summary>The missing value's word: what the error said, or what the copy printed when it did not say.</summary>
        private string value = wordTheErrorSaid ?? "";

        private string CopyStopped =>
            $"A copy of the program, printing a line wherever the {value} could come from, stopped on line {crashLine} with the same {dialect.ErrorName}.";

        public RootCause? FromName(string name, string doing, BraceFunction? stoppedIn, int? calledFrom)
        {
            var lastSet = events.Select((said, index) => (Said: said.Split(' '), Index: index)).LastOrDefault(each => each.Said is ["set", _, _]);

            // An error that does not say which missing value it was is answered by what the line that last set the name printed.
            if (wordTheErrorSaid is null)
            {
                if (lastSet.Said is not ["set", _, var printed] || !dialect.MissingWords.Contains(printed)) return null;
                value = printed;
            }

            var steps = new List<string> { $"The program stopped on line {crashLine}: it {string.Format(doing, Code(name))}, but {Code(name)} is {value} there." };
            var evidence = new List<string> { CopyStopped, $"{Code(name)} was {value} when line {crashLine} ran." };
            var involved = new List<int>();
            var checkFirst = $"check {Code(name)} is not {value} before line {crashLine} uses it";

            int introduced;
            string whatToDo;

            if (lastSet.Said is null)
            {
                // Nothing in the function set it: a parameter, given the value by the call.
                if (stoppedIn is null || !stoppedIn.Parameters.Any(parameter => parameter.Name == name) || calledFrom is not { } from) return null;

                introduced = from;
                involved.Add(from);
                steps.Add($"{Code(name)} is a parameter of {Code(stoppedIn.Name)}, and the call on line {from}, {Code(code.Text(from))}, gave it {value}.");
                evidence.Add($"No line of {Code(stoppedIn.Name)} set {Code(name)} before line {crashLine}: it held what the call on line {from} gave it.");
                whatToDo = $"Make the call on line {from} give {Code(stoppedIn.Name)} {dialect.SomethingThere} for {Code(name)}, or {checkFirst}.";

                // What gave the call its value, when one of the program's functions on that line did, just before this one began.
                var began = events.FindLastIndex(said => said.StartsWith($"call {stoppedIn.Name}{Between}", StringComparison.Ordinal));
                var giver = code.Unique.Values.FirstOrDefault(other => other.Name != stoppedIn.Name && began > 0 &&
                                                                        Regex.IsMatch(code.Text(from), $@"(?<![\w.$]){Regex.Escape(other.Name)}\s*\(") &&
                                                                        CallBefore(began, other.Name) is not null);
                if (giver is not null && CallBefore(began, giver.Name) is { } given)
                    whatToDo = $"{SayWhyItGaveIt(giver, given, steps, evidence, involved, from)} Or {checkFirst}.";
            }
            else
            {
                if (lastSet.Said[2] != value || !int.TryParse(lastSet.Said[1], out var made)) return null;

                introduced = made;
                involved.Add(made);
                var right = code.AssignedFrom(made, name);
                evidence.Add($"Line {made} was the last line to set {Code(name)} before line {crashLine}, and it set it to {value}.");
                whatToDo = $"Decide what the program should do when {Code(name)} is {value}: {checkFirst}.";

                if (right == value)
                {
                    steps.Add($"{Code(name)} was set to {value} on line {made}, {Code(code.Text(made))}, and nothing changed it after that.");

                    var neverRan = code.LinesSetting(name, stoppedIn).Where(line => line != made && !events.Any(said => said.StartsWith($"set {line} ", StringComparison.Ordinal))).ToList();
                    foreach (var line in neverRan)
                    {
                        steps.Add(NeverRan(line, events, inThatCall: false));
                        evidence.Add($"Line {line} did not run at all.");
                    }

                    if (neverRan.Count > 0)
                    {
                        whatToDo = $"Give {Code(name)} {dialect.SomethingThere} for when {AndJoined(neverRan.Select(line => $"line {line}"))} " +
                                   $"{(neverRan.Count == 1 ? "does" : "do")} not run, or {checkFirst}.";
                    }
                }
                else
                {
                    steps.Add($"{Code(name)} became {value} on line {made}: {Code(code.Text(made))}.");

                    if (right is not null && CalledFunction(right) is { } called && code.Unique.TryGetValue(called, out var function) &&
                        CallBefore(lastSet.Index, function.Name) is { } call)
                    {
                        whatToDo = $"{SayWhyItGaveIt(function, call, steps, evidence, involved, made)} Or {checkFirst}.";
                    }
                    else if (right is not null && dialect.Library(right, code) is { } documented)
                    {
                        steps.Add(documented.Said);
                        whatToDo = documented.WhatToDo.Replace("{check}", checkFirst, StringComparison.Ordinal);
                    }
                }
            }

            return new RootCause
            {
                Name = name,
                Value = value,
                CrashLine = crashLine,
                IntroducedLine = introduced,
                IntroducedCode = code.Text(introduced),
                Steps = steps,
                Evidence = evidence,
                LinesInvolved = involved,
                WhatToDo = whatToDo,
            };
        }

        /// <summary>The value was never kept under a name: a function of the program gave it, and the line used it straight away.</summary>
        public RootCause? FromCall(string functionName, string doing, string missing)
        {
            value = missing;
            if (!code.Unique.TryGetValue(functionName, out var function) || CallBefore(events.Count, functionName) is not { } call) return null;

            var steps = new List<string>
            {
                $"The program stopped on line {crashLine}: it {string.Format(doing, $"what {Code(functionName)} gave back")}, but {Code(functionName)} gave back {value}.",
            };
            var evidence = new List<string> { CopyStopped };
            var involved = new List<int> { crashLine };
            var whatToChange = SayWhyItGaveIt(function, call, steps, evidence, involved, crashLine);

            return new RootCause
            {
                Name = $"{functionName}(...)",
                Value = value,
                CrashLine = crashLine,
                IntroducedLine = crashLine,
                IntroducedCode = code.Text(crashLine),
                Steps = steps,
                Evidence = evidence,
                LinesInvolved = involved,
                WhatToDo = $"{whatToChange} Or keep what it gives back under a name, and check that is not {value} before line {crashLine} uses it.",
            };
        }

        /// <summary>The last call to the function before the event at <paramref name="before"/>, with the events of that call.</summary>
        private (string Given, IReadOnlyList<string> During, int? Returned)? CallBefore(int before, string function)
        {
            var start = -1;
            for (var index = Math.Min(before, events.Count) - 1; index >= 0; index--)
            {
                if (!events[index].StartsWith($"call {function}{Between}", StringComparison.Ordinal)) continue;
                start = index;
                break;
            }

            if (start < 0) return null;

            var during = new List<string>();
            int? returned = null;
            for (var index = start + 1; index < before && index < events.Count; index++)
            {
                if (events[index].StartsWith("call ", StringComparison.Ordinal) || events[index].StartsWith("set ", StringComparison.Ordinal)) break;
                if (events[index].StartsWith($"return {function} ", StringComparison.Ordinal) && int.TryParse(events[index].Split(' ')[2], out var line))
                {
                    returned = line;
                    break;
                }

                during.Add(events[index]);
            }

            return (events[start][$"call {function}{Between}".Length..], during, returned);
        }

        /// <summary>
        /// Says why the call gave back the missing value, from what that call printed: the return it ended on, or that it reached
        /// its end, and what never ran. Gives back what to change in the function for it to give back something.
        /// </summary>
        private string SayWhyItGaveIt(BraceFunction function, (string Given, IReadOnlyList<string> During, int? Returned) call, List<string> steps,
            List<string> evidence, List<int> involved, int calledFrom)
        {
            involved.Add(function.Header);
            string whatToChange;

            if (call.Returned is { } returned)
            {
                if (!involved.Contains(returned)) involved.Add(returned);
                steps.Add($"{Code(function.Name)} gave back {value} on line {returned}: {Code(code.Text(returned))}.");
                evidence.Add($"{Code(function.Name)}, called from line {calledFrom}, ended on line {returned}, and what it gave back was {value}.");
                whatToChange = $"Decide what {Code(function.Name)} should give back on line {returned}, and whether its callers expect {value}.";
            }
            else if (dialect.ReachingTheEndGives is { } endGives && endGives == value)
            {
                steps.Add($"{Code(function.Name)} reached its end without a return, and a function that does gives back {endGives}.");
                evidence.Add($"{Code(function.Name)}, called from line {calledFrom}, reached its end without running a return.");
                whatToChange = $"Give {Code(function.Name)} a return at its end, for when it gets there.";
            }
            else
            {
                steps.Add($"{Code(function.Name)} gave back {value}.");
                return $"Decide what {Code(function.Name)} should give back.";
            }

            var given = call.Given.Split(Between, StringSplitOptions.None).Where(part => part.Length > 0).Select(part => part.Length > 70 ? part[..70] + "..." : part).ToList();
            if (given.Count > 0) steps.Add($"It was called with {AndJoined(given)}.");

            if (call.Returned is { } gaveBack) SayWhereTheReturnedVariableGotIt(function, gaveBack, call.During, steps, evidence, involved);

            foreach (var line in code.Returns(function).Where(line => line != call.Returned))
            {
                steps.Add(NeverRan(line, call.During, inThatCall: true));
                evidence.Add($"Line {line} did not run in that call.");
            }

            return whatToChange;
        }

        /// <summary>
        /// For a return that gave back a variable of the function - return best; - the line that gave it the missing value in
        /// that call, from what the copy printed after each line setting it, and the lines setting it that never ran. Said only
        /// when the last of those lines to run gave it the missing value; the call's events reach its return, so none are missing.
        /// </summary>
        private void SayWhereTheReturnedVariableGotIt(BraceFunction function, int gaveBack, IReadOnlyList<string> during, List<string> steps,
            List<string> evidence, List<int> involved)
        {
            if (code.ReturnedName(gaveBack) is not { } returned || code.LinesSetting(returned, function).ToList() is not { Count: > 0 } setting) return;

            var ran = during.Select(said => said.Split(' ', 3))
                .Where(said => said is ["inner", _, _] && int.TryParse(said[1], out var line) && setting.Contains(line))
                .Select(said => (Line: int.Parse(said[1]), Value: said[2]))
                .ToList();
            if (ran.Count == 0 || ran[^1].Value != value) return;

            var made = ran[^1].Line;
            if (!involved.Contains(made)) involved.Add(made);
            steps.Add($"{Code(returned)} was set to {value} on line {made}, {Code(code.Text(made))}, and nothing changed it after that in that call.");
            evidence.Add($"Line {made} was the last line of {Code(function.Name)} to set {Code(returned)} in that call, and it set it to {value}.");

            foreach (var line in setting.Where(line => ran.All(each => each.Line != line)))
            {
                steps.Add(NeverRan(line, during, inThatCall: true));
                evidence.Add($"Line {line} did not run in that call.");
            }
        }

        /// <summary>
        /// That a line never ran - and, when it is the first line under an if that was checked, that the if was never true, which
        /// is the only way past it.
        /// </summary>
        private string NeverRan(int line, IReadOnlyList<string> during, bool inThatCall)
        {
            var said = $"Line {line}, {Code(code.Text(line))}, never ran{(inThatCall ? " in that call" : "")}";

            if (code.IfAbove(line) is { } condition && during.Count(each => each == $"if {condition}") is > 0 and var checks)
                return $"{said}: the if on line {condition}, {Code(code.Text(condition))}, was checked {Times(checks)} and was never true.";

            return said + ".";
        }

        /// <summary>
        /// The function of the program the right side is a call to - name(...), this.name(...) or Name.name(...), or
        /// object.name(...) where the file declares object as one of its own classes and name is a method of that class - or
        /// null. The call's brackets have to close at the end, so in find(list).get(0) the value is get's, not find's.
        /// </summary>
        private string? CalledFunction(string right)
        {
            if (Regex.Match(right, @"^(?:await\s+)?(?:(?<receiver>[A-Za-z_$][\w$]*)\.)?(?<function>[A-Za-z_$][\w$]*)\s*\(.*\)$") is not { Success: true } call) return null;

            var masked = CodeText.Mask(right, dialect.Syntax);
            var open = masked.IndexOf('(', call.Groups["function"].Index + call.Groups["function"].Length);
            if (open < 0 || Brackets.ClosingParenthesis(masked, open) != masked.TrimEnd().Length - 1) return null;

            var receiver = call.Groups["receiver"].Value;
            var function = call.Groups["function"].Value;
            if (receiver.Length == 0 || receiver == "this" || receiver[0] is >= 'A' and <= 'Z') return function;

            return code.Unique.TryGetValue(function, out var method) && code.ClassOf(receiver) is { } type && code.InClass(method, type) ? function : null;
        }
    }
}

/// <summary>A file's lines, read once: where its functions are, and what sets, returns and decides in each.</summary>
internal sealed partial class BraceCode(BraceDialect dialect, IReadOnlyList<string> lines, IReadOnlyList<string> masked)
{
    [GeneratedRegex(@"^\s*return\b[^;{}]*;?\s*$")]
    private static partial Regex ReturnStatement();

    [GeneratedRegex(@"^\s*return\s+(?<name>[A-Za-z_$][\w$]*)\s*;?\s*$")]
    private static partial Regex ReturnedVariable();

    public IReadOnlyList<string> Masked => masked;

    /// <summary>The line's code as written, without a comment it ends with - which the masked line, blank there, says.</summary>
    public string Text(int line)
    {
        var text = lines[line - 1].TrimEnd();
        var codeEnd = masked[line - 1].TrimEnd().Length;
        return (codeEnd > 0 && codeEnd < text.Length ? text[..codeEnd] : text).Trim();
    }

    private readonly List<BraceFunction> _all = [.. dialect.Functions(masked)];

    private Dictionary<string, BraceFunction>? _unique;

    /// <summary>The functions whose name only one function has, so a call by name is a call to it.</summary>
    public IReadOnlyDictionary<string, BraceFunction> Unique => _unique ??= _all
        .GroupBy(function => function.Name, StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

    /// <summary>The innermost function the line is in, or null for the file's own top level.</summary>
    public BraceFunction? FunctionAround(int line) =>
        _all.Where(function => function.Header <= line && line <= function.End).OrderByDescending(function => function.Header).FirstOrDefault();

    /// <summary>The lines of a function's body - or, for null, of the file's top level - that no function inside it has.</summary>
    private IEnumerable<int> OwnLines(BraceFunction? function)
    {
        var (first, last) = function is null ? (1, masked.Count) : (function.Header + 1, function.End - 1);

        for (var line = first; line <= last; line++)
        {
            var inside = _all.Any(other => other != function && other.Header <= line && line <= other.End &&
                                           (function is null || other.Header > function.Header));
            if (!inside) yield return line;
        }
    }

    /// <summary>
    /// The lines of a function, or the file's top level, that give a name a value and hold nothing else - each with the name
    /// and the type it is declared with there, if it is - leaving out a name given a function, whose value says nothing.
    /// </summary>
    public IEnumerable<(int Line, string Name, string Type)> LinesSettingAny(BraceFunction? function)
    {
        foreach (var line in OwnLines(function))
        {
            var text = masked[line - 1];
            if (dialect.SetsAnyName.Match(text) is not { Success: true } set) continue;
            if (text.Count(c => c == '(') != text.Count(c => c == ')') || text.Count(c => c == '{') != text.Count(c => c == '}')) continue;
            if (dialect.GivesAFunction(text)) continue;
            yield return (line, set.Groups["name"].Value, set.Groups["type"].Value);
        }
    }

    /// <summary>What a name is declared as - on the line, among the function's parameters, or on a line of it that declares it - or "".</summary>
    public string TypeOf(string name, string onTheLine, BraceFunction? function)
    {
        if (onTheLine.Length > 0 && onTheLine is not ("var" or "let" or "const" or "val" or "final")) return onTheLine;
        if (function?.Parameters.FirstOrDefault(parameter => parameter.Name == name) is { Name.Length: > 0 } parameter) return parameter.Type;

        return LinesSettingAny(function).Where(each => each.Name == name).Select(each => each.Type)
            .FirstOrDefault(type => type.Length > 0 && type is not ("var" or "let" or "const" or "val" or "final")) ?? "";
    }

    /// <summary>The lines that give the variable a value and hold nothing else: <c>member = findMember(members, 2);</c>.</summary>
    public IEnumerable<int> LinesSetting(string name, BraceFunction? function)
    {
        var sets = dialect.Sets(name);

        foreach (var line in OwnLines(function))
        {
            var text = masked[line - 1];
            if (!sets.IsMatch(text) && dialect.SetsWithoutSaying(text, name) is null) continue;
            if (text.Count(c => c == '(') != text.Count(c => c == ')') || text.Count(c => c == '{') != text.Count(c => c == '}')) continue;
            yield return line;
        }
    }

    /// <summary>The right side of the line that sets the variable, as written, or what it gives without saying so, or null.</summary>
    public string? AssignedFrom(int line, string name) =>
        dialect.Sets(name).Match(masked[line - 1]) is { Success: true } set
            ? lines[line - 1].Substring(set.Groups["right"].Index, set.Groups["right"].Length)
            : dialect.SetsWithoutSaying(masked[line - 1], name);

    /// <summary>Whether the function calls itself, which would mix one call's lines up with another's.</summary>
    public bool CallsItself(BraceFunction function) =>
        OwnLines(function).Any(line => Regex.IsMatch(masked[line - 1], $@"(?<![\w.$]){Regex.Escape(function.Name)}\s*\("));

    public IEnumerable<int> Returns(BraceFunction function) => OwnLines(function).Where(line => ReturnStatement().IsMatch(masked[line - 1]));

    /// <summary>The variable a return gives back, when that is all it gives - return best; - or null.</summary>
    public string? ReturnedName(int line) =>
        ReturnedVariable().Match(masked[line - 1]) is { Success: true } returned && returned.Groups["name"].Value is not ("null" or "nil" or "undefined" or "this" or "true" or "false")
            ? returned.Groups["name"].Value
            : null;

    /// <summary>Each line of the function setting a variable one of its returns gives back, with the variable.</summary>
    public IEnumerable<(int Line, string Name)> LinesSettingWhatItReturns(BraceFunction function) =>
        Returns(function).Select(ReturnedName).OfType<string>().Distinct()
            .SelectMany(name => LinesSetting(name, function).Select(line => (line, name)));

    /// <summary>The ifs of a function, or the file's top level, that start a line after a finished statement, so a line put before one runs where it does.</summary>
    public IEnumerable<int> Ifs(BraceFunction? function) =>
        OwnLines(function).Where(line => dialect.IfLine.IsMatch(masked[line - 1]) && EndsAStatement(line - 1));

    private bool EndsAStatement(int line)
    {
        for (var above = line - 1; above >= 0; above--)
        {
            var text = masked[above].TrimEnd();
            if (text.Trim().Length == 0) continue;
            return dialect.EndsAStatement(text);
        }

        return true;
    }

    /// <summary>The if or else if a line is the first statement under, when its block opens on its own line: the line that runs only when it is true.</summary>
    public int? IfAbove(int line)
    {
        var braceOnItsOwn = false;

        for (var above = line - 1; above >= 1; above--)
        {
            var text = masked[above - 1].Trim();
            if (text.Length == 0) continue;

            // if (x)  then  {  on the next line: the brace belongs to the if above it.
            if (text == "{" && !braceOnItsOwn)
            {
                braceOnItsOwn = true;
                continue;
            }

            var opens = braceOnItsOwn || dialect.OpensABlock(text);
            return (dialect.IfLine.IsMatch(masked[above - 1]) || dialect.ElseIfLine.IsMatch(masked[above - 1])) && opens ? above : null;
        }

        return null;
    }

    /// <summary>Each parameter as "name = value", in the program's language, for the copy to print as the function begins.</summary>
    public string ParametersShown(BraceFunction function) =>
        function.Parameters.Count == 0
            ? dialect.Quoted("")
            : string.Join(dialect.Joining + dialect.Quoted(BraceMissingValue.Between) + dialect.Joining,
                function.Parameters.Select(parameter => dialect.Quoted($"{parameter.Name} = ") + dialect.Joining + dialect.Shown(parameter.Type, parameter.Name)));

    /// <summary>
    /// What a name's declaration in the file says it is: its type, or what it is first given - or null. A TypeScript type
    /// after the name, as in <c>const books: Book[] = [...]</c>, is passed over.
    /// </summary>
    public string? DeclaredAs(string name) =>
        masked.Select(text => Regex.Match(text, $@"(?<type>\b[\w.$]+(?:<[^;=()]*>)?)\s+{Regex.Escape(name)}\s*(?::[^=;()]+)?(?<given>=\s*[^;]*|[;,)])")).FirstOrDefault(match => match.Success) is { } found
            ? found.Groups["type"].Value + (found.Groups["given"].Value.StartsWith('=') ? " " + found.Groups["given"].Value : "")
            : null;

    /// <summary>The file's own class an object is declared as - <c>Library library</c>, or <c>library = new Library()</c> - or null.</summary>
    public string? ClassOf(string name)
    {
        if (DeclaredAs(name) is not { } declared) return null;

        var made = Regex.Match(declared, @"\bnew\s+(?<type>[A-Z][\w$]*)");
        var type = made.Success ? made.Groups["type"].Value : Regex.Match(declared, @"^(?<type>[A-Z][\w$]*)").Groups["type"].Value;
        return type.Length > 0 && ClassLines(type) is not null ? type : null;
    }

    /// <summary>Whether the function is written inside the file's class of that name.</summary>
    public bool InClass(BraceFunction function, string type) =>
        ClassLines(type) is { } body && body.First < function.Header && function.End <= body.Last;

    /// <summary>The line the file's class of that name begins on and the line its body ends on, 1-based - or null.</summary>
    private (int First, int Last)? ClassLines(string type)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (!Regex.IsMatch(masked[index], $@"\bclass\s+{Regex.Escape(type)}\b")) continue;
            return Brackets.BlockEnd(masked, index) is { } end ? (index + 1, end + 1) : null;
        }

        return null;
    }
}
