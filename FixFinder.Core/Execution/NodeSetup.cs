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
        // A copy made to try a fix in runs with the Node.js the program itself runs with: what the program's own code needs
        // decides, so a fix is tried on the version it will run on, never on a later one it would ask for itself.
        var codeNeeds = JavaScriptFeaturesUsed.For(ProgramCopy.OriginalOf(javaScriptFile));

        // A Node.js chosen in Settings holds over the rest: the one a terminal runs when it is of that release, else the
        // newest of it here - and with none of it here, nothing, as the program is then not run with another.
        if (LanguageStandards.Current.NodeRelease is { } chosenInSettings)
        {
            var usualNode = Nodes.Usual;
            var chosenNode = usualNode is not null && usualNode.Version.Major == chosenInSettings.Major
                ? usualNode
                : Nodes.Installed.FirstOrDefault(each => each.Version.Major == chosenInSettings.Major);

            return chosenNode is null ? null
                : new NodeSetup(chosenNode.Program, $"Node.js {chosenNode.VersionText} ({chosenNode.FoundIn}), as Node.js {chosenInSettings} is chosen in Settings",
                                ToolchainChoice.Said(Language, chosenNode, codeNeeds), chosenNode);
        }

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
