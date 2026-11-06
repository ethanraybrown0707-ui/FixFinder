using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a JavaScript program stopping on undefined or null: the variable the line read a property from, the line
/// that gave it that value, and - when one of the program's own functions gave it - which return gave it back, or that the
/// function reached its end without one, and why its other returns never ran.
/// </summary>
/// <remarks>
/// Node's message says what was missing - undefined or null - and which property was read, but not from what: the column of
/// the line it stopped on does, as it points at that property. <see cref="BraceMissingValue"/> runs a copy of the program
/// that prints where the value could have come from.
/// </remarks>
public static partial class JavaScriptMissingOrigin
{
    [GeneratedRegex(@"^Cannot read properties of (?<value>undefined|null) \(reading '(?<property>[^']*)'\)$")]
    private static partial Regex Reading();

    [GeneratedRegex(@"^Cannot set properties of (?<value>undefined|null) \(setting '(?<property>[^']*)'\)$")]
    private static partial Regex Setting();

    [GeneratedRegex(@"^Cannot destructure property '(?<property>[^']*)' of '(?<name>[A-Za-z_$][\w$]*)' as it is (?<value>undefined|null)\.$")]
    private static partial Regex Destructuring();

    [GeneratedRegex(@"^(?<name>[A-Za-z_$][\w$]*) is not iterable$")]
    private static partial Regex NotIterable();

    private static readonly JavaScriptDialect JavaScript = new();

    /// <summary>
    /// Runs a copy of the program with the places the value could have come from marked, and says where it came from - or
    /// null when the copy does not stop the same way, or does not show it.
    /// </summary>
    /// <param name="column">The column Node's trace gives for the line it stopped on: where the property read is.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, string message, int crashLine, int? column, int? calledFrom,
        CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is not { } line) return Task.FromResult<BraceTraced?>(null);
        if (Read(message, line, column) is not var (doing, value, name, calledFunction)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(JavaScript, program, source, typed, crashLine, calledFrom, doing, value, name, calledFunction,
            error => error.ShortExceptionType == "TypeError" && error.Message == message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable holds a value - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(JavaScript, "js-missing-value-guard", cause, source, doing);

    /// <summary>
    /// What the line did with the value, the value's word, and what held it - a variable, or a function of the program whose
    /// result was used straight away - read from Node's message and the line at the column it gives. Null when the message is
    /// not one of these, or what was read from is not a plain name or call.
    /// </summary>
    public static (string Doing, string? Value, string? Name, string? CalledFunction)? Read(string message, string line, int? column)
    {
        if (Destructuring().Match(message) is { Success: true } destructuring)
            return ($"takes {destructuring.Groups["property"].Value} out of {{0}}", destructuring.Groups["value"].Value, destructuring.Groups["name"].Value, null);

        // Which of undefined and null it held is not said, and is left to what the copy prints.
        if (NotIterable().Match(message) is { Success: true } iterating)
            return ("goes through {0} one item at a time", null, iterating.Groups["name"].Value, null);

        var reading = Reading().Match(message);
        var setting = Setting().Match(message);
        if (!reading.Success && !setting.Success || column is not { } at || at < 1 || at > line.Length) return null;

        var said = reading.Success ? reading : setting;
        var property = said.Groups["property"].Value;
        var doing = reading.Success ? $"reads {property} from {{0}}" : $"sets {property} on {{0}}";

        var masked = CodeText.Mask(line, Syntax.CLike);
        if (ObjectEnd(masked, at - 1, property, reading.Success) is not { } end) return null;

        // What the property was read from ends just before it: a name, or a call's closing bracket.
        if (masked[end] == ')')
        {
            var open = Brackets.Opening(masked, end);
            var function = open > 0 ? Regex.Match(masked[..open], @"(?<![\w$.])(?<name>[A-Za-z_$][\w$]*)\s*$") : Match.Empty;
            return function.Success ? (doing, said.Groups["value"].Value, null, function.Groups["name"].Value) : null;
        }

        var held = Regex.Match(masked[..(end + 1)], @"(?<![\w$.\])])(?<name>[A-Za-z_$][\w$]*)$");
        return held.Success && held.Groups["name"].Value is not "this" ? (doing, said.Groups["value"].Value, held.Groups["name"].Value, null) : null;
    }

    /// <summary>
    /// Where what the property was read from ends, from the column Node gives: the property's name after a dot, or the [ of
    /// an index - or, for a property set, the = after it.
    /// </summary>
    private static int? ObjectEnd(string masked, int at, string property, bool reading)
    {
        if (!reading)
        {
            // Node points at the = of a property set: the property is before it.
            var before = masked[..at].TrimEnd();
            if (!before.EndsWith(property, StringComparison.Ordinal) && !before.EndsWith(']')) return null;
            at = before.EndsWith(']') ? Brackets.Opening(masked, before.Length - 1) : before.Length - property.Length;
            if (at < 0) return null;
        }

        if (masked[at] == '[') return at >= 1 ? LastCode(masked, at - 1) : null;

        // A property's name after a dot, or ?. - the dot before it is where what it was read from ends. Node gives the name's
        // column after a name - m.age - and the dot's after a call: find(...).length.
        var dot = masked[at] == '.' ? at : LastCode(masked, at - 1);
        if (dot is not { } found || masked[found] != '.') return null;
        var end = found >= 1 && masked[found - 1] == '?' ? found - 2 : found - 1;
        return end >= 0 ? LastCode(masked, end) : null;
    }

    private static int? LastCode(string masked, int from)
    {
        for (var index = from; index >= 0; index--)
            if (!char.IsWhiteSpace(masked[index])) return index;
        return null;
    }

    /// <summary>How JavaScript writes what following a missing value needs: its functions, what sets a variable, and a line printed to standard error.</summary>
    private sealed partial class JavaScriptDialect : BraceDialect
    {
        [GeneratedRegex(@"^\s*(?:export\s+(?:default\s+)?)?(?:async\s+)?function\s*\*?\s*(?<name>[A-Za-z_$][\w$]*)\s*\((?<parameters>[^)]*)\)\s*(?::[^{]+)?\{?\s*$")]
        private static partial Regex FunctionDeclaration();

        [GeneratedRegex(@"^\s*(?:export\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s+)?(?:function\s*\*?\s*[\w$]*\s*)?\((?<parameters>[^)]*)\)\s*(?::[^=>{]+)?(?:=>\s*)?\{\s*$")]
        private static partial Regex FunctionExpression();

        [GeneratedRegex(@"^\s*(?:(?:static|async|public|private|protected)\s+)*(?<name>[A-Za-z_$][\w$]*)\s*\((?<parameters>[^)]*)\)\s*(?::[^{]+)?\{\s*$")]
        private static partial Regex ClassMethod();

        [GeneratedRegex(@"^\s*(?:return|throw|break|continue|if|else|for|while|do|switch|case|default|try|catch|finally|yield|const|let|var|function|class|import|export)\b|^\s*[A-Za-z_$][\w$]*\s*=(?!=)")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^(?<receiver>[A-Za-z_$][\w$]*)\.(?<method>find|get)\(")]
        private static partial Regex LookedUp();

        public override string ErrorName => "TypeError";

        public override Regex SetsAnyName => SetsAny();

        [GeneratedRegex(@"^(?!\s*(?:if|else|for|while|do|return|case|switch|try|catch|throw|export|yield)\b)\s*(?:(?:const|let|var)\s+)?(?<name>[A-Za-z_$][\w$]*)\s*(?::[^=]+)?[-+*/%]?=(?![=>])[^;]*;?\s*$|^\s*(?<name>[A-Za-z_$][\w$]*)\s*(?:\+\+|--)\s*;?\s*$|^\s*(?:let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*(?::[^=;]+)?;?\s*$")]
        private static partial Regex SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["undefined", "null"];

        public override string SomethingThere => "a value";

        public override string? ReachingTheEndGives => "undefined";

        public override bool StatementsEndInSemicolons => false;

        public override Regex NotAStatementToSkip => NotToSkip();

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                var header = FunctionDeclaration().Match(masked[index]);
                if (!header.Success) header = FunctionExpression().Match(masked[index]);
                if (!header.Success) header = ClassMethod().Match(masked[index]);
                if (!header.Success || header.Groups["name"].Value is "if" or "for" or "while" or "switch" or "catch" or "function" or "return") continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        public override Regex Sets(string name) =>
            new($@"^\s*(?:(?:const|let|var)\s+)?{Regex.Escape(name)}\s*(?::[^=]+)?=(?![=>])\s*(?<right>[^;]*[^;\s])\s*(?:;\s*)?$");

        /// <summary><c>let best;</c> gives best undefined without saying so.</summary>
        public override string? SetsWithoutSaying(string masked, string name) =>
            Regex.IsMatch(masked, $@"^\s*(?:let|var)\s+{Regex.Escape(name)}\s*(?::[^=;]+)?;?\s*$") ? "undefined" : null;

        public override string Print(string expression) => $"console.error({expression});";

        public override string MissingOrValue(string name) => $"({name} === undefined ? \"undefined\" : {name} === null ? \"null\" : \"value\")";

        public override string Shown(string type, string name) =>
            $"(() => {{ try {{ const said = JSON.stringify({name}); return said === undefined ? String({name}) : said; }} catch {{ return String({name}); }} }})()";

        /// <summary>
        /// An array's find, where the file declares the array, and a Map's get, where it declares the Map: JavaScript's
        /// documentation says the one gives undefined when no item passes its test, and the other for a key it does not have.
        /// </summary>
        public override Documented? Library(string right, BraceCode code)
        {
            if (LookedUp().Match(right) is not { Success: true } looked || code.DeclaredAs(looked.Groups["receiver"].Value) is not { } declared) return null;

            var receiver = BraceMissingValue.Code(looked.Groups["receiver"].Value);
            if (looked.Groups["method"].Value == "find" && declared.Contains("= [", StringComparison.Ordinal))
            {
                return new Documented($"{receiver} is an array, and an array's find gives back undefined when no item passes its test.",
                    "Decide what the program should do when no item passes the test, and {check}.");
            }

            if (looked.Groups["method"].Value == "get" && declared.Contains("= new Map", StringComparison.Ordinal))
            {
                return new Documented($"{receiver} is a Map, and a Map's get gives back undefined for a key it does not have.",
                    "Ask has first, or give a value of your own with ?? - or {check}.");
            }

            return null;
        }

        /// <summary>Each parameter's name: a default, a type and a ... taken off; one taken apart, { a, b }, is left out.</summary>
        private static List<(string Type, string Name)> Parameters(string parameters) =>
            parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(parameter => Regex.Match(parameter, @"^(?:\.\.\.)?(?<name>[A-Za-z_$][\w$]*)\s*\??\s*(?::[^=]+)?(?:=.*)?$"))
                .Where(match => match.Success)
                .Select(match => ("", match.Groups["name"].Value))
                .ToList();
    }
}
