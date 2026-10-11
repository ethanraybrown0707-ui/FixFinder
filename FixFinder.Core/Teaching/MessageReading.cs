using FixFinder.Core.Checking;

namespace FixFinder.Core.Teaching;

/// <summary>When a language's message about a program was given: before it ran, or while it ran.</summary>
public enum MessagePhase
{
    /// <summary>While the program was being read or built - a compiler's error or warning, or Python's SyntaxError.</summary>
    BeforeRunning,

    /// <summary>While the program was running - a crash.</summary>
    WhileRunning,
}

/// <summary>What a language said about a finding, as it said it, and how to read a message like it.</summary>
/// <param name="SaidBy">Who said it: the language's name.</param>
/// <param name="Message">The message, as it was printed - its first lines, when it went on for longer.</param>
/// <param name="HowToRead">How a message of this kind is laid out, part by part, in plain words.</param>
public sealed record MessageRead(string SaidBy, MessagePhase Phase, string Message, string HowToRead, bool WasShortened);

/// <summary>
/// How to read each language's messages. Each description is of the layout the language really prints - checked against
/// the messages its own tools printed in FixFinder's tests - and says nothing about the particular mistake, which the
/// finding explains. A C or C++ program that crashes prints nothing of its own - Windows stops it - so there is no
/// message of its to read, and none is described.
/// </summary>
public static class MessageReading
{
    /// <summary>How many lines of a message are shown: enough for the part that names the mistake and where it is.</summary>
    public const int MostLinesShown = 12;

    private static readonly Dictionary<(string Language, MessagePhase Phase), string> HowToRead = new()
    {
        [("Python", MessagePhase.BeforeRunning)] =
            "Python reads a whole file before it runs any of it. Its message names the file and the line number, shows that " +
            "line with ^ marks under the place where it stopped understanding, and ends with the kind of error - SyntaxError, " +
            "or IndentationError - and what it expected. The real slip can be just before the ^ marks, or on the line above: " +
            "a bracket that was never closed, say.",
        [("Python", MessagePhase.WhileRunning)] =
            "This is a traceback: the calls that led to the error, the first call at the top and the line that failed at the " +
            "bottom. Each call is a line naming the file and the line number, with the line of code under it. The very last " +
            "line names the kind of error - such as ZeroDivisionError - and what went wrong. Start reading from the bottom.",
        [("Java", MessagePhase.BeforeRunning)] =
            "javac names the file and the line number, then error: and what is wrong. It reports every error it finds, and one " +
            "slip can cause several of them - fix the first, then check again.",
        [("Java", MessagePhase.WhileRunning)] =
            "The first line names the exception - such as java.lang.ArithmeticException - and its message. Each line under it " +
            "that starts with at is a method that was running, the one that failed first: at Main.main(Main.java:5) means " +
            "line 5 of Main.java, in the method main of the class Main. Look for the first at line in a file of your own.",
        [("C#", MessagePhase.BeforeRunning)] =
            "The C# compiler names the file, then the line and the column in brackets - (5,13) is line 5, column 13 - then " +
            "error or warning, a code such as CS0103, and the message. The code can be looked up by its number. One slip can " +
            "cause several errors: fix the first, then check again.",
        [("C#", MessagePhase.WhileRunning)] =
            "Unhandled exception names the exception - such as System.DivideByZeroException - and its message. Each line under " +
            "it that starts with at is a method that was running, the one that failed first, and the end of that line gives " +
            "the file and line number where it was.",
        [("C", MessagePhase.BeforeRunning)] =
            "The compiler - gcc or clang - names the file, the line and the column - main.c:5:12 is line 5, column 12 - then " +
            "error: or warning: and what is wrong. Fix the first error first: the ones after it can follow from it.",
        [("C++", MessagePhase.BeforeRunning)] =
            "The compiler - g++ or clang - names the file, the line and the column - main.cpp:5:12 is line 5, column 12 - then " +
            "error: or warning: and what is wrong. Fix the first error first: the ones after it can follow from it.",
        [("JavaScript", MessagePhase.BeforeRunning)] =
            "SyntaxError names what Node did not expect where it stopped reading the file. None of the file ran. The lines " +
            "under it that start with at are inside Node itself - node: files - as it was still reading your file, so they " +
            "can be passed over.",
        [("JavaScript", MessagePhase.WhileRunning)] =
            "The first line names the kind of error - TypeError, ReferenceError - and its message. Each line under it that " +
            "starts with at is a function that was running, the one that failed first, with its file, line and column in " +
            "brackets; the ones in node: files are Node's own, and can be passed over.",
        [("Go", MessagePhase.BeforeRunning)] =
            "Go names the file, the line and the column - main.go:7:5 is line 7, column 5 - and what is wrong. Go refuses " +
            "to build a program with even one such error, so none of it ran.",
        [("Go", MessagePhase.WhileRunning)] =
            "This is a panic. The first line says what went wrong - such as runtime error: index out of range [3] with length " +
            "3. Under goroutine 1 [running]: comes each function that was running, the one that failed first, with its file " +
            "and line number on the line below it; the +0x... at the end is a place in the built program, and can be ignored.",
        [("Scala", MessagePhase.BeforeRunning)] =
            "The Scala compiler names the file and the line - and, in Scala 3, the column - says what kind of error it is, and shows the line " +
            "with ^ marks under the part it means. Scala 3 numbers its errors, such as [E007], and the number can be looked " +
            "up. Fix the first error first: the ones after it can follow from it.",
        [("Scala", MessagePhase.WhileRunning)] =
            "Scala runs on the Java virtual machine, so a crash reads as Java's does: the first line names the exception and " +
            "its message, and each line under it that starts with at is a method that was running, the one that failed " +
            "first, with its file and line number in brackets.",
        [("Rust", MessagePhase.BeforeRunning)] =
            "rustc starts with error or warning - an error often with a code in brackets, such as error[E0425], which rustc --explain " +
            "E0425 explains at length - and what is wrong. After --> come the file, the line and the column, then the line itself, " +
            "with ^ marks under the part it means, and often help: with the change rustc suggests.",
        [("Rust", MessagePhase.WhileRunning)] =
            "This is a panic. The first line says which thread stopped - main, for the program itself - and, after panicked at, the " +
            "file, the line and the column where it happened. The line under it is the message saying what went wrong.",
        [("Ruby", MessagePhase.BeforeRunning)] =
            "ruby -c names the file and the line, then what it could not make sense of, with (SyntaxError) at the end. Under it are " +
            "the line and a ^ at the place it stopped - which, for an end that is missing, is often the last line of the file, as " +
            "Ruby only finds out there.",
        [("Ruby", MessagePhase.WhileRunning)] =
            "The first line names the file, the line and the method it was in, then what went wrong and, in brackets at the end, the " +
            "kind of error - such as (ZeroDivisionError). Ruby then shows the line with ^ marks under the part it means, and Did you " +
            "mean? with the name it thinks was meant. Each line starting with from is a call that led there.",
        [("PHP", MessagePhase.BeforeRunning)] =
            "PHP reads a whole file before it runs any of it. Parse error: - or PHP Parse error:, where PHP is set up to log its messages " +
            "- says what it could not make sense of, then after in come the file and, after on line, the line it was reading when it " +
            "noticed - which, for a missing semicolon, is the line after the one that needs it.",
        [("PHP", MessagePhase.WhileRunning)] =
            "Fatal error: Uncaught - or PHP Fatal error: Uncaught, where PHP is set up to log its messages - names the kind of error, " +
            "such as DivisionByZeroError, and what went wrong, then after in come the file and, after a colon, the line. Under Stack " +
            "trace: is each call that led there, the latest first, with its line in brackets; {main} is the program itself, and thrown " +
            "in repeats where it happened. A line that starts Warning: - or PHP Warning: - is one PHP carried on past: it names the " +
            "file, and the line after on line.",
        [("Lua", MessagePhase.BeforeRunning)] =
            "Lua reads a whole file before it runs any of it. Its message starts with the name of the program that read it - luac or " +
            "lua, or the whole path to it - then the file, the line, and what it expected or could not make sense of, ending with near " +
            "and the word or symbol where it stopped. The mistake is often just before that place.",
        [("Lua", MessagePhase.WhileRunning)] =
            "The first line starts with the name of lua - or the whole path to it - then the file, the line and what went wrong; Lua " +
            "gives its errors no type names. In brackets at the end it often says which name held the wrong value - such as (global " +
            "'totl'). Under stack traceback: is each call that led there, the latest first; main chunk is the file itself, and [C] " +
            "lines are inside Lua.",
        [("OCaml", MessagePhase.BeforeRunning)] =
            "OCaml names the file, the line and the characters it means - characters 20-26 are the 21st to the 26th of the " +
            "line, as it counts the first one as 0 - shows the line with ^ marks under them, then Error: or Warning and " +
            "what is wrong. A Hint: after it is OCaml's own suggestion.",
        [("OCaml", MessagePhase.WhileRunning)] =
            "Fatal error: exception names what stopped the program, with what it carries in brackets after it. The lines " +
            "under it - Raised at, Called from - are where it was raised and the calls that led there, each with its file " +
            "and line number.",
    };

    /// <summary>Whether this language's messages of this kind have a description - for the tests that read each one.</summary>
    public static bool Describes(string language, MessagePhase phase) => HowToRead.ContainsKey((language, phase));

    /// <summary>Every description, for the tests that read each one.</summary>
    public static IReadOnlyDictionary<(string Language, MessagePhase Phase), string> All => HowToRead;

    /// <summary>
    /// What the language said about a finding and how to read it - or null for a finding no language's message stands
    /// behind, such as one a logic check found, or one in a language whose messages are not described here.
    /// </summary>
    public static MessageRead? For(Finding finding)
    {
        if (finding.Error is not { } error || error.RawText.Trim().Length == 0) return null;

        MessagePhase? phase = finding.Kind == FindingKind.Runtime ? MessagePhase.WhileRunning
            : finding.Kind == FindingKind.Syntax || error.ExceptionType is "compile error" or "compile warning" or "link error" ? MessagePhase.BeforeRunning
            : null;
        if (phase is not { } when) return null;

        var language = CodeLanguage.Of(finding.InNotebook?.Notebook ?? finding.File)
                       ?? CodeLanguage.All.FirstOrDefault(each => !each.IsAny && each.ParserIds.Contains(error.LanguageId));
        if (language is null || !HowToRead.TryGetValue((language.Name, when), out var howToRead)) return null;

        // MSVC lays its messages out as file(line): error C2065 - not as gcc and clang do, which is what is described.
        if (language.Name is "C" or "C++" && error.LanguageId != "gcc") return null;

        var lines = error.RawText.Replace("\r\n", "\n").Trim('\n').Split('\n');
        var shown = string.Join("\n", lines.Take(MostLinesShown));

        return new MessageRead(language.Name, when, shown, howToRead, lines.Length > MostLinesShown);
    }
}
