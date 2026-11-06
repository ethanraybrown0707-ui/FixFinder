using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Dart program stopping with "Null check operator used on a null value": the variable the ! said was
/// not null, the line that gave it null, and - when one of the program's own functions gave it - which return gave back null
/// and why the others never ran.
/// </summary>
/// <remarks>
/// Dart's trace gives the column of the ! - in member!.age, the ! after member - so what was null is the name just before it,
/// or the call whose result it follows, as in findMember(members, 2)!.age. <see cref="BraceMissingValue"/> then runs a copy of
/// the program that prints where its null could have come from.
/// </remarks>
public static partial class DartNullOrigin
{
    /// <summary>What Dart says when a ! meets null.</summary>
    public const string Message = "Null check operator used on a null value";

    private static readonly DartDialect Dart = new();

    /// <summary>
    /// Runs a copy of the program with the places the null could have come from marked, and says where it came from - or null
    /// when the column does not point at a ! after a name or a call, or the copy does not stop the same way.
    /// </summary>
    /// <param name="column">The column Dart's trace gives for the line it stopped on: where the ! is.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, int crashLine, int? column, int? calledFrom,
        CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is not { } line) return Task.FromResult<BraceTraced?>(null);
        if (Read(line, column) is not var (doing, name, calledFunction)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(Dart, program, source, typed, crashLine, calledFrom, doing, "null", name, calledFunction,
            error => error.LanguageId == "dart" && error.Message == Message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable is not null - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Dart, "dart-null-guard", cause, source, doing);

    /// <summary>
    /// What the line did with the null, and what held it - a variable, or a function of the program whose result the ! follows
    /// - read from the line at the column Dart gives, which may be inside text, as in '${member!.age}'. Null when the column is
    /// not at a !, or what is before it is not a plain name or call.
    /// </summary>
    public static (string Doing, string? Name, string? CalledFunction)? Read(string line, int? column)
    {
        if (column is not { } at || at < 2 || at > line.Length || line[at - 1] != '!') return null;

        var before = line[..(at - 1)].TrimEnd();
        var after = line[at..].TrimStart();
        var doing = Regex.Match(after, @"^\.\s*(?<member>[A-Za-z_$][\w$]*)\s*(?<call>\()?") is { Success: true } used
            ? used.Groups["call"].Success ? $"calls {used.Groups["member"].Value} on {{0}}" : $"reads {used.Groups["member"].Value} from {{0}}"
            : after.StartsWith('[') ? "picks an item out of {0}" : "uses ! to say {0} is not null";

        if (before.EndsWith(')'))
        {
            var open = Brackets.Opening(before, before.Length - 1);
            var function = open > 0 ? Regex.Match(before[..open], @"(?<![\w.$])(?<name>[A-Za-z_$][\w$]*)\s*$") : Match.Empty;
            return function.Success ? (doing, null, function.Groups["name"].Value) : null;
        }

        var held = Regex.Match(before, @"(?<![\w.$])(?<name>[A-Za-z_$][\w$]*)$");
        return held.Success && held.Groups["name"].Value is not ("this" or "super") ? (doing, held.Groups["name"].Value, null) : null;
    }

    /// <summary>How Dart writes what following a null needs: its functions and methods, what sets a variable, and a printed line.</summary>
    private sealed partial class DartDialect : BraceDialect
    {
        /// <summary>A function or method with a body in braces: Member? findMember(List&lt;Member&gt; members, int id) {.</summary>
        [GeneratedRegex(@"^\s*(?:(?:static|external)\s+)*(?:[\w.]+(?:<[^()]*>)?\??\s+)?(?<name>[A-Za-z_$][\w$]*)\s*(?:<[^()]*>)?\s*\((?<parameters>[^)]*)\)\s*(?:async\*?|sync\*)?\s*\{\s*$")]
        private static partial Regex FunctionHeader();

        /// <summary>A return and the like, a block's first line, and a declaration, whose name the lines after it use.</summary>
        [GeneratedRegex(@"^\s*(?:return|throw|rethrow|break|continue|if|else|for|while|do|switch|case|default|try|catch|finally|yield|assert)\b|^\s*(?:(?:final|var|const|late)\s+)?(?:[\w.]+(?:<[^;=]*>)?\??\s+)?[A-Za-z_$][\w$]*\s*=(?![=>])")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^(?<receiver>[A-Za-z_$][\w$]*)\[(?<key>.+)\]$")]
        private static partial Regex Indexed();

        [GeneratedRegex(@"^(?:int|double|num)\.tryParse\(")]
        private static partial Regex TriedAsANumber();

        [GeneratedRegex(@"^(?!\s*(?:if|else|for|while|do|return|case|switch|try|catch|throw|yield|assert)\b)\s*(?:(?:final|var|const|late)\s+)?(?:(?<type>[\w.]+(?:<[^;=]*>)?\??)\s+)?(?<name>[A-Za-z_$][\w$]*)\s*(?:[-+*/%~]|\?\?)?=(?![=>])[^;]*;\s*$|^\s*(?<name>[A-Za-z_$][\w$]*)\s*(?:\+\+|--)\s*;\s*$|^\s*(?<type>[\w.]+(?:<[^;=]*>)?\?)\s+(?<name>[A-Za-z_$][\w$]*)\s*;\s*$")]
        private static partial Regex SetsAny();

        public override string ErrorName => "null check error";

        public override Regex SetsAnyName => SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["null"];

        public override string SomethingThere => "a value";

        /// <summary>Dart's: a function that can give back null and reaches its end gives back null.</summary>
        public override string? ReachingTheEndGives => "null";

        public override bool StatementsEndInSemicolons => true;

        public override Regex NotAStatementToSkip => NotToSkip();

        /// <summary>A raw string, r"text", in which a $ is only a $ - Dart reads one in any other string as a value to put in.</summary>
        public override string Quoted(string text) => "r\"" + text + "\"";

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (FunctionHeader().Match(masked[index]) is not { Success: true } header ||
                    header.Groups["name"].Value is "if" or "for" or "while" or "switch" or "catch" or "return" or "assert") continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        public override Regex Sets(string name) =>
            new($@"^\s*(?:(?:final|var|const|late)\s+)?(?:[\w.]+(?:<[^;=]*>)?\??\s+)?{Regex.Escape(name)}\s*=(?![=>])\s*(?<right>[^;]*[^;\s])\s*;\s*$");

        /// <summary><c>Member? best;</c> and <c>var best;</c> give best null without saying so; a late one has no value until it is given one.</summary>
        public override string? SetsWithoutSaying(string masked, string name) =>
            Regex.IsMatch(masked, $@"^\s*(?:var|[\w.]+(?:<[^;=]*>)?\?)\s+{Regex.Escape(name)}\s*;\s*$") ? "null" : null;

        public override string Print(string expression) => $"print({expression});";

        public override string MissingOrValue(string name) => $"({name} == null ? 'null' : 'value')";

        /// <summary>The value as Dart's toString writes it - [40, 55], Instance of 'Member' - with text in quotes.</summary>
        public override string Shown(string type, string name) =>
            $"(() {{ final Object? fixFinderValue = {name}; return fixFinderValue is String ? '\"' + fixFinderValue + '\"' : '$fixFinderValue'; }})()";

        /// <summary>
        /// A Map indexed with a key, where the file declares the Map, and int.tryParse: Dart's documentation says a Map's [] gives
        /// back null for a key it does not have, and tryParse null where parse would throw.
        /// </summary>
        public override Documented? Library(string right, BraceCode code)
        {
            if (TriedAsANumber().IsMatch(right))
                return new Documented("tryParse gives back null for text that is not a number.", "Decide what the program should do when the text is not a number, and {check}.");

            if (Indexed().Match(right) is not { Success: true } indexed) return null;

            var map = Regex.Escape(indexed.Groups["receiver"].Value);
            if (!code.Masked.Any(line => Regex.IsMatch(line, $@"\bMap\s*<[^;]*>\s+{map}\b|\b{map}\s*=\s*(?:<[^;=]*>\s*)?\{{|\b{map}\s*=\s*Map\b"))) return null;

            var receiver = BraceMissingValue.Code(indexed.Groups["receiver"].Value);
            return new Documented($"{receiver} is a Map, and a Map's [] gives back null for a key it does not have.",
                "Ask containsKey first, or give a value of your own with ?? - or {check}.");
        }

        /// <summary>
        /// Each parameter's name, split at the commas outside brackets - Map&lt;String, int&gt; counts is one - with its type,
        /// required, a default and the brackets of named and optional ones taken off; this.name is left out.
        /// </summary>
        private static List<(string Type, string Name)> Parameters(string parameters)
        {
            var pieces = new List<string>();
            var depth = 0;
            var start = 0;
            for (var index = 0; index < parameters.Length; index++)
            {
                if (parameters[index] is '<' or '(' or '[' or '{') depth++;
                else if (parameters[index] is '>' or ')' or ']' or '}') depth--;
                else if (parameters[index] == ',' && depth == 0)
                {
                    pieces.Add(parameters[start..index]);
                    start = index + 1;
                }
            }

            pieces.Add(parameters[start..]);

            return [.. pieces
                .Select(piece => Regex.Replace(piece.Trim().Trim('{', '}', '[', ']').Trim(), @"\s*=.*$", ""))
                .Select(piece => Regex.Replace(piece, @"^required\s+", ""))
                .Select(piece => Regex.Match(piece, @"^(?:(?<type>.+?)\s+)?(?<name>[A-Za-z_$][\w$]*)$"))
                .Where(match => match.Success && !match.Value.Contains("this.", StringComparison.Ordinal))
                .Select(match => (match.Groups["type"].Value, match.Groups["name"].Value))];
        }
    }
}
