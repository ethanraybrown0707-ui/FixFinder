namespace FixFinder.Core.Execution;

/// <summary>A compiler FixFinder can drive, and what it is called when talking to a person.</summary>
public sealed record Toolchain(string Name, string Program, string? SetupScript = null)
{
    public string Description => SetupScript is null
        ? $"{Name} ({Program})"
        : $"{Name} ({Program}, set up by {Path.GetFileName(SetupScript)})";
}

/// <summary>Finds the compilers on this machine for the languages that have to be built before they run.</summary>
public static class Toolchains
{
    private static readonly string[] VisualStudioRoots =
    [
        @"C:\Program Files\Microsoft Visual Studio",
        @"C:\Program Files (x86)\Microsoft Visual Studio",
    ];

    private static readonly Lazy<Toolchain?> Msvc = new(SearchForMsvc);

    public static Toolchain? FindMsvc() => Msvc.Value;

    private static Toolchain? SearchForMsvc()
    {
        foreach (var root in VisualStudioRoots)
        {
            if (!Directory.Exists(root)) continue;

            string[] editions;
            try { editions = Directory.GetDirectories(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

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

    private static readonly Lazy<IReadOnlyDictionary<string, string>?> MsvcSetUp = new(CaptureMsvcEnvironment);

    public static IReadOnlyDictionary<string, string>? MsvcEnvironment() => MsvcSetUp.Value;

    public static string? ClIn(IReadOnlyDictionary<string, string> environment)
    {
        if (!environment.TryGetValue("PATH", out var path)) return null;

        foreach (var folder in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var cl = Path.Combine(folder, "cl.exe");
                if (File.Exists(cl)) return cl;
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    internal static IReadOnlyDictionary<string, string>? ReadEnvironment(string output)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = line.IndexOf('=');
            if (equals <= 0) continue;

            variables[line[..equals]] = line[(equals + 1)..];
        }

        return variables.ContainsKey("INCLUDE") && variables.ContainsKey("LIB") ? variables : null;
    }

    private static IReadOnlyDictionary<string, string>? CaptureMsvcEnvironment()
    {
        if (FindMsvc() is not { SetupScript: { } script }) return null;

        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/s /c \"\"{script}\" x64 >nul && set\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeSpan.FromMinutes(2)) || !output.Wait(ToolOutput.AfterExit))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return null;
            }

            if (process.ExitCode != 0 || ReadEnvironment(output.Result) is not { } environment) return null;

            if (ClIn(environment) is null) return null;

            var include = environment["INCLUDE"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return include.All(Directory.Exists) && !environment.Values.Any(v => v.Contains('�'))
                ? environment
                : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static readonly AsyncLocal<bool> GnuHidden = new();

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

    /// <summary>javac of the JDK in use: the program's own while it is checked, otherwise the default one.</summary>
    public static Toolchain? FindJavac() => Jdks.InUse is { } jdk ? new Toolchain("javac", jdk.Javac) : null;

    /// <summary>java of the same JDK as <see cref="FindJavac"/>, so what javac builds is always run by a java that can read it.</summary>
    public static Toolchain? FindJava() => Jdks.InUse is { } jdk ? new Toolchain("java", jdk.Java) : null;

    public static IReadOnlyList<string> Describe() =>
    [
        $"Python     : {TargetFactory.FindOnPath("python") ?? TargetFactory.FindOnPath("py") ?? "not found"}",
        .. DescribeJdks(),
        .. Described("C#         : ", ".NET SDK", DotnetSdks.Installed),
        $"C          : {FindGnu(false)?.Description ?? FindMsvc()?.Description ?? "no compiler found"}",
        $"C++        : {FindGnu(true)?.Description ?? FindMsvc()?.Description ?? "no compiler found"}",
        .. Described("JavaScript : ", "Node.js", Nodes.Installed),
        .. Described("Go         : ", "Go", GoToolchains.Installed),
    ];

    /// <summary>Each toolchain of a language found, one to a line, newest first, with where it was found: the one on PATH is used unless a program needs another.</summary>
    private static IEnumerable<string> Described(string heading, string language, IReadOnlyList<Versions.VersionedToolchain> installed)
    {
        if (installed.Count == 0) return [$"{heading}not found"];

        var indent = new string(' ', heading.Length);
        return installed.Select((toolchain, index) => $"{(index == 0 ? heading : indent)}{language} {toolchain.VersionText} ({toolchain.FoundIn}): {toolchain.Program}");
    }

    /// <summary>Each JDK found, one to a line, the one used unless a program asks for another marked as the default.</summary>
    private static IEnumerable<string> DescribeJdks()
    {
        var installed = Jdks.Installed;
        if (installed.Count == 0) return ["Java       : no JDK found"];

        var usual = Jdks.Default;
        return installed.Select((jdk, index) =>
            $"{(index == 0 ? "Java       : " : "             ")}{jdk.Description}{(jdk == usual ? " - the default" : "")}: {jdk.Home}");
    }
}
