using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What the Ruby rules share: whether an error is Ruby's, the line it is on, and the part of it Ruby's ^ marks point at.</summary>
internal static partial class RubyCode
{
    public static bool IsRuby(SourceFile source) => Path.GetExtension(source.Path).Equals(".rb", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Ruby file, line number and line an error is at, or null when it is at none that can be read.</summary>
    public static (SourceFile Source, int Number, string Line)? Locate(LocalFixContext context)
    {
        if (context.Error.LanguageId != "ruby" || context.Frame is not { Line: { } number } frame || context.Read(frame.File) is not { } source) return null;
        if (!IsRuby(source) || source.Line(number) is not { } line) return null;

        return (source, number, line);
    }

    [GeneratedRegex(@"^(?<indent>\s*)\^+\s*$")]
    private static partial Regex Carets();

    /// <summary>
    /// The part of a line Ruby 3.3's ^ marks point at: its message shows the line, and under it, ^ marks under the part it
    /// means. Null when the message shows no such marks, or they do not fall inside this line.
    /// </summary>
    public static (int Start, int End)? Pointed(ParsedError error, string line)
    {
        var shown = error.RawText.Split('\n');

        for (var index = 1; index < shown.Length; index++)
        {
            if (Carets().Match(shown[index]) is not { Success: true } carets) continue;
            if (shown[index - 1].TrimEnd() != line.TrimEnd()) continue;

            var start = carets.Groups["indent"].Length;
            var end = shown[index].TrimEnd().Length;
            return end <= line.Length && start < end ? (start, end) : null;
        }

        return null;
    }
}

/// <summary>
/// Ruby's own "Did you mean?" - for a name or a method it does not know: the name is changed to the one Ruby names first,
/// where it is on the line, and only there.
/// </summary>
public sealed partial class RubyDidYouMean : ILocalFixRule
{
    public string Id => "ruby-did-you-mean";

    [GeneratedRegex(@"^Did you mean\?\s+(?<name>[A-Za-z_][\w!?]*)", RegexOptions.Multiline)]
    private static partial Regex Suggested();

    [GeneratedRegex(@"(?:undefined (?:local variable or method|method)|uninitialized constant) [`']?(?<wrong>[A-Za-z_][\w!?:]*)'?")]
    private static partial Regex Unknown();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType is not ("NameError" or "NoMethodError")) return null;
        if (Suggested().Match(context.Error.RawText) is not { Success: true } suggestion || Unknown().Match(context.Error.Message ?? "") is not { Success: true } unknown) return null;
        if (RubyCode.Locate(context) is not { } at) return null;

        var wrong = unknown.Groups["wrong"].Value.Split("::")[^1];
        var right = suggestion.Groups["name"].Value;
        var places = new Regex($@"(?<![\w@$]){Regex.Escape(wrong)}(?![\w!?])").Matches(CodeText.Mask(at.Line, Syntax.Python));
        if (places.Count != 1 || wrong == right) return null;

        var place = places[0];
        var corrected = at.Line[..place.Index] + right + at.Line[(place.Index + place.Length)..];

        return LocalFix.ReplaceLine(Id, $"Change {wrong} to {right}",
            $"Ruby itself suggests `{right}` here, in place of `{wrong}`.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// ZeroDivisionError: the one division on the line it names is given an answer for when the divisor is zero, as Ruby's
/// condition ? a : b - which gives a value either way.
/// </summary>
public sealed partial class RubyDivisionGuard : ILocalFixRule
{
    public string Id => "ruby-division-guard";

    [GeneratedRegex(@"(?<dividend>[A-Za-z_@][\w.]*(?:\([^()]*\))?|\d+|\([^()]*\))\s*/\s*(?<divisor>\([^()]*\)|[A-Za-z_@][\w.]*(?:\([^()]*\))?)")]
    private static partial Regex Division();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "ZeroDivisionError" || RubyCode.Locate(context) is not { } at) return null;
        if (Division().Matches(CodeText.Mask(at.Line, Syntax.Python)) is not [var division]) return null;

        var divisor = at.Line.Substring(division.Groups["divisor"].Index, division.Groups["divisor"].Length);
        var expression = at.Line.Substring(division.Index, division.Length);
        var corrected = at.Line[..division.Index] + $"({divisor}.zero? ? 0 : {expression})" + at.Line[(division.Index + division.Length)..];

        return LocalFix.ReplaceLine(Id, "Give an answer for when the divisor is zero",
            "Dividing a whole number by zero has no answer, so Ruby stops the program. Checking the divisor first and giving 0 in that " +
            "case keeps the program going - change the 0 to whatever an empty case should give.",
            at.Source.Path, at.Number, corrected);
    }
}

/// <summary>
/// TypeError, "no implicit conversion of Integer into String": text and a number joined with +. The number Ruby's ^ marks
/// point at is turned into text with .to_s.
/// </summary>
public sealed partial class RubyNumberToText : ILocalFixRule
{
    public string Id => "ruby-number-to-text";

    [GeneratedRegex(@"^no implicit conversion of (?:Integer|Float) into String")]
    private static partial Regex Message();

    [GeneratedRegex(@"^[A-Za-z_@][\w.]*$|^\d+(?:\.\d+)?$")]
    private static partial Regex OneValue();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (context.Error.ShortExceptionType != "TypeError" || !Message().IsMatch(context.Error.Message ?? "")) return null;
        if (RubyCode.Locate(context) is not { } at || RubyCode.Pointed(context.Error, at.Line) is not { } pointed) return null;

        var value = at.Line[pointed.Start..pointed.End];
        if (!OneValue().IsMatch(value)) return null;

        var corrected = at.Line[..pointed.Start] + (char.IsDigit(value[0]) ? $"({value}).to_s" : $"{value}.to_s") + at.Line[pointed.End..];

        return LocalFix.ReplaceLine(Id, $"Turn {value} into text: {value}.to_s",
            "+ joins text only to text, and Ruby will not turn a number into text by itself. .to_s turns it into text first.",
            at.Source.Path, at.Number, corrected);
    }
}
