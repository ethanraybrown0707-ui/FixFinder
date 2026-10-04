using System.Text.RegularExpressions;
using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The Go a program's module declares, read from its go.mod as the go command reads it: the go line - the oldest Go that
/// builds the module, which every Go from 1.21 on holds to - and the toolchain line, the Go it suggests; and, in a
/// workspace, go.work's go line, which is at least every module's.
/// </summary>
/// <remarks>
/// The go command looks for go.mod in the folder it runs in and the folders above it, and for go.work the same way unless
/// GOWORK says otherwise. A go.mod without a go line is taken as go 1.16, and a go.work without one as go 1.18, as
/// go.dev/doc/toolchain sets out.
/// </remarks>
public static partial class DeclaredGo
{
    /// <summary>What a program's go.mod - and go.work, in a workspace - declares.</summary>
    /// <param name="Module">The go.mod that holds the program.</param>
    /// <param name="GoLine">Its go line's version, or null when it has none.</param>
    /// <param name="Toolchain">Its toolchain line's version - the Go it suggests - when it has one.</param>
    /// <param name="Workspace">The go.work the go command uses with it, when there is one.</param>
    /// <param name="WorkspaceGoLine">That go.work's go line's version, when it has one.</param>
    public sealed record Declared(string Module, LanguageVersion? GoLine, LanguageVersion? Toolchain, string? Workspace, LanguageVersion? WorkspaceGoLine)
    {
        /// <summary>The Go the module's code is compiled as: its go line, or 1.16 for a go.mod without one.</summary>
        public LanguageVersion LanguageVersion => GoLine ?? new LanguageVersion(1, 16);

        /// <summary>What the Go that builds it has to be at least, and why: the go line, and go.work's, which every Go from 1.21 on refuses to build without.</summary>
        public IReadOnlyList<ToolchainChoice.AtLeast> AtLeast =>
        [
            .. GoLine is { } goLine ? [new ToolchainChoice.AtLeast(goLine, $"its go.mod says go {goLine}")] : Array.Empty<ToolchainChoice.AtLeast>(),
            .. WorkspaceGoLine is { } workspaceGoLine && (GoLine is null || workspaceGoLine > GoLine.Value)
                ? [new ToolchainChoice.AtLeast(workspaceGoLine, $"its go.work says go {workspaceGoLine}")]
                : Array.Empty<ToolchainChoice.AtLeast>(),
        ];
    }

    /// <summary>The go line - go 1.22, go 1.27.0, or a release candidate's go 1.21rc1, which comes before 1.21.0 and so asks for no more than it.</summary>
    [GeneratedRegex(@"^[ \t]*go[ \t]+(?<version>\d+\.\d+(?:\.\d+)?)", RegexOptions.Multiline)]
    private static partial Regex GoLine();

    [GeneratedRegex(@"^[ \t]*toolchain[ \t]+go(?<version>\d+\.\d+(?:\.\d+)?)", RegexOptions.Multiline)]
    private static partial Regex ToolchainLine();

    /// <summary>What the module of this Go file declares, or null when the file is in no module.</summary>
    public static Declared? Of(string goFile)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(goFile));
        if (InThisOrAbove(folder, "go.mod") is not { } module || Text(module) is not { } moduleText) return null;

        var workspace = WorkspaceFor(folder);
        var workspaceText = workspace is null ? null : Text(workspace);

        return new Declared(
            module,
            VersionOf(GoLine(), moduleText),
            VersionOf(ToolchainLine(), moduleText),
            workspaceText is null ? null : workspace,
            workspaceText is null ? null : VersionOf(GoLine(), workspaceText) ?? new LanguageVersion(1, 18));
    }

    /// <summary>The go.work the go command would use from this folder: the one GOWORK names, none when it is off, or the nearest above.</summary>
    private static string? WorkspaceFor(string? folder) =>
        Environment.GetEnvironmentVariable("GOWORK") switch
        {
            "off" => null,
            { Length: > 0 } named => File.Exists(named) ? named : null,
            _ => InThisOrAbove(folder, "go.work"),
        };

    private static LanguageVersion? VersionOf(Regex line, string text) =>
        line.Match(text) is { Success: true } found ? LanguageVersion.FindWithPatch(found.Groups["version"].Value) : null;

    private static string? InThisOrAbove(string? folder, string fileName)
    {
        for (var directory = folder is null ? null : new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string? Text(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
