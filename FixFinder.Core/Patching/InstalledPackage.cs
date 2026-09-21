using FixFinder.Core.Parsing;

namespace FixFinder.Core.Patching;

/// <summary>An installed dependency that a crash went through, and where it lives on disk.</summary>
public sealed record InstalledPackage(string Name, string Root)
{
    public string Ecosystem { get; init; } = "";

    public string? Caveat { get; init; }
}

/// <summary>Finds the folder an installed dependency occupies, when a crash came through one.</summary>
public static class InstalledPackages
{
    private static readonly string[] PackageContainers =
    [
        "site-packages", "dist-packages", "node_modules", "bower_components", "gems",
    ];

    public static InstalledPackage? From(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (For(file) is { } found) return found;
        }

        return null;
    }

    public static InstalledPackage? For(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        string full;

        try
        {
            full = Path.GetFullPath(file);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return GoVendor(full) ?? GoModuleCache(full) ?? CargoRegistry(full) ?? Container(full);
    }

    private static InstalledPackage? Container(string full)
    {
        var directory = Path.GetDirectoryName(full);

        string? scoped = null;

        while (directory is { Length: > 0 })
        {
            var parent = Path.GetDirectoryName(directory);
            if (parent is null) return null;

            var parentName = Path.GetFileName(parent);

            if (PackageContainers.Contains(parentName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);

                if (name.Length == 0) return null;

                if (name.StartsWith('@'))
                {
                    return scoped is null
                        ? null
                        : new InstalledPackage($"{name}/{Path.GetFileName(scoped)}", scoped)
                        {
                            Ecosystem = parentName,
                        };
                }

                return new InstalledPackage(name, directory) { Ecosystem = parentName };
            }

            scoped = directory;
            directory = parent;
        }

        return null;
    }

    private static InstalledPackage? GoVendor(string full)
    {
        var segments = Split(full);

        var vendor = Array.FindLastIndex(segments, s => s.Equals("vendor", StringComparison.OrdinalIgnoreCase));
        if (vendor < 0 || vendor + 1 >= segments.Length) return null;

        var vendorRoot = string.Join(Path.DirectorySeparatorChar, segments[..(vendor + 1)]);
        var manifest = Path.Combine(vendorRoot, "modules.txt");

        string[] lines;

        try
        {
            if (!File.Exists(manifest)) return null;

            lines = File.ReadAllLines(manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var relative = string.Join('/', segments[(vendor + 1)..]);

        string? best = null;

        foreach (var line in lines)
        {
            if (!line.StartsWith("# ", StringComparison.Ordinal)) continue;

            var module = line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (module is not { Length: > 0 }) continue;

            if (!relative.StartsWith(module + "/", StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || module.Length > best.Length) best = module;
        }

        if (best is null) return null;

        var root = Path.Combine(vendorRoot, best.Replace('/', Path.DirectorySeparatorChar));

        return new InstalledPackage(best.Split('/')[^1], root) { Ecosystem = "a vendored Go module" };
    }

    private static InstalledPackage? GoModuleCache(string full)
    {
        var segments = Split(full);

        var mod = IndexOfPair(segments, "pkg", "mod");
        if (mod < 0) return null;

        for (var i = mod + 2; i < segments.Length; i++)
        {
            var at = segments[i].IndexOf('@');
            if (at <= 0) continue;

            var root = string.Join(Path.DirectorySeparatorChar, segments[..(i + 1)]);
            var module = string.Join('/', segments[(mod + 2)..(i + 1)]);

            return new InstalledPackage(segments[i][..at], root)
            {
                Ecosystem = "the Go module cache",
                Caveat =
                    $"Go keeps {module} read-only in the module cache on purpose, so writing here " +
                    "will be refused. Run `go mod vendor` and FixFinder will patch the writable " +
                    "copy in your project instead.",
            };
        }

        return null;
    }

    private static InstalledPackage? CargoRegistry(string full)
    {
        var segments = Split(full);

        var registry = IndexOfPair(segments, "registry", "src");
        if (registry < 0) return null;

        var crate = registry + 3;
        if (crate >= segments.Length) return null;

        var root = string.Join(Path.DirectorySeparatorChar, segments[..(crate + 1)]);
        var folder = segments[crate];

        if (folder.Length == 0) return null;

        return new InstalledPackage(TrimVersion(folder), root)
        {
            Ecosystem = "the Cargo registry",
            Caveat =
                $"Cargo checksums {folder} and re-extracts it when that stops matching, so a patch " +
                "here does not survive the next build. `cargo vendor` gives you a copy that does.",
        };
    }

    private static string TrimVersion(string folder)
    {
        var dash = folder.LastIndexOf('-');

        return dash > 0 && dash + 1 < folder.Length && char.IsDigit(folder[dash + 1])
            ? folder[..dash]
            : folder;
    }

    private static string[] Split(string full) =>
        full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static int IndexOfPair(string[] segments, string first, string second)
    {
        for (var i = 0; i + 1 < segments.Length; i++)
        {
            if (segments[i].Equals(first, StringComparison.OrdinalIgnoreCase) &&
                segments[i + 1].Equals(second, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool IsRuntime(string? file) =>
        FrameClassifier.IsVendored(file) && For(file) is null;
}
