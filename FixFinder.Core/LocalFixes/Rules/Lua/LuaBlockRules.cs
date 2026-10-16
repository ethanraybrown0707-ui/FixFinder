using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>The blocks a Lua program opens - function, if ... then, for and while ... do, do, repeat - and closes with end or until.</summary>
internal static partial class LuaBlocks
{
    /// <summary>
    /// A line that opens a block: a function - named, local, or given to a name - an if, or an else if, which opens an if of its
    /// own, ending in then; a for or a while ending in do; a do on its own; a repeat. A line that closes what it opens opens nothing.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:(?:local\s+)?function\b(?!.*\bend\s*\)?\s*$)|.*=\s*function\s*\([^)]*\)\s*$|(?:else\s+)?if\b.*\bthen\s*$|(?:for|while)\b.*\bdo\s*$|do\s*$|repeat\b)")]
    public static partial Regex Opener();

    [GeneratedRegex(@"^\s*(?:end|until)\b")]
    public static partial Regex Closer();

    [GeneratedRegex(@"^(?<lead>\s*)else\s+if\b")]
    public static partial Regex ElseIf();

    public static IReadOnlyList<string> Masked(SourceFile source) => CodeText.MaskAll(source.Lines, Syntax.Lua);

    /// <summary>
    /// The lines whose blocks are never closed, in order. An end closes the open block at its own indentation, as the program is
    /// laid out - the blocks inside that one, still open, are the ones never closed; an end at no open block's indentation closes
    /// the latest.
    /// </summary>
    public static List<int> NeverClosed(IReadOnlyList<string> masked)
    {
        var open = new List<int>();
        var neverClosed = new List<int>();

        for (var index = 0; index < masked.Count; index++)
        {
            if (Closer().IsMatch(masked[index]) && open.Count > 0)
            {
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
/// "'end' expected (to close 'function' at line 1) near &lt;eof&gt;": a block is never closed. Lua names the outer block the
/// file ends inside; the one left open is found as the program is laid out, and its end goes where its indentation ends.
/// </summary>
public sealed partial class LuaMissingEnd : ILocalFixRule
{
    public string Id => "lua-missing-end";

    [GeneratedRegex(@"^'end' expected \(to close '\w+' at line \d+\) near <eof>$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (context.Read(context.Frame?.File) is not { } source || !LuaCode.IsLua(source)) return null;

        var masked = LuaBlocks.Masked(source);
        if (LuaBlocks.NeverClosed(masked) is not [var opened] || LuaBlocks.ElseIf().IsMatch(masked[opened])) return null;

        return LocalFix.Insert(Id, $"Close the block on line {opened + 1} with end",
            $"Every block Lua opens - a function, an if, a for or a while - is closed by an end, and the one on line {opened + 1} has none, " +
            "so Lua reached the end of the file inside it. Its end goes where its indentation ends - after the last line indented inside it.",
            source.Path, LeftOpen.BlockEndsBefore(source.Lines, masked, opened), [CodeText.Indentation(source.Lines[opened]) + "end"]);
    }
}

/// <summary>
/// "'end' expected (to close 'if' ...) near &lt;eof&gt;", with an else if in the file: Lua writes elseif, and else if opens an if
/// of its own, inside the else, which needs an end of its own. It becomes elseif.
/// </summary>
public sealed partial class LuaElseIf : ILocalFixRule
{
    public string Id => "lua-elseif";

    [GeneratedRegex(@"^'end' expected \(to close 'if' at line \d+\) near <eof>$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (context.Read(context.Frame?.File) is not { } source || !LuaCode.IsLua(source)) return null;

        var masked = LuaBlocks.Masked(source);
        var elseIfs = Enumerable.Range(0, masked.Count).Where(index => LuaBlocks.ElseIf().IsMatch(masked[index])).ToList();
        if (elseIfs is not [var index]) return null;

        var line = source.Lines[index];
        var written = LuaBlocks.ElseIf().Match(line);

        return LocalFix.ReplaceLine(Id, "Write elseif",
            "Lua writes \"else if\" as one word, elseif. else if is an else with a new if inside it - an if that needs an end of its own " +
            "- so Lua reached the end of the file still looking for that end.",
            source.Path, index + 1, written.Groups["lead"].Value + "elseif" + line[written.Length..]);
    }
}

/// <summary>
/// "bad argument #1 to 'upper' (string expected, got no value)", at name.upper(): a method of a string is called with :, which
/// gives it the string itself. With ., upper was given nothing. The . becomes :.
/// </summary>
public sealed partial class LuaColonForMethod : ILocalFixRule
{
    public string Id => "lua-colon-for-method";

    [GeneratedRegex(@"^bad argument #1 to '(?<method>\w+)' \(string expected, got (?:no value|number)\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsRuntimeError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said || LuaCode.Locate(context) is not { } at) return null;

        var method = said.Groups["method"].Value;
        var calls = new Regex($@"(?<object>(?<![\w.:])[A-Za-z_]\w*)\.{Regex.Escape(method)}\s*\(").Matches(CodeText.Mask(at.Line, Syntax.Lua))
            .Where(call => call.Groups["object"].Value != "string")
            .ToList();
        if (calls is not [var call]) return null;

        var dot = call.Groups["object"].Index + call.Groups["object"].Length;
        var objectName = call.Groups["object"].Value;

        return LocalFix.ReplaceLine(Id, $"Call it with a colon: {objectName}:{method}()",
            $"A string's own functions are called with a colon - {objectName}:{method}() - which gives {method} the string itself. With a dot, " +
            $"{method} was given nothing to work on.",
            at.Source.Path, at.Number, at.Line[..dot] + ":" + at.Line[(dot + 1)..]);
    }
}

/// <summary>"unfinished string near '"Ada'": the string's closing quote is missing on its line. It goes where the line's text ends.</summary>
public sealed partial class LuaUnfinishedString : ILocalFixRule
{
    public string Id => "lua-unfinished-string";

    [GeneratedRegex(@"^unfinished string near ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || !Message().IsMatch(context.Error.Message ?? "") || LuaCode.Locate(context) is not { } at) return null;
        if (LeftOpen.ClosedText(at.Line, Syntax.Lua, escape: '\\', comment: "--") is not { } closed) return null;

        return LocalFix.ReplaceLine(Id, "Close the string",
            "The text that starts with this quote has no closing quote on its line, and a Lua string in quotes ends on the line it starts " +
            "on. The closing quote goes where the text ends - at the end of the line, or before the brackets it is inside.",
            at.Source.Path, at.Number, closed);
    }
}

/// <summary>
/// "')' expected (to close '(' at line 1) near 'print'": the ( on the line Lua names is never closed, so Lua read on into the next
/// line. The ) goes at the end of the ('s line.
/// </summary>
public sealed partial class LuaClosingBracket : ILocalFixRule
{
    public string Id => "lua-closing-bracket";

    [GeneratedRegex(@"^'\)' expected \(to close '\(' at line (?<line>\d+)\) near ")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!LuaCode.IsSyntaxError(context) || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (context.Read(context.Frame?.File) is not { } source || !LuaCode.IsLua(source)) return null;

        var index = int.Parse(said.Groups["line"].Value) - 1;
        if (index < 0 || index >= source.Count) return null;

        var masked = CodeText.Mask(source.Lines[index], Syntax.Lua);
        if (masked.Count(character => character == '(') - masked.Count(character => character == ')') != 1) return null;

        var (code, tail) = CodeText.SplitComment(source.Lines[index], Syntax.Lua);
        return LocalFix.ReplaceLine(Id, $"Close the bracket on line {index + 1}",
            $"The ( on line {index + 1} is never closed, so Lua read on into the next line as more of what is inside it, and stopped there.",
            source.Path, index + 1, code + ")" + tail);
    }
}
