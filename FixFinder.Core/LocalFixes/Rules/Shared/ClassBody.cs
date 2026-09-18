using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>
/// Where a class's body is, and how its members are indented, for adding members to it.
/// </summary>
/// <remarks>
/// Shared by C#, Java and C++, whose class bodies are all brace-delimited: a missing member goes in
/// on its own lines just before the closing brace, indented like the members already there.
/// </remarks>
internal static class ClassBody
{
    /// <summary>The line of a class's closing brace (0-based), or null when its body is not laid out over separate lines.</summary>
    public static int? Closing(IReadOnlyList<string> masked, int header)
    {
        if (CppCode.ClassBraces(masked, header) is not { } body || body.Open == body.Close) return null;

        return masked[body.Close].TrimStart().StartsWith('}') ? body.Close : null;
    }

    /// <summary>The indentation a new member of the class gets.</summary>
    public static string MemberIndent(IReadOnlyList<string> lines, IReadOnlyList<string> masked, int header, int closing)
    {
        for (var i = header + 1; i < closing; i++)
        {
            var text = masked[i].Trim();
            if (text.Length == 0 || text is "{" || text.EndsWith(':')) continue;

            return Indent(lines[i]);
        }

        var outer = Indent(lines[header]);
        return outer + (lines.Any(l => l.StartsWith('\t')) ? "\t" : "    ");
    }

    public static string Indent(string line) => line[..(line.Length - line.TrimStart().Length)];

    /// <summary>One more level of indentation than <paramref name="indent"/>, in the same characters.</summary>
    public static string Deeper(string indent) => indent + (indent.Contains('\t') ? "\t" : "    ");
}
