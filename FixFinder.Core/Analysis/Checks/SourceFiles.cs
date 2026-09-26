namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// The one form of a file's path that every reference to the file shares, however each was written: a module that
/// names another does so relative to itself, and the program's list of files may not use the same form.
/// </summary>
internal static class SourceFiles
{
    /// <summary>Windows does not tell file names apart by case, and a program checked there is read the same way.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>The file's full path, or null when it is not a path at all - as for code with no file.</summary>
    public static string? FullPath(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        try
        {
            return Path.GetFullPath(file);
        }
        catch (Exception problem) when (problem is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
