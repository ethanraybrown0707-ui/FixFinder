namespace FixFinder.Core.Teaching;

/// <summary>
/// One idea behind a kind of mistake - dividing by zero, a position that does not exist - which a lesson teaches and every
/// finding of that kind is an example of, whatever language it is in.
/// </summary>
/// <param name="Code">
/// Its short name in a problem code: DIVZERO in FF-PY-DIVZERO-K7Q2MX. A code is how FixFinder Learn finds the lesson, so a
/// concept's code is never changed once given out - a code copied from an older FixFinder must still open its lesson.
/// </param>
/// <param name="Title">What the lesson is called.</param>
/// <param name="Idea">
/// The idea in plain words for someone new to programming. It holds for every language FixFinder checks; where languages
/// differ, it says they do, and each language's lesson shows what that language does by running an example of it.
/// </param>
public sealed record Concept(string Code, string Title, string Idea);

/// <summary>Every concept FixFinder teaches, and the one for a finding that is none of them.</summary>
public static class Concepts
{
    public static Concept DivisionByZero { get; } = new("DIVZERO", "Dividing by zero",
        "Dividing shares a number out into equal parts, and there is no way to share something out into zero parts. So a " +
        "program that divides by zero cannot give an ordinary answer: depending on the language, and on whether the numbers " +
        "are whole numbers or decimals, it stops with an error, gives a special value that is not a normal number, or - in C " +
        "and C++ - does something the language leaves undefined. The zero is usually a count or a length that nothing added " +
        "to, such as the number of items in a list that turned out empty.");

    public static Concept NothingThere { get; } = new("NOTHING", "Using a value that is not there",
        "Most languages have a special value that means \"nothing here\" - None in Python, null in Java, C#, JavaScript and " +
        "Scala, nil in Go, a null pointer in C and C++. It is what a search gives back when it found nothing, or what a " +
        "variable holds before it is given anything. It stands for nothing, so asking it for something only a real value " +
        "has - a field, a method, an item - is a mistake the program usually stops on.");

    public static Concept PositionOutOfRange { get; } = new("INDEX", "A position that does not exist",
        "The items in a list, an array or a piece of text are numbered by position, and in every language FixFinder checks " +
        "the first position is 0, not 1. So a list of 3 items has positions 0, 1 and 2, and there is no position 3. Asking " +
        "for a position past the end is asking for an item that is not there.");

    public static Concept MissingKey { get; } = new("KEYS", "Looking up a key that is not there",
        "A dictionary or a map stores values under keys - a name, an id - and looking a value up means giving its key. " +
        "Asking for a key nothing was stored under has no value to give back: depending on the language, the program stops " +
        "with an error, or gets back a value that stands for nothing, or the empty value of its type - 0 for a number - so " +
        "the mistake shows up later. Checking the key is there first, or asking for a default, avoids it.");

    public static Concept UndefinedName { get; } = new("NAMES", "A name that has not been defined",
        "Every name a program uses - a variable, a function, a class - has to be made before it can be used: given a value, " +
        "declared, defined or imported. The language looks the name up exactly as it is written, capital letters included, " +
        "so a name spelled slightly differently from where it was made is a name the language does not know - and in most " +
        "languages so is a variable used before the line that makes it.");

    public static Concept ReadBeforeSet { get; } = new("UNSET", "A variable used before it is given a value",
        "A variable can be made before it is given anything to hold. Reading it before any value has been stored in it is " +
        "reading nothing in particular, and languages deal with that differently: some refuse the program, some stop when " +
        "the line runs, some give every variable a starting value of their own, and C and C++ can read whatever happens to " +
        "be left in that piece of memory - which can be different on every run.");

    public static Concept WrongType { get; } = new("TYPES", "Values of the wrong type",
        "Every value has a type - a whole number, a decimal, a piece of text, a list - and each type can only do certain " +
        "things. When values of two different types meet - a number and a piece of text, say - some languages refuse to " +
        "combine them, and some turn one into the other's type by rules of their own, which is not always what was meant. " +
        "Turning a value into the type you want yourself, before it is used, says exactly what you mean.");

    public static Concept Blocks { get; } = new("BLOCKS", "Blocks of code",
        "The lines that belong together - the body of a loop, an if, a function - form a block. Some languages mark where a " +
        "block starts and ends with braces { }, some with how far the lines are indented, and some with words such as begin " +
        "and end. Either way the language has to be able to tell exactly which lines are inside the block, and a brace " +
        "missing, or a line indented differently from its neighbours, leaves it unable to.");

    public static Concept UnclosedPair { get; } = new("PAIRS", "Brackets and quotes that are never closed",
        "Brackets, braces and quotes come in pairs: every ( needs a ), every { a }, every opening quote a closing one. The " +
        "language reads on from an opening one looking for its partner, so when one is missing it often only notices much " +
        "later - and the line it complains about can be well after the real mistake.");

    public static Concept StatementEnd { get; } = new("ENDINGS", "Where a statement ends",
        "Languages such as Java, C#, C and C++ need a semicolon ; at the end of each statement, the way a sentence needs a " +
        "full stop. The line break is not enough for them, so without the semicolon the language runs two statements " +
        "together and cannot make sense of the result. Depending on the compiler, it reports the mistake at the end of the " +
        "line that is missing the semicolon, or at the start of the next one.");

    public static Concept AssignOrCompare { get; } = new("COMPARE", "Assigning or comparing",
        "In most languages one = stores a value in a variable, and two == compare two values. A condition - in an if or a " +
        "while - needs a comparison that is true or false. Writing = where == was meant is either refused, or quietly changes " +
        "the variable instead of checking it.");

    public static Concept SameValueOrSameObject { get; } = new("EQUALITY", "Equal values, or the same object",
        "Two things can be equal in two different ways: they can hold the same value - the same letters, the same numbers - " +
        "or they can be the very same object in the computer's memory. Some comparisons ask the first question and some the " +
        "second, and for text in particular the two can give different answers, so the right comparison has to be used.");

    public static Concept OffByOne { get; } = new("OFFBYONE", "Going one step too far",
        "Counting from 0 means the last position of a list of n items is n - 1. A loop that runs while the position is <= n " +
        "takes one step more than there are items - the classic off-by-one mistake. The same slip in the other direction " +
        "misses the last item.");

    public static Concept EndlessLoop { get; } = new("ENDLESS", "A loop that never ends",
        "A loop keeps going until its condition becomes false. If nothing inside the loop ever changes what the condition " +
        "checks, a condition that is true once stays true for ever, and the program never gets past the loop - it looks " +
        "frozen until it is stopped.");

    public static Concept WholeNumberDivision { get; } = new("INTDIV", "Whole-number division",
        "When both numbers in a division are whole numbers, many languages give a whole number back and drop the fraction: " +
        "7 / 2 is 3, not 3.5. That is right when you want whole groups, and wrong when you want an average or a percentage - " +
        "where at least one of the numbers has to be made a decimal first.");

    public static Concept TextToNumber { get; } = new("CONVERT", "Turning text into a number",
        "Whatever is typed in, or read from a file, arrives as text - even when it looks like a number. To calculate with it, " +
        "it has to be turned into a number first, and that only works when the text really is one: \"42\" can be, " +
        "\"forty-two\" cannot.");

    public static Concept Arguments { get; } = new("ARGS", "Giving a function what it needs",
        "A function is written to take certain values - its parameters - in a certain order, and every call has to give it " +
        "what it takes: the right number of values, each of a kind it can use. Most languages refuse a call with one too " +
        "few, one too many or one of the wrong kind; JavaScript lets it run, with a missing value quietly undefined and an " +
        "extra one ignored, so the mistake shows up later, somewhere else.");

    public static Concept ReturnValues { get; } = new("RETURNS", "What a function gives back",
        "A function can hand a value back to the line that called it, with return. A function that only prints its answer, " +
        "or that reaches its end on some way through it without a return, gives nothing back there. Depending on the " +
        "language, the caller then gets the language's value for nothing - None in Python, undefined in JavaScript - or the " +
        "compiler refuses a function that promises a value and does not always return one; C and C++ only warn, and what " +
        "the caller gets is undefined.");

    public static Concept EndlessRecursion { get; } = new("RECURSION", "A function that calls itself for ever",
        "A function may call itself - recursion - as long as each call works on a smaller part of the problem and there is a " +
        "case where it stops calling itself. Without that stopping case every call makes another, until the language's " +
        "limit on calls in progress is reached, or the memory it keeps for them runs out, and the program stops.");

    public static Concept MissedCase { get; } = new("CASES", "A case nobody handled",
        "A match or a switch picks what to do from a list of cases. When a value can arrive that none of the cases matches, " +
        "some compilers warn or refuse, and when such a value does arrive the program either stops with an error or carries " +
        "on having done nothing, depending on the language. Every value that can arrive needs a case - or a default that " +
        "catches the rest.");

    public static Concept FallingThrough { get; } = new("FALLTHROUGH", "A case that runs into the next",
        "In Java, C, C++ and JavaScript the cases of a switch are only places to start from: once a case's lines have run, " +
        "the program carries straight on into the next case's lines unless a break stops it. A case without its break " +
        "therefore also does what the case after it does. C# refuses such a switch, and Go stops at the end of each case " +
        "unless it is told to go on with fallthrough.");

    public static Concept ChangingAConstant { get; } = new("CONSTANT", "Changing something that cannot change",
        "Some values are made so they can never be changed once they have been given - val in Scala, const in JavaScript, " +
        "final in Java, every name made with let in OCaml. That protects them from being changed by mistake, so a line that " +
        "tries to give one a new value is refused.");

    public static Concept ChangingWhileLooping { get; } = new("CHANGELOOP", "Changing a list while looping over it",
        "A loop over a list keeps track of where it has got to. Adding to or removing from the list while the loop is " +
        "walking through it moves the items under the loop's feet, so items are skipped, seen twice, or the language stops " +
        "the program rather than carry on with a list that is no longer the one it started with.");

    public static Concept NeverUsed { get; } = new("UNUSED", "Something made and never used",
        "A variable that is given a value nobody reads, or an import nothing uses, does no harm on its own - but it is often " +
        "a sign of a mistake nearby, such as a result stored under one name and then read from another. Go refuses to build " +
        "a program with an unused variable or import, and the C# compiler - and gcc and clang, with the warnings FixFinder " +
        "turns on - warn about one.");

    /// <summary>The concept for a finding that is none of the others: how to read what went wrong.</summary>
    public static Concept ReadingErrors { get; } = new("GENERAL", "Reading what went wrong",
        "When a program goes wrong, the language says where and what: the file, the line, and a message naming the kind of " +
        "problem. Reading the message from the line it names - and the line before it, as a missing bracket or quote is often " +
        "reported one line late - is the fastest way to the mistake.");

    /// <summary>Every concept, in the order FixFinder Learn lists them.</summary>
    public static IReadOnlyList<Concept> All { get; } =
    [
        DivisionByZero, NothingThere, PositionOutOfRange, MissingKey, UndefinedName, ReadBeforeSet, WrongType, Blocks,
        UnclosedPair, StatementEnd, AssignOrCompare, SameValueOrSameObject, OffByOne, EndlessLoop, WholeNumberDivision,
        TextToNumber, Arguments, ReturnValues, EndlessRecursion, MissedCase, FallingThrough, ChangingAConstant,
        ChangingWhileLooping, NeverUsed, ReadingErrors,
    ];

    /// <summary>The concept a problem code names, or null for one this FixFinder does not have.</summary>
    public static Concept? WithCode(string code) =>
        All.FirstOrDefault(concept => string.Equals(concept.Code, code, StringComparison.OrdinalIgnoreCase));
}
