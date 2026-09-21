using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>What compiling the changed copy produced.</summary>
public sealed record CheckResult(
    bool Ran, int? ExitCode, IReadOnlyList<CapturedLine> Lines, IReadOnlyList<ParsedError> Errors)
{
    public bool Clean => Ran && ExitCode == 0 && Errors.Count == 0;

    public static CheckResult NotRun { get; } = new(false, null, [], []);
}

/// <summary>Compiles a copy of a file with a fix made in it, somewhere that is not the user's source tree.</summary>
public static class CompileCheck
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "FixFinder-check");

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public static bool CanCheck(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".py" or ".java" or ".cs" => true,
        ".js" or ".mjs" or ".cjs" or ".go" => true,
        ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" => true,
        ".h" or ".hpp" or ".hh" or ".hxx" => true,
        _ => false,
    };

    public static async Task<CheckResult> RunAsync(
        SourceFile source,
        IReadOnlyList<string> lines,
        string? pythonInterpreter,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);

            var bytes = source.Render(lines);
            var copy = Path.Combine(folder, Path.GetFileName(source.Path));
            await File.WriteAllBytesAsync(copy, bytes, cancellationToken);

            if (SpecFor(copy, source.Path, folder, pythonInterpreter) is not { } spec) return CheckResult.NotRun;

            var key = KeyFor(spec, folder, source.Path, bytes);

            if (key is not null && Remembered.TryGetValue(key, out var known)) return known;

            var result = Path.GetExtension(copy).ToLowerInvariant() switch
            {
                ".cs" when !CSharpDirectCompile.HasDirectives(Encoding.UTF8.GetString(bytes)) && ProgramLayout.CSharpProject(source.Path) is null =>
                    await CompileCSharpAsync(spec, copy, folder, cancellationToken),
                ".java" => await CompileJavaAsync(spec, copy, source.Path, folder, cancellationToken),
                ".go" when ProgramLayout.GoPackageOf(source.Path).IsSingleFile && GoDirectBuild.KeyFor(Path.GetFileName(copy), Encoding.UTF8.GetString(bytes)) is { } plan =>
                    await CompileGoAsync(spec, copy, folder, plan, bytes, cancellationToken),
                _ => await CompileAsync(spec, direct: false, cancellationToken),
            };

            if (!result.Ran) return result;

            if (key is not null && WorthRemembering(result, copy))
            {
                if (Remembered.Count >= MostRemembered) Remembered.Clear();
                Remembered[key] = result;
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CheckResult.NotRun;
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<CheckResult> CompileAsync(TargetSpec spec, bool direct, CancellationToken cancellationToken)
    {
        var registry = new ParserRegistry();
        var run = await new TargetRunner(registry).RunAsync(spec, cancellationToken);

        if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled)
            return CheckResult.NotRun;

        var lines = direct ? CSharpDirectCompile.AsBuildPrintsIt(run.Lines) : run.Lines;

        return new CheckResult(true, run.ExitCode, lines, ErrorsIn(registry, lines));
    }

    private static async Task<CheckResult> CompileCSharpAsync(
        TargetSpec build, string copy, string folder, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(copy);
        var captured = CSharpDirectCompile.TemplateFor(name, build.ExecutablePath);

        if (captured.IsCompletedSuccessfully && captured.Result is null) return await CompileAsync(build, direct: false, cancellationToken);

        return await FasterCheck.RunAsync(
            "csc",
            CSharpDirectCompile.TrustFor(name),
            token => CompileAsync(build, direct: false, token),
            async (target, targetFolder, token) =>
                await captured.WaitAsync(token) is { } template
                    ? await CompileAsync(CSharpDirectCompile.SpecFor(template, target, targetFolder, Timeout), direct: true, token)
                    : null,
            copy, folder, everyLine: false, cancellationToken);
    }

    private static async Task<CheckResult> CompileJavaAsync(
        TargetSpec javac, string copy, string original, string folder, CancellationToken cancellationToken)
    {
        if (JavaCompileServer.For(javac.ExecutablePath) is not { } server) return await CompileAsync(javac, direct: false, cancellationToken);

        return await FasterCheck.RunAsync(
            "javac",
            server.Trust,
            token => CompileAsync(javac, direct: false, token),
            async (target, targetFolder, token) =>
            {
                if (await server.CompileAsync(JavacArguments(target, ProgramLayout.JavaSourceRoot(original), targetFolder), Timeout, token) is not { } reply)
                    return null;

                var lines = JavaCompileServer.Lines(reply.Output, javac.OutputEncoding);

                return new CheckResult(true, reply.ExitCode, lines, ErrorsIn(new ParserRegistry(), lines));
            },
            copy, folder, everyLine: true, cancellationToken);
    }

    public static void Prepare(SourceFile source)
    {
        if (!Path.GetExtension(source.Path).Equals(".go", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var content = source.Render(source.Lines);
            var name = Path.GetFileName(source.Path);

            if (ProgramLayout.GoPackageOf(source.Path).IsSingleFile &&
                TargetFactory.FindOnPath("go") is { } go && GoDirectBuild.KeyFor(name, Encoding.UTF8.GetString(content)) is { } key)
                _ = GoDirectBuild.PlanFor(go, key, name, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task<CheckResult> CompileGoAsync(
        TargetSpec build, string copy, string folder, string key, byte[] content, CancellationToken cancellationToken)
    {
        var go = build.ExecutablePath;
        var trust = GoDirectBuild.TrustFor(go);
        var planned = GoDirectBuild.PlanFor(go, key, Path.GetFileName(copy), content);

        if (planned.IsCompletedSuccessfully && planned.Result is null) return await CompileAsync(build, direct: false, cancellationToken);

        return await FasterCheck.RunAsync(
            "go",
            trust,
            token => CompileAsync(build, direct: false, token),
            async (target, targetFolder, token) =>
                await planned.WaitAsync(token) is { } plan
                    ? await GoDirectBuild.RunAsync(plan, target, targetFolder, Timeout, token)
                    : null,
            copy, folder, everyLine: true, cancellationToken);
    }

    internal static IReadOnlyList<string> JavacArguments(string copy, string sourceRoot, string folder) =>
        ["-proc:none", CompiledLanguages.JavaLint, "-Xmaxerrs", "500", "-d", Path.Combine(folder, "out"), "-sourcepath", sourceRoot, copy];

    private static readonly ConcurrentDictionary<string, CheckResult> Remembered = new(StringComparer.Ordinal);

    private const int MostRemembered = 256;

    private const int MostNeighbours = 2000;

    internal static string? KeyFor(TargetSpec spec, string folder, string original, byte[] content)
    {
        var extension = Path.GetExtension(original).ToLowerInvariant();
        var directives = Encoding.UTF8.GetString(content).Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => line.StartsWith('#'))
            .ToList();

        if (extension == ".cs" && directives.Any(line => line.StartsWith("#:", StringComparison.Ordinal)))
            return null;

        if (extension == ".cs" && ProgramLayout.CSharpProject(original) is not null) return null;

        var key = new StringBuilder()
            .Append(spec.ExecutablePath).Append('\n')
            .Append(spec.Arguments.Replace(folder, "<copy>", StringComparison.OrdinalIgnoreCase)).Append('\n')
            .Append(spec.LaunchViaDotnet).Append('\n')
            .Append(original).Append('\n')
            .Append(Convert.ToHexString(SHA256.HashData(content))).Append('\n');

        foreach (var (name, value) in spec.ExtraEnvironment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            key.Append(name).Append('=').Append(value).Append('\n');

        if (extension is ".java" or ".c" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".go" or ".h" or ".hpp" or ".hh" or ".hxx")
        {
            if (extension != ".java" && directives.Any(line => line.Contains("..", StringComparison.Ordinal)))
                return null;

            var around = extension == ".java" ? ProgramLayout.JavaSourceRoot(original) : Path.GetDirectoryName(original)!;
            if (Neighbours(around) is not { } listing) return null;
            key.Append(listing);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
    }

    private static string? Neighbours(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        var listing = new StringBuilder();
        var count = 0;

        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                if (++count > MostNeighbours) return null;

                listing.Append(file.FullName).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }

        return listing.ToString();
    }

    internal static bool WorthRemembering(CheckResult result, string copy)
    {
        if (!result.Ran) return false;
        if (result.Clean) return true;
        if (result.Errors.Count == 0) return false;

        var name = Path.GetFileName(copy);

        return result.Errors.All(error =>
            (error.CulpritFrame ?? error.Frames.FirstOrDefault())?.File is { } file &&
            Path.GetFileName(file).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<ParsedError> ErrorsIn(ParserRegistry registry, IReadOnlyList<CapturedLine> lines)
    {
        if (registry.Parse(lines) is not { } first) return [];

        if (first.LanguageId == "generic") return [];

        return [first, .. registry.Others(first, lines)];
    }

    private static TargetSpec? SpecFor(string copy, string original, string folder, string? python)
    {
        var originalFolder = Path.GetDirectoryName(original)!;

        switch (Path.GetExtension(copy).ToLowerInvariant())
        {
            case ".py":
                var interpreter = python ?? TargetFactory.FindOnPath("python") ?? TargetFactory.FindOnPath("py");
                if (interpreter is null) return null;

                return Spec(interpreter, $"-X utf8 -m py_compile \"{copy}\"", folder);

            case ".java":
                if (Toolchains.FindJavac() is not { } javac) return null;

                return Spec(
                    javac.Program,
                    string.Join(" ", JavacArguments(copy, ProgramLayout.JavaSourceRoot(original), folder).Select(a => a.StartsWith('-') ? a : $"\"{a}\"")),
                    folder);

            case ".c" or ".cpp" or ".cc" or ".cxx" or ".c++":
                return Native(copy, originalFolder, folder, others: ProgramLayout.NativeSources(original).Skip(1).ToList());

            case ".h" or ".hpp" or ".hh" or ".hxx":
            {
                if (ProgramLayout.HeaderProgram(original) is not { Count: > 0 } including) return null;

                foreach (var file in ProgramLayout.HeaderNeighbours(original))
                {
                    var target = Path.Combine(folder, Path.GetFileName(file));
                    if (!target.Equals(copy, StringComparison.OrdinalIgnoreCase)) File.Copy(file, target, overwrite: true);
                }

                var copies = including.Select(file => Path.Combine(folder, Path.GetFileName(file))).ToList();
                return Native(copies[0], originalFolder, folder, others: copies.Skip(1).ToList());
            }

            case ".js" or ".mjs" or ".cjs":
                if (TargetFactory.FindOnPath("node") is not { } node) return null;

                return Spec(node, $"--check \"{copy}\"", folder);

            case ".go":
                if (TargetFactory.FindOnPath("go") is not { } go) return null;

                var program = ProgramLayout.GoPackageOf(original);
                if (program.IsSingleFile) return Spec(go, $"build -o \"{Path.Combine(folder, "check.exe")}\" \"{copy}\"", folder);

                if (program.Module is not null)
                {
                    var module = Path.Combine(folder, "module");
                    if (!CopyTree(program.Module, module, original, copy)) return null;

                    return Spec(go, $"build -o \"{Path.Combine(folder, "check.exe")}\" .", module);
                }

                var files = new List<string> { copy };

                foreach (var other in program.Files.Skip(1))
                {
                    var target = Path.Combine(folder, Path.GetFileName(other));
                    File.Copy(other, target, overwrite: true);
                    files.Add(target);
                }

                return Spec(go, $"build -o \"{Path.Combine(folder, "check.exe")}\" {string.Join(" ", files.Select(f => $"\"{f}\""))}", folder);

            case ".cs":
                if (TargetFactory.FindOnPath("dotnet") is not { } dotnet) return null;

                if (ProgramLayout.CSharpProject(original) is { } project)
                {
                    var copied = Path.Combine(folder, "project");
                    if (!CopyTree(Path.GetDirectoryName(project)!, copied, original, copy)) return null;

                    return Spec(dotnet, $"build \"{Path.Combine(copied, Path.GetFileName(project))}\" -nologo -v q", copied);
                }

                return Spec(dotnet, $"build \"{copy}\" -nologo -v q", folder);

            default:
                return null;
        }
    }

    internal static TargetSpec? Native(string copy, string originalFolder, string folder, bool reuseMsvcEnvironment = true, IReadOnlyList<string>? others = null)
    {
        var cpp = !Path.GetExtension(copy).Equals(".c", StringComparison.OrdinalIgnoreCase);
        var exe = Path.Combine(folder, "check.exe");
        var rest = string.Concat((others ?? []).Select(o => $" \"{o}\""));

        if (Toolchains.FindGnu(cpp) is { } gnu)
        {
            var standard = CompiledLanguages.GnuWarnings(cpp);
            return Spec(gnu.Program, $"{standard}-Wformat -I \"{originalFolder}\" -o \"{exe}\" \"{copy}\"{rest}", folder);
        }

        if (Toolchains.FindMsvc() is not { SetupScript: { } vcvarsall }) return null;

        var flags = cpp ? "/nologo /W3 /EHsc /std:c++17" : "/nologo /W3";
        var compile = $"{flags} /I \"{originalFolder}\" /Fe:check.exe \"{Path.GetFileName(copy)}\"{rest}";

        if (reuseMsvcEnvironment && Toolchains.MsvcEnvironment() is { } environment && Toolchains.ClIn(environment) is { } cl)
        {
            return new TargetSpec
            {
                ExecutablePath = cl,
                Arguments = compile,
                WorkingDirectory = folder,
                Timeout = Timeout,
                ExtraEnvironment = environment,
            };
        }

        var batch = Path.Combine(folder, "check.cmd");

        File.WriteAllText(batch, string.Join("\r\n",
        [
            "@echo off",
            $"call \"{vcvarsall}\" x64 >nul",
            "if errorlevel 1 (echo FixFinder: could not set up the MSVC environment & exit /b 1)",
            $"cd /d \"{folder}\"",
            $"cl {compile}",
            "exit /b %errorlevel%",
            "",
        ]), new UTF8Encoding(false));

        return Spec("cmd.exe", $"/c \"{batch}\"", folder);
    }

    private static readonly HashSet<string> NotCopied = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".idea", "node_modules" };

    private const int MostProjectFiles = 500;

    private static bool CopyTree(string from, string to, string original, string changed)
    {
        var files = new List<string>();
        var pending = new Stack<string>([from]);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var sub in Directory.EnumerateDirectories(directory))
                if (!NotCopied.Contains(Path.GetFileName(sub))) pending.Push(sub);

            files.AddRange(Directory.EnumerateFiles(directory));
            if (files.Count > MostProjectFiles) return false;
        }

        foreach (var file in files)
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var isOriginal = Path.GetFullPath(file).Equals(Path.GetFullPath(original), StringComparison.OrdinalIgnoreCase);
            File.Copy(isOriginal ? changed : file, target, overwrite: true);
        }

        return true;
    }

    private static TargetSpec Spec(string program, string arguments, string folder) => new()
    {
        ExecutablePath = program,
        Arguments = arguments,
        WorkingDirectory = folder,
        Timeout = Timeout,
    };
}
