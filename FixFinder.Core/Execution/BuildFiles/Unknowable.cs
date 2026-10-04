using System.Text;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>
/// A value in a build file that could only be known by running something FixFinder does not run - the output of
/// $(shell ...), a variable the shell sets, the folder CMake would build in - kept where it was in the text, so whatever
/// is worked out from it is known to depend on it rather than taken to be empty.
/// </summary>
/// <remarks>
/// The mark travels with the text: a list of sources built from $(shell find ...) still holds it after every variable,
/// substitution and list command it went through, so the program built from that list is known not to be followed,
/// however it was put together. What it would have come from is written inside it in hexadecimal, so no space, quote,
/// bracket or semicolon in it can split it into words or list items, or be read as part of a command.
/// </remarks>
internal static class Unknowable
{
    private const char Opening = '\u0001';
    private const char Closing = '\u0002';

    /// <summary>Text standing for a value that is not known, naming what it would have come from.</summary>
    public static string Mark(string whatItComesFrom) =>
        $"{Opening}{Convert.ToHexString(Encoding.UTF8.GetBytes(whatItComesFrom.Trim()))}{Closing}";

    public static bool IsIn(string text) => text.Contains(Opening);

    /// <summary>What each unknown value in the text would have come from, in the order they appear.</summary>
    public static IEnumerable<string> SourcesIn(string text)
    {
        var searchFrom = 0;

        while (text.IndexOf(Opening, searchFrom) is var opening and >= 0)
        {
            var closing = text.IndexOf(Closing, opening + 1);
            if (closing < 0) yield break;

            yield return Decoded(text[(opening + 1)..closing]);
            searchFrom = closing + 1;
        }
    }

    private static string Decoded(string hexadecimal)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(hexadecimal));
        }
        catch (FormatException)
        {
            return "a value FixFinder does not know";
        }
    }
}
