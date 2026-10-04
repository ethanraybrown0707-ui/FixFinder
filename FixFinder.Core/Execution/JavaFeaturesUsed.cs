using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Java a program's own code needs, read from how it is written, so it is built with a JDK that can build it and run
/// with a java that can start it: the newest part of the language or of Java's library it uses, from Java 9 to 26; a
/// feature that is still only a preview, which needs a JDK that has it and preview features turned on; and an applet,
/// which needs a Java no later than 25, as Java 26 took the Applet API out.
/// </summary>
/// <remarks>
/// Which Java made each feature final, and which Javas have each preview, is from the JDK's own records: the JEPs of each
/// release at openjdk.org, and javac 21 and 25 compiling each of them. Only what is certainly the JDK's own is counted - a
/// class of the program's own named IO, say, is not Java 25's IO - and only forms that cannot be anything else, so the
/// Java said to be needed is never more than the code needs. Code can still need more than is found here: javac names
/// what is missing when it is built.
/// <para>
/// What a later Java only stopped forbidding is not counted either: a statement before super(...), or a main(String[] args)
/// without static, is what Java 25 allows and what every Java before it reports as a mistake, so finding one says nothing
/// of which Java the code is written for - and choosing Java 25 for it would hide the mistake from a course of an earlier one.
/// </para>
/// </remarks>
/// <param name="AtLeast">The Java the code needs at least, from the newest feature found.</param>
/// <param name="AtLeastBecause">Which feature, where, and which Java made it final, as part of a sentence.</param>
/// <param name="AtMost">Java 25, for an applet - the last Java with the Applet API.</param>
/// <param name="AtMostBecause">Which file is the applet, as part of a sentence.</param>
/// <param name="Preview">A preview feature the code uses, and the Javas that have it.</param>
public sealed partial record JavaFeaturesUsed(int? AtLeast, string? AtLeastBecause, int? AtMost, string? AtMostBecause, JavaFeaturesUsed.PreviewUse? Preview = null)
{
    public static readonly JavaFeaturesUsed None = new(null, null, null, null);

    /// <summary>A preview feature the code uses: the Javas that have it as a preview, from <paramref name="From"/> to <paramref name="Until"/>.</summary>
    public sealed record PreviewUse(int From, int Until, string Because);

    /// <summary>
    /// Something the code can be written with that a Java made final: <paramref name="Uses"/> says it after the file's name,
    /// <paramref name="Library"/> says whether Java added it to its library rather than its language, and
    /// <paramref name="DeclaredAs"/> names the type it belongs to, which the program may declare as one of its own.
    /// </summary>
    private sealed record Final(int Since, string Uses, Regex Written, bool Library = false, string? DeclaredAs = null);

    /// <summary>Something the code can be written with that is only a preview, in the Javas from <paramref name="From"/> to <paramref name="Until"/>.</summary>
    private sealed record Previewed(int From, int Until, string Uses, Regex Written, string? DeclaredAs = null);

    private const RegexOptions Compiled = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private const string PrimitiveType = @"(?:byte|short|char|int|long|float|double|boolean)";

    /// <summary>The parts of the language and of its library each Java made final, newest first, so the first one found is the one that decides.</summary>
    private static readonly Final[] Finals =
    [
        new(26, "uses HTTP/3 in the HTTP client", new(@"\bVersion\s*\.\s*HTTP_3\b|\bHttp3DiscoveryMode\b|\bHttpOption\s*\.\s*H3_DISCOVERY\b", Compiled), Library: true),
        new(25, "uses java.lang.IO", new(@"\bIO\s*\.\s*(?:println|print|readln)\s*\(", Compiled), Library: true, DeclaredAs: "IO"),
        new(25, "uses ScopedValue", new(@"\bScopedValue\b", Compiled), Library: true, DeclaredAs: "ScopedValue"),
        new(25, "uses the key derivation function API, KDF", new(@"\bjavax\s*\.\s*crypto\s*\.\s*KDF\b|\bKDF\s*\.\s*getInstance\s*\(", Compiled), Library: true, DeclaredAs: "KDF"),
        new(24, "uses stream gatherers", new(@"\bGatherers\s*\.\s*\w+\s*\(|\bjava\s*\.\s*util\s*\.\s*stream\s*\.\s*Gatherers?\b|\bGatherer\s*\.\s*of(?:Sequential)?\s*\(", Compiled), Library: true, DeclaredAs: "Gatherers"),
        new(24, "uses the class-file API", new(@"\bjava\s*\.\s*lang\s*\.\s*classfile\b", Compiled), Library: true),
        new(22, "uses the foreign function and memory API", new(@"\bjava\s*\.\s*lang\s*\.\s*foreign\b|\bArena\s*\.\s*(?:ofConfined|ofShared|ofAuto|global)\s*\(|\bLinker\s*\.\s*nativeLinker\s*\(", Compiled), Library: true, DeclaredAs: "Arena"),
        new(21, "uses a record pattern", new(@"\b(?:instanceof|case)\s+(?:final\s+)?[A-Z][\w$.]*(?:\s*<[^<>;{}]*>)?\s*\(", Compiled)),
        new(21, "uses a type pattern in a switch", new($@"\bcase\s+(?:final\s+)?(?!{PrimitiveType}\b)[A-Z][\w$.]*(?:\s*<[^<>;{{}}]*>)?(?:\s*\[\s*\])*\s+[A-Za-z_$][\w$]*\s*(?:->|:|\bwhen\b)", Compiled)),
        new(21, "uses case null", new(@"\bcase\s+null\b", Compiled)),
        new(21, "uses virtual threads", new(@"\bThread\s*\.\s*(?:ofVirtual|ofPlatform|startVirtualThread)\s*\(|\bExecutors\s*\.\s*newVirtualThreadPerTaskExecutor\s*\(", Compiled), Library: true),
        new(21, "uses a sequenced collection", new(@"\bSequenced(?:Collection|Map|Set)\b", Compiled), Library: true, DeclaredAs: "SequencedCollection"),
        new(21, "uses Math.clamp", new(@"\bMath\s*\.\s*clamp\s*\(", Compiled), Library: true),
        new(21, "uses String.splitWithDelimiters", new(@"\.\s*splitWithDelimiters\s*\(", Compiled), Library: true),
        new(21, "uses the key encapsulation mechanism API, KEM", new(@"\bjavax\s*\.\s*crypto\s*\.\s*KEM\b|\bKEM\s*\.\s*getInstance\s*\(", Compiled), Library: true, DeclaredAs: "KEM"),
        new(19, "uses HashMap.newHashMap or another such factory", new(@"\b(?:HashMap|HashSet|LinkedHashMap|LinkedHashSet|WeakHashMap)\s*\.\s*new(?:HashMap|HashSet|LinkedHashMap|LinkedHashSet|WeakHashMap)\s*\(", Compiled), Library: true),
        new(17, "declares a sealed class or interface", new(@"(?<![\w$.\-])(?:sealed|non-sealed)\s+(?:(?:public|protected|private|abstract|static|final|strictfp)\s+)*(?:class|interface)\b|\b(?:class|interface)\s+[A-Za-z_$][\w$]*[^{;]*\bpermits\s+[A-Za-z_$]", Compiled)),
        new(17, "uses RandomGenerator or HexFormat", new(@"\bjava\s*\.\s*util\s*\.\s*random\b|\bRandomGenerator\s*\.\s*(?:of|getDefault)\s*\(|\bHexFormat\s*\.\s*of\w*\s*\(", Compiled), Library: true),
        new(16, "declares a record", new(@"(?<![\w$.])record\s+[A-Za-z_$][\w$]*\s*(?:<[^<>;{}]*>)?\s*\(", Compiled)),
        new(16, "uses a pattern in instanceof", new($@"\binstanceof\s+(?:final\s+)?(?!{PrimitiveType}\b)[A-Za-z_$][\w$.]*(?:\s*<[^<>;{{}}]*>)?(?:\s*\[\s*\])*\s+(?!instanceof\b)[A-Za-z_$][\w$]*", Compiled)),
        new(16, "uses Stream.toList or Stream.mapMulti", new(@"\.\s*(?:stream|parallelStream)\s*\(\s*\)[^;]*?\.\s*toList\s*\(\s*\)|\bStream\s*\.\s*(?:of|iterate|generate|concat)\s*\([^;]*?\.\s*toList\s*\(\s*\)|\.\s*mapMulti\s*\(", Compiled), Library: true),
        new(15, "uses a text block", new("\"\"\"", Compiled)),
        new(15, "uses String.formatted, stripIndent or translateEscapes", new(@"""\s*\.\s*formatted\s*\(|\.\s*(?:stripIndent|translateEscapes)\s*\(\s*\)", Compiled), Library: true),
        new(14, "uses a switch with case ... ->", new(@"\bcase\b[^:;{}]*?->|\bdefault\s*->", Compiled)),
        new(12, "uses Collectors.teeing or Files.mismatch", new(@"\bCollectors\s*\.\s*teeing\s*\(|\bFiles\s*\.\s*mismatch\s*\(", Compiled), Library: true),
        new(11, "uses var in a lambda's parameters", new(@"\(\s*(?:final\s+)?var\s+[A-Za-z_$][\w$]*\s*(?:,\s*(?:final\s+)?var\s+[A-Za-z_$][\w$]*\s*)*\)\s*->", Compiled)),
        new(11, "uses the HTTP client, java.net.http", new(@"\bjava\s*\.\s*net\s*\.\s*http\b|\bHttpClient\s*\.\s*new(?:HttpClient|Builder)\s*\(", Compiled), Library: true, DeclaredAs: "HttpClient"),
        new(11, "uses Files.readString, Files.writeString, Path.of or Predicate.not", new(@"\bFiles\s*\.\s*(?:readString|writeString)\s*\(|\bPath\s*\.\s*of\s*\(|\bPredicate\s*\.\s*not\s*\(", Compiled), Library: true, DeclaredAs: "Path"),
        new(11, "uses String.strip, isBlank or repeat", new(@"\.\s*(?:isBlank|strip|stripLeading|stripTrailing)\s*\(\s*\)|""\s*\.\s*repeat\s*\(", Compiled), Library: true),
        new(10, "declares a local variable with var", new(@"(?<![\w$.])var\s+[A-Za-z_$][\w$]*\s*(?:=|:)", Compiled)),
        new(10, "uses copyOf, toUnmodifiableList or Optional.orElseThrow()", new(@"\b(?:List|Set|Map)\s*\.\s*copyOf\s*\(|\bCollectors\s*\.\s*toUnmodifiable(?:List|Set|Map)\s*\(|\.\s*orElseThrow\s*\(\s*\)", Compiled), Library: true),
        new(9, "uses List.of, Set.of or Map.of", new(@"\b(?:List|Set)\s*\.\s*of\s*\(|\bMap\s*\.\s*(?:of|ofEntries|entry)\s*\(", Compiled), Library: true, DeclaredAs: "List"),
        new(9, "uses a resource declared before try (...)", new(@"\btry\s*\(\s*(?:this\s*\.\s*)?[A-Za-z_$][\w$]*\s*\)", Compiled)),
        new(9, "uses <> with an anonymous class", new(@"\bnew\s+[A-Za-z_$][\w$.]*\s*<\s*>\s*\([^;{}]*\)\s*\{", Compiled)),
    ];

    /// <summary>The previews of Java 21 to 27 the code can be written with, and the Javas that have each; a preview javac takes only from a JDK of that Java, with preview features on.</summary>
    private static readonly Previewed[] Previews =
    [
        new(23, 27, "uses a primitive type in a pattern", new($@"\binstanceof\s+(?:final\s+)?{PrimitiveType}\b(?!\s*\[)|\bcase\s+(?:final\s+)?{PrimitiveType}\s+[A-Za-z_$]", Compiled)),
        new(27, 27, "uses Set.ofLazy", new(@"\bSet\s*\.\s*ofLazy\s*\(", Compiled)),
        new(26, 27, "uses lazy constants", new(@"\bLazyConstant\b|\b(?:List|Map)\s*\.\s*ofLazy\s*\(", Compiled), DeclaredAs: "LazyConstant"),
        new(25, 25, "uses StableValue", new(@"\bStableValue\b", Compiled), DeclaredAs: "StableValue"),
        new(25, 27, "uses StructuredTaskScope.open", new(@"\bStructuredTaskScope\s*\.\s*(?:open\s*\(|Joiner\b)", Compiled), DeclaredAs: "StructuredTaskScope"),
        new(21, 24, "uses StructuredTaskScope.ShutdownOnFailure or ShutdownOnSuccess", new(@"\bStructuredTaskScope\s*\.\s*ShutdownOn(?:Failure|Success)\b", Compiled), DeclaredAs: "StructuredTaskScope"),
        new(21, 22, "uses a string template", new(@"\b(?:STR|FMT|RAW)\s*\.\s*""", Compiled)),
    ];

    [GeneratedRegex(@"(?m)^\s*import\s+module\s+[\w.]+\s*;")]
    private static partial Regex ModuleImport();

    [GeneratedRegex(@"(?<![\w$])_(?![\w$])")]
    private static partial Regex Unnamed();

    [GeneratedRegex(@"\bjava\s*\.\s*applet\b|\bjavax\s*\.\s*swing\s*\.\s*JApplet\b|\bextends\s+(?:J?Applet)\b")]
    private static partial Regex Applet();

    [GeneratedRegex(@"(?:^|[\s;}])(?:class|interface|enum|record)\s+[A-Za-z_$]|@interface\b")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"^\s*(?:@[\w.]+(?:\s*\([^)]*\))?\s*)*(?:package|import|(?:open\s+)?module)\b")]
    private static partial Regex PackageOrImport();

    /// <summary>
    /// A main with no parameters that is not static - void main() - as Java 25 writes one. A main(String[] args) that is
    /// not static is not counted: before Java 25 that is the mistake of leaving out static, and it is checked as one.
    /// </summary>
    [GeneratedRegex(@"(?<!\bstatic\b[^;{}()=]*)\bvoid\s+main\s*\(\s*\)")]
    private static partial Regex InstanceMain();

    [GeneratedRegex(@"\bstatic\b[^;{}()=]*\bvoid\s+main\s*\(\s*String")]
    private static partial Regex StaticMain();

    [GeneratedRegex(@"(?:\b(?:class|interface|enum|record)|@interface)\s+(?<name>[A-Za-z_$][\w$]*)")]
    private static partial Regex DeclaredType();

    [GeneratedRegex(@"(?m)^\s*import\s+(?!static\b)(?<package>[\w.]+)\.(?<name>[A-Za-z_$][\w$]*)\s*;")]
    private static partial Regex ImportedType();

    private static readonly ConcurrentDictionary<string, (DateTime Written, Read File)> Remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One file as read for what it needs: its code with comments and the text in quotes blanked out, and what that declares and imports.</summary>
    private sealed record Read(string Name, string Code, IReadOnlySet<string> Declares, IReadOnlyDictionary<string, string> Imports, bool IsPackageOrModuleInfo);

    /// <summary>What the program this Java file is part of needs: the file and the others of its source folder.</summary>
    public static JavaFeaturesUsed For(string javaFile)
    {
        var chosen = Path.GetFullPath(javaFile);
        var root = ProgramLayout.JavaSourceRoot(chosen);

        IEnumerable<string> others;
        try
        {
            others = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories).Take(500) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            others = [];
        }

        return Of(others.Prepend(chosen).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>What these files need together: the newest Java any of them needs, the preview any uses, and whether any is an applet.</summary>
    public static JavaFeaturesUsed Of(IEnumerable<string> javaFiles)
    {
        var files = javaFiles.Select(ReadFile).OfType<Read>().ToList();
        if (files.Count == 0) return None;

        // A type the program declares itself is the program's, whatever Java's library has of that name.
        var declared = files.SelectMany(file => file.Declares).ToHashSet(StringComparer.Ordinal);
        var hasStaticMain = files.Any(file => StaticMain().IsMatch(file.Code));

        bool IsJavas(Read file, string? typeName) =>
            typeName is null ||
            (!declared.Contains(typeName) && (!file.Imports.TryGetValue(typeName, out var package) || package.StartsWith("java.", StringComparison.Ordinal) || package.StartsWith("javax.", StringComparison.Ordinal)));

        var needs = new List<(int Since, string Because)>();

        foreach (var file in files)
        {
            if (!file.IsPackageOrModuleInfo && IsCompactSource(file.Code))
                needs.Add((25, $"{file.Name} is a compact source file - methods with no class around them - which Java 25 made part of the language"));

            if (ModuleImport().Match(file.Code) is { Success: true } moduleImport)
                needs.Add((25, $"{file.Name} uses import module at line {LineOf(file.Code, moduleImport.Index)}, which Java 25 made part of the language"));

            if (!hasStaticMain && !file.IsPackageOrModuleInfo && InstanceMain().Match(file.Code) is { Success: true } instanceMain)
                needs.Add((25, $"{file.Name} has a main method that is not static at line {LineOf(file.Code, instanceMain.Index)}, which Java 25 made a program's start"));

            if (Unnamed().Match(file.Code) is { Success: true } unnamed)
                needs.Add((22, $"{file.Name} names a variable _ at line {LineOf(file.Code, unnamed.Index)}, which Java 22 made part of the language"));

            foreach (var feature in Finals)
            {
                if (needs.Any(need => need.Since >= feature.Since) || !IsJavas(file, feature.DeclaredAs)) continue;
                if (feature.Written.Match(file.Code) is not { Success: true } found) continue;

                needs.Add((feature.Since,
                    $"{file.Name} {feature.Uses} at line {LineOf(file.Code, found.Index)}, which Java {feature.Since} {(feature.Library ? "added" : "made part of the language")}"));
            }
        }

        var newest = needs.Count == 0 ? ((int?)null, (string?)null) : needs.OrderByDescending(need => need.Since).Select(need => ((int?)need.Since, (string?)need.Because)).First();
        var applet = files.FirstOrDefault(file => Applet().IsMatch(file.Code));

        return new JavaFeaturesUsed(
            newest.Item1, newest.Item2,
            applet is null ? null : 25,
            applet is null ? null : $"{applet.Name} is an applet, and Java 26 took the Applet API out of Java",
            PreviewOf(files, IsJavas));
    }

    /// <summary>
    /// The preview the code uses, with the Javas that have it - or, using several, the Javas that have them all. Javas
    /// that have none of them together leave the first one found, and javac then says which it cannot take.
    /// </summary>
    private static PreviewUse? PreviewOf(List<Read> files, Func<Read, string?, bool> isJavas)
    {
        PreviewUse? together = null;

        foreach (var file in files)
        {
            foreach (var preview in Previews)
            {
                if (!isJavas(file, preview.DeclaredAs) || preview.Written.Match(file.Code) is not { Success: true } found) continue;

                var which = preview.From == preview.Until ? $"Java {preview.From} has" : $"Java {preview.From} to {preview.Until} have";
                var use = new PreviewUse(preview.From, preview.Until, $"{file.Name} {preview.Uses} at line {LineOf(file.Code, found.Index)}, which {which} as a preview feature");

                if (together is null) together = use;
                else if (Math.Max(together.From, use.From) <= Math.Min(together.Until, use.Until))
                    together = together with { From = Math.Max(together.From, use.From), Until = Math.Min(together.Until, use.Until) };
            }
        }

        return together;
    }

    private static Read? ReadFile(string file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file);
            if (Remembered.TryGetValue(file, out var known) && known.Written == written) return known.File;

            var code = Masked(File.ReadAllText(file));

            var declares = DeclaredType().Matches(code).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
            var imports = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match imported in ImportedType().Matches(code)) imports[imported.Groups["name"].Value] = imported.Groups["package"].Value;

            // A module's module-info.java and a package's package-info.java declare no class, and are not programs.
            var read = new Read(Path.GetFileName(file), code, declares, imports, Path.GetFileName(file) is "module-info.java" or "package-info.java");

            Remembered[file] = (written, read);
            return read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int LineOf(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    /// <summary>
    /// The code with its comments, and the text inside its quotes - "...", '...' and text blocks - blanked out, the quotes
    /// themselves and every line kept, so only what is written as code is matched, at the line it is on.
    /// </summary>
    internal static string Masked(string text)
    {
        var masked = new StringBuilder(text.Length);
        var at = 0;

        void Blank(int until)
        {
            for (; at < until && at < text.Length; at++) masked.Append(text[at] is '\n' or '\r' ? text[at] : ' ');
        }

        while (at < text.Length)
        {
            var character = text[at];
            var next = at + 1 < text.Length ? text[at + 1] : '\0';

            if (character == '/' && next == '/')
            {
                var lineEnd = text.IndexOf('\n', at);
                Blank(lineEnd < 0 ? text.Length : lineEnd);
            }
            else if (character == '/' && next == '*')
            {
                var close = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                Blank(close < 0 ? text.Length : close + 2);
            }
            else if (character == '"' && string.CompareOrdinal(text, at, "\"\"\"", 0, 3) == 0)
            {
                masked.Append("\"\"\"");
                at += 3;
                Blank(ClosingOf(text, at, "\"\"\""));
                if (at < text.Length)
                {
                    masked.Append("\"\"\"");
                    at += 3;
                }
            }
            else if (character is '"' or '\'')
            {
                masked.Append(character);
                at++;
                Blank(ClosingOf(text, at, character.ToString()));
                if (at < text.Length)
                {
                    masked.Append(text[at]);
                    at++;
                }
            }
            else
            {
                masked.Append(character);
                at++;
            }
        }

        return masked.ToString();
    }

    /// <summary>Where the quote opened before <paramref name="from"/> closes, past any escaped one - or the end of a line, for "..." left open.</summary>
    private static int ClosingOf(string text, int from, string quote)
    {
        for (var at = from; at < text.Length; at++)
        {
            if (text[at] == '\\')
            {
                at++;
                continue;
            }

            if (quote.Length == 1 && text[at] == '\n') return at;
            if (string.CompareOrdinal(text, at, quote, 0, quote.Length) == 0) return at;
        }

        return text.Length;
    }

    /// <summary>
    /// Whether the file is a compact source file: something at its top level - outside every brace - that is neither its
    /// package, an import, nor a class, interface, enum or record. A method or a field written there is one.
    /// </summary>
    internal static bool IsCompactSource(string masked)
    {
        var depth = 0;
        var unit = new StringBuilder();

        bool IsMember(string written)
        {
            var trimmed = written.Trim();
            return trimmed.Length > 0 && !PackageOrImport().IsMatch(trimmed) && !TypeDeclaration().IsMatch(" " + trimmed);
        }

        foreach (var character in masked)
        {
            if (depth == 0)
            {
                if (character == ';')
                {
                    if (IsMember(unit.ToString())) return true;
                    unit.Clear();
                    continue;
                }

                if (character == '{')
                {
                    if (IsMember(unit.ToString())) return true;
                    unit.Clear();
                    depth++;
                    continue;
                }

                unit.Append(character);
                continue;
            }

            if (character == '{') depth++;
            else if (character == '}' && --depth == 0) unit.Clear();
        }

        return false;
    }
}
