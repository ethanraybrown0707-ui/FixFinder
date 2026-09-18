using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// Where a class's body is, and how its members are indented, for adding members to it.
/// </summary>
/// <remarks>
/// Shared by C#, Java and C++, whose class bodies are all brace-delimited: a missing member goes in
/// on its own lines just before the closing brace, indented like the members already there.
/// </remarks>
internal static class ClassBody
{
    /// <summary>The line of a class's closing brace (0-based), or null when its body is not laid out over separate lines.</summary>
    public static int? Closing(IReadOnlyList<string> masked, int header)
    {
        if (CppClass.Body(masked, header) is not { } body || body.Open == body.Close) return null;

        return masked[body.Close].TrimStart().StartsWith('}') ? body.Close : null;
    }

    /// <summary>The indentation a new member of the class gets.</summary>
    public static string MemberIndent(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int header, int closing)
    {
        for (var i = header + 1; i < closing; i++)
        {
            var text = masked[i].Trim();
            if (text.Length == 0 || text is "{" || text.EndsWith(':')) continue;

            return Indent(lines[i]);
        }

        var outer = Indent(lines[header]);
        return outer + (lines.Any(l => l.StartsWith('\t')) ? "\t" : "    ");
    }

    public static string Indent(string line) => line[..(line.Length - line.TrimStart().Length)];

    /// <summary>One more level of indentation than <paramref name="indent"/>, in the same characters.</summary>
    public static string Deeper(string indent) => indent + (indent.Contains('\t') ? "\t" : "    ");
}

// ======================================================================= C#

/// <summary>
/// <c>CS0535</c> and <c>CS0534</c>: a class that says it implements an interface or extends an abstract
/// class, and never writes a member that requires.
/// </summary>
/// <remarks>
/// There is one conventional answer, and it is the one every editor offers as "Implement interface":
/// the member, with the signature the interface or abstract class declares, and a body that throws
/// <see cref="NotImplementedException"/> until the real one is written. Every member missing from the
/// class is added at once, so a class missing three is one change rather than three rounds. The
/// signature is copied from the declaration in the source - which has to be in the same file - so it
/// matches exactly, default values and generic constraints included; an override drops its
/// constraints, which C# does not allow it to repeat.
/// </remarks>
public sealed partial class CSharpUnwrittenMember : ILocalFixRule
{
    public string Id => "csharp-unwritten-member";

    [GeneratedRegex(@"^'(?<cls>\w+)' does not implement (?<kind>interface|inherited abstract) member '(?<owner>[\w.]+?)(?:<[^']*?>)?\.(?<member>\w+)(?:<[^']*?>)?(?:\([^']*\))?'\.?$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:(?:public|protected|internal|private|abstract|virtual|override|sealed|new)\s+)+")]
    private static partial Regex Modifiers();

    [GeneratedRegex(@"\s+where\s+\w+\s*:.*$")]
    private static partial Regex Constraints();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Cs.Is(context, "CS0535", "CS0534") || Message().Match(context.Error.Message ?? "") is not { Success: true } primary) return null;
        if (Cs.Locate(context) is not { } at) return null;

        var source = at.Source;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var cls = primary.Groups["cls"].Value;

        var header = CsTypes.Declaration(masked, cls);
        if (header < 0 || ClassBody.Closing(masked, header) is not { } closing) return null;

        var indent = ClassBody.MemberIndent(lines, masked, header, closing);

        // Every member this class is missing, from every error about it - the one asked about first.
        var wanted = context.AllErrors
            .Where(e => e.LanguageId == "msvc" && e.ErrorCode is "CS0535" or "CS0534")
            .Select(e => Message().Match(e.Message ?? ""))
            .Where(m => m.Success && m.Groups["cls"].Value == cls)
            .Prepend(primary)
            .DistinctBy(m => $"{m.Groups["owner"].Value}.{m.Groups["member"].Value}")
            .ToList();

        var stubs = new List<(string Name, string Line)>();

        foreach (var missing in wanted)
        {
            if (Stub(lines, masked, missing) is { } stub) stubs.Add((missing.Groups["member"].Value, indent + stub));
            else if (ReferenceEquals(missing, primary)) return null;
        }

        var names = string.Join(", ", stubs.Select(s => s.Name));
        var owner = primary.Groups["owner"].Value.Split('.')[^1];

        return LocalFix.Insert(
            Id, $"Add {names} to {cls}, to be written",
            $"`{cls}` {(primary.Groups["kind"].Value == "interface" ? "says it implements" : "extends")} `{owner}`, which requires " +
            $"{(stubs.Count == 1 ? "a member" : "members")} it never writes: {names}. This adds {(stubs.Count == 1 ? "it" : "them")} with the " +
            "signature declared there and a body that throws NotImplementedException - so the program builds, and calling one " +
            "says plainly that it has not been written yet. Replace each body with the real code.",
            source.Path, closing + 1, stubs.Select(s => s.Line).ToList());
    }

    /// <summary>The member to add, copied from its declaration in the interface or abstract class, or null.</summary>
    private string? Stub(IReadOnlyList<string> lines, IReadOnlyList<string> masked, Match missing)
    {
        var owner = missing.Groups["owner"].Value.Split('.')[^1];
        var member = Regex.Escape(missing.Groups["member"].Value);
        var isInterface = missing.Groups["kind"].Value == "interface";

        var declaration = CsTypes.Declaration(masked, owner);
        if (declaration < 0) return null;

        var candidates = CsTypes.Members(masked, declaration)
            .Where(i => Regex.IsMatch(masked[i], $@"(?<![\w.]){member}\s*(?:<[^>]*>)?\s*[({{]"))
            .ToList();

        if (candidates is not [var index]) return null;

        var text = lines[index].Trim();
        var isProperty = !Regex.IsMatch(masked[index], $@"(?<![\w.]){member}\s*(?:<[^>]*>)?\s*\(");

        if (!isInterface && !Regex.IsMatch(masked[index], @"\babstract\b")) return null;

        var modifiers = Modifiers().Match(text);
        var access = isInterface
            ? "public"
            : string.Join(' ', modifiers.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(m => m is "public" or "protected" or "internal" or "private"));
        var head = (access.Length > 0 ? access + " " : "") + (isInterface ? "" : "override ");
        var signature = text[modifiers.Length..];

        const string Throw = "throw new System.NotImplementedException();";

        if (isProperty)
        {
            var open = signature.IndexOf('{');
            if (open < 0 || !signature.EndsWith('}')) return null;

            var accessors = signature[open..];
            var declared = signature[..open].TrimEnd();

            return Regex.IsMatch(accessors, @"\b(?:set|init)\b")
                ? $"{head}{declared} {accessors}"
                : $"{head}{declared} => {Throw}";
        }

        if (!signature.EndsWith(';')) return null;

        signature = signature[..^1].TrimEnd();
        if (!isInterface) signature = Constraints().Replace(signature, "");

        return $"{head}{signature} => {Throw}";
    }
}

// ======================================================================= Java

/// <summary>
/// <c>Square is not abstract and does not override abstract method area() in Shape</c>: a method an
/// interface or abstract class requires, never written.
/// </summary>
/// <remarks>
/// The same conventional answer as C#'s: the method with the declared signature, marked
/// <c>@Override</c>, whose body throws <c>UnsupportedOperationException</c> until it is written. javac
/// names one missing method at a time, so each is its own fix. The declaration is read from the same
/// file, or from the file named for the interface or class beside it.
/// </remarks>
public sealed partial class JavaUnwrittenMethod : ILocalFixRule
{
    public string Id => "java-unwritten-method";

    [GeneratedRegex(@"^(?<cls>\w+) is not abstract and does not override abstract method (?:<[^>]+>)?(?<method>\w+)\((?<params>[^)]*)\) in (?<owner>\w+)")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:(?:public|protected|private|abstract|default|static|final|synchronized)\s+)+")]
    private static partial Regex Modifiers();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!Java.IsCompileError(context.Error) || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
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

        // Everything an interface declares is public, and an implementation may not narrow it.
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

    /// <summary>The abstract declaration of the method, from this file or the owner's own file beside it.</summary>
    private static (string Text, bool Interface)? Declaration(LocalFixContext context, SourceFile here, string owner, string method, string parameters)
    {
        var folder = Path.GetDirectoryName(here.Path)!;

        foreach (var source in new[] { here, context.Read(Path.Combine(folder, owner + ".java")) })
        {
            if (source is null) continue;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var header = Enumerable.Range(0, masked.Count).FirstOrDefault(i => Regex.IsMatch(masked[i], $@"\b(?:class|interface)\s+{owner}\b"), -1);
            if (header < 0 || CppClass.Body(masked, header) is not { } body) continue;

            var isInterface = Regex.IsMatch(masked[header], $@"\binterface\s+{owner}\b");
            var depths = CCode.DepthAtStart(masked);
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

// ======================================================================= C++

/// <summary>
/// A derived class used as an object while pure virtual functions it inherits are still unwritten:
/// g++'s <c>cannot declare variable 's' to be of abstract type 'Square'</c>, MSVC's <c>C2259</c>.
/// </summary>
/// <remarks>
/// Both compilers list the pure virtual functions in notes after the error, with where each is declared,
/// so every one of them is added to the class at once: the declared signature, marked <c>override</c>,
/// with a body that throws <c>std::logic_error</c> until it is written. When the class named is the one
/// declaring the pure functions - <c>Shape s;</c> - there is no derived class to add anything to, and the
/// right fix could be a different type altogether, so nothing is offered.
/// </remarks>
public sealed partial class CppUnwrittenOverride : ILocalFixRule
{
    public string Id => "cpp-unwritten-override";

    [GeneratedRegex(@"^(?:cannot declare variable '[^']*' to be of abstract type|invalid new-expression of abstract class type|cannot allocate an object of abstract type|cannot declare field '[^']*' to be of abstract type|invalid abstract return type|invalid cast to abstract class type) '(?<cls>\w+)'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<cls>\w+)': cannot instantiate abstract class")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?<file>.+?):(?<line>\d+):\d+: note:\s+'virtual (?<signature>.+?)\s*'$")]
    private static partial Regex GccPureNote();

    [GeneratedRegex(@"note:\s+because the following virtual functions are pure within '(?<cls>\w+)':")]
    private static partial Regex GccPureWithin();

    [GeneratedRegex(@"^(?<file>.+)\((?<line>\d+)\): note: see declaration of '(?<owner>\w+)::(?<member>~?\w+)'$")]
    private static partial Regex MsvcDeclaredAt();

    [GeneratedRegex(@"^(?<file>.+?):\d+:\d+: note:\s+because the following virtual functions are pure within '(?<cls>\w+)':$")]
    private static partial Regex GccClassAt();

    [GeneratedRegex(@"^(?<file>.+)\((?<line>\d+)\): note: see declaration of '(?<cls>\w+)'$")]
    private static partial Regex MsvcClassAt();

    [GeneratedRegex(@"^\s*(?<access>public|protected|private)\s*:")]
    private static partial Regex AccessLabel();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var cls = (CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2259", MsvcMessage()))?.Groups["cls"].Value;
        if (cls is null) return null;

        var output = context.Output.Select(l => l.Text).ToList();
        var pure = error.LanguageId == "gcc" ? GccPure(output, cls) : MsvcPure(output);

        // The abstract class itself, used as an object: nothing derived to add to.
        if (pure.Count == 0 || pure.Any(p => p.Owner == cls || p.Member.StartsWith('~'))) return null;

        if (ClassFile(context, output, cls) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var header = CppClass.Header(masked, cls);
        if (header < 0 || ClassBody.Closing(masked, header) is not { } closing) return null;

        var stubs = new List<string>();

        foreach (var (owner, member, file, line) in pure.DistinctBy(p => p.Member))
        {
            if (context.Read(file) is not { } declaring || declaring.Line(line) is not { } declaration) return null;
            if (Stub(declaration, member) is not { } stub) return null;

            stubs.Add(stub);
        }

        var indent = ClassBody.MemberIndent(lines, masked, header, closing);
        var added = new List<string>();

        // A class's members are private until it says otherwise, and an override nobody outside can call
        // would only trade this error for "is private within this context".
        var label = Enumerable.Range(header, closing - header).Select(i => AccessLabel().Match(masked[i])).LastOrDefault(m => m.Success);
        var isStruct = Regex.IsMatch(masked[header], @"^\s*struct\b");

        if (label is null ? !isStruct : label.Groups["access"].Value != "public")
            added.Add(ClassBody.Indent(lines[header]) + "public:");

        added.AddRange(stubs.Select(s => indent + s));

        var names = string.Join(", ", pure.Select(p => p.Member).Distinct());
        var owners = string.Join(", ", pure.Select(p => p.Owner).Distinct());

        return LocalFix.Insert(
            Id, $"Add {names} to {cls}, to be written",
            $"`{cls}` inherits pure virtual {(stubs.Count == 1 ? "function" : "functions")} from `{owners}` that it never writes - {names} - " +
            $"so it is still abstract, and no `{cls}` object can exist. This adds {(stubs.Count == 1 ? "it" : "them")} with the declared " +
            "signature, marked `override`, and a body that throws std::logic_error, so the program builds and calling one says " +
            "plainly that it has not been written yet. Replace each body with the real code.",
            source.Path, closing + 1, added);
    }

    private static List<(string Owner, string Member, string File, int Line)> GccPure(IReadOnlyList<string> output, string cls)
    {
        var found = new List<(string, string, string, int)>();
        var within = false;

        foreach (var line in output)
        {
            if (GccPureWithin().Match(line) is { Success: true } header) { within = header.Groups["cls"].Value == cls; continue; }
            if (line.Contains(": error:", StringComparison.Ordinal)) { within = false; continue; }
            if (!within || GccPureNote().Match(line) is not { Success: true } note) continue;

            var signature = Regex.Match(note.Groups["signature"].Value, @"(?<owner>\w+)::(?<member>~?\w+)\(");
            if (!signature.Success) continue;

            found.Add((signature.Groups["owner"].Value, signature.Groups["member"].Value, note.Groups["file"].Value, int.Parse(note.Groups["line"].Value)));
        }

        return found;
    }

    private static List<(string Owner, string Member, string File, int Line)> MsvcPure(IReadOnlyList<string> output) =>
        output.Select(l => MsvcDeclaredAt().Match(l))
            .Where(m => m.Success)
            .Select(m => (m.Groups["owner"].Value, m.Groups["member"].Value, m.Groups["file"].Value, int.Parse(m.Groups["line"].Value)))
            .ToList();

    /// <summary>The file the class is defined in, as the compiler's note says.</summary>
    private static SourceFile? ClassFile(LocalFixContext context, IReadOnlyList<string> output, string cls)
    {
        foreach (var line in output)
        {
            if (GccClassAt().Match(line) is { Success: true } gcc && gcc.Groups["cls"].Value == cls) return context.Read(gcc.Groups["file"].Value);
            if (MsvcClassAt().Match(line) is { Success: true } msvc && msvc.Groups["cls"].Value == cls) return context.Read(msvc.Groups["file"].Value);
        }

        return context.Read(context.Frame?.File);
    }

    /// <summary><c>virtual double area() const = 0;</c> as <c>double area() const override { throw ...; }</c>, or null.</summary>
    private static string? Stub(string declaration, string member)
    {
        var text = declaration.Trim();
        if (!Regex.IsMatch(text, $@"\b{Regex.Escape(member)}\s*\(") || !Regex.IsMatch(text, @"=\s*0\s*;\s*(?://.*)?$")) return null;

        text = Regex.Replace(text, @"^virtual\s+", "");
        text = Regex.Replace(text, @"\s*=\s*0\s*;\s*(?://.*)?$", "");
        text = Regex.Replace(text, @"\s+(?:override|final)\b", "");

        if (text.Contains(';') || text.Contains('{')) return null;

        return $"{text} override {{ throw std::logic_error(\"{member} is not written yet\"); }}";
    }
}
