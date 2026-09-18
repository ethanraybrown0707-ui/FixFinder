using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>How serious a compiler warning is, how sure it is to be a real mistake, and which logic pattern finds the same thing.</summary>
public sealed record WarningRating(FindingKind Kind, Severity Severity, Confidence Confidence, string? SamePatternAs = null);

public static class WarningRatings
{
    private sealed record Rule(Regex Message, string[] Codes, WarningRating Rating);

    private static Rule Words(string message, FindingKind kind, Severity severity, Confidence confidence, string? samePattern = null) =>
        new(new Regex(message, RegexOptions.IgnoreCase), [], new WarningRating(kind, severity, confidence, samePattern));

    private static Rule Codes(string[] codes, FindingKind kind, Severity severity, Confidence confidence, string? samePattern = null) =>
        new(new Regex("^"), codes, new WarningRating(kind, severity, confidence, samePattern));

    private static readonly Rule[] Rules =
    [
        // Python's compiler
        Words(@"""is"" with|""is not"" with", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-python-is-literal"),
        Words(@"assertion is always true", FindingKind.Logic, Severity.Warning, Confidence.Certain, "logic-python-assert-tuple"),
        Words(@"invalid escape sequence", FindingKind.Style, Severity.Suggestion, Confidence.Likely),
        Words(@"perhaps you missed a comma", FindingKind.Runtime, Severity.Error, Confidence.Likely),
        Words(@"in a 'finally' block", FindingKind.Logic, Severity.Warning, Confidence.Likely),

        // javac -Xlint
        Words(@"^\[fallthrough\]", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-switch-fallthrough"),
        Words(@"^\[empty\]", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-empty-if-body"),
        Words(@"^\[divzero\]", FindingKind.Runtime, Severity.Error, Confidence.Certain),
        Words(@"^\[finally\]", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"^\[overrides\]", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"^\[(?:rawtypes|unchecked)\]", FindingKind.Style, Severity.Suggestion, Confidence.Likely),
        Words(@"^\[(?:cast|static|deprecation)\]", FindingKind.Style, Severity.Suggestion, Confidence.Certain),

        // C#
        Codes(["CS0162"], FindingKind.Logic, Severity.Warning, Confidence.Certain),
        Codes(["CS0168", "CS0219", "CS8321", "CS0169", "CS0414"], FindingKind.Style, Severity.Suggestion, Confidence.Certain),
        Codes(["CS0649"], FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Codes(["CS1717", "CS1718"], FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Codes(["CS0665"], FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-assignment-in-condition"),
        Codes(["CS0252", "CS0253", "CS0472"], FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Codes(["CS4014"], FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Codes(["CS1998"], FindingKind.Style, Severity.Suggestion, Confidence.Likely),
        Codes(["CS0108", "CS0114"], FindingKind.Logic, Severity.Warning, Confidence.Possible),
        Codes(["CS0642"], FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-empty-if-body"),
        Codes(["CS0659", "CS0660", "CS0661"], FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Codes(["CS8600", "CS8601", "CS8602", "CS8603", "CS8604", "CS8618", "CS8625"], FindingKind.Logic, Severity.Warning, Confidence.Possible),

        // MSVC
        Codes(["C4700", "C4701", "C4703"], FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-uninitialised-total"),
        Codes(["C4715", "C4716"], FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Codes(["C4477", "C4473", "C4474", "C4313"], FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Codes(["C4172"], FindingKind.Logic, Severity.Error, Confidence.Likely),
        Codes(["C4013"], FindingKind.Syntax, Severity.Warning, Confidence.Likely),
        Codes(["C4996"], FindingKind.Style, Severity.Suggestion, Confidence.Likely),
        Codes(["C4244", "C4267", "C4305"], FindingKind.Logic, Severity.Suggestion, Confidence.Possible),
        Codes(["C4018", "C4389"], FindingKind.Logic, Severity.Warning, Confidence.Possible),
        Codes(["C4101", "C4189"], FindingKind.Style, Severity.Suggestion, Confidence.Certain),
        Codes(["C4706"], FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-assignment-in-condition"),

        // gcc
        Words(@"unused variable|set but not used|defined but not used|unused function", FindingKind.Style, Severity.Suggestion, Confidence.Certain),
        Words(@"suggest parentheses around assignment", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-assignment-in-condition"),
        Words(@"is used uninitialized|may be used uninitialized", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-uninitialised-total"),
        Words(@"control reaches end of non-void function|no return statement", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"^format '|format specifies type|too many arguments for format|too few arguments for format", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"of different signedness|between signed and unsigned", FindingKind.Logic, Severity.Warning, Confidence.Possible),
        Words(@"clause does not guard", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"suggest braces around empty body|empty body", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-empty-if-body"),
        Words(@"this statement may fall through", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-switch-fallthrough"),
        Words(@"comparison with string literal", FindingKind.Logic, Severity.Warning, Confidence.Likely, "logic-c-string-equals"),
        Words(@"address of local variable|reference to local variable", FindingKind.Logic, Severity.Error, Confidence.Likely),
        Words(@"division by zero", FindingKind.Runtime, Severity.Error, Confidence.Likely),
        Words(@"array bounds|array subscript", FindingKind.Runtime, Severity.Error, Confidence.Likely),
        Words(@"implicit declaration of function", FindingKind.Syntax, Severity.Warning, Confidence.Likely),
        Words(@"catching polymorphic type", FindingKind.Logic, Severity.Suggestion, Confidence.Likely, "logic-cpp-catch-by-value"),
        Words(@"non-virtual destructor", FindingKind.Logic, Severity.Warning, Confidence.Possible, "logic-cpp-non-virtual-destructor"),
        Words(@"mismatched allocation|mismatched-new-delete", FindingKind.Logic, Severity.Error, Confidence.Likely),
        Words(@"comparison is always (?:true|false)|self-comparison|self-assignment", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"statement has no effect|value computed is not used|result unused", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"incompatible pointer type|makes (?:pointer|integer) from", FindingKind.Logic, Severity.Warning, Confidence.Likely),

        // go vet
        Words(@"unreachable code", FindingKind.Logic, Severity.Warning, Confidence.Certain),
        Words(@"Printf|Println call|format %", FindingKind.Logic, Severity.Warning, Confidence.Likely),
        Words(@"captured by func literal|loop variable", FindingKind.Logic, Severity.Warning, Confidence.Likely),
    ];

    public static WarningRating For(ParsedError warning)
    {
        foreach (var rule in Rules)
        {
            if (rule.Codes.Length > 0)
            {
                if (rule.Codes.Contains(warning.ErrorCode ?? "", StringComparer.OrdinalIgnoreCase)) return rule.Rating;
                continue;
            }

            if (rule.Message.IsMatch(warning.Message ?? "")) return rule.Rating;
        }

        return new WarningRating(FindingKind.Logic, Severity.Warning, Confidence.Possible);
    }
}
