using System.Text;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>One command in a CMake file: its name in lower case, its arguments as written, and where it is.</summary>
internal sealed record CMakeCommand(string Name, IReadOnlyList<CMakeArgument> Arguments, string File, int Line);

/// <summary>One argument as written, and how it was written - which decides how CMake expands it.</summary>
internal sealed record CMakeArgument(string Text, CMakeArgumentKind Kind);

internal enum CMakeArgumentKind
{
    /// <summary>Written plainly: its variables are expanded and it is then split into a list at each ;.</summary>
    Unquoted,

    /// <summary>In "...": its variables are expanded and it stays one argument.</summary>
    Quoted,

    /// <summary>In [[...]]: taken exactly as written.</summary>
    Bracket,
}

/// <summary>
/// Reads CMake's language into its commands - names, arguments, comments - as cmake-language(7) sets it out, keeping
/// each argument's escapes and variable references for when the command is run.
/// </summary>
internal static class CMakeScript
{
    public static List<CMakeCommand> Commands(string text, string file)
    {
        var commands = new List<CMakeCommand>();
        var at = 0;
        var line = 1;

        while (at < text.Length)
        {
            var character = text[at];

            if (character == '\n')
            {
                line++;
                at++;
            }
            else if (char.IsWhiteSpace(character))
            {
                at++;
            }
            else if (character == '#')
            {
                at = AfterComment(text, at, ref line);
            }
            else if (char.IsAsciiLetter(character) || character == '_')
            {
                var start = at;
                while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '_')) at++;

                var name = text[start..at];
                var commandLine = line;

                while (at < text.Length && text[at] is ' ' or '\t') at++;

                if (at < text.Length && text[at] == '(')
                {
                    at++;
                    commands.Add(new CMakeCommand(name.ToLowerInvariant(), Arguments(text, ref at, ref line), file, commandLine));
                }
            }
            else
            {
                at++;
            }
        }

        return commands;
    }

    private static List<CMakeArgument> Arguments(string text, ref int at, ref int line)
    {
        var arguments = new List<CMakeArgument>();
        var depth = 0;

        while (at < text.Length)
        {
            var character = text[at];

            switch (character)
            {
                case '\n':
                    line++;
                    at++;
                    continue;

                case ' ' or '\t' or '\r':
                    at++;
                    continue;

                case '#':
                    at = AfterComment(text, at, ref line);
                    continue;

                case '(':
                    depth++;
                    arguments.Add(new CMakeArgument("(", CMakeArgumentKind.Unquoted));
                    at++;
                    continue;

                case ')':
                    at++;
                    if (depth-- == 0) return arguments;

                    arguments.Add(new CMakeArgument(")", CMakeArgumentKind.Unquoted));
                    continue;

                case '[' when BracketLevel(text, at) is { } level:
                    arguments.Add(new CMakeArgument(Bracketed(text, ref at, level, ref line), CMakeArgumentKind.Bracket));
                    continue;

                case '"':
                    arguments.Add(new CMakeArgument(Quoted(text, ref at, ref line), CMakeArgumentKind.Quoted));
                    continue;

                default:
                    arguments.Add(new CMakeArgument(Unquoted(text, ref at), CMakeArgumentKind.Unquoted));
                    continue;
            }
        }

        return arguments;
    }

    /// <summary>The number of = in an opening bracket [==[ at <paramref name="at"/>, or null when there is none there.</summary>
    private static int? BracketLevel(string text, int at)
    {
        var level = 0;
        var position = at + 1;

        while (position < text.Length && text[position] == '=')
        {
            level++;
            position++;
        }

        return position < text.Length && text[position] == '[' ? level : null;
    }

    private static string Bracketed(string text, ref int at, int level, ref int line)
    {
        var opening = at + level + 2;
        var closing = text.IndexOf("]" + new string('=', level) + "]", opening, StringComparison.Ordinal);
        if (closing < 0) closing = text.Length;

        // A new line straight after the opening bracket is not part of the argument.
        var start = opening < text.Length && text[opening] == '\n' ? opening + 1
            : opening + 1 < text.Length && text[opening] == '\r' && text[opening + 1] == '\n' ? opening + 2
            : opening;

        var content = text[Math.Min(start, closing)..closing];
        line += text.AsSpan(at, Math.Min(closing + level + 2, text.Length) - at).Count('\n');
        at = Math.Min(closing + level + 2, text.Length);

        return content;
    }

    /// <summary>What is between the quotes, escapes and all - they are worked out when the argument is expanded.</summary>
    private static string Quoted(string text, ref int at, ref int line)
    {
        var content = new StringBuilder();

        for (at++; at < text.Length; at++)
        {
            var character = text[at];

            if (character == '"')
            {
                at++;
                return content.ToString();
            }

            if (character == '\n') line++;

            if (character == '\\' && at + 1 < text.Length)
            {
                content.Append(character).Append(text[++at]);
                if (text[at] == '\n') line++;
                continue;
            }

            content.Append(character);
        }

        return content.ToString();
    }

    /// <summary>An unquoted argument: up to a space, a bracket or a comment - with any "..." inside it kept whole, as older CMake files write -DNAME="a b".</summary>
    private static string Unquoted(string text, ref int at)
    {
        var content = new StringBuilder();

        while (at < text.Length)
        {
            var character = text[at];

            if (character is ' ' or '\t' or '\r' or '\n' or '(' or ')' or '#') break;

            if (character == '\\' && at + 1 < text.Length)
            {
                content.Append(character).Append(text[at + 1]);
                at += 2;
                continue;
            }

            if (character == '"')
            {
                var closing = text.IndexOf('"', at + 1);
                if (closing < 0) closing = text.Length - 1;

                content.Append(text, at, closing - at + 1);
                at = closing + 1;
                continue;
            }

            content.Append(character);
            at++;
        }

        return content.ToString();
    }

    private static int AfterComment(string text, int at, ref int line)
    {
        if (at + 1 < text.Length && text[at + 1] == '[' && BracketLevel(text, at + 1) is { } level)
        {
            var closing = text.IndexOf("]" + new string('=', level) + "]", at, StringComparison.Ordinal);
            var end = closing < 0 ? text.Length : closing + level + 2;

            line += text.AsSpan(at, end - at).Count('\n');
            return end;
        }

        var lineEnd = text.IndexOf('\n', at);
        return lineEnd < 0 ? text.Length : lineEnd;
    }
}
