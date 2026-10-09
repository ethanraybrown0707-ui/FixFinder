using System.Text;

namespace FixFinder.Core.Execution;

/// <summary>
/// Builds an OCaml program to bytecode and runs it - with the debugging information that lets a crash name its line, and
/// ocamlrun told to print where an exception was raised.
/// </summary>
/// <remarks>
/// ocamlc writes what it compiles beside each source file, so the program's files are copied into FixFinder's own build
/// folder and compiled there, under their own names: what ocamlc says still names marks.ml, and nothing is written into
/// the program's folder. The program then runs from its own folder, so a file it opens by name is found there.
/// </remarks>
public static class OCamlBuild
{
    /// <summary>The bytecode the program is built into, in the build folder.</summary>
    public const string Bytecode = "program.byte";

    public static (BuildAndRun? Plan, string? Problem) Prepare(string source, string output, TimeSpan timeout)
    {
        if (OCamlToolchains.Usual is not { } ocaml)
        {
            return (null,
                $"{Path.GetFileName(source)} is OCaml, which has to be compiled before it can run, and no OCaml was found - not on PATH, nor in " +
                "opam's current switch.\n\nInstall OCaml with opam (opam.ocaml.org), or open a terminal where opam's environment is set up.");
        }

        var layout = OCamlProgram.Of(source);
        var folder = Path.GetDirectoryName(Path.GetFullPath(source))!;

        var batch = WriteBatch(output, layout.Files, CompileArguments(ocaml, layout.Files.Select(Path.GetFileName).OfType<string>().ToList(), layout.Libraries, Bytecode), ocaml);

        var compile = new TargetSpec
        {
            ExecutablePath = "cmd.exe",
            Arguments = $"/c \"{batch}\"",
            WorkingDirectory = folder,
            Timeout = timeout,
        };

        var run = new TargetSpec
        {
            ExecutablePath = ocaml.Ocamlrun,
            Arguments = $"\"{Path.Combine(output, Bytecode)}\"",
            WorkingDirectory = folder,
            Timeout = timeout,
            ExtraEnvironment = new Dictionary<string, string> { ["OCAMLRUNPARAM"] = WithBacktraces(Environment.GetEnvironmentVariable("OCAMLRUNPARAM")) },
        };

        return (new BuildAndRun(compile, run, Explained(ocaml, layout)), null);
    }

    /// <summary>
    /// ocamlc's arguments to build these files - named as they are in the folder it is run in - into one program: with
    /// debugging information, and the libraries that come with OCaml that the code uses. From OCaml 5, those libraries are
    /// in folders of their own, which ocamlc is told with -I +str.
    /// </summary>
    public static string CompileArguments(OCamlToolchains.OCaml ocaml, IReadOnlyList<string> fileNames, IReadOnlyList<string> libraries, string outputName)
    {
        var linked = string.Concat(libraries.Select(library => ocaml.Major >= 5 ? $" -I +{library} {library}.cma" : $" {library}.cma"));
        return $"-g{linked} -o \"{outputName}\"" + string.Concat(fileNames.Select(name => $" \"{name}\""));
    }

    /// <summary>
    /// Writes the batch file that copies the program's files into the build folder and compiles them there, and says where
    /// it is. The files keep their names, so ocamlc's messages name them as the program does.
    /// </summary>
    public static string WriteBatch(string buildFolder, IEnumerable<string> files, string compileArguments, OCamlToolchains.OCaml ocaml)
    {
        Directory.CreateDirectory(buildFolder);

        var lines = new List<string>
        {
            "@echo off",
            "rem Written by FixFinder: the program's files are copied here and compiled here, so nothing is written beside them.",
            "chcp 65001 >nul",
            $"cd /d \"{buildFolder.TrimEnd(Path.DirectorySeparatorChar)}\"",
        };

        foreach (var file in files)
        {
            lines.Add($"copy /y \"{file}\" \"{Path.GetFileName(file)}\" >nul");
            lines.Add("if errorlevel 1 (echo FixFinder: could not copy a file of the program to build it & exit /b 1)");
        }

        lines.Add($"\"{ocaml.Ocamlc}\" {compileArguments}");
        lines.Add("exit /b %errorlevel%");
        lines.Add("");

        var batch = Path.Combine(buildFolder, "build.cmd");
        File.WriteAllText(batch, string.Join("\r\n", lines), new UTF8Encoding(false));
        return batch;
    }

    /// <summary>OCAMLRUNPARAM with b in it, which makes ocamlrun print where an exception nothing caught was raised.</summary>
    public static string WithBacktraces(string? existing) =>
        existing is not { Length: > 0 } ? "b" : existing.Split(',').Contains("b") ? existing : existing + ",b";

    private static string Explained(OCamlToolchains.OCaml ocaml, OCamlProgram.Layout layout)
    {
        var others = layout.Files.Where(OCamlProgram.IsOCaml).SkipLast(1).Select(Path.GetFileName).ToList();
        var together = others.Count switch
        {
            0 => "",
            1 => $" together with {others[0]}",
            _ => $" together with {string.Join(", ", others.SkipLast(1))} and {others[^1]}",
        };

        var libraries = layout.Libraries.Count == 0 ? "" : $", with the {string.Join(" and ", layout.Libraries)} {(layout.Libraries.Count == 1 ? "library" : "libraries")}";
        var dune = layout.DuneFile is null ? "" :
            " Its dune file is not what builds it here: FixFinder never runs dune, and builds it from the files beside it, so a library the dune file names is not built with it.";

        return $"Building it{together} with {ocaml.Description}{libraries} - to bytecode, with what lets a crash name its line - then running it with ocamlrun.{dune}";
    }
}
