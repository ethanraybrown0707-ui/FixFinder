using System.Text;
using System.Text.Json;

namespace FixFinder.Core.Checking;

/// <summary>How findings are written for another program to read.</summary>
public enum DiagnosticFormat
{
    /// <summary>
    /// <c>file(line): error CODE: message</c> - what MSBuild writes, and what Visual Studio, Rider and VS Code's built-in
    /// $msCompile matcher all read into their list of problems.
    /// </summary>
    MsBuild,

    /// <summary><c>file:line:column: error: message</c> - what gcc writes, and what most other tools expect.</summary>
    Gcc,

    /// <summary>Everything a finding says, for a tool that wants more than a line.</summary>
    Json,
}

/// <summary>
/// Findings as the lines an editor reads compiler output from, so FixFinder shows up in the list of problems of
/// whatever editor somebody uses, with a click taking them to the line.
/// </summary>
/// <remarks>
/// This is how every linter reaches every editor: not a plugin for each one, but output in a form they already
/// understand. Editors read it line by line with a pattern, so each finding is exactly one line - an explanation that
/// runs over several would be cut off at the first break - and the path is absolute, so the editor can open it from
/// wherever it was started.
/// </remarks>
public static class DiagnosticLines
{
    /// <summary>
    /// A short code for a rule, of the form MSBuild uses: letters then digits. Worked out from the rule's own name, so the
    /// same rule has the same code in every run and every version, and the full name is given in the message as well.
    /// </summary>
    public static string Code(string ruleId)
    {
        // FNV-1a over the name. string.GetHashCode is different in every process, which would give a rule a new code
        // each time FixFinder ran.
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(ruleId))
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return $"FF{1000 + hash % 9000}";
    }

    /// <summary>One finding as one line, at the depth of explanation asked for.</summary>
    public static string Line(Finding finding, DiagnosticFormat format, ExplanationLevel level = ExplanationLevel.Student)
    {
        var title = finding.Title.TrimEnd();
        var joined = title.Length > 0 && title[^1] is '.' or '!' or '?' or ':' ? $"{title} " : $"{title}. ";
        var message = OneLine(joined + finding.Explanations.At(level));
        var rule = finding.RuleId.Length > 0 ? $" [{finding.RuleId}]" : "";
        var line = finding.Line ?? 1;

        return format switch
        {
            DiagnosticFormat.Gcc => $"{finding.File}:{line}:1: {GccSeverity(finding.Severity)}: {message}{rule}",
            _ => $"{finding.File}({line}): {MsBuildSeverity(finding.Severity)} {Code(finding.RuleId)}: {message}{rule}",
        };
    }

    /// <summary>Every finding, with everything it says, as JSON.</summary>
    public static string Json(IEnumerable<Finding> findings, ExplanationLevel level = ExplanationLevel.Student) =>
        JsonSerializer.Serialize(findings.Select(finding => new
        {
            file = finding.File,
            line = finding.Line,
            severity = finding.Severity.ToString().ToLowerInvariant(),
            confidence = finding.Confidence.ToString().ToLowerInvariant(),
            kind = finding.Kind.ToString().ToLowerInvariant(),
            rule = finding.RuleId,
            code = Code(finding.RuleId),
            title = finding.Title,
            explanation = finding.Explanations.At(level),
            whyItMatters = finding.WhyItMatters,
            suggestedFix = finding.SuggestedFix,
            verified = finding.Verified.Summary,
        }), Engine.JsonOptions.Default);

    /// <summary>
    /// A suggestion is information, not a problem, and MSBuild has a word for that. gcc does not - it has only warning
    /// and error - so there a suggestion is a note, which editors show beside the output but not as a problem.
    /// </summary>
    private static string MsBuildSeverity(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };

    private static string GccSeverity(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "note",
    };

    /// <summary>Everything on one line, with the backticks that mark code in the window taken off.</summary>
    private static string OneLine(string text) =>
        string.Join(" ", text.Replace('`', '\'').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));
}
