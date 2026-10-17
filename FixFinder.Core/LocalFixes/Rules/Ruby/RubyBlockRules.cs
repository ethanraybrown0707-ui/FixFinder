using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>The blocks a Ruby program opens with def, if, do and the rest, and closes with end - read from its lines.</summary>
internal static partial class RubyBlocks
{
    /// <summary>
    /// A line that opens a block closed by end: def, class, module, if, unless, while, until, case, begin or for at its start -
    /// not after a statement, where if and the rest only make it conditional - else if, which opens an if of its own, or a do
    /// at its end. A line that closes what it opens, def total = 0 or if x then y end, opens nothing.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:(?:else\s+)?(?:def|class|module|if|unless|while|until|case|begin|for)\b(?!.*\bend\s*$)|.*\bdo\s*(?:\|[^|]*\|)?\s*$)")]
    public static partial Regex Opener();

    [GeneratedRegex(@"^\s*end\b")]
    public static partial Regex Closer();

    [GeneratedRegex(@"^\s*else\s+if\b")]
    public static partial Regex ElseIf();

    /// <summary>A Ruby file's lines with their comments and the insides of their text blanked out.</summary>
    public static IReadOnlyList<string> Masked(SourceFile source) => CodeText.MaskAll(source.Lines, Syntax.Python);

    /// <summary>
    /// The lines whose blocks are never closed, in order - or null when an end closes nothing, and the first such end is in
    /// <paramref name="extraEnd"/>. An end closes the open block at its own indentation, as the program is laid out: the blocks
    /// inside that one, still open, are the ones never closed. An end at no open block's indentation closes the latest.
    /// </summary>
    public static List<int>? Unclosed(IReadOnlyList<string> masked, out int extraEnd)
    {
        var open = new List<int>();
        var neverClosed = new List<int>();
        extraEnd = -1;

        for (var index = 0; index < masked.Count; index++)
        {
            if (Closer().IsMatch(masked[index]))
            {
                if (open.Count == 0)
                {
                    extraEnd = index;
                    return null;
                }

                var indent = CodeText.Indentation(masked[index]).Length;
                var closes = open.FindLastIndex(opener => CodeText.Indentation(masked[opener]).Length == indent);
                if (closes < 0) closes = open.Count - 1;

                neverClosed.AddRange(open.Skip(closes + 1));
                open.RemoveRange(closes, open.Count - closes);
            }
            else if (Opener().IsMatch(masked[index]))
            {
                open.Add(index);
            }
        }

        return [.. neverClosed.Concat(open).Order()];
    }
}

/// <summary>
/// "syntax error, unexpected end-of-input, expecting `end'": a block opened with def, if, do or the rest is never closed. When
/// one is left open, its end goes where its indentation ends - after the last line indented inside it.
/// </summary>
public sealed partial class RubyMissingEnd : ILocalFixRule
{
    public string Id => "ruby-missing-end";

    [GeneratedRegex(@"unexpected end-of-input, expecting [`']end'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "SyntaxError" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        var masked = RubyBlocks.Masked(at.Source);
        if (RubyBlocks.Unclosed(masked, out _) is not [var opener] || RubyBlocks.ElseIf().IsMatch(masked[opener])) return null;

        var keyword = masked[opener].Trim().Split(' ')[0] is var first && first is "def" or "class" or "module" or "if" or "unless" or "while" or "until" or "case" or "begin" or "for"
            ? first
            : "do";

        return LocalFix.Insert(Id, $"Close the {keyword} on line {opener + 1} with end",
            $"Every block Ruby opens - with def, if, do and the rest - is closed by an end, and the {keyword} on line {opener + 1} has none, so " +
            "Ruby reached the end of the file still inside it. The end goes where the block's indentation ends - after the last line " +
            "indented inside it.",
            at.Source.Path, LeftOpen.BlockEndsBefore(at.Source.Lines, masked, opener), [CodeText.Indentation(at.Source.Lines[opener]) + "end"]);
    }
}

/// <summary>
/// "else if", from other languages, where Ruby writes elsif: else if opens an if of its own, inside the else, so it needs an end
/// of its own - and Ruby reaches the end of the file looking for it. It becomes elsif.
/// </summary>
public sealed partial class RubyElseIf : ILocalFixRule
{
    public string Id => "ruby-else-if";

    [GeneratedRegex(@"unexpected end-of-input, expecting [`']end'")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)else\s+if\b")]
    private static partial Regex ElseIf();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "SyntaxError" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        var masked = RubyBlocks.Masked(at.Source);
        var elseIfs = Enumerable.Range(0, masked.Count).Where(index => ElseIf().IsMatch(masked[index])).ToList();
        if (elseIfs is not [var index]) return null;

        var line = at.Source.Lines[index];
        var written = ElseIf().Match(line);

        return LocalFix.ReplaceLine(Id, "Write elsif",
            "Ruby writes \"else if\" as one word, elsif. else if is an else with an if inside it - an if that needs an end of its own - so " +
            "Ruby reached the end of the file still looking for that end.",
            at.Source.Path, index + 1, written.Groups["lead"].Value + "elsif" + line[written.Length..]);
    }
}

/// <summary>
/// "syntax error, unexpected `end'": an end that closes nothing - every block above it is closed already. The end is taken out.
/// </summary>
public sealed partial class RubyExtraEnd : ILocalFixRule
{
    public string Id => "ruby-extra-end";

    [GeneratedRegex(@"^syntax error, unexpected [`']end'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "SyntaxError" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        var masked = RubyBlocks.Masked(at.Source);
        if (RubyBlocks.Unclosed(masked, out var extraEnd) is not null || extraEnd != at.Number - 1 || masked[extraEnd].Trim() != "end") return null;

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Take out the end on line {at.Number}",
            Explanation = "Each end closes the nearest block above it that is still open, and every block above this end is closed " +
                          "already - so this end closes nothing.",
            File = at.Source.Path,
            StartLine = at.Number,
            RemoveCount = 1,
            NewLines = [],
        };
    }
}

/// <summary>
/// The warning "found `= literal' in conditional, should be ==": = in a condition gives the variable the value, and the condition
/// is then always true. The = becomes ==.
/// </summary>
public sealed partial class RubyCompareInCondition : ILocalFixRule
{
    public string Id => "ruby-compare-in-condition";

    [GeneratedRegex(@"^found [`']= literal' in conditional, should be ==$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*(?:if|unless|while|until|elsif)\s+[A-Za-z_@]\w*\s*)=(?![=~>])")]
    private static partial Regex Assigned();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "compile warning" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at || Assigned().Match(CodeText.Mask(at.Line, Syntax.Python)) is not { Success: true } assigned) return null;

        var equals = assigned.Groups["lead"].Length;
        return LocalFix.ReplaceLine(Id, "Compare with ==",
            "= gives the variable the value on its right, and the condition is then that value - always true - so the block runs " +
            "every time, and the variable has lost what it held. == asks whether the two are equal.",
            at.Source.Path, at.Number, at.Line[..equals] + "==" + at.Line[(equals + 1)..]) with { ResolvesWarning = "found `= literal' in conditional" };
    }
}

/// <summary>
/// "undefined method `+' for nil", at total = total + mark inside a block, where total is first given a value: a variable
/// first given a value inside a block is new on each pass, and nil on the right of its first =. Giving it its starting value,
/// 0, before the block makes it the one variable every pass adds to.
/// </summary>
public sealed partial class RubyTotalStartsAtZero : ILocalFixRule
{
    public string Id => "ruby-total-starts-at-zero";

    [GeneratedRegex(@"^undefined method [`'](?<op>[-+*])' for nil$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "NoMethodError" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        var op = Regex.Escape(said.Groups["op"].Value);
        var masked = RubyBlocks.Masked(at.Source);
        if (Regex.Match(masked[at.Number - 1], $@"^\s*(?<name>[a-z_]\w*)\s*(?:{op}=|=\s*\k<name>\s*{op})") is not { Success: true } adding) return null;

        var name = adding.Groups["name"].Value;
        var indent = CodeText.Indentation(at.Line).Length;

        // The block the line is in: the nearest line above it, further left, that opens a block.
        var opener = Enumerable.Range(0, at.Number - 1).Reverse()
            .FirstOrDefault(index => masked[index].Trim().Length > 0 && CodeText.Indentation(masked[index]).Length < indent && RubyBlocks.Opener().IsMatch(masked[index]), -1);
        if (opener < 0 || !Regex.IsMatch(masked[opener], @"\bdo\b|^\s*(?:while|until|for)\b")) return null;

        var given = new Regex($@"^\s*{Regex.Escape(name)}\s*=(?!=)");
        if (Enumerable.Range(0, opener).Any(index => given.IsMatch(masked[index]))) return null;

        return LocalFix.Insert(Id, $"Start {name} at 0 before the loop",
            $"{name} is first given a value inside the loop, so it is a new variable on each pass, and on the right of its first = it is " +
            $"nil - which has no {said.Groups["op"].Value}. Given its starting value before the loop, {name} is the one variable every pass adds to, " +
            "and it is still there after the loop.",
            at.Source.Path, opener + 1, [CodeText.Indentation(at.Source.Lines[opener]) + $"{name} = 0"]);
    }
}

/// <summary>
/// TypeError, "String can't be coerced into Integer" - or Float: a number and text added with +, the text where the ^ marks
/// point - often a number typed in, which is still text. The text is turned into a number with .to_i, or .to_f.
/// </summary>
public sealed partial class RubyTextToNumber : ILocalFixRule
{
    public string Id => "ruby-text-to-number";

    [GeneratedRegex(@"^String can't be coerced into (?<type>Integer|Float)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^[A-Za-z_@][\w.]*$")]
    private static partial Regex OneValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "TypeError" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (RubyCode.Locate(context) is not { } at || RubyCode.Pointed(context.Error, at.Line) is not { } pointed) return null;

        var value = at.Line[pointed.Start..pointed.End];
        if (!OneValue().IsMatch(value)) return null;

        var (method, kind) = said.Groups["type"].Value == "Integer" ? ("to_i", "whole number") : ("to_f", "decimal number");
        return LocalFix.ReplaceLine(Id, $"Turn {value} into a number: {value}.{method}",
            $"{value} is text - a number typed in stays text until it is turned into one - and Ruby will not add text to a number. " +
            $".{method} turns the text into a {kind} first.",
            at.Source.Path, at.Number, at.Line[..pointed.Start] + $"{value}.{method}" + at.Line[pointed.End..]);
    }
}

/// <summary>
/// "syntax error, unexpected local variable or method, expecting `do' or '{' or '('", on the line after a call that goes over
/// each item - [70, 80].each |mark| - with no do before its |mark|. The do goes in.
/// </summary>
public sealed partial class RubyMissingDo : ILocalFixRule
{
    public string Id => "ruby-missing-do";

    [GeneratedRegex(@"expecting [`']do' or '\{' or '\('$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<call>.*\S)\s+(?<parameters>\|[^|]*\|)\s*$")]
    private static partial Regex WithoutDo();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "SyntaxError" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        var masked = RubyBlocks.Masked(at.Source);
        var before = Enumerable.Range(0, at.Number - 1).Reverse().FirstOrDefault(index => masked[index].Trim().Length > 0, -1);
        if (before < 0 || WithoutDo().Match(masked[before]) is not { Success: true } call || Regex.IsMatch(masked[before], @"\bdo\b|\{")) return null;

        var line = at.Source.Lines[before];
        var parameters = call.Groups["parameters"].Index;

        return LocalFix.ReplaceLine(Id, "Start the block with do",
            "A block given to a method - each, times, map - starts with do, and its |mark| comes after the do. Without it, Ruby read the " +
            "|mark| as more of the call, and stopped on the next line.",
            at.Source.Path, before + 1, line[..parameters] + "do " + line[parameters..]);
    }
}

/// <summary>
/// "syntax error, unexpected ':'", at a def, if, while or else line that ends with a colon, as Python's do: Ruby's blocks need
/// no colon - the line ends, and end closes the block. The colon is taken out.
/// </summary>
public sealed partial class RubyPythonColon : ILocalFixRule
{
    public string Id => "ruby-python-colon";

    [GeneratedRegex(@"^syntax error, unexpected ':'")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s*(?:def|if|elsif|else|unless|while|until|for|class|module)\b.*?(?<colon>:)\s*$")]
    private static partial Regex Heading();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "SyntaxError" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at || Heading().Match(CodeText.Mask(at.Line, Syntax.Python)) is not { Success: true } heading) return null;

        var colon = heading.Groups["colon"].Index;
        return LocalFix.ReplaceLine(Id, "Take out the colon",
            "Ruby's def, if and while lines end with no colon - unlike Python's - and the block they start is closed by end.",
            at.Source.Path, at.Number, at.Line[..colon] + at.Line[(colon + 1)..]);
    }
}

/// <summary>
/// "unterminated string meets end of file", named at the file's last line: a string's closing quote is missing, so Ruby read
/// on to the end of the file inside it. The first line whose quotes do not close gets its closing quote.
/// </summary>
public sealed partial class RubyUnterminatedString : ILocalFixRule
{
    public string Id => "ruby-unterminated-string";

    [GeneratedRegex(@"^unterminated string meets end of file$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "SyntaxError" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        for (var index = 0; index < at.Source.Lines.Count; index++)
        {
            if (LeftOpen.ClosedText(at.Source.Lines[index], Syntax.Python, escape: '\\') is not { } closed) continue;

            return LocalFix.ReplaceLine(Id, $"Close the string on line {index + 1}",
                "The text that starts with this quote has no closing quote on its line, so Ruby read on to the end of the file inside it. " +
                "The closing quote goes where the text ends - at the end of the line, or before the brackets it is inside.",
                at.Source.Path, index + 1, closed);
        }

        return null;
    }
}

/// <summary>
/// "syntax error, unexpected local variable or method, expecting ')'", on the line after one that opens a ( and never closes it:
/// the ) goes at the end of that line.
/// </summary>
public sealed partial class RubyUnclosedBracket : ILocalFixRule
{
    public string Id => "ruby-unclosed-bracket";

    [GeneratedRegex(@"^syntax error, unexpected .+, expecting '\)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "ruby", ExceptionType: "SyntaxError" } || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        var masked = RubyBlocks.Masked(at.Source);
        var before = Enumerable.Range(0, at.Number - 1).Reverse().FirstOrDefault(index => masked[index].Trim().Length > 0, -1);
        if (before < 0 || masked[before].Count(character => character == '(') - masked[before].Count(character => character == ')') != 1) return null;

        var (code, tail) = CodeText.SplitComment(at.Source.Lines[before], Syntax.Python);
        return LocalFix.ReplaceLine(Id, $"Close the bracket on line {before + 1}",
            $"The ( on line {before + 1} is never closed, so Ruby read the next line as more of what is inside it, and stopped there.",
            at.Source.Path, before + 1, code + ")" + tail);
    }
}
