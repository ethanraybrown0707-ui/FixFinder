using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Logic;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>PHP's own functions, as the PHP on PATH lists them - get_defined_functions()['internal'] - asked once.</summary>
internal static class PhpRuntime
{
    private static IReadOnlyList<string>? _internalFunctions;

    public static IReadOnlyList<string> InternalFunctions() => _internalFunctions ??= Ask();

    private static IReadOnlyList<string> Ask()
    {
        if (TargetFactory.FindOnPath("php") is not { } php) return [];

        try
        {
            using var process = Process.Start(new ProcessStartInfo(php, "-r \"echo implode(' ', get_defined_functions()['internal']);\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            });

            if (process is null) return [];

            var said = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(30_000) && process.ExitCode == 0 ? said.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return [];
        }
    }
}

/// <summary>What the PHP rules here share: the file's code with its text and comments blanked out.</summary>
internal static class PhpLines
{
    public static IReadOnlyList<string> Masked(SourceFile source) => CodeText.MaskAll(source.Lines, Syntax.CLike);

    /// <summary>The class a line is in - the nearest class line above it - and the lines of its body, or null outside any class.</summary>
    public static (string Name, int First, int End)? ClassAround(IReadOnlyList<string> masked, int index)
    {
        for (var above = index; above >= 0; above--)
        {
            if (Regex.Match(masked[above], @"^\s*(?:(?:abstract|final)\s+)?class\s+(?<name>\w+)") is not { Success: true } heading) continue;
            if (BraceBlocks.Body(masked, above) is not { } body || index >= body.End) return null;

            return (heading.Groups["name"].Value, body.First, body.End);
        }

        return null;
    }
}

/// <summary>
/// "syntax error, unexpected token "="", at a line that starts with a name and an =: the variable has no $ before its name, so
/// PHP read the name as a constant, which cannot be given a value. The $ goes in.
/// </summary>
public sealed partial class PhpMissingDollar : ILocalFixRule
{
    public string Id => "php-missing-dollar";

    [GeneratedRegex(@"^syntax error, unexpected token ""=""$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?<name>[A-Za-z_]\w*)\s*(?:[-+*/.]?=)(?!=)")]
    private static partial Regex NameGiven();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "ParseError" || !Message().IsMatch(context.Error.Message ?? "") || PhpCode.Locate(context) is not { } at) return null;
        if (NameGiven().Match(CodeText.Mask(at.Line, Syntax.CLike)) is not { Success: true } given) return null;

        var name = given.Groups["name"].Value;
        var nameAt = given.Groups["name"].Index;

        return LocalFix.ReplaceLine(Id, $"Write the variable as ${name}",
            $"Every PHP variable's name starts with $. Without it, PHP read {name} as the name of a constant - which can never be given a " +
            "value - and stopped at the =.",
            at.Source.Path, at.Number, at.Line[..nameAt] + "$" + at.Line[nameAt..]);
    }
}

/// <summary>
/// TypeError "Unsupported operand types: string + int": text and a number joined with +, which in PHP adds numbers only. Text is
/// joined with . - the + next to the text becomes one.
/// </summary>
public sealed partial class PhpJoinWithDot : ILocalFixRule
{
    public string Id => "php-join-with-dot";

    [GeneratedRegex(@"^Unsupported operand types: (?:string \+ (?:int|float)|(?:int|float) \+ string)$")]
    private static partial Regex Message();

    [GeneratedRegex(@"[""']\s*(?<plus>\+)(?!\+)|(?<![+])(?<plus>\+)\s*[""']")]
    private static partial Regex PlusBesideText();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "TypeError" || !Message().IsMatch(context.Error.Message ?? "") || PhpCode.Locate(context) is not { } at) return null;
        if (PlusBesideText().Matches(CodeText.Mask(at.Line, Syntax.CLike)) is not [var found]) return null;

        var plus = found.Groups["plus"].Index;
        return LocalFix.ReplaceLine(Id, "Join the text with .",
            "In PHP + adds numbers only, and text is joined with . - so PHP tried to add the text to the number, and stopped.",
            at.Source.Path, at.Number, at.Line[..plus] + "." + at.Line[(plus + 1)..]);
    }
}

/// <summary>
/// "Unclosed '{' on line 2": a block opened with { is never closed. PHP names the outer block the file ends inside; the one left
/// open is found as the program is laid out - each } closes the open { at its own indentation - and its } goes where its
/// indentation ends.
/// </summary>
public sealed partial class PhpUnclosedBrace : ILocalFixRule
{
    public string Id => "php-unclosed-brace";

    [GeneratedRegex(@"^Unclosed '\{' on line \d+$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "ParseError" || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (context.Read(context.Frame?.File) is not { } source || !PhpCode.IsPhp(source)) return null;

        var masked = PhpLines.Masked(source);
        var open = new List<int>();
        var neverClosed = new List<int>();

        for (var index = 0; index < masked.Count; index++)
        {
            var code = masked[index].Trim();
            if (code.StartsWith('}') && open.Count > 0)
            {
                var indent = CodeText.Indentation(masked[index]).Length;
                var closes = open.FindLastIndex(opener => CodeText.Indentation(masked[opener]).Length == indent);
                if (closes < 0) closes = open.Count - 1;

                neverClosed.AddRange(open.Skip(closes + 1));
                open.RemoveRange(closes, open.Count - closes);
            }

            if (code.EndsWith('{')) open.Add(index);
        }

        if (neverClosed.Concat(open).ToList() is not [var opened]) return null;

        var openerIndent = CodeText.Indentation(source.Lines[opened]);
        var back = Enumerable.Range(opened + 1, Math.Max(0, masked.Count - opened - 1))
            .FirstOrDefault(index => masked[index].Trim().Length > 0 && CodeText.Indentation(source.Lines[index]).Length <= openerIndent.Length, -1);
        var last = (back < 0 ? masked.Count : back) - 1;
        while (last > opened && source.Lines[last].Trim().Length == 0) last--;

        return LocalFix.Insert(Id, $"Close the {{ on line {opened + 1}",
            $"The {{ on line {opened + 1} is never closed, so PHP reached the end of the file inside it. Its }} goes where its indentation " +
            "ends - after the last line indented inside it.",
            source.Path, last + 2, [openerIndent + "}"]);
    }
}

/// <summary>
/// "syntax error, unexpected double-quote mark" - or "unexpected end of file", or "unexpected identifier", by what follows: a
/// string's closing quote is missing, so PHP read on inside it, and stopped further down. The first line above whose quote does
/// not close gets its closing quote, before the line's ; or ).
/// </summary>
public sealed partial class PhpUnclosedString : ILocalFixRule
{
    public string Id => "php-unclosed-string";

    // What follows the text left open decides PHP's words: the next quote, the end of the file, or a word the next quote leaves out.
    [GeneratedRegex(@"^syntax error, unexpected (?:(?:double|single)-quote mark|end of file|identifier ""[^""]*"")$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "ParseError" || !Message().IsMatch(context.Error.Message ?? "")) return null;

        // At the end of the file, PHP names the line after the last one - so the file is read, not the line.
        if (context.Frame?.Line is not { } named || context.Read(context.Frame.File) is not { } source || !PhpCode.IsPhp(source)) return null;

        for (var index = 0; index < Math.Min(named, source.Count); index++)
        {
            if (BraceRules.CloseString(source.Lines[index], Syntax.CLike) is not { } closed) continue;

            return LocalFix.ReplaceLine(Id, $"Close the string on line {index + 1}", BraceRules.CloseStringExplanation, source.Path, index + 1, closed);
        }

        return null;
    }
}

/// <summary>
/// "Call to undefined function strlenn()": no function has this name - not the file's own, and not PHP's. When one of them is
/// within two letters of it, and only one, the name becomes that one. PHP's own functions are the ones its PHP lists.
/// </summary>
public sealed partial class PhpNearestFunction : ILocalFixRule
{
    public string Id => "php-nearest-function";

    [GeneratedRegex(@"^Call to undefined function (?<name>\w+)\(\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "Error" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PhpCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var own = Regex.Matches(string.Join("\n", PhpLines.Masked(at.Source)), @"\bfunction\s+(?<name>\w+)\s*\(").Select(found => found.Groups["name"].Value);
        if (CodeText.Nearest(wrong, own.Concat(PhpRuntime.InternalFunctions()).Distinct()) is not { } right || right == wrong) return null;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.CLike) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"There is no function called {wrong} - not in the file, and not one of PHP's own - so PHP stopped at the call. {right}, a " +
            "letter or two apart, is one there is.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "Call to undefined method Student::gret()": the class has no method by that name. When one of its methods is within two
/// letters of it, and only one, the call is changed to it.
/// </summary>
public sealed partial class PhpNearestMethod : ILocalFixRule
{
    public string Id => "php-nearest-method";

    [GeneratedRegex(@"^Call to undefined method (?<class>\w+)::(?<name>\w+)\(\)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "Error" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PhpCode.Locate(context) is not { } at) return null;

        var masked = PhpLines.Masked(at.Source);
        var heading = Enumerable.Range(0, masked.Count).FirstOrDefault(index => Regex.IsMatch(masked[index], $@"^\s*(?:(?:abstract|final)\s+)?class\s+{Regex.Escape(said.Groups["class"].Value)}\b"), -1);
        if (heading < 0 || BraceBlocks.Body(masked, heading) is not { } body) return null;

        var methods = Enumerable.Range(body.First, body.End - body.First)
            .SelectMany(index => Regex.Matches(masked[index], @"\bfunction\s+(?<name>\w+)\s*\(").Select(found => found.Groups["name"].Value));

        var wrong = said.Groups["name"].Value;
        if (CodeText.Nearest(wrong, methods) is not { } right || right == wrong) return null;
        if (new Regex($@"->{Regex.Escape(wrong)}\s*\(").Matches(CodeText.Mask(at.Line, Syntax.CLike)) is not [var call]) return null;

        return LocalFix.ReplaceLine(Id, $"Call {right}, the method the class has",
            $"{said.Groups["class"].Value} has no method called {wrong}, so PHP stopped at the call. {right}, a letter or two apart, is the " +
            "method the class has.",
            at.Source.Path, at.Number, at.Line[..(call.Index + 2)] + right + at.Line[(call.Index + 2 + wrong.Length)..]);
    }
}

/// <summary>
/// "Undefined variable $count", inside a method of a class that has a property called $count: inside a method, $count is a
/// variable of the method's own, and the object's property is $this->count. Every $count in the method becomes $this->count.
/// </summary>
public sealed partial class PhpThisProperty : ILocalFixRule
{
    public string Id => "php-this-property";

    [GeneratedRegex(@"^Undefined variable \$(?<name>\w+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "Warning" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PhpCode.Locate(context) is not { } at) return null;

        var masked = PhpLines.Masked(at.Source);
        var name = said.Groups["name"].Value;
        if (PhpLines.ClassAround(masked, at.Number - 1) is not { } inClass) return null;

        var property = new Regex($@"^\s*(?:(?:public|private|protected|var|static|readonly)\s+)+(?:\??[\w\\]+\s+)?\${Regex.Escape(name)}\b");
        if (!Enumerable.Range(inClass.First, inClass.End - inClass.First).Any(index => property.IsMatch(masked[index]))) return null;

        // The method the line is in: the nearest function line above it, within the class.
        var method = Enumerable.Range(inClass.First, at.Number - inClass.First).Reverse().FirstOrDefault(index => Regex.IsMatch(masked[index], @"\bfunction\s+\w+\s*\("), -1);
        if (method < 0 || BraceBlocks.Body(masked, method) is not { } body || at.Number - 1 >= body.End) return null;

        // A method that takes $count as a parameter, or gives it a value of its own before this line, means its own $count.
        if (Regex.IsMatch(masked[method], $@"\${Regex.Escape(name)}\b")) return null;

        var variable = new Regex($@"\${Regex.Escape(name)}(?!\w)");
        var changed = Enumerable.Range(body.First, body.End - body.First)
            .Select(index => variable.Replace(at.Source.Lines[index], _ => $"$this->{name}"))
            .ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Use the object's own: $this->{name}",
            Explanation = $"Inside a method, ${name} is a variable of the method's own, which is given no value - so PHP used null for it. The " +
                          $"class's property is the object's own, and a method reaches it as $this->{name}.",
            File = at.Source.Path,
            StartLine = body.First + 1,
            RemoveCount = body.End - body.First,
            NewLines = changed,
        };
    }
}

/// <summary>
/// "Undefined array key "Bob"", where the value is added to or taken from: PHP gives null for a key the array does not have,
/// and carries on. ?? gives the value when the key is there, and 0 when it is not.
/// </summary>
public sealed partial class PhpMissingKeyDefault : ILocalFixRule
{
    public string Id => "php-missing-key-default";

    [GeneratedRegex(@"^Undefined array key (?<key>""[^""]*""|\d+)$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "Warning" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PhpCode.Locate(context) is not { } at) return null;

        var key = said.Groups["key"].Value;
        var lookups = new Regex($@"\$\w+(?:\[[^\]]+\])*\[\s*{Regex.Escape(key)}\s*\]").Matches(at.Line).ToList();
        if (lookups is not [var lookup]) return null;

        var after = at.Line[(lookup.Index + lookup.Length)..].TrimStart();
        var before = at.Line[..lookup.Index].TrimEnd();
        if (!(after.Length > 0 && after[0] is '+' or '-' or '*' or '/') && !(before.Length > 0 && before[^1] is '+' or '-' or '*')) return null;

        return LocalFix.ReplaceLine(Id, $"Give 0 when there is no {key}: ?? 0",
            $"The array has no {key}, so PHP gave null for it, and carried on. ?? gives the value when the key is there, and the value after " +
            "it - 0 here - when it is not; change the 0 to what a missing key should count as.",
            at.Source.Path, at.Number, at.Line[..lookup.Index] + $"({lookup.Value} ?? 0)" + at.Line[(lookup.Index + lookup.Length)..]);
    }
}

/// <summary>
/// "Undefined constant "name"", at $student.name: PHP reaches an object's property with ->, and . joins text - so $student.name
/// joined the object to a constant called name. It becomes $student->name when a class in the file has such a property.
/// </summary>
public sealed partial class PhpArrowForDot : ILocalFixRule
{
    public string Id => "php-arrow-for-dot";

    [GeneratedRegex(@"^Undefined constant ""(?<name>\w+)""$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "Error" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (PhpCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        var masked = PhpLines.Masked(at.Source);
        if (!masked.Any(line => Regex.IsMatch(line, $@"^\s*(?:(?:public|private|protected|var|readonly)\s+)+(?:\??\w+\s+)?\${Regex.Escape(name)}\b"))) return null;

        if (new Regex($@"(?<object>\$\w+)\s*\.\s*{Regex.Escape(name)}\b(?!\s*\()").Matches(CodeText.Mask(at.Line, Syntax.CLike)) is not [var dotted]) return null;

        var objectName = dotted.Groups["object"].Value;
        return LocalFix.ReplaceLine(Id, $"Reach the property with ->: {objectName}->{name}",
            $"PHP reaches an object's property with ->, and . joins text - so {objectName}.{name} joined the object to a constant called {name}, " +
            "and there is none.",
            at.Source.Path, at.Number, at.Line[..dotted.Index] + $"{objectName}->{name}" + at.Line[(dotted.Index + dotted.Length)..]);
    }
}

/// <summary>
/// "syntax error, unexpected token "{"", after elif ( ... ), as Python writes it: PHP writes elseif, and read elif ( ... ) as a
/// call to a function, which no { may follow. It becomes elseif.
/// </summary>
public sealed partial class PhpElseIf : ILocalFixRule
{
    public string Id => "php-elseif";

    [GeneratedRegex(@"^syntax error, unexpected token ""\{""$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*(?:\}\s*)?)elif\b")]
    private static partial Regex Elif();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "ParseError" || !Message().IsMatch(context.Error.Message ?? "") || PhpCode.Locate(context) is not { } at) return null;

        var masked = PhpLines.Masked(at.Source);
        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 2); index--)
        {
            if (Elif().Match(masked[index]) is not { Success: true } elif) continue;

            var lead = elif.Groups["lead"].Length;
            var line = at.Source.Lines[index];
            return LocalFix.ReplaceLine(Id, "Write elseif",
                "PHP writes \"else if\" as elseif - or else if. elif is Python's, and PHP read elif ( ... ) as a call to a function, which no { " +
                "can follow.",
                at.Source.Path, index + 1, line[..lead] + "elseif" + line[(lead + "elif".Length)..]);
        }

        return null;
    }
}

/// <summary>
/// The warning "Array to string conversion", where an array is joined to text with .: PHP writes the word Array in its place, and
/// carries on. implode joins the array's items with ", " between them.
/// </summary>
public sealed partial class PhpImplodeArray : ILocalFixRule
{
    public string Id => "php-implode-array";

    [GeneratedRegex(@"^Array to string conversion$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "Warning" || !Message().IsMatch(context.Error.Message ?? "") || PhpCode.Locate(context) is not { } at) return null;

        var masked = PhpLines.Masked(at.Source);
        var line = CodeText.Mask(at.Line, Syntax.CLike);

        var joined = Regex.Matches(line, @"(?<=\.\s*)\$(?<name>\w+)(?!\s*(?:\[|->|\())|\$(?<name>\w+)(?=\s*\.(?!=))")
            .Where(found => masked.Take(at.Number - 1).Any(earlier => Regex.IsMatch(earlier, $@"^\s*\${Regex.Escape(found.Groups["name"].Value)}\s*=\s*(?:\[|array\s*\()")))
            .ToList();
        if (joined is not [var array]) return null;

        return LocalFix.ReplaceLine(Id, $"Join the array's items: implode(\", \", {array.Value})",
            $"{array.Value} is an array, and an array cannot be joined to text - PHP wrote the word Array in its place. implode joins its " +
            "items into text, with \", \" between them.",
            at.Source.Path, at.Number, at.Line[..array.Index] + $"implode(\", \", {array.Value})" + at.Line[(array.Index + array.Length)..]);
    }
}
