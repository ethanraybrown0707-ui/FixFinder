using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Reading C++ classes: where one is defined, what it inherits, and which lines are its members.</summary>
internal static partial class CppClass
{
    [GeneratedRegex(@"^'(?:[^']*?\s)?(?<class>\w+)::(?<member>~?\w+)\([^']*' marked 'override', but does not override$")]
    private static partial Regex GccOverride();

    [GeneratedRegex(@"^'(?<class>\w+)::(?<member>~?\w+)': method with override specifier 'override' did not override any base class methods$")]
    private static partial Regex MsvcOverride();

    [GeneratedRegex(@"^\s*(?:class|struct)\s+\w+\s*(?:final\s*)?:(?!:)(?<bases>[^{]*)")]
    private static partial Regex BaseList();

    [GeneratedRegex(@"^\s*(?<access>(?:(?:public|protected|private|virtual)\s+)*)(?<name>[A-Za-z_][\w:]*)")]
    private static partial Regex Base();

    /// <summary>An <c>override</c> that overrides nothing, as gcc and MSVC say it.</summary>
    public static Match? Override(ParsedError error) =>
        CCode.GccMessage(error, GccOverride()) ?? CCode.MsvcMessage(error, "C3668", MsvcOverride());

    /// <summary>The top-level line defining a class or struct of that name, or -1 when there is not exactly one.</summary>
    public static int Header(IReadOnlyList<string> masked, string name)
    {
        var depths = CCode.DepthAtStart(masked);
        var pattern = new Regex($@"^\s*(?:class|struct)\s+{Regex.Escape(name)}\b(?!\s*;)");
        var found = Enumerable.Range(0, masked.Count).Where(i => depths[i] == 0 && pattern.IsMatch(masked[i])).ToList();

        return found is [var only] ? only : -1;
    }

    /// <summary>The lines holding a class's opening and closing braces.</summary>
    public static (int Open, int Close)? Body(IReadOnlyList<string> masked, int header)
    {
        var depth = 0;
        var open = -1;

        for (var i = header; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{')
                {
                    if (depth++ == 0) open = i;
                }
                else if (c == '}' && --depth == 0 && open >= 0)
                {
                    return (open, i);
                }
            }

            if (open < 0 && i > header + 2) return null;
        }

        return null;
    }

    /// <summary>The lines directly inside a class's braces - its members, not what their bodies hold.</summary>
    public static List<int> Members(IReadOnlyList<string> masked, int header)
    {
        if (Body(masked, header) is not { } body) return [];

        var depths = CCode.DepthAtStart(masked);
        var level = depths[body.Open] + 1;

        return Enumerable.Range(body.Open + 1, Math.Max(0, body.Close - body.Open - 1)).Where(i => depths[i] == level).ToList();
    }

    /// <summary>The member lines that declare or define a function of that name.</summary>
    public static List<int> Declaring(IReadOnlyList<string> masked, int header, string member) =>
        Members(masked, header).Where(i => Regex.IsMatch(masked[i], $@"(?<![\w:~.>]){Regex.Escape(member)}\s*\(")).ToList();

    /// <summary>What a class inherits from: each base's name, where it starts on the line, and whether an access is written.</summary>
    public static List<(string Name, int Column, bool Specified)> Bases(string maskedHeader)
    {
        if (BaseList().Match(maskedHeader) is not { Success: true } list) return [];

        var group = list.Groups["bases"];
        var bases = new List<(string, int, bool)>();

        foreach (var (start, end) in Cpp.SplitTopLevel(maskedHeader, group.Index, group.Index + group.Length, ','))
        {
            if (Base().Match(maskedHeader[start..end]) is not { Success: true } one) continue;

            var access = one.Groups["access"].Value;
            bases.Add((one.Groups["name"].Value.Split("::")[^1], start + one.Groups["name"].Index,
                Regex.IsMatch(access, @"\b(?:public|protected|private)\b")));
        }

        return bases;
    }

    /// <summary>The C++ files at the top of the source root, for errors that name no line: the linker's, an uncaught exception's.</summary>
    public static IEnumerable<SourceFile> Files(LocalFixContext context)
    {
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c++").Take(200))
            if (SourceFile.Read(path) is { } source) yield return source;
    }
}

/// <summary><c>override</c> of a base class function that is not <c>virtual</c>.</summary>
public sealed class CppVirtualBase : ILocalFixRule
{
    public string Id => "cpp-virtual-base";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppClass.Override(context.Error) is not { } message || Cpp.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var derived = message.Groups["class"].Value;
        var member = message.Groups["member"].Value;

        var header = CppClass.Header(masked, derived);
        if (header < 0) return null;

        var plain = new List<(string Base, int Line)>();

        foreach (var (name, _, _) in CppClass.Bases(masked[header]))
        {
            var baseHeader = CppClass.Header(masked, name);
            if (baseHeader < 0) continue;

            foreach (var i in CppClass.Declaring(masked, baseHeader, member))
            {
                // A virtual one of that name means the signatures differ, and virtual is not the fix for that.
                if (Regex.IsMatch(masked[i], @"\bvirtual\b")) return null;
                if (!Regex.IsMatch(masked[i], @"\bstatic\b")) plain.Add((name, i));
            }
        }

        if (plain is not [var (baseName, line)]) return null;

        var original = source.Lines[line];
        var indent = CodeText.Indentation(original).Length;

        return LocalFix.ReplaceLine(
            Id, $"Mark {baseName}::{member} virtual",
            $"`override` can only replace a function the base class allows to be replaced - one declared `virtual`. `{baseName}::{member}` " +
            $"is not, so there was nothing to override. (Without `virtual`, a `{derived}` used through a `{baseName}&` or `{baseName}*` would " +
            $"still run `{baseName}`'s version.)",
            source.Path, line + 1, original[..indent] + "virtual " + original[indent..]);
    }
}

/// <summary><c>override</c> on a misspelt name - <c>speek</c> for the inherited <c>speak</c>.</summary>
public sealed partial class CppOverrideTypo : ILocalFixRule
{
    public string Id => "cpp-override-typo";

    [GeneratedRegex(@"\bvirtual\b[^(]*?(?<name>~?[A-Za-z_]\w*)\s*\(")]
    private static partial Regex Virtual();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppClass.Override(context.Error) is not { } message || Cpp.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var member = message.Groups["member"].Value;

        var header = CppClass.Header(masked, message.Groups["class"].Value);
        if (header < 0) return null;

        var candidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, _, _) in CppClass.Bases(masked[header]))
        {
            var baseHeader = CppClass.Header(masked, name);
            if (baseHeader < 0) continue;

            foreach (var i in CppClass.Members(masked, baseHeader))
                if (Virtual().Match(masked[i]) is { Success: true } m) candidates.Add(m.Groups["name"].Value);
        }

        if (candidates.Contains(member) || CodeText.Nearest(member, candidates) is not { } right) return null;
        if (CodeText.ReplaceWord(at.Line, member, right, Syntax.CLike, context.Frame?.Column) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id, $"Rename {member} to {right}",
            $"`override` says `{member}` replaces a virtual function it inherits, and nothing it inherits is called `{member}`. The inherited " +
            $"one within a letter or two of it is `{right}`.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>class Dog : Animal</c> - a class inherits privately unless it says <c>public</c>.</summary>
public sealed partial class CppPrivateInheritance : ILocalFixRule
{
    public string Id => "cpp-private-inheritance";

    [GeneratedRegex(@"^'(?<base>\w+)' is not an accessible base of '(?<derived>\w+)'$")]
    private static partial Regex GccBase();

    [GeneratedRegex(@"^'(?:[^']*?\s)?(?<base>\w+)::(?<member>\w+)[^']*' is inaccessible within this context$")]
    private static partial Regex GccMember();

    [GeneratedRegex(@"^'(?<base>\w+)::(?<member>\w+)' not accessible because '(?<derived>\w+)' uses 'private' to inherit from '\k<base>'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.GccMessage(error, GccBase()) ?? CCode.GccMessage(error, GccMember()) ?? CCode.MsvcMessage(error, "C2247", MsvcMessage());

        if (message is null || Cpp.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = CCode.DepthAtStart(masked);
        var baseName = message.Groups["base"].Value;
        var derived = message.Groups["derived"].Success ? message.Groups["derived"].Value : null;

        var unspecified = new List<(int Line, int Column, string Derived)>();

        for (var i = 0; i < masked.Count; i++)
        {
            if (depths[i] != 0 || Regex.Match(masked[i], @"^\s*class\s+(?<name>\w+)") is not { Success: true } cls) continue;
            if (derived is not null && cls.Groups["name"].Value != derived) continue;

            foreach (var (name, column, specified) in CppClass.Bases(masked[i]))
                if (name == baseName && !specified) unspecified.Add((i, column, cls.Groups["name"].Value));
        }

        if (unspecified is not [var (line, col, derivedName)]) return null;

        var original = source.Lines[line];

        return LocalFix.ReplaceLine(
            Id, $"Inherit publicly: class {derivedName} : public {baseName}",
            $"A `class` inherits privately unless it says otherwise, so everything `{derivedName}` gets from `{baseName}` is private inside " +
            $"`{derivedName}` - hidden from the code using it. `public {baseName}` keeps `{baseName}`'s public members public, which is what " +
            "inheriting almost always means. (A `struct` inherits publicly by default.)",
            source.Path, line + 1, original[..col] + "public " + original[col..]);
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
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = message.Groups["name"].Value;

        if (CCode.DepthAtStart(masked)[number - 1] != 0) return null;

        var here = Regex.Matches(masked[number - 1], $@"(?<![\w:~.>]){Regex.Escape(name)}\s*\(").ToList();
        if (here is not [var definition]) return null;

        var owners = new List<string>();

        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.Match(masked[i], @"^\s*(?:class|struct)\s+(?<name>\w+)\b(?!\s*;)") is not { Success: true } cls) continue;
            if (CppClass.Header(masked, cls.Groups["name"].Value) != i) continue;

            var declared = CppClass.Declaring(masked, i, name).Any(k => masked[k].TrimEnd().EndsWith(';'));
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

/// <summary>A <c>std::unique_ptr</c> copied - <c>auto second = first;</c> or passed by value - which only moving allows.</summary>
public sealed partial class CppMoveUniquePtr : ILocalFixRule
{
    public string Id => "cpp-move-unique-ptr";

    [GeneratedRegex(@"^use of deleted function 'std::unique_ptr<[^']*>::unique_ptr\(const std::unique_ptr<[^']*>&\)")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'std::unique_ptr<[^']*>::unique_ptr\(const std::unique_ptr<[^']*> &\)': attempting to reference a deleted function$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<=(?:[=(,])\s*)(?<name>[A-Za-z_]\w*)(?=\s*[;,)])")]
    private static partial Regex Operand();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2280", MsvcMessage())) is null) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        var owners = Operand().Matches(code)
            .Where(m => Cpp.Declaration(masked, number - 1, m.Groups["name"].Value) is { Pointer: false, Reference: false } d &&
                        d.Line != number - 1 &&
                        (d.Type.Contains("unique_ptr", StringComparison.Ordinal) ||
                         (d.Type == "auto" && Regex.IsMatch(masked[d.Line], @"\b(?:make_unique|unique_ptr)\b"))))
            .ToList();

        if (owners is not [var owner]) return null;

        var name = owner.Groups["name"].Value;
        var (_, last) = CCode.EnclosingFunction(masked, number - 1);
        var used = new Regex($@"(?<![\w.>:]){Regex.Escape(name)}\b");

        // Moved from, it is empty. Anything that still uses it afterwards would read a null pointer.
        if (used.IsMatch(code[(owner.Index + owner.Length)..])) return null;
        for (var i = number; i <= last && i < masked.Count; i++)
            if (used.IsMatch(masked[i])) return null;

        var move = Cpp.Std(source, "move");

        return LocalFix.ReplaceLine(
            Id, $"Hand {name} over with {move}",
            $"A `std::unique_ptr` is the only owner of what it points to, so it cannot be copied - that would make two owners. `{move}({name})` " +
            $"hands ownership over instead, and leaves `{name}` empty - which is safe here, because nothing uses `{name}` afterwards.",
            source.Path, number, line[..owner.Index] + $"{move}({name})" + line[(owner.Index + owner.Length)..]);
    }
}

/// <summary>A member function called on a <c>const</c> object that never promised not to change it.</summary>
public sealed partial class CppConstMethod : ILocalFixRule
{
    public string Id => "cpp-const-method";

    [GeneratedRegex(@"^passing 'const (?<class>[\w:]+)' as 'this' argument discards qualifiers")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"note:\s+in call to '(?:[^']*?\s)?(?<class>\w+)::(?<member>\w+)\(")]
    private static partial Regex GccNote();

    [GeneratedRegex(@"^'(?:[^']*?\s)?(?<class>\w+)::(?<member>\w+)\([^']*\)': cannot convert 'this' pointer from 'const \k<class>' to '\k<class> &'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.MsvcMessage(error, "C2662", MsvcMessage());

        if (message is null && CCode.GccMessage(error, GccMessage()) is not null)
        {
            var index = Enumerable.Range(0, context.Output.Count).FirstOrDefault(i => context.Output[i].Sequence == error.FirstLineSequence, -1);

            for (var i = index + 1; index >= 0 && i < Math.Min(context.Output.Count, index + 8) && message is null; i++)
                if (GccNote().Match(context.Output[i].Text) is { Success: true } note) message = note;
        }

        if (message is null || Cpp.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var cls = message.Groups["class"].Value;
        var member = message.Groups["member"].Value;

        var header = CppClass.Header(masked, cls);
        if (header < 0) return null;

        // Defined outside the class as well, the const would have to go in two places.
        if (masked.Any(text => Regex.IsMatch(text, $@"\b{Regex.Escape(cls)}\s*::\s*{Regex.Escape(member)}\s*\("))) return null;

        var signature = new Regex($@"(?<![\w:~.>]){Regex.Escape(member)}\s*\([^()]*(?<close>\))(?!\s*const\b)(?=\s*(?:noexcept\s*)?(?:\{{|;|$))");
        var lines = CppClass.Members(masked, header)
            .Where(i => signature.IsMatch(masked[i]) && !Regex.IsMatch(masked[i], @"\b(?:static|virtual)\b"))
            .ToList();

        if (lines is not [var line]) return null;

        var close = signature.Match(masked[line]).Groups["close"].Index + 1;
        var original = source.Lines[line];

        return LocalFix.ReplaceLine(
            Id, $"Mark {member} as const",
            $"The object here is a `const {cls}` - whatever holds it promised not to change it - so only member functions that make the same " +
            $"promise can be called on it. `{member}` does not say so; `const` after its brackets does. (If `{member}` really does change the " +
            "object, it cannot be called on a const one at all.)",
            source.Path, line + 1, original[..close] + " const" + original[close..]);
    }
}

/// <summary>gcc's <c>need 'typename' before 'std::vector&lt;T&gt;::const_iterator' because ... is a dependent scope</c>.</summary>
public sealed partial class CppTypename : ILocalFixRule
{
    public string Id => "cpp-typename";

    [GeneratedRegex(@"^need 'typename' before '(?<name>[^']+)' because '[^']+' is a dependent scope$")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.GccMessage(context.Error, GccMessage()) is not { } message || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var tokens = Regex.Matches(name, @"\w+|[^\w\s]").Select(t => Regex.Escape(t.Value));
        var pattern = new Regex($@"(?<!\btypename\s+)(?<![\w:]){string.Join(@"\s*", tokens)}");

        var hits = pattern.Matches(CodeText.Mask(line, Syntax.CLike)).ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Add typename before {name}",
            $"Inside a template, C++ cannot tell whether `{name}` names a type or a value until it knows what the template's types are - so it " +
            "assumes a value, unless told otherwise. `typename` in front says it is a type.",
            source.Path, number, line[..hit.Index] + "typename " + line[hit.Index..]);
    }
}

/// <summary>A lambda using a local it never captured, or changing one it captured by copy.</summary>
public sealed partial class CppLambdaCapture : ILocalFixRule
{
    public string Id => "cpp-lambda-capture";

    [GeneratedRegex(@"^'(?<name>\w+)' is not captured$")]
    private static partial Regex GccNotCaptured();

    [GeneratedRegex(@"^'(?<name>\w+)' cannot be implicitly captured because no default capture mode has been specified$")]
    private static partial Regex MsvcNotCaptured();

    [GeneratedRegex(@"^(?:increment|decrement|assignment) of read-only variable '(?<name>\w+)'$")]
    private static partial Regex GccReadOnly();

    [GeneratedRegex(@"^'(?<name>\w+)': a by copy capture cannot be modified in a non-mutable lambda$")]
    private static partial Regex MsvcByCopy();

    [GeneratedRegex(@"(?<=(?:^|[=(,{;]|\breturn)\s*)(?<open>\[)(?<captures>[^\[\]]*)(?<close>\])\s*(?:\([^()]*\))?\s*(?:mutable\b\s*)?(?:->\s*[^{;]+)?\{")]
    private static partial Regex Lambda();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var missing = CCode.GccMessage(error, GccNotCaptured()) ?? CCode.MsvcMessage(error, "C3493", MsvcNotCaptured());
        var copied = missing is null ? CCode.GccMessage(error, GccReadOnly()) ?? CCode.MsvcMessage(error, "C3491", MsvcByCopy()) : null;

        if ((missing ?? copied) is not { } message || Cpp.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var name = message.Groups["name"].Value;

        for (var i = at.Number - 1; i >= Math.Max(0, at.Number - 11); i--)
        {
            if (Lambda().Matches(masked[i]).LastOrDefault() is not { } lambda) continue;

            var captures = lambda.Groups["captures"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            if (missing is not null)
            {
                if (captures.Any(c => c is "=" or "&" || c.TrimStart('&') == name)) return null;
                captures.Add("&" + name);
            }
            else
            {
                var index = captures.IndexOf(name);
                if (index >= 0) captures[index] = "&" + name;
                else if (captures.Contains("=")) captures.Add("&" + name);
                else return null;
            }

            var original = source.Lines[i];
            var open = lambda.Groups["open"].Index;
            var close = lambda.Groups["close"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Capture {name} by reference: [&{name}]",
                missing is not null
                    ? $"A lambda can only use the local variables it captures, listed in its `[ ]`. `&{name}` there lets it use `{name}` - by " +
                      "reference, so it sees the variable itself rather than a copy taken when the lambda was made."
                    : $"`[{name}]` gives the lambda its own copy of `{name}`, taken when the lambda was made, and a lambda may not change its " +
                      $"copies. `[&{name}]` captures the variable itself, so the change reaches the real `{name}`.",
                source.Path, i + 1, original[..(open + 1)] + string.Join(", ", captures) + original[close..]);
        }

        return null;
    }
}

/// <summary><c>Meters m = 5.0;</c> where <c>Meters</c>' constructor is <c>explicit</c>.</summary>
public sealed partial class CppExplicitConstructor : ILocalFixRule
{
    public string Id => "cpp-explicit-constructor";

    [GeneratedRegex(@"^conversion from '(?<from>[^'*]+)' to non-scalar type '(?<to>[\w:]+)' requested$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'initializing': cannot convert from '(?<from>[^'*]+)' to '(?<to>[\w:]+)'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2440", MsvcMessage())) is not { } message) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var type = message.Groups["to"].Value.Split("::")[^1];

        var header = CppClass.Header(masked, type);
        if (header < 0 || !CppClass.Declaring(masked, header, type).Any(i => Regex.IsMatch(masked[i], $@"\bexplicit\s+{Regex.Escape(type)}\s*\("))) return null;

        var statement = Regex.Match(line,
            $@"^(?<lead>\s*)(?<type>(?:\w+::)*{Regex.Escape(type)})\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>[^;{{]+?)\s*;(?<tail>.*)$");

        if (!statement.Success) return null;

        var name = statement.Groups["name"].Value;
        var value = statement.Groups["value"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Construct {name} directly: {type} {name}({value})",
            $"`{type}`'s constructor is `explicit`, so C++ will not turn {message.Groups["from"].Value.Trim()} into a `{type}` by itself - " +
            $"which is what `=` asks it to do. Calling the constructor, `{type} {name}({value})`, makes the conversion on purpose, and " +
            "`explicit` allows that.",
            source.Path, number,
            $"{statement.Groups["lead"].Value}{statement.Groups["type"].Value} {name}({value});{statement.Groups["tail"].Value}");
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

        foreach (var source in CppClass.Files(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var header = CppClass.Header(masked, cls);
            if (header < 0 || CppClass.Body(masked, header) is not { } body) continue;

            // Already defined somewhere is a different problem - a file left out of the build. A definition is at the top level
            // and starts with a type; `return Counter::count;` inside a function is a use.
            var depths = CCode.DepthAtStart(masked);
            var defined = new Regex($@"^\s*(?:const\s+)?[A-Za-z_][\w:<>]*[\s*&]+{Regex.Escape(cls)}\s*::\s*{Regex.Escape(member)}\s*(?:=|;|\{{)");
            if (Enumerable.Range(0, masked.Count).Any(i => depths[i] == 0 && defined.IsMatch(masked[i]))) return null;

            foreach (var i in CppClass.Members(masked, header))
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

/// <summary><c>word[0] == "a"</c> - one character compared with a string.</summary>
public sealed partial class CppCharComparedWithString : ILocalFixRule
{
    public string Id => "cpp-char-compared-with-string";

    [GeneratedRegex(@"^ISO C\+\+ forbids comparison between pointer and integer")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?:==|!=)': no conversion from 'const char \[2\]' to '(?:int|char)'$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2446", MsvcMessage())) is null) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var single = Cpp.Literals(line)
            .Where(l => l.Quote == '"' && Regex.IsMatch(line[(l.Start + 1)..(l.End - 1)], @"^(?:[^""\\]|\\.)$"))
            .Where(l => ComparedWithCharacter(line, masked, number - 1, l.Start, l.End))
            .ToList();

        if (single is not [var (start, end, _)]) return null;

        var inner = line[(start + 1)..(end - 1)];
        var character = "'" + (inner == "'" ? "\\'" : inner) + "'";

        return LocalFix.ReplaceLine(
            Id, $"Compare with the character {character}",
            $"The other side is a single character, a `char`, and {line[start..end]} is a string - C++ cannot compare the two. One character " +
            $"is written in single quotes: {character}.",
            source.Path, number, line[..start] + character + line[end..]);
    }

    private static bool ComparedWithCharacter(string line, IReadOnlyList<string> masked, int index, int start, int end)
    {
        var before = line[..start].TrimEnd();
        var after = line[end..].TrimStart();

        string other;

        if (before.EndsWith("==", StringComparison.Ordinal) || before.EndsWith("!=", StringComparison.Ordinal)) other = before[..^2].TrimEnd();
        else if (after.StartsWith("==", StringComparison.Ordinal) || after.StartsWith("!=", StringComparison.Ordinal)) other = after[2..].TrimStart();
        else return false;

        var left = other == before[..^2].TrimEnd();

        // An element read with [i], .at(i), .front() or .back() - or a variable declared char.
        if (left ? Regex.IsMatch(other, @"(?:\]|\.\s*(?:at\s*\([^()]*\)|front\s*\(\s*\)|back\s*\(\s*\)))$")
                 : Regex.IsMatch(other, @"^[A-Za-z_]\w*\s*(?:\[[^\]]*\]|\.\s*(?:at\s*\([^()]*\)|front\s*\(\s*\)|back\s*\(\s*\)))"))
            return true;

        var word = left ? Regex.Match(other, @"(?<name>[A-Za-z_]\w*)$") : Regex.Match(other, @"^(?<name>[A-Za-z_]\w*)\b");
        return word.Success && Cpp.Declaration(masked, index, word.Groups["name"].Value) is { Type: "char", Pointer: false };
    }
}

/// <summary><c>std::sort(values.begin(), values.end())</c> on a <c>std::list</c>, which has its own <c>sort</c>.</summary>
public sealed partial class CppSortList : ILocalFixRule
{
    public string Id => "cpp-sort-list";

    [GeneratedRegex(@"^no match for 'operator-' \(operand types are 'std::_List_(?:const_)?iterator<")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary '-': 'const std::_List_(?:unchecked_)?(?:const_)?iterator<")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<![\w.>])(?:std\s*::\s*)?sort\s*\(\s*(?<object>[A-Za-z_]\w*)\s*\.\s*begin\s*\(\s*\)\s*,\s*\k<object>\s*\.\s*end\s*\(\s*\)\s*\)")]
    private static partial Regex Sort();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2676", MsvcMessage())) is null) return null;

        var found = new List<(SourceFile Source, int Line, Match Call)>();

        // The error is reported from inside <algorithm>, so the call is found in the program instead.
        foreach (var source in CppClass.Files(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

            for (var i = 0; i < masked.Count; i++)
            {
                foreach (Match call in Sort().Matches(masked[i]))
                {
                    var name = Regex.Escape(call.Groups["object"].Value);
                    if (masked.Any(text => Regex.IsMatch(text, $@"\b(?:std\s*::\s*)?list\s*<[^;]*>\s*{name}\b"))) found.Add((source, i, call));
                }
            }
        }

        if (found is not [var (file, line, only)]) return null;

        var list = only.Groups["object"].Value;
        var original = file.Lines[line];

        return LocalFix.ReplaceLine(
            Id, $"Sort the list with {list}.sort()",
            "`std::sort` jumps straight to elements by position, which needs random access - a `std::vector`, a `std::deque` or an array. A " +
            $"`std::list` can only be walked one element at a time, so it has its own `sort` member that works that way: `{list}.sort()`.",
            file.Path, line + 1, original[..only.Index] + $"{list}.sort()" + original[(only.Index + only.Length)..]);
    }
}

/// <summary><c>void show(auto value)</c> - a C++20 abbreviated template, which MSVC refuses under C++17.</summary>
public sealed partial class CppAutoParameter : ILocalFixRule
{
    public string Id => "cpp-auto-parameter";

    [GeneratedRegex(@"^a parameter cannot have a type that contains 'auto'$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<![\w:])auto(?!\w)")]
    private static partial Regex Auto();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CCode.MsvcMessage(context.Error, "C3533", MsvcMessage()) is null || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if (CCode.DepthAtStart(masked)[number - 1] != 0) return null;
        if (masked.Any(text => Regex.IsMatch(text, @"(?<![\w:])T(?!\w)"))) return null;

        var open = code.IndexOf('(');
        if (open < 0 || CCode.Matching(code, open) is not { } close) return null;

        var autos = Auto().Matches(code[..close]).Where(m => m.Index > open).ToList();
        if (autos is not [var only]) return null;

        var name = Regex.Match(code[..open], @"(?<name>[A-Za-z_]\w*)\s*$").Groups["name"].Value;
        var corrected = line[..only.Index] + "T" + line[(only.Index + only.Length)..];

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Make {name} a template",
            Explanation =
                $"A parameter declared `auto` - so `{name}` takes a value of any type - arrived in C++20, and this program is built as C++17. A " +
                "template says the same thing in C++17: `template <typename T>` names the type, and the parameter uses it.",
            File = source.Path,
            StartLine = number,
            RemoveCount = 1,
            NewLines = [CodeText.Indentation(line) + "template <typename T>", corrected],
        };
    }
}

/// <summary>A default argument written again on the definition, after the declaration already gave it.</summary>
public sealed partial class CppDefaultArgumentRepeated : ILocalFixRule
{
    public string Id => "cpp-default-argument-repeated";

    [GeneratedRegex(@"^default argument given for parameter \d+ of '")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^'(?<name>\w+)': redefinition of default argument: parameter \d+$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"(?<name>[A-Za-z_][\w:]*)\s*(?<open>\()")]
    private static partial Regex Function();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if ((CCode.GccMessage(error, GccMessage()) ?? CCode.MsvcMessage(error, "C2572", MsvcMessage())) is null) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = masked[number - 1];

        if (Function().Match(code) is not { Success: true } function || CCode.Matching(code, function.Groups["open"].Index) is not { } close) return null;

        var name = function.Groups["name"].Value.Split("::")[^1];
        var declaredEarlier = Enumerable.Range(0, number - 1).Any(i =>
            Regex.IsMatch(masked[i], $@"(?<![\w.>]){Regex.Escape(name)}\s*\([^;]*=[^;]*\)\s*;"));

        if (!declaredEarlier) return null;

        var corrected = line;
        var removed = 0;

        foreach (var (start, end) in Enumerable.Reverse(Cpp.SplitTopLevel(code, function.Groups["open"].Index + 1, close, ',')))
        {
            var equals = code.IndexOf('=', start, end - start);
            if (equals < 0) continue;

            var from = equals;
            while (from > start && char.IsWhiteSpace(line[from - 1])) from--;

            var to = end;
            while (to > equals && char.IsWhiteSpace(line[to - 1])) to--;

            corrected = corrected[..from] + corrected[to..];
            removed++;
        }

        if (removed == 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Leave the default argument to {name}'s declaration",
            $"A default argument is given once, where the function is first declared - `{name}`'s declaration above already has it. The " +
            "definition repeats the parameter without the default.",
            source.Path, number, corrected);
    }
}

/// <summary>A <c>const</c> member assigned in the constructor's body instead of initialised before it.</summary>
public sealed partial class CppConstMemberInitialiser : ILocalFixRule
{
    public string Id => "cpp-const-member-initialiser";

    [GeneratedRegex(@"^uninitialized const member in '")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"'(?:[^']*?\s)?(?<class>\w+)::(?<member>\w+)' should be initialized|^assignment of read-only member '(?<class>\w+)::(?<member>\w+)'$")]
    private static partial Regex GccMember();

    [GeneratedRegex(@"^'(?<class>\w+)::(?<member>\w+)': an object of const-qualified type must be initialized$")]
    private static partial Regex MsvcMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        var message = CCode.MsvcMessage(error, "C2789", MsvcMessage());

        if (message is null && CCode.GccMessage(error, GccMessage()) is not null)
        {
            message = context.Output.Select(l => GccMember().Match(l.Text)).FirstOrDefault(m => m.Success) ??
                      context.AllErrors.Select(e => GccMember().Match(e.Message ?? "")).FirstOrDefault(m => m.Success);
        }

        if (message is null || Cpp.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var cls = Regex.Escape(message.Groups["class"].Value);
        var member = message.Groups["member"].Value;

        var header = Regex.Match(masked[number - 1], $@"^\s*(?:explicit\s+)?(?:{cls}\s*::\s*)?{cls}\s*\([^()]*(?<close>\))\s*\{{\s*$");
        if (!header.Success || number >= masked.Count) return null;

        var assignment = Regex.Match(masked[number], $@"^\s*(?:this\s*->\s*)?{Regex.Escape(member)}\s*=\s*(?<value>[^;]+?)\s*;\s*$");
        if (!assignment.Success) return null;

        var value = source.Lines[number].Substring(assignment.Groups["value"].Index, assignment.Groups["value"].Length);
        var close = header.Groups["close"].Index + 1;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Initialise {member} before the constructor's body: : {member}({value})",
            Explanation =
                $"A `const` member can never be assigned to - not even in the constructor's body, because by the time the body runs every " +
                $"member already exists. It has to be given its value as it is made, in the initialiser list: `: {member}({value})`.",
            File = source.Path,
            StartLine = number,
            RemoveCount = 2,
            NewLines = [line[..close] + $" : {member}({value}) {{"],
        };
    }
}

/// <summary>libstdc++'s <c>terminate called without an active exception</c> - a <c>std::thread</c> never joined.</summary>
public sealed partial class CppThreadNotJoined : ILocalFixRule
{
    public string Id => "cpp-thread-not-joined";

    [GeneratedRegex(@"^\s*(?:std\s*::\s*)?thread\s+(?<name>[A-Za-z_]\w*)\s*[({]")]
    private static partial Regex Thread();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "std::terminate" }) return null;

        var missing = new List<(SourceFile Source, int Line, int Before, string Name)>();

        foreach (var source in CppClass.Files(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var depths = CCode.DepthAtStart(masked);

            for (var i = 0; i < masked.Count; i++)
            {
                if (depths[i] == 0 || Thread().Match(masked[i]) is not { Success: true } thread) continue;

                var name = thread.Groups["name"].Value;
                var (_, last) = CCode.EnclosingFunction(masked, i);
                var finished = new Regex($@"\b{Regex.Escape(name)}\s*\.\s*(?:join|detach)\s*\(");

                if (Enumerable.Range(i + 1, Math.Max(0, last - i)).Any(k => finished.IsMatch(masked[k]))) continue;

                var before = Enumerable.Range(i + 1, Math.Max(0, last - i - 1))
                    .FirstOrDefault(k => depths[k] == depths[i] && Regex.IsMatch(masked[k], @"^\s*return\b"), last);

                missing.Add((source, i, before, name));
            }
        }

        if (missing is not [var (file, line, at, threadName)]) return null;

        return LocalFix.Insert(
            Id, $"Wait for {threadName} to finish: {threadName}.join()",
            $"A `std::thread` still attached to its thread when its variable goes out of scope ends the whole program - that is the `terminate " +
            $"called without an active exception`. `{threadName}.join()` waits for the thread to finish first. (`detach()` would let it run on " +
            "unwatched, which is rarely what was meant.)",
            file.Path, at + 1, [CodeText.Indentation(file.Lines[line]) + $"{threadName}.join();"]);
    }
}

/// <summary><c>values[0] = 5;</c> on a vector that was created empty - AddressSanitizer's write to a null address.</summary>
public sealed partial class CppIndexEmptyVector : ILocalFixRule
{
    public string Id => "cpp-index-empty-vector";

    [GeneratedRegex(@"^(?<lead>\s*)(?<vector>[A-Za-z_]\w*)\s*\[\s*0\s*\]\s*=\s*(?<value>[^;=]+?)\s*;(?<tail>.*)$")]
    private static partial Regex Write();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "access-violation" } error) return null;
        if (!(error.Message ?? "").Contains("(a null pointer)", StringComparison.Ordinal) || CCode.Locate(context) is not { } at) return null;
        if (!Cpp.IsCpp(at.Source)) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Write().Match(masked[number - 1]) is not { Success: true } write) return null;

        var vector = write.Groups["vector"].Value;
        if (Cpp.Declaration(masked, number - 1, vector) is not { Pointer: false, Reference: false } declaration) return null;
        if (!Regex.IsMatch(declaration.Type, @"^(?:std\s*::\s*)?vector\s*<")) return null;
        if (!Regex.IsMatch(masked[declaration.Line], $@"\b{Regex.Escape(vector)}\s*(?:\{{\s*\}})?\s*;")) return null;

        var (_, last) = CCode.EnclosingFunction(masked, number - 1);
        var touched = new Regex($@"(?<![\w.>]){Regex.Escape(vector)}\s*(?:\.|->|=(?!=)|\[)");

        // Anything that could have added elements in between, or another indexed write after, makes it more than this one line.
        for (var i = declaration.Line + 1; i < number - 1; i++)
            if (touched.IsMatch(masked[i])) return null;

        for (var i = number; i <= last && i < masked.Count; i++)
            if (Regex.IsMatch(masked[i], $@"(?<![\w.>]){Regex.Escape(vector)}\s*\[[^\]]*\]\s*=(?!=)")) return null;

        var value = line.Substring(write.Groups["value"].Index, write.Groups["value"].Length);

        return LocalFix.ReplaceLine(
            Id, $"Add to {vector} with push_back",
            $"`{vector}` was created empty, and `[0]` does not add an element - it reaches one that must already be there. An empty vector has " +
            $"nothing at `[0]`, so the write went to a null address. `{vector}.push_back({value})` adds the value as a new element.",
            source.Path, number, $"{write.Groups["lead"].Value}{vector}.push_back({value});{line[write.Groups["tail"].Index..]}");
    }
}

/// <summary>
/// <c>for (int v : values) if (...) values.erase(std::find(...));</c> - erasing from a vector inside its own range-based loop, which
/// AddressSanitizer catches walking off the end.
/// </summary>
public sealed class CppEraseInLoop : ILocalFixRule
{
    public string Id => "cpp-erase-in-loop";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "gcc", ExceptionType: "container-overflow" or "heap-use-after-free" or "heap-buffer-overflow" }) return null;
        if (CCode.Locate(context) is not { } at || !Cpp.IsCpp(at.Source)) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (Blocks.RemoveInLoop(at.Source.Lines, masked, at.Number - 1, "cpp") is not { } fix) return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = "Erase with std::remove_if instead of inside the loop",
            Explanation =
                "Erasing from a vector closes the gap by moving every later element down one place - while the range-based `for` keeps " +
                "walking with the positions it already had, so it skips elements and runs off the end, which is what AddressSanitizer " +
                "caught. `std::remove_if` moves the elements to keep to the front in one pass, and a single `erase` then trims the rest.",
            File = at.Source.Path,
            StartLine = fix.Start + 1,
            RemoveCount = fix.Count,
            NewLines = [fix.Line],
        };
    }
}

/// <summary>A function returning a reference to its own local variable: MSVC's <c>C4172</c>, gcc's <c>-Wreturn-local-addr</c>.</summary>
public sealed partial class CppReturnLocalReference : ILocalFixRule
{
    public string Id => "cpp-return-local-reference";

    [GeneratedRegex(@"^returning address of local variable or temporary(?: : (?<name>\w+))?$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^reference to local variable '(?<name>\w+)' returned")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?:const\s+)?(?<type>[A-Za-z_][\w:]*(?:\s*<[^()]*>)?)\s*&\s*(?<function>[A-Za-z_][\w:]*)\s*\(")]
    private static partial Regex Header();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.ExceptionType != "compile warning") return null;
        if ((CCode.MsvcMessage(error, "C4172", MsvcMessage()) ?? CCode.GccMessage(error, GccMessage())) is not { } message) return null;
        if (Cpp.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var (first, _) = CCode.EnclosingFunction(masked, at.Number - 1);

        if (Header().Match(masked[first]) is not { Success: true } header) return null;

        var original = source.Lines[first];
        var type = original.Substring(header.Groups["type"].Index, header.Groups["type"].Length);
        var function = header.Groups["function"];
        var local = message.Groups["name"].Success ? message.Groups["name"].Value : "a local variable";

        return LocalFix.ReplaceLine(
            Id, $"Return {type} by value",
            $"`{function.Value}` returns a reference to `{local}`, which exists only while `{function.Value}` runs - by the time the caller reads " +
            $"it, it is gone, and whatever sits at that address next is what gets read. Returning the `{type}` itself hands back a copy that " +
            "lives on.",
            source.Path, first + 1, header.Groups["lead"].Value + type + " " + original[function.Index..]) with
        {
            ResolvesWarning = error.Message,
        };
    }
}
