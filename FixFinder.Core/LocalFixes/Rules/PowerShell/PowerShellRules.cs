using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the PowerShell rules share: whether an error is PowerShell's, and the line it is on.</summary>
internal static class PowerShellCode
{
    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "powershell" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!Path.GetExtension(source.Path).Equals(".ps1", StringComparison.OrdinalIgnoreCase) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    public static bool IsParserError(LocalFixContext context) => context.Error.ExceptionType == PowerShellParser.ParserError;
}

/// <summary>"Missing statement block after 'else' keyword" at <c>} else if (...)</c>: PowerShell writes it as one word, elseif.</summary>
public sealed partial class PowerShellElseIf : ILocalFixRule
{
    public string Id => "powershell-elseif";

    [GeneratedRegex(@"\belse\s+if\b", RegexOptions.IgnoreCase)]
    private static partial Regex ElseIf();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PowerShellCode.IsParserError(context) || context.Error.ErrorCode != "MissingStatementBlockAfterElse" || PowerShellCode.Locate(context) is not { } at) return null;
        if (ElseIf().Matches(CodeText.Mask(at.Line, Syntax.Python)) is not [var place]) return null;

        return LocalFix.ReplaceLine(Id, "Write it as one word: elseif",
            "PowerShell writes \"else if\" as one word, elseif. After else it wants a block in { }, and found if instead.",
            at.Source.Path, at.Number, at.Line[..place.Index] + "elseif" + at.Line[(place.Index + place.Length)..]);
    }
}

/// <summary>
/// Comparisons written as other languages write them - ==, !=, &lt;, &lt;=, &gt;=, &amp;&amp;, || - in an if or a while.
/// PowerShell compares with words: -eq, -ne, -lt, -le, -ge, -and, -or. Each is refused before the script runs, except ==,
/// which stops at the line as it runs: "The term '=' is not recognized". Every such symbol in the condition becomes its
/// word, and a &gt; with them, which would write to a file, becomes -gt.
/// </summary>
public sealed partial class PowerShellComparisonWords : ILocalFixRule
{
    public string Id => "powershell-comparison-words";

    [GeneratedRegex(@"^\s*(?:\}\s*)?(?:if|elseif|while|until)\s*\((?<condition>.*)\)\s*\{?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Condition();

    /// <summary>The symbols, longest first, and the word PowerShell writes for each.</summary>
    private static readonly (string Symbol, string Word)[] Words =
        [("==", "-eq"), ("!=", "-ne"), (">=", "-ge"), ("<=", "-le"), ("&&", "-and"), ("||", "-or"), ("<", "-lt"), (">", "-gt")];

    [GeneratedRegex(@"==|!=|>=|<=|&&|\|\||(?<![-\w])<|(?<![-\w\d])>(?!>)")]
    private static partial Regex Symbol();

    public LocalFix? Propose(LocalFixContext context)
    {
        var refused = PowerShellCode.IsParserError(context) &&
                      context.Error.ErrorCode is "RedirectionNotSupported" or "UnexpectedToken" or "InvalidEndOfLine" or "MissingEndParenthesisAfterStatement";
        var doubleEqualsRan = context.Error.ErrorCode == "CommandNotFoundException" && (context.Error.Message ?? "").StartsWith("The term '=' is not recognized", StringComparison.Ordinal);
        if (!refused && !doubleEqualsRan || PowerShellCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.Python);
        if (Condition().Match(masked) is not { Success: true } condition) return null;

        var inside = condition.Groups["condition"];
        var symbols = Symbol().Matches(masked[inside.Index..(inside.Index + inside.Length)]);
        if (symbols.Count == 0 || symbols.All(found => found.Value == ">")) return null;

        var corrected = at.Line;
        foreach (var found in symbols.Reverse())
        {
            var word = Words.First(pair => pair.Symbol == found.Value).Word;
            var place = inside.Index + found.Index;
            corrected = corrected[..place] + word + corrected[(place + found.Length)..];
        }

        var named = string.Join(", ", symbols.Select(found => found.Value).Distinct().Select(symbol => $"{symbol} as {Words.First(pair => pair.Symbol == symbol).Word}"));

        return LocalFix.ReplaceLine(Id, "Compare with PowerShell's words",
            $"PowerShell compares with words, not symbols - {named}. Its symbols mean other things: > and < send output to and from files, ! is not, and = gives a variable a value.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "The term 'Write-Ouput' is not recognized as the name of a cmdlet, function, script file, or operable program": when the
/// script has a function, or PowerShell a command a program uses often, within two letters of it - and only one - the name
/// is changed to that one.
/// </summary>
public sealed partial class PowerShellNearestCommand : ILocalFixRule
{
    public string Id => "powershell-nearest-command";

    [GeneratedRegex(@"^The term '(?<name>[\w-]+)' is not recognized as the name of a cmdlet")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*function\s+(?<name>[\w-]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex FunctionDeclared();

    /// <summary>Commands Windows PowerShell 5.1 has, that scripts use most.</summary>
    private static readonly string[] Commands =
    [
        "Write-Output", "Write-Host", "Write-Error", "Write-Warning", "Read-Host", "Get-Content", "Set-Content", "Add-Content",
        "Get-ChildItem", "Get-Item", "Test-Path", "New-Item", "Remove-Item", "Copy-Item", "Move-Item", "Join-Path", "Split-Path",
        "ForEach-Object", "Where-Object", "Select-Object", "Sort-Object", "Measure-Object", "Group-Object", "Get-Date",
        "Get-Random", "Start-Sleep", "Out-File", "Import-Csv", "Export-Csv", "ConvertTo-Json", "ConvertFrom-Json", "Get-Process",
    ];

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ErrorCode != "CommandNotFoundException" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PowerShellCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var functions = FunctionDeclared().Matches(string.Join("\n", CodeText.MaskAll(at.Source.Lines, Syntax.Python))).Select(found => found.Groups["name"].Value);
        if (CodeText.Nearest(wrong, functions.Concat(Commands)) is not { } right) return null;

        var place = at.Line.IndexOf(wrong, StringComparison.Ordinal);
        if (place < 0) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"PowerShell has no command called {wrong}, so it said so and carried on without running the line. {right}, a letter or two apart, is " +
            "one it has.",
            at.Source.Path, at.Number, at.Line[..place] + right + at.Line[(place + wrong.Length)..]);
    }
}

/// <summary>
/// "Exception calling "Add" with "1" argument(s): "Collection was of a fixed size."": an array made with @() or commas
/// cannot grow - += makes a new array one longer, which is what adding to it means in PowerShell.
/// </summary>
public sealed partial class PowerShellFixedSizeArray : ILocalFixRule
{
    public string Id => "powershell-fixed-size-array";

    [GeneratedRegex(@"^(?<indent>\s*)(?<array>\$\w+)\.Add\((?<item>.+)\)\s*$")]
    private static partial Regex AddCall();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!(context.Error.Message ?? "").Contains("Collection was of a fixed size", StringComparison.Ordinal) || PowerShellCode.Locate(context) is not { } at) return null;
        if (AddCall().Match(at.Line) is not { Success: true } add) return null;

        return LocalFix.ReplaceLine(Id, $"Add with +=: {add.Groups["array"].Value} += {add.Groups["item"].Value}",
            "An array made with @() or commas has a fixed size, so Add cannot put anything in it. += makes a new array with the item on the end.",
            at.Source.Path, at.Number, $"{add.Groups["indent"].Value}{add.Groups["array"].Value} += {add.Groups["item"].Value}");
    }
}

/// <summary>"Attempted to divide by zero.": the one division on the line is given an answer for when the divisor is zero.</summary>
public sealed partial class PowerShellDivisionGuard : ILocalFixRule
{
    public string Id => "powershell-division-guard";

    [GeneratedRegex(@"(?<dividend>\$[\w.]+|\d+(?:\.\d+)?|\([^()]*\))\s*/\s*(?<divisor>\$[\w.]+|\([^()]*\))")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.Message != "Attempted to divide by zero." || PowerShellCode.Locate(context) is not { } at) return null;
        if (Division().Matches(CodeText.Mask(at.Line, Syntax.Python)) is not [var division]) return null;

        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = at.Line.Substring(division.Index, division.Length);

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing by zero has no answer, so PowerShell said so and carried on without the line's result. The divisor is checked first, " +
            "and 0 given when it is zero - change the 0 to whatever an empty case should give.",
            at.Source.Path, at.Number, at.Line[..division.Index] + $"$(if ({divisor} -eq 0) {{ 0 }} else {{ {expression} }})" + at.Line[(division.Index + division.Length)..]);
    }
}
