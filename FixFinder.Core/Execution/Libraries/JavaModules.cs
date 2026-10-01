using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// A Java program written as a module - with a module-info.java at the top of its source - which javac compiles as that
/// module, reading only the modules it requires. A build puts those on the module path and everything else on the class
/// path, and compiles the tests patched into the module, reading the class path; here they are given to javac the same way,
/// so a module is neither told it cannot find what it requires nor let off using what it does not.
/// </summary>
/// <remarks>
/// Which jar is which module is read as java reads it: the name its module-info.class declares - in a multi-release jar,
/// perhaps in a versioned entry - or its manifest's Automatic-Module-Name, or else the name java derives from its file's.
/// </remarks>
public static partial class JavaModules
{
    /// <summary>A program's module: its name, the source root its module-info.java is in, and the modules it requires.</summary>
    public sealed record Declared(string Name, string SourceRoot, IReadOnlyList<string> Requires);

    [GeneratedRegex(@"\bmodule\s+(?<name>[A-Za-z_$][\w$]*(?:\s*\.\s*[A-Za-z_$][\w$]*)*)\s*\{")]
    private static partial Regex ModuleHeader();

    [GeneratedRegex(@"\brequires\s+(?:(?:transitive|static)\s+)*(?<name>[A-Za-z_$][\w$]*(?:\s*\.\s*[A-Za-z_$][\w$]*)*)\s*;")]
    private static partial Regex RequiresLine();

    [GeneratedRegex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comment();

    /// <summary>The part of a jar's file name java takes as its version: from the first hyphen followed by a digit.</summary>
    [GeneratedRegex(@"-(\d+(\.|$))")]
    private static partial Regex VersionInName();

    [GeneratedRegex(@"[^A-Za-z0-9]")]
    private static partial Regex NotAlphanumeric();

    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex RepeatedDots();

    /// <summary>The module a source root declares, or null when there is no module-info.java at its top.</summary>
    public static Declared? In(string sourceRoot)
    {
        var declaration = Path.Combine(sourceRoot, "module-info.java");

        string text;
        try
        {
            if (!File.Exists(declaration)) return null;
            text = Comment().Replace(File.ReadAllText(declaration), " ");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (ModuleHeader().Match(text) is not { Success: true } header) return null;

        return new Declared(
            Squeezed(header.Groups["name"].Value),
            Path.GetFullPath(sourceRoot),
            [.. RequiresLine().Matches(text).Select(line => Squeezed(line.Groups["name"].Value)).Distinct(StringComparer.Ordinal)]);
    }

    private static string Squeezed(string name) => Regex.Replace(name, @"\s+", "");

    /// <summary>
    /// The arguments that give javac a file's source and libraries. For a program that is not a module: its libraries on the
    /// class path and its source roots on the source path, as ever. For one that is: the jars of the modules it requires,
    /// and of those they require in turn, on the module path, and the rest on the class path; and, when something besides
    /// the module's own source is compiled with it - its tests, FixFinder's launcher, a copy made to try a change in - that is
    /// patched into the module, which then reads the class path, as a build compiles a module's tests.
    /// </summary>
    /// <param name="ownRoot">The source root of the file compiled.</param>
    /// <param name="otherRoots">The program's other source roots.</param>
    /// <param name="alsoCompiled">Folders of what is compiled with it that is none of the program's own source.</param>
    public static IReadOnlyList<string> SourceAndLibraries(
        string ownRoot, IReadOnlyList<string> otherRoots, IReadOnlyList<string> classPath, IReadOnlyList<string> alsoCompiled)
    {
        var arguments = new List<string>();
        var module = For(ownRoot, otherRoots);

        if (module is null)
        {
            if (classPath.Count > 0) arguments.AddRange(["-cp", Joined(classPath)]);
            arguments.AddRange(["-sourcepath", Joined([ownRoot, .. otherRoots])]);
            return arguments;
        }

        var inTheModule = SameFolder(module.SourceRoot, ownRoot);
        var modulePath = ModulePath(module, classPath);
        var rest = classPath.Where(jar => !modulePath.Contains(jar, StringComparer.OrdinalIgnoreCase)).ToList();

        // A test outside the module's own source is compiled as a build compiles it: patched into the module.
        var patched = new List<string>();
        if (!inTheModule) patched.Add(ownRoot);
        patched.AddRange(alsoCompiled);

        if (modulePath.Count > 0) arguments.AddRange(["--module-path", Joined(modulePath)]);
        if (rest.Count > 0) arguments.AddRange(["-cp", Joined(rest)]);
        if (patched.Count > 0) arguments.AddRange(["--patch-module", $"{module.Name}={Joined(patched)}", "--add-reads", $"{module.Name}=ALL-UNNAMED"]);
        arguments.AddRange(["-sourcepath", Joined(inTheModule ? [ownRoot, .. otherRoots] : otherRoots)]);

        return arguments;
    }

    /// <summary>The module a program's file belongs to, or is compiled into as a test is; null for a program that is not one.</summary>
    public static Declared? For(string ownRoot, IReadOnlyList<string> otherRoots) =>
        In(ownRoot) ?? otherRoots.Select(In).OfType<Declared>().FirstOrDefault();

    /// <summary>
    /// The jars to put on the module path: the modules the program requires, and those they require in turn, as java
    /// resolves them. A module required that no jar is - java.sql, say - is the JDK's own.
    /// </summary>
    public static IReadOnlyList<string> ModulePath(Declared module, IReadOnlyList<string> jars)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var jar in jars)
        {
            if (NameOf(jar) is { } name) byName.TryAdd(name, jar);
        }

        var chosen = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var wanted = new Queue<string>(module.Requires);

        while (wanted.TryDequeue(out var name))
        {
            if (!seen.Add(name) || !byName.TryGetValue(name, out var jar)) continue;

            chosen.Add(jar);
            foreach (var required in DescriptorOf(jar)?.Requires ?? []) wanted.Enqueue(required);
        }

        return chosen;
    }

    /// <summary>The name of the module a jar is: the one its module-info.class declares, its Automatic-Module-Name, or the one java derives.</summary>
    public static string? NameOf(string jar)
    {
        if (DescriptorOf(jar) is { } descriptor) return descriptor.Name;
        if (ManifestValue(jar, "Automatic-Module-Name") is { Length: > 0 } named) return named;

        var name = Path.GetFileName(jar);
        if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (VersionInName().Match(name) is { Success: true } version) name = name[..version.Index];

        var derived = RepeatedDots().Replace(NotAlphanumeric().Replace(name, "."), ".").Trim('.');
        return derived.Length > 0 ? derived : null;
    }

    /// <summary>What a modular jar's module-info.class declares: its name and the modules it requires.</summary>
    private sealed record Descriptor(string Name, IReadOnlyList<string> Requires);

    private static Descriptor? DescriptorOf(string jar)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jar);

            // At the top, or in a multi-release jar's versioned entries.
            var entry = archive.GetEntry("module-info.class") ??
                        archive.Entries
                            .Where(each => each.FullName.StartsWith("META-INF/versions/", StringComparison.Ordinal) &&
                                           each.FullName.EndsWith("/module-info.class", StringComparison.Ordinal))
                            .OrderByDescending(each => each.FullName, StringComparer.Ordinal)
                            .FirstOrDefault();

            if (entry is null) return null;

            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);

            return ModuleAttributeOf(bytes.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or
                                       IndexOutOfRangeException or ArgumentException)
        {
            // A jar that cannot be read, or a module-info.class cut short, says nothing of which module it is.
            return null;
        }
    }

    private static string? ManifestValue(string jar, string attribute)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jar);
            if (archive.GetEntry("META-INF/MANIFEST.MF") is not { } manifest) return null;

            using var reader = new StreamReader(manifest.Open(), Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith(attribute + ":", StringComparison.OrdinalIgnoreCase)) return line[(attribute.Length + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
        }

        return null;
    }

    /// <summary>
    /// The Module attribute of a module-info.class, as the class file format lays it out: the constant pool, then the class's
    /// flags, interfaces, fields and methods - a module-info has none - then its attributes. Module names are written with
    /// their dots, not as class names are.
    /// </summary>
    private static Descriptor? ModuleAttributeOf(byte[] classFile)
    {
        var reader = new ClassFileReader(classFile);
        if (reader.U4() != 0xCAFEBABE) return null;

        reader.Skip(4);
        var poolSize = reader.U2();
        var texts = new string?[poolSize];
        var references = new int[poolSize];

        for (var index = 1; index < poolSize; index++)
        {
            switch (reader.U1())
            {
                case 1:
                    texts[index] = reader.Utf8(reader.U2());
                    break;
                case 7: case 8: case 16: case 19: case 20:
                    references[index] = reader.U2();
                    break;
                case 3: case 4: case 9: case 10: case 11: case 12: case 17: case 18:
                    reader.Skip(4);
                    break;
                case 5: case 6:
                    reader.Skip(8);
                    index++;
                    break;
                case 15:
                    reader.Skip(3);
                    break;
                default:
                    return null;
            }
        }

        string? ModuleNamed(int index) =>
            index > 0 && index < poolSize && references[index] > 0 && references[index] < poolSize ? texts[references[index]] : null;

        reader.Skip(6);
        reader.Skip(2 * reader.U2());

        for (var kind = 0; kind < 2; kind++)
        {
            var members = reader.U2();
            for (var member = 0; member < members; member++)
            {
                reader.Skip(6);
                SkipAttributes(reader);
            }
        }

        var attributes = reader.U2();
        for (var attribute = 0; attribute < attributes; attribute++)
        {
            var name = texts[reader.U2()];
            var length = reader.U4();

            if (name != "Module")
            {
                reader.Skip((int)length);
                continue;
            }

            var moduleName = ModuleNamed(reader.U2());
            reader.Skip(4);

            var requires = new List<string>();
            var count = reader.U2();
            for (var required = 0; required < count; required++)
            {
                if (ModuleNamed(reader.U2()) is { } requiredName) requires.Add(requiredName);
                reader.Skip(4);
            }

            return moduleName is null ? null : new Descriptor(moduleName, requires);
        }

        return null;
    }

    private static void SkipAttributes(ClassFileReader reader)
    {
        var attributes = reader.U2();
        for (var attribute = 0; attribute < attributes; attribute++)
        {
            reader.Skip(2);
            reader.Skip((int)reader.U4());
        }
    }

    /// <summary>Reads a class file's big-endian numbers and its modified UTF-8, which for names is UTF-8.</summary>
    private sealed class ClassFileReader(byte[] bytes)
    {
        private int _at;

        public int U1() => bytes[_at++];

        public int U2()
        {
            var value = (bytes[_at] << 8) | bytes[_at + 1];
            _at += 2;
            return value;
        }

        public uint U4()
        {
            var value = ((uint)bytes[_at] << 24) | ((uint)bytes[_at + 1] << 16) | ((uint)bytes[_at + 2] << 8) | bytes[_at + 3];
            _at += 4;
            return value;
        }

        public string Utf8(int length)
        {
            var text = Encoding.UTF8.GetString(bytes, _at, length);
            _at += length;
            return text;
        }

        public void Skip(int count) => _at += count;
    }

    private static string Joined(IEnumerable<string> paths) => string.Join(Path.PathSeparator, paths);

    private static bool SameFolder(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
