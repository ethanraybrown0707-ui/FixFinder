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
    ];
}
