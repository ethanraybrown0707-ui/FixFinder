namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Finding where brackets and brace blocks open and close, in code with its strings and comments masked out.</summary>
internal static class Brackets
{
    /// <summary>Brace depth at the start of each masked line; one extra entry for the end of the file.</summary>
    public static int[] BraceDepths(IReadOnlyList<string> masked)
    {
        var depths = new int[masked.Count + 1];
        var depth = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            depths[i] = depth;

            foreach (var c in masked[i])
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }
        }

        depths[masked.Count] = depth;
        return depths;
    }

    /// <summary>The index of the bracket closing the one at <paramref name="open"/> on the same line.</summary>
    public static int? ClosingParenthesis(string masked, int open)
    {
        var depth = 0;

        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '(') depth++;
            else if (masked[i] == ')' && --depth == 0) return i;
        }

        return null;
    }

    /// <summary>The closing brace of the block opened on <paramref name="line"/>, or null.</summary>
    public static int? BlockEnd(IReadOnlyList<string> masked, int line)
    {
        var depth = 0;
        var opened = false;

        for (var i = line; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{') { depth++; opened = true; }
                else if (c == '}' && --depth == 0 && opened) return i;
            }

            if (!opened && i > line) return null;
        }

        return null;
    }

    /// <summary>The line holding the brace that closes the first block opened on or after a line.</summary>
    public static int? FirstBlockEnd(IReadOnlyList<string> masked, int start)
    {
        var depth = 0;
        var opened = false;

        for (var i = start; i < masked.Count; i++)
        {
            foreach (var c in masked[i])
            {
                if (c == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (c == '}' && opened && --depth == 0)
                {
                    return i;
                }
            }

            if (!opened && i > start + 1) return null;
        }

        return null;
    }

    /// <summary>The index of the bracket that opens the one closing at <paramref name="close"/>, or -1.</summary>
    public static int Opening(string text, int close)
    {
        var depth = 0;

        for (var i = close; i >= 0; i--)
        {
            if (text[i] is ')' or ']' or '}') depth++;
            else if (text[i] is '(' or '[' or '{' && --depth == 0) return i;
        }

        return -1;
    }

    /// <summary>The index of the bracket that closes the one opening at <paramref name="open"/>, or -1.</summary>
    public static int Closing(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            if (text[i] is '(' or '[' or '{') depth++;
            else if (text[i] is ')' or ']' or '}' && --depth == 0) return i;
        }

        return -1;
    }
}
