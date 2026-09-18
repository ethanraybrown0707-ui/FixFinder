using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>C and C++, from gcc's words and MSVC's codes.</summary>
internal static class NativeGuides
{
    private const string NothingRuns = "The compiler refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Gcc(string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry Msvc(string[] codes, string explanation, string fix, string example, string? why = null) => new()
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
            "A statement is missing its semicolon, or a declaration before it is not finished.",
            "Put a semicolon at the end of the statement - usually the line before the one named.",
            """
            int total = 0;
            total += price;
            """),

        Msvc(["C2143", "C2146", "C2059", "C2061", "C1075", "C1004"],
            "The compiler could not read the code here: a semicolon, bracket or brace is missing or extra.",
            "Check the line named and the one before it for a missing semicolon or unbalanced brace.",
            """
            if (score > 50) {
                printf("pass\n");
            }
            """),

        Gcc(@"undeclared|was not declared in this scope|use of undeclared identifier",
            "The code uses a name the compiler has not seen declared at this point - it is misspelt, declared later or inside another block, " +
            "or needs a header.",
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
            "The code uses a name the compiler has not seen declared at this point - it is misspelt, declared later, or needs a header.",
            "Check the spelling against the declaration, declare it before this line, or include the header that declares it.",
            """
            #include <stdio.h>

            int count = 3;
            printf("%d\n", count);
            """),

        Gcc(@"implicit declaration of function",
            "A function is called before anything declares it - its header is missing, or its prototype comes later in the file.",
            "Include the header that declares it, or add a prototype above the first call.",
            """
            #include <stdlib.h>

            int *numbers = malloc(10 * sizeof *numbers);
            """,
            why: "C then guesses the function returns int; on 64-bit Windows a pointer squeezed through that guess is cut in half and the program crashes."),

        Gcc(@"undefined reference to|unresolved external symbol",
            "The code calls a function that is declared but never defined in any file the program is built from - often a misspelt name.",
            "Check the spelling of the call, and that the file defining the function is part of the build.",
            """
            int square(int n) { return n * n; }

            int main(void) { return square(3); }
            """),

        Msvc(["LNK2019", "LNK2001", "LNK1120"],
            "The code calls a function that is declared but never defined in any file the program is built from - often a misspelt name.",
            "Check the spelling of the call, and that the file defining the function is part of the build.",
            """
            int square(int n) { return n * n; }
            """),

        Gcc(@"too (?:few|many) arguments to function",
            "The function is called with a different number of arguments than its declaration has parameters.",
            "Pass exactly the arguments the declaration lists.",
            """
            int add(int a, int b);

            int sum = add(2, 3);
            """),

        Gcc(@"incompatible types|invalid conversion from|cannot convert|makes (?:pointer from integer|integer from pointer)",
            "A value of one type is used where a different, incompatible type is needed - such as a number where a pointer goes.",
            "Pass the right kind of value - &value for a pointer, *pointer for the value - or convert it explicitly.",
            """
            int count = 0;
            scanf("%d", &count);
            """),

        Msvc(["C2440", "C2664", "C2446"],
            "A value of one type is used where a different, incompatible type is needed.",
            "Pass the right kind of value, or convert it explicitly.",
            """
            int count = 0;
            scanf("%d", &count);
            """),

        Gcc(@"expected declaration or statement at end of input|expected '}' at end of input",
            "A block is opened with { and never closed, so the compiler reaches the end of the file still inside it.",
            "Add the missing } where the block ends.",
            """
            int main(void) {
                printf("hi\n");
                return 0;
            }
            """),

        Gcc(@"No such file or directory|file not found",
            "An #include names a header that cannot be found - it is misspelt, or it is not part of this compiler's library.",
            "Check the header's name; standard headers use angle brackets, your own use quotes.",
            """
            #include <stdio.h>
            #include "list.h"
            """),

        Gcc(@"conflicting types for|redefinition of|redeclared as different kind",
            "The same name is declared twice in different ways, or defined twice.",
            "Make every declaration match the definition, and define each thing once - use a header guard in headers.",
            """
            #ifndef LIST_H
            #define LIST_H
            struct node { int value; struct node *next; };
            #endif
            """),

        Gcc(@"no match for 'operator|invalid operands",
            "The operator is used with types that do not support it, such as << with a type that has no output operator.",
            "Use a type that supports the operator, or write the operator for your type.",
            """
            std::cout << person.name << '\n';
            """),

        Gcc(@"request for member .* which is of (?:non-class|pointer) type|base operand of '->' has non-pointer type",
            "A member is accessed with . on a pointer, or with -> on an object.",
            "Use -> through a pointer and . on an object.",
            """
            struct point *p = &origin;
            printf("%d\n", p->x);
            """),

        // ------------------------------------------------------------------ warnings
        Gcc(@"control reaches end of non-void function|no return statement in function returning non-void",
            "The function promises to return a value, but a path through it reaches the end without a return.",
            "Add a return for the case none of the branches handled.",
            """
            int grade(int score) {
                if (score >= 50) return 1;
                return 0;
            }
            """,
            why: "The caller gets whatever happens to be left in a register - a different wrong number on different machines."),

        Msvc(["C4715", "C4716"],
            "The function promises to return a value, but a path through it reaches the end without a return.",
            "Add a return for the case none of the branches handled.",
            """
            int grade(int score) {
                if (score >= 50) return 1;
                return 0;
            }
            """,
            why: "The caller gets an unpredictable value."),

        Gcc(@"format '%\w+' expects|format specifies type",
            "The printf or scanf format does not match the type of the value given for it.",
            "Use the conversion that matches the type - %d int, %ld long, %f double, %s char*, %c char - and give scanf addresses.",
            """
            double price = 2.5;
            printf("%.2f\n", price);
            """,
            why: "The value is read as the wrong type: printf prints nonsense and scanf writes to the wrong place, which can crash."),

        Msvc(["C4477", "C4473", "C4474"],
            "The printf or scanf format does not match the type of the value given for it.",
            "Use the conversion that matches the type, and give scanf addresses.",
            """
            printf("%.2f\n", price);
            """,
            why: "The value is read as the wrong type, which prints nonsense or crashes."),

        Gcc(@"unused variable|set but not used|defined but not used",
            "A variable or function is declared and never used.",
            "Remove it, or use it where it was meant to be used.",
            """
            int total = sum(values, n);
            printf("%d\n", total);
            """,
            why: "It is often a sign the code uses another variable by mistake, and it clutters the code."),

        Gcc(@"suggest parentheses around assignment used as truth value",
            "The condition uses = (assignment) where == (comparison) was probably meant.",
            "Use == to compare.",
            """
            if (count == 0) {
                printf("empty\n");
            }
            """,
            why: "The condition assigns instead of checking, so the if takes the same branch every time and the variable is overwritten."),

        Gcc(@"is used uninitialized|may be used uninitialized",
            "The variable is read before anything has stored a value in it.",
            "Give it a starting value where it is declared.",
            """
            int total = 0;
            for (int i = 0; i < n; i++) total += values[i];
            """,
            why: "It holds whatever was in memory before, so the result is different from run to run."),

        Msvc(["C4700", "C4701", "C4703"],
            "The variable is read before anything has stored a value in it.",
            "Give it a starting value where it is declared.",
            """
            int total = 0;
            """,
            why: "It holds whatever was in memory before, so the result changes from run to run."),

        Gcc(@"comparison of integer expressions of different signedness|comparison between signed and unsigned",
            "A signed number is compared with an unsigned one, such as an int against a size().",
            "Use the same kind on both sides - size_t for a loop over a container's size.",
            """
            for (size_t i = 0; i < names.size(); i++) {
                std::cout << names[i] << '\n';
            }
            """,
            why: "A negative number turns into a huge unsigned one, so the comparison gives the wrong answer."),

        Msvc(["C4018", "C4389"],
            "A signed number is compared with an unsigned one.",
            "Use the same kind on both sides.",
            """
            for (size_t i = 0; i < names.size(); i++) { }
            """,
            why: "A negative number turns into a huge unsigned one, so the comparison gives the wrong answer."),

        Gcc(@"this '(?:if|for|while|else)' clause does not guard",
            "The indentation makes a line look as if it belongs to the if or loop above, but without braces only the first line does.",
            "Put braces around every line that belongs to the if or loop.",
            """
            if (error) {
                printf("failed\n");
                return 1;
            }
            """,
            why: "The second line runs every time, which is easy to miss because the indentation says otherwise."),

        Gcc(@"suggest braces around empty body|empty body",
            "There is a semicolon straight after the if, while or for, which makes its body empty.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                printf("pass\n");
            }
            """,
            why: "The block below runs every time, whatever the condition."),

        Gcc(@"this statement may fall through",
            "This case has no break, so the program carries straight on into the next case.",
            "End the case with break.",
            """
            case 1:
                printf("one\n");
                break;
            """,
            why: "Choosing one option also runs the next."),

        Gcc(@"comparison with string literal",
            "Strings are compared with ==, which compares where they are in memory, not the letters in them.",
            "Compare C strings with strcmp(a, b) == 0; compare std::string with ==.",
            """
            if (strcmp(answer, "yes") == 0) {
                printf("ok\n");
            }
            """,
            why: "Two strings with the same letters are usually at different addresses, so the check fails."),

        Gcc(@"address of local variable|reference to local variable",
            "The function returns the address of a variable that stops existing when the function returns.",
            "Return the value itself, or allocate the memory with malloc or new and have the caller free it.",
            """
            char *copy = malloc(strlen(text) + 1);
            strcpy(copy, text);
            return copy;
            """,
            why: "The caller reads memory that is reused for something else, so values change or the program crashes."),

        Gcc(@"division by zero",
            "The code divides by zero.",
            "Divide by the value you meant, and check it is not zero first.",
            """
            int average = count ? total / count : 0;
            """,
            why: "The program crashes when the line runs."),

        Gcc(@"array subscript .* (?:is )?(?:above|outside) array bounds",
            "The index is past the end of the array. An array of n items has indexes 0 to n - 1.",
            "Use < n in the loop condition.",
            """
            for (int i = 0; i < 10; i++) values[i] = 0;
            """,
            why: "Writing past the end overwrites other variables, and reading past it gives garbage."),

        Msvc(["C4996"],
            "The function is one MSVC considers unsafe, such as scanf or strcpy, because it does not check the size of what it writes.",
            "Give the size limit - scanf(\"%99s\", name) - or use the checked version it names.",
            """
            char name[100];
            scanf("%99s", name);
            """,
            why: "A longer input than the buffer overwrites memory next to it."),

        Msvc(["C4244", "C4267"],
            "A value is stored in a smaller type, such as a double in an int, which can lose part of it.",
            "Cast it if losing the fraction is intended, or keep it in the larger type.",
            """
            int rounded = (int)(average + 0.5);
            """,
            why: "The fraction or the high part of the number is silently thrown away."),

        // ------------------------------------------------------------------ crashes
        Crash(@"heap-buffer-overflow|stack-buffer-overflow|global-buffer-overflow",
            "The program reads or writes past the end of an array or a malloc'd block.",
            "Without the checker the program may carry on with corrupted memory, crashing later somewhere unrelated.",
            "Check every index against the size, and make sure strings have room for their closing '\\0'.",
            """
            char name[6];
            strncpy(name, "Hello", sizeof name);
            """),

        Crash(@"heap-use-after-free|use-after-free|double-free|attempting double-free",
            "Memory is used, or freed again, after it has already been freed.",
            "The memory may already hold something else, so reads return garbage and writes corrupt other data.",
            "Set pointers to NULL after free, and free each block exactly once.",
            """
            free(node);
            node = NULL;
            """),

        Crash(@"SEGV|Segmentation fault|access violation|0xC0000005",
            "The program used a pointer that does not point to valid memory - often NULL, uninitialised, or past the end of an array.",
            "The program is killed at that moment, and it can happen only sometimes, depending on what is in memory.",
            "Initialise every pointer, check malloc's result and pointers for NULL before using them, and keep indexes in range.",
            """
            int *numbers = malloc(n * sizeof *numbers);
            if (numbers == NULL) return 1;
            """),

        Crash(@"out_of_range|vector::_M_range_check",
            ".at() was called with an index past the end of the container.",
            "The exception ends the program unless something catches it.",
            "Check the index against size() first.",
            """
            if (i < values.size()) std::cout << values.at(i) << '\n';
            """),

        Crash(@"divide.by.zero|Floating point exception|0xC0000094",
            "An integer is divided by zero.",
            "The program is killed at that line.",
            "Check the divisor is not zero before dividing.",
            """
            int average = count ? total / count : 0;
            """),
    ];
}
