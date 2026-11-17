using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// How a run got to the line it stopped on, read from the calls its own stack trace listed: the program's outermost call
/// first, then each line that called the next function in, down to the line it stopped on.
/// </summary>
/// <remarks>
/// Only the program's own lines are shown, and only those the trace named. A call the trace leaves out - Lua and Elixir
/// leave out a function that ended by calling another, and Node lists ten calls at most - is never filled in: the step
/// before it then says what its line ran, and which function the trace went to next, rather than naming a call.
/// </remarks>
public static partial class CallPath
{
    /// <summary>The most steps shown - those nearest the line it stopped on, as a recursion can list thousands of calls.</summary>
    public const int MostSteps = 12;

    /// <summary>The fewest of the same call, one inside the next, that are shown as one step rather than one step each.</summary>
    private const int FewestRepeatsJoined = 3;

    /// <summary>The name the steps give the file's own top level, as the root cause's path does.</summary>
    private const string TopLevel = "<module>";

    /// <summary>One of the program's own lines the trace listed: the file, the line, the function it is in, and the line as written.</summary>
    private sealed record Stop(string File, int Line, string Function, string Code);

    /// <summary>
    /// A stop the trace listed one or more times in a row - a function calling itself from the same line - and how many more
    /// times the trace said it was repeated without listing it, as Python says "[Previous line repeated 1 more time]".
    /// </summary>
    private sealed record Repeated(Stop Stop, int Times)
    {
        public int SaidRepeatedMore { get; init; }
    }

    /// <summary>
    /// The way the run went, outermost call first, from <paramref name="error"/>'s trace - or empty when the trace listed fewer
    /// than two of the program's own lines, as one line alone is just the line it stopped on.
    /// </summary>
    /// <param name="programFiles">The program's own files, in full: a line in any other file is not the program's.</param>
    public static IReadOnlyList<PathStep> From(ParsedError error, IReadOnlyList<string> programFiles)
    {
        var fileLines = new Dictionary<string, string[]?>(StringComparer.OrdinalIgnoreCase);
        var innermostFirst = new List<Stop>();

        foreach (var (frame, symbol) in InFunctionOrder(error))
        {
            if (frame.Line is not > 0 || frame.Origin is FrameOrigin.Runtime or FrameOrigin.ThirdParty) continue;
            if (OwnFile(frame.File, programFiles) is not { } file) continue;
            if (LinesOf(file, fileLines) is not { } lines || frame.Line > lines.Length) continue;

            var stop = new Stop(file, frame.Line.Value, NameOf(symbol, lines), lines[frame.Line.Value - 1].Trim());

            // A call the trace lists at the line that made it, with no line of its own - Ruby lists Integer#/ at the line of
            // average that divides - is that line: the outer of the two, in the function the line is in, is the one kept.
            if (innermostFirst.Count > 0 && innermostFirst[^1] is var inner && SamePlace(inner, stop) && inner.Function != stop.Function)
                innermostFirst[^1] = stop;
            else
                innermostFirst.Add(stop);
        }

        innermostFirst.Reverse();
        var outermostFirst = Grouped(innermostFirst, RepeatedMore(error));
        if (outermostFirst.Count < 2) return [];

        var shown = outermostFirst.Skip(Math.Max(0, outermostFirst.Count - MostSteps)).ToList();
        var inMoreThanOneFile = shown.Select(repeated => repeated.Stop.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

        return shown.Select((repeated, index) =>
        {
            var happened = index == shown.Count - 1
                ? repeated.Times == 1 ? $"stops on {Code(repeated.Stop.Code)}" : $"{CallMadeBy(repeated.Stop, repeated.Stop)}{AndSoOn(repeated)}, and stops on the last of them"
                : $"{CallMadeBy(repeated.Stop, shown[index + 1].Stop)}{(repeated.Times == 1 ? "" : AndSoOn(repeated))}";

            return new PathStep(index, repeated.Stop.Function, repeated.Stop.Line, happened)
            {
                File = inMoreThanOneFile ? Path.GetFileName(repeated.Stop.File) : null,
            };
        }).ToList();
    }

    /// <summary>
    /// Each frame with the name of the function its line is in. PHP's trace names, at each line, the function called there
    /// rather than the one the line is in - "#1 scores.php(12): average(Array)" - so the function a line is in is the one the
    /// next frame out called, and the outermost frame's line is the file's top level.
    /// </summary>
    private static IEnumerable<(ErrorFrame Frame, string? Symbol)> InFunctionOrder(ParsedError error)
    {
        if (error.LanguageId != "php") return error.Frames.Select(frame => (frame, frame.Symbol));

        var listed = error.Frames.ToList();

        // Where it was thrown, when that is a function PHP itself has - intdiv - is the line that called it, listed again next.
        if (listed.Count >= 2 && listed[0].Symbol is null && listed[0].Line == listed[1].Line &&
            string.Equals(listed[0].File, listed[1].File, StringComparison.OrdinalIgnoreCase))
            listed.RemoveAt(0);

        return listed.Select((frame, index) => (frame, index + 1 < listed.Count ? listed[index + 1].Symbol : "{main}"));
    }

    /// <summary>
    /// The stops in order, with each run of the same stop - a function calling itself from one line - counted once, and what
    /// the trace said of the calls it did not list given to the first run listed at that line.
    /// </summary>
    private static List<Repeated> Grouped(IReadOnlyList<Stop> stops, Dictionary<(string FileName, int Line), int> repeatedMore)
    {
        var grouped = new List<Repeated>();

        foreach (var stop in stops)
        {
            if (grouped.Count > 0 && grouped[^1].Stop == stop) grouped[^1] = grouped[^1] with { Times = grouped[^1].Times + 1 };
            else grouped.Add(new Repeated(stop, 1));
        }

        for (var index = 0; index < grouped.Count; index++)
        {
            var place = (FileNameOf(grouped[index].Stop.File), grouped[index].Stop.Line);
            if (grouped[index].Times < FewestRepeatsJoined || !repeatedMore.Remove(place, out var more)) continue;
            grouped[index] = grouped[index] with { SaidRepeatedMore = more };
        }

        // Two of a call are shown as two steps; three or more as one, saying how many there were.
        return grouped.SelectMany(repeated => repeated.Times >= FewestRepeatsJoined ? [repeated] : Enumerable.Repeat(repeated with { Times = 1 }, repeated.Times)).ToList();
    }

    private static string AndSoOn(Repeated repeated) => repeated.SaidRepeatedMore == 0
        ? $", and so on - the trace lists {repeated.Times} of these calls, each inside the one before"
        : $", and so on - {repeated.Times + repeated.SaidRepeatedMore} of these calls, each inside the one before: the trace lists {repeated.Times} " +
          $"and says the line was repeated {repeated.SaidRepeatedMore} more {(repeated.SaidRepeatedMore == 1 ? "time" : "times")}";

    /// <summary>
    /// How many more times a Python traceback says a line was repeated than it listed - it lists the same call three times
    /// at most, then "[Previous line repeated 1 more time]" - by the file's name and the line.
    /// </summary>
    private static Dictionary<(string FileName, int Line), int> RepeatedMore(ParsedError error)
    {
        var repeatedMore = new Dictionary<(string FileName, int Line), int>();
        (string FileName, int Line)? lastListed = null;

        foreach (var text in error.RawText.Split('\n'))
        {
            if (PythonFrameLine().Match(text) is { Success: true } frame)
                lastListed = (FileNameOf(frame.Groups["file"].Value), int.Parse(frame.Groups["line"].Value));
            else if (PythonRepeatedLine().Match(text) is { Success: true } repeated && lastListed is { } place)
                repeatedMore[place] = repeatedMore.GetValueOrDefault(place) + int.Parse(repeated.Groups["more"].Value);
        }

        return repeatedMore;
    }

    /// <summary>A file's name from its path, written with either kind of slash - a trace's path need not be this computer's kind.</summary>
    private static string FileNameOf(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>
    /// What <paramref name="stop"/>'s line did to reach <paramref name="next"/>: the call itself, when the line holds one call
    /// of that function - "calls `average(scores)`" - or the line, and the function the trace went to next.
    /// </summary>
    private static string CallMadeBy(Stop stop, Stop next)
    {
        if (next.Function is "" or TopLevel) return $"runs {Code(stop.Code)}, which leads to {Who(next.Function)}";

        var masked = WithStringsBlanked(stop.Code);
        var calls = CallOf(next.Function).Matches(masked);

        if (calls.Count == 1 && Brackets.ClosingParenthesis(masked, calls[0].Index + calls[0].Length - 1) is { } close)
            return $"calls {Code(stop.Code[calls[0].Index..(close + 1)])}";

        // A line that is the call, written without brackets - Ruby's main, F#'s average scores.
        if (WholeWord(next.Function).Match(masked) is { Success: true, Index: 0 } && !masked.Contains('('))
            return $"calls {Code(stop.Code)}";

        return WholeWord(next.Function).IsMatch(masked)
            ? $"calls {Who(next.Function)} in {Code(stop.Code)}"
            : $"runs {Code(stop.Code)}, which leads to {Who(next.Function)}";
    }

    private static string Who(string function) => function switch
    {
        "" => "a function with no name",
        TopLevel => "the top level",
        _ => $"`{function}()`",
    };

    private static string Code(string text) => $"`{text}`";

    /// <summary>A call of <paramref name="function"/> with what it is called on - average(, self.average(, $this->average(, Scores::average(.</summary>
    private static Regex CallOf(string function) =>
        new($@"(?<![\w$])(?:[\w$]+(?:\.|->|::|:))*{Regex.Escape(function)}\s*\(", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static Regex WholeWord(string function) => new($@"(?<![\w$]){Regex.Escape(function)}(?![\w$])", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>The line with what is inside its quotes blanked, the same length, so a bracket in a string is not taken for one of the call's.</summary>
    private static string WithStringsBlanked(string code)
    {
        var blanked = code.ToCharArray();
        char? quote = null;

        for (var i = 0; i < blanked.Length; i++)
        {
            var c = blanked[i];

            if (quote is null)
            {
                if (c is '"' or '\'' or '`') quote = c;
                continue;
            }

            if (c == '\\' && i + 1 < blanked.Length)
            {
                blanked[i] = blanked[i + 1] = ' ';
                i++;
            }
            else if (c == quote) quote = null;
            else blanked[i] = ' ';
        }

        return new string(blanked);
    }

    /// <summary>
    /// The one file of the program a trace's file is: the same full path, or the one program file whose path ends in the
    /// path the trace gave - "Scores.java", or src/scores.py - or, for a full path elsewhere, a copy FixFinder built the
    /// program in, the one program file with its name.
    /// </summary>
    private static string? OwnFile(string? file, IReadOnlyList<string> programFiles)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        var wanted = file.Replace('\\', '/');
        var exact = programFiles.FirstOrDefault(own => string.Equals(own.Replace('\\', '/'), wanted, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var ending = Path.IsPathRooted(file) ? Path.GetFileName(wanted) : wanted.TrimStart('.', '/');
        var endingThere = programFiles.Where(own => own.Replace('\\', '/').EndsWith("/" + ending, StringComparison.OrdinalIgnoreCase)).ToList();
        return endingThere.Count == 1 ? endingThere[0] : null;
    }

    private static string[]? LinesOf(string file, Dictionary<string, string[]?> fileLines)
    {
        if (fileLines.TryGetValue(file, out var known)) return known;

        try
        {
            return fileLines[file] = File.ReadAllLines(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fileLines[file] = null;
        }
    }

    private static bool SamePlace(Stop first, Stop second) =>
        first.Line == second.Line && string.Equals(first.File, second.File, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The function a frame's line is in, from the name the trace gave it - Scores.average, Program.Average(List`1 values),
    /// main.average, Scores.average/1, local 'main' - as the program names it: average. The file's top level is &lt;module&gt;,
    /// and a function with no name - a lambda, a closure, Go's func1 - is "".
    /// </summary>
    internal static string NameOf(string? symbol, IReadOnlyList<string> lines)
    {
        if (symbol is null) return "";

        var name = symbol.Trim();

        // Elixir's name for a function written without one, in the function it is written in: anonymous fn/0 in Scores.main/0.
        if (name.StartsWith("anonymous fn", StringComparison.Ordinal)) return "";
        if (DotNetLocalFunction().Match(name) is { Success: true } local) return local.Groups["name"].Value;
        if (IsTopLevel(name)) return TopLevel;

        // Lua names a function by how it was reached - local 'main', upvalue 'average' - or, for one it has no name for, by
        // the line it was written on.
        if (LuaWrittenAt().Match(name) is { Success: true } written) return NameWrittenOn(lines, int.Parse(written.Groups["line"].Value));
        if (LuaReachedBy().Match(name) is { Success: true } reached) name = reached.Groups["name"].Value;

        name = HowItWasCalled().Replace(name, "");
        if (IsTopLevel(name)) return TopLevel;

        name = WithoutArguments(name);
        name = Arity().Replace(name, "");

        // Java's name for a constructor: the class it makes.
        if (name.EndsWith(".<init>", StringComparison.Ordinal)) name = name[..^".<init>".Length];

        var cut = name.LastIndexOfAny(['.', ':', '#', '>', '$']);
        if (cut >= 0 && cut < name.Length - 1) name = name[(cut + 1)..];

        return FunctionName().IsMatch(name) && !NoName().IsMatch(name) ? name : "";
    }

    private static bool IsTopLevel(string name) =>
        name is "<module>" or "<main>" or "{main}" or "main chunk" or "(file)" or "Object.<anonymous>" or "<top (required)>" ||
        name.Contains("<StartupCode$", StringComparison.Ordinal) || DotNetTopLevel().IsMatch(name);

    /// <summary>The name a line written in Lua gives the function it starts: function average(, local function average(, Account.deposit = function(.</summary>
    private static string NameWrittenOn(IReadOnlyList<string> lines, int line)
    {
        if (line < 1 || line > lines.Count) return "";

        var written = LuaFunctionStarts().Match(lines[line - 1]);
        if (!written.Success) return "";

        var name = written.Groups["named"].Success ? written.Groups["named"].Value : written.Groups["assigned"].Value;
        var cut = name.LastIndexOfAny(['.', ':']);
        return cut >= 0 ? name[(cut + 1)..] : name;
    }

    /// <summary>The name without the parameters or arguments a trace lists after it: Program.Average(List`1 values) is Program.Average.</summary>
    private static string WithoutArguments(string name)
    {
        if (!name.EndsWith(')')) return name;

        var depth = 0;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] == ')') depth++;
            else if (name[i] == '(' && --depth == 0) return name[..i].TrimEnd();
        }

        return name;
    }

    /// <summary>A C# local function, which .NET names after the method it is in: Program.&lt;&lt;Main&gt;$&gt;g__FindMember|0_0.</summary>
    [GeneratedRegex(@"g__(?<name>[A-Za-z_]\w*)\|")]
    private static partial Regex DotNetLocalFunction();

    /// <summary>The method .NET runs a C# file's top-level statements as: Program.&lt;Main&gt;$(String[] args).</summary>
    [GeneratedRegex(@"(?:^|\.)<Main>\$(?:\(|$)")]
    private static partial Regex DotNetTopLevel();

    [GeneratedRegex(@"^\s*File ""(?<file>[^""]+)"", line (?<line>\d+), in ")]
    private static partial Regex PythonFrameLine();

    [GeneratedRegex(@"^\s*\[Previous line repeated (?<more>\d+) more times?\]\s*$")]
    private static partial Regex PythonRepeatedLine();

    [GeneratedRegex(@"^function <(?<file>.+):(?<line>\d+)>$")]
    private static partial Regex LuaWrittenAt();

    [GeneratedRegex(@"^(?:local|global|function|upvalue|method|field|for iterator) '(?<name>[^']+)'$")]
    private static partial Regex LuaReachedBy();

    [GeneratedRegex(@"\bfunction\s+(?<named>[\w.:]+)\s*\(|(?<assigned>[\w.]+)\s*=\s*function\b")]
    private static partial Regex LuaFunctionStarts();

    /// <summary>What a trace puts before a function's name to say how it was running: async, new, block in, rescue in.</summary>
    [GeneratedRegex(@"^(?:async |new |(?:block|rescue|ensure)(?: \(\d+ levels\))? in )+")]
    private static partial Regex HowItWasCalled();

    /// <summary>How many arguments Elixir and Erlang say a function takes: Scores.average/1.</summary>
    [GeneratedRegex(@"/\d+$")]
    private static partial Regex Arity();

    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_]*[!?']*$")]
    private static partial Regex FunctionName();

    /// <summary>A name a language gives a function written without one: Go's func1, Java's lambda$main$0, JavaScript's anonymous.</summary>
    [GeneratedRegex(@"^(?:func\d+|lambda|anonymous|closure)$|^lambda\$", RegexOptions.IgnoreCase)]
    private static partial Regex NoName();
}
