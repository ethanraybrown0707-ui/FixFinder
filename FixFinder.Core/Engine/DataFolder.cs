namespace FixFinder.Core.Engine;

/// <summary>
/// Where FixFinder keeps what it remembers on this computer - the history of checks, the preferences, the problems and
/// progress of FixFinder Learn, its builds, its cache and its saved sign-in: the folder <see cref="Setting"/> names when it
/// is set, and otherwise FixFinder's folder in the account's local application data.
/// </summary>
/// <remarks>
/// The setting is for trying FixFinder - its window, FixFinder Learn - without what it does going into the person's own
/// record: a check adds to the history, and each problem it finds goes into Learn's list, so a check made only to see the
/// window would otherwise be counted among the person's own programs and mistakes.
/// </remarks>
public static class DataFolder
{
    /// <summary>The environment variable that names another folder for FixFinder's data.</summary>
    public const string Setting = "FIXFINDER_DATA_FOLDER";

    /// <summary>The folder FixFinder's data is kept in.</summary>
    public static string Root { get; } = Environment.GetEnvironmentVariable(Setting) is { Length: > 0 } chosen
        ? Path.GetFullPath(chosen)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixFinder");
}
