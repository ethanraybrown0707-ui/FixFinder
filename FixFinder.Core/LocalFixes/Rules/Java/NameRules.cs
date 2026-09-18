using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>cannot find symbol (symbol: variable avarage)</c>, and the name it was one letter from.</summary>
/// <remarks>
/// javac, unlike Python, gcc and clang, never suggests a name. The candidates are what the file
/// itself declares and uses, or - when the missing name is a member of a JDK class, like
/// <c>System.out.printn</c> - that class's real methods, read from the JDK with javap.
/// </remarks>
public sealed partial class JavaNearestName : ILocalFixRule
{
    public string Id => "java-nearest-name";

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+(?<kind>variable|method|class)\s+(?<name>[A-Za-z_$][\w$]*)(?:\([^)]*\))?(?:,\s*location:\s*(?<location>.+))?\)$")]
    private static partial Regex Symbol();

    [GeneratedRegex(@"^(?:variable [\w$]+ of type|class|interface) (?<type>[\w.$]+)")]
    private static partial Regex LocationType();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error)) return null;
        if (Symbol().Match(error.Message ?? "") is not { Success: true } symbol) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var kind = symbol.Groups["kind"].Value;
        var name = symbol.Groups["name"].Value;

        // A class the import table knows is a missing import, not a misspelling.
        if (kind == "class" && JavaTypes.Packages.ContainsKey(name)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        IEnumerable<string> candidates;
        string where;

        var location = LocationType().Match(symbol.Groups["location"].Value);
        var type = location.Success ? location.Groups["type"].Value : null;

        if (type is not null && !DeclaresType(masked, type) && JavaTypes.Fqn(type, source.Lines) is { } fqn)
        {
            candidates = JavaTypes.Members(fqn, methods: kind == "method");
            where = $"on {type}";
        }
        else
        {
            candidates = CodeText.Identifiers(masked).Where(word => !JavaTypes.Keywords.Contains(word));
            if (kind == "class") candidates = candidates.Concat(JavaTypes.Lang).Concat(JavaTypes.Packages.Keys);
            where = "in this file";
        }

        if (CodeText.Nearest(name, candidates.Where(c => c != name), CommonMembers) is not { } right) return null;

        var caret = CodeText.Caret(context.Output, error);
        int? column = caret is { } found && found.Echo == line ? found.Column : null;

        if (CodeText.ReplaceWord(line, name, right, Syntax.CLike, column) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id,
            $"Change {name} to {right}",
            $"javac could not find the {kind} `{name}`, and does not suggest names. `{right}` is the only name {where} " +
            "within a letter or two of it.",
            source.Path, number, corrected);
    }

    private static readonly string[] CommonMembers = ["println", "printf", "print", "length", "equals", "nextLine", "nextInt", "size", "get", "add"];

    private static bool DeclaresType(IReadOnlyList<string> masked, string type) =>
        masked.Any(text => Regex.IsMatch(text, $@"\b(?:class|interface|enum|record)\s+{Regex.Escape(type)}\b"));
}

/// <summary><c>cannot find symbol (symbol: class List)</c> for a class that only needed importing.</summary>
public sealed partial class JavaMissingImport : ILocalFixRule
{
    public string Id => "java-missing-import";

    // A class used for a static call - Arrays.sort(values) - is reported as a variable, not a class.
    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+(?:class|variable) (?<name>[A-Z][\w$]*)")]
    private static partial Regex MissingClass();

    [GeneratedRegex(@"^\s*import\s+[\w.$]+(?:\.\*)?\s*;")]
    private static partial Regex ImportLine();

    [GeneratedRegex(@"^\s*package\s+[\w.]+\s*;")]
    private static partial Regex PackageLine();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error)) return null;
        if (MissingClass().Match(error.Message ?? "") is not { Success: true } primary) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = primary.Groups["name"].Value;
        if (!JavaTypes.Packages.ContainsKey(name) || JavaTypes.IsImported(name, source.Lines)) return null;

        // Every missing class in this file the table knows, in one change: List and ArrayList
        // nearly always go missing together, and fixing them one at a time is two rounds for one mistake.
        var imports = context.AllErrors
            .Where(e => JavaCode.IsCompileError(e) && SameFile(context, e, source))
            .Select(e => MissingClass().Match(e.Message ?? ""))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .Where(n => JavaTypes.Packages.ContainsKey(n) && !JavaTypes.IsImported(n, source.Lines))
            .Distinct(StringComparer.Ordinal)
            .Select(n => $"{JavaTypes.Packages[n]}.{n}")
            .Order(StringComparer.Ordinal)
            .ToList();

        var lines = source.Lines;
        var lastImport = LastMatch(lines, ImportLine());
        var package = LastMatch(lines, PackageLine());

        List<string> added = [.. imports.Select(i => $"import {i};")];
        int before;

        if (lastImport >= 0)
        {
            before = lastImport + 2;
        }
        else if (package >= 0)
        {
            before = package + 2;
            added.Insert(0, "");
        }
        else
        {
            before = 1;
            if (lines.Count > 0 && lines[0].Trim().Length > 0) added.Add("");
        }

        return LocalFix.Insert(
            Id,
            imports.Count == 1 ? $"Add import {imports[0]}" : $"Add {imports.Count} missing imports",
            imports.Count == 1
                ? $"`{name}` lives in {JavaTypes.Packages[name]}, and Java only sees classes outside java.lang once they are imported."
                : $"{string.Join(", ", imports.Select(i => i[(i.LastIndexOf('.') + 1)..]))} all live outside java.lang, " +
                  "and Java only sees those once they are imported.",
            source.Path, before, added);
    }

    private static int LastMatch(IReadOnlyList<string> lines, Regex pattern)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
            if (pattern.IsMatch(lines[i])) return i;

        return -1;
    }

    private static bool SameFile(LocalFixContext context, ParsedError error, SourceFile source) =>
        context.Resolve((error.CulpritFrame ?? error.Frames.FirstOrDefault())?.File) is { } path &&
        string.Equals(path, source.Path, StringComparison.OrdinalIgnoreCase);
}

/// <summary><c>package system does not exist</c> - a java.lang class written in lower case.</summary>
public sealed partial class JavaLowercaseClass : ILocalFixRule
{
    public string Id => "java-lowercase-class";

    [GeneratedRegex(@"^package (?<name>[a-z]\w*) does not exist$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var right = char.ToUpperInvariant(name[0]) + name[1..];

        if (!JavaTypes.Lang.Contains(right)) return null;

        var hits = Regex.Matches(CodeText.Mask(line, Syntax.CLike), $@"(?<![\w$.]){Regex.Escape(name)}(?=\s*\.)");
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Write {name} as {right}",
            $"Java is case-sensitive, and the class is `{right}` with a capital letter. Written in lower case, javac took `{name}` for a package.",
            source.Path, number, CCode.ReplaceEach(line, hits, _ => right));
    }
}

/// <summary><c>non-static method total() cannot be referenced from a static context</c></summary>
/// <remarks>
/// Making the member static is right exactly when it uses nothing belonging to an object - and that
/// is what the compile check establishes, because a static method that touches instance state does
/// not compile.
/// </remarks>
public sealed partial class JavaNonStaticMember : ILocalFixRule
{
    public string Id => "java-non-static-member";

    [GeneratedRegex(@"^non-static (?<kind>method|variable) (?<name>[A-Za-z_$][\w$]*)(?:\(.*\))? cannot be referenced from a static context$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\b(?:static|abstract)\b")]
    private static partial Regex AlreadyDecided();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var kind = message.Groups["kind"].Value;
        var name = message.Groups["name"].Value;
        var word = Regex.Escape(name);

        var pattern = kind == "method"
            ? new Regex($@"^(?<lead>\s*(?:@\w+(?:\([^)]*\))?\s+)*(?:(?:public|protected|private|final|synchronized|native|strictfp)\s+)*)(?<type>(?:<[^>]+>\s*)?[\w$.]+(?:<[^()]*>)?(?:\[\])*)\s+{word}\s*\(")
            : new Regex($@"^(?<lead>\s*(?:(?:public|protected|private|final|transient|volatile)\s+)*)(?<type>[\w$.]+(?:<[^()=;]*>)?(?:\[\])*)\s+{word}\s*(?:=|;)");

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var declarations = masked
            .Select((text, index) => (Text: text, Index: index, Match: pattern.Match(text)))
            .Where(d => d.Match.Success)
            .Where(d => d.Match.Groups["type"].Value is not ("return" or "new" or "throw" or "else"))
            .Where(d => kind != "method" || !d.Text.TrimEnd().EndsWith(';'))
            .ToList();

        if (declarations.Count != 1 || AlreadyDecided().IsMatch(declarations[0].Text)) return null;

        var (_, index, match) = declarations[0];
        var line = source.Lines[index];
        var lead = match.Groups["lead"].Length;

        return LocalFix.ReplaceLine(
            Id,
            $"Make {name} static",
            $"main is static, so there is no object for it to use `{name}` on. `{name}` uses nothing that belongs to an " +
            "object - it still compiles once static - so that is the direct fix. If it should keep per-object state, " +
            "create an instance with new and use it on that instead.",
            source.Path, index + 1, line[..lead] + "static " + line[lead..]);
    }
}

/// <summary>A constructor called without <c>new</c>: <c>ArrayList&lt;String&gt; names = ArrayList&lt;&gt;();</c></summary>
public sealed partial class JavaMissingNew : ILocalFixRule
{
    public string Id => "java-missing-new";

    [GeneratedRegex(@"(?:=|\breturn\b|\(|,)\s*(?<type>[A-Z][\w$]*\s*<[^<>()]*>)\s*\(")]
    private static partial Regex Diamond();

    [GeneratedRegex(@"^cannot find symbol \(symbol:\s+method (?<name>[A-Z][\w$]*)\(")]
    private static partial Regex MissingMethod();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var message = context.Error.Message ?? "";
        int column;
        string type;

        if (message == "illegal start of expression" && Diamond().Matches(masked) is { Count: 1 } diamonds)
        {
            column = diamonds[0].Groups["type"].Index;
            type = JavaCode.WithoutTypeArguments(diamonds[0].Groups["type"].Value).Trim();
        }
        else if (MissingMethod().Match(message) is { Success: true } missing)
        {
            type = missing.Groups["name"].Value;

            var isClass = JavaTypes.Packages.ContainsKey(type) || JavaTypes.Lang.Contains(type) ||
                          CodeText.MaskAll(source.Lines, Syntax.CLike).Any(text => Regex.IsMatch(text, $@"\b(?:class|record|enum)\s+{Regex.Escape(type)}\b"));
            if (!isClass) return null;

            var calls = Regex.Matches(masked, $@"(?:=|\breturn\b|\(|,)\s*(?<type>{Regex.Escape(type)})\s*\(");
            if (calls.Count != 1) return null;

            column = calls[0].Groups["type"].Index;
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(
            Id, $"Create the {type} with new",
            $"`{type}` is a class. Calling it like a method does not make one - `new` does.",
            source.Path, number, line[..column] + "new " + line[column..]);
    }
}

/// <summary><c>length</c>, <c>length()</c> and <c>size()</c> mixed up, and a method named without its brackets.</summary>
/// <remarks>
/// An array has the field <c>length</c>, a String the method <c>length()</c>, and a collection the
/// method <c>size()</c>. javac says which type the variable is, so which one was meant is not a guess.
/// </remarks>
public sealed partial class JavaLengthAndSize : ILocalFixRule
{
    public string Id => "java-length-size";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at) return null;
        if (JavaCode.CannotFindSymbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;
        if (JavaCode.VariableLocation().Match(symbol.Groups["location"].Value) is not { Success: true } location) return null;

        var (source, number, line) = at;
        var kind = symbol.Groups["kind"].Value;
        var name = symbol.Groups["name"].Value;
        var receiver = location.Groups["receiver"].Value;
        var type = location.Groups["type"].Value;

        string? right = null;
        var call = false;
        var explanation = "";

        // An argument inside the brackets means some other method was meant.
        if (kind == "method" && symbol.Groups["arguments"].Value != "()") return null;

        if (type.EndsWith("[]", StringComparison.Ordinal) && name is "length" or "size" or "count" or "Length" or "Count")
        {
            right = "length";
            explanation = "An array's length is a field in Java - `length`, with no brackets. `length()` belongs to String, and `size()` to collections.";
        }
        else if (type == "String" && name is "length" or "size" or "Length" or "count")
        {
            (right, call) = ("length", true);
            explanation = "A String's length is a method in Java - `length()`, with brackets. (An array's is the field `length`.)";
        }
        else if (JavaCode.IsCollection(type) && name is "length" or "Length" or "count" or "Count")
        {
            (right, call) = ("size", true);
            explanation = $"A {JavaCode.WithoutTypeArguments(type)} counts its elements with `size()`. `length` is for arrays and `length()` for Strings.";
        }
        else if (kind == "variable" && JavaTypes.Fqn(JavaCode.WithoutTypeArguments(type), source.Lines) is { } fqn && JavaTypes.Members(fqn, methods: true).Contains(name))
        {
            (right, call) = (name, true);
            explanation = $"`{name}` is a method of {JavaCode.WithoutTypeArguments(type)}, and calling a method takes brackets: `{name}()`.";
        }

        if (right is null) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var access = Regex.Matches(masked, $@"(?<![\w$.]){Regex.Escape(receiver)}\s*\.\s*(?<name>{Regex.Escape(name)})(?<brackets>\s*\(\s*\))?(?![\w$])");
        if (access.Count != 1) return null;

        var member = access[0].Groups["name"];
        var brackets = access[0].Groups["brackets"];
        var end = brackets.Success ? brackets.Index + brackets.Length : member.Index + member.Length;
        var corrected = line[..member.Index] + right + (call ? "()" : "") + line[end..];

        if (corrected == line) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {receiver}.{right}{(call ? "()" : "")}", explanation, source.Path, number, corrected);
    }
}

/// <summary><c>array required, but List&lt;String&gt; found</c> - square brackets on a List or a String.</summary>
public sealed partial class JavaIndexing : ILocalFixRule
{
    public string Id => "java-indexing";

    [GeneratedRegex(@"^array required, but (?<type>.+) found$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w$.])(?<receiver>[A-Za-z_$][\w$]*)\s*\[(?<index>[^\[\]]+)\]")]
    private static partial Regex Indexed();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var type = message.Groups["type"].Value;

        string method, explanation;

        if (type == "String")
        {
            method = "charAt";
            explanation = "A Java String cannot be indexed with square brackets. `charAt` gives the character at a position.";
        }
        else if (Regex.IsMatch(type, @"^(?:List|ArrayList|LinkedList|Vector)<"))
        {
            method = "get";
            explanation = "Square brackets only index arrays in Java. A List gives the element at a position with `get`.";
        }
        else
        {
            return null;
        }

        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = Indexed().Matches(masked).ToList();
        var caret = JavaCode.CaretColumn(context, line);
        var hit = hits.Count == 1 ? hits[0] : hits.FirstOrDefault(h => caret is { } c && c >= h.Index && c < h.Index + h.Length);
        if (hit is null) return null;

        // Assigning through the brackets is set, not get - and a String cannot be changed at all.
        var after = masked[(hit.Index + hit.Length)..].TrimStart();
        if (after.StartsWith('=') && !after.StartsWith("==", StringComparison.Ordinal)) return null;

        var receiver = hit.Groups["receiver"].Value;
        var index = line.Substring(hit.Groups["index"].Index, hit.Groups["index"].Length).Trim();

        return LocalFix.ReplaceLine(
            Id, $"Use {receiver}.{method}({index})", explanation,
            source.Path, number, line[..hit.Index] + $"{receiver}.{method}({index})" + line[(hit.Index + hit.Length)..]);
    }
}

/// <summary><c>int cannot be dereferenced</c> - <c>x.equals(5)</c> on a primitive.</summary>
public sealed partial class JavaPrimitiveMethod : ILocalFixRule
{
    public string Id => "java-primitive-method";

    [GeneratedRegex(@"^(?<type>int|long|double|float|char|boolean|short|byte) cannot be dereferenced$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<not>!\s*)?(?<![\w$.])(?<receiver>[A-Za-z_$][\w$]*)\s*\.\s*(?<method>equals|toString)\s*\((?<argument>[^()]*)\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = Call().Matches(masked).ToList();
        var caret = JavaCode.CaretColumn(context, line);
        var hit = hits.Count == 1 ? hits[0] : hits.FirstOrDefault(h => caret is { } c && c >= h.Index && c < h.Index + h.Length);
        if (hit is null) return null;

        var receiver = hit.Groups["receiver"].Value;
        var argument = line.Substring(hit.Groups["argument"].Index, hit.Groups["argument"].Length).Trim();
        var negated = hit.Groups["not"].Success;
        string replacement, explanation;

        if (hit.Groups["method"].Value == "equals" && argument.Length > 0)
        {
            // Only where == reads the same as the call did: between brackets, an assignment, or && and ||.
            var before = masked[..hit.Index].TrimEnd();
            var after = masked[(hit.Index + hit.Length)..].TrimStart();
            if (before.Length > 0 && !"(=&|,?:".Contains(before[^1]) && !before.EndsWith("return", StringComparison.Ordinal)) return null;
            if (after.Length > 0 && !");&|,?:".Contains(after[0])) return null;

            replacement = $"{receiver} {(negated ? "!=" : "==")} {argument}";
            explanation = $"`{receiver}` is an `{message.Groups["type"].Value}`, a primitive, and primitives have no methods. They compare with `==`; `equals` is for objects such as Strings.";
        }
        else if (hit.Groups["method"].Value == "toString" && argument.Length == 0 && !negated)
        {
            replacement = $"String.valueOf({receiver})";
            explanation = $"`{receiver}` is an `{message.Groups["type"].Value}`, a primitive, and primitives have no methods. `String.valueOf` gives its text.";
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(
            Id, $"Write it as {replacement}", explanation,
            source.Path, number, line[..hit.Index] + replacement + line[(hit.Index + hit.Length)..]);
    }
}
