using System.Text.RegularExpressions;
using FixFinder.Core.Execution.Libraries;

namespace FixFinder.Core.Execution;

/// <summary>
/// How a Java program is built and run: with which JDK, for which Java, with preview features on or off - each as its
/// project, its own code or Settings asks - and the words that say so.
/// </summary>
/// <param name="Jdk">The JDK whose javac builds it, whose java runs it, and whose javap and tree reader read it.</param>
/// <param name="Release">The Java it is compiled for, or null for the JDK's own.</param>
/// <param name="StrictRelease">Whether that is javac's --release, rather than -source and -target.</param>
/// <param name="Preview">Whether preview features are on, for javac and for java alike.</param>
/// <param name="JdkExplained">Which JDK, and why that one when it is not the default: "Java 25.0.4.1 (Eclipse's own Java), as ...".</param>
/// <param name="HowCompiled">The Java it is compiled for and preview features, as a run explanation says them, or null for neither.</param>
/// <param name="CodeNeeds">
/// The Java the code was found to need, as a sentence of its own, when nothing else said it already: "Its code needs Java
/// 21 or later: Shapes.java uses a record pattern at line 12, which Java 21 made part of the language."
/// </param>
public sealed partial record JavaSetup(Jdk Jdk, int? Release, bool StrictRelease, bool Preview, string JdkExplained, string? HowCompiled, string? CodeNeeds = null)
{
    /// <summary>Both together, as one clause of a sentence.</summary>
    public string Explained => HowCompiled is null ? JdkExplained : $"{JdkExplained}, {HowCompiled}";

    /// <summary>The oldest Java javac compiles for in every JDK from 21 to 27: its Source.MIN.</summary>
    private const int OldestCompiledFor = 8;

    /// <summary>The first JDK whose oldest Java is known to be <see cref="OldestCompiledFor"/>; an older JDK is given the release as asked.</summary>
    private const int OldestKnownFrom = 21;

    /// <summary>The release javac is given: the one asked for, or Java 8 when that is older than this JDK compiles for.</summary>
    private int? CompiledFor => Release is { } release && Jdk.Version >= OldestKnownFrom ? Math.Max(release, OldestCompiledFor) : Release;

    /// <summary>What javac is given for the Java it compiles for: --release 21, or -source 21 -target 21, and --enable-preview.</summary>
    public IReadOnlyList<string> CompilerOptions
    {
        get
        {
            // Preview features are only those of the JDK's own release, and javac takes them only with that release named.
            if (Preview) return ["--enable-preview", "--release", Jdk.Version.ToString()];

            // JDK 8's javac has no --release, and compiling for Java 8 is its own default anyway.
            if (CompiledFor is not { } release || Jdk.Version < 9) return [];

            var level = release.ToString();
            return StrictRelease ? ["--release", level] : ["-source", level, "-target", level];
        }
    }

    /// <summary>What java is given: --enable-preview, when the program was compiled with it, or java will not load its classes.</summary>
    public IReadOnlyList<string> RunOptions => Preview ? ["--enable-preview"] : [];

    /// <summary>The compiler options as one command line's worth, with the space a command line joins them with.</summary>
    public string CompilerArguments => CompilerOptions.Count == 0 ? "" : string.Join(" ", CompilerOptions) + " ";

    /// <summary>The run options as one command line's worth, with the space a command line joins them with.</summary>
    public string RunArguments => RunOptions.Count == 0 ? "" : string.Join(" ", RunOptions) + " ";

    /// <summary>
    /// How this Java file's program is built: the JDK and Java Settings, its project and its code ask for, or why it cannot
    /// be built - no JDK at all, or none new enough for the Java its project or Settings names.
    /// </summary>
    public static (JavaSetup? Setup, string? Problem) For(string javaFile)
    {
        var installed = Jdks.Installed;
        var name = Path.GetFileName(javaFile);

        if (installed.Count == 0)
        {
            return (null,
                $"{name} is Java, which has to be compiled before it can run, and no JDK was found.\n\n" +
                "Install one - for example:\n" +
                "  winget install Microsoft.OpenJDK.25\n\n" +
                "FixFinder looks on PATH, in JAVA_HOME, where JDK installers put one, among the JDKs IntelliJ and Gradle download, " +
                "and in an Eclipse's own Java, so it does not need to be on PATH.");
        }

        var declared = DeclaredJava.Of(javaFile);
        var needs = JavaFeaturesUsed.For(javaFile);

        // Settings is the person's own choice, for their course, so it wins over what the project says.
        int? release = null;
        var strict = true;
        string? releaseSaidBy = null;

        if (int.TryParse(LanguageStandards.Current.JavaReleaseNumber, out var chosen))
        {
            release = chosen;
            releaseSaidBy = "as chosen in Settings";
        }
        else if (declared?.Release is { } projects)
        {
            release = projects;
            strict = declared.StrictRelease;
            releaseSaidBy = $"as {declared.SaidBy} says";
        }

        var preview = declared?.Preview ?? false;

        // The Java the JDK has to be at least, and at most, with why: what the code is written in, and the Lombok the project names.
        var atLeast = new List<(int Version, string Because)>();
        var atMost = new List<(int Version, string Because)>();
        if (needs is { AtLeast: { } codeNeeds, AtLeastBecause: { } codeNeedsBecause }) atLeast.Add((codeNeeds, codeNeedsBecause));
        if (needs is { AtMost: { } codeLimit, AtMostBecause: { } codeLimitBecause }) atMost.Add((codeLimit, codeLimitBecause));
        if (LombokLimit(javaFile) is { } lombok) atMost.Add(lombok);

        // A preview the code uses is turned on for it, with a JDK that has it - unless the project already says whether
        // preview features are on, or Settings or the project names a Java that does not have that preview.
        var codePreview = !preview && needs.Preview is { } used && (release is not { } releaseAsked || (releaseAsked >= used.From && releaseAsked <= used.Until)) ? needs.Preview : null;

        if (codePreview is not null)
        {
            atLeast.Add((codePreview.From, codePreview.Because));
            atMost.Add((codePreview.Until, codePreview.Because));
        }

        var lowest = atLeast.Select(each => each.Version).Append(release ?? 0).Max();
        var highest = atMost.Select(each => each.Version).Append(int.MaxValue).Min();
        var newest = installed[0];

        if (release is { } wanted && wanted > newest.Version)
        {
            var asked = releaseSaidBy == "as chosen in Settings"
                ? $"Settings asks for {name} to be compiled for Java {wanted}"
                : $"{name}'s project is written for Java {wanted} - {declared!.SaidBy} says so";
            var orElse = releaseSaidBy == "as chosen in Settings" ? "\n\nOr choose an earlier Java in Settings." : "";

            return (null,
                $"{asked} - and the newest JDK on this computer is Java {newest.VersionText} ({newest.FoundIn}, {newest.Home}). " +
                $"A JDK cannot compile for a later Java than its own.\n\n" +
                $"Install a JDK of Java {wanted} or later - {InstallAdvice(wanted)}{orElse}");
        }

        bool Fits(Jdk jdk) => jdk.Version >= lowest && jdk.Version <= highest;

        // Preview features are those of one release, so with them on the JDK has to be exactly the release compiled for.
        bool FitsPreview(Jdk jdk) => !preview || release is not { } previewed || jdk.Version == previewed;

        var fitting = installed.Where(Fits).ToList();
        var chosenByProject = declared?.JdkHome is { } home
            ? fitting.FirstOrDefault(jdk => string.Equals(jdk.Home, Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
              ?? (Jdks.At(Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar), "the project's own JDK") is { } named && Fits(named) ? named : null)
            : null;

        var jdk = new[]
            {
                chosenByProject,
                declared?.JdkVersion is { } jdkVersion ? fitting.FirstOrDefault(each => each.Version == jdkVersion && FitsPreview(each)) : null,
                Jdks.Default is { } usual && Fits(usual) && FitsPreview(usual) ? usual : null,
                // A preview changes from one Java to the next, so the newest JDK that has it has the one written about today.
                codePreview is not null ? fitting.Where(FitsPreview).OrderByDescending(each => each.Version).FirstOrDefault() : null,
                fitting.Where(FitsPreview).OrderBy(each => each.Version).FirstOrDefault(),
                fitting.OrderBy(each => each.Version).FirstOrDefault(),
            }
            .FirstOrDefault(candidate => candidate is not null)
            ?? Jdks.Default ?? newest;

        var previewFromCode = codePreview is not null && jdk.Version >= codePreview.From && jdk.Version <= codePreview.Until && FitsPreview(jdk);
        var withPreview = previewFromCode || (preview && (release is not { } previewRelease || previewRelease == jdk.Version));

        // How it was chosen: the JDK, and why that one when it is not the default; the Java it compiles for; preview features.
        var parts = new List<string> { $"Java {jdk.VersionText} ({jdk.FoundIn})" };

        var usualVersion = Jdks.Default?.Version;

        // Each reason is said once: why this JDK, what it compiles for, preview features, and what no JDK here can give.
        var said = new HashSet<string>(StringComparer.Ordinal);

        if (jdk == chosenByProject && declared?.JdkSaidBy is { } chosenBy) parts[0] += $", {chosenBy}";
        else if (declared?.JdkVersion == jdk.Version && declared.JdkSaidBy is { } askedBy && jdk != Jdks.Default) parts[0] += $", as {askedBy} asks";
        else if (atLeast.Where(each => jdk.Version >= each.Version && usualVersion < each.Version && !(previewFromCode && each.Because == codePreview!.Because))
                     .Select(each => each.Because).FirstOrDefault() is { } needsLater && said.Add(needsLater)) parts[0] += $", as {needsLater}";
        else if (atMost.Where(each => jdk.Version <= each.Version && usualVersion > each.Version && !(previewFromCode && each.Because == codePreview!.Because))
                     .Select(each => each.Because).FirstOrDefault() is { } needsEarlier && said.Add(needsEarlier)) parts[0] += $", as {needsEarlier}";

        if (release is { } compiledFor && !withPreview)
        {
            parts.Add(compiledFor < OldestCompiledFor && jdk.Version >= OldestKnownFrom
                ? $"compiling it for Java {OldestCompiledFor}, the oldest Java {jdk.Version}'s javac compiles for - {releaseSaidBy![3..]} Java {compiledFor}"
                : $"compiling it for Java {compiledFor} {releaseSaidBy}");
        }

        if (previewFromCode && said.Add(codePreview!.Because)) parts.Add($"with Java {jdk.Version}'s preview features on, as {codePreview.Because}");
        else if (withPreview && !previewFromCode) parts.Add($"with Java {jdk.Version}'s preview features on, as {declared!.SaidBy} turns them on");
        else if (preview) parts.Add($"with preview features off: {declared!.SaidBy} turns them on, and they need a JDK of exactly Java {release}, which is not on this computer");

        foreach (var (needed, because) in atLeast.Where(each => jdk.Version < each.Version && said.Add(each.Because)))
            parts.Add($"though {because}, and no JDK of Java {needed} or later is on this computer");
        foreach (var (limit, because) in atMost.Where(each => jdk.Version > each.Version && said.Add(each.Because)))
            parts.Add($"though {because}, and no JDK of Java {limit} or earlier is on this computer");

        // The Java the code was found to need, said even when the usual JDK was new enough for it - unless it was said already.
        var codeNeedsSentence = needs is { AtLeast: { } found, AtLeastBecause: { } foundBecause } && !said.Contains(foundBecause)
            ? $"Its code needs Java {found} or later: {foundBecause}."
            : null;

        return (new JavaSetup(jdk, release, strict, withPreview, parts[0], parts.Count > 1 ? string.Join(", ", parts.Skip(1)) : null, codeNeedsSentence), null);
    }


    /// <summary>
    /// The first Lombok that works with each Java, as Lombok's changelog records adding it: Lombok reaches into javac's own
    /// workings, which change with each Java, and an earlier Lombok stops javac building at all.
    /// </summary>
    private static readonly (int Java, Version FirstLombok)[] LombokSupport =
    [
        (16, new(1, 18, 20)), (17, new(1, 18, 22)), (18, new(1, 18, 24)), (19, new(1, 18, 26)), (20, new(1, 18, 28)), (21, new(1, 18, 30)),
        (22, new(1, 18, 32)), (23, new(1, 18, 36)), (24, new(1, 18, 38)), (25, new(1, 18, 40)), (26, new(1, 18, 46)), (27, new(1, 18, 48)),
    ];

    [GeneratedRegex(@"^lombok-(?<version>\d+(?:\.\d+){1,3})")]
    private static partial Regex LombokJar();

    /// <summary>
    /// The latest Java the Lombok the program is built with works with - from the version in its jar's name, as Maven and
    /// Gradle name it - with why, or null when no Lombok runs, or it is one that works with every Java up to 27.
    /// </summary>
    private static (int Version, string Because)? LombokLimit(string javaFile)
    {
        var lombok = JavaLibraries.For(javaFile).ProcessorPath
            .Select(jar => LombokJar().Match(Path.GetFileNameWithoutExtension(jar)))
            .FirstOrDefault(match => match.Success);
        if (lombok is null || !Version.TryParse(lombok.Groups["version"].Value, out var version)) return null;

        var firstUnsupported = LombokSupport.FirstOrDefault(support => support.FirstLombok > version);
        if (firstUnsupported == default) return null;

        return (firstUnsupported.Java - 1,
            $"the project builds with Lombok {lombok.Groups["version"].Value}, and Lombok added support for Java {firstUnsupported.Java} only in {firstUnsupported.FirstLombok}");
    }

    /// <summary>
    /// How to install a JDK of at least this Java: the Microsoft Build of OpenJDK 25 - the newest Java with long-term support,
    /// which compiles for any Java up to its own - or, for a later one, Eclipse Temurin's, which Adoptium publishes - Java 26
    /// and 27 among them - and winget lists once it has them.
    /// </summary>
    internal static string InstallAdvice(int atLeast) => atLeast <= 25
        ? "for example:\n  winget install Microsoft.OpenJDK.25"
        : $"Eclipse Temurin's JDKs are at adoptium.net, and winget lists the ones it can install with:\n  winget search Temurin\n\nOne of Java {atLeast} or later will do.";
}
