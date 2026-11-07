using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// The guides for the checks read in every language's own words - a largest started at 0, a total reset inside its loop, the
/// other end counted one too far, a changed copy thrown away - each with its example written in that language, as a reader of
/// that language would write it.
/// </summary>
internal static class EveryLanguagePatternGuides
{
    private static GuideEntry ResultDropped(string id, string example) =>
        GuideFor([id], "A changed copy thrown away",
            "Some functions do not change the value they are given: they work out a changed copy and give it back. Called on a line " +
            "of its own, the copy goes nowhere - the value is still what it was, and the lines after it read the old one.",
            "The program carries on with the value unchanged - \"ada\" where \"ADA\" was meant - and nothing says so.",
            "Keep what the function gives back, in the name it was made from.",
            example);

    private static GuideEntry LoopNeverAdvances(string id, string example) =>
        GuideFor([id], "A loop that never ends",
            "A loop that counts its way through a list keeps going while the counter is below the end, so something in the loop has " +
            "to move the counter on each pass. Nothing here does: the counter keeps the value it started with, the condition that " +
            "was true on the first pass is true on every pass, and the loop never stops.",
            "The program hangs - it never prints its answer and never finishes, and has to be stopped.",
            "Move the counter on at the end of each pass.",
            example);

    private static GuideEntry SwapLosesValue(string id, string example) =>
        GuideFor([id], "A swap that loses one of its two values",
            "To swap two values, one of them has to be kept somewhere first. Written as two lines - a = b and then b = a - the first " +
            "line gives a what b holds, so by the second line a holds b's value too, and b is given its own value back. What a held " +
            "before is gone.",
            "Nothing is swapped: both places end up holding the same value, and a sort that swaps this way loses some items and " +
            "repeats others, with no error to say so.",
            "Keep the first value in a name of its own before it is changed, or swap the two in one step, as the language allows.",
            example);

    private static GuideEntry RecursionNeverStops(string id, string example) =>
        GuideFor([id], "Recursion that never reaches where it stops, or stops with the wrong value",
            "A function that calls itself needs a case where it stops - if n <= 1 - and each call has to bring n closer to it. A call " +
            "with n + 1 moves away from a stop at 1, a call with n as it is never moves, and a call that takes 2 off n each time goes " +
            "past n == 0 when n is odd. A product such as a factorial also has to stop with 1: n times what the next call gives back, " +
            "with the stop giving back 0, makes every answer 0.",
            "A function that never stops calls itself until the language runs out of room for the calls - RecursionError in Python, " +
            "StackOverflowError in Java, a crash in C. One that stops with 0 gives back 0 for every n, with no error to say so.",
            "Make each call move n towards the stop, stop at the number or past it, and give back 1 where a product stops.",
            example);

    private static GuideEntry WrongList(string id, string example) =>
        GuideFor([id], "A loop that goes through a different list from the one its items are named for",
            "When a program has two lists that go together - widths and heights - and a loop for each, the second loop is often " +
            "written by copying the first and renaming its item. Here the item was renamed and the list it goes through was not: " +
            "each item is called height, the program has a list of heights, and the loop still goes through the widths.",
            "Whatever the loop works out comes from the wrong list - a total of 7 where it should be 11 - with no error to say so.",
            "Go through the list the loop's items are named for.",
            example);

    private static GuideEntry BinarySearch(string id, string example) =>
        GuideFor([id], "A binary search that can stop moving, or read past the end",
            "A binary search looks at the middle of the part still to search, then keeps the half the item can be in. The middle is " +
            "rounded down, so when two positions are left it is the first of them: setting the low end to the middle leaves it where " +
            "it was, and the same pass runs for ever - as it does for a high end set to the middle once the two ends meet, and for a " +
            "search that calls itself again from the middle. A high end started at the list's length is one past its last position, " +
            "and a loop that runs while the ends are equal then reads the list there. Where whole numbers stop at 2147483647, adding " +
            "the two ends before halving them can go past that for a very long list.",
            "For an item that is not in the list the program hangs, runs out of stack, or reads past the end of the list.",
            "Move past the middle - low = mid + 1, high = mid - 1 - and keep where the high end starts, how it moves and the loop's " +
            "condition in step: hi = length with hi = mid and lo < hi, or hi = length - 1 with hi = mid - 1 and lo <= hi.",
            example);

    private static GuideEntry WrongValueUsed(string id, string example) =>
        GuideFor([id], "A value worked out, and another used in its place",
            "A program often works one value out from another - an average from a total. Here the average is worked out and then " +
            "never used: the line after it gives back, or prints, the total it came from, though the function's name, or the " +
            "words printed beside it, say it is the average.",
            "The program gives the total where the average was meant - 210 where 70 is right - with no error to say so.",
            "Give back, or print, the value that was worked out for it.",
            example);

    private static GuideEntry CopiedLine(string id, string example) =>
        GuideFor([id], "A copied line that still reads what the line above read",
            "When two lines do the same thing for two different values - one for the width, one for the height - the second is " +
            "often written by copying the first and changing its name. Here the name was changed and what it reads was not: the " +
            "height is given the width, so both hold the same value.",
            "Anything worked out from the second value is wrong - an area of 9 where it should be 12 - with no error to say so.",
            "Change what the copied line reads to match its own name.",
            example);

    private static GuideEntry BestStartsAtZero(string id, string list, string example) =>
        GuideFor([id], "A largest or smallest started at 0",
            $"A loop that keeps the largest item of {list} starts with some value to compare the first item against. Started at 0, it " +
            "only works when some item is above 0: with temperatures of -5, -2 and -8 no item is bigger than 0, so it stays 0 - a " +
            "temperature that was never there. A smallest started at 0 stays 0 the same way when every item is above 0.",
            "The program prints or uses a value the list does not have, with no error to say so.",
            "Start it at the first item, so it is one of the list's values from the start.",
            example);

    private static GuideEntry ResetInLoop(string id, string example) =>
        GuideFor([id], "A total reset inside its own loop",
            "To add things up, you set the total to 0 once and then add each item to it. Here the total is set back to its start " +
            "at the beginning of every pass, which throws away everything added so far - so at the end it holds only the last " +
            "item's part.",
            "After the loop it only holds the last item's contribution, not the sum of all of them.",
            "Set the starting value once, before the loop.",
            example);

    private static GuideEntry MirrorIndex(string id, string example) =>
        GuideFor([id], "The other end counted one too far",
            "Positions in a list or text run from 0 to one less than its length - \"level\" has positions 0 to 4. The position " +
            "matching i from the other end is length - 1 - i, the last one when i is 0. length - i is one further: on the first pass " +
            "it asks for position 5 of \"level\", which is not there, and every pass after it compares the wrong two letters.",
            "A palindrome check says \"level\" is not one, or the program stops with an index error, depending on the language.",
            "Count from the other end with length - 1 - i.",
            example);

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        ResultDropped("logic-c-result-discarded",
            """
            char letter = 'a';
            letter = toupper(letter);
            printf("%c\n", letter);
            """),
        ResultDropped("logic-php-result-discarded",
            """
            $name = "ada";
            $name = strtoupper($name);
            echo $name, PHP_EOL;
            """),
        ResultDropped("logic-lua-result-discarded",
            """
            local name = "ada"
            name = string.upper(name)
            print(name)
            """),
        ResultDropped("logic-perl-result-discarded",
            """
            my $name = "ada";
            $name = uc($name);
            print "$name\n";
            """),
        ResultDropped("logic-rust-result-discarded",
            """
            let name = String::from("ada");
            let name = name.to_uppercase();
            println!("{}", name);
            """),
        MirrorIndex("logic-mirror-index",
            """
            for (int i = 0; i < length; i++) {
                if (word[i] != word[length - 1 - i]) {
                    same = false;
                }
            }
            """),
        MirrorIndex("logic-python-mirror-index",
            """
            for i in range(len(word)):
                if word[i] != word[len(word) - 1 - i]:
                    same = False
            """),
        MirrorIndex("logic-go-mirror-index",
            """
            for i := 0; i < len(word); i++ {
                if word[i] != word[len(word)-1-i] {
                    same = false
                }
            }
            """),
        MirrorIndex("logic-rust-mirror-index",
            """
            for i in 0..word.len() {
                if word[i] != word[word.len() - 1 - i] {
                    same = false;
                }
            }
            """),
        MirrorIndex("logic-ruby-mirror-index",
            """
            (0...word.length).each do |i|
              same = false if word[i] != word[word.length - 1 - i]
            end
            """),
        MirrorIndex("logic-php-mirror-index",
            """
            for ($i = 0; $i < strlen($word); $i++) {
                if ($word[$i] != $word[strlen($word) - 1 - $i]) {
                    $same = false;
                }
            }
            """),
        MirrorIndex("logic-perl-mirror-index",
            """
            for my $i (0 .. length($word) - 1) {
                if (substr($word, $i, 1) ne substr($word, length($word) - 1 - $i, 1)) {
                    $same = 0;
                }
            }
            """),
        MirrorIndex("logic-dart-mirror-index",
            """
            for (var i = 0; i < word.length; i++) {
              if (word[i] != word[word.length - 1 - i]) {
                same = false;
              }
            }
            """),
        MirrorIndex("logic-powershell-mirror-index",
            """
            for ($i = 0; $i -lt $word.Length; $i++) {
                if ($word[$i] -ne $word[$word.Length - 1 - $i]) {
                    $same = $false
                }
            }
            """),
        MirrorIndex("logic-fsharp-mirror-index",
            """
            for i in 0 .. word.Length - 1 do
                if word.[i] <> word.[word.Length - 1 - i] then
                    same <- false
            """),
        MirrorIndex("logic-scala-mirror-index",
            """
            for i <- 0 until word.length do
              if word(i) != word(word.length - 1 - i) then same = false
            """),
        MirrorIndex("logic-elixir-mirror-index",
            """
            same = Enum.all?(0..(String.length(word) - 1), fn i ->
              String.at(word, i) == String.at(word, String.length(word) - 1 - i)
            end)
            """),
        ResetInLoop("logic-go-reset-in-loop",
            """
            total := 0
            for _, mark := range marks {
                total += mark
            }
            """),
        ResetInLoop("logic-rust-reset-in-loop",
            """
            let mut total = 0;
            for mark in &marks {
                total += mark;
            }
            """),
        ResetInLoop("logic-php-reset-in-loop",
            """
            $total = 0;
            foreach ($marks as $mark) {
                $total += $mark;
            }
            """),
        ResetInLoop("logic-perl-reset-in-loop",
            """
            my $total = 0;
            foreach my $mark (@marks) {
                $total += $mark;
            }
            """),
        ResetInLoop("logic-dart-reset-in-loop",
            """
            var total = 0;
            for (var mark in marks) {
              total += mark;
            }
            """),
        ResetInLoop("logic-powershell-reset-in-loop",
            """
            $total = 0
            foreach ($mark in $marks) {
                $total += $mark
            }
            """),
        ResetInLoop("logic-scala-reset-in-loop",
            """
            var total = 0
            for mark <- marks do
              total += mark
            """),
        ResetInLoop("logic-ruby-reset-in-loop",
            """
            total = 0
            marks.each do |mark|
              total += mark
            end
            """),
        ResetInLoop("logic-lua-reset-in-loop",
            """
            local total = 0
            for _, mark in ipairs(marks) do
              total = total + mark
            end
            """),
        ResetInLoop("logic-fortran-reset-in-loop",
            """
            total = 0
            do i = 1, size(scores)
              total = total + scores(i)
            end do
            """),
        ResetInLoop("logic-fsharp-reset-in-loop",
            """
            let mutable total = 0
            for mark in marks do
                total <- total + mark
            """),
        BestStartsAtZero("logic-best-starts-at-zero", "an array",
            """
            int highest = temperatures[0];
            for (int i = 0; i < count; i++) {
                if (temperatures[i] > highest) {
                    highest = temperatures[i];
                }
            }
            """),
        BestStartsAtZero("logic-go-best-starts-at-zero", "a slice",
            """
            highest := temperatures[0]
            for _, t := range temperatures {
                if t > highest {
                    highest = t
                }
            }
            """),
        BestStartsAtZero("logic-rust-best-starts-at-zero", "a vector",
            """
            let mut highest = temperatures[0];
            for t in &temperatures {
                if *t > highest {
                    highest = *t;
                }
            }
            """),
        BestStartsAtZero("logic-ruby-best-starts-at-zero", "an array",
            """
            highest = temperatures[0]
            temperatures.each do |t|
              highest = t if t > highest
            end
            """),
        BestStartsAtZero("logic-php-best-starts-at-zero", "an array",
            """
            $highest = $temperatures[0];
            foreach ($temperatures as $t) {
                if ($t > $highest) {
                    $highest = $t;
                }
            }
            """),
        BestStartsAtZero("logic-lua-best-starts-at-zero", "a table",
            """
            local highest = temperatures[1]
            for _, t in ipairs(temperatures) do
              if t > highest then
                highest = t
              end
            end
            """),
        BestStartsAtZero("logic-perl-best-starts-at-zero", "an array",
            """
            my $highest = $temperatures[0];
            foreach my $t (@temperatures) {
                if ($t > $highest) {
                    $highest = $t;
                }
            }
            """),
        BestStartsAtZero("logic-dart-best-starts-at-zero", "a list",
            """
            var highest = temperatures[0];
            for (var t in temperatures) {
              if (t > highest) {
                highest = t;
              }
            }
            """),
        BestStartsAtZero("logic-fortran-best-starts-at-zero", "an array",
            """
            highest = temperatures(1)
            do i = 1, size(temperatures)
              if (temperatures(i) > highest) then
                highest = temperatures(i)
              end if
            end do
            """),
        BestStartsAtZero("logic-powershell-best-starts-at-zero", "an array",
            """
            $highest = $temperatures[0]
            foreach ($t in $temperatures) {
                if ($t -gt $highest) {
                    $highest = $t
                }
            }
            """),
        BestStartsAtZero("logic-fsharp-best-starts-at-zero", "a list",
            """
            let mutable highest = temperatures.[0]
            for t in temperatures do
                if t > highest then
                    highest <- t
            """),
        BestStartsAtZero("logic-scala-best-starts-at-zero", "a list",
            """
            var highest = temperatures(0)
            for t <- temperatures do
              if t > highest then highest = t
            """),
        BestStartsAtZero("logic-elixir-best-starts-at-zero", "a list",
            """
            highest = Enum.reduce(temperatures, hd(temperatures), fn t, best -> if t > best, do: t, else: best end)
            """),
        BestStartsAtZero("logic-erlang-best-starts-at-zero", "a list",
            """
            Highest = lists:foldl(fun(T, Best) when T > Best -> T; (_, Best) -> Best end, hd(Temperatures), Temperatures).
            """),
        CopiedLine("logic-python-copied-line",
            """
            self.width = width
            self.height = height
            """),
        CopiedLine("logic-java-copied-line",
            """
            this.width = width;
            this.height = height;
            """),
        CopiedLine("logic-copied-line",
            """
            int width = b.width;
            int height = b.height;
            """),
        CopiedLine("logic-go-copied-line",
            """
            package main

            import "fmt"

            type box struct{ width, height int }

            func main() {
            	b := box{width: 3, height: 4}
            	width := b.width
            	height := b.height
            	fmt.Println(width * height)
            }
            """),
        CopiedLine("logic-rust-copied-line",
            """
            let width = r.width;
            let height = r.height;
            """),
        CopiedLine("logic-ruby-copied-line",
            """
            width = box[:width]
            height = box[:height]
            """),
        CopiedLine("logic-php-copied-line",
            """
            $width = $box['width'];
            $height = $box['height'];
            """),
        CopiedLine("logic-lua-copied-line",
            """
            local width = box.width
            local height = box.height
            """),
        CopiedLine("logic-perl-copied-line",
            """
            my $width = $box{width};
            my $height = $box{height};
            """),
        CopiedLine("logic-dart-copied-line",
            """
            final width = box['width']!;
            final height = box['height']!;
            """),
        CopiedLine("logic-fortran-copied-line",
            """
            width = b%width
            height = b%height
            """),
        CopiedLine("logic-powershell-copied-line",
            """
            $box = @{ Width = 3; Height = 4 }
            $width = $box.Width
            $height = $box.Height
            Write-Output ($width * $height)
            """),
        CopiedLine("logic-fsharp-copied-line",
            """
            let width = box.Width
            let height = box.Height
            """),
        CopiedLine("logic-scala-copied-line",
            """
            val width = box.width
            val height = box.height
            """),
        CopiedLine("logic-elixir-copied-line",
            """
            width = box.width
            height = box.height
            """),
        CopiedLine("logic-erlang-copied-line",
            """
            Width = maps:get(width, Box),
            Height = maps:get(height, Box),
            """),
        WrongValueUsed("logic-python-wrong-value-used",
            """
            def average(marks):
                total = sum(marks)
                return total / len(marks)
            """),
        WrongValueUsed("logic-java-wrong-value-used",
            """
            static int average(int[] marks) {
                int total = 0;
                for (int mark : marks) {
                    total += mark;
                }
                return total / marks.length;
            }
            """),
        WrongValueUsed("logic-wrong-value-used",
            """
            int average(const int marks[], int count) {
                int total = 0;
                for (int i = 0; i < count; i++) {
                    total += marks[i];
                }
                return total / count;
            }
            """),
        WrongValueUsed("logic-rust-wrong-value-used",
            """
            fn average(marks: &[i32]) -> i32 {
                let total: i32 = marks.iter().sum();
                total / marks.len() as i32
            }
            """),
        WrongValueUsed("logic-ruby-wrong-value-used",
            """
            def average(marks)
              marks.sum / marks.length
            end
            """),
        WrongValueUsed("logic-php-wrong-value-used",
            """
            function average($marks) {
                return intdiv(array_sum($marks), count($marks));
            }
            """),
        WrongValueUsed("logic-lua-wrong-value-used",
            """
            local function average(marks)
              local total = 0
              for _, mark in ipairs(marks) do
                total = total + mark
              end
              return total // #marks
            end
            """),
        WrongValueUsed("logic-perl-wrong-value-used",
            """
            sub average {
                my @marks = @_;
                my $total = 0;
                $total += $_ for @marks;
                return $total / scalar(@marks);
            }
            """),
        WrongValueUsed("logic-dart-wrong-value-used",
            """
            int average(List<int> marks) {
              var total = 0;
              for (final mark in marks) {
                total += mark;
              }
              return total ~/ marks.length;
            }
            """),
        WrongValueUsed("logic-fortran-wrong-value-used",
            """
            total = sum(marks)
            average = total / size(marks)
            print *, 'Average:', average
            """),
        WrongValueUsed("logic-powershell-wrong-value-used",
            """
            function Get-Average($marks) {
                $total = ($marks | Measure-Object -Sum).Sum
                return $total / $marks.Count
            }

            Write-Output (Get-Average @(50, 70, 90))
            """),
        WrongValueUsed("logic-fsharp-wrong-value-used",
            """
            let average (marks: int list) =
                let total = List.sum marks
                total / List.length marks
            """),
        WrongValueUsed("logic-scala-wrong-value-used",
            """
            def average(marks: List[Int]): Int = {
              val total = marks.sum
              total / marks.length
            }
            """),
        WrongValueUsed("logic-elixir-wrong-value-used",
            """
            def average(marks) do
              total = Enum.sum(marks)
              div(total, length(marks))
            end
            """),
        BinarySearch("logic-python-binary-search",
            """
            lo = 0
            hi = len(arr) - 1
            while lo <= hi:
                mid = (lo + hi) // 2
                if arr[mid] == x:
                    return mid
                if arr[mid] < x:
                    lo = mid + 1
                else:
                    hi = mid - 1
            """),
        BinarySearch("logic-java-binary-search",
            """
            int lo = 0;
            int hi = arr.length - 1;
            while (lo <= hi) {
                int mid = lo + (hi - lo) / 2;
                if (arr[mid] == x) {
                    return mid;
                } else if (arr[mid] < x) {
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }
            """),
        BinarySearch("logic-binary-search",
            """
            int lo = 0;
            int hi = count - 1;
            while (lo <= hi) {
                int mid = lo + (hi - lo) / 2;
                if (arr[mid] == x) {
                    return mid;
                } else if (arr[mid] < x) {
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }
            """),
        BinarySearch("logic-go-binary-search",
            """
            package main

            import "fmt"

            func find(arr []int, x int) int {
            	lo, hi := 0, len(arr)-1
            	for lo <= hi {
            		mid := lo + (hi-lo)/2
            		if arr[mid] == x {
            			return mid
            		} else if arr[mid] < x {
            			lo = mid + 1
            		} else {
            			hi = mid - 1
            		}
            	}
            	return -1
            }

            func main() {
            	fmt.Println(find([]int{1, 2, 3}, 3))
            }
            """),
        BinarySearch("logic-rust-binary-search",
            """
            let mut lo = 0;
            let mut hi = arr.len();
            while lo < hi {
                let mid = lo + (hi - lo) / 2;
                if arr[mid] < x {
                    lo = mid + 1;
                } else {
                    hi = mid;
                }
            }
            """),
        BinarySearch("logic-ruby-binary-search",
            """
            lo = 0
            hi = arr.length - 1
            while lo <= hi
              mid = (lo + hi) / 2
              return mid if arr[mid] == x
              if arr[mid] < x
                lo = mid + 1
              else
                hi = mid - 1
              end
            end
            """),
        BinarySearch("logic-php-binary-search",
            """
            $lo = 0;
            $hi = count($arr) - 1;
            while ($lo <= $hi) {
                $mid = intdiv($lo + $hi, 2);
                if ($arr[$mid] == $x) {
                    return $mid;
                } elseif ($arr[$mid] < $x) {
                    $lo = $mid + 1;
                } else {
                    $hi = $mid - 1;
                }
            }
            """),
        BinarySearch("logic-lua-binary-search",
            """
            local lo, hi = 1, #arr
            while lo <= hi do
              local mid = (lo + hi) // 2
              if arr[mid] == x then
                return mid
              elseif arr[mid] < x then
                lo = mid + 1
              else
                hi = mid - 1
              end
            end
            """),
        BinarySearch("logic-perl-binary-search",
            """
            my ($lo, $hi) = (0, $#arr);
            while ($lo <= $hi) {
                my $mid = int(($lo + $hi) / 2);
                return $mid if $arr[$mid] == $x;
                if ($arr[$mid] < $x) {
                    $lo = $mid + 1;
                } else {
                    $hi = $mid - 1;
                }
            }
            """),
        BinarySearch("logic-dart-binary-search",
            """
            var lo = 0;
            var hi = arr.length - 1;
            while (lo <= hi) {
              final mid = (lo + hi) ~/ 2;
              if (arr[mid] == x) {
                return mid;
              } else if (arr[mid] < x) {
                lo = mid + 1;
              } else {
                hi = mid - 1;
              }
            }
            """),
        BinarySearch("logic-scala-binary-search",
            """
            var lo = 0
            var hi = arr.length - 1
            while (lo <= hi) {
              val mid = lo + (hi - lo) / 2
              if (arr(mid) == x) return mid
              else if (arr(mid) < x) lo = mid + 1
              else hi = mid - 1
            }
            """),
        BinarySearch("logic-powershell-binary-search",
            """
            function Find($arr, $x) {
                $lo = 0
                $hi = $arr.Count - 1
                while ($lo -le $hi) {
                    $mid = [math]::Floor(($lo + $hi) / 2)
                    if ($arr[$mid] -eq $x) { return $mid }
                    if ($arr[$mid] -lt $x) { $lo = $mid + 1 } else { $hi = $mid - 1 }
                }
                return -1
            }

            Write-Output (Find @(1, 2, 3) 3)
            """),
        BinarySearch("logic-fsharp-binary-search",
            """
            let mutable lo = 0
            let mutable hi = arr.Length - 1
            while lo < hi do
                let mid = (lo + hi) / 2
                if arr.[mid] < x then
                    lo <- mid + 1
                else
                    hi <- mid
            """),
        BinarySearch("logic-elixir-binary-search",
            """
            def search(arr, x, lo, hi) do
              if lo > hi do
                -1
              else
                mid = div(lo + hi, 2)
                at = Enum.at(arr, mid)
                cond do
                  x < at -> search(arr, x, lo, mid - 1)
                  x > at -> search(arr, x, mid + 1, hi)
                  true -> mid
                end
              end
            end
            """),
        BinarySearch("logic-erlang-binary-search",
            """
            search(_, _, Lo, Hi) when Lo > Hi -> -1;
            search(Arr, X, Lo, Hi) ->
                Mid = (Lo + Hi) div 2,
                At = lists:nth(Mid + 1, Arr),
                if
                    X < At -> search(Arr, X, Lo, Mid - 1);
                    X > At -> search(Arr, X, Mid + 1, Hi);
                    true -> Mid
                end.
            """),
        WrongList("logic-python-wrong-list",
            """
            for width in widths:
                total_width += width
            for height in heights:
                total_height += height
            """),
        WrongList("logic-java-wrong-list",
            """
            for (int width : widths) {
                totalWidth += width;
            }
            for (int height : heights) {
                totalHeight += height;
            }
            """),
        WrongList("logic-wrong-list",
            """
            for (const width of widths) {
                totalWidth += width;
            }
            for (const height of heights) {
                totalHeight += height;
            }
            """),
        WrongList("logic-go-wrong-list",
            """
            package main

            import "fmt"

            func main() {
            	widths := []int{3, 4}
            	heights := []int{5, 6}
            	totalWidth, totalHeight := 0, 0
            	for _, width := range widths {
            		totalWidth += width
            	}
            	for _, height := range heights {
            		totalHeight += height
            	}
            	fmt.Println(totalWidth, totalHeight)
            }
            """),
        WrongList("logic-rust-wrong-list",
            """
            for width in &widths {
                total_width += width;
            }
            for height in &heights {
                total_height += height;
            }
            """),
        WrongList("logic-ruby-wrong-list",
            """
            widths.each do |width|
              total_width += width
            end
            heights.each do |height|
              total_height += height
            end
            """),
        WrongList("logic-php-wrong-list",
            """
            foreach ($widths as $width) {
                $totalWidth += $width;
            }
            foreach ($heights as $height) {
                $totalHeight += $height;
            }
            """),
        WrongList("logic-lua-wrong-list",
            """
            for _, width in ipairs(widths) do
              total_width = total_width + width
            end
            for _, height in ipairs(heights) do
              total_height = total_height + height
            end
            """),
        WrongList("logic-perl-wrong-list",
            """
            foreach my $width (@widths) {
                $total_width += $width;
            }
            foreach my $height (@heights) {
                $total_height += $height;
            }
            """),
        WrongList("logic-dart-wrong-list",
            """
            for (final width in widths) {
              totalWidth += width;
            }
            for (final height in heights) {
              totalHeight += height;
            }
            """),
        WrongList("logic-scala-wrong-list",
            """
            for (width <- widths) {
              totalWidth += width
            }
            for (height <- heights) {
              totalHeight += height
            }
            """),
        WrongList("logic-powershell-wrong-list",
            """
            $widths = @(3, 4)
            $heights = @(5, 6)
            $totalWidth = 0
            $totalHeight = 0
            foreach ($width in $widths) {
                $totalWidth += $width
            }
            foreach ($height in $heights) {
                $totalHeight += $height
            }
            Write-Output "$totalWidth $totalHeight"
            """),
        WrongList("logic-fsharp-wrong-list",
            """
            for width in widths do
                totalWidth <- totalWidth + width
            for height in heights do
                totalHeight <- totalHeight + height
            """),
        WrongList("logic-elixir-wrong-list",
            """
            total_width = Enum.reduce(widths, 0, fn width, sum -> sum + width end)
            total_height = Enum.reduce(heights, 0, fn height, sum -> sum + height end)
            """),
        LoopNeverAdvances("logic-ruby-loop-never-advances",
            """
            i = 0
            while i < marks.length
              total += marks[i]
              i += 1
            end
            """),
        LoopNeverAdvances("logic-php-loop-never-advances",
            """
            $i = 0;
            while ($i < count($marks)) {
                $total += $marks[$i];
                $i++;
            }
            """),
        LoopNeverAdvances("logic-lua-loop-never-advances",
            """
            local i = 1
            while i <= #marks do
              total = total + marks[i]
              i = i + 1
            end
            """),
        LoopNeverAdvances("logic-go-loop-never-advances",
            """
            package main

            import "fmt"

            func main() {
            	marks := []int{50, 60, 70}
            	i := 0
            	total := 0
            	for i < len(marks) {
            		total += marks[i]
            		i++
            	}
            	fmt.Println(total)
            }
            """),
        LoopNeverAdvances("logic-rust-loop-never-advances",
            """
            let mut i = 0;
            while i < marks.len() {
                total += marks[i];
                i += 1;
            }
            """),
        LoopNeverAdvances("logic-powershell-loop-never-advances",
            """
            $marks = @(50, 60, 70)
            $i = 0
            $total = 0
            while ($i -lt $marks.Count) {
                $total += $marks[$i]
                $i++
            }
            Write-Output $total
            """),
        LoopNeverAdvances("logic-perl-loop-never-advances",
            """
            my $i = 0;
            while ($i < @marks) {
                $total += $marks[$i];
                $i++;
            }
            """),
        LoopNeverAdvances("logic-dart-loop-never-advances",
            """
            var i = 0;
            while (i < marks.length) {
              total += marks[i];
              i++;
            }
            """),
        LoopNeverAdvances("logic-scala-loop-never-advances",
            """
            var i = 0
            while (i < marks.length) {
              total += marks(i)
              i += 1
            }
            """),
        LoopNeverAdvances("logic-fsharp-loop-never-advances",
            """
            let mutable i = 0
            while i < marks.Length do
                total <- total + marks.[i]
                i <- i + 1
            """),
        LoopNeverAdvances("logic-fortran-loop-never-advances",
            """
            i = 1
            do while (i <= size(marks))
              total = total + marks(i)
              i = i + 1
            end do
            """),
        SwapLosesValue("logic-python-swap-loses-value",
            """
            marks[i], marks[j] = marks[j], marks[i]
            """),
        SwapLosesValue("logic-ruby-swap-loses-value",
            """
            marks[i], marks[j] = marks[j], marks[i]
            """),
        SwapLosesValue("logic-lua-swap-loses-value",
            """
            marks[i], marks[j] = marks[j], marks[i]
            """),
        SwapLosesValue("logic-go-swap-loses-value",
            """
            package main

            import "fmt"

            func main() {
            	marks := []int{55, 40}
            	marks[0], marks[1] = marks[1], marks[0]
            	fmt.Println(marks)
            }
            """),
        SwapLosesValue("logic-powershell-swap-loses-value",
            """
            $marks = @(55, 40)
            $marks[0], $marks[1] = $marks[1], $marks[0]
            Write-Output $marks
            """),
        SwapLosesValue("logic-php-swap-loses-value",
            """
            [$marks[$i], $marks[$j]] = [$marks[$j], $marks[$i]];
            """),
        SwapLosesValue("logic-perl-swap-loses-value",
            """
            ($marks[$i], $marks[$j]) = ($marks[$j], $marks[$i]);
            """),
        SwapLosesValue("logic-js-swap-loses-value",
            """
            [marks[i], marks[j]] = [marks[j], marks[i]];
            """),
        SwapLosesValue("logic-csharp-swap-loses-value",
            """
            (marks[i], marks[j]) = (marks[j], marks[i]);
            """),
        SwapLosesValue("logic-java-swap-loses-value",
            """
            int held = marks[i];
            marks[i] = marks[j];
            marks[j] = held;
            """),
        SwapLosesValue("logic-cpp-swap-loses-value",
            """
            auto held = marks[i];
            marks[i] = marks[j];
            marks[j] = held;
            """),
        SwapLosesValue("logic-c-swap-loses-value",
            """
            int held = marks[i];
            marks[i] = marks[j];
            marks[j] = held;
            """),
        SwapLosesValue("logic-dart-swap-loses-value",
            """
            final held = marks[i];
            marks[i] = marks[j];
            marks[j] = held;
            """),
        SwapLosesValue("logic-rust-swap-loses-value",
            """
            marks.swap(i, j);
            """),
        SwapLosesValue("logic-scala-swap-loses-value",
            """
            val held = marks(i)
            marks(i) = marks(j)
            marks(j) = held
            """),
        RecursionNeverStops("logic-python-recursion-never-stops",
            """
            def factorial(n):
                if n <= 1:
                    return 1
                return n * factorial(n - 1)
            """),
        RecursionNeverStops("logic-ruby-recursion-never-stops",
            """
            def factorial(n)
              return 1 if n <= 1
              n * factorial(n - 1)
            end
            """),
        RecursionNeverStops("logic-lua-recursion-never-stops",
            """
            local function factorial(n)
              if n <= 1 then
                return 1
              end
              return n * factorial(n - 1)
            end
            """),
        RecursionNeverStops("logic-go-recursion-never-stops",
            """
            package main

            import "fmt"

            func factorial(n int) int {
            	if n <= 1 {
            		return 1
            	}
            	return n * factorial(n-1)
            }

            func main() {
            	fmt.Println(factorial(5))
            }
            """),
        RecursionNeverStops("logic-rust-recursion-never-stops",
            """
            fn factorial(n: u64) -> u64 {
                if n <= 1 {
                    return 1;
                }
                n * factorial(n - 1)
            }
            """),
        RecursionNeverStops("logic-js-recursion-never-stops",
            """
            function factorial(n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            """),
        RecursionNeverStops("logic-java-recursion-never-stops",
            """
            static long factorial(int n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            """),
        RecursionNeverStops("logic-csharp-recursion-never-stops",
            """
            static long Factorial(int n)
            {
                if (n <= 1)
                {
                    return 1;
                }
                return n * Factorial(n - 1);
            }
            """),
        RecursionNeverStops("logic-c-recursion-never-stops",
            """
            long factorial(int n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            """),
        RecursionNeverStops("logic-dart-recursion-never-stops",
            """
            int factorial(int n) {
              if (n <= 1) {
                return 1;
              }
              return n * factorial(n - 1);
            }
            """),
        RecursionNeverStops("logic-php-recursion-never-stops",
            """
            function factorial(int $n): int
            {
                if ($n <= 1) {
                    return 1;
                }
                return $n * factorial($n - 1);
            }
            """),
        RecursionNeverStops("logic-perl-recursion-never-stops",
            """
            sub factorial {
                my ($n) = @_;
                return 1 if $n <= 1;
                return $n * factorial($n - 1);
            }
            """),
        RecursionNeverStops("logic-powershell-recursion-never-stops",
            """
            function Get-Factorial($n) {
                if ($n -le 1) {
                    return 1
                }
                return $n * (Get-Factorial ($n - 1))
            }

            Write-Output (Get-Factorial 5)
            """),
        RecursionNeverStops("logic-scala-recursion-never-stops",
            """
            def factorial(n: Int): Long = {
              if (n <= 1) {
                return 1
              }
              n * factorial(n - 1)
            }
            """),
    ];
}
