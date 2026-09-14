using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Go rules share: the file being Go, its imports, and reading one error among the several a build reports.</summary>
/// <remarks>
/// The Go compiler reports every error in a build, and the first is often a consequence rather than the cause:
/// <c>"fmt" imported and not used</c> comes before the <c>console.log</c> that should have used it, and
/// <c>declared and not used: total</c> before the <c>totl</c> that misspelt it. So a rule reads the whole build's errors, and
/// fixes the cause - never deleting the import or the variable that only looked unused.
/// </remarks>
internal static partial class GoCode
{
    public static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for", "func", "go",
        "goto", "if", "import", "interface", "map", "package", "range", "return", "select", "struct", "switch", "type", "var",
    };

    public static readonly string[] Builtins =
    [
        "append", "cap", "close", "copy", "delete", "len", "make", "new", "panic", "print", "println", "recover", "min", "max",
        "clear", "true", "false", "nil", "iota", "int", "int8", "int16", "int32", "int64", "uint", "uint8", "uint16",
        "uint32", "uint64", "float32", "float64", "string", "bool", "byte", "rune", "error", "any",
    ];

    /// <summary>The standard packages a bare name most likely meant, by the name code uses them under.</summary>
    public static readonly Dictionary<string, string> Packages = new(StringComparer.Ordinal)
    {
        ["fmt"] = "fmt", ["strings"] = "strings", ["strconv"] = "strconv", ["math"] = "math", ["os"] = "os", ["time"] = "time",
        ["sort"] = "sort", ["errors"] = "errors", ["sync"] = "sync", ["bufio"] = "bufio", ["unicode"] = "unicode",
        ["bytes"] = "bytes", ["io"] = "io", ["rand"] = "math/rand", ["filepath"] = "path/filepath", ["json"] = "encoding/json",
        ["http"] = "net/http", ["regexp"] = "regexp", ["slices"] = "slices", ["maps"] = "maps", ["cmp"] = "cmp", ["log"] = "log",
        ["utf8"] = "unicode/utf8", ["atomic"] = "sync/atomic", ["context"] = "context", ["reflect"] = "reflect",
        ["exec"] = "os/exec", ["big"] = "math/big", ["csv"] = "encoding/csv", ["heap"] = "container/heap", ["list"] = "container/list",
    };

    public static readonly HashSet<string> Numeric = new(StringComparer.Ordinal)
    {
        "int", "int8", "int16", "int32", "int64", "uint", "uint8", "uint16", "uint32", "uint64", "float32", "float64", "byte", "rune",
    };

    [GeneratedRegex(@"^\s*import\s+(?:[\w.]+\s+)?""(?<path>[^""]+)""\s*$")]
    private static partial Regex SingleImport();

    [GeneratedRegex(@"^\s*import\s*\(\s*$")]
    private static partial Regex ImportBlock();

    [GeneratedRegex(@"^\s*(?:[\w.]+\s+)?""(?<path>[^""]+)""\s*$")]
    private static partial Regex ImportSpec();

    public static bool IsGo(SourceFile source) => Path.GetExtension(source.Path).Equals(".go", StringComparison.OrdinalIgnoreCase);

    public static Match? Compile(ParsedError error, Regex message) =>
        error.LanguageId == "go" && error.ExceptionType == "compile error" && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static Match? Runtime(ParsedError error, Regex message) =>
        error.LanguageId == "go" && error.ExceptionType is not ("compile error" or "link error") &&
        message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static (SourceFile Source, int Number, string Line)? At(LocalFixContext context, ParsedError error)
    {
        if (LocalFixContext.OwnFrame(error) is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsGo(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context) => At(context, context.Error);

    /// <summary>The other errors in the same build, with the message each one matched.</summary>
    public static IEnumerable<(ParsedError Error, Match Message)> Also(LocalFixContext context, Regex message) =>
        context.AllErrors.Where(e => e.LanguageId == "go").Select(e => (Error: e, Message: message.Match(e.Message ?? ""))).Where(x => x.Message.Success);

    public static IEnumerable<SourceFile> Files(LocalFixContext context)
    {
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*.go").Take(200))
            if (SourceFile.Read(path) is { } source) yield return source;
    }

    public static bool Imports(SourceFile source, string path) =>
        source.Lines.Any(line => (SingleImport().Match(line) is { Success: true } s && s.Groups["path"].Value == path) ||
                                 (ImportSpec().Match(line) is { Success: true } spec && spec.Groups["path"].Value == path));

    /// <summary>An import added where gofmt would put it: into the block in order, turning a single import into a block, or after package.</summary>
    public static LocalFix? AddImport(string rule, string title, string explanation, SourceFile source, string path)
    {
        var lines = source.Lines;

        for (var i = 0; i < lines.Count; i++)
        {
            if (ImportBlock().IsMatch(lines[i]))
            {
                var close = Enumerable.Range(i + 1, lines.Count - i - 1).FirstOrDefault(k => lines[k].Trim() == ")", -1);
                if (close < 0) return null;

                var before = Enumerable.Range(i + 1, close - i - 1)
                    .FirstOrDefault(k => ImportSpec().Match(lines[k]) is { Success: true } spec && string.CompareOrdinal(spec.Groups["path"].Value, path) > 0, close);

                return LocalFix.Insert(rule, title, explanation, source.Path, before + 1, [$"\t\"{path}\""]);
            }

            if (SingleImport().Match(lines[i]) is { Success: true } single)
            {
                var existing = single.Groups["path"].Value;
                var ordered = new[] { existing, path }.Order(StringComparer.Ordinal).Select(p => $"\t\"{p}\"");

                return new LocalFix
                {
                    RuleId = rule, Title = title, Explanation = explanation, File = source.Path,
                    StartLine = i + 1, RemoveCount = 1, NewLines = ["import (", .. ordered, ")"],
                };
            }
        }

        var package = Enumerable.Range(0, lines.Count).FirstOrDefault(k => Regex.IsMatch(lines[k], @"^\s*package\s+\w+"), -1);
        if (package < 0) return null;

        return LocalFix.Insert(rule, title, explanation, source.Path, package + 2, ["", $"import \"{path}\""]);
    }

    /// <summary>The top-level function around a line, by its header and its closing brace.</summary>
    public static (int Header, int Close)? Function(IReadOnlyList<string> masked, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (!Regex.IsMatch(masked[i], @"^func\b")) continue;
            return Js.BlockFrom(masked, i) is { } close && close >= index ? (i, close) : null;
        }

        return null;
    }

    public static string ZeroValue(string type) => type.Trim() switch
    {
        var t when Numeric.Contains(t) => "0",
        "string" => "\"\"",
        "bool" => "false",
        _ => "nil",
    };
}

/// <summary>Exported names of a standard package, asked of <c>go doc</c> - the answer for the Go that is installed.</summary>
internal static partial class GoDoc
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Cache = new(StringComparer.Ordinal);

    [GeneratedRegex(@"^\s*(?:func|type|var|const)\s+(?:\([^)]*\)\s*)?(?<name>[A-Z]\w*)")]
    private static partial Regex Declared();

    /// <summary>Only standard packages are asked about - nothing from the program or the internet is fetched or run.</summary>
    public static IReadOnlyList<string> Exported(string path)
    {
        if (!GoCode.Packages.ContainsValue(path)) return [];

        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var known)) return known;
        }

        if (TargetFactory.FindOnPath("go") is not { } go) return [];

        var output = Run(go, path) ?? Run(go, path);
        var names = (output ?? "").Split('\n').Select(l => Declared().Match(l)).Where(m => m.Success).Select(m => m.Groups["name"].Value).Distinct().ToList();

        if (names.Count == 0) return names;

        lock (Cache)
        {
            Cache[path] = names;
        }

        return names;
    }

    private static string? Run(string go, string path)
    {
        try
        {
            var start = new ProcessStartInfo(go)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            foreach (var argument in new[] { "doc", "-short", path }) start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null) return null;

            process.StandardInput.Close();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();

            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return null;
            }

            return process.ExitCode == 0 && output.Wait(5_000) ? output.Result : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

// ====================================================================== imports

/// <summary><c>undefined: fmt</c> - a standard package used without being imported.</summary>
public sealed partial class GoMissingImport : ILocalFixRule
{
    public string Id => "go-missing-import";

    [GeneratedRegex(@"^undefined: (?<name>[a-z]\w*)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        if (!GoCode.Packages.TryGetValue(name, out var path) || GoCode.Imports(at.Source, path)) return null;
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
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        // Anything else wrong in the build may be the code that was meant to use it.
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

/// <summary><c>missing import path</c> - <c>import fmt</c> without its quotes.</summary>
public sealed partial class GoImportQuotes : ILocalFixRule
{
    public string Id => "go-import-quotes";

    [GeneratedRegex(@"^(?:syntax error: )?missing import path")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var import = Regex.Match(at.Line, @"^(?<lead>\s*(?:import\s+)?)(?<path>[a-z][\w/.]*)\s*$");
        if (!import.Success || import.Groups["path"].Value == "import") return null;

        return LocalFix.ReplaceLine(
            Id, $"Quote the import: \"{import.Groups["path"].Value}\"",
            "An import path is a string, so it goes in double quotes: `import \"fmt\"`.",
            at.Source.Path, at.Number, $"{import.Groups["lead"].Value}\"{import.Groups["path"].Value}\"");
    }
}

// ====================================================================== names

/// <summary><c>undefined: console</c>, <c>System</c>, <c>Console</c> - how other languages print.</summary>
public sealed partial class GoForeignPrint : ILocalFixRule
{
    public string Id => "go-foreign-print";

    [GeneratedRegex(@"^undefined: (?<name>console|System|Console)$")]
    private static partial Regex Undefined();

    [GeneratedRegex(@"^""fmt"" imported and not used$")]
    private static partial Regex UnusedFmt();

    [GeneratedRegex(@"^(?<lead>\s*)(?<call>console\s*\.\s*log|System\s*\.\s*out\s*\.\s*println|System\s*\.\s*out\s*\.\s*print|Console\s*\.\s*WriteLine|Console\s*\.\s*Write)\s*\((?<args>.*)\)\s*;?\s*$")]
    private static partial Regex Statement();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "go" || (Undefined().IsMatch(error.Message ?? "") is false && UnusedFmt().IsMatch(error.Message ?? "") is false)) return null;

        var target = Undefined().IsMatch(error.Message ?? "") ? error : GoCode.Also(context, Undefined()).Select(x => x.Error).FirstOrDefault();
        if (target is null || GoCode.At(context, target) is not { } at || !GoCode.Imports(at.Source, "fmt")) return null;

        if (Statement().Match(CodeText.Mask(at.Line, Syntax.CLike)) is not { Success: true } statement) return null;

        var call = Regex.Replace(statement.Groups["call"].Value, @"\s+", "");
        var newline = call is "console.log" or "System.out.println" or "Console.WriteLine";
        var args = at.Line.Substring(statement.Groups["args"].Index, statement.Groups["args"].Length);
        var language = call.StartsWith("console", StringComparison.Ordinal) ? "JavaScript" : call.StartsWith("System", StringComparison.Ordinal) ? "Java" : "C#";

        return LocalFix.ReplaceLine(
            Id, newline ? "Print with fmt.Println" : "Print with fmt.Print",
            $"`{call}` is {language}. Go prints with the `fmt` package: `fmt.Println` ends the line, `fmt.Print` does not.",
            at.Source.Path, at.Number, $"{statement.Groups["lead"].Value}fmt.{(newline ? "Println" : "Print")}({args})");
    }
}

/// <summary><c>undefined: null</c>, <c>True</c>, <c>None</c> - other languages' words for Go's <c>nil</c> and <c>true</c>.</summary>
public sealed partial class GoForeignWord : ILocalFixRule
{
    public string Id => "go-foreign-word";

    [GeneratedRegex(@"^undefined: (?<name>null|NULL|None|undefined|True|False|nullptr)$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, (string Right, string From)> Words = new(StringComparer.Ordinal)
    {
        ["null"] = ("nil", "Java, C# and JavaScript"), ["NULL"] = ("nil", "C"), ["None"] = ("nil", "Python"),
        ["undefined"] = ("nil", "JavaScript"), ["nullptr"] = ("nil", "C++"), ["True"] = ("true", "Python"), ["False"] = ("false", "Python"),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var word = message.Groups["name"].Value;
        var (right, from) = Words[word];
        var hits = Js.Unqualified(CodeText.Mask(at.Line, Syntax.CLike), word);
        if (hits.Count == 0) return null;

        var corrected = at.Line;
        foreach (var index in Enumerable.Reverse(hits)) corrected = corrected[..index] + right + corrected[(index + word.Length)..];

        return LocalFix.ReplaceLine(Id, $"Write {word} as {right}", $"`{word}` is {from}. In Go it is `{right}`.", at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>undefined: count</c> on <c>count = 5</c> - a first assignment that needed <c>:=</c> to declare the variable.</summary>
public sealed partial class GoShortDeclare : ILocalFixRule
{
    public string Id => "go-short-declare";

    [GeneratedRegex(@"^undefined: (?<name>[A-Za-z_]\w*)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var name = Regex.Escape(message.Groups["name"].Value);
        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (GoCode.Function(masked, at.Number - 1) is not { } function) return null;

        var word = new Regex($@"(?<![\w.]){name}(?!\w)");
        var first = Enumerable.Range(function.Header + 1, function.Close - function.Header).FirstOrDefault(i => word.IsMatch(masked[i]), -1);
        if (first < 0) return null;

        var assignment = Regex.Match(masked[first], $@"^(?<lead>\s*){name}\s*(?<op>=)(?!=)\s*\S");
        if (!assignment.Success) return null;

        var op = assignment.Groups["op"].Index;
        var original = at.Source.Lines[first];

        return LocalFix.ReplaceLine(
            Id, $"Declare {message.Groups["name"].Value} with :=",
            $"`=` gives an existing variable a new value, and `{message.Groups["name"].Value}` does not exist yet. `:=` declares it and gives it " +
            "its first value in one go - Go works out the type from the value.",
            at.Source.Path, first + 1, original[..op] + ":=" + original[(op + 1)..]);
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
            // The variable only looks unused because the one place meant to use it spelt it differently.
            var declared = unused.Groups["name"].Value;
            var misspelt = GoCode.Also(context, Undefined())
                .Where(x => CodeText.Nearest(x.Message.Groups["name"].Value, [declared]) == declared)
                .ToList();

            if (misspelt is [var only]) (target, wrong) = (only.Error, only.Message.Groups["name"].Value);
        }

        if (target is null || wrong is null || GoCode.At(context, target) is not { } at) return null;
        if (GoCode.Packages.ContainsKey(wrong) || GoCode.Keywords.Contains(wrong)) return null;

        var candidates = CodeText.Identifiers(CodeText.MaskAll(at.Source.Lines, Syntax.CLike))
            .Where(word => !GoCode.Keywords.Contains(word) && word != wrong)
            .Concat(GoCode.Builtins);

        if (CodeText.Nearest(wrong, candidates) is not { } right) return null;

        // Every use on the line: a name misspelt once in `sqr.side * sqr.side` is misspelt twice, and the copy fails on the other.
        var hits = Js.Unqualified(CodeText.Mask(at.Line, Syntax.CLike), wrong);
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
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        // A misspelling somewhere else would explain it better than deleting anything.
        if (GoCode.Also(context, AnyUndefined()).Any()) return null;

        var name = message.Groups["name"].Value;
        var escaped = Regex.Escape(name);
        var masked = CodeText.Mask(at.Line, Syntax.CLike);

        // One of several names on the left: that one becomes the blank identifier.
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

        // A single variable given a plain value, which nothing reads: the line does nothing.
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
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var package = message.Groups["package"].Value;
        var name = message.Groups["name"].Value;

        // Go before 1.26 does not add "(but have Println)". The name that differs only in case is still the answer - `println`
        // is as near to `Sprintln` as to `Println` by edit distance, but only one of them is the same word.
        var exported = !message.Groups["have"].Success && GoCode.Packages.TryGetValue(package, out var path) ? GoDoc.Exported(path) : [];
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

/// <summary>
/// <c>items.length undefined (type []int ...)</c>, <c>items.append</c>, <c>d.name ... but does have field Name</c> - a field or method
/// the value does not have.
/// </summary>
public sealed partial class GoSelector : ILocalFixRule
{
    public string Id => "go-selector";

    [GeneratedRegex(@"^(?<object>[\w.\[\]()]+)\.(?<name>\w+) undefined \(type (?<type>.+?) has no field or method \k<name>(?:, but does have (?:field|method) (?<right>\w+))?\)$")]
    private static partial Regex Message();

    private static readonly HashSet<string> Lengths = new(StringComparer.Ordinal) { "length", "len", "size", "count", "Length", "Len", "Size", "Count" };
    private static readonly HashSet<string> Appends = new(StringComparer.Ordinal) { "append", "push", "add", "Add", "Append", "Push" };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

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

// ====================================================================== syntax

/// <summary><c>syntax error: unexpected semicolon or newline before {</c> - the brace on the line below.</summary>
public sealed partial class GoBraceOnNextLine : ILocalFixRule
{
    public string Id => "go-brace-on-next-line";

    [GeneratedRegex(@"^syntax error: unexpected semicolon or newline before \{$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (line.Trim() != "{" || number < 2) return null;

        var (code, tail) = CodeText.SplitComment(source.Lines[number - 2], Syntax.CLike);
        if (code.Trim().Length == 0) return null;

        return new LocalFix
        {
            RuleId = Id, Title = "Put the { at the end of the line above",
            Explanation =
                "Go puts an invisible semicolon at the end of a line like this one, which ends the statement before the `{` below it can " +
                "start its block. The opening brace always goes on the same line.",
            File = source.Path, StartLine = number - 1, RemoveCount = 2, NewLines = [code.TrimEnd() + " {" + tail],
        };
    }
}

/// <summary><c>syntax error: unexpected keyword else</c> - <c>else</c> on the line after the closing brace.</summary>
public sealed partial class GoElseOnNextLine : ILocalFixRule
{
    public string Id => "go-else-on-next-line";

    [GeneratedRegex(@"^syntax error: unexpected (?:keyword )?else\b")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var k = number - 2;
        while (k >= 0 && source.Lines[k].Trim().Length == 0) k--;
        if (k < 0 || source.Lines[k].Trim() != "}" || !line.TrimStart().StartsWith("else", StringComparison.Ordinal)) return null;

        return new LocalFix
        {
            RuleId = Id, Title = "Put else on the same line as the }",
            Explanation =
                "Go ends a line after `}` with an invisible semicolon, which finishes the `if` - so an `else` on the next line has nothing " +
                "to belong to. It goes on the same line as the closing brace: `} else {`.",
            File = source.Path, StartLine = k + 1, RemoveCount = number - k,
            NewLines = [CodeText.Indentation(source.Lines[k]) + "} " + line.TrimStart()],
        };
    }
}

/// <summary><c>while x &lt; 3 {</c> - Go has only <c>for</c>.</summary>
public sealed partial class GoWhile : ILocalFixRule
{
    public string Id => "go-while";

    [GeneratedRegex(@"^(?<lead>\s*)while\s+(?<condition>.+?)\s*\{\s*$")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go", ExceptionType: "compile error" } || GoCode.Locate(context) is not { } at) return null;
        if (!(context.Error.Message ?? "").StartsWith("syntax error", StringComparison.Ordinal)) return null;
        if (Loop().Match(at.Line) is not { Success: true } loop) return null;

        return LocalFix.ReplaceLine(
            Id, "Write the loop with for",
            "Go has no `while` - `for` does both jobs. With only a condition, `for x < 3 {` is exactly a while loop.",
            at.Source.Path, at.Number, $"{loop.Groups["lead"].Value}for {loop.Groups["condition"].Value} {{");
    }
}

/// <summary><c>for (i := 0; i &lt; 3; i++) {</c> - Go's for has no brackets.</summary>
public sealed partial class GoForParentheses : ILocalFixRule
{
    public string Id => "go-for-parentheses";

    [GeneratedRegex(@"^(?<lead>\s*)for\s*\((?<clauses>[^;{}]*;[^;{}]*;[^{}]*)\)\s*\{\s*$")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go", ExceptionType: "compile error" } || GoCode.Locate(context) is not { } at) return null;
        if (Loop().Match(at.Line) is not { Success: true } loop) return null;

        return LocalFix.ReplaceLine(
            Id, "Remove the brackets around the for clauses",
            "Go writes `for` like C does but without the brackets: `for i := 0; i < 3; i++ {`.",
            at.Source.Path, at.Number, $"{loop.Groups["lead"].Value}for {loop.Groups["clauses"].Value.Trim()} {{");
    }
}

/// <summary><c>no new variables on left side of :=</c> - declaring again what already exists.</summary>
public sealed partial class GoRedeclared : ILocalFixRule
{
    public string Id => "go-redeclared";

    [GeneratedRegex(@"^no new variables on left side of :=$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), ":=").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, "Assign with = instead of :=",
            "`:=` declares new variables, and everything on its left already exists. `=` gives the existing variable its new value.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + "=" + at.Line[(hit.Index + 2)..]);
    }
}

/// <summary><c>more than one character in rune literal</c> - a string written in single quotes.</summary>
public sealed partial class GoRuneLiteral : ILocalFixRule
{
    public string Id => "go-rune-literal";

    [GeneratedRegex(@"^more than one character in rune literal$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var multi = Cpp.Literals(at.Line).Where(l => l.Quote == '\'' && Regex.Replace(at.Line[(l.Start + 1)..(l.End - 1)], @"\\.", "x").Length > 1).ToList();
        if (multi.Count == 0) return null;

        var corrected = at.Line;
        foreach (var (start, end, _) in Enumerable.Reverse(multi))
            corrected = corrected[..start] + "\"" + at.Line[(start + 1)..(end - 1)].Replace("\"", "\\\"") + "\"" + corrected[end..];

        return LocalFix.ReplaceLine(
            Id, "Put the text in double quotes",
            "Single quotes in Go hold one character - a rune, like `'A'`. A string goes in double quotes.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>cannot use assignment x = 5 as value</c> - <c>=</c> in an <c>if</c> where <c>==</c> was meant.</summary>
public sealed partial class GoAssignmentInCondition : ILocalFixRule
{
    public string Id => "go-assignment-in-condition";

    [GeneratedRegex(@"^syntax error: cannot use assignment .+ as value$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        if (Regex.Match(masked, @"^\s*(?:\}\s*else\s+)?(?:if|for)\s+(?<condition>[^{]+)\{") is not { Success: true } condition) return null;

        var group = condition.Groups["condition"];
        var equals = Regex.Matches(masked.Substring(group.Index, group.Length), @"(?<![=!<>:+\-*/%&|^])=(?!=)").ToList();
        if (equals is not [var only]) return null;

        var index = group.Index + only.Index;

        return LocalFix.ReplaceLine(
            Id, "Compare with == instead of =",
            "`=` stores a value; `==` compares two. An `if` needs a comparison, so it is `==`.",
            at.Source.Path, at.Number, at.Line[..index] + "==" + at.Line[(index + 1)..]);
    }
}

/// <summary><c>newline in string</c> - a string with no closing quote.</summary>
public sealed partial class GoUnclosedString : ILocalFixRule
{
    public string Id => "go-unclosed-string";

    [GeneratedRegex(@"^newline in string$|^string literal not terminated$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;
        if (Blocks.CloseString(at.Line, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, "Close the string", Blocks.CloseStringExplanation, at.Source.Path, at.Number, corrected);
    }
}

/// <summary><c>unexpected newline in argument list; possibly missing comma or )</c> - a call left open.</summary>
public sealed partial class GoMissingClosingParen : ILocalFixRule
{
    public string Id => "go-missing-closing-paren";

    [GeneratedRegex(@"^syntax error: unexpected newline in argument list; possibly missing comma or \)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (code, tail) = CodeText.SplitComment(at.Line, Syntax.CLike);
        var masked = CodeText.Mask(code, Syntax.CLike);
        if (masked.Count(c => c == '(') - masked.Count(c => c == ')') != 1) return null;

        var trimmed = code.TrimEnd();

        return LocalFix.ReplaceLine(
            Id, "Add the missing closing bracket",
            $"A `(` on line {at.Number} is never closed before the line ends.",
            at.Source.Path, at.Number, trimmed + ")" + code[trimmed.Length..] + tail);
    }
}

/// <summary><c>unexpected EOF, expected }</c> - a block never closed.</summary>
public sealed partial class GoMissingClosingBrace : ILocalFixRule
{
    public string Id => "go-missing-closing-brace";

    [GeneratedRegex(@"^syntax error: unexpected EOF, expected \}$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null) return null;
        if (context.Read(context.Frame?.File) is not { } source || !GoCode.IsGo(source)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) return null;
            }
        }

        if (unclosed.Count != 1) return null;

        var opener = unclosed.Pop();
        var last = source.Count - 1;
        while (last > opener && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener + 1}",
            $"The `{{` on line {opener + 1} is never closed, so the file ends inside it.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener]) + "}"]);
    }
}

/// <summary><c>non-declaration statement outside function body</c> on a lone <c>}</c> - or on <c>Func main()</c>.</summary>
public sealed partial class GoOutsideFunction : ILocalFixRule
{
    public string Id => "go-outside-function";

    [GeneratedRegex(@"^syntax error: non-declaration statement outside function body$")]
    private static partial Regex Message();

    private static readonly string[] TopLevel = ["func", "type", "var", "const", "import", "package"];

    private static readonly Dictionary<string, string> Borrowed = new(StringComparer.Ordinal)
    {
        ["function"] = "func", ["def"] = "func", ["fn"] = "func", ["fun"] = "func", ["Function"] = "func", ["class"] = "type",
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

        if (line.Trim() == "}")
        {
            var depth = 0;
            var stray = new List<int>();

            for (var i = 0; i < masked.Count; i++)
            {
                foreach (var c in masked[i])
                {
                    if (c == '{') depth++;
                    else if (c == '}' && depth == 0) stray.Add(i);
                    else if (c == '}') depth--;
                }
            }

            if (stray is not [var extra] || extra != number - 1) return null;

            return CCode.RemoveLine(
                Id, $"Remove the extra }} on line {number}",
                $"The file has one more `}}` than `{{`. The one on line {number} closes nothing - every block is already closed before it.",
                source.Path, number);
        }

        var first = Regex.Match(masked[number - 1], @"^(?<word>[A-Za-z_]\w*)\b");
        if (!first.Success) return null;

        var word = first.Groups["word"].Value;
        if (GoCode.Keywords.Contains(word)) return null;

        var right = Borrowed.GetValueOrDefault(word) ?? CodeText.Nearest(word, TopLevel);
        if (right is null) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {word} to {right}",
            Borrowed.ContainsKey(word) ? $"`{word}` is how another language declares that. Go writes `{right}`." : $"Go keywords are lower case: `{right}`, not `{word}`.",
            source.Path, number, right + line[word.Length..]);
    }
}

// ====================================================================== types and values

/// <summary><c>mismatched types untyped string and int</c> - a number, or a byte, added to a string.</summary>
public sealed partial class GoStringPlusNumber : ILocalFixRule
{
    public string Id => "go-string-plus-number";

    [GeneratedRegex(@"^invalid operation: (?<expression>.+) \(mismatched types (?:untyped )?(?<left>\w+) and (?:untyped )?(?<right>\w+)\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var (left, right) = (message.Groups["left"].Value, message.Groups["right"].Value);
        var number = left == "string" ? right : right == "string" ? left : null;
        if (number is null || !GoCode.Numeric.Contains(number)) return null;

        var expression = message.Groups["expression"].Value;
        if (!expression.Contains('+')) return null;

        var parts = expression.Split('+').Select(p => p.Trim()).ToList();
        var operand = parts.Where(p => !p.StartsWith('"')).ToList();
        if (operand is not [var value]) return null;

        var (source, lineNumber, line) = at;
        var index = line.IndexOf(value, StringComparison.Ordinal);
        if (index < 0 || line.IndexOf(value, index + 1, StringComparison.Ordinal) >= 0) return null;

        string wrapped, how;

        if (number is "byte" or "rune")
        {
            (wrapped, how) = ($"string({value})", $"`{value}` is a single {number} - a character's code - and Go will not add it to a string. `string({value})` makes it text.");
        }
        else if (number == "int" && GoCode.Imports(source, "strconv"))
        {
            (wrapped, how) = ($"strconv.Itoa({value})", $"Go never turns a number into text by itself. `strconv.Itoa({value})` does it.");
        }
        else if (GoCode.Imports(source, "fmt"))
        {
            (wrapped, how) = ($"fmt.Sprint({value})", $"Go never turns a number into text by itself. `fmt.Sprint({value})` does it, whatever kind of number it is.");
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(Id, $"Turn {value} into text with {wrapped[..wrapped.IndexOf('(')]}", how, source.Path, lineNumber, line[..index] + wrapped + line[(index + value.Length)..]);
    }
}

/// <summary><c>name[0] == "A"</c> - a byte compared with a string.</summary>
public sealed partial class GoByteComparedWithString : ILocalFixRule
{
    public string Id => "go-byte-compared-with-string";

    [GeneratedRegex(@"^invalid operation: .+ (?:==|!=) .+ \(mismatched types (?:byte|rune) and untyped string\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var single = Cpp.Literals(at.Line)
            .Where(l => l.Quote == '"' && Regex.IsMatch(at.Line[(l.Start + 1)..(l.End - 1)], @"^(?:[^""\\]|\\.)$"))
            .Where(l => Regex.IsMatch(at.Line[..l.Start].TrimEnd(), @"(?:==|!=)$") || Regex.IsMatch(at.Line[l.End..].TrimStart(), @"^(?:==|!=)"))
            .ToList();

        if (single is not [var (start, end, _)]) return null;

        var inner = at.Line[(start + 1)..(end - 1)];
        var character = "'" + (inner == "'" ? "\\'" : inner) + "'";

        return LocalFix.ReplaceLine(
            Id, $"Compare with the character {character}",
            $"Indexing a string gives one byte, and {at.Line[start..end]} is a string - Go will not compare the two. A single character is " +
            $"written in single quotes: {character}.",
            at.Source.Path, at.Number, at.Line[..start] + character + at.Line[end..]);
    }
}

/// <summary><c>cannot use count (variable of type int) as float64 value</c> - a number of one kind where another is wanted.</summary>
public sealed partial class GoNumericConversion : ILocalFixRule
{
    public string Id => "go-numeric-conversion";

    [GeneratedRegex(@"^cannot use (?<name>[A-Za-z_]\w*) \(variable of type (?<from>\w+)\) as (?<to>\w+) value in ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var (from, to) = (message.Groups["from"].Value, message.Groups["to"].Value);
        if (!GoCode.Numeric.Contains(from) || !GoCode.Numeric.Contains(to)) return null;

        var name = message.Groups["name"].Value;
        var hits = Js.Unqualified(CodeText.Mask(at.Line, Syntax.CLike), name)
            .Where(i => !Regex.IsMatch(at.Line[..i], @"\bvar\s+$"))
            .ToList();

        if (hits is not [var index]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Convert {name} with {to}({name})",
            $"Go never converts between number types by itself, even from `{from}` to `{to}` - it has to be asked: `{to}({name})`.",
            at.Source.Path, at.Number, at.Line[..index] + $"{to}({name})" + at.Line[(index + name.Length)..]);
    }
}

/// <summary><c>too many return values ... want ()</c> - a function returning a value it never declared.</summary>
public sealed partial class GoMissingReturnType : ILocalFixRule
{
    public string Id => "go-missing-return-type";

    [GeneratedRegex(@"^too many return values$")]
    private static partial Regex Message();

    [GeneratedRegex(@"have \((?<have>[^)]+)\)\s+want \(\)")]
    private static partial Regex Types();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;
        if (Types().Match(context.Error.RawText) is not { Success: true } types) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        if (GoCode.Function(masked, at.Number - 1) is not { } function) return null;

        var header = Regex.Match(masked[function.Header], @"^func\s+(?:\([^)]*\)\s*)?\w+\s*\([^)]*(?<close>\))\s*\{\s*$");
        if (!header.Success) return null;

        var have = types.Groups["have"].Value.Trim();
        var result = have.Contains(',') ? $"({have})" : have;
        var close = header.Groups["close"].Index + 1;
        var original = at.Source.Lines[function.Header];

        return LocalFix.ReplaceLine(
            Id, $"Declare that it returns {result}",
            $"A Go function says what it returns after its brackets, and this one says nothing - yet it returns {(have.Contains(',') ? "values" : "a value")} of type `{have}`.",
            at.Source.Path, function.Header + 1, original[..close] + " " + result + original[close..]);
    }
}

/// <summary><c>not enough return values ... have (error) want (int, error)</c> - a return missing its leading values.</summary>
public sealed partial class GoNotEnoughReturnValues : ILocalFixRule
{
    public string Id => "go-not-enough-return-values";

    [GeneratedRegex(@"^not enough return values$")]
    private static partial Regex Message();

    [GeneratedRegex(@"have \((?<have>[^)]*)\)\s+want \((?<want>[^)]+)\)")]
    private static partial Regex Types();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;
        if (Types().Match(context.Error.RawText) is not { Success: true } types) return null;

        var have = types.Groups["have"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var want = types.Groups["want"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (have.Length >= want.Length || !want[^have.Length..].SequenceEqual(have)) return null;

        var returned = Regex.Match(at.Line, @"^(?<lead>\s*return\s+)(?<values>.+)$");
        if (!returned.Success) return null;

        var zeros = want[..(want.Length - have.Length)].Select(GoCode.ZeroValue).ToList();

        return LocalFix.ReplaceLine(
            Id, $"Return {string.Join(", ", zeros)} as well",
            $"The function returns `({types.Groups["want"].Value})`, and every `return` has to give all of them. On this path there is no real " +
            $"{string.Join(" or ", want[..zeros.Count])} to give, so it returns the zero value - which is what callers ignore when the error is not nil.",
            at.Source.Path, at.Number, returned.Groups["lead"].Value + string.Join(", ", zeros) + ", " + returned.Groups["values"].Value);
    }
}

/// <summary><c>assignment mismatch: 1 variable but strconv.Atoi returns 2 values</c>.</summary>
public sealed partial class GoTwoValues : ILocalFixRule
{
    public string Id => "go-two-values";

    [GeneratedRegex(@"^assignment mismatch: 1 variable but (?<call>[\w.]+) returns 2 values$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var assignment = Regex.Match(at.Line, @"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*(?<op>:=|=)\s*(?<rest>.+)$");
        if (!assignment.Success) return null;

        return LocalFix.ReplaceLine(
            Id, $"Take both results: {assignment.Groups["name"].Value}, _",
            $"`{message.Groups["call"].Value}` returns two things - the value and an error - and both have to be taken. `_` throws the error away, " +
            $"which is only right when it cannot fail; for input that can be wrong, name it `err` and check `if err != nil`.",
            at.Source.Path, at.Number, $"{assignment.Groups["lead"].Value}{assignment.Groups["name"].Value}, _ {assignment.Groups["op"].Value} {assignment.Groups["rest"].Value}");
    }
}

/// <summary><c>append(items, 3) (value of type []int) is not used</c> - the result of append thrown away.</summary>
public sealed partial class GoAppendNotUsed : ILocalFixRule
{
    public string Id => "go-append-not-used";

    [GeneratedRegex(@"^append\((?<slice>[A-Za-z_][\w.]*),.*\) \(value of type [^)]+\) is not used$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var slice = message.Groups["slice"].Value;
        var statement = Regex.Match(at.Line, $@"^(?<lead>\s*)(?<call>append\(\s*{Regex.Escape(slice)}\s*,.+\))\s*$");
        if (!statement.Success) return null;

        return LocalFix.ReplaceLine(
            Id, $"Store the result: {slice} = append(...)",
            "`append` does not change the slice it is given - it returns a new one with the values added, and that has to be stored back.",
            at.Source.Path, at.Number, $"{statement.Groups["lead"].Value}{slice} = {statement.Groups["call"].Value}");
    }
}

/// <summary><c>Square does not implement Shape (method Area has pointer receiver)</c>.</summary>
public sealed partial class GoPointerReceiver : ILocalFixRule
{
    public string Id => "go-pointer-receiver";

    [GeneratedRegex(@"^cannot use (?<type>\w+)\{.*does not implement (?<interface>\w+) \(method (?<method>\w+) has pointer receiver\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;

        var type = message.Groups["type"].Value;
        var hits = Regex.Matches(CodeText.Mask(at.Line, Syntax.CLike), $@"(?<![\w&.]){Regex.Escape(type)}\s*\{{").ToList();
        if (hits is not [var hit]) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use a pointer: &{type}{{...}}",
            $"`{message.Groups["method"].Value}` is defined on `*{type}` - a pointer - so only a pointer to a `{type}` has it, and only a pointer " +
            $"satisfies `{message.Groups["interface"].Value}`. `&{type}{{...}}` makes the value and hands over its address.",
            at.Source.Path, at.Number, at.Line[..hit.Index] + "&" + at.Line[hit.Index..]);
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
        if (GoCode.Compile(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;
        if (Have().Match(context.Error.RawText) is not { Success: true } have) return null;

        var (want, wrong) = (message.Groups["want"].Value, have.Groups["have"].Value);
        if (!want.Equals(wrong, StringComparison.OrdinalIgnoreCase) || want == wrong) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var type = Regex.Escape(message.Groups["type"].Value);

        var declaration = new Regex($@"^func\s*\(\s*\w+\s+\*?{type}\s*\)\s*(?<name>{Regex.Escape(wrong)})\s*\(");
        var lines = Enumerable.Range(0, masked.Count).Where(i => declaration.IsMatch(masked[i])).ToList();
        if (lines is not [var index]) return null;

        // Called by the old name somewhere, the rename would break that call.
        if (masked.Any(text => Regex.IsMatch(text, $@"\.\s*{Regex.Escape(wrong)}\s*\("))) return null;

        var name = declaration.Match(masked[index]).Groups["name"];

        return LocalFix.ReplaceLine(
            Id, $"Rename {wrong} to {want}",
            $"The interface asks for `{want}`, and Go names are case-sensitive: `{wrong}` is a different method. A capital letter is also what " +
            "makes a method visible outside its package.",
            source.Path, index + 1, source.Lines[index][..name.Index] + want + source.Lines[index][(name.Index + name.Length)..]);
    }
}

// ====================================================================== running

/// <summary><c>index out of range [3] with length 3</c> - a loop that runs to <c>&lt;= len(...)</c>.</summary>
public sealed partial class GoIndexLoop : ILocalFixRule
{
    public string Id => "go-index-loop";

    [GeneratedRegex(@"index out of range \[(?<index>\d+)\] with length (?<length>\d+)")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Runtime(context.Error, Message()) is not { } message || GoCode.Locate(context) is not { } at) return null;
        if (message.Groups["index"].Value != message.Groups["length"].Value) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);
        var loop = new Regex(@"\bfor\s+(?<var>\w+)\s*:=\s*0\s*;\s*\k<var>\s*(?<op><=)\s*len\((?<slice>[^)]+)\)\s*;");

        for (var k = at.Number - 1; k >= Math.Max(0, at.Number - 16); k--)
        {
            if (loop.Match(masked[k]) is not { Success: true } header) continue;

            var op = header.Groups["op"];
            var original = at.Source.Lines[k];

            return LocalFix.ReplaceLine(
                Id, "Stop the loop at the last index: <",
                $"`{header.Groups["slice"].Value}` has {message.Groups["length"].Value} elements, numbered 0 to " +
                $"{int.Parse(message.Groups["length"].Value) - 1}. The loop on line {k + 1} runs while `{header.Groups["var"].Value} <= len(...)`, " +
                "so its last step asks for one past the end.",
                at.Source.Path, k + 1, original[..op.Index] + "<" + original[(op.Index + op.Length)..]);
        }

        return null;
    }
}

/// <summary><c>assignment to entry in nil map</c> - a map declared but never made.</summary>
public sealed partial class GoNilMap : ILocalFixRule
{
    public string Id => "go-nil-map";

    [GeneratedRegex(@"^assignment to entry in nil map$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Runtime(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (Regex.Matches(masked[at.Number - 1], @"(?<![\w.])(?<map>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?)\s*\[").ToList() is not [var use]) return null;

        var map = use.Groups["map"].Value;

        if (!map.Contains('.'))
        {
            var declaration = new Regex($@"^(?<lead>\s*)var\s+{Regex.Escape(map)}\s+(?<type>map\[[^\]]+\]\S+)\s*$");
            var lines = Enumerable.Range(0, at.Number - 1).Where(i => declaration.IsMatch(masked[i])).ToList();
            if (lines is not [var index]) return null;

            var match = declaration.Match(source.Lines[index]);

            return LocalFix.ReplaceLine(
                Id, $"Make the map: {map} := make({match.Groups["type"].Value})",
                $"`var {map} {match.Groups["type"].Value}` declares a map but does not make one - it is `nil`, and storing into a nil map panics. " +
                "`make` creates the map to store into.",
                source.Path, index + 1, $"{match.Groups["lead"].Value}{map} := make({match.Groups["type"].Value})");
        }

        var parts = map.Split('.');
        var literal = new Regex($@"(?<![\w.]){Regex.Escape(parts[0])}\s*:=\s*(?<type>[A-Z]\w*)\{{\s*\}}");

        for (var i = at.Number - 2; i >= 0; i--)
        {
            if (literal.Match(masked[i]) is not { Success: true } made) continue;

            var type = made.Groups["type"].Value;
            var field = masked.Select(text => Regex.Match(text, $@"^\s*{Regex.Escape(parts[1])}\s+(?<type>map\[[^\]]+\]\S+)\s*$")).FirstOrDefault(m => m.Success);
            if (field is null || !masked.Any(text => Regex.IsMatch(text, $@"^type\s+{Regex.Escape(type)}\s+struct\b"))) return null;

            var braces = made.Index + made.Length;
            var original = source.Lines[i];
            var open = original.LastIndexOf('{', braces - 1);

            return LocalFix.ReplaceLine(
                Id, $"Make the map in {type}{{...}}",
                $"`{type}{{}}` leaves its `{parts[1]}` map `nil`, and storing into a nil map panics. Making it as the struct is created - " +
                $"`{parts[1]}: make({field.Groups["type"].Value})` - gives it a map to store into.",
                source.Path, i + 1, original[..(open + 1)] + $"{parts[1]}: make({field.Groups["type"].Value})" + original[(braces - 1)..]);
        }

        return null;
    }
}

/// <summary><c>all goroutines are asleep - deadlock!</c> from a channel nothing else will ever touch.</summary>
public sealed partial class GoChannelDeadlock : ILocalFixRule
{
    public string Id => "go-channel-deadlock";

    [GeneratedRegex(@"^all goroutines are asleep - deadlock!$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (GoCode.Runtime(context.Error, Message()) is null || GoCode.Locate(context) is not { } at) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var raw = context.Error.RawText;
        var line = masked[at.Number - 1];

        if (GoCode.Function(masked, at.Number - 1) is not { } function) return null;
        var body = Enumerable.Range(function.Header + 1, function.Close - function.Header - 1).ToList();

        // A nil channel: declared with var and never made.
        if (raw.Contains("(nil chan)", StringComparison.Ordinal))
        {
            if (Regex.Match(line, @"<-\s*(?<ch>[A-Za-z_]\w*)|(?<ch>[A-Za-z_]\w*)\s*<-") is not { Success: true } use) return null;

            var declaration = new Regex($@"^(?<lead>\s*)var\s+{Regex.Escape(use.Groups["ch"].Value)}\s+(?<type>chan\s+\S+)\s*$");
            var lines = body.Where(i => declaration.IsMatch(masked[i])).ToList();
            if (lines is not [var index]) return null;

            var match = declaration.Match(source.Lines[index]);

            return LocalFix.ReplaceLine(
                Id, "Make the channel",
                $"`var {use.Groups["ch"].Value} {match.Groups["type"].Value}` declares a channel without making one. A nil channel blocks every send " +
                "and receive for ever, so nothing could move. `make` creates the channel.",
                source.Path, index + 1, $"{match.Groups["lead"].Value}{use.Groups["ch"].Value} := make({match.Groups["type"].Value})");
        }

        // Nothing else runs at the same time to take part, so the buffer or a close is the whole answer.
        if (body.Any(i => Regex.IsMatch(masked[i], @"^\s*go\s"))) return null;

        if (raw.Contains("[chan send]", StringComparison.Ordinal) && Regex.Match(line, @"^\s*(?<ch>[A-Za-z_]\w*)\s*<-") is { Success: true } send)
        {
            var ch = Regex.Escape(send.Groups["ch"].Value);
            var made = new Regex($@"(?<![\w.]){ch}\s*:=\s*make\(\s*chan\s+(?<type>[^,)]+)(?<close>\))");
            var lines = body.Where(i => made.IsMatch(masked[i])).ToList();
            if (lines is not [var index]) return null;

            var receive = body.FirstOrDefault(i => Regex.IsMatch(masked[i], $@"<-\s*{ch}\b"), -1);
            var sends = body.Count(i => i > index && (receive < 0 || i < receive) && Regex.IsMatch(masked[i], $@"^\s*{ch}\s*<-"));
            if (sends == 0) return null;

            var close = made.Match(masked[index]).Groups["close"].Index;
            var original = source.Lines[index];

            return LocalFix.ReplaceLine(
                Id, $"Give the channel room for {sends}: make(chan ..., {sends})",
                "A send on an unbuffered channel waits until another goroutine receives it - and here nothing else is running, so it waits " +
                $"for ever. A buffer of {sends} lets the sends complete before this function goes on to receive them.",
                source.Path, index + 1, original[..close] + $", {sends}" + original[close..]);
        }

        if (raw.Contains("[chan receive]", StringComparison.Ordinal) && Regex.Match(line, @"^\s*for\s+.*\brange\s+(?<ch>[A-Za-z_]\w*)\s*\{") is { Success: true } range)
        {
            var ch = Regex.Escape(range.Groups["ch"].Value);
            if (body.Any(i => Regex.IsMatch(masked[i], $@"\bclose\(\s*{ch}\s*\)"))) return null;
            if (!body.Any(i => i < at.Number - 1 && Regex.IsMatch(masked[i], $@"^\s*{ch}\s*<-"))) return null;

            return LocalFix.Insert(
                Id, $"Close {range.Groups["ch"].Value} before ranging over it",
                "`range` over a channel keeps receiving until the channel is closed. Nothing closes this one, so once the values sent are " +
                "used up it waits for ever. `close` after the last send lets the loop finish.",
                source.Path, at.Number, [CodeText.Indentation(source.Lines[at.Number - 1]) + $"close({range.Groups["ch"].Value})"]);
        }

        return null;
    }
}

/// <summary><c>function main is undeclared in the main package</c> - <c>Main</c> or <c>mian</c>.</summary>
public sealed partial class GoMainName : ILocalFixRule
{
    public string Id => "go-main-name";

    [GeneratedRegex(@"^\s*func\s+(?<name>\w+)\s*\(\s*\)\s*\{")]
    private static partial Regex Function();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "go", Message: "function main is undeclared in the main package" }) return null;

        var functions = new List<(SourceFile Source, int Index, Group Name)>();

        foreach (var source in GoCode.Files(context))
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);

            for (var i = 0; i < masked.Count; i++)
            {
                if (Function().Match(masked[i]) is not { Success: true } function) continue;
                if (function.Groups["name"].Value == "main") return null;

                functions.Add((source, i, function.Groups["name"]));
            }
        }

        if (CodeText.Nearest("main", functions.Select(f => f.Name.Value)) is not { } wrong) return null;
        if (functions.Where(f => f.Name.Value == wrong).ToList() is not [var (file, index, name)]) return null;

        var line = file.Lines[index];

        return LocalFix.ReplaceLine(
            Id, $"Rename {wrong} to main",
            $"A Go program starts at a function called exactly `main`, in lower case, and there is none - `{wrong}` is a letter away.",
            file.Path, index + 1, line[..name.Index] + "main" + line[(name.Index + name.Length)..]);
    }
}

/// <summary><c>package command-line-arguments is not a main package</c> - a program whose file says <c>package app</c>.</summary>
public sealed partial class GoPackageMain : ILocalFixRule
{
    public string Id => "go-package-main";

    [GeneratedRegex(@"is not a main package$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "go" || !Message().IsMatch(context.Error.Message ?? "")) return null;

        var programs = GoCode.Files(context)
            .Where(source => source.Lines.Any(line => Regex.IsMatch(line, @"^\s*func\s+main\s*\(\s*\)")))
            .Select(source => (Source: source, Index: Enumerable.Range(0, source.Count).FirstOrDefault(i => Regex.IsMatch(source.Lines[i], @"^\s*package\s+\w+"), -1)))
            .Where(x => x.Index >= 0 && !Regex.IsMatch(x.Source.Lines[x.Index], @"^\s*package\s+main\b"))
            .ToList();

        if (programs is not [var (file, index)]) return null;

        var package = Regex.Match(file.Lines[index], @"^(?<lead>\s*package\s+)(?<name>\w+)");

        return LocalFix.ReplaceLine(
            Id, "Declare package main",
            $"Only `package main` can be run as a program - `package {package.Groups["name"].Value}` is a library for other code to import, even " +
            "with a `main` function in it.",
            file.Path, index + 1, package.Groups["lead"].Value + "main" + file.Lines[index][(package.Index + package.Length)..]);
    }
}
