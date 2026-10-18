using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// What 415 small programs printed when they failed, in every language FixFinder runs here, read back as FixFinder reads a run:
/// by the reader of the program's language, and by every reader when the language is left to FixFinder - naming what went
/// wrong, the program's own file, and the line it went wrong on.
/// </summary>
/// <remarks>
/// <para>
/// Each program in Fixtures/Captured/(language)/(case) was saved to C:\Users\Public\FixFinderCapture\(language)\(case) and run
/// there by FixFinder's own launcher - built first, for a language that is built - and output.txt beside it holds every line it
/// printed, as printed, each marked with the stream it came on: "out| " or "err| ". The program's last stage is what was saved:
/// the build's output when it would not build, and the run's when it did.
/// </para>
/// <para>
/// What each row expects - what the error is called and its line - was written down before the program ran. Where a tool said
/// otherwise the row says what the tool said: C# calls int total = 10 with no semicolon CS1003, "',' expected", as it reads on
/// into the next line; Node reports a function never closed at the line after the file's last. Line 0 is an error that names
/// its file and no line, as gfortran does for a file that ends with no end line. Lua names itself by the path it was started
/// from, and gfortran's linker names the folder it built in, and both held this computer's user name; those lines say
/// "student" in its place.
/// </para>
/// <para>
/// Running them found four readings to put right. Perl's "BEGIN failed--compilation aborted", below the "Can't locate
/// Missing/Marks.pm in @INC" a use line stopped for, was taken as the error. gfortran's lone "Unexpected end of file" was read
/// by no reader of Fortran's, and nor was its linker's "undefined reference to `show_'", for a subroutine never written. And
/// Dart's late field, named first in its trace with no line, hid the line of main that read it. Five programs, pasted in,
/// would also have been taken for another language's code: three of PowerShell's for PHP, by their $total = 10 lines, one of
/// F#'s for JavaScript, by let, and one of TypeScript's, its string | null not seen as a type.
/// </para>
/// </remarks>
public class CapturedErrorTests : IDisposable
{
    private const string CapturedFolder = @"C:\Users\Public\FixFinderCapture";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The folder a language's programs are kept in: its name in small letters, with C#, C++ and F# spelt out.</summary>
    private static string FolderOf(string language) => language switch
    {
        "C#" => "csharp",
        "C++" => "cpp",
        "F#" => "fsharp",
        _ => language.ToLowerInvariant(),
    };

    /// <summary>
    /// Each program: its language, its case, the file it was saved as, the reader that reads its error, a part of what the error
    /// is called, and the line the error is on.
    /// </summary>
    public static TheoryData<string, string, string, string, string, int> Captured => new()
    {
        // Python
        { "Python", "division-by-zero", "main.py", "python", "ZeroDivisionError", 3 },
        { "Python", "index-past-the-end", "main.py", "python", "IndexError", 2 },
        { "Python", "method-on-none", "main.py", "python", "AttributeError", 2 },
        { "Python", "misspelt-name", "main.py", "python", "NameError", 2 },
        { "Python", "text-plus-number", "main.py", "python", "TypeError", 2 },
        { "Python", "missing-key", "main.py", "python", "KeyError", 2 },
        { "Python", "text-to-number", "main.py", "python", "ValueError", 2 },
        { "Python", "missing-colon", "main.py", "python", "SyntaxError", 2 },
        { "Python", "block-not-indented", "main.py", "python", "IndentationError", 2 },
        { "Python", "error-inside-a-function", "main.py", "python", "ZeroDivisionError", 2 },
        { "Python", "raised-error", "main.py", "python", "ValueError", 3 },
        { "Python", "failed-assertion", "main.py", "python", "AssertionError", 2 },
        { "Python", "endless-recursion", "main.py", "python", "RecursionError", 2 },
        { "Python", "bracket-never-closed", "main.py", "python", "SyntaxError", 1 },
        { "Python", "missing-module", "main.py", "python", "ModuleNotFoundError", 1 },
        { "Python", "number-called", "main.py", "python", "TypeError", 2 },
        { "Python", "argument-missing", "main.py", "python", "TypeError", 4 },
        { "Python", "local-used-before-set", "main.py", "python", "UnboundLocalError", 3 },
        { "Python", "character-past-the-end", "main.py", "python", "IndexError", 2 },
        { "Python", "method-misspelt", "main.py", "python", "AttributeError", 2 },
        { "Python", "tuple-changed", "main.py", "python", "TypeError", 2 },
        { "Python", "decimal-from-text", "main.py", "python", "ValueError", 1 },
        { "Python", "too-many-to-unpack", "main.py", "python", "ValueError", 1 },
        { "Python", "file-not-there", "main.py", "python", "FileNotFoundError", 1 },
        { "Python", "removing-what-is-not-there", "main.py", "python", "ValueError", 2 },
        { "Python", "pop-from-empty", "main.py", "python", "IndexError", 2 },
        { "Python", "length-of-a-number", "main.py", "python", "TypeError", 1 },
        { "Python", "error-inside-a-method", "main.py", "python", "ZeroDivisionError", 3 },
        { "Python", "unexpected-indent", "main.py", "python", "IndentationError", 2 },
        { "Python", "text-never-closed", "main.py", "python", "SyntaxError", 1 },
        { "Python", "assignment-in-if", "main.py", "python", "SyntaxError", 2 },
        { "Python", "import-name-wrong", "main.py", "python", "ImportError", 1 },
        { "Python", "none-plus-number", "main.py", "python", "TypeError", 2 },
        { "Python", "sorting-mixed-values", "main.py", "python", "TypeError", 1 },
        { "Python", "break-outside-loop", "main.py", "python", "SyntaxError", 2 },
        { "Python", "return-outside-function", "main.py", "python", "SyntaxError", 1 },
        { "Python", "dictionary-changed-while-looping", "main.py", "python", "RuntimeError", 2 },
        { "Python", "letter-of-text-changed", "main.py", "python", "TypeError", 2 },
        { "Python", "number-from-none", "main.py", "python", "TypeError", 2 },
        { "Python", "square-root-of-negative", "main.py", "python", "ValueError", 2 },
        { "Python", "list-as-dictionary-key", "main.py", "python", "TypeError", 1 },
        { "Python", "printed-then-failed", "main.py", "python", "ZeroDivisionError", 4 },
        { "Python", "input-with-nothing-typed", "main.py", "python", "EOFError", 1 },
        { "Python", "three-calls-deep", "main.py", "python", "ZeroDivisionError", 2 },
        { "Python", "later-pass-of-a-loop", "main.py", "python", "AttributeError", 3 },
        { "Python", "number-typed-as-input-missing", "main.py", "python", "EOFError", 1 },

        // Java
        { "Java", "division-by-zero", "Main.java", "java", "ArithmeticException", 5 },
        { "Java", "index-past-the-end", "Main.java", "java", "ArrayIndexOutOfBoundsException", 4 },
        { "Java", "method-on-null", "Main.java", "java", "NullPointerException", 4 },
        { "Java", "text-to-number", "Main.java", "java", "NumberFormatException", 3 },
        { "Java", "character-past-the-end", "Main.java", "java", "StringIndexOutOfBoundsException", 4 },
        { "Java", "wrong-cast", "Main.java", "java", "ClassCastException", 4 },
        { "Java", "thrown-exception", "Main.java", "java", "IllegalArgumentException", 3 },
        { "Java", "empty-list-read", "Main.java", "java", "IndexOutOfBoundsException", 6 },
        { "Java", "endless-recursion", "Main.java", "java", "StackOverflowError", 3 },
        { "Java", "missing-semicolon", "Main.java", "java", "';' expected", 3 },
        { "Java", "misspelt-name", "Main.java", "java", "cannot find symbol", 4 },
        { "Java", "text-into-number", "Main.java", "java", "incompatible types", 3 },
        { "Java", "missing-return", "Main.java", "java", "missing return statement", 4 },
        { "Java", "never-given-a-value", "Main.java", "java", "might not have been initialized", 4 },
        { "Java", "class-name-not-the-file-name", "Main.java", "java", "should be declared in a file named", 1 },
        { "Java", "unreachable-statement", "Main.java", "java", "unreachable statement", 4 },
        { "Java", "brace-never-closed", "Main.java", "java", "reached end of file while parsing", 4 },
        { "Java", "method-misspelt", "Main.java", "java", "cannot find symbol", 4 },
        { "Java", "negative-array-size", "Main.java", "java", "NegativeArraySizeException", 4 },
        { "Java", "list-changed-while-looping", "Main.java", "java", "ConcurrentModificationException", 6 },
        { "Java", "decimal-from-text", "Main.java", "java", "NumberFormatException", 3 },
        { "Java", "null-unboxed", "Main.java", "java", "NullPointerException", 4 },
        { "Java", "decimal-into-int", "Main.java", "java", "possible lossy conversion", 3 },
        { "Java", "instance-method-from-static", "Main.java", "java", "non-static method", 7 },
        { "Java", "exception-not-declared", "Main.java", "java", "unreported exception", 3 },
        { "Java", "remainder-by-zero", "Main.java", "java", "ArithmeticException", 4 },
        { "Java", "error-inside-a-method", "Main.java", "java", "ArithmeticException", 3 },
        { "Java", "abstract-class-made", "Main.java", "java", "abstract; cannot be instantiated", 6 },
        { "Java", "next-of-empty-iterator", "Main.java", "java", "NoSuchElementException", 6 },
        { "Java", "number-too-big-for-int", "Main.java", "java", "NumberFormatException", 3 },
        { "Java", "character-into-text", "Main.java", "java", "incompatible types", 3 },
        { "Java", "text-never-closed", "Main.java", "java", "unclosed string literal", 3 },
        { "Java", "printed-then-failed", "Main.java", "java", "ArithmeticException", 6 },
        { "Java", "input-with-nothing-typed", "Main.java", "java", "NoSuchElementException", 6 },
        { "Java", "three-calls-deep", "Main.java", "java", "ArithmeticException", 5 },
        { "Java", "later-pass-of-a-loop", "Main.java", "java", "NullPointerException", 5 },
        { "Java", "line-read-with-nothing-typed", "Main.java", "java", "NoSuchElementException", 6 },

        // C#
        { "C#", "division-by-zero", "main.cs", "csharp", "DivideByZeroException", 3 },
        { "C#", "index-past-the-end", "main.cs", "csharp", "IndexOutOfRangeException", 2 },
        { "C#", "method-on-null", "main.cs", "csharp", "NullReferenceException", 2 },
        { "C#", "text-to-number", "main.cs", "csharp", "FormatException", 1 },
        { "C#", "missing-key", "main.cs", "csharp", "KeyNotFoundException", 2 },
        { "C#", "empty-list-read", "main.cs", "csharp", "ArgumentOutOfRangeException", 2 },
        { "C#", "thrown-exception", "main.cs", "csharp", "InvalidOperationException", 1 },
        { "C#", "missing-semicolon", "main.cs", "msvc", "CS1003", 1 },
        { "C#", "misspelt-name", "main.cs", "msvc", "CS0103", 2 },
        { "C#", "text-into-number", "main.cs", "msvc", "CS0029", 1 },
        { "C#", "argument-missing", "main.cs", "msvc", "CS7036", 1 },
        { "C#", "never-given-a-value", "main.cs", "msvc", "CS0165", 2 },
        { "C#", "converted-from-text", "main.cs", "csharp", "FormatException", 1 },
        { "C#", "list-changed-while-looping", "main.cs", "csharp", "InvalidOperationException", 2 },
        { "C#", "wrong-cast", "main.cs", "csharp", "InvalidCastException", 2 },
        { "C#", "method-misspelt", "main.cs", "msvc", "CS0117", 1 },
        { "C#", "brace-never-closed", "main.cs", "msvc", "CS1513", 3 },
        { "C#", "error-inside-a-method", "main.cs", "csharp", "DivideByZeroException", 5 },
        { "C#", "checked-overflow", "main.cs", "csharp", "OverflowException", 2 },
        { "C#", "decimal-into-int", "main.cs", "msvc", "CS0266", 1 },
        { "C#", "remainder-by-zero", "main.cs", "csharp", "DivideByZeroException", 2 },
        { "C#", "letter-past-the-end", "main.cs", "csharp", "IndexOutOfRangeException", 2 },
        { "C#", "abstract-class-made", "main.cs", "msvc", "CS0144", 1 },
        { "C#", "using-missing", "main.cs", "msvc", "CS0246", 1 },
        { "C#", "text-returned-as-number", "main.cs", "msvc", "CS0029", 5 },
        { "C#", "argument-thrown", "main.cs", "csharp", "ArgumentException", 2 },
        { "C#", "printed-then-failed", "main.cs", "csharp", "DivideByZeroException", 5 },
        { "C#", "input-with-nothing-typed", "main.cs", "csharp", "ArgumentNullException", 2 },
        { "C#", "three-calls-deep", "main.cs", "csharp", "DivideByZeroException", 7 },
        { "C#", "later-pass-of-a-loop", "main.cs", "csharp", "NullReferenceException", 4 },

        // C
        { "C", "misspelt-name", "main.c", "gcc", "undeclared", 5 },
        { "C", "missing-semicolon", "main.c", "gcc", "expected", 5 },
        { "C", "brace-never-closed", "main.c", "gcc", "end of input", 5 },
        { "C", "argument-missing", "main.c", "gcc", "too few arguments", 8 },
        { "C", "unknown-type", "main.c", "gcc", "unknown type name", 4 },
        { "C", "text-into-array", "main.c", "gcc", "assignment to expression with array type", 5 },
        { "C", "commas-in-for", "main.c", "gcc", "expected", 4 },
        { "C", "else-without-if", "main.c", "gcc", "'else' without a previous 'if'", 7 },
        { "C", "declared-twice", "main.c", "gcc", "redefinition of 'total'", 5 },
        { "C", "break-outside-loop", "main.c", "gcc", "break statement not within loop or switch", 5 },
        { "C", "member-not-there", "main.c", "gcc", "has no member named 'y'", 7 },
        { "C", "function-misspelt", "main.c", "gcc", "prnt", 4 },
        { "C", "struct-never-defined", "main.c", "gcc", "storage size of 'p' isn't known", 4 },
        { "C", "case-twice", "main.c", "gcc", "duplicate case value", 9 },
        { "C", "void-result-used", "main.c", "gcc", "void value not ignored", 8 },
        { "C", "too-many-arguments", "main.c", "gcc", "too many arguments", 8 },
        { "C", "printf-of-a-name-never-declared", "main.c", "gcc", "undeclared", 6 },
        { "C", "function-used-before-its-type-known", "main.c", "gcc", "conflicting types", 9 },
        { "C", "pointer-to-int-returned", "main.c", "gcc", "request for member", 6 },

        // C++
        { "C++", "cout-without-std", "main.cpp", "gcc", "was not declared", 4 },
        { "C++", "missing-semicolon", "main.cpp", "gcc", "expected", 5 },
        { "C++", "text-into-number", "main.cpp", "gcc", "invalid conversion", 4 },
        { "C++", "argument-missing", "main.cpp", "gcc", "too few arguments", 8 },
        { "C++", "string-without-std", "main.cpp", "gcc", "was not declared", 5 },
        { "C++", "declared-twice", "main.cpp", "gcc", "redeclaration of 'int total'", 5 },
        { "C++", "vector-without-include", "main.cpp", "gcc", "vector", 4 },
        { "C++", "member-not-there", "main.cpp", "gcc", "has no member named 'y'", 7 },
        { "C++", "constant-changed", "main.cpp", "gcc", "assignment of read-only variable 'total'", 5 },
        { "C++", "text-as-number-argument", "main.cpp", "gcc", "invalid conversion", 8 },
        { "C++", "brace-never-closed", "main.cpp", "gcc", "end of input", 5 },
        { "C++", "private-member-read", "main.cpp", "gcc", "private within this context", 9 },
        { "C++", "no-matching-function", "main.cpp", "gcc", "no matching function for call", 5 },
        { "C++", "semicolon-missing-after-class", "main.cpp", "gcc", "expected ';' after class definition", 5 },
        { "C++", "abstract-class-made", "main.cpp", "gcc", "abstract type", 8 },
        { "C++", "method-on-a-pointer-with-dot", "main.cpp", "gcc", "request for member", 7 },
        { "C++", "loop-variable-never-declared", "main.cpp", "gcc", "was not declared", 5 },
        { "C++", "const-method-changes-a-member", "main.cpp", "gcc", "read-only object", 6 },

        // JavaScript
        { "JavaScript", "misspelt-name", "main.js", "node", "ReferenceError", 2 },
        { "JavaScript", "property-of-undefined", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "number-called", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "constant-changed", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "thrown-error", "main.js", "node", "Error", 1 },
        { "JavaScript", "bad-json", "main.js", "node", "SyntaxError", 1 },
        { "JavaScript", "endless-recursion", "main.js", "node", "RangeError", 2 },
        { "JavaScript", "property-of-null", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "bracket-never-closed", "main.js", "node", "SyntaxError", 1 },
        { "JavaScript", "negative-array-length", "main.js", "node", "RangeError", 1 },
        { "JavaScript", "declared-twice", "main.js", "node", "SyntaxError", 2 },
        { "JavaScript", "method-misspelt", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "module-not-there", "main.js", "node", "Cannot find module", 1 },
        { "JavaScript", "function-never-written", "main.js", "node", "ReferenceError", 1 },
        { "JavaScript", "method-of-a-number", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "assigned-to-a-number", "main.js", "node", "SyntaxError", 2 },
        { "JavaScript", "error-inside-a-function", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "unexpected-end", "main.js", "node", "SyntaxError", 3 },
        { "JavaScript", "class-called-without-new", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "pattern-never-closed", "main.js", "node", "SyntaxError", 1 },
        { "JavaScript", "used-before-declared", "main.js", "node", "ReferenceError", 1 },
        { "JavaScript", "digits-out-of-range", "main.js", "node", "RangeError", 2 },
        { "JavaScript", "bigint-and-number-mixed", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "printed-then-failed", "main.js", "node", "TypeError", 3 },
        { "JavaScript", "three-calls-deep", "main.js", "node", "TypeError", 2 },
        { "JavaScript", "error-in-a-callback", "main.js", "node", "TypeError", 3 },
        { "JavaScript", "error-in-a-class-method", "main.js", "node", "TypeError", 6 },

        // TypeScript
        { "TypeScript", "misspelt-name", "main.ts", "node", "ReferenceError", 2 },
        { "TypeScript", "property-of-undefined", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "thrown-error", "main.ts", "node", "Error", 2 },
        { "TypeScript", "property-of-null", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "error-inside-a-function", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "bad-json", "main.ts", "node", "SyntaxError", 1 },
        { "TypeScript", "endless-recursion", "main.ts", "node", "RangeError", 2 },
        { "TypeScript", "number-called", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "constant-changed", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "missing-key-used", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "item-of-empty-array-used", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "text-method-on-a-number", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "null-read-as-an-interface", "main.ts", "node", "TypeError", 5 },
        { "TypeScript", "used-before-declared", "main.ts", "node", "ReferenceError", 1 },
        { "TypeScript", "printed-then-failed", "main.ts", "node", "TypeError", 3 },
        { "TypeScript", "three-calls-deep", "main.ts", "node", "TypeError", 2 },
        { "TypeScript", "error-in-a-callback", "main.ts", "node", "TypeError", 3 },

        // Go
        { "Go", "misspelt-name", "main.go", "go", "undefined", 7 },
        { "Go", "unused-variable", "main.go", "go", "declared and not used", 6 },
        { "Go", "index-past-the-end", "main.go", "go", "index out of range", 8 },
        { "Go", "division-by-zero", "main.go", "go", "integer divide by zero", 7 },
        { "Go", "nil-map-write", "main.go", "go", "nil map", 5 },
        { "Go", "text-into-number", "main.go", "go", "cannot use", 6 },
        { "Go", "unused-import", "main.go", "go", "imported and not used", 5 },
        { "Go", "missing-return", "main.go", "go", "missing return", 7 },
        { "Go", "nil-pointer", "main.go", "go", "nil pointer dereference", 9 },
        { "Go", "wrong-type-assertion", "main.go", "go", "interface conversion", 7 },
        { "Go", "slice-past-the-end", "main.go", "go", "slice bounds out of range", 8 },
        { "Go", "panic-called", "main.go", "go", "mark out of range", 4 },
        { "Go", "assigned-before-declared", "main.go", "go", "undefined: total", 6 },
        { "Go", "mismatched-number-types", "main.go", "go", "mismatched types", 8 },
        { "Go", "constant-index-past-the-end", "main.go", "go", "out of bounds", 7 },
        { "Go", "nil-channel-closed", "main.go", "go", "close of nil channel", 5 },
        { "Go", "deadlock", "main.go", "go", "deadlock", 5 },
        { "Go", "printed-then-failed", "main.go", "go", "integer divide by zero", 9 },
        { "Go", "three-calls-deep", "main.go", "go", "index out of range", 6 },
        { "Go", "later-pass-of-a-loop", "main.go", "go", "index out of range", 8 },

        // Rust
        { "Rust", "misspelt-name", "main.rs", "rust", "E0425", 3 },
        { "Rust", "mismatched-types", "main.rs", "rust", "E0308", 2 },
        { "Rust", "assigned-twice", "main.rs", "rust", "E0384", 3 },
        { "Rust", "index-past-the-end", "main.rs", "rust", "index out of bounds", 4 },
        { "Rust", "unwrap-none", "main.rs", "rust", "None", 3 },
        { "Rust", "division-by-zero", "main.rs", "rust", "divide by zero", 3 },
        { "Rust", "text-to-number", "main.rs", "rust", "ParseIntError", 2 },
        { "Rust", "used-after-move", "main.rs", "rust", "E0382", 4 },
        { "Rust", "panic-called", "main.rs", "rust", "mark out of range", 2 },
        { "Rust", "expect-on-none", "main.rs", "rust", "no mark at 5", 3 },
        { "Rust", "argument-missing", "main.rs", "rust", "E0061", 6 },
        { "Rust", "changed-without-mut", "main.rs", "rust", "E0596", 3 },
        { "Rust", "missing-semicolon", "main.rs", "rust", "expected `;`", 2 },
        { "Rust", "borrowed-mutably-twice", "main.rs", "rust", "E0499", 4 },
        { "Rust", "match-not-exhaustive", "main.rs", "rust", "E0004", 3 },
        { "Rust", "semicolon-ends-the-result", "main.rs", "rust", "E0308", 1 },
        { "Rust", "text-indexed", "main.rs", "rust", "E0277", 3 },
        { "Rust", "printed-then-failed", "main.rs", "rust", "index out of bounds", 4 },
        { "Rust", "three-calls-deep", "main.rs", "rust", "index out of bounds", 2 },
        { "Rust", "input-parsed-with-nothing-typed", "main.rs", "rust", "ParseIntError", 6 },

        // Ruby
        { "Ruby", "division-by-zero", "main.rb", "ruby", "ZeroDivisionError", 3 },
        { "Ruby", "method-on-nil", "main.rb", "ruby", "NoMethodError", 2 },
        { "Ruby", "misspelt-name", "main.rb", "ruby", "NameError", 2 },
        { "Ruby", "text-plus-number", "main.rb", "ruby", "TypeError", 2 },
        { "Ruby", "text-to-number", "main.rb", "ruby", "ArgumentError", 1 },
        { "Ruby", "missing-key", "main.rb", "ruby", "KeyError", 2 },
        { "Ruby", "raised-error", "main.rb", "ruby", "ArgumentError", 2 },
        { "Ruby", "method-on-missing-item", "main.rb", "ruby", "NoMethodError", 2 },
        { "Ruby", "end-missing", "main.rb", "generic", "syntax error", 4 },
        { "Ruby", "argument-missing", "main.rb", "ruby", "ArgumentError", 1 },
        { "Ruby", "method-of-a-number", "main.rb", "ruby", "NoMethodError", 2 },
        { "Ruby", "constant-misspelt", "main.rb", "ruby", "NameError", 1 },
        { "Ruby", "text-times-text", "main.rb", "ruby", "TypeError", 1 },
        { "Ruby", "remainder-by-zero", "main.rb", "ruby", "ZeroDivisionError", 2 },
        { "Ruby", "text-never-closed", "main.rb", "generic", "unterminated string", 1 },
        { "Ruby", "endless-recursion", "main.rb", "ruby", "SystemStackError", 2 },
        { "Ruby", "printed-then-failed", "main.rb", "ruby", "ZeroDivisionError", 4 },
        { "Ruby", "input-with-nothing-typed", "main.rb", "ruby", "NoMethodError", 1 },
        { "Ruby", "three-calls-deep", "main.rb", "ruby", "ZeroDivisionError", 2 },

        // PHP
        { "PHP", "division-by-zero", "main.php", "php", "DivisionByZeroError", 4 },
        { "PHP", "misspelt-function", "main.php", "php", "undefined function", 2 },
        { "PHP", "missing-semicolon", "main.php", "php", "syntax error", 3 },
        { "PHP", "thrown-exception", "main.php", "php", "Exception", 2 },
        { "PHP", "array-plus-number", "main.php", "php", "Unsupported operand types", 3 },
        { "PHP", "method-on-null", "main.php", "php", "Call to a member function", 3 },
        { "PHP", "wrong-argument-type", "main.php", "php", "TypeError", 2 },
        { "PHP", "brace-never-closed", "main.php", "php", "Unclosed '{'", 4 },
        { "PHP", "class-not-there", "main.php", "php", "not found", 2 },
        { "PHP", "argument-missing", "main.php", "php", "ArgumentCountError", 2 },
        { "PHP", "method-not-there", "main.php", "php", "Call to undefined method", 4 },
        { "PHP", "abstract-class-made", "main.php", "php", "Cannot instantiate abstract class", 3 },
        { "PHP", "text-for-an-int-parameter", "main.php", "php", "must be of type int", 2 },
        { "PHP", "undefined-variable", "main.php", "php", "Undefined variable", 3 },
        { "PHP", "printed-then-failed", "main.php", "php", "DivisionByZeroError", 5 },
        { "PHP", "three-calls-deep", "main.php", "php", "DivisionByZeroError", 3 },
        { "PHP", "method-on-null-in-a-loop", "main.php", "php", "Call to a member function", 7 },

        // Lua
        { "Lua", "arithmetic-on-nil", "main.lua", "lua", "arithmetic on a nil value", 2 },
        { "Lua", "misspelt-function", "main.lua", "lua", "attempt to call a nil value", 1 },
        { "Lua", "index-of-nil", "main.lua", "lua", "attempt to index a nil value", 2 },
        { "Lua", "nil-joined-to-text", "main.lua", "lua", "attempt to concatenate a nil value", 2 },
        { "Lua", "raised-error", "main.lua", "lua", "mark out of range", 2 },
        { "Lua", "nil-compared", "main.lua", "lua", "attempt to compare", 2 },
        { "Lua", "end-missing", "main.lua", "lua", "'end' expected", 3 },
        { "Lua", "field-not-there-called", "main.lua", "lua", "attempt to call a nil value", 2 },
        { "Lua", "error-inside-a-function", "main.lua", "lua", "no winners", 2 },
        { "Lua", "length-of-nil", "main.lua", "lua", "attempt to get length of a nil value", 2 },
        { "Lua", "arithmetic-on-text", "main.lua", "lua", "attempt to add a 'string' with a 'number'", 2 },
        { "Lua", "number-called", "main.lua", "lua", "attempt to call a number value", 2 },
        { "Lua", "number-indexed", "main.lua", "lua", "attempt to index a number value", 2 },
        { "Lua", "then-missing", "main.lua", "lua", "'then' expected", 3 },
        { "Lua", "printed-then-failed", "main.lua", "lua", "attempt to index a nil value", 3 },
        { "Lua", "input-with-nothing-typed", "main.lua", "lua", "attempt to perform arithmetic on a nil value", 2 },
        { "Lua", "three-calls-deep", "main.lua", "lua", "attempt to index a nil value", 2 },

        // Perl
        { "Perl", "division-by-zero", "main.pl", "perl", "Illegal division by zero", 5 },
        { "Perl", "undeclared-variable", "main.pl", "perl", "Global symbol", 3 },
        { "Perl", "died", "main.pl", "perl", "mark out of range", 3 },
        { "Perl", "undefined-sub", "main.pl", "perl", "Undefined subroutine", 3 },
        { "Perl", "missing-semicolon", "main.pl", "perl", "syntax error", 4 },
        { "Perl", "module-not-there", "main.pl", "perl", "Can't locate Missing/Marks.pm", 3 },
        { "Perl", "method-on-undef", "main.pl", "perl", "Can't call method", 4 },
        { "Perl", "text-used-as-a-list", "main.pl", "perl", "Can't use string", 4 },
        { "Perl", "error-inside-a-sub", "main.pl", "perl", "Illegal division by zero", 6 },
        { "Perl", "brace-never-closed", "main.pl", "perl", "Missing right curly", 4 },
        { "Perl", "text-used-as-a-hash", "main.pl", "perl", "Can't use string", 4 },
        { "Perl", "bareword", "main.pl", "perl", "Bareword", 3 },
        { "Perl", "list-named-as-scalar", "main.pl", "perl", "Global symbol", 4 },
        { "Perl", "bracket-never-closed", "main.pl", "perl", "syntax error", 4 },
        { "Perl", "printed-then-failed", "main.pl", "perl", "Illegal division by zero", 6 },
        { "Perl", "three-calls-deep", "main.pl", "perl", "Illegal division by zero", 8 },
        { "Perl", "method-on-undef-in-a-loop", "main.pl", "perl", "Can't call method", 5 },

        // Dart
        { "Dart", "misspelt-name", "main.dart", "dart", "totl", 3 },
        { "Dart", "text-into-number", "main.dart", "dart", "String", 2 },
        { "Dart", "index-past-the-end", "main.dart", "dart", "RangeError", 3 },
        { "Dart", "text-to-number", "main.dart", "dart", "FormatException", 2 },
        { "Dart", "null-checked", "main.dart", "dart", "Null check operator", 3 },
        { "Dart", "missing-semicolon", "main.dart", "dart", "Expected ';'", 2 },
        { "Dart", "getter-not-there", "main.dart", "dart", "size", 3 },
        { "Dart", "thrown-exception", "main.dart", "dart", "mark out of range", 2 },
        { "Dart", "first-of-empty", "main.dart", "dart", "No element", 3 },
        { "Dart", "error-inside-a-function", "main.dart", "dart", "RangeError", 2 },
        { "Dart", "text-cast-to-int", "main.dart", "dart", "is not a subtype of type 'int'", 3 },
        { "Dart", "late-never-given-a-value", "main.dart", "dart", "LateInitializationError", 4 },
        { "Dart", "missing-key-null-checked", "main.dart", "dart", "Null check operator", 3 },
        { "Dart", "method-not-there", "main.dart", "dart", "size", 3 },
        { "Dart", "printed-then-failed", "main.dart", "dart", "RangeError", 4 },
        { "Dart", "three-calls-deep", "main.dart", "dart", "No element", 2 },
        { "Dart", "input-with-nothing-typed", "main.dart", "dart", "Null check operator", 5 },

        // Fortran
        { "Fortran", "index-past-the-end", "main.f90", "fortran", "upper bound", 7 },
        { "Fortran", "undeclared-name", "main.f90", "fortran", "IMPLICIT type", 3 },
        { "Fortran", "text-into-number", "main.f90", "fortran", "Cannot convert", 4 },
        { "Fortran", "end-missing", "main.f90", "fortran", "Unexpected end of file", 0 },
        { "Fortran", "bracket-never-closed", "main.f90", "fortran", "Expected", 3 },
        { "Fortran", "wrong-argument-type", "main.f90", "fortran", "Type mismatch", 3 },
        { "Fortran", "input-ended", "main.f90", "fortran", "End of file", 4 },
        { "Fortran", "argument-missing", "main.f90", "fortran", "Missing actual argument", 3 },
        { "Fortran", "constant-changed", "main.f90", "fortran", "Named constant", 4 },
        { "Fortran", "text-never-closed", "main.f90", "fortran", "Unterminated character constant", 3 },
        { "Fortran", "index-below-the-start", "main.f90", "fortran", "below lower bound", 7 },
        { "Fortran", "subroutine-never-written", "main.f90", "fortran", "show", 3 },
        { "Fortran", "printed-then-failed", "main.f90", "fortran", "above upper bound", 7 },
        { "Fortran", "text-typed-as-a-number-missing", "main.f90", "fortran", "End of file", 5 },
        { "Fortran", "inside-a-subroutine", "main.f90", "fortran", "above upper bound", 10 },

        // PowerShell
        { "PowerShell", "misspelt-command", "main.ps1", "powershell", "CommandNotFoundException", 1 },
        { "PowerShell", "division-by-zero", "main.ps1", "powershell", "divide by zero", 3 },
        { "PowerShell", "method-on-null", "main.ps1", "powershell", "null-valued expression", 2 },
        { "PowerShell", "thrown-error", "main.ps1", "powershell", "mark out of range", 2 },
        { "PowerShell", "index-into-null", "main.ps1", "powershell", "Cannot index into a null array", 2 },
        { "PowerShell", "method-not-there", "main.ps1", "powershell", "does not contain a method named 'Size'", 2 },
        { "PowerShell", "text-to-number", "main.ps1", "powershell", "Cannot convert value", 1 },
        { "PowerShell", "brace-never-closed", "main.ps1", "powershell", "Missing closing '}'", 2 },
        { "PowerShell", "file-not-there", "main.ps1", "powershell", "Cannot find path", 1 },
        { "PowerShell", "index-past-the-end-set", "main.ps1", "powershell", "Index was outside the bounds of the array", 2 },
        { "PowerShell", "parameter-not-there", "main.ps1", "powershell", "A parameter cannot be found that matches parameter name 'Filterr'", 1 },
        { "PowerShell", "text-added-to-a-number", "main.ps1", "powershell", "Cannot convert value", 1 },
        { "PowerShell", "bracket-never-closed", "main.ps1", "powershell", "Unexpected token '{'", 2 },
        { "PowerShell", "printed-then-failed", "main.ps1", "powershell", "null-valued expression", 3 },
        { "PowerShell", "inside-a-function", "main.ps1", "powershell", "divide by zero", 2 },
        { "PowerShell", "thrown-in-a-function", "main.ps1", "powershell", "mark out of range", 2 },

        // Elixir
        { "Elixir", "division-by-zero", "main.exs", "elixir", "ArithmeticError", 3 },
        { "Elixir", "missing-key", "main.exs", "elixir", "KeyError", 2 },
        { "Elixir", "misspelt-variable", "main.exs", "elixir", "undefined variable", 2 },
        { "Elixir", "raised-error", "main.exs", "elixir", "ArgumentError", 2 },
        { "Elixir", "no-clause-matches", "main.exs", "elixir", "FunctionClauseError", 5 },
        { "Elixir", "function-misspelt", "main.exs", "elixir", "UndefinedFunctionError", 1 },
        { "Elixir", "match-failed", "main.exs", "elixir", "MatchError", 1 },
        { "Elixir", "end-missing", "main.exs", "elixir", "missing terminator", 2 },
        { "Elixir", "text-to-number", "main.exs", "elixir", "ArgumentError", 1 },
        { "Elixir", "function-never-written", "main.exs", "elixir", "undefined function", 2 },
        { "Elixir", "number-not-a-list", "main.exs", "elixir", "Protocol.UndefinedError", 1 },
        { "Elixir", "text-plus-number", "main.exs", "elixir", "ArithmeticError", 2 },
        { "Elixir", "no-case-matches", "main.exs", "elixir", "CaseClauseError", 2 },
        { "Elixir", "printed-then-failed", "main.exs", "elixir", "ArithmeticError", 4 },
        { "Elixir", "input-with-nothing-typed", "main.exs", "elixir", "FunctionClauseError", 2 },
        { "Elixir", "three-calls-deep", "main.exs", "elixir", "ArithmeticError", 2 },

        // Erlang
        { "Erlang", "division-by-zero", "main.erl", "erlang", "badarith", 6 },
        { "Erlang", "no-clause-matches", "main.erl", "erlang", "function_clause", 4 },
        { "Erlang", "unbound-variable", "main.erl", "erlang", "unbound", 5 },
        { "Erlang", "match-failed", "main.erl", "erlang", "badmatch", 5 },
        { "Erlang", "function-misspelt", "main.erl", "erlang", "undef", 5 },
        { "Erlang", "no-case-matches", "main.erl", "erlang", "case_clause", 6 },
        { "Erlang", "full-stop-missing", "main.erl", "erlang", "syntax error", 5 },
        { "Erlang", "function-never-written", "main.erl", "erlang", "undefined", 5 },
        { "Erlang", "text-to-number", "main.erl", "erlang", "badarg", 5 },
        { "Erlang", "head-of-empty", "main.erl", "erlang", "badarg", 6 },
        { "Erlang", "no-if-matches", "main.erl", "erlang", "if_clause", 6 },
        { "Erlang", "text-plus-number", "main.erl", "erlang", "badarith", 6 },
        { "Erlang", "printed-then-failed", "main.erl", "erlang", "badarith", 8 },
        { "Erlang", "three-calls-deep", "main.erl", "erlang", "badarith", 4 },

        // F#
        { "F#", "division-by-zero", "main.fsx", "fsharp", "DivideByZeroException", 3 },
        { "F#", "index-past-the-end", "main.fsx", "fsharp", "IndexOutOfRangeException", 2 },
        { "F#", "misspelt-name", "main.fsx", "fsharp", "FS0039", 2 },
        { "F#", "text-into-number", "main.fsx", "fsharp", "FS0001", 1 },
        { "F#", "missing-key", "main.fsx", "fsharp", "KeyNotFoundException", 2 },
        { "F#", "head-of-empty", "main.fsx", "fsharp", "ArgumentException", 2 },
        { "F#", "failwith-called", "main.fsx", "fsharp", "mark out of range", 2 },
        { "F#", "text-to-number", "main.fsx", "fsharp", "FormatException", 1 },
        { "F#", "argument-of-the-wrong-type", "main.fsx", "fsharp", "FS0001", 2 },
        { "F#", "item-past-the-end", "main.fsx", "fsharp", "ArgumentException", 2 },
        { "F#", "option-value-of-none", "main.fsx", "fsharp", "ArgumentException", 2 },
        { "F#", "function-misspelt", "main.fsx", "fsharp", "FS0039", 2 },
        { "F#", "number-too-big", "main.fsx", "fsharp", "OverflowException", 1 },
        { "F#", "text-plus-number", "main.fsx", "fsharp", "FS0001", 1 },
        { "F#", "printed-then-failed", "main.fsx", "fsharp", "DivideByZeroException", 4 },
        { "F#", "three-calls-deep", "main.fsx", "fsharp", "DivideByZeroException", 1 },
        { "F#", "input-with-nothing-typed", "main.fsx", "fsharp", "NullReferenceException", 2 },

        // Scala
        { "Scala", "division-by-zero", "main.scala", "scala", "ArithmeticException", 4 },
        { "Scala", "index-past-the-end", "main.scala", "scala", "ArrayIndexOutOfBoundsException", 3 },
        { "Scala", "option-get-none", "main.scala", "scala", "NoSuchElementException", 3 },
        { "Scala", "misspelt-name", "main.scala", "scala", "Not found", 3 },
        { "Scala", "text-into-number", "main.scala", "scala", "Required: Int", 2 },
        { "Scala", "missing-key", "main.scala", "scala", "NoSuchElementException", 3 },
        { "Scala", "text-to-number", "main.scala", "scala", "NumberFormatException", 2 },
        { "Scala", "head-of-empty", "main.scala", "scala", "NoSuchElementException", 3 },
        { "Scala", "thrown-exception", "main.scala", "scala", "IllegalArgumentException", 3 },
        { "Scala", "method-on-null", "main.scala", "scala", "NullPointerException", 3 },
        { "Scala", "wrong-cast", "main.scala", "scala", "ClassCastException", 3 },
        { "Scala", "text-for-an-int-parameter", "main.scala", "scala", "Required: Int", 4 },
        { "Scala", "no-case-matches", "main.scala", "scala", "MatchError", 4 },
        { "Scala", "printed-then-failed", "main.scala", "scala", "ArithmeticException", 5 },
        { "Scala", "three-calls-deep", "main.scala", "scala", "ArithmeticException", 1 },
        { "Scala", "input-with-nothing-typed", "main.scala", "scala", "Exception", 4 },
    };

    private static IReadOnlyList<CapturedLine> Output(string language, string caseName) => Fixtures.LoadCaptured(FolderOf(language), caseName);

    private static string[] SourceRoots(string language, string caseName) => [Path.Combine(CapturedFolder, FolderOf(language), caseName)];

    /// <summary>What the error is called, its code and its message, together.</summary>
    private static string Described(ParsedError error) =>
        string.Join(" ", new[] { error.ExceptionType, error.ErrorCode, error.Message }.Where(part => !string.IsNullOrEmpty(part)));

    private static void AssertNamesTheErrorAndItsLine(ParsedError? error, string reader, string file, string kind, int line)
    {
        Assert.NotNull(error);
        Assert.Equal(reader, error.LanguageId);
        Assert.Contains(kind, Described(error), StringComparison.Ordinal);

        var culprit = error.CulpritFrame;
        Assert.NotNull(culprit);
        Assert.Equal(file, Path.GetFileName(culprit.File), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(line == 0 ? null : line, culprit.Line);
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void ItsLanguagesReaderNamesTheErrorTheFileAndTheLine(string language, string caseName, string file, string reader, string kind, int line)
    {
        var languageRead = CodeLanguage.All.Single(each => each.Name == language);

        var error = languageRead.Parsers().Parse(Output(language, caseName), SourceRoots(language, caseName));

        AssertNamesTheErrorAndItsLine(error, reader, file, kind, line);
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void ItIsReadTheSameWhenNotToldWhereTheProgramIs(string language, string caseName, string file, string reader, string kind, int line)
    {
        // As the window's run reads it: its frames are of no one's files in particular, as no folder is named for the program.
        var error = CodeLanguage.All.Single(each => each.Name == language).Parsers().Parse(Output(language, caseName));

        AssertNamesTheErrorAndItsLine(error, reader, file, kind, line);
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void EveryReaderTogetherReadsItTheSameWhenTheLanguageIsLeftToFixFinder(string language, string caseName, string file, string reader, string kind, int line)
    {
        var error = new ParserRegistry().Parse(Output(language, caseName), SourceRoots(language, caseName));

        AssertNamesTheErrorAndItsLine(error, reader, file, kind, line);
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void ItIsReadTheSameAfterAPageOfWhatTheProgramPrintedFirst(string language, string caseName, string file, string reader, string kind, int line)
    {
        // A program that prints a page before it fails: 200 lines of its own, on the stream a program prints on.
        var printedFirst = Enumerable.Range(1, 200).Select(number => new CapturedLine(number, StreamKind.StdOut, $"Mark {number} of 200 is {50 + number % 50}", TimeSpan.Zero));
        var failure = Output(language, caseName).Select(captured => captured with { Sequence = captured.Sequence + 200 });

        var error = CodeLanguage.All.Single(each => each.Name == language).Parsers().Parse([.. printedFirst, .. failure], SourceRoots(language, caseName));

        AssertNamesTheErrorAndItsLine(error, reader, file, kind, line);
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void TheErrorIsPutDownToTheProgramNotToALibrary(string language, string caseName, string file, string reader, string kind, int line)
    {
        var error = CodeLanguage.All.Single(each => each.Name == language).Parsers().Parse(Output(language, caseName), SourceRoots(language, caseName));

        Assert.NotNull(error);
        Assert.True(CulpritFrameSelector.CulpritIsFirstParty(error), $"{language} {caseName}: put down to {error.CulpritFrame?.Origin} code, {error.CulpritFrame?.File}");
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void TheProgramIsNeverTakenForAnotherLanguagesCode(string language, string caseName, string file, string reader, string kind, int line)
    {
        var program = File.ReadAllText(Path.Combine(Fixtures.Root, "Captured", FolderOf(language), caseName, file));

        var guessed = PastedCode.LanguageOf(program);

        // Unsure is the person's to settle; a wrong guess would run their code as another language's.
        Assert.True(guessed is null || guessed.Name == language, $"{language} {caseName} was taken for {guessed?.Name}");
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void TheLineNamedIsALineOfTheProgramThatRan(string language, string caseName, string file, string reader, string kind, int line)
    {
        var program = File.ReadAllText(Path.Combine(Fixtures.Root, "Captured", FolderOf(language), caseName, file)).ReplaceLineEndings("\n");

        // A file ending in a newline has one line more after it, empty - where a tool says it reached the end still reading.
        Assert.InRange(line, 0, program.Split('\n').Length);
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void EveryLogicCheckReadsTheProgramAndPointsOnlyInsideIt(string language, string caseName, string file, string reader, string kind, int line) =>
        LogicCheckRobustnessTests.AssertEveryCheckReadsItAndPointsOnlyInsideIt(SourceFile.Read(Path.Combine(Fixtures.Root, "Captured", FolderOf(language), caseName, file))!);

    [Theory]
    [MemberData(nameof(Captured))]
    public void EveryFixRuleAnswersTheErrorAndAnyChangeItOffersFitsTheProgram(string language, string caseName, string file, string reader, string kind, int line)
    {
        // The program copied to a folder of its own, and the output made to name that folder where it named the one it ran in -
        // with \ or /, and with \\ as gcc writes the file of a change it suggests.
        var copy = Path.Combine(_temp.Path, FolderOf(language), caseName);
        Directory.CreateDirectory(copy);
        File.Copy(Path.Combine(Fixtures.Root, "Captured", FolderOf(language), caseName, file), Path.Combine(copy, file));
        var ranIn = SourceRoots(language, caseName)[0];
        var output = Output(language, caseName)
            .Select(captured => captured with
            {
                Text = captured.Text.Replace(ranIn.Replace(@"\", @"\\"), copy.Replace(@"\", @"\\"), StringComparison.OrdinalIgnoreCase)
                    .Replace(ranIn, copy, StringComparison.OrdinalIgnoreCase)
                    .Replace(ranIn.Replace('\\', '/'), copy.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase),
            })
            .ToList();

        var languageRead = CodeLanguage.All.Single(each => each.Name == language);
        var readers = languageRead.Parsers();
        var error = readers.Parse(output, [copy]);
        Assert.NotNull(error);
        var context = new LocalFixContext { Error = error, Others = readers.Others(error, output, [copy]), Output = output, SourceRoot = copy, Language = languageRead };
        var program = SourceFile.Read(Path.Combine(copy, file))!;

        foreach (var rule in LocalFixEngine.Rules.Where(languageRead.Reads))
        {
            // A rule that throws fails the test, with what it threw.
            if (rule.Propose(context) is not { } change) continue;

            Assert.Equal(Path.GetFullPath(Path.Combine(copy, file)), Path.GetFullPath(change.File), StringComparer.OrdinalIgnoreCase);
            Assert.InRange(change.StartLine, 1, program.Count + 1);
            Assert.True(change.StartLine - 1 + change.RemoveCount <= program.Count, $"{rule.Id} removes lines past the end of {file}");
            Assert.DoesNotContain(change.NewLines, newLine => newLine.Contains('\n') || newLine.Contains('\r'));
            Assert.False(change.ApplyTo(program)!.SequenceEqual(program.Lines), $"{rule.Id} offers a change that changes nothing");
        }
    }
}
