using System.Text.RegularExpressions;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Teaching;

/// <summary>A programming word, and what it means in plain words.</summary>
public sealed record GlossaryEntry(string Word, string Meaning);

/// <summary>
/// What the programming words in a finding mean, for someone meeting them for the first time. Each meaning holds in every
/// language FixFinder checks, or says where languages differ. A word that is also everyday English - None, NaN - is only
/// taken in the form a language writes it.
/// </summary>
public static class Glossary
{
    /// <summary>How many words one finding explains at most, so the list stays something to read rather than to skim.</summary>
    public const int MostWordsShown = 5;

    /// <summary>A word: the forms it is written in, whether its capital letters matter, and what it means.</summary>
    private sealed record Term(string Word, string[] Forms, bool CaseMatters, string Meaning);

    private static Term Word(string word, string meaning, params string[] otherForms) => new(word, [word, .. otherForms], false, meaning);

    private static Term ExactWord(string word, string meaning) => new(word, [word], true, meaning);

    private static readonly Term[] Terms =
    [
        Word("variable", "A name that holds a value, such as total, which the program can read and change.", "variables"),
        Word("function", "A named piece of code that does one job. Calling it runs that code, and it can hand a value back.", "functions"),
        Word("method", "A function that belongs to a class or an object, called with a dot: name.upper().", "methods"),
        Word("parameter", "A name in a function's definition standing for a value the caller will give it.", "parameters"),
        Word("argument", "A value given to a function when it is called: in area(3, 4), 3 and 4 are the arguments.", "arguments"),
        Word("return", "To hand a value back from a function to the line that called it.", "returns", "returned"),
        Word("loop", "Code that runs again and again - once for each item, or for as long as a condition stays true.", "loops"),
        Word("condition", "A question that is either true or false, such as count > 0, which decides what an if or a while does.", "conditions"),
        Word("index", "The position of an item in a list, an array or a piece of text. Most languages count from 0, so the first item is at index 0.", "indexes", "indices"),
        Word("list", "A collection of items kept in order, which can grow and shrink, such as [70, 80, 90].", "lists"),
        Word("array", "A collection of items kept in order and read by position: marks[0] is the first.", "arrays"),
        Word("string", "A piece of text, such as \"Ada\", written between quotes.", "strings"),
        Word("integer", "A whole number, with no fraction: 3, 0 or -12.", "integers"),
        Word("decimal", "A number with a fraction, such as 2.5. Computers store most of them only approximately, which is why 0.1 + 0.2 is not exactly 0.3.", "decimals"),
        Word("float", "A decimal number - a number with a fraction, such as 2.5 - as most languages store it.", "floats"),
        Word("boolean", "A value that is either true or false.", "booleans"),
        Word("type", "The kind of a value - a whole number, a decimal, text, a list - which decides what can be done with it.", "types"),
        Word("class", "A description of a kind of object: the values each one holds and the methods it has.", "classes"),
        Word("object", "One value made from a class, holding values of its own.", "objects"),
        Word("exception", "An error raised while the program runs. Unless the program catches it, it stops the program.", "exceptions"),
        Word("crash", "When a program stops part-way because of an error, instead of finishing.", "crashes", "crashed"),
        Word("compiler", "The program that turns source code into something the computer can run, and refuses code it cannot make sense of.", "compilers"),
        Word("compile", "To turn source code into a program that can run. The compiler does it, and stops at the first errors it finds.", "compiles", "compiled"),
        Word("syntax", "The rules for how code has to be written - brackets, quotes, punctuation, indentation - before what it means is even looked at."),
        Word("indentation", "The spaces at the start of a line. In Python they decide which lines belong to a loop, an if or a function.", "indented"),
        Word("bracket", "One of the pairs ( ), [ ] and { }. Each one opened has to be closed.", "brackets"),
        Word("brace", "One of the pair { }, which marks where a block of code starts and ends in Java, C#, C, C++, JavaScript and Go.", "braces"),
        Word("semicolon", "The ; that ends a statement in Java, C#, C, C++ and JavaScript.", "semicolons"),
        Word("quote", "A mark around text: \"double\" or 'single'. Each opening quote needs a closing one.", "quotes"),
        Word("statement", "One instruction in a program, such as a line that gives a variable a value.", "statements"),
        Word("expression", "A piece of code that works out a value, such as total / count.", "expressions"),
        Word("declaration", "Saying that a name exists - and often its type - before it is used, as int total; does.", "declarations", "declared"),
        Word("scope", "The part of a program where a name can be used. A variable made inside a function cannot be used outside it."),
        Word("module", "A file of code that other files can import and use.", "modules"),
        Word("import", "To bring in code from another file or library, so this file can use it.", "imports", "imported"),
        Word("library", "Code written by someone else and packaged so that programs can use it.", "libraries"),
        ExactWord("null", "A value meaning \"no object here\". Calling a method on it, or reading a field of it, is an error."),
        ExactWord("None", "Python's value for \"nothing here\" - what a function gives back when it reaches its end without a return."),
        ExactWord("undefined", "JavaScript's value for \"nothing here\" - in a variable given no value, or for an argument or a property that is missing."),
        ExactWord("NaN", "\"Not a number\": what arithmetic gives when there is no real answer, such as 0 divided by 0 with decimals."),
        Word("recursion", "A function calling itself, each time on a smaller part of the problem, until it reaches a case it can answer straight away.", "recursive"),
        Word("stack", "The calls still in progress, the newest on top. Each call waiting for another one to finish takes room on it."),
        Word("overflow", "Going past the most something can hold - too many calls in progress for the stack, or a number too big for its type."),
        Word("remainder", "What is left over after dividing whole numbers: 7 divided by 3 is 2, with 1 left over.", "remainders"),
        Word("divisor", "The number divided by: in total / count, count is the divisor."),
        Word("dictionary", "A collection of pairs - a key and its value - where a value is looked up by its key, as in ages[\"Ada\"].", "dictionaries"),
        Word("key", "The name a value is stored under in a dictionary or a map, used to look the value up.", "keys"),
        Word("constant", "A name whose value cannot change once it is given - final in Java, const in JavaScript, val in Scala.", "constants"),
        Word("iteration", "One pass through a loop. To iterate over a collection is to go through its items one at a time.", "iterate", "iterates"),
        Word("runtime", "While the program is running, as opposed to while it is being built."),
        Word("warning", "Something the compiler or a checker thinks is probably a mistake, which does not stop the program from running.", "warnings"),
        Word("operator", "A symbol that works out a value from others, such as +, -, == or &&.", "operators"),
        Word("comparison", "A check of how two values relate, such as a == b or a < b, which gives true or false.", "comparisons"),
        Word("assignment", "Giving a variable a value with =, as total = 0 does.", "assign", "assigned"),
        Word("interpreter", "A program that reads code and runs it straight away, without building it first, as Python and Node do.", "interpreters"),
        Word("traceback", "Python's list of the calls that led to an error, ending with the error itself.", "tracebacks"),
        Word("stack trace", "The list of calls that were in progress when an exception was raised, starting with the one that failed.", "stack traces"),
        Word("character", "One letter, digit or symbol in a piece of text.", "characters"),
        Word("pointer", "In C and C++, a value that holds where in memory something is kept, rather than the thing itself.", "pointers"),
        Word("memory", "Where a running program keeps its values."),
        Word("thread", "A part of a program that runs at the same time as the rest of it.", "threads"),
        Word("parse", "To read text and work out its structure - turning \"42\" into the number 42, or code into its parts.", "parsing", "parses"),
        Word("literal", "A value written straight into the code, such as 42, 2.5 or \"Ada\".", "literals"),
        Word("field", "A value stored inside an object, read with a dot: student.name.", "fields"),
        Word("attribute", "A value stored inside an object, read with a dot: student.name.", "attributes"),
        Word("property", "A value belonging to an object, read with a dot: text.length.", "properties"),
        Word("instance", "One object made from a class.", "instances"),
        Word("constructor", "The part of a class that sets up each new object when it is made.", "constructors"),
        Word("interface", "A list of methods that a class promises to have.", "interfaces"),
        Word("tuple", "A fixed group of values kept together, such as (3, 4).", "tuples"),
        Word("switch", "A statement that picks which code to run, from a list of cases, by a value.", "switches"),
        Word("goroutine", "A function Go runs at the same time as the rest of the program, started with go.", "goroutines"),
        Word("panic", "Go's word for an error that stops the program while it runs.", "panics"),
        Word("comment", "Text in the code for people to read, which the language ignores - after # in Python, after // in Java.", "comments"),
        Word("block", "Lines that belong together - the body of a loop, an if or a function - marked by braces or by indentation.", "blocks"),
        Word("body", "The lines inside a loop, an if or a function, which run as part of it."),
        Word("input", "What a program reads in - typed by a person, or from a file."),
        Word("output", "What a program prints or writes."),
    ];

    private static readonly (Term Term, Regex Pattern)[] Patterns = Terms
        .Select(term => (term, new Regex(
            @"(?<![\w-])(?:" + string.Join("|", term.Forms.OrderByDescending(form => form.Length).Select(Regex.Escape)) + @")(?![\w-])",
            term.CaseMatters ? RegexOptions.CultureInvariant : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
        .ToArray();

    /// <summary>
    /// The words a finding uses that this list explains, in the order they first appear in what the finding says - its
    /// title, what is wrong, why it matters and how to fix it - and at most <see cref="MostWordsShown"/> of them.
    /// </summary>
    public static IReadOnlyList<GlossaryEntry> In(Finding finding) =>
        In(string.Join("\n", finding.Title, finding.Explanation, finding.WhyItMatters, finding.SuggestedFix));

    public static IReadOnlyList<GlossaryEntry> In(string text)
    {
        var found = Patterns
            .Select(entry => (entry.Term, Found: entry.Pattern.Match(text)))
            .Where(entry => entry.Found.Success)
            .OrderBy(entry => entry.Found.Index)
            .ThenByDescending(entry => entry.Found.Length)
            .ToList();

        // "stack trace" is one term, so the "stack" inside it is not explained again on its own.
        var explained = new List<(Term Term, Match Found)>();
        foreach (var entry in found)
        {
            var inside = explained.Any(kept => entry.Found.Index >= kept.Found.Index && entry.Found.Index < kept.Found.Index + kept.Found.Length);
            if (!inside && explained.All(kept => kept.Term.Meaning != entry.Term.Meaning)) explained.Add(entry);
        }

        return explained.Take(MostWordsShown).Select(entry => new GlossaryEntry(entry.Term.Word, entry.Term.Meaning)).ToList();
    }

    /// <summary>Every word the list explains, for the tests that read each meaning.</summary>
    public static IReadOnlyList<GlossaryEntry> All => Terms.Select(term => new GlossaryEntry(term.Word, term.Meaning)).ToList();
}
