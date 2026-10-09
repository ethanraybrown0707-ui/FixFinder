using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Go's compile errors, vet's warnings and the runtime's panics, each explained for someone new to programming.
/// </summary>
internal static class GoGuides
{
    private const string NothingRuns = "go refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Entry(
        string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Entry(@"^undefined:",
            "Go needs every name declared before it is used - with var, :=, func or type, or by importing its package. This " +
            "name is not declared anywhere this line can see. Capitals matter in Go: Total and total are different names.",
            "Check the spelling against the declaration, or import the package it comes from.",
            """
            import "fmt"

            func main() {
                fmt.Println("hi")
            }
            """),

        Entry(@"declared and not used|declared but not used",
            "Go does not allow a variable that is declared inside a function and never used - it treats it as a mistake to " +
            "fix. Use the variable, remove it, or use _ if you only wanted to call something.",
            "Use the variable, remove it, or assign the value to _ if you only need the call.",
            """
            total := sum(values)
            fmt.Println(total)
            """),

        Entry(@"imported and not used",
            "Go treats an import that nothing in the file uses as a mistake, which keeps programs tidy and quick to build. " +
            "Remove the import, or use the package.",
            "Remove the import, or use the package.",
            """
            import "fmt"
            """),

        Entry(@"missing return",
            "A function that says it gives back a value has to give one back on every way through it. There is a way to reach " +
            "the end of this one without a return.",
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
            "Go checks that each value goes where its kind belongs, and never converts one kind to another by itself - not " +
            "even an int to a float64. Here one kind is used where another is needed, so you have to convert it yourself.",
            "Convert explicitly - float64(n), strconv.Itoa(n), strconv.Atoi(s) - or change the declared type.",
            """
            count := 3
            average := float64(total) / float64(count)
            """),

        Entry(@"^syntax error",
            "Go could not read the code here. A common cause is putting { on the next line: Go adds an invisible semicolon at " +
            "the end of a line, so the brace must stay on the same line as its if, for or func. A list spread over several " +
            "lines also needs a comma after its last item.",
            "Keep an opening brace on the same line as its if, for or func, and end every line of a multi-line list with a comma.",
            """
            if count > 0 {
                fmt.Println(count)
            }
            """),

        Entry(@"non-boolean condition|used as value",
            "An if or a for needs a true-or-false question. = stores a value rather than asking anything, and Go does not " +
            "allow storing in a condition - so write == to compare.",
            "Use == to compare.",
            """
            if count == 0 {
                fmt.Println("empty")
            }
            """),

        Entry(@"too many arguments|not enough arguments|too many return values|not enough return values",
            "A function's declaration lists how many values it takes and how many it gives back. This call or return gives a " +
            "different number - and in Go a function can give back several values at once, such as a result and an error.",
            "Match the number and order of values the function's signature lists.",
            """
            value, err := strconv.Atoi(text)
            """),

        Entry(@"redeclared in this block|no new variables on left side of :=",
            "In Go, := makes a new variable and = changes one that already exists. This line uses := for a name already made " +
            "in this block, so there is nothing new for it to make.",
            "Use = to change a variable that already exists.",
            """
            count := 0
            count = 5
            """),

        Entry(@"index out of range",
            "A slice's items are numbered from 0 to len - 1. This index is outside that range, so Go stops the program right " +
            "there rather than read memory it should not.",
            "Loop with range, or check the index against len first.",
            """
            for i, value := range values {
                fmt.Println(i, value)
            }
            """,
            why: "The program panics and stops at this line."),

        Entry(@"assignment to entry in nil map",
            "A map has to be made before it can hold anything. Declaring it with var only gives a name that points at no map " +
            "yet - nil - so storing into it fails.",
            "Create it with make before adding to it.",
            """
            counts := make(map[string]int)
            counts["a"]++
            """,
            why: "The program panics and stops at this line."),

        Entry(@"nil pointer dereference|invalid memory address",
            "A pointer holds the address of a value, and nil means it holds no address at all. This line follows a nil pointer " +
            "to read or change the value, but there is nothing there.",
            "Create the value before using it, or check for nil first.",
            """
            if node != nil {
                fmt.Println(node.value)
            }
            """,
            why: "The program panics and stops at this line."),

        Entry(@"all goroutines are asleep|deadlock",
            "Goroutines are parts of the program running at the same time, and they can wait for each other through channels. " +
            "Here every one of them is waiting for another and none can go on, so Go stops the program instead of letting it " +
            "hang for ever.",
            "Make sure each receive has a sender, close channels the receiver ranges over, and pass WaitGroups by pointer.",
            """
            var wg sync.WaitGroup
            wg.Add(1)
            go work(&wg)
            wg.Wait()
            """,
            why: "The program stops with a fatal error instead of finishing."),

        Entry(@"integer divide by zero",
            "Dividing a whole number by zero has no answer, so Go stops the program. The number divided by was zero at this " +
            "point.",
            "Check the divisor is not zero first.",
            """
            if count > 0 {
                average = total / count
            }
            """,
            why: "The program panics and stops at this line."),

        Entry(@"format %\w+ has arg|wrong type|Printf call has|Println call has possible Printf formatting directive",
            "Printf fills in each %-code in its text with one of the values after it: %d for a whole number, %s for text, %v " +
            "for anything. The code here does not match the kind of value given for it.",
            "Use the verb that matches the type - %d whole numbers, %f decimals, %s strings, %v anything.",
            """
            fmt.Printf("%s scored %d\n", name, score)
            """,
            why: "Go prints a %!d(string=...) marker in place of the value."),

        Entry(@"self-assignment|self-comparison",
            "The line stores a variable's value back into the same variable, or compares it with itself, which does nothing. " +
            "Usually another variable with a similar name was meant.",
            "Use the other variable you meant.",
            """
            p.name = name
            """,
            why: "The line does nothing useful, so the value you meant to use is ignored."),

        Entry(@"unreachable code",
            "return and panic end the function straight away, so code written after one of them can never run.",
            "Move it before the return, or remove it.",
            """
            fmt.Println("done")
            return total
            """,
            why: "Whatever it was meant to do never happens."),

        Entry(@"loop variable .* captured|range variable .* captured",
            "A goroutine or a function made inside a loop uses the loop's variable. Before Go 1.22 the loop reused one variable " +
            "for every pass, so functions that ran later could all see its last value.",
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
