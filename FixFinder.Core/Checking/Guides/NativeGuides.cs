using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// C and C++, from gcc's words and MSVC's codes, each explained for someone new to programming.
/// </summary>
internal static class NativeGuides
{
    private const string NothingRuns = "The compiler refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Gcc(
        string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry Msvc(
        string[] codes, string explanation, string fix, string example, string? why = null) => new()
    {
        Codes = codes,
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry Crash(string pattern, string explanation, string why, string fix, string example) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Gcc(@"^expected '?;'?",
            "C and C++ need a semicolon at the end of every statement and declaration, like a full stop at the end of a " +
            "sentence. One is missing - and the compiler often only notices at the start of the next line, so look at the end " +
            "of the line before.",
            "Put a semicolon at the end of the statement - usually the line before the one named.",
            """
            int total = 0;
            total += price;
            """),

        Msvc(["C2143", "C2146", "C2059", "C2061", "C1075", "C1004"],
            "The compiler reads code in a fixed shape: statements end with semicolons, and brackets and braces come in pairs. " +
            "Something here breaks that shape - a semicolon is missing, or a bracket or brace is missing or extra.",
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
            "Check the spelling of the call, and that the file defining the function is part of the build.",
            """
            int square(int n) { return n * n; }

            int main(void) { return square(3); }
            """),

        Msvc(["LNK2019", "LNK2001", "LNK1120"],
            "Building a program has two steps: compiling each file, then linking them into one program. Compiling worked " +
            "because the function is declared, but the linker could not find its code anywhere - the name may be misspelt, or " +
            "the file defining it is not part of the build.",
            "Check the spelling of the call, and that the file defining the function is part of the build.",
            """
            int square(int n) { return n * n; }
            """),

        Gcc(@"too (?:few|many) arguments to function",
            "A function lists the values it needs in its declaration. This call gives it more or fewer values than that list, " +
            "so the compiler stops.",
            "Pass exactly the arguments the declaration lists.",
            """
            int add(int a, int b);

            int sum = add(2, 3);
            """),

        Gcc(@"incompatible types|invalid conversion from|cannot convert|makes (?:pointer from integer|integer from pointer)",
            "C and C++ check that each value goes where its kind belongs. Here one kind is used where another is needed - " +
            "often a plain number where a pointer, an address in memory, goes, as when scanf is given count instead of &count.",
            "Pass the right kind of value - &value for a pointer, *pointer for the value - or convert it explicitly.",
            """
            int count = 0;
            scanf("%d", &count);
            """),

        Msvc(["C2440", "C2664", "C2446"],
            "The compiler checks that each value goes where its kind belongs, and this line uses one kind of value where a " +
            "different kind is needed - a number where an address goes, for example.",
            "Pass the right kind of value, or convert it explicitly.",
            """
            int count = 0;
            scanf("%d", &count);
            """),

        Gcc(@"expected declaration or statement at end of input|expected '}' at end of input",
            "Every { that opens a block needs a } to close it. One block was never closed, so the compiler reached the end of " +
            "the file still inside it.",
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
            "Check the header's name; standard headers use angle brackets, your own use quotes.",
            """
            #include <stdio.h>
            #include "list.h"
            """),

        Gcc(@"conflicting types for|redefinition of|redeclared as different kind",
            "Each function or variable must be described the same way wherever it is declared, and given its body or value " +
            "only once. Here the same name is declared in two different ways, or defined twice - often because a header is " +
            "included twice without a guard.",
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
            "Use a type that supports the operator, or write the operator for your type.",
            """
            std::cout << person.name << '\n';
            """),

        Gcc(@"request for member .* which is of (?:non-class|pointer) type|base operand of '->' has non-pointer type",
            "A dot reads a part of a struct or an object: p.x. When what you have is a pointer to one - its address - you use " +
            "an arrow instead, p->x, which follows the address first. This line uses the wrong one.",
            "Use -> through a pointer and . on an object.",
            """
            struct point *p = &origin;
            printf("%d\n", p->x);
            """),

        Gcc(@"control reaches end of non-void function|no return statement in function returning non-void",
            "A function that says it gives back a value has to give one back on every way through it. There is a way to reach " +
            "the end of this one without a return - and then the caller gets whatever junk happens to be left there.",
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
            "Use the conversion that matches the type - %d int, %ld long, %f double, %s char*, %c char - and give scanf addresses.",
            """
            double price = 2.5;
            printf("%.2f\n", price);
            """,
            why: "The value is read as the wrong type: printf prints nonsense and scanf writes to the wrong place, which can crash."),

        Msvc(["C4477", "C4473", "C4474"],
            "printf and scanf use codes in the text - %d, %f, %s - to know what kind of value to expect for each one. The code " +
            "here does not match the value given, or there are too few or too many values for the codes.",
            "Use the conversion that matches the type, and give scanf addresses.",
            """
            printf("%.2f\n", price);
            """,
            why: "The value is read as the wrong type, which prints nonsense or crashes."),

        Gcc(@"unused variable|set but not used|defined but not used",
            "The code makes a variable or a function and never uses it. Often it is left over from earlier code, or a sign " +
            "that a variable with a similar name is being used by mistake.",
            "Remove it, or use it where it was meant to be used.",
            """
            int total = sum(values, n);
            printf("%d\n", total);
            """,
            why: "It is often a sign the code uses another variable by mistake, and it clutters the code."),

        Gcc(@"suggest parentheses around assignment used as truth value",
            "One = stores a value; two == compare values. The condition here uses one =, so instead of checking the variable " +
            "it overwrites it, and the if goes the same way almost every time.",
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
            "Give it a starting value where it is declared.",
            """
            int total = 0;
            for (int i = 0; i < n; i++) total += values[i];
            """,
            why: "It holds whatever was in memory before, so the result is different from run to run."),

        Msvc(["C4700", "C4701", "C4703"],
            "A variable declared inside a function starts with whatever was in that memory before - leftover junk, not zero. " +
            "This one is read, on at least one way through the code, before anything has been stored in it.",
            "Give it a starting value where it is declared.",
            """
            int total = 0;
            """,
            why: "It holds whatever was in memory before, so the result changes from run to run."),

        Gcc(@"comparison of integer expressions of different signedness|comparison between signed and unsigned",
            "Unsigned numbers cannot be negative. When a signed number, which can be, is compared with an unsigned one, C and " +
            "C++ convert the signed one first - and -1 becomes a huge positive number, so the comparison comes out wrong.",
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
            "Use the same kind on both sides.",
            """
            for (size_t i = 0; i < names.size(); i++) { }
            """,
            why: "A negative number turns into a huge unsigned one, so the comparison gives the wrong answer."),

        Gcc(@"this '(?:if|for|while|else)' clause does not guard",
            "Without braces, an if or a loop controls only the one statement straight after it, however the lines are " +
            "indented. The next line here is indented as if it belongs, but it runs every time.",
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
            "Divide by the value you meant, and check it is not zero first.",
            """
            int average = count ? total / count : 0;
            """,
            why: "The program crashes when the line runs."),

        Gcc(@"array subscript .* (?:is )?(?:above|outside) array bounds",
            "An array of n items has places 0 to n - 1. This index goes past the end, so the program reads or writes memory " +
            "that belongs to something else - and C does nothing to stop it.",
            "Use < n in the loop condition.",
            """
            for (int i = 0; i < 10; i++) values[i] = 0;
            """,
            why: "Writing past the end overwrites other variables, and reading past it gives garbage."),

        Msvc(["C4996"],
            "Functions like scanf and strcpy write as many characters as they are given, without checking they fit. " +
            "Microsoft's compiler warns about them, because a long input can overflow the space and overwrite other memory.",
            "Give the size limit - scanf(\"%99s\", name) - or use the checked version it names.",
            """
            char name[100];
            scanf("%99s", name);
            """,
            why: "A longer input than the buffer overwrites memory next to it."),

        Msvc(["C4244", "C4267"],
            "Each type of number has a size and a kind: an int holds whole numbers, a double fractions, a size_t sizes. " +
            "Storing one in a smaller or different type can drop the fraction or the top part of the number, without any error.",
            "Cast it if losing the fraction is intended, or keep it in the larger type.",
            """
            int rounded = (int)(average + 0.5);
            """,
            why: "The fraction or the high part of the number is silently thrown away."),

        Crash(@"heap-buffer-overflow|stack-buffer-overflow|global-buffer-overflow",
            "An array, or a block from malloc, has a fixed number of places. The program read or wrote past the end of one, " +
            "into memory that belongs to something else. A checker built into the program, AddressSanitizer, caught it the " +
            "moment it happened.",
            "Without the checker the program may carry on with corrupted memory, crashing later somewhere unrelated.",
            "Check every index against the size, and make sure strings have room for their closing '\\0'.",
            """
            char name[6];
            strncpy(name, "Hello", sizeof name);
            """),

        Crash(@"heap-use-after-free|use-after-free|double-free|attempting double-free",
            "free gives memory back so it can be used for something else. After that it is not yours any more - and this " +
            "program used it again, or freed it a second time. The checker caught it the moment it happened.",
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
            "The program is killed at that moment, and it can happen only sometimes, depending on what is in memory.",
            "Initialise every pointer, check malloc's result and pointers for NULL before using them, and keep indexes in range.",
            """
            int *numbers = malloc(n * sizeof *numbers);
            if (numbers == NULL) return 1;
            """),

        Crash(@"out_of_range|vector::_M_range_check",
            "A vector's items are numbered from 0 to size() - 1. .at() checks the position it is given and refuses one outside " +
            "that range by throwing an exception, which stops the program unless something catches it.",
            "The exception ends the program unless something catches it.",
            "Check the index against size() first.",
            """
            if (i < values.size()) std::cout << values.at(i) << '\n';
            """),

        Crash(@"divide.by.zero|Floating point exception|0xC0000094",
            "Dividing a whole number by zero has no answer, so the processor refuses and the operating system stops the " +
            "program. The number divided by was zero at this point.",
            "The program is killed at that line.",
            "Check the divisor is not zero before dividing.",
            """
            int average = count ? total / count : 0;
            """),
    ];
}
