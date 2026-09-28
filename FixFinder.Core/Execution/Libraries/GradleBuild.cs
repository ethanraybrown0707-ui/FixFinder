using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// What a Gradle build file declares that the program needs, read from the forms a course project's build file is written
/// in: implementation 'group:artifact:version' and testImplementation("..."), the group:/name:/version: form, a platform()
/// BOM, a version catalog's libs.name, and files() or fileTree() of jars beside it. Anything written another way is named
/// as not read, rather than guessed at.
/// </summary>
/// <remarks>
/// Gradle itself is not run: it downloads what it needs and takes time to start, and FixFinder neither downloads nor
/// waits on a build tool. What the build file names is looked up among what Gradle and Maven have already downloaded.
/// </remarks>
public static partial class GradleBuild
{
    /// <summary>What a build file declares: libraries, the BOMs that set their versions, jar files, and lines not read.</summary>
    public sealed record Declared(
        IReadOnlyList<DeclaredDependency> Dependencies,
        IReadOnlyList<LibraryName> Platforms,
        IReadOnlyList<(string Jar, bool Test)> Files,
        IReadOnlyList<string> NotRead);

    /// <summary>The configurations a program's own code and its tests are built and run with; others belong to the build.</summary>
    private static readonly Dictionary<string, bool> ConfigurationIsTest = new(StringComparer.Ordinal)
    {
        ["implementation"] = false, ["api"] = false, ["compileOnly"] = false, ["compileOnlyApi"] = false, ["runtimeOnly"] = false,
        ["compile"] = false, ["runtime"] = false, ["testImplementation"] = true, ["testCompileOnly"] = true,
        ["testRuntimeOnly"] = true, ["testCompile"] = true, ["testRuntime"] = true,
    };

    [GeneratedRegex(@"(?m)^\s*(?<configuration>[a-zA-Z]+)\s*(?:\(\s*)?(?<rest>.+?)\s*$")]
    private static partial Regex DeclarationLine();

    [GeneratedRegex(@"^(?:platform|enforcedPlatform)\s*\(\s*(?<what>.+?)\s*\)\s*\)?\s*$")]
    private static partial Regex Platform();

    [GeneratedRegex(@"^(?<quote>['""])(?<notation>[^'""]+)\k<quote>\s*\)?(?:\s*\{.*)?$")]
    private static partial Regex StringNotation();

    [GeneratedRegex(@"group\s*[:=]\s*['""](?<group>[^'""]+)['""]\s*,\s*name\s*[:=]\s*['""](?<name>[^'""]+)['""](?:\s*,\s*version\s*[:=]\s*['""](?<version>[^'""]+)['""])?")]
    private static partial Regex MapNotation();

    [GeneratedRegex(@"^libs\.(?<alias>[\w.]+?)\s*\)?\s*$")]
    private static partial Regex CatalogAlias();

    [GeneratedRegex(@"^(?<kind>files|fileTree)\s*\((?<arguments>.*)\)\s*\)?\s*$")]
    private static partial Regex FileDependency();

    [GeneratedRegex(@"['""](?<text>[^'""]+)['""]")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"(?m)^\s*(?:def|val|var|ext\.|extra\[""|set\(\s*"")\s*(?<name>\w+)[""\]]*\s*(?:=|,)\s*['""](?<value>[^'""$]+)['""]")]
    private static partial Regex Variable();

    [GeneratedRegex(@"\$\{(?<name>\w+)\}|\$(?<name>\w+)")]
    private static partial Regex Interpolation();

    [GeneratedRegex(@"id\s*\(?\s*['""]org\.springframework\.boot['""]\s*\)?\s*version\s*\(?\s*['""](?<version>[^'""]+)['""]")]
    private static partial Regex SpringBootPlugin();

    public static Declared Read(string buildFile)
    {
        var text = WithoutComments(ReadAllText(buildFile));
        var folder = Path.GetDirectoryName(buildFile)!;
        var variables = VariablesFor(text, folder);
        var catalog = VersionCatalog.Read(Path.Combine(folder, "gradle", "libs.versions.toml"));

        var dependencies = new List<DeclaredDependency>();
        var platforms = new List<LibraryName>();
        var files = new List<(string, bool)>();
        var notRead = new List<string>();

        if (SpringBootPlugin().Match(text) is { Success: true } boot)
            platforms.Add(new LibraryName("org.springframework.boot", "spring-boot-dependencies", boot.Groups["version"].Value));

        foreach (var block in DependencyBlocks(text))
        {
            foreach (Match line in DeclarationLine().Matches(block))
            {
                if (!ConfigurationIsTest.TryGetValue(line.Groups["configuration"].Value, out var test)) continue;

                var rest = Fill(line.Groups["rest"].Value.Trim(), variables);

                if (Platform().Match(rest) is { Success: true } platform)
                {
                    var what = platform.Groups["what"].Value;
                    if (Library(what, catalog) is { Version: { } version } bom) platforms.Add(new LibraryName(bom.Group, bom.Artifact, version));
                    else notRead.Add(line.Value.Trim());
                }
                else if (FileDependency().Match(rest) is { Success: true } local)
                {
                    files.AddRange(Jars(local.Groups["kind"].Value, local.Groups["arguments"].Value, folder).Select(jar => (jar, test)));
                }
                else if (CatalogAlias().Match(rest) is { Success: true } alias && alias.Groups["alias"].Value.StartsWith("bundles.", StringComparison.Ordinal))
                {
                    var bundle = catalog.Bundle(alias.Groups["alias"].Value["bundles.".Length..]);
                    if (bundle is null) notRead.Add(line.Value.Trim());
                    else dependencies.AddRange(bundle.Select(library => library with { Scope = test ? "test" : null }));
                }
                else if (Library(rest, catalog) is { } library)
                {
                    dependencies.Add(library with { Scope = test ? "test" : null });
                }
                else
                {
                    notRead.Add(line.Value.Trim());
                }
            }
        }

        return new Declared(dependencies, platforms, files, notRead);
    }

    /// <summary>One library, written as "group:artifact:version", as group:/name:/version:, or as a version catalog alias.</summary>
    private static DeclaredDependency? Library(string written, VersionCatalog catalog)
    {
        if (StringNotation().Match(written) is { Success: true } notation)
        {
            var coordinates = notation.Groups["notation"].Value;
            var extension = coordinates.Contains('@') ? coordinates[(coordinates.IndexOf('@') + 1)..] : "jar";
            var parts = coordinates.Split('@')[0].Split(':');

            if (parts.Length < 2 || parts.Any(part => part.Contains('$'))) return null;

            return new DeclaredDependency(
                parts[0], parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null,
                Type: extension == "jar" ? "jar" : extension, Classifier: parts.Length > 3 ? parts[3] : "");
        }

        if (MapNotation().Match(written) is { Success: true } map)
        {
            return new DeclaredDependency(map.Groups["group"].Value, map.Groups["name"].Value, map.Groups["version"].Success ? map.Groups["version"].Value : null);
        }

        return CatalogAlias().Match(written) is { Success: true } alias ? catalog.Library(alias.Groups["alias"].Value) : null;
    }

    /// <summary>The jars files('lib/a.jar') or fileTree(dir: 'libs', include: ['*.jar']) names, as files beside the build.</summary>
    private static IEnumerable<string> Jars(string kind, string arguments, string folder)
    {
        var named = Quoted().Matches(arguments).Select(match => match.Groups["text"].Value).ToList();

        if (kind == "files") return named.Select(file => Path.GetFullPath(Path.Combine(folder, file))).Where(File.Exists);

        // fileTree: the first path is the folder; its jars are taken whatever the include says, as every jar is what one is for.
        var tree = named.FirstOrDefault(name => !name.Contains('*') && name is not ("dir" or "include")) is { } dir ? Path.Combine(folder, dir) : null;
        if (tree is null || !Directory.Exists(tree)) return [];

        try
        {
            return Directory.EnumerateFiles(tree, "*.jar", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The text inside each top-level dependencies { } block - not the one in buildscript { }, which is Gradle's own.</summary>
    private static IEnumerable<string> DependencyBlocks(string text)
    {
        var blocks = new List<string>();
        var depth = 0;
        var insideBuildscript = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (insideBuildscript >= 0 && depth < insideBuildscript) insideBuildscript = -1;
            }

            if (!IsWordAt(text, i)) continue;

            if (Starts(text, i, "buildscript") && OpensBlock(text, i + "buildscript".Length, out _)) insideBuildscript = depth + 1;

            if (insideBuildscript < 0 && Starts(text, i, "dependencies") && OpensBlock(text, i + "dependencies".Length, out var open) &&
                ClosingBrace(text, open) is var close and >= 0)
            {
                blocks.Add(text[(open + 1)..close]);
                i = close;
            }
        }

        return blocks;
    }

    private static bool IsWordAt(string text, int index) => index == 0 || !(char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '_' || text[index - 1] == '.');

    private static bool Starts(string text, int index, string word) =>
        string.CompareOrdinal(text, index, word, 0, word.Length) == 0 &&
        (index + word.Length == text.Length || !char.IsLetterOrDigit(text[index + word.Length]));

    private static bool OpensBlock(string text, int from, out int brace)
    {
        brace = from;
        while (brace < text.Length && char.IsWhiteSpace(text[brace])) brace++;
        return brace < text.Length && text[brace] == '{';
    }

    private static int ClosingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
        }

        return -1;
    }

    /// <summary>Names a build file gives values to - def, val, ext. - and those gradle.properties beside it sets.</summary>
    private static Dictionary<string, string> VariablesFor(string text, string folder)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        var properties = Path.Combine(folder, "gradle.properties");
        if (File.Exists(properties))
        {
            foreach (var line in ReadAllText(properties).Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] is '#' or '!' || trimmed.IndexOf('=') is not (> 0 and var equals)) continue;
                variables[trimmed[..equals].Trim()] = trimmed[(equals + 1)..].Trim();
            }
        }

        foreach (Match variable in Variable().Matches(text)) variables[variable.Groups["name"].Value] = variable.Groups["value"].Value;

        // ext { junitVersion = '5.10.2' } sets names in a block of its own.
        foreach (Match block in Regex.Matches(text, @"\bext\s*\{(?<body>[^{}]*)\}"))
        {
            foreach (Match setting in Regex.Matches(block.Groups["body"].Value, @"(?m)^\s*(?<name>\w+)\s*=\s*['""](?<value>[^'""$]+)['""]"))
                variables[setting.Groups["name"].Value] = setting.Groups["value"].Value;
        }

        return variables;
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> variables) =>
        Interpolation().Replace(text, match => variables.TryGetValue(match.Groups["name"].Value, out var value) ? value : match.Value);

    /// <summary>The build file without its comments, keeping anything in quotes - a URL's // is not a comment.</summary>
    private static string WithoutComments(string text)
    {
        var kept = new StringBuilder(text.Length);
        char? quote = null;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quote is { } open)
            {
                kept.Append(c);
                if (c == '\\' && i + 1 < text.Length) kept.Append(text[++i]);
                else if (c == open) quote = null;
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                kept.Append(c);
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                kept.Append('\n');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
            }
            else
            {
                kept.Append(c);
            }
        }

        return kept.ToString();
    }

    private static string ReadAllText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
