using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A count left out of step with what it counts, in the languages whose classes FixFinder does not follow through the code:
/// one method of a class puts an item in a list field and adds 1 to a count field, and another takes an item out and leaves
/// the count as it was - or the other way round - so the count no longer says how many items there are. TypeScript, Ruby,
/// PHP, Lua, Perl, Dart, PowerShell, Scala and Rust; Python, Java, C#, JavaScript, Go and C++ have it found by comparing
/// what each method does, analysis-count-out-of-step.
/// </summary>
/// <remarks>
/// A class's methods are read in its block - Lua's by the table their names start with, Perl's by their package - and each
/// method's lines for what they do to the object's own fields, in the language's words: <c>this.items.push(item)</c>,
/// <c>@items &lt;&lt; item</c>, <c>$this-&gt;items[] = $item</c>, <c>table.insert(self.items, item)</c>,
/// <c>push @{$self-&gt;{items}}, $item</c>, <c>self.size += 1</c>, <c>$this.size++</c>. The rule, the number's name, and
/// what is left alone are as the analysis has them: a method calling another of the class's methods, one giving either
/// field a value of another kind, and a constructor.
/// </remarks>
public static partial class CountOutOfStepPatterns
{
    /// <summary>
    /// How one language writes a class and reaches its fields: <c>Owner</c> starts a class (or, for Lua, is read from the
    /// method's name; for Perl, a package); <c>Method</c> starts a method, with its name; <c>Field</c> is the pattern for a
    /// field <c>{N}</c>; <c>OwnCall</c> a call of the object's own method <c>{M}</c>; and the adding, taking-out and counting
    /// patterns, with <c>{F}</c> for the field as the language writes it.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex? Owner, Regex Method, string Field, string OwnCall,
        string Adds, string TakesOut, string Constructor, bool IgnoreCase = false, bool OnlyTypeScript = false, bool BareFields = false);

    private const string CallAdds = @"{F}\s*\.\s*(?:push|append|add|insert|unshift|push_back|push_front|addLast|addFirst|Add|Push|Enqueue|Insert)\s*\(";
    private const string CallTakesOut = @"{F}\s*\.\s*(?:pop|shift|remove\w*|pop_back|pop_front|removeLast|removeFirst|removeAt|Remove\w*|Pop|Dequeue)\s*\(";

    private static readonly Dialect[] Dialects =
    [
        new("typescript", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, ClassBlock(), BraceMethod(), @"\bthis\.{N}(?![\w$])", @"\bthis\.{M}\s*\(",
            CallAdds, CallTakesOut, "constructor", OnlyTypeScript: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, RubyClass(), RubyMethod(), @"@{N}\b", @"(?:\bself\.|(?<![\w.@:]))(?:{M})\b(?!\s*=[^=])",
            @"{F}\s*(?:<<|\.\s*(?:push|append|unshift|prepend|insert)\b)", @"{F}\s*\.\s*(?:pop|shift|delete\w*|slice!)\b", "initialize"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, ClassBlock(), PhpMethod(), @"\$this->{N}\b", @"\$this->{M}\s*\(",
            @"{F}\s*\[\s*\]\s*=|array_(?:push|unshift)\s*\(\s*{F}", @"array_(?:pop|shift|splice)\s*\(\s*{F}|unset\s*\(\s*{F}\s*\[", "__construct", IgnoreCase: true),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, null, LuaMethod(), @"\bself\.{N}\b", @"\bself:{M}\s*\(",
            @"table\.insert\s*\(\s*{F}\s*,|{F}\s*\[\s*#\s*{F}\s*\+\s*1\s*\]\s*=", @"table\.remove\s*\(\s*{F}\b", "new"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlPackage(), PerlMethod(), @"\$self->\{\s*{N}\s*\}", @"\$self->{M}\s*\(",
            @"(?:push|unshift)\s*\(?\s*@\{\s*{F}\s*\}", @"(?:pop|shift|splice)\s*\(?\s*@\{\s*{F}\s*\}", "new"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, ClassBlock(), DartMethod(), @"(?:\bthis\.|(?<![\w.]))\b{N}\b", @"(?:\bthis\.|(?<![\w.]))\b{M}\s*\(",
            @"{F}\s*\.\s*(?:add|insert|addAll)\s*\(", @"{F}\s*\.\s*(?:removeLast|removeAt|removeFirst|remove|removeWhere)\s*\(", "", BareFields: true),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, ClassBlock(), PowerShellMethod(), @"\$this\.{N}\b", @"\$this\.{M}\s*\(",
            CallAdds, CallTakesOut, "", IgnoreCase: true),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, ScalaClass(), ScalaMethod(), @"(?:\bthis\.|(?<![\w.]))\b{N}\b", @"(?:\bthis\.|(?<![\w.]))\b{M}\s*\(",
            @"{F}\s*(?:\+=|\.\s*(?:append|addOne|prepend|insert|push|enqueue)\s*\()", @"{F}\s*(?:-=|\.\s*(?:remove|pop|dequeue|trimEnd|dropRightInPlace)\s*\()", "",
            BareFields: true),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, RustImpl(), RustMethod(), @"\bself\.{N}\b", @"\bself\.{M}\s*\(",
            @"{F}\s*\.\s*(?:push|insert|push_back|push_front)\s*\(", @"{F}\s*\.\s*(?:pop|remove|pop_back|pop_front|swap_remove|truncate)\s*\(", "new"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-count-out-of-step", dialect.Files, dialect.Syntax,
            (id, source, masked) => OutOfStep(id, source, masked, dialect))),
    ];

    private enum Kind { Added, TakenOut, CountedUp, CountedDown, GivenAnotherValue }

    private sealed record Method(string Owner, string Name, int Header, int End);

    private static IEnumerable<LogicFinding> OutOfStep(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (dialect.OnlyTypeScript && !TypeScriptCheck.IsTypeScript(source.Path)) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var methods = Methods(masked, dialect);

        foreach (var owner in methods.GroupBy(method => method.Owner))
        {
            var names = owner.Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
            var fields = FieldsOf(owner, masked, dialect);
            var changes = owner
                .Where(method => !string.Equals(method.Name, dialect.Constructor, StringComparison.OrdinalIgnoreCase) && method.Name != owner.Key)
                .Select(method => (Method: method, Changes: ChangesOf(method, masked, fields, dialect, options), Delegates: CallsItsOwn(method, masked, names, dialect, options)))
                .ToList();

            // The rule the class keeps: a list and a count one method changes together, the same way.
            var keptTogether = new List<(string Items, string Count, Method KeptBy)>();
            foreach (var (method, found, _) in changes)
                foreach (var items in found.Where(change => change.Kind is Kind.Added or Kind.TakenOut).Select(change => change.Field).Distinct())
                    foreach (var count in found.Where(change => change.Kind is Kind.CountedUp or Kind.CountedDown && Counts().IsMatch(change.Field)).Select(change => change.Field).Distinct())
                        if (((Has(found, items, Kind.Added) && Has(found, count, Kind.CountedUp)) || (Has(found, items, Kind.TakenOut) && Has(found, count, Kind.CountedDown))) &&
                            !keptTogether.Any(kept => kept.Items == items && kept.Count == count))
                            keptTogether.Add((items, count, method));

            foreach (var (items, count, keptBy) in keptTogether)
                foreach (var (method, found, delegates) in changes)
                {
                    if (method == keptBy || delegates || Has(found, items, Kind.GivenAnotherValue) || Has(found, count, Kind.GivenAnotherValue)) continue;

                    var added = Has(found, items, Kind.Added);
                    var takenOut = Has(found, items, Kind.TakenOut);
                    var countedUp = Has(found, count, Kind.CountedUp);
                    var countedDown = Has(found, count, Kind.CountedDown);
                    (int Line, string What)? oneSided =
                        added && !countedUp ? (First(found, items, Kind.Added), $"puts an item in `{items}` but leaves `{count}` as it was") :
                        takenOut && !countedDown ? (First(found, items, Kind.TakenOut), $"takes an item out of `{items}` but leaves `{count}` as it was") :
                        countedUp && !added ? (First(found, count, Kind.CountedUp), $"adds 1 to `{count}` but puts nothing in `{items}`") :
                        countedDown && !takenOut ? (First(found, count, Kind.CountedDown), $"takes 1 off `{count}` but takes nothing out of `{items}`") :
                        null;
                    if (oneSided is not var (line, what)) continue;

                    yield return new LogicFinding(id, line + 1,
                        $"`{method.Name}` {what}, while `{keptBy.Name}` changes both - after it, `{count}` no longer says how many `{items}` holds", null);
                }
        }
    }

    private static bool Has(List<(string Field, Kind Kind, int Line)> changes, string field, Kind kind) =>
        changes.Any(change => change.Field == field && change.Kind == kind);

    private static int First(List<(string Field, Kind Kind, int Line)> changes, string field, Kind kind) =>
        changes.Where(change => change.Field == field && change.Kind == kind).Min(change => change.Line);

    /// <summary>The methods of the file, each with the class it belongs to and its lines.</summary>
    private static List<Method> Methods(IReadOnlyList<string> masked, Dialect dialect)
    {
        var methods = new List<Method>();
        var owner = "";
        var ownerIndent = -1;

        for (var line = 0; line < masked.Count; line++)
        {
            if (IsBlank(masked[line])) continue;
            var indent = CodeText.Indentation(masked[line]).Length;

            if (dialect.Owner?.Match(masked[line]) is { Success: true } started)
            {
                owner = started.Groups["owner"].Value;
                ownerIndent = dialect.Language == "perl" ? -1 : indent;
                continue;
            }
            // A class ends at the first line no deeper than its header that is not its own brace or end.
            var trimmed = masked[line].TrimStart();
            if (dialect.Language != "perl" && dialect.Owner is not null && indent <= ownerIndent && owner.Length > 0 && !trimmed.StartsWith('}') &&
                !trimmed.StartsWith('{') && !trimmed.StartsWith("end", StringComparison.Ordinal))
            {
                owner = "";
                ownerIndent = -1;
            }

            if (dialect.Method.Match(masked[line]) is not { Success: true } header) continue;
            var methodOwner = header.Groups["owner"].Success ? header.Groups["owner"].Value : owner;
            if (methodOwner.Length == 0) continue;

            // The body runs from the header - or from a brace on its own line below it, as PHP's are written - to the first
            // line no deeper than the header.
            var end = line + 1;
            while (end < masked.Count && IsBlank(masked[end])) end++;
            if (end < masked.Count && masked[end].Trim() == "{" && CodeText.Indentation(masked[end]).Length == indent) end++;
            while (end < masked.Count && (IsBlank(masked[end]) || CodeText.Indentation(masked[end]).Length > indent)) end++;
            methods.Add(new Method(methodOwner, header.Groups["name"].Value, line, end));
        }

        return methods;
    }

    /// <summary>
    /// The class's fields: every name the language's field pattern finds in its methods - and, where a field may be named
    /// bare, the names declared in the class's own body, between its methods.
    /// </summary>
    private static HashSet<string> FieldsOf(IEnumerable<Method> methods, IReadOnlyList<string> masked, Dialect dialect)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var list = methods.OrderBy(method => method.Header).ToList();
        if (dialect.BareFields)
        {
            if (list.Count == 0) return fields;
            var bodyIndent = CodeText.Indentation(masked[list[0].Header]).Length;
            for (var line = 0; line < masked.Count; line++)
                if (CodeText.Indentation(masked[line]).Length == bodyIndent && !list.Any(method => line >= method.Header && line < method.End) &&
                    DeclaredField().Match(masked[line]) is { Success: true } declared)
                    fields.Add(declared.Groups["name"].Value);
            return fields;
        }

        var anyField = new Regex(dialect.Field.Replace("{N}", @"(?<name>[A-Za-z_]\w*)"), dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        foreach (var method in list)
            for (var line = method.Header; line < method.End; line++)
                foreach (Match field in anyField.Matches(masked[line]))
                    fields.Add(field.Groups["name"].Value);
        return fields;
    }

    /// <summary>Every change a method's lines make to a field: an item added or taken out, 1 added or taken off, or another value.</summary>
    private static List<(string Field, Kind Kind, int Line)> ChangesOf(Method method, IReadOnlyList<string> masked, HashSet<string> fields, Dialect dialect, RegexOptions options)
    {
        var changes = new List<(string, Kind, int)>();
        foreach (var field in fields)
        {
            var written = dialect.Field.Replace("{N}", Regex.Escape(field));
            var mention = new Regex(written, options);
            var adds = new Regex(dialect.Adds.Replace("{F}", written), options);
            var takesOut = new Regex(dialect.TakesOut.Replace("{F}", written), options);
            var up = new Regex($@"(?:{written})\s*(?:\+=\s*1\b|\+\+)|\+\+\s*(?:{written})|(?:{written})\s*=\s*(?:{written})\s*\+\s*1\b", options);
            var down = new Regex($@"(?:{written})\s*(?:-=\s*1\b|--)|--\s*(?:{written})|(?:{written})\s*=\s*(?:{written})\s*-\s*1\b", options);
            var given = new Regex($@"(?:{written})\s*(?<![=!<>])=(?!=)|(?:{written})\s*:=", options);

            for (var line = method.Header + 1; line < method.End; line++)
            {
                var text = masked[line];
                if (!mention.IsMatch(text)) continue;
                // 1 added or taken off first, as Scala's size += 1 reads as its list's += item does.
                if (up.IsMatch(text)) changes.Add((field, Kind.CountedUp, line));
                else if (down.IsMatch(text)) changes.Add((field, Kind.CountedDown, line));
                else if (adds.IsMatch(text)) changes.Add((field, Kind.Added, line));
                else if (takesOut.IsMatch(text)) changes.Add((field, Kind.TakenOut, line));
                else if (given.IsMatch(text)) changes.Add((field, Kind.GivenAnotherValue, line));
            }
        }
        return changes;
    }

    /// <summary>Whether the method calls another of the class's methods, which may keep the rule for it.</summary>
    private static bool CallsItsOwn(Method method, IReadOnlyList<string> masked, HashSet<string> names, Dialect dialect, RegexOptions options)
    {
        var others = names.Where(name => name != method.Name).Select(Regex.Escape).ToList();
        if (others.Count == 0) return false;
        var call = new Regex(dialect.OwnCall.Replace("{M}", $"(?:{string.Join("|", others)})"), options);
        return Enumerable.Range(method.Header + 1, Math.Max(0, method.End - method.Header - 1)).Any(line => call.IsMatch(masked[line]));
    }

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    /// <summary>A name that says it counts: size, count, length, len, num, top, n.</summary>
    [GeneratedRegex(@"(?i)size|count|length|len|num|top|^n$")]
    private static partial Regex Counts();

    /// <summary>class Stack {, export class Stack extends Base {, PowerShell's class Stack {, and PHP's class Stack with its brace below.</summary>
    [GeneratedRegex(@"^\s*(?:export\s+)?(?:default\s+)?(?:abstract\s+|final\s+)?class\s+(?<owner>(?>\w+))\b[^{]*\{?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ClassBlock();

    /// <summary>A TypeScript method: name(...) {, with its modifiers.</summary>
    [GeneratedRegex(@"^(?>\s*)(?:(?:public|private|protected|static|async|override|readonly)\s+)*(?!(?:if|for|while|switch|catch|function|return)\b)(?<name>(?>[\w$]+))\s*\([^()]*\)\s*\{\s*$")]
    private static partial Regex BraceMethod();

    [GeneratedRegex(@"^\s*class\s+(?<owner>(?>\w+))")]
    private static partial Regex RubyClass();

    [GeneratedRegex(@"^\s*def\s+(?<name>(?>[\w?!]+))")]
    private static partial Regex RubyMethod();

    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final)\s+)*function\s+(?<name>(?>\w+))\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex PhpMethod();

    /// <summary>function Stack:push(item) or function Stack.push(self, item) - the class is the table the name starts with.</summary>
    [GeneratedRegex(@"^\s*function\s+(?<owner>(?>\w+))[:.](?<name>(?>\w+))\s*\(")]
    private static partial Regex LuaMethod();

    [GeneratedRegex(@"^\s*package\s+(?<owner>[\w:]+)\s*;")]
    private static partial Regex PerlPackage();

    [GeneratedRegex(@"^\s*sub\s+(?<name>(?>\w+))\s*\{")]
    private static partial Regex PerlMethod();

    /// <summary>A Dart method: a return type and its name, then its parameters and a brace.</summary>
    [GeneratedRegex(@"^(?>\s*)(?!(?:if|for|while|switch|catch|return|else)\b)(?:(?>[\w<>?\[\]]+(?:,\s*[\w<>?\[\]]+)*)\s+)?(?<name>(?>\w+))\s*\([^()]*\)\s*\{\s*$")]
    private static partial Regex DartMethod();

    /// <summary>A PowerShell class method: [void] Push($item) {.</summary>
    [GeneratedRegex(@"^(?>\s*)(?:hidden\s+|static\s+)*(?:\[[^\]]+\]\s*)?(?!(?:if|elseif|while|for|foreach|switch|catch|until|trap)\b)(?<name>(?>\w+))\s*\([^()]*\)\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellMethod();

    [GeneratedRegex(@"^\s*(?:case\s+|abstract\s+|final\s+)*class\s+(?<owner>(?>\w+))")]
    private static partial Regex ScalaClass();

    [GeneratedRegex(@"^\s*(?:(?:override|private|protected|final)\s+)*def\s+(?<name>(?>\w+))")]
    private static partial Regex ScalaMethod();

    /// <summary>impl Stack {, impl&lt;T&gt; Stack&lt;T&gt; {, impl Display for Stack {.</summary>
    [GeneratedRegex(@"^\s*impl\b(?:<[^>]*>)?\s*(?:[\w:<>]+\s+for\s+)?(?<owner>(?>\w+))")]
    private static partial Regex RustImpl();

    [GeneratedRegex(@"^\s*(?:pub(?:\([^)]*\))?\s+)?fn\s+(?<name>(?>\w+))")]
    private static partial Regex RustMethod();

    /// <summary>A field declared in a Dart or Scala class's body: final List&lt;int&gt; items = [];, int size = 0;, var size = 0, val items = ListBuffer[Int]().</summary>
    [GeneratedRegex(@"^(?>\s*)(?:(?:private|protected|final|late|var|val)\s+)*(?:(?>[\w<>?\[\]]+(?:,\s*[\w<>?\[\]]+)*)\s+)?(?<name>(?>\w+))\s*(?::\s*[^=]+)?=")]
    private static partial Regex DeclaredField();
}
