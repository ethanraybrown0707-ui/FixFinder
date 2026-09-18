using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

internal static class GoGuides
{
    private const string NothingRuns = "go refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Entry(string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Entry(@"^undefined:",
            "The code uses a name Go cannot find - it is misspelt (capitals matter), declared in another block, or needs an import.",
            "Check the spelling against the declaration, or import the package it comes from.",
            """
            import "fmt"

            func main() {
                fmt.Println("hi")
            }
            """),

        Entry(@"declared and not used|declared but not used",
            "A variable is declared and never used, and Go treats that as an error.",
            "Use the variable, remove it, or assign the value to _ if you only need the call.",
            """
            total := sum(values)
            fmt.Println(total)
            """),

        Entry(@"imported and not used",
            "A package is imported and nothing in the file uses it, which Go treats as an error.",
            "Remove the import, or use the package.",
            """
            import "fmt"
            """),

        Entry(@"missing return",
            "The function promises to return a value, but a path through it reaches the end without a return.",
            "Add a return for the case none of the branches handled.",
            """
            func grade(score int) string {
                if score >= 50 {
                    return "pass"
                }
                return "fail"
            }
            """),

        Entry(@"cannot use .* as .* value|mismatched types|cannot convert",
            "A value of one type is used where Go needs another, and Go never converts between types by itself.",
            "Convert explicitly - float64(n), strconv.Itoa(n), strconv.Atoi(s) - or change the declared type.",
            """
            count := 3
            average := float64(total) / float64(count)
            """),

        Entry(@"^syntax error",
            "Go could not read the code here - often a brace on the next line, a missing comma in a list, or parentheses where Go uses none.",
            "Keep an opening brace on the same line as its if, for or func, and end every line of a multi-line list with a comma.",
            """
            if count > 0 {
                fmt.Println(count)
            }
            """),

        Entry(@"non-boolean condition|used as value",
            "The if or for condition is not a true-or-false value - often = where == was meant.",
            "Use == to compare.",
            """
            if count == 0 {
                fmt.Println("empty")
            }
            """),

        Entry(@"too many arguments|not enough arguments|too many return values|not enough return values",
            "A call or return gives a different number of values than the function declares.",
            "Match the number and order of values the function's signature lists.",
            """
            value, err := strconv.Atoi(text)
            """),

        Entry(@"redeclared in this block|no new variables on left side of :=",
            "The name is declared twice in the same block - := declares, = assigns.",
            "Use = to change a variable that already exists.",
            """
            count := 0
            count = 5
            """),

        Entry(@"index out of range",
            "The index is outside the slice or array. Indexes run from 0 to len - 1.",
            "Loop with range, or check the index against len first.",
            """
            for i, value := range values {
                fmt.Println(i, value)
            }
            """,
            why: "The program panics and stops at this line."),

        Entry(@"assignment to entry in nil map",
            "A map is declared but never made, so it cannot hold anything yet.",
            "Create it with make before adding to it.",
            """
            counts := make(map[string]int)
            counts["a"]++
            """,
            why: "The program panics and stops at this line."),

        Entry(@"nil pointer dereference|invalid memory address",
            "A pointer that is nil - never given anything to point to - is used as if it pointed to something.",
            "Create the value before using it, or check for nil first.",
            """
            if node != nil {
                fmt.Println(node.value)
            }
            """,
            why: "The program panics and stops at this line."),

        Entry(@"all goroutines are asleep|deadlock",
            "Every goroutine is waiting - usually on a channel nobody will ever send to or receive from, or a WaitGroup that never reaches zero.",
            "Make sure each receive has a sender, close channels the receiver ranges over, and pass WaitGroups by pointer.",
            """
            var wg sync.WaitGroup
            wg.Add(1)
            go work(&wg)
            wg.Wait()
            """,
            why: "The program stops with a fatal error instead of finishing."),

        Entry(@"integer divide by zero",
            "An integer is divided by zero.",
            "Check the divisor is not zero first.",
            """
            if count > 0 {
                average = total / count
            }
            """,
            why: "The program panics and stops at this line."),

        Entry(@"format %\w+ has arg|wrong type|Printf call has|Println call has possible Printf formatting directive",
            "The Printf format does not match the value given for it.",
            "Use the verb that matches the type - %d whole numbers, %f decimals, %s strings, %v anything.",
            """
            fmt.Printf("%s scored %d\n", name, score)
            """,
            why: "Go prints a %!d(string=...) marker in place of the value."),

        Entry(@"self-assignment|self-comparison",
            "A variable is assigned to, or compared with, itself.",
            "Use the other variable you meant.",
            """
            p.name = name
            """,
            why: "The line does nothing useful, so the value you meant to use is ignored."),

        Entry(@"unreachable code",
            "This code comes after a return or panic, so it can never run.",
            "Move it before the return, or remove it.",
            """
            fmt.Println("done")
            return total
            """,
            why: "Whatever it was meant to do never happens."),

        Entry(@"loop variable .* captured|range variable .* captured",
            "A goroutine or closure inside the loop uses the loop variable, which older Go versions share across every pass.",
            "Pass the variable in as an argument, or copy it inside the loop.",
            """
            for _, item := range items {
                item := item
                go process(item)
            }
            """,
            why: "Every goroutine may see the last item instead of its own."),
    ];
}
