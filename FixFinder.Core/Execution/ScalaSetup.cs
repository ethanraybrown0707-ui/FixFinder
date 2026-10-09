using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Execution;

/// <summary>
/// How a Scala program is built and run: with Scala CLI, with which version of Scala, and on which JDK - each chosen from
/// what is already on this computer, as nothing is downloaded, and each said, with why.
/// </summary>
/// <remarks>
/// <para>
/// The version of Scala is the one the program asks for - in a Scala CLI <c>//> using scala</c> directive, or as its
/// build.sbt's scalaVersion - when that one is in the cache, and otherwise the newest of the same series there: any Scala
/// 3 for a Scala 3, and the same 2.13 or 2.12 for a Scala 2. A program that asks for nothing is built with the newest
/// Scala 3 here, unless its code was found to be Scala 2's, as below.
/// </para>
/// <para>
/// Which Scala code is written in is worked out by the compiler, never from a list kept here: code that asks for no
/// version and does not compile as Scala 3 is compiled as the newest Scala 2 here, and when that compiles it with no
/// error, it is Scala 2 code and is built as Scala 2 - Scala 3 refuses some of what Scala 2 allowed, such as a method
/// written <c>def main(args: Array[String]) {</c> without <c>: Unit =</c>.
/// </para>
/// </remarks>
public static partial class ScalaSetup
{
    /// <summary>How the program is built.</summary>
    /// <param name="Version">The version of Scala it is built with - one in the cache.</param>
    /// <param name="JavaHome">The JDK Scala CLI compiles and runs it with, or null when no JDK was found and Scala CLI looks for itself.</param>
    /// <param name="Explained">What "how it ran" says of the version and the JDK, and why that version.</param>
    /// <param name="IsAskedFor">Whether the program asks for a version - in a directive or its build.sbt - so its version is not FixFinder's to work out.</param>
    public sealed record Setup(ScalaToolchains.ScalaCli Cli, ScalaToolchains.CachedScala Version, string? JavaHome, string Explained, bool IsAskedFor)
    {
        /// <summary>The libraries its build.sbt names, as Scala CLI's --dep takes them - found in the cache, as nothing is downloaded.</summary>
        public IReadOnlyList<string> Libraries { get; init; } = [];
    }

    /// <summary>A version of Scala the code was found to compile as, and what says so - in the compiler's own words.</summary>
    public sealed record Found(ScalaToolchains.CachedScala Version, string Explained);

    /// <summary>What asked for a version, and the version it asked for: "3.3.1" from "its build.sbt".</summary>
    private sealed record Asked(string Version, string By);

    [GeneratedRegex(@"(?m)^\s*//>\s*using\s+scala\s+(?<versions>.+?)\s*$")]
    private static partial Regex UsingScala();

    /// <summary>scalaVersion := "3.3.1", or ThisBuild / scalaVersion := scala3Version, or the older scalaVersion in ThisBuild := ....</summary>
    [GeneratedRegex(@"(?m)^\s*(?:ThisBuild\s*/\s*)?scalaVersion(?:\s+in\s+ThisBuild)?\s*:=\s*(?:""(?<version>[^""]+)""|(?<name>[A-Za-z_]\w*))")]
    private static partial Regex ScalaVersionSetting();

    [GeneratedRegex(@"--scala \S+")]
    private static partial Regex ScalaVersionOption();

    private static readonly ConcurrentDictionary<string, (string Version, DateTime Written)> Remembered = new(StringComparer.OrdinalIgnoreCase);

    public static (Setup? Setup, string? Problem) For(string source)
    {
        var original = ProgramCopy.OriginalOf(Path.GetFullPath(source));
        var name = Path.GetFileName(source);

        if (ScalaToolchains.Cli is not { } cli)
        {
            return (null,
                $"{name} is Scala, which Scala CLI builds and runs, and Scala CLI was not found - not on PATH, nor where its installers put it.\n\n" +
                "Install Scala CLI from scala-cli.virtuslab.org, and build the program once with it while online, so the Scala it needs is downloaded.");
        }

        var cached = ScalaToolchains.CachedVersions;
        if (cached.Count == 0)
        {
            return (null,
                $"{name} is Scala, and no version of Scala is in Scala CLI's cache on this computer, so it cannot be built here: FixFinder never " +
                "downloads anything. Building it once with Scala CLI while online - scala-cli compile . in its folder - downloads Scala, and FixFinder builds it after that.");
        }

        var jdk = Jdks.Default;
        var withJdk = jdk is null ? "" : $", with Java {jdk.VersionText} ({jdk.FoundIn})";
        var sbt = SbtBuildOf(original);
        var asked = AskedFor(original, sbt);

        if (asked is not null)
        {
            var wanted = asked.Version;

            if (cached.FirstOrDefault(version => version.Version == wanted) is { } exactly)
                return (WithLibraries(new Setup(cli, exactly, jdk?.Home, $"Scala {exactly.Version}, which {asked.By} asks for{withJdk}", IsAskedFor: true), sbt), null);

            var series = SeriesOf(wanted);
            if (cached.FirstOrDefault(version => version.Series == series) is { } nearest)
            {
                var newest = nearest.IsScala3 ? "the newest Scala 3 here" : $"the newest Scala {nearest.Series} here";
                return (WithLibraries(new Setup(cli, nearest, jdk?.Home,
                    $"Scala {nearest.Version}{withJdk} - {asked.By} asks for Scala {wanted}, which is not in Scala CLI's cache on this computer, so it is built with {newest}",
                    IsAskedFor: true), sbt), null);
            }

            return (null,
                $"{name} needs Scala {wanted}, as {asked.By} asks for it, and no Scala {series} is in Scala CLI's cache on this computer - only " +
                $"{string.Join(", ", cached.Select(version => version.Version))}. FixFinder never downloads anything: building the program once while online, with sbt " +
                "or with scala-cli compile . in its folder, downloads it, and FixFinder builds it after that.");
        }

        if (RememberedFor(original) is { } remembered && cached.FirstOrDefault(version => version.Version == remembered) is { } found)
            return (WithLibraries(new Setup(cli, found, jdk?.Home, $"Scala {found.Version}, the Scala its code was found to be written in{withJdk}", IsAskedFor: false), sbt), null);

        var usual = cached.FirstOrDefault(version => version.IsScala3) ?? cached[0];
        var which = usual.IsScala3 ? "the newest Scala 3 here" : "the newest Scala here";
        return (WithLibraries(new Setup(cli, usual, jdk?.Home, $"Scala {usual.Version}, {which}{withJdk}", IsAskedFor: false), sbt), null);
    }

    /// <summary>The program's build.sbt, read - or null when it has none.</summary>
    private static SbtBuildFile.Read? SbtBuildOf(string source)
    {
        var layout = ScalaProgram.Of(source);
        var buildFile = layout.BuildFile ?? Path.Combine(layout.Folder, "build.sbt");

        return File.Exists(buildFile) ? SbtBuildFile.Of(TextOf(buildFile)) : null;
    }

    /// <summary>The setup with the libraries its build.sbt names, and how it ran saying which - and which could not be read.</summary>
    private static Setup WithLibraries(Setup setup, SbtBuildFile.Read? sbt)
    {
        if (sbt is null || sbt.Libraries.Count == 0 && sbt.NotRead.Count == 0) return setup;

        var named = sbt.Libraries.Count == 0 ? ""
            : $", with the {(sbt.Libraries.Count == 1 ? "library" : "libraries")} its build.sbt names ({string.Join(", ", sbt.Libraries)})";
        var notRead = sbt.NotRead.Count == 0 ? "" : $" - what its build.sbt names that FixFinder could not read: {string.Join("; ", sbt.NotRead)}";

        return setup with { Libraries = sbt.Libraries, Explained = setup.Explained + named + notRead };
    }

    /// <summary>
    /// What Scala's compilers are given beside the program: no colour - Scala 3 colours its messages otherwise - every
    /// deprecation by name rather than a count of them, and a warning for an import, a private member or a local value
    /// that is never used. Scala 2.12 has no -Wunused, and only Scala 3 knows -color.
    /// </summary>
    public static string CompilerOptions(ScalaToolchains.CachedScala version) =>
        version.IsScala3 ? "-O -color:never -O -deprecation -O -Wunused:imports,privates,locals"
        : version is { Major: 2, Minor: >= 13 } ? "-O -deprecation -O -Wunused:imports,privates,locals"
        : "-O -deprecation";

    /// <summary>
    /// Scala CLI's arguments to build or run the program - <paramref name="action"/> is compile or run - offline, without its
    /// compile server, with its build kept in <paramref name="workspace"/> rather than the program's folder.
    /// </summary>
    public static string Arguments(string action, Setup setup, string workspace, IReadOnlyList<string> files, string? mainClass) =>
        $"--power {action} --server=false --offline --suppress-experimental-feature-warning --workspace \"{workspace}\"" +
        (setup.JavaHome is { } javaHome ? $" --java-home \"{javaHome}\"" : "") +
        $" --scala {setup.Version.Version} {CompilerOptions(setup.Version)}" +
        string.Concat(setup.Libraries.Select(library => $" --dep {library}")) +
        (mainClass is { } main ? $" --main-class {main}" : "") +
        string.Concat(files.Select(file => $" \"{file}\""));

    /// <summary>The version a program asks for, and what asks for it: a using directive in its files, then its build.sbt.</summary>
    private static Asked? AskedFor(string source, SbtBuildFile.Read? sbt)
    {
        foreach (var file in ScalaProgram.Of(source).Files)
        {
            if (UsingScala().Match(TextOf(file)) is { Success: true } directive &&
                directive.Groups["versions"].Value.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } first)
                return new Asked(first.Trim('"', '\''), $"the using directive in {Path.GetFileName(file)}");
        }

        return sbt?.ScalaVersion is { } version ? new Asked(version, "its build.sbt") : null;
    }

    /// <summary>The scalaVersion a build.sbt sets - written out, or as a val the file gives it, as sbt's templates write it.</summary>
    internal static string? ScalaVersionIn(string buildSbt)
    {
        if (ScalaVersionSetting().Match(buildSbt) is not { Success: true } setting) return null;
        if (setting.Groups["version"].Success) return setting.Groups["version"].Value;

        var valName = Regex.Escape(setting.Groups["name"].Value);
        return Regex.Match(buildSbt, $@"(?m)^\s*(?:lazy\s+)?val\s+{valName}\s*=\s*""(?<version>[^""]+)""") is { Success: true } val
            ? val.Groups["version"].Value
            : null;
    }

    /// <summary>The series a version is in: 3 for every Scala 3, else its major and minor - 2.13.12 is in 2.13.</summary>
    internal static string SeriesOf(string version)
    {
        var numbers = version.Split('.');
        if (numbers.Length == 0 || !int.TryParse(numbers[0], out var major)) return version;

        return major >= 3 || numbers.Length < 2 ? major.ToString() : $"{major}.{numbers[1]}";
    }

    /// <summary>
    /// The version of Scala, other than the one the build used, that compiles the program with no error - or null when
    /// there is none, or the program asks for a version of its own, which is kept to.
    /// </summary>
    /// <param name="errorsIn">Reads the errors in what Scala CLI printed, as the build's own errors were read.</param>
    public static async Task<Found?> FindAsync(
        string chosen, TargetSpec compile, IReadOnlyList<ParsedError> failed, Func<IReadOnlyList<CapturedLine>, IReadOnlyList<ParsedError>> errorsIn,
        CancellationToken cancellationToken)
    {
        if (For(chosen).Setup is not { IsAskedFor: false } setup) return null;

        var used = setup.Version;
        var others = ScalaToolchains.CachedVersions
            .Where(version => version.IsScala3 != used.IsScala3)
            .GroupBy(version => version.Series)
            .Select(series => series.First());

        foreach (var other in others)
        {
            var probe = new TargetSpec
            {
                ExecutablePath = compile.ExecutablePath,
                Arguments = ScalaVersionOption().Replace(compile.Arguments.Replace(CompilerOptions(used), CompilerOptions(other), StringComparison.Ordinal), $"--scala {other.Version}"),
                WorkingDirectory = compile.WorkingDirectory,
                Timeout = compile.Timeout,
                ExtraEnvironment = compile.ExtraEnvironment,
            };

            var run = await new TargetRunner().RunAsync(probe, cancellationToken);
            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled) return null;
            if (run.ExitCode != 0 || errorsIn(run.Lines).Count > 0) continue;

            var said = failed.FirstOrDefault() is { Frames: [{ File: { } file, Line: { } line }, ..] } first
                ? $" - Scala {used.Version} said of {Path.GetFileName(file)}:{line}: \"{FirstLineOf(first.Message)}\" -"
                : "";

            Remember(ScalaProgram.Of(chosen).Files, other.Version);

            return new Found(other,
                $"{Path.GetFileName(chosen)} does not compile as Scala {used.Version}{said} and does as Scala {other.Version}, so it is Scala " +
                $"{(other.IsScala3 ? "3" : "2")} code, and was built and run as Scala {other.Version}.");
        }

        return null;
    }

    /// <summary>The version a file of the program was found to be written in, while the file is as it was then - or null.</summary>
    public static string? RememberedFor(string source)
    {
        var path = Path.GetFullPath(ProgramCopy.OriginalOf(Path.GetFullPath(source)));
        return Remembered.TryGetValue(path, out var known) && known.Written == WrittenAt(path) ? known.Version : null;
    }

    /// <summary>Remembers the version for every file of the program, so its next build, and the copies its fixes are checked in, use it too.</summary>
    public static void Remember(IEnumerable<string> files, string version)
    {
        foreach (var file in files)
        {
            var path = Path.GetFullPath(file);
            Remembered[path] = (version, WrittenAt(path));
        }
    }

    private static string FirstLineOf(string? message) => (message ?? "").Split('\n')[0].Trim();

    private static DateTime WrittenAt(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DateTime.MinValue;
        }
    }

    private static string TextOf(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
