using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Ruby program stopping because a method was called on nil - "undefined method `age' for nil": the
/// variable that held nil, the line that gave it nil, and - when one of the program's own methods gave it with a return -
/// which return gave back nil and why the others never ran.
/// </summary>
/// <remarks>
/// Ruby's message names the method, so what held nil is the variable the line calls it on - member.age - or the program's
/// own method whose result it is called on straight away; the line is read as written, since the call may be inside text,
/// "#{member.age}". A Ruby method also gives back its last line without saying return, so a method that ended that way is
/// said only to have given back nil. <see cref="BraceMissingValue"/> then runs a copy of the program that prints where its nil
/// could have come from - Ruby's blocks end with end, which the dialect tells it.
/// </remarks>
public static partial class RubyNilOrigin
{
    /// <summary>Ruby 3.3's undefined method `age' for nil, and 3.4's undefined method 'age' for nil.</summary>
    [GeneratedRegex(@"^undefined method [`'](?<method>[^`']+)' for nil$")]
    private static partial Regex MethodOnNil();

    private static readonly RubyDialect Ruby = new();

    /// <summary>
    /// Runs a copy of the program with the places the nil could have come from marked, and says where it came from - or null
    /// when the line does not show what held it, or the copy does not stop the same way.
    /// </summary>
    /// <param name="calledFrom">The line of the same file the method it stopped in was called from, when Ruby's trace says so.</param>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, string message, int crashLine, int? calledFrom,
        CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is not { } line) return Task.FromResult<BraceTraced?>(null);
        if (Read(message, line) is not var (doing, name, calledFunction)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(Ruby, program, source, typed, crashLine, calledFrom, doing, "nil", name, calledFunction,
            error => error.LanguageId == "ruby" && error.Message == message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable is not nil - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Ruby, "ruby-nil-guard", cause, source, doing);

    /// <summary>
    /// What the line did with nil, and what held it - a local variable, or a method of the program whose result the line
    /// called the method on straight away, as in find_member(members, 2).age - read from Ruby's message and the line as
    /// written. Null when the message is not this one, or the method is not called on a plain variable or call.
    /// </summary>
    public static (string Doing, string? Name, string? CalledFunction)? Read(string message, string line)
    {
        if (MethodOnNil().Match(message) is not { Success: true } method) return null;

        var called = method.Groups["method"].Value;
        if (called == "[]")
        {
            return Regex.Match(line, @"(?<![\w.@$:])(?<name>[a-z_]\w*)\s*\[") is { Success: true } indexed
                ? ("picks an item out of {0}", indexed.Groups["name"].Value, null)
                : null;
        }

        var doing = $"calls {called} on {{0}}";
        var escaped = Regex.Escape(called);
        if (Regex.Match(line, $@"(?<![\w.@$:])(?<name>[a-z_]\w*)\s*\.\s*{escaped}(?![\w?!=])") is { Success: true } held && held.Groups["name"].Value != "self")
            return (doing, held.Groups["name"].Value, null);
        if (Regex.Match(line, $@"(?<![\w.@$:])(?<function>[a-z_]\w*[?!]?)\s*\((?:[^()]|\([^()]*\))*\)\s*\.\s*{escaped}(?![\w?!=])") is { Success: true } call)
            return (doing, null, call.Groups["function"].Value);
        return null;
    }

    /// <summary>
    /// How Ruby writes what following a nil needs: its methods, what sets a variable, a line printed to standard error - and
    /// that its blocks end with end: a method's runs from def to the end that closes it.
    /// </summary>
    private sealed partial class RubyDialect : BraceDialect
    {
        /// <summary>def name(...), def name, or def self.name(...) - not Ruby 3's def name(...) = value, which has no end.</summary>
        [GeneratedRegex(@"^\s*def\s+(?:self\.)?(?<name>[a-z_]\w*[?!]?)\s*(?:\((?<parameters>[^)]*)\))?(?!\s*=)")]
        private static partial Regex MethodHeader();

        /// <summary>A line that opens a block closed by end: one that starts with a word that does, or has a do.</summary>
        [GeneratedRegex(@"^\s*(?:[a-z_]\w*\s*=\s*)?(?<word>if|unless|while|until|case|begin|def|class|module|for)\b")]
        private static partial Regex OpeningWord();

        [GeneratedRegex(@"\bdo\b(?:\s*\|[^|]*\|)?")]
        private static partial Regex Do();

        [GeneratedRegex(@"\bend\b")]
        private static partial Regex End();

        /// <summary>A return and the like, a block's first line, and a line that sets a variable, which the lines after it use.</summary>
        [GeneratedRegex(@"^\s*(?:return|break|next|redo|retry|if|unless|elsif|else|while|until|for|case|when|begin|rescue|ensure|end|def|class|module|yield)\b|^\s*[a-z_]\w*\s*(?:[-+*/%]|\|\||&&)?=(?![=~>])")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^(?<collection>[a-z_]\w*)\.(?:find|detect)\b")]
        private static partial Regex Found();

        [GeneratedRegex(@"^(?<hash>[a-z_]\w*)\[(?<key>.+)\]$")]
        private static partial Regex Indexed();

        [GeneratedRegex(@"^(?:[a-z_]\w*\.match\(|gets\b)")]
        private static partial Regex GivesNilWhenNothing();

        /// <summary>A line that sets a variable and holds nothing else - not one a modifier decides, x = 5 if ready, which may set nothing.</summary>
        [GeneratedRegex(@"^(?!.*\s(?:if|unless|while|until|rescue)\s)(?!\s*(?:if|unless|elsif|else|while|until|for|return|def|class|module|case|when|begin|rescue|ensure|end)\b)\s*(?<name>[a-z_]\w*)\s*(?:[-+*/%]|\|\||&&)?=(?![=~>])\s*\S.*$")]
        private static partial Regex SetsAny();

        [GeneratedRegex(@"^\s*if\b")]
        private static partial Regex IfWord();

        [GeneratedRegex(@"^\s*elsif\b")]
        private static partial Regex ElseIfWord();

        [GeneratedRegex(@"(?:[\w)\]}""'?!]|\bdo(?:\s*\|[^|]*\|)?)$")]
        private static partial Regex StatementEnd();

        [GeneratedRegex(@"(?<![=!<>])=(?![=~>])\s*(?:->|lambda\b|proc\b|Proc\.new\b)")]
        private static partial Regex FunctionGiven();

        /// <summary>The modifier a return ends with - return member if member.id == id - which has to stay outside the begin ... end.</summary>
        [GeneratedRegex(@"^(?<returned>return\b.*?)\s+(?<modifier>(?:if|unless)\s.*)$")]
        private static partial Regex ReturnWithModifier();

        public override string ErrorName => "NoMethodError";

        public override Regex SetsAnyName => SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["nil"];

        public override string SomethingThere => "a value";

        /// <summary>None to say: a Ruby method that reaches its end gives back its last line, whatever that gave.</summary>
        public override string? ReachingTheEndGives => null;

        public override bool StatementsEndInSemicolons => false;

        public override Regex NotAStatementToSkip => NotToSkip();

        /// <summary>Ruby's comments start with #, as Python's do, and its text is in quotes.</summary>
        public override Syntax Syntax => Syntax.Python;

        /// <summary>An if, not an unless, whose body runs when its test is not true - so "was never true" would not be.</summary>
        public override Regex IfLine => IfWord();

        public override Regex ElseIfLine => ElseIfWord();

        public override bool EndsAStatement(string code) => StatementEnd().IsMatch(code) || End().IsMatch(code);

        /// <summary>An if line opens its block unless it closes it too: if ready then go end.</summary>
        public override bool OpensABlock(string code) => !End().IsMatch(code);

        public override int? BodyStartsAfter(string masked) =>
            MethodHeader().Match(masked) is { Success: true } header ? header.Index + header.Length : null;

        /// <summary>After a ;, which a header without brackets - def greet - needs before anything else on its line. The print ends with its own.</summary>
        public override string OpeningStatement(string statement) => "; " + statement;

        public override string WrappedReturn(string print, string returned) =>
            ReturnWithModifier().Match(returned) is { Success: true } modified
                ? $"begin {print} {modified.Groups["returned"].Value} end {modified.Groups["modifier"].Value}"
                : $"begin {print} {returned} end";

        public override string GuardOpening(string name) => $"unless {name}.nil?";

        public override string GuardClosing => "end";

        /// <summary>In single quotes, where Ruby puts nothing in for #{...}.</summary>
        public override string Quoted(string text) => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

        public override bool GivesAFunction(string masked) => FunctionGiven().IsMatch(masked);

        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (MethodHeader().Match(masked[index]) is not { Success: true } header) continue;
                if (BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(header.Groups["parameters"].Value));
            }
        }

        /// <summary>
        /// The line the block opened on the given line closes on: a line starting with if, unless, while, until, case, begin,
        /// def, class, module or for opens one - an if after a statement only decides it - and so does a do, but not the do
        /// of a while, until or for; each end closes one. Counted on the blanked lines.
        /// </summary>
        private static int? BlockEnd(IReadOnlyList<string> masked, int start)
        {
            var depth = 0;
            for (var index = start; index < masked.Count; index++)
            {
                var text = masked[index];
                var opening = OpeningWord().Match(text);
                if (opening.Success) depth++;
                if (!(opening.Success && opening.Groups["word"].Value is "while" or "until" or "for")) depth += Do().Matches(text).Count;
                foreach (Match _ in End().Matches(text))
                {
                    depth--;
                    if (depth == 0) return index;
                }
            }

            return null;
        }

        /// <summary>x = ... - with what it gives as "right" - but not one a modifier decides, x = 5 if ready.</summary>
        public override Regex Sets(string name) =>
            new($@"^\s*{Regex.Escape(name)}\s*=(?![=~>])\s*(?<right>(?:(?!\s(?:if|unless|while|until|rescue)\s).)*\S)\s*$");

        /// <summary>Ended with a ;, so the if it is put before on the same line still starts a block, and does not decide the print.</summary>
        public override string Print(string expression) => $"$stderr.puts({expression});";

        public override string MissingOrValue(string name) => $"({name}.nil? ? 'nil' : 'value')";

        /// <summary>The value as Ruby's inspect writes it - "Dune", [40, 55], #&lt;struct Member id=1, age=30&gt; - without an object's address, which changes from run to run.</summary>
        public override string Shown(string type, string name) => $"({name}).inspect.gsub(/:0x\\h+/, '')";

        /// <summary>
        /// find and detect, a Hash indexed with a key, where the file builds the Hash, a String's match, and gets: Ruby's
        /// documentation says find gives back nil when nothing passes, a Hash nil for a key it does not have, match nil when
        /// nothing matches, and gets nil when there is nothing more to read.
        /// </summary>
        public override Documented? Library(string right, BraceCode code)
        {
            if (Found().Match(right) is { Success: true })
                return new Documented("find gives back nil when no item passes its test.", "Decide what the program should do when no item passes the test, and {check}.");

            if (GivesNilWhenNothing().IsMatch(right))
                return new Documented("match gives back nil when nothing matches, and gets when there is nothing more to read.", "Decide what the program should do when there is nothing, and {check}.");

            if (Indexed().Match(right) is not { Success: true } indexed) return null;

            var hash = Regex.Escape(indexed.Groups["hash"].Value);
            if (!code.Masked.Any(line => Regex.IsMatch(line, $@"^\s*{hash}\s*=\s*(?:\{{|Hash\.new\b)"))) return null;

            var receiver = BraceMissingValue.Code(indexed.Groups["hash"].Value);
            return new Documented($"{receiver} is a Hash, and a Hash gives back nil for a key it does not have.",
                "Ask key? first, or use fetch with a value of its own - or {check}.");
        }

        /// <summary>Each parameter's name: a default, a keyword's : and the * and &amp; of the rest and of a block taken off.</summary>
        private static List<(string Type, string Name)> Parameters(string parameters) =>
            [.. parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(parameter => Regex.Match(parameter, @"^[*&]{0,2}(?<name>[a-z_]\w*)\s*(?::.*|=.*)?$"))
                .Where(match => match.Success)
                .Select(match => ("", match.Groups["name"].Value))];
    }
}
