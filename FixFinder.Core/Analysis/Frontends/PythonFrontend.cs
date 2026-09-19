using System.Text;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Reads Python files into the IR with Python's own parser, run by the program's own interpreter.</summary>
public static class PythonFrontend
{
    private static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(30);

    public static string? FindInterpreter() => TargetFactory.FindOnPath("python") ?? TargetFactory.FindOnPath("py");

    public static async Task<IrProgram> ReadAsync(IReadOnlyList<string> files, string interpreter, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-analysis");
        var id = Guid.NewGuid().ToString("N");
        var script = Path.Combine(folder, $"ast-{id}.py");
        var output = Path.Combine(folder, $"ast-{id}.json");

        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(script, PythonAstScript.Source, new UTF8Encoding(false), cancellationToken);

            var run = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = interpreter,
                Arguments = $"-X utf8 \"{script}\" \"{output}\" " + string.Join(" ", files.Select(f => $"\"{f}\"")),
                WorkingDirectory = Path.GetDirectoryName(files[0]) ?? folder,
                Timeout = ParseTimeout,
            }, cancellationToken);

            if (!File.Exists(output))
                return Unread(files, run.LaunchError ?? "Python could not read the program: " + string.Join(" ", run.Lines.Select(l => l.Text)));

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellationToken), new JsonDocumentOptions { MaxDepth = 4096 });
            return Read(document.RootElement);
        }
        finally
        {
            TryDelete(script);
            TryDelete(output);
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

            var reader = new PythonAstReader(path);
            var (moduleFunctions, moduleClasses) = reader.ReadModule(entry.GetProperty("tree"));
            functions.AddRange(moduleFunctions);
            classes.AddRange(moduleClasses);
            problems.AddRange(reader.Problems);
        }

        return new IrProgram(SourceLanguage.Python, files, classes, functions, problems);
    }

    private static IrProgram Unread(IReadOnlyList<string> files, string problem) =>
        new(SourceLanguage.Python, files, [], [], [problem]);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
