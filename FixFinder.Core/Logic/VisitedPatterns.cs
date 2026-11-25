using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// The places a search has been, asked and never given anything: a search asks visited for a node - if node in visited:
/// return False - but nothing in the file ever adds a node to visited, so it stays empty, and the search goes to the same
/// places again; on a graph whose edges come back round - a cycle, or any undirected graph - it can go round them until the
/// program runs out of room for its calls, or for ever. QuixBugs' depth_first_search is this mistake, in Python and in Java.
/// The change adds the node to the set where the search goes on from it.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: the set - or the map, table, hash or array of flags a language keeps one in - is
/// made empty in the file, or handed over empty; nothing anywhere adds to it; every other line naming it asks it, reads its
/// size, or passes it on whole to a function of the file's own whose parameter has its name; and it is asked inside a loop
/// that goes on until it is done, or inside a function that calls itself. The change is offered where where it goes is plain -
/// after a guard that leaves, first in the else of a guard's if, or first in the block an if not in the set runs.
/// </remarks>
public static partial class VisitedPatterns
{
    /// <summary>How the file's blocks are written: by indentation, between braces, or up to an end.</summary>
    private enum Blocks { Indented, Braces, Ends }

    /// <summary>Whether the places are asked whether they have an item, or an item in them is read, true or not.</summary>
    private enum Kind { Set, Flags }

    /// <summary>One way a language keeps the places visited.</summary>
    /// <param name="Empty">An empty one, as an expression.</param>
    /// <param name="Asked">It asked - the set as "set", what it is asked for as "item", and "not" when the line asks whether it is not there.</param>
    /// <param name="Added">Anything giving the set {s} an item.</param>
    /// <param name="Add">The line adding {item} to {s} - {owned} is the item without a leading &amp;, {yes} true or 1 as the set was made - or null where no change is offered.</param>
    /// <param name="Made">The set {s} made {empty}, when not written as {s} = {empty}.</param>
    /// <param name="Typed">The type a parameter holding one is declared with, in a language that declares one.</param>
    private sealed record Form(Kind Kind, string Empty, Regex Asked, string Added, string? Add, string? Made = null, string? Typed = null);

    /// <param name="TypeFirst">Whether a parameter's type comes before its name: int visited[], Set&lt;Node&gt; seen.</param>
    /// <param name="Declared">A line naming {s} with no value - a field, or a variable given one later.</param>
    /// <param name="Sigil">What the set's name is shown with when the set group leaves it out: Perl's %.</param>
    private sealed record Dialect(string Id, IReadOnlySet<string> Files, Syntax Syntax, Blocks Blocks, Form[] Forms,
        bool TypeFirst = false, string? Declared = null, string Sigil = "", bool IgnoreCase = false);

    /// <summary>An item of the set read - [v], or [r][c] as one item r][c - and not given a value.</summary>
    private const string Index = @"\[(?<item>[^\[\]]+(?:\]\s*\[[^\[\]]+)*)\](?!\s*(?:\[|[-+*/|&^.]?=(?!=)|\+\+|--|:=))";

    private const string TypedDeclared = @"^\s*(?:[\w.:<>\[\],?*&]+\s+)+{s}\s*(?:\[[^\]]*\]\s*)*;\s*$";

    private const string AssignedEmpty = @"^\s*(?:[\w.:<>\[\],?*&]+\s+)*{s}\s*(?::[^=]*)?:?=\s*(?:{empty})\s*;?\s*$";

    private static readonly Dialect[] Dialects =
    [
        new("logic-python-visited-never-added", CodePattern.Files(".py", ".pyw"), Syntax.Python, Blocks.Indented,
        [
            new(Kind.Set, @"set\(\s*\)",
                new(@"(?<!\bfor\s+[\w\s,()]{0,80})(?<![\w.])(?<item>\([^()]*\)|[A-Za-z_][\w.]*(?:\[[^\[\]]*\])*)\s+(?<not>not\s+)?in\s+(?<set>[A-Za-z_]\w*)\b(?!\s*[.(\[])"),
                @"\b{s}\s*\.\s*(?:add|update|\w+_update)\s*\(|\b{s}\s*[|^]=", "{s}.add({item})"),
            new(Kind.Flags, @"\[\s*False\s*\]\s*\*\s*[\w.()+\-*/ ]*[\w)]|\[\s*\[\s*False\s*\]\s*\*[^\[\]]+for\s+\w+\s+in\s+[^\[\]]+\]|\[\s*False\s+for\s+\w+\s+in\s+[^\[\]]+\]",
                new(@"(?<![\w.])(?<set>[A-Za-z_]\w*)" + Index), @"\b{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)", "{s}[{item}] = True"),
        ]),
        new("logic-java-visited-never-added", CodePattern.Files(".java"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Set, @"new\s+(?:Hash|Tree|LinkedHash)Set\s*<[^;()]*>\s*\(\s*\)",
                new(@"(?<not>!\s*)?\b(?<set>[A-Za-z_]\w*)\.contains\(\s*(?<item>[^()]+?)\s*\)"), @"\b{s}\.(?:add|addAll)\(", "{s}.add({item});", Typed: @"\b(?:Hash|Tree|LinkedHash)?Set\s*<"),
            new(Kind.Flags, @"new\s+boolean\s*(?:\[[^\]]+\]\s*)+",
                new(@"(?<not>!\s*)?(?<![\w.])(?<set>[A-Za-z_]\w*)" + Index), @"\b{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)", "{s}[{item}] = true;", Typed: @"\bboolean\s*(?:\[\s*\])+"),
        ], TypeFirst: true, Declared: TypedDeclared),
        new("logic-csharp-visited-never-added", CodePattern.Files(".cs"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Set, @"new\s+(?:Hash|Sorted)Set\s*<[^;()]*>\s*\(\s*\)",
                new(@"(?<not>!\s*)?\b(?<set>[A-Za-z_]\w*)\.Contains\(\s*(?<item>[^()]+?)\s*\)"), @"\b{s}\.(?:Add|UnionWith)\(", "{s}.Add({item});",
                @"^\s*(?:(?:private|public|protected|internal|static|readonly)\s+)*(?:var\s+{s}\s*=\s*(?:{empty})|(?:Hash|Sorted)Set\s*<[^;=]*>\s+{s}\s*=\s*(?:{empty}|new\s*\(\s*\)|\[\s*\])|{s}\s*=\s*(?:{empty}|new\s*\(\s*\)))\s*;\s*$", @"\b(?:I|IReadOnly|Hash|Sorted)?Set\s*<|\bICollection\s*<"),
            new(Kind.Flags, @"new\s+bool\s*\[[^\]]+\]",
                new(@"(?<not>!\s*)?(?<![\w.])(?<set>[A-Za-z_]\w*)" + Index), @"\b{s}\s*\[[^\]]*\]\s*=(?!=)", "{s}[{item}] = true;", Typed: @"\bbool\s*\[[\s,]*\]"),
        ], TypeFirst: true, Declared: TypedDeclared),
        new("logic-js-visited-never-added", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Set, @"new\s+Set\s*(?:<[^<>]*>)?\s*\(\s*\)",
                new(@"(?<not>!\s*)?\b(?<set>[A-Za-z_$][\w$]*)\.has\(\s*(?<item>[^()]+?)\s*\)"), @"\b{s}\.add\(", "{s}.add({item});"),
            new(Kind.Flags, @"\{\s*\}|\[\s*\]|(?:new\s+)?Array\s*\([^()]*\)\s*\.fill\(\s*(?:false|0)\s*\)",
                new(@"(?<not>!\s*)?(?<![\w$.])(?<set>[A-Za-z_$][\w$]*)" + Index + @"|(?<![\w$.])(?<item>[A-Za-z_$][\w$.]*)\s+in\s+(?<set>[A-Za-z_$][\w$]*)\b(?!\s*[.(\[])"),
                @"\b{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)|\b{s}\.(?:push|fill|splice|unshift|set)\(|\b{s}\.[A-Za-z_$][\w$]*\s*=(?!=)", "{s}[{item}] = true;"),
        ]),
        new("logic-c-visited-never-added", CodePattern.Files(".c"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Flags, @"\{\s*(?:0|false)?\s*\}",
                new(@"(?<not>!\s*)?(?<![\w.>])(?<set>[A-Za-z_]\w*)" + Index),
                @"\b{s}\s*(?:\[[^\]]*\]\s*)+(?:[-+*/|&^]?=(?!=)|\+\+|--)|(?:\+\+|--)\s*{s}\s*\[", "{s}[{item}] = {yes};",
                @"^(?:\s*(?:static\s+)?(?:bool|_Bool|int|char|unsigned(?:\s+(?:char|int))?)\s+{s}\s*(?:\[[^\]]+\]\s*)+=\s*(?:{empty})|(?:static\s+)?(?:bool|_Bool|int|char|unsigned(?:\s+(?:char|int))?)\s+{s}\s*(?:\[[^\]]+\]\s*)+|\s+static\s+(?:bool|_Bool|int|char)\s+{s}\s*(?:\[[^\]]+\]\s*)+)\s*;\s*$",
                @"\b(?:bool|_Bool|int|char|unsigned(?:\s+(?:char|int))?)\b[^,]*(?:\[[^\]]*\]|\*)"),
        ], TypeFirst: true),
        new("logic-cpp-visited-never-added", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Set, @"(?:std::)?(?:unordered_)?set\s*<[^;()]*>\s*(?:\{\s*\}|\(\s*\))",
                new(@"(?<not>!\s*)?\b(?<set>[A-Za-z_]\w*)\.(?:count|contains)\(\s*(?<item>[^()]+?)\s*\)|\b(?<set>[A-Za-z_]\w*)\.find\(\s*(?<item>[^()]+?)\s*\)\s*(?:(?<not>==)|!=)\s*\k<set>\.end\(\s*\)"),
                @"\b{s}\.(?:insert|emplace|merge)\(", "{s}.insert({item});",
                @"^\s*(?:static\s+)?(?:std::)?(?:unordered_)?set\s*<[^;=]*>\s+{s}\s*(?:\{\s*\}|=\s*\{\s*\})?\s*;\s*$",
                @"\b(?:std::)?(?:unordered_)?set\s*<"),
            new(Kind.Flags, @"(?:std::)?vector\s*<\s*(?:bool|int|char)\s*>\s*\(\s*[^,()]+(?:,\s*(?:false|0)\s*)?\)",
                new(@"(?<not>!\s*)?(?<![\w.>])(?<set>[A-Za-z_]\w*)" + Index),
                @"\b{s}\s*(?:\[[^\]]*\]\s*)+(?:[-+*/|&^]?=(?!=)|\+\+|--)|(?:\+\+|--)\s*{s}\s*\[|\b{s}\.(?:assign|push_back|emplace_back|resize|flip)\(", "{s}[{item}] = true;",
                @"^(?:\s*(?:static\s+)?(?:std::)?vector\s*<\s*(?:bool|int|char)\s*>\s+{s}\s*[({]\s*[^,(){}]+(?:,\s*(?:false|0)\s*)?[)}]|\s*(?:static\s+)?(?:bool|int|char)\s+{s}\s*(?:\[[^\]]+\]\s*)+=\s*\{\s*(?:0|false)?\s*\}|(?:static\s+)?(?:bool|int|char)\s+{s}\s*(?:\[[^\]]+\]\s*)+)\s*;\s*$",
                @"\b(?:std::)?vector\s*<\s*(?:bool|int|char)\s*>|\b(?:bool|int|char)\b[^,]*(?:\[[^\]]*\]|\*)"),
        ], TypeFirst: true),
        new("logic-go-visited-never-added", CodePattern.Files(".go"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Flags, @"map\[[^\]]+\]bool\s*\{\s*\}|make\(\s*(?:map\[[^\]]+\]|\[\])bool\b[^()]*\)",
                new(@"(?<not>!\s*)?(?<![\w.])(?<set>[A-Za-z_]\w*)" + Index), @"\b{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)", "{s}[{item}] = true", Typed: @"(?:map\[[^\]]+\]|\[\])bool\b"),
            new(Kind.Set, @"map\[[^\]]+\]struct\s*\{\s*\}\s*\{\s*\}|make\(\s*map\[[^\]]+\]struct\s*\{\s*\}[^()]*\)",
                new(@"\b_\s*,\s*(?<flag>[A-Za-z_]\w*)\s*:=\s*(?<set>[A-Za-z_]\w*)\[(?<item>[^\[\]]+)\]\s*;\s*(?<not>!\s*)?\k<flag>\b"),
                @"\b{s}\s*\[[^\]]*\]\s*=(?!=)", "{s}[{item}] = struct{}{}", Typed: @"map\[[^\]]+\]struct\s*\{\s*\}"),
        ], Declared: @"^\s*var\s+{s}\s+[^=]+$"),
        new("logic-rust-visited-never-added", CodePattern.Files(".rs"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Set, @"(?:Hash|BTree)Set\s*(?:::\s*<[^<>]*>)?\s*::\s*new\(\s*\)",
                new(@"(?<not>!\s*)?\b(?<set>[A-Za-z_]\w*)\.contains\(\s*(?<item>[^()]+?)\s*\)"), @"\b{s}\.(?:insert|extend|append)\(", "{s}.insert({owned});", Typed: @":\s*&?\s*(?:mut\s+)?(?:std::collections::)?(?:Hash|BTree)Set\s*<"),
            new(Kind.Flags, @"vec!\s*\[\s*false\s*;\s*[^\]]+\]",
                new(@"(?<not>!\s*)?(?<![\w.])(?<set>[A-Za-z_]\w*)" + Index), @"\b{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)", "{s}[{item}] = true;", Typed: @":\s*&?\s*(?:mut\s+)?(?:Vec\s*<\s*bool\s*>|\[\s*bool\b)"),
        ]),
        new("logic-ruby-visited-never-added", CodePattern.Files(".rb"), Syntax.Python, Blocks.Ends,
        [
            new(Kind.Set, @"Set\.new(?:\(\s*\))?|Set\[\s*\]",
                new(@"(?<not>!\s*)?\b(?<set>[a-z_]\w*)\.(?:include\?|member\?)(?:\(\s*(?<item>[^()]+?)\s*\)|\s+(?<item>[a-z_]\w*))"),
                @"\b{s}\.(?:add\??|merge)\b|\b{s}\s*<<", "{s}.add({item})"),
            new(Kind.Flags, @"\{\s*\}|Hash\.new(?:\(\s*(?:false|nil)?\s*\))?|Array\.new\(\s*[^,()]+,\s*false\s*\)",
                new(@"(?<not>!\s*)?(?<![\w.@$])(?<set>[a-z_]\w*)" + Index), @"\b{s}\s*(?:\[[^\]]*\]\s*)+(?:\|\|)?=(?![=~>])|\b{s}\.store\(", "{s}[{item}] = true"),
        ]),
        new("logic-lua-visited-never-added", CodePattern.Files(".lua"), Syntax.Lua, Blocks.Ends,
        [
            new(Kind.Flags, @"\{\s*\}",
                new(@"(?<not>\bnot\s+)?(?<![\w.])(?<set>[A-Za-z_]\w*)" + Index),
                @"\b{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)|\b{s}\.[A-Za-z_]\w*\s*=(?!=)|\btable\.insert\(\s*{s}\b", "{s}[{item}] = true"),
        ]),
        new("logic-php-visited-never-added", CodePattern.Files(".php"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Flags, @"\[\s*\]|array\(\s*\)",
                new(@"(?<not>!\s*)?\bisset\(\s*(?<set>\$[A-Za-z_]\w*)\[(?<item>[^\[\]]+)\]\s*\)|(?<not>!\s*)?\barray_key_exists\(\s*(?<item>[^,()]+?)\s*,\s*(?<set>\$[A-Za-z_]\w*)\s*\)"),
                @"{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)|\barray_(?:push|unshift|splice)\(\s*{s}\b", "{s}[{item}] = true;"),
            new(Kind.Set, @"\[\s*\]|array\(\s*\)",
                new(@"(?<not>!\s*)?\bin_array\(\s*(?<item>[^,()]+?)\s*,\s*(?<set>\$[A-Za-z_]\w*)\s*(?:,\s*true\s*)?\)"),
                @"{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)|\barray_(?:push|unshift|splice)\(\s*{s}\b", "{s}[] = {item};"),
        ]),
        new("logic-perl-visited-never-added", CodePattern.Files(".pl", ".pm"), Syntax.Perl, Blocks.Braces,
        [
            new(Kind.Flags, @"\(\s*\)",
                new(@"(?<not>!\s*)?\bexists\s+\$(?<set>[A-Za-z_]\w*)\{(?<item>[^{}]+)\}|(?<not>!\s*)?(?<!\bexists\s+)\$(?<set>[A-Za-z_]\w*)\{(?<item>[^{}]+)\}(?!\s*(?:\+\+|--|[-+.*/|&]?=(?![=~])|//=|\|\|=|\{|\[|->))"),
                @"\${s}\s*\{[^}]*\}\s*(?:[-+.*/|&]?=(?![=~])|//=|\|\|=|\+\+|--)|(?:\+\+|--)\s*\${s}\s*\{|@{s}\s*\{|%{s}\s*=\s*\((?!\s*\))", "${s}{{item}} = 1;",
                @"^\s*(?:my|our)\s+%{s}\s*(?:=\s*(?:{empty}))?\s*;\s*$"),
        ], Sigil: "%"),
        new("logic-powershell-visited-never-added", CodePattern.Files(".ps1"), Syntax.Python, Blocks.Braces,
        [
            new(Kind.Set, @"\[(?:System\.Collections\.Generic\.)?HashSet\[[^\]]+\]\]::new\(\s*\)",
                new(@"(?<not>-not\s+|!\s*)?(?<set>\$[A-Za-z_]\w*)\.Contains\(\s*(?<item>[^()]+?)\s*\)", RegexOptions.IgnoreCase),
                @"{s}\.(?:Add|UnionWith)\(", "[void]{s}.Add({item})"),
            new(Kind.Flags, @"@\{\s*\}",
                new(@"(?<not>-not\s+|!\s*)?(?<set>\$[A-Za-z_]\w*)\.ContainsKey\(\s*(?<item>[^()]+?)\s*\)|(?<not>-not\s+|!\s*)?(?<set>\$[A-Za-z_]\w*)" + Index, RegexOptions.IgnoreCase),
                @"{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)|{s}\.(?:Add|Set_Item)\(|{s}\.[A-Za-z_]\w*\s*=(?!=)", "{s}[{item}] = $true"),
            new(Kind.Set, @"@\(\s*\)",
                new(@"(?<set>\$[A-Za-z_]\w*)\s+-(?<not>not)?contains\s+(?<item>\$[\w.]+)|(?<item>\$[\w.]+)\s+-(?<not>not)?in\s+(?<set>\$[A-Za-z_]\w*)\b", RegexOptions.IgnoreCase),
                @"{s}\s*\+=|{s}\.Add\(", "{s} += {item}"),
        ], IgnoreCase: true),
        new("logic-dart-visited-never-added", CodePattern.Files(".dart"), Syntax.CLike, Blocks.Braces,
        [
            new(Kind.Set, @"<[^<>]*>\s*\{\s*\}|(?:Hash|Linked)?Set\s*<[^;()]*>\s*\(\s*\)",
                new(@"(?<not>!\s*)?\b(?<set>[A-Za-z_]\w*)\.contains\(\s*(?<item>[^()]+?)\s*\)"), @"\b{s}\.(?:add|addAll)\(", "{s}.add({item});",
                Typed: @"\bSet\s*<",
                Made: @"^\s*(?:(?:final|var|late|static)\s+)*(?:Set\s*<[^;=]*>\s+{s}\s*=\s*(?:{empty}|\{\s*\})|{s}\s*=\s*(?:{empty}))\s*;\s*$"),
            new(Kind.Flags, @"List\s*(?:<\s*bool\s*>)?\.filled\(\s*[^,()]+,\s*false\s*\)",
                new(@"(?<not>!\s*)?(?<![\w.])(?<set>[A-Za-z_]\w*)" + Index), @"\b{s}\s*(?:\[[^\]]*\]\s*)+=(?!=)", "{s}[{item}] = true;", Typed: @"\bList\s*<\s*bool\s*>"),
        ], TypeFirst: true, Declared: TypedDeclared),
        new("logic-scala-visited-never-added", CodePattern.Files(".scala", ".sc"), Syntax.Scala, Blocks.Braces,
        [
            new(Kind.Set, @"(?:(?:scala\.collection\.)?mutable\.)?(?:Hash|Linked)?Set(?:\[[^\]]*\])?\s*(?:\(\s*\)|\.empty(?:\[[^\]]*\])?)",
                new(@"(?<not>!\s*)?\b(?<set>[A-Za-z_]\w*)\.contains\(\s*(?<item>[^()]+?)\s*\)"), @"\b{s}\s*(?:\+\+?=|--?=)|\b{s}\.(?:add|addOne|addAll)\(", "{s} += {item}", Typed: @":\s*(?:(?:scala\.collection\.)?mutable\.)?(?:Hash|Linked)?Set\s*\["),
        ]),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        [.. Dialects.Select(dialect => new CodePattern(dialect.Id, dialect.Files, dialect.Syntax,
            (id, source, masked) => NeverAdded(id, source, masked, dialect), Severity.Error, Confidence.Likely))];

    /// <summary>Saying only what is in the set: how many, or the set itself printed.</summary>
    private const string Reads = @"\b(?:len|print|println|puts|p|pp|sorted|repr|str)\s*\(\s*{s}\s*\)|\b(?:console\.log|System\.out\.println|Console\.WriteLine|fmt\.Println)\s*\(\s*{s}\s*\)|{s}\s*\.\s*(?:size|isEmpty|length|Count|empty\??|is_empty|len|Length|nonEmpty)\b(?!\s*=)";

    [GeneratedRegex(@"\b(?:return|continue|break|next|redo|exit|die|goto)\b")]
    private static partial Regex LeavesTheWay();

    [GeneratedRegex(@"\b(?:if|elif|elsif|elseif|while|unless|until)\b|\?|&&|\|\||\band\b|\bor\b|-and\b|-or\b")]
    private static partial Regex Condition();

    /// <summary>Whether the line is a condition: an if, a loop's own, or any test joined with and, or, or ?.</summary>
    private static bool InCondition(string text) => Condition().IsMatch(text) || LoopsUntilDone().IsMatch(text);

    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:do\b|while\b|until\b|loop\b|repeat\b|for\s*\(\s*;\s*;\s*\)|for\s*\{|for\s+(?![^{]*\brange\b)(?![^{]*;)[^{]*\{)", RegexOptions.IgnoreCase)]
    private static partial Regex LoopsUntilDone();

    [GeneratedRegex(@"^(?:if|else|for|foreach|while|switch|catch|using|lock|return|new|throw|await|case|delete|yield|sizeof|when|until|unless|elif|elsif|do|in|and|or|not|typeof|instanceof|match|loop|fixed|checked|unchecked|synchronized|function|fn|func|def|sub|print|echo)$", RegexOptions.IgnoreCase)]
    private static partial Regex Keyword();

    private static IEnumerable<LogicFinding> NeverAdded(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var names = dialect.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var functionsRead = new Lazy<List<Function>>(() => Functions(masked, dialect));
        var parameters = new Lazy<List<string>>(() => [.. functionsRead.Value.SelectMany(function => function.Parameters ?? [])]);
        var said = new HashSet<string>(names);
        var conditions = masked.Where(InCondition).ToList();
        var comparison = dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var form in dialect.Forms)
        {
            // A parameter holding the set: a line names it with the form's type, where the language declares one, and a
            // function's parameter has its name. Only read when the set is not made empty here, as the file's functions are.
            bool IsAParameter(string set) =>
                masked.Any(text => text.Contains(set, comparison) && (form.Typed is null || Regex.IsMatch(text, form.Typed))) &&
                parameters.Value.Any(each => Holds(each, set, form, dialect));

            var asked = conditions.SelectMany(text => form.Asked.Matches(text)).Select(ask => ask.Groups["set"].Value).Distinct(names).ToList();
            foreach (var set in asked)
            {
                if (said.Contains(set)) continue;

                var name = Regex.Escape(set);
                var added = new Regex(form.Added.Replace("{s}", name, StringComparison.Ordinal), options);
                if (masked.Any(text => added.IsMatch(text))) continue;

                // Only a set made empty here, or a parameter, can be one the file shows is only ever empty.
                var made = new Regex(MadeTemplate(form).Replace("{s}", name, StringComparison.Ordinal), options);
                var madeHere = masked.Any(text => text.Contains(set, comparison) && made.IsMatch(text));
                if (!madeHere && !IsAParameter(set)) continue;

                var functions = functionsRead.Value;
                if (OnlyEverEmpty(masked, dialect, form, set, functions, options) is not { } madeLine) continue;
                if (SearchesWith(masked, dialect, form, set, functions, options) is not { } search) continue;

                said.Add(set);
                var line = search.Line;
                var recursion = search.Function is not null;
                var ask = form.Asked.Matches(masked[line]).First(each => SameName(each.Groups["set"].Value, set, dialect));
                var item = source.Lines[line].Substring(ask.Groups["item"].Index, ask.Groups["item"].Length).Trim();
                var shown = dialect.Sigil + set;
                var element = Element(dialect, set, item);
                var what = form.Kind == Kind.Set
                    ? $"Line {line + 1} asks `{shown}` whether it has `{item}`, but no line of the file ever adds anything to `{shown}`: it stays empty, so the answer is always no."
                    : $"Line {line + 1} reads `{element}`, but no line of the file ever sets anything in `{shown}`: nothing in it is ever set, so `{element}` is never true.";
                var then = recursion
                    ? "The search never knows where it has been, so it goes to the same places again - and on a graph whose edges come back round, a cycle or any undirected graph, it can call itself round them until the program runs out of room for its calls and stops with an error."
                    : "The search never knows where it has been, so it goes to the same places again - and on a graph whose edges come back round, a cycle or any undirected graph, it can go round them for ever, and the program then never stops.";

                yield return new LogicFinding(id, line + 1, $"{what} {then}", Change(id, source, masked, dialect, form, search, ask, set, item, masked[madeLine], options))
                {
                    Severity = Severity.Error,
                    Confidence = Confidence.Likely,
                };
            }
        }
    }

    private static string MadeTemplate(Form form) => (form.Made ?? AssignedEmpty).Replace("{empty}", form.Empty, StringComparison.Ordinal);

    private static string Element(Dialect dialect, string set, string item) =>
        dialect.Sigil == "%" ? $"${set}{{{item}}}" : $"{set}[{item}]";

    private static bool SameName(string first, string second, Dialect dialect) =>
        string.Equals(first, second, dialect.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>A function the file writes: where its header is, its name, its parameters - null when they go on past the line - and the last line of its body.</summary>
    private sealed record Function(int Header, string Name, IReadOnlyList<string>? Parameters, Lazy<int> BodyLast)
    {
        public int End => BodyLast.Value;
    }

    /// <summary>
    /// The line making the set empty - or, when it is handed over empty, the header or the call that does it - when the file
    /// shows the set is only ever empty: every line naming it makes it, declares it, asks it, reads what is in it, or passes it
    /// on whole to a function of the file's own whose parameter has its name; and each such function is called in the file, by
    /// every call, with the set or with an empty one. Null when anything else names it.
    /// </summary>
    private static int? OnlyEverEmpty(IReadOnlyList<string> masked, Dialect dialect, Form form, string set, List<Function> functions, RegexOptions options)
    {
        var name = Regex.Escape(set);
        var named = dialect.Sigil == "%"
            ? new Regex($@"(?:%{name}\b|[$@]{name}\s*\{{)", options)
            : new Regex($@"(?<![\w$.>]){name}(?![\w$])", options);
        var made = new Regex(MadeTemplate(form).Replace("{s}", name, StringComparison.Ordinal), options);
        var declared = dialect.Declared is { } declaration ? new Regex(declaration.Replace("{s}", name, StringComparison.Ordinal), options) : null;
        var reads = new Regex(Reads.Replace("{s}", name, StringComparison.Ordinal), options);
        var emptyAlone = new Regex($@"^\s*(?:&\s*(?:mut\s+)?)?(?:{form.Empty})\s*$", options);

        // A header whose parameters go on past its line, naming the set, may take it in a way not read here.
        if (functions.Any(function => function.Parameters is null && named.IsMatch(masked[function.Header]))) return null;

        // The functions taking the set by its name, and where in their parameters.
        var taking = new List<(Function Function, int At)>();
        foreach (var function in functions.Where(function => function.Parameters is not null))
        {
            var at = function.Parameters!.ToList().FindIndex(each => Holds(each, set, form, dialect));
            if (at >= 0) taking.Add((function, at));
        }

        int? madeLine = null;
        for (var line = 0; line < masked.Count; line++)
        {
            var text = masked[line];
            if (!named.IsMatch(text)) continue;
            if (made.IsMatch(text)) { madeLine ??= line; continue; }
            if (declared?.IsMatch(text) == true || taking.Any(each => each.Function.Header == line)) continue;

            var covered = new List<(int Start, int End)>();
            if (InCondition(text))
            {
                foreach (Match each in form.Asked.Matches(text))
                    if (SameName(each.Groups["set"].Value, set, dialect)) covered.Add((each.Index, each.Index + each.Length));
            }

            foreach (Match each in reads.Matches(text)) covered.Add((each.Index, each.Index + each.Length));
            foreach (var (function, at) in taking)
            {
                foreach (var call in Calls(text, function.Name, options, dialect.Blocks == Blocks.Braces))
                {
                    if (call.Arguments is null) return null;
                    if (at < call.Arguments.Count && Bare(call.Arguments[at].Text, set, options)) covered.Add((call.Arguments[at].Start, call.Arguments[at].End));
                }
            }

            if (named.Matches(text).Any(naming => !covered.Any(span => naming.Index >= span.Start && naming.Index < span.End))) return null;
        }

        // Each function taking it is called here, and every call gives it the set, or an empty one.
        foreach (var (function, at) in taking)
        {
            var calls = Enumerable.Range(0, masked.Count).Where(line => line != function.Header)
                .SelectMany(line => Calls(masked[line], function.Name, options, dialect.Blocks == Blocks.Braces).Select(call => (Line: line, Call: call))).ToList();
            if (calls.Count == 0) return null;

            var given = Regex.Match(function.Parameters![at], @"=\s*(?<value>.+)$");
            var emptyByDefault = given.Success && emptyAlone.IsMatch(given.Groups["value"].Value);
            if (emptyByDefault) madeLine ??= function.Header;

            foreach (var (line, call) in calls)
            {
                if (call.Arguments is null) return null;
                if (at >= call.Arguments.Count)
                {
                    if (!emptyByDefault) return null;
                    continue;
                }

                var argument = call.Arguments[at].Text;
                if (Bare(argument, set, options)) continue;
                if (!emptyAlone.IsMatch(argument)) return null;
                madeLine ??= line;
            }
        }

        return madeLine;
    }

    /// <summary>Whether the parameter is the set: it has the set's name, and - in a language that declares one - the form's type.</summary>
    private static bool Holds(string parameter, string set, Form form, Dialect dialect) =>
        SameName(ParameterName(parameter, dialect), set, dialect) && (form.Typed is null || Regex.IsMatch(parameter, form.Typed));

    /// <summary>Whether an argument is the set itself: visited, &amp;visited, &amp;mut visited.</summary>
    private static bool Bare(string argument, string set, RegexOptions options) =>
        Regex.IsMatch(argument, $@"^\s*(?:&\s*(?:mut\s+)?|ref\s+)?{Regex.Escape(set)}\s*$", options);

    /// <summary>The line asking the set, and the search it is asked in: the loop's header, and the function when it is one calling itself.</summary>
    private sealed record Search(int Line, int Header, Function? Function);

    /// <summary>
    /// The first line asking the set inside a search - a loop that goes on until it is done, or a function that calls itself,
    /// whichever holds the line more closely. Null when no line asking it is.
    /// </summary>
    private static Search? SearchesWith(IReadOnlyList<string> masked, Dialect dialect, Form form, string set, List<Function> functions, RegexOptions options)
    {
        for (var line = 0; line < masked.Count; line++)
        {
            if (!InCondition(masked[line]) || !form.Asked.Matches(masked[line]).Any(each => SameName(each.Groups["set"].Value, set, dialect))) continue;

            if (LoopsUntilDone().IsMatch(masked[line])) return new Search(line, line, null);
            foreach (var header in Enclosing(masked, line, dialect.Blocks))
            {
                if (LoopsUntilDone().IsMatch(masked[header])) return new Search(line, header, null);
                if (functions.FirstOrDefault(function => function.Header == header) is not { } function) continue;

                if (CallsItself(masked, function, dialect, options)) return new Search(line, header, function);
                break;
            }
        }

        return null;
    }

    /// <summary>Whether a line of the function's body calls it - by name and brackets, passed on itself as in map(reach), or, in PowerShell, by its name as a command.</summary>
    private static bool CallsItself(IReadOnlyList<string> masked, Function function, Dialect dialect, RegexOptions options)
    {
        var command = new Regex($@"(?<![\w$-]){Regex.Escape(function.Name)}(?![\w-])", options);
        var passedOn = new Regex($@"[(,]\s*{Regex.Escape(function.Name)}\s*[,)]", options);
        for (var inside = function.Header + 1; inside <= function.End && inside < masked.Count; inside++)
        {
            if (Calls(masked[inside], function.Name, options, dialect.Blocks == Blocks.Braces).Any() || passedOn.IsMatch(masked[inside])) return true;
            if (dialect.IgnoreCase && command.IsMatch(masked[inside])) return true;
        }

        return false;
    }

    /// <summary>The lines heading each block the line is in, the closest first - for a { on a line of its own, the line above it.</summary>
    private static IEnumerable<int> Enclosing(IReadOnlyList<string> masked, int line, Blocks blocks)
    {
        if (blocks != Blocks.Braces)
        {
            var indent = CodeText.Indentation(masked[line]).Length;
            for (var above = line - 1; above >= 0 && indent > 0; above--)
            {
                if (masked[above].Trim().Length == 0) continue;
                var width = CodeText.Indentation(masked[above]).Length;
                if (width >= indent) continue;
                indent = width;
                yield return above;
            }

            yield break;
        }

        var depth = 0;
        for (var above = line - 1; above >= 0; above--)
        {
            var text = masked[above];
            for (var at = text.Length - 1; at >= 0; at--)
            {
                if (text[at] == '}') depth++;
                else if (text[at] == '{' && --depth < 0)
                {
                    depth = 0;
                    yield return text.Trim().StartsWith('{') ? PreviousCode(masked, above) : above;
                    break;
                }
            }
        }
    }

    private static int PreviousCode(IReadOnlyList<string> masked, int line)
    {
        for (var above = line - 1; above >= 0; above--) if (masked[above].Trim().Length > 0) return above;
        return line;
    }

    [GeneratedRegex(@"\b(?:def|fn|func|function|sub)\s+(?:\([^()]*\)\s*)?(?:[\w$]+\s*(?:\.|::|:)\s*)*(?<name>[A-Za-z_$][\w$-]*[?!]?)", RegexOptions.IgnoreCase)]
    private static partial Regex DefinedByWord();

    [GeneratedRegex(@"(?<![\w$])(?<name>[A-Za-z_$][\w$]*)\s*[:=]\s*(?:async\s+)?(?:function\b|\([^()]*\)\s*(?::[^=]*)?=>|[A-Za-z_$][\w$]*\s*=>)")]
    private static partial Regex DefinedByValue();

    [GeneratedRegex(@"^\s*(?:[\w.:<>\[\],?*&]+\s+)*(?<name>[A-Za-z_]\w*)\s*\([^;]*$")]
    private static partial Regex DefinedByType();

    [GeneratedRegex(@"\)\s*(?:const\s*)?(?:throws\s+[\w.,\s]+)?(?:\{\s*)?$")]
    private static partial Regex EndsAHeader();

    /// <summary>Every function the file writes, found from its header line.</summary>
    private static List<Function> Functions(IReadOnlyList<string> masked, Dialect dialect)
    {
        var functions = new List<Function>();
        for (var line = 0; line < masked.Count; line++)
        {
            var text = masked[line];
            var found = DefinedByWord().Match(text);
            if (!found.Success) found = DefinedByValue().Match(text);
            if (!found.Success && dialect.Blocks == Blocks.Braces && !text.Contains('=') && !Regex.IsMatch(text, @"^\s*(?:\}|else\b|return\b|new\b)") && EndsAHeader().IsMatch(text))
            {
                found = DefinedByType().Match(text);
                if (found.Success && Regex.IsMatch(text, @"^\s*[A-Za-z_]\w*\s*\(") && !Regex.IsMatch(text, @"\{\s*$")) continue;
            }

            if (!found.Success || Keyword().IsMatch(found.Groups["name"].Value)) continue;

            var opening = text.IndexOf('(', found.Groups["name"].Index + found.Groups["name"].Length);
            IReadOnlyList<string>? parameters = null;
            if (opening < 0) parameters = dialect.Sigil == "%" || Regex.IsMatch(text, @"\{\s*$") ? [] : null;
            else if (Brackets.ClosingParenthesis(text, opening) is { } closing)
            {
                parameters = [.. TopLevel(text, opening + 1, closing).Select(piece => piece.Text.Trim()).Where(piece => piece.Length > 0)
                    .SkipWhile(piece => Regex.IsMatch(piece, @"^(?:&\s*(?:mut\s+)?|mut\s+)?(?:self|cls)$"))];
            }

            var header = line;
            functions.Add(new Function(line, found.Groups["name"].Value, parameters, new Lazy<int>(() => BodyEnd(masked, header, dialect.Blocks))));
        }

        return functions;
    }

    /// <summary>The last line of the block a header opens.</summary>
    private static int BodyEnd(IReadOnlyList<string> masked, int header, Blocks blocks)
    {
        if (blocks == Blocks.Braces)
        {
            var opener = masked[header].Contains('{') ? header : BraceBlocks.NextCode(masked, header + 1);
            return opener >= 0 && BlockAfter(masked, opener) is { } end ? end : header;
        }

        var lines = BodyBelow(masked, header);
        if (lines.Count == 0) return header;
        var last = lines[^1];
        return blocks == Blocks.Ends && BraceBlocks.NextCode(masked, last + 1) is var next && next > 0 && Regex.IsMatch(masked[next], @"^\s*end\b") ? next : last;
    }

    /// <summary>The line of the } closing the last { on the line, or null.</summary>
    private static int? BlockAfter(IReadOnlyList<string> masked, int line)
    {
        var opening = masked[line].LastIndexOf('{');
        if (opening < 0) return null;

        var depth = 0;
        for (var at = line; at < masked.Count; at++)
        {
            var text = masked[at];
            for (var column = at == line ? opening : 0; column < text.Length; column++)
            {
                if (text[column] == '{') depth++;
                else if (text[column] == '}' && --depth == 0) return at;
            }
        }

        return null;
    }

    /// <summary>The pieces between commas at the top level of the text from start up to end, each with where it is.</summary>
    private static List<(string Text, int Start, int End)> TopLevel(string text, int start, int end)
    {
        var pieces = new List<(string, int, int)>();
        var depth = 0;
        var from = start;
        for (var at = start; at < end; at++)
        {
            var c = text[at];
            var arrow = c == '>' && at > start && text[at - 1] is '=' or '-';
            if (c is '(' or '[' or '{' || c == '<' && at + 1 < end && char.IsLetter(text[at + 1])) depth++;
            else if ((c is ')' or ']' or '}' || c == '>' && !arrow) && depth > 0) depth--;
            else if (c == ',' && depth == 0)
            {
                pieces.Add((text[from..at], from, at));
                from = at + 1;
            }
        }

        if (end > start) pieces.Add((text[from..end], from, end));
        return pieces;
    }

    /// <summary>A call's arguments, each with where it is on the line - null when the call goes on past the line.</summary>
    private sealed record Call(List<(string Text, int Start, int End)>? Arguments);

    /// <summary>The calls of the function on the line - not its header - with their arguments. Only brace languages write a typed header, int reach(...).</summary>
    private static IEnumerable<Call> Calls(string text, string function, RegexOptions options, bool typed = true)
    {
        if (DefinedByWord().Match(text) is { Success: true } header && SameText(header.Groups["name"].Value, function, options)) yield break;
        if (typed && Regex.IsMatch(text, @"^\s*(?:[\w.:<>\[\],?*&]+\s+)+" + Regex.Escape(function) + @"\s*\([^;]*$") && EndsAHeader().IsMatch(text) && !text.Contains('=')) yield break;

        foreach (Match call in Regex.Matches(text, $@"(?<![\w$-]){Regex.Escape(function)}\s*\(", options))
        {
            var opening = call.Index + call.Length - 1;
            yield return Brackets.ClosingParenthesis(text, opening) is { } closing
                ? new Call(TopLevel(text, opening + 1, closing))
                : new Call(null);
        }
    }

    private static bool SameText(string first, string second, RegexOptions options) =>
        string.Equals(first, second, options.HasFlag(RegexOptions.IgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>A parameter's name: the $name, the last word before any default when the type comes first, or else the first word.</summary>
    private static string ParameterName(string parameter, Dialect dialect)
    {
        var declared = Regex.Replace(parameter, @"\s*=.*$", "");
        if (Regex.Match(declared, @"\$[A-Za-z_]\w*") is { Success: true } dollar) return dollar.Value;

        if (dialect.TypeFirst)
            return Regex.Match(Regex.Replace(declared, @"(?:\s*\[[^\]]*\])+\s*$", ""), @"[A-Za-z_]\w*(?=\s*$)").Value;

        return Regex.Match(declared, @"^\s*(?:(?:mut|ref|out|in|const|final|var|val)\s+|[*&]+\s*|\.\.\.)*(?<name>[A-Za-z_$][\w$]*)").Groups["name"].Value;
    }

    /// <summary>
    /// The line that adds the item, where it plainly goes. In a function calling itself on the item - for next in graph[node]:
    /// if next not in visited: search(next) - the function's own parameter, on its first line, so the first place is marked
    /// too; and else, where the item is the place the search is at: after a guard that leaves on its own line, first in the
    /// else of a guard's if, or after the if when it has none, or first in the block an if not in the set runs. Null for
    /// anything else - a loop asking each place next to the one it is at, which needs the first place marked as well.
    /// </summary>
    private static LocalFix? Change(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect, Form form, Search search, Match ask, string set, string item, string madeText, RegexOptions options)
    {
        if (form.Add is not { } add) return null;

        var line = search.Line;
        var yes = Regex.IsMatch(madeText, @"\b(?:bool|_Bool|boolean)\b|\bfalse\b") ? "true" : "1";
        string Adding(string what) => add.Replace("{s}", set, StringComparison.Ordinal).Replace("{owned}", Regex.Replace(what, @"^&\s*", ""), StringComparison.Ordinal)
            .Replace("{item}", what, StringComparison.Ordinal).Replace("{yes}", yes, StringComparison.Ordinal);
        var adding = Adding(item);
        var text = masked[line];
        var indent = CodeText.Indentation(source.Lines[line]);
        var step = BraceBlocks.IndentStep(source.Lines);
        var negated = Negated(text, ask);
        var shown = dialect.Sigil + set;
        var element = Element(dialect, set, item);

        LocalFix Insert(int before, string lineIndent) => form.Kind == Kind.Set
            ? LocalFix.Insert(id, $"Add {item} to {shown} where the search goes on from it: {adding}",
                $"{item} is then in {shown} the next time the search reaches it, so the search goes on from it only once.",
                source.Path, before + 1, [lineIndent + adding])
            : LocalFix.Insert(id, $"Set {element} where the search goes on from it: {adding}",
                $"{element} is then set the next time the search reaches it, so the search goes on from it only once.",
                source.Path, before + 1, [lineIndent + adding]);

        var place = Regex.Replace(item, @"^[&*\s]+", "");
        if (search.Function is { } function)
        {
            // Called on the item: mark the place the function is at, as it starts.
            if (PassedAt(masked, function, place, options, dialect) is { } at && function.Parameters is { } parameters && at < parameters.Count &&
                ParameterName(parameters[at], dialect) is { Length: > 0 } parameter && !SameText(parameter, place, options))
            {
                var marking = Adding(parameter);
                var first = FirstInside(masked, function, dialect.Blocks);
                if (first is null) return null;
                var firstIndent = masked[first.Value].Trim() is "" or "}" or "end"
                    ? CodeText.Indentation(source.Lines[function.Header]) + step
                    : CodeText.Indentation(source.Lines[first.Value]);
                return form.Kind == Kind.Set
                    ? LocalFix.Insert(id, $"Add {parameter} to {shown} when the search comes to it: {marking}",
                        $"Each place is then in {shown} from the moment the search comes to it - the first one too - so the search goes on from it only once.",
                        source.Path, first.Value + 1, [firstIndent + marking])
                    : LocalFix.Insert(id, $"Set {Element(dialect, set, parameter)} when the search comes to it: {marking}",
                        $"Each place is then set in {shown} from the moment the search comes to it - the first one too - so the search goes on from it only once.",
                        source.Path, first.Value + 1, [firstIndent + marking]);
            }

            if (!(function.Parameters ?? []).Any(each => SameText(ParameterName(each, dialect), place, options))) return null;
        }
        else if (EachPlaceNextTo(masked, line, search.Header, dialect.Blocks, item))
        {
            return null;
        }

        // A guard leaving on its own line: if node in visited: return False, or return false if visited.include?(node).
        var opensABlock = Regex.IsMatch(text, @"[{:]\s*$|\b(?:then|do)\s*$") ||
                          dialect.Blocks == Blocks.Ends && Regex.IsMatch(text, @"^\s*(?:if|unless|while|until|elsif|elseif)\b") && !Regex.IsMatch(text, @"\bend\s*$");
        if (!negated && LeavesTheWay().IsMatch(text) && !opensABlock)
        {
            var next = BraceBlocks.NextCode(masked, line + 1);
            if (next < 0 || Regex.IsMatch(masked[next], @"^\s*(?:\}?\s*else\b|elif\b|elsif\b|elseif\b)")) return null;
            return Insert(line + 1, indent);
        }

        if (!opensABlock) return null;

        // A line put first in a block takes the indentation of the line it goes before, or one step in from the if's.
        string FirstIndent(int first) => first < masked.Count && masked[first].Trim() is { Length: > 0 } code && !code.StartsWith('}')
            ? CodeText.Indentation(source.Lines[first])
            : indent + step;
        var loopsOnIt = LoopsUntilDone().IsMatch(text);
        if (loopsOnIt ? !negated : !Regex.IsMatch(text, @"^\s*(?:\}\s*)?(?:else\s*)?(?:if|elif|elsif|elseif|unless)\b")) return null;

        if (dialect.Blocks == Blocks.Braces)
        {
            if (BlockAfter(masked, line) is not { } end) return null;
            if (negated) return Insert(line + 1, FirstIndent(line + 1));
            if (!Enumerable.Range(line + 1, Math.Max(0, end - line - 1)).Any(inside => LeavesTheWay().IsMatch(masked[inside]))) return null;

            for (var closing = end; ;)
            {
                var following = BraceBlocks.NextCode(masked, closing + 1);
                var elseLine = Regex.IsMatch(masked[closing], @"^\s*\}\s*else\b") ? closing
                    : following > 0 && Regex.IsMatch(masked[following], @"^\s*else\b") ? following : -1;
                if (elseLine < 0) return Insert(closing + 1, indent);
                if (Regex.IsMatch(masked[elseLine], @"\belse\s*\{\s*$")) return Insert(elseLine + 1, FirstIndent(elseLine + 1));
                if (!Regex.IsMatch(masked[elseLine], @"\belse\s+if\b.*\{\s*$") || BlockAfter(masked, elseLine) is not { } elseEnd) return null;
                closing = elseEnd;
            }
        }

        var body = BodyBelow(masked, line);
        if (body.Count == 0) return null;
        if (negated) return Insert(body[0], CodeText.Indentation(source.Lines[body[0]]));
        if (!body.Any(inside => LeavesTheWay().IsMatch(masked[inside]))) return null;

        for (var branch = BraceBlocks.NextCode(masked, body[^1] + 1); ;)
        {
            if (branch < 0) return null;
            if (CodeText.Indentation(masked[branch]).Length != indent.Length) return dialect.Blocks == Blocks.Indented ? Insert(branch, indent) : null;
            if (Regex.IsMatch(masked[branch], @"^\s*end\b")) return Insert(branch + 1, indent);
            if (Regex.IsMatch(masked[branch], @"^\s*else\s*:?\s*$"))
                return BodyBelow(masked, branch) is { Count: > 0 } elseBody ? Insert(elseBody[0], CodeText.Indentation(source.Lines[elseBody[0]])) : null;
            if (!Regex.IsMatch(masked[branch], @"^\s*(?:elif|elsif|elseif)\b")) return dialect.Blocks == Blocks.Indented ? Insert(branch, indent) : null;

            var further = BodyBelow(masked, branch);
            if (further.Count == 0) return null;
            branch = BraceBlocks.NextCode(masked, further[^1] + 1);
        }
    }

    /// <summary>Where a call of the function inside it passes the item on whole, or null.</summary>
    private static int? PassedAt(IReadOnlyList<string> masked, Function function, string item, RegexOptions options, Dialect dialect)
    {
        for (var inside = function.Header + 1; inside <= function.End && inside < masked.Count; inside++)
        {
            foreach (var call in Calls(masked[inside], function.Name, options, dialect.Blocks == Blocks.Braces))
            {
                var at = call.Arguments?.FindIndex(argument => Bare(argument.Text, item, options)) ?? -1;
                if (at >= 0) return at;
            }
        }

        return null;
    }

    /// <summary>The first line of the function's body - after the line its { is on - or null when its body is on its header's line.</summary>
    private static int? FirstInside(IReadOnlyList<string> masked, Function function, Blocks blocks)
    {
        if (blocks != Blocks.Braces) return BodyBelow(masked, function.Header) is { Count: > 0 } body ? body[0] : null;

        var opener = masked[function.Header].Contains('{') ? function.Header : BraceBlocks.NextCode(masked, function.Header + 1);
        if (opener < 0 || function.End <= opener) return null;
        return opener + 1;
    }

    /// <summary>Whether, between the line and the loop, a loop over each place next to the one the search is at gives the item.</summary>
    private static bool EachPlaceNextTo(IReadOnlyList<string> masked, int line, int loop, Blocks blocks, string item)
    {
        if (Regex.Match(item, @"^[&*\s]*(?<name>[$@]?[A-Za-z_$][\w$]*)") is not { Success: true } named) return false;
        var word = new Regex($@"(?<![\w$]){Regex.Escape(named.Groups["name"].Value)}(?![\w$])");

        return loop != line && Enclosing(masked, line, blocks).TakeWhile(header => header != loop)
            .Any(header => Regex.IsMatch(masked[header], @"\b(?:for|foreach|each|forEach|ipairs|pairs)\b") && word.IsMatch(masked[header]));
    }

    /// <summary>Whether the line asks whether the item is not there: not in, !contains, == false, unless, find == end.</summary>
    private static bool Negated(string text, Match ask)
    {
        var not = ask.Groups["not"].Success && ask.Groups["not"].Length > 0;
        if (Regex.IsMatch(text[..ask.Index], @"(?:\bnot\s+|!\s*|-not\s+)$", RegexOptions.IgnoreCase)) not = !not;
        if (Regex.IsMatch(text[(ask.Index + ask.Length)..], @"^\s*(?:===?|-eq)\s*(?:0|false|False|nil|null|None|undefined|\$false)\b", RegexOptions.IgnoreCase)) not = !not;
        if (Regex.IsMatch(text, @"\b(?:unless|until)\b")) not = !not;
        return not;
    }

    /// <summary>The lines below a header that are indented further than it, up to the first that is not.</summary>
    private static List<int> BodyBelow(IReadOnlyList<string> masked, int header)
    {
        var indent = CodeText.Indentation(masked[header]).Length;
        var lines = new List<int>();
        for (var line = header + 1; line < masked.Count; line++)
        {
            if (masked[line].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[line]).Length <= indent) break;
            lines.Add(line);
        }

        return lines;
    }
}
