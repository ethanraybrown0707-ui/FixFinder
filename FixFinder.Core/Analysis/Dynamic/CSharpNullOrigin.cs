using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a C# program stopping with a NullReferenceException: the variable the line read from, the line that
/// gave it null, and - when one of the program's own methods gave it - which return gave back null and why the others never
/// ran.
/// </summary>
/// <remarks>
/// C#'s message names nothing - "Object reference not set to an instance of an object." - so what was null is read from the
/// line it stopped on: the variables it reads a member or an item from, of those the program sets or is given as a
/// parameter. Only when that is one variable is anything said. <see cref="BraceMissingValue"/> then runs a copy of the program
/// that prints where its null could have come from.
/// </remarks>
public static partial class CSharpNullOrigin
{
    /// <summary>A name the line reads a member or an item from: member.Age, member?.Age, marks[0].</summary>
    [GeneratedRegex(@"(?<![\w.@])(?<name>@?[A-Za-z_]\w*)\s*(?:\?\s*)?(?<use>\.\s*(?<member>[A-Za-z_]\w*)\s*(?<call>\()?|\[)")]
    private static partial Regex ReadFrom();

    private static readonly CSharpDialect CSharp = new();

    /// <summary>
    /// Runs a copy of the program with the places the null could have come from marked, and says where it came from - or null
    /// when what was null is not one variable, or the copy does not stop the same way.
    /// </summary>
    /// <param name="program">The file the program is run from.</param>
    /// <param name="file">The file it stopped in.</param>
    /// <param name="calledFrom">The line of the same file the method it stopped in was called from, when .NET's trace says so.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, int crashLine, int? calledFrom, CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || Read(source, crashLine) is not var (name, doing)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(CSharp, program, source, typed, crashLine, calledFrom, doing, "null", name, null,
            error => error.ShortExceptionType == "NullReferenceException", cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable is not null - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(CSharp, "csharp-null-guard", cause, source, doing);

    /// <summary>
    /// What was null on the line and what the line did with it - the one variable it reads a member or an item from that the
    /// program sets, or is given as a parameter - or null when that is none, or more than one.
    /// </summary>
    public static (string Name, string Doing)? Read(SourceFile source, int crashLine)
    {
        if (source.Line(crashLine) is null) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var code = new BraceCode(CSharp, source.Lines, masked);
        var around = code.FunctionAround(crashLine);

        var used = ReadFrom().Matches(masked[crashLine - 1])
            .Where(each => each.Groups["name"].Value is not ("this" or "base"))
            .GroupBy(each => each.Groups["name"].Value)
            .Where(each => code.LinesSetting(each.Key, around).Any() || around?.Parameters.Any(parameter => parameter.Name == each.Key) == true)
            .ToList();
        if (used is not [var only]) return null;

        var first = only.First();
        var doing = first.Groups["use"].Value.StartsWith('[') ? "picks an item out of {0}"
            : first.Groups["call"].Success ? $"calls {first.Groups["member"].Value} on {{0}}"
            : $"reads {first.Groups["member"].Value} from {{0}}";
        return (only.Key, doing);
    }

    /// <summary>How C# writes what following a null needs: its methods and local functions, what sets a variable, and a line printed to standard error.</summary>
    private sealed partial class CSharpDialect : BraceDialect
    {
        [GeneratedRegex(@"^\s*(?:(?:public|private|protected|internal|static|async|virtual|override|sealed|unsafe|extern)\s+)*(?:<[^>]+>\s+)?[\w.<>\[\],?]+\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^>]+>)?\s*\((?<parameters>[^)]*)\)\s*\{?\s*$")]
        private static partial Regex MethodHeader();

        [GeneratedRegex(@"^\s*(?:return|throw|break|continue|if|else|for|foreach|while|do|switch|case|default|try|catch|finally|lock|using|yield)\b|^\s*(?:var|[\w.]+(?:<[^;=]*>)?(?:\[\])*\??)\s+[A-Za-z_]\w*\s*=")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^(?<receiver>[A-Za-z_]\w*)\.(?<method>FirstOrDefault|LastOrDefault|SingleOrDefault|Find|GetValueOrDefault)\(")]
        private static partial Regex LookedUp();

        public override string ErrorName => "NullReferenceException";

        public override Regex SetsAnyName => SetsAny();

        [GeneratedRegex(@"^(?!\s*(?:if|else|for|foreach|while|do|return|case|switch|try|catch|throw|yield|using|lock)\b)\s*(?:(?<type>[\w.]+(?:<[^;=]*>)?(?:\[\])*\??)\s+)?(?<name>[A-Za-z_]\w*)\s*[-+*/%]?=(?![=>])[^;]*;\s*$|^\s*(?<name>[A-Za-z_]\w*)\s*(?:\+\+|--)\s*;\s*$")]
        private static partial Regex SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["null"];

        public override string SomethingThere => "an object";

        public override string? ReachingTheEndGives => null;

        public override bool StatementsEndInSemicolons => true;

        public override Regex NotAStatementToSkip => NotToSkip();

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (MethodHeader().Match(masked[index]) is not { Success: true } header ||
                    header.Groups["name"].Value is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock") continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        public override Regex Sets(string name) =>
            new($@"^\s*(?:(?:var|[\w.]+(?:<[^;=]*>)?(?:\[\])*\??)\s+)?{Regex.Escape(name)}\s*=(?![=>])\s*(?<right>[^;]*[^;\s])\s*;\s*$");

        public override string Print(string expression) => $"System.Console.Error.WriteLine({expression});";

        public override string MissingOrValue(string name) => $"({name} is null ? \"null\" : \"value\")";

        /// <summary>
        /// The value as C# would write it: text in quotes, a list as its items between [ ], anything else - a record's own
        /// Member { Id = 1, Age = 30 } - as ToString gives it. Not JSON, which a program built for trimming cannot write.
        /// </summary>
        public override string Shown(string type, string name) =>
            $"((System.Func<object?, string>)(fixFinderValue => fixFinderValue is null ? \"null\" : fixFinderValue is string fixFinderText ? \"\\\"\" + fixFinderText + \"\\\"\" " +
            $": fixFinderValue is System.Collections.IEnumerable fixFinderItems ? \"[\" + string.Join(\", \", System.Linq.Enumerable.Select(System.Linq.Enumerable.Cast<object?>(fixFinderItems), " +
            $"fixFinderItem => fixFinderItem is string fixFinderWord ? \"\\\"\" + fixFinderWord + \"\\\"\" : System.Convert.ToString(fixFinderItem) ?? \"null\")) + \"]\" : System.Convert.ToString(fixFinderValue) ?? \"null\"))({name})";

        /// <summary>
        /// The lookups .NET documents as giving the default - null, for objects - when nothing is found: FirstOrDefault,
        /// LastOrDefault, SingleOrDefault, a list's Find and a dictionary's GetValueOrDefault.
        /// </summary>
        public override Documented? Library(string right, BraceCode code) =>
            LookedUp().Match(right) is { Success: true } looked
                ? new Documented($"{looked.Groups["method"].Value} gives back the default when nothing is found - null, for a list or dictionary of objects.",
                    "Decide what the program should do when nothing is found, and {check}.")
                : null;

        private static List<(string Type, string Name)> Parameters(string parameters) =>
            [.. parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(parameter => Regex.Match(parameter, @"^(?:(?:this|ref|out|in|params)\s+)?(?<type>.+?)\s+(?<name>@?[A-Za-z_]\w*)(?:\s*=.*)?$"))
                .Where(match => match.Success)
                .Select(match => (match.Groups["type"].Value, match.Groups["name"].Value))];
    }
}
