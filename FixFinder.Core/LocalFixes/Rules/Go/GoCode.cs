using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

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
    public static readonly Dictionary<string, string> StandardPackages = new(StringComparer.Ordinal)
    {
        ["fmt"] = "fmt", ["strings"] = "strings", ["strconv"] = "strconv", ["math"] = "math", ["os"] = "os", ["time"] = "time",
        ["sort"] = "sort", ["errors"] = "errors", ["sync"] = "sync", ["bufio"] = "bufio", ["unicode"] = "unicode",
        ["bytes"] = "bytes", ["io"] = "io", ["rand"] = "math/rand", ["filepath"] = "path/filepath", ["json"] = "encoding/json",
        ["http"] = "net/http", ["regexp"] = "regexp", ["slices"] = "slices", ["maps"] = "maps", ["cmp"] = "cmp", ["log"] = "log",
        ["utf8"] = "unicode/utf8", ["atomic"] = "sync/atomic", ["context"] = "context", ["reflect"] = "reflect",
        ["exec"] = "os/exec", ["big"] = "math/big", ["csv"] = "encoding/csv", ["heap"] = "container/heap", ["list"] = "container/list",
    };

    public static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
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

    public static Match? CompileMessage(ParsedError error, Regex message) =>
        error.LanguageId == "go" && error.ExceptionType == "compile error" && message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static Match? RuntimeMessage(ParsedError error, Regex message) =>
        error.LanguageId == "go" && error.ExceptionType is not ("compile error" or "link error") &&
        message.Match(error.Message ?? "") is { Success: true } m ? m : null;

    public static (SourceFile Source, int Number, string Line)? LocateError(LocalFixContext context, ParsedError error)
    {
        if (LocalFixContext.OwnFrame(error) is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsGo(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context) => LocateError(context, context.Error);

    /// <summary>The other errors in the same build, with the message each one matched.</summary>
    public static IEnumerable<(ParsedError Error, Match Message)> OtherErrors(LocalFixContext context, Regex message) =>
        context.AllErrors.Where(e => e.LanguageId == "go").Select(e => (Error: e, Message: message.Match(e.Message ?? ""))).Where(x => x.Message.Success);

    public static IEnumerable<SourceFile> SourceFiles(LocalFixContext context)
    {
        if (context.SourceRoot is not { } root || !Directory.Exists(root)) yield break;

        foreach (var path in Directory.EnumerateFiles(root, "*.go").Take(200))
            if (SourceFile.Read(path) is { } source) yield return source;
    }

    public static bool HasImport(SourceFile source, string path) =>
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
    public static (int Header, int Close)? EnclosingFunction(IReadOnlyList<string> masked, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (!Regex.IsMatch(masked[i], @"^func\b")) continue;
            return Brackets.FirstBlockEnd(masked, i) is { } close && close >= index ? (i, close) : null;
        }

        return null;
    }

    public static string ZeroValue(string type) => type.Trim() switch
    {
        var t when NumericTypes.Contains(t) => "0",
        "string" => "\"\"",
        "bool" => "false",
        _ => "nil",
    };

    private static readonly Dictionary<string, IReadOnlyList<string>> Cache = new(StringComparer.Ordinal);

    [GeneratedRegex(@"^\s*(?:func|type|var|const)\s+(?:\([^)]*\)\s*)?(?<name>[A-Z]\w*)")]
    private static partial Regex Declared();

    /// <summary>Only standard packages are asked about - nothing from the program or the internet is fetched or run.</summary>
    public static IReadOnlyList<string> ExportedNames(string path)
    {
        if (!StandardPackages.ContainsValue(path)) return [];

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
