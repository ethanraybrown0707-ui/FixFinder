using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// C and C++, from gcc's words and MSVC's codes, each explained three ways: for someone new to programming, as it is
/// usually taught, and in the language's own terms.
/// </summary>
internal static class NativeGuides
{
    private const string NothingRuns = "The compiler refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Gcc(
        string pattern, string beginner, string explanation, string technical, string fix, string example, string? why = null) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    private static GuideEntry Msvc(
        string[] codes, string beginner, string explanation, string technical, string fix, string example, string? why = null) => new()
    {
        Codes = codes,
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    private static GuideEntry Crash(string pattern, string beginner, string explanation, string technical, string why, string fix, string example) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Gcc(@"^expected '?;'?",
            "C and C++ need a semicolon at the end of every statement and declaration, like a full stop at the end of a " +
            "sentence. One is missing - and the compiler often only notices at the start of the next line, so look at the end " +
            "of the line before.",
            "A statement is missing its semicolon, or a declaration before it is not finished.",
            "Statements and declarations end with a semicolon - a struct or class definition too, after its closing brace. The " +
            "parser met a token that cannot continue the construct where it expected one, and reports where that token is, " +
            "often on the following line.",
            "Put a semicolon at the end of the statement - usually the line before the one named.",
            """
            int total = 0;
            total += price;
            """),

        Msvc(["C2143", "C2146", "C2059", "C2061", "C1075", "C1004"],
            "The compiler reads code in a fixed shape: statements end with semicolons, and brackets and braces come in pairs. " +
            "Something here breaks that shape - a semicolon is missing, or a bracket or brace is missing or extra.",
            "The compiler could not read the code here: a semicolon, bracket or brace is missing or extra.",
            "MSVC's parser could not go on: C2143 and C2146 report a token missing before the one named, C2059 and C2061 a " +
            "token that cannot appear there, C1075 a brace or bracket never matched, and C1004 the end of the file reached " +
            "inside a construct.",
            "Check the line named and the one before it for a missing semicolon or unbalanced brace.",
            """
            if (score > 50) {
                printf("pass\n");
            }
            """),

        Gcc(@"undeclared|was not declared in this scope|use of undeclared identifier",
            "In C and C++ every name has to be declared before the line that uses it, because the compiler reads the file from " +
            "top to bottom. This name has not been declared by this point: it may be spelt differently, declared lower down or " +
            "inside other braces, or come from a header that is not #included.",
            "The code uses a name the compiler has not seen declared at this point - it is misspelt, declared later or inside another block, " +
            "or needs a header.",
            "Unqualified lookup found no declaration visible here. C and C++ require declaration before use, in the order the " +
            "file is read; a name declared in a block ends at its closing brace; and library names come into scope only " +
            "through their headers - C++'s standard ones through std:: or a using-declaration too.",
            "Check the spelling against the declaration, declare it before this line, or include the header that declares it.",
            """
            #include <stdio.h>

            int main(void) {
                int count = 3;
                printf("%d\n", count);
                return 0;
            }
            """),

        Msvc(["C2065", "C3861"],
            "In C and C++ every name must be declared before the line that uses it, because the compiler reads the file from " +
            "top to bottom. This name has not been declared by this point - it may be misspelt, declared further down, or come " +
            "from a header that is not #included.",
            "The code uses a name the compiler has not seen declared at this point - it is misspelt, declared later, or needs a header.",
            "C2065 is an undeclared identifier and C3861 a function name that could not be resolved, even by argument-dependent " +
            "lookup. Lookup only sees declarations earlier in the file and in the headers included before this line.",
            "Check the spelling against the declaration, declare it before this line, or include the header that declares it.",
            """
            #include <stdio.h>

            int count = 3;
            printf("%d\n", count);
            """),

        Gcc(@"implicit declaration of function",
            "A function has to be declared before it is called, so the compiler knows what it takes and what it gives back. " +
            "This one is called before any declaration - its header is not #included, or it is written further down the file " +
            "with no prototype above.",
            "A function is called before anything declares it - its header is missing, or its prototype comes later in the file.",
            "C99 removed implicit function declarations, which C89 allowed by assuming the function returns int. GCC 14 and " +
            "later reject the call by default; earlier versions only warn. The assumed int return type truncates a returned " +
            "pointer wherever pointers are wider than int, as on 64-bit systems.",
            "Include the header that declares it, or add a prototype above the first call.",
            """
            #include <stdlib.h>

            int *numbers = malloc(10 * sizeof *numbers);
            """,
            why: "C then guesses the function returns int; on 64-bit Windows a pointer squeezed through that guess is cut in half and the program crashes."),

        Gcc(@"undefined reference to|unresolved external symbol",
            "Building a program has two steps: compiling each file, then linking them together. The compiler was satisfied " +
            "because the function is declared, but the linker could not find the function's actual code in any of the files " +
            "- it may be misspelt, or the file that defines it is not part of the build.",
            "The code calls a function that is declared but never defined in any file the program is built from - often a misspelt name.",
            "The linker found a reference to a symbol that no object file or library in the link defines: the definition is " +
            "missing, spelt differently, not compiled into the build, in a library not given to the linker, or - in C++ - a " +
            "different mangled name, because its parameter types differ or extern \"C\" is on one side only.",
            "Check the spelling of the call, and that the file defining the function is part of the build.",
            """
            int square(int n) { return n * n; }

            int main(void) { return square(3); }
            """),

        Msvc(["LNK2019", "LNK2001", "LNK1120"],
            "Building a program has two steps: compiling each file, then linking them into one program. Compiling worked " +
            "because the function is declared, but the linker could not find its code anywhere - the name may be misspelt, or " +
            "the file defining it is not part of the build.",
            "The code calls a function that is declared but never defined in any file the program is built from - often a misspelt name.",
            "LNK2019 and LNK2001 report a symbol that is used but defined in no object file or library given to the linker, " +
            "and LNK1120 counts them. With C++'s decorated names, a definition whose parameter types or calling convention " +
            "differ, or that has extern \"C\" on one side only, is a different symbol.",
            "Check the spelling of the call, and that the file defining the function is part of the build.",
            """
            int square(int n) { return n * n; }
            """),

        Gcc(@"too (?:few|many) arguments to function",
            "A function lists the values it needs in its declaration. This call gives it more or fewer values than that list, " +
            "so the compiler stops.",
            "The function is called with a different number of arguments than its declaration has parameters.",
            "A call to a function with a prototype must give exactly one argument per parameter, unless the parameter list ends " +
            "in ... or, in C++, the trailing parameters have default arguments.",
            "Pass exactly the arguments the declaration lists.",
            """
            int add(int a, int b);

            int sum = add(2, 3);
            """),

        Gcc(@"incompatible types|invalid conversion from|cannot convert|makes (?:pointer from integer|integer from pointer)",
            "C and C++ check that each value goes where its kind belongs. Here one kind is used where another is needed - " +
            "often a plain number where a pointer, an address in memory, goes, as when scanf is given count instead of &count.",
            "A value of one type is used where a different, incompatible type is needed - such as a number where a pointer goes.",
            "There is no implicit conversion between these types here. C converts between integers and pointers only with a " +
            "cast - GCC 14 and later reject the implicit form by default - and C++ allows neither that nor an implicit " +
            "conversion from void*.",
            "Pass the right kind of value - &value for a pointer, *pointer for the value - or convert it explicitly.",
            """
            int count = 0;
            scanf("%d", &count);
            """),

        Msvc(["C2440", "C2664", "C2446"],
            "The compiler checks that each value goes where its kind belongs, and this line uses one kind of value where a " +
            "different kind is needed - a number where an address goes, for example.",
            "A value of one type is used where a different, incompatible type is needed.",
            "C2440 is a conversion that cannot be made, C2664 an argument that cannot be converted to its parameter's type, and " +
            "C2446 the two sides of an operator with no conversion between them; each needs a cast or a value of the right type.",
            "Pass the right kind of value, or convert it explicitly.",
            """
            int count = 0;
            scanf("%d", &count);
            """),

        Gcc(@"expected declaration or statement at end of input|expected '}' at end of input",
            "Every { that opens a block needs a } to close it. One block was never closed, so the compiler reached the end of " +
            "the file still inside it.",
            "A block is opened with { and never closed, so the compiler reaches the end of the file still inside it.",
            "The file ended inside a compound statement or a declaration: the braces do not balance. C and C++ ignore " +
            "indentation, so the missing } can belong anywhere after the brace that opened the unclosed block.",
            "Add the missing } where the block ends.",
            """
            int main(void) {
                printf("hi\n");
                return 0;
            }
            """),

        Gcc(@"No such file or directory|file not found",
            "#include copies in a header file - a list of declarations. The compiler looked for this one and could not find " +
            "it: the name may be misspelt, or it is not one of this compiler's standard headers.",
            "An #include names a header that cannot be found - it is misspelt, or it is not part of this compiler's library.",
            "#include <name> searches the compiler's system include directories and any given with -I, and #include \"name\" " +
            "searches the including file's own directory first and then those; neither found the file. Some headers belong to " +
            "one platform only, such as conio.h on Windows or unistd.h on POSIX systems.",
            "Check the header's name; standard headers use angle brackets, your own use quotes.",
            """
            #include <stdio.h>
            #include "list.h"
            """),

        Gcc(@"conflicting types for|redefinition of|redeclared as different kind",
            "Each function or variable must be described the same way wherever it is declared, and given its body or value " +
            "only once. Here the same name is declared in two different ways, or defined twice - often because a header is " +
            "included twice without a guard.",
            "The same name is declared twice in different ways, or defined twice.",
            "Every declaration of an entity must have a compatible type, and the one-definition rule allows only one " +
            "definition; an include guard or #pragma once stops a header's definitions being read twice. In C, calling a " +
            "function before declaring it can also clash with the int-returning declaration C89 assumed.",
            "Make every declaration match the definition, and define each thing once - use a header guard in headers.",
            """
            #ifndef LIST_H
            #define LIST_H
            struct node { int value; struct node *next; };
            #endif
            """),

        Gcc(@"no match for 'operator|invalid operands",
            "Operators like + and << only work on types that support them. std::cout << knows how to print numbers and text, " +
            "but not a type you made yourself - unless you write how.",
            "The operator is used with types that do not support it, such as << with a type that has no output operator.",
            "In C, the operators apply to arithmetic and pointer operands, so a struct can be neither added nor compared. In " +
            "C++, overload resolution found no built-in or user-defined operator for these operand types - a class needs its " +
            "own overload, such as operator<<(std::ostream&, const T&), found by argument-dependent lookup.",
            "Use a type that supports the operator, or write the operator for your type.",
            """
            std::cout << person.name << '\n';
            """),

        Gcc(@"request for member .* which is of (?:non-class|pointer) type|base operand of '->' has non-pointer type",
            "A dot reads a part of a struct or an object: p.x. When what you have is a pointer to one - its address - you use " +
            "an arrow instead, p->x, which follows the address first. This line uses the wrong one.",
            "A member is accessed with . on a pointer, or with -> on an object.",
            "The . operator needs an operand of struct, union or class type and -> one of pointer type; a->b means (*a).b. " +
            "Using . on a pointer, or -> on an object, asks for a member of a type that has no members.",
            "Use -> through a pointer and . on an object.",
            """
            struct point *p = &origin;
            printf("%d\n", p->x);
            """),

        Gcc(@"control reaches end of non-void function|no return statement in function returning non-void",
            "A function that says it gives back a value has to give one back on every way through it. There is a way to reach " +
            "the end of this one without a return - and then the caller gets whatever junk happens to be left there.",
            "The function promises to return a value, but a path through it reaches the end without a return.",
            "Flowing off the end of a non-void function is undefined behaviour in C++, and in C when the caller uses the " +
            "value; compilers only warn (-Wreturn-type). main is the exception: reaching its end returns 0.",
            "Add a return for the case none of the branches handled.",
            """
            int grade(int score) {
                if (score >= 50) return 1;
                return 0;
            }
            """,
            why: "The caller gets whatever happens to be left in a register - a different wrong number on different machines."),

        Msvc(["C4715", "C4716"],
            "A function that says it gives back a value has to give one back on every way through it. There is a way to reach " +
            "the end of this one without a return, and then the caller gets an unpredictable value.",
            "The function promises to return a value, but a path through it reaches the end without a return.",
            "C4715 warns that not every control path returns a value, and C4716 that a function returns no value at all; the " +
            "caller using the missing value is undefined behaviour.",
            "Add a return for the case none of the branches handled.",
            """
            int grade(int score) {
                if (score >= 50) return 1;
                return 0;
            }
            """,
            why: "The caller gets an unpredictable value."),

        Gcc(@"format '%\w+' expects|format specifies type",
            "printf and scanf use codes in the text - %d, %f, %s - to know what kind of value to expect for each one. The code " +
            "here does not match the value given, so printf reads the value the wrong way, and scanf may write to the wrong " +
            "place.",
            "The printf or scanf format does not match the type of the value given for it.",
            "Variadic arguments carry no type, so the conversion specifier alone tells printf how to read each one, and a " +
            "mismatch is undefined behaviour. -Wformat checks a literal format string against the arguments' types after the " +
            "default promotions - a float is passed as a double.",
            "Use the conversion that matches the type - %d int, %ld long, %f double, %s char*, %c char - and give scanf addresses.",
            """
            double price = 2.5;
            printf("%.2f\n", price);
            """,
            why: "The value is read as the wrong type: printf prints nonsense and scanf writes to the wrong place, which can crash."),

        Msvc(["C4477", "C4473", "C4474"],
            "printf and scanf use codes in the text - %d, %f, %s - to know what kind of value to expect for each one. The code " +
            "here does not match the value given, or there are too few or too many values for the codes.",
            "The printf or scanf format does not match the type of the value given for it.",
            "MSVC checks format strings: C4477 is an argument whose type does not match its conversion specifier, C4473 too " +
            "few arguments for the format and C4474 too many. Variadic arguments carry no type, so a mismatch is undefined " +
            "behaviour.",
            "Use the conversion that matches the type, and give scanf addresses.",
            """
            printf("%.2f\n", price);
            """,
            why: "The value is read as the wrong type, which prints nonsense or crashes."),

        Gcc(@"unused variable|set but not used|defined but not used",
            "The code makes a variable or a function and never uses it. Often it is left over from earlier code, or a sign " +
            "that a variable with a similar name is being used by mistake.",
            "A variable or function is declared and never used.",
            "-Wunused-variable reports a local never referenced, -Wunused-but-set-variable one assigned but never read, and " +
            "-Wunused-function a static function never called; [[maybe_unused]] (C++17, C23) marks one that is unused on purpose.",
            "Remove it, or use it where it was meant to be used.",
            """
            int total = sum(values, n);
            printf("%d\n", total);
            """,
            why: "It is often a sign the code uses another variable by mistake, and it clutters the code."),

        Gcc(@"suggest parentheses around assignment used as truth value",
            "One = stores a value; two == compare values. The condition here uses one =, so instead of checking the variable " +
            "it overwrites it, and the if goes the same way almost every time.",
            "The condition uses = (assignment) where == (comparison) was probably meant.",
            "An assignment is an expression whose value is the value assigned, so if (x = 0) stores 0 and tests it as false. " +
            "-Wparentheses flags an assignment used as a condition, and a second pair of parentheses marks one that is meant.",
            "Use == to compare.",
            """
            if (count == 0) {
                printf("empty\n");
            }
            """,
            why: "The condition assigns instead of checking, so the if takes the same branch every time and the variable is overwritten."),

        Gcc(@"is used uninitialized|may be used uninitialized",
            "A variable declared inside a function starts with whatever was in that memory before - leftover junk, not zero. " +
            "This one is read before the program has stored anything in it, so its value can differ from run to run.",
            "The variable is read before anything has stored a value in it.",
            "An automatic variable's value is indeterminate until it is assigned, and reading it is undefined behaviour - in C, " +
            "for a variable whose address is never taken. -Wuninitialized and -Wmaybe-uninitialized depend on the optimiser's " +
            "data-flow analysis, so they find more at -O1 and above.",
            "Give it a starting value where it is declared.",
            """
            int total = 0;
            for (int i = 0; i < n; i++) total += values[i];
            """,
            why: "It holds whatever was in memory before, so the result is different from run to run."),

        Msvc(["C4700", "C4701", "C4703"],
            "A variable declared inside a function starts with whatever was in that memory before - leftover junk, not zero. " +
            "This one is read, on at least one way through the code, before anything has been stored in it.",
            "The variable is read before anything has stored a value in it.",
            "C4700 is an uninitialized local variable that is certainly used, C4701 one that may be, and C4703 a local pointer " +
            "that may be; an automatic variable's value is indeterminate until it is assigned.",
            "Give it a starting value where it is declared.",
            """
            int total = 0;
            """,
            why: "It holds whatever was in memory before, so the result changes from run to run."),

        Gcc(@"comparison of integer expressions of different signedness|comparison between signed and unsigned",
            "Unsigned numbers cannot be negative. When a signed number, which can be, is compared with an unsigned one, C and " +
            "C++ convert the signed one first - and -1 becomes a huge positive number, so the comparison comes out wrong.",
            "A signed number is compared with an unsigned one, such as an int against a size().",
            "The usual arithmetic conversions turn a signed operand unsigned when the unsigned type's rank is at least as high, " +
            "so a negative int compared with a size_t wraps around to a large value; -Wsign-compare flags the comparison.",
            "Use the same kind on both sides - size_t for a loop over a container's size.",
            """
            for (size_t i = 0; i < names.size(); i++) {
                std::cout << names[i] << '\n';
            }
            """,
            why: "A negative number turns into a huge unsigned one, so the comparison gives the wrong answer."),

        Msvc(["C4018", "C4389"],
            "Unsigned numbers cannot be negative. When a signed number is compared with an unsigned one, it is converted " +
            "first - and -1 becomes a huge positive number, so the comparison comes out wrong.",
            "A signed number is compared with an unsigned one.",
            "C4018 is a signed/unsigned mismatch in an ordering comparison and C4389 in an equality comparison; the usual " +
            "arithmetic conversions make the signed operand unsigned, so a negative value becomes a large one.",
            "Use the same kind on both sides.",
            """
            for (size_t i = 0; i < names.size(); i++) { }
            """,
            why: "A negative number turns into a huge unsigned one, so the comparison gives the wrong answer."),

        Gcc(@"this '(?:if|for|while|else)' clause does not guard",
            "Without braces, an if or a loop controls only the one statement straight after it, however the lines are " +
            "indented. The next line here is indented as if it belongs, but it runs every time.",
            "The indentation makes a line look as if it belongs to the if or loop above, but without braces only the first line does.",
            "An if, else, for or while without braces governs exactly one statement. C and C++ ignore indentation, and " +
            "-Wmisleading-indentation warns when the next line's indentation suggests otherwise.",
            "Put braces around every line that belongs to the if or loop.",
            """
            if (error) {
                printf("failed\n");
                return 1;
            }
            """,
            why: "The second line runs every time, which is easy to miss because the indentation says otherwise."),

        Gcc(@"suggest braces around empty body|empty body",
            "A semicolon on its own is a complete, empty statement. Put straight after if (...), while (...) or for (...), it " +
            "becomes the whole body - so the block underneath is not part of it, and runs every time.",
            "There is a semicolon straight after the if, while or for, which makes its body empty.",
            "A null statement - a lone semicolon - as the body makes the block after it unconditional. -Wempty-body, part of " +
            "-Wextra, warns about an if, else or do-while whose body is empty.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                printf("pass\n");
            }
            """,
            why: "The block below runs every time, whatever the condition."),

        Gcc(@"this statement may fall through",
            "In a switch the program jumps to the matching case and keeps going down through the cases below until it meets a " +
            "break. This case has no break, so the next case's code runs as well.",
            "This case has no break, so the program carries straight on into the next case.",
            "Execution falls from one case's statements into the next unless break, return, goto or a call to a function that " +
            "never returns intervenes; -Wimplicit-fallthrough reports it, and [[fallthrough]]; (C++17, C23) marks it as meant.",
            "End the case with break.",
            """
            case 1:
                printf("one\n");
                break;
            """,
            why: "Choosing one option also runs the next."),

        Gcc(@"comparison with string literal",
            "In C, text is stored as characters somewhere in memory, and a string variable holds where they are. == compares " +
            "those places, not the letters, so two copies of \"yes\" kept in different places do not count as equal.",
            "Strings are compared with ==, which compares where they are in memory, not the letters in them.",
            "A string literal decays to a pointer to its first character, so == compares addresses, and whether identical " +
            "literals share storage is unspecified; -Waddress warns about the comparison. strcmp compares the characters, and " +
            "std::string's == compares contents.",
            "Compare C strings with strcmp(a, b) == 0; compare std::string with ==.",
            """
            if (strcmp(answer, "yes") == 0) {
                printf("ok\n");
            }
            """,
            why: "Two strings with the same letters are usually at different addresses, so the check fails."),

        Gcc(@"address of local variable|reference to local variable",
            "Variables declared inside a function only exist while the function runs. This function gives back the address of " +
            "one of them - but once it returns, that memory is handed to whatever runs next, so the address points at junk.",
            "The function returns the address of a variable that stops existing when the function returns.",
            "An object with automatic storage ends its lifetime when its block exits, so a returned pointer or reference to it " +
            "dangles and using it is undefined behaviour; GCC's -Wreturn-local-addr, on by default, reports it. Return the " +
            "value itself, or use static or dynamically allocated storage.",
            "Return the value itself, or allocate the memory with malloc or new and have the caller free it.",
            """
            char *copy = malloc(strlen(text) + 1);
            strcpy(copy, text);
            return copy;
            """,
            why: "The caller reads memory that is reused for something else, so values change or the program crashes."),

        Gcc(@"division by zero",
            "Dividing a whole number by zero has no answer. The number divided by here is written as zero, so the division " +
            "fails every time it runs - usually the program is killed.",
            "The code divides by zero.",
            "Integer division or remainder by zero is undefined behaviour; on x86 it raises a divide error, delivered as SIGFPE " +
            "on POSIX systems and as exception 0xC0000094 on Windows. -Wdiv-by-zero, on by default, reports a constant zero " +
            "divisor.",
            "Divide by the value you meant, and check it is not zero first.",
            """
            int average = count ? total / count : 0;
            """,
            why: "The program crashes when the line runs."),

        Gcc(@"array subscript .* (?:is )?(?:above|outside) array bounds",
            "An array of n items has places 0 to n - 1. This index goes past the end, so the program reads or writes memory " +
            "that belongs to something else - and C does nothing to stop it.",
            "The index is past the end of the array. An array of n items has indexes 0 to n - 1.",
            "Indexing outside an array - other than forming the pointer just past its end - is undefined behaviour, and C and " +
            "C++ check no bounds. -Warray-bounds reports indexes GCC's analysis proves out of range, which needs optimisation " +
            "to see most of them.",
            "Use < n in the loop condition.",
            """
            for (int i = 0; i < 10; i++) values[i] = 0;
            """,
            why: "Writing past the end overwrites other variables, and reading past it gives garbage."),

        Msvc(["C4996"],
            "Functions like scanf and strcpy write as many characters as they are given, without checking they fit. " +
            "Microsoft's compiler warns about them, because a long input can overflow the space and overwrite other memory.",
            "The function is one MSVC considers unsafe, such as scanf or strcpy, because it does not check the size of what it writes.",
            "C4996 reports a function declared deprecated; Microsoft's C runtime deprecates strcpy, scanf and others in favour " +
            "of bounds-checked _s versions, and _CRT_SECURE_NO_WARNINGS silences it. A width in the format, such as %99s, " +
            "bounds scanf portably.",
            "Give the size limit - scanf(\"%99s\", name) - or use the checked version it names.",
            """
            char name[100];
            scanf("%99s", name);
            """,
            why: "A longer input than the buffer overwrites memory next to it."),

        Msvc(["C4244", "C4267"],
            "Each type of number has a size and a kind: an int holds whole numbers, a double fractions, a size_t sizes. " +
            "Storing one in a smaller or different type can drop the fraction or the top part of the number, without any error.",
            "A value is stored in a smaller type, such as a double in an int, which can lose part of it.",
            "C4244 is an implicit conversion that may lose data, such as double to int, and C4267 a conversion from size_t - " +
            "64 bits in a 64-bit build - to a smaller type such as int; the fraction is truncated or the high bits discarded.",
            "Cast it if losing the fraction is intended, or keep it in the larger type.",
            """
            int rounded = (int)(average + 0.5);
            """,
            why: "The fraction or the high part of the number is silently thrown away."),

        Crash(@"heap-buffer-overflow|stack-buffer-overflow|global-buffer-overflow",
            "An array, or a block from malloc, has a fixed number of places. The program read or wrote past the end of one, " +
            "into memory that belongs to something else. A checker built into the program, AddressSanitizer, caught it the " +
            "moment it happened.",
            "The program reads or writes past the end of an array or a malloc'd block.",
            "AddressSanitizer surrounds heap, stack and global objects with poisoned redzones and checks every access against " +
            "shadow memory, so the first byte out of bounds is reported with the access's stack and where the object was " +
            "allocated. Without it the access is undefined behaviour that can corrupt data silently.",
            "Without the checker the program may carry on with corrupted memory, crashing later somewhere unrelated.",
            "Check every index against the size, and make sure strings have room for their closing '\\0'.",
            """
            char name[6];
            strncpy(name, "Hello", sizeof name);
            """),

        Crash(@"heap-use-after-free|use-after-free|double-free|attempting double-free",
            "free gives memory back so it can be used for something else. After that it is not yours any more - and this " +
            "program used it again, or freed it a second time. The checker caught it the moment it happened.",
            "Memory is used, or freed again, after it has already been freed.",
            "AddressSanitizer keeps freed blocks in a quarantine with their shadow memory poisoned, so a later access " +
            "(heap-use-after-free) or a second free (attempting double-free) is reported with the stacks of the allocation, " +
            "the free and the access. Both are undefined behaviour.",
            "The memory may already hold something else, so reads return garbage and writes corrupt other data.",
            "Set pointers to NULL after free, and free each block exactly once.",
            """
            free(node);
            node = NULL;
            """),

        Crash(@"SEGV|Segmentation fault|access violation|0xC0000005",
            "A pointer holds an address in memory. This one pointed somewhere the program is not allowed to touch - often NULL, " +
            "which is address 0, a pointer that was never set, or one past the end of an array - so the operating system " +
            "stopped the program.",
            "The program used a pointer that does not point to valid memory - often NULL, uninitialised, or past the end of an array.",
            "The access touched a page that is unmapped or protected, so the processor faulted and the operating system " +
            "delivered SIGSEGV on POSIX systems, or an access violation, 0xC0000005, on Windows. Using a null, uninitialised " +
            "or dangling pointer is undefined behaviour, and faults only when the address happens to be invalid.",
            "The program is killed at that moment, and it can happen only sometimes, depending on what is in memory.",
            "Initialise every pointer, check malloc's result and pointers for NULL before using them, and keep indexes in range.",
            """
            int *numbers = malloc(n * sizeof *numbers);
            if (numbers == NULL) return 1;
            """),

        Crash(@"out_of_range|vector::_M_range_check",
            "A vector's items are numbered from 0 to size() - 1. .at() checks the position it is given and refuses one outside " +
            "that range by throwing an exception, which stops the program unless something catches it.",
            ".at() was called with an index past the end of the container.",
            "std::vector::at, std::string::at and the like check the index and throw std::out_of_range; operator[] checks " +
            "nothing, and an index out of range with it is undefined behaviour instead. An exception nothing catches calls " +
            "std::terminate.",
            "The exception ends the program unless something catches it.",
            "Check the index against size() first.",
            """
            if (i < values.size()) std::cout << values.at(i) << '\n';
            """),

        Crash(@"divide.by.zero|Floating point exception|0xC0000094",
            "Dividing a whole number by zero has no answer, so the processor refuses and the operating system stops the " +
            "program. The number divided by was zero at this point.",
            "An integer is divided by zero.",
            "Integer division by zero is undefined behaviour. On x86 it raises a divide error, which POSIX systems deliver as " +
            "SIGFPE - 'Floating point exception', despite the name - and Windows as 0xC0000094, integer divide by zero. " +
            "Floating-point division by zero gives infinity or NaN instead.",
            "The program is killed at that line.",
            "Check the divisor is not zero before dividing.",
            """
            int average = count ? total / count : 0;
            """),
    ];
}
