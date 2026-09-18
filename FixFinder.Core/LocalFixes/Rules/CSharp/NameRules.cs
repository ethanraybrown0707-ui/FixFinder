using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>CS0103: The name 'x' does not exist in the current context</c>.</summary>
/// <remarks>
/// Four different mistakes produce this one message: a word from another language (<c>print</c>,
/// <c>True</c>, <c>None</c>, <c>len</c>), a loop counter nobody declared, or a plain typo. Each is
/// told apart by what the name is and where it sits.
/// </remarks>
public sealed partial class CSharpNameMissing : ILocalFixRule
{
    public string Id => "csharp-name-missing";

    [GeneratedRegex(@"^The name '(?<name>\w+)' does not exist in the current context$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["True"] = "true", ["False"] = "false", ["None"] = "null", ["NULL"] = "null", ["nil"] = "null", ["Null"] = "null",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0103") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        var name = message.Groups["name"].Value;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var escaped = Regex.Escape(name);

        if (Words.TryGetValue(name, out var word))
            return Replace(source, number, line, masked, name, word, $"C# writes `{name}` as `{word}`, in lower case.");

        if (name is "print" or "println" or "printf" && Regex.Match(masked, $@"(?<![\w.]){escaped}\s*\(") is { Success: true } call)
        {
            return LocalFix.ReplaceLine(
                Id, "Write to the console with Console.WriteLine",
                $"`{name}` is not C#. Text goes to the console with `Console.WriteLine`.",
                source.Path, number, line[..call.Index] + "Console.WriteLine" + line[(call.Index + name.Length)..]);
        }

        if (name == "len" && Regex.Match(masked, @"(?<![\w.])len\s*\((?<arg>[^()]+)\)") is { Success: true } len)
        {
            var argument = line.Substring(len.Groups["arg"].Index, len.Groups["arg"].Length).Trim();

            return LocalFix.ReplaceLine(
                Id, $"Use {argument}.Length",
                "`len(...)` is Python. In C# a string or array has a `Length` property.",
                source.Path, number, line[..len.Index] + $"{argument}.Length" + line[(len.Index + len.Length)..]);
        }

        if (Regex.Match(masked, $@"\bfor\s*\(\s*(?<name>{escaped})\s*=") is { Success: true } counter)
        {
            var at_ = counter.Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Declare the loop counter {name}",
                $"`{name}` is used as the loop counter but never declared. Declaring it in the loop with `int` is the usual way.",
                source.Path, number, line[..at_] + "int " + line[at_..]);
        }

        var candidates = CodeText.Identifiers(CodeText.MaskAll(source.Lines, Syntax.CLike))
            .Where(identifier => identifier != name && !CSharpCode.Keywords.Contains(identifier));

        if (CodeText.Nearest(name, candidates) is not { } right) return null;
        if (CodeText.ReplaceWord(line, name, right, Syntax.CLike, index) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {name} to {right}",
            $"Nothing called `{name}` exists here. `{right}` is the only name in this file within a letter or two of it.",
            source.Path, number, corrected);
    }

    private LocalFix? Replace(SourceFile source, int number, string line, string masked, string name, string right, string explanation)
    {
        var hits = Regex.Matches(masked, $@"(?<![\w.]){Regex.Escape(name)}(?!\w)");
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + right + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(Id, $"Write {name} as {right}", explanation, source.Path, number, corrected);
    }
}

/// <summary>
/// <c>CS0117 / CS1061: 'X' does not contain a definition for 'y'</c> - matched against what the type
/// really has, read from the runtime.
/// </summary>
public sealed partial class CSharpMissingMember : ILocalFixRule
{
    public string Id => "csharp-missing-member";

    [GeneratedRegex(@"^'(?<type>[^']+)' does not contain a definition for '(?<name>\w+)'")]
    private static partial Regex Message();

    private static readonly HashSet<string> LengthWords = ["Length", "length", "Count", "count", "Size", "size", "len"];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0117", "CS1061") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        var typeName = message.Groups["type"].Value;
        var name = message.Groups["name"].Value;

        if (CSharpTypes.Resolve(typeName) is not { } type) return null;

        var isStatic = context.Error.ErrorCode == "CS0117";
        var members = CSharpTypes.Members(type, isStatic);

        string? right;

        if (!isStatic && LengthWords.Contains(name))
            right = CSharpTypes.IsCollection(type) ? "Count" : type == typeof(string) || type == typeof(Array) ? "Length" : null;
        else
            right = CodeText.Nearest(name, members.Keys.Where(member => member != name));

        if (right is null || !members.TryGetValue(right, out var callable)) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = Regex.Matches(masked, $@"\.\s*(?<name>{Regex.Escape(name)})(?!\w)").Select(m => m.Groups["name"]).ToList();
        var chosen = hits.Count == 1 ? hits[0] : hits.FirstOrDefault(h => h.Index == index);
        if (chosen is null) return null;

        var after = chosen.Index + chosen.Length;
        var corrected = line[..chosen.Index] + right;
        var rest = line[after..];

        // Java's name.length() is a property in C#: the brackets have to go with the rename, or the
        // fix trades one error for "non-invocable member".
        if (!callable && Regex.Match(rest, @"^\s*\(\s*\)") is { Success: true } brackets) rest = rest[brackets.Length..];

        return LocalFix.ReplaceLine(
            Id, $"Change {name} to {right}",
            $"`{typeName}` has no `{name}`. What it does have is `{right}`" + (callable ? "." : ", which is a property rather than a method."),
            source.Path, number, corrected + rest);
    }
}

/// <summary><c>CS0246</c>: a type that needs a using, or whose name is misspelt or borrowed from Java.</summary>
public sealed partial class CSharpTypeNotFound : ILocalFixRule
{
    public string Id => "csharp-type-not-found";

    [GeneratedRegex(@"^The type or namespace name '(?<name>\w+)(?:<[^']*>)?' could not be found")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*using\s+[\w.]+\s*;")]
    private static partial Regex UsingLine();

    private static readonly Dictionary<string, string> Namespaces = new(StringComparer.Ordinal)
    {
        ["List"] = "System.Collections.Generic", ["Dictionary"] = "System.Collections.Generic", ["HashSet"] = "System.Collections.Generic",
        ["Queue"] = "System.Collections.Generic", ["Stack"] = "System.Collections.Generic", ["LinkedList"] = "System.Collections.Generic",
        ["SortedDictionary"] = "System.Collections.Generic", ["KeyValuePair"] = "System.Collections.Generic",
        ["StringBuilder"] = "System.Text", ["Encoding"] = "System.Text", ["Regex"] = "System.Text.RegularExpressions",
        ["Task"] = "System.Threading.Tasks", ["Thread"] = "System.Threading", ["File"] = "System.IO", ["Path"] = "System.IO",
        ["Directory"] = "System.IO", ["StreamReader"] = "System.IO", ["StreamWriter"] = "System.IO", ["Stopwatch"] = "System.Diagnostics",
        ["HttpClient"] = "System.Net.Http", ["JsonSerializer"] = "System.Text.Json", ["CultureInfo"] = "System.Globalization",
    };

    private static readonly Dictionary<string, string> Borrowed = new(StringComparer.Ordinal)
    {
        ["boolean"] = "bool", ["Integer"] = "int", ["HashMap"] = "Dictionary", ["str"] = "string",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0246") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var name = message.Groups["name"].Value;

        if (Namespaces.TryGetValue(name, out var ns))
        {
            if (source.Lines.Any(l => l.Trim() == $"using {ns};")) return null;

            var last = -1;
            for (var i = 0; i < source.Count; i++)
                if (UsingLine().IsMatch(source.Lines[i])) last = i;

            return LocalFix.Insert(
                Id, $"Add using {ns};",
                $"`{name}` lives in `{ns}`, which this file does not use.",
                source.Path, last + 2, [$"using {ns};"]);
        }

        var declared = CodeText.MaskAll(source.Lines, Syntax.CLike)
            .SelectMany(text => Regex.Matches(text, @"\b(?:class|struct|record|interface|enum)\s+(?<name>\w+)").Select(m => m.Groups["name"].Value));

        var right = Borrowed.GetValueOrDefault(name) ??
                    CodeText.Nearest(name, Namespaces.Keys.Concat(CSharpTypes.Names).Concat(declared).Where(n => n != name));

        if (right is null) return null;

        var hits = Regex.Matches(CodeText.Mask(line, Syntax.CLike), $@"(?<![\w.]){Regex.Escape(name)}(?!\w)");
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var hit in hits.Reverse()) corrected = corrected[..hit.Index] + right + corrected[(hit.Index + hit.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Change {name} to {right}",
            Borrowed.ContainsKey(name)
                ? $"`{name}` is how another language says it. In C# the type is `{right}`."
                : $"There is no type called `{name}`. `{right}` is the only one within a letter or two of it.",
            source.Path, number, corrected);
    }
}

/// <summary><c>CS0234</c>: a namespace, or a type written with its namespace, spelt almost right - <c>System.Collection.Generic</c>.</summary>
public sealed partial class CSharpNamespaceTypo : ILocalFixRule
{
    public string Id => "csharp-namespace-typo";

    [GeneratedRegex(@"^The type or namespace name '(?<name>\w+)(?:<[^']*>)?' does not exist in the namespace '(?<parent>[\w.]+)'")]
    private static partial Regex Message();

    private static readonly string[] Known =
    [
        "System.Collections", "System.Collections.Generic", "System.Collections.Concurrent", "System.IO", "System.Linq",
        "System.Text", "System.Text.Json", "System.Text.RegularExpressions", "System.Threading", "System.Threading.Tasks",
        "System.Net", "System.Net.Http", "System.Diagnostics", "System.Globalization", "System.Numerics", "System.Reflection",
        "System.Runtime", "System.Security", "System.Security.Cryptography", "System.Xml", "System.Xml.Linq", "System.Data",
        "System.ComponentModel", "System.Buffers", "System.Timers",
    ];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0234") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        var parent = message.Groups["parent"].Value;
        var name = message.Groups["name"].Value;

        // What the parent namespace holds: the namespaces under it, and the types it declares.
        var children = Known
            .Where(ns => ns.StartsWith(parent + ".", StringComparison.Ordinal))
            .Select(ns => ns[(parent.Length + 1)..].Split('.')[0])
            .Concat(CSharpTypes.Names.Where(type => CSharpTypes.Resolve(type)?.Namespace == parent))
            .Distinct();

        if (CodeText.Nearest(name, children) is not { } right) return null;

        var wrong = $"{parent}.{name}";
        var at_ = line.IndexOf(wrong, StringComparison.Ordinal);
        if (at_ < 0 || line.IndexOf(wrong, at_ + 1, StringComparison.Ordinal) >= 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong} to {parent}.{right}",
            $"`{parent}` has no `{name}`. What it does have is `{right}`.",
            source.Path, number, line[..at_] + $"{parent}.{right}" + line[(at_ + wrong.Length)..]);
    }
}

/// <summary><c>CS0120</c>: a method called from <c>static Main</c> that is not static.</summary>
public sealed partial class CSharpNonStaticMember : ILocalFixRule
{
    public string Id => "csharp-non-static-member";

    [GeneratedRegex(@"^An object reference is required for the non-static field, method, or property '(?:[\w.]+\.)?(?<type>\w+)\.(?<member>\w+)(?:\(.*\))?'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0120") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var declaration = new Regex($@"^(?<lead>\s*(?:(?:public|private|protected|internal|virtual|override|sealed|async|readonly)\s+)*)(?<type>[\w<>\[\],.?]+)\s+{member}\s*[({{=;]");

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var found = masked.Select((text, i) => (Index: i, Match: declaration.Match(text)))
            .Where(d => d.Match.Success && d.Match.Groups["type"].Value is not ("return" or "new" or "else"))
            .ToList();

        if (found.Count != 1 || Regex.IsMatch(masked[found[0].Index], @"\b(?:static|const)\b")) return null;

        var (index, match) = found[0];
        var lead = match.Groups["lead"].Length;
        var original = source.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Make {message.Groups["member"].Value} static",
            $"`Main` is static, so it has no object to call `{message.Groups["member"].Value}` on. It uses nothing that belongs to one - it compiles as static - so that is the direct fix.",
            source.Path, index + 1, original[..lead] + "static " + original[lead..]);
    }
}

/// <summary><c>CS1955: Non-invocable member</c> - brackets after a property, or a type used without new.</summary>
public sealed partial class CSharpNonInvocable : ILocalFixRule
{
    public string Id => "csharp-non-invocable";

    [GeneratedRegex(@"^Non-invocable member '(?<member>[^']+)' cannot be used like a method\.$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS1955") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, index) = at;
        if (index < 0) return null;

        var member = message.Groups["member"].Value;
        var dot = member.LastIndexOf('.');

        // A type - List<T> - called like a method: the object was never created. Roslyn points at the
        // type's own name, so `new` goes in front of any namespace written before it.
        if (dot < 0 || member.IndexOf('<') is var generic && generic >= 0 && generic < dot)
        {
            var start = index;
            while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] is '_' or '.')) start--;

            return LocalFix.ReplaceLine(
                Id, "Create it with new",
                $"`{member}` is a type, and calling it like a method does not make one. `new` creates the object.",
                source.Path, number, line[..start] + "new " + line[start..]);
        }

        var name = member[(dot + 1)..];
        if (!line[index..].StartsWith(name, StringComparison.Ordinal)) return null;

        var after = index + name.Length;
        if (Regex.Match(line[after..], @"^\s*\(\s*\)") is not { Success: true } brackets) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {name} without brackets",
            $"`{name}` is a property, so it is read, not called.",
            source.Path, number, line[..after] + line[(after + brackets.Length)..]);
    }
}

/// <summary><c>CS0428</c>: a method named without the brackets that call it.</summary>
public sealed partial class CSharpMethodGroup : ILocalFixRule
{
    public string Id => "csharp-method-group";

    [GeneratedRegex(@"^Cannot convert method group '(?<method>\w+)' to non-delegate type '[^']+'\. Did you intend to invoke the method\?$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0428") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var method = message.Groups["method"].Value;
        var name = Regex.Match(CodeText.Mask(line, Syntax.CLike)[index..], $@"\b{Regex.Escape(method)}\b(?!\s*\()");
        if (!name.Success) return null;

        var end = index + name.Index + name.Length;

        return LocalFix.ReplaceLine(
            Id, $"Call {method}()",
            $"Without brackets `{method}` names the method rather than calling it. `{method}()` runs it and gives its result.",
            source.Path, number, line[..end] + "()" + line[end..]);
    }
}

/// <summary><c>CS0176</c>: a static member reached through an object instead of its class.</summary>
public sealed partial class CSharpStaticThroughInstance : ILocalFixRule
{
    public string Id => "csharp-static-through-instance";

    [GeneratedRegex(@"^Member '(?<type>[\w.]+)\.(?<member>\w+)(?:\(.*\))?' cannot be accessed with an instance reference; qualify it with a type name instead$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0176") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (CSharpCode.Locate(context) is not { } at || at.Index < 0) return null;

        var (source, number, line, index) = at;
        var member = Regex.Escape(message.Groups["member"].Value);
        var access = Regex.Match(line[index..], $@"^(?<receiver>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\.\s*{member}\b");
        if (!access.Success) return null;

        var type = message.Groups["type"].Value;
        var receiver = access.Groups["receiver"];

        return LocalFix.ReplaceLine(
            Id, $"Use {type}.{message.Groups["member"].Value}",
            $"`{message.Groups["member"].Value}` is static: it belongs to `{type}` itself, not to any one object, so it is reached through the class name.",
            source.Path, number, line[..index] + type + line[(index + receiver.Length)..]);
    }
}

/// <summary><c>CS0122</c>: a member of a class in this file used from outside it, without being public.</summary>
public sealed partial class CSharpInaccessible : ILocalFixRule
{
    public string Id => "csharp-inaccessible";

    [GeneratedRegex(@"^'(?:[\w.]+\.)?(?<type>\w+)\.(?<member>\w+)(?:\(.*\))?' is inaccessible due to its protection level$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0122") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var type = Regex.Escape(message.Groups["type"].Value);
        var member = Regex.Escape(message.Groups["member"].Value);
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var classes = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"\b(?:class|struct|record)\s+{type}\b")).ToList();
        if (classes.Count != 1) return null;

        var depth = 0;
        var started = false;
        var declaration = new Regex($@"^(?<lead>\s*)(?<access>(?:private|protected|internal)\s+)?(?<rest>(?:(?:static|virtual|override|readonly|async)\s+)*[\w<>\[\],.?]+\s+{member}\s*[({{=;])");

        for (var i = classes[0]; i < masked.Count; i++)
        {
            if (started && depth == 1 && declaration.Match(masked[i]) is { Success: true } match)
            {
                var original = source.Lines[i];
                var access = match.Groups["access"];
                var corrected = access.Success
                    ? original[..access.Index] + "public " + original[(access.Index + access.Length)..]
                    : original[..match.Groups["lead"].Length] + "public " + original[match.Groups["lead"].Length..];

                return LocalFix.ReplaceLine(
                    Id, $"Make {message.Groups["member"].Value} public",
                    $"Members of a class are private unless they say otherwise, so `{message.Groups["member"].Value}` cannot be used from outside `{message.Groups["type"].Value}`.",
                    source.Path, i + 1, corrected);
            }

            foreach (var c in masked[i])
            {
                if (c == '{') { depth++; started = true; }
                else if (c == '}') depth--;
            }

            if (started && depth == 0) break;
        }

        return null;
    }
}
