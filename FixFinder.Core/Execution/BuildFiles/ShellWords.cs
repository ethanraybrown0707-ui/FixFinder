using System.Text;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>
/// A recipe line split the way a POSIX shell splits it - into the commands joined by ;, &amp;&amp;, || and |, and each command
/// into its words with the quotes taken off - without running any of it.
/// </summary>
/// <remarks>
/// Redirections are left out, as they say where output goes rather than what is built. A word the shell itself would
/// fill in - $HOME, $(pwd), a backquoted command - is marked as not known, since only running the shell could say what
/// it becomes.
/// </remarks>
internal static class ShellWords
{
    public static IReadOnlyList<IReadOnlyList<string>> Commands(string line)
    {
        var commands = new List<IReadOnlyList<string>>();
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        var dependsOnTheShell = false;

        void EndWord()
        {
            if (!inWord) return;

            words.Add(dependsOnTheShell ? Unknowable.Mark(word.ToString()) : word.ToString());
            word.Clear();
            inWord = false;
            dependsOnTheShell = false;
        }

        void EndCommand()
        {
            EndWord();
            if (words.Count == 0) return;

            commands.Add(words.ToList());
            words.Clear();
        }

        for (var at = 0; at < line.Length; at++)
        {
            var character = line[at];

            switch (character)
            {
                case '\\' when at + 1 < line.Length:
                    at++;
                    if (line[at] != '\n') word.Append(line[at]);
                    inWord = true;
                    break;

                case '\'':
                {
                    var closing = line.IndexOf('\'', at + 1);
                    if (closing < 0) closing = line.Length;

                    word.Append(line, at + 1, closing - at - 1);
                    inWord = true;
                    at = closing;
                    break;
                }

                case '"':
                    at = DoubleQuoted(line, at + 1, word, ref dependsOnTheShell);
                    inWord = true;
                    break;

                case '$' or '`':
                    word.Append(character);
                    inWord = true;
                    dependsOnTheShell = true;
                    break;

                case '#' when !inWord:
                    at = line.IndexOf('\n', at) is var lineEnd and >= 0 ? lineEnd - 1 : line.Length;
                    break;

                case '>' or '<':
                case '&' when at + 1 < line.Length && line[at + 1] == '>':
                    // 2>errors.txt: the 2 is the stream being redirected, not a word of the command.
                    if (inWord && word.ToString().All(char.IsAsciiDigit))
                    {
                        word.Clear();
                        inWord = false;
                        dependsOnTheShell = false;
                    }

                    EndWord();
                    at = AfterRedirection(line, at);
                    break;

                case ' ' or '\t' or '\r':
                    EndWord();
                    break;

                case '\n' or ';' or '|' or '&' or '(' or ')':
                    EndCommand();
                    break;

                default:
                    word.Append(character);
                    inWord = true;
                    break;
            }
        }

        EndCommand();
        return commands;
    }

    /// <summary>Reads what is between double quotes, where only \ before $, `, ", \ or a new line is an escape.</summary>
    private static int DoubleQuoted(string line, int start, StringBuilder word, ref bool dependsOnTheShell)
    {
        for (var at = start; at < line.Length; at++)
        {
            var character = line[at];

            if (character == '"') return at;

            if (character == '\\' && at + 1 < line.Length && line[at + 1] is '$' or '`' or '"' or '\\' or '\n')
            {
                at++;
                if (line[at] != '\n') word.Append(line[at]);
                continue;
            }

            if (character is '$' or '`') dependsOnTheShell = true;
            word.Append(character);
        }

        return line.Length;
    }

    /// <summary>Where the command goes on after a redirection - &gt;file, 2&gt;&amp;1, &lt; input.txt - and the file it names.</summary>
    private static int AfterRedirection(string line, int at)
    {
        while (at < line.Length && line[at] is '>' or '<' or '&') at++;

        // >&2 and 2>&1 name a stream rather than a file.
        if (at < line.Length && line[at - 1] == '&' && (char.IsAsciiDigit(line[at]) || line[at] == '-'))
        {
            while (at < line.Length && (char.IsAsciiDigit(line[at]) || line[at] == '-')) at++;
            return at - 1;
        }

        while (at < line.Length && line[at] is ' ' or '\t') at++;

        while (at < line.Length && line[at] is not (' ' or '\t' or '\r' or '\n' or ';' or '|' or '&' or '(' or ')'))
        {
            if (line[at] is '"' or '\'')
            {
                var closing = line.IndexOf(line[at], at + 1);
                at = closing < 0 ? line.Length : closing + 1;
                continue;
            }

            at++;
        }

        return at - 1;
    }
}
