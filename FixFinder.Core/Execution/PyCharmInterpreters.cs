using System.Xml;
using System.Xml.Linq;

namespace FixFinder.Core.Execution;

/// <summary>
/// The interpreter a PyCharm project is set to use. The project's .idea\misc.xml names it - Python 3.12 (coursework) - and
/// PyCharm's own list of interpreters, jdk.table.xml in its settings folder, says where that one is.
/// </summary>
public static class PyCharmInterpreters
{
    /// <summary>The Python the PyCharm project in the folder is set to use, when its settings name one on this computer.</summary>
    public static PythonEnvironment.Found? NamedFor(string folder, PythonEnvironment.Places places)
    {
        if (ChosenIn(Path.Combine(folder, ".idea", "misc.xml")) is not { } name) return null;

        foreach (var interpreters in Lists(places))
        {
            if (HomeOf(name, interpreters, places) is { } interpreter && File.Exists(interpreter) &&
                Path.GetFileName(interpreter).StartsWith("python", StringComparison.OrdinalIgnoreCase))
            {
                return new PythonEnvironment.Found(interpreter, $"the Python this project's PyCharm settings name, {name}");
            }
        }

        return null;
    }

    /// <summary>The interpreter misc.xml names: project-jdk-name on its ProjectRootManager.</summary>
    private static string? ChosenIn(string settings)
    {
        if (!File.Exists(settings)) return null;

        try
        {
            return XDocument.Load(settings).Descendants("component")
                .FirstOrDefault(component => (string?)component.Attribute("name") == "ProjectRootManager")?
                .Attribute("project-jdk-name")?.Value is { Length: > 0 } name ? name : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    /// <summary>PyCharm's lists of interpreters, one in each JetBrains settings folder - PyCharm2024.3, PyCharmCE2024.3 - the latest first.</summary>
    private static IReadOnlyList<string> Lists(PythonEnvironment.Places places)
    {
        var jetBrains = Path.Combine(places.AppData, "JetBrains");

        try
        {
            return Directory.Exists(jetBrains)
                ? [.. Directory.EnumerateDirectories(jetBrains)
                    .Select(product => Path.Combine(product, "options", "jdk.table.xml"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Where the interpreter of that name is, as the list says - with PyCharm's $USER_HOME$ put back as the home folder.</summary>
    private static string? HomeOf(string name, string interpreters, PythonEnvironment.Places places)
    {
        try
        {
            var home = XDocument.Load(interpreters).Descendants("jdk")
                .Where(interpreter => (string?)interpreter.Element("name")?.Attribute("value") == name)
                .Select(interpreter => (string?)interpreter.Element("homePath")?.Attribute("value"))
                .FirstOrDefault(path => path is { Length: > 0 });

            return home?.Replace("$USER_HOME$", places.Home, StringComparison.Ordinal).Replace('/', Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }
}
