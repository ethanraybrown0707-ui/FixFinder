using FixFinder.Core.Execution.Versions;

namespace FixFinder.Core.Execution;

/// <summary>
/// What a C# program is built with, in words: the .NET SDK dotnet uses for it, the C# its project is compiled as and why,
/// and the C# its code needs - which dotnet chooses for itself, so FixFinder says it rather than choosing.
/// </summary>
/// <param name="Explained">How a run explanation names what builds it: "the .NET SDK 10.0.400".</param>
/// <param name="Said">The sentences after it: "It is built as C# 12, as Marks.csproj targets net8.0. Its code needs C# 9 or later: ...".</param>
/// <param name="Sdk">The .NET SDK that builds it, when dotnet says which.</param>
public sealed record CSharpSetup(string Explained, string? Said, VersionedToolchain? Sdk)
{
    public static CSharpSetup? For(string csharpFile)
    {
        if (DotnetSdks.Dotnet is null) return null;

        // A copy of the program made to try a change in is built as the program is; what it needs is worked out - by
        // compiling it, which takes a moment - only for the program itself, whose run explanation is the one shown.
        var original = ProgramCopy.OriginalOf(csharpFile);
        var isACopy = !string.Equals(Path.GetFullPath(original), Path.GetFullPath(csharpFile), StringComparison.OrdinalIgnoreCase);

        var builtAs = CSharpBuiltAs.For(original);
        var folder = Path.GetDirectoryName(Path.GetFullPath(builtAs?.Project ?? original))!;
        var sdk = DotnetSdks.UsedIn(folder);
        var needs = isACopy ? null : CSharpFeaturesUsed.For(csharpFile);

        var builtAsSaid = builtAs is null ? null : $"it is built as C# {builtAs.Version}, {builtAs.Because}";
        var said = needs switch
        {
            null => builtAsSaid is null ? null : Capitalised(builtAsSaid) + ".",
            _ when builtAs is not null && builtAs.Version < needs.Version => $"Its code needs C# {needs.Version} or later: {needs.Because} - and {builtAsSaid}.",
            _ => (builtAsSaid is null ? "" : Capitalised(builtAsSaid) + ". ") + $"Its code needs C# {needs.Version} or later: {needs.Because}.",
        };

        return new CSharpSetup(sdk is null ? "dotnet" : $"the .NET SDK {sdk.VersionText}", said, sdk);
    }

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
