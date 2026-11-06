using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// The root cause of a Perl program stopping on undef - a method called on it, or it used as an array or a hash reference:
/// the variable that held undef, the line that gave it undef, and - when one of the program's own subs gave it with a
/// return - which return gave back undef and why the others never ran.
/// </summary>
/// <remarks>
/// Perl's message names the method - Can't call method "describe" on an undefined value - so what held undef is the variable
/// the line calls it on, $member-&gt;describe(), or the sub whose result it is called on straight away. Perl's die gives no
/// trace of calls, so a sub's parameter the call gave undef is not followed. <see cref="BraceMissingValue"/> then runs a copy
/// of the program that prints where its undef could have come from.
/// </remarks>
public static partial class PerlUndefOrigin
{
    [GeneratedRegex(@"^Can't call method ""(?<method>\w+)"" on an undefined value")]
    private static partial Regex MethodOnUndef();

    [GeneratedRegex(@"^Can't use an undefined value as an? (?<kind>ARRAY|HASH) reference")]
    private static partial Regex UsedAsReference();

    private static readonly PerlDialect Perl = new();

    /// <summary>
    /// Runs a copy of the program with the places the undef could have come from marked, and says where it came from - or null
    /// when the line does not show what held it, or the copy does not stop the same way.
    /// </summary>
    public static Task<BraceTraced?> TraceAsync(string program, string file, string? typed, string message, int crashLine, CancellationToken cancellationToken)
    {
        if (SourceFile.Read(file) is not { } source || source.Line(crashLine) is not { } line) return Task.FromResult<BraceTraced?>(null);
        if (Read(message, CodeText.Mask(line, Syntax.Perl)) is not var (doing, name, calledFunction)) return Task.FromResult<BraceTraced?>(null);

        return BraceMissingValue.TraceAsync(Perl, program, source, typed, crashLine, null, doing, "undef", name, calledFunction,
            error => error.LanguageId == "perl" && error.Message == message, cancellationToken);
    }

    /// <summary>The change that makes the line it stopped on run only when the variable is defined - or null for a line no such change suits.</summary>
    public static LocalFix? GuardFor(RootCause cause, SourceFile source, string doing) =>
        BraceMissingValue.GuardFor(Perl, "perl-undef-guard", cause, source, doing);

    /// <summary>
    /// What the line did with undef, and what held it - a variable, or a sub whose result the line called the method on straight
    /// away - read from Perl's message and the line, with its text blanked. Null when the message is not one of these, or what
    /// held undef is not a plain variable or call.
    /// </summary>
    public static (string Doing, string? Name, string? CalledFunction)? Read(string message, string masked)
    {
        if (MethodOnUndef().Match(message) is { Success: true } method)
        {
            var called = Regex.Escape(method.Groups["method"].Value);
            var doing = $"calls {method.Groups["method"].Value} on {{0}}";
            if (Regex.Match(masked, $@"(?<name>\$[A-Za-z_]\w*)\s*->\s*{called}\b") is { Success: true } held)
                return (doing, held.Groups["name"].Value, null);
            if (Regex.Match(masked, $@"(?<![\w$>:&])(?<function>[A-Za-z_]\w*)\s*\((?:[^()]|\([^()]*\))*\)\s*->\s*{called}\b") is { Success: true } call)
                return (doing, null, call.Groups["function"].Value);
            return null;
        }

        if (UsedAsReference().Match(message) is not { Success: true } reference) return null;

        var array = reference.Groups["kind"].Value == "ARRAY";
        var used = array
            ? Regex.Match(masked, @"@\{?\s*(?<name>\$[A-Za-z_]\w*)\s*\}?|(?<name>\$[A-Za-z_]\w*)\s*->\s*\[")
            : Regex.Match(masked, @"%\{?\s*(?<name>\$[A-Za-z_]\w*)\s*\}?|(?<name>\$[A-Za-z_]\w*)\s*->\s*\{");
        return used.Success ? (array ? "uses {0} as a list" : "uses {0} as a hash", used.Groups["name"].Value, null) : null;
    }

    /// <summary>How Perl writes what following undef needs: its subs, what sets a variable, and a line printed to standard error.</summary>
    private sealed partial class PerlDialect : BraceDialect
    {
        [GeneratedRegex(@"^\s*sub\s+(?<name>[A-Za-z_]\w*)\s*\{?\s*$")]
        private static partial Regex SubHeader();

        /// <summary>my ($members, $id) = @_; - the parameters a sub copies out of @_ on its first line.</summary>
        [GeneratedRegex(@"^\s*my\s*\((?<names>[^)]*)\)\s*=\s*@_\s*;")]
        private static partial Regex CopiedFromArguments();

        /// <summary>my $name = shift; - one parameter taken off @_.</summary>
        [GeneratedRegex(@"^\s*my\s+(?<name>\$[A-Za-z_]\w*)\s*=\s*shift\b")]
        private static partial Regex Shifted();

        [GeneratedRegex(@"^\s*(?:return|last|next|redo|if|elsif|else|unless|for|foreach|while|until|sub|package|use|my|our|local)\b|^\s*[$@%][A-Za-z_]\w*\s*(?:[-+*/%.x]|//|\|\|)?=(?![=~>])")]
        private static partial Regex NotToSkip();

        [GeneratedRegex(@"^\$(?<hash>[A-Za-z_]\w*)\{(?<key>[^{}]*)\}$")]
        private static partial Regex HashLookUp();

        [GeneratedRegex(@"^(?!\s*(?:if|elsif|else|unless|for|foreach|while|until|return|sub|package|use)\b)\s*(?:(?:my|our|local)\s+)?(?<name>[$@%][A-Za-z_]\w*)\s*(?:[-+*/%.x]|//|\|\|)?=(?![=~>])[^;]*;\s*$|^\s*my\s+(?<name>\$[A-Za-z_]\w*)\s*;\s*$")]
        private static partial Regex SetsAny();

        [GeneratedRegex(@"^\s*\}?\s*elsif\s*\(")]
        private static partial Regex ElsIf();

        [GeneratedRegex(@"(?<![=!<>])=(?![=~>])\s*sub\s*\{")]
        private static partial Regex FunctionGiven();

        public override string ErrorName => "error";

        public override Regex SetsAnyName => SetsAny();

        public override IReadOnlyList<string> MissingWords { get; } = ["undef"];

        public override string SomethingThere => "a value";

        /// <summary>None to say: a Perl sub that reaches its end gives back what its last statement gave.</summary>
        public override string? ReachingTheEndGives => null;

        public override bool StatementsEndInSemicolons => true;

        public override Regex NotAStatementToSkip => NotToSkip();

        public override Syntax Syntax => Syntax.Perl;

        public override Regex ElseIfLine => ElsIf();

        public override string GuardOpening(string name) => $"if (defined {name}) {{";

        /// <summary>In single quotes, where Perl puts no variable in.</summary>
        public override string Quoted(string text) => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

        public override string Joining => " . ";

        public override bool GivesAFunction(string masked) => FunctionGiven().IsMatch(masked);

        /// <summary>$_[0], $_[1] - what the sub's first line copies its parameters from, as the sub begins.</summary>
        public override string ParameterValue(int index, string name) => $"$_[{index}]";

        /// <summary>A sub, with the parameters its first line copies out of @_ - my ($a, $b) = @_; or my $a = shift; - if it has one.</summary>
        public override IEnumerable<BraceFunction> Functions(IReadOnlyList<string> masked)
        {
            for (var index = 0; index < masked.Count; index++)
            {
                if (SubHeader().Match(masked[index]) is not { Success: true } header) continue;
                if (Brackets.BlockEnd(masked, index) is not { } end) continue;
                yield return new BraceFunction(header.Groups["name"].Value, index + 1, end + 1, Parameters(masked, index, end));
            }
        }

        /// <summary>The names the sub's first lines copy out of @_, in the order they are copied.</summary>
        private static List<(string Type, string Name)> Parameters(IReadOnlyList<string> masked, int header, int end)
        {
            foreach (var index in Enumerable.Range(header + 1, Math.Max(0, end - header - 1)))
            {
                var text = masked[index];
                if (text.Trim() is "" or "{") continue;

                if (CopiedFromArguments().Match(text) is { Success: true } copied)
                {
                    return [.. copied.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(name => Regex.IsMatch(name, @"^\$[A-Za-z_]\w*$"))
                        .Select(name => ("", name))];
                }

                var shifted = new List<(string Type, string Name)>();
                for (var line = index; line < end && Shifted().Match(masked[line]) is { Success: true } one; line++)
                    shifted.Add(("", one.Groups["name"].Value));
                return shifted;
            }

            return [];
        }

        public override Regex Sets(string name) =>
            new($@"^\s*(?:(?:my|our|local)\s+)?{Regex.Escape(name)}\s*=(?![=~>])\s*(?<right>[^;]*[^;\s])\s*;\s*$");

        /// <summary><c>my $best;</c> gives $best undef without saying so.</summary>
        public override string? SetsWithoutSaying(string masked, string name) =>
            Regex.IsMatch(masked, $@"^\s*my\s+{Regex.Escape(name)}\s*;\s*$") ? "undef" : null;

        public override string Print(string expression) => $"print STDERR {expression} . \"\\n\";";

        public override string MissingOrValue(string name) => $"(defined({name}) ? 'value' : 'undef')";

        /// <summary>The value as Data::Dumper writes it, on one line - an array or a hash by a reference to it - with a hash's keys in order.</summary>
        public override string Shown(string type, string name) =>
            "do { require Data::Dumper; local $Data::Dumper::Indent = 0; local $Data::Dumper::Terse = 1; local $Data::Dumper::Sortkeys = 1; " +
            $"Data::Dumper::Dumper({(name.StartsWith('@') || name.StartsWith('%') ? "\\" + name : name)}) }}";

        /// <summary>A hash looked up by a key, where the file fills the hash: perldata says a hash gives back undef for a key it does not have.</summary>
        public override Documented? Library(string right, BraceCode code)
        {
            if (HashLookUp().Match(right) is not { Success: true } looked) return null;

            var hash = Regex.Escape(looked.Groups["hash"].Value);
            if (!code.Masked.Any(line => Regex.IsMatch(line, $@"^\s*(?:my\s+)?%{hash}\s*=\s*\("))) return null;

            return new Documented($"{BraceMissingValue.Code("%" + looked.Groups["hash"].Value)} is a hash, and a hash gives back undef for a key it does not have.",
                "Ask exists first, or give a value of your own with // - or {check}.");
        }
    }
}
