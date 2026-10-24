using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Dart rules share: whether an error is Dart's, and the line it is on.</summary>
internal static class DartCode
{
    public static bool IsDart(SourceFile source) => Path.GetExtension(source.Path).Equals(".dart", StringComparison.OrdinalIgnoreCase);

    public static (SourceFile Source, int Number, string Line, int? Column)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "dart" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsDart(source) || source.Line(number) is not { } line) return null;

        return (source, number, line, frame.Column);
    }

    /// <summary>The analyzer's diagnostic, by its name - undefined_identifier - or null for anything else.</summary>
    public static string? AnalyzerCode(LocalFixContext context) => context.Error.ExceptionType is "compile error" or "compile warning" ? context.Error.ErrorCode : null;

    /// <summary>The value a type's variable is given in place of null: 0 for a number, empty text, false.</summary>
    public static string? DefaultFor(string type) => type switch
    {
        "int" or "num" => "0",
        "double" => "0.0",
        "String" => "''",
        "bool" => "false",
        _ => null,
    };
}

/// <summary>"Expected to find ';'." at the end of a line: the semicolon goes there, after the last of the line's code.</summary>
public sealed class DartMissingSemicolon : ILocalFixRule
{
    public string Id => "dart-missing-semicolon";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "expected_token" || !(context.Error.Message ?? "").StartsWith("Expected to find ';'.", StringComparison.Ordinal)) return null;
        if (DartCode.Locate(context) is not { } at) return null;

        var (code, tail) = CodeText.SplitComment(at.Line, Syntax.CLike);
        if (code.TrimEnd().EndsWith(';') || code.TrimEnd().EndsWith('{') || code.TrimEnd().EndsWith('}')) return null;

        return LocalFix.ReplaceLine(Id, "Put a ; at the end of the line",
            "Each statement in Dart ends with a semicolon, and the analyzer found this one without its own.",
            at.Source.Path, at.Number, code.TrimEnd() + ";" + tail);
    }
}

/// <summary>
/// "Undefined name 'totl'.", or "The function 'averge' isn't defined.": when the file declares one name within two letters
/// of it - a variable, a parameter or a function - and only one, the name is changed to that one on the line the analyzer names.
/// </summary>
public sealed partial class DartNearestName : ILocalFixRule
{
    public string Id => "dart-nearest-name";

    [GeneratedRegex(@"^(?:Undefined name '(?<name>\w+)'\.|The function '(?<name>\w+)' isn't defined\.)")]
    private static partial Regex Message();

    /// <summary>A name the code declares: after var, final, const, a type, or as a function, a parameter or a loop's variable.</summary>
    [GeneratedRegex(@"\b(?:var|final|const|late|int|double|num|String|bool|dynamic|List<[^>]*>|Map<[^>]*>|Set<[^>]*>|[A-Z]\w*(?:<[^>]*>)?\??)\s+(?<name>[a-z_]\w*)\s*(?=[=;,)(]|\s+in\b)")]
    private static partial Regex Declared();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) is not ("undefined_identifier" or "undefined_function") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (DartCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        var declared = Declared().Matches(string.Join("\n", CodeText.MaskAll(at.Source.Lines, Syntax.CLike))).Select(name => name.Groups["name"].Value);
        if (CodeText.Nearest(wrong, declared) is not { } right) return null;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.CLike, at.Column - 1) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"{wrong} is declared nowhere, so Dart will not compile the program. {right}, a letter or two apart, is the name the file declares.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "A value of type 'double' can't be assigned to a variable of type 'int'." for <c>int average = total / count;</c>: / always
/// gives a decimal in Dart. ~/ is its whole-number division, which an int can hold.
/// </summary>
public sealed partial class DartWholeNumberDivision : ILocalFixRule
{
    public string Id => "dart-whole-number-division";

    [GeneratedRegex(@"^\s*(?:final\s+)?int\s+\w+\s*=\s*(?<value>[^;]+);")]
    private static partial Regex IntAssignment();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "invalid_assignment" ||
            !(context.Error.Message ?? "").StartsWith("A value of type 'double' can't be assigned to a variable of type 'int'.", StringComparison.Ordinal)) return null;
        if (DartCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        if (IntAssignment().Match(masked) is not { Success: true } assignment) return null;

        var slashes = Regex.Matches(assignment.Groups["value"].Value, @"(?<![~/*])/(?![/*=])");
        if (slashes is not [var slash]) return null;

        var place = assignment.Groups["value"].Index + slash.Index;

        return LocalFix.ReplaceLine(Id, "Divide as whole numbers with ~/",
            "/ always gives a decimal in Dart - 7 / 2 is 3.5 - which an int cannot hold. ~/ divides and keeps the whole number, 3; or " +
            "make the variable a double to keep the fraction.",
            at.Source.Path, at.Number, at.Line[..place] + "~/" + at.Line[(place + 1)..]);
    }
}

/// <summary>
/// "A value of type 'int?' can't be assigned to a variable of type 'int'." for a map looked up by a key: the key may not be
/// there, and then the lookup gives null - so a value is given to use in its place, with ??.
/// </summary>
public sealed partial class DartNullDefault : ILocalFixRule
{
    public string Id => "dart-null-default";

    [GeneratedRegex(@"^A value of type '(?<given>\w+)\?' can't be assigned to a variable of type '(?<wanted>\w+)'\.")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<head>\s*(?:final\s+)?\w+\s+\w+\s*=\s*)(?<value>\w+\s*\[[^\]]+\])\s*;")]
    private static partial Regex LookedUp();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "invalid_assignment" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (said.Groups["given"].Value != said.Groups["wanted"].Value || DartCode.DefaultFor(said.Groups["wanted"].Value) is not { } fallback) return null;
        if (DartCode.Locate(context) is not { } at || LookedUp().Match(at.Line) is not { Success: true } lookup) return null;

        var value = lookup.Groups["value"];

        return LocalFix.ReplaceLine(Id, $"Give a value for a missing key: ?? {fallback}",
            $"A map gives null for a key it does not have, so the lookup's type is {said.Groups["given"].Value}? - which may be null - and a " +
            $"{said.Groups["wanted"].Value} cannot hold null. ?? {fallback} gives {fallback} in its place; change it to whatever a missing key should give.",
            at.Source.Path, at.Number, at.Line[..(value.Index + value.Length)] + $" ?? {fallback}" + at.Line[(value.Index + value.Length)..]);
    }
}

/// <summary>"The argument type 'int' can't be assigned to the parameter type 'String'." for text + a number: the number is turned into text with toString().</summary>
public sealed partial class DartNumberToText : ILocalFixRule
{
    public string Id => "dart-number-to-text";

    [GeneratedRegex(@"[""']\s*\+\s*(?<name>[A-Za-z_]\w*)\b(?!\s*\.toString\(\))")]
    private static partial Regex TextPlusName();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "argument_type_not_assignable" ||
            !Regex.IsMatch(context.Error.Message ?? "", @"^The argument type '(?:int|double|num)' can't be assigned to the parameter type 'String'\.")) return null;
        if (DartCode.Locate(context) is not { } at) return null;

        if (TextPlusName().Matches(CodeText.Mask(at.Line, Syntax.CLike)) is not [var joined]) return null;

        var name = joined.Groups["name"];

        return LocalFix.ReplaceLine(Id, $"Turn the number into text: {name.Value}.toString()",
            "+ on text only joins more text, and Dart will not turn a number into text by itself. toString() does - or write it inside " +
            $"the text: \"...${name.Value}\".",
            at.Source.Path, at.Number, at.Line[..(name.Index + name.Length)] + ".toString()" + at.Line[(name.Index + name.Length)..]);
    }
}

/// <summary>"Conditions must have a static type of 'bool'." for <c>if (mark = 40)</c>: = stores a value; == compares.</summary>
public sealed partial class DartCompareWithDoubleEquals : ILocalFixRule
{
    public string Id => "dart-compare-with-double-equals";

    [GeneratedRegex(@"\b(?:if|while)\s*\(\s*\w+(?:\.\w+)*\s*(?<assign>=)(?![=>])\s*[^)]+\)")]
    private static partial Regex StoredInCondition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "non_bool_condition" || DartCode.Locate(context) is not { } at) return null;
        if (StoredInCondition().Match(CodeText.Mask(at.Line, Syntax.CLike)) is not { Success: true } condition) return null;

        var assign = condition.Groups["assign"];

        return LocalFix.ReplaceLine(Id, "Compare with ==",
            "== compares two values; = stores one, and what it gives - the value stored - is not true or false, which an if needs.",
            at.Source.Path, at.Number, at.Line[..assign.Index] + "==" + at.Line[(assign.Index + assign.Length)..]);
    }
}

/// <summary>
/// "The final variable 'passMark' can only be set once.": the variable is changed after all, so its declaration's final
/// becomes var.
/// </summary>
public sealed partial class DartFinalChanged : ILocalFixRule
{
    public string Id => "dart-final-changed";

    [GeneratedRegex(@"^The final variable '(?<name>\w+)' can only be set once\.")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "assignment_to_final_local" || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (DartCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        var declaration = new Regex($@"^(?<indent>\s*)final\s+(?:(?<type>[\w<>?,\s]+?)\s+)?{Regex.Escape(name)}\s*=");

        for (var index = at.Number - 2; index >= 0; index--)
        {
            if (declaration.Match(at.Source.Lines[index]) is not { Success: true } declared) continue;

            // final int passMark loses its final; final passMark, with no type, becomes var passMark.
            var typed = declared.Groups["type"].Success;
            var line = at.Source.Lines[index];
            var finalWord = line.IndexOf("final", StringComparison.Ordinal);
            var corrected = line[..finalWord] + (typed ? "" : "var ") + line[(finalWord + "final".Length)..].TrimStart();

            return LocalFix.ReplaceLine(Id, typed ? $"Let {name} change: take away final" : $"Let {name} change: var in place of final",
                $"final means {name} is set once and never again, but line {at.Number} sets it again. Declared without final, it can change.",
                at.Source.Path, index + 1, corrected);
        }

        return null;
    }
}

/// <summary>IntegerDivisionByZeroException: the one ~/ or % on the line Dart names is given an answer for when the divisor is zero.</summary>
public sealed partial class DartDivisionGuard : ILocalFixRule
{
    public string Id => "dart-division-guard";

    [GeneratedRegex(@"(?<dividend>[A-Za-z_][\w.]*(?:\([^()]*\))?|\d+|\([^()]*\))\s*(?<operator>~/|%)\s*(?<divisor>\([^()]*\)|[A-Za-z_][\w.]*(?:\([^()]*\))?|\d+)")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ExceptionType != "IntegerDivisionByZeroException" || DartCode.Locate(context) is not { } at) return null;
        if (Division().Matches(CodeText.Mask(at.Line, Syntax.CLike)) is not [var division]) return null;

        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = at.Line.Substring(division.Index, division.Length);
        var corrected = at.Line[..division.Index] + $"({divisor} == 0 ? 0 : {expression})" + at.Line[(division.Index + division.Length)..];

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            $"A whole number divided by zero with {division.Groups["operator"].Value} has no answer, so Dart stopped the program. The divisor is " +
            "checked first, and 0 given when it is zero - change the 0 to whatever an empty case should give.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// RangeError from a loop <c>for (var i = 0; i &lt;= marks.length; i++)</c>: the loop takes one step more than the list has
/// positions. The change is the logic check's too.
/// </summary>
public sealed class DartLoopToLength : ILocalFixRule
{
    public string Id => "dart-loop-to-length";

    /// <summary>How far above the line Dart names a loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 6;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType?.StartsWith("RangeError", StringComparison.Ordinal) != true || DartCode.Locate(context) is not { } at) return null;

        var masked = CodeText.MaskAll(at.Source.Lines, Syntax.CLike);

        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (DartLoops.LoopToLength().Match(masked[index]) is { Success: true } loop) return DartLoops.StrictlyBelowFix(Id, at.Source, index + 1, loop);
        }

        return null;
    }
}

/// <summary>The change the RangeError's rule and the logic check both make to a loop to &lt;= list.length, so the two are one fix.</summary>
internal static partial class DartLoops
{
    [GeneratedRegex(@"\bfor\s*\(\s*(?:var|int)\s+(?<variable>\w+)\s*=\s*0\s*;\s*\k<variable>\s*(?<comparison><=)\s*(?<list>\w+)\.length\s*;")]
    public static partial Regex LoopToLength();

    public static LocalFix StrictlyBelowFix(string id, SourceFile source, int lineNumber, Match loop)
    {
        var line = source.Lines[lineNumber - 1];
        var comparison = loop.Groups["comparison"];
        var list = loop.Groups["list"].Value;

        return LocalFix.ReplaceLine(id, "Stop the loop below the length: <",
            $"{list}'s positions run from 0 to {list}.length - 1, so the loop has to stop while {loop.Groups["variable"].Value} is still below {list}.length.",
            source.Path, lineNumber, line[..comparison.Index] + "<" + line[(comparison.Index + comparison.Length)..]);
    }
}

/// <summary>
/// "The method 'toUppercase' isn't defined for the type 'String'.", and the same for a getter: a member of one of Dart's own
/// types spelt differently. When one of that type's members is within two letters of it, and only one, the name is changed
/// to it - and the change is checked by dart analyze in a copy, like every other.
/// </summary>
public sealed partial class DartNearestMember : ILocalFixRule
{
    public string Id => "dart-nearest-member";

    [GeneratedRegex(@"^The (?:method|getter) '(?<name>\w+)' isn't defined for the type '(?<type>\w+)'\.")]
    private static partial Regex Message();

    /// <summary>The members of Dart's own types a program uses most - every one a member of that type in dart:core.</summary>
    private static readonly Dictionary<string, string[]> Members = new(StringComparer.Ordinal)
    {
        ["String"] =
        [
            "length", "isEmpty", "isNotEmpty", "toUpperCase", "toLowerCase", "trim", "trimLeft", "trimRight", "split", "contains",
            "substring", "indexOf", "lastIndexOf", "startsWith", "endsWith", "replaceAll", "replaceFirst", "padLeft", "padRight",
            "codeUnits", "runes", "compareTo", "toString",
        ],
        ["List"] =
        [
            "length", "isEmpty", "isNotEmpty", "first", "last", "add", "addAll", "remove", "removeAt", "removeLast", "insert", "clear",
            "contains", "indexOf", "sort", "reversed", "join", "map", "where", "reduce", "fold", "any", "every", "forEach", "toList",
            "toSet", "sublist", "take", "skip",
        ],
        ["Map"] =
        [
            "keys", "values", "entries", "length", "isEmpty", "isNotEmpty", "containsKey", "containsValue", "putIfAbsent", "remove",
            "forEach", "update", "clear",
        ],
        ["int"] = ["abs", "round", "floor", "ceil", "toInt", "toDouble", "toString", "toStringAsFixed", "isEven", "isOdd", "isNegative", "clamp", "compareTo", "remainder"],
        ["double"] = ["abs", "round", "floor", "ceil", "toInt", "toDouble", "toString", "toStringAsFixed", "isNegative", "isNaN", "isInfinite", "clamp", "compareTo", "remainder"],
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) is not ("undefined_method" or "undefined_getter") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (!Members.TryGetValue(said.Groups["type"].Value, out var members) || DartCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["name"].Value;
        if (CodeText.Nearest(wrong, members) is not { } right) return null;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.CLike, at.Column - 1) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"{said.Groups["type"].Value} has no member called {wrong}, so Dart will not compile the program. {right}, a letter or two apart, is " +
            $"one {said.Groups["type"].Value} has.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "Too many positional arguments: 1 expected, but 2 found." on <c>print("Total: %d", total)</c>: Dart's print takes one value
/// and fills nothing in. The change puts each value into the text where its code was, as Dart puts values into text -
/// <c>$total</c>, or <c>${total + 5}</c> for more than a name - and %.2f as <c>${value.toStringAsFixed(2)}</c>. Left alone where
/// a code has no such form - a width, %x - or the text already holds a $, which Dart would read as a value to put in.
/// </summary>
public sealed partial class DartFormatIntoText : ILocalFixRule
{
    public string Id => "dart-format-into-text";

    [GeneratedRegex(@"(?<![\w.])print\s*\(")]
    private static partial Regex PrintCall();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (DartCode.AnalyzerCode(context) != "extra_positional_arguments" || DartCode.Locate(context) is not { } at) return null;

        var masked = CodeText.Mask(at.Line, Syntax.CLike);
        if (PrintCall().Matches(masked) is not [var call]) return null;
        if (Logic.FormatCodePatterns.Arguments(masked, call.Index + call.Length, bracketed: true) is not [var first, .. var rest] ||
            !Logic.FormatCodePatterns.IsText(masked, first) || rest.Count == 0) return null;

        var format = at.Line[first.Start..first.End];
        var values = rest.Select(span => at.Line[span.Start..span.End]).ToList();
        if (format.Contains('$') || Logic.FormatCodePatterns.Codes(format).Count != values.Count || Filled(format, values) is not { } filled) return null;

        return LocalFix.ReplaceLine(Id, $"Put the values into the text: print({filled})",
            "Dart's print takes one value, and fills in no %d. Dart puts a value into text with $ - $total, or ${...} around anything " +
            "more than a name - so the values go into the text where their codes were.",
            at.Source.Path, at.Number, at.Line[..first.Start] + filled + at.Line[rest[^1].End..]);
    }

    /// <summary>The text with each code replaced by its value, put in with $ - or null where a code has no such form.</summary>
    private static string? Filled(string format, IReadOnlyList<string> values)
    {
        var next = 0;
        var fillable = true;
        var filled = Logic.FormatCodePatterns.FormatCode().Replace(format, code =>
        {
            if (code.Value == "%%") return "%";

            var kind = code.Groups["kind"].Value;
            var precision = code.Groups["precision"];
            if (code.Groups["flags"].Value.Length > 0 || code.Groups["width"].Value.Length > 0 || kind is not ("d" or "i" or "u" or "s" or "f") ||
                (precision.Success && kind != "f"))
            {
                fillable = false;
                return code.Value;
            }

            var value = values[next++];
            var name = Regex.IsMatch(value, @"^[A-Za-z_]\w*$");
            if (kind == "f") return $"${{{(name ? value : $"({value})")}.toStringAsFixed({(precision.Success ? precision.Value : "6")})}}";

            var followedByNamePart = code.Index + code.Length < format.Length && (char.IsLetterOrDigit(format[code.Index + code.Length]) || format[code.Index + code.Length] == '_');
            return name && !followedByNamePart ? $"${value}" : $"${{{value}}}";
        });

        return fillable ? filled : null;
    }
}
