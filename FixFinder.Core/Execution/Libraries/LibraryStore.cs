using System.Xml.Linq;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// A folder of libraries already downloaded to this computer - Maven's local repository, or Gradle's cache of what it
/// has downloaded. FixFinder only ever reads these; it never downloads a library itself.
/// </summary>
public abstract class LibraryStore
{
    /// <summary>The store as a sentence can name it: "the Maven repository at C:\Users\sam\.m2\repository".</summary>
    public abstract string Name { get; }

    /// <summary>The library's file with this extension - "jar" or "pom" - or null when it has not been downloaded.</summary>
    public abstract string? FileOf(LibraryName library, string extension);

    /// <summary>Every version of a library this store holds, for choosing one from a range such as [1.0,2.0).</summary>
    public abstract IEnumerable<string> VersionsOf(string group, string artifact);

    protected static string FileName(LibraryName library, string extension) =>
        extension == "jar" && library.Classifier.Length > 0
            ? $"{library.Artifact}-{library.Version}-{library.Classifier}.jar"
            : $"{library.Artifact}-{library.Version}.{extension}";

    protected static IEnumerable<string> FoldersIn(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>().ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The folder a user's own files are under, which Maven and Gradle keep their downloads beneath.</summary>
    protected static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

/// <summary>
/// Maven's local repository: group/as/folders/artifact/version/artifact-version.jar, beside the pom.xml that says what the
/// library itself needs. IntelliJ, Eclipse and VS Code put the libraries a Maven project needs here too.
/// </summary>
public sealed class MavenRepository(string root) : LibraryStore
{
    public string Root => root;

    public override string Name => $"the Maven repository at {root}";

    public override string? FileOf(LibraryName library, string extension)
    {
        var file = Path.Combine([root, .. library.Group.Split('.'), library.Artifact, library.Version, FileName(library, extension)]);
        return File.Exists(file) ? file : null;
    }

    public override IEnumerable<string> VersionsOf(string group, string artifact) =>
        FoldersIn(Path.Combine([root, .. group.Split('.'), artifact]));

    /// <summary>
    /// Where Maven keeps what it downloads for this user: the localRepository its settings.xml names, or .m2\repository
    /// in the user's folder, which is where it is unless someone changed it.
    /// </summary>
    public static MavenRepository ForThisUser()
    {
        var settings = Path.Combine(UserHome, ".m2", "settings.xml");

        try
        {
            if (File.Exists(settings) &&
                XDocument.Load(settings).Descendants().FirstOrDefault(element => element.Name.LocalName == "localRepository")?.Value.Trim() is { Length: > 0 } named)
            {
                return new MavenRepository(Path.GetFullPath(named.Replace("${user.home}", UserHome, StringComparison.Ordinal)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or NotSupportedException)
        {
        }

        return new MavenRepository(Path.Combine(UserHome, ".m2", "repository"));
    }
}

/// <summary>
/// Gradle's cache of downloaded libraries: group/artifact/version/checksum/artifact-version.jar, with the pom.xml that says
/// what each library needs kept beside it the same way.
/// </summary>
public sealed class GradleCache(string root) : LibraryStore
{
    public override string Name => $"Gradle's cache at {root}";

    public override string? FileOf(LibraryName library, string extension)
    {
        var folder = Path.Combine(root, library.Group, library.Artifact, library.Version);
        var name = FileName(library, extension);

        foreach (var checksum in FoldersIn(folder))
        {
            var file = Path.Combine(folder, checksum, name);
            if (File.Exists(file)) return file;
        }

        return null;
    }

    public override IEnumerable<string> VersionsOf(string group, string artifact) => FoldersIn(Path.Combine(root, group, artifact));

    /// <summary>Gradle's cache for this user, under GRADLE_USER_HOME when that is set and .gradle in the user's folder when not.</summary>
    public static GradleCache ForThisUser()
    {
        var home = Environment.GetEnvironmentVariable("GRADLE_USER_HOME") is { Length: > 0 } set ? set : Path.Combine(UserHome, ".gradle");
        return new GradleCache(Path.Combine(home, "caches", "modules-2", "files-2.1"));
    }
}

/// <summary>Several stores looked in one after another, the first that has a file giving it.</summary>
public sealed class LibraryStores(IReadOnlyList<LibraryStore> stores) : LibraryStore
{
    public IReadOnlyList<LibraryStore> Each => stores;

    public override string Name => stores.Count == 1 ? stores[0].Name : string.Join(" or ", stores.Select(store => store.Name));

    public override string? FileOf(LibraryName library, string extension) =>
        stores.Select(store => store.FileOf(library, extension)).FirstOrDefault(file => file is not null);

    public override IEnumerable<string> VersionsOf(string group, string artifact) =>
        stores.SelectMany(store => store.VersionsOf(group, artifact)).Distinct(StringComparer.Ordinal);

    /// <summary>Where this user's Maven and Gradle keep their downloads: Maven's repository first, as Maven builds use it.</summary>
    public static LibraryStores ForThisUser() => new([MavenRepository.ForThisUser(), GradleCache.ForThisUser()]);
}
