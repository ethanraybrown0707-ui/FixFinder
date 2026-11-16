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

    private static GuideEntry TwoStepsOneAsked(string id, string example) =>
        GuideFor([id], "A pointer moved two on, but asked about one",
            "Floyd's cycle search moves a tortoise one node on and a hare two each time round: if the list loops back on itself " +
            "they meet, and if it does not the hare reaches the end. Moving two on, the hare can land on nothing - the next node " +
            "is there, the one after is not - so before asking for its next node the loop must ask whether the hare is there at " +
            "all. Here it asks only about the next node: if hare.successor is None.",
            "On a list that does not loop - one of four nodes is enough - the hare lands on nothing, and asking for its next node " +
            "fails: Python's AttributeError, Java's NullPointerException, Go's nil pointer dereference.",
            "Ask about the hare itself first: if hare is None or hare.successor is None.",
            example);

    private static GuideEntry PlaceNotTakenAway(string id, string example) =>
        GuideFor([id], "A place in the part above the pivot asked for as if counted from the start",
            "Quickselect finds the item that would be at place k if the list were sorted, without sorting it. It takes a pivot " +
            "and splits the items into those below it and those above it. If k falls among the items below, it looks for place " +
            "k in that part. If k falls past every item up to the pivot, it looks among the items above - but there k is no " +
            "longer the place: the items below the pivot and those equal to it come first, so within the part above the place " +
            "is k less how many they are. Here it asks the part above for place k as it is.",
            "It looks for a later item than the one wanted - the third smallest of 1, 9, 2, 3 comes out as 9 where 3 is right - " +
            "or for a place past the end of the part, and then each call has fewer items and the same k until a part has none: " +
            "reading its first item fails - Python's IndexError, Java's IndexOutOfBoundsException, Rust's index out of bounds - " +
            "or, where a missing item reads as nothing, it calls itself until the stack runs out - JavaScript's RangeError - or " +
            "past the time it was given, as Lua's and PowerShell's did.",
            "Take away the items up to the pivot: kth(above, k - num_lessoreq).",
            example);

    private static GuideEntry SumNeverStartedAgain(string id, string example) =>
        GuideFor([id], "The sum of a run of items never started again",
            "The largest sum of a run of items in a list can be worked out in one pass - Kadane's way. The pass keeps two sums: " +
            "the best run ending at the item it is at, and the largest of those so far. A run whose sum has fallen below nothing " +
            "only lowers whatever follows it, so when that happens the best run ending at the next item starts after it - its " +
            "sum starts again at nothing. Here each item is added and the sum never starts again, so it is the sum of every " +
            "item so far.",
            "It gives the largest sum of the list's first items, not of any run of them: 4 for 4, -5, 2, 1, -1, 3, where the " +
            "run 2, 1, -1, 3 makes 5, and 2 for -2, 1, -3, 4, -1, 2, 1, -5, 4, where 4, -1, 2, 1 makes 6.",
            "Start the run again when its sum falls below nothing: max_ending_here = max(0, max_ending_here + x).",
            example);

    private static GuideEntry AnyInsteadOfAll(string id, string example) =>
        GuideFor([id], "A number kept as prime when any prime leaves a remainder",
            "A prime search can keep each number that no prime found so far divides: divided by each of them, the number must " +
            "leave a remainder. That asks it of all of them, not any - any is true when one of them leaves a remainder, " +
            "whatever the others do. Here the test asks any: any(n % p > 0 for p in primes).",
            "With no primes found yet, any of none is false, so the first prime, 2, is never kept, nor anything after it: the " +
            "search finds no primes at all. Started with 2, it kept every number from 3 to 10 - 4 because 3 does not divide it.",
            "Ask that every one leaves a remainder: all(n % p > 0 for p in primes).",
            example);

    private static GuideEntry XorInsteadOfAnd(string id, string example) =>
        GuideFor([id], "The lowest set bit taken away with xor instead of and",
            "A count of a number's set bits can take the lowest one away each time round until nothing is left. n - 1 is n " +
            "with its lowest set bit cleared and every bit below it set, so n & (n - 1) is n without its lowest set bit, and " +
            "the loop runs once for each set bit. Here it is n ^ (n - 1): xor keeps the bits that differ - that bit and every " +
            "bit below it.",
            "n ^ (n - 1) is never 0 once n is above 0 - 6 becomes 3, then 1, and 1 ^ 0 is 1 again - so a loop waiting for n to " +
            "reach 0 never ends, and the count is never printed.",
            "Clear the lowest set bit with and: n &= n - 1.",
            example);

    private static GuideEntry EuclidPairNotMoved(string id, string example) =>
        GuideFor([id], "Euclid's pair gone on with in the wrong order",
            "Euclid's way finds the greatest common divisor of two numbers by going on with a smaller pair that has the same " +
            "one: the number divided by, and the remainder - gcd(a, b) is gcd(b, a % b) - until the remainder is 0. Here it " +
            "goes on with the remainder and the number divided by: gcd(a % b, b).",
            "The search stops when the second number is 0, and it is given back as it was, so it never is: a % b is below b, " +
            "and taking the remainder by b again changes nothing - gcd(35, 21) goes on as gcd(14, 21) for ever. A call runs " +
            "until the stack runs out - Python's RecursionError, Java's StackOverflowError - and a loop never ends.",
            "Go on with the number divided by and the remainder: gcd(b, a % b).",
            example);

    private static GuideEntry BracketsLeftOpen(string id, string example) =>
        GuideFor([id], "Brackets left open said to be valid",
            "A check of a string's brackets can count the ones still open: one more for each opening bracket, one fewer for " +
            "each closing one. A closing bracket with nothing open makes the count go below 0, and the check says no at once. " +
            "At the end every bracket opened must have been closed - the count must be 0. Here the end says yes whatever the " +
            "count is.",
            "Brackets opened and never closed are said to be valid: ((, ((() and ( all come out valid, though )( is caught.",
            "Ask at the end that nothing is left open: return depth == 0.",
            example);

    private static GuideEntry DigitsWrittenBackwards(string id, string example) =>
        GuideFor([id], "A number's digits written backwards",
            "A number can be written in another base by taking its remainder by the base - its last digit - and dividing it by " +
            "the base to drop that digit, again and again until nothing is left. The remainder gives the last digit first, then " +
            "the one before it, so each digit found goes in front of the ones found already. Here each one is put after them: " +
            "result = result + alphabet[i].",
            "The digits come out backwards: 31 in base 16 as F1 where 1F is right, 41 in base 2 as 100101 where 101001 is, and " +
            "3005 in base 16 as DBB where BBD is. A number whose digits read the same both ways comes out right - 8227 in base " +
            "18 is 1771 either way - which can hide it.",
            "Put each digit in front: result = alphabet[i] + result.",
            example);

    private static GuideEntry OperandsTakenBackwards(string id, string example) =>
        GuideFor([id], "Two values taken off a stack used the wrong way round",
            "A reverse Polish expression puts its numbers on a stack, and each operator takes the top two off and puts back what " +
            "it makes. The value taken first was pushed last: it is the right-hand operand, and the one taken after it the " +
            "left-hand one - 10 2 - is 10 - 2. Here the value taken first is used on the left.",
            "Adding and multiplying come out right whichever way round they go; subtracting and dividing come out turned round: " +
            "10 2 - 3 - gives 11 where 5 is right, and 2 3 4 * - gives 10 where -10 is.",
            "Use the value taken first on the right: op(token, b, a) - or name it so, taking b off first and then a.",
            example);

    private static GuideEntry LoopOneShort(string id, string example) =>
        GuideFor([id], "A loop over a row's places that stops one short",
            "Pascal's triangle builds each row from the one before: each number is the two above it added - the one up and to " +
            "the left, and the one straight up - with 0 where there is none. Row r has r + 1 numbers, so the loop over its places " +
            "goes from 0 up to r itself, and the guard c < r, for the place with nothing straight above it, is there for c = r. " +
            "Here the loop stops before r: range(0, r), c < r - so the guard can never fail.",
            "Each row comes out one number short, and the next row, built from it, reads past its end: Python's IndexError, " +
            "Java's IndexOutOfBoundsException, C++'s out_of_range. Where a missing number reads as 0 or nothing, the rows print " +
            "one short - 1|1|1 1|1 2 1|1 3 3 1 where 1|1 1|1 2 1|1 3 3 1|1 4 6 4 1 is right - or, in JavaScript, as NaN.",
            "Go up to r as well: range(0, r + 1).",
            example);

    private static GuideEntry LastFactorDropped(string id, string example) =>
        GuideFor([id], "A number's prime factors losing the last one",
            "A number's prime factors can be found by looking for the smallest number that divides it, from 2 up to its square " +
            "root, and going on with what is left once that is divided out. A number with no factor up to its square root is " +
            "a prime: it is its own last factor. Here, when no factor is found, nothing is given back.",
            "Every number loses its last prime - 12 comes out as 2 2 where 2 2 3 is right, 60 as 2 2 3, 100 as 2 2 5 - and a " +
            "prime such as 13 or 97 has no factors at all.",
            "Give back the number itself when no factor is found: return [n].",
            example);

    private static GuideEntry EmptyChoiceMissing(string id, string example) =>
        GuideFor([id], "Choices made with no empty choice at the bottom",
            "Every choice of k items can be made by choosing a first item and then, by calling the same function, every choice " +
            "of the rest - putting the first item in front of each one. With nothing left to choose there is still one choice: " +
            "choosing nothing, the empty one, and putting an item in front of it gives each choice of one item. Here the base " +
            "case gives back no choices at all.",
            "With nothing at the bottom to put an item in front of, every call gives back nothing: the choices of 3 from 1 to 5 " +
            "come out as none, where there are 10.",
            "Give back the one empty choice: return [[]].",
            example);

    private static GuideEntry LeafFlattenedAgain(string id, string example) =>
        GuideFor([id], "An item that is not a list given back to the flattening",
            "Flattening a list of items and lists goes through its items: a list is flattened in turn, by calling the same " +
            "function, and an item that is not a list goes into the result as it is. Here that item is given to the flattening " +
            "function again: yield flatten(x) where yield x is meant.",
            "Python's flatten is a generator, and calling it only makes a generator object: five came out where 1 2 3 4 5 is " +
            "right. Where the function goes through what it is given, the item - or a list holding just it - goes back in for " +
            "ever, until the stack runs out: Java's StackOverflowError, Ruby's stack level too deep.",
            "Give back the item itself: yield x.",
            example);

    private static GuideEntry SubsetsWithoutFirstDropped(string id, string example) =>
        GuideFor([id], "Subsets given back without the ones that leave the first item out",
            "Every subset of a list can be made from the subsets of the rest of it, found by calling the same function: each " +
            "subset either leaves the first item out - one of the rest's subsets as it is - or holds it - one of them with the " +
            "item put in front. Here only the second half is given back: the rest's own subsets are left out.",
            "Each call gives back as many subsets as the call below it, and the one at the bottom gives back one, the empty set " +
            "- so the subsets of 1 2 3 come out as [1 2 3] alone, where there are 8.",
            "Give back the rest's subsets too: return rest_subsets + [[first] + subset for subset in rest_subsets].",
            example);

    private static GuideEntry RootGuessNotSquared(string id, string example) =>
        GuideFor([id], "A square root's guess tested instead of its square",
            "Newton's method finds a square root by guessing and improving the guess: each new guess is the average of the last " +
            "and the number over it, (guess + x / guess) / 2, and it is done when the guess times itself is within the tolerance " +
            "of the number. Here the guess itself is tested against the number, abs(x - guess), not its square.",
            "The guess heads for the square root, which is not near the number unless the number is near 1 - for 2, the root " +
            "1.414 stays 0.586 away - so the test is never passed and the loop never ends.",
            "Test the guess's square: abs(x - guess * guess) > epsilon.",
            example);

    private static GuideEntry PreviousNeverMoved(string id, string example) =>
        GuideFor([id], "A list reversed without moving on the node before",
            "Reversing a linked list in a loop keeps two things: the node it is at, and the reversed list so far - the node " +
            "before, which starts as nothing. Each time round, it keeps the next node, points this node back at the one before, " +
            "and then moves both on: the one before becomes this node, and this node the next. Here the one before is never " +
            "moved on, so it stays nothing.",
            "Every node is pointed back at nothing, and the reversed list given back is empty: reversing 1, 2, 3 gives nothing at " +
            "all.",
            "Move the one before on, before going to the next node: prevnode = node.",
            example);

    private static GuideEntry TakenFromEmpty(string id, string example) =>
        GuideFor([id], "A queue taken from in a loop that never asks whether it is empty",
            "A search such as breadth-first takes the next place to look at from a queue - queue.popleft(), queue.removeFirst(), " +
            "queue.shift() - and puts the places next to it on the end. When what is looked for is there, it turns up and the " +
            "search returns. When it is not, every place is taken in turn and the queue runs out. Here the loop is written to go " +
            "round for ever - while True - and nothing in it asks whether the queue has anything left.",
            "Searching for something that cannot be reached, the next take finds the queue empty and fails - Python's pop from an " +
            "empty deque, Java's NoSuchElementException, Go's index out of range - or, where a missing item reads as nothing, the " +
            "loop goes round for ever.",
            "Make the loop ask: while queue:, while (!queue.isEmpty()) - and give back what the search finds when it ends with " +
            "nothing, return false, after it.",
            example);

    private static GuideEntry BestOverwritten(string id, string example) =>
        GuideFor([id], "The best so far written over without asking whether it is better",
            "A loop keeping the best found so far - the longest, the largest, the smallest - gives it a new value only when the " +
            "new one is better: if length + 1 > longest, or longest = max(longest, length + 1). Here the test around the line " +
            "does not make sure of that. QuixBugs' lis asks length == longest or val < arr[ends[length + 1]]: the first half " +
            "makes length + 1 longer, but the second holds for another reason, and then a shorter length is written over the " +
            "longest.",
            "The best can go down as well as up, so the answer is whatever was written last under that test - for lis on 4, 1, 5, " +
            "3, 7, 6, 2, 2 where 3 is right.",
            "Keep the better of the two: longest = max(longest, length + 1).",
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
        BestOverwritten("logic-python-best-overwritten",
            """
            if length == longest or val < arr[ends[length + 1]]:
                ends[length + 1] = i
                longest = max(longest, length + 1)
            """),
        BestOverwritten("logic-java-best-overwritten",
            """
            if (length == longest || items[i] < items[ends[length + 1]]) {
                ends[length + 1] = i;
                longest = Math.max(longest, length + 1);
            }
            """),
        BestOverwritten("logic-csharp-best-overwritten",
            """
            if (length == longest || items[i] < items[ends[length + 1]])
            {
                ends[length + 1] = i;
                longest = Math.Max(longest, length + 1);
            }
            """),
        BestOverwritten("logic-js-best-overwritten",
            """
            if (length === longest || items[i] < items[ends[length + 1]]) {
              ends[length + 1] = i;
              longest = Math.max(longest, length + 1);
            }
            """),
        BestOverwritten("logic-c-best-overwritten",
            """
            if (length == longest || items[i] < items[ends[length + 1]]) {
                ends[length + 1] = i;
                longest = length + 1 > longest ? length + 1 : longest;
            }
            """),
        BestOverwritten("logic-cpp-best-overwritten",
            """
            if (length == longest || items[i] < items[ends[length + 1]]) {
                ends[length + 1] = i;
                longest = std::max(longest, length + 1);
            }
            """),
        BestOverwritten("logic-go-best-overwritten",
            """
            if length == longest || items[i] < items[ends[length+1]] {
            	ends[length+1] = i
            	longest = max(longest, length + 1)
            }
            """),
        BestOverwritten("logic-rust-best-overwritten",
            """
            if length == longest || items[i] < items[ends[length + 1]] {
                ends[length + 1] = i;
                longest = longest.max(length + 1);
            }
            """),
        BestOverwritten("logic-ruby-best-overwritten",
            """
            if length == longest || value < items[ends[length + 1]]
              ends[length + 1] = i
              longest = [longest, length + 1].max
            end
            """),
        BestOverwritten("logic-lua-best-overwritten",
            """
            if length == longest or items[i] < items[ends[length + 1]] then
              ends[length + 1] = i
              longest = math.max(longest, length + 1)
            end
            """),
        BestOverwritten("logic-php-best-overwritten",
            """
            if ($length === $longest || $items[$i] < $items[$ends[$length + 1]]) {
                $ends[$length + 1] = $i;
                $longest = max($longest, $length + 1);
            }
            """),
        BestOverwritten("logic-perl-best-overwritten",
            """
            if ($length == $longest || $items[$i] < $items[$ends[$length + 1]]) {
                $ends[$length + 1] = $i;
                $longest = $length + 1 > $longest ? $length + 1 : $longest;
            }
            """),
        BestOverwritten("logic-powershell-best-overwritten",
            """
            if ($length -eq $longest -or $items[$i] -lt $items[$ends[$length + 1]]) {
                $ends[$length + 1] = $i
                $longest = [math]::Max($longest, $length + 1)
            }
            """),
        BestOverwritten("logic-dart-best-overwritten",
            """
            if (length == longest || items[i] < items[ends[length + 1]]) {
              ends[length + 1] = i;
              longest = max(longest, length + 1);
            }
            """),
        BestOverwritten("logic-scala-best-overwritten",
            """
            if (length == longest || items(i) < items(ends(length + 1))) {
              ends(length + 1) = i
              longest = math.max(longest, length + 1)
            }
            """),
        BestOverwritten("logic-fortran-best-overwritten",
            """
            if (length == longest .or. shorter) then
              ends(length + 1) = i
              longest = max(longest, length + 1)
            end if
            """),
        TakenFromEmpty("logic-python-taken-from-empty",
            """
            while queue:
                node = queue.popleft()
                if node == goal:
                    return True
                for successor in graph[node]:
                    if successor not in seen:
                        seen.add(successor)
                        queue.append(successor)
            """),
        TakenFromEmpty("logic-java-taken-from-empty",
            """
            while (!queue.isEmpty()) {
                int node = queue.removeFirst();
                if (node == goal) {
                    return true;
                }
                for (int successor : graph.get(node)) {
                    if (seen.add(successor)) {
                        queue.addLast(successor);
                    }
                }
            }
            return false;
            """),
        TakenFromEmpty("logic-csharp-taken-from-empty",
            """
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node == goal)
                {
                    return true;
                }

                foreach (var successor in graph[node])
                {
                    if (seen.Add(successor))
                    {
                        queue.Enqueue(successor);
                    }
                }
            }
            """),
        TakenFromEmpty("logic-js-taken-from-empty",
            """
            while (queue.length > 0) {
              const node = queue.shift();
              if (node === goal) return true;
              for (const successor of graph[node]) {
                if (!seen.has(successor)) {
                  seen.add(successor);
                  queue.push(successor);
                }
              }
            }
            """),
        TakenFromEmpty("logic-c-taken-from-empty",
            """
            while (head < tail) {
                int node = queue[head++];
                if (node == goal) return 1;
                for (int successor = 0; successor < 5; successor++) {
                    if (graph[node][successor] && !seen[successor]) {
                        seen[successor] = 1;
                        queue[tail++] = successor;
                    }
                }
            }
            """),
        TakenFromEmpty("logic-cpp-taken-from-empty",
            """
            while (!queue.empty()) {
                int node = queue.front();
                queue.pop();
                if (node == goal) {
                    return true;
                }
                for (int successor : graph[node]) {
                    if (seen.insert(successor).second) {
                        queue.push(successor);
                    }
                }
            }
            """),
        TakenFromEmpty("logic-go-taken-from-empty",
            """
            for len(queue) > 0 {
            	node := queue[0]
            	queue = queue[1:]
            	if node == goal {
            		return true
            	}
            	for _, successor := range graph[node] {
            		if !seen[successor] {
            			seen[successor] = true
            			queue = append(queue, successor)
            		}
            	}
            }
            """),
        TakenFromEmpty("logic-rust-taken-from-empty",
            """
            while !queue.is_empty() {
                let node = queue.pop_front().unwrap();
                if node == goal {
                    return true;
                }
                for &successor in &graph[node] {
                    if seen.insert(successor) {
                        queue.push_back(successor);
                    }
                }
            }
            """),
        TakenFromEmpty("logic-ruby-taken-from-empty",
            """
            until queue.empty?
              node = queue.shift
              return true if node == goal
              graph[node].each do |successor|
                next if seen.include?(successor)
                seen << successor
                queue << successor
              end
            end
            """),
        TakenFromEmpty("logic-lua-taken-from-empty",
            """
            while #queue > 0 do
              local node = table.remove(queue, 1)
              if node == goal then
                return true
              end
              for _, successor in ipairs(graph[node]) do
                if not seen[successor] then
                  seen[successor] = true
                  table.insert(queue, successor)
                end
              end
            end
            """),
        TakenFromEmpty("logic-php-taken-from-empty",
            """
            while (count($queue) > 0) {
                $node = array_shift($queue);
                if ($node === $goal) {
                    return true;
                }
                foreach ($graph[$node] as $successor) {
                    if (!isset($seen[$successor])) {
                        $seen[$successor] = true;
                        $queue[] = $successor;
                    }
                }
            }
            """),
        TakenFromEmpty("logic-perl-taken-from-empty",
            """
            while (@queue) {
                my $node = shift @queue;
                return 1 if $node == $goal;
                for my $successor (@{ $graph->[$node] }) {
                    next if $seen{$successor}++;
                    push @queue, $successor;
                }
            }
            """),
        TakenFromEmpty("logic-powershell-taken-from-empty",
            """
            while ($queue.Count -gt 0) {
                $node = $queue.Dequeue()
                if ($node -eq $goal) {
                    return $true
                }
                foreach ($successor in $graph[$node]) {
                    if (-not $seen.ContainsKey($successor)) {
                        $seen[$successor] = $true
                        $queue.Enqueue($successor)
                    }
                }
            }
            """),
        TakenFromEmpty("logic-dart-taken-from-empty",
            """
            while (queue.isNotEmpty) {
              final node = queue.removeFirst();
              if (node == goal) {
                return true;
              }
              for (final successor in graph[node]) {
                if (seen.add(successor)) {
                  queue.add(successor);
                }
              }
            }
            """),
        TakenFromEmpty("logic-scala-taken-from-empty",
            """
            while (queue.nonEmpty) {
              val node = queue.dequeue()
              if (node == goal) {
                return true
              }
              for (successor <- graph(node) if seen.add(successor)) {
                queue.enqueue(successor)
              }
            }
            """),
        TakenFromEmpty("logic-fortran-taken-from-empty",
            """
            do while (head <= tail)
              node = queue(head)
              head = head + 1
              if (node == 5) then
                found = .true.
                exit
              end if
              do successor = 1, 5
                if (graph(node, successor) .and. .not. seen(successor)) then
                  seen(successor) = .true.
                  tail = tail + 1
                  queue(tail) = successor
                end if
              end do
            end do
            """),
        PreviousNeverMoved("logic-python-previous-never-moved",
            """
            while node:
                nextnode = node.successor
                node.successor = prevnode
                prevnode = node
                node = nextnode
            """),
        PreviousNeverMoved("logic-java-previous-never-moved",
            """
            while (node != null) {
                nextnode = node.getSuccessor();
                node.setSuccessor(prevnode);
                prevnode = node;
                node = nextnode;
            }
            """),
        PreviousNeverMoved("logic-csharp-previous-never-moved",
            """
            while (node != null)
            {
                var following = node.Next;
                node.Next = previous;
                previous = node;
                node = following;
            }
            """),
        PreviousNeverMoved("logic-js-previous-never-moved",
            """
            while (node !== null) {
              const following = node.next;
              node.next = previous;
              previous = node;
              node = following;
            }
            """),
        PreviousNeverMoved("logic-c-previous-never-moved",
            """
            while (node != NULL) {
                struct node *following = node->next;
                node->next = previous;
                previous = node;
                node = following;
            }
            """),
        PreviousNeverMoved("logic-cpp-previous-never-moved",
            """
            while (node != nullptr) {
                Node* following = node->next;
                node->next = previous;
                previous = node;
                node = following;
            }
            """),
        PreviousNeverMoved("logic-go-previous-never-moved",
            """
            for current != nil {
            	following := current.next
            	current.next = previous
            	previous = current
            	current = following
            }
            """),
        PreviousNeverMoved("logic-rust-previous-never-moved",
            """
            while let Some(index) = node {
                let following = next[index];
                next[index] = previous;
                previous = Some(index);
                node = following;
            }
            """),
        PreviousNeverMoved("logic-ruby-previous-never-moved",
            """
            while node
              following = node.successor
              node.successor = previous
              previous = node
              node = following
            end
            """),
        PreviousNeverMoved("logic-lua-previous-never-moved",
            """
            while node do
              local following = node.next
              node.next = previous
              previous = node
              node = following
            end
            """),
        PreviousNeverMoved("logic-php-previous-never-moved",
            """
            while ($node !== null) {
                $following = $node->next;
                $node->next = $previous;
                $previous = $node;
                $node = $following;
            }
            """),
        PreviousNeverMoved("logic-perl-previous-never-moved",
            """
            while (defined $node) {
                my $following = $node->{next};
                $node->{next} = $previous;
                $previous = $node;
                $node = $following;
            }
            """),
        PreviousNeverMoved("logic-powershell-previous-never-moved",
            """
            while ($null -ne $node) {
                $following = $node.Next
                $node.Next = $previous
                $previous = $node
                $node = $following
            }
            """),
        PreviousNeverMoved("logic-dart-previous-never-moved",
            """
            while (node != null) {
              final following = node.next;
              node.next = previous;
              previous = node;
              node = following;
            }
            """),
        PreviousNeverMoved("logic-scala-previous-never-moved",
            """
            while (node != null) {
              val following = node.next
              node.next = previous
              previous = node
              node = following
            }
            """),
        PreviousNeverMoved("logic-fortran-previous-never-moved",
            """
            do while (node /= 0)
              following = next(node)
              next(node) = previous
              previous = node
              node = following
            end do
            """),
        TwoStepsOneAsked("logic-python-two-steps-one-asked",
            """
            while True:
                if hare is None or hare.successor is None:
                    return False
                tortoise = tortoise.successor
                hare = hare.successor.successor
                if hare is tortoise:
                    return True
            """),
        TwoStepsOneAsked("logic-java-two-steps-one-asked",
            """
            while (true) {
                if (hare == null || hare.getSuccessor() == null)
                    return false;
                tortoise = tortoise.getSuccessor();
                hare = hare.getSuccessor().getSuccessor();
                if (hare == tortoise)
                    return true;
            }
            """),
        TwoStepsOneAsked("logic-csharp-two-steps-one-asked",
            """
            while (true)
            {
                if (hare == null || hare!.Next == null)
                {
                    return false;
                }

                tortoise = tortoise!.Next;
                hare = hare!.Next!.Next;
                if (hare == tortoise)
                {
                    return true;
                }
            }
            """),
        TwoStepsOneAsked("logic-js-two-steps-one-asked",
            """
            while (true) {
              if (hare === null || hare.next === null) return false;
              tortoise = tortoise.next;
              hare = hare.next.next;
              if (hare === tortoise) return true;
            }
            """),
        TwoStepsOneAsked("logic-c-two-steps-one-asked",
            """
            while (1) {
                if (hare == NULL || hare->next == NULL) return 0;
                tortoise = tortoise->next;
                hare = hare->next->next;
                if (hare == tortoise) return 1;
            }
            """),
        TwoStepsOneAsked("logic-cpp-two-steps-one-asked",
            """
            while (true) {
                if (hare == nullptr || hare->next == nullptr) {
                    return false;
                }
                tortoise = tortoise->next;
                hare = hare->next->next;
                if (hare == tortoise) {
                    return true;
                }
            }
            """),
        TwoStepsOneAsked("logic-go-two-steps-one-asked",
            """
            for {
            	if hare == nil || hare.next == nil {
            		return false
            	}
            	tortoise = tortoise.next
            	hare = hare.next.next
            	if hare == tortoise {
            		return true
            	}
            }
            """),
        TwoStepsOneAsked("logic-rust-two-steps-one-asked",
            """
            loop {
                if hare == NONE || next[hare] == NONE {
                    return false;
                }
                tortoise = next[tortoise];
                hare = next[next[hare]];
                if hare == tortoise {
                    return true;
                }
            }
            """),
        TwoStepsOneAsked("logic-ruby-two-steps-one-asked",
            """
            loop do
              if hare.nil? || hare.successor.nil?
                return false
              end
              tortoise = tortoise.successor
              hare = hare.successor.successor
              return true if hare.equal?(tortoise)
            end
            """),
        TwoStepsOneAsked("logic-lua-two-steps-one-asked",
            """
            while true do
              if hare == nil or hare.next == nil then
                return false
              end
              tortoise = tortoise.next
              hare = hare.next.next
              if hare == tortoise then
                return true
              end
            end
            """),
        TwoStepsOneAsked("logic-php-two-steps-one-asked",
            """
            while (true) {
                if ($hare === null || $hare->next === null) {
                    return false;
                }
                $tortoise = $tortoise->next;
                $hare = $hare->next->next;
                if ($hare === $tortoise) {
                    return true;
                }
            }
            """),
        TwoStepsOneAsked("logic-powershell-two-steps-one-asked",
            """
            while ($true) {
                if ($null -eq $hare -or $null -eq $hare.Next) {
                    return $false
                }
                $tortoise = $tortoise.Next
                $hare = $hare.Next.Next
                if ([object]::ReferenceEquals($hare, $tortoise)) {
                    return $true
                }
            }
            """),
        TwoStepsOneAsked("logic-dart-two-steps-one-asked",
            """
            while (true) {
              if (hare == null || hare!.next == null) {
                return false;
              }
              tortoise = tortoise!.next;
              hare = hare!.next!.next;
              if (identical(hare, tortoise)) {
                return true;
              }
            }
            """),
        TwoStepsOneAsked("logic-scala-two-steps-one-asked",
            """
            while (true) {
              if (hare == null || hare.next == null) {
                return false
              }
              tortoise = tortoise.next
              hare = hare.next.next
              if (hare eq tortoise) {
                return true
              }
            }
            """),
        PlaceNotTakenAway("logic-python-place-not-taken-away",
            """
            num_less = len(below)
            num_lessoreq = len(arr) - len(above)

            if k < num_less:
                return kth(below, k)
            elif k >= num_lessoreq:
                return kth(above, k - num_lessoreq)
            else:
                return pivot
            """),
        PlaceNotTakenAway("logic-java-place-not-taken-away",
            """
            int numLess = below.size();
            int numLessOrEqual = arr.size() - above.size();
            if (k < numLess) {
                return kth(below, k);
            } else if (k >= numLessOrEqual) {
                return kth(above, k - numLessOrEqual);
            } else {
                return pivot;
            }
            """),
        PlaceNotTakenAway("logic-csharp-place-not-taken-away",
            """
            var lessOrEqual = items.Count - above.Count;
            if (k < below.Count)
            {
                return Kth(below, k);
            }

            if (k >= lessOrEqual)
            {
                return Kth(above, k - lessOrEqual);
            }

            return pivot;
            """),
        PlaceNotTakenAway("logic-js-place-not-taken-away",
            """
            const lessOrEqual = items.length - above.length;
            if (k < below.length) return kth(below, k);
            if (k >= lessOrEqual) return kth(above, k - lessOrEqual);
            return pivot;
            """),
        PlaceNotTakenAway("logic-c-place-not-taken-away",
            """
            int less_or_equal = count - num_above;
            if (k < num_below) return kth(below, num_below, k);
            if (k >= less_or_equal) return kth(above, num_above, k - less_or_equal);
            return pivot;
            """),
        PlaceNotTakenAway("logic-cpp-place-not-taken-away",
            """
            int lessOrEqual = static_cast<int>(items.size() - above.size());
            if (k < static_cast<int>(below.size())) {
                return kth(below, k);
            }
            if (k >= lessOrEqual) {
                return kth(above, k - lessOrEqual);
            }
            return pivot;
            """),
        PlaceNotTakenAway("logic-go-place-not-taken-away",
            """
            lessOrEqual := len(items) - len(above)
            if k < len(below) {
            	return kth(below, k)
            }
            if k >= lessOrEqual {
            	return kth(above, k - lessOrEqual)
            }
            return pivot
            """),
        PlaceNotTakenAway("logic-rust-place-not-taken-away",
            """
            let less_or_equal = items.len() - above.len();
            if k < below.len() {
                return kth(&below, k);
            }
            if k >= less_or_equal {
                return kth(&above, k - less_or_equal);
            }
            pivot
            """),
        PlaceNotTakenAway("logic-ruby-place-not-taken-away",
            """
            less_or_equal = items.length - above.length
            if k < below.length
              kth(below, k)
            elsif k >= less_or_equal
              kth(above, k - less_or_equal)
            else
              pivot
            end
            """),
        PlaceNotTakenAway("logic-lua-place-not-taken-away",
            """
            local less_or_equal = #items - #above
            if k <= #below then
              return kth(below, k)
            elseif k > less_or_equal then
              return kth(above, k - less_or_equal)
            end
            return pivot
            """),
        PlaceNotTakenAway("logic-php-place-not-taken-away",
            """
            $lessOrEqual = count($items) - count($above);
            if ($k < count($below)) {
                return kth($below, $k);
            }
            if ($k >= $lessOrEqual) {
                return kth($above, $k - $lessOrEqual);
            }
            return $pivot;
            """),
        PlaceNotTakenAway("logic-perl-place-not-taken-away",
            """
            my $less_or_equal = @$items - @above;
            if ($k < @below) {
                return kth(\@below, $k);
            }
            if ($k >= $less_or_equal) {
                return kth(\@above, $k - $less_or_equal);
            }
            return $pivot;
            """),
        PlaceNotTakenAway("logic-powershell-place-not-taken-away",
            """
            $lessOrEqual = $items.Count - $above.Count
            if ($k -lt $below.Count) {
                return Get-Kth $below $k
            }
            if ($k -ge $lessOrEqual) {
                return Get-Kth $above ($k - $lessOrEqual)
            }
            return $pivot
            """),
        PlaceNotTakenAway("logic-dart-place-not-taken-away",
            """
            final lessOrEqual = items.length - above.length;
            if (k < below.length) {
              return kth(below, k);
            }
            if (k >= lessOrEqual) {
              return kth(above, k - lessOrEqual);
            }
            return pivot;
            """),
        PlaceNotTakenAway("logic-scala-place-not-taken-away",
            """
            val lessOrEqual = items.length - above.length
            if (k < below.length) {
              kth(below, k)
            } else if (k >= lessOrEqual) {
              kth(above, k - lessOrEqual)
            } else {
              pivot
            }
            """),
        PlaceNotTakenAway("logic-fortran-place-not-taken-away",
            """
            less_or_equal = size(items) - count(items > pivot)
            if (k <= count(items < pivot)) then
              found = kth(pack(items, items < pivot), k)
            else if (k > less_or_equal) then
              found = kth(pack(items, items > pivot), k - less_or_equal)
            else
              found = pivot
            end if
            """),
        SumNeverStartedAgain("logic-python-sum-never-started-again",
            """
            for x in arr:
                max_ending_here = max(0, max_ending_here + x)
                max_so_far = max(max_so_far, max_ending_here)
            """),
        SumNeverStartedAgain("logic-java-sum-never-started-again",
            """
            for (int x : arr) {
                max_ending_here = Math.max(0, max_ending_here + x);
                max_so_far = Math.max(max_so_far, max_ending_here);
            }
            """),
        SumNeverStartedAgain("logic-csharp-sum-never-started-again",
            """
            foreach (var item in items)
            {
                current = Math.Max(0, current + item);
                if (current > best)
                {
                    best = current;
                }
            }
            """),
        SumNeverStartedAgain("logic-js-sum-never-started-again",
            """
            for (let i = 0; i < items.length; i++) {
              endingHere = Math.max(0, endingHere + items[i]);
              best = Math.max(best, endingHere);
            }
            """),
        SumNeverStartedAgain("logic-c-sum-never-started-again",
            """
            for (int i = 0; i < count; i++) {
                ending_here += items[i];
                if (ending_here < 0) ending_here = 0;
                if (ending_here > best) best = ending_here;
            }
            """),
        SumNeverStartedAgain("logic-cpp-sum-never-started-again",
            """
            for (int item : items) {
                current = current + item;
                if (current < 0) current = 0;
                best = std::max(best, current);
            }
            """),
        SumNeverStartedAgain("logic-go-sum-never-started-again",
            """
            for _, item := range items {
            	current += item
            	if current < 0 {
            		current = 0
            	}
            	if current > best {
            		best = current
            	}
            }
            """),
        SumNeverStartedAgain("logic-rust-sum-never-started-again",
            """
            for &item in items {
                ending_here = (ending_here + item).max(0);
                best = best.max(ending_here);
            }
            """),
        SumNeverStartedAgain("logic-ruby-sum-never-started-again",
            """
            items.each do |item|
              current = [0, current + item].max
              best = current if current > best
            end
            """),
        SumNeverStartedAgain("logic-lua-sum-never-started-again",
            """
            for _, item in ipairs(items) do
              ending_here = math.max(0, ending_here + item)
              best = math.max(best, ending_here)
            end
            """),
        SumNeverStartedAgain("logic-php-sum-never-started-again",
            """
            foreach ($items as $item) {
                $current = max(0, $current + $item);
                $best = max($best, $current);
            }
            """),
        SumNeverStartedAgain("logic-perl-sum-never-started-again",
            """
            for my $item (@$items) {
                $current += $item;
                $current = 0 if $current < 0;
                $best = $current if $current > $best;
            }
            """),
        SumNeverStartedAgain("logic-powershell-sum-never-started-again",
            """
            foreach ($item in $items) {
                $current = [Math]::Max(0, $current + $item)
                if ($current -gt $best) { $best = $current }
            }
            """),
        SumNeverStartedAgain("logic-dart-sum-never-started-again",
            """
            for (final item in items) {
              endingHere += item;
              if (endingHere < 0) endingHere = 0;
              if (endingHere > best) best = endingHere;
            }
            """),
        SumNeverStartedAgain("logic-scala-sum-never-started-again",
            """
            for (item <- items) {
              current = math.max(0, current + item)
              best = math.max(best, current)
            }
            """),
        SumNeverStartedAgain("logic-fortran-sum-never-started-again",
            """
            do i = 1, size(items)
              ending_here = ending_here + items(i)
              if (ending_here < 0) ending_here = 0
              max_subarray = max(max_subarray, ending_here)
            end do
            """),
        AnyInsteadOfAll("logic-python-any-instead-of-all",
            """
            for n in range(2, max + 1):
                if all(n % p > 0 for p in primes):
                    primes.append(n)
            """),
        AnyInsteadOfAll("logic-java-any-instead-of-all",
            """
            for (int n = 2; n <= max; n++) {
                final int number = n;
                if (primes.stream().allMatch(p -> number % p != 0)) {
                    primes.add(number);
                }
            }
            """),
        AnyInsteadOfAll("logic-csharp-any-instead-of-all",
            """
            for (var n = 2; n <= max; n++)
            {
                if (primes.All(p => n % p != 0))
                {
                    primes.Add(n);
                }
            }
            """),
        AnyInsteadOfAll("logic-js-any-instead-of-all",
            """
            for (let n = 2; n <= max; n++) {
              if (primes.every((p) => n % p !== 0)) primes.push(n);
            }
            """),
        AnyInsteadOfAll("logic-cpp-any-instead-of-all",
            """
            for (int n = 2; n <= max; n++) {
                if (std::all_of(primes.begin(), primes.end(), [n](int p) { return n % p != 0; })) {
                    primes.push_back(n);
                }
            }
            """),
        AnyInsteadOfAll("logic-rust-any-instead-of-all",
            """
            for n in 2..=max {
                if primes.iter().all(|&p| n % p != 0) {
                    primes.push(n);
                }
            }
            """),
        AnyInsteadOfAll("logic-ruby-any-instead-of-all",
            """
            (2..max).each do |n|
              primes << n if primes.all? { |p| n % p != 0 }
            end
            """),
        AnyInsteadOfAll("logic-php-any-instead-of-all",
            """
            for ($n = 2; $n <= $max; $n++) {
                if (array_all($primes, fn($p) => $n % $p != 0)) {
                    $primes[] = $n;
                }
            }
            """),
        AnyInsteadOfAll("logic-perl-any-instead-of-all",
            """
            for my $n (2 .. $max) {
                push @primes, $n if all { $n % $_ != 0 } @primes;
            }
            """),
        AnyInsteadOfAll("logic-dart-any-instead-of-all",
            """
            for (var n = 2; n <= max; n++) {
              if (primes.every((p) => n % p != 0)) {
                primes.add(n);
              }
            }
            """),
        AnyInsteadOfAll("logic-scala-any-instead-of-all",
            """
            for (n <- 2 to max) {
              if (primes.forall(p => n % p != 0)) {
                primes += n
              }
            }
            """),
        AnyInsteadOfAll("logic-fortran-any-instead-of-all",
            """
            do n = 2, 30
              if (all(mod(n, primes(1:found)) /= 0)) then
                found = found + 1
                primes(found) = n
              end if
            end do
            """),
        AnyInsteadOfAll("logic-fsharp-any-instead-of-all",
            """
            let primesUpTo max =
                [ 2 .. max ]
                |> List.fold (fun primes n -> if List.forall (fun p -> n % p <> 0) primes then primes @ [ n ] else primes) []
            """),
        AnyInsteadOfAll("logic-ocaml-any-instead-of-all",
            """
            let rec walk n primes =
              if n > max then List.rev primes
              else if List.for_all (fun p -> n mod p <> 0) primes then walk (n + 1) (n :: primes)
              else walk (n + 1) primes
            """),
        AnyInsteadOfAll("logic-elixir-any-instead-of-all",
            """
            Enum.reduce(2..max, [], fn n, primes ->
              if Enum.all?(primes, fn p -> rem(n, p) != 0 end), do: primes ++ [n], else: primes
            end)
            """),
        AnyInsteadOfAll("logic-erlang-any-instead-of-all",
            """
            lists:foldl(fun(N, Primes) ->
                                case lists:all(fun(P) -> N rem P =/= 0 end, Primes) of
                                    true -> Primes ++ [N];
                                    false -> Primes
                                end
                        end, [], lists:seq(2, Max)).
            """),
        XorInsteadOfAnd("logic-python-xor-instead-of-and",
            """
            while n:
                n &= n - 1
                count += 1
            """),
        XorInsteadOfAnd("logic-java-xor-instead-of-and",
            """
            while (n != 0) {
                n = (n & (n - 1));
                count++;
            }
            """),
        XorInsteadOfAnd("logic-csharp-xor-instead-of-and",
            """
            while (number > 0)
            {
                number &= number - 1;
                count++;
            }
            """),
        XorInsteadOfAnd("logic-js-xor-instead-of-and",
            """
            while (n !== 0) {
              n = n & (n - 1);
              count++;
            }
            """),
        XorInsteadOfAnd("logic-c-xor-instead-of-and",
            """
            while (n) {
                n &= n - 1;
                count++;
            }
            """),
        XorInsteadOfAnd("logic-cpp-xor-instead-of-and",
            """
            for (; n != 0; count++) {
                n &= n - 1;
            }
            """),
        XorInsteadOfAnd("logic-go-xor-instead-of-and",
            """
            for n != 0 {
            	n &= n - 1
            	count++
            }
            """),
        XorInsteadOfAnd("logic-rust-xor-instead-of-and",
            """
            while n != 0 {
                n &= n - 1;
                count += 1;
            }
            """),
        XorInsteadOfAnd("logic-ruby-xor-instead-of-and",
            """
            until n.zero?
              n &= n - 1
              count += 1
            end
            """),
        XorInsteadOfAnd("logic-lua-xor-instead-of-and",
            """
            while n ~= 0 do
              n = n & (n - 1)
              count = count + 1
            end
            """),
        XorInsteadOfAnd("logic-php-xor-instead-of-and",
            """
            while ($n != 0) {
                $n &= $n - 1;
                $count++;
            }
            """),
        XorInsteadOfAnd("logic-perl-xor-instead-of-and",
            """
            while ($n) {
                $n &= $n - 1;
                $count++;
            }
            """),
        XorInsteadOfAnd("logic-powershell-xor-instead-of-and",
            """
            while ($n -ne 0) {
                $n = $n -band ($n - 1)
                $count++
            }
            """),
        XorInsteadOfAnd("logic-dart-xor-instead-of-and",
            """
            while (n != 0) {
              n &= n - 1;
              count++;
            }
            """),
        XorInsteadOfAnd("logic-scala-xor-instead-of-and",
            """
            while (n != 0) {
              n &= n - 1
              count += 1
            }
            """),
        XorInsteadOfAnd("logic-fortran-xor-instead-of-and",
            """
            do while (n /= 0)
              n = iand(n, n - 1)
              bitcount = bitcount + 1
            end do
            """),
        XorInsteadOfAnd("logic-fsharp-xor-instead-of-and",
            """
            let rec countBits n count =
                if n = 0 then count else countBits (n &&& (n - 1)) (count + 1)
            """),
        XorInsteadOfAnd("logic-ocaml-xor-instead-of-and",
            """
            let rec count_bits n count =
              if n = 0 then count else count_bits (n land (n - 1)) (count + 1)
            """),
        XorInsteadOfAnd("logic-elixir-xor-instead-of-and",
            """
            defmodule Bits do
              import Bitwise

              def count(n), do: count(n, 0)

              defp count(0, count), do: count
              defp count(n, count), do: count(band(n, n - 1), count + 1)
            end
            """),
        XorInsteadOfAnd("logic-erlang-xor-instead-of-and",
            """
            count(0, Count) -> Count;
            count(N, Count) -> count(N band (N - 1), Count + 1).
            """),
        EuclidPairNotMoved("logic-python-euclid-pair-not-moved",
            """
            def gcd(a, b):
                if b == 0:
                    return a
                else:
                    return gcd(b, a % b)
            """),
        EuclidPairNotMoved("logic-java-euclid-pair-not-moved",
            """
            public static int gcd(int a, int b) {
                if (b == 0) {
                    return a;
                } else {
                    return gcd(b, a % b);
                }
            }
            """),
        EuclidPairNotMoved("logic-csharp-euclid-pair-not-moved",
            """
            static int Gcd(int first, int second)
            {
                while (second != 0)
                {
                    (first, second) = (second, first % second);
                }

                return first;
            }
            """),
        EuclidPairNotMoved("logic-js-euclid-pair-not-moved",
            """
            function gcd(a, b) {
              return b === 0 ? a : gcd(b, a % b);
            }
            """),
        EuclidPairNotMoved("logic-c-euclid-pair-not-moved",
            """
            static int gcd(int a, int b) {
                if (b == 0) return a;
                return gcd(b, a % b);
            }
            """),
        EuclidPairNotMoved("logic-cpp-euclid-pair-not-moved",
            """
            int gcd(int a, int b) {
                if (b == 0) {
                    return a;
                }
                return gcd(b, a % b);
            }
            """),
        EuclidPairNotMoved("logic-go-euclid-pair-not-moved",
            """
            func gcd(a, b int) int {
            	for b != 0 {
            		a, b = b, a%b
            	}
            	return a
            }
            """),
        EuclidPairNotMoved("logic-rust-euclid-pair-not-moved",
            """
            fn gcd(a: u32, b: u32) -> u32 {
                if b == 0 {
                    a
                } else {
                    gcd(b, a % b)
                }
            }
            """),
        EuclidPairNotMoved("logic-ruby-euclid-pair-not-moved",
            """
            def gcd(a, b)
              return a if b.zero?

              gcd(b, a % b)
            end
            """),
        EuclidPairNotMoved("logic-lua-euclid-pair-not-moved",
            """
            local function gcd(a, b)
              if b == 0 then
                return a
              end
              return gcd(b, a % b)
            end
            """),
        EuclidPairNotMoved("logic-php-euclid-pair-not-moved",
            """
            function gcd(int $a, int $b): int {
                if ($b == 0) {
                    return $a;
                }
                return gcd($b, $a % $b);
            }
            """),
        EuclidPairNotMoved("logic-perl-euclid-pair-not-moved",
            """
            sub gcd {
                my ($a, $b) = @_;
                while ($b != 0) {
                    ($a, $b) = ($b, $a % $b);
                }
                return $a;
            }
            """),
        EuclidPairNotMoved("logic-powershell-euclid-pair-not-moved",
            """
            function Get-Gcd([int]$a, [int]$b) {
                if ($b -eq 0) {
                    return $a
                }
                return Get-Gcd $b ($a % $b)
            }
            """),
        EuclidPairNotMoved("logic-dart-euclid-pair-not-moved",
            """
            int gcd(int a, int b) {
              if (b == 0) {
                return a;
              }
              return gcd(b, a % b);
            }
            """),
        EuclidPairNotMoved("logic-scala-euclid-pair-not-moved",
            """
            def gcd(a: Int, b: Int): Int =
              if (b == 0) a else gcd(b, a % b)
            """),
        EuclidPairNotMoved("logic-fortran-euclid-pair-not-moved",
            """
            recursive integer function gcd(a, b) result(divisor)
              integer, intent(in) :: a, b
              if (b == 0) then
                divisor = a
              else
                divisor = gcd(b, mod(a, b))
              end if
            end function gcd
            """),
        EuclidPairNotMoved("logic-fsharp-euclid-pair-not-moved",
            """
            let rec gcd a b =
                if b = 0 then a else gcd b (a % b)
            """),
        EuclidPairNotMoved("logic-ocaml-euclid-pair-not-moved",
            """
            let rec gcd a b =
              if b = 0 then a else gcd b (a mod b)
            """),
        EuclidPairNotMoved("logic-elixir-euclid-pair-not-moved",
            """
            def gcd(a, 0), do: a
            def gcd(a, b), do: gcd(b, rem(a, b))
            """),
        EuclidPairNotMoved("logic-erlang-euclid-pair-not-moved",
            """
            gcd(A, 0) -> A;
            gcd(A, B) -> gcd(B, A rem B).
            """),
        BracketsLeftOpen("logic-python-brackets-left-open",
            """
            def is_valid_parenthesization(parens):
                depth = 0
                for paren in parens:
                    if paren == '(':
                        depth += 1
                    else:
                        depth -= 1
                        if depth < 0:
                            return False

                return depth == 0
            """),
        BracketsLeftOpen("logic-java-brackets-left-open",
            """
            public static Boolean is_valid_parenthesization(String parens) {
                int depth = 0;
                for (int i = 0; i < parens.length(); i++) {
                    Character paren = parens.charAt(i);
                    if (paren.equals('(')) {
                        depth++;
                    } else {
                        depth--;
                        if (depth < 0) { return false; }
                    }
                }
                return depth == 0;
            }
            """),
        BracketsLeftOpen("logic-csharp-brackets-left-open",
            """
            static bool Balanced(string text)
            {
                var open = 0;
                foreach (var bracket in text)
                {
                    if (bracket == '(')
                    {
                        open++;
                    }
                    else
                    {
                        open--;
                        if (open < 0)
                        {
                            return false;
                        }
                    }
                }

                return open == 0;
            }
            """),
        BracketsLeftOpen("logic-js-brackets-left-open",
            """
            function balanced(text) {
              let depth = 0;
              for (const bracket of text) {
                if (bracket === "(") {
                  depth++;
                } else {
                  depth--;
                  if (depth < 0) return false;
                }
              }
              return depth === 0;
            }
            """),
        BracketsLeftOpen("logic-c-brackets-left-open",
            """
            static int balanced(const char *text) {
                int depth = 0;
                for (; *text; text++) {
                    if (*text == '(') {
                        depth++;
                    } else {
                        depth--;
                        if (depth < 0) return 0;
                    }
                }
                return depth == 0;
            }
            """),
        BracketsLeftOpen("logic-cpp-brackets-left-open",
            """
            bool balanced(const std::string& text) {
                int depth = 0;
                for (char bracket : text) {
                    if (bracket == '(') {
                        depth++;
                    } else {
                        depth--;
                        if (depth < 0) {
                            return false;
                        }
                    }
                }
                return depth == 0;
            }
            """),
        BracketsLeftOpen("logic-go-brackets-left-open",
            """
            func balanced(text string) bool {
            	depth := 0
            	for _, bracket := range text {
            		if bracket == '(' {
            			depth++
            		} else {
            			depth--
            			if depth < 0 {
            				return false
            			}
            		}
            	}
            	return depth == 0
            }
            """),
        BracketsLeftOpen("logic-rust-brackets-left-open",
            """
            fn balanced(text: &str) -> bool {
                let mut depth = 0;
                for bracket in text.chars() {
                    if bracket == '(' {
                        depth += 1;
                    } else {
                        depth -= 1;
                        if depth < 0 {
                            return false;
                        }
                    }
                }
                depth == 0
            }
            """),
        BracketsLeftOpen("logic-ruby-brackets-left-open",
            """
            def balanced?(text)
              depth = 0
              text.each_char do |bracket|
                if bracket == "("
                  depth += 1
                else
                  depth -= 1
                  return false if depth < 0
                end
              end
              depth == 0
            end
            """),
        BracketsLeftOpen("logic-lua-brackets-left-open",
            """
            local function balanced(text)
              local depth = 0
              for bracket in text:gmatch(".") do
                if bracket == "(" then
                  depth = depth + 1
                else
                  depth = depth - 1
                  if depth < 0 then
                    return false
                  end
                end
              end
              return depth == 0
            end
            """),
        BracketsLeftOpen("logic-php-brackets-left-open",
            """
            function balanced(string $text): bool {
                $depth = 0;
                foreach (str_split($text) as $bracket) {
                    if ($bracket === "(") {
                        $depth++;
                    } else {
                        $depth--;
                        if ($depth < 0) {
                            return false;
                        }
                    }
                }
                return $depth === 0;
            }
            """),
        BracketsLeftOpen("logic-perl-brackets-left-open",
            """
            sub balanced {
                my ($text) = @_;
                my $depth = 0;
                for my $bracket (split //, $text) {
                    if ($bracket eq "(") {
                        $depth++;
                    } else {
                        $depth--;
                        return 0 if $depth < 0;
                    }
                }
                return $depth == 0;
            }
            """),
        BracketsLeftOpen("logic-powershell-brackets-left-open",
            """
            function Test-Balanced([string]$text) {
                $depth = 0
                foreach ($bracket in $text.ToCharArray()) {
                    if ($bracket -eq '(') {
                        $depth++
                    } else {
                        $depth--
                        if ($depth -lt 0) {
                            return $false
                        }
                    }
                }
                return $depth -eq 0
            }
            """),
        BracketsLeftOpen("logic-dart-brackets-left-open",
            """
            bool balanced(String text) {
              var depth = 0;
              for (final bracket in text.split('')) {
                if (bracket == '(') {
                  depth++;
                } else {
                  depth--;
                  if (depth < 0) {
                    return false;
                  }
                }
              }
              return depth == 0;
            }
            """),
        BracketsLeftOpen("logic-scala-brackets-left-open",
            """
            def balanced(text: String): Boolean = {
              var depth = 0
              for (bracket <- text) {
                if (bracket == '(') {
                  depth += 1
                } else {
                  depth -= 1
                  if (depth < 0) {
                    return false
                  }
                }
              }
              depth == 0
            }
            """),
        BracketsLeftOpen("logic-fortran-brackets-left-open",
            """
            logical function balanced(text)
              character(len=*), intent(in) :: text
              integer :: depth, i
              depth = 0
              do i = 1, len(text)
                if (text(i:i) == '(') then
                  depth = depth + 1
                else
                  depth = depth - 1
                  if (depth < 0) then
                    balanced = .false.
                    return
                  end if
                end if
              end do
              balanced = depth == 0
            end function balanced
            """),
        BracketsLeftOpen("logic-fsharp-brackets-left-open",
            """
            let rec balanced depth brackets =
                match brackets with
                | [] -> depth = 0
                | '(' :: rest -> balanced (depth + 1) rest
                | _ :: rest -> depth > 0 && balanced (depth - 1) rest
            """),
        BracketsLeftOpen("logic-ocaml-brackets-left-open",
            """
            let rec balanced depth brackets =
              match brackets with
              | [] -> depth = 0
              | '(' :: rest -> balanced (depth + 1) rest
              | _ :: rest -> depth > 0 && balanced (depth - 1) rest
            """),
        BracketsLeftOpen("logic-elixir-brackets-left-open",
            """
            def balanced?(text), do: balanced?(String.graphemes(text), 0)
            defp balanced?([], depth), do: depth == 0
            defp balanced?(["(" | rest], depth), do: balanced?(rest, depth + 1)
            defp balanced?([_ | rest], depth), do: depth > 0 and balanced?(rest, depth - 1)
            """),
        BracketsLeftOpen("logic-erlang-brackets-left-open",
            """
            balanced([], Depth) -> Depth =:= 0;
            balanced([$( | Rest], Depth) -> balanced(Rest, Depth + 1);
            balanced([_ | Rest], Depth) -> Depth > 0 andalso balanced(Rest, Depth - 1).
            """),
        DigitsWrittenBackwards("logic-python-digits-written-backwards",
            """
            def to_base(num, b):
                result = ''
                alphabet = string.digits + string.ascii_uppercase
                while num > 0:
                    i = num % b
                    num = num // b
                    result = alphabet[i] + result
                return result
            """),
        DigitsWrittenBackwards("logic-java-digits-written-backwards",
            """
            public static String to_base(int num, int b) {
                String result = "";
                String alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                int i;
                while (num > 0) {
                    i = num % b;
                    num = num / b;
                    result = String.valueOf(alphabet.charAt(i)) + result;
                }
                return result;
            }
            """),
        DigitsWrittenBackwards("logic-csharp-digits-written-backwards",
            """
            static string ToBase(int number, int radix)
            {
                var written = "";
                while (number > 0)
                {
                    written = Digits[number % radix] + written;
                    number /= radix;
                }

                return written;
            }
            """),
        DigitsWrittenBackwards("logic-js-digits-written-backwards",
            """
            function toBase(num, base) {
              let result = "";
              while (num > 0) {
                const digit = num % base;
                result = DIGITS[digit] + result;
                num = Math.floor(num / base);
              }
              return result;
            }
            """),
        DigitsWrittenBackwards("logic-cpp-digits-written-backwards",
            """
            std::string toBase(int num, int base) {
                std::string result;
                while (num > 0) {
                    result = DIGITS[num % base] + result;
                    num /= base;
                }
                return result;
            }
            """),
        DigitsWrittenBackwards("logic-go-digits-written-backwards",
            """
            func toBase(num, base int) string {
            	result := ""
            	for num > 0 {
            		result = string(digits[num%base]) + result
            		num /= base
            	}
            	return result
            }
            """),
        DigitsWrittenBackwards("logic-rust-digits-written-backwards",
            """
            fn to_base(mut num: u32, base: u32) -> String {
                let mut result = String::new();
                while num > 0 {
                    result.insert(0, DIGITS[(num % base) as usize] as char);
                    num /= base;
                }
                result
            }
            """),
        DigitsWrittenBackwards("logic-ruby-digits-written-backwards",
            """
            def to_base(num, base)
              result = ""
              while num > 0
                result = DIGITS[num % base] + result
                num /= base
              end
              result
            end
            """),
        DigitsWrittenBackwards("logic-lua-digits-written-backwards",
            """
            result = DIGITS:sub(digit + 1, digit + 1) .. result
            """),
        DigitsWrittenBackwards("logic-php-digits-written-backwards",
            """
            function to_base(int $num, int $base): string {
                $result = "";
                while ($num > 0) {
                    $result = DIGITS[$num % $base] . $result;
                    $num = intdiv($num, $base);
                }
                return $result;
            }
            """),
        DigitsWrittenBackwards("logic-perl-digits-written-backwards",
            """
            sub to_base {
                my ($num, $base) = @_;
                my $result = "";
                while ($num > 0) {
                    $result = substr($digits, $num % $base, 1) . $result;
                    $num = int($num / $base);
                }
                return $result;
            }
            """),
        DigitsWrittenBackwards("logic-powershell-digits-written-backwards",
            """
            function ConvertTo-Base([int]$num, [int]$base) {
                $result = ""
                while ($num -gt 0) {
                    $result = $digits.Substring($num % $base, 1) + $result
                    $num = [math]::Floor($num / $base)
                }
                return $result
            }
            """),
        DigitsWrittenBackwards("logic-dart-digits-written-backwards",
            """
            String toBase(int num, int base) {
              var result = '';
              while (num > 0) {
                result = digits[num % base] + result;
                num ~/= base;
              }
              return result;
            }
            """),
        DigitsWrittenBackwards("logic-scala-digits-written-backwards",
            """
            def toBase(start: Int, base: Int): String = {
              var num = start
              var result = ""
              while (num > 0) {
                result = Digits.substring(num % base, num % base + 1) + result
                num /= base
              }
              result
            }
            """),
        DigitsWrittenBackwards("logic-fortran-digits-written-backwards",
            """
            function to_base(start, base) result(written)
              integer, intent(in) :: start, base
              character(len=32) :: written
              integer :: num, digit
              num = start
              written = ''
              do while (num > 0)
                digit = mod(num, base)
                written = digits(digit + 1:digit + 1) // trim(written)
                num = num / base
              end do
            end function to_base
            """),
        DigitsWrittenBackwards("logic-fsharp-digits-written-backwards",
            """
            let toBase start radix =
                let mutable num = start
                let mutable result = ""
                while num > 0 do
                    result <- string digits.[num % radix] + result
                    num <- num / radix
                result
            """),
        DigitsWrittenBackwards("logic-ocaml-digits-written-backwards",
            """
            let to_base start radix =
              let num = ref start in
              let result = ref "" in
              while !num > 0 do
                result := String.make 1 digits.[!num mod radix] ^ !result;
                num := !num / radix
              done;
              !result
            """),
        DigitsWrittenBackwards("logic-elixir-digits-written-backwards",
            """
            defp digits(0, _base, written), do: written
            defp digits(num, base, written), do: digits(div(num, base), base, String.at(@digits, rem(num, base)) <> written)
            """),
        DigitsWrittenBackwards("logic-erlang-digits-written-backwards",
            """
            digits(0, _Base, Written) -> Written;
            digits(Num, Base, Written) -> digits(Num div Base, Base, [lists:nth(Num rem Base + 1, ?DIGITS)] ++ Written).
            """),
        OperandsTakenBackwards("logic-python-operands-taken-backwards",
            """
            a = stack.pop()
            b = stack.pop()
            stack.append(
                op(token, b, a)
            """),
        OperandsTakenBackwards("logic-java-operands-taken-backwards",
            """
            Integer a = stack.pop();
            Integer b = stack.pop();
            BinaryOperator<Integer> bin_op = op.get(token);
            Integer c = bin_op.apply(b,a);
            """),
        OperandsTakenBackwards("logic-csharp-operands-taken-backwards",
            """
            var right = stack.Pop();
            var left = stack.Pop();
            stack.Push(token switch
            {
                "+" => left + right,
                "-" => left - right,
            """),
        OperandsTakenBackwards("logic-js-operands-taken-backwards",
            """
            const b = stack.pop();
            const a = stack.pop();
            if (token === "+") stack.push(a + b);
            else if (token === "-") stack.push(a - b);
            """),
        OperandsTakenBackwards("logic-c-operands-taken-backwards",
            """
            int b = stack[--top];
            int a = stack[--top];
            switch (token[0]) {
            case '+': stack[top++] = a + b; break;
            case '-': stack[top++] = a - b; break;
            """),
        OperandsTakenBackwards("logic-cpp-operands-taken-backwards",
            """
            int b = stack.back();
            stack.pop_back();
            int a = stack.back();
            stack.pop_back();
            if (token == "+") stack.push_back(a + b);
            else if (token == "-") stack.push_back(a - b);
            """),
        OperandsTakenBackwards("logic-go-operands-taken-backwards",
            """
            b := stack[len(stack)-1]
            stack = stack[:len(stack)-1]
            a := stack[len(stack)-1]
            stack = stack[:len(stack)-1]
            switch token {
            case "+":
            	stack = append(stack, a+b)
            case "-":
            	stack = append(stack, a-b)
            """),
        OperandsTakenBackwards("logic-rust-operands-taken-backwards",
            """
            let b = stack.pop().unwrap();
            let a = stack.pop().unwrap();
            stack.push(match *token {
                "+" => a + b,
                "-" => a - b,
            """),
        OperandsTakenBackwards("logic-ruby-operands-taken-backwards",
            """
            b = stack.pop
            a = stack.pop
            stack.push(case token
                       when "+" then a + b
                       when "-" then a - b
            """),
        OperandsTakenBackwards("logic-lua-operands-taken-backwards",
            """
            local b = table.remove(stack)
            local a = table.remove(stack)
            if token == "+" then
              stack[#stack + 1] = a + b
            elseif token == "-" then
              stack[#stack + 1] = a - b
            """),
        OperandsTakenBackwards("logic-php-operands-taken-backwards",
            """
            $b = array_pop($stack);
            $a = array_pop($stack);
            $stack[] = match ($token) {
                '+' => $a + $b,
                '-' => $a - $b,
            """),
        OperandsTakenBackwards("logic-perl-operands-taken-backwards",
            """
            my $y = pop @stack;
            my $x = pop @stack;
            if ($token eq "+") { push @stack, $x + $y }
            elsif ($token eq "-") { push @stack, $x - $y }
            """),
        OperandsTakenBackwards("logic-powershell-operands-taken-backwards",
            """
            $b = $stack.Pop()
            $a = $stack.Pop()
            switch ($token) {
                '+' { $stack.Push($a + $b) }
                '-' { $stack.Push($a - $b) }
            """),
        OperandsTakenBackwards("logic-dart-operands-taken-backwards",
            """
            final b = stack.removeLast();
            final a = stack.removeLast();
            switch (token) {
              case '+':
                stack.add(a + b);
              case '-':
                stack.add(a - b);
            """),
        OperandsTakenBackwards("logic-scala-operands-taken-backwards",
            """
            val b = stack.pop()
            val a = stack.pop()
            stack.push(token match {
              case "+" => a + b
              case "-" => a - b
            """),
        OperandsTakenBackwards("logic-fortran-operands-taken-backwards",
            """
            b = stack(top)
            top = top - 1
            a = stack(top)
            top = top - 1
            top = top + 1
            select case (trim(tokens(i)))
            case ('+')
              stack(top) = a + b
            case ('-')
              stack(top) = a - b
            """),
        OperandsTakenBackwards("logic-fsharp-operands-taken-backwards",
            """
            let step stack token =
                match token, stack with
                | "+", a :: b :: rest -> (a + b) :: rest
                | "-", b :: a :: rest -> (a - b) :: rest
                | "*", a :: b :: rest -> (a * b) :: rest
                | number, rest -> int number :: rest
            """),
        OperandsTakenBackwards("logic-ocaml-operands-taken-backwards",
            """
            let step stack token =
              match token, stack with
              | "+", a :: b :: rest -> (a + b) :: rest
              | "-", b :: a :: rest -> (a - b) :: rest
              | "*", a :: b :: rest -> (a * b) :: rest
              | number, rest -> int_of_string number :: rest
            """),
        OperandsTakenBackwards("logic-elixir-operands-taken-backwards",
            """
            defp step("+", [a, b | rest]), do: [a + b | rest]
            defp step("-", [b, a | rest]), do: [a - b | rest]
            defp step("*", [a, b | rest]), do: [a * b | rest]
            defp step(number, stack), do: [String.to_integer(number) | stack]
            """),
        OperandsTakenBackwards("logic-erlang-operands-taken-backwards",
            """
            step("+", [A, B | Rest]) -> [A + B | Rest];
            step("-", [B, A | Rest]) -> [A - B | Rest];
            step("*", [A, B | Rest]) -> [A * B | Rest];
            step(Number, Stack) -> [list_to_integer(Number) | Stack].
            """),
        LoopOneShort("logic-python-loop-one-short",
            """
            for c in range(0, r + 1):
                upleft = rows[r - 1][c - 1] if c > 0 else 0
                upright = rows[r - 1][c] if c < r else 0
                row.append(upleft + upright)
            """),
        LoopOneShort("logic-java-loop-one-short",
            """
            for (int c=0; c<r+1; c++) {
                int upleft, upright;
                if (c > 0) {
                    upleft = rows.get(r-1).get(c-1);
                } else {
                    upleft = 0;
                }
                if (c < r) {
                    upright = rows.get(r-1).get(c);
                } else {
                    upright = 0;
                }
                row.add(upleft+upright);
            }
            """),
        LoopOneShort("logic-csharp-loop-one-short",
            """
            for (var c = 0; c < r + 1; c++)
            {
                var upleft = c > 0 ? rows[r - 1][c - 1] : 0;
                var upright = c < r ? rows[r - 1][c] : 0;
                row.Add(upleft + upright);
            }
            """),
        LoopOneShort("logic-js-loop-one-short",
            """
            for (let c = 0; c < r + 1; c++) {
              const upleft = c > 0 ? rows[r - 1][c - 1] : 0;
              const upright = c < r ? rows[r - 1][c] : 0;
              row.push(upleft + upright);
            }
            """),
        LoopOneShort("logic-c-loop-one-short",
            """
            for (int c = 0; c < r + 1; c++) {
                int upleft = c > 0 ? rows[r - 1][c - 1] : 0;
                int upright = c < r ? rows[r - 1][c] : 0;
                rows[r][c] = upleft + upright;
                lengths[r] = c + 1;
            }
            """),
        LoopOneShort("logic-cpp-loop-one-short",
            """
            for (int c = 0; c < r + 1; c++) {
                int upleft = c > 0 ? rows[r - 1].at(c - 1) : 0;
                int upright = c < r ? rows[r - 1].at(c) : 0;
                row.push_back(upleft + upright);
            }
            """),
        LoopOneShort("logic-go-loop-one-short",
            """
            for c := 0; c < r + 1; c++ {
            	upleft, upright := 0, 0
            	if c > 0 {
            		upleft = rows[r-1][c-1]
            	}
            	if c < r {
            		upright = rows[r-1][c]
            	}
            	row = append(row, upleft+upright)
            }
            """),
        LoopOneShort("logic-rust-loop-one-short",
            """
            for c in 0..r + 1 {
                let upleft = if c > 0 { rows[r - 1][c - 1] } else { 0 };
                let upright = if c < r { rows[r - 1][c] } else { 0 };
                row.push(upleft + upright);
            }
            """),
        LoopOneShort("logic-ruby-loop-one-short",
            """
            (0...r + 1).each do |c|
              upleft = c > 0 ? rows[r - 1][c - 1] : 0
              upright = c < r ? rows[r - 1][c] : 0
              row << upleft + upright
            end
            """),
        LoopOneShort("logic-lua-loop-one-short",
            """
            for c = 1, r do
              local upleft = c > 1 and rows[r - 1][c - 1] or 0
              local upright = c < r and rows[r - 1][c] or 0
              row[#row + 1] = upleft + upright
            end
            """),
        LoopOneShort("logic-php-loop-one-short",
            """
            for ($c = 0; $c < $r + 1; $c++) {
                $upleft = $c > 0 ? $rows[$r - 1][$c - 1] : 0;
                $upright = $c < $r ? $rows[$r - 1][$c] : 0;
                $row[] = $upleft + $upright;
            }
            """),
        LoopOneShort("logic-perl-loop-one-short",
            """
            for my $c (0 .. $r) {
                my $upleft = $c > 0 ? $rows[$r - 1][$c - 1] : 0;
                my $upright = $c < $r ? $rows[$r - 1][$c] : 0;
                push @row, $upleft + $upright;
            }
            """),
        LoopOneShort("logic-powershell-loop-one-short",
            """
            for ($c = 0; $c -lt $r + 1; $c++) {
                $upleft = if ($c -gt 0) { $rows[$r - 1][$c - 1] } else { 0 }
                $upright = if ($c -lt $r) { $rows[$r - 1][$c] } else { 0 }
                $row += $upleft + $upright
            }
            """),
        LoopOneShort("logic-dart-loop-one-short",
            """
            for (var c = 0; c < r + 1; c++) {
              final upleft = c > 0 ? rows[r - 1][c - 1] : 0;
              final upright = c < r ? rows[r - 1][c] : 0;
              row.add(upleft + upright);
            }
            """),
        LoopOneShort("logic-scala-loop-one-short",
            """
            for (c <- 0 until r + 1) {
              val upleft = if (c > 0) rows(r - 1)(c - 1) else 0
              val upright = if (c < r) rows(r - 1)(c) else 0
              row += upleft + upright
            }
            """),
        LoopOneShort("logic-fortran-loop-one-short",
            """
            do c = 0, r
              upleft = 0
              upright = 0
              if (c > 0) upleft = rows(r - 1, c - 1)
              if (c < r) upright = rows(r - 1, c)
              rows(r, c) = upleft + upright
              lengths(r) = c + 1
            end do
            """),
        LoopOneShort("logic-fsharp-loop-one-short",
            """
            let nextRow (previous: int list) r =
                [ for c in 0 .. r -> (if c > 0 then previous.[c - 1] else 0) + (if c < r then previous.[c] else 0) ]
            """),
        LoopOneShort("logic-ocaml-loop-one-short",
            """
            let next_row previous r =
              List.init (r + 1) (fun c -> (if c > 0 then List.nth previous (c - 1) else 0) + (if c < r then List.nth previous c else 0))
            """),
        LoopOneShort("logic-elixir-loop-one-short",
            """
            def next_row(previous, r) do
              for c <- 0..r, do: (if c > 0, do: Enum.at(previous, c - 1), else: 0) + (if c < r, do: Enum.at(previous, c), else: 0)
            end
            """),
        LoopOneShort("logic-erlang-loop-one-short",
            """
            next_row(Previous, R) ->
                [(case C > 0 of true -> lists:nth(C, Previous); false -> 0 end) +
                 (case C < R of true -> lists:nth(C + 1, Previous); false -> 0 end) || C <- lists:seq(0, R)].
            """),
        LastFactorDropped("logic-python-last-factor-dropped",
            """
            def get_factors(n):
                if n == 1:
                    return []

                for i in range(2, int(n ** 0.5) + 1):
                    if n % i == 0:
                        return [i] + get_factors(n // i)

                return [n]
            """),
        LastFactorDropped("logic-java-last-factor-dropped",
            """
            public static ArrayList<Integer> get_factors(int n) {
                if (n == 1) {
                    return new ArrayList<Integer>();
                }
                int max = (int)(Math.sqrt(n) + 1.0);
                for (int i=2; i < max; i++) {
                    if (n % i == 0) {
                        ArrayList<Integer> prepend = new ArrayList<Integer>(0);
                        prepend.add(i);
                        prepend.addAll(get_factors(n / i));
                        return prepend;
                    }
                }
                return new ArrayList<Integer>(Arrays.asList(n));
            }
            """),
        LastFactorDropped("logic-csharp-last-factor-dropped",
            """
            static List<int> Factors(int n)
            {
                if (n == 1)
                {
                    return new List<int>();
                }

                for (var i = 2; i * i <= n; i++)
                {
                    if (n % i == 0)
                    {
                        return new List<int> { i }.Concat(Factors(n / i)).ToList();
                    }
                }

                return new List<int> { n };
            }
            """),
        LastFactorDropped("logic-js-last-factor-dropped",
            """
            function factors(n) {
              if (n === 1) return [];
              for (let i = 2; i <= Math.sqrt(n); i++) {
                if (n % i === 0) return [i, ...factors(n / i)];
              }
              return [n];
            }
            """),
        LastFactorDropped("logic-cpp-last-factor-dropped",
            """
            std::vector<int> factors(int n) {
                if (n == 1) {
                    return {};
                }
                for (int i = 2; i <= static_cast<int>(std::sqrt(n)); i++) {
                    if (n % i == 0) {
                        std::vector<int> found = {i};
                        std::vector<int> rest = factors(n / i);
                        found.insert(found.end(), rest.begin(), rest.end());
                        return found;
                    }
                }
                return {n};
            }
            """),
        LastFactorDropped("logic-go-last-factor-dropped",
            """
            func factors(n int) []int {
            	if n == 1 {
            		return []int{}
            	}
            	for i := 2; i*i <= n; i++ {
            		if n%i == 0 {
            			return append([]int{i}, factors(n/i)...)
            		}
            	}
            	return []int{n}
            }
            """),
        LastFactorDropped("logic-rust-last-factor-dropped",
            """
            fn factors(n: u32) -> Vec<u32> {
                if n == 1 {
                    return vec![];
                }
                let mut i = 2;
                while i * i <= n {
                    if n % i == 0 {
                        let mut found = vec![i];
                        found.extend(factors(n / i));
                        return found;
                    }
                    i += 1;
                }
                vec![n]
            }
            """),
        LastFactorDropped("logic-ruby-last-factor-dropped",
            """
            def factors(n)
              return [] if n == 1

              (2..Integer.sqrt(n)).each do |i|
                return [i] + factors(n / i) if (n % i).zero?
              end
              [n]
            end
            """),
        LastFactorDropped("logic-lua-last-factor-dropped",
            """
            local function factors(n)
              if n == 1 then
                return {}
              end
              for i = 2, math.floor(math.sqrt(n)) do
                if n % i == 0 then
                  local found = { i }
                  for _, factor in ipairs(factors(n // i)) do
                    found[#found + 1] = factor
                  end
                  return found
                end
              end
              return { n }
            end
            """),
        LastFactorDropped("logic-php-last-factor-dropped",
            """
            function factors(int $n): array {
                if ($n == 1) {
                    return [];
                }
                for ($i = 2; $i * $i <= $n; $i++) {
                    if ($n % $i == 0) {
                        return array_merge([$i], factors(intdiv($n, $i)));
                    }
                }
                return [$n];
            }
            """),
        LastFactorDropped("logic-perl-last-factor-dropped",
            """
            sub factors {
                my ($n) = @_;
                return () if $n == 1;
                for (my $i = 2; $i * $i <= $n; $i++) {
                    return ($i, factors($n / $i)) if $n % $i == 0;
                }
                return ($n);
            }
            """),
        LastFactorDropped("logic-powershell-last-factor-dropped",
            """
            function Get-Factors([int]$n) {
                if ($n -eq 1) {
                    return @()
                }
                for ($i = 2; $i * $i -le $n; $i++) {
                    if ($n % $i -eq 0) {
                        return @($i) + @(Get-Factors ($n / $i))
                    }
                }
                return @($n)
            }
            """),
        LastFactorDropped("logic-dart-last-factor-dropped",
            """
            List<int> factors(int n) {
              if (n == 1) {
                return [];
              }
              for (var i = 2; i <= sqrt(n); i++) {
                if (n % i == 0) {
                  return [i, ...factors(n ~/ i)];
                }
              }
              return [n];
            }
            """),
        LastFactorDropped("logic-scala-last-factor-dropped",
            """
            def factors(n: Int): List[Int] = {
              if (n == 1) return List()
              for (i <- 2 to math.sqrt(n).toInt) {
                if (n % i == 0) return i :: factors(n / i)
              }
              List(n)
            }
            """),
        LastFactorDropped("logic-fortran-last-factor-dropped",
            """
            recursive function factors(n) result(found)
              integer, intent(in) :: n
              integer, allocatable :: found(:)
              integer :: i
              if (n == 1) then
                found = [integer ::]
                return
              end if
              do i = 2, int(sqrt(real(n)))
                if (mod(n, i) == 0) then
                  found = [i, factors(n / i)]
                  return
                end if
              end do
              found = [n]
            end function factors
            """),
        LastFactorDropped("logic-fsharp-last-factor-dropped",
            """
            let rec factors n =
                if n = 1 then []
                else
                    match [ 2 .. int (sqrt (float n)) ] |> List.tryFind (fun i -> n % i = 0) with
                    | Some i -> i :: factors (n / i)
                    | None -> [ n ]
            """),
        LastFactorDropped("logic-ocaml-last-factor-dropped",
            """
            let rec factors n =
              if n = 1 then []
              else
                match List.find_opt (fun i -> n mod i = 0) (List.init (max 0 (int_of_float (sqrt (float_of_int n)) - 1)) (fun k -> k + 2)) with
                | Some i -> i :: factors (n / i)
                | None -> [ n ]
            """),
        LastFactorDropped("logic-elixir-last-factor-dropped",
            """
            def factors(1), do: []

            def factors(n) do
              case Enum.find(2..max(2, trunc(:math.sqrt(n)))//1, fn i -> i < n and rem(n, i) == 0 end) do
                nil -> [n]
                i -> [i | factors(div(n, i))]
              end
            end
            """),
        LastFactorDropped("logic-erlang-last-factor-dropped",
            """
            factors(1) -> [];
            factors(N) ->
                case [I || I <- lists:seq(2, trunc(math:sqrt(N))), N rem I =:= 0] of
                    [] -> [N];
                    [I | _] -> [I | factors(N div I)]
                end.
            """),
        EmptyChoiceMissing("logic-python-empty-choice-missing",
            """
            def subsequences(a, b, k):
                if k == 0:
                    return [[]]

                ret = []
                for i in range(a, b + 1 - k):
                    ret.extend(
                        [i] + rest for rest in subsequences(i + 1, b, k - 1)
                    )

                return ret
            """),
        EmptyChoiceMissing("logic-java-empty-choice-missing",
            """
            public static ArrayList<ArrayList> subsequences(int a, int b, int k) {
                if (k == 0) {
                    ArrayList<ArrayList> nothingChosen = new ArrayList();
                    nothingChosen.add(new ArrayList<>());
                    return nothingChosen;
                }
                ArrayList ret = new ArrayList(50);
                for (int i=a; i<b+1-k; i++) {
                    ArrayList base = new ArrayList(50);
                    for (ArrayList rest : subsequences(i+1, b, k-1)) {
                        rest.add(0,i);
                        base.add(rest);
                    }
                    ret.addAll(base);
                }
                return ret;
            }
            """),
        EmptyChoiceMissing("logic-csharp-empty-choice-missing",
            """
            static List<List<int>> Choices(int from, int to, int count)
            {
                if (count == 0)
                {
                    return new List<List<int>> { new List<int>() };
                }

                var choices = new List<List<int>>();
                for (var first = from; first <= to - count; first++)
                {
                    foreach (var rest in Choices(first + 1, to, count - 1))
                    {
                        choices.Add(new List<int> { first }.Concat(rest).ToList());
                    }
                }

                return choices;
            }
            """),
        EmptyChoiceMissing("logic-js-empty-choice-missing",
            """
            function choices(from, to, count) {
              if (count === 0) return [[]];
              const found = [];
              for (let first = from; first <= to - count; first++) {
                for (const rest of choices(first + 1, to, count - 1)) {
                  found.push([first, ...rest]);
                }
              }
              return found;
            }
            """),
        EmptyChoiceMissing("logic-cpp-empty-choice-missing",
            """
            std::vector<std::vector<int>> choices(int from, int to, int count) {
                if (count == 0) {
                    return {{}};
                }
                std::vector<std::vector<int>> found;
                for (int first = from; first <= to - count; first++) {
                    for (std::vector<int> rest : choices(first + 1, to, count - 1)) {
                        rest.insert(rest.begin(), first);
                        found.push_back(rest);
                    }
                }
                return found;
            }
            """),
        EmptyChoiceMissing("logic-go-empty-choice-missing",
            """
            func choices(from, to, count int) [][]int {
            	if count == 0 {
            		return [][]int{{}}
            	}
            	var found [][]int
            	for first := from; first <= to-count; first++ {
            		for _, rest := range choices(first+1, to, count-1) {
            			found = append(found, append([]int{first}, rest...))
            		}
            	}
            	return found
            }
            """),
        EmptyChoiceMissing("logic-rust-empty-choice-missing",
            """
            fn choices(from: u32, to: u32, count: u32) -> Vec<Vec<u32>> {
                if count == 0 {
                    return vec![vec![]];
                }
                let mut found = Vec::new();
                for first in from..=to - count {
                    for rest in choices(first + 1, to, count - 1) {
                        let mut choice = vec![first];
                        choice.extend(rest);
                        found.push(choice);
                    }
                }
                found
            }
            """),
        EmptyChoiceMissing("logic-ruby-empty-choice-missing",
            """
            def choices(from, to, count)
              return [[]] if count.zero?

              (from..(to - count)).flat_map do |first|
                choices(first + 1, to, count - 1).map { |rest| [first] + rest }
              end
            end
            """),
        EmptyChoiceMissing("logic-lua-empty-choice-missing",
            """
            local function choices(from, to, count)
              if count == 0 then
                return { {} }
              end
              local found = {}
              for first = from, to - count do
                for _, rest in ipairs(choices(first + 1, to, count - 1)) do
                  local choice = { first }
                  for _, number in ipairs(rest) do
                    choice[#choice + 1] = number
                  end
                  found[#found + 1] = choice
                end
              end
              return found
            end
            """),
        EmptyChoiceMissing("logic-php-empty-choice-missing",
            """
            function choices(int $from, int $to, int $count): array {
                if ($count == 0) {
                    return [[]];
                }
                $found = [];
                for ($first = $from; $first <= $to - $count; $first++) {
                    foreach (choices($first + 1, $to, $count - 1) as $rest) {
                        $found[] = array_merge([$first], $rest);
                    }
                }
                return $found;
            }
            """),
        EmptyChoiceMissing("logic-perl-empty-choice-missing",
            """
            sub choices {
                my ($from, $to, $count) = @_;
                return ([]) if $count == 0;
                my @found;
                for my $first ($from .. $to - $count) {
                    for my $rest (choices($first + 1, $to, $count - 1)) {
                        push @found, [$first, @$rest];
                    }
                }
                return @found;
            }
            """),
        EmptyChoiceMissing("logic-powershell-empty-choice-missing",
            """
            function Get-Choices([int]$from, [int]$to, [int]$count) {
                if ($count -eq 0) {
                    return ,@(,@())
                }
                $found = [System.Collections.Generic.List[object]]::new()
                for ($first = $from; $first -le $to - $count; $first++) {
                    foreach ($rest in (Get-Choices ($first + 1) $to ($count - 1))) {
                        $found.Add(@($first) + $rest)
                    }
                }
                return ,$found.ToArray()
            }
            """),
        EmptyChoiceMissing("logic-dart-empty-choice-missing",
            """
            List<List<int>> choices(int from, int to, int count) {
              if (count == 0) {
                return [[]];
              }
              final found = <List<int>>[];
              for (var first = from; first <= to - count; first++) {
                for (final rest in choices(first + 1, to, count - 1)) {
                  found.add([first, ...rest]);
                }
              }
              return found;
            }
            """),
        EmptyChoiceMissing("logic-scala-empty-choice-missing",
            """
            def choices(from: Int, to: Int, count: Int): List[List[Int]] =
              if (count == 0) List(List())
              else (from to (to - count)).toList.flatMap(first => choices(first + 1, to, count - 1).map(rest => first :: rest))
            """),
        EmptyChoiceMissing("logic-fsharp-empty-choice-missing",
            """
            let rec choices first last count =
                if count = 0 then [ [] ]
                else [ for number in first .. last - count do for rest in choices (number + 1) last (count - 1) -> number :: rest ]
            """),
        EmptyChoiceMissing("logic-ocaml-empty-choice-missing",
            """
            let rec choices first last count =
              if count = 0 then [ [] ]
              else
                List.concat_map (fun number -> List.map (fun rest -> number :: rest) (choices (number + 1) last (count - 1)))
                  (List.init (max 0 (last - count - first + 1)) (fun k -> first + k))
            """),
        EmptyChoiceMissing("logic-elixir-empty-choice-missing",
            """
            def choices(_from, _to, 0), do: [[]]

            def choices(from, to, count) do
              for first <- from..(to - count)//1, rest <- choices(first + 1, to, count - 1), do: [first | rest]
            end
            """),
        EmptyChoiceMissing("logic-erlang-empty-choice-missing",
            """
            choices(_From, _To, 0) -> [[]];
            choices(From, To, Count) ->
                [[First | Rest] || First <- lists:seq(From, To - Count), Rest <- choices(First + 1, To, Count - 1)].
            """),
        LeafFlattenedAgain("logic-python-leaf-flattened-again",
            """
            def flatten(arr):
                for x in arr:
                    if isinstance(x, list):
                        for y in flatten(x):
                            yield y
                    else:
                        yield x
            """),
        LeafFlattenedAgain("logic-java-leaf-flattened-again",
            """
            public static Object flatten(Object arr) {
                if (arr instanceof ArrayList) {
                    ArrayList narr = (ArrayList) arr;
                    ArrayList result = new ArrayList(50);
                    for (Object x : narr) {
                        if (x instanceof ArrayList) {
                            result.addAll((ArrayList) flatten(x));
                        } else {
                            result.add(x);
                        }
                    }
                    return result;
                } else {
                    return flatten(arr);
                }
            }
            """),
        LeafFlattenedAgain("logic-csharp-leaf-flattened-again",
            """
            static IEnumerable<object> Flatten(List<object> items)
            {
                foreach (var item in items)
                {
                    if (item is List<object> inner)
                    {
                        foreach (var each in Flatten(inner))
                        {
                            yield return each;
                        }
                    }
                    else
                    {
                        yield return item;
                    }
                }
            }
            """),
        LeafFlattenedAgain("logic-js-leaf-flattened-again",
            """
            function* flatten(items) {
              for (const item of items) {
                if (Array.isArray(item)) {
                  yield* flatten(item);
                } else {
                  yield item;
                }
              }
            }
            """),
        LeafFlattenedAgain("logic-go-leaf-flattened-again",
            """
            func flatten(items []any) []any {
            	var flat []any
            	for _, item := range items {
            		if inner, isList := item.([]any); isList {
            			flat = append(flat, flatten(inner)...)
            		} else {
            			flat = append(flat, item)
            		}
            	}
            	return flat
            }
            """),
        LeafFlattenedAgain("logic-ruby-leaf-flattened-again",
            """
            def flatten(items)
              items.flat_map do |item|
                if item.is_a?(Array)
                  flatten(item)
                else
                  [item]
                end
              end
            end
            """),
        LeafFlattenedAgain("logic-lua-leaf-flattened-again",
            """
            local function flatten(items)
              local flat = {}
              for _, item in ipairs(items) do
                if type(item) == "table" then
                  for _, each in ipairs(flatten(item)) do
                    flat[#flat + 1] = each
                  end
                else
                  flat[#flat + 1] = item
                end
              end
              return flat
            end
            """),
        LeafFlattenedAgain("logic-php-leaf-flattened-again",
            """
            function flatten(array $items): array {
                $flat = [];
                foreach ($items as $item) {
                    if (is_array($item)) {
                        $flat = array_merge($flat, flatten($item));
                    } else {
                        $flat[] = $item;
                    }
                }
                return $flat;
            }
            """),
        LeafFlattenedAgain("logic-perl-leaf-flattened-again",
            """
            sub flatten {
                my @flat;
                for my $item (@_) {
                    if (ref $item eq 'ARRAY') {
                        push @flat, flatten(@$item);
                    } else {
                        push @flat, $item;
                    }
                }
                return @flat;
            }
            """),
        LeafFlattenedAgain("logic-powershell-leaf-flattened-again",
            """
            function Get-Flat($items) {
                $flat = [System.Collections.Generic.List[object]]::new()
                foreach ($item in $items) {
                    if ($item -is [array]) {
                        foreach ($each in (Get-Flat $item)) { $flat.Add($each) }
                    } else {
                        $flat.Add($item)
                    }
                }
                return ,$flat.ToArray()
            }
            """),
        LeafFlattenedAgain("logic-dart-leaf-flattened-again",
            """
            List<Object> flatten(List<Object> items) {
              final flat = <Object>[];
              for (final item in items) {
                if (item is List<Object>) {
                  flat.addAll(flatten(item));
                } else {
                  flat.add(item);
                }
              }
              return flat;
            }
            """),
        LeafFlattenedAgain("logic-scala-leaf-flattened-again",
            """
            def flatten(items: List[Any]): List[Any] = items.flatMap {
              case inner: List[Any] @unchecked => flatten(inner)
              case item => List(item)
            }
            """),
        LeafFlattenedAgain("logic-elixir-leaf-flattened-again",
            """
            def flatten(items) do
              Enum.flat_map(items, fn
                item when is_list(item) -> flatten(item)
                item -> [item]
              end)
            end
            """),
        LeafFlattenedAgain("logic-erlang-leaf-flattened-again",
            """
            flatten([]) -> [];
            flatten([Item | Rest]) when is_list(Item) -> flatten(Item) ++ flatten(Rest);
            flatten([Item | Rest]) -> [Item | flatten(Rest)].
            """),
        SubsetsWithoutFirstDropped("logic-python-subsets-without-first-dropped",
            """
            def powerset(items):
                if not items:
                    return [[]]
                first, *rest = items
                rest_subsets = powerset(rest)
                return rest_subsets + [[first] + subset for subset in rest_subsets]
            """),
        SubsetsWithoutFirstDropped("logic-java-subsets-without-first-dropped",
            """
            static List<List<Integer>> powerset(List<Integer> items) {
                if (items.isEmpty()) {
                    List<List<Integer>> justTheEmptySet = new ArrayList<>();
                    justTheEmptySet.add(new ArrayList<>());
                    return justTheEmptySet;
                }
                int first = items.get(0);
                List<List<Integer>> restSubsets = powerset(items.subList(1, items.size()));
                List<List<Integer>> subsets = new ArrayList<>(restSubsets);
                for (List<Integer> subset : restSubsets) {
                    List<Integer> withFirst = new ArrayList<>();
                    withFirst.add(first);
                    withFirst.addAll(subset);
                    subsets.add(withFirst);
                }
                return subsets;
            }
            """),
        SubsetsWithoutFirstDropped("logic-csharp-subsets-without-first-dropped",
            """
            static List<List<int>> Powerset(List<int> items)
            {
                if (items.Count == 0) return new List<List<int>> { new List<int>() };
                int first = items[0];
                var restSubsets = Powerset(items.Skip(1).ToList());
                return restSubsets.Concat(restSubsets.Select(subset => new List<int> { first }.Concat(subset).ToList())).ToList();
            }
            """),
        SubsetsWithoutFirstDropped("logic-js-subsets-without-first-dropped",
            """
            function powerset(items) {
                if (items.length === 0) {
                    return [[]];
                }
                const [first, ...rest] = items;
                const restSubsets = powerset(rest);
                return [...restSubsets, ...restSubsets.map((subset) => [first, ...subset])];
            }
            """),
        SubsetsWithoutFirstDropped("logic-go-subsets-without-first-dropped",
            """
            func powerset(items []int) [][]int {
            	if len(items) == 0 {
            		return [][]int{{}}
            	}
            	first := items[0]
            	restSubsets := powerset(items[1:])
            	subsets := append([][]int{}, restSubsets...)
            	for _, subset := range restSubsets {
            		subsets = append(subsets, append([]int{first}, subset...))
            	}
            	return subsets
            }
            """),
        SubsetsWithoutFirstDropped("logic-ruby-subsets-without-first-dropped",
            """
            def powerset(items)
              return [[]] if items.empty?
              first, *rest = items
              rest_subsets = powerset(rest)
              rest_subsets + rest_subsets.map { |subset| [first] + subset }
            end
            """),
        SubsetsWithoutFirstDropped("logic-lua-subsets-without-first-dropped",
            """
            local function powerset(items)
              if #items == 0 then
                return { {} }
              end
              local first = items[1]
              local restSubsets = powerset({ table.unpack(items, 2) })
              local subsets = { table.unpack(restSubsets) }
              for _, subset in ipairs(restSubsets) do
                subsets[#subsets + 1] = { first, table.unpack(subset) }
              end
              return subsets
            end
            """),
        SubsetsWithoutFirstDropped("logic-php-subsets-without-first-dropped",
            """
            function powerset(array $items): array {
                if (count($items) === 0) {
                    return [[]];
                }
                $first = $items[0];
                $restSubsets = powerset(array_slice($items, 1));
                $subsets = $restSubsets;
                foreach ($restSubsets as $subset) {
                    $subsets[] = array_merge([$first], $subset);
                }
                return $subsets;
            }
            """),
        SubsetsWithoutFirstDropped("logic-perl-subsets-without-first-dropped",
            """
            sub powerset {
                my @items = @_;
                return ([]) unless @items;
                my ($first, @rest) = @items;
                my @rest_subsets = powerset(@rest);
                return (@rest_subsets, map { [$first, @$_] } @rest_subsets);
            }
            """),
        SubsetsWithoutFirstDropped("logic-powershell-subsets-without-first-dropped",
            """
            function Get-Powerset($items) {
                if ($items.Count -eq 0) {
                    return ,@(,@())
                }
                $first = $items[0]
                $restSubsets = Get-Powerset @($items | Select-Object -Skip 1)
                $subsets = @($restSubsets)
                foreach ($subset in $restSubsets) {
                    $subsets += ,(@($first) + $subset)
                }
                return ,$subsets
            }
            """),
        SubsetsWithoutFirstDropped("logic-dart-subsets-without-first-dropped",
            """
            List<List<int>> powerset(List<int> items) {
              if (items.isEmpty) {
                return [[]];
              }
              final first = items.first;
              final restSubsets = powerset(items.sublist(1));
              return [...restSubsets, ...restSubsets.map((subset) => [first, ...subset])];
            }
            """),
        SubsetsWithoutFirstDropped("logic-scala-subsets-without-first-dropped",
            """
            def powerset(items: List[Int]): List[List[Int]] = {
              if (items.isEmpty) return List(Nil)
              val first = items.head
              val restSubsets = powerset(items.tail)
              restSubsets ++ restSubsets.map(subset => first :: subset)
            }
            """),
        SubsetsWithoutFirstDropped("logic-elixir-subsets-without-first-dropped",
            """
            def powerset([]), do: [[]]

            def powerset([first | rest]) do
              rest_subsets = powerset(rest)
              rest_subsets ++ Enum.map(rest_subsets, fn subset -> [first | subset] end)
            end
            """),
        SubsetsWithoutFirstDropped("logic-erlang-subsets-without-first-dropped",
            """
            powerset([]) -> [[]];
            powerset([First | Rest]) ->
                RestSubsets = powerset(Rest),
                RestSubsets ++ [[First | Subset] || Subset <- RestSubsets].
            """),
        SubsetsWithoutFirstDropped("logic-fsharp-subsets-without-first-dropped",
            """
            let rec powerset items =
                match items with
                | [] -> [ [] ]
                | first :: rest ->
                    let restSubsets = powerset rest
                    restSubsets @ List.map (fun subset -> first :: subset) restSubsets
            """),
        SubsetsWithoutFirstDropped("logic-ocaml-subsets-without-first-dropped",
            """
            let rec powerset items =
              match items with
              | [] -> [ [] ]
              | first :: rest ->
                let rest_subsets = powerset rest in
                rest_subsets @ List.map (fun subset -> first :: subset) rest_subsets
            """),
        SubsetsWithoutFirstDropped("logic-rust-subsets-without-first-dropped",
            """
            fn powerset(items: &[i32]) -> Vec<Vec<i32>> {
                if items.is_empty() {
                    return vec![vec![]];
                }
                let first = items[0];
                let rest_subsets = powerset(&items[1..]);
                let mut subsets = rest_subsets.clone();
                for subset in &rest_subsets {
                    let mut with_first = vec![first];
                    with_first.extend(subset);
                    subsets.push(with_first);
                }
                subsets
            }
            """),
        SubsetsWithoutFirstDropped("logic-cpp-subsets-without-first-dropped",
            """
            std::vector<std::vector<int>> powerset(const std::vector<int>& items) {
                if (items.empty()) {
                    return {{}};
                }
                int first = items[0];
                std::vector<std::vector<int>> restSubsets = powerset(std::vector<int>(items.begin() + 1, items.end()));
                std::vector<std::vector<int>> subsets = restSubsets;
                for (const auto& subset : restSubsets) {
                    std::vector<int> withFirst{first};
                    withFirst.insert(withFirst.end(), subset.begin(), subset.end());
                    subsets.push_back(withFirst);
                }
                return subsets;
            }
            """),
        RootGuessNotSquared("logic-python-root-guess-not-squared",
            """
            def sqrt(x, epsilon):
                approx = x / 2
                while abs(x - approx ** 2) > epsilon:
                    approx = 0.5 * (approx + x / approx)
                return approx
            """),
        RootGuessNotSquared("logic-java-root-guess-not-squared",
            """
            static double sqrt(double x, double epsilon) {
                double approx = x / 2d;
                while (Math.abs(x-approx*approx) > epsilon) {
                    approx = 0.5d * (approx + x / approx);
                }
                return approx;
            }
            """),
        RootGuessNotSquared("logic-csharp-root-guess-not-squared",
            """
            static double SquareRoot(double x, double epsilon)
            {
                double guess = x / 2;
                while (Math.Abs(x - guess * guess) > epsilon)
                    guess = (guess + x / guess) / 2;
                return guess;
            }
            """),
        RootGuessNotSquared("logic-c-root-guess-not-squared",
            """
            double square_root(double x, double epsilon) {
                double guess = x / 2;
                while (fabs(x - guess * guess) > epsilon) {
                    guess = (guess + x / guess) / 2;
                }
                return guess;
            }
            """),
        RootGuessNotSquared("logic-cpp-root-guess-not-squared",
            """
            double squareRoot(double x, double epsilon) {
                double guess = x / 2;
                while (std::abs(x - guess * guess) > epsilon) {
                    guess = 0.5 * (guess + x / guess);
                }
                return guess;
            }
            """),
        RootGuessNotSquared("logic-js-root-guess-not-squared",
            """
            function squareRoot(x, epsilon) {
                let guess = x / 2;
                while (Math.abs(x - guess * guess) > epsilon) {
                    guess = (guess + x / guess) / 2;
                }
                return guess;
            }
            """),
        RootGuessNotSquared("logic-go-root-guess-not-squared",
            """
            func squareRoot(x, epsilon float64) float64 {
            	guess := x / 2
            	for math.Abs(x-guess*guess) > epsilon {
            		guess = (guess + x/guess) / 2
            	}
            	return guess
            }
            """),
        RootGuessNotSquared("logic-rust-root-guess-not-squared",
            """
            fn square_root(x: f64, epsilon: f64) -> f64 {
                let mut guess = x / 2.0;
                while (x - guess * guess).abs() > epsilon {
                    guess = (guess + x / guess) / 2.0;
                }
                guess
            }
            """),
        RootGuessNotSquared("logic-ruby-root-guess-not-squared",
            """
            def square_root(x, epsilon)
              guess = x / 2.0
              while (x - guess * guess).abs > epsilon
                guess = (guess + x / guess) / 2
              end
              guess
            end
            """),
        RootGuessNotSquared("logic-php-root-guess-not-squared",
            """
            function squareRoot(float $x, float $epsilon): float {
                $guess = $x / 2;
                while (abs($x - $guess * $guess) > $epsilon) {
                    $guess = ($guess + $x / $guess) / 2;
                }
                return $guess;
            }
            """),
        RootGuessNotSquared("logic-lua-root-guess-not-squared",
            """
            local function squareRoot(x, epsilon)
              local guess = x / 2
              while math.abs(x - guess * guess) > epsilon do
                guess = (guess + x / guess) / 2
              end
              return guess
            end
            """),
        RootGuessNotSquared("logic-perl-root-guess-not-squared",
            """
            sub square_root {
                my ($x, $epsilon) = @_;
                my $guess = $x / 2;
                while (abs($x - $guess * $guess) > $epsilon) {
                    $guess = ($guess + $x / $guess) / 2;
                }
                return $guess;
            }
            """),
        RootGuessNotSquared("logic-powershell-root-guess-not-squared",
            """
            function Get-SquareRoot($x, $epsilon) {
                $guess = $x / 2
                while ([Math]::Abs($x - $guess * $guess) -gt $epsilon) {
                    $guess = ($guess + $x / $guess) / 2
                }
                return $guess
            }
            """),
        RootGuessNotSquared("logic-dart-root-guess-not-squared",
            """
            double squareRoot(double x, double epsilon) {
              var guess = x / 2;
              while ((x - guess * guess).abs() > epsilon) {
                guess = (guess + x / guess) / 2;
              }
              return guess;
            }
            """),
        RootGuessNotSquared("logic-scala-root-guess-not-squared",
            """
            def squareRoot(x: Double, epsilon: Double): Double = {
              var guess = x / 2
              while (math.abs(x - guess * guess) > epsilon) {
                guess = (guess + x / guess) / 2
              }
              guess
            }
            """),
        RootGuessNotSquared("logic-fortran-root-guess-not-squared",
            """
            double precision function square_root(x, epsilon)
              double precision, intent(in) :: x, epsilon
              double precision :: guess
              guess = x / 2
              do while (abs(x - guess * guess) > epsilon)
                guess = (guess + x / guess) / 2
              end do
              square_root = guess
            end function square_root
            """),
        RootGuessNotSquared("logic-elixir-root-guess-not-squared",
            """
            def square_root(x, epsilon), do: improve(x, x / 2, epsilon)

            defp improve(x, guess, epsilon) do
              if abs(x - guess * guess) > epsilon do
                improve(x, (guess + x / guess) / 2, epsilon)
              else
                guess
              end
            end
            """),
        RootGuessNotSquared("logic-erlang-root-guess-not-squared",
            """
            square_root(X, Epsilon) -> improve(X, X / 2, Epsilon).

            improve(X, Guess, Epsilon) ->
                case abs(X - Guess * Guess) > Epsilon of
                    true -> improve(X, (Guess + X / Guess) / 2, Epsilon);
                    false -> Guess
                end.
            """),
        RootGuessNotSquared("logic-fsharp-root-guess-not-squared",
            """
            let squareRoot x epsilon =
                let rec improve guess =
                    if abs (x - guess * guess) > epsilon then improve ((guess + x / guess) / 2.0) else guess
                improve (x / 2.0)
            """),
        RootGuessNotSquared("logic-ocaml-root-guess-not-squared",
            """
            let square_root x epsilon =
              let rec improve guess =
                if Float.abs (x -. guess *. guess) > epsilon then improve ((guess +. x /. guess) /. 2.0) else guess
              in
              improve (x /. 2.0)
            """),
    ];
}
