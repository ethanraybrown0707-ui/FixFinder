using System.Reflection.Metadata;

namespace FixFinder.Core.Patching;

/// <summary>Reads the source file paths recorded in a .NET portable PDB.</summary>
public static class PortablePdbReader
{
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
            return [];
        }
    }

    public static string? FindCommonRoot(string assemblyPath)
    {
        var paths = ReadDocumentPaths(assemblyPath)
            .Where(p => !p.Contains("<", StringComparison.Ordinal))
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

        if (shared < 2) return null;

        var root = string.Join(Path.DirectorySeparatorChar, segments[0].Take(shared));
        return Directory.Exists(root) ? root : null;
    }
}
