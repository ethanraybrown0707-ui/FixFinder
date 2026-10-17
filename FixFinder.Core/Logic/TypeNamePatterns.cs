using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// A value's type compared with a name it can never be - each run with its language's own toolchain, which said nothing:
/// Node's typeof gives one of eight words, never "array", so typeof marks === "array" was false for an array; and Python's
/// type(age) gives the type itself, never its name as text, so type(age) == "int" was false for 21.
/// </summary>
public static partial class TypeNamePatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-js-typeof-never-gives", CodePattern.Files(".js", ".mjs", ".cjs"), Syntax.CLike, TypeofNeverGives, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-type-compared-with-text", CodePattern.Files(".py", ".pyw"), Syntax.Python, TypeComparedWithText, Severity.Error, Confidence.Certain),
    ];

    /// <summary>Every word JavaScript's typeof gives - for a value of any kind, one of these eight.</summary>
    private static readonly string[] TypeofWords = ["undefined", "object", "boolean", "number", "bigint", "string", "symbol", "function"];

    /// <summary>The Python types whose names a beginner writes as text, by each name written - "string" for str among them.</summary>
    private static readonly Dictionary<string, string> PythonTypes = new(StringComparer.Ordinal)
    {
        ["int"] = "int", ["integer"] = "int", ["float"] = "float", ["str"] = "str", ["string"] = "str", ["bool"] = "bool",
        ["boolean"] = "bool", ["list"] = "list", ["dict"] = "dict", ["tuple"] = "tuple", ["set"] = "set",
    };

    /// <summary><c>typeof marks === "</c> - the value, the comparison and the quote that opens the word compared with.</summary>
    [GeneratedRegex(@"\btypeof\s+(?<value>[A-Za-z_$][\w$.]*(?:\[[^\]]*\])*|\([^()]*\))\s*(?<op>===|!==|==|!=)\s*(?<quote>[""'])")]
    private static partial Regex TypeofComparison();

    /// <summary><c>type(age) == "</c> - the value, the comparison and the quote that opens the text compared with.</summary>
    [GeneratedRegex(@"\btype\((?<value>[^()]+)\)\s*(?<op>==|!=|is not|is)\s*(?<quote>[""'])")]
    private static partial Regex PythonTypeComparison();

    /// <summary>The word between the quote at <paramref name="quoteAt"/> and the next like it, read from the line as written.</summary>
    private static string? QuotedWord(string line, int quoteAt)
    {
        var close = line.IndexOf(line[quoteAt], quoteAt + 1);
        return close < 0 ? null : line[(quoteAt + 1)..close];
    }

    /// <summary>
    /// typeof compared with a word it never gives - "array", "integer", "null", "String" - which is always false, or always true
    /// with != and !==. The change asks what was meant, where that is plain from the word: Array.isArray for "array",
    /// Number.isInteger for "integer", === null for "null", and the word in small letters for "String" and the like.
    /// </summary>
    private static IEnumerable<LogicFinding> TypeofNeverGives(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];

            foreach (Match comparison in TypeofComparison().Matches(masked[index]))
            {
                var quoteAt = comparison.Groups["quote"].Index;
                if (QuotedWord(line, quoteAt) is not { Length: > 0 } word || TypeofWords.Contains(word)) continue;

                var value = line.Substring(comparison.Groups["value"].Index, comparison.Groups["value"].Length);
                var op = comparison.Groups["op"].Value;
                var negated = op.StartsWith('!');
                var meant = Meant(word, value, op, negated);
                var always = negated ? "true" : "false";

                var end = quoteAt + word.Length + 2;
                var fix = meant is null
                    ? null
                    : LocalFix.ReplaceLine(id, $"Ask what was meant: {meant}",
                        WhatTheChangeAsks(word),
                        source.Path, index + 1, line[..comparison.Index] + meant + line[end..]);

                yield return new LogicFinding(id, index + 1,
                    $"typeof gives one of eight words - {string.Join(", ", TypeofWords.Select(each => $"\"{each}\""))} - never \"{word}\", so " +
                    $"typeof {value} {op} \"{word}\" is always {always}",
                    fix);
                break;
            }
        }
    }

    /// <summary>What typeof's comparison with <paramref name="word"/> was meant to ask, written the way that asks it.</summary>
    private static string? Meant(string word, string value, string op, bool negated)
    {
        var not = negated ? "!" : "";
        var lower = word.ToLowerInvariant();

        return lower switch
        {
            "array" or "list" => $"{not}Array.isArray({value})",
            "integer" or "int" => $"{not}Number.isInteger({value})",
            "null" => $"{value} {(negated ? "!==" : "===")} null",
            "float" or "double" or "decimal" => $"typeof {value} {op} \"number\"",
            _ when TypeofWords.Contains(lower) => $"typeof {value} {op} \"{lower}\"",
            _ => null,
        };
    }

    private static string WhatTheChangeAsks(string word) => word.ToLowerInvariant() switch
    {
        "array" or "list" => "typeof gives \"object\" for an array, as it does for every object. Array.isArray asks whether the value is an array.",
        "integer" or "int" => "typeof gives \"number\" for every number, whole or not. Number.isInteger asks whether the value is a whole number.",
        "null" => "typeof gives \"object\" for null, as it does for an object. === null asks whether the value is null.",
        "float" or "double" or "decimal" => "typeof gives \"number\" for every number, whole or not: it has no word of its own for a decimal.",
        _ => "typeof's words are all in small letters.",
    };

    /// <summary>
    /// type(age) == "int": type gives the type itself - int - not its name as text, so the two are never equal, and the condition
    /// is always false, or always true with !=. The change asks with isinstance.
    /// </summary>
    private static IEnumerable<LogicFinding> TypeComparedWithText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];

            foreach (Match comparison in PythonTypeComparison().Matches(masked[index]))
            {
                var quoteAt = comparison.Groups["quote"].Index;
                if (QuotedWord(line, quoteAt) is not { } word || !PythonTypes.TryGetValue(word, out var type)) continue;

                var value = line.Substring(comparison.Groups["value"].Index, comparison.Groups["value"].Length).Trim();
                var op = comparison.Groups["op"].Value;
                var negated = op is "!=" or "is not";
                var check = $"{(negated ? "not " : "")}isinstance({value}, {type})";
                var end = quoteAt + word.Length + 2;

                yield return new LogicFinding(id, index + 1,
                    $"type({value}) gives the type itself - <class '{type}'> - not its name as text, so type({value}) {op} \"{word}\" compares a " +
                    $"type with text, and is always {(negated ? "true" : "false")}",
                    LocalFix.ReplaceLine(id, $"Check the type with isinstance: {check}",
                        $"isinstance({value}, {type}) asks whether {value} is a {type} - the type is named as it is in code, {type}, not as text.",
                        source.Path, index + 1, line[..comparison.Index] + check + line[end..]));
                break;
            }
        }
    }
}
