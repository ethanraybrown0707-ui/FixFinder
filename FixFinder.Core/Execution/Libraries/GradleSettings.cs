using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// A Gradle build as its settings.gradle - or settings.gradle.kts - in the build's top folder lays it out: the projects it
/// includes, each in a folder of its own, and the builds it includes for their build logic. A project uses another's code
/// by naming it in its build file: implementation project(':core').
/// </summary>
/// <remarks>
/// A project's folder is found as Gradle finds it: the names in its path as folders under the build's top folder - :libs:core
/// is libs\core - unless the settings give it another with project(':core').projectDir = file('...'). Including :libs:core
/// includes :libs as well, as it does in Gradle. Settings that work out what to include as they run, in a loop say, are not
/// run here, so what they include is not known.
/// </remarks>
public static partial class GradleSettings
{
    /// <summary>A Gradle build: its top folder, its settings file, each project it includes by path with its folder, and the folders of the builds it includes.</summary>
    public sealed record Build(string RootFolder, string SettingsFile, IReadOnlyDictionary<string, string> ProjectFolders, IReadOnlyList<string> IncludedBuilds)
    {
        /// <summary>The folder of the project of a path - :core, or : for the build's top project - or null when the build includes none of that path.</summary>
        public string? FolderOf(string projectPath) =>
            projectPath == ":" ? RootFolder : ProjectFolders.TryGetValue(projectPath, out var folder) ? folder : null;

        /// <summary>The path of the project in a folder - :core, or : for the top folder - or null when no project of the build is there.</summary>
        public string? PathOf(string folder)
        {
            var full = Full(folder);
            if (SameFolder(full, RootFolder)) return ":";

            return ProjectFolders.Where(project => SameFolder(project.Value, full)).Select(project => project.Key).FirstOrDefault();
        }

        /// <summary>The paths of the projects above one, from the top of the build down: : and then :libs, for :libs:core.</summary>
        public static IReadOnlyList<string> Above(string projectPath)
        {
            if (projectPath == ":") return [];

            var names = projectPath.Split(':', StringSplitOptions.RemoveEmptyEntries);
            return [":", .. Enumerable.Range(1, names.Length - 1).Select(count => ":" + string.Join(':', names.Take(count)))];
        }

        /// <summary>
        /// The path of the project a build file names with project('...'), read as Gradle reads it: from the top of the build
        /// when it starts with a colon, and otherwise from the project that names it.
        /// </summary>
        public static string PathNamed(string written, string namedFrom)
        {
            var path = written.Trim();
            return path.StartsWith(':') ? path : namedFrom == ":" ? ":" + path : $"{namedFrom}:{path}";
        }
    }

    private static readonly string[] SettingsNames = ["settings.gradle", "settings.gradle.kts"];

    /// <summary>include 'app', 'core' - or include(":app", ":core"), over several lines if need be - with the names it gives.</summary>
    [GeneratedRegex(@"\binclude\s*\(?\s*(?<names>(?:['""][^'""\r\n]+['""]\s*,?\s*)+)\)?")]
    private static partial Regex IncludeLine();

    [GeneratedRegex(@"['""](?<name>[^'""\r\n]+)['""]")]
    private static partial Regex Quoted();

    /// <summary>project(':core').projectDir = file('modules/core'), with new File(settingsDir, '...') as another way of writing it.</summary>
    [GeneratedRegex(@"project\s*\(\s*['""](?<path>:?[^'""]+)['""]\s*\)\s*\.projectDir\s*=\s*(?:file|new\s+File|File)\s*\(\s*(?:(?:settingsDir|rootDir)\s*,\s*)?['""](?<folder>[^'""]+)['""]\s*\)")]
    private static partial Regex ProjectFolderLine();

    /// <summary>includeBuild('build-logic'): a build of its own, included for the plugins it makes - or for its libraries.</summary>
    [GeneratedRegex(@"\bincludeBuild\s*\(?\s*['""](?<folder>[^'""\r\n]+)['""]")]
    private static partial Regex IncludeBuildLine();

    /// <summary>
    /// The Gradle build a project folder is part of, from the settings file in that folder or in the nearest folder above it
    /// that has one: null when there is none, or when that settings file's build has no project in this folder.
    /// </summary>
    public static Build? Of(string projectFolder)
    {
        var own = Full(projectFolder);

        for (var folder = new DirectoryInfo(own); folder is not null; folder = folder.Parent)
        {
            if (SettingsNames.Select(name => Path.Combine(folder.FullName, name)).FirstOrDefault(File.Exists) is not { } settings) continue;

            var build = Read(settings);
            return build.PathOf(own) is not null ? build : null;
        }

        return null;
    }

    private static Build Read(string settingsFile)
    {
        var root = Full(Path.GetDirectoryName(Path.GetFullPath(settingsFile))!);
        var folders = new Dictionary<string, string>(StringComparer.Ordinal);

        string text;
        try
        {
            text = GradleBuild.WithoutComments(File.ReadAllText(settingsFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Build(root, Path.GetFullPath(settingsFile), folders, []);
        }

        foreach (Match include in IncludeLine().Matches(text))
        {
            foreach (Match name in Quoted().Matches(include.Groups["names"].Value))
            {
                var path = Build.PathNamed(name.Groups["name"].Value, ":");

                // Including :libs:core includes :libs too, in the folder above core's.
                foreach (var each in Build.Above(path).Skip(1).Append(path))
                    folders.TryAdd(each, Path.Combine([root, .. each.Split(':', StringSplitOptions.RemoveEmptyEntries)]));
            }
        }

        foreach (Match moved in ProjectFolderLine().Matches(text))
        {
            var path = Build.PathNamed(moved.Groups["path"].Value, ":");
            if (folders.ContainsKey(path)) folders[path] = Full(Path.Combine(root, moved.Groups["folder"].Value));
        }

        var includedBuilds = IncludeBuildLine().Matches(text).Select(included => Full(Path.Combine(root, included.Groups["folder"].Value))).ToList();

        return new Build(root, Path.GetFullPath(settingsFile), folders, includedBuilds);
    }

    private static string Full(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/');

    private static bool SameFolder(string first, string second) => string.Equals(Full(first), Full(second), StringComparison.OrdinalIgnoreCase);
}
