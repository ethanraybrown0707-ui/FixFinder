using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Functions written twice: two or more of a program's functions whose lines are the same, with only the names of their
/// own parameters and variables - and their own names - different. A mistake put right in one stays in the other.
/// </summary>
/// <remarks>
/// Each function is compared by its own lines as written, read with the comments left out and the text in its strings
/// set aside, then compared as well, in order, from the code's own reading - so two functions that print different
/// messages differ. Its own names are set aside in the order it first uses them; a name after a dot, a keyword argument
/// and everything else stay as written, so a function that uses another field, calls another function or is given
/// another number is not the same. A copy in a file of the same name in another folder is another copy of the whole
/// file, not a function the program has twice, and is not counted. Only a function of at least <see cref="FewestStatements"/> statements and
/// <see cref="FewestCharacters"/> characters of code is compared: below that, two functions alike - a getter, a one-line
/// return - say nothing. A function the reader could not read all of is not compared.
/// </remarks>
public static partial class DuplicateFunctions
{
    public const string CheckId = "analysis-duplicate-function";

    public const string FoundBy = "comparing the program's functions line by line, with their own names set aside";

    /// <summary>The fewest statements, nested ones counted, a function needs to be compared.</summary>
    private const int FewestStatements = 3;

    /// <summary>The fewest characters of code, spaces left out, a function needs to be compared.</summary>
    private const int FewestCharacters = 40;

    [GeneratedRegex(@"[A-Za-z_$][\w$]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>One function as it is compared: its lines with its own names set aside, and the text of its strings.</summary>
    private sealed record Written(IrFunction Function, string Shape, int Lines);

    /// <summary>
    /// A finding for each group of functions written twice or more that has one in <paramref name="chosenFile"/>, on the
    /// later of them, naming the first; every group when no file is named.
    /// </summary>
    public static IReadOnlyList<AnalysisFinding> Find(IrProgram program, string? chosenFile)
    {
        var sources = new Dictionary<string, (SourceFile Source, IReadOnlyList<string> Masked)?>(StringComparer.OrdinalIgnoreCase);
        var written = new List<Written>();

        foreach (var function in program.AllFunctions)
        {
            if (function.Name == IrFunction.ModuleBody || function.Span.EndLine <= function.Span.Line) continue;
            if (IrWalk.Statements(function.Body).Count() < FewestStatements) continue;
            if (IrWalk.Statements(function.Body).Any(statement => statement is OpaqueStmt) ||
                IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within).Any(expression => expression is Opaque)) continue;

            if (!sources.TryGetValue(function.Span.File, out var read))
            {
                read = SourceFile.Read(function.Span.File) is { } source
                    ? (source, CodeText.MaskAll(source.Lines, IsPython(source.Path) ? Syntax.Python : Syntax.CLike))
                    : null;
                sources[function.Span.File] = read;
            }

            if (read is not { } file || function.Span.EndLine > file.Masked.Count) continue;
            if (WrittenAs(function, file.Masked, IsPython(file.Source.Path)) is { } shape) written.Add(shape);
        }

        var findings = new List<AnalysisFinding>();
        foreach (var copies in written.GroupBy(each => each.Shape, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var inOrder = copies.OrderBy(each => !SamePath(each.Function.Span.File, chosenFile)).ThenBy(each => each.Function.Span.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(each => each.Function.Span.Line).ToList();
            if (chosenFile is not null && !inOrder.Any(each => SamePath(each.Function.Span.File, chosenFile))) continue;

            // A file of the same name in another folder is another copy of the whole file - a version kept, as a benchmark
            // keeps its correct programs beside its broken ones - and not a function the program has twice.
            var anchor = inOrder[0].Function.Span.File;
            inOrder = [.. inOrder.Where(each => SamePath(each.Function.Span.File, anchor) ||
                                                !Path.GetFileName(each.Function.Span.File).Equals(Path.GetFileName(anchor), StringComparison.OrdinalIgnoreCase))];
            if (inOrder.Count < 2) continue;

            var first = inOrder[0];
            var copy = inOrder.FirstOrDefault(each => !ReferenceEquals(each, first) && (chosenFile is null || SamePath(each.Function.Span.File, chosenFile))) ?? inOrder[1];
            var others = inOrder.Where(each => !ReferenceEquals(each, copy)).ToList();
            findings.Add(FindingFor(copy, others));
        }

        return findings;
    }

    /// <summary>
    /// The function's lines as they are compared - comments out, strings emptied, its own names set aside in order, spaces
    /// made one - then its strings' text in order; null when it is too short to say anything by being alike.
    /// </summary>
    private static Written? WrittenAs(IrFunction function, IReadOnlyList<string> masked, bool python)
    {
        var own = new HashSet<string>(StringComparer.Ordinal) { function.Name };
        own.UnionWith(function.Parameters.Select(parameter => parameter.Name));
        foreach (var statement in IrWalk.Statements(function.Body))
        {
            switch (statement)
            {
                case Declare declared:
                    own.Add(declared.Variable);
                    break;
                case Assign { Target: Name assigned }:
                    own.Add(assigned.Identifier);
                    break;
                case ForEach { Target: var target }:
                    own.UnionWith(IrWalk.Names(target));
                    break;
                case Try caught:
                    own.UnionWith(caught.Handlers.Select(handler => handler.Variable).OfType<string>());
                    break;
            }
        }

        var lines = Enumerable.Range(function.Span.Line - 1, function.Span.EndLine - function.Span.Line + 1).Select(index => masked[index]).ToList();
        var code = string.Join("\n", lines);
        var setAside = new Dictionary<string, string>(StringComparer.Ordinal);
        var shape = new StringBuilder();
        var at = 0;

        foreach (Match word in Word().Matches(code))
        {
            shape.Append(code, at, word.Index - at);
            at = word.Index + word.Length;

            var afterADot = word.Index > 0 && code[word.Index - 1] == '.' ||
                            word.Index > 1 && code.Substring(word.Index - 2, 2) is "->" or "::";
            var keywordArgument = python && KeywordArgument(code, word.Index, word.Length);
            if (!own.Contains(word.Value) || afterADot || keywordArgument)
            {
                shape.Append(word.Value);
                continue;
            }

            if (!setAside.TryGetValue(word.Value, out var placeholder))
            {
                placeholder = $"·{setAside.Count}";
                setAside[word.Value] = placeholder;
            }

            shape.Append(placeholder);
        }

        shape.Append(code, at, code.Length - at);
        var compact = Spaces().Replace(shape.ToString(), " ").Trim();
        if (compact.Count(character => !char.IsWhiteSpace(character)) < FewestCharacters) return null;

        var strings = IrWalk.Statements(function.Body).SelectMany(IrWalk.Expressions).SelectMany(IrWalk.Within)
            .OfType<Literal>().Where(literal => literal.Kind is LiteralKind.Text or LiteralKind.Character).Select(literal => literal.Value?.ToString() ?? "");
        return new Written(function, compact + "\u0001" + string.Join("\u0002", strings), lines.Count);
    }

    /// <summary>Whether the word is a Python keyword argument - total=3 inside a call's brackets - which names the callee's parameter.</summary>
    private static bool KeywordArgument(string code, int index, int length)
    {
        var after = index + length;
        while (after < code.Length && code[after] == ' ') after++;
        if (after >= code.Length || code[after] != '=' || after + 1 < code.Length && code[after + 1] == '=') return false;

        var before = index - 1;
        while (before >= 0 && code[before] is ' ' or '\n') before--;
        return before >= 0 && code[before] is '(' or ',';
    }

    /// <summary>The finding on the copy, naming the function it copies and any others written the same way.</summary>
    private static AnalysisFinding FindingFor(Written copy, IReadOnlyList<Written> others)
    {
        string Where(Written each) => SamePath(each.Function.Span.File, copy.Function.Span.File)
            ? $"`{each.Function.Name}` on line {each.Function.Span.Line}"
            : $"`{each.Function.Name}` on line {each.Function.Span.Line} of {Path.GetFileName(each.Function.Span.File)}";

        var named = others.Count == 1 ? Where(others[0]) : string.Join(", ", others.SkipLast(1).Select(Where)) + " and " + Where(others[^1]);
        var alike = others.Count == 1 ? "is written the same as" : "is written the same as each of";
        var message = $"`{copy.Function.Name}` on line {copy.Function.Span.Line} {alike} {named}: the same {copy.Lines} lines, but for the " +
                      "names of their own parameters and variables and their own names";

        return new AnalysisFinding(CheckId, new SourceSpan(copy.Function.Span.File, copy.Function.Span.Line), message,
            Severity.Suggestion, Confidence.Certain, FindingKind.Logic, FoundBy)
        {
            Function = copy.Function.Name,
        };
    }

    private static bool IsPython(string file) => Path.GetExtension(file).ToLowerInvariant() is ".py" or ".pyw";

    private static bool SamePath(string first, string? second) =>
        second is not null && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
