using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Lua program stopping on nil - "attempt to index a nil value (local 'member')" and the rest: the
/// variable that held nil, the line that gave it nil, and - when one of the program's own functions gave it - which return
/// gave back nil and why the others never ran.
/// </summary>
/// <remarks>
/// Lua's message names the variable and says whether it is a local, a global or an upvalue, so what held nil is read from
/// the message itself; a field - (field 'age') - is part of a table, not a variable, and is not followed.
/// <see cref="BraceMissingValue"/> then runs a copy of the program that prints where its nil could have come from - Lua's
/// blocks end with end rather than a brace, which the dialect tells it.
/// </remarks>
public static partial class LuaNilOrigin
{
    /// <summary>What Lua says it tried to do with nil, and the variable that held it.</summary>
    [GeneratedRegex(@"^attempt to (?<what>index|perform arithmetic on|concatenate|get length of) a nil value \((?:local|global|upvalue) '(?<name>[A-Za-z_]\w*)'\)$")]
    private static partial Regex TriedWithNil();

    private static readonly LuaDialect Lua = new();

    /// <summary>
    /// Runs a copy of the program with the places the nil could have come from marked, and says where it came from - or null
    /// when the message names no variable, or the copy does not stop the same way.
    /// </summary>
    /// <param name="calledFrom">The line of the same file the function it stopped in was called from, when Lua's traceback says so.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, string message, int crashLine, int? calledFrom,
        CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is not { } line) return Task.FromResult<BraceTraced?>(null);
        if (Read(message, CodeText.Mask(line, Syntax.Lua)) is not var (doing, name)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(Lua, program, source, typed, crashLine, calledFrom, doing, "nil", name, null,
            error => error.LanguageId == "lua" && error.Message == message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable is not nil - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Lua, "lua-nil-guard", cause, source, doing);

    /// <summary>
    /// The variable Lua's message names, and what the line did with it - read a field of it, call a method on it, take an item
    /// out of it, add to it, join it to text, take its length - or null for a message that names no variable.
    /// </summary>
    public static (string Doing, string Name)? Read(string message, string masked)
    {
        if (TriedWithNil().Match(message) is not { Success: true } tried) return null;

        var name = tried.Groups["name"].Value;
        var doing = tried.Groups["what"].Value switch
        {
            "perform arithmetic on" => "does arithmetic with {0}",
            "concatenate" => "joins {0} to text with ..",
            "get length of" => "takes the length of {0} with #",
            _ when Regex.Match(masked, $@"(?<![\w.:]){Regex.Escape(name)}\s*(?<use>[.:])\s*(?<member>[A-Za-z_]\w*)\s*(?<call>[(""'{{])?") is { Success: true } used =>
                used.Groups["use"].Value == ":" || used.Groups["call"].Success ? $"calls {used.Groups["member"].Value} on {{0}}" : $"reads {used.Groups["member"].Value} from {{0}}",
            _ => "picks an item out of {0}",
        };
        return (doing, name);
    }

    /// <summary>
    /// How Lua writes what following a nil needs: its functions, what sets a variable, a line printed to standard error - and
    /// that its blocks end with end: a function's runs from its header to the end that closes it.
    /// </summary>
    private sealed partial class LuaDialect : BraceDialect
    {
        /// <summary>function name(...), local function name(...), or a table's function M.name(...) and method M:name(...).</summary>
        [GeneratedRegex(@"^\s*(?:local\s+)?function\s+(?:[A-Za-z_]\w*[.:])*(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)")]
        private static partial Regex FunctionHeader();

        /// <summary>A word that opens a block closed by end or until - if, function, do, repeat - one that closes one, or a while or for whose do opens it.</summary>
        [GeneratedRegex(@"\b(?<word>if|function|do|repeat|end|until)\b")]
        private static partial Regex BlockWord();

        /// <summary>A return and the like, a block's first line, and a line that sets a variable, which the lines after it use.</summary>
        [GeneratedRegex(@"^\s*(?:return|break|goto|if|elseif|else|for|while|repeat|until|function|local|end)\b|^\s*[A-Za-z_][\w\s,]*=(?!=)")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^(?<table>[A-Za-z_]\w*)\[(?<key>.+)\]$")]
        private static partial Regex Indexed();

        [GeneratedRegex(@"^(?:tonumber\(|string\.match\(|string\.find\(|[A-Za-z_]\w*:match\()")]
        private static partial Regex GivesFailWhenNothing();

        [GeneratedRegex(@"^(?!\s*(?:if|elseif|else|for|while|repeat|until|return|function|local\s+function|end)\b)\s*(?:local\s+)?(?<name>[A-Za-z_]\w*)\s*=(?!=)\s*\S.*$|^\s*local\s+(?<name>[A-Za-z_]\w*)\s*$")]
        private static partial Regex SetsAny();

        [GeneratedRegex(@"^\s*if\b")]
        private static partial Regex IfWord();

        [GeneratedRegex(@"^\s*elseif\b")]
        private static partial Regex ElseIfWord();

        [GeneratedRegex(@"[\w)\]}""']$")]
        private static partial Regex StatementEnd();

        [GeneratedRegex(@"\bthen$")]
        private static partial Regex Then();

        [GeneratedRegex(@"(?<![=~<>])=(?!=)\s*function\b")]
        private static partial Regex FunctionGiven();

        public override string ErrorName => "error";

        public override Regex SetsAnyName => SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["nil"];

        public override string SomethingThere => "a value";

        /// <summary>The Lua manual's: a function that ends without a return gives back nothing, which a variable takes as nil.</summary>
        public override string? ReachingTheEndGives => "nil";

        public override bool StatementsEndInSemicolons => false;

        public override Regex NotAStatementToSkip => NotToSkip();

        public override Syntax Syntax => Syntax.Lua;

        public override Regex IfLine => IfWord();

        public override Regex ElseIfLine => ElseIfWord();

        public override bool EndsAStatement(string code) => StatementEnd().IsMatch(code) || Then().IsMatch(code) || code.EndsWith(" do", StringComparison.Ordinal);

        public override bool OpensABlock(string code) => Then().IsMatch(code);

        /// <summary>Straight after the ) that closes the header's parameters - Lua's body has no brace.</summary>
        public override int? BodyStartsAfter(string masked) =>
            FunctionHeader().Match(masked) is { Success: true } header ? header.Index + header.Length : null;

        public override string WrappedReturn(string print, string returned) => "do " + print + " " + returned + " end";

        public override string GuardOpening(string name) => $"if {name} ~= nil then";

        public override string GuardClosing => "end";

        public override string Joining => " .. ";

        public override bool GivesAFunction(string masked) => FunctionGiven().IsMatch(masked);

        /// <summary>
        /// A function that writes a value: text in quotes, and a table as its items and then its keys, in order, to three tables
        /// deep - where Lua's tostring writes table: and an address that changes from run to run. Global, and before the first
        /// line, so it is there whichever line calls it.
        /// </summary>
        public override string? BeforeTheFirstLine =>
            "function fixFinderShow(value, depth) depth = depth or 0; " +
            "if type(value) == \"string\" then return '\"' .. (value:gsub(\"\\n\", \"\\\\n\")) .. '\"' end; " +
            "if type(value) ~= \"table\" then return tostring(value) end; " +
            "if depth > 2 then return \"{...}\" end; " +
            "local parts, count = {}, 0; " +
            "for index, item in ipairs(value) do count = index; parts[#parts + 1] = fixFinderShow(item, depth + 1) end; " +
            "local keys = {}; " +
            "for key in pairs(value) do if not (math.type(key) == \"integer\" and key >= 1 and key <= count) then keys[#keys + 1] = key end end; " +
            "table.sort(keys, function(left, right) return tostring(left) < tostring(right) end); " +
            "for _, key in ipairs(keys) do parts[#parts + 1] = (type(key) == \"string\" and key:match(\"^[%a_][%w_]*$\") and key or \"[\" .. fixFinderShow(key, depth + 1) .. \"]\") .. \" = \" .. fixFinderShow(value[key], depth + 1) end; " +
            "return \"{\" .. table.concat(parts, \", \") .. \"}\" end;";

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (FunctionHeader().Match(masked[index]) is not { Success: true } header) continue;
                if (BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        /// <summary>
        /// The line the block opened on the given line closes on: each if, function, do and repeat opens one, each end and until
        /// closes one - a while's or a for's do is the one that opens it - counted on the blanked lines.
        /// </summary>
        private static int? BlockEnd(IReadOnlyList<string> masked, int start)
        {
            var depth = 0;
            for (var index = start; index < masked.Count; index++)
            {
                foreach (Match word in BlockWord().Matches(masked[index]))
                {
                    depth += word.Groups["word"].Value is "end" or "until" ? -1 : 1;
                    if (depth == 0) return index;
                }
            }

            return null;
        }

        /// <summary>x = ..., local x = ... - with what it gives as "right".</summary>
        public override Regex Sets(string name) =>
            new($@"^\s*(?:local\s+)?{Regex.Escape(name)}\s*=(?!=)\s*(?<right>.*\S)\s*$");

        /// <summary><c>local best</c> gives best nil without saying so.</summary>
        public override string? SetsWithoutSaying(string masked, string name) =>
            Regex.IsMatch(masked, $@"^\s*local\s+{Regex.Escape(name)}\s*$") ? "nil" : null;

        public override string Print(string expression) => $"io.stderr:write({expression} .. \"\\n\")";

        public override string MissingOrValue(string name) => $"({name} == nil and \"nil\" or \"value\")";

        public override string Shown(string type, string name) => $"fixFinderShow({name})";

        /// <summary>
        /// A table indexed with a key, where the file builds the table, and tonumber, string.match and string.find: the Lua
        /// manual says a table gives back nil for a key it does not have, and those give back fail - nil - when there is nothing.
        /// </summary>
        public override Documented? Library(string right, BraceCode code)
        {
            if (GivesFailWhenNothing().IsMatch(right))
            {
                return new Documented("tonumber, string.match and string.find give back nil when there is no number or nothing matches.",
                    "Decide what the program should do when there is nothing, and {check}.");
            }

            if (Indexed().Match(right) is not { Success: true } indexed) return null;

            var table = Regex.Escape(indexed.Groups["table"].Value);
            if (!code.Masked.Any(line => Regex.IsMatch(line, $@"^\s*(?:local\s+)?{table}\s*=\s*\{{"))) return null;

            var receiver = BraceMissingValue.Code(indexed.Groups["table"].Value);
            return new Documented($"{receiver} is a table, and a table gives back nil for a key it does not have.",
                "Give a value of your own with or - or {check}.");
        }

        /// <summary>Each parameter's name; the ... of a function taking any number is left out, as it is no name.</summary>
        private static List<(string Type, string Name)> Parameters(string parameters) =>
            [.. parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(parameter => Regex.IsMatch(parameter, @"^[A-Za-z_]\w*$"))
                .Select(parameter => ("", parameter))];
    }
}
