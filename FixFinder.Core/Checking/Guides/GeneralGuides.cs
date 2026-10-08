namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// The guide used when nothing more specific is known about a mistake: one per kind, worded for the language, and each
/// explained for someone new to programming.
/// </summary>
internal static class GeneralGuides
{
    public static MistakeGuide For(string language, FindingKind kind) => kind switch
    {
        FindingKind.Syntax => new MistakeGuide(
            $"Before a program runs, {language} reads the whole of it to understand it - like reading a recipe through before " +
            "cooking. Something here does not follow the rules for how code has to be written, so it stopped before running " +
            "anything at all.",
            "Until this is fixed nothing in the program runs at all - not even the lines before it.",
            "Read the message from the line it names, and look at the end of the line before it too: a missing bracket, quote or " +
            "colon is often reported one line late.",
            ""),

        FindingKind.Runtime => new MistakeGuide(
            "The code was fine to start with, but while the program was running it reached this line and something went wrong - " +
            $"a value was not what the code expected - so {language} stopped it here.",
            "Everything after this point is skipped, and anyone running the program sees it crash instead of the result.",
            "Work out which value on the line was not what the code expected, and either correct where that value comes from or " +
            "check for it before this line.",
            ""),

        FindingKind.Logic => new MistakeGuide(
            "Nothing crashes and no error appears - the program just does the wrong thing. This line does not do what the rest " +
            "of the code expects of it, so the answers the program gives are wrong.",
            "Nothing crashes, so the mistake goes unnoticed and the program quietly gives wrong answers.",
            "Compare what the line does with what the rest of the function expects of it, and change the line to match.",
            ""),

        FindingKind.Performance => new MistakeGuide(
            "The program gets the right answer, but it takes a longer way round to it than it needs to - and the extra work " +
            "grows as the amount of data grows.",
            "It is correct either way, and quick while the data is small; it is the part that slows down first when real data arrives.",
            "Do the work once rather than over and over - the finding says what to change.",
            ""),

        _ => new MistakeGuide(
            "The code works as it is, but it is written in a way that is harder to read, or easier to break later. There is a " +
            "clearer way to say the same thing.",
            "Code written this way is harder to read and easier to break when it is changed later.",
            "Rewrite it in the form shown.",
            ""),
    };
}
