using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// What each language does when a program goes wrong, in the words its own messages use - and the few rules that differ
/// between them: what counts as whole-number division, and what may be nothing without anything going wrong.
/// </summary>
public static class Failures
{
    /// <summary>What the language calls nothing: None, nil, NULL, nullptr, null.</summary>
    public static string Nothing(SourceLanguage language) => language switch
    {
        SourceLanguage.Python => "None",
        SourceLanguage.Go => "nil",
        SourceLanguage.C => "NULL",
        SourceLanguage.Cpp => "nullptr",
        _ => "null",
    };

    public static string DividingByZero(SourceLanguage language) => language switch
    {
        SourceLanguage.Python => "ZeroDivisionError",
        SourceLanguage.CSharp => "a DivideByZeroException",
        SourceLanguage.Go => "a panic - integer divide by zero",
        SourceLanguage.C or SourceLanguage.Cpp => "undefined behaviour - dividing by zero usually stops the program",
        _ => "an ArithmeticException",
    };

    /// <param name="takingAnItem">Whether the value was indexed rather than read from, which Python reports differently.</param>
    public static string UsingNothing(SourceLanguage language, bool takingAnItem = false) => language switch
    {
        SourceLanguage.Python => takingAnItem ? "TypeError" : "AttributeError",
        SourceLanguage.CSharp => "a NullReferenceException",
        SourceLanguage.Go => "a panic - nil pointer dereference",
        SourceLanguage.C or SourceLanguage.Cpp => "undefined behaviour - going through a null pointer usually stops the program",
        SourceLanguage.JavaScript => "a TypeError",
        _ => "a NullPointerException",
    };

    public static string OutsideTheList(SourceLanguage language) => language switch
    {
        SourceLanguage.Python => "IndexError",
        SourceLanguage.CSharp => "an index out of range exception",
        SourceLanguage.Go => "a panic - index out of range",
        SourceLanguage.C or SourceLanguage.Cpp => "undefined behaviour - it reads or writes past the end of the array",
        _ => "an IndexOutOfBoundsException",
    };

    public static string TakingFromEmpty(SourceLanguage language) => language switch
    {
        SourceLanguage.Python => "IndexError",
        SourceLanguage.CSharp => "an InvalidOperationException",
        _ => "a NoSuchElementException",
    };

    /// <summary>
    /// Whether dividing can fail at all, and whether these two values divide as whole numbers. Go gives both sides of a
    /// division the same type, so one side being a whole number settles it; JavaScript has one number type and gives
    /// Infinity rather than failing.
    /// </summary>
    public static bool WholeNumberDivision(SourceLanguage language, bool leftIsWhole, bool rightIsWhole, bool leftIsReal, bool rightIsReal, bool leftIsLiteral,
        bool rightIsLiteral)
    {
        if (language == SourceLanguage.JavaScript) return false;
        if (leftIsWhole && rightIsWhole) return true;
        if (language != SourceLanguage.Go) return false;

        return leftIsWhole && !leftIsLiteral && !rightIsReal || rightIsWhole && !rightIsLiteral && !leftIsReal;
    }

    /// <summary>
    /// Whether taking an item from nothing goes wrong. In Go a nil slice has no items and a nil map reads as missing, so
    /// neither fails; walking through either is fine too.
    /// </summary>
    public static bool NothingCanBeIndexed(SourceLanguage language) => language is SourceLanguage.Go;

    /// <summary>Whether walking through nothing goes wrong: in Go, ranging over a nil slice, map or channel is allowed.</summary>
    public static bool NothingCanBeWalked(SourceLanguage language) => language is SourceLanguage.Go;

    /// <summary>
    /// Whether a method can run on nothing. A Go method with a pointer receiver runs even when the pointer is nil - the
    /// standard library relies on it - so only reading a field of nothing is a mistake.
    /// </summary>
    public static bool NothingCanRunMethods(SourceLanguage language) => language is SourceLanguage.Go;

    /// <summary>Whether dividing gives a fraction rather than a whole number: JavaScript has one number type, and Python's / always does.</summary>
    public static bool DivisionGivesReal(SourceLanguage language) => language is SourceLanguage.Python or SourceLanguage.JavaScript;

    /// <summary>Whether whole numbers wrap round when they grow too large: they do not in Python or JavaScript.</summary>
    public static bool NumbersWrapRound(SourceLanguage language) => language is not (SourceLanguage.Python or SourceLanguage.JavaScript);

    /// <summary>
    /// Whether asking for a position that does not exist goes wrong. JavaScript gives undefined rather than failing, so
    /// the mistake shows up later, where that undefined is used.
    /// </summary>
    public static bool ReadingPastTheEndFails(SourceLanguage language) => language is not SourceLanguage.JavaScript;

    /// <summary>
    /// Whether an empty collection counts as false. It does in Python; in JavaScript every object and array is true,
    /// however empty, and only 0, "" and nothing are false.
    /// </summary>
    public static bool EmptyIsFalse(SourceLanguage language) => language is not SourceLanguage.JavaScript;

    /// <summary>
    /// Whether handing a list to other code can change how long it is. A Go slice is a value: what the function it was
    /// given to does cannot make the caller's slice longer or shorter, though a map or a set it shares still can.
    /// </summary>
    public static bool ListsKeepTheirLength(SourceLanguage language) => language is SourceLanguage.Go;

    /// <summary>
    /// Whether an ordinary call can fail in a way that skips the code after it. Go has no exceptions - only a panic,
    /// which is not something ordinary code guards against - so a lock released after a call is not a warning.
    /// </summary>
    public static bool CallsCanThrow(SourceLanguage language) => language is not SourceLanguage.Go;

    /// <summary>
    /// Whether what a method was called on can itself be nothing. In Go it can, and <c>if c == nil</c> at the top of a
    /// method is ordinary code, not a test that can never be true.
    /// </summary>
    public static bool ReceiverCanBeNothing(SourceLanguage language) => language is SourceLanguage.Go;
}
