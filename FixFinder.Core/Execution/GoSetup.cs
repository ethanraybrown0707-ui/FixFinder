using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Go a program is built and run with, and the words that say which and why: the Go a terminal runs when it is new
/// enough for the module's go line and the code's needs, and otherwise the oldest Go here that is - or, when the module's
/// toolchain line names a later Go than the usual one, a Go of that, as the go command itself would run.
/// </summary>
/// <param name="Go">The go.exe that builds and runs it.</param>
/// <param name="Explained">How a run explanation names it, with why that one: "Go 1.22.5 (golang.org/dl's), as its go.mod says go 1.22.0".</param>
/// <param name="CodeNeeds">What the code needs, as a sentence of its own, when nothing else said it.</param>
/// <param name="Toolchain">The Go, with its version.</param>
public sealed record GoSetup(string Go, string Explained, string? CodeNeeds, VersionedToolchain Toolchain)
{
    private const string Language = "Go";

    /// <summary>
    /// What every go command FixFinder starts is given, as FixFinder never downloads anything: GOTOOLCHAIN=local, so the
    /// Go chosen is the Go that runs - a go command left to itself downloads and runs a later Go when the module asks for
    /// one; GOPROXY=off, so a module its go.mod requires that is not in Go's module cache is not downloaded, from a proxy
    /// or - for a private one - from where its code is kept; and GOSUMDB=off, as with no proxy the go command would reach
    /// the checksum database directly whenever it adds a module's checksum to go.sum. A build never adds one unless GOFLAGS
    /// asks for -mod=mod, and the modules it could add one for are only those already in the cache.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string>
    {
        ["GOTOOLCHAIN"] = "local",
        ["GOPROXY"] = "off",
        ["GOSUMDB"] = "off",
    };

    public static GoSetup? For(string goFile)
    {
        // A copy made to try a fix in is built with the Go the program itself is built with: what the program's own code
        // needs decides, so a fix is tried on the version it will run on, never on a later one it would ask for itself.
        var codeNeeds = GoFeaturesUsed.For(ProgramCopy.OriginalOf(goFile));

        // A copy of the program made to try a change in has the change; its module's files are read where it came from.
        var declared = DeclaredGo.Of(ProgramCopy.OriginalOf(goFile));
        var atLeast = (declared?.AtLeast ?? []).Concat(codeNeeds is null ? [] : [codeNeeds]).ToList();

        var usual = GoToolchains.Usual;

        // The go command runs the Go a toolchain line names when it is later than its own: so does FixFinder, when that Go is here.
        if (declared?.Toolchain is { } suggested && (usual is null || usual.Version < suggested))
        {
            var installed = GoToolchains.Installed;
            if (installed.Any(toolchain => toolchain.Version >= suggested && atLeast.All(need => toolchain.Version >= need.Version)))
                atLeast.Add(new ToolchainChoice.AtLeast(suggested, $"its go.mod's toolchain line names go{suggested}"));

            return Chosen(ToolchainChoice.Choose(Language, installed, usual, atLeast, [], codeNeeds));
        }

        var usualFits = usual is not null && atLeast.All(need => usual.Version >= need.Version);

        // Every Go on the computer is looked for only when the usual one will not do.
        return Chosen(ToolchainChoice.Choose(Language, usualFits ? [usual!] : GoToolchains.Installed, usual, atLeast, [], codeNeeds));
    }

    private static GoSetup? Chosen(ToolchainChoice.Chosen? chosen) =>
        chosen is null ? null : new GoSetup(chosen.Toolchain.Program, chosen.Explained, chosen.CodeNeeds, chosen.Toolchain);
}
