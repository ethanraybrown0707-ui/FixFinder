using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A parameter given a value before anything reads the one the caller passed - <c>function totalPrice(price, quantity)</c>
/// and then <c>quantity = 1</c> - so whatever the caller passes for it is never used, in the languages whose values FixFinder
/// does not follow through the code: TypeScript, Ruby, PHP, Lua, Perl, Dart and PowerShell, and F# and OCaml, where a let of
/// the parameter's name hides it. Python, Java, C#, JavaScript, Go, C and C++ have it found by following every way through
/// the function, analysis-parameter-ignored.
/// </summary>
/// <remarks>
/// The first line of the function to name the parameter has to be at the function's own depth - not inside an if or a loop,
/// which may not run - and has to give it a value that does not read it: <c>quantity = 1</c>, not <c>name = name.strip</c>.
/// A header and its parameters are read from one line; Perl's parameters from its <c>my (...) = @_;</c> or <c>shift</c>
/// lines, PowerShell's from its header or a <c>param(...)</c> line. A parameter the caller's own variable is passed as - PHP's
/// <c>&amp;$count</c>, PowerShell's <c>[ref]</c> - is left alone, as giving it a value gives the caller's variable it, and so
/// is Dart's <c>this.width</c>, which the object keeps. Rust's compiler says it itself - value passed to it is never read -
/// and Elixir's that the parameter is unused; Scala's parameters cannot be given a value, Erlang's variables a second one, and
/// Fortran's arguments are the caller's own variables.
/// </remarks>
public static partial class ParameterIgnoredPatterns
{
    /// <summary>
    /// How one language writes a function: <c>Header</c> finds its first line, with the parameters in the group
    /// <c>parameters</c>; <c>Name</c> takes one parameter's name from its part of that list, with <c>passed</c> set where the
    /// caller's own variable is passed; <c>Replaced</c>, with the name <c>{N}</c>, is a line giving it a value, the value in
    /// <c>value</c>.
    /// </summary>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex Header, Regex Name, string Replaced,
        bool LetHidesIt = false, bool IgnoreCase = false, bool OnlyTypeScript = false);

    private static readonly Dialect[] Dialects =
    [
        new("typescript", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, TypeScriptHeader(), PlainName(),
            @"^\s*{N}\s*=(?![=>])\s*(?<value>[^;]+?)\s*;?\s*$", OnlyTypeScript: true),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, RubyHeader(), RubyName(),
            @"^\s*{N}\s*=(?![=~>])\s*(?<value>\S.*)$"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, PhpHeader(), PhpName(),
            @"^\s*{N}\s*=(?![=>])\s*(?<value>[^;]+?)\s*;\s*$"),
        new("lua", CodePattern.Files(".lua"), Syntax.Lua, LuaHeader(), PlainName(),
            @"^\s*{N}\s*=(?!=)\s*(?<value>\S.*)$"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlHeader(), PerlName(),
            @"^\s*{N}\s*=(?![=~])\s*(?<value>[^;]+?)\s*;\s*$"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, DartHeader(), DartName(),
            @"^\s*{N}\s*=(?!=)\s*(?<value>[^;]+?)\s*;\s*$"),
        new("powershell", CodePattern.Files(".ps1"), Syntax.Python, PowerShellHeader(), PowerShellName(),
            @"^\s*{N}\s*=\s*(?<value>\S.*)$", IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, FSharpHeader(), CurriedName(),
            @"^\s*let\s+(?:mutable\s+)?{N}\s*(?::[^=]+)?=(?!=)\s*(?<value>\S.*)$", LetHidesIt: true),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, OCamlHeader(), CurriedName(),
            @"^\s*let\s+{N}\s*(?::[^=]+)?=(?!=)\s*(?<value>\S.*?)(?:\s+in)?\s*$", LetHidesIt: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-parameter-ignored", dialect.Files, dialect.Syntax,
            (id, source, masked) => ReplacedBeforeRead(id, source, masked, dialect))),
    ];

    private static IEnumerable<LogicFinding> ReplacedBeforeRead(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        if (dialect.OnlyTypeScript && !TypeScriptCheck.IsTypeScript(source.Path)) yield break;
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

        for (var header = 0; header < masked.Count; header++)
        {
            if (dialect.Header.Match(masked[header]) is not { Success: true } function) continue;

            // The body: from the line after the header - or after a brace on its own line - to the first line no deeper than it.
            var headerIndent = CodeText.Indentation(masked[header]).Length;
            var first = NextCode(masked, header + 1);
            if (first >= 0 && masked[first].Trim() == "{" && CodeText.Indentation(masked[first]).Length == headerIndent) first = NextCode(masked, first + 1);
            if (first < 0 || CodeText.Indentation(masked[first]).Length <= headerIndent) continue;
            var bodyIndent = CodeText.Indentation(masked[first]).Length;
            var end = first;
            while (end < masked.Count && (IsBlank(masked[end]) || CodeText.Indentation(masked[end]).Length > headerIndent)) end++;

            var (names, taken) = Parameters(function.Groups["parameters"].Value, masked, first, end, bodyIndent, dialect);
            foreach (var name in names)
            {
                // Mentions are looked for in the lines as written, as a name inside text - "#{quantity}" - is read too.
                var mention = new Regex($@"(?<![\w$@%]){Regex.Escape(name)}(?![\w$])", options);
                var replaced = Enumerable.Range(taken, Math.Max(0, end - taken)).FirstOrDefault(line => mention.IsMatch(source.Lines[line]), -1);
                if (replaced < 0 || CodeText.Indentation(masked[replaced]).Length != bodyIndent) continue;

                var replacing = new Regex(dialect.Replaced.Replace("{N}", Regex.Escape(name)), options);
                if (replacing.Match(masked[replaced]) is not { Success: true } given) continue;

                var value = Value(source.Lines[replaced], given);
                if (mention.IsMatch(value)) continue;
                yield return new LogicFinding(id, replaced + 1, dialect.LetHidesIt
                    ? $"`let {name} = {value}` on line {replaced + 1} hides the parameter `{name}` before anything reads it, so the value the caller passes for `{name}` is never used"
                    : $"`{name}` is given `{value}` on line {replaced + 1} before anything reads it, so the value the caller passes for `{name}` is never used",
                    null);
            }
        }
    }

    /// <summary>
    /// The function's parameters, and the line its body starts after taking them: the header's list - or Perl's
    /// <c>my (...) = @_;</c> and <c>my $x = shift;</c> lines, and PowerShell's <c>param(...)</c> line, at the start of the body.
    /// </summary>
    private static (List<string> Names, int Taken) Parameters(string list, IReadOnlyList<string> masked, int first, int end, int bodyIndent, Dialect dialect)
    {
        var names = new List<string>();
        var taken = first;

        if (dialect.Language == "perl")
        {
            while (taken < end && (IsBlank(masked[taken]) || PerlTakes().Match(masked[taken]) is { Success: true }))
            {
                if (PerlTakes().Match(masked[taken]) is { Success: true } takes)
                    names.AddRange(PerlName().Matches(takes.Groups["parameters"].Value).Select(name => name.Groups["name"].Value));
                taken++;
            }
            return (names, taken);
        }

        if (dialect.Language == "powershell" && list.Trim().Length == 0 && PowerShellParam().Match(masked[first]) is { Success: true } param)
        {
            list = param.Groups["parameters"].Value;
            taken = first + 1;
        }

        var parts = dialect.LetHidesIt ? CurriedParts().Matches(list).Select(part => part.Value) : SplitAtTopLevel(list);
        foreach (var part in parts)
            if (dialect.Name.Match(part) is { Success: true } named && !named.Groups["passed"].Success && named.Groups["name"].Value is { Length: > 0 } name &&
                !NotAParameter().IsMatch(name))
                names.Add(name);
        return (names.Distinct(StringComparer.Ordinal).ToList(), taken);
    }

    /// <summary>A parameter list split at its commas, not those inside brackets, braces or angle brackets.</summary>
    private static IEnumerable<string> SplitAtTopLevel(string list)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < list.Length; index++)
        {
            depth += list[index] switch { '(' or '[' or '{' or '<' => 1, ')' or ']' or '}' or '>' => -1, _ => 0 };
            if (list[index] == ',' && depth == 0)
            {
                yield return list[start..index];
                start = index + 1;
            }
        }
        if (list[start..].Trim().Length > 0) yield return list[start..];
    }

    /// <summary>The value as written, read from the original line where the masked one blanked text.</summary>
    private static string Value(string written, Match given)
    {
        var value = given.Groups["value"];
        return (value.Index + value.Length <= written.Length ? written.Substring(value.Index, value.Length) : value.Value).Trim();
    }

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    private static int NextCode(IReadOnlyList<string> masked, int from)
    {
        for (var line = from; line < masked.Count; line++)
            if (!IsBlank(masked[line])) return line;
        return -1;
    }

    /// <summary>function name(...) {, a method name(...) {, constructor(...) {, (a, b) => {, all on one line.</summary>
    [GeneratedRegex(@"^\s*(?:(?:export|default|async|public|private|protected|static|override|readonly)\s+)*(?:function\s*\*?\s*(?>[\w$]*)\s*|(?!(?:if|for|while|switch|catch|with|return)\b)(?>[\w$]+)\s*|(?:const|let|var)\s+(?>[\w$]+)\s*=\s*(?:async\s*)?)\((?<parameters>[^()]*)\)\s*(?:=>\s*)?\{\s*$")]
    private static partial Regex TypeScriptHeader();

    /// <summary>def name(a, b = 1) or def name a, b - not an endless def name(a) = value.</summary>
    [GeneratedRegex(@"^\s*def\s+(?:self\.)?(?>[\w?!]+)(?:\s*\((?<parameters>[^()]*)\)|\s+(?<parameters>[^=#]+)|)\s*$")]
    private static partial Regex RubyHeader();

    /// <summary>function name(...) with its modifiers, a brace after it or on the next line.</summary>
    [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final|abstract)\s+)*function\s+&?(?>\w+)\s*\((?<parameters>[^()]*)\)\s*(?::\s*\??[\w\\|]+\s*)?\{?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PhpHeader();

    /// <summary>function name(a, b), local function name(...), name = function(...), function object:method(...).</summary>
    [GeneratedRegex(@"^\s*(?:local\s+)?(?:function\s+(?>[\w.:]+)\s*|(?>[\w.]+)\s*=\s*function\s*)\((?<parameters>[^()]*)\)\s*$")]
    private static partial Regex LuaHeader();

    /// <summary>sub name {, its parameters taken on the lines after it.</summary>
    [GeneratedRegex(@"^\s*sub\s+(?>\w+)\s*\{\s*(?<parameters>)$")]
    private static partial Regex PerlHeader();

    /// <summary>A line taking Perl parameters: my ($a, $b) = @_; or my $a = shift;.</summary>
    [GeneratedRegex(@"^\s*my\s*(?:\((?<parameters>[^()]*)\)\s*=\s*@_|(?<parameters>\$\w+)\s*=\s*shift(?:\s*\(\s*@_\s*\))?)\s*;\s*$")]
    private static partial Regex PerlTakes();

    /// <summary>
    /// A Dart function or method: an optional return type, its name, and its parameters, then a brace. The indentation is
    /// taken whole, so the words a statement starts with - if, for, while - are tested where the line's code starts, and the
    /// return type holds no space but after a comma, so it cannot take the name's place.
    /// </summary>
    [GeneratedRegex(@"^(?>\s*)(?!(?:if|for|while|switch|catch|return|else|do|try|await|assert)\b)(?:(?:static|external|final)\s+)?(?:(?>[\w<>?\[\]]+(?:,\s*[\w<>?\[\]]+)*)\s+)?(?>\w+)\s*\((?<parameters>[^()]*)\)\s*(?:async\s*)?\{\s*$")]
    private static partial Regex DartHeader();

    /// <summary>function Name($a, $b) {, or function Name { with a param(...) line after it.</summary>
    [GeneratedRegex(@"^\s*function\s+(?>[\w-]+)\s*(?:\((?<parameters>[^()]*)\)|(?<parameters>))\s*\{\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellHeader();

    [GeneratedRegex(@"^\s*param\s*\((?<parameters>[^()]*(?:\([^()]*\)[^()]*)*)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellParam();

    /// <summary>let name a b =, let rec name (a: int) b =, member this.Name a = - a function, not a value, ending its line.</summary>
    [GeneratedRegex(@"^\s*(?:let\s+(?:rec\s+|inline\s+|private\s+)*|member\s+(?>\w+)\.)(?>\w+)\s+(?<parameters>[^=]+?)\s*(?::\s*[^=]+)?=\s*$")]
    private static partial Regex FSharpHeader();

    /// <summary>let name a b =, let rec name (a : int) ~b =, ending its line.</summary>
    [GeneratedRegex(@"^\s*let\s+(?:rec\s+)?(?>\w+)\s+(?<parameters>[^=]+?)\s*(?::\s*[^=]+)?=\s*$")]
    private static partial Regex OCamlHeader();

    /// <summary>A curried parameter: a word, or one in brackets with its type, or OCaml's ~label.</summary>
    [GeneratedRegex(@"\([^()]*\)|[~?]?[a-z_]\w*'*")]
    private static partial Regex CurriedParts();

    [GeneratedRegex(@"^\s*[~?(]?\s*(?<name>[a-z_]\w*)")]
    private static partial Regex CurriedName();

    /// <summary>width, ...rest, width = 1, as TypeScript's are once Node has blanked their types.</summary>
    [GeneratedRegex(@"^\s*(?:\.\.\.)?(?<name>[A-Za-z_$][\w$]*)\s*(?:=.*)?$")]
    private static partial Regex PlainName();

    /// <summary>width, height = 1, *rest, key: 1 - not &amp;block.</summary>
    [GeneratedRegex(@"^\s*(?:(?<passed>&)|\*{0,2})(?<name>[a-z_]\w*)\s*(?::.*|=.*)?$")]
    private static partial Regex RubyName();

    /// <summary>int $width, ?string $name = null, ...$rest - and &amp;$count, the caller's own variable.</summary>
    [GeneratedRegex(@"^[^$&]*(?:(?<passed>&)\s*)?(?:\.\.\.)?(?<name>\$\w+)\s*(?:=.*)?$")]
    private static partial Regex PhpName();

    [GeneratedRegex(@"(?<name>\$\w+)")]
    private static partial Regex PerlName();

    /// <summary>int width, required int width, int width = 1 - not this.width, which the object keeps.</summary>
    [GeneratedRegex(@"^\s*[{\[]?\s*(?:required\s+)?(?:(?<passed>this\.|super\.)|[\w<>?,. ]*?\s)?(?<name>[A-Za-z_]\w*)\s*(?:=[^,]*)?[}\]]?\s*$")]
    private static partial Regex DartName();

    /// <summary>$width, [int]$width, [int]$width = 1 - and [ref]$count, the caller's own variable.</summary>
    [GeneratedRegex(@"^\s*(?:\[Parameter[^\]]*\]\s*)?(?:(?<passed>\[ref\])|\[[^\]]*\])?\s*(?<name>\$\w+)\s*(?:=.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellName();

    /// <summary>Words a parameter list holds that are not parameters: F#'s and OCaml's unit, and a this or self.</summary>
    [GeneratedRegex(@"^(?:this|self|unit|_)$")]
    private static partial Regex NotAParameter();
}
