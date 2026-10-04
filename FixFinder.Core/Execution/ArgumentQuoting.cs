using System.Text;

namespace FixFinder.Core.Execution;

/// <summary>Writes one command-line argument so a Windows program reads it back exactly as it was.</summary>
public static class ArgumentQuoting
{
    /// <summary>
    /// The argument as it is when it needs no quotes; otherwise in quotes, with each quote in it escaped and the
    /// backslashes before a quote doubled, which is how the Windows C runtime splits a command line.
    /// </summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;

        var quoted = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes).Append(character);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
