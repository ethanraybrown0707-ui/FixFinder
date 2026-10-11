namespace FixFinder.Core.Execution;

/// <summary>
/// Builds a Rust program with rustc and runs what it built. rustc writes the program into FixFinder's own build folder,
/// so nothing is written into the program's; it is built as cargo builds a debug build - no optimising, with the checks
/// that stop a program when a whole number overflows - and says what it found as JSON, one message a line, which keeps
/// its suggested changes with each message.
/// </summary>
public static class RustBuild
{
    /// <summary>The program rustc builds, in the build folder.</summary>
    public const string Program = "program.exe";

    public static (BuildAndRun? Plan, string? Problem) Prepare(string source, string output, TimeSpan timeout)
    {
        if (RustToolchains.Usual is not { } rustc)
        {
            return (null,
                $"{Path.GetFileName(source)} is Rust, which has to be compiled before it can run, and no rustc was found - not on PATH, " +
                "nor in the .cargo\\bin folder rustup installs into.\n\nInstall Rust with rustup (rustup.rs).");
        }

        var layout = RustProgram.Of(source);
        var edition = EditionFor(layout, rustc);

        if (!rustc.Builds(edition))
        {
            return (null,
                $"{Path.GetFileName(layout.CargoFile)} asks for Rust {edition}, and {rustc.Description} builds editions up to " +
                $"{rustc.NewestEdition}.\n\nUpdate Rust with rustup update.");
        }

        var compile = new TargetSpec
        {
            ExecutablePath = rustc.Program,
            Arguments = CompileArguments(edition, Path.GetRelativePath(layout.Folder, layout.Root), Path.Combine(output, Program)),
            WorkingDirectory = layout.Folder,
            Timeout = timeout,
        };

        var run = new TargetSpec
        {
            ExecutablePath = Path.Combine(output, Program),
            Arguments = "",
            WorkingDirectory = layout.Folder,
            Timeout = timeout,
        };

        return (new BuildAndRun(compile, run, Explained(rustc, layout, edition)), null);
    }

    /// <summary>The edition a program is built as: its Cargo.toml's - 2015 when it gives none, as cargo takes it - or the newest.</summary>
    public static string EditionFor(RustProgram.Layout layout, RustToolchains.Rustc rustc) =>
        layout.CargoFile is null ? rustc.NewestEdition : layout.Edition ?? "2015";

    /// <summary>rustc's arguments: the edition, its messages as JSON with no colour, debugging information, and where to write.</summary>
    public static string CompileArguments(string edition, string root, string outputFile) =>
        $"--edition {edition} --error-format=json --color never -g -o \"{outputFile}\" \"{root}\"";

    private static string Explained(RustToolchains.Rustc rustc, RustProgram.Layout layout, string edition)
    {
        var why = layout.CargoFile is null ? "the newest edition it builds"
            : layout.Edition is null ? "as its Cargo.toml gives no edition, which cargo takes as 2015"
            : "the edition its Cargo.toml gives";
        var together = layout.Files.Count > 1 ? $" with {string.Join(", ", layout.Files.Skip(1).Select(Path.GetFileName))}" : "";
        var crates = layout.Crates.Count == 0 ? ""
            : $" FixFinder builds it with rustc and never runs cargo, which would download them, so the crates its Cargo.toml names - " +
              $"{string.Join(", ", layout.Crates)} - are not built with it, and code that uses them does not build here.";

        return $"Building {Path.GetFileName(layout.Root)}{together} with {rustc.Description}, as Rust {edition} - {why} - then running it.{crates}";
    }
}
