using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// Where OCaml is found, which files an OCaml program is built from and in what order, and how ocamlc is told to build it
/// - in folders laid out as opam lays them out, as a test cannot install an OCaml.
/// </summary>
public class OCamlSetupTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string text = "")
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    [Fact]
    public void TheOCamlOnPathIsFoundWithTheOcamlrunBesideItAndItsVersion()
    {
        var ocamlc = Write(Path.Combine("bin", Exe("ocamlc")));
        Write(Path.Combine("bin", Exe("ocamlrun")));

        using var looking = OCamlToolchains.LookingIn(new OCamlToolchains.Places(Path.Combine(_temp.Path, "opam"), name => name == "ocamlc" ? ocamlc : null, _ => "5.2.0"));

        var ocaml = OCamlToolchains.Usual;
        Assert.NotNull(ocaml);
        Assert.Equal(("5.2.0", 5, 2, "on PATH"), (ocaml.VersionText, ocaml.Major, ocaml.Minor, ocaml.FoundIn));
        Assert.Equal(Path.Combine(_temp.Path, "bin", Exe("ocamlrun")), ocaml.Ocamlrun);
    }

    [Fact]
    public void WithoutOneOnPathTheSwitchOpamsConfigNamesIsUsed()
    {
        Write(Path.Combine("opam", "config"), "opam-version: \"2.0\"\nswitch: \"default\"\n");
        Write(Path.Combine("opam", "default", "bin", Exe("ocamlc")));
        Write(Path.Combine("opam", "default", "bin", Exe("ocamlrun")));
        Write(Path.Combine("opam", "4.14", "bin", Exe("ocamlc")));

        using var looking = OCamlToolchains.LookingIn(new OCamlToolchains.Places(Path.Combine(_temp.Path, "opam"), _ => null, _ => "4.14.2"));

        var ocaml = OCamlToolchains.Usual;
        Assert.NotNull(ocaml);
        Assert.Equal("opam's switch default", ocaml.FoundIn);
        Assert.Equal(4, ocaml.Major);
    }

    [Fact]
    public void AnOCamlThatGivesNoVersionOrHasNoOcamlrunIsNotUsed()
    {
        var ocamlc = Write(Path.Combine("bin", Exe("ocamlc")));

        using (OCamlToolchains.LookingIn(new OCamlToolchains.Places(Path.Combine(_temp.Path, "opam"), _ => ocamlc, _ => "5.2.0")))
            Assert.Null(OCamlToolchains.Usual);

        Write(Path.Combine("bin", Exe("ocamlrun")));
        using (OCamlToolchains.LookingIn(new OCamlToolchains.Places(Path.Combine(_temp.Path, "opam"), _ => ocamlc, _ => null)))
            Assert.Null(OCamlToolchains.Usual);
    }

    [Fact]
    public void AProgramIsTheFileAndTheModulesItUsesEachAfterWhatItUsesWithItsInterfaceFirst()
    {
        var marks = Write(Path.Combine("week4", "marks.ml"), "let () = print_int (Stats.average Data.scores)\n");
        var stats = Write(Path.Combine("week4", "stats.ml"), "let average xs = List.fold_left ( + ) 0 xs / List.length xs\n");
        var statsInterface = Write(Path.Combine("week4", "stats.mli"), "val average : int list -> int\n");
        var data = Write(Path.Combine("week4", "data.ml"), "open Stats\nlet scores = [70; 80]\nlet _ = average\n");
        Write(Path.Combine("week4", "other.ml"), "let () = print_endline \"another exercise\"\n");

        var layout = OCamlProgram.Of(marks);

        Assert.Equal(new[] { statsInterface, stats, data, marks }, layout.Files);
        Assert.Empty(layout.Libraries);
        Assert.Null(layout.DuneFile);
    }

    [Fact]
    public void TheLibrariesThatComeWithOCamlAreNamedWhenTheCodeUsesThemAndADuneFileIsNoted()
    {
        var words = Write(Path.Combine("words", "words.ml"), "let () = print_endline (Str.global_replace (Str.regexp \"a\") \"b\" \"aaa\")\n(* Unix is only mentioned here *)\n");
        var dune = Write(Path.Combine("words", "dune"), "(executable (name words) (libraries str))\n");

        var layout = OCamlProgram.Of(words);

        Assert.Equal(new[] { "str" }, layout.Libraries);
        Assert.Equal(dune, layout.DuneFile);
    }

    [Theory]
    [InlineData(5, " -I +str str.cma")]
    [InlineData(4, " str.cma")]
    public void OCaml5sLibrariesAreInFoldersOfTheirOwn(int major, string named)
    {
        var ocaml = new OCamlToolchains.OCaml("ocamlc.exe", "ocamlrun.exe", $"{major}.1.0", major, 1, "on PATH");

        Assert.Equal($"-g{named} -o \"program.byte\" \"words.ml\"", OCamlBuild.CompileArguments(ocaml, ["words.ml"], ["str"], "program.byte"));
    }

    [Theory]
    [InlineData(null, "b")]
    [InlineData("", "b")]
    [InlineData("s=4M", "s=4M,b")]
    [InlineData("s=4M,b", "s=4M,b")]
    public void BacktracesAreTurnedOnWithWhateverElseOcamlrunparamSays(string? existing, string expected) =>
        Assert.Equal(expected, OCamlBuild.WithBacktraces(existing));

    [Fact]
    public void TheBuildCopiesTheProgramsFilesAwayFromItAndCompilesThemThere()
    {
        var build = Path.Combine(_temp.Path, "build");
        var marks = Write(Path.Combine("week4", "marks.ml"), "let () = print_int 1\n");
        var ocaml = new OCamlToolchains.OCaml(@"C:\opam\default\bin\ocamlc.exe", @"C:\opam\default\bin\ocamlrun.exe", "5.2.0", 5, 2, "on PATH");

        var batch = File.ReadAllText(OCamlBuild.WriteBatch(build, [marks], OCamlBuild.CompileArguments(ocaml, ["marks.ml"], [], OCamlBuild.Bytecode), ocaml));

        Assert.Contains($"cd /d \"{build}\"", batch);
        Assert.Contains($"copy /y \"{marks}\" \"marks.ml\" >nul", batch);
        Assert.Contains("\"C:\\opam\\default\\bin\\ocamlc.exe\" -g -o \"program.byte\" \"marks.ml\"", batch);
    }
}
