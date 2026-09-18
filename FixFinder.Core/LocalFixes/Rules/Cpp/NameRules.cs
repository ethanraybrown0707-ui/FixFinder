using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>cout</c>, <c>endl</c> and <c>string</c> without <c>std::</c> - the header is included, the namespace is not written.</summary>
/// <remarks>
/// Every standard name the compiler reported on the line is qualified at once. gcc suggests <c>std::cout</c> for the
/// first of them, but <c>cout &lt;&lt; "hello" &lt;&lt; endl</c> still fails on <c>endl</c> with only that change made.
/// </remarks>
public sealed class CppStdPrefix : ILocalFixRule
{
    public string Id => "cpp-std-prefix";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppCode.UndeclaredName(context.Error) is not { } reported || CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (CppCode.UsesStd(source) || !InStd(source, reported)) return null;

        var names = context.AllErrors
            .Where(e => LocalFixContext.OwnFrame(e) is { Line: { } l } && l == number && CCode.SameFile(context, e, source))
            .Select(CppCode.UndeclaredName)
            .OfType<string>()
            .Prepend(reported)
            .Where(name => InStd(source, name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = names
            .SelectMany(name => CppCode.UnqualifiedUses(masked, name).Select(index => (Index: index, Name: name)))
            .OrderByDescending(hit => hit.Index)
            .ToList();

        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var (index, _) in hits) corrected = corrected[..index] + "std::" + corrected[index..];

        var written = names.Where(name => hits.Any(hit => hit.Name == name)).ToList();

        return LocalFix.ReplaceLine(
            Id, $"Write {CppCode.JoinWithAnd(written.Select(name => "std::" + name))}",
            $"{CppCode.JoinWithAnd(written.Select(name => $"`{name}`"))} {(written.Count == 1 ? "is" : "are")} part of the C++ standard library, which keeps " +
            $"its names inside the namespace `std` - so they are written {CppCode.JoinWithAnd(written.Select(name => $"`std::{name}`"))}. A file can say " +
            "`using namespace std;` once instead, but that brings in every name the library has.",
            source.Path, number, corrected);
    }

    /// <summary>A standard C++ name whose header this file has, and that is not a C function under the same name.</summary>
    private static bool InStd(SourceFile source, string name) =>
        CStandardLibrary.CppHeaderOf(name) is { } header && CStandardLibrary.CHeaderOf(name) is null &&
        (CCode.Includes(source, header) || (header == "string" && CppCode.HasStdString(source)));
}

/// <summary><c>std::endll</c> - a misspelt standard name, which MSVC reports as not a member of <c>std</c> and never corrects.</summary>
public sealed partial class CppStdNameTypo : ILocalFixRule
{
    public string Id => "cpp-std-name-typo";

    [GeneratedRegex(@"^'(?<name>\w+)': is not a member of 'std'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C2039", MsvcMessage()) is not { } message || CppCode.Locate(context) is not { } at) return null;

        // A real standard name is a header left out, which is the header rule's to answer.
        var wrong = message.Groups["name"].Value;
        if (CStandardLibrary.CppHeaderOf(wrong) is not null || CodeText.Nearest(wrong, CStandardLibrary.CppTable.Keys) is not { } right) return null;

        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), $@"\bstd\s*::\s*(?<name>{Regex.Escape(wrong)})\b").ToList();
        if (hits is not [var hit]) return null;

        var name = hit.Groups["name"];

        return LocalFix.ReplaceLine(
            Id, $"Change std::{wrong} to std::{right}",
            $"The standard library has no `{wrong}`. `std::{right}` is the only standard name within a letter or two of it.",
            at.Source.Path, at.Number, at.Line[..name.Index] + right + at.Line[(name.Index + name.Length)..]);
    }
}

/// <summary>
/// A member a standard container does not have: <c>values.add(1)</c>, <c>values.length()</c>, <c>ages.containsKey(k)</c>,
/// or a misspelt one, which MSVC never corrects.
/// </summary>
public sealed partial class CppContainerMember : ILocalFixRule
{
    public string Id => "cpp-container-member";

    [GeneratedRegex(@"^'(?:class |struct )?(?<type>std::[^']+)' has no member named '(?<member>\w+)'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<member>\w+)': is not a member of '(?<type>std::[^']+)'$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^std::(?:__cxx11::|__1::)?(?<kind>vector|basic_string|string|list|deque|map|unordered_map|multimap|set|unordered_set|multiset|array|stack|queue|priority_queue)\b")]
    private static partial Regex Kind();

    private const string MapMembers = "insert emplace erase find count at size empty clear begin end swap try_emplace insert_or_assign lower_bound upper_bound equal_range";
    private const string SetMembers = "insert emplace erase find count size empty clear begin end swap lower_bound upper_bound equal_range";

    private static readonly Dictionary<string, string> Members = new(StringComparer.Ordinal)
    {
        ["vector"] = "push_back pop_back emplace_back emplace size empty clear front back at begin end rbegin rend cbegin cend insert erase reserve resize data capacity assign swap shrink_to_fit",
        ["deque"] = "push_back push_front pop_back pop_front emplace_back emplace_front size empty clear front back at begin end insert erase resize assign swap",
        ["list"] = "push_back push_front pop_back pop_front emplace_back emplace_front size empty clear front back begin end insert erase remove remove_if reverse sort unique merge splice resize assign swap",
        ["string"] = "size length empty clear push_back pop_back append insert erase replace substr find rfind find_first_of find_last_of find_first_not_of find_last_not_of compare c_str data at front back begin end resize reserve capacity swap",
        ["map"] = MapMembers,
        ["unordered_map"] = MapMembers,
        ["multimap"] = MapMembers,
        ["set"] = SetMembers,
        ["unordered_set"] = SetMembers,
        ["multiset"] = SetMembers,
        ["array"] = "size empty front back at begin end data fill swap",
        ["stack"] = "push pop top size empty emplace swap",
        ["queue"] = "push pop front back size empty emplace swap",
        ["priority_queue"] = "push pop top size empty emplace swap",
    };

    private static readonly Dictionary<string, string> Sequence = new(StringComparer.Ordinal)
    {
        ["add"] = "push_back", ["Add"] = "push_back", ["append"] = "push_back", ["push"] = "push_back", ["length"] = "size",
        ["Length"] = "size", ["Count"] = "size", ["count"] = "size", ["len"] = "size", ["get"] = "at", ["isEmpty"] = "empty",
        ["IsEmpty"] = "empty", ["first"] = "front", ["last"] = "back", ["pop"] = "pop_back", ["Clear"] = "clear",
    };

    private static readonly Dictionary<string, string> Text = new(StringComparer.Ordinal)
    {
        ["Length"] = "length", ["Count"] = "size", ["len"] = "size", ["charAt"] = "at", ["get"] = "at", ["substring"] = "substr",
        ["Substring"] = "substr", ["indexOf"] = "find", ["IndexOf"] = "find", ["isEmpty"] = "empty", ["push"] = "push_back",
    };

    private static readonly Dictionary<string, string> Associative = new(StringComparer.Ordinal)
    {
        ["containsKey"] = "count", ["ContainsKey"] = "count", ["contains"] = "count", ["has"] = "count", ["get"] = "at",
        ["remove"] = "erase", ["Remove"] = "erase", ["length"] = "size", ["Count"] = "size", ["isEmpty"] = "empty",
        ["add"] = "insert", ["Add"] = "insert", ["Contains"] = "count",
    };

    private static readonly Dictionary<string, string> Adaptor = new(StringComparer.Ordinal)
    {
        ["append"] = "push", ["add"] = "push", ["Push"] = "push", ["Pop"] = "pop", ["length"] = "size", ["Count"] = "size",
        ["isEmpty"] = "empty", ["offer"] = "push", ["Enqueue"] = "push",
    };

    /// <summary>Members that take no arguments, which a property-style <c>values.Count</c> has to call.</summary>
    private static readonly HashSet<string> Called = new(StringComparer.Ordinal)
    {
        "size", "length", "empty", "front", "back", "top", "clear", "pop_back", "pop", "begin", "end", "data", "capacity",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2039", MsvcMessage())) is not { } message) return null;
        if (Kind().Match(message.Groups["type"].Value) is not { Success: true } kindMatch) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var kind = kindMatch.Groups["kind"].Value is "basic_string" ? "string" : kindMatch.Groups["kind"].Value;
        var member = message.Groups["member"].Value;
        var members = Members[kind].Split(' ');

        var foreign = kind switch
        {
            "vector" or "deque" or "list" or "array" => Sequence,
            "string" => Text,
            "map" or "unordered_map" or "multimap" or "set" or "unordered_set" or "multiset" => Associative,
            _ => Adaptor,
        };

        var typo = !foreign.TryGetValue(member, out var right);
        if (typo && CodeText.Nearest(member, members) is { } nearest) right = nearest;
        if (right is null || !members.Contains(right)) return null;

        // add on a map would need a pair; insert on a set takes the value as it is.
        if (!typo && kind is "map" or "unordered_map" or "multimap" && right == "insert") return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = Regex.Matches(masked, $@"(?:\.|->)\s*(?<name>{Regex.Escape(member)})\b").ToList();
        if (hits is not [var hit]) return null;

        var name = hit.Groups["name"];
        var after = name.Index + name.Length;
        var calls = masked[after..].TrimStart().StartsWith('(');
        var replacement = right + (!calls && Called.Contains(right) ? "()" : "");

        var shown = kind is "string" ? "std::string" : $"std::{kind}";

        return LocalFix.ReplaceLine(
            Id, $"Change {member} to {right}",
            typo
                ? $"`{shown}` has no member `{member}`. `{right}` is the only member it has within a letter or two of it."
                : $"`{shown}` has no `{member}` - that is the name in another language. The C++ name for it is `{right}`.",
            source.Path, number, line[..name.Index] + replacement + line[after..]);
    }
}

/// <summary><c>name.size</c> without its brackets - a member function named rather than called.</summary>
public sealed partial class CppMethodWithoutCall : ILocalFixRule
{
    public string Id => "cpp-method-without-call";

    [GeneratedRegex(@"^'(?:[^']*::)?(?<member>\w+)': non-standard syntax; use '&' to create a pointer to member$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"<unresolved overloaded function type>|^invalid use of non-static member function '[^']*::(?<member>\w+)\(")]
    private static partial Regex GccMessage();

    private static readonly HashSet<string> ZeroArguments = new(StringComparer.Ordinal)
    {
        "size", "length", "empty", "front", "back", "top", "begin", "end", "rbegin", "rend", "c_str", "data", "capacity",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.MsvcMessage(error, "C3867", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage())) is not { } message) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var named = message.Groups["member"].Success ? message.Groups["member"].Value : null;
        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var hits = Regex.Matches(masked, @"(?:\.|->)\s*(?<name>[A-Za-z_]\w*)\b(?!\s*\()")
            .Where(m => named is null ? ZeroArguments.Contains(m.Groups["name"].Value) : m.Groups["name"].Value == named)
            .ToList();

        if (hits is not [var hit]) return null;

        var name = hit.Groups["name"];
        var end = name.Index + name.Length;

        return LocalFix.ReplaceLine(
            Id, $"Call {name.Value}()",
            $"`{name.Value}` is a function, so it has to be called - `{name.Value}()` - to give its result. Without the brackets it names " +
            "the function itself, which is not a value that can be printed or stored.",
            source.Path, number, line[..end] + "()" + line[end..]);
    }
}

/// <summary><c>d-&gt;age</c> on an object and <c>d.age</c> on a pointer, in gcc's and MSVC's C++ words.</summary>
public sealed partial class CppMemberOperator : ILocalFixRule
{
    public string Id => "cpp-member-operator";

    [GeneratedRegex(@"^base operand of '->' has non-pointer type '(?<type>[^']+)'$")]
    private static partial Regex GccArrow();

    [GeneratedRegex(@"^type '(?<type>[^']+)' does not have an overloaded member 'operator ->'$")]
    private static partial Regex MsvcArrow();

    [GeneratedRegex(@"^request for member '(?<member>\w+)' in '(?<object>[^']+)', which is of pointer type")]
    private static partial Regex GccDot();

    [GeneratedRegex(@"^left of '\.(?<member>\w+)' must have class/struct/union$")]
    private static partial Regex MsvcDot();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if ((CCode.GccMessage(error, GccArrow()) ?? CCode.MsvcMessage(error, "C2819", MsvcArrow())) is { } arrow)
        {
            var type = Regex.Replace(arrow.Groups["type"].Value, @"^(?:struct|class)\s+|\s*const\s*|\s*&$", "");

            var hits = Regex.Matches(code, @"(?<![\w.>])(?<object>[A-Za-z_]\w*)\s*(?<op>->)")
                .Where(m => CppCode.VariableDeclaration(masked, number - 1, m.Groups["object"].Value) is { Pointer: false } d && d.Type.Split("::")[^1] == type.Split("::")[^1])
                .ToList();

            if (hits is not [var hit]) return null;

            var op = hit.Groups["op"].Index;

            return LocalFix.ReplaceLine(
                Id, "Use . instead of ->",
                $"`->` reaches a member through a pointer. `{hit.Groups["object"].Value}` is the {type} itself, not a pointer to one, so it is `.`.",
                source.Path, number, line[..op] + "." + line[(op + 2)..]);
        }

        if ((CCode.GccMessage(error, GccDot()) ?? CCode.MsvcMessage(error, "C2228", MsvcDot())) is { } dot)
        {
            var member = Regex.Escape(dot.Groups["member"].Value);
            var named = dot.Groups["object"].Success ? dot.Groups["object"].Value : null;

            var hits = Regex.Matches(code, $@"(?<![\w.>])(?<object>[A-Za-z_]\w*)\s*(?<op>\.)\s*{member}\b")
                .Where(m =>
                {
                    var name = m.Groups["object"].Value;
                    if (named is not null && name != named && !(name == "this" && named.Contains("this", StringComparison.Ordinal))) return false;
                    return name == "this" || CppCode.VariableDeclaration(masked, number - 1, name) is { Pointer: true };
                })
                .ToList();

            if (hits is not [var hit]) return null;

            var op = hit.Groups["op"].Index;
            var name = hit.Groups["object"].Value;

            return LocalFix.ReplaceLine(
                Id, "Use -> instead of .",
                name == "this"
                    ? "`this` is a pointer to the object, not the object itself - so its members are reached with `->`: `this->" + dot.Groups["member"].Value + "`. (In Java and C#, `this.` is right.)"
                    : $"`{name}` is a pointer to an object, and a member is reached through a pointer with `->`.",
                source.Path, number, line[..op] + "->" + line[(op + 1)..]);
        }

        return null;
    }
}

/// <summary>A member of a <c>class</c> used from outside it, where the class never says <c>public:</c>.</summary>
public sealed partial class CppPrivateMember : ILocalFixRule
{
    public string Id => "cpp-private-member";

    [GeneratedRegex(@"^'(?:[^']*?\s)?(?<class>\w+)::(?<member>\w+)[^']*' is private within this context$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<class>\w+)::(?<member>\w+)': cannot access private member declared in class '\k<class>'$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"\b(?:public|private|protected)\s*:(?!:)")]
    private static partial Regex Access();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2248", MsvcMessage())) is not { } message) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = Brackets.BraceDepths(masked);
        var cls = Regex.Escape(message.Groups["class"].Value);
        var member = Regex.Escape(message.Groups["member"].Value);

        var header = Enumerable.Range(0, masked.Count).Where(i => depths[i] == 0 && Regex.IsMatch(masked[i], $@"^\s*class\s+{cls}\b(?!\s*;)")).ToList();
        if (header is not [var start]) return null;

        var open = masked[start].TrimEnd().EndsWith('{') ? start : start + 1 < masked.Count && masked[start + 1].Trim() == "{" ? start + 1 : -1;
        if (open < 0) return null;

        for (var i = open + 1; i < masked.Count && depths[i] > 0; i++)
        {
            if (Access().IsMatch(masked[i])) return null;
            if (depths[i] != 1 || !Regex.IsMatch(masked[i], $@"\b{member}\s*[;=({{\[]")) continue;

            return LocalFix.Insert(
                Id, $"Make {message.Groups["member"].Value} public",
                $"Everything in a `class` is private until the class says otherwise, so only `{message.Groups["class"].Value}`'s own functions " +
                $"can reach `{message.Groups["member"].Value}`. `public:` above it lets the rest of the program use it - or, if it should stay " +
                "private, give the class a function that returns it. (In a `struct` everything starts public.)",
                source.Path, open + 2, [CodeText.Indentation(source.Lines[start]) + "public:"]);
        }

        return null;
    }
}

/// <summary><c>void speak() const { ... }</c> outside the class it belongs to, without <c>Dog::</c>.</summary>
public sealed partial class CppMemberWithoutClassName : ILocalFixRule
{
    public string Id => "cpp-member-without-class-name";

    [GeneratedRegex(@"^non-member function '(?:[^']*?\s)?(?<name>\w+)\([^']*\)' cannot have cv-qualifier$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<name>\w+)': modifiers not allowed on nonmember functions$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2270", MsvcMessage())) is not { } message) return null;
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = message.Groups["name"].Value;

        if (Brackets.BraceDepths(masked)[number - 1] != 0) return null;

        var here = Regex.Matches(masked[number - 1], $@"(?<![\w:~.>]){Regex.Escape(name)}\s*\(").ToList();
        if (here is not [var definition]) return null;

        var owners = new List<string>();

        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^\s*(?:class|struct)\s+(?<name>\w+)\b(?!\s*;)") is not { Success: true } cls) continue;
            if (CppCode.ClassHeader(masked, cls.Groups["name"].Value) != i) continue;

            var declared = CppCode.LinesDeclaring(masked, i, name).Any(k => masked[k].TrimEnd().EndsWith(';'));
            if (declared) owners.Add(cls.Groups["name"].Value);
        }

        if (owners is not [var owner]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Define {name} as {owner}::{name}",
            $"`{name}` is declared inside `{owner}`, but defined out here without saying so - so C++ took it for a separate, ordinary function, " +
            $"which cannot be `const` and cannot see `{owner}`'s members. `{owner}::{name}` says this is the member being defined.",
            source.Path, number, line[..definition.Index] + owner + "::" + line[definition.Index..]);
    }
}

/// <summary>A <c>static</c> data member declared in its class and never defined - which only the linker notices.</summary>
public sealed partial class CppStaticMemberDefinition : ILocalFixRule
{
    public string Id => "cpp-static-member-definition";

    [GeneratedRegex(@"^undefined reference to '(?<class>\w+)::(?<member>\w+)'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^unresolved external symbol ""(?:public|private|protected): static [^""]*?\b(?<class>\w+)::(?<member>\w+)""")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?:bool|char|short|int|long|float|double|unsigned|signed|size_t|std::size_t|u?int\d+_t|std::u?int\d+_t)(?:\s+(?:int|long|char|short))*$")]
    private static partial Regex Arithmetic();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = error.LanguageId == "gcc" && error.ExceptionType == "link error"
            ? GccMessage().Match(error.Message ?? "") is { Success: true } g ? g : null
            : CCode.IsMsvc(error, "LNK2001", "LNK2019") ? MsvcMessage().Match(error.Message ?? "") is { Success: true } m ? m : null : null;

        if (message is null) return null;

        var cls = message.Groups["class"].Value;
        var member = message.Groups["member"].Value;
        var found = new List<(SourceFile Source, int Close, string Type)>();

        foreach (var source in CppCode.SourceFiles(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var header = CppCode.ClassHeader(masked, cls);
            if (header < 0 || CppCode.ClassBraces(masked, header) is not { } body) continue;

            // Already defined somewhere is a different problem - a file left out of the build. A definition is at the top level
            // and starts with a type; `return Counter::count;` inside a function is a use.
            var depths = Brackets.BraceDepths(masked);
            var defined = new Regex($@"^\s*(?:const\s+)?[A-Za-z_][\w:<>]*[\s*&]+{Regex.Escape(cls)}\s*::\s*{Regex.Escape(member)}\s*(?:=|;|\{{)");
            if (Enumerable.Range(0, masked.Count).Any(i => depths[i] == 0 && defined.IsMatch(masked[i]))) return null;

            foreach (var i in CppCode.MemberLines(masked, header))
            {
                var declared = Regex.Match(masked[i], $@"^\s*static\s+(?<type>(?!inline\b|constexpr\b)[^;=(){{}}]+?)\s+{Regex.Escape(member)}\s*;");
                if (declared.Success) found.Add((source, body.Close, Regex.Replace(declared.Groups["type"].Value.Trim(), @"^const\s+", "const ")));
            }
        }

        if (found is not [var (file, close, type)]) return null;

        var initial = type == "bool" ? " = false" : Arithmetic().IsMatch(type) ? " = 0" : "";
        var definition = $"{type} {cls}::{member}{initial};";

        return LocalFix.Insert(
            Id, $"Define {cls}::{member} outside the class",
            $"`static {type} {member};` inside `{cls}` only declares that the variable exists - one shared by every `{cls}`. It still has to be " +
            $"defined once, outside the class, and that definition is what the linker could not find. (Since C++17, `inline static {type} " +
            $"{member}{initial};` inside the class does both.)",
            file.Path, close + 2, [definition]);
    }
}
