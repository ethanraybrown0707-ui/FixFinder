using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// The change that stops a loop searching the same list from the start on every pass: make a set from it once, just
/// before the loop, and search that.
/// </summary>
/// <remarks>
/// The change is only offered where the code shows it gives the same answers, which takes all of these:
/// <list type="bullet">
/// <item>the list is this function's own - made right there from a literal or a copy, never a parameter, a global or a
/// field - and is never handed to other code or seen by a nested function, so nothing else can change it while the loop
/// runs;</item>
/// <item>inside the loop it is only read;</item>
/// <item>it is given its value on every way to the loop, so making the set there cannot fail where the loop did not;</item>
/// <item>its items compare the same way in a set as in the list: in Python they and what is looked for are plain values
/// a set can hold, in Java and C# they are of a library type whose equality and hash agree, and in JavaScript a Set's
/// has compares exactly as includes does;</item>
/// <item>and the new line can go just before the loop without landing inside anything else, such as the body of an if
/// written without braces.</item>
/// </list>
/// A string is never changed this way: searching one looks for a piece of text, which a set of its characters cannot do.
/// </remarks>
internal static partial class SetBeforeTheLoop
{
    [GeneratedRegex(@"^(async\s+for|for|foreach|while|do)\b")]
    private static partial Regex LoopKeyword();

    /// <summary>Types from each language's library whose equality and hash agree, so a set finds just what the list did.</summary>
    private static readonly Dictionary<SourceLanguage, HashSet<string>> AgreeingTypes = new()
    {
        [SourceLanguage.Java] = new(StringComparer.Ordinal) { "String", "Integer", "Long", "Short", "Byte", "Character", "Boolean" },
        [SourceLanguage.CSharp] = new(StringComparer.Ordinal) { "string", "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort", "char", "bool" },
    };

    /// <summary>Python's own functions that read what they are given and keep no hold of it.</summary>
    private static readonly HashSet<string> PythonReaders = new(StringComparer.Ordinal)
    {
        "len", "print", "sorted", "list", "tuple", "set", "frozenset", "str", "repr", "enumerate", "reversed", "sum", "min", "max", "any", "all",
    };

    /// <summary>Members that only read a list, an array or a string.</summary>
    private static readonly HashSet<string> ReadingMembers = new(StringComparer.Ordinal)
    {
        "index", "count", "find", "copy", "contains", "indexOf", "lastIndexOf", "size", "isEmpty", "get", "length",
        "Contains", "IndexOf", "LastIndexOf", "Find", "Count", "Length", "includes", "slice", "join", "at",
    };

    /// <summary>Python's calls and string methods whose result is always a plain value a set can hold.</summary>
    private static readonly HashSet<string> PlainValueCalls = new(StringComparer.Ordinal)
    {
        "str", "int", "float", "bool", "len", "ord", "chr", "input", "repr", "round", "abs", "hex", "oct", "bin",
    };

    private static readonly HashSet<string> TextMethods = new(StringComparer.Ordinal)
    {
        "lower", "upper", "strip", "lstrip", "rstrip", "title", "casefold", "capitalize", "swapcase", "replace", "format", "join",
        "zfill", "center", "ljust", "rjust",
    };

    public static LocalFix? For(IrProgram program, IrFunction function, Stmt loop, string collection, Evidence what, SourceText source)
    {
        var language = program.Language;
        if (what is not { Lookup: Lookup.FromTheStart, Fresh: true, Kind: "list" or "array" or "tuple" }) return null;
        if (language == SourceLanguage.JavaScript && what.Kind != "array") return null;
        if (function.IsGenerator || function.OuterNames.Contains(collection)) return null;

        var searches = PerformanceChecks.SearchesIn(PerformanceChecks.BodyOf(loop), language)
            .Where(s => s.Collection.Identifier == collection && Rewritable(s, language)).ToList();
        if (searches.Count == 0) return null;

        if (!ItemsCompareAlike(function, collection, what, searches, language)) return null;
        if (!OnlyThisFunctionHolds(program, function, collection)) return null;
        if (!OnlyReadInTheLoop(loop, collection, language) || !NeverHandedOn(function, loop, collection, language)) return null;
        if (!GivenAValueBefore(function, loop, collection)) return null;

        var file = loop.Span.File;
        var lines = source.Lines(file);
        if (loop.Span.Line < 1 || loop.Span.Line > lines.Count || !LoopStartsItsLine(lines, loop.Span.Line, language)) return null;

        var setName = language == SourceLanguage.Python ? $"{collection}_set" : $"{collection}Set";
        if (lines.Any(line => Regex.IsMatch(line, $@"\b{Regex.Escape(setName)}\b"))) return null;

        var edits = new List<(int Line, int Start, int End, string Text)>();
        foreach (var search in searches)
        {
            if (Edit(search, lines, setName, language) is not { } edit) return null;
            edits.Add(edit);
        }

        var first = loop.Span.Line;
        var last = edits.Max(e => e.Line);
        var changedLines = new List<string>();
        for (var number = first; number <= last; number++)
        {
            var text = lines[number - 1];
            foreach (var edit in edits.Where(e => e.Line == number).OrderByDescending(e => e.Start))
                text = text[..edit.Start] + edit.Text + text[edit.End..];
            changedLines.Add(text);
        }

        var loopLine = lines[first - 1];
        var indent = loopLine[..(loopLine.Length - loopLine.TrimStart().Length)];
        var (making, madeWith, compares) = Wording(language, collection, setName, what.Elements, lines);

        return new LocalFix
        {
            RuleId = PerformanceChecks.RepeatedSearchRule,
            Title = $"Make a {madeWith} from `{collection}` once, before the loop, and search that",
            Explanation =
                $"Every search of `{collection}` inside the loop compared what it looked for with the items one at a time, on every pass. " +
                $"A {madeWith} finds an item by its hash instead, so a search of `{setName}` takes about the same time however long " +
                $"`{collection}` grows, and making it goes through `{collection}` once. Nothing changes `{collection}` while the loop runs, " +
                $"so the {madeWith} holds the same items on every pass. {compares}",
            File = file,
            StartLine = first,
            RemoveCount = last - first + 1,
            NewLines = [indent + making, .. changedLines],
        };
    }

    /// <summary>A membership test a set can answer the same way: Python's in, Java's contains, C#'s Contains, JavaScript's includes.</summary>
    private static bool Rewritable(PerformanceChecks.Search search, SourceLanguage language) => (language, search.Method) switch
    {
        (SourceLanguage.Python, null) => true,
        (SourceLanguage.Java, "contains") => true,
        (SourceLanguage.CSharp, "Contains") => true,
        (SourceLanguage.JavaScript, "includes") => true,
        _ => false,
    };

    private static (string Making, string MadeWith, string Compares) Wording(
        SourceLanguage language, string collection, string setName, string? elements, IReadOnlyList<string> lines)
    {
        switch (language)
        {
            case SourceLanguage.Python:
                return ($"{setName} = set({collection})", "set",
                    "Its items and what is looked for are plain values - text, numbers - which a set compares just as the list did.");

            case SourceLanguage.Java:
                var imported = lines.Any(l => l.Trim() == "import java.util.*;") ||
                               lines.Any(l => l.Trim() == "import java.util.Set;") && lines.Any(l => l.Trim() == "import java.util.HashSet;");
                var (set, hashSet) = imported ? ("Set", "HashSet") : ("java.util.Set", "java.util.HashSet");
                return ($"{set}<{elements}> {setName} = new {hashSet}<>({collection});", "HashSet",
                    $"{elements}'s equals and hashCode agree, so the set finds exactly what the list found.");

            case SourceLanguage.CSharp:
                var generic = lines.Any(l => l.Trim() == "using System.Collections.Generic;") ? "HashSet" : "System.Collections.Generic.HashSet";
                return ($"var {setName} = new {generic}<{elements}>({collection});", "HashSet",
                    $"{elements}'s Equals and GetHashCode agree, so the set finds exactly what the list found.");

            default:
                return ($"const {setName} = new Set({collection});", "Set",
                    "A Set's has compares values exactly as includes does, so every answer is the same.");
        }
    }

    /// <summary>
    /// Whether a set would compare the items as the list does. Java and C# need a library type whose equality and hash
    /// agree; Python needs every item, and everything looked for, to be a plain value a set can hold - an unhashable one
    /// would make the set fail where the list did not.
    /// </summary>
    private static bool ItemsCompareAlike(
        IrFunction function, string collection, Evidence what, IReadOnlyList<PerformanceChecks.Search> searches, SourceLanguage language)
    {
        switch (language)
        {
            case SourceLanguage.Java or SourceLanguage.CSharp:
                return what.Elements is { } elements && AgreeingTypes[language].Contains(elements);

            case SourceLanguage.Python:
                var searchedFor = searches.Select(s => ((Binary)s.Whole).Left);
                return ItemsArePlain(function, collection) && searchedFor.All(value => IsPlainValue(function, value, []));

            default:
                return true;
        }
    }

    /// <summary>Whether everything a Python list is given - its literal items, and what is appended or inserted - is a plain value.</summary>
    private static bool ItemsArePlain(IrFunction function, string collection)
    {
        foreach (var definition in CollectionEvidence.Definitions(function, collection))
        {
            var plain = definition.Value switch
            {
                CollectionLiteral literal => literal.Items.All(item => IsPlainValue(function, item, [])),
                Call { Callee: Member { MemberName: "split" or "splitlines" } } => true,
                _ => false,
            };

            if (!plain) return false;
        }

        foreach (var call in IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(CollectionEvidence.Within).OfType<Call>())
        {
            if (call.Callee is not Member { Target: Name { Identifier: var target }, MemberName: var method } || target != collection) continue;

            var added = method switch
            {
                "append" => call.Arguments.Select(a => a.Value).ToList(),
                "insert" => call.Arguments.Skip(1).Select(a => a.Value).ToList(),
                _ when PerformanceChecks.Changing.Contains(method) && method is not ("remove" or "pop" or "clear" or "sort" or "reverse") => null,
                _ => [],
            };

            if (added is null || !added.All(value => IsPlainValue(function, value, []))) return false;
        }

        return true;
    }

    /// <summary>Whether a Python value is always text, a number, True, False or None - or a tuple of them.</summary>
    private static bool IsPlainValue(IrFunction function, Expr value, HashSet<string> following) => value switch
    {
        Literal => true,
        CollectionLiteral { Kind: CollectionKind.Tuple } tuple => tuple.Items.All(item => IsPlainValue(function, item, following)),
        Call { Callee: Name { Identifier: var called } } => PlainValueCalls.Contains(called),
        Call { Callee: Member { MemberName: var method } } => TextMethods.Contains(method),
        Name name => PlainName(function, name.Identifier, following),
        _ => false,
    };

    /// <summary>Whether every value a name is given is plain: a parameter typed as text or a number, or plain values assigned or looped over.</summary>
    private static bool PlainName(IrFunction function, string name, HashSet<string> following)
    {
        if (!following.Add(name)) return false;

        var definitions = CollectionEvidence.Definitions(function, name).ToList();
        if (definitions.Count == 0) return false;

        foreach (var definition in definitions)
        {
            var plain = definition switch
            {
                { IsParameter: true, Declared.Name: "str" or "int" or "float" or "bool" or "bytes" } => true,
                { IsParameter: true } => false,
                { Understood: true, Value: { } assigned } => IsPlainValue(function, assigned, following),
                { Understood: false } => LoopsOverPlainValues(function, name, definition.At, following),
                _ => false,
            };

            if (!plain) return false;
        }

        return true;
    }

    /// <summary>Whether the name is a for loop's variable, over something whose items are all plain values.</summary>
    private static bool LoopsOverPlainValues(IrFunction function, string name, SourceSpan at, HashSet<string> following)
    {
        var loop = IrWalk.Statements(function.Body).OfType<ForEach>().FirstOrDefault(l => l.Span == at && l.Target is Name { Identifier: var target } && target == name);
        if (loop is null) return false;

        return loop.Items switch
        {
            Call { Callee: Member { MemberName: "split" or "splitlines" } } => true,
            Call { Callee: Name { Identifier: "range" } } => true,
            Literal { Kind: LiteralKind.Text } => true,
            CollectionLiteral literal => literal.Items.All(item => IsPlainValue(function, item, following)),
            Name items => ItemsArePlain(function, items.Identifier) && CollectionEvidence.Definitions(function, items.Identifier).All(d => !d.IsParameter && d.Understood),
            _ => false,
        };
    }

    /// <summary>
    /// Whether no other function can see the list: none nested in this one, which could change it, and - for a list made
    /// at the top of a module, which every function there can reach - none anywhere that names it.
    /// </summary>
    private static bool OnlyThisFunctionHolds(IrProgram program, IrFunction function, string collection)
    {
        if (IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(CollectionEvidence.Within)
            .Any(e => e is Opaque { What: var what } && (what.Contains("lambda") || what.Contains("function"))))
        {
            return false;
        }

        var atTop = function.Name == IrFunction.ModuleBody;
        var byName = program.AllFunctions.GroupBy(f => f.FullName).ToDictionary(g => g.Key, g => g.First());

        foreach (var other in program.AllFunctions.Where(f => !ReferenceEquals(f, function)))
        {
            if (!atTop && !NestedIn(byName, other, function)) continue;

            var names = IrWalk.Statements(other.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Names);
            if (names.Contains(collection) || other.OuterNames.Contains(collection)) return false;
        }

        return true;
    }

    private static bool NestedIn(Dictionary<string, IrFunction> byName, IrFunction inner, IrFunction outer)
    {
        for (var enclosing = inner.EnclosedBy; enclosing is not null; enclosing = byName.GetValueOrDefault(enclosing)?.EnclosedBy)
        {
            if (enclosing == outer.FullName) return true;
        }

        return false;
    }

    /// <summary>Whether, inside the loop, the list is only searched, measured, read or looped over.</summary>
    private static bool OnlyReadInTheLoop(Stmt loop, string collection, SourceLanguage language) =>
        IrWalk.Statements([loop]).All(statement => StatementOnlyReads(statement, collection, language, returning: false));

    /// <summary>
    /// Whether the function never gives the list to other code, which could keep it and change it while the loop runs.
    /// Outside the loop it may be added to, sorted and so on, since the set is made just before the loop starts.
    /// </summary>
    private static bool NeverHandedOn(IrFunction function, Stmt loop, string collection, SourceLanguage language)
    {
        var insideLoop = IrWalk.Statements([loop]).ToHashSet(ReferenceEqualityComparer.Instance);

        return IrWalk.Statements(function.Body).Where(s => !insideLoop.Contains(s)).All(statement => statement switch
        {
            Assign { Target: var target } assign when PerformanceChecks.Root(target) == collection =>
                UsesOnlyRead(assign.Value, null, collection, language, changing: true),
            Evaluate { Value: Call { Callee: Member { Target: Name { Identifier: var target }, MemberName: var method } } call }
                when target == collection && PerformanceChecks.Changing.Contains(method) =>
                call.Arguments.All(a => UsesOnlyRead(a.Value, null, collection, language, changing: true)),
            _ => StatementOnlyReads(statement, collection, language, returning: true),
        });
    }

    private static bool StatementOnlyReads(Stmt statement, string collection, SourceLanguage language, bool returning) => statement switch
    {
        Assign { Target: var target } when PerformanceChecks.Root(target) == collection => false,
        Declare declare when declare.Variable == collection => declare.Initial is null || UsesOnlyRead(declare.Initial, null, collection, language, changing: false),
        ForEach loop when IrWalk.Names(loop.Target).Contains(collection) => false,
        ForEach loop => (loop.Items is Name { Identifier: var looped } && looped == collection) || UsesOnlyRead(loop.Items, null, collection, language, changing: false),
        Return { Value: Name { Identifier: var returned } } when returned == collection => returning,
        OpaqueStmt opaque => !opaque.MayAssign.Contains(collection) && !opaque.Parts.SelectMany(IrWalk.Names).Contains(collection),
        Using used => !IrWalk.Names(used.Resource).Contains(collection) && (used.Variable is null || !IrWalk.Names(used.Variable).Contains(collection)),
        _ => IrWalk.Expressions(statement).All(expression => UsesOnlyRead(expression, null, collection, language, changing: false)),
    };

    /// <summary>Whether every mention of the list inside an expression only reads it.</summary>
    private static bool UsesOnlyRead(Expr expression, Expr? parent, string collection, SourceLanguage language, bool changing)
    {
        if (expression is Name { Identifier: var identifier } && identifier == collection)
            return parent is not null && ReadHere(parent, expression, language);

        if (expression is AssignValue assigned && IrWalk.Names(assigned.Target).Contains(collection))
            return changing && UsesOnlyRead(assigned.Value, assigned, collection, language, changing);

        return IrWalk.Children(expression).All(child => UsesOnlyRead(child, expression, collection, language, changing));
    }

    /// <summary>Whether the place a mention of the list sits in only reads it, and keeps no hold of it.</summary>
    private static bool ReadHere(Expr parent, Expr mention, SourceLanguage language) => parent switch
    {
        Binary { Operator: BinaryOperator.In or BinaryOperator.NotIn } search => ReferenceEquals(search.Right, mention),
        Member { MemberName: var member } read => ReferenceEquals(read.Target, mention) && ReadingMembers.Contains(member),
        ElementAccess element => ReferenceEquals(element.Target, mention),
        Unary { Operator: UnaryOperator.Not } => true,
        Call { Callee: Name { Identifier: var called } } when language == SourceLanguage.Python => PythonReaders.Contains(called),
        Call { Callee: Member { Target: var printer, MemberName: "println" or "print" or "printf" or "WriteLine" or "Write" or "log" } } =>
            printer is Name { Identifier: "console" or "Console" } or Member { Target: Name { Identifier: "System" }, MemberName: "out" or "err" },
        _ => false,
    };

    /// <summary>
    /// Whether the list is given a value on every way to the loop: an assignment earlier in the same block as the loop, or
    /// in a block around it. Anything else could leave the new line reading a list the loop never needed.
    /// </summary>
    private static bool GivenAValueBefore(IrFunction function, Stmt loop, string collection)
    {
        if (PathTo(function.Body, loop) is not { } path) return false;

        return path.Any(step => step.Block.Take(step.Index).Any(statement => statement switch
        {
            Declare { Initial: not null } declare => declare.Variable == collection,
            Assign { Target: Name target, Compound: null } => target.Identifier == collection,
            Evaluate { Value: AssignValue { Target: Name target } } => target.Identifier == collection,
            _ => false,
        }));
    }

    /// <summary>The blocks from the function's body down to the statement, and where in each the way down goes on.</summary>
    private static List<(IReadOnlyList<Stmt> Block, int Index)>? PathTo(IReadOnlyList<Stmt> block, Stmt target)
    {
        for (var index = 0; index < block.Count; index++)
        {
            if (ReferenceEquals(block[index], target)) return [(block, index)];

            foreach (var inner in BlocksIn(block[index]))
            {
                if (PathTo(inner, target) is { } deeper) return [(block, index), .. deeper];
            }
        }

        return null;
    }

    private static IEnumerable<IReadOnlyList<Stmt>> BlocksIn(Stmt statement) => statement switch
    {
        If branch => [branch.Then, branch.Else],
        While loop => [loop.Body, loop.Else],
        For loop => [loop.Setup, loop.Body, loop.Step],
        ForEach loop => [loop.Body, loop.Else],
        Try attempt => [attempt.Body, .. attempt.Handlers.Select(h => h.Body), attempt.Else, attempt.Finally],
        Switch choice => choice.Cases.Select(c => c.Body),
        Using used => [used.Body],
        Labeled labeled => [labeled.Body],
        _ => [],
    };

    /// <summary>
    /// Whether a new line can go just before the loop and be part of the same block: the loop is the first thing on its
    /// line, and - where braces make blocks - the line before it ends a statement or opens or closes a block, so the new
    /// line cannot become the body of an if or a loop written without braces.
    /// </summary>
    private static bool LoopStartsItsLine(IReadOnlyList<string> lines, int loopLine, SourceLanguage language)
    {
        if (!LoopKeyword().IsMatch(lines[loopLine - 1].TrimStart())) return false;
        if (language == SourceLanguage.Python) return true;

        for (var number = loopLine - 1; number >= 1; number--)
        {
            var before = lines[number - 1].Trim();
            if (before.Length == 0 || before.StartsWith("//", StringComparison.Ordinal) || before.StartsWith('*') || before.StartsWith("/*", StringComparison.Ordinal))
                continue;

            return before.EndsWith('{') || before.EndsWith('}') || before.EndsWith(';');
        }

        return true;
    }

    /// <summary>
    /// The change to one search: its collection's name becomes the set's, and JavaScript's includes becomes has. The
    /// text at the place the reader gave must be the name itself, or no change is made at all.
    /// </summary>
    private static (int Line, int Start, int End, string Text)? Edit(PerformanceChecks.Search search, IReadOnlyList<string> lines, string setName, SourceLanguage language)
    {
        var span = search.Collection.Span;
        if (span.Line < 1 || span.Line > lines.Count || span.EndLine != span.Line) return null;

        var line = lines[span.Line - 1];
        if (span.Column < 0 || span.EndColumn > line.Length || span.EndColumn - span.Column != search.Collection.Identifier.Length) return null;
        if (line[span.Column..span.EndColumn] != search.Collection.Identifier) return null;

        if (language != SourceLanguage.JavaScript) return (span.Line, span.Column, span.EndColumn, setName);

        const string includes = ".includes";
        return string.CompareOrdinal(line, span.EndColumn, includes, 0, includes.Length) == 0
            ? (span.Line, span.Column, span.EndColumn + includes.Length, setName + ".has")
            : null;
    }
}
