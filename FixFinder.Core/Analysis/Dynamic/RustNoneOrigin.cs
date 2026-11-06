using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Rust program panicking with "called `Option::unwrap()` on a `None` value": the variable unwrap was
/// called on, the line that gave it None, and - when one of the program's own functions gave it with a return - which return
/// gave back None and why the others never ran.
/// </summary>
/// <remarks>
/// Rust's panic gives the column of the unwrap - in member.unwrap().age, the u of unwrap - so what held None is the name just
/// before its dot, or the call whose result unwrap was called on, as in find_member(&amp;members, 2).unwrap(). Without a backtrace
/// the panic names no call, so a parameter the call gave None is not followed. <see cref="BraceMissingValue"/> then runs a
/// copy of the program that prints where its None could have come from.
/// </remarks>
public static partial class RustNoneOrigin
{
    /// <summary>What Rust's panic says when unwrap meets None.</summary>
    public const string Message = "called `Option::unwrap()` on a `None` value";

    private static readonly RustDialect Rust = new();

    /// <summary>
    /// Runs a copy of the program with the places the None could have come from marked, and says where it came from - or null
    /// when the column does not point at an unwrap after a name or a call, or the copy does not stop the same way.
    /// </summary>
    /// <param name="column">The column Rust's panic gives: where unwrap is.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, int crashLine, int? column, CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is not { } line) return Task.FromResult<BraceTraced?>(null);
        if (Read(line, column) is not var (doing, name, calledFunction)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(Rust, program, source, typed, crashLine, null, doing, "None", name, calledFunction,
            error => error.LanguageId == "rust" && error.Message == Message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable holds Some - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Rust, "rust-none-guard", cause, source, doing);

    /// <summary>
    /// What held None - a variable, or a function of the program whose result unwrap was called on straight away - read from
    /// the line at the column Rust gives. Null when the column is not at an unwrap after a dot, or what is before it is not a
    /// plain name or call.
    /// </summary>
    public static (string Doing, string? Name, string? CalledFunction)? Read(string line, int? column)
    {
        if (column is not { } at || at < 2 || at > line.Length || !line[(at - 1)..].StartsWith("unwrap", StringComparison.Ordinal)) return null;

        var before = line[..(at - 1)].TrimEnd();
        if (!before.EndsWith('.')) return null;
        before = before[..^1].TrimEnd();

        const string doing = "calls unwrap on {0}";
        if (before.EndsWith(')'))
        {
            var open = Brackets.Opening(before, before.Length - 1);
            var function = open > 0 ? Regex.Match(before[..open], @"(?<![\w.:])(?<name>[a-z_]\w*)\s*$") : Match.Empty;
            return function.Success ? (doing, null, function.Groups["name"].Value) : null;
        }

        var held = Regex.Match(before, @"(?<![\w.:])(?<name>[a-z_]\w*)$");
        return held.Success && held.Groups["name"].Value != "self" ? (doing, held.Groups["name"].Value, null) : null;
    }

    /// <summary>How Rust writes what following None needs: its functions, what sets a variable, and a line printed to standard error.</summary>
    private sealed partial class RustDialect : BraceDialect
    {
        [GeneratedRegex(@"^\s*(?:pub(?:\([^)]*\))?\s+)?(?:(?:const|async|unsafe)\s+)*fn\s+(?<name>[a-z_]\w*)\s*(?:<[^()]*>)?\s*\((?<parameters>[^)]*)\)[^{;]*\{\s*$")]
        private static partial Regex FunctionHeader();

        /// <summary>A return and the like, a block's first line, and a let, whose name the lines after it use.</summary>
        [GeneratedRegex(@"^\s*(?:return|break|continue|if|else|for|while|loop|match|let|fn|struct|enum|impl|use|mod|pub)\b")]
        private static partial Regex NotToSkip();

        /// <summary>The last call on the right: members.iter().find(...), scores.get(5), names.pop().</summary>
        [GeneratedRegex(@"\.(?<method>find|position|get|first|last|pop|max|min|max_by_key|min_by_key)\((?:[^()]|\([^()]*\))*\)$")]
        private static partial Regex GivesNoneWhenNothing();

        [GeneratedRegex(@"^(?!\s*(?:if|else|for|while|loop|match|return)\b)\s*(?:let\s+(?:mut\s+)?)?(?<name>[a-z_]\w*)\s*(?::\s*(?<type>[^=;]+?))?\s*(?:[-+*/%&|^]|<<|>>)?=(?![=>])[^;]*;\s*$")]
        private static partial Regex SetsAny();

        [GeneratedRegex(@"^\s*if\s+(?!let\b)")]
        private static partial Regex IfWithoutLet();

        [GeneratedRegex(@"^\s*\}?\s*else\s+if\s+(?!let\b)")]
        private static partial Regex ElseIfWithoutLet();

        [GeneratedRegex(@"(?<![=!<>])=(?![=>])\s*(?:move\s+)?\|")]
        private static partial Regex ClosureGiven();

        /// <summary>A lifetime's quote - the ' of &amp;'a str or 'static - which no second quote closes, as one would a character's: 'a'.</summary>
        [GeneratedRegex(@"'(?=[A-Za-z_]\w*(?![\w']))")]
        private static partial Regex LifetimeQuote();

        /// <summary>Blanked as C's are, after each lifetime's quote is made a _ - so &amp;'a [Item] is not read as text running on to the next quote.</summary>
        public override IReadOnlyList<string> Blanked(IReadOnlyList<string> lines) => CodeText.MaskAll([.. lines.Select(WithoutLifetimeQuotes)], Syntax);

        public override string Blanked(string line) => CodeText.Mask(WithoutLifetimeQuotes(line), Syntax);

        private static string WithoutLifetimeQuotes(string line) => LifetimeQuote().Replace(line, "_");

        public override string ErrorName => "panic";

        public override Regex SetsAnyName => SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["None"];

        public override string SomethingThere => "a value";

        /// <summary>None to say: a Rust function that reaches its end gives back its last expression.</summary>
        public override string? ReachingTheEndGives => null;

        public override bool StatementsEndInSemicolons => true;

        public override Regex NotAStatementToSkip => NotToSkip();

        /// <summary>An if, not an if let, whose test is a pattern - "was never true" would not say what happened.</summary>
        public override Regex IfLine => IfWithoutLet();

        public override Regex ElseIfLine => ElseIfWithoutLet();

        public override string GuardOpening(string name) => $"if {name}.is_some() {{";

        /// <summary>A String, which the next piece is added to: String::from("text").</summary>
        public override string Quoted(string text) => "String::from(\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\")";

        /// <summary>A String joined with the next as a String's text: + &amp;.</summary>
        public override string Joining => " + &";

        public override bool GivesAFunction(string masked) => ClosureGiven().IsMatch(masked);

        /// <summary>
        /// After the file's last line of code, what writes a value: with Debug, {:?} - Some(30), [1, 2]; an Option of a type
        /// without it as None or Some(..); and anything else without it, which {:?} would not build with, as words that say so.
        /// Rust takes the first of the three that fits the value's type, each one reference further from the call.
        /// </summary>
        public override IEnumerable<(int Line, string Added)> Additions(IReadOnlyList<string> masked)
        {
            var last = Enumerable.Range(0, masked.Count).LastOrDefault(index => masked[index].Trim().Length > 0, -1);
            if (last < 0) yield break;

            yield return (last + 1,
                " struct FixFinderShown<'a, T: ?Sized>(&'a T);" +
                " trait FixFinderWithDebug { fn shown(&self) -> String; }" +
                " impl<'a, T: ?Sized + std::fmt::Debug> FixFinderWithDebug for &&FixFinderShown<'a, T> { fn shown(&self) -> String { format!(\"{:?}\", self.0) } }" +
                " trait FixFinderOption { fn shown(&self) -> String; }" +
                " impl<'a, T> FixFinderOption for &FixFinderShown<'a, Option<T>> { fn shown(&self) -> String { match self.0 { None => String::from(\"None\"), Some(_) => String::from(\"Some(..)\") } } }" +
                " trait FixFinderWithoutDebug { fn shown(&self) -> String; }" +
                " impl<'a, T: ?Sized> FixFinderWithoutDebug for FixFinderShown<'a, T> { fn shown(&self) -> String { String::from(\"(its type has no Debug)\") } }");
        }

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (FunctionHeader().Match(masked[index]) is not { Success: true } header) continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        public override Regex Sets(string name) =>
            new($@"^\s*(?:let\s+(?:mut\s+)?)?{Regex.Escape(name)}\s*(?::[^=;]+)?=(?![=>])\s*(?<right>[^;]*[^;\s])\s*;\s*$");

        public override string Print(string expression) => $"eprintln!(\"{{}}\", {expression});";

        public override string MissingOrValue(string name) => $"(if {name}.is_none() {{ \"None\" }} else {{ \"value\" }}).to_string()";

        public override string Shown(string type, string name) => $"(&&&FixFinderShown(&{name})).shown()";

        /// <summary>
        /// The calls Rust's documentation says give back None when there is nothing to give: find and position when no item
        /// passes, get for an index or a key there is not, and first, last, pop, max and min of an empty collection.
        /// </summary>
        public override Documented? Library(string right, BraceCode code)
        {
            if (GivesNoneWhenNothing().Match(right) is not { Success: true } call) return null;

            var method = call.Groups["method"].Value;
            var when = method switch
            {
                "find" or "position" => "when no item passes its test",
                "get" => "for an index or a key the collection does not have",
                _ => "for an empty collection",
            };
            return new Documented($"{method} gives back None {when}.", "Decide what the program should do when there is nothing, and {check} - or match on it, or use if let.");
        }

        /// <summary>
        /// Each parameter's name and type, split at the commas outside brackets - a HashMap&lt;String, u32&gt; is one - with mut
        /// taken off; self, and a pattern such as (a, b): (u32, u32), are left out.
        /// </summary>
        private static List<(string Type, string Name)> Parameters(string parameters)
        {
            var pieces = new List<string>();
            var depth = 0;
            var start = 0;
            for (var index = 0; index < parameters.Length; index++)
            {
                if (parameters[index] is '<' or '(' or '[') depth++;
                else if (parameters[index] is '>' or ')' or ']') depth--;
                else if (parameters[index] == ',' && depth == 0)
                {
                    pieces.Add(parameters[start..index]);
                    start = index + 1;
                }
            }

            pieces.Add(parameters[start..]);

            return [.. pieces
                .Select(piece => Regex.Match(piece.Trim(), @"^(?:mut\s+)?(?<name>[a-z_]\w*)\s*:\s*(?<type>.+)$"))
                .Where(match => match.Success && match.Groups["name"].Value != "self")
                .Select(match => (match.Groups["type"].Value, match.Groups["name"].Value))];
        }
    }
}
