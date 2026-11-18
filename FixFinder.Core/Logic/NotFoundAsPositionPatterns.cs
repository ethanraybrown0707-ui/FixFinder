using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A not-found answer used as a position: a function of the program's answers -1 when it finds nothing - <c>return -1</c>
/// after a loop that returns the place it finds - and a caller reads a list at its answer with nothing checking for -1
/// first: <c>place = find(names, "dan")</c>, then <c>names[place]</c>. What -1 does is the language's: Python, Ruby, Perl
/// and PowerShell read the last item, JavaScript reads undefined, Lua nil, PHP warns of a key the array lacks, and Java,
/// C#, Go, Rust, Dart, Scala and C++'s at stop the program; C reads before the list.
/// </summary>
/// <remarks>
/// The answer is read at the position straight from the call - <c>names[find(names, "dan")]</c> - or through a variable
/// given it, whose first use after is the read: a line comparing it, or testing it in a condition, checks it, and the
/// read is left alone. A function answering -1 is one with a line returning it - <c>return -1</c>, a last line of -1 in
/// Ruby, Rust and Scala, Fortran's <c>find = -1</c>. Scala's and Fortran's reads look like calls, so only a list the file
/// declares is taken as one. F#, OCaml, Elixir and Erlang answer not found with an option or an atom, which a caller has
/// to match, and have no check of it.
/// </remarks>
public static partial class NotFoundAsPositionPatterns
{
    /// <summary>
    /// How one language reads a list at a position and gives a variable a value: <c>Given</c> gives the name <c>{V}</c> the
    /// result of a call, the called function in <c>called</c>; <c>Read</c> reads the list <c>list</c> at the place
    /// <c>{P}</c>; <c>What</c> is what -1 does there.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, string Given, string Read, string What,
        bool IgnoreCase = false, bool ListsDeclared = false);

    private const string Brackets = @"(?<![\w$])(?<list>\$?(?>[A-Za-z_]\w*))\s*\[\s*{P}\s*(?:as\s+usize\s*)?\]";

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py"), Syntax.Python, @"^\s*(?<name>(?>[A-Za-z_]\w*))\s*=\s*(?<called>(?>[\w.]+))\s*\(", Brackets,
            "Python reads -1 as the last item, so the last one is used as if it were found"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, @"^\s*(?:final\s+)?(?:int|var|Integer)\s+(?<name>(?>\w+))\s*=\s*(?<called>(?>[\w.]+))\s*\(",
            Brackets + @"|(?<![\w$])(?<list>(?>[A-Za-z_]\w*))\s*\.\s*(?:get|charAt)\s*\(\s*{P}\s*\)", "Java stops with ArrayIndexOutOfBoundsException"),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, @"^\s*(?:int|var)\s+(?<name>(?>\w+))\s*=\s*(?<called>(?>[\w.]+))\s*\(",
            Brackets + @"|(?<![\w$])(?<list>(?>[A-Za-z_]\w*))\s*\.\s*ElementAt\s*\(\s*{P}\s*\)", "C# stops with IndexOutOfRangeException"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, @"^\s*(?:const|let|var)\s+(?<name>(?>[\w$]+))\s*(?::[^=]+)?=\s*(?<called>(?>[\w$.]+))\s*\(",
            Brackets, "JavaScript reads -1 as a key the list does not have, and gives undefined"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, @"^\s*(?<name>(?>\w+))\s*:?=\s*(?<called>(?>[\w.]+))\s*\(", Brackets,
            "Go stops with index out of range [-1]"),
        new("c", CodePattern.Files(".c"), Syntax.CLike, @"^\s*(?:const\s+)?(?:int|long|short)\s+(?<name>(?>\w+))\s*=\s*(?<called>(?>\w+))\s*\(", Brackets,
            "C reads before the start of the list, which it leaves undefined"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike,
            @"^\s*(?:const\s+)?(?:int|long|short|auto)\s+(?<name>(?>\w+))\s*=\s*(?<called>(?>[\w:]+))\s*\(",
            Brackets + @"|(?<![\w$])(?<list>(?>[A-Za-z_]\w*))\s*\.\s*at\s*\(\s*{P}\s*\)",
            "C++ stops with out_of_range where the list is read with at, and reads before its start with []"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, @"^\s*let\s+(?:mut\s+)?(?<name>(?>\w+))\s*(?::[^=]+)?=\s*(?<called>(?>[\w:]+))\s*\(", Brackets,
            "Rust stops with an index out of bounds, as -1 made a position is the largest there is"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, @"^\s*(?<name>(?>[a-z_]\w*))\s*=\s*(?<called>(?>[\w.]+))\s*[\s(]", Brackets,
            "Ruby reads -1 as the last item, so the last one is used as if it were found"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, @"^\s*(?<name>\$(?>\w+))\s*=\s*(?<called>(?>\w+))\s*\(", Brackets,
            "PHP warns of a key the array does not have, and gives null", IgnoreCase: true),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, @"^\s*(?:local\s+)?(?<name>(?>\w+))\s*=\s*(?<called>(?>[\w.:]+))\s*\(", Brackets,
            "Lua reads -1 as a key the table does not have, and gives nil"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, @"^\s*(?:my\s+)?(?<name>\$(?>\w+))\s*=\s*&?(?<called>(?>\w+))\s*\(",
            @"(?<![\w$@])\$(?<list>(?>[A-Za-z_]\w*))\s*\[\s*{P}\s*\]|(?<![\w$@])(?<list>\$(?>[A-Za-z_]\w*))\s*->\s*\[\s*{P}\s*\]",
            "Perl reads -1 as the last item, so the last one is used as if it were found"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, @"^\s*(?:final|var|int)\s+(?<name>(?>\w+))\s*=\s*(?<called>(?>[\w.]+))\s*\(", Brackets,
            "Dart stops with a RangeError"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, @"^\s*(?<name>\$(?>\w+))\s*=\s*(?<called>(?>[\w-]+))\b", Brackets,
            "PowerShell reads -1 as the last item, so the last one is used as if it were found", IgnoreCase: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, @"^\s*(?:val|var)\s+(?<name>(?>\w+))\s*(?::[^=]+)?=\s*(?<called>(?>[\w.]+))\s*\(",
            @"(?<![\w$.])(?<list>(?>[A-Za-z_]\w*))\s*\(\s*{P}\s*\)", "Scala stops with ArrayIndexOutOfBoundsException", ListsDeclared: true),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, @"^\s*(?<name>(?>[A-Za-z]\w*))\s*=\s*(?<called>(?>\w+))\s*\(",
            @"(?<![\w$%])(?<list>(?>[A-Za-z]\w*))\s*\(\s*{P}\s*\)",
            "Fortran reads outside the array - built with bounds checked it stops, and without, it reads whatever is there",
            IgnoreCase: true, ListsDeclared: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-not-found-as-position", dialect.Files, dialect.Syntax,
            (id, source, masked) => ReadAtNotFound(id, source, masked, dialect))),
    ];

    private static IEnumerable<LogicFinding> ReadAtNotFound(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var answeringNotFound = FunctionsAnsweringMinusOne(masked, dialect, options);
        if (answeringNotFound.Count == 0) yield break;
        var declaredLists = dialect.ListsDeclared ? DeclaredLists(masked, dialect, options) : null;
        var given = new Regex(dialect.Given, options);

        for (var line = 0; line < masked.Count; line++)
        {
            foreach (var (function, header, end) in answeringNotFound)
            {
                if (line >= header && line < end) continue;
                var called = Regex.Escape(function);

                // Read straight at the call's answer: names[find(names, "dan")].
                var straight = new Regex(dialect.Read.Replace("{P}", $@"{called}\s*\([^()]*(?:\([^()]*\)[^()]*)*\)"), options);
                if (straight.Match(masked[line]) is { Success: true } readAtCall && IsAList(readAtCall.Groups["list"].Value, declaredLists))
                {
                    yield return Found(id, function, readAtCall.Groups["list"].Value, line, line, dialect);
                    continue;
                }

                // Or through a variable given the answer, whose first use after is the read.
                if (given.Match(masked[line]) is not { Success: true } assigned ||
                    !string.Equals(LastPart(assigned.Groups["called"].Value), function, dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                var place = assigned.Groups["name"].Value;
                var mention = new Regex($@"(?<![\w$@]){Regex.Escape(place)}(?![\w$])", options);
                var used = Enumerable.Range(line + 1, masked.Count - line - 1).FirstOrDefault(later => mention.IsMatch(masked[later]), -1);
                if (used < 0 || Checks(masked[used], place, options)) continue;

                var read = new Regex(dialect.Read.Replace("{P}", Regex.Escape(place)), options).Match(masked[used]);
                if (!read.Success || !IsAList(read.Groups["list"].Value, declaredLists)) continue;
                yield return Found(id, function, read.Groups["list"].Value, line, used, dialect);
            }
        }
    }

    private static LogicFinding Found(string id, string function, string list, int called, int used, Dialect dialect) =>
        new(id, used + 1,
            $"`{function}` answers -1 when it finds nothing, and its answer from line {called + 1} is used as a position in `{list}` with nothing " +
            $"checking for -1 first - {dialect.What}", null);

    private static string LastPart(string called) => called.Split('.', ':').Last();

    private static bool IsAList(string name, HashSet<string>? declared) => declared is null || declared.Contains(name);

    /// <summary>Whether a line checks the place rather than only using it: a comparison of it, or a condition naming it.</summary>
    private static bool Checks(string line, string place, RegexOptions options)
    {
        var named = Regex.Escape(place);
        return Regex.IsMatch(line,
            $@"(?<![\w$@]){named}\s*(?:[<>]=?|[!=]==?|~=|/=|-(?:lt|le|gt|ge|eq|ne)\b|\.(?:lt|le|gt|ge|eq|ne)\.)|(?:[<>]=?|[!=]==?|~=|/=)\s*{named}(?![\w$])|" +
            $@"^\s*(?:\}}\s*)?(?:if|elif|elsif|elseif|unless|while|until|when|case|switch|match)\b|\?\s*[^:]*:", options);
    }

    /// <summary>The lists Scala's or Fortran's file declares, as their reads look like calls.</summary>
    private static HashSet<string> DeclaredLists(IReadOnlyList<string> masked, Dialect dialect, RegexOptions options)
    {
        var declares = dialect.Language == "scala"
            ? new Regex(@"^\s*(?:val|var)\s+(?<name>(?>\w+))\s*(?::[^=]+)?=\s*(?:Array|List|Vector|IndexedSeq|ArrayBuffer|ListBuffer|Seq)\b", options)
            : new Regex(@"::.*?(?<![\w%])(?<name>(?>[A-Za-z]\w*))\s*\(\s*[\d:]", options);
        var lists = new HashSet<string>(dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var line in masked)
            foreach (Match declared in declares.Matches(line))
                lists.Add(declared.Groups["name"].Value);
        return lists;
    }

    /// <summary>The program's functions with a line answering -1, each with its first line and the line after its last.</summary>
    private static List<(string Name, int Header, int End)> FunctionsAnsweringMinusOne(IReadOnlyList<string> masked, Dialect dialect, RegexOptions options)
    {
        var functions = new List<(string, int, int)>();
        for (var line = 0; line < masked.Count; line++)
        {
            if (FunctionName(masked[line], dialect) is not { } name) continue;

            var indent = CodeText.Indentation(masked[line]).Length;
            var end = line + 1;
            while (end < masked.Count && (masked[end].Trim().Length == 0 || masked[end].Trim() == "{" || CodeText.Indentation(masked[end]).Length > indent)) end++;

            var answers = new Regex(
                $@"^\s*return\s*\(?\s*-\s*1\s*\)?\s*;?\s*$|^\s*-\s*1\s*;?\s*$|^\s*{Regex.Escape(name)}\s*=\s*-\s*1\s*$", options);
            var body = Enumerable.Range(line + 1, Math.Max(0, end - line - 1)).Select(each => masked[each]).ToList();
            var answersMinusOne = body.Any(each => answers.IsMatch(each) &&
                                                   (dialect.Language is "ruby" or "rust" or "scala" || !Regex.IsMatch(each, @"^\s*-\s*1\s*;?\s*$")));
            if (answersMinusOne) functions.Add((name, line, end));
        }
        return functions;
    }

    /// <summary>The function a line defines, in the language's words; null for any other line.</summary>
    private static string? FunctionName(string line, Dialect dialect)
    {
        var defines = dialect.Language switch
        {
            "python" => PythonFunction(),
            "go" => GoFunction(),
            "rust" => RustFunction(),
            "ruby" => RubyFunction(),
            "php" or "lua" or "js" => WordFunction(),
            "perl" => PerlFunction(),
            "powershell" => PowerShellFunction(),
            "scala" => ScalaFunction(),
            "fortran" => FortranFunction(),
            _ => TypedFunction(),
        };
        return defines.Match(line) is { Success: true } defined && !ControlWord().IsMatch(line) && !line.TrimEnd().EndsWith(';')
            ? defined.Groups["name"].Value
            : null;
    }

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>(?>\w+))\s*\(")]
    private static partial Regex PythonFunction();

    [GeneratedRegex(@"^\s*func\s+(?:\([^)]*\)\s*)?(?<name>(?>\w+))\s*\(")]
    private static partial Regex GoFunction();

    [GeneratedRegex(@"^\s*(?:pub(?:\([^)]*\))?\s+)?fn\s+(?<name>(?>\w+))")]
    private static partial Regex RustFunction();

    [GeneratedRegex(@"^\s*def\s+(?:self\.)?(?<name>(?>[\w?!]+))")]
    private static partial Regex RubyFunction();

    [GeneratedRegex(@"\bfunction\s+(?:[\w.]+[.:])?(?<name>(?>\w+))\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex WordFunction();

    [GeneratedRegex(@"^\s*sub\s+(?<name>(?>\w+))")]
    private static partial Regex PerlFunction();

    [GeneratedRegex(@"^\s*function\s+(?<name>(?>[\w-]+))", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellFunction();

    [GeneratedRegex(@"\bdef\s+(?<name>(?>\w+))")]
    private static partial Regex ScalaFunction();

    [GeneratedRegex(@"\bfunction\s+(?<name>(?>\w+))\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex FortranFunction();

    /// <summary>A function in a typed language: a return type, then its name and parameters.</summary>
    [GeneratedRegex(@"^(?>\s*)(?:[\w<>\[\]:*&,]+\s+)+[*&]?\s*(?<name>(?>\w+))\s*\([^=;]*\)\s*(?:const\s*)?\{?\s*$")]
    private static partial Regex TypedFunction();

    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:if|for|foreach|while|switch|return|else|elif|elsif|do|catch|until)\b")]
    private static partial Regex ControlWord();
}
