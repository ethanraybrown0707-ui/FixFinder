using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// What FixFinder knows of an F# script - an .fsx file, which F# Interactive, dotnet fsi, checks and runs: how it is checked
/// and run, the files it loads, and whether it fetches packages.
/// </summary>
/// <remarks>
/// <para>
/// dotnet fsi --typecheck-only checks a script, and the files it loads, as running it would, and runs none of it. It stops at
/// the first error, so a check names one.
/// </para>
/// <para>
/// A script is run with --optimize-: optimised, as F# Interactive runs a script unless told otherwise, the line .NET names
/// for a crash can be the wrong one - an array read past its end inside a for loop on line 3 was said to be on line 1.
/// </para>
/// </remarks>
public static partial class FSharpScript
{
    /// <summary>What checks a script, before its name: no banner, no colours in the messages, nothing run.</summary>
    public const string CheckArguments = "fsi --nologo --consolecolors- --typecheck-only";

    /// <summary>What runs a script, before its name: no banner, no colours, and the lines of a crash named as written.</summary>
    public const string RunArguments = "fsi --nologo --consolecolors- --optimize-";

    /// <summary><c>#load "helpers.fsx"</c>, or several at once: <c>#load "a.fs" "b.fs"</c>.</summary>
    [GeneratedRegex(@"^\s*#load\s+(?<names>(?:@?""[^""]+""\s*)+)")]
    private static partial Regex LoadLine();

    [GeneratedRegex(@"@?""(?<name>[^""]+)""")]
    private static partial Regex QuotedName();

    /// <summary><c>#r "nuget: Newtonsoft.Json"</c> - a package F# Interactive fetches as the script starts.</summary>
    [GeneratedRegex(@"^\s*#(?:r|i)\s+@?""\s*nuget\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex PackageLine();

    /// <summary>The files a script loads with #load that are there - each named from the script's own folder, as F# finds them.</summary>
    public static IEnumerable<string> Loaded(string script)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(script);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(script))!;

        foreach (var line in lines)
        {
            if (LoadLine().Match(line) is not { Success: true } load) continue;

            foreach (Match name in QuotedName().Matches(load.Groups["names"].Value))
            {
                var loaded = Path.GetFullPath(Path.Combine(folder, name.Groups["name"].Value.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(loaded)) yield return loaded;
            }
        }
    }

    /// <summary>Whether a script brings in a NuGet package with #r "nuget: ...", which F# Interactive downloads as it starts.</summary>
    public static bool FetchesPackages(string script)
    {
        try
        {
            return File.ReadLines(script).Any(line => PackageLine().IsMatch(line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
