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

    /// <summary>javac and java of the JDK the program is built with - the one its project or code asks for - or of the JDK in use.</summary>
    public static (string Javac, string Java)? FindTools(string? programFile = null)
    {
        var jdk = programFile is not null && JavaSetup.For(programFile).Setup is { } setup ? setup.Jdk : Jdks.InUse;
        return jdk is null ? null : (jdk.Javac, jdk.Java);
    }

    /// <summary>Reads the program's files with the JDK it is built with, and with its preview features on when its build has them on.</summary>
    public static Task<IrProgram> ReadAsync(IReadOnlyList<string> files, JavaSetup setup, CancellationToken cancellationToken = default) =>
        ReadAsync(files, setup.Jdk.Javac, setup.Jdk.Java, cancellationToken, setup.Preview ? setup.CompilerOptions : []);

    public static async Task<IrProgram> ReadAsync(
        IReadOnlyList<string> files, string javac, string java, CancellationToken cancellationToken = default, IReadOnlyList<string>? parseOptions = null)
    {
        var helper = await HelperAsync(javac, cancellationToken);
        if (helper.Problem is { } problem) return Unread(files, problem);

        var output = Path.Combine(Path.GetTempPath(), "FixFinder-analysis", $"java-ast-{Guid.NewGuid():N}.json");
        var listing = Path.ChangeExtension(output, ".files");
        var options = Path.ChangeExtension(output, ".options");

        try
        {
            await File.WriteAllLinesAsync(listing, files, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllLinesAsync(options, parseOptions ?? [], new UTF8Encoding(false), cancellationToken);

            var run = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = java,
                Arguments = $"-cp \"{helper.Folder}\" {JavaAstScript.ClassName} \"{output}\" \"{listing}\" \"{options}\"",
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
            foreach (var written in new[] { output, listing, options })
            {
                try { File.Delete(written); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Compiles the helper into a folder named after its source and the javac compiling it, so a changed helper is compiled
    /// afresh - and so is one for another JDK, as an older java cannot load what a newer javac compiled.
    /// </summary>
    private static async Task<(string Folder, string? Problem)> HelperAsync(string javac, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JavaAstScript.Source + "\n" + Path.GetFullPath(javac).ToLowerInvariant())))[..12];
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

        return ProgramStops.Lower(new IrProgram(SourceLanguage.Java, files, classes, functions, problems));
    }

    private static IrProgram Unread(IReadOnlyList<string> files, string problem) => new(SourceLanguage.Java, files, [], [], [problem]) { NotRead = problem };
}
