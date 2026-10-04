using System.Text.RegularExpressions;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The C# a project is compiled as: its LangVersion when it sets one as a number, else the C# its target framework comes
/// with - read from its .csproj, and from the Directory.Build.props above it, which MSBuild reads first.
/// </summary>
/// <remarks>
/// Which C# each target framework comes with is Microsoft's table of defaults ("C# language versioning", learn.microsoft.com):
/// C# 15 for .NET 11, 14 for .NET 10, 13 for .NET 9, 12 for .NET 8, 11 for .NET 7, 10 for .NET 6, 9 for .NET 5, 8 for .NET
/// Core 3 and .NET Standard 2.1, and 7.3 for everything older - .NET Framework among them. A LangVersion of latest, preview or
/// default depends on the compiler that builds it, so it is left unsaid.
/// </remarks>
public static partial class CSharpBuiltAs
{
    /// <summary>The C# a project is compiled as, and why.</summary>
    /// <param name="Version">The C# it is compiled as.</param>
    /// <param name="Because">"as Marks.csproj targets net8.0", or "as Marks.csproj's LangVersion says 11.0".</param>
    /// <param name="Project">The project file.</param>
    /// <param name="TargetFramework">The target framework it is compiled for, when it names one.</param>
    /// <param name="LangVersionSet">Whether its LangVersion decides, rather than its target framework.</param>
    /// <param name="DecidedIn">The file that sets what decides: the project, or the Directory.Build.props above it.</param>
    public sealed record BuiltAs(LanguageVersion Version, string Because, string Project, string? TargetFramework, bool LangVersionSet, string DecidedIn);

    [GeneratedRegex(@"^net(?<major>\d+)\.\d+", RegexOptions.IgnoreCase)]
    private static partial Regex ModernDotNet();

    [GeneratedRegex(@"^netcoreapp(?<major>\d+)\.\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetCore();

    [GeneratedRegex(@"^netstandard(?<version>\d+\.\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetStandard();

    [GeneratedRegex(@"^net\d{2,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetFramework();

    [GeneratedRegex(@"^\d+(?:\.\d+)?$")]
    private static partial Regex NumberedRelease();

    /// <summary>The folders a project's .cs files are never in: what it is built into.</summary>
    private static readonly HashSet<string> BuiltInto = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj" };

    /// <summary>What the project of this .cs file - or this .csproj - is compiled as, or null when it has none or it cannot be told.</summary>
    public static BuiltAs? For(string csharpFile)
    {
        var project = Path.GetExtension(csharpFile).Equals(".csproj", StringComparison.OrdinalIgnoreCase) ? csharpFile : ProgramLayout.CSharpProject(csharpFile);
        if (project is null || Read(project) is not { } projectText) return null;

        var propsFile = BuildPropsAbove(Path.GetDirectoryName(Path.GetFullPath(project))!);
        var propsText = propsFile is null ? null : Read(propsFile);

        // A property the project sets is its own; one it leaves to Directory.Build.props is said as that file's.
        (string Value, string SetIn)? Property(string name) =>
            PropertyIn(projectText, name) is { } own ? (own, Path.GetFileName(project))
            : propsText is not null && PropertyIn(propsText, name) is { } shared ? (shared, Path.GetFileName(propsFile)!)
            : null;

        var targetFramework = Property("TargetFramework") ?? (Property("TargetFrameworks") is { } several
            ? (several.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "", several.SetIn)
            : null);

        if (Property("LangVersion") is { } langVersion)
        {
            return NumberedRelease().IsMatch(langVersion.Value) && LanguageVersion.Find(langVersion.Value) is { } set
                ? new BuiltAs(set.Minor > 0 ? set : set.MajorOnly, $"as {langVersion.SetIn}'s LangVersion says {langVersion.Value}", project, targetFramework?.Value, LangVersionSet: true, langVersion.SetIn)
                : null;
        }

        return targetFramework is { Value.Length: > 0 } target && DefaultFor(target.Value) is { } version
            ? new BuiltAs(version, $"as {target.SetIn} targets {target.Value}", project, target.Value, LangVersionSet: false, target.SetIn)
            : null;
    }

    /// <summary>The C# a target framework comes with, or null for one the table does not have.</summary>
    internal static LanguageVersion? DefaultFor(string targetFramework)
    {
        if (ModernDotNet().Match(targetFramework) is { Success: true } modern)
        {
            var major = int.Parse(modern.Groups["major"].Value);
            return major is >= 5 and <= 11 ? new LanguageVersion(major + 4) : null;
        }

        if (DotNetCore().Match(targetFramework) is { Success: true } core)
            return int.Parse(core.Groups["major"].Value) >= 3 ? new LanguageVersion(8) : new LanguageVersion(7, 3);

        if (DotNetStandard().Match(targetFramework) is { Success: true } standard)
            return standard.Groups["version"].Value == "2.1" ? new LanguageVersion(8) : new LanguageVersion(7, 3);

        return DotNetFramework().IsMatch(targetFramework) ? new LanguageVersion(7, 3) : null;
    }

    /// <summary>The .NET a target framework is of, when it is .NET 5 or later: 8 for net8.0 and net8.0-windows.</summary>
    internal static int? DotNetOf(string targetFramework) =>
        ModernDotNet().Match(targetFramework) is { Success: true } modern && int.Parse(modern.Groups["major"].Value) >= 5 ? int.Parse(modern.Groups["major"].Value) : null;

    /// <summary>The .cs files of a project: those in its folder and the folders under it, but not in what it is built into.</summary>
    internal static IEnumerable<string> SourcesOf(string project)
    {
        var sources = new List<string>();
        var folders = new Stack<string>([Path.GetDirectoryName(Path.GetFullPath(project))!]);

        while (folders.Count > 0 && sources.Count < 500)
        {
            var folder = folders.Pop();

            try
            {
                sources.AddRange(Directory.EnumerateFiles(folder, "*.cs"));
                foreach (var child in Directory.EnumerateDirectories(folder).Where(child => !BuiltInto.Contains(Path.GetFileName(child)) && !Path.GetFileName(child).StartsWith('.')))
                    folders.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return sources;
    }

    /// <summary>A property as a project file sets it - the first time, whatever its condition: &lt;LangVersion&gt;12&lt;/LangVersion&gt;.</summary>
    private static string? PropertyIn(string projectText, string name) =>
        Regex.Match(projectText, $@"<{name}(?:\s[^>]*)?>\s*(?<value>[^<]*?)\s*</{name}>", RegexOptions.IgnoreCase) is { Success: true } property && property.Groups["value"].Value.Length > 0
            ? property.Groups["value"].Value
            : null;

    /// <summary>The Directory.Build.props MSBuild reads for a project: the nearest in its folder or a folder above it.</summary>
    private static string? BuildPropsAbove(string folder)
    {
        for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
        {
            var props = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(props)) return props;
        }

        return null;
    }

    private static string? Read(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
