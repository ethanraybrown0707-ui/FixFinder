namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// The guide used when nothing more specific is known about a mistake: one per kind, worded for the language, and each
/// explained three ways - for someone new to programming, as it is usually taught, and in technical terms.
/// </summary>
internal static class GeneralGuides
{
    public static MistakeGuide For(string language, FindingKind kind) => kind switch
    {
        FindingKind.Syntax => new MistakeGuide(
            $"{language} could not make sense of the code here, so it stopped before running anything.",
            "Until this is fixed nothing in the program runs at all - not even the lines before it.",
            "Read the message from the line it names, and look at the end of the line before it too: a missing bracket, quote or " +
            "colon is often reported one line late.",
            "")
        {
            ForBeginners =
                $"Before a program runs, {language} reads the whole of it to understand it - like reading a recipe through before " +
                "cooking. Something here does not follow the rules for how code has to be written, so it stopped before running " +
                "anything at all.",
            ForTechnical =
                $"The code does not conform to {language}'s grammar, so it could not be parsed and nothing was produced to run. The " +
                "position reported is where the parser could go no further, which is often after the real mistake.",
        },

        FindingKind.Runtime => new MistakeGuide(
            $"The program started, and then {language} stopped it with an error at this line.",
            "Everything after this point is skipped, and anyone running the program sees it crash instead of the result.",
            "Work out which value on the line was not what the code expected, and either correct where that value comes from or " +
            "check for it before this line.",
            "")
        {
            ForBeginners =
                "The code was fine to start with, but while the program was running it reached this line and something went wrong - " +
                $"a value was not what the code expected - so {language} stopped it here.",
            ForTechnical =
                "Execution failed at this line with an error nothing handled - an exception, or in C and C++ a signal from the " +
                "operating system - so the program ended there; the message names the operation that failed.",
        },

        FindingKind.Logic => new MistakeGuide(
            "The code runs, but this line does not do what the code around it sets out to do.",
            "Nothing crashes, so the mistake goes unnoticed and the program quietly gives wrong answers.",
            "Compare what the line does with what the rest of the function expects of it, and change the line to match.",
            "")
        {
            ForBeginners =
                "Nothing crashes and no error appears - the program just does the wrong thing. This line does not do what the rest " +
                "of the code expects of it, so the answers the program gives are wrong.",
            ForTechnical =
                "The line is well formed and runs without error, but what it does contradicts what the surrounding code relies on, " +
                "so the program computes something other than what it was written to compute.",
        },

        FindingKind.Performance => new MistakeGuide(
            "This works, but it does more work than it needs to as its data grows.",
            "It is correct either way, and quick while the data is small; it is the part that slows down first when real data arrives.",
            "Do the work once rather than over and over - the finding says what to change.",
            "")
        {
            ForBeginners =
                "The program gets the right answer, but it takes a longer way round to it than it needs to - and the extra work " +
                "grows as the amount of data grows.",
            ForTechnical =
                "The code's running time grows faster with its input than the task requires, because of repeated work that another " +
                "data structure or order of operations would avoid.",
        },

        _ => new MistakeGuide(
            "This works, but there is a clearer or safer way to write it.",
            "Code written this way is harder to read and easier to break when it is changed later.",
            "Rewrite it in the form shown.",
            "")
        {
            ForBeginners =
                "The code works as it is, but it is written in a way that is harder to read, or easier to break later. There is a " +
                "clearer way to say the same thing.",
            ForTechnical =
                "The construct is valid and behaves as written, but a more usual form says the same thing more directly and leaves " +
                "less room for mistakes when the code is changed.",
        },
    };
}
