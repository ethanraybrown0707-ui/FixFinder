using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Node.js a JavaScript program runs with, and the words that say which and why: the Node.js a terminal runs when it
/// is new enough for what the project declares and the code needs, and otherwise the oldest Node.js here that is.
/// </summary>
/// <param name="Node">The node.exe that runs it.</param>
/// <param name="Explained">How a run explanation names it, with why that one: "Node.js 22.23.2 (nvm's), as app.js calls Promise.withResolvers ...".</param>
/// <param name="CodeNeeds">What the code needs, as a sentence of its own, when nothing else said it.</param>
/// <param name="Toolchain">The Node.js, with its version.</param>
public sealed record NodeSetup(string Node, string Explained, string? CodeNeeds, VersionedToolchain Toolchain)
{
    private const string Language = "Node.js";

    public static NodeSetup? For(string javaScriptFile)
    {
        var codeNeeds = JavaScriptFeaturesUsed.For(javaScriptFile);

        // A copy of the program made to try a change in has the change; its project's files are read where it came from.
        var declared = DeclaredNode.Of(ProgramCopy.OriginalOf(javaScriptFile));
        var atLeast = new[] { declared?.AtLeast, codeNeeds }.OfType<ToolchainChoice.AtLeast>().ToList();
        var atMost = new[] { declared?.AtMost }.OfType<ToolchainChoice.AtMost>().ToList();

        var usual = Nodes.Usual;
        var usualFits = usual is not null && atLeast.All(need => usual.Version >= need.Version) && atMost.All(limit => usual.Version <= limit.Version);

        // Every Node.js on the computer is looked for only when the usual one will not do.
        var installed = usualFits ? [usual!] : Nodes.Installed;

        return ToolchainChoice.Choose(Language, installed, usual, atLeast, atMost, codeNeeds) is { } chosen
            ? new NodeSetup(chosen.Toolchain.Program, chosen.Explained, chosen.CodeNeeds, chosen.Toolchain)
            : null;
    }

    /// <summary>
    /// How to install a Node.js of at least this major release: winget's package of the newest long-term support release
    /// has every release up to 24, and its package of the newest release has every release there is.
    /// </summary>
    internal static string InstallAdvice(LanguageVersion needed) =>
        needed.Major <= 24
            ? "for example:\n  winget install OpenJS.NodeJS.LTS\nwhich installs the newest long-term support release"
            : "nodejs.org has every release, and winget installs the newest with:\n  winget install OpenJS.NodeJS";
}
