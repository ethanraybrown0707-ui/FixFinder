using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

// The Java mistakes of a computer science degree past the first weeks: collections and generics, records and interfaces,
// comparison and sorting, threads and monitors, serialisation, formatting. Each rule proposes one change.

internal static partial class JavaCourse
{
    /// <summary>The file and line of a runtime exception's first frame in the program's own code.</summary>
    public static (SourceFile Source, int Number, string Line)? AtRuntime(LocalFixContext context, string type)
    {
        if (context.Error is not { LanguageId: "java" } error || error.ExceptionType != type) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!source.Path.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>A type written so it compiles here: its simple name when imported, its full name otherwise.</summary>
    public static string Qualified(SourceFile source, string package, string name) =>
        source.Lines.Any(l => Regex.IsMatch(l, $@"^\s*import\s+{Regex.Escape(package)}\.(?:{Regex.Escape(name)}|\*)\s*;"))
            ? name
            : $"{package}.{name}";

    /// <summary>Every Java file in the program's folder, the one the error named first.</summary>
    public static IEnumerable<SourceFile> Files(LocalFixContext context)
    {
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) yield break;

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true };

        foreach (var path in Directory.EnumerateFiles(root, "*.java", options).Take(300))
            if (SourceFile.Read(path) is { } source) yield return source;
    }
}

/// <summary><c>Main method not found in class App</c> - <c>main(String args)</c>, or a <c>main</c> that is not static.</summary>
public sealed partial class JavaMainSignature : ILocalFixRule
{
    public string Id => "java-main-signature";

    [GeneratedRegex(@"Error: Main method (?<problem>not found|is not static) in class (?<class>[\w$.]+)")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<head>\s*public\s+(?<static>static\s+)?void\s+main\s*\(\s*)(?<parameter>(?:final\s+)?String\s*(?:\[\s*\]|\.\.\.)?\s*[A-Za-z_]\w*(?:\s*\[\s*\])?)(?<tail>\s*\).*)$")]
    private static partial Regex Main();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Message().Match(context.Error.RawText) is not { Success: true } message) return null;

        var name = message.Groups["class"].Value.Split('.')[^1];
        if (context.Read(name + ".java") is not { } source) return null;

        var mains = Enumerable.Range(0, source.Count).Where(i => Main().IsMatch(source.Lines[i])).ToList();
        if (mains is not [var index]) return null;

        var match = Main().Match(source.Lines[index]);
        var parameter = match.Groups["parameter"].Value;
        var isArray = Regex.IsMatch(parameter, @"\[\s*\]|\.\.\.");
        var isStatic = match.Groups["static"].Success;

        if (isArray && isStatic) return null;

        var argument = Regex.Match(parameter, @"(?<name>[A-Za-z_]\w*)(?:\s*\[\s*\])?$").Groups["name"].Value;
        var head = match.Groups["head"].Value;
        if (!isStatic) head = head.Replace("public ", "public static ", StringComparison.Ordinal);

        return LocalFix.ReplaceLine(
            Id, "Declare main as Java looks for it: public static void main(String[] args)",
            "Java starts a program by looking for exactly `public static void main(String[] args)` - `static`, so it can be called before " +
            "any object exists, and taking an array of the command-line arguments. Anything else compiles, because it is a legal method, " +
            "and then is not found when the program is run.",
            source.Path, index + 1, head + $"String[] {argument}" + match.Groups["tail"].Value);
    }
}

/// <summary><c>IllegalFormatConversionException: d != java.lang.Double</c> - a format code for one kind of value given another.</summary>
public sealed partial class JavaFormatConversion : ILocalFixRule
{
    public string Id => "java-format-conversion";

    [GeneratedRegex(@"^(?<code>[a-zA-Z]) != java\.(?:lang|math)\.(?<type>\w+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCourse.AtRuntime(context, "java.util.IllegalFormatConversionException") is not { } at) return null;
        if (Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var code = message.Groups["code"].Value;
        var wanted = message.Groups["type"].Value switch
        {
            "Double" or "Float" or "BigDecimal" => "f",
            "Integer" or "Long" or "Short" or "Byte" or "BigInteger" => "d",
            "String" => "s",
            "Character" => "c",
            "Boolean" => "b",
            _ => null,
        };

        if (wanted is null || wanted == code) return null;

        var (source, number, line) = at;
        if (!Regex.IsMatch(line, @"\b(?:format|printf|formatted)\s*\(")) return null;

        // The one specifier with that code on the line; two of them and it cannot be told which argument was wrong.
        var specifiers = Regex.Matches(line, $@"%(?:\d+\$)?[-#+ 0,(]*\d*(?:\.\d+)?{Regex.Escape(code)}").ToList();
        if (specifiers is not [var specifier]) return null;

        // A precision means nothing to %d, so it is kept only where the new code uses one.
        var text = specifier.Value[..^1];
        if (wanted is "d" or "c" or "b") text = Regex.Replace(text, @"\.\d+$", "");

        var corrected = text + wanted;

        return LocalFix.ReplaceLine(
            Id, $"Format a {message.Groups["type"].Value} with %{wanted}",
            $"`%{code}` formats {Describe(code)}, and the value given for it is a `{message.Groups["type"].Value}`. Java checks this when the " +
            $"line runs, not when it compiles. `%{wanted}` is the code for {Describe(wanted)}.",
            source.Path, number, line[..specifier.Index] + corrected + line[(specifier.Index + specifier.Length)..]);
    }

    private static string Describe(string code) => code switch
    {
        "d" => "a whole number", "f" => "a decimal number", "s" => "text", "c" => "a character", "b" => "a true or false value",
        _ => $"`%{code}` values",
    };
}

/// <summary><c>UnsupportedOperationException</c> from <c>add</c> on a list made by <c>Arrays.asList</c> or <c>List.of</c>.</summary>
public sealed partial class JavaFixedSizeCollection : ILocalFixRule
{
    public string Id => "java-fixed-size-collection";

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_$][\w$]*)\s*\.\s*(?:add|addAll|remove|removeIf|removeAll|clear|put|putAll)\s*\(")]
    private static partial Regex Change();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCourse.AtRuntime(context, "java.lang.UnsupportedOperationException") is not { } at) return null;
        if (!Regex.IsMatch(context.Error.RawText, @"java\.util\.(?:AbstractList|ImmutableCollections|AbstractCollection|AbstractMap)")) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Change().Matches(masked[number - 1]).ToList() is not [var change]) return null;

        var name = change.Groups["name"].Value;
        var declaration = new Regex($@"(?<![\w.]){Regex.Escape(name)}\s*=\s*(?<init>(?<factory>Arrays\.asList|List\.of|Set\.of|Map\.of|Collections\.unmodifiable\w+)\s*\()");
        var found = Enumerable.Range(0, number - 1).Where(i => declaration.IsMatch(masked[i])).ToList();
        if (found is not [var index]) return null;

        var match = declaration.Match(masked[index]);
        var factory = match.Groups["factory"].Value;
        var init = match.Groups["init"];
        var close = CCode.Matching(masked[index], init.Index + init.Length - 1);
        if (close is null) return null;

        var copy = factory switch
        {
            "Set.of" => "HashSet",
            "Map.of" => "HashMap",
            _ when factory.Contains("Set", StringComparison.Ordinal) => "HashSet",
            _ when factory.Contains("Map", StringComparison.Ordinal) => "HashMap",
            _ => "ArrayList",
        };

        var original = source.Lines[index];
        var type = JavaCourse.Qualified(source, "java.util", copy);
        var expression = original[init.Index..(close.Value + 1)];

        return LocalFix.ReplaceLine(
            Id, $"Make a copy that can change: new {copy}<>({factory}(...))",
            $"`{factory}` gives back a {(factory == "Arrays.asList" ? "fixed-size list - a view of the array, so nothing can be added or removed" : "collection that cannot be changed at all")}, " +
            $"and `{name}` is then changed. `new {copy}<>(...)` copies the items into an ordinary {copy} that can grow and shrink.",
            source.Path, index + 1, original[..init.Index] + $"new {type}<>({expression})" + original[(close.Value + 1)..]);
    }
}

/// <summary><c>Student is not abstract and does not override abstract method compareTo(Object) in Comparable</c> - a raw <c>Comparable</c>.</summary>
/// <remarks>
/// The class already has <c>compareTo(Student)</c>; it is the missing <c>&lt;Student&gt;</c> that makes Java look for
/// <c>compareTo(Object)</c> instead. Tried before the rule that writes missing methods, which would add the wrong one.
/// </remarks>
public sealed partial class JavaRawComparable : ILocalFixRule
{
    public string Id => "java-raw-comparable";

    [GeneratedRegex(@"^(?<class>[\w$]+) is not abstract and does not override abstract method (?<method>compareTo|compare)\((?:Object|T)(?:,(?:Object|T))?\) in (?<interface>Comparable|Comparator)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Java.IsCompileError(context.Error) || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var interfaceName = message.Groups["interface"].Value;
        var method = message.Groups["method"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var raw = Regex.Matches(masked[number - 1], $@"(?<![\w.]){interfaceName}(?!\s*[<\w])").ToList();
        if (raw is not [var mention]) return null;

        // The type the class's own compareTo or compare already takes - a method one level inside this class's body.
        var depths = CCode.DepthAtStart(masked);
        var outer = depths[number - 1];
        var end = number;
        while (end < masked.Count && depths[end + 1] > outer) end++;

        var parameter = method == "compareTo"
            ? $@"\bcompareTo\s*\(\s*(?:final\s+)?(?<type>[A-Z][\w$.<>]*)\s+\w+\s*\)"
            : $@"\bcompare\s*\(\s*(?:final\s+)?(?<type>[A-Z][\w$.<>]*)\s+\w+\s*,\s*(?:final\s+)?\k<type>\s+\w+\s*\)";

        var types = Enumerable.Range(number, Math.Max(0, end - number))
            .Where(i => depths[i] == outer + 1)
            .Select(i => Regex.Match(masked[i], parameter))
            .Where(m => m.Success && m.Groups["type"].Value != "Object")
            .Select(m => m.Groups["type"].Value)
            .Distinct()
            .ToList();

        if (types is not [var type]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Say what it compares: {interfaceName}<{type}>",
            $"`{interfaceName}` without a type means `{interfaceName}<Object>`, so Java looks for `{method}` taking `Object` and does not count " +
            $"the `{method}` taking `{type}` that is already there. `{interfaceName}<{type}>` says what is being compared, and that method is " +
            "then the one it needs.",
            source.Path, number, line[..mention.Index] + $"{interfaceName}<{type}>" + line[(mention.Index + mention.Length)..]);
    }
}

/// <summary><c>interface abstract methods cannot have body</c> - a method written out in an interface without <c>default</c>.</summary>
public sealed partial class JavaInterfaceDefault : ILocalFixRule
{
    public string Id => "java-interface-default";

    [GeneratedRegex(@"^(?<lead>\s*(?:public\s+)?)(?<rest>(?!default\b|static\b|private\b|abstract\b)[\w$<>\[\],.?\s]+?\s+[\w$]+\s*\(.*)$")]
    private static partial Regex Method();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Java.IsCompileError(context.Error) || context.Error.Message != "interface abstract methods cannot have body") return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Method().Match(line) is not { Success: true } method) return null;

        return LocalFix.ReplaceLine(
            Id, "Give the interface method a body with default",
            "A method in an interface is abstract unless it says otherwise - a promise each class keeps by writing it. `default` marks one " +
            "that the interface writes itself, which every class gets unless it writes its own.",
            source.Path, number, method.Groups["lead"].Value + "default " + method.Groups["rest"].Value);
    }
}

/// <summary><c>x has private access in Point</c> for a record - whose components are read with <c>p.x()</c>.</summary>
public sealed partial class JavaRecordAccessor : ILocalFixRule
{
    public string Id => "java-record-accessor";

    [GeneratedRegex(@"^(?<field>[\w$]+) has private access in (?<type>[\w$.]+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Java.IsCompileError(context.Error) || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var field = message.Groups["field"].Value;
        var type = message.Groups["type"].Value.Split('.')[^1];

        var record = JavaCourse.Files(context).Prepend(source)
            .Select(f => f.Lines.Select(l => Regex.Match(CodeText.Mask(l, Syntax.CLike), $@"\brecord\s+{Regex.Escape(type)}\s*(?:<[^>]*>)?\s*\((?<components>[^)]*)\)")).FirstOrDefault(m => m.Success))
            .FirstOrDefault(m => m is not null);

        if (record is null || !Regex.IsMatch(record.Groups["components"].Value, $@"\b{Regex.Escape(field)}\s*(?:,|$)")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var receivers = new Regex($@"(?:\b{Regex.Escape(type)}(?:<[^>]*>)?\s+|\bvar\s+)(?<name>[A-Za-z_$][\w$]*)\s*(?:=\s*new\s+{Regex.Escape(type)}\b)?");
        var names = masked.SelectMany(l => receivers.Matches(l)).Where(m => m.Value.StartsWith(type, StringComparison.Ordinal) || m.Value.Contains("new", StringComparison.Ordinal)).Select(m => m.Groups["name"].Value).ToHashSet();

        // Every component of this record the build reported on the same line, so p.x + p.y is corrected in one go.
        var fields = context.AllErrors
            .Where(e => Java.IsCompileError(e) && LocalFixContext.OwnFrame(e)?.Line == number)
            .Select(e => Message().Match(e.Message ?? ""))
            .Where(m => m.Success && m.Groups["type"].Value.Split('.')[^1] == type && Regex.IsMatch(record.Groups["components"].Value, $@"\b{Regex.Escape(m.Groups["field"].Value)}\s*(?:,|$)"))
            .Select(m => m.Groups["field"].Value)
            .Append(field)
            .Distinct()
            .ToList();

        var uses = Regex.Matches(masked[number - 1], $@"(?<![\w$.])(?<receiver>[A-Za-z_$][\w$]*)\.(?<field>{string.Join("|", fields.Select(Regex.Escape))})\b(?!\s*[(=])")
            .Where(m => names.Contains(m.Groups["receiver"].Value))
            .ToList();
        if (uses.Count == 0) return null;

        var list = string.Join(", ", fields.Select(f => f + "()"));

        return LocalFix.ReplaceLine(
            Id, $"Read the record's components with {list}",
            $"`{type}` is a record, and a record keeps each component in a private field, read through a method of the same name. " +
            $"`{list}` {(fields.Count == 1 ? "is that method" : "are those methods")}.",
            source.Path, number, CCode.Replace(line, uses, m => m.Value + "()"));
    }
}

/// <summary><c>counts.get(word)++</c> - <c>unexpected type</c>, because a value read from a map cannot be added to in place.</summary>
public sealed partial class JavaMapIncrement : ILocalFixRule
{
    public string Id => "java-map-increment";

    [GeneratedRegex(@"^(?<lead>\s*)(?:(?<pre>\+\+|--)\s*)?(?<map>[A-Za-z_$][\w$.]*)\.get\((?<key>[^()]*)\)\s*(?:(?<post>\+\+|--)|(?<op>[+\-*])=\s*(?<amount>[^;]+?))?\s*;(?<tail>.*)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Java.IsCompileError(context.Error) || context.Error.Message != "unexpected type") return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Statement().Match(line) is not { Success: true } statement) return null;

        var change = statement.Groups["pre"].Success ? statement.Groups["pre"].Value : statement.Groups["post"].Success ? statement.Groups["post"].Value : null;
        var (op, amount) = change is not null
            ? (change == "++" ? "+" : "-", "1")
            : statement.Groups["op"].Success ? (statement.Groups["op"].Value, statement.Groups["amount"].Value.Trim()) : (null, null);

        if (op is null) return null;

        var map = statement.Groups["map"].Value;
        var key = statement.Groups["key"].Value.Trim();
        if (key.Length == 0 || key.Contains(',')) return null;

        return LocalFix.ReplaceLine(
            Id, $"Store the new count back: {map}.put({key}, ...)",
            $"`{map}.get({key})` hands back a copy of the value, not a place to store one, so it cannot be changed with `{change ?? op + "="}`. " +
            $"The new value is worked out and stored back with `put`.",
            source.Path, number, $"{statement.Groups["lead"].Value}{map}.put({key}, {map}.get({key}) {op} {amount});{statement.Groups["tail"].Value}");
    }
}

/// <summary><c>cannot find symbol: class T</c> in a method that uses a type parameter it never declared.</summary>
public sealed partial class JavaGenericMethodParameter : ILocalFixRule
{
    public string Id => "java-generic-method-parameter";

    [GeneratedRegex(@"^(?<modifiers>\s*(?:(?:public|private|protected|static|final|synchronized|abstract)\s+)*)(?<rest>(?!return\b|new\b)[\w$<>\[\],.?\s]+?\s+[a-z_$][\w$]*\s*\(.*)$")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Java.IsCompileError(context.Error) || JavaCode.Symbol().Match(context.Error.Message ?? "") is not { Success: true } symbol) return null;
        if (symbol.Groups["kind"].Value != "class" || !Regex.IsMatch(symbol.Groups["name"].Value, @"^[A-Z]$")) return null;
        if (JavaCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = symbol.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (Header().Match(masked[number - 1]) is not { Success: true } header || !masked[number - 1].TrimEnd().EndsWith('{')) return null;
        if (Regex.IsMatch(header.Groups["rest"].Value, @"^\s*<")) return null;
        if (!Regex.IsMatch(masked[number - 1], $@"(?<![\w$]){name}(?![\w$])")) return null;

        // A class or interface that declares the parameter itself has a different problem.
        if (masked.Any(l => Regex.IsMatch(l, $@"\b(?:class|interface|record)\s+[\w$]+\s*<[^>]*\b{name}\b"))) return null;

        var at0 = header.Groups["rest"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Declare the type parameter: <{name}>",
            $"`{name}` is used as a type in this method, but nothing declares it - the class is not generic. A generic method declares its " +
            $"own type parameter, in angle brackets before the return type, and Java then works `{name}` out from each call.",
            source.Path, number, line[..at0] + $"<{name}> " + line[at0..]);
    }
}

/// <summary><c>IllegalMonitorStateException: current thread is not owner</c> from <c>wait()</c> or <c>notify()</c> outside <c>synchronized</c>.</summary>
public sealed partial class JavaWaitWithoutMonitor : ILocalFixRule
{
    public string Id => "java-wait-without-monitor";

    [GeneratedRegex(@"(?<![\w$.])(?:this\s*\.\s*)?(?<call>wait|notify|notifyAll)\s*\(")]
    private static partial Regex OwnMonitor();

    [GeneratedRegex(@"^(?<modifiers>\s*(?:(?:public|private|protected|static|final)\s+)*)(?<rest>(?!synchronized\b)[\w$<>\[\],.?\s]+?\s+[a-z_$][\w$]*\s*\(.*)$")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCourse.AtRuntime(context, "java.lang.IllegalMonitorStateException") is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (OwnMonitor().Matches(masked[number - 1]).ToList() is not [var call]) return null;

        var (first, _) = JavaCode.EnclosingMethod(masked, number - 1);
        var headerLine = masked[first].Trim() == "{" && first > 0 ? first - 1 : first;

        if (Header().Match(source.Lines[headerLine]) is not { Success: true } header || Regex.IsMatch(masked[headerLine], @"\bstatic\b")) return null;

        // Already inside a synchronized block of some other object: this one is not the monitor that block holds.
        if (Enumerable.Range(headerLine, number - 1 - headerLine).Any(i => Regex.IsMatch(masked[i], @"\bsynchronized\s*\("))) return null;

        var name = call.Groups["call"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Hold the object's lock around {name}(): synchronized",
            $"`{name}()` works on this object's monitor, and a thread may only use a monitor it holds - otherwise another thread could change " +
            $"the condition between checking it and waiting. A `synchronized` method holds this object's monitor for as long as it runs; " +
            "`wait` lets it go while waiting and takes it back before returning.",
            source.Path, headerLine + 1, header.Groups["modifiers"].Value + "synchronized " + header.Groups["rest"].Value);
    }
}

/// <summary><c>NotSerializableException: Student</c> - an object written to an <c>ObjectOutputStream</c> whose class never said it could be.</summary>
public sealed partial class JavaNotSerializable : ILocalFixRule
{
    public string Id => "java-not-serializable";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "java", ExceptionType: "java.io.NotSerializableException" } error) return null;

        var name = (error.Message ?? "").Trim().Split('.')[^1].Split('$')[^1];
        if (!Regex.IsMatch(name, @"^[A-Za-z_$][\w$]*$")) return null;

        var header = new Regex($@"^(?<head>\s*(?:(?:public|private|protected|static|final|abstract)\s+)*class\s+{Regex.Escape(name)}\b(?:\s*<[^>]*>)?(?:\s+extends\s+[\w$.<>]+)?)(?<implements>\s+implements\s+[^{{]+?)?(?<tail>\s*\{{.*)$");

        var found = JavaCourse.Files(context)
            .SelectMany(f => Enumerable.Range(0, f.Count).Where(i => header.IsMatch(CodeText.Mask(f.Lines[i], Syntax.CLike))).Select(i => (Source: f, Index: i)))
            .ToList();

        if (found is not [var at]) return null;

        var line = at.Source.Lines[at.Index];
        var match = header.Match(line);
        var serializable = JavaCourse.Qualified(at.Source, "java.io", "Serializable");

        var corrected = match.Groups["implements"].Success
            ? match.Groups["head"].Value + match.Groups["implements"].Value.TrimEnd() + $", {serializable}" + match.Groups["tail"].Value
            : match.Groups["head"].Value + $" implements {serializable}" + match.Groups["tail"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Let {name} be saved: implements Serializable",
            $"Java only writes an object to an `ObjectOutputStream` when its class says it may, by implementing `Serializable` - a marker " +
            $"with no methods to write. Every field of `{name}` has to be serializable too, or be marked `transient`.",
            at.Source.Path, at.Index + 1, corrected);
    }
}
