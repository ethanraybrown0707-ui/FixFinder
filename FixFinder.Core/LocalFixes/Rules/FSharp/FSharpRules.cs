using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the F# rules share: whether an error is F#'s, the line and column it names, and F#'s code read without its text.</summary>
internal static partial class FSharpCode
{
    public static bool IsFSharp(SourceFile source) => Path.GetExtension(source.Path).ToLowerInvariant() is ".fsx" or ".fs";

    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "fsharp" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsFSharp(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    /// <summary>Whether F# gave this error, of this kind - and, when one is named, with this number of its own: FS0039.</summary>
    public static bool Is(LocalFixContext context, string kind, string? code = null) =>
        context.Error.LanguageId == "fsharp" && context.Error.ExceptionType == kind && (code is null || context.Error.ErrorCode == code);

    /// <summary>Where on the line F# says the problem is, counted from 0 - F# counts its columns from 1.</summary>
    public static int? At(LocalFixContext context) => context.Frame?.Column is { } column ? column - 1 : null;

    /// <summary>The code of a line with its comments and the insides of its text and characters blanked out.</summary>
    public static string Masked(string line) => CodeText.Mask(line, Syntax.FSharp);

    /// <summary>The line with <paramref name="added"/> put at the end of its code, before any comment.</summary>
    public static string Ended(string line, string added)
    {
        var (code, tail) = CodeText.SplitComment(line, Syntax.FSharp);
        return code + added + tail;
    }

    /// <summary><c>let total = </c> or <c>let total : int = </c> - a value given with let, not a function.</summary>
    public static Regex ValueBinding(string name) => new($@"^\s*let\s+(?<name>{Regex.Escape(name)})\s*(?::[^=]+)?=(?!=)");
}

/// <summary>
/// "The value or constructor 'totl' is not defined. Maybe you want one of the following: total": F#'s own suggestion, made on
/// the line and at the column it names - for a value, a function, or a module's function: List.lenght.
/// </summary>
public sealed partial class FSharpDidYouMean : ILocalFixRule
{
    public string Id => "fsharp-did-you-mean";

    [GeneratedRegex(@"^The [\w, ]+ '(?<wrong>[^']+)' is not defined\..* Maybe you want one of the following: (?<right>[^,\s]+)")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0039") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at) return null;

        var wrong = said.Groups["wrong"].Value;
        var right = said.Groups["right"].Value;
        if (CodeText.ReplaceWord(at.Line, wrong, right, Syntax.FSharp, FSharpCode.At(context)) is not { } corrected) return null;

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"F# knows nothing called {wrong} at this point in the script, so it would not run any of it. F# itself compared {wrong} " +
            $"with the names it knew here, and suggested {right}.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// "The value or constructor 'factorial' is not defined.", inside factorial itself: a function that calls itself has to be
/// defined with let rec. Without rec, factorial is not defined yet while its own body is read.
/// </summary>
public sealed partial class FSharpMissingRec : ILocalFixRule
{
    public string Id => "fsharp-missing-rec";

    [GeneratedRegex(@"^The value or constructor '(?<name>[^']+)' is not defined\.")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0039") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        var heading = new Regex($@"^(?<before>\s*let\s+(?:inline\s+)?)(?={Regex.Escape(name)}\s+[\w(])");
        var lines = at.Source.Lines;

        // The function the line is in: the nearest let above it - or on it - that sits further left than every line between.
        var leftmost = int.MaxValue;
        for (var index = at.Number - 1; index >= 0; index--)
        {
            var masked = FSharpCode.Masked(lines[index]);
            if (masked.Trim().Length == 0) continue;

            var indent = CodeText.Indentation(masked).Length;
            if (index < at.Number - 1 && indent >= leftmost) continue;

            if (heading.Match(masked) is { Success: true } let)
            {
                var before = let.Groups["before"].Length;
                return LocalFix.ReplaceLine(Id, $"Let {name} call itself: let rec {name}",
                    $"An F# function can use its own name inside its body only when it is defined with let rec - rec for recursive. Without " +
                    $"rec, {name} is not defined yet while its own body is read, so F# found no {name} there and would not run the script.",
                    at.Source.Path, index + 1, lines[index][..before] + "rec " + lines[index][before..]);
            }

            leftmost = Math.Min(leftmost, indent);
            if (indent == 0) break;
        }

        return null;
    }
}

/// <summary>
/// "This value is not mutable. Consider using the mutable keyword, e.g. 'let mutable count = expression'.": a value given with
/// let keeps its value, so count &lt;- ... needs count declared with let mutable. Its declaration - the nearest let of it above
/// - gets mutable.
/// </summary>
public sealed partial class FSharpNotMutable : ILocalFixRule
{
    public string Id => "fsharp-not-mutable";

    [GeneratedRegex(@"^This value is not mutable\. Consider using the mutable keyword, e\.g\. 'let mutable (?<name>[^\s']+) = expression'\.")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0027") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at) return null;

        var name = said.Groups["name"].Value;
        var declaration = FSharpCode.ValueBinding(name);
        var lines = at.Source.Lines;

        for (var index = at.Number - 2; index >= 0; index--)
        {
            if (declaration.Match(FSharpCode.Masked(lines[index])) is not { Success: true } found) continue;

            var nameAt = found.Groups["name"].Index;
            return LocalFix.ReplaceLine(Id, $"Declare {name} with let mutable",
                $"A value given with let keeps the value it was given - F# values do not change unless they are declared mutable. {name} <- " +
                $"gives {name} a new value, so {name} has to be declared with let mutable.",
                at.Source.Path, index + 1, lines[index][..nameAt] + "mutable " + lines[index][nameAt..]);
        }

        return null;
    }
}

/// <summary>
/// "The type 'int' does not support the operator '!='" - or '==': F# writes not equal as &lt;&gt;, and equal as a single =. The
/// operator at the column F# names is changed.
/// </summary>
public sealed partial class FSharpComparisonOperator : ILocalFixRule
{
    public string Id => "fsharp-comparison-operator";

    [GeneratedRegex(@"^The type '[^']+' does not support the operator '(?<written>!=|==)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0043") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at || FSharpCode.At(context) is not { } column) return null;

        var written = said.Groups["written"].Value;
        if (column + written.Length > at.Line.Length || FSharpCode.Masked(at.Line).Substring(column, written.Length) != written) return null;

        var (meant, means) = written == "!=" ? ("<>", "not equal") : ("=", "equal");
        return LocalFix.ReplaceLine(Id, $"Compare with {meant}",
            $"F# writes \"is {means} to\" as {meant}. {written} is not one of F#'s operators, so F# looked for an operator called {written} " +
            "on the values being compared, found none, and would not run the script.",
            at.Source.Path, at.Number, at.Line[..column] + meant + at.Line[(column + written.Length)..]);
    }
}

/// <summary>
/// "This construct may only be used within computation expressions. To return a value from an ordinary function simply write
/// the expression without 'return'.": the return F# names is taken out.
/// </summary>
public sealed partial class FSharpReturn : ILocalFixRule
{
    public string Id => "fsharp-return";

    [GeneratedRegex(@"^return\s+")]
    private static partial Regex ReturnWord();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0748") || FSharpCode.Locate(context) is not { } at || FSharpCode.At(context) is not { } column) return null;
        if (column >= at.Line.Length || ReturnWord().Match(FSharpCode.Masked(at.Line)[column..]) is not { Success: true } word) return null;

        return LocalFix.ReplaceLine(Id, "Take out return",
            "An F# function gives back the value of its last expression, with no word in front of it. return belongs to computation " +
            "expressions, such as async { }, so F# would not take it here.",
            at.Source.Path, at.Number, at.Line[..column] + at.Line[(column + word.Length)..]);
    }
}

/// <summary>"Missing 'do' in 'for' expression.", at the for: the line gets the do that starts the loop's body.</summary>
public sealed partial class FSharpMissingDo : ILocalFixRule
{
    public string Id => "fsharp-missing-do";

    [GeneratedRegex(@"^\s*for\b.*\b(?:in|to|downto)\b.*\S")]
    private static partial Regex ForHeading();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS3123") || FSharpCode.Locate(context) is not { } at) return null;

        var masked = FSharpCode.Masked(at.Line);
        if (!ForHeading().IsMatch(masked) || Regex.IsMatch(masked, @"\bdo\b")) return null;

        return LocalFix.ReplaceLine(Id, "End the for line with do",
            "An F# for loop is written for ... in ... do, and the do starts the loop's body. Without it, F# could not tell where the " +
            "loop's heading ends, and would not run the script.",
            at.Source.Path, at.Number, FSharpCode.Ended(at.Line, " do"));
    }
}

/// <summary>
/// "Incomplete conditional. Expected 'if &lt;expr&gt; then &lt;expr&gt;'...", named on the line after the if: the if - or elif -
/// above it has no then, and gets one.
/// </summary>
public sealed partial class FSharpMissingThen : ILocalFixRule
{
    public string Id => "fsharp-missing-then";

    /// <summary>How far above the line F# names the if is looked for.</summary>
    private const int LinesAboveLooked = 3;

    [GeneratedRegex(@"^\s*(?:if|elif|else\s+if)\b")]
    private static partial Regex Condition();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0589") || FSharpCode.Locate(context) is not { } at) return null;

        var lines = at.Source.Lines;
        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            var masked = FSharpCode.Masked(lines[index]);
            if (!Condition().IsMatch(masked)) continue;
            if (Regex.IsMatch(masked, @"\bthen\b")) return null;

            return LocalFix.ReplaceLine(Id, "End the condition with then",
                "An F# if is written if ... then, and the then ends the condition. Without it, F# read on into the next line as more of " +
                "the condition, and would not run the script.",
                at.Source.Path, index + 1, FSharpCode.Ended(lines[index], " then"));
        }

        return null;
    }
}

/// <summary>
/// "The '!' operator is used to dereference a ref cell. Consider using 'not expr' here.": !finished, written as other languages
/// write not, becomes not finished.
/// </summary>
public sealed class FSharpNot : ILocalFixRule
{
    public string Id => "fsharp-not";

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0001") ||
            !(context.Error.Message ?? "").Contains("Consider using 'not expr' here.", StringComparison.Ordinal)) return null;
        if (FSharpCode.Locate(context) is not { } at || FSharpCode.At(context) is not { } column) return null;

        var bang = column - 1;
        if (bang < 0 || bang >= at.Line.Length || FSharpCode.Masked(at.Line)[bang] != '!') return null;

        return LocalFix.ReplaceLine(Id, "Write not",
            "F# writes \"not\" as the word not. ! in front of a value reads what a ref cell holds, so F# expected a ref cell there, found " +
            "a true-or-false value, and would not run the script.",
            at.Source.Path, at.Number, at.Line[..bang] + "not " + at.Line[(bang + 1)..]);
    }
}

/// <summary>
/// The warning "The result of this equality expression has type 'bool' and is implicitly discarded. ... If you intended to
/// mutate a value, then use the '&lt;-' operator e.g. 'count &lt;- expression'.": count = count + 1 compares count with
/// count + 1, and changes nothing. The = becomes &lt;-.
/// </summary>
public sealed partial class FSharpAssignWithArrow : ILocalFixRule
{
    public string Id => "fsharp-assign-with-arrow";

    [GeneratedRegex(@"If you intended to mutate a value, then use the '<-' operator e\.g\. '(?<name>[^\s']+) <- expression'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.Warning, "FS0020") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at) return null;

        return FSharpAssignments.ArrowFix(Id, at.Source, at.Number, said.Groups["name"].Value) is { } fix ? fix with { ResolvesWarning = "FS0020" } : null;
    }
}

/// <summary>The change a comparison written for an assignment gets, from F#'s warning and from the logic check alike, so the two are one fix.</summary>
internal static class FSharpAssignments
{
    /// <summary><c>count = count + 1</c> as a line of its own, with the = to change.</summary>
    public static Regex Comparison(string name) => new($@"^(?<before>\s*{Regex.Escape(name)}\s*)=(?!=)");

    public static LocalFix? ArrowFix(string id, SourceFile source, int lineNumber, string name)
    {
        var line = source.Lines[lineNumber - 1];
        if (Comparison(name).Match(FSharpCode.Masked(line)) is not { Success: true } compared) return null;

        var equals = compared.Groups["before"].Length;
        return LocalFix.ReplaceLine(id, $"Give {name} its new value with <-",
            $"In F# = compares two values; it does not change one. {name} = ... works out whether {name} equals the value on the right, and " +
            $"the true or false it gives is thrown away, so {name} keeps its old value. <- gives a mutable value a new one.",
            source.Path, lineNumber, line[..equals] + "<-" + line[(equals + 1)..]);
    }
}

/// <summary>
/// A printf whose format does not fit a value it is given: "The type 'string' is not compatible with any of the types byte,
/// int16,int32,... arising from the use of a printf-style format string" for %d given text, and "This expression was expected
/// to have type 'string' but here has type 'int'" for %s given a number. The value is the one at the column F# names; its
/// place among the values after the format says which % it fills, and that % becomes the one for its type.
/// </summary>
public sealed partial class FSharpPrintfFormat : ILocalFixRule
{
    public string Id => "fsharp-printf-format";

    [GeneratedRegex(@"^The type '(?<type>[^']+)' is not compatible with any of the types [\w,]+, arising from the use of a printf-style format string$")]
    private static partial Regex NotCompatible();

    [GeneratedRegex(@"^This expression was expected to have type 'string' but here has type '(?<type>[^']+)'$")]
    private static partial Regex NotText();

    [GeneratedRegex(@"\b(?:printfn|printf|eprintfn|eprintf|sprintf|failwithf)\s+(?="")")]
    private static partial Regex PrintfCall();

    [GeneratedRegex(@"%%|%(?<flags>[-+ 0]*)(?<width>\d+|\*)?(?:\.(?<precision>\d+|\*))?(?<kind>[a-zA-Z])")]
    private static partial Regex Placeholder();

    /// <summary>The % for a value of each of F#'s own types, and what it writes.</summary>
    private static readonly IReadOnlyDictionary<string, (string Kind, string Writes)> ForType = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["string"] = ("s", "text"),
        ["int"] = ("d", "a whole number"),
        ["int64"] = ("d", "a whole number"),
        ["float"] = ("f", "a decimal number"),
        ["float32"] = ("f", "a decimal number"),
        ["decimal"] = ("M", "a decimal number"),
        ["bool"] = ("b", "true or false"),
        ["char"] = ("c", "a character"),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0001") || FSharpCode.Locate(context) is not { } at || FSharpCode.At(context) is not { } column) return null;

        var message = context.Error.Message ?? "";
        var type = NotCompatible().Match(message) is { Success: true } notCompatible ? notCompatible.Groups["type"].Value
            : NotText().Match(message) is { Success: true } notText ? notText.Groups["type"].Value
            : null;
        if (type is null || !ForType.TryGetValue(type, out var meant)) return null;

        var masked = FSharpCode.Masked(at.Line);
        if (PrintfCall().Match(masked) is not { Success: true } call) return null;

        // The format, read from the line itself - its inside is blanked in the masked line - and the values after it.
        var formatStart = call.Index + call.Length;
        var formatEnd = masked.IndexOf('"', formatStart + 1);
        if (formatEnd < 0 || column <= formatEnd) return null;

        var placeholders = Placeholder().Matches(at.Line[(formatStart + 1)..formatEnd]).Where(found => found.Value != "%%").ToList();
        if (placeholders.Any(found => found.Groups["width"].Value == "*" || found.Groups["precision"].Value == "*")) return null;

        var values = Values(masked, formatEnd + 1);
        var place = values.FindIndex(value => column >= value.Start && column < value.End);
        if (place < 0 || place >= placeholders.Count) return null;

        var written = placeholders[place];
        var kind = written.Groups["kind"];
        if (kind.Value == meant.Kind) return null;

        var kindAt = formatStart + 1 + kind.Index;
        var corrected = at.Line[..kindAt] + meant.Kind + at.Line[(kindAt + 1)..];
        var value = at.Line[values[place].Start..values[place].End];

        return LocalFix.ReplaceLine(Id, $"Write {value} with %{meant.Kind}",
            $"Each % in a printf format stands for one of the values after it, in order, and its letter says what kind of value: %{kind.Value} " +
            $"is not for a {type}, and {value}, the value it stands for, is one. %{meant.Kind} writes {meant.Writes}.",
            at.Source.Path, at.Number, corrected);
    }

    /// <summary>The values after a printf's format, where each starts and ends: each a word, or a whole bracket, apart from the next by spaces.</summary>
    private static List<(int Start, int End)> Values(string masked, int from)
    {
        var values = new List<(int Start, int End)>();
        var depth = 0;
        var start = -1;

        for (var index = from; index <= masked.Length; index++)
        {
            var character = index < masked.Length ? masked[index] : ' ';

            if (depth == 0 && char.IsWhiteSpace(character))
            {
                if (start >= 0) values.Add((start, index));
                start = -1;
                continue;
            }

            // A pipe or an operator after the values ends them: printfn "%d" total |> ignore.
            if (depth == 0 && start < 0 && character is '|' or ')' or ']') break;

            if (start < 0) start = index;
            if (character is '(' or '[' or '{') depth++;
            if (character is ')' or ']' or '}') depth--;
            if (depth < 0) break;
        }

        return values;
    }
}

/// <summary>
/// The note "This list expression contains a single tuple element. Did you mean to use ';' instead of ',' to separate list
/// elements?": [70, 80, 90] is a list of one item, the three numbers together. The commas between its items become ;.
/// </summary>
public sealed partial class FSharpListSemicolons : ILocalFixRule
{
    public string Id => "fsharp-list-semicolons";

    [GeneratedRegex(@"^This (?<kind>list|array) expression contains a single tuple element\.")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.Warning, "FS3886") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at || FSharpCode.At(context) is not { } column) return null;

        var masked = FSharpCode.Masked(at.Line);
        if (column >= masked.Length || masked[column] != '[') return null;

        // The commas inside the brackets F# names, and not inside anything within them: [(1, 2), (3, 4)] keeps its pairs.
        var corrected = at.Line.ToCharArray();
        var depth = 0;
        var closed = false;

        for (var index = column; index < masked.Length; index++)
        {
            if (masked[index] is '(' or '[' or '{') depth++;
            if (masked[index] is ')' or ']' or '}') depth--;
            if (depth == 1 && masked[index] == ',') corrected[index] = ';';

            if (depth == 0)
            {
                closed = true;
                break;
            }
        }

        if (!closed) return null;

        var kind = said.Groups["kind"].Value;
        return new string(corrected) is var changed && changed != at.Line
            ? LocalFix.ReplaceLine(Id, $"Separate the {kind}'s items with ;",
                $"In F# the items of a {kind} are separated by ; and a comma joins values into one tuple - so [70, 80, 90] is a {kind} of " +
                $"one item, the three numbers together. With ; it is a {kind} of three numbers.",
                at.Source.Path, at.Number, changed) with { ResolvesWarning = "FS3886" }
            : null;
    }
}

/// <summary>
/// "The type 'int' does not match the type 'string'", at a number joined to text with + - "Mark: " + mark: F# turns nothing
/// into text by itself, so the number at the column F# names becomes string mark.
/// </summary>
public sealed partial class FSharpNumberToText : ILocalFixRule
{
    public string Id => "fsharp-number-to-text";

    [GeneratedRegex(@"^The type '(?<type>int|int64|float|decimal|bool|char)' does not match the type 'string'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?:[A-Za-z_][\w']*(?:\.[A-Za-z_][\w']*)*|\d+(?:\.\d+)?|\((?:[^()]|\([^()]*\))*\))")]
    private static partial Regex Operand();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0001") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at || FSharpCode.At(context) is not { } column) return null;

        var masked = FSharpCode.Masked(at.Line);
        if (column >= masked.Length || !masked[..column].TrimEnd().EndsWith('+')) return null;
        if (Operand().Match(masked[column..]) is not { Success: true } operand) return null;

        var value = at.Line.Substring(column, operand.Length);

        return LocalFix.ReplaceLine(Id, $"Turn {value} into text: string {value}",
            $"+ joins two pieces of text, or adds two numbers - and in F# both sides have to be the same type, as F# turns nothing into " +
            $"text by itself. {value} is a {said.Groups["type"].Value}, so string turns it into text first.",
            at.Source.Path, at.Number, at.Line[..column] + $"string {value}" + at.Line[(column + operand.Length)..]);
    }
}

/// <summary>
/// System.DivideByZeroException: a whole number divided by zero. The one division on the line - / or % - is given an answer,
/// 0, for when what it divides by is zero. A decimal divided by zero gives infinity instead, and stops nothing.
/// </summary>
public sealed partial class FSharpDivisionGuard : ILocalFixRule
{
    public string Id => "fsharp-division-guard";

    private const string Operand = @"(?:[A-Za-z_][\w']*(?:\.[A-Za-z_][\w']*)*|\d+|\((?:[^()]|\([^()]*\))*\))";

    /// <summary>A division standing on its own: not a function's argument on either side - List.sum marks / count is not one.</summary>
    [GeneratedRegex(@"(?<=(?:^|[=(,;]|<-|->|\bthen|\belse|\bin)\s*)(?<dividend>" + Operand + @")\s*(?<operator>/|%)\s*(?<divisor>" + Operand + @")(?=\s*(?:$|[),;\]]|\bthen\b|\belse\b|\bin\b))")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "fsharp", ExceptionType: "System.DivideByZeroException" } || FSharpCode.Locate(context) is not { } at) return null;

        var masked = FSharpCode.Masked(at.Line);
        if (masked.Count(character => character is '/' or '%') != 1 || Division().Match(masked) is not { Success: true } division) return null;

        var written = at.Line.Substring(division.Index, division.Length);
        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);

        return LocalFix.ReplaceLine(Id, $"Give an answer for when {divisor} is zero",
            $"Dividing a whole number by zero stops an F# script with System.DivideByZeroException. The change works {written} out only " +
            $"when {divisor} is not zero, and gives 0 when it is.",
            at.Source.Path, at.Number, at.Line[..division.Index] + $"(if {divisor} = 0 then 0 else {written})" + at.Line[(division.Index + division.Length)..]);
    }
}

/// <summary>
/// System.IndexOutOfRangeException from a loop to an array's Length - for index in 0 .. marks.Length do, or for index = 0 to
/// marks.Length do: both count to Length itself, one past the last position. The change is the logic check's too.
/// </summary>
public sealed class FSharpLoopToLength : ILocalFixRule
{
    public string Id => "fsharp-loop-to-length";

    /// <summary>How far above the line .NET names the loop's heading is looked for.</summary>
    private const int LinesAboveLooked = 6;

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error is not { LanguageId: "fsharp", ExceptionType: "System.IndexOutOfRangeException" } || FSharpCode.Locate(context) is not { } at) return null;

        var lines = at.Source.Lines;
        for (var index = at.Number - 1; index >= Math.Max(0, at.Number - 1 - LinesAboveLooked); index--)
        {
            if (FSharpLoops.ToLength().Match(FSharpCode.Masked(lines[index])) is { Success: true } loop) return FSharpLoops.LastPositionFix(Id, at.Source, index + 1, loop);
        }

        return null;
    }
}

/// <summary>The change the crash's rule and the logic check both make to a loop to an array's Length, so the two are one fix.</summary>
internal static partial class FSharpLoops
{
    [GeneratedRegex(@"^\s*for\s+(?<variable>[A-Za-z_][\w']*)\s+(?:in\s+0\s*\.\.\s*|=\s*0\s+to\s+)(?<bound>(?<array>[A-Za-z_][\w']*)\.Length)\s+do\b")]
    public static partial Regex ToLength();

    public static LocalFix LastPositionFix(string id, SourceFile source, int lineNumber, Match loop)
    {
        var line = source.Lines[lineNumber - 1];
        var bound = loop.Groups["bound"];
        var array = loop.Groups["array"].Value;

        return LocalFix.ReplaceLine(id, $"Stop the loop at the last position: {array}.Length - 1",
            $"{array}'s positions run from 0 to {array}.Length - 1, and an F# loop to {array}.Length counts to {array}.Length itself - one " +
            $"past the last position. Counting to {array}.Length - 1 stops at the last one.",
            source.Path, lineNumber, line[..bound.Index] + $"{array}.Length - 1" + line[(bound.Index + bound.Length)..]);
    }
}

/// <summary>
/// "This expression was expected to have type 'int' but is a tuple of type 'int * int'", at the brackets of area(3, 4): an F#
/// function defined as let area width height takes its arguments one after another, separated by spaces, and area(3, 4) gives
/// it one argument - the pair (3, 4). When the script defines the function with as many parameters as the brackets hold
/// values, the call becomes area 3 4 - each value that is more than a name or a number kept in brackets of its own.
/// </summary>
public sealed partial class FSharpCurriedArguments : ILocalFixRule
{
    public string Id => "fsharp-curried-arguments";

    [GeneratedRegex(@"^This expression was expected to have type '[^']+' but is a tuple of type '[^']+'$")]
    private static partial Regex Message();

    [GeneratedRegex(@"(?<name>[A-Za-z_][\w']*)\s*$")]
    private static partial Regex NameBefore();

    [GeneratedRegex(@"^[A-Za-z_][\w'.]*$|^-?\d+(?:\.\d+)?$|^""[^""]*""$")]
    private static partial Regex Simple();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0001") || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (FSharpCode.Locate(context) is not { } at || FSharpCode.At(context) is not { } column) return null;

        var masked = FSharpCode.Masked(at.Line);
        if (column >= masked.Length || masked[column] != '(' || NameBefore().Match(masked[..column]) is not { Success: true } called) return null;

        // The values between the brackets, split at the commas that are not inside anything within them.
        var values = new List<(int Start, int End)>();
        var depth = 0;
        var start = column + 1;
        var close = -1;

        for (var index = column; index < masked.Length && close < 0; index++)
        {
            if (masked[index] is '(' or '[' or '{') depth++;
            if (masked[index] is ')' or ']' or '}') depth--;

            if (depth == 1 && masked[index] == ',' || depth == 0)
            {
                values.Add((start, index));
                start = index + 1;
                if (depth == 0) close = index;
            }
        }

        var name = called.Groups["name"].Value;
        var definition = new Regex($@"^\s*let\s+(?:rec\s+|inline\s+)?{Regex.Escape(name)}(?<parameters>(?:\s+(?:[a-z_][\w']*|\([^()]*\)))+)\s*(?::[^=]+)?=");
        var defined = at.Source.Lines.Select(FSharpCode.Masked).Select(line => definition.Match(line)).FirstOrDefault(found => found.Success);
        if (close < 0 || defined is null) return null;

        if (ParametersIn(defined.Groups["parameters"].Value) != values.Count || values.Count < 2) return null;

        var arguments = values
            .Select(value => at.Line[value.Start..value.End].Trim())
            .Select(value => Simple().IsMatch(value) ? value : $"({value})");

        var replaced = (called.Groups["name"].Index + name.Length == column ? " " : "") + string.Join(" ", arguments);
        return LocalFix.ReplaceLine(Id, $"Give {name} its arguments with spaces: {name} {string.Join(" ", arguments)}",
            $"{name} is defined with its parameters one after another, so it takes its arguments one after another, separated by spaces. " +
            $"With brackets and commas, {name} is given one argument - the values joined into one tuple - where it needs {values.Count}.",
            at.Source.Path, at.Number, at.Line[..column] + replaced + at.Line[(close + 1)..]);
    }

    /// <summary>How many parameters a function's definition lists: the words, and the bracketed ones - (width: int) - apart by spaces.</summary>
    private static int ParametersIn(string parameters)
    {
        var count = 0;
        var depth = 0;
        var inOne = false;

        foreach (var character in parameters)
        {
            if (depth == 0 && char.IsWhiteSpace(character))
            {
                inOne = false;
                continue;
            }

            if (!inOne) count++;
            inOne = true;
            if (character == '(') depth++;
            if (character == ')') depth--;
        }

        return count;
    }
}

/// <summary>
/// "Unmatched '('", at the bracket F# names - or the same for [, [| or {: the bracket is closed at the end of its line, and the
/// change is checked like every other, so a bracket meant to close further on is left to its own error.
/// </summary>
public sealed partial class FSharpUnmatchedBracket : ILocalFixRule
{
    public string Id => "fsharp-unmatched-bracket";

    [GeneratedRegex(@"^Unmatched '(?<opening>\(|\[|\[\||\{)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!FSharpCode.Is(context, FSharpParser.CompileError, "FS0583") || Message().Match(context.Error.Message ?? "") is not { Success: true } said) return null;
        if (FSharpCode.Locate(context) is not { } at) return null;

        var opening = said.Groups["opening"].Value;
        var closing = opening switch { "(" => ")", "[" => "]", "[|" => "|]", _ => "}" };

        return LocalFix.ReplaceLine(Id, $"Close the {opening} with {closing}",
            $"Every {opening} in F# is closed by a {closing}, and this one is not closed anywhere, so F# could not tell where what is " +
            $"inside it ends, and would not run the script. The {closing} goes at the end of its line.",
            at.Source.Path, at.Number, FSharpCode.Ended(at.Line, closing));
    }
}
