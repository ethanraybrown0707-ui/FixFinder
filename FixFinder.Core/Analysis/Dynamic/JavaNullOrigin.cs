using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Java program stopping on null: the variable Java's own message names, the line that gave it null,
/// and - when one of the program's own methods gave it - which return gave back null and why the others never ran.
/// </summary>
/// <remarks>
/// Java's message already names what was null, since FixFinder builds with -g; what it does not say is where that null came
/// from. <see cref="BraceMissingValue"/> runs a copy of the program that prints where it could have come from.
/// </remarks>
public static partial class JavaNullOrigin
{
    [GeneratedRegex(@"because ""(?<name>[A-Za-z_]\w*)"" is null$")]
    private static partial Regex NamedNull();

    [GeneratedRegex(@"because the return value of ""(?<owner>[\w.$]+)\.(?<method>[A-Za-z_]\w*)\((?<types>[^)]*)\)"" is null$")]
    private static partial Regex ReturnedNull();

    [GeneratedRegex(@"^Cannot invoke ""(?<type>[\w.$]+)\.(?<method>\w+)\((?<types>[^)]*)\)""")]
    private static partial Regex Invoking();

    [GeneratedRegex(@"^Cannot (?<what>read|assign) field ""(?<field>\w+)""")]
    private static partial Regex Field();

    [GeneratedRegex(@"^Cannot (?<what>load from|store to) \w+ array")]
    private static partial Regex ArrayItem();

    private static readonly JavaDialect Java = new();

    /// <summary>
    /// Runs a copy of the program with the places the null could have come from marked, and says where it came from - or
    /// null when the copy does not stop the same way, or does not show it.
    /// </summary>
    /// <param name="program">The file the program is run from.</param>
    /// <param name="file">The file it stopped in.</param>
    /// <param name="message">Java's message: Cannot invoke "java.util.Map.get(Object)" because "member" is null.</param>
    /// <param name="calledFrom">The line of the same file the method it stopped in was called from, when Java's trace says so.</param>
    public static Task<RootCause?> TraceAsync(string program, string file, string? typed, string message, int crashLine, int? calledFrom,
        CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source) return Task.FromResult<RootCause?>(null);

        var named = NamedNull().Match(message);
        var returned = ReturnedNull().Match(message);
        if (!named.Success && !returned.Success) return Task.FromResult<RootCause?>(null);

        return BraceMissingValue.TraceAsync(Java, program, source, typed, crashLine, calledFrom, DoingIn(message), "null",
            named.Success ? named.Groups["name"].Value : null, returned.Success ? returned.Groups["method"].Value : null,
            error => error.ShortExceptionType == "NullPointerException" && error.Message == message, cancellationToken);
    }

    /// <summary>The change that makes line it stopped on run only when the variable is not null - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Java, "java-null-guard", cause, source, doing);

    /// <summary>What the line that stopped did with the null, from the start of Java's message, with {0} where its name goes.</summary>
    public static string DoingIn(string message)
    {
        if (Invoking().Match(message) is { Success: true } invoking)
        {
            var called = invoking.Groups["method"].Value;
            return invoking.Groups["type"].Value.StartsWith("java.lang.", StringComparison.Ordinal) && called.EndsWith("Value", StringComparison.Ordinal)
                ? "uses {0} as a plain " + called[..^"Value".Length]
                : $"calls {called} on {{0}}";
        }

        if (Field().Match(message) is { Success: true } field)
            return field.Groups["what"].Value == "read" ? $"reads the field {field.Groups["field"].Value} of {{0}}" : $"sets the field {field.Groups["field"].Value} of {{0}}";

        if (ArrayItem().Match(message) is { Success: true } item)
            return item.Groups["what"].Value == "load from" ? "reads an item of {0}" : "stores an item in {0}";

        if (message.StartsWith("Cannot read the array length", StringComparison.Ordinal)) return "asks for the length of {0}";
        return "uses {0}";
    }

    /// <summary>How Java writes what following a null needs: its methods, what sets a variable, and a line printed to standard error.</summary>
    private sealed partial class JavaDialect : BraceDialect
    {
        [GeneratedRegex(@"^\s*(?:(?:public|private|protected|static|final|synchronized|abstract|native)\s+)*(?:<[^>]+>\s+)?[\w.$<>\[\],? ]+\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*(?:throws\s+[\w.,\s]+)?\{?\s*$")]
        private static partial Regex MethodHeader();

        [GeneratedRegex(@"^\s*(?:return|throw|break|continue|if|else|for|while|do|switch|case|default|try|catch|finally|synchronized|yield)\b|^\s*(?:final\s+)?[\w.$]+(?:<[^;=]*>)?(?:\[\])*\s+[A-Za-z_]\w*\s*=")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^(?<receiver>[A-Za-z_]\w*)\.get\([^,()]*\)$")]
        private static partial Regex GetCalled();

        public override string ErrorName => "NullPointerException";

        public override IReadOnlyList<string> MissingWords { get; } = ["null"];

        public override string SomethingThere => "an object";

        public override string? ReachingTheEndGives => null;

        public override bool StatementsEndInSemicolons => true;

        public override Regex NotAStatementToSkip => NotToSkip();

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (MethodHeader().Match(masked[index]) is not { Success: true } header || header.Groups["name"].Value is "if" or "for" or "while" or "switch" or "catch") continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        public override Regex Sets(string name) =>
            new($@"^\s*(?:final\s+)?(?:[\w.$]+(?:<[^;=]*>)?(?:\[\])*\s+)?{Regex.Escape(name)}\s*=(?!=)\s*(?<right>[^;]*[^;\s])\s*;\s*$");

        public override string Print(string expression) => $"System.err.println({expression});";

        public override string MissingOrValue(string name) => $"({name} == null ? \"null\" : \"value\")";

        public override string Shown(string type, string name) =>
            type.EndsWith("[][]", StringComparison.Ordinal) ? $"java.util.Arrays.deepToString({name})"
            : type.EndsWith("[]", StringComparison.Ordinal) || type.EndsWith("...", StringComparison.Ordinal) ? $"java.util.Arrays.toString({name})"
            : type is "String" or "java.lang.String" ? $"({name} == null ? \"null\" : \"\\\"\" + {name} + \"\\\"\")"
            : $"String.valueOf({name})";

        /// <summary>A Map's get, where the file declares the Map: Java documents that it gives null for a key it does not have.</summary>
        public override Documented? Library(string right, BraceCode code) =>
            GetCalled().Match(right) is { Success: true } get && code.DeclaredAs(get.Groups["receiver"].Value) is { } type &&
            Regex.IsMatch(type, @"^(?:java\.util\.)?(?:Map|HashMap|TreeMap|LinkedHashMap)\b")
                ? new Documented($"{BraceMissingValue.Code(get.Groups["receiver"].Value)} is declared a Map, and a Map's get gives back null for a key it does not have.",
                    "Use getOrDefault, to give back a value of your own when the key is not there - or {check}.")
                : null;

        private static List<(string Type, string Name)> Parameters(string parameters)
        {
            var split = new List<(string, string)>();
            var depth = 0;
            var start = 0;

            for (var i = 0; i <= parameters.Length; i++)
            {
                if (i < parameters.Length && parameters[i] == '<') depth++;
                else if (i < parameters.Length && parameters[i] == '>') depth--;
                else if (i == parameters.Length || parameters[i] == ',' && depth == 0)
                {
                    var one = Regex.Replace(parameters[start..i].Trim(), @"^(?:final\s+|@\w+\s+)*", "");
                    if (Regex.Match(one, @"^(?<type>.+?)\s+(?<name>[A-Za-z_]\w*)$") is { Success: true } parameter)
                        split.Add((parameter.Groups["type"].Value.Trim(), parameter.Groups["name"].Value));
                    start = i + 1;
                }
            }

            return split;
        }
    }
}
