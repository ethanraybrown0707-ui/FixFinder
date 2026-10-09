using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for what following every value through a program proves or shows is possible.</summary>
internal static class AnalysisGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts - so Python gives up rather than answer. FixFinder followed the values along one route through the code and found the number on the bottom of the division can be 0 by the time this line runs.",
            "Dividing by zero stops the program with an error, often only for the inputs nobody tried, like an empty list.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if count == 0:
                return 0
            return total / count
            """),

        GuideFor(["analysis-null-used"], "Using something that can be None",
            "None is Python's way of saying there is nothing here - it is what a search gives back when it found no match. It has no attributes and no items, so asking it for one stops the program. FixFinder found a route through the code where the value is still None when this line uses it.",
            "Reading an attribute of None, calling a method on it or taking an item from it stops the program with an error.",
            "Check for None before using it, or make sure every way through the code gives it a real value.",
            """
            found = re.match(pattern, text)
            if found is None:
                return ""
            return found.group(0)
            """),

        GuideFor(["analysis-type-mismatch"], "Combining values of the wrong types",
            "Some values just do not go together: you cannot add a number to a piece of text, or ask a number how long it is. FixFinder followed the values through the code and found that at this line they are certainly of kinds that cannot be combined this way.",
            "Python stops with a TypeError as soon as the line runs.",
            "Convert one side first: str(number) to join it to text, or int(text) to calculate with it.",
            """
            print("Age: " + str(age))
            """),

        GuideFor(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Counting positions starts at 0, not 1, so a list of four things has positions 0, 1, 2 and 3 - there is no position 4. FixFinder knows how long this list is at this point in the code, and the position being asked for is past its last one.",
            "Positions run from 0 to length - 1, so this stops the program with an IndexError.",
            "Use a position inside the list, such as -1 for the last item.",
            """
            points = [3, 5, 8]
            last = points[-1]
            """),

        GuideFor(["analysis-empty-collection"], "Taking an item from something empty",
            "pop() takes the last item off a list, and there has to be an item there to take. On every way to this line the list has nothing in it, so there is nothing to take.",
            "pop() on an empty list stops the program with an IndexError.",
            "Check that it has items first.",
            """
            if stack:
                top = stack.pop()
            """),

        GuideFor(["analysis-not-a-number"], "Converting text that is not a number",
            "int() and float() turn text into a number, but only when the text looks like one - \"42\" works, \"forty-two\" does not. The text here can never look like a number, so the conversion always fails.",
            "The conversion stops the program with a ValueError.",
            "Convert text that holds digits, and check input with isdigit() or try/except before converting it.",
            """
            value = int("42")
            """),

        GuideFor(["analysis-never-true"], "A condition that can never be true",
            "An if runs its block only when its condition is true. FixFinder followed the values to this point and found that, however the program gets here, the condition is false - so the block underneath never runs.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if mark > 100 or mark < 0:
                print("Out of range")
            """),

        GuideFor(["analysis-always-true"], "A condition that is always true",
            "A condition is meant to decide between two things. Here an earlier check has already made sure this one holds, so it is true every time and decides nothing.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            if mark >= 50:
                result = "pass"
            else:
                result = "fail"
            """),

        GuideFor(["analysis-loop-never-runs"], "A loop that never runs",
            "A loop checks its condition before its first pass. Here the condition is already false the very first time, so the loop's body is skipped completely.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            n = 10
            while n > 0:
                n -= 1
            """),

        GuideFor(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            n = 5
            while n > 0:
                print(n)
                n -= 1
            """),

        GuideFor(["analysis-assert-always-fails"], "An assert that always fails",
            "assert checks that something is true, and stops the program if it is not. FixFinder followed the values to this line and found the condition is false every time it is reached - so the program always stops here.",
            "The program stops with an AssertionError here every time.",
            "Check the condition; if it describes what should be true, the code before it is setting the value wrongly.",
            """
            n = 10
            assert n > 5
            """),

        GuideFor(["analysis-contract-broken"], "A call that breaks what the function checks for",
            "Some functions start by checking what they have been given and refusing anything wrong by raising an error. This call gives the function something its own check refuses, so the error is certain.",
            "The function raises its error as soon as the call runs.",
            "Give the function an argument it accepts, or check the value before calling it.",
            """
            if x >= 0:
                print(root(x))
            """),

        GuideFor(["analysis-wrong-arguments"], "A call with the wrong arguments",
            "A function's def line lists the values it needs. This call gives it a different set - one missing, one too many, or one with a name the function does not have - so Python cannot match them up.",
            "Python stops with a TypeError when the call runs.",
            "Give exactly the arguments the definition lists, in order or by their names.",
            """
            def area(width, height):
                return width * height

            print(area(3, 4))
            """),

        GuideFor(["analysis-type-hint-broken"], "A value that does not match its type hint",
            "A type hint is a note saying what kind of value a function takes or gives back, like score: int. Python does not enforce the note, but here the value can never be the kind the note says.",
            "Python does not stop at a wrong hint, but the code that trusts the hint - or a type checker - will be wrong about it.",
            "Return or pass a value of the hinted type, or change the hint to say what really happens.",
            """
            def label(score: int) -> str:
                if score > 50:
                    return "pass"
                return "fail"
            """),

        GuideFor(["analysis-used-after-close"], "Using a file after it is closed",
            "A file has to be open to read from it or write to it. Leaving a with block closes the file automatically - and every way to this line has already closed it.",
            "Reading or writing a closed file stops the program with a ValueError.",
            "Use the file inside the with block, or open it again.",
            """
            with open(path) as handle:
                first = handle.readline()
            """),

        GuideFor(["analysis-lock-not-released"], "A lock that is not always released",
            "A lock lets only one thread at a time do something. Taking it with acquire() means giving it back with release(). On one way out of this function it is never given back, so every other thread that wants it waits for ever.",
            "Anything else that needs the lock waits for ever, so the program freezes.",
            "Use the lock in a with block, which always releases it.",
            """
            with lock:
                count += 1
            """),
        GuideFor(["analysis-lost-update"], "An update two threads can lose",
            "An update like count += 1 looks like one step, but the computer does it in three: read the value, add, write it back. If two threads do it at the same moment, both can read the same old value, and one of the additions is lost.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Hold a lock around the update, so only one thread does it at a time.",
            """
            with lock:
                counter += 1
            """),

        GuideFor(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two threads each need the same two locks. One takes the first and then the second; the other takes them the other way round. If each gets its first lock at the same moment, each waits for the other's - for ever.",
            "The program freezes - a deadlock - and only sometimes, when the timing lines up.",
            "Always take the locks in the same order everywhere.",
            """
            with first_lock:
                with second_lock:
                    move(money)
            """),

        GuideFor(["analysis-lock-cycle"], "Locks taken round a circle",
            "Picture people round a table, each holding one fork and waiting for the fork of the person beside them. Nobody lets go, so nobody ever eats. FixFinder found places in the program that take locks in an order that goes all the way round like that: each one holds a lock the next one is waiting for.",
            "The program freezes - a deadlock - and only when the threads' timing lines up, so it can pass every test and still hang.",
            "Give the locks one order and take them in that order everywhere - for example, always the lower-numbered one first.",
            """
            first, second = sorted([left, right], key=id)
            with first:
                with second:
                    eat()
            """),

        GuideFor(["analysis-lock-reacquired"], "A lock taken again by the thread holding it",
            "A Lock is like a key only one person can hold at a time. This thread already has the key, and then waits for it to be handed over - but the only one who could hand it over is the thread itself, so it waits for ever.",
            "The program hangs at this line whenever it gets here, with no error message.",
            "Use threading.RLock, which the thread holding it can take again, or arrange for the lock to be taken only once.",
            """
            self.lock = threading.RLock()
            """),

        GuideFor(["analysis-loop-can-get-stuck"], "A loop that can come back round with nothing changed",
            "A loop ends when its variables move far enough - lo and hi meeting, say. Here there is a situation where going round once leaves them exactly where they were, so the next time round is the same as the last, and so is every one after it. FixFinder made sure that situation can really happen: it fits everything the loop's own code always keeps true.",
            "The program hangs - often only for particular inputs, such as a two-item list in a binary search.",
            "Make every way through the loop move its variables closer to the end - lo = mid + 1 rather than lo = mid.",
            """
            while lo < hi:
                mid = (lo + hi) // 2
                if a[mid] < x:
                    lo = mid + 1
                else:
                    hi = mid
            """),

        GuideFor(["analysis-code-injection"], "Running what the user typed as code",
            "eval() treats the text it is given as a piece of Python and runs it. Here the text is whatever the person running the program typed - so they can type any Python at all, and it runs as if it were part of your program.",
            "Anything typed runs: it can read or delete files, or do anything else the program is allowed to.",
            "For a number use int() or float(); for a Python literal such as a list, ast.literal_eval().",
            """
            count = int(input("How many? "))
            """),

        GuideFor(["analysis-command-injection"], "Running a shell command built from what the user typed",
            "The shell reads a command as text, and some characters in it - like ; or | - mean 'and now run another command'. Building the command from what someone typed lets them add a command of their own.",
            "Whoever runs the program can make it run any command at all.",
            "Pass the command and its arguments as a list to subprocess.run, without shell=True.",
            """
            subprocess.run(["ls", folder])
            """),

        GuideFor(["analysis-sql-injection"], "SQL built from what the user typed",
            "A database reads the SQL it is given as instructions. Pasting what someone typed straight into those instructions means that if they type SQL, the database obeys it as if you had written it.",
            "Typing ' OR '1'='1 can show every row, and worse can change or delete data.",
            "Keep the SQL fixed and pass the values separately, with ? placeholders.",
            """
            connection.execute("SELECT * FROM orders WHERE customer = ?", (name,))
            """),

        GuideFor(["analysis-thread-started-twice"], "A thread started twice",
            "A Thread object is for one run of its work. Once start() has been called on it, it can never be started again - even after it has finished. Each new run needs a new Thread.",
            "The second start() stops the program with RuntimeError: threads can only be started once.",
            "Make a new Thread for each piece of work.",
            """
            for attempt in range(3):
                worker = threading.Thread(target=work)
                worker.start()
                worker.join()
            """),

        GuideFor(["analysis-join-before-start"], "Waiting for a thread that was never started",
            "join() means 'wait here until that thread has finished'. This thread has not been started yet, so there is nothing to wait for, and Python stops with an error.",
            "The program stops with RuntimeError: cannot join thread before it is started.",
            "Call start() before join().",
            """
            worker.start()
            worker.join()
            """),

        GuideFor(["analysis-finally-overrides"], "Leaving a finally block with return, break or continue",
            "The finally block always runs as the try block ends - even when it ended with an error. A return, break or continue in finally ends things right there, so an error the try raised is thrown away, and the try's own return value is replaced.",
            "Errors disappear without a trace, and a function returns something other than what its try block returned.",
            "Keep finally for cleaning up - closing, releasing - and return from the try block instead.",
            """
            try:
                return open(path).read()
            finally:
                print("done")
            """),

        GuideFor(["analysis-unassigned-after-error"], "A variable that may have no value after an error",
            "The variable is only given its value inside the try block. If the line that gives it fails, the program jumps straight to the except block - and the variable never gets a value at all, so reading it afterwards fails too.",
            "The program stops with UnboundLocalError - often hiding the error that caused it.",
            "Give the variable a value before the try, or in the except block, or leave the function in the except block.",
            """
            try:
                number = int(text)
            except ValueError:
                number = 0
            """),

        GuideFor(["analysis-resource-not-closed"], "A file that is never closed",
            "Opening a file is like borrowing it: it has to be given back by closing it. On this way out of the function nothing closes it, so it is left for Python to tidy up whenever it gets round to it - and until then the file stays open.",
            "Nothing closes the file on this way out, so it is left for Python to clean up: CPython closes it when the last reference goes, but other Pythons may keep it open, with what was written still unsaved, for as long as they like.",
            "Open it with with, which closes it however the function ends.",
            """
            with open(path) as handle:
                return handle.read()
            """),

        GuideFor(["analysis-changed-while-looping"], "A collection changed while a loop walks over it",
            "A for loop walks through a list one position at a time. Taking an item out moves everything after it back one place, so the loop steps over the item that moved into the gap - and here the change is made under another name for the same list, or inside a function the loop calls, where it is easy to miss.",
            "Items are missed without any error, or the loop stops with RuntimeError part-way through.",
            "Loop over a copy - for item in list(items): - or collect what to change and change it after the loop.",
            """
            for mark in list(marks):
                if mark < 50:
                    drop(marks, mark)
            """),

        GuideFor(["analysis-read-before-join"], "Reading a result before the threads have finished",
            "Starting a thread is like asking someone to count a pile of coins while you get on with something else. Reading the total straight away gets whatever they have counted so far, not the final answer. join() is waiting for them to say they have finished.",
            "The program shows a value from part-way through - different on each run, and usually wrong.",
            "Read the result after join() has returned for every thread that changes it.",
            """
            worker.start()
            worker.join()
            print(total)
            """),

        GuideFor(["analysis-data-race"], "Shared data used without the lock that guards it",
            "A lock is like a talking stick: only whoever holds it may change the shared value. Here one thread holds the stick while it changes the value, but another changes it without the stick - so the stick stops nothing. A lock only helps if every piece of code that uses the value takes the same one - even code that only reads it.",
            "Changes can be lost and totals come out wrong - differently from one run to the next.",
            "Hold the same lock everywhere the data is used.",
            """
            with lock:
                total += 1
            """),

        GuideFor(["analysis-run-not-start"], "run() called instead of start()",
            "start() tells a thread to go and do its work alongside the rest of the program. run() is the work itself - calling it directly just does the work right here, the ordinary way, with no new thread at all.",
            "Nothing runs at the same time, so the program is slower than it should be and never actually uses the thread.",
            "Call start(), which runs the work on the new thread.",
            """
            worker = threading.Thread(target=work)
            worker.start()
            """),

        GuideFor(["analysis-list-front-in-loop"], "Taking from or adding to the front of a list in a loop",
            "A list keeps its items one after another from the very start, with no gaps. Taking out the first item leaves a gap at the front, so every other item moves along one place to fill it; putting an item in front means every item moves along one place to make room. Inside a loop that happens on every pass, so a list of ten thousand items means ten thousand moves each time round.",
            "It gives the right answer either way, and is quick while the list is short. A queue that grows to thousands of items - a breadth-first search of a big graph, say - makes it the slowest line in the program.",
            "Use a collections.deque for a list taken from at the front: popleft() takes the first item and appendleft(x) puts one in front, without moving the rest. A deque has no slices and prints as deque([...]), so check what else the program does with the list first. If the loop only ever adds to the front and nothing reads the list until the loop is done, add to the end instead and reverse the list once afterwards.",
            """
            from collections import deque

            queue = deque([start])
            while queue:
                node = queue.popleft()
                print(node)
            """),
    ];

    /// <summary>What Go does when a program goes wrong, in Go's own words and code.</summary>
    public static IReadOnlyList<GuideEntry> Go { get; } =
    [
        GuideFor(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Dividing a whole number by zero panics with `integer divide by zero`, often only for the inputs nobody tried, like an empty slice.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if count == 0 {
                return 0
            }
            return total / count
            """),

        GuideFor(["analysis-null-used"], "Reading something through a nil pointer",
            "A pointer holds the address of a value, and nil means it holds no address at all. On one route through the code this pointer is still nil when the line tries to read a field through it.",
            "Reading a field through a nil pointer panics with `invalid memory address or nil pointer dereference`. A nil slice or map is fine to measure, walk or read from; a pointer is not.",
            "Check for nil first, or give the pointer a value on every way through the code.",
            """
            if node == nil {
                return 0
            }
            return node.value
            """),

        GuideFor(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Positions in a slice start at 0, so a slice of three things has positions 0, 1 and 2. FixFinder knows how long this slice is here, and the position asked for is past its last one.",
            "Positions run from 0 to len(x)-1, so this panics with `index out of range`.",
            "Use a position inside the slice, such as len(x)-1 for the last item.",
            """
            if len(points) > 0 {
                last := points[len(points)-1]
                fmt.Println(last)
            }
            """),

        GuideFor(["analysis-lock-not-released"], "A lock that is not always released",
            "A mutex lets one goroutine at a time into a part of the code: Lock() to go in, Unlock() to come out. On one way out of this function Unlock() is never reached - often because of a return in between - so everything waiting for the mutex waits for ever.",
            "Everything else that needs the lock waits for ever.",
            "Release it with defer, which runs on every way out of the function.",
            """
            c.mu.Lock()
            defer c.mu.Unlock()
            c.count += n
            """),

        GuideFor(["analysis-lost-update"], "An update two goroutines can lose",
            "count++ looks like one step, but the computer reads count, adds one and writes it back. If two goroutines do that at the same moment, both can read the same old value, and one of the additions disappears.",
            "Two goroutines can read the same old value, so one of the updates is lost.",
            "Hold a mutex around the update, or use sync/atomic.",
            """
            var mu sync.Mutex
            mu.Lock()
            count++
            mu.Unlock()
            """),
    ];

    /// <summary>What JavaScript does when a program goes wrong, in JavaScript's own words and code.</summary>
    public static IReadOnlyList<GuideEntry> JavaScript { get; } =
    [
        GuideFor(["analysis-null-used"], "Using something that is null or undefined",
            "null and undefined both mean 'no value'. On one route through the code this value is still null or undefined when the line asks it for one of its parts - and a no-value has no parts. It happens with a variable declared without a value, a search that found nothing, or a field that is only sometimes set.",
            "Reading a property of null or undefined stops the program with a TypeError.",
            "Check it first, or reach it with ?. so the whole chain gives undefined instead of failing.",
            """
            const found = people.find((p) => p.id === id);
            if (!found) {
              return '';
            }
            return found.name;
            """),

        GuideFor(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "The program hangs, using the processor and answering nothing.",
            "Change the value the condition reads inside the loop, or break out of it.",
            """
            let left = items.length;
            while (left > 0) {
              left -= 1;
            }
            """),
    ];

    /// <summary>What C and C++ do when a program goes wrong, and what the memory checks find.</summary>
    public static IReadOnlyList<GuideEntry> Native { get; } =
    [
        GuideFor(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Dividing a whole number by zero is undefined behaviour: on most machines it stops the program with a floating point exception.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0) {
                return 0;
            }
            return total / count;
            """),

        GuideFor(["analysis-null-used"], "Going through a pointer that can be NULL",
            "A pointer holds the address of some memory, and NULL means it holds none. malloc gives back NULL when there is no memory to give, and a pointer set only on some branches may still be NULL. On one route through the code, this one is NULL here.",
            "Reading or writing through a null pointer is undefined behaviour: it usually stops the program with a segmentation fault.",
            "Check what you were given before using it.",
            """
            int *numbers = malloc(count * sizeof(int));
            if (numbers == NULL) {
                return 1;
            }
            numbers[0] = 1;
            """),

        GuideFor(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "An array of three places has positions 0, 1 and 2. FixFinder knows this array's size here, and the position asked for is past the end - and C does not stop you: it just reads or writes whatever memory lies beyond.",
            "C does not check positions, so this reads or writes memory that belongs to something else - undefined behaviour, and often a crash or a security hole.",
            "Use a position inside the array, such as size - 1 for the last item.",
            """
            int marks[3] = {1, 2, 3};
            int last = marks[2];
            """),

        GuideFor(["analysis-use-after-free"], "Memory used after it is freed",
            "free gives memory back so it can be reused. Afterwards the pointer still holds the old address, but the memory is not yours any more - and this line uses it anyway.",
            "The memory may already belong to something else, so this reads or writes whatever is there now: undefined behaviour, and a common security hole.",
            "Take what you need before freeing, and set the pointer to NULL afterwards.",
            """
            struct node *next = n->next;
            free(n);
            n = next;
            """),

        GuideFor(["analysis-double-free"], "Memory freed twice",
            "Each block of memory from malloc has to be given back with free exactly once. This line frees memory that has already been freed.",
            "Freeing memory twice corrupts what the allocator keeps about it: the program usually stops, and it can be exploited.",
            "Free once, and set the pointer to NULL so a second free does nothing.",
            """
            free(buffer);
            buffer = NULL;
            """),

        GuideFor(["analysis-memory-leak"], "Memory nobody frees",
            "Memory from malloc stays yours until you give it back with free. This function gets some, then finishes without freeing it, returning it or storing it anywhere - so nothing can ever free it.",
            "The program keeps hold of memory it can never use again; in something long-running it grows until it stops.",
            "Free it before every way out of the function, or return it so the caller can.",
            """
            int *numbers = malloc(count * sizeof(int));
            /* ... */
            free(numbers);
            """),

        GuideFor(["analysis-dangling-pointer"], "The address of something that is about to go",
            "A function's own variables only exist while it runs. Giving back the address of one of them is like giving someone the address of a room that is about to be knocked down: by the time they get there, something else is in its place.",
            "Whatever is written through that address later lands on memory that is now something else's: undefined behaviour.",
            "Ask for memory that outlives the function, or let the caller pass memory in.",
            """
            int *counter(void)
            {
                int *count = malloc(sizeof(int));
                *count = 0;
                return count;
            }
            """),

        GuideFor(["analysis-uninitialised-read"], "A value read before it is given one",
            "A variable declared inside a C function does not start at 0 - it starts with whatever was left in that memory. Nothing has been stored in this one on any way to this line, so its value is leftover junk.",
            "The value is whatever happened to be in that memory, so the program does something different each time it runs.",
            "Give it a value where it is declared.",
            """
            int total = 0;
            printf("%d\n", total);
            """),
    ];

    public static IReadOnlyList<GuideEntry> Java { get; } =
    [
        GuideFor(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Dividing a whole number by zero stops the program with an ArithmeticException, often only for the inputs nobody tried, like an empty array.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0) {
                return 0;
            }
            return total / count;
            """),

        GuideFor(["analysis-null-used"], "Using something that can be null",
            "A variable for an object holds a reference - an arrow to the object - and null means the arrow points at nothing. On one route through the code this one is still null when the line uses it.",
            "Calling a method on null or reading a field of it stops the program with a NullPointerException.",
            "Check for null before using it, or make sure every way through the code gives it a real value.",
            """
            String message = "none";
            if (score > 90) {
                message = "top";
            }
            return message.toUpperCase();
            """),

        GuideFor(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Positions in an array or a list start at 0, so one of three things has positions 0, 1 and 2. FixFinder knows how many things there are here, and the position asked for is past the last one.",
            "Positions run from 0 to length - 1, so this stops the program with an IndexOutOfBoundsException - an ArrayIndexOutOfBoundsException for an array.",
            "Use a position inside it, such as length - 1 - or size() - 1 for a list - for the last item.",
            """
            int[] points = {3, 5, 8};
            int last = points[points.length - 1];

            List<String> names = List.of("Ada", "Alan");
            String lastName = names.get(names.size() - 1);
            """),

        GuideFor(["analysis-empty-collection"], "Taking an item from something empty",
            "pop() and remove() take an item out of a stack or a queue, and there has to be an item there to take. On every way to this line the collection has nothing in it.",
            "Taking an item from an empty stack or queue stops the program with an exception.",
            "Check that it has items first.",
            """
            if (!stack.isEmpty()) {
                int top = stack.pop();
            }
            """),

        GuideFor(["analysis-not-a-number"], "Converting text that is not a number",
            "Integer.parseInt turns text into a number, but only when the text is written as a number - \"42\" works, \"forty-two\" does not. The text here can never be read as a number, so the conversion always fails.",
            "The conversion stops the program with a NumberFormatException.",
            "Convert text that holds digits, and catch NumberFormatException around text that comes from outside the program.",
            """
            int value = Integer.parseInt("42");
            """),

        GuideFor(["analysis-never-true"], "A condition that can never be true",
            "An if runs its block only when its condition is true. FixFinder followed the values to this point and found that, however the program gets here, the condition is false - so the block underneath never runs.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if (mark > 100 || mark < 0) {
                System.out.println("Out of range");
            }
            """),

        GuideFor(["analysis-always-true"], "A condition that is always true",
            "A condition is meant to decide between two things. Here an earlier check has already made sure this one holds, so it is true every time and decides nothing.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            if (mark >= 50) {
                result = "pass";
            } else {
                result = "fail";
            }
            """),

        GuideFor(["analysis-loop-never-runs"], "A loop that never runs",
            "A loop checks its condition before its first pass. Here the condition is already false the very first time, so the loop's body is skipped completely.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            int n = 10;
            while (n > 0) {
                n--;
            }
            """),

        GuideFor(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            for (int i = 0; i < 10; i++) {
                System.out.println(i);
            }
            """),

        GuideFor(["analysis-assert-always-fails"], "An assert that always fails",
            "assert checks that something is true, and stops the program if it is not. The condition here is false every time the line is reached - though Java only checks asserts when it is run with -ea.",
            "With assertions turned on (java -ea) the program stops with an AssertionError here every time.",
            "Check the condition; if it describes what should be true, the code before it is setting the value wrongly.",
            """
            int n = 10;
            assert n > 5;
            """),

        GuideFor(["analysis-contract-broken"], "A call that breaks what the method checks for",
            "Some methods start by checking what they have been given and throwing an exception if it is wrong. This call gives the method something its own check refuses, so the exception is certain.",
            "The method throws its exception as soon as the call runs.",
            "Give the method an argument it accepts, or check the value before calling it.",
            """
            if (n >= 0) {
                System.out.println(half(n));
            }
            """),

        GuideFor(["analysis-used-after-close"], "Using a stream after it is closed",
            "A stream has to be open to read from it or write to it. Every way to this line has already closed it - try-with-resources closes it at the end of its block.",
            "Reading or writing a closed stream throws an IOException.",
            "Finish using the stream before closing it - try-with-resources closes it at the right time.",
            """
            try (BufferedReader reader = new BufferedReader(new FileReader(path))) {
                String first = reader.readLine();
            }
            """),

        GuideFor(["analysis-lock-not-released"], "A lock that is not always released",
            "A lock lets one thread at a time into a part of the code: lock() to go in, unlock() to come out. On one way out of this method - a return, or an exception - unlock() is never reached, so every other thread waiting for the lock waits for ever.",
            "Every other thread that needs the lock waits for ever, so the program freezes.",
            "Unlock in a finally block, straight after the try that follows lock().",
            """
            lock.lock();
            try {
                count++;
            } finally {
                lock.unlock();
            }
            """),
        GuideFor(["analysis-lost-update"], "An update two threads can lose",
            "An update like count += 1 looks like one step, but the computer does it in three: read the value, add, write it back. If two threads do it at the same moment, both can read the same old value, and one of the additions is lost.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Make the update synchronized, or use an AtomicInteger.",
            """
            private final AtomicInteger count = new AtomicInteger();

            public void run() {
                count.incrementAndGet();
            }
            """),

        GuideFor(["analysis-stale-read"], "A flag a thread may never see change",
            "To run fast, each thread may keep its own copy of a value instead of looking it up every time. Without volatile, a thread looping on this flag may keep using the copy it read at the start - and never see another thread change it.",
            "The thread may never stop, even after the flag is set.",
            "Declare the field volatile, so every thread sees each write.",
            """
            private volatile boolean running = true;
            """),

        GuideFor(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two threads each need the same two locks. One takes the first and then the second; the other takes them the other way round. If each gets its first lock at the same moment, each waits for the other's - for ever.",
            "The program freezes - a deadlock - and only sometimes, when the timing lines up.",
            "Always take the locks in the same order everywhere.",
            """
            synchronized (first) {
                synchronized (second) {
                    move(money);
                }
            }
            """),

        GuideFor(["analysis-lock-cycle"], "Locks taken round a circle",
            "Picture people round a table, each holding one fork and waiting for the fork of the person beside them. Nobody lets go, so nobody ever eats. These places take locks in an order that goes all the way round like that: each one holds a lock the next one is waiting for.",
            "The program freezes - a deadlock - and only when the threads' timing lines up, so it can pass every test and still hang.",
            "Give the locks one order and take them in that order everywhere - for example, by an id each object has.",
            """
            Account first = a.id < b.id ? a : b;
            Account second = a.id < b.id ? b : a;
            synchronized (first) {
                synchronized (second) {
                    move(money);
                }
            }
            """),

        GuideFor(["analysis-loop-can-get-stuck"], "A loop that can come back round with nothing changed",
            "A loop ends when its variables move far enough - lo and hi meeting, say. There is a situation where going round once leaves them exactly where they were, so the next pass is the same as the last, and so is every one after it. FixFinder made sure that situation can really happen: it fits everything the loop's own code always keeps true.",
            "The program hangs - often only for particular inputs, such as a two-item array in a binary search.",
            "Make every way through the loop move its variables closer to the end - lo = mid + 1 rather than lo = mid.",
            """
            while (lo < hi) {
                int mid = (lo + hi) / 2;
                if (a[mid] < x) {
                    lo = mid + 1;
                } else {
                    hi = mid;
                }
            }
            """),

        GuideFor(["analysis-command-injection"], "Running a command built from outside text",
            "A command is a program's name followed by its arguments. Building it as one piece of text from what someone typed lets them slip in arguments, or a different program, of their own.",
            "Whoever runs the program can change what the command does.",
            "Pass the command and each argument separately, as a String[] or to a ProcessBuilder.",
            """
            new ProcessBuilder("ls", folder).start();
            """),

        GuideFor(["analysis-sql-injection"], "SQL built from outside text",
            "A database reads the SQL it is given as instructions. Joining what someone typed into those instructions means that if they type SQL, the database obeys it as if you had written it.",
            "Typing ' OR '1'='1 can show every row, and worse can change or delete data.",
            "Use a PreparedStatement with ? placeholders, and set each value with setString.",
            """
            PreparedStatement query = connection.prepareStatement("SELECT * FROM orders WHERE customer = ?");
            query.setString(1, customer);
            """),

        GuideFor(["analysis-thread-started-twice"], "A thread started twice",
            "A Thread object is for one run of its work. Once start() has been called on it, it can never be started again - even after it has finished.",
            "The second start() throws an IllegalThreadStateException.",
            "Make a new Thread for each piece of work.",
            """
            for (int i = 0; i < 3; i++) {
                Thread worker = new Thread(task);
                worker.start();
                worker.join();
            }
            """),

        GuideFor(["analysis-join-before-start"], "Waiting for a thread that was never started",
            "join() means 'wait here until that thread has finished'. This thread has not been started, so Java does not wait at all - join() returns straight away, and the code after it runs as if the work were done.",
            "The code after join() runs as if the thread's work were done, when it has not even begun.",
            "Call start() before join().",
            """
            worker.start();
            worker.join();
            """),

        GuideFor(["analysis-finally-overrides"], "Leaving a finally block with return, break or continue",
            "The finally block always runs as the try block ends - even when it ended with an exception. A return, break or continue in finally ends things right there, so an exception the try threw is thrown away, and the try's own return value is replaced.",
            "Exceptions disappear without a trace, and a method returns something other than what its try block returned.",
            "Keep finally for cleaning up - closing, releasing - and return from the try block instead.",
            """
            try {
                return Integer.parseInt(text);
            } finally {
                System.out.println("done");
            }
            """),

        GuideFor(["analysis-resource-not-closed"], "A file or stream that is never closed",
            "Opening a file or a stream is like borrowing it: it has to be given back by closing it. On one way out of this method it is never closed - and a writer that is never closed may never save what it was given.",
            "A writer that is never closed may never write out what it holds, so the file is left empty or cut short; any stream left open holds on to the file.",
            "Open it in a try-with-resources, which closes it however the method ends.",
            """
            try (FileWriter writer = new FileWriter("report.txt")) {
                writer.write("total: " + total);
            }
            """),

        GuideFor(["analysis-changed-while-looping"], "A collection changed while a loop walks over it",
            "A for-each loop walks through a collection with a hidden helper that remembers where it is. Here the collection is changed while the loop is going - under another name for the same collection, or in a method the loop calls - so Java stops the loop.",
            "The loop can stop with a ConcurrentModificationException, often only for some data.",
            "Loop over a copy, or remove through the iterator with it.remove(), or collect what to remove and call removeAll after the loop.",
            """
            for (String item : new ArrayList<>(items)) {
                discard(item);
            }
            """),

        GuideFor(["analysis-read-before-join"], "Reading a result before the threads have finished",
            "Starting work on another thread is like asking someone to count a pile of coins while you get on with something else. Reading the total straight away gets whatever they have counted so far, not the final answer; waiting for them to finish is what join does.",
            "The program shows a value from part-way through - different on each run, and usually wrong.",
            "Read the result after join() has returned for every thread that changes it.",
            """
            worker.start();
            worker.join();
            System.out.println(total);
            """),

        GuideFor(["analysis-data-race"], "Shared data used without the lock that guards it",
            "A lock is like a talking stick: only whoever holds it may use the shared value. Here one place holds the stick while it uses the value, but another uses it without the stick - so the stick protects nothing. A lock only helps if every piece of code that uses the value takes the same one - even code that only reads it.",
            "Changes can be lost, and a thread can read an out-of-date value - differently from one run to the next.",
            "Hold the same lock everywhere the data is used - for example, make the method that reads it synchronized too.",
            """
            public synchronized int getBalance() {
                return balance;
            }
            """),

        GuideFor(["analysis-wait-without-lock"], "wait or notify without its lock",
            "wait() and notify() let threads signal each other about an object, and they only work while the thread holds that object's lock - inside synchronized on that object. Here the lock is not held.",
            "The call throws an IllegalMonitorStateException every time.",
            "Call it inside synchronized (that object), or from a synchronized method of it.",
            """
            synchronized void take() throws InterruptedException {
                while (!full) {
                    wait();
                }
            }
            """),

        GuideFor(["analysis-wait-not-in-loop"], "wait() not in a loop",
            "wait() puts a thread to sleep until it is told something has changed. But it can wake up without being told, or find that another thread has already used what it was waiting for. Checking again in a loop makes sure the condition really holds.",
            "The code after wait() can run while the condition it needs is still false.",
            "Wait in a while loop that checks the condition again each time it wakes.",
            """
            while (!full) {
                wait();
            }
            """),

        GuideFor(["analysis-run-not-start"], "run() called instead of start()",
            "start() tells a thread to go and do its work alongside the rest of the program. run() is the work itself - calling it directly just does the work right here, the ordinary way, with no new thread at all.",
            "Nothing runs at the same time, so the program is slower than it should be and never actually uses the thread.",
            "Call start(), which runs the work on the new thread.",
            """
            Thread worker = new Thread(task);
            worker.start();
            """),

        GuideFor(["analysis-text-built-in-loop"], "Building text a piece at a time in a loop",
            "A String in Java can never be changed once it is made. So text += piece does not add to the text you have - it makes a brand new String, copies all the text so far into it, then adds the piece. Inside a loop that copying happens on every pass, and each copy is longer than the one before.",
            "It gives the right text either way and is quick for a few pieces. With thousands of pieces the copying dominates, and the program slows down far more than the amount of text would suggest.",
            "Collect the pieces in a StringBuilder, which adds each one to the end of the text it already holds, and turn it into a String once the loop is done.",
            """
            StringBuilder report = new StringBuilder();
            for (String name : names) {
                report.append(name).append('\n');
            }
            String text = report.toString();
            """),

        GuideFor(["analysis-list-front-in-loop"], "Taking from or adding to the front of a list in a loop",
            "An ArrayList keeps its items one after another in a block of memory, with no gaps. remove(0) takes out the first item and then moves every other item along one place to fill the gap; add(0, x) moves every item along one place to make room. Inside a loop that happens on every pass, so a list of ten thousand items means ten thousand moves each time round.",
            "It gives the right answer either way, and is quick while the list is short. A queue that grows to thousands of items - a breadth-first search of a big graph, say - makes it the slowest line in the program.",
            "Use an ArrayDeque for a list taken from at the front: poll() takes the first item and addFirst(x) puts one in front, without moving the rest, and add(x) still adds to the end. An ArrayDeque cannot hold null and cannot be read by position with get(i), so check what else the program does with the list first.",
            """
            Deque<Integer> queue = new ArrayDeque<>();
            queue.add(start);
            while (!queue.isEmpty()) {
                int node = queue.poll();
                System.out.println(node);
            }
            """),
    ];

    public static IReadOnlyList<GuideEntry> CSharp { get; } =
    [
        GuideFor(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Dividing a whole number by zero stops the program with a DivideByZeroException, often only for the inputs nobody tried, like an empty list.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0)
                return 0;
            return total / count;
            """),

        GuideFor(["analysis-null-used"], "Using something that can be null",
            "A variable for an object holds a reference - an arrow to the object - and null means it points at nothing. On one route through the code this one is still null when the line uses it.",
            "Calling a method on null or reading a property of it stops the program with a NullReferenceException.",
            "Check for null before using it, give a default with ??, or make sure every way through the code gives it a real value.",
            """
            string? message = null;
            if (score > 90) message = "top";
            return (message ?? "none").ToUpper();
            """),

        GuideFor(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Positions start at 0, so a collection of three things has positions 0, 1 and 2. FixFinder knows this one's length here, and the position asked for is past its last one.",
            "Positions run from 0 to Length - 1, so this stops the program with an IndexOutOfRangeException (ArgumentOutOfRangeException for a List).",
            "Use a position inside the collection, such as ^1 for the last item.",
            """
            int[] points = { 3, 5, 8 };
            int last = points[^1];
            """),

        GuideFor(["analysis-empty-collection"], "Taking an item from something empty",
            "Pop, Dequeue and First take an item out, and there has to be one there to take. On every way to this line the collection is empty.",
            "Pop, Dequeue, Peek, First and Last on an empty collection stop the program with an InvalidOperationException.",
            "Check Count first, or use TryPop, TryDequeue or FirstOrDefault.",
            """
            if (stack.TryPop(out var top))
                Console.WriteLine(top);
            """),

        GuideFor(["analysis-not-a-number"], "Converting text that is not a number",
            "int.Parse turns text into a number, but only when the text is written as a number. The text here can never be read as one, so the conversion always fails.",
            "The conversion stops the program with a FormatException.",
            "Convert text that holds digits, and use int.TryParse for text that comes from outside the program.",
            """
            if (int.TryParse(text, out var value))
                Console.WriteLine(value);
            """),

        GuideFor(["analysis-never-true"], "A condition that can never be true",
            "An if runs its block only when its condition is true. FixFinder followed the values to this point and found that, however the program gets here, the condition is false - so the block underneath never runs.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if (mark > 100 || mark < 0)
                Console.WriteLine("Out of range");
            """),

        GuideFor(["analysis-always-true"], "A condition that is always true",
            "A condition is meant to decide between two things. Here an earlier check has already made sure this one holds, so it is true every time and decides nothing.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            var result = mark >= 50 ? "pass" : "fail";
            """),

        GuideFor(["analysis-loop-never-runs"], "A loop that never runs",
            "A loop checks its condition before its first pass. Here the condition is already false the very first time, so the loop's body is skipped completely.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            int n = 10;
            while (n > 0)
                n--;
            """),

        GuideFor(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            for (int i = 0; i < 10; i++)
                Console.WriteLine(i);
            """),

        GuideFor(["analysis-contract-broken"], "A call that breaks what the method checks for",
            "Some methods start by checking what they have been given and throwing an exception if it is wrong. This call gives the method something its own check refuses, so the exception is certain.",
            "The method throws its exception as soon as the call runs.",
            "Give the method an argument it accepts, or check the value before calling it.",
            """
            if (n >= 0)
                Console.WriteLine(Half(n));
            """),

        GuideFor(["analysis-used-after-close"], "Using a stream after it is disposed",
            "A stream has to be open to use it. Leaving a using block disposes of the stream - closes it - and every way to this line has already done that.",
            "Using a disposed stream throws an ObjectDisposedException.",
            "Use the stream inside the using block, or open it again.",
            """
            using (var reader = new StreamReader(path))
            {
                var first = reader.ReadLine();
            }
            """),

        GuideFor(["analysis-lock-not-released"], "A lock that is not always released",
            "A lock lets one thread at a time into a part of the code. Monitor.Enter takes it and Monitor.Exit gives it back; on one way out of this method Exit is never reached, so every other thread that wants the lock waits for ever.",
            "Every other thread that needs the lock waits for ever, so the program freezes.",
            "Use a lock statement, which always releases it.",
            """
            lock (gate)
            {
                count++;
            }
            """),
        GuideFor(["analysis-lost-update"], "An update two threads can lose",
            "An update like count += 1 looks like one step, but the computer does it in three: read the value, add, write it back. If two threads do it at the same moment, both can read the same old value, and one of the additions is lost.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Use Interlocked.Add or Interlocked.Increment, or a lock statement around the update.",
            """
            Parallel.For(0, values.Length, i => Interlocked.Add(ref total, values[i]));
            """),

        GuideFor(["analysis-stale-read"], "A flag a thread may never see change",
            "To run fast, a thread may keep its own copy of a value instead of looking it up each time. Without volatile, a thread looping on this flag may keep using the copy it read first - and never see another thread change it.",
            "The thread may never stop, even after the flag is set.",
            "Declare the field volatile, or use a CancellationToken.",
            """
            private volatile bool running = true;
            """),

        GuideFor(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two threads each need the same two locks. One takes the first and then the second; the other takes them the other way round. If each gets its first lock at the same moment, each waits for the other's - for ever.",
            "The program freezes - a deadlock - and only sometimes, when the timing lines up.",
            "Always take the locks in the same order everywhere.",
            """
            lock (first)
            {
                lock (second)
                {
                    Move(money);
                }
            }
            """),

        GuideFor(["analysis-lock-cycle"], "Locks taken round a circle",
            "Picture people round a table, each holding one fork and waiting for the fork of the person beside them. Nobody lets go, so nobody ever eats. These places take locks in an order that goes all the way round like that: each one holds a lock the next one is waiting for.",
            "The program freezes - a deadlock - and only when the threads' timing lines up, so it can pass every test and still hang.",
            "Give the locks one order and take them in that order everywhere - for example, by an Id each object has.",
            """
            var first = a.Id < b.Id ? a : b;
            var second = a.Id < b.Id ? b : a;
            lock (first)
            {
                lock (second)
                {
                    Move(money);
                }
            }
            """),

        GuideFor(["analysis-loop-can-get-stuck"], "A loop that can come back round with nothing changed",
            "A loop ends when its variables move far enough - lo and hi meeting, say. There is a situation where going round once leaves them exactly where they were, so the next pass is the same as the last, and so is every one after it. FixFinder made sure that situation can really happen: it fits everything the loop's own code always keeps true.",
            "The program hangs - often only for particular inputs, such as a two-item array in a binary search.",
            "Make every way through the loop move its variables closer to the end - lo = mid + 1 rather than lo = mid.",
            """
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (a[mid] < x) lo = mid + 1;
                else hi = mid;
            }
            """),

        GuideFor(["analysis-command-injection"], "Starting a program named by outside text",
            "Process.Start runs another program, chosen by the text it is given. Here that text comes from whoever runs the program, so they get to choose which program runs.",
            "Whoever runs the program can make it start a program of their choosing.",
            "Check the text against the commands the program means to run before starting one.",
            """
            if (allowed.Contains(tool)) Process.Start(tool);
            """),

        GuideFor(["analysis-sql-injection"], "SQL built from outside text",
            "A database reads SQL as instructions. Building the SQL from what someone typed means that if they type SQL, the database obeys it as if you had written it.",
            "Typing ' OR '1'='1 can show every row, and worse can change or delete data.",
            "Keep the SQL fixed and pass the values as parameters.",
            """
            var command = new SqlCommand("SELECT * FROM Orders WHERE Customer = @name", connection);
            command.Parameters.AddWithValue("@name", name);
            """),

        GuideFor(["analysis-thread-started-twice"], "A thread started twice",
            "A Thread object is for one run of its work. Once Start() has been called on it, it can never be started again - even after it has finished.",
            "The second Start() throws a ThreadStateException.",
            "Make a new Thread for each piece of work - or use Task.Run.",
            """
            for (var i = 0; i < 3; i++)
            {
                var worker = new Thread(Work);
                worker.Start();
                worker.Join();
            }
            """),

        GuideFor(["analysis-join-before-start"], "Waiting for a thread that was never started",
            "Join() means 'wait here until that thread has finished'. This thread has not been started yet, and .NET refuses to wait for a thread that has not started.",
            "Join() throws a ThreadStateException.",
            "Call Start() before Join().",
            """
            worker.Start();
            worker.Join();
            """),

        GuideFor(["analysis-resource-not-closed"], "A file or stream that is never disposed of",
            "Opening a file with a stream is like borrowing it: it has to be given back by disposing of it. On one way out of this method it is never disposed of - and a writer that is not may never save what it was given.",
            "A writer that is never disposed of may never write out what it holds, so the file is left empty or cut short; any stream left open holds on to the file.",
            "Declare it with using, which disposes of it however the method ends.",
            """
            using var writer = new StreamWriter("report.txt");
            writer.Write($"total: {total}");
            """),

        GuideFor(["analysis-changed-while-looping"], "A collection changed while a loop walks over it",
            "A foreach loop walks through a collection with a hidden helper that remembers where it is. Here the collection is changed while the loop is going - under another name, or in a method the loop calls - so .NET stops the loop.",
            "The next step of the loop throws InvalidOperationException: the collection was modified.",
            "Loop over a copy with ToList(), or collect what to change and change it after the loop.",
            """
            foreach (var name in names.ToList())
            {
                alias.Add(name + "!");
            }
            """),

        GuideFor(["analysis-read-before-join"], "Reading a result before the threads have finished",
            "Starting work on another thread is like asking someone to count a pile of coins while you get on with something else. Reading the total straight away gets whatever they have counted so far, not the final answer; waiting for them to finish is what join does.",
            "The program shows a value from part-way through - different on each run, and usually wrong.",
            "Read the result after the thread's Join() or the task's Wait() - or after awaiting it.",
            """
            task.Wait();
            Console.WriteLine(total);
            """),

        GuideFor(["analysis-data-race"], "Shared data used without the lock that guards it",
            "A lock is like a talking stick: only whoever holds it may use the shared value. Here one place holds the stick while it uses the value, but another uses it without the stick - so the stick protects nothing. A lock only helps if every piece of code that uses the value takes the same one - even code that only reads it.",
            "Changes can be lost, and a thread can read an out-of-date value - differently from one run to the next.",
            "Take the same lock everywhere the data is used.",
            """
            lock (gate)
            {
                total += amount;
            }
            """),

        GuideFor(["analysis-wait-without-lock"], "Monitor.Wait or Pulse without its lock",
            "Monitor.Wait and Pulse let threads signal each other about an object, and they only work while the thread holds that object's lock - inside lock on that object. Here the lock is not held.",
            "The call throws a SynchronizationLockException every time.",
            "Call it inside lock (that object).",
            """
            lock (gate)
            {
                while (!ready)
                    Monitor.Wait(gate);
            }
            """),

        GuideFor(["analysis-wait-not-in-loop"], "Monitor.Wait not in a loop",
            "Monitor.Wait puts a thread to sleep until it is told something changed. By the time it wakes and gets the lock back, another thread may already have used what it was waiting for. Checking again in a loop makes sure the condition really holds.",
            "The code after the wait can run while the condition it needs is still false.",
            "Wait in a while loop that checks the condition again each time it wakes.",
            """
            while (!ready)
                Monitor.Wait(gate);
            """),

        GuideFor(["analysis-text-built-in-loop"], "Building text a piece at a time in a loop",
            "A string in C# can never be changed once it is made. So text += piece does not add to the text you have - it makes a brand new string, copies all the text so far into it, then adds the piece. Inside a loop that copying happens on every pass, and each copy is longer than the one before.",
            "It gives the right text either way and is quick for a few pieces. With thousands of pieces the copying dominates, and the program slows down far more than the amount of text would suggest.",
            "Collect the pieces in a StringBuilder, which adds each one to the end of the text it already holds, and turn it into a string once the loop is done.",
            """
            var report = new StringBuilder();
            foreach (var name in names)
            {
                report.Append(name).Append('\n');
            }
            var text = report.ToString();
            """),

        GuideFor(["analysis-list-front-in-loop"], "Taking from or adding to the front of a list in a loop",
            "A List keeps its items one after another in a block of memory, with no gaps. RemoveAt(0) takes out the first item and then moves every other item along one place to fill the gap; Insert(0, x) moves every item along one place to make room. Inside a loop that happens on every pass, so a list of ten thousand items means ten thousand moves each time round.",
            "It gives the right answer either way, and is quick while the list is short. A queue that grows to thousands of items - a breadth-first search of a big graph, say - makes it the slowest line in the program.",
            "Use a Queue<T> for a list taken from at the front: Dequeue() takes the first item without moving the rest, and Enqueue(x) adds to the end. To put items in front, a LinkedList<T> does it with AddFirst(x). Neither can be read by position with [i], so check what else the program does with the list first.",
            """
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                Console.WriteLine(node);
            }
            """),
    ];
}
