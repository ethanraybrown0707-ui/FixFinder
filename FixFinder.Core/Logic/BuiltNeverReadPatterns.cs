using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A list, set, map or text builder that is filled and never read: every line that names it after it is made only adds to
/// it - <c>result.append(mark * 2)</c> - and nothing gives it back or uses it, so all that was put in it is lost. Most often
/// the function then gives back something else, the list it was given: <c>return marks</c> where <c>return result</c> was
/// meant. Python, Java, C#, JavaScript (and TypeScript, which is read with its checks), Go, C++, Rust, Ruby, PHP, Lua, Perl,
/// Dart, PowerShell, Scala and F#.
/// </summary>
/// <remarks>
/// Only a variable made empty inside a block, in the language's own words - <c>result = []</c>, <c>new ArrayList&lt;&gt;()</c>,
/// <c>Vec::new()</c>, <c>my @result</c>, <c>$result = @()</c> - and every later line in that block naming it a whole
/// statement that only adds to it: an append, push, add, put or insert call, an item given a value, or +=, with what is
/// added never naming it. A use of what an add gives back - <c>if (!seen.add(mark))</c> - is a read, and so is any other
/// line naming it, a function or a nested function among them. Python's global and nonlocal names, and PHP's global ones,
/// are left alone. The change gives back the built variable in place of what the function's last line gives back, when
/// that is one of the function's parameters and - in a typed language - of the built variable's type, or one Scala's
/// <c>toList</c> or F#'s <c>List.ofSeq</c> makes it.
/// </remarks>
public static partial class BuiltNeverReadPatterns
{
    /// <summary>
    /// How one language makes an empty list and adds to it. <c>Declared</c> finds a line making one, with its name, and its
    /// written type or the type it is made as; <c>Adding</c> is a whole statement only adding to the name <c>{N}</c> -
    /// Perl's bare name <c>{B}</c> - with what is added in the group <c>added</c>; <c>Parameter</c>, with the name
    /// <c>{V}</c>, the type of a parameter in a function's header, for a typed language.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex Declared, string Adding,
        string? Parameter = null, bool LastLineGivesBack = false, bool IgnoreCase = false);

    private const string BraceParameter = @"[(,]\s*(?:final\s+|const\s+|this\s+|params\s+|ref\s+)?(?<type>[^,()]*?)\s*&?\s*(?<![\w$]){V}\s*[,)=]";

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py"), Syntax.Python, PythonDeclared(),
            @"^\s*{N}\s*(?:\.\s*(?:append|extend|insert|add|update|appendleft|extendleft)\s*\((?<added>.*)\)|\[(?<added>[^\]]*)\]\s*=(?!=)(?<added>.*)|\+=(?<added>.*))\s*$"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, JavaDeclared(),
            @"^\s*{N}\s*\.\s*(?:add|addAll|put|putAll|push|offer|addFirst|addLast|offerFirst|offerLast|append|insert)\s*\((?<added>.*)\)\s*;\s*$",
            BraceParameter),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, CSharpDeclared(),
            @"^\s*{N}\s*(?:\.\s*(?:Add|AddRange|Insert|Push|Enqueue|Append|AppendLine|TryAdd)\s*\((?<added>.*)\)|\[(?<added>[^\]]*)\]\s*=(?![=>])(?<added>.*))\s*;\s*$",
            BraceParameter),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, JavaScriptDeclared(),
            @"^\s*{N}\s*(?:\.\s*(?:push|unshift|add|set)\s*\((?<added>.*)\)|\[(?<added>[^\]]*)\]\s*=(?![=>])(?<added>.*)|\+=(?<added>.*))\s*;?\s*$"),
        new("go", CodePattern.Files(".go"), Syntax.CLike, GoDeclared(),
            @"^\s*(?:{N}\s*=\s*append\(\s*{N}\s*,(?<added>.*)\)|{N}\s*\[(?<added>[^\]]*)\]\s*=(?!=)(?<added>.*)|{N}\s*\.\s*(?:WriteString|WriteByte|WriteRune)\s*\((?<added>.*)\))\s*$",
            @"(?<![\w$]){V}\s+(?<type>[^,()]+?)\s*[,)]"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++", ".hpp", ".hh"), Syntax.CLike, CppDeclared(),
            @"^\s*{N}\s*(?:\.\s*(?:push_back|emplace_back|insert|emplace|push|push_front|emplace_front|append)\s*\((?<added>.*)\)|\[(?<added>[^\]]*)\]\s*=(?!=)(?<added>.*)|\+=(?<added>.*)|<<(?<added>.*))\s*;\s*$",
            BraceParameter),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, RustDeclared(),
            @"^\s*{N}\s*\.\s*(?:push|push_str|insert|extend|push_back|push_front)\s*\((?<added>.*)\)\s*;\s*$",
            @"(?<![\w$]){V}\s*:\s*(?<type>[^,()]+?)\s*[,)]", LastLineGivesBack: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, RubyDeclared(),
            @"^\s*{N}\s*(?:<<(?<added>.*)|\.\s*(?:push|append|unshift|prepend|add|store)(?:\s*\((?<added>.*)\)|\s+(?<added>.*))|\[(?<added>[^\]]*)\]\s*=(?![=~>])(?<added>.*)|\+=(?<added>.*))\s*$",
            LastLineGivesBack: true),
        new("php", CodePattern.Files(".php"), Syntax.CLike, PhpDeclared(),
            @"^\s*(?:{N}\s*\[(?<added>[^\]]*)\]\s*=(?![=>])(?<added>.*)|{N}\s*\.=(?<added>.*)|array_(?:push|unshift)\s*\(\s*{N}\s*,(?<added>.*)\))\s*;\s*$"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, LuaDeclared(),
            @"^\s*(?:table\.insert\s*\(\s*{N}\s*,(?<added>.*)\)|{N}\s*\[\s*#\s*{N}\s*\+\s*1\s*\]\s*=(?<added>.*)|{N}\s*\[(?<added>[^\]]*)\]\s*=(?!=)(?<added>.*))\s*;?\s*$"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlDeclared(),
            @"^\s*(?:(?:push|unshift)\s*(?:\(\s*{N}\s*,(?<added>.*)\)|{N}\s*,(?<added>.*))|\${B}\s*[\[{](?<added>[^\]}]*)[\]}]\s*=(?![=~])(?<added>.*))\s*;\s*$"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, DartDeclared(),
            @"^\s*{N}\s*(?:\.\s*(?:add|addAll|insert|insertAll|write|writeln|writeAll)\s*\((?<added>.*)\)|\[(?<added>[^\]]*)\]\s*=(?!=)(?<added>.*))\s*;\s*$",
            BraceParameter),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, PowerShellDeclared(),
            @"^\s*(?:\[void\]\s*|\$null\s*=\s*)?{N}\s*(?:\+=(?<added>.*)|\.\s*(?:Add|AddRange|Insert|Push|Enqueue|Append|AppendLine)\s*\((?<added>.*)\)(?:\s*\|\s*Out-Null)?|\[(?<added>[^\]]*)\]\s*=(?<added>.*))\s*;?\s*$",
            IgnoreCase: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaDeclared(),
            @"^\s*{N}\s*(?:\+\+?=(?<added>.*)|\.\s*(?:append|addOne|addAll|appendAll|prepend|put|update|enqueue|push|insert)\s*\((?<added>.*)\)|\((?<added>[^)]*)\)\s*=(?!=)(?<added>.*))\s*;?\s*$",
            @"(?<![\w$]){V}\s*:\s*(?<type>[^,()]+?)\s*[,)]", LastLineGivesBack: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, FSharpDeclared(),
            @"^\s*{N}\s*(?:\.\s*(?:Add|AddRange|Insert|Append|AppendLine|Push|Enqueue)\s*\((?<added>.*)\)(?:\s*\|>\s*ignore)?|\.\[(?<added>[^\]]*)\]\s*<-(?<added>.*))\s*$",
            @"\(\s*{V}\s*:\s*(?<type>[^)]+?)\s*\)", LastLineGivesBack: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-built-never-read", dialect.Files, dialect.Syntax,
            (id, source, masked) => FilledNeverRead(id, source, masked, dialect))),
    ];

    /// <summary>What the block's last line gives back: its value, and the line.</summary>
    private sealed record GivenBack(string Value, int Line);

    private static IEnumerable<LogicFinding> FilledNeverRead(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var lines = source.Lines;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

        for (var declared = 0; declared < masked.Count; declared++)
        {
            if (dialect.Declared.Match(masked[declared]) is not { Success: true } made) continue;

            var name = made.Groups["name"].Value;
            var mention = Mention(name, dialect, options);
            if (NamedOutside(lines, masked, name, dialect, options)) continue;

            // The block the variable is made in ends at the first line indented less than it.
            var indent = CodeText.Indentation(masked[declared]).Length;
            var end = declared + 1;
            while (end < masked.Count && (IsBlank(masked[end]) || CodeText.Indentation(masked[end]).Length >= indent)) end++;

            var adding = new Regex(dialect.Adding.Replace("{N}", Regex.Escape(name)).Replace("{B}", Regex.Escape(name.TrimStart('@', '%'))), options);
            var additions = 0;
            var onlyAdded = true;
            for (var line = declared + 1; line < end && onlyAdded; line++)
            {
                if (!mention.IsMatch(masked[line])) continue;
                if (adding.Match(masked[line]) is not { Success: true } added || NamesItInWhatIsAdded(lines[line], added, mention)) onlyAdded = false;
                else additions++;
            }
            if (!onlyAdded || additions == 0) continue;

            var givenBack = LastLineGivesBack(lines, masked, declared, end, indent, dialect);
            if (givenBack is null)
            {
                yield return new LogicFinding(id, declared + 1,
                    $"`{name}` is filled but never read - nothing gives it back or uses it - so everything put in it is lost", null);
                continue;
            }

            // Giving back one of the function's own parameters - what it was given - is most often where the built one was meant.
            var parameters = ParameterText(lines, masked, declared, dialect);
            var value = givenBack.Value.TrimEnd(';').Trim();
            var givesBackAParameter = PlainName().IsMatch(value) && parameters is not null &&
                Regex.IsMatch(parameters, $@"(?<![\w$@]){Regex.Escape(value)}(?![\w$])", options);

            yield return givesBackAParameter
                ? new LogicFinding(id, givenBack.Line + 1,
                    $"`{name}` is filled but never read: the function gives back `{value}`, what it was given, so everything put in `{name}` is lost",
                    Change(id, source, dialect, made, givenBack with { Value = value }, parameters!, options))
                : new LogicFinding(id, givenBack.Line + 1,
                    $"`{name}` is filled but never read: the function gives back `{value}`, and nothing uses `{name}`, so everything put in it is lost", null);
        }
    }

    /// <summary>Any mention of the name: Perl's array by its @, $# and its items' $, its hash by %, and its items' $ and @.</summary>
    private static Regex Mention(string name, Dialect dialect, RegexOptions options)
    {
        if (dialect.Language == "perl")
        {
            var bare = Regex.Escape(name[1..]);
            return name[0] == '@'
                ? new Regex($@"(?<![\w$@%])(?:@{bare}\b|\$#{bare}\b|\${bare}\s*\[)", options)
                : new Regex($@"(?<![\w$@%])(?:%{bare}\b|\${bare}\s*\{{|@{bare}\s*\{{)", options);
        }
        return new Regex($@"(?<![\w$]){Regex.Escape(name)}(?![\w$])", options);
    }

    /// <summary>Python's global and nonlocal names, and PHP's global ones, belong to more than the block.</summary>
    private static bool NamedOutside(IReadOnlyList<string> lines, IReadOnlyList<string> masked, string name, Dialect dialect, RegexOptions options) =>
        dialect.Language is "python" or "php" &&
        masked.Any(line => Regex.IsMatch(line, $@"^\s*(?:global|nonlocal)\b.*(?<![\w$]){Regex.Escape(name)}(?![\w$])", options));

    /// <summary>Whether what an add puts in names the variable itself - read from the line as written, as text is blanked in the masked one.</summary>
    private static bool NamesItInWhatIsAdded(string written, Match added, Regex mention) =>
        added.Groups["added"].Captures.Any(capture =>
            capture.Index + capture.Length <= written.Length && mention.IsMatch(written.Substring(capture.Index, capture.Length)));

    /// <summary>
    /// What the block's last statement gives back - a return's value, or, where a function's last line is its value, that
    /// line - when it is something other than the built variable; null when the block ends some other way.
    /// </summary>
    private static GivenBack? LastLineGivesBack(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int declared, int end, int indent, Dialect dialect)
    {
        var last = -1;
        for (var line = end - 1; line > declared && last < 0; line--)
            if (!IsBlank(masked[line]) && CodeText.Indentation(masked[line]).Length == indent) last = line;
        if (last < 0) return null;

        if (Returned().Match(masked[last]) is { Success: true } returned)
        {
            var value = lines[last].Trim();
            value = Returned().Match(value) is { Success: true } written ? written.Groups["value"].Value.Trim() : "";
            return value.Length > 0 ? new GivenBack(value, last) : null;
        }

        if (dialect.LastLineGivesBack && BareValue().Match(masked[last]) is { Success: true } bare && !NotAValue().IsMatch(bare.Groups["value"].Value))
            return new GivenBack(lines[last].Trim(), last);
        return null;
    }

    /// <summary>
    /// The built variable given back in place of the parameter the last line gives back - as it is, or made into the
    /// parameter's type by Scala's toList and its kind or F#'s List.ofSeq; null where the parameter's type is not the built one.
    /// </summary>
    private static LocalFix? Change(string id, SourceFile source, Dialect dialect, Match made, GivenBack givenBack, string parameters, RegexOptions options)
    {
        var lines = source.Lines;
        var name = made.Groups["name"].Value;
        var value = givenBack.Value;

        var replacement = name;
        if (dialect.Parameter is not null)
        {
            var bareValue = Regex.Escape(value);
            if (Regex.Match(parameters, dialect.Parameter.Replace("{V}", bareValue), options) is not { Success: true } parameter) return null;
            var parameterType = Normalized(parameter.Groups["type"].Value);
            replacement = AsType(name, BuiltType(made, dialect), parameterType, dialect) ?? "";
            if (replacement.Length == 0) return null;
        }

        var lastLine = lines[givenBack.Line];
        var position = Regex.Match(lastLine, $@"(?<![\w$@]){Regex.Escape(value)}(?![\w$])", options);
        if (!position.Success) return null;
        var changed = lastLine[..position.Index] + replacement + lastLine[(position.Index + position.Length)..];

        return LocalFix.ReplaceLine(id, $"Give back {replacement}, not {value}",
            $"`{name}` is filled and then never read, and the function gives back `{value}` - what it was given - so all it built is " +
            $"thrown away. Giving back `{replacement}` returns what it built.",
            source.Path, givenBack.Line + 1, changed);
    }

    /// <summary>
    /// The function's parameters as written: the header above the block - the line before a brace on its own - and, in Perl
    /// and PowerShell, the lines in it that take them, my (...) = @_ and param(...).
    /// </summary>
    private static string? ParameterText(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int declared, Dialect dialect)
    {
        var indent = CodeText.Indentation(masked[declared]).Length;
        var header = -1;
        for (var line = declared - 1; line >= 0 && header < 0; line--)
            if (!IsBlank(masked[line]) && CodeText.Indentation(masked[line]).Length < indent) header = line;
        if (header < 0) return null;
        if (masked[header].Trim() == "{")
            for (var line = header - 1; line >= 0; line--)
                if (!IsBlank(masked[line])) { header = line; break; }

        var taken = Enumerable.Range(header + 1, declared - header - 1)
            .Where(line => dialect.Language is "perl" or "powershell" && TakesParameters().IsMatch(masked[line]))
            .Select(line => lines[line]);
        return string.Join("\n", [lines[header], .. taken]);
    }

    /// <summary>The type the variable was made as: written beside its name, or the type it was made with.</summary>
    private static string BuiltType(Match made, Dialect dialect)
    {
        var written = made.Groups["type"].Success ? made.Groups["type"].Value.Trim() : "";
        if (written.Length > 0 && written != "var") return Normalized(written);

        var madeAs = made.Groups["made"].Success ? made.Groups["made"].Value.Trim() : "";
        if (dialect.Language == "go" && GoMade().Match(madeAs) is { Success: true } goType) return Normalized(goType.Groups["type"].Value);
        return Normalized(madeAs);
    }

    /// <summary>The built variable as the parameter's type: as it is where the types are the same, or by Scala's or F#'s conversion.</summary>
    private static string? AsType(string name, string builtType, string parameterType, Dialect dialect)
    {
        if (builtType.Length > 0 && builtType == parameterType) return name;

        if (dialect.Language == "scala" && ScalaBuffer().IsMatch(builtType) && ScalaConversion().Match(parameterType) is { Success: true } kind)
            return $"{name}.to{kind.Groups["kind"].Value}";
        if (dialect.Language == "fsharp" && FSharpGrowing().IsMatch(builtType))
        {
            if (parameterType.EndsWith("list", StringComparison.Ordinal)) return $"List.ofSeq {name}";
            if (parameterType.EndsWith("[]", StringComparison.Ordinal) || parameterType.EndsWith("array", StringComparison.Ordinal)) return $"Array.ofSeq {name}";
        }
        return null;
    }

    private static string Normalized(string type) => TypeNoise().Replace(type, "");

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    /// <summary>result = [], {}, set(), list(), dict(), deque(), defaultdict(list), Counter(), indented inside a block.</summary>
    [GeneratedRegex(@"^[ \t]+(?<name>(?>[A-Za-z_]\w*))\s*=\s*(?:\[\s*\]|\{\s*\}|(?:collections\.)?(?:set|list|dict|deque|defaultdict|Counter|OrderedDict)\(\s*\w*\s*\))\s*$")]
    private static partial Regex PythonDeclared();

    /// <summary>List&lt;Integer&gt; result = new ArrayList&lt;&gt;(); and the other collections and StringBuilder.</summary>
    [GeneratedRegex(@"^\s+(?:final\s+)?(?<type>(?>[\w.]+)(?:<[\w.<>,?\[\] ]*>)?(?:\[\])*)\s+(?<name>(?>\w+))\s*=\s*new\s+(?<made>ArrayList|LinkedList|HashSet|TreeSet|LinkedHashSet|HashMap|TreeMap|LinkedHashMap|ArrayDeque|Stack|StringBuilder|StringBuffer|PriorityQueue|Vector)\s*(?:<[\w.<>,?\[\] ]*>)?\s*\(\s*\)\s*;\s*$")]
    private static partial Regex JavaDeclared();

    /// <summary>List&lt;int&gt; result = new List&lt;int&gt;();, var result = new List&lt;int&gt;();, List&lt;int&gt; result = [];, and the other collections.</summary>
    [GeneratedRegex(@"^\s+(?<type>var|(?:List|HashSet|SortedSet|Dictionary|SortedDictionary|SortedList|Queue|Stack|StringBuilder|LinkedList|ArrayList)(?:<[\w.<>,?\[\] ]*>)?)\s+(?<name>(?>\w+))\s*=\s*(?:new\s*(?<made>(?:List|HashSet|SortedSet|Dictionary|SortedDictionary|SortedList|Queue|Stack|StringBuilder|LinkedList|ArrayList)(?:<[\w.<>,?\[\] ]*>)?)?\s*\(\s*\)|\[\s*\])\s*;\s*$")]
    private static partial Regex CSharpDeclared();

    /// <summary>const result = [];, let seen = new Set();, var counts = {};.</summary>
    [GeneratedRegex(@"^\s+(?:const|let|var)\s+(?<name>(?>[A-Za-z_$][\w$]*))\s*=\s*(?:\[\s*\]|\{\s*\}|new\s+(?:Set|Map|Array)\s*\(\s*\))\s*;?\s*$")]
    private static partial Regex JavaScriptDeclared();

    /// <summary>result := []int{}, result := make([]int, 0), counts := map[string]int{}, var result []int, var text strings.Builder.</summary>
    [GeneratedRegex(@"^\s+(?:(?<name>(?>\w+))\s*:=\s*(?<made>\[\](?>[\w.*]+)\{\s*\}|map\[[^\]]+\](?>[\w.*]+)\{\s*\}|make\(\s*(?:\[\]|map\[[^\]]+\])(?>[\w.*]+)[^)]*\))|var\s+(?<name>(?>\w+))\s+(?<type>\[\](?>[\w.*]+)|map\[[^\]]+\](?>[\w.*]+)|strings\.Builder))\s*$")]
    private static partial Regex GoDeclared();

    /// <summary>std::vector&lt;int&gt; result;, std::map&lt;std::string, int&gt; counts{};, std::string text;.</summary>
    [GeneratedRegex(@"^\s+(?<type>(?:std::)?(?:vector|set|unordered_set|multiset|map|unordered_map|multimap|deque|list|string|stack|queue|priority_queue|ostringstream|stringstream)\s*(?:<[\w:<>,\s*]*>)?)\s+(?<name>(?>\w+))\s*(?:\{\s*\}|=\s*\{\s*\})?\s*;\s*$")]
    private static partial Regex CppDeclared();

    /// <summary>let mut result = Vec::new();, let mut result: Vec&lt;i32&gt; = vec![];, let mut text = String::new();.</summary>
    [GeneratedRegex(@"^\s+let\s+mut\s+(?<name>(?>\w+))\s*(?::\s*(?<type>[^=;]+))?=\s*(?:(?:Vec|HashMap|HashSet|BTreeMap|BTreeSet|VecDeque|String)(?:::<[^>;]*>)?::(?:new|with_capacity)\([^)]*\)|vec!\[\s*\])\s*;\s*$")]
    private static partial Regex RustDeclared();

    /// <summary>result = [], seen = Set.new, counts = Hash.new(0).</summary>
    [GeneratedRegex(@"^\s+(?<name>(?>[a-z_]\w*))\s*=\s*(?:\[\s*\]|\{\s*\}|Set\.new(?:\(\s*\))?|Array\.new(?:\(\s*\))?|Hash\.new(?:\(\s*\w*\s*\))?)\s*$")]
    private static partial Regex RubyDeclared();

    /// <summary>$result = [];, $result = array();.</summary>
    [GeneratedRegex(@"^\s+(?<name>\$(?>\w+))\s*=\s*(?:\[\s*\]|array\(\s*\))\s*;\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PhpDeclared();

    /// <summary>local result = {}.</summary>
    [GeneratedRegex(@"^\s+local\s+(?<name>(?>\w+))\s*=\s*\{\s*\}\s*;?\s*$")]
    private static partial Regex LuaDeclared();

    /// <summary>my @result;, my @result = ();, my %seen;.</summary>
    [GeneratedRegex(@"^\s+my\s+(?<name>[@%](?>\w+))\s*(?:=\s*\(\s*\)\s*)?;\s*$")]
    private static partial Regex PerlDeclared();

    /// <summary>final List&lt;int&gt; result = [];, var result = &lt;int&gt;[];, final text = StringBuffer();.</summary>
    [GeneratedRegex(@"^\s+(?:(?:final|var)\s+(?:(?<type>(?:List|Set|Map|StringBuffer)(?:<[\w<>, ?]*>)?)\s+)?|(?<type>(?:List|Set|Map|StringBuffer)(?:<[\w<>, ?]*>)?)\s+)(?<name>(?>\w+))\s*=\s*(?:<[\w<>, ?]*>)?(?:\[\s*\]|\{\s*\}|StringBuffer\(\s*\))\s*;\s*$")]
    private static partial Regex DartDeclared();

    /// <summary>$result = @(), $counts = @{}, $result = [System.Collections.Generic.List[int]]::new(), New-Object System.Collections.ArrayList.</summary>
    [GeneratedRegex(@"^\s+(?<name>\$(?>\w+))\s*=\s*(?:@\(\s*\)|@\{\s*\}|\[System\.Collections\.(?:ArrayList|Generic\.(?:List|HashSet|Dictionary)\[[^\]]*\])\]::new\(\s*\)|New-Object\s+(?:-TypeName\s+)?(?:System\.Collections\.)?(?:ArrayList|Generic\.(?:List|HashSet|Dictionary)\[[^\]]*\]))\s*;?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellDeclared();

    /// <summary>val result = ListBuffer[Int](), val seen = mutable.Set[Int](), val text = new StringBuilder.</summary>
    [GeneratedRegex(@"^\s+val\s+(?<name>(?>\w+))\s*(?::\s*[^=]+)?=\s*(?:new\s+)?(?:(?:scala\.collection\.)?mutable\.)?(?<made>ListBuffer|ArrayBuffer|StringBuilder|HashMap|HashSet|LinkedHashMap|LinkedHashSet|Queue|Stack|TreeMap|TreeSet|ArrayDeque|Set|Map)(?:\[[^\]=]*\])?\s*(?:\(\s*\))?\s*$")]
    private static partial Regex ScalaDeclared();

    /// <summary>let result = ResizeArray&lt;int&gt;(), let seen = HashSet&lt;int&gt;(), let text = StringBuilder().</summary>
    [GeneratedRegex(@"^\s+let\s+(?<name>(?>\w+))\s*=\s*(?:new\s+)?(?<made>(?:System\.Collections\.Generic\.)?(?:List|HashSet|Dictionary|Queue|Stack)|ResizeArray|(?:System\.Text\.)?StringBuilder)\s*(?:<[^>]*>)?\s*\(\s*\)\s*$")]
    private static partial Regex FSharpDeclared();

    /// <summary>A line that is a return and nothing more, with what it gives back.</summary>
    [GeneratedRegex(@"^\s*return\b(?<value>[^;]*);?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Returned();

    /// <summary>A last line that is only a value: a name, perhaps with a method or field after it, as Rust's, Ruby's, Scala's and F#'s functions end.</summary>
    [GeneratedRegex(@"^\s*(?<value>[A-Za-z_$@][\w$]*(?:\.\w+(?:\(\s*\))?)*)\s*;?\s*$")]
    private static partial Regex BareValue();

    /// <summary>Words a last line can be that are not a value: a block's end, or a statement on its own.</summary>
    [GeneratedRegex(@"^(?:end|else|break|continue|next|redo|retry|pass|done|fi|then|do|begin|ensure|rescue|yield|nil|None|null|true|false|self|this)$")]
    private static partial Regex NotAValue();

    [GeneratedRegex(@"^[$@%]?[A-Za-z_]\w*$")]
    private static partial Regex PlainName();

    /// <summary>Perl's my (...) = @_; and shift, and PowerShell's param(...): lines that take a function's parameters.</summary>
    [GeneratedRegex(@"@_|\bshift\b|^\s*param\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex TakesParameters();

    [GeneratedRegex(@"^(?:make\(\s*)?(?<type>\[\][\w.*]+|map\[[^\]]+\][\w.*]+)")]
    private static partial Regex GoMade();

    [GeneratedRegex(@"\s+|\bconst\b|\bfinal\b|&")]
    private static partial Regex TypeNoise();

    [GeneratedRegex(@"^(?:mutable\.)?(?:ListBuffer|ArrayBuffer)")]
    private static partial Regex ScalaBuffer();

    [GeneratedRegex(@"^(?<kind>List|Vector|Seq|Array)\[")]
    private static partial Regex ScalaConversion();

    [GeneratedRegex(@"^(?:System\.Collections\.Generic\.)?(?:List|ResizeArray)")]
    private static partial Regex FSharpGrowing();
}
