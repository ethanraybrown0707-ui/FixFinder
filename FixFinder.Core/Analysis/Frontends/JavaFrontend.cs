using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Reads Java files into the IR with javac's own parser, through a small helper compiled once with the program's JDK.</summary>
public static class JavaFrontend
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(60);
    private static readonly SemaphoreSlim Compiling = new(1, 1);

    public static (string Javac, string Java)? FindTools() =>
        Toolchains.FindJavac() is { } javac && Toolchains.FindJava() is { } java ? (javac.Program, java.Program) : null;

    public static async Task<IrProgram> ReadAsync(IReadOnlyList<string> files, string javac, string java, CancellationToken cancellationToken = default)
    {
        var helper = await HelperAsync(javac, cancellationToken);
        if (helper.Problem is { } problem) return Unread(files, problem);

        var output = Path.Combine(Path.GetTempPath(), "FixFinder-analysis", $"java-ast-{Guid.NewGuid():N}.json");

        try
        {
            var run = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = java,
                Arguments = $"-cp \"{helper.Folder}\" {JavaAstScript.ClassName} \"{output}\" " + string.Join(" ", files.Select(f => $"\"{f}\"")),
                WorkingDirectory = Path.GetDirectoryName(files[0]) ?? helper.Folder,
                Timeout = ToolTimeout,
            }, cancellationToken);

            if (!File.Exists(output))
                return Unread(files, run.LaunchError ?? "Java could not read the program: " + string.Join(" ", run.Lines.Select(l => l.Text)));

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken), new JsonDocumentOptions { MaxDepth = 4096 });
            return Read(document.RootElement);
        }
        finally
        {
            try { File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Compiles the helper into a folder named after its source, so a changed helper is compiled afresh.</summary>
    private static async Task<(string Folder, string? Problem)> HelperAsync(string javac, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JavaAstScript.Source)))[..12];
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-analysis", $"java-ast-{hash}");
        var compiled = Path.Combine(folder, JavaAstScript.ClassName + ".class");

        await Compiling.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(compiled)) return (folder, null);

            Directory.CreateDirectory(folder);
            var source = Path.Combine(folder, JavaAstScript.ClassName + ".java");
            await File.WriteAllTextAsync(source, JavaAstScript.Source, new UTF8Encoding(false), cancellationToken);

            var build = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = javac,
                Arguments = $"-d \"{folder}\" \"{source}\"",
                WorkingDirectory = folder,
                Timeout = ToolTimeout,
            }, cancellationToken);

            return File.Exists(compiled)
                ? (folder, null)
                : (folder, "The Java reader could not be compiled with this JDK: " + string.Join(" ", build.Lines.Select(l => l.Text)));
        }
        finally
        {
            Compiling.Release();
        }
    }

    private static IrProgram Read(JsonElement root)
    {
        var files = new List<string>();
        var functions = new List<IrFunction>();
        var classes = new List<IrClass>();
        var problems = new List<string>();

        foreach (var entry in root.GetProperty("files").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString() ?? "";
            files.Add(path);

            if (entry.TryGetProperty("problem", out var problem))
            {
                problems.Add($"{Path.GetFileName(path)}: {problem.GetString()}");
                continue;
            }

            if (!entry.TryGetProperty("tree", out var tree)) continue;

            var (unitFunctions, unitClasses) = new JavaAstReader(path).ReadUnit(tree);
            functions.AddRange(unitFunctions);
            classes.AddRange(unitClasses);
        }

        return new IrProgram(SourceLanguage.Java, files, classes, functions, problems);
    }

    private static IrProgram Unread(IReadOnlyList<string> files, string problem) => new(SourceLanguage.Java, files, [], [], [problem]);
}
