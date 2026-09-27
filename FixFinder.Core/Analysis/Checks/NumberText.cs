using System.Globalization;
using System.Numerics;
using System.Text;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// Whether a text reads as a number the way each language's own conversion reads it - Python's int() and float(),
/// Java's Integer.parseInt and Double.parseDouble, C#'s int.Parse and double.Parse. The three disagree: float("inf")
/// is infinity in Python and an error in Java, Double.parseDouble("1.5f") is 1.5 in Java and an error in C#, and C#'s
/// double.Parse reads "1,000.5" as a thousand and a half. A text is only said not to be a number when the language
/// itself would refuse it.
/// </summary>
internal static class NumberText
{
    /// <summary>
    /// What a conversion makes of a text: the number it asked for; a number with a fraction, which a whole-number
    /// conversion refuses; a number written in a way the whole-number conversion does not read, such as "1,000" for
    /// int.Parse or "12.0" for int(); or no number at all.
    /// </summary>
    public enum Reading { Reads, NotWhole, WrittenOtherwise, NotANumber }

    public static Reading Read(SourceLanguage language, string text, bool whole)
    {
        var fraction = language switch
        {
            SourceLanguage.Python => PythonFraction(text),
            SourceLanguage.Java => JavaFraction(text),
            _ => CSharpFraction(text),
        };

        if (!whole) return fraction is null ? Reading.NotANumber : Reading.Reads;
        if (ReadsWhole(language, text)) return Reading.Reads;

        return fraction switch
        {
            null => Reading.NotANumber,
            { IsWhole: false } => Reading.NotWhole,
            _ => Reading.WrittenOtherwise,
        };
    }

    /// <summary>A text a floating-point conversion reads, and whether the number it stands for is whole - null when that cannot be told.</summary>
    private sealed record Fraction(bool? IsWhole);

    /// <summary>
    /// The whole-number conversions. Python's int() takes surrounding white space, a sign, and digits with single
    /// underscores between them. Java's parseInt takes a sign and digits and nothing else, not even a space. C#'s
    /// int.Parse takes white space, a sign and digits.
    /// </summary>
    private static bool ReadsWhole(SourceLanguage language, string text)
    {
        switch (language)
        {
            case SourceLanguage.Python:
                var at = 0;
                var unsigned = WithoutSign(text.Trim());
                return DigitsOf(unsigned, ref at, char.IsDigit, underscores: true).Length > 0 && at == unsigned.Length;

            case SourceLanguage.Java:
                var digits = WithoutSign(text);
                return digits.Length > 0 && digits.All(char.IsDigit);

            default:
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }
    }

    /// <summary>
    /// Python's float(): surrounding white space, a sign, then inf, infinity or nan in any case, or digits with single
    /// underscores between them, a point and an exponent. Any Unicode digit counts, as it does in Python.
    /// </summary>
    private static Fraction? PythonFraction(string text)
    {
        var unsigned = WithoutSign(text.Trim());
        if (unsigned.Equals("inf", StringComparison.OrdinalIgnoreCase) || unsigned.Equals("infinity", StringComparison.OrdinalIgnoreCase) ||
            unsigned.Equals("nan", StringComparison.OrdinalIgnoreCase)) return new Fraction(false);

        return ReadDecimal(unsigned, char.IsDigit, underscores: true) is { } parts ? new Fraction(IsWhole(parts)) : null;
    }

    /// <summary>
    /// Java's parseDouble: surrounding white space ignored; NaN or Infinity written exactly so; a decimal number, or a
    /// hexadecimal one with its power of two - 0x1p3 is 8; and an f or d after the number - "1.5f" is 1.5.
    /// </summary>
    private static Fraction? JavaFraction(string text)
    {
        var unsigned = WithoutSign(text.Trim(JavaWhiteSpace));
        if (unsigned is "NaN" or "Infinity") return new Fraction(false);

        // 0xfd is not 0xf with a d after it: without a power of two, its last letter is one of its hexadecimal digits.
        var hexadecimal = unsigned.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (unsigned.Length > 0 && unsigned[^1] is 'f' or 'F' or 'd' or 'D' && (!hexadecimal || unsigned.IndexOfAny(['p', 'P']) >= 0))
            unsigned = unsigned[..^1];

        if (hexadecimal) return ReadHexadecimal(unsigned[2..]);
        return ReadDecimal(unsigned, char.IsAsciiDigit, underscores: false) is { } parts ? new Fraction(IsWhole(parts)) : null;
    }

    /// <summary>Java trims every character up to and including the space, as String.trim does.</summary>
    private static readonly char[] JavaWhiteSpace = Enumerable.Range(0, 33).Select(code => (char)code).ToArray();

    /// <summary>
    /// C#'s double.Parse: white space, a sign, a point, an exponent and thousands separators, read as the culture the
    /// program runs in says. A text is only refused when no common culture would read it - "1,5" is fifteen in one and
    /// one and a half in another, but a number in both - and it is only whole when every culture that reads it agrees.
    /// </summary>
    private static Fraction? CSharpFraction(string text)
    {
        var values = new List<double>();
        foreach (var culture in Cultures)
            if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var value)) values.Add(value);

        if (values.Count == 0) return null;
        if (values.Any(value => !double.IsFinite(value))) return new Fraction(null);

        var whole = values.Select(value => value == Math.Floor(value)).Distinct().ToList();
        return new Fraction(whole is [var agreed] ? agreed : null);
    }

    /// <summary>
    /// The ways cultures write a number: a point for the fraction and commas between thousands, as English does; a comma
    /// for the fraction and points between thousands, as German does; a comma and spaces, as French does.
    /// </summary>
    private static readonly IReadOnlyList<NumberFormatInfo> Cultures =
    [
        NumberFormatInfo.InvariantInfo,
        new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." },
        new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = " " },
        new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = " " },
    ];

    /// <summary>A number written in decimal: its digits before and after the point, and the power of ten written after them.</summary>
    private sealed record DecimalParts(string Before, string After, BigInteger Exponent);

    /// <summary>Digits, a point and more digits, and an exponent - as far as the text is exactly that. Null when it is not.</summary>
    private static DecimalParts? ReadDecimal(string text, Func<char, bool> isDigit, bool underscores)
    {
        var at = 0;
        var before = DigitsOf(text, ref at, isDigit, underscores);
        var after = "";
        if (at < text.Length && text[at] == '.')
        {
            at++;
            after = DigitsOf(text, ref at, isDigit, underscores);
        }

        if (before.Length == 0 && after.Length == 0) return null;
        if (ReadExponent(text, ref at, "eE", isDigit, underscores) is not { } exponent || at != text.Length) return null;
        return new DecimalParts(before, after, exponent);
    }

    /// <summary>
    /// Java's hexadecimal form, after its 0x: hexadecimal digits, perhaps a point and more of them, then p and the power of
    /// two, which must be there. Null when the text is not that form.
    /// </summary>
    private static Fraction? ReadHexadecimal(string text)
    {
        var at = 0;
        var before = HexadecimalDigits(text, ref at);
        var after = "";
        if (at < text.Length && text[at] == '.')
        {
            at++;
            after = HexadecimalDigits(text, ref at);
        }

        if (before.Length == 0 && after.Length == 0 || at >= text.Length || text[at] is not ('p' or 'P')) return null;
        if (ReadExponent(text, ref at, "pP", char.IsAsciiDigit, underscores: false) is not { } power || at != text.Length) return null;

        // Each hexadecimal digit after the point is four places of two.
        var fraction = after.TrimEnd('0');
        var shift = power - 4 * fraction.Length;
        if (shift >= 0) return new Fraction(true);
        if (-shift > MostPlaces) return new Fraction(null);

        var mantissa = BigInteger.Parse("0" + before + fraction, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return new Fraction(mantissa % BigInteger.Pow(2, (int)-shift) == 0);
    }

    private static string HexadecimalDigits(string text, ref int at)
    {
        var start = at;
        while (at < text.Length && Uri.IsHexDigit(text[at])) at++;
        return text[start..at];
    }

    /// <summary>
    /// An exponent, if there is one: its letter, a sign, and at least one digit - with underscores between them where the
    /// language allows them there. Zero when there is none; null when it is started but not finished.
    /// </summary>
    private static BigInteger? ReadExponent(string text, ref int at, string letters, Func<char, bool> isDigit, bool underscores)
    {
        if (at >= text.Length || !letters.Contains(text[at])) return BigInteger.Zero;

        at++;
        var negative = at < text.Length && text[at] == '-';
        if (at < text.Length && text[at] is '+' or '-') at++;

        var digits = DigitsOf(text, ref at, isDigit, underscores);
        if (digits.Length == 0) return null;

        var power = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        return negative ? -power : power;
    }

    /// <summary>
    /// Digits, written as plain 0 to 9, with any underscores between them left out; empty when there are none. Reading
    /// stops at anything else - an underscore that is not between two digits among them.
    /// </summary>
    private static string DigitsOf(string text, ref int at, Func<char, bool> isDigit, bool underscores)
    {
        var digits = new StringBuilder();
        if (at >= text.Length || !isDigit(text[at])) return "";

        digits.Append(Plain(text[at++]));
        while (at < text.Length)
        {
            if (isDigit(text[at]))
            {
                digits.Append(Plain(text[at++]));
            }
            else if (underscores && text[at] == '_' && at + 1 < text.Length && isDigit(text[at + 1]))
            {
                digits.Append(Plain(text[at + 1]));
                at += 2;
            }
            else
            {
                break;
            }
        }

        return digits.ToString();
    }

    /// <summary>A digit from any script, as the 0 to 9 it stands for.</summary>
    private static char Plain(char digit) => (char)('0' + (int)char.GetNumericValue(digit));

    /// <summary>Whether a decimal number is whole: its digits, times ten to the power that the point and the exponent leave.</summary>
    private static bool? IsWhole(DecimalParts parts)
    {
        var fraction = parts.After.TrimEnd('0');
        var shift = parts.Exponent - fraction.Length;
        if (shift >= 0) return true;
        if (-shift > MostPlaces) return null;

        var digits = BigInteger.Parse("0" + parts.Before + fraction, CultureInfo.InvariantCulture);
        return digits % BigInteger.Pow(10, (int)-shift) == 0;
    }

    /// <summary>Beyond this many places, whether a number is whole is not worked out - "1e-999999999" would take a very long time.</summary>
    private const int MostPlaces = 10_000;

    private static string WithoutSign(string text) => text.Length > 0 && text[0] is '+' or '-' ? text[1..] : text;
}
