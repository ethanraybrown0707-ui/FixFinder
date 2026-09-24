using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking.Guides;

/// <summary>Finds the guide for a mistake: by the rule that fixed it, then by the error, then by the kind of mistake.</summary>
public static class Guidebook
{
    public static MistakeGuide For(string file, FindingKind kind, string? ruleId = null, ParsedError? error = null)
    {
        var table = TableFor(file);

        if (ruleId is { Length: > 0 })
        {
            var byRule = table.Concat(LogicGuides.All).FirstOrDefault(entry => entry.RuleIds.Contains(ruleId, StringComparer.Ordinal));
            if (byRule is not null) return byRule.Guide;
        }

        if (error is not null && table.FirstOrDefault(entry => entry.Describes(error)) is { } byError) return byError.Guide;

        return GeneralGuides.For(LanguageName(file), kind);
    }

    public static bool HasRule(string ruleId) =>
        AllTables.Any(table => table.Any(entry => entry.RuleIds.Contains(ruleId, StringComparer.Ordinal)));

    /// <summary>Every guide FixFinder has, for checks that hold of all of them rather than of one.</summary>
    public static IEnumerable<MistakeGuide> Every() => AllTables.SelectMany(table => table).Select(entry => entry.Guide);

    internal static IEnumerable<IReadOnlyList<GuideEntry>> AllTables =>
    [
        PythonGuides.All, JavaGuides.All, CSharpGuides.All, NativeGuides.All, JavaScriptGuides.All, GoGuides.All, LogicGuides.All,
    ];

    private static readonly IReadOnlyList<GuideEntry> JavaTable = [.. JavaGuides.All, .. AnalysisGuides.Java];
    private static readonly IReadOnlyList<GuideEntry> CSharpTable = [.. CSharpGuides.All, .. AnalysisGuides.CSharp];
    private static readonly IReadOnlyList<GuideEntry> NativeTable = [.. NativeGuides.All, .. AnalysisGuides.Native];
    private static readonly IReadOnlyList<GuideEntry> JavaScriptTable = [.. JavaScriptGuides.All, .. AnalysisGuides.JavaScript];
    private static readonly IReadOnlyList<GuideEntry> GoTable = [.. GoGuides.All, .. AnalysisGuides.Go];

    private static IReadOnlyList<GuideEntry> TableFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".py" or ".pyw" => PythonGuides.All,
        ".java" => JavaTable,
        ".cs" => CSharpTable,
        ".c" or ".h" or ".cpp" or ".cc" or ".cxx" or ".c++" or ".hpp" or ".hh" or ".hxx" => NativeTable,
        ".js" or ".mjs" or ".cjs" => JavaScriptTable,
        ".go" => GoTable,
        _ => [],
    };

    public static string LanguageName(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".py" or ".pyw" => "Python",
        ".java" => "Java",
        ".cs" => "C#",
        ".c" or ".h" => "C",
        ".cpp" or ".cc" or ".cxx" or ".c++" or ".hpp" or ".hh" or ".hxx" => "C++",
        ".js" or ".mjs" or ".cjs" => "JavaScript",
        ".go" => "Go",
        _ => "the program",
    };
}
