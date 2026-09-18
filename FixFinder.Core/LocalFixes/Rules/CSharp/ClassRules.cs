using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>CS0737</c>: a class member that implements an interface member, without being public.</summary>
public sealed partial class CSharpInterfaceMemberPublic : ILocalFixRule
{
    public string Id => "csharp-interface-member-public";

    [GeneratedRegex(@"^'(?<cls>\w+)' does not implement interface member '(?<iface>[\w.<>]+)\.(?<member>\w+)(?:\(.*\))?'\.")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0737") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CSharpCode.TypeDeclarationLine(masked, message.Groups["cls"].Value);
        if (declaration < 0) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var pattern = new Regex($@"^(?<lead>\s*)(?<access>(?:private|protected|internal)\s+)?(?:(?:static|virtual|override|async|abstract)\s+)*[\w<>\[\],.?]+\s+{member}\s*[({{]");

        var lines = CSharpCode.MemberLines(masked, declaration).Where(i => pattern.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        var match = pattern.Match(masked[index]);
        var original = source.Lines[index];
        var access = match.Groups["access"];
        var lead = match.Groups["lead"].Length;

        var corrected = access.Success
            ? original[..access.Index] + "public " + original[(access.Index + access.Length)..]
            : original[..lead] + "public " + original[lead..];

        return LocalFix.ReplaceLine(
            Id, $"Make {message.Groups["member"].Value} public",
            $"Everything an interface declares is public, so whatever implements `{message.Groups["iface"].Value}.{message.Groups["member"].Value}` " +
            "has to be public too - and a class member with no modifier is private.",
            source.Path, index + 1, corrected);
    }
}

/// <summary><c>CS0506</c>: <c>override</c> of a base method that is not <c>virtual</c>.</summary>
public sealed partial class CSharpVirtualBase : ILocalFixRule
{
    public string Id => "csharp-virtual-base";

    [GeneratedRegex(@"^'(?<derived>[\w.]+)(?:\(.*\))?': cannot override inherited member '(?<base>\w+)\.(?<member>\w+)(?:\(.*\))?' because it is not marked virtual, abstract, or override$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0506") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CSharpCode.TypeDeclarationLine(masked, message.Groups["base"].Value);
        if (declaration < 0) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var pattern = new Regex($@"^(?<lead>\s*(?:(?:public|protected|internal|private)\s+)*)[\w<>\[\],.?]+\s+{member}\s*[({{]");

        var lines = CSharpCode.MemberLines(masked, declaration)
            .Where(i => pattern.IsMatch(masked[i]) && !Regex.IsMatch(masked[i], @"\b(?:static|virtual|abstract|override|sealed)\b"))
            .ToList();

        if (lines is not [var index]) return null;

        var lead = pattern.Match(masked[index]).Groups["lead"].Length;
        var original = source.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Mark {message.Groups["base"].Value}.{message.Groups["member"].Value} virtual",
            $"`override` can only replace a method the base class allows to be replaced - one marked `virtual`. " +
            $"`{message.Groups["base"].Value}.{message.Groups["member"].Value}` is not, so nothing could override it.",
            source.Path, index + 1, original[..lead] + "virtual " + original[lead..]);
    }
}

/// <summary><c>CS0115: no suitable method found to override</c> - a misspelt override.</summary>
public sealed partial class CSharpOverrideTypo : ILocalFixRule
{
    public string Id => "csharp-override-typo";

    [GeneratedRegex(@"^'(?<cls>\w+)\.(?<member>\w+)(?:\(.*\))?': no suitable method found to override$")]
    private static partial Regex Message();

    private static readonly string[] ObjectMembers = ["ToString", "Equals", "GetHashCode"];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0115") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var member = message.Groups["member"].Value;
        if (!line[index..].StartsWith(member, StringComparison.Ordinal)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var declaration = CSharpCode.TypeDeclarationLine(masked, message.Groups["cls"].Value);
        if (declaration < 0) return null;

        var candidates = new HashSet<string>(ObjectMembers, StringComparer.Ordinal);
        var bases = Regex.Match(masked[declaration], $@"\b{Regex.Escape(message.Groups["cls"].Value)}\s*(?:<[^>]*>)?\s*:\s*(?<bases>[^{{]+)");

        foreach (Match name in Regex.Matches(bases.Groups["bases"].Value, @"[A-Za-z_]\w*"))
        {
            var baseDeclaration = CSharpCode.TypeDeclarationLine(masked, name.Value);
            if (baseDeclaration < 0) continue;

            foreach (var i in CSharpCode.MemberLines(masked, baseDeclaration))
                if (Regex.Match(masked[i], @"\b(?:virtual|abstract|override)\b[^(=]*\s(?<name>\w+)\s*[({]") is { Success: true } m)
                    candidates.Add(m.Groups["name"].Value);
        }

        if (CodeText.Nearest(member, candidates.Where(c => c != member)) is not { } right) return null;

        return LocalFix.ReplaceLine(
            Id, $"Rename {member} to {right}",
            $"`override` says `{member}` replaces a method it inherits, and nothing it inherits is called `{member}`. The inherited method " +
            $"within a letter or two of it is `{right}`.",
            source.Path, number, line[..index] + right + line[(index + member.Length)..]);
    }
}

/// <summary><c>CS0051</c> and friends: a public member whose signature uses a type that is not public.</summary>
public sealed partial class CSharpInconsistentAccessibility : ILocalFixRule
{
    public string Id => "csharp-inconsistent-accessibility";

    [GeneratedRegex(@"^Inconsistent accessibility: (?:parameter|return|field|property|indexer return|base class) type '(?<type>[^']+)' is less accessible than ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0050", "CS0051", "CS0052", "CS0053", "CS0060") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        foreach (Match name in Regex.Matches(message.Groups["type"].Value, @"[A-Za-z_]\w*").Reverse())
        {
            var declaration = CSharpCode.TypeDeclarationLine(masked, name.Value);
            if (declaration < 0) continue;

            var type = Regex.Match(masked[declaration], $@"^(?<lead>\s*)(?<access>(?:internal|private|protected)\s+)?(?:(?:static|sealed|abstract|partial)\s+)*(?:class|struct|interface|enum|record)\s+{Regex.Escape(name.Value)}\b");
            if (!type.Success || Regex.IsMatch(masked[declaration], @"\bpublic\b")) return null;

            var original = source.Lines[declaration];
            var access = type.Groups["access"];
            var lead = type.Groups["lead"].Length;

            var corrected = access.Success
                ? original[..access.Index] + "public " + original[(access.Index + access.Length)..]
                : original[..lead] + "public " + original[lead..];

            return LocalFix.ReplaceLine(
                Id, $"Make {name.Value} public",
                $"A public member can be used from anywhere, so every type in its signature has to be usable from anywhere too. `{name.Value}` has " +
                "no access modifier, which makes it internal - visible only inside this project. Making it public matches the member " +
                "(making the member less public would too, if it is not meant to be used from outside).",
                source.Path, declaration + 1, corrected);
        }

        return null;
    }
}

/// <summary><c>CS0535</c> and <c>CS0534</c>: a class that says it implements an interface or extends an abstract class, and never
/// writes a member that requires.</summary>
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
        if (!CSharpCode.HasErrorCode(context, "CS0535", "CS0534") || Message().Match(context.Error.Message ?? "") is not { Success: true } primary) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var cls = primary.Groups["cls"].Value;

        var header = CSharpCode.TypeDeclarationLine(masked, cls);
        if (header < 0 || ClassBody.Closing(masked, header) is not { } closing) return null;

        var indent = ClassBody.MemberIndent(lines, masked, header, closing);

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

    private string? Stub(IReadOnlyList<string> lines, IReadOnlyList<string> masked, Match missing)
    {
        var owner = missing.Groups["owner"].Value.Split('.')[^1];
        var member = Regex.Escape(missing.Groups["member"].Value);
        var isInterface = missing.Groups["kind"].Value == "interface";

        var declaration = CSharpCode.TypeDeclarationLine(masked, owner);
        if (declaration < 0) return null;

        var candidates = CSharpCode.MemberLines(masked, declaration)
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
