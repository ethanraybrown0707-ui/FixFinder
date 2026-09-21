using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>undefined: fmt</c> - a standard package used without being imported.</summary>
public sealed partial class GoMissingImport : ILocalFixRule
{
    public string Id => "go-missing-import";

    [GeneratedRegex(@"^undefined: (?<name>[a-z]\w*)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        if (!GoCode.StandardPackages.TryGetValue(name, out var path) || GoCode.HasImport(at.Source, path)) return null;
        if (!Regex.IsMatch(CodeText.Mask(at.Line, Syntax.CLike), $@"(?<![\w.]){Regex.Escape(name)}\s*\.\s*[A-Z]")) return null;

        return GoCode.AddImport(Id, $"Import \"{path}\"",
            $"`{name}` is the standard package \"{path}\", and a Go file can only use a package it imports.", at.Source, path);
    }
}

/// <summary><c>"os" imported and not used</c> - when that is the build's only complaint.</summary>
public sealed partial class GoUnusedImport : ILocalFixRule
{
    public string Id => "go-unused-import";

    [GeneratedRegex(@"^""(?<path>[^""]+)"" imported and not used$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        if (context.AllErrors.Count() > 1) return null;

        var path = Regex.Escape(message.Groups["path"].Value);
        if (!Regex.IsMatch(at.Line, $@"^\s*(?:import\s+)?""{path}""\s*$")) return null;

        return CCode.RemoveLine(
            Id, $"Remove the import of \"{message.Groups["path"].Value}\"",
            "Go refuses to build a file that imports a package it never uses - an unused import is an error, not a warning. Nothing " +
            "in this file uses it, so the import goes.",
            at.Source.Path, at.Number);
    }
}

/// <summary><c>undefined: totl</c> - the one name within a letter or two, even when Go first reported <c>total</c> as unused.</summary>
public sealed partial class GoNearestName : ILocalFixRule
{
    public string Id => "go-nearest-name";

    [GeneratedRegex(@"^undefined: (?<name>[A-Za-z_]\w*)$")]
    private static partial Regex Undefined();

    [GeneratedRegex(@"^declared and not used: (?<name>\w+)$")]
    private static partial Regex Unused();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "go") return null;

        ParsedError? target = null;
        string? wrong = null;

        if (Undefined().Match(error.Message ?? "") is { Success: true } undefined)
        {
            (target, wrong) = (error, undefined.Groups["name"].Value);
        }
        else if (Unused().Match(error.Message ?? "") is { Success: true } unused)
        {
            var declared = unused.Groups["name"].Value;
            var misspelt = GoCode.OtherErrors(context, Undefined())
                .Where(x => CodeText.Nearest(x.Message.Groups["name"].Value, [declared]) == declared)
                .ToList();

            if (misspelt is [var only]) (target, wrong) = (only.Error, only.Message.Groups["name"].Value);
        }

        if (target is null || wrong is null || GoCode.LocateError(context, target) is not { } at) return null;
        if (GoCode.StandardPackages.ContainsKey(wrong) || GoCode.Keywords.Contains(wrong)) return null;

        var candidates = CodeText.Identifiers(CodeText.MaskAll(at.Source.Lines, Syntax.CLike))
            .Where(word => !GoCode.Keywords.Contains(word) && word != wrong)
            .Concat(GoCode.Builtins);

        if (CodeText.Nearest(wrong, candidates) is not { } right) return null;

        var hits = JavaScriptCode.UnqualifiedUses(CodeText.Mask(at.Line, Syntax.CLike), wrong);
        if (hits.Count == 0) return null;

        var corrected = at.Line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + wrong.Length)..];

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong} to {right}",
            $"Nothing called `{wrong}` exists, and `{right}` is the only name in this file within a letter or two of it.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>declared and not used: i</c> - a variable Go will not build with, when it truly is unused.</summary>
public sealed partial class GoUnusedVariable : ILocalFixRule
{
    public string Id => "go-unused-variable";

    [GeneratedRegex(@"^declared and not used: (?<name>\w+)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^undefined: ")]
    private static partial Regex AnyUndefined();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        if (GoCode.OtherErrors(context, AnyUndefined()).Any()) return null;

        var name = message.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.Mask(at.Line, Syntax.CLike);

        var several = Regex.Match(masked, @"^(?<lead>\s*(?:for\s+)?)(?<names>[\w\s,]+?)\s*(?<op>:=|=)");
        if (several.Success && several.Groups["names"].Value.Contains(','))
        {
            var names = several.Groups["names"];
            var parts = names.Value.Split(',').Select(p => p.Trim()).ToList();
            if (!parts.Contains(name) || parts.Count(p => p != name && p != "_") == 0) return null;

            var within = Regex.Match(at.Line.Substring(names.Index, names.Length), $@"(?<![\w]){escaped}(?!\w)");
            var index = names.Index + within.Index;

            return LocalFix.ReplaceLine(
                Id, $"Replace {name} with _",
                $"Go will not build with a variable that is never used. `{name}` is never used, so `_` - which takes a value and throws it " +
                "away - stands in its place." + (name == "err" ? " Throwing an error away is only right when it cannot happen; otherwise check it with `if err != nil`." : ""),
                at.Source.Path, at.Number, at.Line[..index] + "_" + at.Line[(index + name.Length)..]);
        }

        if (Regex.IsMatch(masked, $@"^\s*{escaped}\s*:=\s*(?:-?\d+(?:\.\d+)?|""\s*""|true|false)\s*$"))
        {
            return CCode.RemoveLine(
                Id, $"Remove the unused variable {name}",
                $"Go will not build with a variable that is never used, and `{name}` is given a value that nothing ever reads - so the line " +
                "does nothing, and goes.",
                at.Source.Path, at.Number);
        }

        return null;
    }
}

/// <summary><c>undefined: fmt.println (but have Println)</c>, <c>undefined: fmt.Printn</c> - a package member misspelt.</summary>
public sealed partial class GoPackageMember : ILocalFixRule
{
    public string Id => "go-package-member";

    [GeneratedRegex(@"^undefined: (?<package>[a-z]\w*)\.(?<name>\w+)(?: \(but have (?<have>\w+)\))?$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var package = message.Groups["package"].Value;
        var name = message.Groups["name"].Value;

        var exported = !message.Groups["have"].Success && GoCode.StandardPackages.TryGetValue(package, out var path) ? GoCode.ExportedNames(path) : [];
        var sameWord = exported.Where(e => e.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

        var right = message.Groups["have"].Success
            ? message.Groups["have"].Value
            : sameWord is [var only] ? only : CodeText.Nearest(name, exported);

        if (right is null) return null;

        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), $@"(?<![\w.]){Regex.Escape(package)}\s*\.\s*(?<name>{Regex.Escape(name)})\b").ToList();
        if (hits is not [var hit]) return null;

        var index = hit.Groups["name"].Index;

        return LocalFix.ReplaceLine(
            Id, $"Change {package}.{name} to {package}.{right}",
            message.Groups["have"].Success
                ? $"`{package}` has no `{name}`, and Go says what it does have: `{right}`. Only names starting with a capital letter can be used from outside a package."
                : $"`{package}` has no `{name}`. `{right}` is the only name it exports within a letter or two - read from `go doc` itself.",
            at.Source.Path, at.Number, at.Line[..index] + right + at.Line[(index + name.Length)..]);
    }
}

/// <summary><c>items.length undefined (type []int ...)</c>, <c>items.append</c>, <c>d.name ...</summary>
public sealed partial class GoSelector : ILocalFixRule
{
    public string Id => "go-selector";

    [GeneratedRegex(@"^(?<object>[\w.\[\]()]+)\.(?<name>\w+) undefined \(type (?<type>.+?) has no field or method \k<name>(?:, but does have (?:field|method) (?<right>\w+))?\)$")]
    private static partial Regex Message();

    private static readonly HashSet<string> Lengths = new(StringComparer.Ordinal) { "length", "len", "size", "count", "Length", "Len", "Size", "Count" };
    private static readonly HashSet<string> Appends = new(StringComparer.Ordinal) { "append", "push", "add", "Add", "Append", "Push" };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.CLike);
        var obj = message.Groups["object"].Value;
        var name = message.Groups["name"].Value;
        var type = message.Groups["type"].Value;

        var hits = Regex.Matches(masked, $@"(?<![\w.]){Regex.Escape(obj)}\s*\.\s*(?<name>{Regex.Escape(name)})\b(?<call>\s*\(\s*\))?").ToList();
        if (hits is not [var hit]) return null;

        if (message.Groups["right"].Success)
        {
            var right = message.Groups["right"].Value;
            var at_ = hit.Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Change {name} to {right}",
                $"`{type}` has no `{name}`, and Go says what it does have: `{right}`. Case matters in Go - a capital letter is what makes a " +
                "name usable outside its package.",
                source.Path, number, line[..at_] + right + line[(at_ + name.Length)..]);
        }

        var sequence = type.StartsWith("[]", StringComparison.Ordinal) || type == "string" || type.StartsWith("map[", StringComparison.Ordinal);
        if (!sequence) return null;

        if (Lengths.Contains(name))
        {
            var end = hit.Groups["call"].Success ? hit.Groups["call"].Index + hit.Groups["call"].Length : hit.Groups["name"].Index + name.Length;

            return LocalFix.ReplaceLine(
                Id, $"Use len({obj})",
                $"A Go {(type == "string" ? "string" : type.StartsWith("map", StringComparison.Ordinal) ? "map" : "slice")} has no `{name}` field or method - its " +
                $"length comes from the built-in `len`: `len({obj})`.",
                source.Path, number, line[..hit.Index] + $"len({obj})" + line[end..]);
        }

        if (Appends.Contains(name) && type.StartsWith("[]", StringComparison.Ordinal))
        {
            var statement = Regex.Match(line, $@"^(?<lead>\s*){Regex.Escape(obj)}\s*\.\s*{Regex.Escape(name)}\s*\((?<args>.+)\)\s*$");
            if (!statement.Success) return null;

            return LocalFix.ReplaceLine(
                Id, $"Use {obj} = append({obj}, ...)",
                "A Go slice has no methods to add to it. The built-in `append` returns a new slice with the values added, and that has to be " +
                $"stored back: `{obj} = append({obj}, {statement.Groups["args"].Value})`.",
                source.Path, number, $"{statement.Groups["lead"].Value}{obj} = append({obj}, {statement.Groups["args"].Value})");
        }

        return null;
    }
}

/// <summary><c>(missing method Area) have area() float64</c> - the method written with the wrong case.</summary>
public sealed partial class GoMethodCase : ILocalFixRule
{
    public string Id => "go-method-case";

    [GeneratedRegex(@"^cannot use (?<type>\w+)\{.*does not implement \w+ \(missing method (?<want>\w+)\)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"have (?<have>\w+)\(")]
    private static partial Regex Have();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.CompileMessage(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;
        if (Have().Match(context.Error.RawText) is not { Success: true } have) return null;

        var (want, wrong) = (message.Groups["want"].Value, have.Groups["have"].Value);
        if (!want.Equals(wrong, StringComparison.OrdinalIgnoreCase) || want == wrong) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var type = Regex.Escape(message.Groups["type"].Value);

        var declaration = new Regex($@"^func\s*\(\s*\w+\s+\*?{type}\s*\)\s*(?<name>{Regex.Escape(wrong)})\s*\(");
        var lines = Enumerable.Range(0, masked.Count).Where(i => declaration.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        if (masked.Any(text => Regex.IsMatch(text, $@"\.\s*{Regex.Escape(wrong)}\s*\("))) return null;

        var name = declaration.Match(masked[index]).Groups["name"];

        return LocalFix.ReplaceLine(
            Id, $"Rename {wrong} to {want}",
            $"The interface asks for `{want}`, and Go names are case-sensitive: `{wrong}` is a different method. A capital letter is also what " +
            "makes a method visible outside its package.",
            source.Path, index + 1, source.Lines[index][..name.Index] + want + source.Lines[index][(name.Index + name.Length)..]);
    }
}
