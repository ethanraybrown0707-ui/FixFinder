using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>A logic pattern made from a function that reads masked lines, rated once for everything it finds.</summary>
internal sealed class CodePattern(
    string id,
    IReadOnlySet<string> extensions,
    Syntax syntax,
    Func<string, SourceFile, IReadOnlyList<string>, IEnumerable<LogicFinding>> find,
    Severity severity = Severity.Warning,
    Confidence confidence = Confidence.Likely,
    FindingKind kind = FindingKind.Logic) : ILogicPattern
{
    public string Id => id;

    public IReadOnlySet<string> Extensions => extensions;

    public IEnumerable<LogicFinding> Find(SourceFile source) =>
        find(id, source, CodeText.MaskAll(source.Lines, syntax))
            .Select(finding => finding with
            {
                Severity = finding.Severity == Severity.Warning ? severity : finding.Severity,
                Confidence = finding.Confidence == Confidence.Likely ? confidence : finding.Confidence,
                Kind = finding.Kind == FindingKind.Logic ? kind : finding.Kind,
            });

    public static IReadOnlySet<string> Files(params string[] extensions) =>
        new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reading Python's indented blocks from masked lines.</summary>
internal static class PythonBlocks
{
    public static string Indent(string line) => CodeText.Indentation(line);

    public static (int First, int End) Body(IReadOnlyList<string> lines, int header)
    {
        var indent = Indent(lines[header]).Length;
        var end = header + 1;

        while (end < lines.Count && (lines[end].Trim().Length == 0 || Indent(lines[end]).Length > indent)) end++;
        while (end > header + 1 && lines[end - 1].Trim().Length == 0) end--;

        return (header + 1, end);
    }

    public static IEnumerable<int> Statements(IReadOnlyList<string> masked, int first, int end) =>
        Enumerable.Range(first, Math.Max(0, end - first)).Where(i => masked[i].Trim().Length > 0);

    public static int[] OpenBrackets(IReadOnlyList<string> masked)
    {
        var open = new int[masked.Count];
        var depth = 0;

        for (var i = 0; i < masked.Count; i++)
        {
            open[i] = depth;
            depth = Math.Max(0, depth + masked[i].Count(c => c is '(' or '[' or '{') - masked[i].Count(c => c is ')' or ']' or '}'));
        }

        return open;
    }

    public static HashSet<string> DefinedNames(IReadOnlyList<string> masked)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in masked)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(line,
                         @"^\s*(?<names>[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)\s*(?:[-+*/%]|//)?=(?!=)|\bfor\s+(?<names>[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)\s+in\b|\b(?:def|class)\s+(?<names>[A-Za-z_]\w*)|\bas\s+(?<names>[A-Za-z_]\w*)"))
            {
                foreach (var name in m.Groups["names"].Value.Split(',')) names.Add(name.Trim());
            }

            if (System.Text.RegularExpressions.Regex.Match(line, @"\bdef\s+\w+\s*\((?<parameters>[^)]*)\)") is { Success: true } def)
            {
                foreach (var parameter in def.Groups["parameters"].Value.Split(','))
                {
                    var name = parameter.Split('=', ':')[0].Trim().TrimStart('*');
                    if (name.Length > 0) names.Add(name);
                }
            }
        }

        return names;
    }
}
