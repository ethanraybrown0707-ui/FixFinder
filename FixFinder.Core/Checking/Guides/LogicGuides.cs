namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for logic mistakes, keyed by the pattern that finds them.</summary>
internal static class LogicGuides
{
    /// <summary>The guide for the checks with these ids: its title, and its explanation written for someone new to programming.</summary>
    internal static GuideEntry GuideFor(
        string[] ids, string title, string explanation, string why, string fix, string example) => new()
    {
        RuleIds = ids,
        Guide = new MistakeGuide(explanation, why, fix, example) { Title = title },
    };

    private static IReadOnlyList<GuideEntry> Shared { get; } =
    [
        GuideFor(["analysis-same-answer-always"], "A yes-or-no function that always gives the same answer",
            "A function named as a question - is it prime, has it a space, is it valid - is there to answer it, yes for some "
            + "values and no for others. Every return of this one gives back the same answer, so whatever it is asked about, "
            + "it says the same: the code that calls it cannot tell anything apart by it. Most often one of the returns was "
            + "meant to give the other answer - the one where it finds what it looks for, as a divisor in a prime test.",
            "Each if or loop that asks it goes the same way every time, so the program treats every value alike - every number "
            + "prime, every name valid - however wrong that is for some of them.",
            "Work out by hand one value it should say yes to and one it should say no to, follow each through the function, "
            + "and change the return that gives the wrong answer for one of them.",
            ""),

        GuideFor(["analysis-duplicate-function"], "A function written twice",
            "Two functions here are the same lines, but for the names of their own parameters and variables: one was made by "
            + "copying the other. Code written twice has to be put right twice - a mistake fixed in one copy stays in the other.",
            "The copies drift apart: the next fix or change goes into one of them, and the program then does one thing in one "
            + "place and something else in the other, which is hard to see from either.",
            "Keep one of them and call it from both places, with a parameter for anything that should differ between them - or "
            + "have the copy call the one it was copied from.",
            ""),

        GuideFor(["tests-miss-change"], "A change the tests do not catch",
            "Every test passes, so FixFinder measured what they check: it made small changes to the code they call, one at a "
            + "time in a copy - the kinds of change a mistake makes, a bound off by one or the wrong operator - and ran the tests "
            + "with each. With this one every test still passed, so if the code went wrong this way, the tests would not say so. "
            + "Now and then a change makes no difference to what the code does at all; then no test could notice it, and there "
            + "is nothing to add.",
            "Tests that pass whatever a line does check less than they seem to, and the mistakes they miss are the ones that "
            + "reach whoever uses the program.",
            "Add a test whose answer depends on that line, and check the answer exactly: for a bound or a comparison, one with a "
            + "value right on the boundary; for arithmetic, one whose answer would change with the other operator.",
            ""),

        GuideFor(["algorithm-sort-wrong"], "A sort that does not sort every list",
            "The function is written the way a sort is written, so FixFinder called it with lists of its own - empty, of one "
            + "item, in order, in reverse order, with repeats - and looked at what came back. For one of them it was not the "
            + "same items in order, or the call stopped with an error. The list shown is the shortest that went wrong, which "
            + "is the easiest to follow through the code by hand.",
            "A sort that works on the lists it was tried with by hand can still miss the last pair, the first item, or a "
            + "list of two - and everything that relies on the order goes wrong with it.",
            "Follow the list shown through the loops by hand, writing down the list after each pass: the pass where it stops "
            + "changing too early, or changes the wrong items, is where the loop's start, end or comparison is out by one.",
            ""),

        GuideFor(["algorithm-extreme-wrong"], "A search for the largest or smallest that misses it",
            "The function is written the way a search for the largest, or smallest, item is written - it keeps the best it "
            + "has seen and replaces it with any better item - so FixFinder called it with lists of its own, never empty, and "
            + "looked at what came back. For one of them it was not the list's largest, or smallest, item, or the call stopped "
            + "with an error.",
            "A start that is not an item of the list - 0, or a very large or small number - is never replaced when every item "
            + "is on the wrong side of it, so the answer is a number that is not in the list at all.",
            "Start with the list's first item, and compare the rest with it - or use the language's own largest and smallest, "
            + "such as max and min, which start from the list itself.",
            ""),

        GuideFor(["algorithm-prime-wrong"], "A prime test that gives a wrong answer",
            "The function is written the way a prime test is written - it tries numbers as divisors, and says not prime at the "
            + "first that divides exactly - so FixFinder called it with every number from 0 to 30 and set each answer beside "
            + "whether the number is prime. The number shown is the smallest it got wrong.",
            "A prime test usually goes wrong at the edges: 0 and 1, which are not prime and have no divisor to find; 4, 9 and "
            + "the other squares, when the divisors stop too soon; or every number, when the divisors run up to the number "
            + "itself, which always divides it.",
            "Say first that a number below 2 is not prime, then try each divisor from 2 while divisor times divisor is no more "
            + "than the number - not the number itself.",
            ""),

        GuideFor(["algorithm-leap-year-wrong"], "A leap-year test that gives a wrong answer",
            "The function is written the way a leap-year test is written - it asks whether 4, 100 or 400 divide the year - so "
            + "FixFinder called it with years whose answers are known, and set each answer beside whether the year is a leap "
            + "year. The year shown is the first it got wrong.",
            "A leap-year test usually goes wrong at the centuries: 1900 and 2100, which 4 divides but are not leap years because "
            + "100 divides them too; or 2000 and 2400, which are leap years after all because 400 divides them. A test that "
            + "asks only of 4 gets the first wrong, and one that leaves out 400 the second.",
            "Ask of 400 first, then of 100, then of 4: a year 400 divides is a leap year; otherwise one 100 divides is not; "
            + "otherwise one 4 divides is. As one condition: 4 divides it, and either 100 does not or 400 does.",
            ""),

        GuideFor(["algorithm-factorial-wrong"], "A factorial that gives a wrong answer",
            "The function is written the way a factorial is written - it multiplies a product by each number up to the one it is "
            + "given, or gives back that number times itself called with one less - so FixFinder called it with every number "
            + "from 1 to 10, and then 0, and set each answer beside that number's factorial. The number shown is the first it "
            + "got wrong.",
            "A factorial usually goes wrong at its ends: a loop that stops one short leaves out the number itself, so 5 gives "
            + "24; a product that starts at 0 stays 0 whatever it is multiplied by; and a call of itself that stops only at 1 "
            + "never stops when given 0, whose factorial is 1.",
            "Start the product at 1 and multiply it by every number from 1 up to and including n - in Python, range(1, n + 1). "
            + "Written as a function calling itself, give back 1 when n is 0 or 1, and n times the factorial of n - 1 otherwise.",
            ""),

        GuideFor(["algorithm-reversal-wrong"], "A reversal that does not reverse every list",
            "The function is written the way a reversal is written - it swaps the items at the two ends of the list and moves "
            + "in, or builds a new list from the last item back - so FixFinder called it with lists of its own, of odd and even "
            + "lengths, and set what came back beside each list in the opposite order. The list shown is the shortest it got "
            + "wrong.",
            "A reversal in place goes wrong at its middle: a loop that runs all the way along swaps each pair twice, and puts "
            + "the list back as it was; one that stops a step too soon leaves the middle pair of an even-length list as it was. "
            + "One that builds a new list from the end goes wrong at position 0, when its loop stops before it.",
            "Swap while the left position is before the right one - or for i in the first half only, len(items) // 2 times - "
            + "and move both in after each swap. Going back from the end, run down to position 0 itself: in Python, "
            + "range(len(items) - 1, -1, -1).",
            ""),

        GuideFor(["algorithm-gcd-wrong"], "A greatest common divisor that gives a wrong answer",
            "The function is written the way Euclid's greatest common divisor is written - it takes the remainder of one number "
            + "divided by the other until there is none - so FixFinder called it with pairs of numbers whose greatest common "
            + "divisor is known, and set each answer beside it. The pair shown is the first it got wrong.",
            "Euclid's algorithm goes wrong at its end: when the remainder is 0, the answer is the number that was divided by, "
            + "not the remainder - giving back the second number once it has become 0 gives 0 every time. And a pair with 0 "
            + "in it needs no division at all: every number divides 0, so the answer is the other number.",
            "Replace the pair a and b by b and a % b while b is not 0, then give back a. Calling itself, give back a when b is "
            + "0, and otherwise the greatest common divisor of b and a % b.",
            ""),

        GuideFor(["algorithm-fibonacci-wrong"], "Fibonacci numbers that are not the two before added",
            "The function is written the way the Fibonacci numbers are written - it keeps two numbers and works out the next "
            + "as their sum, or calls itself with one less and two less - so FixFinder called it with every number from 1 to "
            + "10 and checked what every Fibonacci function gives, wherever it starts: each answer is the two before it added.",
            "The two numbers have to be moved on together. Written one after the other - a = b, then b = a + b - the second line "
            + "reads the a just changed, so b is added to itself and the numbers double: 1, 2, 4, 8. Starting from the wrong pair, "
            + "or adding the wrong two, gives numbers that grow, but are not these.",
            "Move both on at once, a, b = b, a + b - or keep the sum first: next = a + b, then a = b, then b = next. Calling "
            + "itself, give back fib(n - 1) + fib(n - 2), with the first two numbers given back as they are.",
            ""),

        GuideFor(["algorithm-merge-wrong"], "A merge that loses or misplaces items",
            "The function is written the way two lists in order are merged - it compares the item it is at in each and takes the "
            + "smaller - so FixFinder called it with pairs of lists of its own, and set what came back beside every item of both, "
            + "in order. The pair shown is the shortest it got wrong.",
            "The loop runs while both lists have items left, so it stops as soon as one runs out - and whatever is left of the "
            + "other has not been taken. Without a step after the loop to take it, those items are lost: [1, 3] and [2] give "
            + "[1, 2], with no error to say so. Taking the larger item, or moving on in the wrong list, puts items out of order.",
            "After the loop, add what is left of each list - in Python, result.extend(first[i:]) and result.extend(second[j:]) - "
            + "and in the loop take the smaller item and move on in the list it came from.",
            ""),

        GuideFor(["algorithm-in-order-wrong"], "A test of order that gives a wrong answer",
            "The function is written the way a test of whether a list is in order is written - it compares each item with the one "
            + "after it and says no at the first pair out of order - so FixFinder called it with lists of its own and set each "
            + "answer beside its own comparison applied to every pair. The list shown is the shortest it got wrong.",
            "A test of order goes wrong at its ends: a loop that stops a step too soon never looks at the last pair, so a list "
            + "out of order only there - [1, 2, 4, 3] - is said to be in order; and a yes given inside the loop, at the first "
            + "pair in order, says so before the rest have been looked at.",
            "Run the loop over every pair - in Python, for i in range(len(items) - 1) - say no at the first pair out of order, "
            + "and say yes only after the loop.",
            ""),

        GuideFor(["algorithm-palindrome-wrong"], "A palindrome test that gives a wrong answer",
            "The function is written the way a palindrome test is written - it compares the letters at the two ends of a word "
            + "in towards its middle and says no at the first pair that differ - so FixFinder called it with words of its own "
            + "and set each answer beside the word read backwards. The word shown is the shortest it got wrong.",
            "A palindrome test goes wrong at its ends and its middle: the last letter is at length - 1, so one compared with "
            + "the letter at length is past the end; a loop that stops a pair short never compares the pair next to the "
            + "middle, so abca is said to be a palindrome; and a yes given inside the loop, at the first pair that matches, "
            + "says so before the rest have been compared.",
            "Compare word[i] with word[len(word) - 1 - i] for every i up to the middle - in Python, for i in "
            + "range(len(word) // 2) - say no at the first pair that differ, and say yes only after the loop.",
            ""),

        GuideFor(["algorithm-round-trip-wrong"], "A pair of functions that do not undo each other",
            "The two functions are named for one thing and its undoing - encrypt and decrypt, to_roman and from_roman - so "
            + "FixFinder called the first with words or numbers of its own, and the second with what the first gave back, "
            + "and set what came back beside what the first was given. The input shown is the first that did not come back.",
            "Whatever the first gives back, the second has to turn back into what it was given, or what is saved or sent "
            + "cannot be read again. A cipher goes wrong at the end of the alphabet: a letter shifted past z has to wrap "
            + "round to a, and back; and in Java and JavaScript the remainder of a number below zero is below zero, so "
            + "(c - 'a' - shift) % 26 can be -3 where Python's is 23.",
            "Make the second do each step of the first backwards, in the opposite order, and wrap a remainder below zero "
            + "round - in Java or JavaScript, ((c - 'a' - shift) % 26 + 26) % 26.",
            ""),

        GuideFor(["algorithm-search-wrong"], "A search that does not find every value",
            "The function is written the way a search is written, so FixFinder called it with lists of its own, in order, "
            + "and with every value in them and some that are not. For one of them it gave the wrong answer - not there when "
            + "it was, there when it was not, or the position of another item - or the call stopped with an error.",
            "A search that misses the first item, the last item or the only item of a list gives a wrong answer without "
            + "any error, and the program goes on as if the value were missing.",
            "Follow the list and the value shown through the loop by hand: check the loop's condition lets it look at every "
            + "item it should - for a binary search, low <= high, with low = mid + 1 and high = mid - 1 - and that it gives up "
            + "only after the loop has looked everywhere.",
            ""),

        GuideFor(["analysis-repeated-search"], "Searching from the start on every pass",
            "Looking for something in a list, or in a piece of text, means going through it from the start until it "
            + "turns up. Doing that inside a loop means doing it again on every pass, so when the loop and the list both "
            + "get ten times longer, the work gets a hundred times bigger rather than ten.",
            "It is correct either way, and stays quick while both are small. It is the pattern that gets slow first as "
            + "real data arrives, and the reason is rarely obvious afterwards.",
            "If the collection does not change inside the loop and its items can go in a set, put them in a set once "
            + "before the loop and search that instead - a set answers in about the same time however many items it "
            + "holds. Searching text for a piece of text cannot be sped up this way.",
            """
            wanted = set(names)
            for person in people:
                if person in wanted:
                    ...
            """),

        GuideFor(["wrong-output"], "Wrong output",
            "The program did not crash - it ran all the way through - but what it printed is different from what you said it " +
            "should print. So the mistake is in what the program works out, not in how it is written.",
            "The program looks as if it works, and gives the wrong answer.",
            "Change the line FixFinder points to; the lines the wrong runs passed through most are the likeliest place.",
            ""),

        GuideFor(["stopped-before-expected-output"], "Stopped before printing what was expected",
            "You said what the program should print, but it never got that far: it stopped with an error, or was still going " +
            "when the time ran out, before it had printed all of it. Whatever stopped it has to be put right first - only a " +
            "program that runs to the end can print everything it should.",
            "Until it runs to the end, the program cannot print everything it should, however right the rest of it is.",
            "Put right what stops it first - the error shown for the run - and then check what it prints again.",
            ""),

        GuideFor(["logic-python-is-literal"], "Comparing values with is",
            "`is` asks whether two names point at the very same thing in memory; == asks whether two values are equal. Python " +
            "sometimes keeps one copy of a small number or a short piece of text and shares it, so `is` can work while testing " +
            "and then fail with other values.",
            "Small numbers and short strings happen to be shared, so it works in testing and fails with other values.",
            "Compare values with == and !=.",
            """
            if answer == "yes":
                print("ok")
            """),

        GuideFor(["logic-python-assert-tuple"], "An assert that can never fail",
            "assert checks that something is true. Putting the condition and its message together in brackets makes a single " +
            "tuple - a pair of values - and a tuple with anything in it always counts as true, so this assert can never fail.",
            "The assert can never fail, so it never catches the mistake it was written to catch.",
            "Remove the brackets: assert condition, message.",
            """
            assert total >= 0, "total should never be negative"
            """),

        GuideFor(["logic-python-mutable-default"], "A list shared by every call",
            "A default value like items=[] is made once, when Python first reads the def line - not each time the function is " +
            "called. So every call that leaves the argument out shares the same list, and whatever one call adds is still " +
            "there in the next.",
            "Each call sees what earlier calls added, so results leak from one call into the next.",
            "Use None as the default and make a new list inside the function.",
            """
            def add_item(item, items=None):
                if items is None:
                    items = []
                items.append(item)
                return items
            """),

        GuideFor(["logic-python-result-discarded", "logic-result-discarded"], "A result that is thrown away",
            "Some methods, like upper() or strip() on text, do not change the original - they give back a new, changed copy. " +
            "The line calls one of them but does not keep the copy, so nothing actually changes.",
            "The line does nothing, so the change you wanted never happens.",
            "Store the result: name = name.upper().",
            """
            name = name.strip().title()
            """),

        GuideFor(["logic-python-return-in-loop", "logic-return-in-loop"], "A loop that stops after the first item",
            "return ends the whole function straight away. Here every way through the loop's first pass reaches a return, so " +
            "the loop never gets to its second item - anything further along is never looked at.",
            "Only the first item is ever looked at, so a match further along is never found.",
            "Return the 'not found' answer after the loop, once every item has been checked.",
            """
            for item in items:
                if item == target:
                    return True
            return False
            """),

        GuideFor(["logic-python-reset-in-loop", "logic-reset-in-loop"], "A total reset inside its own loop",
            "To add things up, you set the total to 0 once and then add each item to it. Here the total is set back to its " +
            "start at the beginning of every pass, which throws away everything added so far - so at the end it holds only " +
            "the last item's part.",
            "After the loop it only holds the last item's contribution, not the sum of all of them.",
            "Set the starting value once, before the loop.",
            """
            total = 0
            for price in prices:
                total += price
            """),

        GuideFor(["logic-python-comparison-statement"], "A comparison where an assignment was meant",
            "One = stores a value; two == compare values. This line compares with == but does nothing with the answer, so " +
            "nothing changes - it was probably meant to store a value with =.",
            "The variable is never changed.",
            "Use = to store a value.",
            """
            total = 0
            """),

        GuideFor(["logic-python-loop-never-advances", "logic-loop-never-advances"], "A loop that never ends",
            "A while loop keeps going until its condition becomes false. Nothing inside this loop changes anything the " +
            "condition looks at, so once the condition is true it stays true, and the loop never stops.",
            "The program hangs forever and never prints its result.",
            "Move the counter on inside the loop.",
            """
            i = 0
            while i < len(items):
                print(items[i])
                i += 1
            """),

        GuideFor(["logic-integer-division"], "Whole-number division loses the fraction",
            "When both numbers in a division are whole numbers, Java, C#, C and C++ give a whole-number answer and throw the " +
            "fraction away - 7 / 2 is 3. Storing that in a decimal variable afterwards is too late: the fraction is already gone.",
            "Averages and percentages come out rounded down - 7 / 2 gives 3, not 3.5.",
            "Make one side a decimal before dividing.",
            """
            double average = (double) sum / count;
            """),

        GuideFor(["logic-empty-loop-body"], "A loop with an empty body",
            "A semicolon straight after for (...) or while (...) is a complete, empty statement, so it becomes the loop's whole " +
            "body. The loop runs doing nothing, and the block underneath runs once, afterwards.",
            "The loop runs without doing anything, and the block below runs once afterwards.",
            "Remove the semicolon after the loop's brackets.",
            """
            for (int i = 0; i < n; i++) {
                total += values[i];
            }
            """),

        GuideFor(["logic-empty-if-body"], "An if that controls nothing",
            "A semicolon straight after if (...) is a complete, empty statement, so it becomes everything the if controls. The " +
            "block underneath is not part of the if, and runs every time.",
            "The block below runs every time, whether the condition is true or not.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                System.out.println("pass");
            }
            """),

        GuideFor(["logic-java-string-equals"], "Strings compared with ==",
            "In Java a String variable holds a reference - where the text is stored - and == compares those references, not " +
            "the letters. Text typed in or put together while the program runs is stored in a new place, so == says 'different' " +
            "even when the words match.",
            "Text read from input or built at run time is a new object, so the comparison fails even when the words match.",
            "Compare strings with .equals().",
            """
            if (answer.equals("yes")) {
                System.out.println("ok");
            }
            """),

        GuideFor(["logic-assignment-in-condition"], "An assignment inside a condition",
            "One = stores a value; two == compare values. The condition here uses one =, so it overwrites the variable instead " +
            "of checking it, and the if goes the same way almost every time.",
            "The condition assigns instead of checking, so the if takes the same branch every time and the variable is overwritten.",
            "Use == to compare.",
            """
            if (count == 0) {
                printf("empty\n");
            }
            """),

        GuideFor(["logic-bitwise-precedence"], "A bit test worked out in the wrong order",
            "Operators are worked out in a set order, as multiplication comes before addition in maths. In these languages == " +
            "comes before & and |, so flags & MASK == MASK does the comparison first and then the & on its answer.",
            "The test checks something other than what it reads as.",
            "Put brackets around the bit test.",
            """
            if ((flags & MASK) == MASK) {
            }
            """),

        GuideFor(["logic-switch-fallthrough"], "A case that runs into the next",
            "In a switch the program jumps to the matching case and keeps going down through the cases below until it meets a " +
            "break. This case has no break, so the next case's code runs as well.",
            "Choosing one option also runs the next.",
            "End the case with break.",
            """
            case 1:
                System.out.println("one");
                break;
            """),

        GuideFor(["logic-uninitialised-total"], "A total that starts from garbage",
            "In C and C++ a variable declared inside a function starts with whatever junk was in that memory, not with 0. " +
            "Adding to it adds to the junk, so the total is wrong - and different from run to run.",
            "The result is different from run to run.",
            "Start the total at 0.",
            """
            int total = 0;
            """),

        GuideFor(["logic-string-literal-modified"], "Changing a string literal",
            "Text written in quotes in C, like \"hello\", is stored where the program is not meant to change it. A pointer to it " +
            "may read the letters but must not change them; an array made from it, char name[] = \"hello\", is your own copy, " +
            "which you can change.",
            "The program crashes on some systems and silently changes shared text on others.",
            "Copy the text into an array first.",
            """
            char name[] = "hello";
            name[0] = 'H';
            """),

        GuideFor(["logic-c-string-equals"], "C strings compared with ==",
            "In C a string variable holds the address of where its letters are stored, and == compares addresses, not letters. " +
            "Two copies of the same word are usually at different addresses, so == says they are different.",
            "The comparison fails even when the words match.",
            "Use strcmp(a, b) == 0.",
            """
            if (strcmp(answer, "yes") == 0) {
            }
            """),

        GuideFor(["logic-cpp-catch-by-value"], "An exception caught by value",
            "Exceptions often come in families: a specific error built on a general one. Catching the general kind by value " +
            "makes a copy of just the general part, so the specific details are cut off.",
            "The handler loses the real error's type and message.",
            "Catch by const reference.",
            """
            catch (const std::exception& error) {
                std::cerr << error.what() << '\n';
            }
            """),

        GuideFor(["logic-cpp-non-virtual-destructor"], "A base class without a virtual destructor",
            "When an object is deleted, its destructor cleans up what it owns. If the base class's destructor is not virtual, " +
            "deleting a derived object through a pointer to the base runs only the base's clean-up, and the derived part's " +
            "never happens.",
            "Deleting a derived object through a base pointer skips the derived destructor, leaking what it owns.",
            "Give the base class a virtual destructor.",
            """
            class Shape {
            public:
                virtual ~Shape() = default;
            };
            """),

        GuideFor(["logic-js-var-in-closure"], "Every callback sees the last loop value",
            "A var variable in a for loop is one single variable shared by every pass. Functions made inside the loop remember " +
            "the variable, not its value at the time - so when they run later, they all see its final value.",
            "Every callback uses the last value instead of its own.",
            "Declare the loop variable with let.",
            """
            for (let i = 0; i < buttons.length; i++) {
              buttons[i].onclick = () => console.log(i);
            }
            """),

        GuideFor(["logic-js-numeric-sort"], "Numbers sorted as text",
            "sort() with nothing in its brackets sorts everything as text, character by character - and as text \"10\" comes " +
            "before \"9\", because \"1\" comes before \"9\". Giving sort a way to compare numbers fixes it.",
            "Numbers come out in the wrong order.",
            "Give sort a comparison: (a, b) => a - b.",
            """
            scores.sort((a, b) => a - b);
            """),

        GuideFor(["logic-js-map-parseint"], "parseInt given the index as its base",
            "map calls the function you give it with each item and also the item's position in the list. parseInt takes a " +
            "second value as the number base - base 1, base 2 and so on - so most items come out as NaN, 'not a number'.",
            "Most values come out as NaN.",
            "Use Number, or wrap parseInt: map(s => parseInt(s, 10)).",
            """
            const numbers = texts.map(Number);
            """),

        GuideFor(["logic-js-float-equality"], "Decimal numbers compared exactly",
            "Every number in JavaScript is stored in binary, where most fractions - 0.1, 0.2 - can only be stored approximately, " +
            "the way 1/3 can only be written as 0.333... in decimal. Arithmetic on them leaves a tiny error, so 0.1 + 0.2 is " +
            "0.30000000000000004, and 0.1 + 0.2 === 0.3 is false.",
            "An exact comparison fails even when the numbers are equal for every practical purpose, so the code it guards never runs.",
            "Check that the difference is smaller than a tiny tolerance: Math.abs(total - 0.3) < 1e-9.",
            """
            const total = 0.1 + 0.2;
            if (Math.abs(total - 0.3) < 1e-9) {
              console.log("equal");
            }
            """),
    ];

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        .. PythonPatternGuides.All,
        .. BracePatternGuides.All,
        .. ManagedPatternGuides.All,
        .. ScalaPatternGuides.All,
        .. OCamlPatternGuides.All,
        .. GoPatternGuides.All,
        .. RustPatternGuides.All,
        .. RubyPatternGuides.All,
        .. PhpPatternGuides.All,
        .. LuaPatternGuides.All,
        .. PerlPatternGuides.All,
        .. DartPatternGuides.All,
        .. FortranPatternGuides.All,
        .. PowerShellPatternGuides.All,
        .. ElixirPatternGuides.All,
        .. FSharpPatternGuides.All,
        .. ErlangPatternGuides.All,
        .. EveryLanguagePatternGuides.All,
        .. AnalysisGuides.All,
        .. Shared,
    ];

    /// <summary>The guides written in this file, apart from those the other pattern files add.</summary>
    internal static IReadOnlyList<GuideEntry> SharedGuides => Shared;
}
