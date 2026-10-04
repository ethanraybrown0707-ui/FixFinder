using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>
/// The files a wildcard pattern such as src/*.c names - found as make's $(wildcard) and CMake's file(GLOB) find them, in
/// the order of their names, and written the way the pattern is written: src/*.c gives src/main.c.
/// </summary>
internal static class PathPattern
{
    /// <summary>More entries than this is more than a program's folder - a whole drive, say - and the search stops there.</summary>
    private const int MostEntriesLookedAt = 20_000;

    public static bool HasWildcards(string pattern) => pattern.IndexOfAny(['*', '?', '[']) >= 0;

    /// <summary>What the pattern names, a relative pattern taken from <paramref name="folder"/>.</summary>
    public static IReadOnlyList<string> Matches(string folder, string pattern)
    {
        if (pattern.Length == 0) return [];

        var parts = pattern.Replace('\\', '/').Split('/');
        var fromADrive = parts[0].Length == 2 && parts[0][1] == ':';
        var rooted = fromADrive || parts[0].Length == 0;

        // Each found path twice: as the pattern writes it, and where it is on disk.
        var found = new List<(string Written, string OnDisk)>
        {
            fromADrive ? (parts[0] + "/", parts[0] + "\\") : rooted ? ("/", Path.GetPathRoot(Path.GetFullPath(folder))!) : ("", folder),
        };

        var looked = 0;

        foreach (var (part, index) in parts.Select((part, index) => (part, index)).Skip(rooted ? 1 : 0))
        {
            var isLast = index == parts.Length - 1;
            var next = new List<(string, string)>();

            foreach (var (written, onDisk) in found)
            {
                if (part.Length == 0)
                {
                    if (!isLast) next.Add((written, onDisk));
                    continue;
                }

                if (!HasWildcards(part))
                {
                    var path = Path.Combine(onDisk, part);
                    if (isLast ? File.Exists(path) || Directory.Exists(path) : Directory.Exists(path)) next.Add((Join(written, part), path));
                    continue;
                }

                var name = NamePattern(part);

                foreach (var entry in Entries(onDisk, directoriesOnly: !isLast))
                {
                    if (++looked > MostEntriesLookedAt) return [];

                    var entryName = Path.GetFileName(entry);
                    if (name.IsMatch(entryName) && (entryName[0] != '.' || part[0] == '.')) next.Add((Join(written, entryName), entry));
                }
            }

            found = next;
        }

        return found.Select(path => path.Written).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// CMake's GLOB_RECURSE: the files whose names match the last part of the pattern, in the folder the rest of it names
    /// and every folder under it - src/*.c finds src/main.c and src/shapes/circle.c.
    /// </summary>
    public static IReadOnlyList<string> RecursiveMatches(string folder, string pattern)
    {
        var normalised = pattern.Replace('\\', '/');
        var slash = normalised.LastIndexOf('/');
        var name = NamePattern(normalised[(slash + 1)..]);

        var bases = slash < 0
            ? [folder]
            : HasWildcards(normalised[..slash])
                ? Matches(folder, normalised[..slash]).Select(found => Path.GetFullPath(Path.Combine(folder, found))).Where(Directory.Exists).ToList()
                : [Path.GetFullPath(Path.Combine(folder, normalised[..(slash == 0 ? 1 : slash)]))];

        var files = new List<string>();
        var looked = 0;

        foreach (var start in bases)
        {
            var pending = new Stack<string>([start]);

            while (pending.Count > 0)
            {
                var directory = pending.Pop();

                foreach (var entry in Entries(directory, directoriesOnly: false))
                {
                    if (++looked > MostEntriesLookedAt) return [];

                    if (Directory.Exists(entry)) pending.Push(entry);
                    else if (name.IsMatch(Path.GetFileName(entry))) files.Add(entry);
                }
            }
        }

        return files.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Whether a name matches a pattern of one part: *.c, test_?.c, [ab]*.c.</summary>
    public static bool NameMatches(string name, string pattern) => NamePattern(pattern).IsMatch(name);

    private static string Join(string written, string part) => written.Length == 0 || written.EndsWith('/') ? written + part : $"{written}/{part}";

    private static IEnumerable<string> Entries(string directory, bool directoriesOnly)
    {
        try
        {
            return directoriesOnly ? Directory.EnumerateDirectories(directory).ToList() : Directory.EnumerateFileSystemEntries(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>One part of a pattern as a regular expression: * is any run of characters, ? one, [...] one of a set. Names on Windows match whatever their case.</summary>
    private static Regex NamePattern(string part)
    {
        var expression = new StringBuilder("^");

        for (var at = 0; at < part.Length; at++)
        {
            var character = part[at];

            switch (character)
            {
                case '*':
                    expression.Append(".*");
                    break;
                case '?':
                    expression.Append('.');
                    break;
                case '[' when part.IndexOf(']', at + 1) is var closing and > 0:
                {
                    var set = part[(at + 1)..closing];
                    var negated = set.StartsWith('!') || set.StartsWith('^');
                    if (negated) set = set[1..];

                    expression.Append(negated ? "[^" : "[").Append(set.Replace("\\", "\\\\", StringComparison.Ordinal)).Append(']');
                    at = closing;
                    break;
                }
                default:
                    expression.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        return new Regex(expression.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
