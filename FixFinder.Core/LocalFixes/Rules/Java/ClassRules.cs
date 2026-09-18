using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>area() in Circle cannot implement area() in Shape</c> - <c>attempting to assign weaker access privileges; was
/// public</c>.</summary>
public sealed partial class JavaWeakerAccess : ILocalFixRule
{
    public string Id => "java-weaker-access";

    [GeneratedRegex(@"^(?<method>[\w$]+)\([^)]*\) in [\w$.]+ cannot (?:implement|override) [\w$]+\([^)]*\) in (?<super>[\w$.]+)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"weaker access privileges; was (?<access>public|protected)")]
    private static partial Regex Was();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.Locate(context) is not { } at || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Output.Select(line => Was().Match(line.Text)).FirstOrDefault(m => m.Success) is not { } was) return null;

        var (source, number, line) = at;
        var method = message.Groups["method"].Value;
        var declaration = Regex.Match(line, $@"^(?<lead>\s*)(?<access>(?:private|protected|public)\s+)?(?:(?:static|final|abstract|synchronized)\s+)*[\w<>\[\],.? ]+\s+{Regex.Escape(method)}\s*\(");
        if (!declaration.Success) return null;

        var access = was.Groups["access"].Value;
        var current = declaration.Groups["access"];
        var lead = declaration.Groups["lead"].Length;

        var corrected = current.Success
            ? line[..current.Index] + access + " " + line[(current.Index + current.Length)..]
            : line[..lead] + access + " " + line[lead..];

        return LocalFix.ReplaceLine(
            Id, $"Make {method} {access}",
            $"`{method}` comes from `{message.Groups["super"].Value}`, where it is {access}. An implementation can make a method more visible " +
            $"but never less - and with no modifier it is visible only inside its package - so it has to be {access} here as well.",
            source.Path, number, corrected);
    }
}

/// <summary><c>method does not override or implement a method from a supertype</c> - a misspelt <c>@Override</c>.</summary>
public sealed partial class JavaOverrideTypo : ILocalFixRule
{
    public string Id => "java-override-typo";

    [GeneratedRegex(@"^\s*(?:(?:public|protected|private|static|final|synchronized|abstract|default)\s+)*(?:<[^>]+>\s+)?[\w$<>\[\],.? ]+?\s+(?<name>[\w$]+)\s*\(")]
    private static partial Regex Method();

    [GeneratedRegex(@"\b(?:class|record|enum|interface)\s+(?<name>[\w$]+)(?:<[^>]*>)?(?<rest>[^{]*)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^\s*@\w+(?:\([^)]*\))?\s*$")]
    private static partial Regex Annotation();

    private static readonly string[] ObjectMethods = ["toString", "equals", "hashCode", "clone", "finalize"];

    private static readonly Dictionary<string, string[]> JdkMethods = new(StringComparer.Ordinal)
    {
        ["Runnable"] = ["run"], ["Thread"] = ["run"], ["Comparable"] = ["compareTo"], ["Comparator"] = ["compare"],
        ["Iterable"] = ["iterator"], ["Iterator"] = ["hasNext", "next", "remove"], ["AutoCloseable"] = ["close"], ["Closeable"] = ["close"],
        ["Callable"] = ["call"], ["Supplier"] = ["get"], ["Consumer"] = ["accept"], ["Function"] = ["apply"], ["Predicate"] = ["test"],
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "method does not override or implement a method from a supertype" || JavaCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var k = number - 1;
        while (k < masked.Count && (masked[k].Trim().Length == 0 || Annotation().IsMatch(masked[k]))) k++;
        if (k >= masked.Count || Method().Match(masked[k]) is not { Success: true } method) return null;

        var depths = Brackets.BraceDepths(masked);
        var owner = Enumerable.Range(0, k).Reverse().FirstOrDefault(i => depths[i] < depths[k] && TypeDeclaration().IsMatch(masked[i]), -1);
        if (owner < 0) return null;

        var candidates = new HashSet<string>(ObjectMethods, StringComparer.Ordinal);

        foreach (Match supertype in Regex.Matches(TypeDeclaration().Match(masked[owner]).Groups["rest"].Value, @"[A-Z][\w$]*"))
        {
            if (JdkMethods.TryGetValue(supertype.Value, out var known)) candidates.UnionWith(known);
            candidates.UnionWith(DeclaredMethods(masked, depths, supertype.Value));
        }

        var name = method.Groups["name"].Value;
        if (CodeText.Nearest(name, candidates.Where(c => c != name)) is not { } right) return null;

        var group = method.Groups["name"];
        var original = source.Lines[k];

        return LocalFix.ReplaceLine(
            Id, $"Rename {name} to {right}",
            $"`@Override` says `{name}` replaces a method it inherits, and nothing it inherits is called `{name}`. The inherited method within a " +
            $"letter or two of it is `{right}` - as written, `{name}` is a new method and `{right}` is never replaced.",
            source.Path, k + 1, original[..group.Index] + right + original[(group.Index + group.Length)..]);
    }

    private static IEnumerable<string> DeclaredMethods(IReadOnlyList<string> masked, int[] depths, string type)
    {
        var declaration = Enumerable.Range(0, masked.Count)
            .FirstOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:class|interface|record|enum)\s+{Regex.Escape(type)}\b"), -1);

        if (declaration < 0) yield break;

        var member = depths[declaration] + 1;

        for (var i = declaration + 1; i < masked.Count; i++)
        {
            if (i == declaration + 1 && masked[i].Trim() == "{") continue;
            if (depths[i] < member) break;

            if (depths[i] == member && !masked[i].Contains('=') && Method().Match(masked[i]) is { Success: true } m)
                yield return m.Groups["name"].Value;
        }
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
        if (!JavaCode.IsCompileError(context.Error) || context.Error.Message != "interface abstract methods cannot have body") return null;
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

/// <summary><c>Square is not abstract and does not override abstract method area() in Shape</c>: a method an interface or
/// abstract class requires, never written.</summary>
public sealed partial class JavaUnwrittenMethod : ILocalFixRule
{
    public string Id => "java-unwritten-method";

    [GeneratedRegex(@"^(?<cls>\w+) is not abstract and does not override abstract method (?:<[^>]+>)?(?<method>\w+)\((?<params>[^)]*)\) in (?<owner>\w+)")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:(?:public|protected|private|abstract|default|static|final|synchronized)\s+)+")]
    private static partial Regex Modifiers();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!JavaCode.IsCompileError(context.Error) || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } || context.Read(context.Frame.File) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var cls = message.Groups["cls"].Value;
        var owner = message.Groups["owner"].Value;
        var method = message.Groups["method"].Value;

        var header = Enumerable.Range(0, masked.Count).FirstOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:class|enum)\s+{cls}\b"), -1);
        if (header < 0 || ClassBody.Closing(masked, header) is not { } closing) return null;

        if (Declaration(context, source, owner, method, message.Groups["params"].Value) is not { } declared) return null;

        var modifiers = Modifiers().Match(declared.Text);
        var kept = modifiers.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(m => m is "public" or "protected" && !declared.Interface)
            .ToList();

        if (declared.Interface) kept.Insert(0, "public");

        var signature = declared.Text[modifiers.Length..].TrimEnd(';').TrimEnd();
        var indent = ClassBody.MemberIndent(lines, masked, header, closing);

        List<string> added =
        [
            indent + "@Override",
            $"{indent}{string.Join(' ', kept.Append(signature))} {{",
            $"{ClassBody.Deeper(indent)}throw new UnsupportedOperationException(\"{method} is not written yet\");",
            indent + "}",
        ];

        return LocalFix.Insert(
            Id, $"Add {method}() to {cls}, to be written",
            $"`{cls}` {(declared.Interface ? "implements" : "extends")} `{owner}`, which requires `{method}` - and `{cls}` never writes it. " +
            "This adds it with the signature declared there and a body that throws UnsupportedOperationException, so the " +
            "program compiles and calling it says plainly that it has not been written yet. Replace the body with the real code.",
            source.Path, closing + 1, added);
    }

    private static (string Text, bool Interface)? Declaration(LocalFixContext context, SourceFile here, string owner, string method, string parameters)
    {
        var folder = Path.GetDirectoryName(here.Path)!;

        foreach (var source in new[] { here, context.Read(Path.Combine(folder, owner + ".java")) })
        {
            if (source is null) continue;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var header = Enumerable.Range(0, masked.Count).FirstOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:class|interface)\s+{owner}\b"), -1);
            if (header < 0 || CppCode.ClassBraces(masked, header) is not { } body) continue;

            var isInterface = Regex.IsMatch(masked[header], $@"\binterface\s+{owner}\b");
            var depths = Brackets.BraceDepths(masked);
            var level = depths[body.Open] + 1;
            var count = parameters.Length == 0 ? 0 : parameters.Split(',').Length;

            var found = Enumerable.Range(body.Open + 1, Math.Max(0, body.Close - body.Open - 1))
                .Where(i => depths[i] == level)
                .Where(i => Regex.IsMatch(masked[i], $@"(?<![\w.$]){method}\s*\(") && masked[i].TrimEnd().EndsWith(';'))
                .Where(i => isInterface ? !Regex.IsMatch(masked[i], @"\b(?:default|static)\b") : Regex.IsMatch(masked[i], @"\babstract\b"))
                .Where(i => Arguments(masked[i], method) == count)
                .ToList();

            if (found is [var index]) return (source.Lines[index].Trim(), isInterface);
        }

        return null;
    }

    private static int Arguments(string masked, string method)
    {
        var open = Regex.Match(masked, $@"(?<![\w.$]){method}\s*\(");
        var close = masked.IndexOf(')', open.Index + open.Length);
        if (close < 0) return -1;

        var inside = masked[(open.Index + open.Length)..close].Trim();
        return inside.Length == 0 ? 0 : inside.Split(',').Length;
    }
}

/// <summary><c>NotSerializableException: Student</c> - an object written to an <c>ObjectOutputStream</c> whose class never said
/// it could be.</summary>
public sealed partial class JavaNotSerializable : ILocalFixRule
{
    public string Id => "java-not-serializable";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "java", ExceptionType: "java.io.NotSerializableException" } error) return null;

        var name = (error.Message ?? "").Trim().Split('.')[^1].Split('$')[^1];
        if (!Regex.IsMatch(name, @"^[A-Za-z_$][\w$]*$")) return null;

        var header = new Regex($@"^(?<head>\s*(?:(?:public|private|protected|static|final|abstract)\s+)*class\s+{Regex.Escape(name)}\b(?:\s*<[^>]*>)?(?:\s+extends\s+[\w$.<>]+)?)(?<implements>\s+implements\s+[^{{]+?)?(?<tail>\s*\{{.*)$");

        var found = JavaCode.SourceFiles(context)
            .SelectMany(f => Enumerable.Range(0, f.Count).Where(i => header.IsMatch(CodeText.Mask(f.Lines[i], Syntax.CLike))).Select(i => (Source: f, Index: i)))
            .ToList();

        if (found is not [var at]) return null;

        var line = at.Source.Lines[at.Index];
        var match = header.Match(line);
        var serializable = JavaCode.QualifiedName(at.Source, "java.io", "Serializable");

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

/// <summary><c>IllegalMonitorStateException: current thread is not owner</c> from <c>wait()</c> or <c>notify()</c> outside
/// <c>synchronized</c>.</summary>
public sealed partial class JavaWaitWithoutMonitor : ILocalFixRule
{
    public string Id => "java-wait-without-monitor";

    [GeneratedRegex(@"(?<![\w$.])(?:this\s*\.\s*)?(?<call>wait|notify|notifyAll)\s*\(")]
    private static partial Regex OwnMonitor();

    [GeneratedRegex(@"^(?<modifiers>\s*(?:(?:public|private|protected|static|final)\s+)*)(?<rest>(?!synchronized\b)[\w$<>\[\],.?\s]+?\s+[a-z_$][\w$]*\s*\(.*)$")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaCode.AtRuntime(context, "java.lang.IllegalMonitorStateException") is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (OwnMonitor().Matches(masked[number - 1]).ToList() is not [var call]) return null;

        var (first, _) = JavaCode.EnclosingMethod(masked, number - 1);
        var headerLine = masked[first].Trim() == "{" && first > 0 ? first - 1 : first;

        if (Header().Match(source.Lines[headerLine]) is not { Success: true } header || Regex.IsMatch(masked[headerLine], @"\bstatic\b")) return null;

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

/// <summary><c>unreported exception InterruptedException; must be caught or declared to be thrown</c></summary>
public sealed partial class JavaUnreportedException : ILocalFixRule
{
    public string Id => "java-unreported-exception";

    [GeneratedRegex(@"^unreported exception (?<type>[\w.$]+); must be caught or declared to be thrown$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:\}\s*)?(?:if|else|for|while|do|switch|try|catch|finally|synchronized)\b")]
    private static partial Regex ControlBlock();

    [GeneratedRegex(@"\b(?:class|interface|enum|record)\b|->\s*$|\bnew\s+[\w.$<>]+\s*\(.*\)\s*$|^static$|^default$")]
    private static partial Regex CannotDeclare();

    [GeneratedRegex(@"(?<name>[A-Za-z_$][\w$]*)\s*\((?:[^()]|\([^()]*\))*\)\s*(?:throws\s+[\w.$,\s]+?)?\s*$")]
    private static partial Regex MethodHeader();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (!JavaCode.IsCompileError(error)) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unmatchedClosers = 0;

        for (var k = number - 2; k >= 0; k--)
        {
            var text = masked[k];

            for (var c = text.Length - 1; c >= 0; c--)
            {
                if (text[c] == '}')
                {
                    unmatchedClosers++;
                    continue;
                }

                if (text[c] != '{') continue;

                if (unmatchedClosers > 0)
                {
                    unmatchedClosers--;
                    continue;
                }

                var headerLine = k;
                var header = text[..c].Trim();

                if (header.Length == 0 && k > 0)
                {
                    headerLine = k - 1;
                    header = masked[k - 1].Trim();
                }

                if (CannotDeclare().IsMatch(header)) return null;
                if (ControlBlock().IsMatch(header)) continue;
                if (MethodHeader().Match(header) is not { Success: true } method) return null;

                return Declare(source, masked, headerLine, headerLine == k ? c : -1,
                    method.Groups["name"].Value, Together(message.Groups["type"].Value, masked[number - 1]), number);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> Together(string type, string maskedLine)
    {
        (string Call, string[] Types)[] companions =
        [
            (@"\.get\s*\(", ["InterruptedException", "ExecutionException"]),
            (@"\.invoke\s*\(", ["IllegalAccessException", "InvocationTargetException"]),
            (@"\.newInstance\s*\(", ["InstantiationException", "IllegalAccessException", "InvocationTargetException", "NoSuchMethodException"]),
        ];

        foreach (var (call, types) in companions)
            if (types.Contains(type) && Regex.IsMatch(maskedLine, call)) return types;

        return [type];
    }

    private LocalFix? Declare(
        SourceFile source, IReadOnlyList<string> masked, int headerLine, int braceColumn,
        string method, IReadOnlyList<string> types, int errorLine)
    {
        var type = string.Join(", ", types);
        var line = source.Lines[headerLine];
        var code = masked[headerLine];
        var end = braceColumn >= 0 ? braceColumn : code.TrimEnd().Length;

        if (end <= 0) return null;

        var closeParen = code.LastIndexOf(')', end - 1);
        if (closeParen < 0) return null;

        var declared = string.Join(", ", types.Select(one =>
            !one.Contains('.') && !JavaTypes.Lang.Contains(one) && !JavaTypes.IsImported(one, source.Lines) &&
            JavaTypes.Packages.TryGetValue(one, out var package)
                ? $"{package}.{one}"
                : one));

        var between = code[(closeParen + 1)..end];
        string corrected;

        if (between.Contains("throws", StringComparison.Ordinal))
        {
            var listEnd = closeParen + 1 + between.TrimEnd().Length;
            corrected = line[..listEnd] + ", " + declared + line[listEnd..];
        }
        else
        {
            corrected = line[..(closeParen + 1)] + " throws " + declared + line[(closeParen + 1)..];
        }

        return LocalFix.ReplaceLine(
            Id,
            $"Declare that {method} throws {type}",
            $"Line {errorLine} can throw {type}, which Java requires to be caught or declared. Declaring it on {method} " +
            "passes it on to whatever calls it - for main, that means the program stops with the exception, as a crash " +
            $"would. To handle it where it happens instead, wrap line {errorLine} in try/catch.",
            source.Path, headerLine + 1, corrected);
    }
}
