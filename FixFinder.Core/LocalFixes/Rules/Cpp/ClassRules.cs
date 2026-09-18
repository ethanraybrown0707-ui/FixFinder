using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>override</c> of a base class function that is not <c>virtual</c>.</summary>
public sealed class CppVirtualBase : ILocalFixRule
{
    public string Id => "cpp-virtual-base";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppCode.OverrideError(context.Error) is not { } message || CppCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var derived = message.Groups["class"].Value;
        var member = message.Groups["member"].Value;

        var header = CppCode.ClassHeader(masked, derived);
        if (header < 0) return null;

        var plain = new List<(string Base, int Line)>();

        foreach (var (name, _, _) in CppCode.BaseClasses(masked[header]))
        {
            var baseHeader = CppCode.ClassHeader(masked, name);
            if (baseHeader < 0) continue;

            foreach (var i in CppCode.LinesDeclaring(masked, baseHeader, member))
            {
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
        if (CppCode.OverrideError(context.Error) is not { } message || CppCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var member = message.Groups["member"].Value;

        var header = CppCode.ClassHeader(masked, message.Groups["class"].Value);
        if (header < 0) return null;

        var candidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, _, _) in CppCode.BaseClasses(masked[header]))
        {
            var baseHeader = CppCode.ClassHeader(masked, name);
            if (baseHeader < 0) continue;

            foreach (var i in CppCode.MemberLines(masked, baseHeader))
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

        if (message is null || CppCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = Brackets.BraceDepths(masked);
        var baseName = message.Groups["base"].Value;
        var derived = message.Groups["derived"].Success ? message.Groups["derived"].Value : null;

        var unspecified = new List<(int Line, int Column, string Derived)>();

        for (var i = 0; i < masked.Count; i++)
        {
            if (depths[i] != 0 || Regex.Match(masked[i], @"^\s*class\s+(?<name>\w+)") is not { Success: true } cls) continue;
            if (derived is not null && cls.Groups["name"].Value != derived) continue;

            foreach (var (name, column, specified) in CppCode.BaseClasses(masked[i]))
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

        if (message is null || CppCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var cls = message.Groups["class"].Value;
        var member = message.Groups["member"].Value;

        var header = CppCode.ClassHeader(masked, cls);
        if (header < 0) return null;

        if (masked.Any(text => Regex.IsMatch(text, $@"\b{Regex.Escape(cls)}\s*::\s*{Regex.Escape(member)}\s*\("))) return null;

        var signature = new Regex($@"(?<![\w:~.>]){Regex.Escape(member)}\s*\([^()]*(?<close>\))(?!\s*const\b)(?=\s*(?:noexcept\s*)?(?:\{{|;|$))");
        var lines = CppCode.MemberLines(masked, header)
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
        if (CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var type = message.Groups["to"].Value.Split("::")[^1];

        var header = CppCode.ClassHeader(masked, type);
        if (header < 0 || !CppCode.LinesDeclaring(masked, header, type).Any(i => Regex.IsMatch(masked[i], $@"\bexplicit\s+{Regex.Escape(type)}\s*\("))) return null;

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

        if (message is null || CppCode.Locate(context) is not { } at) return null;

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

/// <summary>A derived class used as an object while pure virtual functions it inherits are still unwritten: g++'s <c>cannot
/// declare variable 's' to be of abstract type 'Square'</c>, MSVC's <c>C2259</c>.</summary>
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

        if (pure.Count == 0 || pure.Any(p => p.Owner == cls || p.Member.StartsWith('~'))) return null;

        if (ClassFile(context, output, cls) is not { } source) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var header = CppCode.ClassHeader(masked, cls);
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

    private static SourceFile? ClassFile(LocalFixContext context, IReadOnlyList<string> output, string cls)
    {
        foreach (var line in output)
        {
            if (GccClassAt().Match(line) is { Success: true } gcc && gcc.Groups["cls"].Value == cls) return context.Read(gcc.Groups["file"].Value);
            if (MsvcClassAt().Match(line) is { Success: true } msvc && msvc.Groups["cls"].Value == cls) return context.Read(msvc.Groups["file"].Value);
        }

        return context.Read(context.Frame?.File);
    }

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

/// <summary><c>std::ostream&amp; operator&lt;&lt;(std::ostream&amp;, const Point&amp;)</c> written inside the class, without
/// <c>friend</c>.</summary>
public sealed partial class CppStreamOperatorFriend : ILocalFixRule
{
    public string Id => "cpp-stream-operator-friend";

    [GeneratedRegex(@"operator(?:<<|>>)\([^']*\)' must (?:have|take) exactly one argument$")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"^binary operator '(?:<<|>>)' has too many parameters$")]
    private static partial Regex MsvcMessage();

    [GeneratedRegex(@"^(?<lead>\s*)(?!friend\b)(?<rest>(?:std::)?[io]?stream\s*&\s*operator\s*(?:<<|>>)\s*\(.*)$")]
    private static partial Regex Declaration();

    public LocalFix? Propose(LocalFixContext context)
    {
        if ((CCode.GccMessage(context.Error, GccMessage()) ?? CCode.MsvcMessage(context.Error, "C2804", MsvcMessage())) is null) return null;
        if (CppCode.Locate(context) is not { } at || Declaration().Match(at.Line) is not { Success: true } declaration) return null;

        return LocalFix.ReplaceLine(
            Id, "Make the stream operator a friend of the class",
            "An operator written inside a class takes the object itself as its left side, so `operator<<` there would be used as " +
            "`point << stream` and can only take one more argument. `std::cout << point` has the stream on the left, which needs a " +
            "function outside the class; `friend` makes this one exactly that, while still letting it read the private members.",
            at.Source.Path, at.Number, declaration.Groups["lead"].Value + "friend " + declaration.Groups["rest"].Value);
    }
}

/// <summary><c>double area() override</c> where the base declares <c>virtual double area() const</c> - the missing <c>const</c>
/// makes it a different function.</summary>
public sealed partial class CppOverrideMissingConst : ILocalFixRule
{
    public string Id => "cpp-override-missing-const";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (CppCode.OverrideError(context.Error) is not { } message || CppCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var member = message.Groups["member"].Value;

        var own = Regex.Match(masked[number - 1], $@"(?<![\w:~]){Regex.Escape(member)}\s*\((?<parameters>[^()]*)\)\s*(?<const>const\s*)?(?<override>override)\b");
        if (!own.Success || own.Groups["const"].Success) return null;

        var header = CppCode.ClassHeader(masked, message.Groups["class"].Value);
        if (header < 0) return null;

        var parameters = Normalise(own.Groups["parameters"].Value);
        var constInBase = false;

        foreach (var (name, _, _) in CppCode.BaseClasses(masked[header]))
        {
            var baseHeader = CppCode.ClassHeader(masked, name);
            if (baseHeader < 0) continue;

            foreach (var i in CppCode.LinesDeclaring(masked, baseHeader, member))
            {
                var declared = Regex.Match(masked[i], $@"\bvirtual\b.*?(?<![\w:~]){Regex.Escape(member)}\s*\((?<parameters>[^()]*)\)\s*(?<const>const)?");
                if (declared.Success && declared.Groups["const"].Success && Normalise(declared.Groups["parameters"].Value) == parameters) constInBase = true;
            }
        }

        if (!constInBase) return null;

        var at0 = own.Groups["override"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Match the base class: {member}() const override",
            $"The base class declares `{member}` as `const` - it promises not to change the object - and `const` is part of which function " +
            $"it is. Without it, this `{member}` is a different function that overrides nothing, and the class stays abstract. Adding " +
            "`const` makes it the same function.",
            source.Path, number, line[..at0] + "const " + line[at0..]);
    }

    private static string Normalise(string parameters) =>
        string.Join(",", parameters.Split(',').Select(p => Regex.Replace(Regex.Replace(p.Trim(), @"\s+\w+$", ""), @"\s+", " "))).Trim();
}

/// <summary><c>delete pet</c> through a base class whose destructor is not <c>virtual</c> - the derived destructor never runs.</summary>
public sealed partial class CppVirtualDestructor : ILocalFixRule
{
    public string Id => "cpp-virtual-destructor";

    [GeneratedRegex(@"^deleting object of (?:abstract|polymorphic) class type '(?<class>\w+)' which has non-virtual destructor")]
    private static partial Regex GccMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is not { } message) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var name = message.Groups["class"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var header = CppCode.ClassHeader(masked, name);
        if (header < 0 || CppCode.ClassBraces(masked, header) is not { } body) return null;

        var members = CppCode.MemberLines(masked, header);
        var destructor = members.Where(i => Regex.IsMatch(masked[i], $@"(?<![\w:])~{Regex.Escape(name)}\s*\(")).ToList();

        const string Explanation =
            "The object is deleted through a pointer to its base class, and the base class's destructor is not `virtual` - so only the base " +
            "part is destroyed, the derived class's destructor never runs, and whatever it would have released leaks. A class meant to be " +
            "used through a base pointer needs a `virtual` destructor, which makes `delete` find the right one.";

        if (destructor is [var line])
        {
            var text = source.Lines[line];
            var at = Regex.Match(text, $@"(?<![\w:])~{Regex.Escape(name)}\s*\(").Index;
            if (Regex.IsMatch(masked[line][..at], @"\bvirtual\b")) return null;

            return LocalFix.ReplaceLine(Id, $"Make ~{name}() virtual", Explanation, source.Path, line + 1, text[..at] + "virtual " + text[at..]) with
            {
                ResolvesWarning = "non-virtual destructor",
            };
        }

        if (destructor.Count > 0) return null;

        var isStruct = Regex.IsMatch(masked[header], @"^\s*struct\b");
        var publicLine = members.FirstOrDefault(i => Regex.IsMatch(masked[i], @"^\s*public\s*:"), -1);
        var indent = members.Select(i => source.Lines[i]).FirstOrDefault(l => l.Trim().Length > 0 && !l.Trim().EndsWith(':')) is { } memberLine
            ? CodeText.Indentation(memberLine)
            : "    ";

        IReadOnlyList<string> added = publicLine >= 0 || isStruct
            ? [$"{indent}virtual ~{name}() = default;"]
            : [$"public:", $"{indent}virtual ~{name}() = default;"];

        var before = publicLine >= 0 ? publicLine + 2 : body.Open + 2;

        return LocalFix.Insert(Id, $"Give {name} a virtual destructor", Explanation, source.Path, before, added) with
        {
            ResolvesWarning = "non-virtual destructor",
        };
    }
}

/// <summary><c>catch (std::exception e)</c> - a copy of just the base part, which loses what was really thrown.</summary>
public sealed partial class CppCatchByReference : ILocalFixRule
{
    public string Id => "cpp-catch-by-reference";

    [GeneratedRegex(@"^catching polymorphic type '(?:class |struct )?(?<type>[\w:]+)' by value")]
    private static partial Regex GccMessage();

    [GeneratedRegex(@"\bcatch\s*\(\s*(?<const>const\s+)?(?<type>[\w:]+)\s+(?<name>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex Catch();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "compile warning" || CCode.GccMessage(context.Error, GccMessage()) is null) return null;
        if (CppCode.Locate(context) is not { } at || Catch().Matches(at.Line).ToList() is not [var clause]) return null;

        var type = clause.Groups["type"].Value;
        var name = clause.Groups["name"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Catch by reference: const {type}& {name}",
            $"Catching `{type}` by value copies just the `{type}` part of whatever was thrown - so `{name}.what()` answers for a plain " +
            $"`{type}`, and the real message, from the class that was thrown, is lost. A reference is the thrown object itself.",
            at.Source.Path, at.Number, at.Line[..clause.Index] + $"catch (const {type}& {name})" + at.Line[(clause.Index + clause.Length)..]) with
        {
            ResolvesWarning = "by value",
        };
    }
}
