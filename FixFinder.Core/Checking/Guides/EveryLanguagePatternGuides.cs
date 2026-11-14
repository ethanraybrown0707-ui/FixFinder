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

    private static GuideEntry NewNodeLost(string id, string example) =>
        GuideFor([id], "A tree's insert that throws away the node it makes",
            "An insert that works by calling itself goes down the tree until it reaches an empty place, makes a new node there and " +
            "gives it back. The call one level up has to keep it - node.left = insert(node.left, value) - since an empty place " +
            "cannot be changed from inside the call it was given to. Written as insert(node.left, value) on its own, the new node is " +
            "made and lost.",
            "Nothing below the first node is ever added: a tree of 50, 30, 70, 20 and 40 prints only 50, with no error to say so.",
            "Give the field what the call gives back: node.left = insert(node.left, value).",
            example);

    private static GuideEntry VisitedNeverAdded(string id, string example) =>
        GuideFor([id], "A set of places visited that is asked and never given anything",
            "A search keeps the places it has been in a set - visited - and asks it about each place it comes to, so that it goes " +
            "on from each place only once. That only works when each place is added to the set as the search comes to it. Here " +
            "nothing is ever added, so the set is always empty, and every place looks new each time the search reaches it.",
            "The search goes to the same places again and again. On a graph whose edges come back round - a cycle, or any " +
            "undirected graph - a search that calls itself can do so until the program runs out of room for its calls and stops " +
            "with an error, and a search going round a loop can go on for ever.",
            "Add each place to the set as the search comes to it: first thing in the function that searches from it, or straight " +
            "after the line that asks whether it is there.",
            example);

    private static GuideEntry HalvingNeverStops(string id, string example) =>
        GuideFor([id], "A list split in half that stops only when nothing is left",
            "A merge sort - or any function that splits what it is given in half and calls itself on each half - has to stop " +
            "before a half can be all of what it was given. The middle is the length halved and rounded down, so with one item " +
            "left the middle is 0, and the half from the middle on is that one item. When the function stops only for an empty " +
            "list, or an empty range, it calls itself with that one item again, and that call does the same.",
            "The calls never stop: the program runs until it has no room left for them and stops with an error, or runs on " +
            "without an answer. Halving any list that is not empty comes down to one item, so the function never finishes for any " +
            "of them.",
            "Stop at one item as well as none - a list of one item is in order already - or, for a range, at one place: lo >= hi " +
            "when both ends are in it, hi - lo <= 1 when hi is one past its end. Where a function is written as clauses, add one " +
            "for a list of one item.",
            example);

    private static GuideEntry PartitionDropsEqual(string id, string example) =>
        GuideFor([id], "A quicksort whose parts leave out what equals the pivot",
            "A quicksort takes one item, the pivot, and puts the other items into two parts - those below it and those above it - " +
            "sorts each part by calling itself, and puts the pivot back between them. When one part keeps the items below the " +
            "pivot, x < pivot, and the other the items above it, x > pivot, an item equal to the pivot goes into neither. Only the " +
            "pivot itself is put back, so any other item equal to it never is.",
            "A list that holds a value more than once does not come back sorted: the copies of a repeated value go missing - or, " +
            "where the sort writes its answer over the list, what was there before is left in their places.",
            "Keep the items equal to the pivot in one of the parts - x >= pivot for the items above it - when the parts are drawn " +
            "from the items other than the pivot, so the pivot is not among them; or give them a part of their own, put back " +
            "beside the pivot.",
            example);

    private static GuideEntry PathFromWrongEnd(string id, string example) =>
        GuideFor([id], "A shortest path worked out from the end the edge goes to",
            "Dijkstra's search keeps, for each place, the shortest length found to it so far. At each place it comes to, it looks " +
            "at each edge going out: the way through this place to the next one is the length to this place plus the edge's, and " +
            "when that is shorter than what is kept for the next place, it is kept instead. Here the edge's length is added to " +
            "what is kept for the next place itself - dist[next] + length - not to the length to the place the edge comes from.",
            "The length of a way through the place the search is at is never worked out, so the lengths found are wrong: a place " +
            "can keep the length it started with, or the length of one edge alone, however the graph is joined.",
            "Start the sum from the place the edge comes from: dist[node] + length, or the length the search took with that place.",
            example);

    private static GuideEntry PathPartsDontMeet(string id, string example) =>
        GuideFor([id], "A way through a place whose two parts do not meet there",
            "Floyd-Warshall's search tries every place k as a stop on the way from i to j: the way through k is the length from " +
            "i to k, then the length from k to j, and it is kept when it is shorter. The two parts have to meet at k. Here the " +
            "second part is written dist[j][k], the length from j to k - the way back - so the sum is not the length of any way " +
            "from i to j.",
            "On a graph whose every edge goes both ways at the same length the two are the same, and the answer happens to be " +
            "right; on any graph whose edges go one way, the lengths found are wrong - a place can be left unreached.",
            "Write the second part from k to j: dist[i][k] + dist[k][j].",
            example);

    private static GuideEntry PathLengthNotKept(string id, string example) =>
        GuideFor([id], "A shorter length worked out and kept in the edge, not as the place's own",
            "Bellman-Ford's search weighs every edge, again and again: the way to the place an edge goes to, through the place it " +
            "comes from, is dist[u] plus the edge's length, and when that is shorter than dist[v] it becomes dist[v]. Each pass " +
            "starts from the lengths the last one kept. Here the smaller of the two is worked out right, and then kept in the " +
            "edge - edge[2], weight_by_edge[u, v] - where its length was, not in dist[v].",
            "dist never changes, so every place but the start keeps the length it started with - infinity, or the " +
            "big number standing for it - and each pass only writes over the edges' own lengths; the search ends having found no way to anywhere.",
            "Keep the smaller length as the place's own: dist[v] = min(dist[u] + length, dist[v]).",
            example);

    private static GuideEntry MatchMovesOne(string id, string example) =>
        GuideFor([id], "A match that moves on in one sequence only",
            "Lining two sequences up item by item - the longest common subsequence, the longest common substring - a match is the " +
            "two items compared being equal, and it uses up an item of each: the length goes on from the entry one step back in " +
            "both places, table[i - 1][j - 1] + 1, or from the call given both sequences past their first items, lcs(a[1:], " +
            "b[1:]). Here it goes on from one step back in one place only - table[i - 1][j] + 1, lcs(a[1:], b) - so the other " +
            "sequence's item, already matched, is still there to be matched again.",
            "One item can be counted in more than one match, so the length found can be too long; for a common substring, whose " +
            "matches must follow one another, it can come out too short.",
            "Move past both items on a match: table[i - 1][j - 1] + 1, or lcs(a[1:], b[1:]).",
            example);

    private static GuideEntry MatchCharged(string id, string example) =>
        GuideFor([id], "A match charged as an edit",
            "An edit distance counts the fewest edits - an item put in, taken out, or replaced - turning one sequence into the " +
            "other. Where the two items compared differ, it is one more than the cheapest of the three edits: 1 + min(table[i - " +
            "1][j], table[i][j - 1], table[i - 1][j - 1]). Where they are equal they need no edit, and the distance goes on from " +
            "past both as it is: table[i - 1][j - 1]. Here the match adds 1 as well, as if equal items had to be replaced.",
            "Every match costs as much as an edit, so the distance can come out too large: kitten to sitting gives 7 where 3 is right.",
            "Charge nothing for a match: table[i - 1][j - 1], or levenshtein(source[1:], target[1:]).",
            example);

    private static GuideEntry NoStopWhenEmpty(string id, string example) =>
        GuideFor([id], "A list taken apart with no stop for when it is empty",
            "A function can work through a list by taking it apart: it reads the first item, then calls itself again with the " +
            "rest - coins[1:], coins.tail, first, *rest = coins. Each call passes one item over, so the list it is given gets " +
            "shorter until one call is given none at all. That call needs a stop of its own - asking whether the list is empty " +
            "before its first item is read. Here no test in the function asks it.",
            "The call given an empty list reads a first item that is not there: Python's unpacking or index fails, Java's index " +
            "is out of bounds, Ruby's and Lua's arithmetic meets nil - and where a missing item reads as nothing, as in Perl and " +
            "PowerShell, the calls go on and on: Perl warns of deep recursion.",
            "Stop when no items are left, before the first item is read: if total < 0 or not coins: return 0.",
            example);

    private static GuideEntry ExactFitLeftOut(string id, string example) =>
        GuideFor([id], "What fits exactly is left out",
            "Filling a table by taking what fits - a knapsack's items, change's coins - taking a weight w at a room j goes on " +
            "from the entry for the room left: table[i - 1][j - w], best[j - w]. When w is exactly j the room left is 0, and the " +
            "table has that entry - it is where the filling starts. Here the test asks w < j, strictly, so it leaves that case " +
            "out: whatever fills the room exactly is never taken.",
            "A best answer that fills some room exactly is missed, so the answer can come out wrong - too small for a knapsack; " +
            "for the fewest coins making an amount, no way at all, since every way ends with a coin equal to what is left.",
            "Let in what fits exactly: w <= j.",
            example);

    private static GuideEntry MemoNeverKept(string id, string example) =>
        GuideFor([id], "A memo that is asked and never given an answer",
            "A memo keeps each answer a function works out, so the next time the same answer is wanted it is looked up rather than " +
            "worked out again: fib(n) asks memo for n first. That only helps when each answer is put into the memo once it is " +
            "worked out. Here nothing ever is, so the memo is always empty, and every call works its answer out again.",
            "The program works out the right answer, but as slowly as it would with no memo at all - a fib(60) written this way was " +
            "still running when FixFinder's time ran out.",
            "Put the answer into the memo where the function works it out, and give back what is in the memo.",
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
        NewNodeLost("logic-python-new-node-lost",
            """
            def insert(node, value):
                if node is None:
                    return Node(value)
                if value < node.value:
                    node.left = insert(node.left, value)
                else:
                    node.right = insert(node.right, value)
                return node
            """),
        NewNodeLost("logic-ruby-new-node-lost",
            """
            def insert(node, value)
              return Node.new(value, nil, nil) if node.nil?
              if value < node.value
                node.left = insert(node.left, value)
              else
                node.right = insert(node.right, value)
              end
              node
            end
            """),
        NewNodeLost("logic-lua-new-node-lost",
            """
            local function insert(node, value)
              if node == nil then
                return { value = value }
              end
              if value < node.value then
                node.left = insert(node.left, value)
              else
                node.right = insert(node.right, value)
              end
              return node
            end
            """),
        NewNodeLost("logic-go-new-node-lost",
            """
            package main

            import "fmt"

            type Node struct {
            	Value       int
            	Left, Right *Node
            }

            func insert(node *Node, value int) *Node {
            	if node == nil {
            		return &Node{Value: value}
            	}
            	if value < node.Value {
            		node.Left = insert(node.Left, value)
            	} else {
            		node.Right = insert(node.Right, value)
            	}
            	return node
            }

            func main() {
            	root := insert(nil, 50)
            	root = insert(root, 30)
            	fmt.Println(root.Left.Value)
            }
            """),
        NewNodeLost("logic-js-new-node-lost",
            """
            function insert(node, value) {
                if (node === null) {
                    return { value: value, left: null, right: null };
                }
                if (value < node.value) {
                    node.left = insert(node.left, value);
                } else {
                    node.right = insert(node.right, value);
                }
                return node;
            }
            """),
        NewNodeLost("logic-java-new-node-lost",
            """
            static Node insert(Node node, int value) {
                if (node == null) {
                    return new Node(value);
                }
                if (value < node.value) {
                    node.left = insert(node.left, value);
                } else {
                    node.right = insert(node.right, value);
                }
                return node;
            }
            """),
        NewNodeLost("logic-csharp-new-node-lost",
            """
            static Node Insert(Node? node, int value)
            {
                if (node == null)
                {
                    return new Node(value);
                }
                if (value < node.Value)
                {
                    node.Left = Insert(node.Left, value);
                }
                else
                {
                    node.Right = Insert(node.Right, value);
                }
                return node;
            }
            """),
        NewNodeLost("logic-c-new-node-lost",
            """
            struct Node *insert(struct Node *node, int value) {
                if (node == NULL) {
                    return make_node(value);
                }
                if (value < node->value) {
                    node->left = insert(node->left, value);
                } else {
                    node->right = insert(node->right, value);
                }
                return node;
            }
            """),
        NewNodeLost("logic-dart-new-node-lost",
            """
            Node insert(Node? node, int value) {
              if (node == null) {
                return Node(value);
              }
              if (value < node.value) {
                node.left = insert(node.left, value);
              } else {
                node.right = insert(node.right, value);
              }
              return node;
            }
            """),
        NewNodeLost("logic-php-new-node-lost",
            """
            function insert(?Node $node, int $value): Node
            {
                if ($node === null) {
                    return new Node($value);
                }
                if ($value < $node->value) {
                    $node->left = insert($node->left, $value);
                } else {
                    $node->right = insert($node->right, $value);
                }
                return $node;
            }
            """),
        NewNodeLost("logic-perl-new-node-lost",
            """
            sub insert {
                my ($node, $value) = @_;
                return { value => $value } if !defined $node;
                if ($value < $node->{value}) {
                    $node->{left} = insert($node->{left}, $value);
                } else {
                    $node->{right} = insert($node->{right}, $value);
                }
                return $node;
            }
            """),
        NewNodeLost("logic-scala-new-node-lost",
            """
            def insert(node: Node, value: Int): Node = {
              if (node == null) {
                return new Node(value)
              }
              if (value < node.value) {
                node.left = insert(node.left, value)
              } else {
                node.right = insert(node.right, value)
              }
              node
            }
            """),
        MemoNeverKept("logic-python-memo-never-kept",
            """
            memo = {}


            def fib(n):
                if n in memo:
                    return memo[n]
                if n <= 1:
                    return n
                memo[n] = fib(n - 1) + fib(n - 2)
                return memo[n]
            """),
        MemoNeverKept("logic-ruby-memo-never-kept",
            """
            def fib(n, memo)
              return memo[n] if memo.key?(n)
              return n if n <= 1
              memo[n] = fib(n - 1, memo) + fib(n - 2, memo)
            end
            """),
        MemoNeverKept("logic-lua-memo-never-kept",
            """
            local memo = {}

            local function fib(n)
              if memo[n] then
                return memo[n]
              end
              if n <= 1 then
                return n
              end
              memo[n] = fib(n - 1) + fib(n - 2)
              return memo[n]
            end
            """),
        MemoNeverKept("logic-go-memo-never-kept",
            """
            package main

            import "fmt"

            var memo = map[int]int{}

            func fib(n int) int {
            	if value, ok := memo[n]; ok {
            		return value
            	}
            	if n <= 1 {
            		return n
            	}
            	memo[n] = fib(n-1) + fib(n-2)
            	return memo[n]
            }

            func main() {
            	fmt.Println(fib(60))
            }
            """),
        MemoNeverKept("logic-js-memo-never-kept",
            """
            const memo = new Map();

            function fib(n) {
                if (memo.has(n)) {
                    return memo.get(n);
                }
                if (n <= 1) {
                    return n;
                }
                memo.set(n, fib(n - 1) + fib(n - 2));
                return memo.get(n);
            }
            """),
        MemoNeverKept("logic-java-memo-never-kept",
            """
            static Map<Integer, Long> memo = new HashMap<>();

            static long fib(int n) {
                if (memo.containsKey(n)) {
                    return memo.get(n);
                }
                if (n <= 1) {
                    return n;
                }
                memo.put(n, fib(n - 1) + fib(n - 2));
                return memo.get(n);
            }
            """),
        MemoNeverKept("logic-csharp-memo-never-kept",
            """
            var memo = new Dictionary<int, long>();

            long Fib(int n)
            {
                if (memo.ContainsKey(n))
                {
                    return memo[n];
                }
                if (n <= 1)
                {
                    return n;
                }
                memo[n] = Fib(n - 1) + Fib(n - 2);
                return memo[n];
            }
            """),
        MemoNeverKept("logic-c-memo-never-kept",
            """
            std::map<int, long long> memo;

            long long fib(int n) {
                if (memo.count(n)) {
                    return memo[n];
                }
                if (n <= 1) {
                    return n;
                }
                memo[n] = fib(n - 1) + fib(n - 2);
                return memo[n];
            }
            """),
        MemoNeverKept("logic-dart-memo-never-kept",
            """
            final memo = <int, int>{};

            int fib(int n) {
              if (memo.containsKey(n)) {
                return memo[n]!;
              }
              if (n <= 1) {
                return n;
              }
              memo[n] = fib(n - 1) + fib(n - 2);
              return memo[n]!;
            }
            """),
        MemoNeverKept("logic-perl-memo-never-kept",
            """
            my %memo;

            sub fib {
                my ($n) = @_;
                return $memo{$n} if exists $memo{$n};
                return $n if $n <= 1;
                $memo{$n} = fib($n - 1) + fib($n - 2);
                return $memo{$n};
            }
            """),
        MemoNeverKept("logic-powershell-memo-never-kept",
            """
            $memo = @{}

            function Get-Fib($n) {
                if ($memo.ContainsKey($n)) {
                    return $memo[$n]
                }
                if ($n -le 1) {
                    return $n
                }
                $memo[$n] = (Get-Fib ($n - 1)) + (Get-Fib ($n - 2))
                return $memo[$n]
            }

            Write-Output (Get-Fib 60)
            """),
        MemoNeverKept("logic-scala-memo-never-kept",
            """
            val memo = mutable.Map[Int, Long]()

            def fib(n: Int): Long = {
              if (memo.contains(n)) {
                return memo(n)
              }
              if (n <= 1) {
                return n
              }
              memo(n) = fib(n - 1) + fib(n - 2)
              memo(n)
            }
            """),
        VisitedNeverAdded("logic-python-visited-never-added",
            """
            visited = set()


            def reach(node):
                if node in visited:
                    return 0
                visited.add(node)
                return 1 + sum(reach(next_node) for next_node in graph[node])
            """),
        VisitedNeverAdded("logic-java-visited-never-added",
            """
            static Set<Integer> visited = new HashSet<>();

            static int reach(int node) {
                if (visited.contains(node)) {
                    return 0;
                }
                visited.add(node);
                int count = 1;
                for (int next : graph.get(node)) {
                    count += reach(next);
                }
                return count;
            }
            """),
        VisitedNeverAdded("logic-csharp-visited-never-added",
            """
            static int Reach(int node, HashSet<int> visited)
            {
                if (visited.Contains(node)) return 0;
                visited.Add(node);
                int count = 1;
                foreach (int next in Graph[node]) count += Reach(next, visited);
                return count;
            }
            """),
        VisitedNeverAdded("logic-js-visited-never-added",
            """
            const visited = new Set();
            let node = 0;
            let steps = 0;
            while (true) {
                if (visited.has(node)) {
                    break;
                }
                visited.add(node);
                steps += 1;
                node = links[node];
            }
            """),
        VisitedNeverAdded("logic-c-visited-never-added",
            """
            int visited[4];

            int main(void) {
                int node = 0;
                int steps = 0;
                while (!visited[node]) {
                    visited[node] = 1;
                    steps++;
                    node = links[node];
                }
                printf("%d\n", steps);
                return 0;
            }
            """),
        VisitedNeverAdded("logic-cpp-visited-never-added",
            """
            std::set<int> visited;

            int reach(int node) {
                if (visited.count(node)) {
                    return 0;
                }
                visited.insert(node);
                int count = 1;
                for (int next : graph[node]) {
                    count += reach(next);
                }
                return count;
            }
            """),
        VisitedNeverAdded("logic-go-visited-never-added",
            """
            visited := map[int]bool{}
            node := 0
            steps := 0
            for !visited[node] {
            	visited[node] = true
            	steps++
            	node = links[node]
            }
            """),
        VisitedNeverAdded("logic-rust-visited-never-added",
            """
            fn reach(node: usize, graph: &Vec<Vec<usize>>, visited: &mut HashSet<usize>) -> usize {
                if visited.contains(&node) {
                    return 0;
                }
                visited.insert(node);
                let mut count = 1;
                for &next in &graph[node] {
                    count += reach(next, graph, visited);
                }
                count
            }
            """),
        VisitedNeverAdded("logic-ruby-visited-never-added",
            """
            visited = Set.new
            node = 0
            steps = 0
            until visited.include?(node)
              visited.add(node)
              steps += 1
              node = links[node]
            end
            """),
        VisitedNeverAdded("logic-lua-visited-never-added",
            """
            local visited = {}

            local function reach(node)
              if visited[node] then
                return 0
              end
              visited[node] = true
              local count = 1
              for _, next_node in ipairs(graph[node]) do
                count = count + reach(next_node)
              end
              return count
            end
            """),
        VisitedNeverAdded("logic-php-visited-never-added",
            """
            $visited = [];
            $node = 0;
            $steps = 0;
            while (!isset($visited[$node])) {
                $visited[$node] = true;
                $steps++;
                $node = $links[$node];
            }
            """),
        VisitedNeverAdded("logic-perl-visited-never-added",
            """
            my %visited;
            my $node = 0;
            my $steps = 0;
            while (!exists $visited{$node}) {
                $visited{$node} = 1;
                $steps++;
                $node = $links[$node];
            }
            """),
        VisitedNeverAdded("logic-powershell-visited-never-added",
            """
            $visited = [System.Collections.Generic.HashSet[int]]::new()
            $node = 0
            $steps = 0
            while (-not $visited.Contains($node)) {
                [void]$visited.Add($node)
                $steps++
                $node = $links[$node]
            }
            """),
        VisitedNeverAdded("logic-dart-visited-never-added",
            """
            final visited = <int>{};

            int reach(int node) {
              if (visited.contains(node)) return 0;
              visited.add(node);
              var count = 1;
              for (final next in graph[node]) {
                count += reach(next);
              }
              return count;
            }
            """),
        VisitedNeverAdded("logic-scala-visited-never-added",
            """
            val visited = mutable.Set[Int]()

            def reach(node: Int): Int = {
              if (visited.contains(node)) {
                return 0
              }
              visited += node
              1 + graph(node).map(reach).sum
            }
            """),
        HalvingNeverStops("logic-python-halving-never-stops",
            """
            def merge_sort(items):
                if len(items) <= 1:
                    return items
                middle = len(items) // 2
                return merge(merge_sort(items[:middle]), merge_sort(items[middle:]))
            """),
        HalvingNeverStops("logic-java-halving-never-stops",
            """
            static void sortRange(int[] items, int lo, int hi) {
                if (lo >= hi) return;
                int mid = lo + (hi - lo) / 2;
                sortRange(items, lo, mid);
                sortRange(items, mid + 1, hi);
                merge(items, lo, mid, hi);
            }
            """),
        HalvingNeverStops("logic-csharp-halving-never-stops",
            """
            static int[] MergeSort(int[] items)
            {
                if (items.Length <= 1)
                {
                    return items;
                }

                int mid = items.Length / 2;
                return Merge(MergeSort(items[..mid]), MergeSort(items[mid..]));
            }
            """),
        HalvingNeverStops("logic-js-halving-never-stops",
            """
            function mergeSort(items) {
              if (items.length <= 1) return items;
              const mid = Math.floor(items.length / 2);
              return merge(mergeSort(items.slice(0, mid)), mergeSort(items.slice(mid)));
            }
            """),
        HalvingNeverStops("logic-c-halving-never-stops",
            """
            static void merge_sort(int *items, int count) {
                if (count <= 1) return;
                int mid = count / 2;
                merge_sort(items, mid);
                merge_sort(items + mid, count - mid);
                merge(items, mid, count);
            }
            """),
        HalvingNeverStops("logic-cpp-halving-never-stops",
            """
            std::vector<int> merge_sort(const std::vector<int>& items) {
                if (items.size() <= 1) return items;
                std::size_t mid = items.size() / 2;
                std::vector<int> left(items.begin(), items.begin() + mid);
                std::vector<int> right(items.begin() + mid, items.end());
                return merge(merge_sort(left), merge_sort(right));
            }
            """),
        HalvingNeverStops("logic-go-halving-never-stops",
            """
            func mergeSort(items []int) []int {
            	if len(items) <= 1 {
            		return items
            	}
            	mid := len(items) / 2
            	return merge(mergeSort(items[:mid]), mergeSort(items[mid:]))
            }
            """),
        HalvingNeverStops("logic-rust-halving-never-stops",
            """
            fn merge_sort(items: Vec<i32>) -> Vec<i32> {
                if items.len() <= 1 {
                    return items;
                }
                let mid = items.len() / 2;
                let left = merge_sort(items[..mid].to_vec());
                let right = merge_sort(items[mid..].to_vec());
                merge(left, right)
            }
            """),
        HalvingNeverStops("logic-ruby-halving-never-stops",
            """
            def merge_sort(items)
              return items if items.length <= 1
              mid = items.length / 2
              merge(merge_sort(items[0...mid]), merge_sort(items[mid..]))
            end
            """),
        HalvingNeverStops("logic-lua-halving-never-stops",
            """
            local function merge_sort(items)
              if #items <= 1 then return items end
              local mid = math.floor(#items / 2)
              local left = merge_sort({ table.unpack(items, 1, mid) })
              local right = merge_sort({ table.unpack(items, mid + 1) })
              return merge(left, right)
            end
            """),
        HalvingNeverStops("logic-php-halving-never-stops",
            """
            function merge_sort(array $items): array {
                if (count($items) <= 1) return $items;
                $mid = intdiv(count($items), 2);
                return merge_lists(merge_sort(array_slice($items, 0, $mid)), merge_sort(array_slice($items, $mid)));
            }
            """),
        HalvingNeverStops("logic-perl-halving-never-stops",
            """
            sub merge_sort {
                my @items = @_;
                return @items if @items <= 1;
                my $mid = int(@items / 2);
                my @left = merge_sort(@items[0 .. $mid - 1]);
                my @right = merge_sort(@items[$mid .. $#items]);
                return merge_lists(\@left, \@right);
            }
            """),
        HalvingNeverStops("logic-powershell-halving-never-stops",
            """
            function Merge-Sort($items) {
                if ($items.Count -le 1) { return ,$items }
                $mid = [math]::Floor($items.Count / 2)
                $left = Merge-Sort $items[0..($mid - 1)]
                $right = Merge-Sort $items[$mid..($items.Count - 1)]
                return ,(Merge-Lists $left $right)
            }
            """),
        HalvingNeverStops("logic-dart-halving-never-stops",
            """
            List<int> mergeSort(List<int> items) {
              if (items.length <= 1) return items;
              final mid = items.length ~/ 2;
              return merge(mergeSort(items.sublist(0, mid)), mergeSort(items.sublist(mid)));
            }
            """),
        HalvingNeverStops("logic-scala-halving-never-stops",
            """
            def mergeSort(items: List[Int]): List[Int] = {
              if (items.length <= 1) items
              else {
                val (left, right) = items.splitAt(items.length / 2)
                merge(mergeSort(left), mergeSort(right))
              }
            }
            """),
        HalvingNeverStops("logic-fortran-halving-never-stops",
            """
            recursive subroutine sort_range(items, lo, hi)
              integer, intent(inout) :: items(:)
              integer, intent(in) :: lo, hi
              integer :: mid
              if (lo >= hi) return
              mid = (lo + hi) / 2
              call sort_range(items, lo, mid)
              call sort_range(items, mid + 1, hi)
              call merge(items, lo, mid, hi)
            end subroutine sort_range
            """),
        HalvingNeverStops("logic-elixir-halving-never-stops",
            """
            def merge_sort([]), do: []
            def merge_sort([item]), do: [item]

            def merge_sort(items) do
              {left, right} = Enum.split(items, div(length(items), 2))
              merge(merge_sort(left), merge_sort(right))
            end
            """),
        HalvingNeverStops("logic-erlang-halving-never-stops",
            """
            merge_sort([]) -> [];
            merge_sort([Item]) -> [Item];
            merge_sort(Items) ->
                {Left, Right} = lists:split(length(Items) div 2, Items),
                merge(merge_sort(Left), merge_sort(Right)).
            """),
        HalvingNeverStops("logic-fsharp-halving-never-stops",
            """
            let rec mergeSort items =
                match items with
                | [] -> []
                | [item] -> [item]
                | _ ->
                    let left, right = List.splitAt (List.length items / 2) items
                    merge (mergeSort left) (mergeSort right)
            """),
        PartitionDropsEqual("logic-python-partition-drops-equal",
            """
            def quicksort(items):
                if not items:
                    return []
                pivot = items[0]
                lesser = quicksort([x for x in items[1:] if x < pivot])
                greater = quicksort([x for x in items[1:] if x >= pivot])
                return lesser + [pivot] + greater
            """),
        PartitionDropsEqual("logic-java-partition-drops-equal",
            """
            static ArrayList<Integer> quicksort(ArrayList<Integer> items) {
                if (items.isEmpty()) return new ArrayList<>();
                Integer pivot = items.get(0);
                ArrayList<Integer> lesser = new ArrayList<>();
                ArrayList<Integer> greater = new ArrayList<>();
                for (Integer x : items.subList(1, items.size())) {
                    if (x < pivot) {
                        lesser.add(x);
                    } else if (x >= pivot) {
                        greater.add(x);
                    }
                }
                ArrayList<Integer> sorted = quicksort(lesser);
                sorted.add(pivot);
                sorted.addAll(quicksort(greater));
                return sorted;
            }
            """),
        PartitionDropsEqual("logic-csharp-partition-drops-equal",
            """
            static List<int> QuickSort(List<int> items)
            {
                if (items.Count <= 1) return items;
                int pivot = items[0];
                var rest = items.Skip(1).ToList();
                var sorted = QuickSort(rest.Where(x => x < pivot).ToList());
                sorted.Add(pivot);
                sorted.AddRange(QuickSort(rest.Where(x => x >= pivot).ToList()));
                return sorted;
            }
            """),
        PartitionDropsEqual("logic-js-partition-drops-equal",
            """
            function quickSort(items) {
              if (items.length <= 1) return items;
              const [pivot, ...rest] = items;
              const lower = rest.filter((x) => x < pivot);
              const higher = rest.filter((x) => x >= pivot);
              return [...quickSort(lower), pivot, ...quickSort(higher)];
            }
            """),
        PartitionDropsEqual("logic-c-partition-drops-equal",
            """
            static void quicksort(int *items, int count) {
                if (count <= 1) return;
                int pivot = items[0];
                int lower[64], higher[64];
                int lowerCount = 0, higherCount = 0;
                for (int i = 1; i < count; i++) {
                    if (items[i] < pivot) {
                        lower[lowerCount++] = items[i];
                    } else if (items[i] >= pivot) {
                        higher[higherCount++] = items[i];
                    }
                }
                quicksort(lower, lowerCount);
                quicksort(higher, higherCount);
                int at = 0;
                for (int i = 0; i < lowerCount; i++) items[at++] = lower[i];
                items[at++] = pivot;
                for (int i = 0; i < higherCount; i++) items[at++] = higher[i];
            }
            """),
        PartitionDropsEqual("logic-cpp-partition-drops-equal",
            """
            std::vector<int> quicksort(const std::vector<int>& items) {
                if (items.size() <= 1) return items;
                int pivot = items[0];
                std::vector<int> rest(items.begin() + 1, items.end());
                std::vector<int> lower, higher;
                for (int x : rest) {
                    if (x < pivot) {
                        lower.push_back(x);
                    } else if (x >= pivot) {
                        higher.push_back(x);
                    }
                }
                std::vector<int> sorted = quicksort(lower);
                sorted.push_back(pivot);
                for (int x : quicksort(higher)) sorted.push_back(x);
                return sorted;
            }
            """),
        PartitionDropsEqual("logic-go-partition-drops-equal",
            """
            func quickSort(items []int) []int {
            	if len(items) <= 1 {
            		return items
            	}
            	pivot := items[0]
            	var lower, higher []int
            	for _, x := range items[1:] {
            		if x < pivot {
            			lower = append(lower, x)
            		} else if x >= pivot {
            			higher = append(higher, x)
            		}
            	}
            	sorted := append(quickSort(lower), pivot)
            	return append(sorted, quickSort(higher)...)
            }
            """),
        PartitionDropsEqual("logic-rust-partition-drops-equal",
            """
            fn quick_sort(items: Vec<i32>) -> Vec<i32> {
                if items.len() <= 1 {
                    return items;
                }
                let pivot = items[0];
                let rest = &items[1..];
                let lower: Vec<i32> = rest.iter().filter(|&&x| x < pivot).cloned().collect();
                let higher: Vec<i32> = rest.iter().filter(|&&x| x >= pivot).cloned().collect();
                let mut sorted = quick_sort(lower);
                sorted.push(pivot);
                sorted.extend(quick_sort(higher));
                sorted
            }
            """),
        PartitionDropsEqual("logic-ruby-partition-drops-equal",
            """
            def quick_sort(items)
              return items if items.length <= 1
              pivot, *rest = items
              quick_sort(rest.select { |x| x < pivot }) + [pivot] + quick_sort(rest.select { |x| x >= pivot })
            end
            """),
        PartitionDropsEqual("logic-lua-partition-drops-equal",
            """
            local function quick_sort(items)
              if #items <= 1 then return items end
              local pivot = items[1]
              local lower, higher = {}, {}
              for i = 2, #items do
                local x = items[i]
                if x < pivot then
                  table.insert(lower, x)
                elseif x >= pivot then
                  table.insert(higher, x)
                end
              end
              local sorted = quick_sort(lower)
              table.insert(sorted, pivot)
              for _, x in ipairs(quick_sort(higher)) do table.insert(sorted, x) end
              return sorted
            end
            """),
        PartitionDropsEqual("logic-php-partition-drops-equal",
            """
            function quick_sort(array $items): array {
                if (count($items) <= 1) return $items;
                $pivot = $items[0];
                $rest = array_slice($items, 1);
                $lower = array_values(array_filter($rest, fn($x) => $x < $pivot));
                $higher = array_values(array_filter($rest, fn($x) => $x >= $pivot));
                return array_merge(quick_sort($lower), [$pivot], quick_sort($higher));
            }
            """),
        PartitionDropsEqual("logic-perl-partition-drops-equal",
            """
            sub quick_sort {
                my ($pivot, @rest) = @_;
                return () unless defined $pivot;
                my @lower = grep { $_ < $pivot } @rest;
                my @higher = grep { $_ >= $pivot } @rest;
                return (quick_sort(@lower), $pivot, quick_sort(@higher));
            }
            """),
        PartitionDropsEqual("logic-powershell-partition-drops-equal",
            """
            function Quick-Sort($items) {
                if ($items.Count -le 1) { return $items }
                $pivot = $items[0]
                $rest = $items[1..($items.Count - 1)]
                $lower = @($rest | Where-Object { $_ -lt $pivot })
                $higher = @($rest | Where-Object { $_ -ge $pivot })
                return @(Quick-Sort $lower) + @($pivot) + @(Quick-Sort $higher)
            }
            """),
        PartitionDropsEqual("logic-dart-partition-drops-equal",
            """
            List<int> quickSort(List<int> items) {
              if (items.length <= 1) return items;
              final pivot = items[0];
              final rest = items.sublist(1);
              final lower = rest.where((x) => x < pivot).toList();
              final higher = rest.where((x) => x >= pivot).toList();
              return [...quickSort(lower), pivot, ...quickSort(higher)];
            }
            """),
        PartitionDropsEqual("logic-scala-partition-drops-equal",
            """
            def quickSort(items: List[Int]): List[Int] = items match {
              case Nil => Nil
              case pivot :: rest =>
                quickSort(rest.filter(_ < pivot)) ++ List(pivot) ++ quickSort(rest.filter(_ >= pivot))
            }
            """),
        PartitionDropsEqual("logic-fortran-partition-drops-equal",
            """
            recursive function quick_sort(items) result(sorted)
              integer, intent(in) :: items(:)
              integer :: sorted(size(items))
              integer :: pivot
              if (size(items) <= 1) then
                sorted = items
                return
              end if
              pivot = items(1)
              sorted = [quick_sort(pack(items(2:), items(2:) < pivot)), pivot, quick_sort(pack(items(2:), items(2:) >= pivot))]
            end function quick_sort
            """),
        PartitionDropsEqual("logic-elixir-partition-drops-equal",
            """
            def quick_sort([]), do: []

            def quick_sort([pivot | rest]) do
              lower = for x <- rest, x < pivot, do: x
              higher = for x <- rest, x >= pivot, do: x
              quick_sort(lower) ++ [pivot] ++ quick_sort(higher)
            end
            """),
        PartitionDropsEqual("logic-erlang-partition-drops-equal",
            """
            quick_sort([]) -> [];
            quick_sort([Pivot | Rest]) ->
                Lower = [X || X <- Rest, X < Pivot],
                Higher = [X || X <- Rest, X >= Pivot],
                quick_sort(Lower) ++ [Pivot] ++ quick_sort(Higher).
            """),
        PartitionDropsEqual("logic-fsharp-partition-drops-equal",
            """
            let rec quickSort items =
                match items with
                | [] -> []
                | pivot :: rest ->
                    let lower = List.filter (fun x -> x < pivot) rest
                    let higher = List.filter (fun x -> x >= pivot) rest
                    quickSort lower @ [pivot] @ quickSort higher
            """),
        PartitionDropsEqual("logic-ocaml-partition-drops-equal",
            """
            let rec quick_sort items =
              match items with
              | [] -> []
              | pivot :: rest ->
                let lower = List.filter (fun x -> x < pivot) rest in
                let higher = List.filter (fun x -> x >= pivot) rest in
                quick_sort lower @ [pivot] @ quick_sort higher
            """),
        PathFromWrongEnd("logic-python-path-from-wrong-end",
            """
            for next_node, length in graph[node]:
                if dist[node] + length < dist[next_node]:
                    dist[next_node] = dist[node] + length
                    heapq.heappush(heap, (dist[next_node], next_node))
            """),
        PathFromWrongEnd("logic-java-path-from-wrong-end",
            """
            for (int[] edge : graph.get(node)) {
                int next = edge[0];
                int length = edge[1];
                if (dist[node] + length < dist[next]) {
                    dist[next] = dist[node] + length;
                }
            }
            """),
        PathFromWrongEnd("logic-csharp-path-from-wrong-end",
            """
            foreach (var (next, length) in graph[node])
            {
                int shorter = Math.Min(dist[next], dist[node] + length);
                if (shorter < dist[next])
                {
                    dist[next] = shorter;
                    queue.Enqueue(next, shorter);
                }
            }
            """),
        PathFromWrongEnd("logic-js-path-from-wrong-end",
            """
            for (const [next, length] of graph[node]) {
              if (dist[node] + length < dist[next]) {
                dist[next] = dist[node] + length;
              }
            }
            """),
        PathFromWrongEnd("logic-c-path-from-wrong-end",
            """
            for (int next = 0; next < N; next++) {
                if (graph[node][next] > 0 && dist[node] + graph[node][next] < dist[next]) dist[next] = dist[node] + graph[node][next];
            }
            """),
        PathFromWrongEnd("logic-cpp-path-from-wrong-end",
            """
            for (auto [next, length] : graph[node]) {
                int shorter = std::min(dist[next], dist[node] + length);
                if (shorter < dist[next]) {
                    dist[next] = shorter;
                    queue.push({shorter, next});
                }
            }
            """),
        PathFromWrongEnd("logic-go-path-from-wrong-end",
            """
            for _, e := range graph[node] {
            	if dist[node]+e.length < dist[e.to] {
            		dist[e.to] = dist[node] + e.length
            	}
            }
            """),
        PathFromWrongEnd("logic-rust-path-from-wrong-end",
            """
            for &(next, length) in &graph[node] {
                dist[next] = dist[next].min(dist[node] + length);
            }
            """),
        PathFromWrongEnd("logic-ruby-path-from-wrong-end",
            """
            graph[node].each do |nxt, length|
              dist[nxt] = [dist[nxt], dist[node] + length].min
            end
            """),
        PathFromWrongEnd("logic-lua-path-from-wrong-end",
            """
            for _, edge in ipairs(graph[node]) do
              local nxt, length = edge[1], edge[2]
              if dist[node] + length < dist[nxt] then
                dist[nxt] = dist[node] + length
              end
            end
            """),
        PathFromWrongEnd("logic-php-path-from-wrong-end",
            """
            foreach ($graph[$node] as [$next, $length]) {
                $dist[$next] = min($dist[$next], $dist[$node] + $length);
            }
            """),
        PathFromWrongEnd("logic-perl-path-from-wrong-end",
            """
            for my $edge (@{ $graph{$node} }) {
                my ($next, $length) = @$edge;
                if ($dist{$node} + $length < $dist{$next}) {
                    $dist{$next} = $dist{$node} + $length;
                }
            }
            """),
        PathFromWrongEnd("logic-powershell-path-from-wrong-end",
            """
            foreach ($edge in $graph[$node]) {
                $next = $edge[0]
                $length = $edge[1]
                if ($dist[$node] + $length -lt $dist[$next]) {
                    $dist[$next] = $dist[$node] + $length
                }
            }
            """),
        PathFromWrongEnd("logic-dart-path-from-wrong-end",
            """
            for (final edge in graph[node]) {
              final next = edge[0];
              final length = edge[1];
              if (dist[node] + length < dist[next]) {
                dist[next] = dist[node] + length;
              }
            }
            """),
        PathFromWrongEnd("logic-scala-path-from-wrong-end",
            """
            for ((next, length) <- graph(node)) {
              dist(next) = math.min(dist(next), dist(node) + length)
            }
            """),
        PathFromWrongEnd("logic-fortran-path-from-wrong-end",
            """
            do nxt = 1, n
              if (graph(node, nxt) > 0 .and. dist(node) + graph(node, nxt) < dist(nxt)) dist(nxt) = dist(node) + graph(node, nxt)
            end do
            """),
        PathPartsDontMeet("logic-python-path-parts-dont-meet",
            """
            for k in range(n):
                for i in range(n):
                    for j in range(n):
                        dist[i][j] = min(dist[i][j], dist[i][k] + dist[k][j])
            """),
        PathPartsDontMeet("logic-java-path-parts-dont-meet",
            """
            for (int k = 0; k < n; k++) {
                for (int i = 0; i < n; i++) {
                    for (int j = 0; j < n; j++) {
                        if (dist[i][k] + dist[k][j] < dist[i][j]) {
                            dist[i][j] = dist[i][k] + dist[k][j];
                        }
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-csharp-path-parts-dont-meet",
            """
            for (int k = 0; k < n; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        dist[i, j] = Math.Min(dist[i, j], dist[i, k] + dist[k, j]);
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-js-path-parts-dont-meet",
            """
            for (let k = 0; k < n; k++) {
              for (let i = 0; i < n; i++) {
                for (let j = 0; j < n; j++) {
                  dist[i][j] = Math.min(dist[i][j], dist[i][k] + dist[k][j]);
                }
              }
            }
            """),
        PathPartsDontMeet("logic-c-path-parts-dont-meet",
            """
            for (int k = 0; k < N; k++) {
                for (int i = 0; i < N; i++) {
                    for (int j = 0; j < N; j++) {
                        if (dist[i][k] + dist[k][j] < dist[i][j]) dist[i][j] = dist[i][k] + dist[k][j];
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-cpp-path-parts-dont-meet",
            """
            for (int k = 0; k < n; k++) {
                for (int i = 0; i < n; i++) {
                    for (int j = 0; j < n; j++) {
                        dist[i][j] = std::min(dist[i][j], dist[i][k] + dist[k][j]);
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-go-path-parts-dont-meet",
            """
            for k := 0; k < n; k++ {
            	for i := 0; i < n; i++ {
            		for j := 0; j < n; j++ {
            			if dist[i][k]+dist[k][j] < dist[i][j] {
            				dist[i][j] = dist[i][k] + dist[k][j]
            			}
            		}
            	}
            }
            """),
        PathPartsDontMeet("logic-rust-path-parts-dont-meet",
            """
            for k in 0..n {
                for i in 0..n {
                    for j in 0..n {
                        dist[i][j] = dist[i][j].min(dist[i][k] + dist[k][j]);
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-ruby-path-parts-dont-meet",
            """
            (0...n).each do |k|
              (0...n).each do |i|
                (0...n).each do |j|
                  dist[i][j] = [dist[i][j], dist[i][k] + dist[k][j]].min
                end
              end
            end
            """),
        PathPartsDontMeet("logic-lua-path-parts-dont-meet",
            """
            for k = 1, n do
              for i = 1, n do
                for j = 1, n do
                  if dist[i][k] + dist[k][j] < dist[i][j] then
                    dist[i][j] = dist[i][k] + dist[k][j]
                  end
                end
              end
            end
            """),
        PathPartsDontMeet("logic-php-path-parts-dont-meet",
            """
            for ($k = 0; $k < $n; $k++) {
                for ($i = 0; $i < $n; $i++) {
                    for ($j = 0; $j < $n; $j++) {
                        $dist[$i][$j] = min($dist[$i][$j], $dist[$i][$k] + $dist[$k][$j]);
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-perl-path-parts-dont-meet",
            """
            for my $k (0 .. $n - 1) {
                for my $i (0 .. $n - 1) {
                    for my $j (0 .. $n - 1) {
                        if ($dist[$i][$k] + $dist[$k][$j] < $dist[$i][$j]) {
                            $dist[$i][$j] = $dist[$i][$k] + $dist[$k][$j];
                        }
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-powershell-path-parts-dont-meet",
            """
            foreach ($k in 0..($n - 1)) {
                foreach ($i in 0..($n - 1)) {
                    foreach ($j in 0..($n - 1)) {
                        if ($dist[$i][$k] + $dist[$k][$j] -lt $dist[$i][$j]) {
                            $dist[$i][$j] = $dist[$i][$k] + $dist[$k][$j]
                        }
                    }
                }
            }
            """),
        PathPartsDontMeet("logic-dart-path-parts-dont-meet",
            """
            for (var k = 0; k < n; k++) {
              for (var i = 0; i < n; i++) {
                for (var j = 0; j < n; j++) {
                  if (dist[i][k] + dist[k][j] < dist[i][j]) {
                    dist[i][j] = dist[i][k] + dist[k][j];
                  }
                }
              }
            }
            """),
        PathPartsDontMeet("logic-scala-path-parts-dont-meet",
            """
            for (k <- 0 until n) {
              for (i <- 0 until n) {
                for (j <- 0 until n) {
                  dist(i)(j) = math.min(dist(i)(j), dist(i)(k) + dist(k)(j))
                }
              }
            }
            """),
        PathPartsDontMeet("logic-fortran-path-parts-dont-meet",
            """
            do k = 1, n
              do i = 1, n
                do j = 1, n
                  if (dist(i, k) + dist(k, j) < dist(i, j)) dist(i, j) = dist(i, k) + dist(k, j)
                end do
              end do
            end do
            """),
        PathLengthNotKept("logic-python-path-length-not-kept",
            """
            for _ in range(len(dist) - 1):
                for edge in edges:
                    u, v, length = edge
                    dist[v] = min(dist[u] + length, dist[v])
            """),
        PathLengthNotKept("logic-java-path-length-not-kept",
            """
            for (int round = 0; round < dist.length - 1; round++) {
                for (int[] edge : edges) {
                    int u = edge[0], v = edge[1], length = edge[2];
                    int shorter = Math.min(dist[u] + length, dist[v]);
                    dist[v] = shorter;
                }
            }
            """),
        PathLengthNotKept("logic-csharp-path-length-not-kept",
            """
            for (int round = 0; round < dist.Length - 1; round++)
            {
                for (int e = 0; e < edges.Length; e++)
                {
                    int u = edges[e][0], v = edges[e][1], length = edges[e][2];
                    int shorter = Math.Min(dist[u] + length, dist[v]);
                    dist[v] = shorter;
                }
            }
            """),
        PathLengthNotKept("logic-js-path-length-not-kept",
            """
            for (let round = 0; round < dist.length - 1; round++) {
              for (const edge of edges) {
                const [u, v, length] = edge;
                dist[v] = Math.min(dist[u] + length, dist[v]);
              }
            }
            """),
        PathLengthNotKept("logic-c-path-length-not-kept",
            """
            for (int round = 0; round < 3; round++) {
                for (int e = 0; e < 5; e++) {
                    int u = edges[e][0], v = edges[e][1], length = edges[e][2];
                    int shorter = MIN(dist[u] + length, dist[v]);
                    dist[v] = shorter;
                }
            }
            """),
        PathLengthNotKept("logic-cpp-path-length-not-kept",
            """
            for (std::size_t round = 0; round + 1 < dist.size(); round++) {
                for (auto& edge : edges) {
                    int u = edge[0], v = edge[1], length = edge[2];
                    dist[v] = std::min(dist[u] + length, dist[v]);
                }
            }
            """),
        PathLengthNotKept("logic-go-path-length-not-kept",
            """
            for round := 0; round < len(dist)-1; round++ {
            	for e := range edges {
            		u, v, length := edges[e][0], edges[e][1], edges[e][2]
            		shorter := min(dist[u]+length, dist[v])
            		dist[v] = shorter
            	}
            }
            """),
        PathLengthNotKept("logic-rust-path-length-not-kept",
            """
            for _ in 0..dist.len() - 1 {
                for edge in edges.iter_mut() {
                    let (u, v, length) = *edge;
                    dist[v] = dist[v].min(dist[u] + length);
                }
            }
            """),
        PathLengthNotKept("logic-ruby-path-length-not-kept",
            """
            (dist.length - 1).times do
              edges.each do |edge|
                u, v, length = edge
                dist[v] = [dist[u] + length, dist[v]].min
              end
            end
            """),
        PathLengthNotKept("logic-lua-path-length-not-kept",
            """
            for _ = 1, #dist - 1 do
              for _, edge in ipairs(edges) do
                local u, v, length = edge[1], edge[2], edge[3]
                dist[v] = math.min(dist[u] + length, dist[v])
              end
            end
            """),
        PathLengthNotKept("logic-php-path-length-not-kept",
            """
            for ($round = 0; $round < count($dist) - 1; $round++) {
                foreach ($edges as $e => [$u, $v, $length]) {
                    $dist[$v] = min($dist[$u] + $length, $dist[$v]);
                }
            }
            """),
        PathLengthNotKept("logic-perl-path-length-not-kept",
            """
            for my $round (1 .. $#dist) {
                for my $edge (@edges) {
                    my ($u, $v, $length) = @$edge;
                    my $shorter = min($dist[$u] + $length, $dist[$v]);
                    $dist[$v] = $shorter;
                }
            }
            """),
        PathLengthNotKept("logic-powershell-path-length-not-kept",
            """
            foreach ($round in 1..($dist.Count - 1)) {
                foreach ($edge in $edges) {
                    $u, $v, $length = $edge
                    $dist[$v] = [math]::Min($dist[$u] + $length, $dist[$v])
                }
            }
            """),
        PathLengthNotKept("logic-dart-path-length-not-kept",
            """
            for (var round = 0; round < dist.length - 1; round++) {
              for (final edge in edges) {
                final u = edge[0], v = edge[1], length = edge[2];
                dist[v] = min(dist[u] + length, dist[v]);
              }
            }
            """),
        PathLengthNotKept("logic-scala-path-length-not-kept",
            """
            for (_ <- 0 until dist.length - 1) {
              for (edge <- edges) {
                val u = edge(0)
                val v = edge(1)
                val length = edge(2)
                dist(v) = math.min(dist(u) + length, dist(v))
              }
            }
            """),
        PathLengthNotKept("logic-fortran-path-length-not-kept",
            """
            do round = 1, 3
              do e = 1, 5
                u = edges(1, e)
                v = edges(2, e)
                length = edges(3, e)
                dist(v) = min(dist(u) + length, dist(v))
              end do
            end do
            """),
        MatchMovesOne("logic-python-match-moves-one",
            """
            for i in range(1, len(first) + 1):
                for j in range(1, len(second) + 1):
                    if first[i - 1] == second[j - 1]:
                        table[i][j] = table[i - 1][j - 1] + 1
                    else:
                        table[i][j] = max(table[i - 1][j], table[i][j - 1])
            """),
        MatchMovesOne("logic-java-match-moves-one",
            """
            for (int i = 1; i <= first.length(); i++) {
                for (int j = 1; j <= second.length(); j++) {
                    if (first.charAt(i - 1) == second.charAt(j - 1)) {
                        table[i][j] = table[i - 1][j - 1] + 1;
                    } else {
                        table[i][j] = Math.max(table[i - 1][j], table[i][j - 1]);
                    }
                }
            }
            """),
        MatchMovesOne("logic-csharp-match-moves-one",
            """
            for (int i = 1; i <= first.Length; i++)
            {
                for (int j = 1; j <= second.Length; j++)
                {
                    if (first[i - 1] == second[j - 1])
                    {
                        table[i, j] = table[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        table[i, j] = Math.Max(table[i - 1, j], table[i, j - 1]);
                    }
                }
            }
            """),
        MatchMovesOne("logic-js-match-moves-one",
            """
            for (let i = 1; i <= first.length; i++) {
              for (let j = 1; j <= second.length; j++) {
                if (first[i - 1] === second[j - 1]) {
                  table[i][j] = table[i - 1][j - 1] + 1;
                } else {
                  table[i][j] = Math.max(table[i - 1][j], table[i][j - 1]);
                }
              }
            }
            """),
        MatchMovesOne("logic-c-match-moves-one",
            """
            for (int i = 1; i <= rows; i++) {
                for (int j = 1; j <= cols; j++) {
                    if (first[i - 1] == second[j - 1]) {
                        table[i][j] = table[i - 1][j - 1] + 1;
                    } else {
                        table[i][j] = MAX(table[i - 1][j], table[i][j - 1]);
                    }
                }
            }
            """),
        MatchMovesOne("logic-cpp-match-moves-one",
            """
            for (std::size_t i = 1; i <= first.size(); i++) {
                for (std::size_t j = 1; j <= second.size(); j++) {
                    if (first[i - 1] == second[j - 1]) {
                        table[i][j] = table[i - 1][j - 1] + 1;
                    } else {
                        table[i][j] = std::max(table[i - 1][j], table[i][j - 1]);
                    }
                }
            }
            """),
        MatchMovesOne("logic-go-match-moves-one",
            """
            for i := 1; i <= len(first); i++ {
            	for j := 1; j <= len(second); j++ {
            		if first[i-1] == second[j-1] {
            			table[i][j] = table[i-1][j-1] + 1
            		} else {
            			table[i][j] = max(table[i-1][j], table[i][j-1])
            		}
            	}
            }
            """),
        MatchMovesOne("logic-rust-match-moves-one",
            """
            for i in 1..=a.len() {
                for j in 1..=b.len() {
                    if a[i - 1] == b[j - 1] {
                        table[i][j] = table[i - 1][j - 1] + 1;
                    } else {
                        table[i][j] = table[i - 1][j].max(table[i][j - 1]);
                    }
                }
            }
            """),
        MatchMovesOne("logic-ruby-match-moves-one",
            """
            (1..first.length).each do |i|
              (1..second.length).each do |j|
                if first[i - 1] == second[j - 1]
                  table[i][j] = table[i - 1][j - 1] + 1
                else
                  table[i][j] = [table[i - 1][j], table[i][j - 1]].max
                end
              end
            end
            """),
        MatchMovesOne("logic-lua-match-moves-one",
            """
            for i = 1, #first do
              for j = 1, #second do
                if first:sub(i, i) == second:sub(j, j) then
                  lengths[i][j] = lengths[i - 1][j - 1] + 1
                else
                  lengths[i][j] = math.max(lengths[i - 1][j], lengths[i][j - 1])
                end
              end
            end
            """),
        MatchMovesOne("logic-php-match-moves-one",
            """
            for ($i = 1; $i <= strlen($first); $i++) {
                for ($j = 1; $j <= strlen($second); $j++) {
                    if ($first[$i - 1] === $second[$j - 1]) {
                        $table[$i][$j] = $table[$i - 1][$j - 1] + 1;
                    } else {
                        $table[$i][$j] = max($table[$i - 1][$j], $table[$i][$j - 1]);
                    }
                }
            }
            """),
        MatchMovesOne("logic-perl-match-moves-one",
            """
            for my $i (1 .. length($first)) {
                for my $j (1 .. length($second)) {
                    if (substr($first, $i - 1, 1) eq substr($second, $j - 1, 1)) {
                        $table[$i][$j] = $table[$i - 1][$j - 1] + 1;
                    } else {
                        $table[$i][$j] = max($table[$i - 1][$j], $table[$i][$j - 1]);
                    }
                }
            }
            """),
        MatchMovesOne("logic-powershell-match-moves-one",
            """
            for ($i = 1; $i -le $first.Length; $i++) {
                for ($j = 1; $j -le $second.Length; $j++) {
                    if ($first[$i - 1] -eq $second[$j - 1]) {
                        $table[$i][$j] = $table[$i - 1][$j - 1] + 1
                    } else {
                        $table[$i][$j] = [math]::Max($table[$i - 1][$j], $table[$i][$j - 1])
                    }
                }
            }
            """),
        MatchMovesOne("logic-dart-match-moves-one",
            """
            for (var i = 1; i <= first.length; i++) {
              for (var j = 1; j <= second.length; j++) {
                if (first[i - 1] == second[j - 1]) {
                  table[i][j] = table[i - 1][j - 1] + 1;
                } else {
                  table[i][j] = max(table[i - 1][j], table[i][j - 1]);
                }
              }
            }
            """),
        MatchMovesOne("logic-scala-match-moves-one",
            """
            for (i <- 1 to first.length; j <- 1 to second.length) {
              if (first(i - 1) == second(j - 1)) {
                table(i)(j) = table(i - 1)(j - 1) + 1
              } else {
                table(i)(j) = math.max(table(i - 1)(j), table(i)(j - 1))
              }
            }
            """),
        MatchMovesOne("logic-fortran-match-moves-one",
            """
            do i = 1, len(first)
              do j = 1, len(second)
                if (first(i:i) == second(j:j)) then
                  table(i, j) = table(i - 1, j - 1) + 1
                else
                  table(i, j) = max(table(i - 1, j), table(i, j - 1))
                end if
              end do
            end do
            """),
        ExactFitLeftOut("logic-python-exact-fit-left-out",
            """
            for j in range(1, capacity + 1):
                if weight <= j:
                    table[i][j] = max(table[i - 1][j], value + table[i - 1][j - weight])
                else:
                    table[i][j] = table[i - 1][j]
            """),
        ExactFitLeftOut("logic-java-exact-fit-left-out",
            """
            if (i == 0 || j == 0) {
                table[i][j] = 0;
            } else if (weight <= j) {
                table[i][j] = Math.max(table[i - 1][j], value + table[i - 1][j - weight]);
            } else {
                table[i][j] = table[i - 1][j];
            }
            """),
        ExactFitLeftOut("logic-csharp-exact-fit-left-out",
            """
            if (j >= weight)
            {
                table[i, j] = Math.Max(table[i - 1, j], value + table[i - 1, j - weight]);
            }
            else
            {
                table[i, j] = table[i - 1, j];
            }
            """),
        ExactFitLeftOut("logic-js-exact-fit-left-out",
            """
            for (let j = capacity; j > 0; j--) {
              if (weight <= j) {
                best[j] = Math.max(best[j], value + best[j - weight]);
              }
            }
            """),
        ExactFitLeftOut("logic-c-exact-fit-left-out",
            """
            if (weights[i - 1] <= j) table[i][j] = MAX(table[i - 1][j], values[i - 1] + table[i - 1][j - weights[i - 1]]);
            else table[i][j] = table[i - 1][j];
            """),
        ExactFitLeftOut("logic-cpp-exact-fit-left-out",
            """
            if (weight <= j) {
                table[i][j] = std::max(table[i - 1][j], value + table[i - 1][j - weight]);
            } else {
                table[i][j] = table[i - 1][j];
            }
            """),
        ExactFitLeftOut("logic-go-exact-fit-left-out",
            """
            if weight <= j {
            	table[i][j] = max(table[i-1][j], value+table[i-1][j-weight])
            } else {
            	table[i][j] = table[i-1][j]
            }
            """),
        ExactFitLeftOut("logic-rust-exact-fit-left-out",
            """
            if weight <= j {
                table[i][j] = table[i - 1][j].max(value + table[i - 1][j - weight]);
            } else {
                table[i][j] = table[i - 1][j];
            }
            """),
        ExactFitLeftOut("logic-ruby-exact-fit-left-out",
            """
            if j >= weight
              table[i][j] = [table[i - 1][j], value + table[i - 1][j - weight]].max
            else
              table[i][j] = table[i - 1][j]
            end
            """),
        ExactFitLeftOut("logic-lua-exact-fit-left-out",
            """
            if weight <= j then
              table_of[i][j] = math.max(table_of[i - 1][j], value + table_of[i - 1][j - weight])
            else
              table_of[i][j] = table_of[i - 1][j]
            end
            """),
        ExactFitLeftOut("logic-php-exact-fit-left-out",
            """
            if ($weight <= $j) {
                $table[$i][$j] = max($table[$i - 1][$j], $value + $table[$i - 1][$j - $weight]);
            } else {
                $table[$i][$j] = $table[$i - 1][$j];
            }
            """),
        ExactFitLeftOut("logic-perl-exact-fit-left-out",
            """
            if ($weight <= $j) {
                $table[$i][$j] = max($table[$i - 1][$j], $value + $table[$i - 1][$j - $weight]);
            } else {
                $table[$i][$j] = $table[$i - 1][$j];
            }
            """),
        ExactFitLeftOut("logic-powershell-exact-fit-left-out",
            """
            if ($weight -le $j) {
                $table[$i][$j] = [math]::Max($table[$i - 1][$j], $value + $table[$i - 1][$j - $weight])
            } else {
                $table[$i][$j] = $table[$i - 1][$j]
            }
            """),
        ExactFitLeftOut("logic-dart-exact-fit-left-out",
            """
            if (weight <= j) {
              table[i][j] = max(table[i - 1][j], value + table[i - 1][j - weight]);
            } else {
              table[i][j] = table[i - 1][j];
            }
            """),
        ExactFitLeftOut("logic-scala-exact-fit-left-out",
            """
            if (weight <= j) {
              table(i)(j) = math.max(table(i - 1)(j), value + table(i - 1)(j - weight))
            } else {
              table(i)(j) = table(i - 1)(j)
            }
            """),
        ExactFitLeftOut("logic-fortran-exact-fit-left-out",
            """
            if (weights(i) <= j) then
              table(i, j) = max(table(i - 1, j), values(i) + table(i - 1, j - weights(i)))
            else
              table(i, j) = table(i - 1, j)
            end if
            """),
        MatchCharged("logic-python-match-charged",
            """
            if first[i - 1] == second[j - 1]:
                table[i][j] = table[i - 1][j - 1]
            else:
                table[i][j] = 1 + min(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1])
            """),
        MatchCharged("logic-java-match-charged",
            """
            if (first.charAt(i - 1) == second.charAt(j - 1)) {
                table[i][j] = table[i - 1][j - 1];
            } else {
                table[i][j] = 1 + Math.min(table[i - 1][j], Math.min(table[i][j - 1], table[i - 1][j - 1]));
            }
            """),
        MatchCharged("logic-csharp-match-charged",
            """
            if (first[i - 1] == second[j - 1])
            {
                table[i, j] = table[i - 1, j - 1];
            }
            else
            {
                table[i, j] = 1 + Math.Min(table[i - 1, j], Math.Min(table[i, j - 1], table[i - 1, j - 1]));
            }
            """),
        MatchCharged("logic-js-match-charged",
            """
            if (first[i - 1] === second[j - 1]) {
              table[i][j] = table[i - 1][j - 1];
            } else {
              table[i][j] = 1 + Math.min(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1]);
            }
            """),
        MatchCharged("logic-c-match-charged",
            """
            if (first[i - 1] == second[j - 1]) {
                table[i][j] = table[i - 1][j - 1];
            } else {
                table[i][j] = 1 + MIN3(table[i - 1][j], table[i][j - 1], table[i - 1][j - 1]);
            }
            """),
        MatchCharged("logic-cpp-match-charged",
            """
            if (first[i - 1] == second[j - 1]) {
                table[i][j] = table[i - 1][j - 1];
            } else {
                table[i][j] = 1 + std::min({table[i - 1][j], table[i][j - 1], table[i - 1][j - 1]});
            }
            """),
        MatchCharged("logic-go-match-charged",
            """
            if first[i-1] == second[j-1] {
            	table[i][j] = table[i-1][j-1]
            } else {
            	table[i][j] = 1 + min(table[i-1][j], table[i][j-1], table[i-1][j-1])
            }
            """),
        MatchCharged("logic-rust-match-charged",
            """
            if a[i - 1] == b[j - 1] {
                table[i][j] = table[i - 1][j - 1];
            } else {
                table[i][j] = 1 + table[i - 1][j].min(table[i][j - 1]).min(table[i - 1][j - 1]);
            }
            """),
        MatchCharged("logic-ruby-match-charged",
            """
            if first[i - 1] == second[j - 1]
              table[i][j] = table[i - 1][j - 1]
            else
              table[i][j] = 1 + [table[i - 1][j], table[i][j - 1], table[i - 1][j - 1]].min
            end
            """),
        MatchCharged("logic-lua-match-charged",
            """
            if first:sub(i, i) == second:sub(j, j) then
              distances[i][j] = distances[i - 1][j - 1]
            else
              distances[i][j] = 1 + math.min(distances[i - 1][j], distances[i][j - 1], distances[i - 1][j - 1])
            end
            """),
        MatchCharged("logic-php-match-charged",
            """
            if ($first[$i - 1] === $second[$j - 1]) {
                $table[$i][$j] = $table[$i - 1][$j - 1];
            } else {
                $table[$i][$j] = 1 + min($table[$i - 1][$j], $table[$i][$j - 1], $table[$i - 1][$j - 1]);
            }
            """),
        MatchCharged("logic-perl-match-charged",
            """
            if (substr($first, $i - 1, 1) eq substr($second, $j - 1, 1)) {
                $table[$i][$j] = $table[$i - 1][$j - 1];
            } else {
                $table[$i][$j] = 1 + min($table[$i - 1][$j], $table[$i][$j - 1], $table[$i - 1][$j - 1]);
            }
            """),
        MatchCharged("logic-powershell-match-charged",
            """
            if ($first[$i - 1] -eq $second[$j - 1]) {
                $table[$i][$j] = $table[$i - 1][$j - 1]
            } else {
                $table[$i][$j] = 1 + [math]::Min($table[$i - 1][$j], [math]::Min($table[$i][$j - 1], $table[$i - 1][$j - 1]))
            }
            """),
        MatchCharged("logic-dart-match-charged",
            """
            if (first[i - 1] == second[j - 1]) {
              table[i][j] = table[i - 1][j - 1];
            } else {
              table[i][j] = 1 + min(table[i - 1][j], min(table[i][j - 1], table[i - 1][j - 1]));
            }
            """),
        MatchCharged("logic-scala-match-charged",
            """
            if (first(i - 1) == second(j - 1)) {
              table(i)(j) = table(i - 1)(j - 1)
            } else {
              table(i)(j) = 1 + math.min(table(i - 1)(j), math.min(table(i)(j - 1), table(i - 1)(j - 1)))
            }
            """),
        MatchCharged("logic-fortran-match-charged",
            """
            if (first(i:i) == second(j:j)) then
              table(i, j) = table(i - 1, j - 1)
            else
              table(i, j) = 1 + min(table(i - 1, j), table(i, j - 1), table(i - 1, j - 1))
            end if
            """),
        NoStopWhenEmpty("logic-python-no-stop-when-empty",
            """
            if total < 0 or not coins:
                return 0

            first, *rest = coins
            return possible_change(coins, total - first) + possible_change(rest, total)
            """),
        NoStopWhenEmpty("logic-java-no-stop-when-empty",
            """
            if (total < 0 || coins.length == 0) {
                return 0;
            }
            int first = coins[0];
            int[] rest = Arrays.copyOfRange(coins, 1, coins.length);
            return possibleChange(coins, total - first) + possibleChange(rest, total);
            """),
        NoStopWhenEmpty("logic-csharp-no-stop-when-empty",
            """
            if (total < 0 || coins.Count == 0)
            {
                return 0;
            }

            var first = coins[0];
            return PossibleChange(coins, total - first) + PossibleChange(coins.Skip(1).ToList(), total);
            """),
        NoStopWhenEmpty("logic-js-no-stop-when-empty",
            """
            if (total < 0 || coins.length === 0) return 0;
            const [first, ...rest] = coins;
            return possibleChange(coins, total - first) + possibleChange(rest, total);
            """),
        NoStopWhenEmpty("logic-c-no-stop-when-empty",
            """
            if (total < 0 || count == 0) return 0;
            return possible_change(coins, count, total - coins[0]) + possible_change(coins + 1, count - 1, total);
            """),
        NoStopWhenEmpty("logic-cpp-no-stop-when-empty",
            """
            if (total < 0 || coins.empty()) {
                return 0;
            }
            std::vector<int> rest(coins.begin() + 1, coins.end());
            return possibleChange(coins, total - coins[0]) + possibleChange(rest, total);
            """),
        NoStopWhenEmpty("logic-go-no-stop-when-empty",
            """
            if total < 0 || len(coins) == 0 {
            	return 0
            }
            return possibleChange(coins, total-coins[0]) + possibleChange(coins[1:], total)
            """),
        NoStopWhenEmpty("logic-rust-no-stop-when-empty",
            """
            if total < 0 || coins.is_empty() {
                return 0;
            }
            possible_change(coins, total - coins[0]) + possible_change(&coins[1..], total)
            """),
        NoStopWhenEmpty("logic-ruby-no-stop-when-empty",
            """
            if total < 0 || coins.empty?
              return 0
            end

            first, *rest = coins
            possible_change(coins, total - first) + possible_change(rest, total)
            """),
        NoStopWhenEmpty("logic-lua-no-stop-when-empty",
            """
            if total < 0 or #coins == 0 then
              return 0
            end
            local rest = { table.unpack(coins, 2) }
            return possible_change(coins, total - coins[1]) + possible_change(rest, total)
            """),
        NoStopWhenEmpty("logic-php-no-stop-when-empty",
            """
            if ($total < 0 || count($coins) === 0) {
                return 0;
            }
            return possibleChange($coins, $total - $coins[0]) + possibleChange(array_slice($coins, 1), $total);
            """),
        NoStopWhenEmpty("logic-perl-no-stop-when-empty",
            """
            if ($total < 0 || !@$coins) {
                return 0;
            }
            my ($first, @rest) = @$coins;
            return possible_change($coins, $total - $first) + possible_change(\@rest, $total);
            """),
        NoStopWhenEmpty("logic-powershell-no-stop-when-empty",
            """
            if ($total -lt 0 -or $coins.Count -eq 0) {
                return 0
            }
            $rest = @($coins | Select-Object -Skip 1)
            return (Get-PossibleChange $coins ($total - $coins[0])) + (Get-PossibleChange $rest $total)
            """),
        NoStopWhenEmpty("logic-dart-no-stop-when-empty",
            """
            if (total < 0 || coins.isEmpty) {
              return 0;
            }
            return possibleChange(coins, total - coins[0]) + possibleChange(coins.sublist(1), total);
            """),
        NoStopWhenEmpty("logic-scala-no-stop-when-empty",
            """
            if (total == 0) {
              1
            } else if (total < 0 || coins.isEmpty) {
              0
            } else {
              possibleChange(coins, total - coins.head) + possibleChange(coins.tail, total)
            }
            """),
        NoStopWhenEmpty("logic-fortran-no-stop-when-empty",
            """
            if (total == 0) then
              ways = 1
            else if (total < 0 .or. size(coins) == 0) then
              ways = 0
            else
              ways = possible_change(coins, total - coins(1)) + possible_change(coins(2:), total)
            end if
            """),
    ];
}
