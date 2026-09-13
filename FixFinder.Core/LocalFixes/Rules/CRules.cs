using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

internal static partial class Msvc
{
    [GeneratedRegex(@"^\s*#\s*include\s*[<""](?<header>[^>""]+)[>""]")]
    public static partial Regex Include();

    public static bool SameFile(LocalFixContext context, ParsedError error, SourceFile source) =>
        context.Resolve((error.CulpritFrame ?? error.Frames.FirstOrDefault())?.File) is { } path &&
        string.Equals(path, source.Path, StringComparison.OrdinalIgnoreCase);

    public static bool IsC(SourceFile source) =>
        Path.GetExtension(source.Path).Equals(".c", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A standard name used without its header: <c>C2065 'bool'</c>, <c>C3861</c>, <c>C2039 'cout' is not
/// a member of 'std'</c>, or the warning <c>C4013 'malloc' undefined</c>.
/// </summary>
public sealed partial class CMissingStandardHeader : ILocalFixRule
{
    public string Id => "c-missing-standard-header";

    [GeneratedRegex(@"^'(?<name>\w+)': (?:undeclared identifier|identifier not found)$")]
    private static partial Regex Undeclared();

    [GeneratedRegex(@"^'(?<name>\w+)' undefined; assuming extern returning int$")]
    private static partial Regex Undefined();

    [GeneratedRegex(@"^'(?<name>\w+)': is not a member of 'std'$")]
    private static partial Regex NotInStd();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "msvc" || Needed(error) is not { } primary) return null;
        if (context.Read(context.Frame?.File) is not { } source) return null;

        var c = Msvc.IsC(source);

        string? HeaderFor((string Name, bool Std) need) =>
            need.Std ? (c ? null : CStandardLibrary.CppHeaderOf(need.Name)) : CStandardLibrary.CHeaderOf(need.Name);

        var included = source.Lines
            .Select(line => Msvc.Include().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups["header"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Already included: something else - a macro, a missing define - is keeping the name hidden.
        if (HeaderFor(primary) is not { } primaryHeader || included.Contains(primaryHeader)) return null;

        var headers = context.AllErrors
            .Concat(MsvcParser.ParseWarnings(context.Output))
            .Where(e => Msvc.SameFile(context, e, source))
            .Select(Needed)
            .OfType<(string Name, bool Std)>()
            .Select(HeaderFor)
            .OfType<string>()
            .Where(header => !included.Contains(header))
            .Append(primaryHeader)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        var lastInclude = -1;
        for (var i = 0; i < source.Count; i++)
            if (Msvc.Include().IsMatch(source.Lines[i])) lastInclude = i;

        var isWarning = error.ExceptionType == "compile warning";
        var name = primary.Name;

        var explanation = isWarning && CStandardLibrary.ReturnsPointer.Contains(name)
            ? $"The compiler warned `'{name}' undefined; assuming extern returning int`, and that warning is the crash. " +
              $"Without <{primaryHeader}>, C assumes {name} returns a 32-bit int, so the 64-bit pointer it really returns " +
              "is cut in half - and the program then uses a broken pointer and is killed without a message."
            : $"`{name}` is declared in <{primaryHeader}>, which this file does not include.";

        return LocalFix.Insert(
            Id,
            headers.Count == 1 ? $"Add #include <{headers[0]}>" : $"Add {headers.Count} missing #includes",
            explanation,
            source.Path,
            lastInclude + 2,
            [.. headers.Select(header => $"#include <{header}>")]) with
        {
            ResolvesWarning = isWarning ? $"'{name}' undefined" : null,
        };
    }

    private static (string Name, bool Std)? Needed(ParsedError error)
    {
        var message = error.Message ?? "";

        return error.ErrorCode switch
        {
            "C2065" or "C3861" when Undeclared().Match(message) is { Success: true } m => (m.Groups["name"].Value, false),
            "C4013" when Undefined().Match(message) is { Success: true } m => (m.Groups["name"].Value, false),
            "C2039" when NotInStd().Match(message) is { Success: true } m => (m.Groups["name"].Value, true),
            _ => null,
        };
    }
}

/// <summary>
/// A misspelt name: an undeclared identifier, a struct member that does not exist, or a function the
/// linker cannot find.
/// </summary>
/// <remarks>
/// MSVC never suggests a name. For a misspelt function in C it does not even report an error at the
/// call - only the warning <c>C4013 'prinft' undefined</c> and then, from the linker,
/// <c>LNK2019: unresolved external symbol prinft</c>. The warning is where the line number is.
/// </remarks>
public sealed partial class CNearestName : ILocalFixRule
{
    public string Id => "c-nearest-name";

    [GeneratedRegex(@"^'(?<name>\w+)': (?:undeclared identifier|identifier not found)$")]
    private static partial Regex Undeclared();

    [GeneratedRegex(@"^'(?<member>\w+)': is not a member of '(?<type>\w+)'$")]
    private static partial Regex NotAMember();

    [GeneratedRegex(@"^unresolved external symbol (?<name>[A-Za-z_]\w*) referenced in function")]
    private static partial Regex Unresolved();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex Called();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;
        if (error.LanguageId != "msvc") return null;

        var message = error.Message ?? "";

        if (error.ErrorCode is "C2065" or "C3861" && Undeclared().Match(message) is { Success: true } undeclared)
        {
            var name = undeclared.Groups["name"].Value;
            if (CStandardLibrary.CHeaderOf(name) is not null) return null;

            if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

            var candidates = CodeText.Identifiers(CodeText.MaskAll(source.Lines, Syntax.CLike))
                .Where(word => !CStandardLibrary.Keywords.Contains(word))
                .Concat(CStandardLibrary.CNames);

            return Rename(source, number, name, candidates, "in this file or the C library");
        }

        if (error.ErrorCode == "C2039" && NotAMember().Match(message) is { Success: true } member &&
            member.Groups["type"].Value != "std")
        {
            if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

            var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
            var type = member.Groups["type"].Value;

            if (MembersOf(masked, type) is not { Count: > 0 } members) return null;

            var wrong = member.Groups["member"].Value;
            if (CodeText.Nearest(wrong, members) is not { } right || source.Line(number) is not { } line) return null;

            var access = new Regex($@"(?:\.|->)\s*(?<name>{Regex.Escape(wrong)})\b");
            var hits = access.Matches(CodeText.Mask(line, Syntax.CLike));
            if (hits.Count != 1) return null;

            var at = hits[0].Groups["name"].Index;

            return LocalFix.ReplaceLine(
                Id, $"Change {wrong} to {right}",
                $"`{type}` has no member `{wrong}`. `{right}` is the only member it does have within a letter or two of it.",
                source.Path, number, line[..at] + right + line[(at + wrong.Length)..]);
        }

        if (error.ErrorCode == "LNK2019" && Unresolved().Match(message) is { Success: true } unresolved)
        {
            var name = unresolved.Groups["name"].Value;

            var warning = MsvcParser.ParseWarnings(context.Output).FirstOrDefault(w =>
                w.ErrorCode == "C4013" && (w.Message ?? "").StartsWith($"'{name}' undefined", StringComparison.Ordinal));

            if (warning?.Frames.FirstOrDefault() is not { Line: { } number } frame || context.Read(frame.File) is not { } source)
                return null;

            var calledHere = CodeText.MaskAll(source.Lines, Syntax.CLike)
                .SelectMany(text => Called().Matches(text).Select(m => m.Groups["name"].Value))
                .Where(word => !CStandardLibrary.Keywords.Contains(word));

            return Rename(source, number, name, calledHere.Concat(CStandardLibrary.CNames), "among this file's functions and the C library");
        }

        return null;
    }

    private LocalFix? Rename(SourceFile source, int number, string wrong, IEnumerable<string> candidates, string where)
    {
        if (CodeText.Nearest(wrong, candidates.Where(c => c != wrong)) is not { } right) return null;
        if (source.Line(number) is not { } line) return null;
        if (CodeText.ReplaceWord(line, wrong, right, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change {wrong} to {right}",
            $"Nothing called `{wrong}` exists, and MSVC does not suggest names. `{right}` is the only name {where} " +
            "within a letter or two of it.",
            source.Path, number, corrected);
    }

    /// <summary>The words declared inside <c>struct type { ... }</c>, or <c>typedef struct { ... } type;</c>.</summary>
    private static List<string>? MembersOf(IReadOnlyList<string> masked, string type)
    {
        var name = Regex.Escape(type);

        for (var i = 0; i < masked.Count; i++)
        {
            if (Regex.IsMatch(masked[i], $@"\b(?:struct|union|class)\s+{name}\b(?!\s*[;*\w])"))
                return BlockWords(masked, i, forward: true);

            if (Regex.IsMatch(masked[i], $@"\}}\s*{name}\s*;"))
                return BlockWords(masked, i, forward: false);
        }

        return null;
    }

    private static List<string>? BlockWords(IReadOnlyList<string> masked, int line, bool forward)
    {
        var text = new List<string>();
        var depth = 0;
        var started = false;

        if (forward)
        {
            for (var i = line; i < masked.Count; i++)
            {
                foreach (var c in masked[i])
                {
                    if (c == '{') { depth++; started = true; continue; }
                    if (c == '}') { depth--; if (started && depth == 0) return Words(text); continue; }
                    if (started) text.Add(c.ToString());
                }

                if (started) text.Add(" ");
            }
        }
        else
        {
            for (var i = line; i >= 0; i--)
            {
                var row = masked[i];

                for (var k = (i == line ? row.IndexOf('}') : row.Length - 1); k >= 0; k--)
                {
                    var c = row[k];
                    if (c == '}') { depth++; started = true; continue; }
                    if (c == '{') { depth--; if (started && depth == 0) return Words(text, reversed: true); continue; }
                    if (started) text.Add(c.ToString());
                }

                if (started) text.Add(" ");
            }
        }

        return null;
    }

    private static List<string> Words(List<string> characters, bool reversed = false)
    {
        if (reversed) characters.Reverse();

        return CodeText.Identifiers([string.Concat(characters)])
            .Where(word => !CStandardLibrary.Keywords.Contains(word))
            .ToList();
    }
}

/// <summary><c>C1083: Cannot open include file: 'stdoi.h'</c>, one letter from a standard header.</summary>
/// <remarks>
/// Only for <c>&lt;...&gt;</c> includes. A quoted include names the project's own header, and the
/// nearest standard header to a missing <c>"time.hpp"</c> is not what anybody meant.
/// </remarks>
public sealed partial class CHeaderTypo : ILocalFixRule
{
    public string Id => "c-header-typo";

    [GeneratedRegex(@"^Cannot open include file: '(?<header>[^']+)': No such file or directory$")]
    private static partial Regex Message();

    /// <summary>gcc: <c>stdoi.h: No such file or directory</c>; clang: <c>'stdoi.h' file not found</c>.</summary>
    [GeneratedRegex(@"^'?(?<header>[^':]+)'?(?: file not found|: No such file or directory)$")]
    private static partial Regex GnuMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        var message = error.LanguageId switch
        {
            "msvc" when error.ErrorCode == "C1083" => Message().Match(error.Message ?? ""),
            "gcc" => GnuMessage().Match(error.Message ?? ""),
            _ => null,
        };

        if (message is not { Success: true }) return null;

        var header = message.Groups["header"].Value;
        if (CStandardLibrary.Headers.Contains(header)) return null;

        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var bracketed = $"<{header}>";
        var at = line.IndexOf(bracketed, StringComparison.Ordinal);
        if (at < 0 || line.IndexOf(bracketed, at + 1, StringComparison.Ordinal) >= 0) return null;

        if (CodeText.Nearest(header, CStandardLibrary.Headers) is not { } right) return null;

        return LocalFix.ReplaceLine(
            Id, $"Change <{header}> to <{right}>",
            $"There is no standard header <{header}>. <{right}> is the only one within a letter or two of it.",
            source.Path, number, line[..(at + 1)] + right + line[(at + 1 + header.Length)..]);
    }
}

/// <summary><c>C2146 / C2143: syntax error: missing ';' before ...</c></summary>
/// <remarks>
/// MSVC reports a missing semicolon on the line where it noticed - the start of the next statement -
/// so the semicolon goes at the end of the code line before it.
/// </remarks>
public sealed partial class CMissingSemicolon : ILocalFixRule
{
    public string Id => "c-missing-semicolon";

    [GeneratedRegex(@"^syntax error: missing ';' before (?:identifier )?'(?<token>[^']+)'$")]
    private static partial Regex Message();

    /// <summary>gcc: <c>expected ',' or ';' before 'printf'</c>, and <c>expected ';' before '}' token</c>.</summary>
    [GeneratedRegex(@"^expected (?:'[^']+' or )*';'(?: or '[^']+')* before '(?<token>[^']+)'(?: token)?$")]
    private static partial Regex GnuMessage();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        var message = error.LanguageId switch
        {
            "msvc" when error.ErrorCode is "C2146" or "C2143" => Message().Match(error.Message ?? ""),
            "gcc" => GnuMessage().Match(error.Message ?? ""),
            _ => null,
        };

        if (message is not { Success: true }) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        if (number < 1 || number > masked.Count) return null;

        var token = message.Groups["token"].Value;
        if (!masked[number - 1].TrimStart().StartsWith(token, StringComparison.Ordinal)) return null;

        for (var k = number - 2; k >= 0; k--)
        {
            var code = masked[k].Trim();
            if (code.Length == 0) continue;

            if (code.StartsWith('#') || code.EndsWith(';') || code.EndsWith('{') || code.EndsWith('}') ||
                code.EndsWith(',') || code.EndsWith('(') || code.EndsWith('\\'))
                return null;

            var (lineCode, tail) = CodeText.SplitComment(source.Lines[k], Syntax.CLike);

            return LocalFix.ReplaceLine(
                Id, "Add the missing semicolon",
                $"The compiler reports the missing semicolon at line {number}, where it noticed - it belongs at the end of the " +
                $"statement on line {k + 1}.",
                source.Path, k + 1, lineCode + ";" + tail);
        }

        return null;
    }
}

/// <summary><c>C1075: '{': no matching token found</c> - the file ends before the block closes.</summary>
public sealed partial class CMissingClosingBrace : ILocalFixRule
{
    public string Id => "c-missing-closing-brace";

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        // MSVC names the brace that was never closed. gcc names the end of the file, where it gave
        // up - so the opener is found by matching braces, which serves both.
        var recognised = error.LanguageId switch
        {
            "msvc" => error.ErrorCode == "C1075" &&
                      (error.Message ?? "").StartsWith("'{': no matching token found", StringComparison.Ordinal),
            "gcc" => error.Message is "expected declaration or statement at end of input" or "expected '}' at end of input",
            _ => false,
        };

        if (!recognised || context.Read(context.Frame?.File) is not { } source) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unclosed = new Stack<int>();
        var stray = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') unclosed.Push(i);
                else if (c == '}' && !unclosed.TryPop(out _)) stray++;
            }
        }

        // Exactly one short. More than that, and where each one goes is a real question.
        if (stray > 0 || unclosed.Count != 1) return null;

        var opener = unclosed.Pop() + 1;
        var last = source.Count - 1;
        while (last > 0 && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(
            Id, $"Close the block opened on line {opener}",
            $"The `{{` on line {opener} is never closed - the file ends first. The closing brace goes at the end; if " +
            "code at the bottom belongs outside the block, move the brace up to where the block really ends.",
            source.Path, last + 2, [CodeText.Indentation(source.Lines[opener - 1]) + "}"]);
    }
}
