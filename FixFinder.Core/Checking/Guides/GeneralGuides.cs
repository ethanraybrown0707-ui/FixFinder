namespace FixFinder.Core.Checking.Guides;

/// <summary>The guide used when nothing more specific is known about a mistake: one per kind, worded for the language.</summary>
internal static class GeneralGuides
{
    public static MistakeGuide For(string language, FindingKind kind) => kind switch
    {
        FindingKind.Syntax => new MistakeGuide(
            $"{language} could not make sense of the code here, so it stopped before running anything.",
            "Until this is fixed nothing in the program runs at all - not even the lines before it.",
            "Read the message from the line it names, and look at the end of the line before it too: a missing bracket, quote or " +
            "colon is often reported one line late.",
            ""),

        FindingKind.Runtime => new MistakeGuide(
            $"The program started, and then {language} stopped it with an error at this line.",
            "Everything after this point is skipped, and anyone running the program sees it crash instead of the result.",
            "Work out which value on the line was not what the code expected, and either correct where that value comes from or " +
            "check for it before this line.",
            ""),

        FindingKind.Logic => new MistakeGuide(
            "The code runs, but this line does not do what the code around it sets out to do.",
            "Nothing crashes, so the mistake goes unnoticed and the program quietly gives wrong answers.",
            "Compare what the line does with what the rest of the function expects of it, and change the line to match.",
            ""),

        _ => new MistakeGuide(
            "This works, but there is a clearer or safer way to write it.",
            "Code written this way is harder to read and easier to break when it is changed later.",
            "Rewrite it in the form shown.",
            ""),
    };
}
