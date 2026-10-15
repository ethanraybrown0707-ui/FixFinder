using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// The change rustc itself suggests with an error or a warning, made as rustc describes it. rustc marks each suggestion
/// with how sure it is: one it is sure of is made as it stands; one it is not sure of is made only when it is a name it
/// thinks was meant, the use line for the one item it found, or the "{}" a println! is missing. One with a blank in it -
/// todo!(), /* i32 */ - is never made, and nor is one that only quietens a warning: an _ in front of an unused name, or
/// let _ = in front of a value thrown away. Every change is compiled in a copy before it is offered, as every rule's is.
/// </summary>
public sealed partial class RustCompilerSuggestion : ILocalFixRule
{
    public string Id => "rust-compiler-suggestion";

    /// <summary>What rustc says of a change it is not sure of, when the change is one worth making: a name, an import, a "{}".</summary>
    [GeneratedRegex(@"similar name|^consider importing this (?:struct|enum|function|trait|type alias|module|macro|constant|unit struct)\b|you might be missing a string literal")]
    private static partial Regex WorthMakingThoughUnsure();

    /// <summary>What rustc says of a change that only quietens a warning rather than correcting anything.</summary>
    [GeneratedRegex(@"^if this is intentional|let _ = \.\.\.|^remove the whole")]
    private static partial Regex OnlyQuietens();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.LanguageId != "rust" || context.Error.Suggestions.Count == 0) return null;

        foreach (var suggestion in context.Error.Suggestions)
        {
            if (!WorthMaking(suggestion)) continue;
            if (Made(context, suggestion) is { } fix) return fix;
        }

        return null;
    }

    private static bool WorthMaking(SuggestedChange suggestion) => suggestion.Certainty switch
    {
        SuggestionCertainty.Certain => !OnlyQuietens().IsMatch(suggestion.Said),
        SuggestionCertainty.Possible => WorthMakingThoughUnsure().IsMatch(suggestion.Said),
        _ => false,
    };

    /// <summary>The suggestion's edits made to the lines they are on, as one change - or null when they are not all in one file here.</summary>
    private LocalFix? Made(LocalFixContext context, SuggestedChange suggestion)
    {
        var files = suggestion.Edits.Select(edit => edit.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count != 1 || SourceOf(context, files[0]) is not { } source || !RustProgram.IsRust(source.Path)) return null;

        var first = suggestion.Edits.Min(edit => edit.LineStart);
        var last = suggestion.Edits.Max(edit => edit.LineEnd);
        if (first < 1 || last > source.Lines.Count) return null;

        var original = string.Join("\n", source.Lines.Skip(first - 1).Take(last - first + 1));
        var changed = new StringBuilder(original);

        // From the last edit to the first, so the places of those still to make do not move.
        foreach (var edit in suggestion.Edits.OrderByDescending(edit => edit.LineStart).ThenByDescending(edit => edit.ColumnStart))
        {
            if (OffsetOf(source, first, edit.LineStart, edit.ColumnStart) is not { } start ||
                OffsetOf(source, first, edit.LineEnd, edit.ColumnEnd) is not { } end || end < start)
                return null;

            changed.Remove(start, end - start).Insert(start, edit.Replacement);
        }

        var newLines = changed.ToString().Split('\n');
        if (string.Join("\n", newLines) == original) return null;

        var said = suggestion.Said.Length == 0 ? "make this change" : suggestion.Said;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Do as rustc suggests: {said}",
            Explanation = suggestion.Certainty == SuggestionCertainty.Certain
                ? $"rustc itself suggests this change, and marks it as right as it stands: {said}."
                : $"rustc itself suggests this change - {said} - and FixFinder compiled it in a copy to see that it builds.",
            File = source.Path,
            StartLine = first,
            RemoveCount = last - first + 1,
            NewLines = newLines,
        };
    }

    /// <summary>The file an edit is in: the error's own file when it is that one, as rustc named it from where it ran.</summary>
    private static SourceFile? SourceOf(LocalFixContext context, string named)
    {
        if (context.Frame?.File is { } errorFile &&
            Path.GetFullPath(errorFile).EndsWith(named.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return context.Read(errorFile);

        return context.Read(named);
    }

    /// <summary>
    /// Where a line and a column - both counted from 1, the column in characters as rustc counts them - fall in the lines
    /// joined from <paramref name="first"/>, each followed by \n.
    /// </summary>
    private static int? OffsetOf(SourceFile source, int first, int line, int column)
    {
        if (line < first || line > source.Lines.Count) return null;

        var offset = 0;
        for (var number = first; number < line; number++) offset += source.Lines[number - 1].Length + 1;

        var text = source.Lines[line - 1];
        var characters = new StringInfoWalker(text);
        if (characters.OffsetOfCharacter(column - 1) is not { } within) return null;

        return offset + within;
    }

    /// <summary>
    /// Counts characters as rustc does - Unicode scalar values, so a letter outside the basic plane is one, not the two
    /// UTF-16 units .NET keeps it as.
    /// </summary>
    private readonly struct StringInfoWalker(string text)
    {
        public int? OffsetOfCharacter(int characterIndex)
        {
            var counted = 0;

            for (var index = 0; index <= text.Length; index++)
            {
                if (counted == characterIndex) return index;
                if (index == text.Length) break;
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])) index++;
                counted++;
            }

            return null;
        }
    }
}
