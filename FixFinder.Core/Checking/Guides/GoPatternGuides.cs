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

        GuideFor(["logic-go-percent-divided-first"], "A percentage that divides first",
            "In Go, / of two whole numbers gives a whole number - the part below 1 is thrown away. In score / total * 100 the " +
            "division comes first, so for 45 out of 60 it is 0, and 0 * 100 is 0.",
            "Every score below the total comes out as 0%, and neither go build nor go vet says anything of it.",
            "Multiply first - score * 100 / total - so only the last division drops a fraction: 45 * 100 / 60 is 75.",
            """
            package main

            import "fmt"

            func main() {
            	score := 45
            	total := 60
            	fmt.Println(score * 100 / total)
            }
            """),

        GuideFor(["logic-go-pow-whole-number-fraction"], "A power of 1/2 that is 0",
            "In Go, / of two whole numbers gives a whole number, so 1/2 is 0. math.Pow(side, 1/2) is therefore side to the power 0, " +
            "which is 1 whatever side is - not its square root.",
            "The program works out 1 instead of the root, and neither go build nor go vet says anything of it.",
            "Write the top of the fraction as a decimal - math.Pow(side, 1.0/2) - or use math.Sqrt(side).",
            """
            package main

            import (
            	"fmt"
            	"math"
            )

            func main() {
            	fmt.Println(math.Sqrt(16))
            }
            """),

        GuideFor(["logic-go-octal-literal"], "A number written with a 0 in front",
            "In Go a whole number written with 0 in front is read in octal - base 8 - so 010 is 8, and 0123 is 83. It is meant for " +
            "file modes such as 0755, and rarely anything else.",
            "The number is not the one written, and neither go build nor go vet says anything of it.",
            "Write the number without the 0 in front: 10.",
            """
            package main

            import "fmt"

            func main() {
            	code := 10
            	fmt.Println(code)
            }
            """),

        GuideFor(["logic-go-plus-equals-reversed"], "=+ where += was meant",
            "total =+ mark is read as total = +mark: the = gives total a new value, and the + is only a plus sign in front of mark. So " +
            "total becomes mark itself, and what it held is lost. += adds to what is already there - and =- the same way gives total " +
            "-mark, where -= takes away.",
            "A running total ends as the last value given - 10, not 15, for 5 and 10 - and neither the compiler nor go vet says anything.",
            "Write += to add, or -= to take away: total += mark.",
            """
            package main

            import "fmt"

            func main() {
            	total := 0
            	for _, mark := range []int{5, 10} {
            		total += mark
            	}
            	fmt.Println(total)
            }
            """),

        GuideFor(["logic-go-odd-test-negative"], "An odd number looked for with % 2 == 1",
            "% gives what is left over after dividing, and it keeps the sign of the number divided: 7 % 2 is 1, but -7 % 2 is -1. So " +
            "n%2 == 1 is true only for odd numbers above 0 - a negative odd number leaves -1, and is taken for even.",
            "The program is right until it meets a negative odd number, which it treats as even, with nothing from go vet.",
            "Ask whether anything is left over: n%2 != 0 is true for every odd number, negative or not.",
            """
            package main

            import "fmt"

            func main() {
            	n := -3
            	if n%2 != 0 {
            		fmt.Println("odd")
            	}
            }
            """),

        GuideFor(["logic-go-date-layout-letters"], "A date layout written with letters",
            "Go does not write a date from letters like YYYY-MM-DD. Its layout is the way one moment - 2 January 2006 at 15:04:05 - " +
            "would be written: 2006 is the year, 01 the month, 02 the day, 15 the hour, 04 the minutes and 05 the seconds. Letters are " +
            "not part of it, so they are printed as they are: Format(\"YYYY-MM-DD\") prints YYYY-MM-DD.",
            "Every date prints as the same letters, with no error and nothing from go vet.",
            "Write the layout as that moment would be written: when.Format(\"2006-01-02\").",
            """
            package main

            import (
            	"fmt"
            	"time"
            )

            func main() {
            	when := time.Date(2024, 2, 5, 9, 30, 0, 0, time.UTC)
            	fmt.Println(when.Format("2006-01-02"))
            }
            """),

        GuideFor(["logic-go-sleep-nanoseconds"], "time.Sleep given a bare number",
            "time.Sleep takes a time.Duration, and a number on its own is that many nanoseconds - billionths of a second. So " +
            "time.Sleep(2) waits two nanoseconds, which is no wait at all.",
            "The program does not pause where it was meant to, and go vet says nothing.",
            "Say the unit: time.Sleep(2 * time.Second), or time.Sleep(500 * time.Millisecond).",
            """
            package main

            import (
            	"fmt"
            	"time"
            )

            func main() {
            	time.Sleep(10 * time.Millisecond)
            	fmt.Println("done")
            }
            """),

        GuideFor(["logic-go-copy-into-empty"], "copy into a slice of length 0",
            "copy copies only as many items as both slices have room for - the shorter length. A slice made with make([]int, 0), or " +
            "[]int{}, has length 0, so copy puts nothing in it.",
            "The copy stays empty, with no error and nothing from go vet.",
            "Make the slice as long as what is copied: backup := make([]int, len(marks)) - or append: backup := append([]int(nil), marks...).",
            """
            package main

            import "fmt"

            func main() {
            	marks := []int{70, 80, 90}
            	backup := make([]int, len(marks))
            	copy(backup, marks)
            	fmt.Println(backup)
            }
            """),

        GuideFor(["logic-go-byte-printed"], "One character of a string printed as a number",
            "name[0] is a byte - the number that stands for the character - not a piece of text. fmt.Println prints numbers as numbers, " +
            "so \"hello\"[0] prints 104.",
            "The program prints numbers where it meant letters, with nothing from go vet.",
            "Turn it back into text: string(name[0]) - or print it with %c in Printf.",
            """
            package main

            import "fmt"

            func main() {
            	name := "hello"
            	fmt.Println(string(name[0]))
            }
            """),

        GuideFor(["logic-go-goroutine-not-waited"], "A goroutine main does not wait for",
            "A program ends when main returns - goroutines still running are stopped with it. A go statement starts the goroutine and " +
            "carries straight on, so if main has nothing left to do, it ends before the goroutine gets to run.",
            "The goroutine's work may never happen - its line was never printed - and go vet says nothing.",
            "Wait for it: a sync.WaitGroup, with wg.Add(1) before go, wg.Done() when it finishes, and wg.Wait() in main.",
            """
            package main

            import (
            	"fmt"
            	"sync"
            )

            func main() {
            	var wg sync.WaitGroup
            	wg.Add(1)
            	go func() {
            		defer wg.Done()
            		fmt.Println("from the goroutine")
            	}()
            	wg.Wait()
            }
            """),
    ];
}
