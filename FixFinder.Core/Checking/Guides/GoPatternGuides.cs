using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Go programs, each explained for someone new to programming. Every example is a whole
/// program, and GoLogicLiveTests builds each one and runs go vet on it, and fails on anything either says.
/// </summary>
internal static class GoPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-go-loop-to-length"], "A loop that runs one past the end",
            "The positions in a slice or an array run from 0 to one less than its length: a slice of 3 items has positions 0, 1 and " +
            "2. A loop that keeps going while i <= len(xs) takes one step more than that, and on its last step xs[i] asks for a " +
            "position that is not there.",
            "The last pass stops the program with a panic, index out of range - after the earlier passes have already run.",
            "Stop while i is still less than the length - i < len(xs) - or loop with range, which gives exactly the positions there are.",
            """
            package main

            import "fmt"

            func main() {
            	marks := []int{70, 80, 90}
            	for i := 0; i < len(marks); i++ {
            		fmt.Println(marks[i])
            	}
            }
            """),

        GuideFor(["logic-go-integer-average"], "A decimal that lost its fraction",
            "Dividing one int by another in Go gives an int: the part after the point is dropped, so 3 / 2 is 1. float64(...) " +
            "around the division comes too late - it turns the 1 into 1.0, as the fraction is already gone. Turning each number " +
            "into a float64 first makes the division a decimal one.",
            "Averages and percentages come out rounded down to a whole number, with no error to say so.",
            "Make each number a float64 before dividing: float64(total) / float64(count).",
            """
            package main

            import "fmt"

            func main() {
            	total := 3
            	count := 2
            	average := float64(total) / float64(count)
            	fmt.Println(average)
            }
            """),

        GuideFor(["logic-go-range-value-changed"], "A change made to a copy",
            "for i, mark := range marks gives mark a copy of each item in turn. Giving mark a new value changes the copy, not the " +
            "item in marks - so when nothing after uses mark, the line has no effect at all. marks[i] is the item itself.",
            "The slice is just as it was after the loop, so everything that uses it afterwards works with the old values.",
            "Give the new value to the item itself: marks[i] = mark + 5, naming the position in the loop if it has none.",
            """
            package main

            import "fmt"

            func main() {
            	marks := []int{70, 80, 90}
            	for i, mark := range marks {
            		marks[i] = mark + 5
            	}
            	fmt.Println(marks)
            }
            """),

        GuideFor(["logic-go-parse-error-ignored"], "A conversion whose error is thrown away",
            "strconv.Atoi turns text such as \"42\" into a number, and gives back two things: the number, and an error that says " +
            "when the text was not a number. Writing _ for the error throws it away. For text such as \"forty-two\" the number is " +
            "then 0, and the program carries on with it as if it had been typed.",
            "Text that is not a number is quietly treated as 0 - or false, for strconv.ParseBool - so the program gives wrong answers instead of saying what was wrong.",
            "Keep the error and check it: if err != nil, say what was wrong, and do not use the number.",
            """
            package main

            import (
            	"fmt"
            	"strconv"
            )

            func main() {
            	text := "forty-two"
            	age, err := strconv.Atoi(text)
            	if err != nil {
            		fmt.Println("not a whole number:", text)
            		return
            	}
            	fmt.Println(age + 1)
            }
            """),

        GuideFor(["logic-go-result-discarded"], "A changed copy that is thrown away",
            "A string in Go never changes once made. strings.ToUpper, strings.TrimSpace, strings.Replace and their like do not " +
            "change the text they are given - they give back a new, changed copy. A line that calls one and keeps nothing changes " +
            "nothing: the text is just as it was on the next line.",
            "The change never happens, so the lines after it go on using the text as it was.",
            "Keep the copy: give it back to the variable - name = strings.ToUpper(name) - or to a new one.",
            """
            package main

            import (
            	"fmt"
            	"strings"
            )

            func main() {
            	name := "ada"
            	name = strings.ToUpper(name)
            	fmt.Println(name)
            }
            """),

        GuideFor(["logic-go-replace-none"], "strings.Replace with a count of 0",
            "strings.Replace's last argument is how many times to replace, counting from the start: 1 replaces the first match, -1 " +
            "every one - and 0 none at all.",
            "The text comes back exactly as it was, and neither Go nor go vet says anything of it.",
            "Use strings.ReplaceAll to replace every match, or give strings.Replace the count wanted.",
            """
            package main

            import (
            	"fmt"
            	"strings"
            )

            func main() {
            	fmt.Println(strings.ReplaceAll("a-b-c", "-", "+"))
            }
            """),

        GuideFor(["logic-go-caret-as-power"], "^ used as a power",
            "In maths side^2 means side squared, but in Go ^ is exclusive or: it compares two whole numbers bit by bit " +
            "and keeps the bits that differ. 5 is 101 in binary and 2 is 010, so 5 ^ 2 is 111 - which is 7, not 25.",
            "The program runs and works out a number - just the wrong one - and neither Go nor go vet says anything of it.",
            "Multiply the number by itself: side * side. For a decimal power, math.Pow works with float64s.",
            """
            package main

            import "fmt"

            func main() {
            	side := 5
            	fmt.Println(side * side)
            }
            """),

        GuideFor(["logic-go-input-not-trimmed"], "A line read with its newline still on it",
            "ReadString('\\n') reads up to the newline from pressing Enter, and gives back the line with that newline still on the end: " +
            "typing yes gives \"yes\\n\". Compared with \"yes\", it is not equal.",
            "The comparison is false whatever was typed, so the program takes the other branch every time.",
            "Trim the line as it is read: answer = strings.TrimSpace(answer).",
            """
            package main

            import (
            	"fmt"
            	"strings"
            )

            func main() {
            	answer := strings.TrimSpace("yes\n")
            	fmt.Println(answer == "yes")
            }
            """),

        GuideFor(["logic-go-range-index-as-value"], "range with one variable gives positions",
            "range over a slice gives two values each time round: the position, and the item at it. With one variable, that variable is " +
            "the position - 0, 1, 2 - not the item, so for mark := range marks makes mark a position.",
            "The loop works with positions where it means items, with no error: the total of 70, 80 and 90 comes out as 3.",
            "Take the item as range's second variable, and leave the position unused with _: for _, mark := range marks.",
            """
            package main

            import "fmt"

            func main() {
            	marks := []int{70, 80, 90}
            	total := 0
            	for _, mark := range marks {
            		total += mark
            	}
            	fmt.Println(total)
            }
            """),

        GuideFor(["logic-go-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so value == math.NaN() is false even when value is NaN. " +
            "Neither go build nor go vet says anything of it.",
            "A comparison with == is always false, and with != always true, so a value that is NaN is never noticed.",
            "Test with math.IsNaN(value).",
            """
            package main

            import (
            	"fmt"
            	"math"
            )

            func main() {
            	value := math.NaN()
            	if math.IsNaN(value) {
            		fmt.Println("not a number")
            	}
            }
            """),

        GuideFor(["logic-go-slice-shared"], "A second name for a slice is not a copy",
            "backup := marks does not copy the slice: both names share the one array of items beneath it. A change to " +
            "an item through either name - marks[0] = 100 - shows in the other. Neither go build nor go vet says " +
            "anything of it.",
            "The second name does not keep the values the slice had: after marks[0] = 100, backup prints [100 80 90].",
            "Make a copy: backup := append([]int{}, marks...) makes a new slice with the same items, and a change to " +
            "one no longer shows in the other.",
            """
            package main

            import "fmt"

            func main() {
            	marks := []int{70, 80, 90}
            	backup := append([]int{}, marks...)
            	marks[0] = 100
            	fmt.Println(backup)
            }
            """),
    ];
}
