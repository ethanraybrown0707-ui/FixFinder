namespace FixFinder.Core.Execution;

/// <summary>A compiler FixFinder can drive, and what it is called when talking to a person.</summary>
/// <param name="Name">Display name: "MSVC", "gcc", "javac".</param>
/// <param name="Program">Full path to the executable.</param>
/// <param name="SetupScript">
/// A batch file that must be called first to put the compiler on PATH, or null.
/// </param>
public sealed record Toolchain(string Name, string Program, string? SetupScript = null)
{
    public string Description => SetupScript is null
        ? $"{Name} ({Program})"
        : $"{Name} ({Program}, set up by {Path.GetFileName(SetupScript)})";
}

/// <summary>
/// Finds the compilers on this machine for the languages that have to be built before they run.
/// </summary>
/// <remarks>
/// Needed because C, C++ and Java are not like Python: you cannot point a runner at
/// <c>main.c</c> and see what happens. Something has to compile it first, and on Windows that
/// something is rarely on PATH - <c>cl.exe</c> will not run at all until
/// <c>vcvarsall.bat</c> has set up two dozen environment variables, which is why the compile
/// step is generated as a small batch file rather than launched directly.
/// <para>
/// Discovery is by looking at known locations rather than by running the Visual Studio locator:
/// it is deterministic, costs no subprocess, and is easy to read when it gets the wrong answer.
/// </para>
/// </remarks>
public static class Toolchains
{
    /// <summary>Where Visual Studio puts the script that makes cl.exe usable, newest first.</summary>
    private static readonly string[] VisualStudioRoots =
    [
        @"C:\Program Files\Microsoft Visual Studio",
        @"C:\Program Files (x86)\Microsoft Visual Studio",
    ];

    /// <summary>Where a JDK usually ends up on Windows when it is not on PATH.</summary>
    private static readonly string[] JavaRoots =
    [
        @"C:\Program Files\Java",
        @"C:\Program Files\Eclipse Adoptium",
        @"C:\Program Files\Microsoft",
        @"C:\Program Files\Amazon Corretto",
        @"C:\Program Files\Zulu",
    ];

    /// <summary>Searched for once, by whichever check asks first.</summary>
    /// <remarks>
    /// Lazy rather than a field and a flag, because fixes are now compiled several at a time. With a
    /// flag set before the search finished, a second check asking at the same moment was told there
    /// was no MSVC at all - and refused a fix for a compiler that was there.
    /// </remarks>
    private static readonly Lazy<Toolchain?> Msvc = new(SearchForMsvc);

    /// <summary>
    /// Finds MSVC, which is the compiler most likely to already be present on a Windows machine.
    /// </summary>
    /// <remarks>
    /// What is returned is <c>vcvarsall.bat</c>, not <c>cl.exe</c>. Running the compiler
    /// directly fails with a missing-DLL error or a flood of "cannot open include file", because
    /// it depends entirely on the environment that script sets up.
    /// </remarks>
    public static Toolchain? FindMsvc() => Msvc.Value;

    private static Toolchain? SearchForMsvc()
    {
        foreach (var root in VisualStudioRoots)
        {
            if (!Directory.Exists(root)) continue;

            string[] editions;
            try { editions = Directory.GetDirectories(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            // Newest first: "18" beats "2022" beats "2019" by ordinal descending well enough,
            // and every candidate is checked anyway.
            foreach (var year in editions.OrderByDescending(d => d, StringComparer.Ordinal))
            {
                string[] products;
                try { products = Directory.GetDirectories(year); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                foreach (var product in products)
                {
                    var script = Path.Combine(product, "VC", "Auxiliary", "Build", "vcvarsall.bat");
                    if (!File.Exists(script)) continue;

                    var edition = $"{Path.GetFileName(year)} {Path.GetFileName(product)}";

                    return new Toolchain($"MSVC ({edition})", "cl.exe", script);
                }
            }
        }

        return null;
    }

    private static readonly AsyncLocal<bool> GnuHidden = new();

    /// <summary>
    /// Leaves gcc and clang out of the search until the returned scope is disposed, on this async
    /// flow only.
    /// </summary>
    /// <remarks>
    /// The build prefers a GNU compiler to MSVC whenever one is on PATH. On a machine with MinGW -
    /// Strawberry Perl brings one, and so does the CI image - that means MSVC's own diagnostics are
    /// never produced, and nothing that reads them ever runs. Changing PATH would do the same for
    /// every other test running at the time; an async-local flag does it for one.
    /// </remarks>
    public static IDisposable WithoutGnu()
    {
        var previous = GnuHidden.Value;
        GnuHidden.Value = true;

        return new Restore(() => GnuHidden.Value = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>Finds a GNU-style compiler for C or C++, in order of preference.</summary>
    public static Toolchain? FindGnu(bool cpp)
    {
        if (GnuHidden.Value) return null;

        var names = cpp ? new[] { "g++", "clang++" } : new[] { "gcc", "clang" };

        foreach (var name in names)
        {
            if (TargetFactory.FindOnPath(name) is { } found) return new Toolchain(name, found);
        }

        return null;
    }

    /// <summary>Finds javac, on PATH or in a JDK installed in the usual place.</summary>
    public static Toolchain? FindJavac() => FindJavaTool("javac");

    /// <summary>Finds the java launcher that runs what javac produced.</summary>
    public static Toolchain? FindJava() => FindJavaTool("java");

    private static Toolchain? FindJavaTool(string tool)
    {
        if (TargetFactory.FindOnPath(tool) is { } onPath) return new Toolchain(tool, onPath);

        foreach (var root in JavaRoots)
        {
            if (!Directory.Exists(root)) continue;

            string[] jdks;
            try { jdks = Directory.GetDirectories(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var jdk in jdks.OrderByDescending(d => d, StringComparer.Ordinal))
            {
                // Some layouts nest the runtime one level deeper, as with a bundled JBR.
                foreach (var candidate in new[]
                {
                    Path.Combine(jdk, "bin", tool + ".exe"),
                    Path.Combine(jdk, "jbr", "bin", tool + ".exe"),
                })
                {
                    if (File.Exists(candidate)) return new Toolchain(tool, candidate);
                }
            }
        }

        return null;
    }

    /// <summary>A plain-English list of what is and is not available, for the Settings window.</summary>
    public static IReadOnlyList<string> Describe() =>
    [
        $"C     : {FindGnu(false)?.Description ?? FindMsvc()?.Description ?? "no compiler found"}",
        $"C++   : {FindGnu(true)?.Description ?? FindMsvc()?.Description ?? "no compiler found"}",
        $"C#    : dotnet ({TargetFactory.FindOnPath("dotnet") ?? "not found"})",
        $"Java  : {FindJavac()?.Description ?? "no JDK found"}",
    ];
}
