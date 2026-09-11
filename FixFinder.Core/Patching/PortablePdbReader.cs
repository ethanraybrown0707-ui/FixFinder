using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace FixFinder.Core.Patching;

/// <summary>
/// Reads the source file paths recorded in a .NET portable PDB.
/// </summary>
/// <remarks>
/// This is how FixFinder finds the source tree for a compiled .NET target when the crash itself
/// gives nothing away - a release build with no file paths in its stack trace, for instance.
/// The PDB records the absolute path of every file that went into the assembly, so the longest
/// directory prefix they share is the project root, or very close to it.
/// <para>
/// <c>System.Reflection.Metadata</c> is in the shared framework, so this needs no package.
/// </para>
/// </remarks>
public static class PortablePdbReader
{
    /// <summary>
    /// Returns every document path recorded in the PDB beside <paramref name="assemblyPath"/>,
    /// or an empty list when there is no PDB or it cannot be read.
    /// </summary>
    /// <remarks>
    /// Never throws. A PDB can be absent, be the older Windows format, be truncated, or belong
    /// to a different build - all of which are ordinary situations, none of which should stop a
    /// run. The caller simply falls back to another detection strategy.
    /// </remarks>
    public static IReadOnlyList<string> ReadDocumentPaths(string assemblyPath)
    {
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(pdbPath)) return [];

        try
        {
            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();

            var paths = new List<string>();
            foreach (var handle in reader.Documents)
            {
                if (handle.IsNil) continue;

                var document = reader.GetDocument(handle);
                if (document.Name.IsNil) continue;

                var name = reader.GetString(document.Name);
                if (!string.IsNullOrWhiteSpace(name)) paths.Add(name);
            }

            return paths;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException
                                       or UnauthorizedAccessException or InvalidOperationException)
        {
            // Not a portable PDB, unreadable, or locked. All ordinary; fall back elsewhere.
            return [];
        }
    }

    /// <summary>
    /// The longest directory prefix shared by every recorded document, or null.
    /// </summary>
    /// <remarks>
    /// Deliberately requires more than one document before trusting the answer, and refuses a
    /// prefix that is merely a drive root. A single-file assembly would otherwise "resolve" its
    /// source root to whatever folder that one file sits in, which is usually too deep.
    /// </remarks>
    public static string? FindCommonRoot(string assemblyPath)
    {
        var paths = ReadDocumentPaths(assemblyPath)
            .Where(p => !p.Contains("<", StringComparison.Ordinal))   // generated documents
            .Select(p => p.Replace('/', Path.DirectorySeparatorChar))
            .Where(Path.IsPathRooted)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length < 2) return null;

        var segments = paths
            .Select(p => Path.GetDirectoryName(p)?.Split(Path.DirectorySeparatorChar) ?? [])
            .ToArray();

        var shortest = segments.Min(s => s.Length);
        var shared = 0;

        while (shared < shortest &&
               segments.All(s => string.Equals(s[shared], segments[0][shared], StringComparison.OrdinalIgnoreCase)))
        {
            shared++;
        }

        // "C:" alone is not a source root.
        if (shared < 2) return null;

        var root = string.Join(Path.DirectorySeparatorChar, segments[0].Take(shared));
        return Directory.Exists(root) ? root : null;
    }
}
