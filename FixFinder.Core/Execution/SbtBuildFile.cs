using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// What a build.sbt says a Scala program is built with: its scalaVersion and the libraries its libraryDependencies name -
/// read from the file, never by running sbt.
/// </summary>
/// <remarks>
/// <para>
/// A library is written <c>"org.typelevel" %% "cats-core" % "2.10.0"</c>: %% for a library built for each version of
/// Scala, which Scala CLI is given as <c>org.typelevel::cats-core:2.10.0</c>, and % for a Java one, given as
/// <c>group:name:version</c>. A version may be a val the file sets - <c>val catsVersion = "2.10.0"</c> - as sbt's own
/// templates write it. A library only the tests use (<c>% Test</c>) is left out, as the tests are not built.
/// </para>
/// <para>
/// sbt keeps what it downloads in Coursier's cache, which Scala CLI reads, so a project sbt has built once has its
/// libraries there. What cannot be read this way - a version worked out by code, a Scala.js library - is named as not read.
/// </para>
/// </remarks>
public static partial class SbtBuildFile
{
    /// <param name="ScalaVersion">The scalaVersion it sets, or null when it sets none that can be read.</param>
    /// <param name="Libraries">Each library, as Scala CLI's --dep takes it: <c>org.typelevel::cats-core:2.10.0</c>.</param>
    /// <param name="NotRead">Each library the file names that could not be read, as the file writes it.</param>
    public sealed record Read(string? ScalaVersion, IReadOnlyList<string> Libraries, IReadOnlyList<string> NotRead);

    /// <summary><c>"group" %% "name" % version</c>, with what follows it: a scope such as % Test.</summary>
    [GeneratedRegex(@"""(?<group>[^""\s]+)""\s*(?<cross>%%%|%%|%)\s*""(?<name>[^""\s]+)""\s*%\s*(?:""(?<version>[^""\s]+)""|(?<versionName>[A-Za-z_]\w*))(?<scope>\s*%\s*(?:Test\b|""test""|IntegrationTest\b|""it""|Provided\b|""provided""))?")]
    private static partial Regex Library();

    [GeneratedRegex(@"\b(?:addSbtPlugin|addCompilerPlugin)\s*\(\s*$")]
    private static partial Regex PluginBefore();

    public static Read Of(string buildSbt)
    {
        var text = WithoutComments(buildSbt);
        var libraries = new List<string>();
        var notRead = new List<string>();

        foreach (Match library in Library().Matches(text))
        {
            // A plugin of sbt's or of the compiler's is written the same way, but is not a library the program uses.
            if (PluginBefore().IsMatch(text[..library.Index])) continue;

            var scope = library.Groups["scope"].Value;
            if (scope.Contains("Test", StringComparison.Ordinal) || scope.Contains("test", StringComparison.Ordinal) || scope.Contains("it\"", StringComparison.Ordinal))
                continue;

            var version = library.Groups["version"].Success ? library.Groups["version"].Value : ValOf(text, library.Groups["versionName"].Value);
            var cross = library.Groups["cross"].Value;

            if (version is null || cross == "%%%")
            {
                notRead.Add(library.Value.Trim());
                continue;
            }

            libraries.Add($"{library.Groups["group"].Value}{(cross == "%%" ? "::" : ":")}{library.Groups["name"].Value}:{version}");
        }

        return new Read(ScalaSetup.ScalaVersionIn(text), libraries.Distinct(StringComparer.Ordinal).ToList(), notRead);
    }

    /// <summary>The text a val of the file is set to - <c>val catsVersion = "2.10.0"</c> - or null when it is set to anything else.</summary>
    private static string? ValOf(string text, string name) =>
        Regex.Match(text, $@"(?m)^\s*(?:lazy\s+)?val\s+{Regex.Escape(name)}\s*=\s*""(?<value>[^""]+)""") is { Success: true } val ? val.Groups["value"].Value : null;

    /// <summary>
    /// The file with its comments blanked out - // to the end of a line, and /* to */ - and its strings kept, so a library
    /// commented out is not read as one, and one whose name has // in it is still read.
    /// </summary>
    private static string WithoutComments(string buildSbt)
    {
        var text = buildSbt.Replace("\r\n", "\n").ToCharArray();
        var inString = false;
        var inBlockComment = false;

        for (var index = 0; index < text.Length; index++)
        {
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (inBlockComment)
            {
                if (text[index] == '*' && next == '/')
                {
                    text[index] = text[index + 1] = ' ';
                    inBlockComment = false;
                    index++;
                }
                else if (text[index] != '\n')
                {
                    text[index] = ' ';
                }

                continue;
            }

            if (inString)
            {
                if (text[index] == '\\') index++;
                else if (text[index] == '"' || text[index] == '\n') inString = false;
                continue;
            }

            if (text[index] == '"') inString = true;
            else if (text[index] == '/' && next == '*') inBlockComment = true;
            else if (text[index] == '/' && next == '/')
            {
                while (index < text.Length && text[index] != '\n') text[index++] = ' ';
                index--;
                continue;
            }

            if (inBlockComment) text[index] = ' ';
        }

        return new string(text);
    }
}
