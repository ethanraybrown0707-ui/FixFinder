using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A prime search keeping a number when some prime found so far does not divide it - any(n % p &gt; 0 for p in primes) - though
/// a number is prime when no prime below it divides it: every one must leave a remainder. With no primes yet, any of none
/// is false, so 2 is never kept and the list stays empty; started with 2, it kept every number from 3 to 10, 4 because 3
/// does not divide it. QuixBugs' sieve is this mistake, in Python and in Java - the Java one through its own any and all.
/// The change asks all, as QuixBugs' correct programs do.
/// </summary>
/// <remarks>
/// Said only when the file shows all of it: a language's any - any, some, anyMatch, Any, any?, exists, any_of, array_any,
/// List.exists, Enum.any?, lists:any - asks of a list whether one of its items does not divide a number - n % p &gt; 0,
/// n % p != 0, rem(n, p) != 0, mod(n, primes(1:found)) /= 0, N rem P =/= 0 - and what it guards adds that number to that
/// same list. Not read for C, Go, Lua or PowerShell, which have no any of their own.
/// </remarks>
public static partial class AnyInsteadOfAllPatterns
{
    /// <summary>How one language asks any, and what asks all in its place.</summary>
    /// <param name="Any">The word asking any, where it is written - the word itself in the group named word.</param>
    /// <param name="All">The word asking all, written in the word's place.</param>
    /// <param name="OwnFunctions">Whether a bare any(...) is the file's own function, as QuixBugs' Java has it, and all must be the file's too.</param>
    private sealed record Dialect(string Language, IReadOnlySet<string> Files, Syntax Syntax, Regex Any, string All, bool OwnFunctions = false, bool IgnoreCase = false);

    private static readonly Dialect[] Dialects =
    [
        new("python", CodePattern.Files(".py", ".pyw"), Syntax.Python, new(@"(?<![\w.])(?<word>any)(?=\s*\()"), "all"),
        new("java", CodePattern.Files(".java"), Syntax.CLike, new(@"\.\s*(?<word>anyMatch)(?=\s*\()|(?<![\w.])(?<word>any)(?=\s*\()"), "allMatch", OwnFunctions: true),
        new("csharp", CodePattern.Files(".cs"), Syntax.CLike, new(@"\.\s*(?<word>Any)(?=\s*\()"), "All"),
        new("js", CodePattern.Files(".js", ".mjs", ".cjs", ".ts", ".mts", ".cts"), Syntax.CLike, new(@"\.\s*(?<word>some)(?=\s*\()"), "every"),
        new("cpp", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, new(@"\bstd\s*::\s*(?:ranges\s*::\s*)?(?<word>any_of)(?=\s*\()"), "all_of"),
        new("rust", CodePattern.Files(".rs"), Syntax.CLike, new(@"\.\s*(?<word>any)(?=\s*\()"), "all"),
        new("ruby", CodePattern.Files(".rb"), Syntax.Python, new(@"\.\s*(?<word>any\?)"), "all?"),
        new("php", CodePattern.Files(".php"), Syntax.CLike, new(@"(?<![\w$>])(?<word>array_any)(?=\s*\()"), "array_all"),
        new("perl", CodePattern.Files(".pl", ".pm"), Syntax.Perl, new(@"(?<![\w$@%>])(?:List::Util::)?(?<word>any)(?=\s*\{)"), "all"),
        new("dart", CodePattern.Files(".dart"), Syntax.CLike, new(@"\.\s*(?<word>any)(?=\s*\()"), "every"),
        new("scala", CodePattern.Files(".scala", ".sc"), Syntax.Scala, new(@"\.\s*(?<word>exists)(?=\s*[({])"), "forall"),
        new("fortran", CodePattern.Files(".f90", ".f95", ".f03", ".f08", ".f"), Syntax.Fortran, new(@"(?<![\w%])(?<word>any)(?=\s*\()", RegexOptions.IgnoreCase), "all", IgnoreCase: true),
        new("fsharp", CodePattern.Files(".fs", ".fsx"), Syntax.FSharp, new(@"\b(?:List|Seq|Array)\.(?<word>exists)\b"), "forall"),
        new("ocaml", CodePattern.Files(".ml"), Syntax.OCaml, new(@"\b(?:List|Array|Seq)\.(?<word>exists)\b"), "for_all"),
        new("elixir", CodePattern.Files(".ex", ".exs"), Syntax.Python, new(@"\bEnum\.(?<word>any\?)"), "all?"),
        new("erlang", CodePattern.Files(".erl"), Syntax.Erlang, new(@"\blists:(?<word>any)(?=\s*\()"), "all"),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        .. Dialects.Select(dialect => new CodePattern($"logic-{dialect.Language}-any-instead-of-all", dialect.Files, dialect.Syntax,
            (id, source, masked) => AnyInsteadOfAll(id, source, masked, dialect), Severity.Error, Confidence.Likely)),
    ];

    /// <summary>
    /// A number an item does not divide: n % p &gt; 0, n % p != 0, n mod p &lt;&gt; 0, N rem P =/= 0, $n % $_ != 0 - the number in
    /// the group named number.
    /// </summary>
    [GeneratedRegex(@"(?<![\w$@.])(?<number>\$?[A-Za-z_]\w*)\s*(?:%|\bmod\b|\brem\b)\s*\$?[A-Za-z_]\w*\s*(?:>\s*0|!==?\s*0|<>\s*0|=/=\s*0|/=\s*0|\.ne\.\s*0|\.gt\.\s*0)(?![\w.])", RegexOptions.IgnoreCase)]
    private static partial Regex Remainder();

    /// <summary>The same as a call: rem(n, p) != 0, mod(n, primes(1:found)) /= 0.</summary>
    [GeneratedRegex(@"(?<![\w.:])(?:rem|mod)\s*\(\s*(?<number>[A-Za-z_]\w*)\s*,[^()]*(?:\([^()]*\)[^()]*)*\)\s*(?:>|!=|/=|\.ne\.|\.gt\.)\s*0(?![\w.])", RegexOptions.IgnoreCase)]
    private static partial Regex RemainderCalled();

    /// <summary>A remainder taken as true when it is not 0: any(n % p for p in primes), some((p) =&gt; n % p).</summary>
    [GeneratedRegex(@"(?:\(|=>)\s*(?<number>[A-Za-z_]\w*)\s*%\s*[A-Za-z_]\w*\s*(?:\bfor\b|\))")]
    private static partial Regex RemainderAsTrue();

    /// <summary>A function the file writes with this name: boolean all(..., def all(..., function all(.</summary>
    private static bool Writes(IReadOnlyList<string> masked, string name) =>
        masked.Any(line => Regex.IsMatch(line, $@"^\s*(?:(?:public|private|protected|static|final)\s+)*(?:[\w<>\[\],]+\s+)?{name}\s*\([^)]*\)\s*\{{?\s*$"));

    private static IEnumerable<LogicFinding> AnyInsteadOfAll(string id, SourceFile source, IReadOnlyList<string> masked, Dialect dialect)
    {
        var options = dialect.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        for (var line = 0; line < masked.Count; line++)
        {
            foreach (Match asked in dialect.Any.Matches(masked[line]))
            {
                var word = asked.Groups["word"];
                var ownFunction = dialect.OwnFunctions && word.Value == "any";
                if (ownFunction && !(Writes(masked, "any") && Writes(masked, "all"))) continue;

                var (window, after) = Asked(masked, line, word.Index);
                if (Divided(window, masked, ownFunction) is not { } number) continue;
                if (AddedTo(masked, line, word.Index, window, after, number, options) is not { } list) continue;

                var text = source.Lines[line];
                var all = ownFunction ? "all" : AllAsWritten(dialect, word.Value, source.Lines, text[..word.Index]);
                var changed = text[..word.Index] + all + text[(word.Index + word.Length)..];
                var told = $"Line {line + 1} keeps `{number}` in `{list}` when one item of `{list}` does not divide it - `{word.Value}` asks that - but " +
                           "a number is prime when none of the primes below it divides it: every one must leave a remainder. With nothing in " +
                           $"`{list}` yet, `{word.Value}` is false, so the first prime is never kept and `{list}` stays empty; started with a prime, " +
                           "it keeps numbers one of them does not divide, which are not all prime.";
                yield return new LogicFinding(id, line + 1, told,
                    LocalFix.ReplaceLine(id, $"Ask that every one leaves a remainder: {all}",
                        $"`{all}` is true when no item divides `{number}` - and with nothing in `{list}` yet it is true, so the first prime is kept.",
                        source.Path, line + 1, changed));
                break;
            }
        }
    }

    /// <summary>
    /// What is asked: the line from the word on, with the lines after it while its brackets are open - and those lines'
    /// count, so what it guards is read from after them.
    /// </summary>
    private static (string Window, int After) Asked(IReadOnlyList<string> masked, int line, int from)
    {
        var window = masked[line][from..];
        var depth = Depth(window);
        var at = line;
        while (depth > 0 && at + 1 < masked.Count && at - line < 6)
        {
            at++;
            window += "\n" + masked[at];
            depth = Depth(window);
        }

        return (window, at + 1);
    }

    private static int Depth(string text) => text.Count(c => c is '(' or '[' or '{') - text.Count(c => c is ')' or ']' or '}');

    /// <summary>
    /// The number the items are asked not to divide, in what is asked - or, for the file's own any, in the function whose
    /// list it is given: any(list_comp(n, primes)), where list_comp adds n % p &gt; 0 for each p.
    /// </summary>
    private static string? Divided(string window, IReadOnlyList<string> masked, bool ownFunction)
    {
        if (Remainder().Match(window) is { Success: true } remainder) return remainder.Groups["number"].Value;
        if (RemainderCalled().Match(window) is { Success: true } called) return called.Groups["number"].Value;
        if (!ownFunction && RemainderAsTrue().Match(window) is { Success: true } asTrue) return asTrue.Groups["number"].Value;
        if (!ownFunction) return null;

        // any(list_comp(n, primes)): list_comp's parameter the remainder is of, and the argument given in its place.
        if (Regex.Match(window, @"^any\s*\(\s*(?<made>[A-Za-z_]\w*)\s*\((?<arguments>[^()]*)\)\s*\)") is not { Success: true } given) return null;
        var arguments = given.Groups["arguments"].Value.Split(',').Select(argument => argument.Trim()).ToList();
        for (var line = 0; line < masked.Count; line++)
        {
            var header = Regex.Match(masked[line], $@"\b{Regex.Escape(given.Groups["made"].Value)}\s*\((?<parameters>[^)]*)\)\s*\{{?\s*$");
            if (!header.Success) continue;
            var parameters = header.Groups["parameters"].Value.Split(',').Select(parameter => parameter.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "").ToList();
            var indent = CodeText.Indentation(masked[line]).Length;
            for (var below = line + 1; below < masked.Count && (masked[below].Trim().Length == 0 || CodeText.Indentation(masked[below]).Length > indent); below++)
            {
                if (Remainder().Match(masked[below]) is not { Success: true } made) continue;
                var at = parameters.IndexOf(made.Groups["number"].Value);
                if (at >= 0 && at < arguments.Count && Regex.IsMatch(arguments[at], @"^[A-Za-z_]\w*$")) return arguments[at];
            }
        }

        return null;
    }

    /// <summary>
    /// The list the number is added to, where what is asked guards the adding - after it on its line, before it on its line
    /// for a test written after what it guards, or on the lines of its block - and the list is named in what is asked, or
    /// is what it is asked of: primes.some(...). Null when nothing adds the number to a list named there.
    /// </summary>
    private static string? AddedTo(IReadOnlyList<string> masked, int line, int from, string window, int after, string number, RegexOptions options)
    {
        var numberNamed = new Regex($@"(?<![\w$@.])[$@]?{Regex.Escape(number.TrimStart('$'))}(?!\w)", options);
        var before = masked[line][..from];

        // What is guarded: the rest of the statement after the remainder, the line before the word when the test follows
        // what it guards - push @primes, $n if any { ... } @primes; - and the block below the line.
        var remainderEnd = Math.Max(Remainder().Match(window) is { Success: true } r ? r.Index + r.Length : 0,
                                    RemainderCalled().Match(window) is { Success: true } c ? c.Index + c.Length : 0);
        var guarded = new List<string> { window[remainderEnd..] };
        if (Regex.IsMatch(before, @"\S\s+(?:if|unless)\s+(?:\S.*)?$")) guarded.Add(before);
        var indent = CodeText.Indentation(masked[line]).Length;
        for (var below = after; below < masked.Count && below < after + 4; below++)
        {
            if (masked[below].Trim().Length == 0) continue;
            if (CodeText.Indentation(masked[below]).Length < indent) break;
            guarded.Add(masked[below]);
        }

        // The lists named in what is asked, and the one it is asked of.
        var lists = Regex.Matches(window, @"[$@]?[A-Za-z_]\w*").Select(name => name.Value).ToList();
        if (Regex.Match(before, @"(?<list>[$@]?[A-Za-z_]\w*)\s*(?:\.\s*(?:stream|iter)\s*\(\s*\)\s*)?\.\s*$") is { Success: true } receiver) lists.Add(receiver.Groups["list"].Value);

        foreach (var list in lists.Distinct().Where(list => !numberNamed.IsMatch(list)))
        {
            var listNamed = new Regex($@"(?<![\w$@.])[$@]?{Regex.Escape(list.TrimStart('$', '@'))}(?!\w)", options);
            if (Regex.IsMatch(list, @"^(?:fn|fun|end|for|in|if|then|else|do|return|true|false|std|List|Enum|lists|mod|rem|any|all|Util)$")) continue;
            if (guarded.Any(text => listNamed.IsMatch(text) && numberNamed.IsMatch(text) && Adds(text))) return list.TrimStart('$', '@');
        }

        return null;
    }

    /// <summary>
    /// Whether a line adds to a list: append, add, Add, push, push_back, &lt;&lt;, +=, ++, @, ::, :+, $list[] =, list(i) = - or
    /// a walk on with it put first, walk (n + 1) (n :: primes).
    /// </summary>
    private static bool Adds(string text) =>
        Regex.IsMatch(text, @"\b(?:append|add|Add|push|push_back|insert|unshift)\b|<<|\+=|\+\+|(?<![\w])@(?![\w$])|::|:\+|\[\s*\]\s*=|\)\s*=(?!=)", RegexOptions.IgnoreCase);

    /// <summary>
    /// The word asking all as the code would write it: Fortran's in the any's own case; Perl's with its library's name in
    /// front unless the file brings all in - use List::Util qw(any all), read from the lines as written, since masking
    /// blanks what qw quotes - or the any already has the name in front.
    /// </summary>
    private static string AllAsWritten(Dialect dialect, string written, IReadOnlyList<string> lines, string before)
    {
        if (dialect.Language == "fortran") return written == written.ToUpperInvariant() ? dialect.All.ToUpperInvariant() : dialect.All;
        if (dialect.Language == "perl" && !before.EndsWith("List::Util::", StringComparison.Ordinal) &&
            !lines.Any(line => Regex.IsMatch(line, @"^\s*use\s+List::Util\b[^;]*\ball\b"))) return "List::Util::all";
        return dialect.All;
    }
}
