using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A variable declared again inside a block, from the value of the one outside it - local total = total + mark in Lua, my $total =
/// $total + $mark in Perl. Each declares a new variable for the block alone, worked out from the outer one, which never changes:
/// with marks of 70, 80 and 90, Lua 5.4.6 and Strawberry Perl 5.42.2 both printed Total: 0, and luac -p and perl -wc said nothing.
/// </summary>
public static partial class HiddenOuterVariablePatterns
{
    /// <summary>
    /// How one language declares a variable and reads it: the declaration, with the keyword - and the space after it - that the
    /// change takes away, the name and the value; and how a line reads the name.
    /// </summary>
    private sealed record Declaring(string Id, IReadOnlySet<string> Extensions, Syntax Syntax, string Language, string Keyword,
        Regex Declaration, Func<string, Regex> Reads, bool ReadsInsideText);

    private static readonly Declaring[] Languages =
    [
        new("logic-lua-local-hides-outer", CodePattern.Files(".lua"), Syntax.Lua, "Lua", "local",
            new Regex(@"^\s*(?<keyword>local\s+)(?<name>[A-Za-z_]\w*)\s*=(?!=)\s*(?<value>.+)$"),
            name => new Regex($@"(?<![\w.:]){Regex.Escape(name)}\b"), ReadsInsideText: false),
        // Perl puts a variable's value into text in double quotes - "Total: $total" reads $total - so the lines are read as written.
        new("logic-perl-my-hides-outer", CodePattern.Files(".pl", ".pm"), Syntax.Perl, "Perl", "my",
            new Regex(@"^\s*(?<keyword>my\s+)\$(?<name>[A-Za-z_]\w*)\s*=(?!=)\s*(?<value>.+)$"),
            name => new Regex($@"\${Regex.Escape(name)}\b(?![\[{{])"), ReadsInsideText: true),
    ];

    public static IReadOnlyList<ILogicPattern> All { get; } =
        Languages.Select(language => (ILogicPattern)new CodePattern(language.Id, language.Extensions, language.Syntax,
            (id, source, masked) => HiddenOuter(id, source, masked, language), Severity.Error)).ToList();

    private static int Indent(string line) => CodeText.Indentation(line).Length;

    /// <summary>
    /// A declaration whose value reads its own name, inside a block - indented more deeply than an earlier declaration of that
    /// name - where the new variable is not read again before the block ends, and the outer one is read after it. The change
    /// takes the keyword away, so the line gives the outer variable its new value.
    /// </summary>
    private static IEnumerable<LogicFinding> HiddenOuter(string id, SourceFile source, IReadOnlyList<string> masked, Declaring language)
    {
        var lines = language.ReadsInsideText ? source.Lines : masked;

        for (var index = 0; index < masked.Count; index++)
        {
            if (language.Declaration.Match(masked[index]) is not { Success: true } declaration) continue;

            var name = declaration.Groups["name"].Value;
            var reads = language.Reads(name);
            if (!reads.IsMatch(declaration.Groups["value"].Value)) continue;

            var depth = Indent(source.Lines[index]);
            var outerDeclared = Enumerable.Range(0, index).Any(earlier =>
                language.Declaration.Match(masked[earlier]) is { Success: true } outer && outer.Groups["name"].Value == name &&
                Indent(source.Lines[earlier]) < depth);
            if (!outerDeclared) continue;

            // The block ends at the first line after the declaration indented less deeply than it.
            var blockEnd = Enumerable.Range(index + 1, masked.Count - index - 1)
                .FirstOrDefault(later => masked[later].Trim().Length > 0 && Indent(source.Lines[later]) < depth, masked.Count);
            var readInBlock = Enumerable.Range(index + 1, blockEnd - index - 1).Any(inside => reads.IsMatch(lines[inside]));
            var readAfter = Enumerable.Range(blockEnd, masked.Count - blockEnd).Any(after => reads.IsMatch(lines[after]));
            if (readInBlock || !readAfter) continue;

            var line = source.Lines[index];
            var keyword = declaration.Groups["keyword"];
            var written = language.Language == "Perl" ? "$" + name : name;

            yield return new LogicFinding(id, index + 1,
                $"{language.Keyword} declares a new {written} for this block alone, worked out from the {written} outside it - so the outer " +
                $"{written} never changes, and after the block it still holds the value it had before",
                LocalFix.ReplaceLine(id, $"Change the outer {written}: take away {language.Keyword}",
                    $"Without {language.Keyword}, the line gives the {written} declared outside the block its new value, and the block's " +
                    "changes are still there after it.",
                    source.Path, index + 1, line[..keyword.Index] + line[(keyword.Index + keyword.Length)..]));
        }
    }
}
