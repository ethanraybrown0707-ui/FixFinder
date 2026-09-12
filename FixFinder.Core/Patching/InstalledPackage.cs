using FixFinder.Core.Parsing;

namespace FixFinder.Core.Patching;

/// <summary>An installed dependency that a crash went through, and where it lives on disk.</summary>
/// <param name="Name">The package as a person would name it - <c>requests</c>, <c>errors</c>.</param>
/// <param name="Root">The package's own folder. Nothing outside it is ever written.</param>
public sealed record InstalledPackage(string Name, string Root)
{
    /// <summary>Where it came from, in the words that ecosystem uses.</summary>
    public string Ecosystem { get; init; } = "";

    /// <summary>
    /// What is awkward about writing here, when something is.
    /// </summary>
    /// <remarks>
    /// Go marks its module cache read-only on purpose, and Cargo re-extracts a crate whose
    /// checksum stops matching. Those are not obstacles to route around - they are the ecosystem
    /// saying its cache is not the place to edit - so the advice names the command that produces
    /// a writable copy instead.
    /// </remarks>
    public string? Caveat { get; init; }
}

/// <summary>
/// Finds the folder an installed dependency occupies, when a crash came through one.
/// </summary>
/// <remarks>
/// Exists for the commonest real fix there is and the one FixFinder could not previously offer:
/// the bug is in a library, somebody upstream has already fixed it, and their patch changes that
/// library's files. Those files are on this disk and until now the patch resolved to nothing,
/// because the source root is the user's project and a dependency is by definition outside it.
/// <para>
/// <b>The package folder, not the packages folder.</b> Rooting at <c>site-packages</c> would put
/// every installed library in range of one patch; rooting at <c>site-packages/requests</c> keeps
/// the containment check meaning something. Every layout below has to name exactly one package,
/// which is why each ecosystem gets its own rule rather than one shared guess at depth.
/// </para>
/// <para>
/// The runtime's own standard library is deliberately excluded. A patch to <c>lib/python3.13</c>
/// or to <c>Microsoft.NETCore.App</c> is not a dependency fix, it is an edit to the language
/// installation, and no published issue is asking anybody to do that on their own machine.
/// </para>
/// </remarks>
public static class InstalledPackages
{
    /// <summary>Folders whose immediate children are individual packages.</summary>
    /// <remarks>
    /// One level down, and only these. Go and Rust are handled separately below, because their
    /// depth is not one and cannot be read off the path without knowing the ecosystem.
    /// </remarks>
    private static readonly string[] PackageContainers =
    [
        "site-packages", "dist-packages", "node_modules", "bower_components", "gems",
    ];

    /// <summary>
    /// The nearest installed package a stack trace passed through, or null.
    /// </summary>
    /// <param name="files">Files named in the crash, innermost first.</param>
    public static InstalledPackage? From(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (For(file) is { } found) return found;
        }

        return null;
    }

    /// <summary>The package folder containing one file, or null when it is not in one.</summary>
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

        // Vendored Go first: it is the only one of these meant to be edited, so where a project
        // has both a vendor directory and the module cache, the writable copy wins.
        return GoVendor(full) ?? GoModuleCache(full) ?? CargoRegistry(full) ?? Container(full);
    }

    // ------------------------------------------------------------------ one level down

    /// <summary>site-packages, node_modules and friends, where a package is one folder down.</summary>
    private static InstalledPackage? Container(string full)
    {
        var directory = Path.GetDirectoryName(full);

        // The level below the one being examined, needed for scoped npm packages, where the
        // package is two levels under node_modules rather than one.
        string? scoped = null;

        while (directory is { Length: > 0 })
        {
            var parent = Path.GetDirectoryName(directory);
            if (parent is null) return null;

            var parentName = Path.GetFileName(parent);

            if (PackageContainers.Contains(parentName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);

                // A loose module - site-packages/six.py - has no folder of its own, and there is
                // nothing to contain a patch to. Refused rather than rooted at site-packages.
                if (name.Length == 0) return null;

                // A scoped npm package lives one level further down: node_modules/@scope/thing.
                // Rooting at the scope would let a patch reach every package sharing it, which is
                // exactly the reach the containment check exists to deny.
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

    // ------------------------------------------------------------------ Go

    /// <summary>
    /// A vendored Go module, whose depth comes from <c>vendor/modules.txt</c> rather than a guess.
    /// </summary>
    /// <remarks>
    /// The reason <c>vendor/</c> was refused outright until now. A module path is a variable
    /// number of segments - two for <c>gopkg.in/yaml.v2</c>, three for
    /// <c>github.com/pkg/errors</c> - so rooting one level down would put every dependency from a
    /// host inside one patch's reach. <c>modules.txt</c> lists the module paths exactly, which
    /// leaves nothing to guess; where that file is missing the folder is left alone as before.
    /// <para>
    /// This is also the one Go layout that is writable, and the one Go's own tooling expects you
    /// to edit, which is why it is tried before the cache.
    /// </para>
    /// </remarks>
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

        // Longest match wins, so a module published under another module's path is not mistaken
        // for its parent.
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

    /// <summary>
    /// The Go module cache: <c>pkg/mod/github.com/pkg/errors@v0.9.1</c>.
    /// </summary>
    /// <remarks>
    /// The module directory is the one carrying <c>@version</c> in its name, which is a far more
    /// reliable marker than counting segments, for the same reason vendoring needs a manifest.
    /// <para>
    /// Go makes the whole cache read-only deliberately, and the applier refuses a read-only file,
    /// so this is detected in order to <i>explain itself</i> rather than to be written to. Saying
    /// "the file is read-only" and stopping would be true and baffling; the caveat names
    /// <c>go mod vendor</c>, which produces a copy that can be patched.
    /// </para>
    /// </remarks>
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

    // ------------------------------------------------------------------ Rust

    /// <summary>
    /// A crate unpacked by Cargo: <c>.cargo/registry/src/&lt;index&gt;/serde-1.0.197</c>.
    /// </summary>
    /// <remarks>
    /// Always exactly two levels under <c>registry/src</c> - an index directory, then one
    /// crate-and-version - so unlike Go this depth really is fixed and needs no manifest.
    /// <para>
    /// The version is trimmed off the name so the confirmation reads <c>APPLY TO SERDE</c> rather
    /// than carrying a semver, but only when what follows the last dash is actually a number:
    /// crates like <c>pin-project</c> would otherwise lose a word.
    /// </para>
    /// </remarks>
    private static InstalledPackage? CargoRegistry(string full)
    {
        var segments = Split(full);

        var registry = IndexOfPair(segments, "registry", "src");
        if (registry < 0) return null;

        // registry / src / <index> / <crate-version> / ...
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

    /// <summary>Turns <c>serde-1.0.197</c> into <c>serde</c>, leaving a name with no version alone.</summary>
    private static string TrimVersion(string folder)
    {
        var dash = folder.LastIndexOf('-');

        return dash > 0 && dash + 1 < folder.Length && char.IsDigit(folder[dash + 1])
            ? folder[..dash]
            : folder;
    }

    // ------------------------------------------------------------------

    private static string[] Split(string full) =>
        full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Index of the first of two adjacent path segments, or -1.</summary>
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

    /// <summary>True when the file is inside the language's own installation rather than a package.</summary>
    public static bool IsRuntime(string? file) =>
        FrameClassifier.IsVendored(file) && For(file) is null;
}
