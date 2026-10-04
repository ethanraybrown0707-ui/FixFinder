using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// What javac says when the code is newer than the Java it is built for - a feature that is only a preview in this JDK, or
/// that the release it compiles for does not have, or a library built for a later Java - said once, as a note naming the
/// Java the code needs and whether a JDK of it is on this computer, beside the errors themselves.
/// </summary>
public static partial class JavaVersionErrors
{
    /// <summary>
    /// The release that made each feature javac names part of the language, from javac 27's own Source.Feature table, and
    /// the earlier names the same feature had in 21 to 24. Primitive patterns are a preview in every JDK up to 27; string
    /// templates were a preview in 21 and 22 and were then taken out, so no Java has them.
    /// </summary>
    private static readonly Dictionary<string, int> StandardSince = new(StringComparer.Ordinal)
    {
        ["unnamed variables"] = 22,
        ["unnamed classes"] = 25,
        ["implicitly declared classes"] = 25,
        ["statements before super()"] = 25,
        ["flexible constructors"] = 25,
        ["module imports"] = 25,
        ["transitive modifier for java.base"] = 25,
    };

    /// <summary>The features that javac names - as it named them in the JDK that said so - which are a preview in every Java up to 27.</summary>
    private static readonly HashSet<string> StillPreview = new(StringComparer.Ordinal) { "primitive patterns" };

    /// <summary>The features a preview once had that no Java has kept.</summary>
    private static readonly HashSet<string> TakenOut = new(StringComparer.Ordinal) { "string templates" };

    [GeneratedRegex(@"^(?<feature>.+?) (?:is|are) a preview feature and (?:is|are) disabled by default")]
    private static partial Regex PreviewDisabled();

    [GeneratedRegex(@"^(?<feature>.+?) (?:is|are) not supported in -source (?<release>\d+)")]
    private static partial Regex NotInTheRelease();

    [GeneratedRegex(@"use -source (?<needed>\d+) or higher")]
    private static partial Regex HigherNeeded();

    [GeneratedRegex(@"class file has wrong version (?<found>\d+)\.\d+, should be (?<expected>\d+)\.\d+")]
    private static partial Regex WrongClassVersion();

    [GeneratedRegex(@"bad class file:\s*(?<file>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex BadClassFile();

    [GeneratedRegex(@"^package java\.applet does not exist$|^cannot find symbol$")]
    private static partial Regex AppletMissing();

    /// <summary>
    /// A class file's major version is its Java plus 44 - 52 for Java 8, 65 for 21, 69 for 25, 71 for 27 - as the Java Virtual
    /// Machine Specification's table of class file versions has it.
    /// </summary>
    internal static int JavaOfClassFileVersion(int major) => major - 44;

    /// <summary>The note for what javac said, or null when nothing it said is about the Java the code is built for.</summary>
    public static string? NoteFor(IReadOnlyList<ParsedError> errors, string chosen)
    {
        if (!chosen.EndsWith(".java", StringComparison.OrdinalIgnoreCase) || errors.Count == 0) return null;

        var setup = JavaSetup.For(chosen).Setup;
        var built = setup is null ? "the JDK in use" : $"Java {setup.Jdk.VersionText} ({setup.Jdk.FoundIn})";

        foreach (var error in errors)
        {
            var message = error.Message ?? "";
            var where = error.Frames.FirstOrDefault() is { File: { } file, Line: > 0 } frame ? $"{Path.GetFileName(file)}:{frame.Line}" : Path.GetFileName(chosen);

            if (PreviewDisabled().Match(message) is { Success: true } preview)
                return PreviewNote(preview.Groups["feature"].Value, where, built, setup);

            if (NotInTheRelease().Match(message) is { Success: true } notInRelease)
            {
                // javac's own line under the error says which Java the feature came in; failing that, its record of the feature does.
                var needed = HigherNeeded().Match($"{message}\n{error.RawText}") is { Success: true } higher ? higher.Groups["needed"].Value
                    : StandardSince.TryGetValue(notInRelease.Groups["feature"].Value, out var since) ? since.ToString()
                    : null;
                var feature = notInRelease.Groups["feature"].Value;
                return $"{Capitalised(feature)}, which {where} uses, " +
                       (needed is null ? "came in a later Java" : $"came in Java {needed}") +
                       $" - and the program is compiled for Java {notInRelease.Groups["release"].Value}" +
                       (setup?.HowCompiled is { } how ? $" ({how})" : "") +
                       $". Compiling it for {(needed is null ? "a later Java" : $"Java {needed} or later")} builds it; so does writing the code without {feature}.";
            }

            if (WrongClassVersion().Match(error.RawText ?? message) is { Success: true } wrong)
            {
                var builtFor = JavaOfClassFileVersion(int.Parse(wrong.Groups["found"].Value));
                var readsUpTo = JavaOfClassFileVersion(int.Parse(wrong.Groups["expected"].Value));
                var classFile = BadClassFile().Match(error.RawText ?? "") is { Success: true } bad ? bad.Groups["file"].Value : "A class the program uses";
                var newer = Jdks.AtLeast(builtFor);

                return $"{classFile} was built for Java {builtFor} - its class file's version is {wrong.Groups["found"].Value} - and {built} reads " +
                       $"class files only up to Java {readsUpTo}'s. " +
                       (newer is not null
                           ? $"Java {newer.VersionText} ({newer.FoundIn}) is on this computer and can read it: choosing Java {builtFor} in Settings builds the program with it."
                           : $"No JDK of Java {builtFor} or later is on this computer. Install one - {JavaSetup.InstallAdvice(builtFor)}");
            }
        }

        // An applet's classes are not in Java 26 or later: javac then cannot find them, which is not a mistake in the code.
        if (setup is { Jdk.Version: >= 26 } && JavaFeaturesUsed.For(chosen) is { AtMost: { } atMost, AtMostBecause: { } because } &&
            errors.Any(error => AppletMissing().IsMatch(error.Message ?? "")))
        {
            return $"{because} - so javac, of {built}, cannot find the applet's classes. No JDK of Java {atMost} or earlier is on " +
                   "this computer to build it with; the applet has to be written as another kind of program - a Swing JFrame, say - to run on a later Java.";
        }

        // Code written for a later Java than any JDK here: javac's errors about it come from the JDK being older, not from a mistake.
        if (setup is not null && JavaFeaturesUsed.For(chosen) is var used)
        {
            if (used is { AtLeast: { } needed, AtLeastBecause: { } neededBecause } && setup.Jdk.Version < needed && Jdks.AtLeast(needed) is null)
            {
                return $"{neededBecause} - and {built} is the newest JDK on this computer, so javac cannot build that part of it, which is " +
                       $"not a mistake in the code. Installing a JDK of Java {needed} or later builds it - {JavaSetup.InstallAdvice(needed)}";
            }

            if (used.Preview is { } preview && !setup.Preview && !Jdks.Installed.Any(jdk => jdk.Version >= preview.From && jdk.Version <= preview.Until))
            {
                var javas = preview.From == preview.Until ? $"Java {preview.From}" : $"Java {preview.From} to {preview.Until}";
                return $"{preview.Because} - and no JDK of {javas} is on this computer, so javac cannot build that part of it, which is " +
                       $"not a mistake in the code. Installing a JDK of {javas} builds it, with its preview features on - {PreviewInstallAdvice(preview.From, preview.Until)}";
            }
        }

        return null;
    }

    /// <summary>
    /// How to install a JDK that has a preview: the Microsoft Build of OpenJDK 25 or 21 - the releases with long-term
    /// support - when the preview is in one of them, and otherwise Eclipse Temurin's JDK of one of those Javas.
    /// </summary>
    private static string PreviewInstallAdvice(int from, int until)
    {
        foreach (var longTermSupport in new[] { 25, 21 })
        {
            if (longTermSupport >= from && longTermSupport <= until) return $"for example:\n  winget install Microsoft.OpenJDK.{longTermSupport}";
        }

        var javas = from == until ? $"Java {from}" : $"Java {from} to {until}";
        return $"Eclipse Temurin's JDKs are at adoptium.net, and winget lists the ones it can install with:\n  winget search Temurin\n\nOne of {javas} will do.";
    }

    /// <summary>A feature that is only a preview in the JDK the program was built with: which Java made it standard, and whether a JDK of it is here.</summary>
    private static string PreviewNote(string feature, string where, string built, JavaSetup? setup)
    {
        var start = $"{Capitalised(feature)}, which {where} uses, ";

        if (TakenOut.Contains(feature))
            return start + "were a preview in Java 21 and 22 only, and were then taken out of Java, so no Java has them: the code has to " +
                   "build its text another way, such as with + or String.format.";

        if (StillPreview.Contains(feature))
            return start + "are a preview feature in every Java up to 27, so javac builds them only when it is told to with --enable-preview, " +
                   "for the JDK's own Java. A project turns that on in its build - maven-compiler-plugin's enablePreview, or --enable-preview " +
                   "among Gradle's options.compilerArgs - and FixFinder turns it on with it.";

        if (!StandardSince.TryGetValue(feature, out var standard))
            return start + $"is a preview feature in {built}, which javac builds only when told to with --enable-preview.";

        var newer = Jdks.AtLeast(standard);
        return start + $"became part of Java in Java {standard}, and the program was built with {built}, where it is only a preview. " +
               (newer is not null
                   ? $"Java {newer.VersionText} ({newer.FoundIn}) is on this computer and can build it: choosing Java {standard} in Settings builds the program with it."
                   : $"No JDK of Java {standard} or later is on this computer. Install one - {JavaSetup.InstallAdvice(standard)}");
    }

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
