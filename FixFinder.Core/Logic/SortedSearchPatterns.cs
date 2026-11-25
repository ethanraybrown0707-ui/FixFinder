using System.Globalization;
using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A binary search given a list out of order: a search that looks at the middle item and throws away the half the wanted
/// item cannot be in - the program's own, or the library's <c>bisect</c>, <c>Arrays.binarySearch</c>,
/// <c>Array.BinarySearch</c>, <c>std::binary_search</c>, <c>sort.SearchInts</c>, <c>binary_search</c>, <c>bsearch</c> -
/// is given a list written out of order, <c>marks = [5, 1, 4, 9]</c>, so it can throw away the half the item is in and say
/// it is not there. Its contract - the list in order - is read from the search itself.
/// </summary>
/// <remarks>
/// The program's own search is a function with a middle worked out by halving two bounds - <c>mid = (low + high) // 2</c>,
/// <c>low + (high - low) / 2</c>, <c>Math.floor((low + high) / 2)</c>, <c>intdiv($low + $high, 2)</c> - and an item of one
/// of its parameters read at that middle - <c>items[mid]</c>, <c>items(mid)</c>, <c>items.(mid)</c>, <c>$items-&gt;[$mid]</c>.
/// A call to it, or to the library's search, is checked when the list it passes is a list of numbers written in the code -
/// there or in the nearest line before giving the name one, with no line between naming it, so a list sorted, added to or
/// read in from anywhere is left alone. The change writes the list in order. Elixir and Erlang have no check of it.
/// </remarks>
public static partial class SortedSearchPatterns
{
    /// <summary>
    /// How one language writes a list and a search: <c>Declares</c> gives a name a list written in the code, with the name in
    /// <c>name</c> and the list's items in <c>items</c>; <c>Literal</c> is a list written in place, its items in
    /// <c>items</c>; <c>Library</c> the library's searches, with the list passed in <c>list</c>; <c>Curried</c> where a call
    /// passes its arguments with spaces between, as F#, OCaml and PowerShell do.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex Declares, Regex Literal, Regex[] Library,
        char Separator = ',', bool Curried = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py"), Syntax.Python,
            new(@"^\s*(?<name>(?>[A-Za-z_]\w*))\s*(?::[^=]+)?=\s*\[(?<items>[^\[\]]*)\]\s*$"), new(@"^\s*\[(?<items>[^\[\]]*)\]\s*$"),
            [new(@"\b(?:bisect\.)?(?:bisect_left|bisect_right|bisect|insort_left|insort_right|insort)\s*\(\s*(?<list>[^,()]+?)\s*,")]),
        new("java", CodePattern.Files(".java"), Syntax.CLike,
            new(@"^\s*(?:final\s+)?(?:int|long|short|double|float)\s*(?:\[\]\s*(?<name>(?>\w+))|(?<name>(?>\w+))\s*\[\])\s*=\s*(?:new\s+\w+\s*\[\s*\]\s*)?\{(?<items>[^{}]*)\}\s*;\s*$"),
            new(@"^\s*new\s+\w+\s*\[\s*\]\s*\{(?<items>[^{}]*)\}\s*$"),
            [new(@"\b(?:Arrays|Collections)\.binarySearch\s*\(\s*(?<list>[^,()]+?)\s*,")]),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike,
            new(@"^\s*(?:var|(?:int|long|short|double|float)\s*\[\])\s+(?<name>(?>\w+))\s*=\s*(?:new\s*(?:\w+)?\s*\[\s*\]\s*)?[{\[](?<items>[^{}\[\]]*)[}\]]\s*;\s*$"),
            new(@"^\s*new\s*(?:\w+)?\s*\[\s*\]\s*\{(?<items>[^{}]*)\}\s*$"),
            [new(@"\bArray\.BinarySearch\s*\(\s*(?<list>[^,()]+?)\s*,"), new(@"(?<![\w.])(?<list>(?>[A-Za-z_]\w*))\s*\.\s*BinarySearch\s*\(")]),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike,
            new(@"^\s*(?:const|let|var)\s+(?<name>(?>[\w$]+))\s*=\s*\[(?<items>[^\[\]]*)\]\s*;?\s*$"), new(@"^\s*\[(?<items>[^\[\]]*)\]\s*$"), []),
        new("go", CodePattern.Files(".go"), Syntax.CLike,
            new(@"^\s*(?:(?<name>(?>\w+))\s*:=|var\s+(?<name>(?>\w+))\s*=)\s*\[\d*\]\w+\{(?<items>[^{}]*)\}\s*$"), new(@"^\s*\[\d*\]\w+\{(?<items>[^{}]*)\}\s*$"),
            [new(@"\bsort\.Search(?:Ints|Float64s|Strings)\s*\(\s*(?<list>[^,()]+?)\s*,"), new(@"\bslices\.BinarySearch\s*\(\s*(?<list>[^,()]+?)\s*,")]),
        new("c", CodePattern.Files(".c"), Syntax.CLike,
            new(@"^\s*(?:static\s+)?(?:const\s+)?(?:int|long|short|double|float|unsigned)\s+(?<name>(?>\w+))\s*\[\s*\d*\s*\]\s*=\s*\{(?<items>[^{}]*)\}\s*;\s*$"),
            new(@"^\s*\{(?<items>[^{}]*)\}\s*$"),
            [new(@"\bbsearch\s*\(\s*[^,()]+,\s*(?<list>[^,()]+?)\s*,")]),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike,
            new(@"^\s*(?:(?:static|const)\s+)*(?:(?:int|long|short|double|float|unsigned)\s+(?<name>(?>\w+))\s*\[\s*\d*\s*\]|(?:std::)?(?:vector|array)\s*<[^<>]*>\s+(?<name>(?>\w+)))\s*=?\s*\{(?<items>[^{}]*)\}\s*;\s*$"),
            new(@"^\s*\{(?<items>[^{}]*)\}\s*$"),
            [new(@"\bstd::(?:binary_search|lower_bound|upper_bound|equal_range)\s*\(\s*(?:std::begin\s*\(\s*)?(?<list>(?>\w+))")]),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike,
            new(@"^\s*let\s+(?:mut\s+)?(?<name>(?>\w+))\s*(?::[^=]+)?=\s*(?:vec!)?\[(?<items>[^\[\]]*)\]\s*;\s*$"), new(@"^\s*&?\s*(?:vec!)?\[(?<items>[^\[\]]*)\]\s*$"),
            [new(@"(?<![\w.])(?<list>(?>[A-Za-z_]\w*))\s*\.\s*binary_search(?:_by(?:_key)?)?\s*\(")]),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python,
            new(@"^\s*(?<name>(?>[a-z_]\w*))\s*=\s*\[(?<items>[^\[\]]*)\]\s*$"), new(@"^\s*\[(?<items>[^\[\]]*)\]\s*$"),
            [new(@"(?<![\w.@$])(?<list>(?>[a-z_]\w*))\s*\.\s*bsearch(?:_index)?\b")]),
        new("php", CodePattern.Files(".php"), Syntax.CLike,
            new(@"^\s*(?<name>\$(?>\w+))\s*=\s*(?:\[(?<items>[^\[\]]*)\]|array\((?<items>[^()]*)\))\s*;\s*$", RegexOptions.IgnoreCase),
            new(@"^\s*(?:\[(?<items>[^\[\]]*)\]|array\((?<items>[^()]*)\))\s*$", RegexOptions.IgnoreCase), [], IgnoreCase: true),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua,
            new(@"^\s*(?:local\s+)?(?<name>(?>\w+))\s*=\s*\{(?<items>[^{}]*)\}\s*$"), new(@"^\s*\{(?<items>[^{}]*)\}\s*$"), []),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl,
            new(@"^\s*my\s+(?<name>@(?>\w+))\s*=\s*\((?<items>[^()]*)\)\s*;\s*$"), new(@"^\s*\[(?<items>[^\[\]]*)\]\s*$"), []),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike,
            new(@"^\s*(?:final|var|const|List<\w+>)\s+(?<name>(?>\w+))\s*=\s*(?:<\w+>)?\[(?<items>[^\[\]]*)\]\s*;\s*$"), new(@"^\s*(?:<\w+>)?\[(?<items>[^\[\]]*)\]\s*$"), []),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python,
            new(@"^\s*(?<name>\$(?>\w+))\s*=\s*(?:\[(?:[^\[\]]|\[\])+\]\s*)?@\((?<items>[^()]*)\)\s*$", RegexOptions.IgnoreCase), new(@"^\s*@\((?<items>[^()]*)\)\s*$"),
            [new(@"\[(?:System\.)?Array\]::BinarySearch\s*\(\s*(?<list>[^,()]+?)\s*,", RegexOptions.IgnoreCase)], Curried: true, IgnoreCase: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala,
            new(@"^\s*(?:val|var)\s+(?<name>(?>\w+))\s*(?::[^=]+)?=\s*(?:Array|List|Vector|IndexedSeq|ArrayBuffer)\((?<items>[^()]*)\)\s*$"),
            new(@"^\s*(?:Array|List|Vector|IndexedSeq)\((?<items>[^()]*)\)\s*$"),
            [new(@"(?<![\w.])(?<list>(?>[A-Za-z_]\w*))\s*\.\s*search\s*\(")]),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran,
            new(@"^\s*(?:integer|real)\b[^:]*::\s*(?<name>(?>\w+))\s*(?:\(\s*\d+\s*\))?\s*=\s*(?:\[(?<items>[^\[\]]*)\]|\(/(?<items>[^/]*)/\))\s*$", RegexOptions.IgnoreCase),
            new(@"^\s*(?:\[(?<items>[^\[\]]*)\]|\(/(?<items>[^/]*)/\))\s*$"), [], IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp,
            new(@"^\s*let\s+(?<name>(?>\w+))\s*=\s*\[\|(?<items>[^\[\]|]*)\|\]\s*$"), new(@"^\s*\[\|(?<items>[^\[\]|]*)\|\]\s*$"),
            [new(@"\b(?:System\.)?Array\.BinarySearch\s*\(\s*(?<list>[^,()]+?)\s*,")], Separator: ';', Curried: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml,
            new(@"^\s*let\s+(?<name>(?>\w+))\s*=\s*\[\|(?<items>[^\[\]|]*)\|\]\s*(?:in)?\s*$"), new(@"^\s*\[\|(?<items>[^\[\]|]*)\|\]\s*$"), [],
            Separator: ';', Curried: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-search-unsorted", dialect.Files, dialect.Syntax,
            (id, source, masked) => SearchesOutOfOrder(id, source, masked, dialect))),
    ];

    /// <summary>A search the program writes: the function's name, the place of the list among its arguments, the list's name in it, and the middle it reads.</summary>
    private sealed record OwnSearch(string Name, int ListAt, string List, string Middle, int Header, int End);

    private static IEnumerable<LogicFinding> SearchesOutOfOrder(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var searches = OwnSearches(masked, dialect, options);

        for (var line = 0; line < masked.Count; line++)
        {
            var said = new HashSet<int>();

            // The library's searches on this line, and calls to the program's own.
            foreach (var library in dialect.Library)
                foreach (Match call in library.Matches(masked[line]))
                    if (Given(call.Groups["list"].Value, line, source, masked, dialect, options) is { } given && said.Add(given.Line))
                        yield return Found(id, source, given, line,
                            $"`{SearchName(call.Value)}` halves the list at each step, so it needs the list in order, but {Shown(given)} holds " +
                            $"{Items(given)}, out of order, so it can miss what is there");

            foreach (var search in searches)
            {
                if (line >= search.Header && line < search.End) continue;
                if (Arguments(masked[line], search.Name, dialect, options) is not { } arguments || search.ListAt >= arguments.Count) continue;
                if (Given(arguments[search.ListAt], line, source, masked, dialect, options) is not { } given || !said.Add(given.Line)) continue;

                yield return Found(id, source, given, line,
                    $"`{search.Name}` halves the list at each step - it compares `{search.List}` at `{search.Middle}` - so it needs the list in order, " +
                    $"but {Shown(given)} holds {Items(given)}, out of order, so it can miss what is there");
            }
        }
    }

    /// <summary>A list given to a search: its name - empty where it is written in place - its items, and the line and place they are written.</summary>
    private sealed record GivenList(string Name, IReadOnlyList<string> Items, int Line, int ItemsAt, int ItemsLength, char Separator);

    private static string Shown(GivenList given) => given.Name.Length > 0 ? $"`{given.Name}`" : "the list it is given";

    private static string Items(GivenList given) => string.Join($"{given.Separator} ", given.Items);

    private static string SearchName(string call) => SearchWord().Matches(call).LastOrDefault() is { } word ? word.Value : call.Trim();

    /// <summary>
    /// The list an argument passes, when it is a list of numbers written in the code out of order: in place, or in the
    /// nearest line before giving the name one, with no line between naming it; null otherwise.
    /// </summary>
    private static GivenList? Given(string argument, int line, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, RegexOptions options)
    {
        var passed = argument.Trim().TrimStart('&', '\\', '*').Trim();

        if (dialect.Literal.Match(passed) is { Success: true } inPlace)
        {
            var at = masked[line].IndexOf(passed, StringComparison.Ordinal);
            var group = inPlace.Groups["items"];
            return OutOfOrder(group.Value, dialect.Separator) is { } items && at >= 0
                ? new GivenList("", items, line, at + group.Index, group.Length, dialect.Separator)
                : null;
        }

        if (!PlainName().IsMatch(passed)) return null;
        // Any mention of the name, with a sigil or none: Perl's @marks = sort ... @marks names it.
        var mention = new Regex($@"(?<![\w$@%])[$@%]?{Regex.Escape(passed.TrimStart('@', '$', '%'))}(?![\w$])", options);
        for (var before = line - 1; before >= 0; before--)
        {
            if (dialect.Declares.Match(masked[before]) is { Success: true } declared &&
                string.Equals(declared.Groups["name"].Value.TrimStart('@'), passed.TrimStart('@'), dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                var group = declared.Groups["items"];
                return OutOfOrder(group.Value, dialect.Separator) is { } items
                    ? new GivenList(passed, items, before, group.Index, group.Length, dialect.Separator)
                    : null;
            }
            if (mention.IsMatch(masked[before])) return null;
        }
        return null;
    }

    /// <summary>The items of a list of two or more numbers not in order, as written; null for any other list.</summary>
    private static List<string>? OutOfOrder(string written, char separator)
    {
        var items = written.Split(separator).Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
        if (items.Count < 2 || !items.All(item => ANumber().IsMatch(item))) return null;
        var values = items.Select(item => double.Parse(item, CultureInfo.InvariantCulture)).ToList();
        return values.Zip(values.Skip(1)).Any(pair => pair.Second < pair.First) ? items : null;
    }

    /// <summary>The finding at the search, with the change writing the list in order where it is written.</summary>
    private static LogicFinding Found(string id, SourceFile source, GivenList given, int line, string message)
    {
        var written = source.Lines[given.Line];
        var original = given.ItemsAt + given.ItemsLength <= written.Length ? written.Substring(given.ItemsAt, given.ItemsLength) : "";
        var sorted = given.Items.OrderBy(item => double.Parse(item, CultureInfo.InvariantCulture)).ToList();
        var leading = original[..(original.Length - original.TrimStart().Length)];
        var trailing = original[original.TrimEnd().Length..];
        var inOrder = $"{leading}{string.Join($"{given.Separator} ", sorted)}{trailing}";
        var name = given.Name.Length > 0 ? given.Name : "the list";

        return new LogicFinding(id, line + 1, message, original.Length == 0 ? null : LocalFix.ReplaceLine(id,
            $"Write {name} in order: {string.Join($"{given.Separator} ", sorted)}",
            "A binary search looks at the middle item and throws away the half the wanted item cannot be in, which holds only when " +
            "the list is in order. Written in order, the search finds what is there; a list read in from elsewhere is sorted before " +
            "the search instead.",
            source.Path, given.Line + 1, written[..given.ItemsAt] + inOrder + written[(given.ItemsAt + given.ItemsLength)..]));
    }

    /// <summary>
    /// The program's own searches: functions with a middle worked out by halving two bounds and an item of one of their
    /// parameters read at that middle.
    /// </summary>
    private static List<OwnSearch> OwnSearches(IReadOnlyList<string> masked, Dialect dialect, RegexOptions options)
    {
        var searches = new List<OwnSearch>();
        for (var line = 0; line < masked.Count; line++)
        {
            if (Halving().Match(masked[line]) is not { Success: true } halving) continue;
            var middle = halving.Groups["mid"].Value;
            var index = new Regex($@"(?<![\w$])(?<list>\$?(?>[A-Za-z_]\w*))\s*(?:->\s*)?(?:\.\s*)?[\[(]\s*{Regex.Escape(middle)}\b", options);

            // The function the middle is worked out in: the nearest line above, less indented, that names its parameters.
            var indent = CodeText.Indentation(masked[line]).Length;
            var header = -1;
            for (var above = line - 1; above >= 0 && header < 0; above--)
                if (masked[above].Trim().Length > 0 && masked[above].Trim() != "{" && CodeText.Indentation(masked[above]).Length < indent &&
                    FunctionHeader(masked[above], dialect) is not null)
                    header = above;
            if (header < 0 || FunctionHeader(masked[header], dialect) is not var (name, parameters)) continue;
            if (searches.Any(search => search.Header == header)) continue;

            var headerIndent = CodeText.Indentation(masked[header]).Length;
            var end = header + 1;
            while (end < masked.Count && (masked[end].Trim().Length == 0 || masked[end].Trim() == "{" || CodeText.Indentation(masked[end]).Length > headerIndent)) end++;

            // Perl's parameters are taken in the body: my ($items, $wanted) = @_;.
            if (dialect.Language == "perl")
                parameters = Enumerable.Range(header + 1, Math.Max(0, end - header - 1)).Select(body => PerlTakes().Match(masked[body]))
                    .FirstOrDefault(taken => taken.Success) is { } takes ? PerlName().Matches(takes.Groups["parameters"].Value).Select(found => found.Value).ToList() : [];

            var read = Enumerable.Range(line, Math.Max(0, end - line)).Select(body => index.Match(masked[body])).FirstOrDefault(found => found.Success);
            if (read is null) continue;
            var list = read.Groups["list"].Value;
            var at = parameters.FindIndex(parameter => string.Equals(parameter, list, dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (at < 0) continue;

            // A search, not a sort that also halves - quicksort's middle pivot, merge sort's halves, binary insertion sort:
            // it tests its item at the middle, moves a bound to the middle or searches again from beside it, and never
            // writes into its list.
            var body = Enumerable.Range(header + 1, Math.Max(0, end - header - 1)).Select(each => masked[each]).ToList();
            var itemAtMiddle = $@"(?<![\w$]){Regex.Escape(list)}\s*(?:->\s*)?(?:\.\s*)?[\[(]\s*{Regex.Escape(middle)}\b";
            var tested = new Regex($@"\b(?:if|elif|elsif|elseif|when|unless|while)\b.*{itemAtMiddle}", options);
            var bounds = $"(?:{Regex.Escape(halving.Groups["low"].Value)}|{Regex.Escape(halving.Groups["high"].Value)})";
            var moved = new Regex(
                $@"(?<![\w$]){bounds}\s*(?::=|(?<![=!<>])=(?!=)|<-)\s*!?{Regex.Escape(middle)}\b|(?<![\w$.-]){Regex.Escape(name)}\b.*{Regex.Escape(middle)}\s*[+-]\s*1\b", options);
            // F# and OCaml write an item with <-, as their = compares.
            var givesAnItem = dialect.Language is "fsharp" or "ocaml" ? "<-" : @"(?::=|(?<![=!<>])=(?!=)|<-)";
            var writes = new Regex(
                $@"(?<![\w$]){Regex.Escape(list)}\s*(?:->\s*)?(?:\.\s*)?[\[(][^\])]*[\])]\s*{givesAnItem}|" +
                $@"(?<![\w$]){Regex.Escape(list)}\s*\.\s*(?:insert|append|push|pop|remove|sort|reverse|swap|add|set|splice|fill|copyWithin|shift|unshift|clear|erase|" +
                $@"emplace\w*|push_back|pop_back|Add|Insert|RemoveAt|Remove|Sort|Reverse|Clear)\b|" +
                $@"\b(?:std::)?swap\s*\(\s*{Regex.Escape(list)}\b", options);
            if (!body.Any(tested.IsMatch) || !body.Any(moved.IsMatch) || body.Any(writes.IsMatch)) continue;

            searches.Add(new OwnSearch(name, at, list, middle, header, end));
        }
        return searches;
    }

    /// <summary>A function's name and its parameters' names, from its first line; null when the line does not start one.</summary>
    private static (string Name, List<string> Parameters)? FunctionHeader(string line, Dialect dialect)
    {
        if (dialect.Curried)
        {
            if (dialect.Language == "powershell" && PowerShellHeader().Match(line) is { Success: true } powershell)
                return (powershell.Groups["name"].Value, SplitAtTopLevel(powershell.Groups["parameters"].Value).Select(ParameterName).Where(name => name.Length > 0).ToList());
            if (CurriedHeader().Match(line) is not { Success: true } curried) return null;
            return (curried.Groups["name"].Value, CurriedParts().Matches(curried.Groups["parameters"].Value).Select(part => CurriedName().Match(part.Value).Groups["name"].Value)
                .Where(name => name.Length > 0).ToList());
        }

        if (dialect.Language == "perl") return PerlHeader().Match(line) is { Success: true } sub ? (sub.Groups["name"].Value, []) : null;
        if (!DefinesAFunction(line, dialect.Language) || ControlWord().IsMatch(line) || NamedWithParameters().Match(line) is not { Success: true } header) return null;
        var names = SplitAtTopLevel(header.Groups["parameters"].Value)
            .Select(part => dialect.Language == "go" ? GoParameter().Match(part).Groups["name"].Value : ParameterName(part))
            .Where(name => name.Length > 0).ToList();
        return (header.Groups["name"].Value, names);
    }

    /// <summary>A parameter's name: the word before a type after a colon, or else the last word, with its $ kept.</summary>
    private static string ParameterName(string part)
    {
        var withoutDefault = part.Split('=')[0];
        var typeColon = TypeColon().Match(withoutDefault);
        var beforeType = typeColon.Success ? withoutDefault[..typeColon.Index] : withoutDefault;
        return LastName().Match(beforeType) is { Success: true } last ? last.Value : "";
    }

    /// <summary>The arguments a line's call to the function passes, or null where the line does not call it.</summary>
    private static List<string>? Arguments(string line, string function, Dialect dialect, RegexOptions options)
    {
        if (dialect.Curried)
        {
            var curried = Regex.Match(line, $@"(?<![\w$.-]){Regex.Escape(function)}\s+(?<arguments>[^=|)]+)", options);
            if (!curried.Success || Regex.IsMatch(line, $@"^\s*(?:let\s+(?:rec\s+)?|function\s+){Regex.Escape(function)}\b", options)) return null;
            return CurriedArgument().Matches(curried.Groups["arguments"].Value).Select(argument => argument.Value.Trim('(', ')')).ToList();
        }

        var call = Regex.Match(line, $@"(?<![\w$.]){Regex.Escape(function)}\s*\(", options);
        if (!call.Success) return null;
        var start = call.Index + call.Length;
        var depth = 1;
        var end = start;
        while (end < line.Length && depth > 0)
        {
            depth += line[end] switch { '(' or '[' or '{' => 1, ')' or ']' or '}' => -1, _ => 0 };
            end++;
        }
        return depth == 0 ? SplitAtTopLevel(line[start..(end - 1)]).ToList() : null;
    }

    /// <summary>A list split at its commas, not those inside brackets, braces or angle brackets.</summary>
    private static IEnumerable<string> SplitAtTopLevel(string list)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < list.Length; index++)
        {
            depth += list[index] switch { '(' or '[' or '{' or '<' => 1, ')' or ']' or '}' or '>' => -1, _ => 0 };
            if (list[index] == ',' && depth == 0)
            {
                yield return list[start..index];
                start = index + 1;
            }
        }
        if (list[start..].Trim().Length > 0) yield return list[start..];
    }

    /// <summary>Whether a line defines a function, in the language's own words - not a line that only calls one, as len(items) does.</summary>
    private static bool DefinesAFunction(string line, string language) => language switch
    {
        "python" => PythonDefines().IsMatch(line),
        "go" => GoDefines().IsMatch(line),
        "rust" => RustDefines().IsMatch(line),
        "ruby" => RubyDefines().IsMatch(line),
        "php" or "lua" => WordFunction().IsMatch(line),
        "scala" => ScalaDefines().IsMatch(line),
        "fortran" => FortranDefines().IsMatch(line),
        "js" => WordFunction().IsMatch(line) || JsMethod().IsMatch(line),
        _ => !line.TrimEnd().EndsWith(';') && TypedDefines().IsMatch(line),
    };

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s")]
    private static partial Regex PythonDefines();

    [GeneratedRegex(@"^\s*func\s")]
    private static partial Regex GoDefines();

    [GeneratedRegex(@"^\s*(?:pub(?:\([^)]*\))?\s+)?(?:async\s+|const\s+)*fn\s")]
    private static partial Regex RustDefines();

    [GeneratedRegex(@"^\s*def\s")]
    private static partial Regex RubyDefines();

    [GeneratedRegex(@"\bfunction\b", RegexOptions.IgnoreCase)]
    private static partial Regex WordFunction();

    [GeneratedRegex(@"\bdef\s")]
    private static partial Regex ScalaDefines();

    [GeneratedRegex(@"\b(?:function|subroutine)\s", RegexOptions.IgnoreCase)]
    private static partial Regex FortranDefines();

    /// <summary>A JavaScript method, name(...) {, or an arrow function given a name.</summary>
    [GeneratedRegex(@"^\s*(?:async\s+|static\s+)*(?!(?:if|for|while|switch|catch)\b)[\w$]+\s*\([^()]*\)\s*\{\s*$|=\s*(?:async\s*)?\([^()]*\)\s*=>")]
    private static partial Regex JsMethod();

    /// <summary>A function in a typed language: a return type, then its name and parameters - not an assignment.</summary>
    [GeneratedRegex(@"^(?>\s*)(?:[\w<>\[\]:*&,]+\s+)+[*&]?\s*\w+\s*\([^=;]*\)\s*(?:const\s*|async\s*)?\{?\s*$")]
    private static partial Regex TypedDefines();

    /// <summary>A colon on its own, which names a type - not one of std::vector's.</summary>
    [GeneratedRegex(@"(?<!:):(?!:)")]
    private static partial Regex TypeColon();

    /// <summary>
    /// A middle worked out by halving two bounds: mid = (low + high) // 2, low + (high - low) / 2, Math.floor((low + high) / 2),
    /// intdiv($low + $high, 2), (low + high) ~/ 2, (!low + !high) / 2 - the middle's name in mid.
    /// </summary>
    [GeneratedRegex(@"(?<![\w$])(?<mid>\$?(?>[A-Za-z_]\w*))\s*(?::=|(?<![=!<>])=(?!=)|<-)\s*(?:[\w.:\[\]]+\s*\(\s*)?\(?\s*!?(?<low>\$?(?>\w+))\s*\+\s*(?:\(\s*!?(?<high>\$?(?>\w+))\s*-\s*!?\$?(?>\w+)\s*\)|!?(?<high>\$?(?>\w+)))\s*\)?\s*(?:/|//|>>|~/|,|\bdiv\b)\s*2\b")]
    private static partial Regex Halving();

    /// <summary>A name and its parameters in brackets: contains(items, wanted), Test-Contains($items, $wanted).</summary>
    [GeneratedRegex(@"(?<![\w$.])(?<name>(?>[A-Za-z_][\w-]*))\s*\((?<parameters>[^()]*(?:\([^()]*\)[^()]*)*)\)")]
    private static partial Regex NamedWithParameters();

    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:if|for|foreach|while|switch|return|else|elif|elsif|do|catch|until|print|puts|echo)\b")]
    private static partial Regex ControlWord();

    [GeneratedRegex(@"^\s*function\s+(?<name>(?>[\w-]+))\s*\((?<parameters>[^()]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellHeader();

    [GeneratedRegex(@"^\s*let\s+(?:rec\s+)?(?<name>(?>\w+))\s+(?<parameters>[^=]+?)\s*(?::\s*[^=]+)?=\s*$")]
    private static partial Regex CurriedHeader();

    [GeneratedRegex(@"\([^()]*\)|[~?]?[a-z_]\w*'*")]
    private static partial Regex CurriedParts();

    [GeneratedRegex(@"^\s*[~?(]?\s*(?<name>[a-z_]\w*)")]
    private static partial Regex CurriedName();

    /// <summary>One argument of a call written with spaces: a word, a $name, a number, or something in brackets.</summary>
    [GeneratedRegex(@"\([^()]*\)|\$?[\w.]+")]
    private static partial Regex CurriedArgument();

    [GeneratedRegex(@"^\s*sub\s+(?<name>(?>\w+))\s*\{")]
    private static partial Regex PerlHeader();

    [GeneratedRegex(@"^\s*my\s*\((?<parameters>[^()]*)\)\s*=\s*@_\s*;")]
    private static partial Regex PerlTakes();

    [GeneratedRegex(@"\$\w+")]
    private static partial Regex PerlName();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_]\w*)")]
    private static partial Regex GoParameter();

    [GeneratedRegex(@"\$?[A-Za-z_]\w*(?=[^\w$]*$)")]
    private static partial Regex LastName();

    [GeneratedRegex(@"^@?\$?[A-Za-z_]\w*$")]
    private static partial Regex PlainName();

    [GeneratedRegex(@"^-?\d+(?:\.\d+)?$")]
    private static partial Regex ANumber();

    /// <summary>The search's own name in a library call: binarySearch, BinarySearch, bisect_left, binary_search, bsearch, search.</summary>
    [GeneratedRegex(@"(?i)\b\w*(?:search|bisect|insort|bound|range)\w*\b")]
    private static partial Regex SearchWord();
}
