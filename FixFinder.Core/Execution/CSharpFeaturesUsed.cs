using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution.Versions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using LanguageVersion = FixFinder.Core.Execution.Versions.LanguageVersion;
using RoslynLanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion;

namespace FixFinder.Core.Execution;

/// <summary>
/// The C# a program's own code needs, worked out by the C# compiler itself: the program is compiled as C# 7.3, 8, 9 and
/// so on to 13, and the oldest that compiles it with no error it does not also have as the newest C# is the C# it needs.
/// The compiler's own words, as the C# before that one says them, name what it needs and where.
/// </summary>
/// <remarks>
/// <para>
/// The program is compiled whole, as the compiler checks most of the language only when it binds the code, and with this
/// computer's .NET libraries, so the program's own mistakes - and a library it uses that is not here - are errors at every
/// C# alike, and decide nothing. The oldest C# that compiles it is looked for, rather than the newest C# the compiler asks
/// for as C# 7.3, because older C# reads some newer code as something else: a record struct read as C# 7.3 looks like a
/// method with a primary constructor, which C# 12 added, though record structs are C# 10's.
/// </para>
/// <para>
/// Nothing C# 7.3 already has is counted - it is what every .NET Framework project is built as. What this compiler knows
/// only as a preview - parts of C# 14 - is said as a C# later than 13, and what it does not know at all is not counted.
/// </para>
/// </remarks>
public static partial class CSharpFeaturesUsed
{
    /// <summary>The C# releases looked at, oldest first, with how they are written: 7.3, then 8 to 13.</summary>
    private static readonly (RoslynLanguageVersion Compiler, LanguageVersion Version)[] Releases =
    [
        (RoslynLanguageVersion.CSharp7_3, new LanguageVersion(7, 3)),
        (RoslynLanguageVersion.CSharp8, new LanguageVersion(8)),
        (RoslynLanguageVersion.CSharp9, new LanguageVersion(9)),
        (RoslynLanguageVersion.CSharp10, new LanguageVersion(10)),
        (RoslynLanguageVersion.CSharp11, new LanguageVersion(11)),
        (RoslynLanguageVersion.CSharp12, new LanguageVersion(12)),
        (RoslynLanguageVersion.CSharp13, new LanguageVersion(13)),
    ];

    /// <summary>A C# later than the last release this compiler has - which its previews are part of.</summary>
    private static readonly LanguageVersion AfterTheLast = new(14);

    /// <summary>The libraries of the .NET FixFinder runs on, which every compile is given.</summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> Libraries = new(() =>
        ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(library => library.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(library => (MetadataReference)MetadataReference.CreateFromFile(library))
            .ToList());

    /// <summary>The compiler asking for a later C#: "Feature 'records' is not available in C# 8.0. Please use language version 9.0 or greater."</summary>
    [GeneratedRegex(@"Feature '(?<feature>[^']+)' is not available in C# [\d.]+\. Please use language version '?(?<needed>[\d.]+)'? or greater")]
    private static partial Regex FeatureNotInThisRelease();

    /// <summary>The compiler naming one of its previews: "The feature 'null conditional assignment' is currently in Preview and *unsupported*."</summary>
    [GeneratedRegex(@"The feature '(?<feature>[^']+)' is currently in Preview")]
    private static partial Regex FeatureInPreview();

    private static readonly ConcurrentDictionary<string, ToolchainChoice.AtLeast?> Remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The C# the program this file is part of needs, and why; or null when C# 7.3 has everything it uses.</summary>
    /// <param name="csharpFile">A .cs file, or a project's .csproj, whose .cs files are its program.</param>
    public static ToolchainChoice.AtLeast? For(string csharpFile)
    {
        IEnumerable<string> files;

        try
        {
            files = Path.GetExtension(csharpFile).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                ? CSharpBuiltAs.SourcesOf(csharpFile)
                : ProgramFiles.Of(csharpFile).Prepend(csharpFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [csharpFile];
        }

        return Of(files.Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static ToolchainChoice.AtLeast? Of(IEnumerable<string> csharpFiles)
    {
        var sources = new List<(string Path, string Text, DateTime Written)>();

        foreach (var file in csharpFiles)
        {
            try
            {
                sources.Add((Path.GetFullPath(file), File.ReadAllText(file), File.GetLastWriteTimeUtc(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (sources.Count == 0) return null;

        var key = string.Join("|", sources.Select(source => $"{source.Path}@{source.Written.Ticks}"));
        return Remembered.GetOrAdd(key, _ => Needed(sources.Select(source => (source.Path, source.Text)).ToList()));
    }

    private static ToolchainChoice.AtLeast? Needed(IReadOnlyList<(string Path, string Text)> sources)
    {
        // What is wrong with the code whatever C# it is read as: what the newest C# this compiler has still says.
        var always = Errors(sources, RoslynLanguageVersion.Preview).Select(Place).ToHashSet();
        var said = new Dictionary<int, IReadOnlyList<Diagnostic>>();

        IReadOnlyList<Diagnostic> OnlyAs(int release)
        {
            if (!said.TryGetValue(release, out var errors))
                said[release] = errors = Errors(sources, Releases[release].Compiler).Where(error => !always.Contains(Place(error))).ToList();

            return errors;
        }

        if (OnlyAs(0).Count == 0) return null;

        // The oldest release that compiles it - each later one compiles whatever an earlier one does.
        var oldest = Releases.Length;
        for (int low = 1, high = Releases.Length - 1; low <= high;)
        {
            var middle = (low + high) / 2;
            if (OnlyAs(middle).Count == 0)
            {
                oldest = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        // What the release before it says it lacks - in its own words, where they name the release that added it.
        var lacking = OnlyAs(oldest - 1);

        var previous = Releases[oldest - 1].Version;

        if (oldest == Releases.Length)
        {
            var preview = lacking.Select(error => (Error: error, Feature: FeatureInPreview().Match(error.GetMessage(CultureInfo.InvariantCulture)))).FirstOrDefault(found => found.Feature.Success);
            var because = preview.Error is null
                ? $"{Name(lacking[0])} has code at line {Line(lacking[0])} that C# {previous} cannot compile"
                : $"{Name(preview.Error)} uses {preview.Feature.Groups["feature"].Value} at line {Line(preview.Error)}, which no C# up to {previous} has";

            return new ToolchainChoice.AtLeast(AfterTheLast, because);
        }

        var version = Releases[oldest].Version;
        var named = lacking.Select(error => (Error: error, Feature: FeatureNotInThisRelease().Match(error.GetMessage(CultureInfo.InvariantCulture))))
            .FirstOrDefault(found => found.Feature.Success && LanguageVersion.Find(found.Feature.Groups["needed"].Value) is { } asked && asked.Major == version.Major);

        return new ToolchainChoice.AtLeast(version, named.Error is null
            ? $"{Name(lacking[0])} has code at line {Line(lacking[0])} that C# {previous} cannot compile and C# {version} can"
            : $"{Name(named.Error)} uses {named.Feature.Groups["feature"].Value} at line {Line(named.Error)}, which C# {version} added");
    }

    private static IReadOnlyList<Diagnostic> Errors(IReadOnlyList<(string Path, string Text)> sources, RoslynLanguageVersion release)
    {
        var options = new CSharpParseOptions(release);
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, options, source.Path)).ToList();
        var compilation = CSharpCompilation.Create("program", trees, Libraries.Value, new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        return compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.IsInSource).ToList();
    }

    private static (string Id, string File, int Start) Place(Diagnostic error) =>
        (error.Id, error.Location.SourceTree?.FilePath ?? "", error.Location.SourceSpan.Start);

    private static string Name(Diagnostic error) => Path.GetFileName(error.Location.SourceTree?.FilePath ?? "");

    private static int Line(Diagnostic error) => error.Location.GetLineSpan().StartLinePosition.Line + 1;
}
