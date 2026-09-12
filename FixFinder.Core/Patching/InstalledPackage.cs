using FixFinder.Core.Parsing;

namespace FixFinder.Core.Patching;

/// <summary>An installed dependency that a crash went through, and where it lives on disk.</summary>
/// <param name="Name">The package as a person would name it - <c>requests</c>, <c>express</c>.</param>
/// <param name="Root">The package's own folder. Nothing outside it is ever written.</param>
public sealed record InstalledPackage(string Name, string Root);

/// <summary>
/// Finds the folder an installed dependency occupies, when a crash came through one.
/// </summary>
/// <remarks>
/// Exists for the commonest real fix there is and the one FixFinder could not previously offer:
/// the bug is in a library, somebody upstream has already fixed it, and their patch changes that
/// library's files. Those files are on this disk - in <c>site-packages</c>, <c>node_modules</c>,
/// <c>vendor</c> - and until now the patch resolved to nothing, because the source root is the
/// user's project and a dependency is by definition outside it.
/// <para>
/// <b>The package folder, not the packages folder.</b> Rooting at <c>site-packages</c> itself
/// would put every installed library in range of one patch; rooting at
/// <c>site-packages/requests</c> keeps the containment check meaning something, and means a
/// refusal to write outside it is a refusal to touch anything but the one library named in the
/// crash.
/// </para>
/// <para>
/// The runtime's own standard library is deliberately excluded. A patch to <c>lib/python3.13</c>
/// or to <c>Microsoft.NETCore.App</c> is not a dependency fix, it is an edit to the language
/// installation, and no published issue is asking anybody to do that on their own machine.
/// </para>
/// </remarks>
public static class InstalledPackages
{
    /// <summary>
    /// Folders whose immediate children are individual packages.
    /// </summary>
    /// <remarks>
    /// Every entry here has to put exactly one package one level down, because that level is what
    /// the containment check is measured against. <c>vendor</c> is deliberately absent for that
    /// reason: Go lays it out as <c>vendor/github.com/org/repo</c> and Composer as
    /// <c>vendor/org/package</c>, so treating it as one level deep would root at
    /// <c>vendor/github.com</c> and put every dependency from that host inside one patch's reach.
    /// Offering nothing there is better than offering too much.
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

        var directory = Path.GetDirectoryName(full);

        // The level below the one being examined, needed for scoped npm packages, where the
        // package is two levels under node_modules rather than one.
        string? scoped = null;

        // Walk up until the parent is a packages folder; the directory itself is then the package.
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
                    if (scoped is null) return null;

                    return new InstalledPackage($"{name}/{Path.GetFileName(scoped)}", scoped);
                }

                return new InstalledPackage(name, directory);
            }

            scoped = directory;

            directory = parent;
        }

        return null;
    }

    /// <summary>True when the file is inside the language's own installation rather than a package.</summary>
    public static bool IsRuntime(string? file) =>
        FrameClassifier.IsVendored(file) && For(file) is null;
}
