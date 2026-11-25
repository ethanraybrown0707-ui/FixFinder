using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// A name misspelt - prnt for print, console.lg for console.log, std::cot for std::cout - that the language names in its own
/// words without saying what was meant, or saying the wrong thing: changed to the one name, among those the language has and
/// those the program itself defines, within a letter or two of it.
/// </summary>
/// <param name="Id">The rule's id, with its language's prefix.</param>
/// <param name="Languages">The parser ids whose errors this reads: "gcc", "node".</param>
/// <param name="Said">The language's words, with the misspelt name as the group "name".</param>
/// <param name="Known">The names the language has that the misspelt one is compared with.</param>
/// <param name="Defined">The names the program defines itself, found in its own lines; none when it is null.</param>
/// <param name="NameOnTheLine">Where the name is on the line the error names, for words that do not name it: gfortran's Unclassifiable statement.</param>
/// <param name="InTextToo">Whether the name is read inside text as well - Perl's and PowerShell's "$total" - and changed there too.</param>
internal sealed class MisspeltName(
    string id, string[] languages, Regex said, IReadOnlyList<string> known, Regex? defined, Syntax syntax, IReadOnlySet<string> files,
    Regex? nameOnTheLine = null, bool inTextToo = false) : ILocalFixRule
{
    public string Id => id;

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (!languages.Contains(error.LanguageId) || said.Match(error.Message ?? "") is not { Success: true } message) return null;

        var frame = context.Frame;
        var framed = context.Read(frame?.File);
        var name = message.Groups["name"].Success
            ? message.Groups["name"].Value
            : nameOnTheLine is not null && framed is not null && frame?.Line is { } named && framed.Line(named) is { } namedText &&
              nameOnTheLine.Match(CodeText.Mask(namedText, syntax)) is { Success: true } onTheLine
                ? onTheLine.Groups["name"].Value
                : null;
        if (name is null) return null;

        var used = new Regex($@"(?<![\w$]){Regex.Escape(name)}(?![\w$])");

        // An error that names no file - Erlang's undef - is the program's one file that uses the name.
        var source = framed ?? TheOneFileUsing(context, used);
        if (source is null || !files.Contains(Path.GetExtension(source.Path).ToLowerInvariant())) return null;

        var masked = CodeText.MaskAll(source.Lines, syntax);
        var own = defined is null ? [] : masked.SelectMany(line => defined.Matches(line)).Select(match => match.Groups["name"].Value);
        var candidates = known.Concat(own).Where(candidate => candidate != name).ToList();
        if ((CodeText.Nearest(name, candidates, known) ?? OneLetterFrom(name, candidates)) is not { } right) return null;

        // The line the error names, or the first line that uses the name when it names none - read as written when the name is
        // used inside text too.
        var readable = inTextToo ? source.Lines : masked;
        var number = frame?.Line is { } line && line >= 1 && line <= readable.Count && used.IsMatch(readable[line - 1])
            ? line
            : Enumerable.Range(0, readable.Count).FirstOrDefault(index => used.IsMatch(readable[index]), -1) + 1;
        if (number < 1 || source.Line(number) is not { } erring) return null;

        var corrected = inTextToo ? used.Replace(erring, right) : CodeText.ReplaceWord(erring, name, right, syntax);
        if (corrected is null || corrected == erring) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {name} to {right}",
            $"`{name}` is not a name the program or the language has, and `{right}` is the only one within a letter or two of it.",
            source.Path, number, corrected);
    }

    /// <summary>
    /// For a name of two letters - console.lg - the one candidate a single letter away: too short for the nearest name in
    /// general, where a guess could be anything, but not among a few names the language has.
    /// </summary>
    private static string? OneLetterFrom(string name, IReadOnlyList<string> candidates) =>
        name.Length == 2 && candidates.Where(candidate => CodeText.Distance(name, candidate) == 1).ToList() is [var only] ? only : null;

    /// <summary>The one file of the language's in the program's folder that uses the name; null when none or several do.</summary>
    private SourceFile? TheOneFileUsing(LocalFixContext context, Regex used)
    {
        if (context.SourceRoot is not { Length: > 0 } root || !Directory.Exists(root)) return null;

        var usingTheName = Directory.EnumerateFiles(root)
            .Where(path => files.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .Select(context.Read)
            .OfType<SourceFile>()
            .Where(source => source.Lines.Any(used.IsMatch))
            .ToList();

        return usingTheName is [var only] ? only : null;
    }
}

/// <summary>The misspelt-name rules for each language that names a misspelt function without saying what was meant.</summary>
internal static partial class MisspeltNameRulesByLanguage
{
    private static IReadOnlySet<string> Files(params string[] extensions) => new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ILocalFixRule> All { get; } =
    [
        // g++: 'cot' is not a member of 'std'; did you mean 'oct'? - which is std's oct, a stream setting, not the cout meant.
        new MisspeltName("cpp-std-member", ["gcc"], new(@"^'(?<name>\w+)' is not a member of 'std'"),
            ["cout", "cin", "cerr", "endl", "string", "vector", "map", "set", "sort", "max", "min", "swap", "getline", "to_string", "stoi", "stod", "abs", "pair", "array"],
            null, Syntax.CLike, Files(".cpp", ".cc", ".cxx", ".c++")),

        // Node.js and TypeScript: console.lg is not a function.
        new MisspeltName("js-console-method", ["node", "typescript"], new(@"^console\.(?<name>\w+) is not a function$"),
            ["log", "error", "warn", "info", "debug", "table", "dir", "trace", "assert", "count", "group", "groupEnd", "time", "timeEnd"],
            null, Syntax.CLike, Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts")),

        // Erlang: undefined function io:fomat/1.
        new MisspeltName("erlang-nearest-function", ["erlang"], new(@"undefined function io:(?<name>\w+)/\d+"),
            ["format", "fwrite", "read", "write", "put_chars", "nl", "get_line"], null, Syntax.Erlang, Files(".erl")),

        // PowerShell with Set-StrictMode: The variable '$totl' cannot be retrieved because it has not been set.
        new MisspeltName("powershell-nearest-variable", ["powershell"], new(@"^The variable '(?<name>\$[A-Za-z_]\w*)' cannot be retrieved because it has not been set"),
            [], new(@"^\s*(?<name>\$[A-Za-z_]\w*)\s*=(?!=)"), Syntax.Python, Files(".ps1"), inTextToo: true),

        // gfortran: Unclassifiable statement at (1) - for prnt *, 'hi', a statement word misspelt.
        new MisspeltName("fortran-nearest-statement", ["fortran"], new(@"^Unclassifiable statement"),
            ["print", "write", "read", "call", "stop", "allocate", "deallocate", "open", "close", "return", "exit", "cycle"],
            null, Syntax.Fortran, Files(".f90", ".f95", ".f03", ".f08"), nameOnTheLine: new(@"^\s*(?<name>[A-Za-z]\w*)\s*[*(,]")),
    ];
}
