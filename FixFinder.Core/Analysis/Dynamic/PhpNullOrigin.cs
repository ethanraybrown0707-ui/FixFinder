using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a PHP program stopping because a method was called on null, or a property set on it: the variable that
/// held null, the line that gave it null, and - when one of the program's own functions gave it - which return gave back
/// null and why the others never ran.
/// </summary>
/// <remarks>
/// PHP's message names the method or property - "Call to a member function describe() on null" - so what held null is the
/// variable the line calls that method on, or sets that property of: <c>$member-&gt;describe()</c>. Reading a property of
/// null is only a warning in PHP 8, and the program carries on, so it is not followed. <see cref="BraceMissingValue"/> then
/// runs a copy of the program that prints where its null could have come from.
/// </remarks>
public static partial class PhpNullOrigin
{
    [GeneratedRegex(@"^Call to a member function (?<method>\w+)\(\) on null$")]
    private static partial Regex MethodOnNull();

    [GeneratedRegex(@"^Attempt to assign property ""(?<property>\w+)"" on null$")]
    private static partial Regex PropertySetOnNull();

    private static readonly PhpDialect Php = new();

    /// <summary>
    /// Runs a copy of the program with the places the null could have come from marked, and says where it came from - or null
    /// when the line does not show what held it, or the copy does not stop the same way.
    /// </summary>
    /// <param name="program">The file the program is run from.</param>
    /// <param name="file">The file it stopped in.</param>
    /// <param name="calledFrom">The line of the same file the function it stopped in was called from, when PHP's trace says so.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, string message, int crashLine, int? calledFrom,
        CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is null) return Task.FromResult<BraceTraced?>(null);
        if (Read(message, CodeText.Mask(source.Line(crashLine)!, Syntax.CLike)) is not var (doing, name, calledFunction)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(Php, program, source, typed, crashLine, calledFrom, doing, "null", name, calledFunction,
            error => error.LanguageId == "php" && error.Message == message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable is not null - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Php, "php-null-guard", cause, source, doing);

    /// <summary>
    /// What the line did with the null, and what held it - a variable, or a function whose result the line used straight
    /// away, as in findMember($members, 2)-&gt;describe() - read from PHP's message and the line, with its text blanked. Null
    /// when the message is not one of these, or the method or property is not used on a plain variable or call.
    /// </summary>
    public static (string Doing, string? Name, string? CalledFunction)? Read(string message, string masked)
    {
        if (MethodOnNull().Match(message) is { Success: true } method)
        {
            var called = Regex.Escape(method.Groups["method"].Value);
            var doing = $"calls {method.Groups["method"].Value} on {{0}}";
            if (Regex.Match(masked, $@"(?<name>\$[A-Za-z_]\w*)\s*->\s*{called}\s*\(") is { Success: true } held && held.Groups["name"].Value != "$this")
                return (doing, held.Groups["name"].Value, null);
            if (Regex.Match(masked, $@"(?<![\w$>:\\])(?<function>[A-Za-z_]\w*)\s*\((?:[^()]|\([^()]*\))*\)\s*->\s*{called}\s*\(") is { Success: true } call)
                return (doing, null, call.Groups["function"].Value);
            return null;
        }

        if (PropertySetOnNull().Match(message) is { Success: true } property &&
            Regex.Match(masked, $@"(?<name>\$[A-Za-z_]\w*)\s*->\s*{Regex.Escape(property.Groups["property"].Value)}\s*=(?!=)") is { Success: true } set &&
            set.Groups["name"].Value != "$this")
        {
            return ($"sets {property.Groups["property"].Value} on {{0}}", set.Groups["name"].Value, null);
        }

        return null;
    }

    /// <summary>How PHP writes what following a null needs: its functions and methods, what sets a variable, and a line printed to standard error.</summary>
    private sealed partial class PhpDialect : BraceDialect
    {
        /// <summary>function name(...): type, with its brace on the line or the next, as a method's or a function's.</summary>
        [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final|abstract)\s+)*function\s+&?(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*(?::\s*[?\w\\|&]+\s*)?\{?\s*$")]
        private static partial Regex FunctionHeader();

        /// <summary>A return and the like, a block's first line, and a line that sets a variable, which the lines after it use.</summary>
        [GeneratedRegex(@"^\s*(?:return|throw|break|continue|if|else|elseif|for|foreach|while|do|switch|case|default|try|catch|finally|function|class|use|namespace|yield)\b|^\s*\$\w+\s*(?:[-+*/%.]|\?\?)?=(?![=>])")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^array_find\(")]
        private static partial Regex Found();

        [GeneratedRegex(@"^(?!\s*(?:if|else|elseif|for|foreach|while|do|return|case|switch|try|catch|throw|echo|print|yield)\b)\s*(?<name>\$[A-Za-z_]\w*)\s*(?:[-+*/%.]|\?\?)?=(?![=>])[^;]*;\s*$|^\s*(?<name>\$[A-Za-z_]\w*)\s*(?:\+\+|--)\s*;\s*$")]
        private static partial Regex SetsAny();

        [GeneratedRegex(@"^\s*\}?\s*(?:else\s*if|elseif)\s*\(")]
        private static partial Regex ElseIfInBrackets();

        [GeneratedRegex(@"(?<![=!<>])=(?![=>])\s*(?:static\s+)?(?:function|fn)\s*&?\s*\(")]
        private static partial Regex FunctionGiven();

        public override string ErrorName => "Error";

        public override Regex SetsAnyName => SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["null"];

        public override string SomethingThere => "an object";

        /// <summary>PHP's documentation: a function whose return gives no value, or that reaches its end, gives back null.</summary>
        public override string? ReachingTheEndGives => "null";

        public override bool StatementsEndInSemicolons => true;

        public override Regex NotAStatementToSkip => NotToSkip();

        public override Regex ElseIfLine => ElseIfInBrackets();

        public override string GuardOpening(string name) => $"if ({name} !== null) {{";

        /// <summary>In single quotes, where PHP reads a $ as a $ and not as a variable.</summary>
        public override string Quoted(string text) => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

        public override string Joining => " . ";

        /// <summary>What follows the = starts with function (...) or fn (...) - a PHP array's =&gt; gives no function.</summary>
        public override bool GivesAFunction(string masked) => FunctionGiven().IsMatch(masked);

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
            new($@"^\s*{Regex.Escape(name)}\s*=(?![=>])\s*(?<right>[^;]*[^;\s])\s*;\s*$");

        public override string Print(string expression) => $"fwrite(STDERR, {expression} . \"\\n\");";

        public override string MissingOrValue(string name) => $"({name} === null ? 'null' : 'value')";

        /// <summary>The value as JSON - an object with its class's name first, Member {"id":1,"age":30} - on one line, as PHP's json_encode writes it.</summary>
        public override string Shown(string type, string name) =>
            $"((is_object({name}) ? get_class({name}) . ' ' : '') . json_encode({name}, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PARTIAL_OUTPUT_ON_ERROR))";

        /// <summary>PHP's documentation says array_find gives back null when no item passes its test.</summary>
        public override Documented? Library(string right, BraceCode code) =>
            Found().IsMatch(right)
                ? new Documented("array_find gives back null when no item passes its test.", "Decide what the program should do when no item passes the test, and {check}.")
                : null;

        /// <summary>Each parameter's name, with its $: a type, a default, a reference's &amp; and a ... taken off.</summary>
        private static List<(string Type, string Name)> Parameters(string parameters) =>
            [.. parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(parameter => Regex.Match(parameter, @"^(?:(?:public|private|protected|readonly)\s+)*(?:(?<type>[?\w\\|&]+)\s+)?&?(?:\.\.\.)?(?<name>\$[A-Za-z_]\w*)(?:\s*=.*)?$"))
                .Where(match => match.Success)
                .Select(match => (match.Groups["type"].Value, match.Groups["name"].Value))];
    }
}
