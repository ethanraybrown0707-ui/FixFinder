using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>len(items)</c> from Python, which JavaScript writes <c>items.length</c>.</summary>
public sealed partial class JsLen : ILocalFixRule
{
    public string Id => "js-len";

    [GeneratedRegex(@"^len is not defined$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w$.])len\s*\(\s*(?<arg>[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\s*\)")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var hits = Call().Matches(CodeText.Mask(at.Line, Syntax.CLike));
        if (hits.Count == 0) return null;

        return LocalFix.ReplaceLine(
            Id, "Use .length", "`len(...)` is Python. In JavaScript a list or a string carries its own length: `items.length`.",
            at.Source.Path, at.Number, CCode.ReplaceEach(at.Line, hits, hit => hit.Groups["arg"].Value + ".length"));
    }
}

/// <summary><c>count is not defined</c> inside a class that has <c>this.count</c> - a member used without <c>this</c>.</summary>
public sealed partial class JsMissingThis : ILocalFixRule
{
    public string Id => "js-missing-this";

    [GeneratedRegex(@"^(?<name>[A-Za-z_$][\w$]*) is not defined$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var name = message.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (JavaScriptCode.EnclosingClass(masked, number - 1) is not { } cls) return null;

        var escaped = Regex.Escape(name);
        var body = Enumerable.Range(cls.Header, cls.Close - cls.Header + 1).Select(i => masked[i]).ToList();

        var field = body.Any(text => Regex.IsMatch(text, $@"\bthis\.{escaped}\s*(?:=(?!=)|\+\+|--|[-+*/]=)"));
        var method = body.Any(text => Regex.IsMatch(text, $@"^\s*(?:async\s+)?(?<!static\s+){escaped}\s*\([^()]*\)\s*\{{") && !Regex.IsMatch(text, @"^\s*static\b"));

        if (!field && !method) return null;

        // Inside a static method there is no object for `this` to be.
        if (JavaScriptCode.EnclosingFunction(masked, number - 1) is { } function && Regex.IsMatch(masked[function.Line], @"^\s*static\b")) return null;

        var hits = JavaScriptCode.UnqualifiedUses(masked[number - 1], name);
        if (hits.Count == 0) return null;

        var corrected = line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + "this." + corrected[index..];

        return LocalFix.ReplaceLine(
            Id, $"Write this.{name}",
            $"`{name}` belongs to the object - {(field ? $"it is set as `this.{name}`" : $"it is a method of `{cls.Name}`")} - and inside a " +
            $"class a member is always reached through `this`. Without it, JavaScript looks for a variable called `{name}`, and there is none.",
            source.Path, number, corrected);
    }
}

/// <summary><c>totl is not defined</c> - the one name within a letter or two, from the file or JavaScript's globals.</summary>
public sealed partial class JsNearestName : ILocalFixRule
{
    public string Id => "js-nearest-name";

    [GeneratedRegex(@"^(?<name>[A-Za-z_$][\w$]*) is not defined$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var wrong = message.Groups["name"].Value;
        var candidates = CodeText.Identifiers(CodeText.MaskAll(at.Source.Lines, Syntax.CLike))
            .Where(word => !JavaScriptCode.Keywords.Contains(word))
            .Concat(JavaScriptCode.Globals);

        if (CodeText.Nearest(wrong, candidates.Where(c => c != wrong)) is not { } right) return null;

        // Every use on the line, or the copy still fails on the one Node did not point at.
        var hits = JavaScriptCode.UnqualifiedUses(CodeText.Mask(at.Line, Syntax.CLike), wrong);
        if (hits.Count == 0) return null;

        var corrected = at.Line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + wrong.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong} to {right}",
            $"Nothing called `{wrong}` exists. `{right}` is the only name in this file or JavaScript's globals within a letter or two of it.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>Class constructor Dog cannot be invoked without 'new'</c>.</summary>
public sealed partial class JsClassWithoutNew : ILocalFixRule
{
    public string Id => "js-class-without-new";

    [GeneratedRegex(@"^Class constructor (?<name>[\w$]+) cannot be invoked without 'new'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), $@"(?<![\w$.])(?<!\bnew\s+){Regex.Escape(name)}\s*\(").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Create the {name} with new",
            $"`{name}` is a class, and a class makes an object only with `new` - `new {name}(...)`. Called like a function, it refuses.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + "new " + at.Line[hit.Index..]);
    }
}

/// <summary><c>Assignment to constant variable.</c> - a <c>const</c> that the program goes on to change.</summary>
public sealed partial class JsConstReassigned : ILocalFixRule
{
    public string Id => "js-const-reassigned";

    [GeneratedRegex(@"^Assignment to constant variable\.$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![\w$.])(?<name>[A-Za-z_$][\w$]*)\s*(?:[-+*/%]?=(?![=>])|\+\+|--)|(?:\+\+|--)\s*(?<name>[A-Za-z_$][\w$]*)")]
    private static partial Regex Assigned();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var (source, number, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        var found = new List<(int Line, int Index, string Name)>();

        foreach (var name in Assigned().Matches(masked[number - 1]).Select(m => m.Groups["name"].Value).Distinct())
        {
            var declaration = new Regex($@"\bconst\s+(?={Regex.Escape(name)}\b)");

            for (var i = number - 1; i >= 0; i--)
            {
                if (declaration.Match(masked[i]) is not { Success: true } d) continue;
                found.Add((i, d.Index, name));
                break;
            }
        }

        if (found is not [var (line, index, variable)]) return null;

        var original = source.Lines[line];

        return LocalFix.ReplaceLine(
            Id, $"Declare {variable} with let",
            $"`const` promises `{variable}` will never be given another value, and line {number} gives it one. `let` declares a variable " +
            "that is allowed to change.",
            source.Path, line + 1, original[..index] + "let" + original[(index + "const".Length)..]);
    }
}

/// <summary><c>(intermediate value).area is not a function</c> on a getter - <c>get area()</c> is read, not called.</summary>
public sealed partial class JsGetterCalled : ILocalFixRule
{
    public string Id => "js-getter-called";

    [GeneratedRegex(@"^(?<object>.+)\.(?<member>[\w$]+) is not a function$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var member = Regex.Escape(message.Groups["member"].Value);
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (!masked.Any(text => Regex.IsMatch(text, $@"^\s*(?:static\s+)?get\s+{member}\s*\(\s*\)\s*\{{"))) return null;

        var hits = Regex.Matches(masked[at.Number - 1], $@"\.\s*{member}(?<call>\s*\(\s*\))").ToList();
        if (hits is not [var hit]) return null;

        var call = hit.Groups["call"];

        return LocalFix.ReplaceLine(
            Id, $"Read {message.Groups["member"].Value} without brackets",
            $"`{message.Groups["member"].Value}` is a getter - `get {message.Groups["member"].Value}()` - so it is read like a property and gives " +
            "its value straight away. Brackets try to call that value as a function.",
            at.Source.Path, at.Number, at.Line[..call.Index] + at.Line[(call.Index + call.Length)..]);
    }
}

/// <summary><c>d.create is not a function</c> where <c>create</c> is static - it belongs to the class, not the object.</summary>
public sealed partial class JsStaticOnInstance : ILocalFixRule
{
    public string Id => "js-static-on-instance";

    [GeneratedRegex(@"^(?<object>[A-Za-z_$][\w$]*)\.(?<member>[\w$]+) is not a function$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var name = message.Groups["object"].Value;
        var member = message.Groups["member"].Value;

        if (JavaScriptCode.ClassOf(masked, name) is not { } cls || JavaScriptCode.ClassBlock(masked, cls) is not { } body) return null;

        var isStatic = Enumerable.Range(body.Header, body.Close - body.Header + 1)
            .Any(i => Regex.IsMatch(masked[i], $@"^\s*static\s+(?:async\s+)?{Regex.Escape(member)}\s*\("));
        if (!isStatic) return null;

        var hits = Regex.Matches(masked[at.Number - 1], $@"(?<![\w$.]){Regex.Escape(name)}(?=\s*\.\s*{Regex.Escape(member)}\s*\()").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Call {member} on {cls}",
            $"`{member}` is `static`: it belongs to the class `{cls}`, not to each object made from it, so it is called as `{cls}.{member}(...)`.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + cls + at.Line[(hit.Index + name.Length)..]);
    }
}

/// <summary><c>Must call super constructor in derived class</c> - a subclass constructor using <c>this</c> before <c>super</c>.</summary>
public sealed partial class JsSuperMissing : ILocalFixRule
{
    public string Id => "js-super-missing";

    [GeneratedRegex(@"^Must call super constructor in derived class before accessing 'this'")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<indent>\s*)constructor\s*\((?<params>[^()]*)\)\s*\{\s*$")]
    private static partial Regex Constructor();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (JavaScriptCode.EnclosingClass(masked, at.Number - 1) is not { } cls) return null;
        if (Regex.Match(masked[cls.Header], @"\bextends\s+(?<base>[A-Za-z_$][\w$]*)") is not { Success: true } extends) return null;

        var header = Enumerable.Range(cls.Header, at.Number - cls.Header).LastOrDefault(i => Constructor().IsMatch(masked[i]), -1);
        if (header < 0) return null;

        var derived = Names(Constructor().Match(masked[header]).Groups["params"].Value);

        IReadOnlyList<string> needed = [];
        if (JavaScriptCode.ClassBlock(masked, extends.Groups["base"].Value) is { } parent)
        {
            var baseHeader = Enumerable.Range(parent.Header, parent.Close - parent.Header + 1).FirstOrDefault(i => Constructor().IsMatch(masked[i]), -1);
            if (baseHeader >= 0) needed = Names(Constructor().Match(masked[baseHeader]).Groups["params"].Value);
        }
        else
        {
            return null;
        }

        if (needed.Any(name => !derived.Contains(name))) return null;

        var indent = header + 1 < source.Count && source.Lines[header + 1].Trim().Length > 0
            ? CodeText.Indentation(source.Lines[header + 1])
            : CodeText.Indentation(source.Lines[header]) + "  ";

        return LocalFix.Insert(
            Id, $"Call super({string.Join(", ", needed)}) first",
            $"A class that `extends {extends.Groups["base"].Value}` has to let `{extends.Groups["base"].Value}` build the object before it can use " +
            $"`this` - that is what `super(...)` does, and it comes first in the constructor.",
            source.Path, header + 2, [$"{indent}super({string.Join(", ", needed)});"]);
    }

    private static IReadOnlyList<string> Names(string parameters) =>
        parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Regex.Match(p, @"^[A-Za-z_$][\w$]*").Value)
            .ToList();
}

/// <summary><c>name = name</c> in a constructor, so <c>d.name</c> is undefined later.</summary>
public sealed partial class JsConstructorWithoutThis : ILocalFixRule
{
    public string Id => "js-constructor-without-this";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '(?<property>[^']+)'\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var property = Regex.Escape(message.Groups["property"].Value);

        var reads = Regex.Matches(masked[at.Number - 1], $@"(?<![\w$.])(?<object>[A-Za-z_$][\w$]*)\s*\.\s*(?<field>[A-Za-z_$][\w$]*)\s*\.\s*{property}\b").ToList();
        if (reads is not [var read]) return null;

        if (JavaScriptCode.ClassOf(masked, read.Groups["object"].Value) is not { } cls || JavaScriptCode.ClassBlock(masked, cls) is not { } body) return null;

        var field = Regex.Escape(read.Groups["field"].Value);
        var assignment = new Regex($@"^(?<lead>\s*){field}\s*=(?!=)\s*(?<value>[^;]+?)\s*;?\s*$");

        var lines = Enumerable.Range(body.Header, body.Close - body.Header + 1).Where(i => assignment.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        var match = assignment.Match(source.Lines[index]);

        return LocalFix.ReplaceLine(
            Id, $"Store it as this.{read.Groups["field"].Value}",
            $"`{read.Groups["field"].Value} = {match.Groups["value"].Value}` inside the constructor only gives the parameter its own value back. " +
            $"The object keeps it only as `this.{read.Groups["field"].Value}`, which is what `{read.Groups["object"].Value}.{read.Groups["field"].Value}` reads.",
            source.Path, index + 1, $"{match.Groups["lead"].Value}this.{read.Groups["field"].Value} = {match.Groups["value"].Value};");
    }
}

/// <summary><c>this.ticks</c> inside <c>function () {...}</c> passed as a callback - where <c>this</c> is not the object.</summary>
public sealed partial class JsThisInCallback : ILocalFixRule
{
    public string Id => "js-this-in-callback";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '(?<property>[^']+)'\)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<=[(,]\s*)function\s*\((?<params>[^()]*)\)\s*\{")]
    private static partial Regex Callback();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (!Regex.IsMatch(masked[at.Number - 1], $@"\bthis\s*\.\s*{Regex.Escape(message.Groups["property"].Value)}\b")) return null;

        if (JavaScriptCode.EnclosingFunction(masked, at.Number - 1) is not { } function || !function.Opener.Groups["keyword"].Success) return null;
        if (Callback().Matches(masked[function.Line]).ToList() is not [var callback]) return null;

        var original = source.Lines[function.Line];

        return LocalFix.ReplaceLine(
            Id, "Use an arrow function so this stays the object",
            "A `function` passed as a callback gets its own `this`, which here is `undefined` - not the object whose method passed it. An " +
            "arrow function has no `this` of its own, so inside it `this` is still the object.",
            source.Path, function.Line + 1,
            original[..callback.Index] + $"({callback.Groups["params"].Value}) => {{" + original[(callback.Index + callback.Length)..]);
    }
}

/// <summary><c>const inc = c.increment;</c> then <c>inc()</c> - a method taken off its object, so <c>this</c> is undefined inside.</summary>
public sealed partial class JsDetachedMethod : ILocalFixRule
{
    public string Id => "js-detached-method";

    [GeneratedRegex(@"^Cannot read properties of undefined \(reading '(?<property>[^']+)'\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (!Regex.IsMatch(masked[at.Number - 1], $@"\bthis\s*\.\s*{Regex.Escape(message.Groups["property"].Value)}\b")) return null;

        if (JavaScriptCode.EnclosingFunction(masked, at.Number - 1) is not { } function || !function.Opener.Groups["method"].Success) return null;

        var method = Regex.Escape(function.Opener.Groups["method"].Value);
        var detached = new Regex($@"^(?<lead>\s*(?:const|let|var)\s+[A-Za-z_$][\w$]*\s*=\s*)(?<object>[A-Za-z_$][\w$]*)\s*\.\s*{method}\s*;\s*$");

        var lines = Enumerable.Range(0, masked.Count).Where(i => detached.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        var match = detached.Match(source.Lines[index]);
        var obj = match.Groups["object"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Bind {function.Opener.Groups["method"].Value} to {obj}",
            $"Taking `{obj}.{function.Opener.Groups["method"].Value}` off the object and calling it on its own loses `{obj}` - inside, `this` is " +
            $"`undefined`. `.bind({obj})` makes a function that always runs with `this` as `{obj}`.",
            source.Path, index + 1, $"{match.Groups["lead"].Value}{obj}.{function.Opener.Groups["method"].Value}.bind({obj});");
    }
}

/// <summary><c>Maximum call stack size exceeded</c> from a setter that assigns to its own property.</summary>
public sealed partial class JsSetterRecursion : ILocalFixRule
{
    public string Id => "js-setter-recursion";

    [GeneratedRegex(@"^Maximum call stack size exceeded$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "RangeError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (JavaScriptCode.EnclosingFunction(masked, at.Number - 1) is not { } function) return null;

        var setter = Regex.Match(masked[function.Line], @"^\s*set\s+(?<name>[A-Za-z_$][\w$]*)\s*\(");
        if (!setter.Success) return null;

        var name = Regex.Escape(setter.Groups["name"].Value);
        if (masked.Any(text => Regex.IsMatch(text, $@"^\s*get\s+{name}\s*\("))) return null;

        var hits = Regex.Matches(masked[at.Number - 1], $@"\bthis\s*\.\s*(?<name>{name})\s*=(?!=)").ToList();
        if (hits is not [var hit]) return null;

        var index = hit.Groups["name"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Store the value as this._{setter.Groups["name"].Value}",
            $"Assigning `this.{setter.Groups["name"].Value}` inside `set {setter.Groups["name"].Value}` calls the same setter again, which assigns " +
            $"again, until the stack runs out. The value has to be kept under another name - `this._{setter.Groups["name"].Value}` is the usual one.",
            source.Path, at.Number, at.Line[..index] + "_" + at.Line[index..]);
    }
}
