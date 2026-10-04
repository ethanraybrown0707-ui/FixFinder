using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Execution.Versions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// What dotnet says when a C# program asks for more than the C# or the .NET SDK it is built with: code that uses what a
/// later C# added than the one its project is compiled as, a project that targets a later .NET than the SDK can build for,
/// and a global.json that asks for an SDK that is not here - said once, as a note naming what it needs and what to change.
/// </summary>
/// <remarks>
/// Each is read from dotnet's own words: "Feature 'params collections' is not available in C# 12.0. Please use language
/// version 13.0 or greater.", "NETSDK1045: The current .NET SDK does not support targeting .NET 10.0.", and "A compatible
/// .NET SDK was not found. ... Requested SDK version: 9.0.100". That C# 13 is supported only on .NET 9 and later, and so
/// on, is Microsoft's own table of which C# each .NET comes with.
/// </remarks>
public static partial class CSharpVersionErrors
{
    [GeneratedRegex(@"Feature '(?<feature>[^']+)' is not available in C# (?<used>[\d.]+)\. Please use language version '?(?<needed>[\d.]+)'? or greater")]
    private static partial Regex FeatureNotInThisRelease();

    [GeneratedRegex(@"The current \.NET SDK does not support targeting \.NET (?<target>\d+)\.\d+")]
    private static partial Regex SdkCannotTarget();

    [GeneratedRegex(@"Requested SDK version: (?<requested>\S+)")]
    private static partial Regex RequestedSdk();

    /// <summary>The note for what dotnet said, or null when nothing it said is about the C# or the SDK the program asks for.</summary>
    public static string? NoteFor(IReadOnlyList<ParsedError> errors, IReadOnlyList<CapturedLine> output, string chosen)
    {
        if (Path.GetExtension(chosen).ToLowerInvariant() is not (".cs" or ".csproj")) return null;

        var said = output.Select(line => line.Text).Concat(errors.Select(error => error.Message ?? "")).ToList();

        if (said.Any(line => line.Contains("A compatible .NET SDK was not found", StringComparison.Ordinal)) &&
            said.Select(line => RequestedSdk().Match(line)).FirstOrDefault(match => match.Success) is { } requested)
        {
            return SdkNotHereNote(requested.Groups["requested"].Value);
        }

        if (said.Select(line => SdkCannotTarget().Match(line)).FirstOrDefault(match => match.Success) is { } cannotTarget)
            return SdkTooOldNote(int.Parse(cannotTarget.Groups["target"].Value), chosen);

        foreach (var error in errors)
        {
            if (FeatureNotInThisRelease().Match(error.Message ?? "") is not { Success: true } notInThisRelease) continue;

            var where = error.Frames.FirstOrDefault() is { File: { } file, Line: > 0 } frame ? $"{Path.GetFileName(file)}:{frame.Line}" : Path.GetFileName(chosen);
            return FeatureNote(where, notInThisRelease.Groups["feature"].Value, LanguageVersion.Find(notInThisRelease.Groups["used"].Value)!.Value,
                               LanguageVersion.Find(notInThisRelease.Groups["needed"].Value)!.Value, chosen);
        }

        // Code needing a C# no SDK here builds for: its compiler may not know the code at all, and say only that it is wrong.
        return errors.Count > 0 ? NoSdkForItsCSharpNote(chosen) : null;
    }

    private static string? NoSdkForItsCSharpNote(string chosen)
    {
        if (CSharpFeaturesUsed.For(chosen) is not { Version.Major: >= 9 and <= 13 } needs) return null;

        var dotNet = needs.Version.Major - 4;
        var installed = DotnetSdks.Installed;
        if (installed.Count == 0 || installed.Any(sdk => sdk.Version.Major >= dotNet)) return null;

        return $"{needs.Because} - and C# {needs.Version.Major} is supported only on .NET {dotNet} and later, which the newest .NET SDK on this computer, " +
               $"{installed[0].VersionText}, cannot build for, so that part of it cannot be built, which is not a mistake in the code. Installing the .NET " +
               $"{dotNet} SDK or later builds it - {InstallAdvice(dotNet)}";
    }

    /// <summary>How to install a .NET SDK: winget has one package for each .NET from 5 to 10.</summary>
    internal static string InstallAdvice(int dotNet) =>
        dotNet is >= 5 and <= 10
            ? $"for example:\n  winget install Microsoft.DotNet.SDK.{dotNet}"
            : "dotnet.microsoft.com has every .NET SDK.";

    private static string SdkNotHereNote(string requested)
    {
        var installed = DotnetSdks.Installed;
        var here = installed.Count == 0 ? "" : $" - the SDKs here are {string.Join(", ", installed.Select(sdk => sdk.VersionText))}";

        return $"Its global.json asks for the .NET SDK {requested}, and dotnet finds no SDK on this computer that the global.json allows{here} - so it was " +
               "not built, which is not a mistake in the code. Changing the version global.json asks for to one here, or installing the .NET SDK " +
               $"{requested}, builds it.";
    }

    private static string SdkTooOldNote(int target, string chosen)
    {
        var project = CSharpBuiltAs.For(chosen)?.Project ?? ProgramLayout.CSharpProject(chosen) ?? chosen;
        var used = DotnetSdks.UsedIn(Path.GetDirectoryName(Path.GetFullPath(project))!);
        var start = $"{Path.GetFileName(project)} targets .NET {target}, and {(used is null ? "the .NET SDK" : $"the .NET SDK {used.VersionText}")} that built it " +
                    $"cannot build for .NET {target} - so it was not built, which is not a mistake in the code.";

        // dotnet builds with the newest SDK here unless a global.json asks for another: a newer one here was passed over for that.
        if (DotnetSdks.Installed.FirstOrDefault(sdk => sdk.Version.Major >= target) is { } newer)
        {
            return $"{start} The .NET SDK {newer.VersionText} is on this computer and can" +
                   (DotnetSdks.GlobalJsonAbove(Path.GetDirectoryName(Path.GetFullPath(project))!) is not null
                       ? ": changing the version its global.json asks for to it builds it."
                       : ".");
        }

        return $"{start} Installing the .NET {target} SDK or later builds it - {InstallAdvice(target)}";
    }

    private static string FeatureNote(string where, string feature, LanguageVersion used, LanguageVersion needed, string chosen)
    {
        var builtAs = CSharpBuiltAs.For(chosen);
        var start = $"{where} uses {feature}, which C# {needed.MajorOnly} added - and it is built as C# {(used.Minor > 0 ? used : used.MajorOnly)}" +
                    (builtAs is null ? "" : $", {builtAs.Because}") + ", so that part of it cannot be built, which is not a mistake in the code.";

        if (builtAs is null) return start;

        var project = builtAs.DecidedIn;

        if (builtAs.LangVersionSet) return $"{start} Raising the LangVersion in {project} to {needed.MajorOnly} builds it.";

        // C# 9 came with .NET 5, and each C# since with the .NET after: targeting that .NET builds it as that C#.
        if (builtAs.TargetFramework is { } targetFramework && CSharpBuiltAs.DotNetOf(targetFramework) is not null && needed.Major >= 9)
        {
            var dotNet = needed.Major - 4;
            var sdkHere = DotnetSdks.Installed.FirstOrDefault(sdk => sdk.Version.Major >= dotNet);

            return $"{start} C# {needed.Major} is supported on .NET {dotNet} and later: targeting net{dotNet}.0 in {project} builds it" +
                   (sdkHere is not null ? $", with the .NET SDK {sdkHere.VersionText} here." : $" - and needs the .NET {dotNet} SDK or later, {InstallAdvice(dotNet)}");
        }

        return $"{start} Setting <LangVersion>{needed.MajorOnly}</LangVersion> in {project} builds it, though Microsoft does not support a C# newer than " +
               "the one its target framework comes with.";
    }
}
