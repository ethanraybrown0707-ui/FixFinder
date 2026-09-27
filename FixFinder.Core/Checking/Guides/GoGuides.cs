using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Go's compile errors, vet's warnings and the runtime's panics, each explained three ways: for someone new to
/// programming, as it is usually taught, and in the language's own terms.
/// </summary>
internal static class GoGuides
{
    private const string NothingRuns = "go refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Entry(
        string pattern, string beginner, string explanation, string technical, string fix, string example, string? why = null) => new()
    {
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Entry(@"^undefined:",
            "Go needs every name declared before it is used - with var, :=, func or type, or by importing its package. This " +
            "name is not declared anywhere this line can see. Capitals matter in Go: Total and total are different names.",
            "The code uses a name Go cannot find - it is misspelt (capitals matter), declared in another block, or needs an import.",
            "The identifier resolves to no declaration in the enclosing blocks, the package or the universe block. A name from " +
            "another package must be qualified with the package's name, and only identifiers that start with a capital letter " +
            "are exported from it.",
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
            "A variable is declared and never used, and Go treats that as an error.",
            "The Go specification lets a compiler reject a variable declared inside a function body and never used, and the gc " +
            "compiler always does; the blank identifier _ discards a value without declaring anything. Package-level variables " +
            "and function parameters are exempt.",
            "Use the variable, remove it, or assign the value to _ if you only need the call.",
            """
            total := sum(values)
            fmt.Println(total)
            """),

        Entry(@"imported and not used",
            "Go treats an import that nothing in the file uses as a mistake, which keeps programs tidy and quick to build. " +
            "Remove the import, or use the package.",
            "A package is imported and nothing in the file uses it, which Go treats as an error.",
            "It is illegal to import a package without referring to any of its exported identifiers; a blank import, import _ " +
            "\"path\", imports a package only for the side effects of its initialisation.",
            "Remove the import, or use the package.",
            """
            import "fmt"
            """),

        Entry(@"missing return",
            "A function that says it gives back a value has to give one back on every way through it. There is a way to reach " +
            "the end of this one without a return.",
            "The function promises to return a value, but a path through it reaches the end without a return.",
            "A function with result parameters must end in a terminating statement: a return, a call to panic, a for with no " +
            "condition and no break, or an if with an else, or a switch with a default, whose every branch terminates. Go does " +
            "not look at conditions, so an if without an else never counts.",
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
            "A value of one type is used where Go needs another, and Go never converts between types by itself.",
            "Go has no implicit conversions: a value is assignable only to an identical type, an interface it implements, or - " +
            "for an untyped constant - a type that can represent it, and the operands of a binary operator must have identical " +
            "types. Conversions such as float64(n) are written out, and text-number conversions go through strconv.",
            "Convert explicitly - float64(n), strconv.Itoa(n), strconv.Atoi(s) - or change the declared type.",
            """
            count := 3
            average := float64(total) / float64(count)
            """),

        Entry(@"^syntax error",
            "Go could not read the code here. A common cause is putting { on the next line: Go adds an invisible semicolon at " +
            "the end of a line, so the brace must stay on the same line as its if, for or func. A list spread over several " +
            "lines also needs a comma after its last item.",
            "Go could not read the code here - often a brace on the next line, a missing comma in a list, or parentheses where Go uses none.",
            "The lexer inserts a semicolon after a line's last token when it is an identifier, a literal, one of break, " +
            "continue, fallthrough or return, or a closing ), ] or }. So a { on the next line leaves the if, for or func with " +
            "no body, and a composite literal over several lines needs a trailing comma.",
            "Keep an opening brace on the same line as its if, for or func, and end every line of a multi-line list with a comma.",
            """
            if count > 0 {
                fmt.Println(count)
            }
            """),

        Entry(@"non-boolean condition|used as value",
            "An if or a for needs a true-or-false question. = stores a value rather than asking anything, and Go does not " +
            "allow storing in a condition - so write == to compare.",
            "The if or for condition is not a true-or-false value - often = where == was meant.",
            "The condition of an if or for must have a boolean type, and in Go an assignment is a statement, not an expression, " +
            "so it cannot be a condition - though an if may start with a simple statement, as in if v := f(); v > 0.",
            "Use == to compare.",
            """
            if count == 0 {
                fmt.Println("empty")
            }
            """),

        Entry(@"too many arguments|not enough arguments|too many return values|not enough return values",
            "A function's declaration lists how many values it takes and how many it gives back. This call or return gives a " +
            "different number - and in Go a function can give back several values at once, such as a result and an error.",
            "A call or return gives a different number of values than the function declares.",
            "A call must supply exactly the declared parameters - a final ...T parameter takes any number - and a return must " +
            "supply every result, or none when the results are named. A call returning several values must be assigned to " +
            "that many variables.",
            "Match the number and order of values the function's signature lists.",
            """
            value, err := strconv.Atoi(text)
            """),

        Entry(@"redeclared in this block|no new variables on left side of :=",
            "In Go, := makes a new variable and = changes one that already exists. This line uses := for a name already made " +
            "in this block, so there is nothing new for it to make.",
            "The name is declared twice in the same block - := declares, = assigns.",
            "A short variable declaration must declare at least one new non-blank variable on its left, and merely assigns the " +
            "others already declared in the same block. Declaring one name twice in a block is an error, although an inner " +
            "block may shadow it.",
            "Use = to change a variable that already exists.",
            """
            count := 0
            count = 5
            """),

        Entry(@"index out of range",
            "A slice's items are numbered from 0 to len - 1. This index is outside that range, so Go stops the program right " +
            "there rather than read memory it should not.",
            "The index is outside the slice or array. Indexes run from 0 to len - 1.",
            "Every index into an array, slice or string is bounds-checked when it runs and must satisfy 0 <= i < len; " +
            "otherwise the runtime panics with 'index out of range [i] with length n'. A constant index out of range for an " +
            "array is a compile error instead.",
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
            "A map is declared but never made, so it cannot hold anything yet.",
            "A map type's zero value is nil, which reads as an empty map but panics on assignment; make(map[K]V) or a map " +
            "literal allocates one.",
            "Create it with make before adding to it.",
            """
            counts := make(map[string]int)
            counts["a"]++
            """,
            why: "The program panics and stops at this line."),

        Entry(@"nil pointer dereference|invalid memory address",
            "A pointer holds the address of a value, and nil means it holds no address at all. This line follows a nil pointer " +
            "to read or change the value, but there is nothing there.",
            "A pointer that is nil - never given anything to point to - is used as if it pointed to something.",
            "Dereferencing a nil pointer - directly, or through a field access or a method call on a nil struct pointer - " +
            "causes a run-time panic, 'invalid memory address or nil pointer dereference'. A method with a pointer receiver can " +
            "be called on nil only if it never dereferences it.",
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
            "Every goroutine is waiting - usually on a channel nobody will ever send to or receive from, or a WaitGroup that never reaches zero.",
            "The runtime found that no goroutine can proceed - each is blocked on a channel operation, a sync primitive or a " +
            "select, with nothing left that could wake it - and aborts with 'fatal error: all goroutines are asleep - " +
            "deadlock!'. It only detects the case where every goroutine is blocked.",
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
            "An integer is divided by zero.",
            "Integer division or remainder by zero causes a run-time panic, and a constant zero divisor is a compile error " +
            "instead. Floating-point division by zero follows IEEE 754 and gives an infinity or NaN.",
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
            "The Printf format does not match the value given for it.",
            "go vet's printf check compares each verb with its argument's type, and the number of verbs with the number of " +
            "arguments. At run time fmt does not fail: it prints a marker such as %!d(string=Ada) in place of the value.",
            "Use the verb that matches the type - %d whole numbers, %f decimals, %s strings, %v anything.",
            """
            fmt.Printf("%s scored %d\n", name, score)
            """,
            why: "Go prints a %!d(string=...) marker in place of the value."),

        Entry(@"self-assignment|self-comparison",
            "The line stores a variable's value back into the same variable, or compares it with itself, which does nothing. " +
            "Usually another variable with a similar name was meant.",
            "A variable is assigned to, or compared with, itself.",
            "go vet's check for useless assignments reports x = x, and a comparison of an expression with itself always gives " +
            "the same result - a floating-point NaN aside.",
            "Use the other variable you meant.",
            """
            p.name = name
            """,
            why: "The line does nothing useful, so the value you meant to use is ignored."),

        Entry(@"unreachable code",
            "return and panic end the function straight away, so code written after one of them can never run.",
            "This code comes after a return or panic, so it can never run.",
            "go vet's unreachable check reports statements after a return, a panic, an endless loop or another terminating " +
            "statement; the compiler itself accepts them.",
            "Move it before the return, or remove it.",
            """
            fmt.Println("done")
            return total
            """,
            why: "Whatever it was meant to do never happens."),

        Entry(@"loop variable .* captured|range variable .* captured",
            "A goroutine or a function made inside a loop uses the loop's variable. Before Go 1.22 the loop reused one variable " +
            "for every pass, so functions that ran later could all see its last value.",
            "A goroutine or closure inside the loop uses the loop variable, which older Go versions share across every pass.",
            "Before Go 1.22 a for loop declared its variables once and shared them across iterations, so closures and " +
            "goroutines capturing them saw later values; since 1.22, in modules that declare go 1.22 or later, each iteration " +
            "has its own. go vet's loopclosure check reports the capture.",
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
