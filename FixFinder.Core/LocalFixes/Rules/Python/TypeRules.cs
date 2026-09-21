using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>TypeError: can only concatenate str (not "int") to str</c></summary>
public sealed partial class PythonStrConcatenation : ILocalFixRule
{
    public string Id => "python-str-concatenation";

    [GeneratedRegex(@"^can only concatenate str \(not ""(?<type>\w+)""\) to str$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\s+File\s+"".+"",\s+line\s+(?<line>\d+)")]
    private static partial Regex FileLine();

    [GeneratedRegex(@"^\s*~+\^+~+\s*$")]
    private static partial Regex Underline();

    [GeneratedRegex(@"^[rRbBuUfF]{0,2}[""']|^str\(")]
    private static partial Regex Text();

    [GeneratedRegex(@"^-?\d+(?<fraction>\.\d*)?$")]
    private static partial Regex Number();

    public LocalFix? Propose(LocalFixContext context)
    {
        var error = context.Error;

        if (error.LanguageId != "python" || error.ExceptionType != "TypeError") return null;
        if (Message().Match(error.Message ?? "") is not { Success: true } message) return null;
        if (context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (source.Line(number) is not { } line) return null;

        var raw = error.RawText.Split('\n').Select(text => text.TrimEnd('\r')).ToList();
        var at = raw.FindLastIndex(text => FileLine().Match(text) is { Success: true } m && m.Groups["line"].Value == number.ToString());

        if (at < 0 || at + 2 >= raw.Count) return null;

        var echo = raw[at + 1];
        var underline = raw[at + 2];

        if (!Underline().IsMatch(underline)) return null;

        var echoed = echo.Trim();
        var offset = line.IndexOf(echoed, StringComparison.Ordinal);
        if (echoed.Length == 0 || offset < 0) return null;

        var echoStart = echo.Length - echo.TrimStart().Length;
        int Column(int printed) => offset + (printed - echoStart);

        var leftStart = underline.IndexOf('~');
        var operatorStart = underline.IndexOf('^');
        var operatorEnd = underline.LastIndexOf('^') + 1;
        var rightEnd = underline.TrimEnd().Length;

        if (Column(leftStart) < 0 || Column(rightEnd) > line.Length) return null;
        if (line[Column(operatorStart)..Column(operatorEnd)].Trim() != "+") return null;

        var (leftAt, left) = Operand(line, Column(leftStart), Column(operatorStart), isLeft: true);
        var (rightAt, right) = Operand(line, Column(operatorEnd), Column(rightEnd), isLeft: false);

        if (left.Length == 0 || right.Length == 0) return null;

        if (CutsAName(line, leftAt, left) || CutsAName(line, rightAt, right)) return null;

        if (Text().IsMatch(left))
        {
            if (Text().IsMatch(right)) return null;

            if (message.Groups["type"].Value == "bytes")
            {
                var decoded = PythonCode.IsSingleOperand(right) ? $"{right}.decode()" : $"({right}).decode()";

                return LocalFix.ReplaceLine(
                    Id,
                    $"Decode {right} into text before joining it",
                    $"`{right}` is bytes, not text - what a socket or a binary file hands back - and Python will only join text to text. " +
                    $"`str({right})` would join its printed form, `b'...'` quotes and all. `.decode()` turns the bytes back into the text " +
                    "they hold, as UTF-8.",
                    source.Path, number, line[..rightAt] + decoded + line[(rightAt + right.Length)..]);
            }

            return LocalFix.ReplaceLine(
                Id,
                $"Convert {right} to a string before joining it",
                $"The left side of + is text and `{right}` is an {message.Groups["type"].Value}; Python will only join text " +
                "to more text. str() makes the text form of it. An f-string does the same job if you prefer it.",
                source.Path, number, line[..rightAt] + $"str({right})" + line[(rightAt + right.Length)..]);
        }

        if (Number().Match(right) is { Success: true } numeric)
        {
            var convert = numeric.Groups["fraction"].Success ? "float" : "int";

            return LocalFix.ReplaceLine(
                Id,
                $"Turn {left} into a number before adding {right}",
                $"`{left}` holds text - `input()` always returns text, for one - and `{right}` is a number, so Python " +
                $"will not add them. `{convert}()` reads the number out of the text. If `{left}` really is meant to stay " +
                $"text, join `str({right})` instead.",
                source.Path, number, line[..leftAt] + $"{convert}({left})" + line[(leftAt + left.Length)..]);
        }

        return null;
    }

    private static bool CutsAName(string line, int at, string operand) =>
        (at > 0 && CodeText.IsWordChar(line[at - 1]) && CodeText.IsWordChar(operand[0])) ||
        (at + operand.Length < line.Length && CodeText.IsWordChar(line[at + operand.Length]) && CodeText.IsWordChar(operand[^1]));

    private static (int At, string Operand) Operand(string line, int start, int end, bool isLeft)
    {
        var span = line[start..end];
        var operand = span.Trim();

        if (isLeft)
        {
            while (operand.StartsWith('(') && operand.Count(c => c == '(') > operand.Count(c => c == ')')) operand = operand[1..].TrimStart();
        }
        else
        {
            while (operand.EndsWith(')') && operand.Count(c => c == ')') > operand.Count(c => c == '(')) operand = operand[..^1].TrimEnd();
        }

        var at = operand.Length == 0 ? start : start + span.IndexOf(operand, StringComparison.Ordinal);

        return (at, operand);
    }
}

/// <summary><c>for i in len(items):</c> - a number is not something to loop over; a range of it is.</summary>
public sealed partial class PythonRangeForInt : ILocalFixRule
{
    public string Id => "python-range-for-int";

    [GeneratedRegex(@"^(?<head>\s*(?:async\s+)?for\s+.+?\s+in\s+)(?<expr>.+?)(?<colon>\s*:)$")]
    private static partial Regex ForIn();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "'int' object is not iterable") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);

        if (ForIn().Match(code) is not { Success: true } match) return null;

        var expression = match.Groups["expr"].Value;
        if (expression.StartsWith("range(", StringComparison.Ordinal)) return null;

        return LocalFix.ReplaceLine(
            Id, $"Loop over range({expression})",
            $"`{expression}` is a number, and a for loop needs something to step through. `range({expression})` counts from 0 up to it.",
            source.Path, number, $"{match.Groups["head"].Value}range({expression}){match.Groups["colon"].Value}{tail}");
    }
}

/// <summary>A float where a whole number is needed, from <c>/</c> - which always gives a float in Python 3.</summary>
public sealed partial class PythonFloatDivision : ILocalFixRule
{
    public string Id => "python-float-division";

    [GeneratedRegex(@"^(?:(?:list|tuple|str|string|byte|bytes) indices must be integers.*float|'float' object cannot be interpreted as an integer|slice indices must be integers)")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<![/])/(?![/=])")]
    private static partial Regex Slash();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = Slash().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        return LocalFix.ReplaceLine(
            Id, "Divide with // for a whole number",
            "`/` always gives a float in Python 3, even for 10 / 2, and an index or a range needs a whole number. `//` divides and keeps it whole.",
            source.Path, number, line[..hits[0].Index] + "//" + line[(hits[0].Index + 1)..]);
    }
}

/// <summary><c>'&gt;' not supported between instances of 'str' and 'int'</c> - text compared with a number.</summary>
public sealed partial class PythonComparisonTypes : ILocalFixRule
{
    public string Id => "python-comparison-types";

    [GeneratedRegex(@"^'(?<op><=|>=|<|>)' not supported between instances of '(?<leftType>str|int|float)' and '(?<rightType>str|int|float)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var leftType = message.Groups["leftType"].Value;
        var rightType = message.Groups["rightType"].Value;
        if ((leftType == "str") == (rightType == "str")) return null;

        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var text = line[span.Start..span.End];
        var operatorPattern = Regex.Escape(message.Groups["op"].Value);
        var hits = Regex.Matches(CodeText.Mask(text, Syntax.Python), $@"(?<![<>=!]){operatorPattern}(?![=<>])");
        if (hits.Count != 1) return null;

        var operatorIndex = hits[0].Index;
        var leftText = text[..operatorIndex];
        var rightText = text[(operatorIndex + hits[0].Length)..];
        var left = leftText.Trim();
        var right = rightText.Trim();

        if (left.Length == 0 || right.Length == 0) return null;

        var leftAt = span.Start + leftText.IndexOf(left, StringComparison.Ordinal);
        var rightAt = span.Start + operatorIndex + hits[0].Length + rightText.IndexOf(right, StringComparison.Ordinal);

        string corrected;
        string value;

        if (leftType == "str" && PythonCode.NumberLiteral().Match(right) is { Success: true } rightNumber && !PythonCode.StringLiteral().IsMatch(left))
        {
            var convert = rightNumber.Groups["fraction"].Success ? "float" : "int";
            corrected = line[..leftAt] + $"{convert}({left})" + line[(leftAt + left.Length)..];
            value = left;
        }
        else if (rightType == "str" && PythonCode.NumberLiteral().Match(left) is { Success: true } leftNumber && !PythonCode.StringLiteral().IsMatch(right))
        {
            var convert = leftNumber.Groups["fraction"].Success ? "float" : "int";
            corrected = line[..rightAt] + $"{convert}({right})" + line[(rightAt + right.Length)..];
            value = right;
        }
        else
        {
            return null;
        }

        return LocalFix.ReplaceLine(
            Id, $"Turn {value} into a number before comparing",
            $"`{value}` holds text - `input()` always returns text - and Python will not compare text with a number. " +
            "Converting it compares the numbers.",
            source.Path, number, corrected);
    }
}

/// <summary><c>", ".join(numbers)</c> with numbers in the list - join only joins text.</summary>
public sealed partial class PythonJoinNonStrings : ILocalFixRule
{
    public string Id => "python-join-non-strings";

    [GeneratedRegex(@"^sequence item \d+: expected str instance, \w+ found$")]
    private static partial Regex Message();

    [GeneratedRegex(@"\.join\(")]
    private static partial Regex Join();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        var hits = Join().Matches(masked);
        if (hits.Count != 1) return null;

        var open = hits[0].Index + hits[0].Length - 1;
        var close = Brackets.Closing(masked, open);
        if (close < 0) return null;

        var argument = line[(open + 1)..close].Trim();
        if (argument.Length == 0 || argument.StartsWith("map(str", StringComparison.Ordinal)) return null;

        return LocalFix.ReplaceLine(
            Id, "Turn each item into text before joining",
            "`join` puts text together, and something in the list is not text. `map(str, ...)` converts each item first.",
            source.Path, number, line[..(open + 1)] + $"map(str, {argument})" + line[close..]);
    }
}

/// <summary><c>math.pi()</c> - a constant is a value, not a function.</summary>
public sealed partial class PythonCalledConstant : ILocalFixRule
{
    public string Id => "python-called-constant";

    [GeneratedRegex(@"(?<![\w.])(?:math|cmath)\.(?:pi|e|tau|inf|nan|infj|nanj)(?<call>\s*\(\s*\))")]
    private static partial Regex CalledConstant();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message is not ("'float' object is not callable" or "'complex' object is not callable")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = CalledConstant().Matches(CodeText.Mask(line, Syntax.Python));
        if (hits.Count != 1) return null;

        var call = hits[0].Groups["call"];

        return LocalFix.ReplaceLine(
            Id, "Use the constant without brackets",
            "A constant such as `math.pi` is a number. Brackets after it try to call the number as if it were a function.",
            source.Path, number, line[..call.Index] + line[(call.Index + call.Length)..]);
    }
}

/// <summary><c>isinstance(x, "int")</c> - the type itself, not its name in quotes.</summary>
public sealed partial class PythonIsinstanceString : ILocalFixRule
{
    public string Id => "python-isinstance-string";

    [GeneratedRegex(@"\b(?:isinstance|issubclass)\([^,()]+,\s*(?<quoted>(?<q>[""'])(?<type>int|str|float|bool|list|dict|tuple|set|bytes|complex)\k<q>)\s*\)")]
    private static partial Regex QuotedType();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message?.Contains("arg 2 must be a type", StringComparison.Ordinal) != true) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var hits = QuotedType().Matches(line);
        if (hits.Count != 1) return null;

        var quoted = hits[0].Groups["quoted"];
        var type = hits[0].Groups["type"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Pass the type {type}, not the text \"{type}\"",
            $"`isinstance` checks against a type. `\"{type}\"` is only text that spells its name; `{type}` is the type.",
            source.Path, number, line[..quoted.Index] + type + line[(quoted.Index + quoted.Length)..]);
    }
}

/// <summary><c>list indices must be integers or slices, not str</c> from <c>names[name]</c> inside <c>for name in names</c>.</summary>
public sealed partial class PythonIndexWithElement : ILocalFixRule
{
    public string Id => "python-index-with-element";

    [GeneratedRegex(@"^(?:list|tuple) indices must be integers(?: or slices)?, not \w+$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<seq>[A-Za-z_][\w.]*)\[(?<var>[A-Za-z_]\w*)\]$")]
    private static partial Regex Subscript();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span) return null;
        if (Subscript().Match(line[span.Start..span.End]) is not { Success: true } subscript) return null;

        var seq = subscript.Groups["seq"].Value;
        var variable = subscript.Groups["var"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);

        var loop = PythonCode.EnclosingHeader(masked, number - 1, new Regex($@"^\s*for\s+{Regex.Escape(variable)}\s+in\s+{Regex.Escape(seq)}\s*:"));
        if (loop < 0) return null;

        return LocalFix.ReplaceLine(
            Id, $"Use {variable} itself",
            $"`for {variable} in {seq}` already hands over each element - `{variable}` is the element, not its position - so `{seq}[{variable}]` " +
            $"looks an element up by itself. `{variable}` alone is the value; `for i, {variable} in enumerate({seq})` gives the position as well.",
            source.Path, number, line[..span.Start] + variable + line[span.End..]);
    }
}

/// <summary><c>'tuple' object does not support item assignment</c> - the tuple should have been a list.</summary>
public sealed partial class PythonTupleToList : ILocalFixRule
{
    public string Id => "python-tuple-to-list";

    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*)\[")]
    private static partial Regex Target();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "'tuple' object does not support item assignment") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span || Target().Match(line[span.Start..span.End]) is not { Success: true } target) return null;

        var name = target.Groups["name"].Value;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (PythonCode.AssignmentLines(masked, name) is not [var index]) return null;

        var assignment = Regex.Match(masked[index], $@"^\s*{Regex.Escape(name)}\s*=\s*(?<value>.+?)\s*$");
        if (!assignment.Success) return null;

        var value = assignment.Groups["value"];
        var original = source.Lines[index];
        var start = value.Index;
        var end = value.Index + value.Length;
        string corrected;

        var row = masked[index];

        if (row[start] == '(' && Brackets.Closing(row, start) == end - 1 && PythonCode.TopLevelIndexOf(row[(start + 1)..(end - 1)], ",") >= 0)
            corrected = original[..start] + "[" + original[(start + 1)..(end - 1)] + "]" + original[end..];
        else if (PythonCode.TopLevelIndexOf(value.Value, ",") >= 0)
            corrected = original[..start] + "[" + original[start..end] + "]" + original[end..];
        else
            return null;

        return LocalFix.ReplaceLine(
            Id, $"Make {name} a list",
            $"`{name}` is a tuple, and a tuple cannot be changed once it is made. A list holds the same values and can be changed - square " +
            "brackets instead of round ones.",
            source.Path, index + 1, corrected);
    }
}

/// <summary><c>'str' object does not support item assignment</c> - a new string has to be built.</summary>
public sealed partial class PythonStringItemAssignment : ILocalFixRule
{
    public string Id => "python-string-item-assignment";

    [GeneratedRegex(@"^(?<indent>\s*)(?<name>[A-Za-z_]\w*)\[(?<index>[^\[\]:]+)\]\s*=(?!=)\s*(?<value>.+?)\s*$")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "'str' object does not support item assignment") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var (code, tail) = CodeText.SplitComment(line, Syntax.Python);
        if (Assignment().Match(code) is not { Success: true } m) return null;

        var name = m.Groups["name"].Value;
        var index = m.Groups["index"].Value.Trim();
        var value = m.Groups["value"].Value;

        var rebuilt = index switch
        {
            "0" => $"{name} = {value} + {name}[1:]",
            "-1" => $"{name} = {name}[:-1] + {value}",
            _ => $"{name} = {name}[:{index}] + {value} + {name}[{index} + 1:]",
        };

        return LocalFix.ReplaceLine(
            Id, $"Build a new string for {name}",
            "A string cannot be changed in place. To change one character, build a new string from the pieces around it and assign that.",
            source.Path, number, m.Groups["indent"].Value + rebuilt + tail);
    }
}

/// <summary><c>'map' object is not subscriptable</c>, and the same for zip, filter, generators and dictionary views.</summary>
public sealed partial class PythonNotSubscriptable : ILocalFixRule
{
    public string Id => "python-not-subscriptable";

    [GeneratedRegex(@"^'(?<type>map|filter|zip|generator|dict_keys|dict_values|dict_items|reversed|enumerate)' object is not subscriptable$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:map|filter|zip|reversed|enumerate)\s*\(|\.(?:keys|values|items)\s*\(\s*\)$")]
    private static partial Regex Lazy();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var text = line[span.Start..span.End];
        var maskedText = CodeText.Mask(text, Syntax.Python);
        if (!maskedText.EndsWith(']')) return null;

        var bracket = Brackets.Opening(maskedText, maskedText.Length - 1);
        if (bracket <= 0) return null;

        var receiver = text[..bracket].Trim();
        var type = message.Groups["type"].Value;
        var explanation = type == "generator"
            ? "A generator makes its values one at a time, as they are asked for, so there is no position to look up. A list comprehension - " +
              "square brackets - makes them all at once, and can be indexed."
            : $"A `{type}` hands its values out one at a time and cannot be indexed. `list(...)` collects them into a list, which can.";

        if (receiver.EndsWith(')') && Lazy().IsMatch(receiver))
        {
            return LocalFix.ReplaceLine(
                Id, $"Make it a list first: list({receiver})", explanation,
                source.Path, number, line[..span.Start] + $"list({receiver})" + line[(span.Start + bracket)..]);
        }

        if (!Regex.IsMatch(receiver, @"^[A-Za-z_]\w*$")) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (PythonCode.AssignmentLines(masked, receiver) is not [var index]) return null;

        var assignment = Regex.Match(masked[index], $@"^\s*{Regex.Escape(receiver)}\s*=\s*(?<value>.+?)\s*$");
        if (!assignment.Success) return null;

        var value = assignment.Groups["value"];
        var original = source.Lines[index];
        var start = value.Index;
        var end = start + value.Length;

        if (type == "generator")
        {
            var row = masked[index];
            if (row[start] != '(' || Brackets.Closing(row, start) != end - 1 || !Regex.IsMatch(value.Value, @"\sfor\s")) return null;

            return LocalFix.ReplaceLine(
                Id, $"Make {receiver} a list comprehension", explanation,
                source.Path, index + 1, original[..start] + "[" + original[(start + 1)..(end - 1)] + "]" + original[end..]);
        }

        if (!Lazy().IsMatch(value.Value) || !value.Value.EndsWith(')')) return null;

        return LocalFix.ReplaceLine(
            Id, $"Make {receiver} a list", explanation,
            source.Path, index + 1, original[..start] + "list(" + original[start..end] + ")" + original[end..]);
    }
}

/// <summary>Two names unpacked from each element of something that does not have two: a dict, or a list of numbers.</summary>
public sealed partial class PythonLoopUnpack : ILocalFixRule
{
    public string Id => "python-loop-unpack";

    [GeneratedRegex(@"^(?:too many values to unpack \(expected 2\)|not enough values to unpack \(expected 2, got \d+\))$")]
    private static partial Regex FromDict();

    [GeneratedRegex(@"^cannot unpack non-iterable (?:int|float|bool|NoneType) object$")]
    private static partial Regex FromScalars();

    [GeneratedRegex(@"^(?<head>\s*(?:async\s+)?for\s+\(?\s*[A-Za-z_]\w*\s*,\s*[A-Za-z_]\w*\s*\)?\s+in\s+)(?<iterable>[A-Za-z_][\w.]*)(?<tail>\s*:.*)$")]
    private static partial Regex Loop();

    public LocalFix? Propose(LocalFixContext context)
    {
        var message = context.Error.Message ?? "";
        var dict = PythonCode.Raised(context, "ValueError") && FromDict().IsMatch(message);
        var scalars = PythonCode.Raised(context, "TypeError") && FromScalars().IsMatch(message);

        if (!dict && !scalars || PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (Loop().Match(line) is not { Success: true } loop) return null;

        var iterable = loop.Groups["iterable"].Value;
        string replacement, explanation;

        if (dict)
        {
            var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
            if (PythonCode.AssignmentLines(masked, iterable) is not [var index]) return null;
            if (!Regex.IsMatch(masked[index], $@"^\s*{Regex.Escape(iterable)}\s*=\s*(?:\{{.*:.*\}}|dict\s*\()")) return null;

            replacement = $"{iterable}.items()";
            explanation = $"Looping over a dictionary gives its keys, one at a time - not keys and values. `{iterable}.items()` gives the pairs.";
        }
        else
        {
            replacement = $"enumerate({iterable})";
            explanation = $"Each element of `{iterable}` is a single value, so there is nothing to split into two names. " +
                          $"`enumerate({iterable})` gives each position together with its element.";
        }

        return LocalFix.ReplaceLine(
            Id, $"Loop over {replacement}", explanation,
            source.Path, number, loop.Groups["head"].Value + replacement + loop.Groups["tail"].Value);
    }
}

/// <summary><c>unhashable type: 'list'</c> - a list added to a set.</summary>
public sealed partial class PythonUnhashableList : ILocalFixRule
{
    public string Id => "python-unhashable-list";

    [GeneratedRegex(@"\.add\(\s*(?<open>\[)")]
    private static partial Regex AddList();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "unhashable type: 'list'") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (AddList().Matches(masked) is not { Count: 1 } hits) return null;

        var open = hits[0].Groups["open"].Index;
        var close = Brackets.Closing(masked, open);
        if (close < 0 || !masked[(close + 1)..].TrimStart().StartsWith(')')) return null;

        var inside = line[(open + 1)..close];
        var tuple = PythonCode.TopLevelIndexOf(masked[(open + 1)..close], ",") < 0 && inside.Trim().Length > 0 ? $"({inside.Trim()},)" : $"({inside})";

        return LocalFix.ReplaceLine(
            Id, "Add a tuple instead of a list",
            "A set - like a dictionary's keys - can only hold values that never change, and a list can change. A tuple holds the same values and cannot.",
            source.Path, number, line[..open] + tuple + line[(close + 1)..]);
    }
}

/// <summary><c>Unknown format code 'f' for object of type 'str'</c> - a number format applied to text.</summary>
public sealed partial class PythonFormatCodeOnText : ILocalFixRule
{
    public string Id => "python-format-code-on-text";

    [GeneratedRegex(@"^Unknown format code '(?<code>.)' for object of type 'str'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^\{(?<expr>[^{}:!]+?)(?:![rsa])?:[^{}]*\}$")]
    private static partial Regex Field();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "ValueError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span) return null;
        if (Field().Match(line[span.Start..span.End]) is not { Success: true } field) return null;

        var code = message.Groups["code"].Value;
        var convert = "dnbcoxX".Contains(code) ? "int" : "eEfFgG%".Contains(code) ? "float" : null;
        if (convert is null) return null;

        var expr = field.Groups["expr"];
        var start = span.Start + expr.Index;
        var text = expr.Value.Trim();

        return LocalFix.ReplaceLine(
            Id, $"Convert {text} with {convert}()",
            $"`:{code}` formats a number, but `{text}` is text - probably read from input or a file. `{convert}({text})` turns it into a number to format.",
            source.Path, number, line[..start] + $"{convert}({text})" + line[(start + expr.Length)..]);
    }
}

/// <summary><c>can't multiply sequence by non-int of type 'float'</c> - a <c>/</c> that should have been <c>//</c>.</summary>
public sealed class PythonSequenceTimesFloat : ILocalFixRule
{
    public string Id => "python-sequence-times-float";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "can't multiply sequence by non-int of type 'float'") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var masked = CodeText.Mask(line[span.Start..span.End], Syntax.Python);
        var slashes = Enumerable.Range(0, masked.Length)
            .Where(i => masked[i] == '/' && (i + 1 >= masked.Length || masked[i + 1] != '/') && (i == 0 || masked[i - 1] != '/'))
            .ToList();

        if (slashes is not [var slash]) return null;

        var at_ = span.Start + slash;

        return LocalFix.ReplaceLine(
            Id, "Divide with // to keep a whole number",
            "A string or a list can only be repeated a whole number of times. `/` always gives a float - `9 / 2` is `4.5` - and `//` gives the whole number, `4`.",
            source.Path, number, line[..at_] + "//" + line[(at_ + 1)..]);
    }
}

/// <summary><c>'str' object cannot be interpreted as an integer</c> for a number read with <c>input()</c>.</summary>
public sealed partial class PythonInputNotNumber : ILocalFixRule
{
    public string Id => "python-input-not-number";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "'str' object cannot be interpreted as an integer") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        var names = Regex.Matches(masked[number - 1], @"(?<![\w.])[A-Za-z_]\w*(?![\w(])").Select(m => m.Value).Distinct().ToList();

        var found = names
            .SelectMany(name => Enumerable.Range(0, number - 1)
                .Where(i => Regex.IsMatch(masked[i], $@"^\s*{Regex.Escape(name)}\s*=\s*input\s*\(.*\)\s*$"))
                .Select(i => (Name: name, Index: i)))
            .ToList();

        if (found is not [var only]) return null;

        var assignment = source.Lines[only.Index];
        var equals = assignment.IndexOf('=');
        var value = assignment[(equals + 1)..].Trim();

        return LocalFix.ReplaceLine(
            Id, $"Read {only.Name} as a whole number: int(input(...))",
            $"`input()` always returns text, even when what was typed is a number, and `{only.Name}` is used where a whole number is " +
            "needed. `int()` reads the number out of the text.",
            source.Path, only.Index + 1, assignment[..(equals + 1)] + " " + $"int({value})");
    }
}

/// <summary><c>a bytes-like object is required, not 'str'</c>, and <c>Strings must be encoded before hashing</c> - text where
/// bytes go.</summary>
public sealed partial class PythonTextToBytes : ILocalFixRule
{
    public string Id => "python-text-to-bytes";

    [GeneratedRegex(@"^(?:a bytes-like object is required, not 'str'|Strings must be encoded before hashing)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?:\.(?:sendall|send|sendto|write|update)|\bhashlib\.(?:md5|sha1|sha224|sha256|sha384|sha512|sha3_256|sha3_512|blake2b|blake2s)|\bhashlib\.new\s*\(\s*['""]\w+['""]\s*,)\s*\(?")]
    private static partial Regex Call();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        var hashing = context.Error.Message!.StartsWith("Strings", StringComparison.Ordinal);

        var calls = Call().Matches(masked).Where(m => hashing == m.Value.Contains("hashlib", StringComparison.Ordinal) || (hashing && m.Value.StartsWith(".update", StringComparison.Ordinal))).ToList();
        if (calls is not [var call]) return null;

        var open = masked.LastIndexOf('(', call.Index + call.Length - 1);
        if (call.Value.Contains("hashlib.new", StringComparison.Ordinal)) open = masked.IndexOf('(', call.Index);
        if (open < 0 || PythonCode.CallArguments(line, masked, open) is not { Count: > 0 } arguments) return null;

        var target = call.Value.Contains("hashlib.new", StringComparison.Ordinal) ? arguments.ElementAtOrDefault(1) : arguments[0];
        if (target.Text is null || target.Text.Contains('=')) return null;

        var text = target.Text;
        string corrected;

        if (Regex.IsMatch(text, @"^(?:""[\x20-\x7E]*""|'[\x20-\x7E]*')$") && !text.Contains('\\'))
            corrected = "b" + text;
        else if (PythonCode.IsSingleOperand(text) || Regex.IsMatch(text, @"^[fF]?(?:""[^""]*""|'[^']*')$"))
            corrected = text + ".encode()";
        else
            corrected = $"({text}).encode()";

        return LocalFix.ReplaceLine(
            Id, $"Turn the text into bytes: {corrected}",
            hashing
                ? "A hash is worked out over bytes, not text, because the same text can be stored as different bytes. `.encode()` turns " +
                  "text into bytes as UTF-8; a `b\"...\"` literal is bytes already."
                : "Sockets and binary files carry bytes, not text. `.encode()` turns text into bytes as UTF-8, and a `b\"...\"` literal is " +
                  "bytes already. The other end turns them back with `.decode()`.",
            source.Path, number, line[..target.Start] + corrected + line[(target.Start + text.Length)..]);
    }
}

/// <summary><c>object of type 'generator' has no len()</c>.</summary>
public sealed partial class PythonGeneratorLen : ILocalFixRule
{
    public string Id => "python-generator-len";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message is not ("object of type 'generator' has no len()" or "object of type 'map' has no len()" or "object of type 'filter' has no len()" or "object of type 'zip' has no len()")) return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.Mask(line, Syntax.Python);
        if (Regex.Matches(masked, @"(?<![\w.])len\s*\(").ToList() is not [var len]) return null;

        var open = masked.IndexOf('(', len.Index);
        var close = Brackets.Closing(masked, open);
        if (close < 0) return null;

        var inner = line[(open + 1)..close];
        var kind = context.Error.Message!.Split('\'')[1];

        return LocalFix.ReplaceLine(
            Id, "Count the items by making a list of them: len(list(...))",
            $"A {kind} hands its items out one at a time and does not know how many there are until it has run out, so it has no length. " +
            "`list(...)` collects them all first, and a list can be counted.",
            source.Path, number, line[..(open + 1)] + $"list({inner})" + line[close..]);
    }
}

/// <summary><c>Object of type set is not JSON serializable</c>.</summary>
public sealed partial class PythonJsonSet : ILocalFixRule
{
    public string Id => "python-json-set";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || context.Error.Message != "Object of type set is not JSON serializable") return null;
        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.Python);
        if (Regex.Matches(masked[number - 1], @"\bjson\.dumps?\s*\(\s*(?<name>[A-Za-z_]\w*)\s*[,)]").ToList() is not [var dump]) return null;

        var name = dump.Groups["name"];
        var assigned = Enumerable.Range(0, number - 1).Where(i => Regex.IsMatch(masked[i], $@"^\s*{Regex.Escape(name.Value)}\s*=")).ToList();

        if (assigned is not [var index] || !Regex.IsMatch(masked[index], $@"=\s*(?:set\s*\(|\{{[^:{{}}]*\}}\s*$)")) return null;

        return LocalFix.ReplaceLine(
            Id, $"Save the set as a list: list({name.Value})",
            "JSON has lists but no sets, so a set cannot be written as JSON directly. `list(...)` writes its items as a JSON list; reading " +
            "it back gives a list, which `set(...)` turns back into a set.",
            source.Path, number, line[..name.Index] + $"list({name.Value})" + line[(name.Index + name.Length)..]);
    }
}

/// <summary><c>unsupported operand type(s) for +: 'NoneType' and 'int'</c> from a function that works a value out and never
/// returns it.</summary>
public sealed partial class PythonMissingReturn : ILocalFixRule
{
    public string Id => "python-missing-return";

    [GeneratedRegex(@"^unsupported operand type\(s\) for (?<op>\S+): '(?<left>[\w.]+)' and '(?<right>[\w.]+)'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<func>[A-Za-z_]\w*)\(.*\)$")]
    private static partial Regex Call();

    [GeneratedRegex(@"^(?<indent>\s+)(?<var>[A-Za-z_]\w*)\s*(?:[+\-*/%]|//|\*\*)?=(?!=)")]
    private static partial Regex Assignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!PythonCode.Raised(context, "TypeError") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;

        var leftIsNone = message.Groups["left"].Value == "NoneType";
        if (leftIsNone == (message.Groups["right"].Value == "NoneType")) return null;

        if (PythonCode.Locate(context) is not { } at) return null;

        var (source, number, line) = at;
        if (PythonCode.UnderlineSpan(context.Error, number, line) is not { } span) return null;

        var op = message.Groups["op"].Value;
        var text = line[span.Start..span.End];
        var position = PythonCode.TopLevelIndexOf(CodeText.Mask(text, Syntax.Python), op);
        if (position < 0) return null;

        var operand = (leftIsNone ? text[..position] : text[(position + op.Length)..]).Trim();
        if (Call().Match(operand) is not { Success: true } call) return null;

        var func = call.Groups["func"].Value;
        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.Python);

        var defs = Enumerable.Range(0, masked.Count).Where(i => Regex.IsMatch(masked[i], $@"^def\s+{Regex.Escape(func)}\s*\(")).ToList();
        if (defs is not [var def]) return null;

        var (first, end) = PythonCode.BlockBody(lines, def);
        if (end <= first || Enumerable.Range(first, end - first).Any(i => Regex.IsMatch(masked[i], @"\breturn\b|\byield\b"))) return null;

        var last = end - 1;
        if (Assignment().Match(masked[last]) is not { Success: true } assigned) return null;

        var variable = assigned.Groups["var"].Value;

        return LocalFix.Insert(
            Id, $"Return {variable} from {func}",
            $"`{func}` works out `{variable}` but never returns it, and a function with no `return` gives back `None` - which is what the " +
            $"`{op}` then met. `return {variable}` hands the result back.",
            source.Path, last + 2, [assigned.Groups["indent"].Value + "return " + variable]);
    }
}
