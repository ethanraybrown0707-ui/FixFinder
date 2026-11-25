using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Recognition;

/// <summary>
/// Pairs of functions named for one thing and its undoing - encrypt and decrypt, to_roman and from_roman, int_to_roman and
/// roman_to_int - in the same file, and in the same class or both outside any. Unlike the algorithms, a pair is read from
/// its names, not the shape of its code: whatever the code, two functions named so are meant to turn back what the other
/// gives, and a trial can hold them to that.
/// </summary>
public static partial class AlgorithmRecognition
{
    public const string UndoingPair = "one of a pair of functions that undo each other";

    /// <summary>
    /// The words a name can say a thing is done with, each beside the word for undoing it: encrypt_message and
    /// decrypt_message, to_roman and from_roman.
    /// </summary>
    private static readonly (string Doing, string Undoing)[] UndoingWords =
    [
        ("encrypt", "decrypt"), ("encode", "decode"), ("encipher", "decipher"), ("compress", "decompress"),
        ("serialize", "deserialize"), ("serialise", "deserialise"), ("pack", "unpack"), ("escape", "unescape"),
        ("to", "from"),
    ];

    private const string UndoingPairAdvice =
        "Whatever the one gives back, the other has to turn back into what the one was given, for every input: a cipher's " +
        "letters near the end of the alphabet have to wrap round to its start and back, and in Java and JavaScript the " +
        "remainder of a number below zero is below zero too.";

    /// <summary>The words of a name, whether it is written in snake_case or camelCase: caesarEncrypt is caesar and encrypt.</summary>
    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex NameWord();

    /// <summary>
    /// Each pair of functions named to undo each other, both taking something to work on, in the same file and in the same
    /// class or both outside any - the one that does it first. A name written more than once there says nothing of which
    /// it goes with: JavaScript's methods of object literals have no class to tell them apart, and a file of a hundred
    /// messages, each an object with its own toJSON and fromJSON, would pair each toJSON with every fromJSON.
    /// </summary>
    private static IEnumerable<AlgorithmSeen> UndoingPairs(IrProgram program)
    {
        var sideBySide = program.AllFunctions
            .Where(function => function.Name != IrFunction.ModuleBody && !function.IsConstructor && function.Parameters.Count > 0)
            .GroupBy(function => (File: Path.GetFullPath(function.Span.File).ToLowerInvariant(), function.Owner,
                                  Enclosing: function.EnclosedBy == IrFunction.ModuleBody ? null : function.EnclosedBy));

        foreach (var together in sideBySide)
        {
            var functions = together.GroupBy(function => function.Name, StringComparer.Ordinal)
                .Where(named => named.Count() == 1)
                .Select(named => named.Single())
                .OrderBy(function => function.Span.Line)
                .ToList();
            for (var earlier = 0; earlier < functions.Count; earlier++)
            {
                for (var later = earlier + 1; later < functions.Count; later++)
                {
                    if (Undoing(functions[earlier], functions[later]) is not { } pair) continue;

                    var undoneBy = ShownName(pair.Undoing);
                    yield return new AlgorithmSeen(pair.Doing.Span.File, pair.Doing.Span.Line, ShownName(pair.Doing), UndoingPair,
                        $"`{undoneBy}` is named for undoing it - {pair.NamedFor}", UndoingPairAdvice)
                    {
                        UndoneBy = undoneBy,
                        EitherWayRound = pair.EitherWayRound,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Whether two functions are named to undo each other, and which does it: their names the same but for a word and the
    /// word for undoing it - encrypt_message and decrypt_message - or each named for turning one thing into the other -
    /// int_to_roman and roman_to_int - when the one written first goes first. Both have to start with as many underscores:
    /// a module's b32encode goes with its b32decode, and its own _b32encode with _b32decode, not each with the other.
    /// </summary>
    private static (IrFunction Doing, IrFunction Undoing, string NamedFor, bool EitherWayRound)? Undoing(IrFunction first, IrFunction second)
    {
        if (first.Name.TakeWhile(letter => letter == '_').Count() != second.Name.TakeWhile(letter => letter == '_').Count()) return null;

        var firstWords = WordsOf(first.Name);
        var secondWords = WordsOf(second.Name);

        if (firstWords.Count == secondWords.Count &&
            Enumerable.Range(0, firstWords.Count).Where(at => firstWords[at] != secondWords[at]).ToList() is [var differing])
        {
            foreach (var (doing, undoing) in UndoingWords)
            {
                if (firstWords[differing] == doing && secondWords[differing] == undoing) return (first, second, $"{doing} and {undoing}", false);
                if (secondWords[differing] == doing && firstWords[differing] == undoing) return (second, first, $"{doing} and {undoing}", false);
            }
        }

        if (TurnsFromInto(firstWords) is { } firstTurns && TurnsFromInto(secondWords) is { } secondTurns &&
            firstTurns.From.SequenceEqual(secondTurns.Into) && firstTurns.Into.SequenceEqual(secondTurns.From))
            return (first, second, $"{string.Join(" ", firstTurns.From)} to {string.Join(" ", firstTurns.Into)}, and back", true);

        return null;
    }

    /// <summary>A name with one to between two parts - int_to_roman - as what it turns from and what into.</summary>
    private static (IReadOnlyList<string> From, IReadOnlyList<string> Into)? TurnsFromInto(IReadOnlyList<string> words)
    {
        var at = words.ToList().IndexOf("to");
        return at > 0 && at < words.Count - 1 && words.Count(word => word == "to") == 1 ? (words.Take(at).ToList(), words.Skip(at + 1).ToList()) : null;
    }

    private static IReadOnlyList<string> WordsOf(string name) => [.. NameWord().Matches(name).Select(word => word.Value.ToLowerInvariant())];
}
