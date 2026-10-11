namespace FixFinder.Core.Parsing;

/// <summary>How sure a compiler is that a change it suggested is right, in rustc's own words for it.</summary>
public enum SuggestionCertainty
{
    /// <summary>MachineApplicable: the change is right as it stands.</summary>
    Certain,

    /// <summary>MaybeIncorrect: the change may be right, and may not be.</summary>
    Possible,

    /// <summary>HasPlaceholders: the change has a blank in it - todo!(), /* i32 */ - for a person to fill in.</summary>
    HasPlaceholders,

    /// <summary>Unspecified: the compiler did not say.</summary>
    Unknown,
}

/// <summary>One text replacement a compiler suggested: where - lines and columns, both counted from 1 - and what goes there.</summary>
public sealed record SuggestedEdit(string File, int LineStart, int ColumnStart, int LineEnd, int ColumnEnd, string Replacement);

/// <summary>A change a compiler suggested with an error: what it said of it, how sure it is, and the edits it makes.</summary>
public sealed record SuggestedChange(string Said, SuggestionCertainty Certainty, IReadOnlyList<SuggestedEdit> Edits);
