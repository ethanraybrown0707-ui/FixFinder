using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Go a program's own code needs, read from how it is written: the newest part of the language or of the standard
/// library it uses - a type parameter is Go 1.18's, the built-in min 1.21's, ranging over an integer 1.22's, the iter
/// package 1.23's, a generic method 1.27's - so it is built with a Go that has it, and how it ran says which and why.
/// </summary>
/// <remarks>
/// Which Go made each part of the language is from the release notes at go.dev/doc, checked against what Go 1.27's own
/// compiler says when a module's go line is older ("generic method requires go1.27 or later"); which Go added each
/// package and function is from the api/go1.N.txt files every Go has, where each release lists what it added. Only forms
/// that cannot be anything else are counted - min called where the package has no min of its own, a function called
/// through the name a standard package was imported under - so the Go said to be needed is never more than the code needs.
/// </remarks>
public static partial class GoFeaturesUsed
{
    /// <summary>Something Go made part of its language, with how to find it in the code with its comments and text blanked out.</summary>
    private sealed record Feature(LanguageVersion Since, string Uses, Regex Written, string Added = "made part of the language");

    private const RegexOptions Compiled = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline;

    private static LanguageVersion Go(int minor) => new(1, minor);

    /// <summary>What each Go made part of the language, newest first.</summary>
    /// <remarks>
    /// A generic method is told from a function literal by starting its line, as every declaration gofmt writes does. A
    /// generic type alias compiles with a go line of 1.23 - the compiler says so - but Go 1.23 had it only when built with
    /// GOEXPERIMENT=aliastypeparams; 1.24 is the first Go that has it as it comes.
    /// </remarks>
    private static readonly Feature[] Features =
    [
        new(Go(27), "declares a generic method", new(@"^func\s*\([^()\n]*\)\s*[A-Za-z_]\w*\s*\[", Compiled)),
        new(Go(26), "passes an expression to new", new(@"(?<![\w.])new\s*\(\s*(?:[0-9""'`]|-\s*[0-9]|(?:true|false)\s*\)|(?!(?:func|chan|map|struct|interface)\b)[A-Za-z_][\w.]*\s*[({])", Compiled)),
        new(Go(24), "declares a generic type alias", new(@"\btype\s+[A-Za-z_]\w*\s*\[[^\]\n]*\]\s*=", Compiled)),
        new(Go(23), "ranges over a function", new(@"\brange\s+func\s*\(", Compiled)),
        new(Go(22), "ranges over an integer", new(@"\brange\s+(?:[0-9]|len\s*\(|cap\s*\()", Compiled)),
        new(Go(18), "declares a type parameter", new(@"\bfunc\s+[A-Za-z_]\w*\s*\[|\btype\s+[A-Za-z_]\w*\s*\[\s*[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*\s+(?:~|[A-Za-z_\[])", Compiled)),
    ];

    /// <summary>The built-in functions Go 1.21 added - counted only where the program has nothing of its own by the name.</summary>
    private static readonly string[] BuiltInsOf121 = ["min", "max", "clear"];

    /// <summary>The standard packages each Go added, as api/go1.N.txt first lists them.</summary>
    private static readonly Dictionary<string, LanguageVersion> PackageAdded = new(StringComparer.Ordinal)
    {
        ["crypto/mldsa"] = Go(27),
        ["encoding/json/jsontext"] = Go(27),
        ["encoding/json/v2"] = Go(27),
        ["uuid"] = Go(27),
        ["crypto/hpke"] = Go(26),
        ["testing/synctest"] = Go(25),
        ["crypto/hkdf"] = Go(24),
        ["crypto/mlkem"] = Go(24),
        ["crypto/pbkdf2"] = Go(24),
        ["crypto/sha3"] = Go(24),
        ["weak"] = Go(24),
        ["iter"] = Go(23),
        ["structs"] = Go(23),
        ["unique"] = Go(23),
        ["go/version"] = Go(22),
        ["math/rand/v2"] = Go(22),
        ["cmp"] = Go(21),
        ["log/slog"] = Go(21),
        ["maps"] = Go(21),
        ["slices"] = Go(21),
        ["testing/slogtest"] = Go(21),
    };

    /// <summary>Functions each Go added to a package an earlier Go already had, as api/go1.N.txt lists them.</summary>
    private static readonly Dictionary<(string Package, string Function), LanguageVersion> FunctionAdded = Functions(
        (Go(27), "bytes", ["CutLast"]),
        (Go(27), "strings", ["CutLast"]),
        (Go(26), "errors", ["AsType"]),
        (Go(26), "log/slog", ["NewMultiHandler"]),
        (Go(25), "log/slog", ["GroupAttrs"]),
        (Go(25), "reflect", ["TypeAssert"]),
        (Go(24), "bytes", ["FieldsFuncSeq", "FieldsSeq", "Lines", "SplitAfterSeq", "SplitSeq"]),
        (Go(24), "os", ["OpenInRoot", "OpenRoot"]),
        (Go(24), "strings", ["FieldsFuncSeq", "FieldsSeq", "Lines", "SplitAfterSeq", "SplitSeq"]),
        (Go(23), "maps", ["All", "Collect", "Insert", "Keys", "Values"]),
        (Go(23), "os", ["CopyFS"]),
        (Go(23), "reflect", ["SliceAt"]),
        (Go(23), "slices", ["All", "AppendSeq", "Backward", "Chunk", "Collect", "Repeat", "Sorted", "SortedFunc", "SortedStableFunc", "Values"]),
        (Go(22), "cmp", ["Or"]),
        (Go(22), "reflect", ["TypeFor"]),
        (Go(22), "slices", ["Concat"]),
        (Go(21), "bytes", ["ContainsFunc"]),
        (Go(21), "context", ["AfterFunc", "WithDeadlineCause", "WithTimeoutCause", "WithoutCancel"]),
        (Go(21), "strings", ["ContainsFunc"]),
        (Go(21), "sync", ["OnceFunc", "OnceValue", "OnceValues"]));

    private static Dictionary<(string, string), LanguageVersion> Functions(params (LanguageVersion Since, string Package, string[] Names)[] added) =>
        added.SelectMany(each => each.Names.Select(name => ((each.Package, name), each.Since))).ToDictionary(pair => pair.Item1, pair => pair.Since);

    [GeneratedRegex(@"^[ \t]*import\b\s*(?:\((?<block>[^)]*)\)|(?<single>[^\n]*))", RegexOptions.Multiline)]
    private static partial Regex ImportDeclaration();

    [GeneratedRegex(@"(?:(?<name>[A-Za-z_]\w*|\.)\s+)?(?<quote>[""`])[^""`\n]*\k<quote>")]
    private static partial Regex ImportSpec();

    [GeneratedRegex(@"(?<![\w.])(?<package>[A-Za-z_]\w*)\s*\.\s*(?<member>[A-Za-z_]\w*)")]
    private static partial Regex Selector();

    [GeneratedRegex(@"(?<!\w)(?<name>[A-Za-z_]\w*)\s*\.\s*Go\s*\(")]
    private static partial Regex GoMethodCall();

    [GeneratedRegex(@"^v\d+$")]
    private static partial Regex MajorVersionSuffix();

    /// <summary>One file read: its name, its code with comments and text blanked out, and what it imports under which name.</summary>
    private sealed record Read(string Name, string Code, IReadOnlyList<(string Name, string Path, int At)> Imports);

    private static readonly ConcurrentDictionary<string, (DateTime Written, Read? File)> Remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The newest Go the program this file is part of needs - the file and the rest of its package - and why; or null when nothing was found.</summary>
    public static ToolchainChoice.AtLeast? For(string goFile)
    {
        IEnumerable<string> files;

        try
        {
            files = ProgramFiles.Of(goFile).Where(file => Path.GetExtension(file).Equals(".go", StringComparison.OrdinalIgnoreCase)).Prepend(goFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [goFile];
        }

        return Of(files.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The newest Go these files of one package need, and why.</summary>
    public static ToolchainChoice.AtLeast? Of(IEnumerable<string> goFiles)
    {
        var read = goFiles.Select(ReadFile).OfType<Read>().ToList();

        // A package's own min, max or clear - a function, a variable, a parameter - is what its calls of the name call.
        var ownNames = BuiltInsOf121.Where(name => read.Any(file => HasItsOwn(file.Code, name))).ToHashSet(StringComparer.Ordinal);
        var newIsItsOwn = read.Any(file => HasItsOwn(file.Code, "new"));

        (LanguageVersion Since, string Because)? newest = null;

        void Consider(LanguageVersion since, string because)
        {
            if (newest is null || since > newest.Value.Since) newest = (since, because);
        }

        foreach (var file in read)
        {
            foreach (var feature in Features)
            {
                if (newest is { } found && found.Since >= feature.Since) break;
                if (feature.Since == Go(26) && newIsItsOwn) continue;
                if (feature.Written.Match(file.Code) is not { Success: true } match) continue;

                Consider(feature.Since, $"{file.Name} {feature.Uses} at line {LineOf(file.Code, match.Index)}, which Go {feature.Since} {feature.Added}");
            }

            foreach (var name in BuiltInsOf121.Where(name => !ownNames.Contains(name)))
            {
                if (newest is { } found && found.Since >= Go(21)) break;
                if (Regex.Match(file.Code, $@"(?<![\w.]){name}\s*\(") is { Success: true } call)
                    Consider(Go(21), $"{file.Name} calls the built-in {name} at line {LineOf(file.Code, call.Index)}, which Go 1.21 made part of the language");
            }

            foreach (var (_, path, at) in file.Imports)
            {
                if (PackageAdded.TryGetValue(path, out var since))
                    Consider(since, $"{file.Name} imports {path} at line {LineOf(file.Code, at)}, which Go {since} added");
            }

            var importedAs = file.Imports.Where(import => import.Name is not ("_" or "."))
                .GroupBy(import => import.Name, StringComparer.Ordinal)
                .ToDictionary(names => names.Key, names => names.First().Path, StringComparer.Ordinal);

            foreach (Match selector in Selector().Matches(file.Code))
            {
                if (!importedAs.TryGetValue(selector.Groups["package"].Value, out var path)) continue;
                if (!FunctionAdded.TryGetValue((path, selector.Groups["member"].Value), out var since)) continue;
                if (newest is { } found && found.Since >= since) continue;

                Consider(since, $"{file.Name} calls {ShortName(path)}.{selector.Groups["member"].Value} at line {LineOf(file.Code, selector.Index)}, which Go {since} added");
            }

            if (importedAs.FirstOrDefault(pair => pair.Value == "sync").Key is { } sync && (newest is null || newest.Value.Since < Go(25)))
            {
                foreach (Match call in GoMethodCall().Matches(file.Code))
                {
                    if (!IsAWaitGroup(file.Code, call.Groups["name"].Value, sync)) continue;

                    Consider(Go(25), $"{file.Name} calls a sync.WaitGroup's Go method at line {LineOf(file.Code, call.Index)}, which Go 1.25 added");
                    break;
                }
            }
        }

        return newest is { } need ? new ToolchainChoice.AtLeast(need.Since, need.Because) : null;
    }

    /// <summary>
    /// Whether the code has a min, max, clear or new of its own - a function, a method, an interface's method, or a name
    /// used other than by calling it: a variable, a parameter, a field - so a call of the name may be a call of that.
    /// </summary>
    private static bool HasItsOwn(string code, string name) =>
        Regex.IsMatch(code, $@"\bfunc\s+{name}\s*[(\[]|\bfunc\s*\([^()\n]*\)\s*{name}\s*[(\[]|\binterface\s*\{{[^{{}}]*?(?<![\w.]){name}\s*\(|(?<![\w.]){name}\b(?!\s*\()");

    /// <summary>Whether the name is declared in the file as a sync.WaitGroup - var wg sync.WaitGroup, wg := &amp;sync.WaitGroup{}, a field or a parameter.</summary>
    private static bool IsAWaitGroup(string code, string name, string sync) =>
        Regex.IsMatch(code, $@"(?<![\w.]){Regex.Escape(name)}\s+\*?{sync}\s*\.\s*WaitGroup\b|(?<![\w.]){Regex.Escape(name)}\s*:?=\s*(?:&\s*{sync}\s*\.\s*WaitGroup\s*\{{|{sync}\s*\.\s*WaitGroup\s*\{{|new\s*\(\s*{sync}\s*\.\s*WaitGroup\s*\))");

    /// <summary>The name a standard package is known by: the last part of its path, or the one before a major version - rand for math/rand/v2.</summary>
    private static string ShortName(string path)
    {
        var parts = path.Split('/');
        return parts.Length > 1 && MajorVersionSuffix().IsMatch(parts[^1]) ? parts[^2] : parts[^1];
    }

    private static Read? ReadFile(string file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (Remembered.TryGetValue(file, out var known) && known.Written == written) return known.File;

            var text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
            var code = Masked(text);
            var read = new Read(Path.GetFileName(file), code, ImportsOf(text, code));

            Remembered[file] = (written, read);
            return read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a file imports, under which name: the paths are read from the text itself, where the blanked code has only
    /// their quotes - at the same places, as blanking keeps every character where it was.
    /// </summary>
    private static List<(string Name, string Path, int At)> ImportsOf(string text, string code)
    {
        var imports = new List<(string, string, int)>();

        foreach (Match declaration in ImportDeclaration().Matches(code))
        {
            var specs = declaration.Groups["block"].Success ? declaration.Groups["block"] : declaration.Groups["single"];

            foreach (Match spec in ImportSpec().Matches(specs.Value))
            {
                var opening = specs.Index + spec.Groups["quote"].Index;
                var closing = specs.Index + spec.Index + spec.Length - 1;
                var path = text[(opening + 1)..closing];
                var name = spec.Groups["name"].Success ? spec.Groups["name"].Value : ShortName(path);

                imports.Add((name, path, opening));
            }
        }

        return imports;
    }

    private static int LineOf(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    /// <summary>
    /// Go code with its comments blanked out and the insides of its strings, runes and raw strings too - every character
    /// kept where it was, and every line break, so a place in one is the same place in the other.
    /// </summary>
    internal static string Masked(string text)
    {
        var masked = new StringBuilder(text.Length);
        var at = 0;

        void Blank(int until)
        {
            for (; at < until && at < text.Length; at++) masked.Append(text[at] is '\n' or '\r' ? text[at] : ' ');
        }

        void Keep()
        {
            if (at >= text.Length) return;
            masked.Append(text[at]);
            at++;
        }

        while (at < text.Length)
        {
            var character = text[at];
            var next = at + 1 < text.Length ? text[at + 1] : '\0';

            if (character == '/' && next == '/')
            {
                var lineEnd = text.IndexOf('\n', at);
                Blank(lineEnd < 0 ? text.Length : lineEnd);
            }
            else if (character == '/' && next == '*')
            {
                var close = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                Blank(close < 0 ? text.Length : close + 2);
            }
            else if (character == '`')
            {
                // A raw string has no escapes and can run over lines.
                Keep();
                var close = text.IndexOf('`', at);
                Blank(close < 0 ? text.Length : close);
                Keep();
            }
            else if (character is '"' or '\'')
            {
                Keep();
                Blank(ClosingOf(text, at, character));
                if (at < text.Length && text[at] == character) Keep();
            }
            else
            {
                Keep();
            }
        }

        return masked.ToString();
    }

    /// <summary>Where the quote opened before <paramref name="from"/> closes, past any escaped one - or the end of the line, for one left open.</summary>
    private static int ClosingOf(string text, int from, char quote)
    {
        for (var at = from; at < text.Length; at++)
        {
            if (text[at] == '\\')
            {
                at++;
                continue;
            }

            if (text[at] == '\n' || text[at] == quote) return at;
        }

        return text.Length;
    }
}
