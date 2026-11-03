using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>A string left open on a line, for a language whose compiler says so in its own words: the closing quote put where the text ends.</summary>
internal sealed class StringLeftOpen(string id, Func<LocalFixContext, bool> said, LeftOpen.Writing writing) : ILocalFixRule
{
    public string Id => id;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!said(context) || LeftOpen.NearestRepaired(context, writing, LeftOpen.TextClosedBeforeTheEnding) is not (var source, var number, var corrected)) return null;

        return LocalFix.ReplaceLine(Id, "Close the string",
            $"The string on line {number} has no closing quote, so everything after it on the line was read as part of the text. The " +
            "quote goes where the text ends - before the brackets and the ending that belong to the code around it.",
            source.Path, number, corrected);
    }
}

/// <summary>A bracket left open on a line, for a language whose compiler says so in its own words: each closed where it was meant to end.</summary>
internal sealed class BracketLeftOpen(string id, Func<LocalFixContext, bool> said, LeftOpen.Writing writing) : ILocalFixRule
{
    public string Id => id;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!said(context) || LeftOpen.NearestRepaired(context, writing, LeftOpen.BracketsClosed) is not (var source, var number, var corrected)) return null;

        return LocalFix.ReplaceLine(Id, "Close the bracket left open",
            $"Line {number} opens a bracket it never closes, so the compiler read on past where it was meant to end. Each bracket is " +
            "closed where its part ends: before the bracket around it closes, or before the statement's own ending.",
            source.Path, number, corrected);
    }
}

/// <summary>
/// A block whose } is missing, for a language whose compiler says so in its own words: the } goes where the block ends by its
/// indentation, at the indentation of the line that opened it.
/// </summary>
internal sealed class BlockLeftOpen(string id, Func<LocalFixContext, bool> said, LeftOpen.Writing writing) : ILocalFixRule
{
    public string Id => id;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!said(context) || context.Frame is not { } frame || context.Read(frame.File) is not { } source) return null;
        if (!writing.Files.Contains(Path.GetExtension(source.Path).ToLowerInvariant())) return null;

        var masked = CodeText.MaskAll(source.Lines, writing.Syntax);
        if (LeftOpen.NeverClosedBraces(masked) is not [var opened]) return null;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opened + 1}",
            $"The `{{` on line {opened + 1} is never closed. Its `}}` goes where the lines indented under it end.",
            source.Path, LeftOpen.BlockEndsBefore(source.Lines, masked, opened), [CodeText.Indentation(source.Lines[opened]) + "}"]);
    }
}

/// <summary>
/// The left-open rules of the languages whose compilers name a string, bracket or block left open in words of their own - each
/// with the words it was seen to use on this computer when these were written: the C# compiler's CS1010 and CS1026, rustc,
/// PHP, Elixir, Erlang's compiler, Dart's analyzer, F# Interactive and Perl.
/// </summary>
internal static partial class LeftOpenRulesByLanguage
{
    private static readonly LeftOpen.Writing CSharp = new(Files(".cs"), Syntax.CLike, "\"", "//", ";", ClosesAtLineEnd: false);
    private static readonly LeftOpen.Writing Perl = new(Files(".pl", ".pm"), Syntax.Perl, "\"'", "#", ";", ClosesAtLineEnd: false);
    private static readonly LeftOpen.Writing Rust = new(Files(".rs"), Syntax.CLike, "\"", "//", ";", ClosesAtLineEnd: false);
    private static readonly LeftOpen.Writing Php = new(Files(".php"), Syntax.CLike, "\"'", "//", ";", ClosesAtLineEnd: false);
    private static readonly LeftOpen.Writing Elixir = new(Files(".exs", ".ex"), Syntax.Python, "\"", "#", "", ClosesAtLineEnd: true);
    private static readonly LeftOpen.Writing Erlang = new(Files(".erl"), Syntax.Erlang, "\"", "%", ".,;", ClosesAtLineEnd: false);
    private static readonly LeftOpen.Writing Dart = new(Files(".dart"), Syntax.CLike, "'\"", "//", ";", ClosesAtLineEnd: false);
    private static readonly LeftOpen.Writing FSharp = new(Files(".fsx", ".fs"), Syntax.FSharp, "\"", "//", "", ClosesAtLineEnd: true);

    private static IReadOnlySet<string> Files(params string[] extensions) => new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the error is the language's and its message holds these words.</summary>
    private static bool Says(LocalFixContext context, string language, string words) =>
        context.Error.LanguageId == language && (context.Error.Message ?? "").Contains(words, StringComparison.OrdinalIgnoreCase);

    /// <summary>Erlang's syntax error before a closing bracket: syntax error before: ']'.</summary>
    [GeneratedRegex(@"syntax error before: '[\])}]'", RegexOptions.IgnoreCase)]
    private static partial Regex ErlangBeforeACloser();

    public static IReadOnlyList<ILocalFixRule> All { get; } =
    [
        new StringLeftOpen("csharp-unclosed-string", context => CSharpCode.HasErrorCode(context, "CS1010"), CSharp),
        new BracketLeftOpen("csharp-unclosed-bracket", context => CSharpCode.HasErrorCode(context, "CS1026"), CSharp),

        new StringLeftOpen("rust-unclosed-string", context => Says(context, "rust", "unterminated double quote string"), Rust),
        new BracketLeftOpen("rust-unclosed-bracket", context => Says(context, "rust", "closing delimiter") || Says(context, "rust", "unclosed delimiter"), Rust),
        new BlockLeftOpen("rust-unclosed-block", context => Says(context, "rust", "unclosed delimiter"), Rust),

        new StringLeftOpen("php-unclosed-string", context => Says(context, "php", "expecting variable or \"${\""), Php),
        new BracketLeftOpen("php-unclosed-bracket", context => Says(context, "php", "expecting \")\"") || Says(context, "php", "expecting \"]\""), Php),

        new StringLeftOpen("elixir-unclosed-string", context => Says(context, "elixir", "missing terminator: \""), Elixir),
        new BracketLeftOpen("elixir-unclosed-bracket",
            context => Says(context, "elixir", "missing terminator: )") || Says(context, "elixir", "missing terminator: ]") || Says(context, "elixir", "missing terminator: }"), Elixir),

        new StringLeftOpen("erlang-unclosed-string", context => Says(context, "erlang", "unterminated string"), Erlang),
        new BracketLeftOpen("erlang-unclosed-bracket", context => context.Error.LanguageId == "erlang" && ErlangBeforeACloser().IsMatch(context.Error.Message ?? ""), Erlang),

        new StringLeftOpen("dart-unclosed-string", context => context.Error is { LanguageId: "dart", ErrorCode: "unterminated_string_literal" }, Dart),
        new BracketLeftOpen("dart-unclosed-bracket",
            context => context.Error is { LanguageId: "dart", ErrorCode: "expected_token" } && (Says(context, "dart", "find ')'") || Says(context, "dart", "find ']'")), Dart),
        new BlockLeftOpen("dart-unclosed-block", context => context.Error is { LanguageId: "dart", ErrorCode: "expected_token" } && Says(context, "dart", "find '}'"), Dart),

        new StringLeftOpen("fsharp-unclosed-string", context => context.Error is { LanguageId: "fsharp", ErrorCode: "FS0514" }, FSharp),

        // Perl says only "syntax error", so the rule acts only on a line whose own brackets do not balance.
        new BracketLeftOpen("perl-unclosed-bracket", context => Says(context, "perl", "syntax error"), Perl),
    ];
}
