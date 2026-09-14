using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the C++ construct rules share: the file being C++, the standard namespace, and declarations.</summary>
/// <remarks>
/// C++ is read with the C helpers - the same braces, comments and literals - and these add what only C++ has:
/// whether the file already says <c>using namespace std;</c>, which decides between <c>std::cout</c> and
/// <c>cout</c>, and how a name was declared, which decides between <c>.</c> and <c>-&gt;</c>.
/// </remarks>
internal static partial class Cpp
{
    public static bool IsCpp(SourceFile source) =>
        Path.GetExtension(source.Path).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c++" or ".hpp" or ".hh" or ".hxx";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context) =>
        CCode.IsCompiler(context.Error) && CCode.Locate(context) is { } at && IsCpp(at.Source) ? at : null;

    [GeneratedRegex(@"^\s*using\s+namespace\s+std\s*;")]
    private static partial Regex UsingStd();

    [GeneratedRegex(@"^'(?<name>[A-Za-z_]\w*)' was not declared in this scope")]
    private static partial Regex GccUndeclared();

    [GeneratedRegex(@"^'(?<name>[A-Za-z_]\w*)': (?:undeclared identifier|identifier not found)$")]
    private static partial Regex MsvcUndeclared();

    [GeneratedRegex(@"^(?:return|delete|new|else|case|throw|goto|sizeof|typedef|using|co_return|co_yield|operator|public|private|protected)$")]
    private static partial Regex NotAType();

    public static bool UsesStd(SourceFile source) =>
        CodeText.MaskAll(source.Lines, Syntax.CLike).Any(line => UsingStd().IsMatch(line));

    /// <summary>A standard name as this file has to write it: <c>std::cout</c>, or <c>cout</c> under <c>using namespace std;</c>.</summary>
    public static string Std(SourceFile source, string name) => UsesStd(source) ? name : "std::" + name;

    /// <summary>Whether <c>std::string</c> is there to be used - its own header, or one of the streams, which bring it.</summary>
    public static bool HasStdString(SourceFile source) =>
        new[] { "string", "iostream", "sstream", "fstream", "ostream", "istream" }.Any(header => CCode.Includes(source, header));

    /// <summary>The name an error says was never declared, in gcc's words or MSVC's.</summary>
    public static string? Undeclared(ParsedError error) =>
        (CCode.GccMessage(error, GccUndeclared()) ??
         (CCode.IsMsvc(error, "C2065", "C3861") ? MsvcUndeclared().Match(error.Message ?? "") is { Success: true } m ? m : null : null))
        ?.Groups["name"].Value;

    /// <summary>Where a word stands alone on a masked line, not already qualified and not a member of something.</summary>
    public static List<int> Unqualified(string masked, string word) =>
        Regex.Matches(masked, $@"(?<![\w.:])(?<!->){Regex.Escape(word)}(?!\w)").Select(m => m.Index).ToList();

    public static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.SkipLast(1)) + " and " + list[^1];
    }

    /// <summary>
    /// How a local variable or parameter is declared - its type, and whether it is a pointer or a reference - found by
    /// looking up from a line to the top of the function it is in.
    /// </summary>
    /// <remarks>
    /// A declaration starts a statement, a parameter or a for loop's first clause, so only a type that follows the start
    /// of a line, <c>;</c>, <c>(</c>, <c>{</c> or <c>,</c> counts: <c>total = price * count;</c> declares nothing.
    /// </remarks>
    public static (string Type, bool Pointer, bool Reference, int Line)? Declaration(IReadOnlyList<string> masked, int index, string name)
    {
        var (first, _) = CCode.EnclosingFunction(masked, index);
        var pattern = new Regex(
            $@"(?<=(?:^|[;({{,])\s*)(?<type>(?:(?:const|unsigned|signed|long|short|static)\s+)*[A-Za-z_][\w:]*(?:\s*<[^;()]*>)?)(?<marks>\s*[*&]+\s*|\s+){Regex.Escape(name)}\s*(?=[=;,)\[({{])");

        for (var i = index; i >= first; i--)
        {
            foreach (var match in pattern.Matches(masked[i]).Reverse())
            {
                var type = match.Groups["type"].Value.Trim();
                if (NotAType().IsMatch(type)) continue;

                var marks = match.Groups["marks"].Value;
                return (Regex.Replace(type, @"^(?:const|static)\s+", ""), marks.Contains('*'), marks.Contains('&'), i);
            }
        }

        return null;
    }

    /// <summary>The string and character literals on a line: where each starts, where it ends (exclusive), and its quote.</summary>
    /// <remarks>A <c>'</c> straight after a digit is C++14's digit separator - <c>1'000'000</c> - and not a quote.</remarks>
    public static List<(int Start, int End, char Quote)> Literals(string line)
    {
        var found = new List<(int, int, char)>();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] is '/' or '*') break;
            if (c is not ('"' or '\'')) continue;
            if (c == '\'' && i > 0 && char.IsLetterOrDigit(line[i - 1])) continue;

            var start = i;
            for (i++; i < line.Length && line[i] != c; i++)
                if (line[i] == '\\') i++;

            if (i >= line.Length) break;
            found.Add((start, i + 1, c));
        }

        return found;
    }

    /// <summary>The pieces of a masked stretch split on a character that is not inside brackets.</summary>
    public static List<(int Start, int End)> SplitTopLevel(string masked, int from, int to, char separator)
    {
        var parts = new List<(int, int)>();
        var depth = 0;
        var start = from;

        for (var i = from; i < to; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (masked[i] == separator && depth == 0)
            {
                // `++` and `+=` are not a join.
                if (separator == '+' && ((i + 1 < to && masked[i + 1] is '+' or '=') || (i > from && masked[i - 1] == '+'))) continue;

                parts.Add((start, i));
                start = i + 1;
            }
        }

        parts.Add((start, to));
        return parts;
    }
}

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
        if (Cpp.Undeclared(context.Error) is not { } reported || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Cpp.UsesStd(source) || !InStd(source, reported)) return null;

        var names = context.AllErrors
            .Where(e => LocalFixContext.OwnFrame(e) is { Line: { } l } && l == number && Msvc.SameFile(context, e, source))
            .Select(Cpp.Undeclared)
            .OfType<string>()
            .Prepend(reported)
            .Where(name => InStd(source, name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var masked = CodeText.Mask(line, Syntax.CLike);
        var hits = names
            .SelectMany(name => Cpp.Unqualified(masked, name).Select(index => (Index: index, Name: name)))
            .OrderByDescending(hit => hit.Index)
            .ToList();

        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var (index, _) in hits) corrected = corrected[..index] + "std::" + corrected[index..];

        var written = names.Where(name => hits.Any(hit => hit.Name == name)).ToList();

        return LocalFix.ReplaceLine(
            Id, $"Write {Cpp.Join(written.Select(name => "std::" + name))}",
            $"{Cpp.Join(written.Select(name => $"`{name}`"))} {(written.Count == 1 ? "is" : "are")} part of the C++ standard library, which keeps " +
            $"its names inside the namespace `std` - so they are written {Cpp.Join(written.Select(name => $"`std::{name}`"))}. A file can say " +
            "`using namespace std;` once instead, but that brings in every name the library has.",
            source.Path, number, corrected);
    }

    /// <summary>A standard C++ name whose header this file has, and that is not a C function under the same name.</summary>
    private static bool InStd(SourceFile source, string name) =>
        CStandardLibrary.CppHeaderOf(name) is { } header && CStandardLibrary.CHeaderOf(name) is null &&
        (CCode.Includes(source, header) || (header == "string" && Cpp.HasStdString(source)));
}

/// <summary><c>std::endll</c> - a misspelt standard name, which MSVC reports as not a member of <c>std</c> and never corrects.</summary>
public sealed partial class CppStdNameTypo : ILocalFixRule
{
    public string Id => "cpp-std-name-typo";

    [GeneratedRegex(@"^'(?<name>\w+)': is not a member of 'std'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C2039", MsvcMessage()) is not { } message || Cpp.Locate(context) is not { } at) return null;

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

/// <summary><c>System.out.println</c>, <c>Console.WriteLine</c> and <c>print</c> in C++, which prints with <c>std::cout</c>.</summary>
public sealed partial class CppForeignPrint : ILocalFixRule
{
    public string Id => "cpp-foreign-print";

    [GeneratedRegex(@"^(?<lead>\s*)(?:System\s*\.\s*out\s*\.\s*(?<call>println|print)|Console\s*\.\s*(?<call>WriteLine|Write)|(?<call>print))\s*\((?<args>.*)\)\s*;(?<tail>.*)$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Cpp.Undeclared(context.Error) is not ("System" or "Console" or "print") || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (!CCode.Includes(source, "iostream")) return null;

        var masked = CodeText.Mask(line, Syntax.CLike);
        if (Statement().Match(masked) is not { Success: true } statement) return null;

        var call = statement.Groups["call"].Value;
        var (language, newline) = call switch
        {
            "println" => ("Java", true),
            "print" when masked.Contains("System", StringComparison.Ordinal) => ("Java", false),
            "WriteLine" => ("C#", true),
            "Write" => ("C#", false),
            _ => ("Python", true),
        };

        var args = statement.Groups["args"];
        var pieces = new List<string>();

        if (masked[args.Index..(args.Index + args.Length)].Trim().Length > 0)
        {
            var arguments = Cpp.SplitTopLevel(masked, args.Index, args.Index + args.Length, ',');

            // Python's print separates its arguments with a space. Anywhere else a comma means a format string.
            if (arguments.Count > 1 && language != "Python") return null;

            foreach (var (start, end) in arguments)
            {
                if (arguments.Count > 1 && pieces.Count > 0) pieces.Add("\" \"");

                var joined = Cpp.SplitTopLevel(masked, start, end, '+');
                var parts = joined.Select(p => line[p.Start..p.End].Trim()).ToList();

                if (parts.Any(part => part.Length == 0 || part.StartsWith("$\"", StringComparison.Ordinal) || part.StartsWith("@\"", StringComparison.Ordinal)))
                    return null;

                // "Total: " + total joins text in Java and C#; a sum with no text in it is still a sum.
                if (parts.Count > 1 && parts.Any(part => part.StartsWith('"'))) pieces.AddRange(parts);
                else pieces.Add(line[start..end].Trim());
            }
        }

        if (pieces.Count == 0 && !newline) return null;

        var cout = Cpp.Std(source, "cout");
        var endl = Cpp.Std(source, "endl");
        var printed = string.Join(" << ", pieces.Prepend(cout).Concat(newline ? [endl] : []));

        return LocalFix.ReplaceLine(
            Id, $"Print with {cout}",
            $"`{(language == "Python" ? "print" : line.Trim().Split('(')[0])}` is {language}. C++ prints by sending each value to `{cout}` with `<<`" +
            (newline ? $", and `{endl}` ends the line." : "."),
            source.Path, number, $"{statement.Groups["lead"].Value}{printed};{line[statement.Groups["tail"].Index..]}");
    }
}

/// <summary><c>null</c>, <c>True</c>, <c>boolean</c>, <c>String</c> - words from other languages that C++ spells differently.</summary>
public sealed class CppForeignWord : ILocalFixRule
{
    public string Id => "cpp-foreign-word";

    private static readonly Dictionary<string, (string Right, string From)> Words = new(StringComparer.Ordinal)
    {
        ["null"] = ("nullptr", "Java, C# and JavaScript"),
        ["None"] = ("nullptr", "Python"),
        ["True"] = ("true", "Python"),
        ["False"] = ("false", "Python"),
        ["boolean"] = ("bool", "Java"),
        ["String"] = ("string", "Java and C#"),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (Cpp.Undeclared(context.Error) is not { } word || !Words.TryGetValue(word, out var entry)) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var right = entry.Right;

        if (right == "string")
        {
            if (!Cpp.HasStdString(source)) return null;
            right = Cpp.Std(source, "string");
        }

        var hits = Cpp.Unqualified(CodeText.Mask(line, Syntax.CLike), word);
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + word.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Write {word} as {right}",
            $"`{word}` is {entry.From}. In C++ it is `{right}`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>std::cout &gt;&gt; total</c> and <c>std::cin &lt;&lt; age</c> - the stream's arrows pointing the wrong way.</summary>
public sealed partial class CppStreamArrows : ILocalFixRule
{
    public string Id => "cpp-stream-arrows";

    [GeneratedRegex(@"^no match for 'operator(?<op>>>|<<)' \(operand types are 'std::(?<stream>ostream|istream)'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary '(?<op>>>|<<)': no operator found which takes a left-hand operand of type 'std::(?<stream>ostream|istream)'")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<![\w:])(?:std\s*::\s*)?(?<name>cout|cerr|clog|cin)\b")]
    private static partial Regex Stream();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2678", MsvcMessage())) is not { } message) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var output = message.Groups["stream"].Value == "ostream";
        var (wrong, right) = output ? (">>", "<<") : ("<<", ">>");
        if (message.Groups["op"].Value != wrong) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);

        var streams = Stream().Matches(masked).Where(m => (m.Groups["name"].Value == "cin") != output).ToList();
        if (streams is not [var stream]) return null;

        var from = stream.Index + stream.Length;
        var end = masked.IndexOf(';', from);
        if (end < 0) end = masked.Length;

        var arrows = new List<int>();
        var depth = 0;

        for (var i = from; i + 1 < end; i++)
        {
            if (masked[i] is '(' or '[' or '{') depth++;
            else if (masked[i] is ')' or ']' or '}') depth--;
            else if (depth == 0 && masked.AsSpan(i, 2).SequenceEqual(wrong) && (i + 2 >= end || masked[i + 2] is not ('<' or '>' or '=')))
            {
                arrows.Add(i);
                i++;
            }
        }

        if (arrows.Count == 0) return null;

        var corrected = line;
        foreach (var index in Enumerable.Reverse(arrows)) corrected = corrected[..index] + right + corrected[(index + 2)..];

        return LocalFix.ReplaceLine(
            Id, output ? $"Print with << into {stream.Value}" : $"Read with >> from {stream.Value}",
            "The arrows point the way the data goes: `<<` sends a value into an output stream like `std::cout`, and `>>` takes one out of " +
            $"an input stream like `std::cin`. `{stream.Value} {wrong}` points the wrong way.",
            source.Path, number, corrected);
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
        if (Cpp.Locate(context) is not { } at) return null;

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
        if (Cpp.Locate(context) is not { } at) return null;

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

/// <summary><c>Dog d = new Dog();</c> - how Java and C# make an object, which in C++ hands back a pointer.</summary>
public sealed partial class CppNewWithoutPointer : ILocalFixRule
{
    public string Id => "cpp-new-without-pointer";

    [GeneratedRegex(@"^conversion from '(?<from>[\w:]+) ?\*' to non-scalar type '(?<to>[\w:]+)' requested$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'initializing': cannot convert from '(?<from>[\w:]+) \*' to '(?<to>[\w:]+)'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2440", MsvcMessage())) is not { } message) return null;
        if (message.Groups["from"].Value != message.Groups["to"].Value || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var type = Regex.Escape(message.Groups["to"].Value.Split("::")[^1]);

        var statement = Regex.Match(line,
            $@"^(?<lead>\s*)(?<type>(?:[\w]+::)*{type})\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?:[\w]+::)*{type}\s*(?:\((?<args>[^;]*)\)|\{{(?<brace>[^;]*)\}})?\s*;(?<tail>.*)$");

        if (!statement.Success) return null;

        var name = statement.Groups["name"].Value;
        var declared = statement.Groups["args"].Value.Trim() is { Length: > 0 } args
            ? $"{name}({args})"
            : statement.Groups["brace"].Value.Trim() is { Length: > 0 } brace ? $"{name}{{{brace}}}" : name;

        var corrected = $"{statement.Groups["lead"].Value}{statement.Groups["type"].Value} {declared};{statement.Groups["tail"].Value}";

        return LocalFix.ReplaceLine(
            Id, $"Declare {name} without new",
            "`new` builds an object somewhere else and hands back a pointer to it - which is how Java and C# make every object. In C++ an " +
            $"object can simply be a variable: `{corrected.Trim()}` makes one, and it is cleaned up by itself when `{name}` goes out of scope.",
            source.Path, number, corrected);
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
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if ((CCode.GccMessage(error, GccArrow()) ?? CCode.MsvcMessage(error, "C2819", MsvcArrow())) is { } arrow)
        {
            var type = Regex.Replace(arrow.Groups["type"].Value, @"^(?:struct|class)\s+|\s*const\s*|\s*&$", "");

            var hits = Regex.Matches(code, @"(?<![\w.>])(?<object>[A-Za-z_]\w*)\s*(?<op>->)")
                .Where(m => Cpp.Declaration(masked, number - 1, m.Groups["object"].Value) is { Pointer: false } d && d.Type.Split("::")[^1] == type.Split("::")[^1])
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
                    return name == "this" || Cpp.Declaration(masked, number - 1, name) is { Pointer: true };
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
        if (Cpp.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = CCode.DepthAtStart(masked);
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

/// <summary><c>void main()</c> - gcc's <c>'::main' must return 'int'</c>.</summary>
public sealed partial class CppMainReturnsInt : ILocalFixRule
{
    public string Id => "cpp-main-returns-int";

    [GeneratedRegex(@"^'::main' must return 'int'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)void(?<rest>\s+main\s*\()")]
    private static partial Regex VoidMain();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.GccMessage(context.Error, GccMessage()) is null || Cpp.Locate(context) is not { } at) return null;
        if (VoidMain().Match(at.Line) is not { Success: true } main) return null;

        return LocalFix.ReplaceLine(
            Id, "Declare main as int main",
            "`main` gives back an `int` - the exit code, 0 for success - and returns it without a `return` at the end. MSVC lets " +
            "`void main` through, but the C++ standard and gcc do not.",
            at.Source.Path, at.Number, main.Groups["lead"].Value + "int" + at.Line[(main.Groups["rest"].Index)..]);
    }
}

/// <summary><c>char name = "Ada";</c> - a single character given a whole string.</summary>
public sealed partial class CppCharForString : ILocalFixRule
{
    public string Id => "cpp-char-for-string";

    [GeneratedRegex(@"^invalid conversion from 'const char\*' to 'char'")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'initializing': cannot convert from 'const char \[\d+\]' to 'char'$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?:const\s+)?char\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>""(?:[^""\\]|\\.)*"")\s*;(?<tail>.*)$")]
    private static partial Regex Declaration();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2440", MsvcMessage())) is null) return null;
        if (Cpp.Locate(context) is not { } at || !Cpp.HasStdString(at.Source)) return null;

        var (source, number, line) = at;
        if (Declaration().Match(line) is not { Success: true } declaration) return null;

        var type = Cpp.Std(source, "string");
        var name = declaration.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Declare {name} as {type}",
            $"`char` holds a single character, like `'A'`, and {declaration.Groups["value"].Value} is a whole string. Text in C++ is a `{type}`.",
            source.Path, number,
            $"{declaration.Groups["lead"].Value}{type} {name} = {declaration.Groups["value"].Value};{declaration.Groups["tail"].Value}");
    }
}

/// <summary><c>std::string name = 'Ada';</c> - text in single quotes, which C++ reads as one number.</summary>
public sealed partial class CppMultiCharString : ILocalFixRule
{
    public string Id => "cpp-multichar-string";

    public LocalFix? Propose(LocalFixContext context)
    {
        var message = context.Error.Message ?? "";
        if (!message.Contains("'int'", StringComparison.Ordinal) || !message.Contains("string", StringComparison.Ordinal)) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;

        var multi = Cpp.Literals(line)
            .Where(l => l.Quote == '\'' && Regex.Replace(line[(l.Start + 1)..(l.End - 1)], @"\\.", "x").Length > 1)
            .ToList();

        if (multi.Count == 0) return null;

        var corrected = line;
        foreach (var (start, end, _) in Enumerable.Reverse(multi))
            corrected = corrected[..start] + "\"" + line[(start + 1)..(end - 1)].Replace("\"", "\\\"") + "\"" + corrected[end..];

        var first = line[multi[0].Start..multi[0].End];

        return LocalFix.ReplaceLine(
            Id, "Put the text in double quotes",
            $"Single quotes hold one character, like `'A'`. Text longer than that goes in double quotes. C++ reads {first} as a single number " +
            "made from the characters' codes, which is why the error talks about an `int`.",
            source.Path, number, corrected);
    }
}

/// <summary><c>std::vector&lt;int&gt; values();</c> - empty brackets that declare a function instead of a variable.</summary>
public sealed partial class CppVexingParse : ILocalFixRule
{
    public string Id => "cpp-vexing-parse";

    [GeneratedRegex(@"^request for member '(?<member>\w+)' in '(?<name>\w+)', which is of non-class type '[^']*\(\)'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^left of '\.(?<member>\w+)' must have class/struct/union$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2228", MsvcMessage())) is not { } message) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var names = message.Groups["name"].Success
            ? [message.Groups["name"].Value]
            : Regex.Matches(masked[number - 1], $@"(?<![\w.>])(?<name>[A-Za-z_]\w*)\s*\.\s*{Regex.Escape(message.Groups["member"].Value)}\b")
                .Select(m => m.Groups["name"].Value).Distinct().ToList();

        if (names is not [var name]) return null;

        var (first, _) = CCode.EnclosingFunction(masked, number - 1);
        var declaration = new Regex($@"^(?<lead>\s*)(?<type>(?!return\b)[A-Za-z_][\w:]*(?:\s*<[^;()]*>)?)\s+{Regex.Escape(name)}\s*(?<brackets>\(\s*\))\s*;\s*$");

        for (var i = number - 2; i > first; i--)
        {
            if (declaration.Match(masked[i]) is not { Success: true } found) continue;

            var brackets = found.Groups["brackets"];
            var original = source.Lines[i];

            return LocalFix.ReplaceLine(
                Id, $"Remove the empty brackets after {name}",
                $"With empty brackets, `{found.Groups["type"].Value} {name}();` does not make a variable - C++ reads it as declaring a " +
                $"function called `{name}` that returns a {found.Groups["type"].Value}. Without the brackets it is the variable that was meant. " +
                "(This is C++'s \"most vexing parse\".)",
                source.Path, i + 1, original[..brackets.Index].TrimEnd() + original[(brackets.Index + brackets.Length)..]);
        }

        return null;
    }
}

/// <summary><c>"Hello, " + "world"</c> - two string literals, which are character arrays and cannot be added.</summary>
public sealed partial class CppLiteralConcatenation : ILocalFixRule
{
    public string Id => "cpp-literal-concatenation";

    [GeneratedRegex(@"^invalid operands of types 'const char ?\[\d+\]' and 'const char ?\[\d+\]' to binary 'operator\+'$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'\+': cannot add two pointers$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2110", MsvcMessage())) is null) return null;
        if (Cpp.Locate(context) is not { } at || !Cpp.HasStdString(at.Source)) return null;

        var (source, number, line) = at;
        var literals = Cpp.Literals(line).Where(l => l.Quote == '"').ToList();

        var starts = new List<int>();

        for (var k = 0; k + 1 < literals.Count; k++)
        {
            if (line[literals[k].End..literals[k + 1].Start].Trim() != "+") continue;
            if (k > 0 && line[literals[k - 1].End..literals[k].Start].Trim() == "+") continue;

            // Something already added in front - a std::string - makes this pair legal.
            if (line[..literals[k].Start].TrimEnd().EndsWith('+')) continue;

            starts.Add(k);
        }

        if (starts is not [var only]) return null;

        var (start, end, _) = literals[only];
        var type = Cpp.Std(source, "string");

        return LocalFix.ReplaceLine(
            Id, $"Make the first piece a {type}",
            $"{line[start..end]} and the text after it are not `{type}`s but arrays of characters, and C++ cannot add two arrays. " +
            $"Making the first one a `{type}` lets `+` join them: a `{type}` plus text is a `{type}`.",
            source.Path, number, line[..start] + $"{type}({line[start..end]})" + line[end..]);
    }
}

/// <summary><c>text + age</c> with <c>age</c> an <c>int</c> - a number added to a string.</summary>
public sealed partial class CppStringPlusNumber : ILocalFixRule
{
    public string Id => "cpp-string-plus-number";

    [GeneratedRegex(@"^no match for 'operator\+' \(operand types are '(?<left>[^']+)'(?: \{aka '[^']+'\})? and '(?<right>[^']+)'(?: \{aka '[^']+'\})?\)$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary '\+': 'std::(?:string|basic_string<[^']*>)' does not define this operator")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?:(?:unsigned|signed|long|short)\s+)*(?:int|long|short|double|float|size_t|std::size_t|unsigned|int\d+_t|uint\d+_t)$")]
    private static partial Regex Numeric();

    [GeneratedRegex(@"string")]
    private static partial Regex Text();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (CCode.GccMessage(error, GccMessage()) is { } gcc)
        {
            var (left, right) = (gcc.Groups["left"].Value, gcc.Groups["right"].Value);
            if (!(Text().IsMatch(left) && Numeric().IsMatch(right)) && !(Text().IsMatch(right) && Numeric().IsMatch(left))) return null;
        }
        else if (CCode.MsvcMessage(error, "C2676", MsvcMessage()) is null)
        {
            return null;
        }

        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        var operands = Regex.Matches(code, @"(?<=\+\s*)(?<name>[A-Za-z_]\w*)\b(?!\s*[(\[.:])|(?<![\w.>:])(?<name>[A-Za-z_]\w*)(?=\s*\+(?![+=]))")
            .Where(m => !code[..m.Index].TrimEnd().EndsWith("++", StringComparison.Ordinal))
            .Where(m => Cpp.Declaration(masked, number - 1, m.Groups["name"].Value) is { Pointer: false, Reference: false } d && Numeric().IsMatch(d.Type))
            .GroupBy(m => m.Index)
            .Select(g => g.First())
            .ToList();

        if (operands is not [var operand]) return null;

        var name = operand.Groups["name"];
        var call = Cpp.Std(source, "to_string");

        return LocalFix.ReplaceLine(
            Id, $"Turn {name.Value} into text with {call}",
            $"C++ does not turn a number into text by adding it to a string - `+` between a string and `{name.Value}` has no meaning. " +
            $"`{call}({name.Value})` makes the text first, and text can be added to text.",
            source.Path, number, line[..name.Index] + $"{call}({name.Value})" + line[(name.Index + name.Length)..]);
    }
}

/// <summary>
/// libstdc++'s <c>vector::_M_range_check: __n (which is 3) &gt;= this-&gt;size() (which is 3)</c> - an <c>.at(i)</c> in a loop that
/// runs to <c>&lt;= size()</c>.
/// </summary>
/// <remarks>
/// An exception nobody caught prints no stack, so there is no line to start from. The fix is offered only when the index
/// asked for is exactly the size - one past the end - and the program has exactly one loop that reads <c>.at(i)</c> up to
/// and including <c>.size()</c>. Anything less definite is a guess about which loop it was.
/// </remarks>
public sealed partial class CppAtOutOfRange : ILocalFixRule
{
    public string Id => "cpp-at-out-of-range";

    [GeneratedRegex(@"^(?:vector::_M_range_check|basic_string::at): __n \(which is (?<n>\d+)\) >= this->size\(\) \(which is (?<size>\d+)\)")]
    private static partial Regex Message();

    [GeneratedRegex(@"\bfor\s*\(\s*(?:[\w:]+\s+)?(?<var>[A-Za-z_]\w*)\s*=\s*0\s*;\s*\k<var>\s*(?<op><=)\s*(?<object>[A-Za-z_]\w*)\s*\.\s*(?:size|length)\s*\(\s*\)\s*;")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "std::out_of_range" } error) return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message || message.Groups["n"].Value != message.Groups["size"].Value) return null;
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) return null;

        var found = new List<(SourceFile Source, int Line, Match Loop)>();

        foreach (var path in Directory.EnumerateFiles(root).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c++").Take(200))
        {
            if (SourceFile.Read(path) is not { } source) continue;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

            for (var i = 0; i < masked.Count; i++)
            {
                if (Loop().Match(masked[i]) is not { Success: true } loop) continue;

                var use = new Regex($@"\b{Regex.Escape(loop.Groups["object"].Value)}\s*\.\s*at\s*\(\s*{Regex.Escape(loop.Groups["var"].Value)}\s*\)");
                if (Body(masked, i, loop.Index + loop.Length).Any(k => use.IsMatch(masked[k]))) found.Add((source, i, loop));
            }
        }

        if (found is not [var (file, index, only)]) return null;

        var op = only.Groups["op"];
        var original = file.Lines[index];
        var vector = only.Groups["object"].Value;
        var size = message.Groups["size"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Stop the loop before {vector}.size(): <",
            $"`{vector}.at({only.Groups["var"].Value})` checks the index and throws `std::out_of_range` when it is past the end. `{vector}` had " +
            $"{size} elements, numbered 0 to {int.Parse(size) - 1}, and the loop on line {index + 1} runs while `{only.Groups["var"].Value} <= " +
            $"{vector}.size()` - so it asked for element {size}. The exception came with no line number; this is the only loop in the program " +
            "that reads `.at()` up to and including `.size()`.",
            file.Path, index + 1, original[..op.Index] + "<" + original[(op.Index + op.Length)..]);
    }

    /// <summary>The lines a for loop's body covers, from the end of its header.</summary>
    private static IEnumerable<int> Body(IReadOnlyList<string> masked, int line, int after)
    {
        var rest = masked[line][after..];
        var brace = rest.IndexOf('{');

        if (brace < 0)
        {
            yield return rest.Trim().TrimEnd(')').Trim().Length > 0 ? line : Math.Min(line + 1, masked.Count - 1);
            yield break;
        }

        var depth = 0;

        for (var i = line; i < masked.Count; i++)
        {
            yield return i;

            for (var c = i == line ? after + brace : 0; c < masked[i].Length; c++)
            {
                if (masked[i][c] == '{') depth++;
                else if (masked[i][c] == '}' && --depth == 0) yield break;
            }
        }
    }
}

/// <summary><c>add(int a, int b) { ... }</c> with no return type - MSVC's <c>C4430 missing type specifier - int assumed</c>.</summary>
/// <remarks>gcc accepts it with a warning, the way C once did, and the program runs; the C++ standard does not.</remarks>
public sealed partial class CppMissingReturnType : ILocalFixRule
{
    public string Id => "cpp-missing-return-type";

    [GeneratedRegex(@"^missing type specifier - int assumed")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^()]*)\)\s*(?:const\s*)?\{?\s*$")]
    private static partial Regex Header();

    [GeneratedRegex(@"\breturn\b\s*(?<value>[^;]*);")]
    private static partial Regex Return();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C4430", MsvcMessage()) is null || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (CCode.DepthAtStart(masked)[number - 1] != 0 || Header().Match(masked[number - 1]) is not { Success: true } header) return null;
        if (number >= masked.Count) return null;

        var name = header.Groups["name"].Value;
        if (CStandardLibrary.Keywords.Contains(name)) return null;

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in header.Groups["parameters"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = Regex.Match(parameter, @"^(?:const\s+)?(?<type>[A-Za-z_][\w:]*(?:\s*<[^>]*>)?)\s*[&*]?\s*(?<name>[A-Za-z_]\w*)$");
            if (parts.Success) parameters[parts.Groups["name"].Value] = parts.Groups["type"].Value;
        }

        var (_, end) = CCode.EnclosingFunction(masked, number);
        var returned = Enumerable.Range(number - 1, end - number + 2)
            .SelectMany(i => Return().Matches(masked[i]).Select(m => source.Lines[i].Substring(m.Groups["value"].Index, m.Groups["value"].Length).Trim()))
            .ToList();

        var types = returned.All(value => value.Length == 0)
            ? ["void"]
            : returned.Select(value => TypeOf(value, parameters, source)).Distinct().ToList();

        if (types is not [{ } type]) return null;

        var column = header.Groups["name"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Give {name} the return type {type}",
            "C++ needs every function to say what it returns - it will not assume `int` the way old C did. " +
            (type == "void" ? $"`{name}` returns nothing, which is written `void`." : $"Everything `{name}` returns is {(type == "int" ? "an" : "a")} `{type}`."),
            source.Path, number, line[..column] + type + " " + line[column..]);
    }

    private static string? TypeOf(string value, Dictionary<string, string> parameters, SourceFile source)
    {
        if (value is "true" or "false") return "bool";
        if (Regex.IsMatch(value, @"^\d+$")) return "int";
        if (Regex.IsMatch(value, @"^\d+\.\d*(?:[eE][-+]?\d+)?$")) return "double";
        if (Regex.IsMatch(value, @"^""(?:[^""\\]|\\.)*""$")) return Cpp.HasStdString(source) ? Cpp.Std(source, "string") : null;

        // Parameters of one type, combined with arithmetic, give that type back.
        if (!Regex.IsMatch(value, @"^[\w\s+\-*/%()]+$")) return null;

        var words = Regex.Matches(value, @"[A-Za-z_]\w*").Select(m => m.Value).ToList();
        if (words.Count == 0 || words.Any(word => !parameters.ContainsKey(word))) return null;

        var types = words.Select(word => parameters[word]).Distinct().ToList();
        return types is [var only] && only is "int" or "long" or "double" or "float" or "short" ? only : null;
    }
}
