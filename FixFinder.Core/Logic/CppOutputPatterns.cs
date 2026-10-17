using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// What C++ writes for an array sent to cout - run with g++, which said nothing: std::cout &lt;&lt; marks, for int marks[] =
/// {70, 80, 90}, printed where the array is in memory, 0x1a535ffcf8, not its items. A char array is text, and prints as text.
/// </summary>
public static partial class CppOutputPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-cpp-array-printed", CodePattern.Files(".cpp", ".cc", ".cxx", ".c++"), Syntax.CLike, ArrayPrinted,
            Severity.Error, Confidence.Certain),
    ];

    /// <summary><c>int marks[] = {</c> - an array of anything but char, whose items cout does not write one by one.</summary>
    [GeneratedRegex(@"\b(?<type>(?:unsigned\s+|signed\s+|long\s+|short\s+)*(?:int|long|short|double|float|bool|size_t)|(?:std::)?string)\s+(?<name>[A-Za-z_]\w*)\s*\[[^\]]*\]")]
    private static partial Regex ArrayDeclared();

    /// <summary>A whole statement that starts with cout: <c>std::cout &lt;&lt; "Marks: " &lt;&lt; marks &lt;&lt; "\n";</c>.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<cout>(?:std::)?cout)(?<parts>\s*<<.*);\s*$")]
    private static partial Regex CoutStatement();

    /// <summary>
    /// An array of numbers, bools or strings sent to cout with &lt;&lt;: cout writes where the array is, not its items. The
    /// change writes each item in a loop, and the rest of the statement before and after it as it was.
    /// </summary>
    private static IEnumerable<LogicFinding> ArrayPrinted(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var arrays = masked
            .SelectMany(code => ArrayDeclared().Matches(code))
            .GroupBy(found => found.Groups["name"].Value)
            .ToDictionary(group => group.Key, group => group.First().Groups["type"].Value, StringComparer.Ordinal);
        if (arrays.Count == 0) yield break;

        var namesInCode = masked.SelectMany(code => Regex.Matches(code, @"[A-Za-z_]\w*")).Select(found => found.Value).ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < masked.Count; index++)
        {
            if (CoutStatement().Match(masked[index]) is not { Success: true } statement) continue;

            var parts = statement.Groups["parts"];
            var sent = Regex.Matches(parts.Value, @"<<\s*(?<name>[A-Za-z_]\w*)\s*(?=<<|$)")
                .FirstOrDefault(found => arrays.ContainsKey(found.Groups["name"].Value));
            if (sent is null) continue;

            var line = source.Lines[index];
            var name = sent.Groups["name"].Value;
            var type = arrays[name];
            var indent = statement.Groups["indent"].Value;
            var cout = statement.Groups["cout"].Value;

            // The loop's variable: the array's name without its s - mark for marks - unless the code uses that name already.
            var item = new[] { name.EndsWith('s') && name.Length > 1 ? name[..^1] : "", "item", "each" }
                .FirstOrDefault(candidate => candidate.Length > 0 && !namesInCode.Contains(candidate));

            LocalFix? fix = null;
            if (item is not null)
            {
                var before = line.Substring(parts.Index, sent.Index).TrimEnd();
                var after = line.Substring(parts.Index + sent.Index + sent.Length, parts.Length - sent.Index - sent.Length).Trim();

                var written = new List<string>();
                if (before.Length > 0) written.Add($"{indent}{cout}{before};");
                written.Add($"{indent}for ({type} {item} : {name}) {cout} << {item} << \" \";");
                if (after.Length > 0) written.Add($"{indent}{cout} {after};");

                fix = new LocalFix
                {
                    RuleId = id,
                    Title = $"Write each item: for ({type} {item} : {name}) {cout} << {item} << \" \";",
                    Explanation = $"A loop over {name} gives each item in turn, and cout writes each one as a number - or as text, for " +
                                  "strings - with a space after it.",
                    File = source.Path,
                    StartLine = index + 1,
                    RemoveCount = 1,
                    NewLines = written,
                };
            }

            yield return new LogicFinding(id, index + 1,
                $"{name} is an array, and cout << {name} writes where the array is in memory - such as 0x1a535ffcf8 - not its items: an " +
                "array sent to cout becomes the address of its first item",
                fix);
        }
    }
}
