using System.Security.Cryptography;
using System.Text;

namespace FixFinder.Core.Teaching;

/// <summary>
/// The short code FixFinder gives a problem it found, for pasting into FixFinder Learn: FF-PY-DIVZERO-K7Q2MXA. It says the
/// language and the idea behind the mistake, so the lesson opens on any computer; the last part also finds what FixFinder
/// said of this very problem, kept on the computer that checked it.
/// </summary>
/// <remarks>
/// The last part is seven letters and digits from Crockford's base 32 - which has no I, L, O or U, so none can be misread
/// as a 1 or a 0 - five worked out from the problem and two check letters worked out from everything before them. A code
/// typed with a slip in it is then almost always known to have one - all but about one slip in a thousand - rather than
/// opening another problem. Lower case, spaces and a missing FF- are all accepted, and I, L and O are read as 1, 1 and 0.
/// </remarks>
public sealed record ProblemCode(CodeLanguage Language, Concept Concept, string Problem)
{
    private const string Prefix = "FF";

    /// <summary>The letters Crockford's base 32 is written with.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int ProblemLetters = 5;
    private const int CheckLetters = 2;

    /// <summary>The short name of each language in a code. Never changed once given out, so an older code still opens.</summary>
    private static readonly Dictionary<string, string> LanguageTags = new(StringComparer.Ordinal)
    {
        ["Python"] = "PY",
        ["Java"] = "JAVA",
        ["C#"] = "CS",
        ["C"] = "C",
        ["C++"] = "CPP",
        ["JavaScript"] = "JS",
        ["Go"] = "GO",
        ["Scala"] = "SCALA",
        ["OCaml"] = "OCAML",
    };

    /// <summary>The code as it is shown and copied: FF-PY-DIVZERO-K7Q2MXA.</summary>
    public string Text => $"{Prefix}-{LanguageTags[Language.Name]}-{Concept.Code}-{Problem}";

    public override string ToString() => Text;

    /// <summary>
    /// The code for a problem: its language, the idea behind it, five letters worked out from what identifies the problem
    /// - so the same problem on the same line of code has the same code each time it is found - and two check letters.
    /// </summary>
    public static ProblemCode For(CodeLanguage language, Concept concept, string identity)
    {
        var problem = Letters(SHA256.HashData(Encoding.UTF8.GetBytes(identity)), ProblemLetters);
        return new ProblemCode(language, concept, problem + Check(LanguageTags[language.Name], concept.Code, problem));
    }

    /// <summary>Whether a language has a short name for codes - every language FixFinder checks has one.</summary>
    public static bool Names(CodeLanguage language) => LanguageTags.ContainsKey(language.Name);

    /// <summary>What reading a pasted code found: the code, or why it is not one.</summary>
    public sealed record Reading(ProblemCode? Code, string? Problem);

    /// <summary>
    /// Reads a code as somebody may paste or type it - with or without FF-, in any case, with spaces round it or instead of
    /// the dashes - and says plainly what is wrong with one that cannot be read.
    /// </summary>
    public static Reading Read(string typed)
    {
        var parts = typed.Trim().ToUpperInvariant()
            .Split(['-', ' ', '_'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (parts.Count > 0 && parts[0] == Prefix) parts.RemoveAt(0);

        if (parts.Count != 3)
            return new Reading(null, "A problem code has three parts after FF - the language, the kind of mistake and seven letters, as in FF-PY-DIVZERO-K7Q2MXA.");

        var language = LanguageTags.FirstOrDefault(pair => pair.Value == parts[0]).Key is { } name
            ? CodeLanguage.All.FirstOrDefault(each => each.Name == name)
            : null;
        if (language is null)
            return new Reading(null, $"{parts[0]} is not a language FixFinder checks. The part after FF- is the language: {string.Join(", ", LanguageTags.Values)}.");

        if (Concepts.WithCode(parts[1]) is not { } concept)
            return new Reading(null, $"{parts[1]} is not a kind of mistake this FixFinder Learn knows - the code may come from a newer FixFinder.");

        var problem = new string(parts[2].Select(AsCrockford).ToArray());
        if (problem.Length != ProblemLetters + CheckLetters || problem.Any(letter => !Alphabet.Contains(letter)))
            return new Reading(null, "The last part of a problem code is seven letters and digits, as in K7Q2MXA.");

        if (Check(parts[0], concept.Code, problem[..ProblemLetters]) != problem[ProblemLetters..])
            return new Reading(null, "That code has a slip in it - one of its letters is not the one FixFinder gave. Copy it again from FixFinder.");

        return new Reading(new ProblemCode(language, concept, problem), null);
    }

    /// <summary>The part of a code that finds the problem itself, without its check letters - what problems are kept under.</summary>
    public string Key => Problem[..ProblemLetters];

    /// <summary>The letters that are easily typed in place of a digit, read as the digit, as Crockford's base 32 reads them.</summary>
    private static char AsCrockford(char letter) => letter switch
    {
        'O' => '0',
        'I' or 'L' => '1',
        _ => letter,
    };

    /// <summary>Two letters worked out from the whole of the rest of the code, so a slip anywhere in it changes them.</summary>
    private static string Check(string languageTag, string concept, string problem) =>
        Letters(SHA256.HashData(Encoding.UTF8.GetBytes($"{languageTag}-{concept}-{problem}")), CheckLetters);

    /// <summary>The first letters of base 32 a hash makes, five bits each.</summary>
    private static string Letters(byte[] hash, int count)
    {
        var bits = (ulong)hash[0] << 32 | (ulong)hash[1] << 24 | (ulong)hash[2] << 16 | (ulong)hash[3] << 8 | hash[4];

        var letters = new StringBuilder();
        for (var place = 0; place < count; place++) letters.Append(Alphabet[(int)(bits >> (35 - 5 * (place + 1)) & 31)]);

        return letters.ToString();
    }
}
