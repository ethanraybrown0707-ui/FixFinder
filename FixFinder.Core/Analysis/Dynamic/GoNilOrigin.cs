using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Go program stopping with a nil pointer dereference: the variable the line read through, the line that
/// gave it nil, and - when one of the program's own functions gave it - which return gave back nil and why the others never
/// ran.
/// </summary>
/// <remarks>
/// Go's panic names nothing and gives no column - "runtime error: invalid memory address or nil pointer dereference" - so
/// what was nil is read from the line it stopped on, as for C#: the variables it reads a field or calls a method through, of
/// those the program sets or is given as a parameter. Only when that is one variable is anything said.
/// <see cref="BraceMissingValue"/> then runs a copy of the program that prints where its nil could have come from.
/// </remarks>
public static partial class GoNilOrigin
{
    /// <summary>What Go's panic says, after "panic: ", when a program reads through a nil pointer.</summary>
    public const string Message = "runtime error: invalid memory address or nil pointer dereference";

    /// <summary>A name the line reads a field through, or calls a method on: member.Age, shelf.Longest().</summary>
    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*\.\s*(?<member>[A-Za-z_]\w*)\s*(?<call>\()?")]
    private static partial Regex ReadThrough();

    private static readonly GoDialect Go = new();

    /// <summary>
    /// Runs a copy of the program with the places the nil could have come from marked, and says where it came from - or null
    /// when what was nil is not one variable, or the copy does not stop the same way.
    /// </summary>
    /// <param name="program">The file the program is run from.</param>
    /// <param name="file">The file it stopped in.</param>
    /// <param name="calledFrom">The line of the same file the function it stopped in was called from, when Go's trace says so.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, int crashLine, int? calledFrom, CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || Read(source, crashLine) is not var (name, doing)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(Go, program, source, typed, crashLine, calledFrom, doing, "nil", name, null,
            error => error.LanguageId == "go" && error.Message == Message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable is not nil - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Go, "go-nil-guard", cause, source, doing);

    /// <summary>
    /// What was nil on the line and what the line did with it - the one variable it reads a field through or calls a method
    /// on that the program sets, or is given as a parameter - or null when that is none, or more than one. A package's name,
    /// as in fmt.Println, is set by nothing, so it is never the one.
    /// </summary>
    public static (string Name, string Doing)? Read(SourceFile source, int crashLine)
    {
        if (source.Line(crashLine) is null) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = new BraceCode(Go, source.Lines, masked);
        var around = code.FunctionAround(crashLine);

        var used = ReadThrough().Matches(masked[crashLine - 1])
            .GroupBy(each => each.Groups["name"].Value)
            .Where(each => code.LinesSetting(each.Key, around).Any() || around?.Parameters.Any(parameter => parameter.Name == each.Key) == true)
            .ToList();
        if (used is not [var only]) return null;

        var first = only.First();
        var doing = first.Groups["call"].Success ? $"calls {first.Groups["member"].Value} on {{0}}" : $"reads {first.Groups["member"].Value} from {{0}}";
        return (only.Key, doing);
    }

    /// <summary>How Go writes what following a nil needs: its functions and methods, what sets a variable, and a line printed to standard error.</summary>
    private sealed partial class GoDialect : BraceDialect
    {
        /// <summary>func name(...) result {, or a method's func (s *Shelf) name(...) result {.</summary>
        [GeneratedRegex(@"^\s*func\s+(?:\([^)]*\)\s*)?(?<name>[A-Za-z_]\w*)\s*(?:\[[^\]]*\]\s*)?\((?<parameters>[^)]*)\)[^{]*\{\s*$")]
        private static partial Regex FunctionHeader();

        /// <summary>A return and the like, a block's first line, and a declaration, whose name the lines after it use.</summary>
        [GeneratedRegex(@"^\s*(?:return|break|continue|goto|fallthrough|if|else|for|switch|select|case|default|go|defer|func|var|const|type)\b|^\s*[A-Za-z_][\w\s,]*:=")]
        private static partial Regex NotToSkip();

        /// <summary>A map indexed with a key, all a line gives: stock["plums"].</summary>
        [GeneratedRegex(@"^(?<receiver>[A-Za-z_]\w*)\[(?<key>.+)\]$")]
        private static partial Regex Indexed();

        [GeneratedRegex(@"^(?!\s*(?:if|else|for|switch|select|case|default|return|go|defer)\b)\s*(?:var\s+(?<name>[A-Za-z_]\w*)(?:\s+(?<type>[^=]*[^=\s]))?\s*=|(?<name>[A-Za-z_]\w*)\s*(?::=|[-+*/%]?=))(?!=)\s*\S.*$|^\s*(?<name>[A-Za-z_]\w*)\s*(?:\+\+|--)\s*$|^\s*var\s+(?<name>[A-Za-z_]\w*)\s+(?<type>[^=]*[^=\s])\s*$")]
        private static partial Regex SetsAny();

        [GeneratedRegex(@"^\s*if\s")]
        private static partial Regex IfWithoutBrackets();

        [GeneratedRegex(@"^\s*\}?\s*else\s+if\s")]
        private static partial Regex ElseIfWithoutBrackets();

        /// <summary>Where Go ends a statement at the end of a line: after a name, a number, text, a closing bracket, ++ or --.</summary>
        [GeneratedRegex(@"(?:[\w)\]}""'`{]|\+\+|--)$")]
        private static partial Regex StatementEnd();

        [GeneratedRegex(@"^\s*package\s+\w+")]
        private static partial Regex PackageClause();

        public override string ErrorName => "nil pointer dereference";

        public override Regex SetsAnyName => SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["nil"];

        public override string SomethingThere => "a value";

        /// <summary>Go will not build a function with a result that can reach its end.</summary>
        public override string? ReachingTheEndGives => null;

        public override bool StatementsEndInSemicolons => false;

        public override Regex NotAStatementToSkip => NotToSkip();

        public override Regex IfLine => IfWithoutBrackets();

        public override Regex ElseIfLine => ElseIfWithoutBrackets();

        public override bool EndsAStatement(string code) => StatementEnd().IsMatch(code);

        public override string GuardOpening(string name) => $"if {name} != nil {{";

        /// <summary>
        /// The packages the copy's prints use, under names of FixFinder's own, imported on the package line - so no line moves,
        /// and a file that imports fmt itself still builds - and the function that writes a value, after the file's last line
        /// of code.
        /// </summary>
        public override IEnumerable<(int Line, string Added)> Additions(IReadOnlyList<string> masked)
        {
            var package = Enumerable.Range(0, masked.Count).FirstOrDefault(index => PackageClause().IsMatch(masked[index]), -1);
            var last = Enumerable.Range(0, masked.Count).LastOrDefault(index => masked[index].Trim().Length > 0, -1);
            if (package < 0 || last <= package) yield break;

            yield return (package + 1, "; import (fixFinderFmt \"fmt\"; fixFinderOs \"os\"; fixFinderReflect \"reflect\"; fixFinderSort \"sort\"; fixFinderStrconv \"strconv\"; fixFinderStrings \"strings\")");
            yield return (last + 1, "; " + ShowFunction);
        }

        /// <summary>
        /// Writes a value as Go's %+v does - &amp;{ID:1 Age:30}, [40 55] - but follows a pointer inside a list or a map, which
        /// %+v writes as an address that changes from run to run; writes nil as nil, where %+v writes a nil map as map[], the
        /// same as an empty one; puts text in quotes; and gives a value with a String or Error method what that says. Three
        /// pointers deep, or after 20 items, it writes ... - so a linked list that points back to itself still ends.
        /// </summary>
        private const string ShowFunction =
            "func fixFinderShow(fixFinderValue any) string { return fixFinderShowValue(fixFinderReflect.ValueOf(fixFinderValue), 0) }; " +
            "func fixFinderShowValue(fixFinderHeld fixFinderReflect.Value, fixFinderDepth int) string { " +
            "if !fixFinderHeld.IsValid() { return \"nil\" }; " +
            "switch fixFinderHeld.Kind() { case fixFinderReflect.Ptr, fixFinderReflect.Interface, fixFinderReflect.Map, fixFinderReflect.Slice, fixFinderReflect.Chan, fixFinderReflect.Func: if fixFinderHeld.IsNil() { return \"nil\" } }; " +
            "if fixFinderHeld.CanInterface() { if fixFinderSaid, fixFinderSays := fixFinderHeld.Interface().(fixFinderFmt.Stringer); fixFinderSays { return fixFinderSaid.String() }; " +
            "if fixFinderSaid, fixFinderSays := fixFinderHeld.Interface().(error); fixFinderSays { return fixFinderSaid.Error() } }; " +
            "if fixFinderDepth > 3 { return \"...\" }; " +
            "switch fixFinderHeld.Kind() { " +
            "case fixFinderReflect.Ptr: return \"&\" + fixFinderShowValue(fixFinderHeld.Elem(), fixFinderDepth+1); " +
            "case fixFinderReflect.Interface: return fixFinderShowValue(fixFinderHeld.Elem(), fixFinderDepth); " +
            "case fixFinderReflect.String: return fixFinderStrconv.Quote(fixFinderHeld.String()); " +
            "case fixFinderReflect.Slice, fixFinderReflect.Array: fixFinderItems := []string{}; " +
            "for fixFinderIndex := 0; fixFinderIndex < fixFinderHeld.Len() && fixFinderIndex < 20; fixFinderIndex++ { fixFinderItems = append(fixFinderItems, fixFinderShowValue(fixFinderHeld.Index(fixFinderIndex), fixFinderDepth+1)) }; " +
            "if fixFinderHeld.Len() > 20 { fixFinderItems = append(fixFinderItems, \"...\") }; return \"[\" + fixFinderStrings.Join(fixFinderItems, \" \") + \"]\"; " +
            "case fixFinderReflect.Map: fixFinderKeys := fixFinderHeld.MapKeys(); " +
            "fixFinderSort.Slice(fixFinderKeys, func(fixFinderLeft, fixFinderRight int) bool { return fixFinderFmt.Sprint(fixFinderKeys[fixFinderLeft]) < fixFinderFmt.Sprint(fixFinderKeys[fixFinderRight]) }); " +
            "fixFinderPairs := []string{}; for _, fixFinderKey := range fixFinderKeys { fixFinderPairs = append(fixFinderPairs, fixFinderShowValue(fixFinderKey, fixFinderDepth+1)+\":\"+fixFinderShowValue(fixFinderHeld.MapIndex(fixFinderKey), fixFinderDepth+1)) }; " +
            "return \"map[\" + fixFinderStrings.Join(fixFinderPairs, \" \") + \"]\"; " +
            "case fixFinderReflect.Struct: fixFinderFields := []string{}; " +
            "for fixFinderIndex := 0; fixFinderIndex < fixFinderHeld.NumField(); fixFinderIndex++ { fixFinderFields = append(fixFinderFields, fixFinderHeld.Type().Field(fixFinderIndex).Name+\":\"+fixFinderShowValue(fixFinderHeld.Field(fixFinderIndex), fixFinderDepth+1)) }; " +
            "return \"{\" + fixFinderStrings.Join(fixFinderFields, \" \") + \"}\" }; " +
            "return fixFinderFmt.Sprint(fixFinderHeld) }";

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (FunctionHeader().Match(masked[index]) is not { Success: true } header) continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        /// <summary>x = ..., x := ..., var x T = ..., and x, err := ... - with what it gives as "right".</summary>
        public override Regex Sets(string name) =>
            new($@"^\s*(?:var\s+{Regex.Escape(name)}(?:\s+[^=]*[^=\s])?\s*=|{Regex.Escape(name)}\s*(?:,\s*[A-Za-z_]\w*\s*)*:?=)(?!=)\s*(?<right>.*\S)\s*$");

        /// <summary><c>var best *Score</c> gives best nil without saying so - as it does a map, a slice, a channel, a function or an interface.</summary>
        public override string? SetsWithoutSaying(string masked, string name) =>
            Regex.IsMatch(masked, $@"^\s*var\s+{Regex.Escape(name)}\s+(?:\*|map\[|\[\]|chan\b|func\b|error\b|interface\b|any\b)[^=]*$") ? "nil" : null;

        /// <summary>Ended with a semicolon, as the if or return it is put before on the same line needs.</summary>
        public override string Print(string expression) => $"fixFinderFmt.Fprintln(fixFinderOs.Stderr, {expression});";

        public override string MissingOrValue(string name) => $"func() string {{ if {name} == nil {{ return \"nil\" }}; return \"value\" }}()";

        /// <summary>The value as the copy's own function writes it, after the file's last line: see <see cref="ShowFunction"/>.</summary>
        public override string Shown(string type, string name) => $"fixFinderShow({name})";

        /// <summary>
        /// A map indexed with a key, where the file declares the map: the Go specification says indexing a map with a key it
        /// does not have gives the zero value of its values - nil, for pointers, maps, slices and interfaces.
        /// </summary>
        public override Documented? Library(string right, BraceCode code)
        {
            if (Indexed().Match(right) is not { Success: true } indexed) return null;

            var map = Regex.Escape(indexed.Groups["receiver"].Value);
            if (!code.Masked.Any(line => Regex.IsMatch(line, $@"(?:\b{map}\s*:?=\s*(?:make\(\s*)?map\[|\bvar\s+{map}\s+map\[|\b{map}\s+map\[)"))) return null;

            var receiver = BraceMissingValue.Code(indexed.Groups["receiver"].Value);
            return new Documented($"{receiver} is a map, and indexing a map with a key it does not have gives the zero value of its values - nil, for pointers.",
                $"Ask with two values - value, ok := {indexed.Groups["receiver"].Value}[key] - and decide what to do when ok is false, or {{check}}.");
        }

        /// <summary>
        /// Each parameter's name: in Go a name may share the type after it - a, b int - and a function whose parameters are
        /// only types names none. The blank name _ is left out, as it cannot be read.
        /// </summary>
        private static List<(string Type, string Name)> Parameters(string parameters)
        {
            var pieces = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!pieces.Any(piece => Regex.IsMatch(piece, @"^[A-Za-z_]\w*\s+\S"))) return [];

            return [.. pieces
                .Select(piece => Regex.Match(piece, @"^(?<name>[A-Za-z_]\w*)(?:\s+(?<type>.+))?$"))
                .Where(match => match.Success && match.Groups["name"].Value != "_")
                .Select(match => (match.Groups["type"].Value, match.Groups["name"].Value))];
        }
    }
}
