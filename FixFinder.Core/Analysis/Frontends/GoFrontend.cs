using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Reads Go files into the IR with Go's own parser, go/parser, through a small helper built once with the program's Go.</summary>
public static class GoFrontend
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Building the reader is a one-off, and on a machine with nothing cached Go has the standard library to compile first.</summary>
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim Building = new(1, 1);

    public static string? FindGo() => TargetFactory.FindOnPath("go");

    public static async Task<IrProgram> ReadAsync(IReadOnlyList<string> files, string go, CancellationToken cancellationToken = default)
    {
        var helper = await HelperAsync(go, cancellationToken);
        if (helper.Problem is { } problem) return Unread(files, problem);

        var output = Path.Combine(Path.GetTempPath(), "FixFinder-analysis", $"go-ast-{Guid.NewGuid():N}.json");

        try
        {
            var run = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = helper.Program,
                Arguments = $"\"{output}\" " + string.Join(" ", files.Select(f => $"\"{f}\"")),
                WorkingDirectory = Path.GetDirectoryName(files[0]) ?? Path.GetTempPath(),
                Timeout = ReadTimeout,
            }, cancellationToken);

            if (!File.Exists(output))
                return Unread(files, run.LaunchError ?? "The Go reader could not read the program: " + string.Join(" ", run.Lines.Select(l => l.Text)));

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken), new JsonDocumentOptions { MaxDepth = 4096 });
            return Read(document.RootElement);
        }
        finally
        {
            try { File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Builds the helper into a folder named after its source, so a changed helper is built afresh.</summary>
    private static async Task<(string Program, string? Problem)> HelperAsync(string go, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GoAstScript.Source)))[..12];
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-analysis", $"go-ast-{hash}");
        var program = Path.Combine(folder, OperatingSystem.IsWindows() ? "fixfinder-go-ast.exe" : "fixfinder-go-ast");

        await Building.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(program)) return (program, null);

            Directory.CreateDirectory(folder);
            var source = Path.Combine(folder, "main.go");
            await File.WriteAllTextAsync(source, GoAstScript.Source, new UTF8Encoding(false), cancellationToken);

            var build = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = go,
                Arguments = $"build -o \"{program}\" \"{source}\"",
                WorkingDirectory = folder,
                Timeout = BuildTimeout,
            }, cancellationToken);

            if (File.Exists(program)) return (program, null);

            var said = build.Outcome == RunOutcome.TimedOut
                ? $"it was still going after {BuildTimeout.TotalMinutes:0} minutes"
                : string.Join(" ", build.Lines.Select(l => l.Text));

            return (program, $"The Go reader could not be built with this Go: {said}".TrimEnd());
        }
        finally
        {
            Building.Release();
        }
    }

    private static IrProgram Read(JsonElement root)
    {
        var files = new List<string>();
        var trees = new List<(string Path, JsonElement Tree)>();
        var problems = new List<string>();

        foreach (var entry in root.GetProperty("files").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString() ?? "";
            files.Add(path);

            if (entry.TryGetProperty("problem", out var problem)) problems.Add($"{Path.GetFileName(path)}: {problem.GetString()}");
            else if (entry.TryGetProperty("tree", out var tree)) trees.Add((path, tree));
        }

        var types = new GoTypes();
        foreach (var (path, tree) in trees) types.Collect(path, tree);

        var functions = new List<IrFunction>();
        var methods = new List<IrFunction>();
        foreach (var (path, tree) in trees)
        {
            var (own, bound) = new GoAstReader(path, types).ReadFile(tree);
            functions.AddRange(own);
            methods.AddRange(bound);
        }

        var classes = types.Classes(methods);
        return new IrProgram(SourceLanguage.Go, files, classes, functions, problems);
    }

    private static IrProgram Unread(IReadOnlyList<string> files, string problem) => new(SourceLanguage.Go, files, [], [], [problem]);
}
