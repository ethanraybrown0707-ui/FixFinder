using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for what following every value through a program proves or shows is possible.</summary>
internal static class AnalysisGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        AtEveryLevel(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts - so Python gives up rather than answer. FixFinder followed the values along one route through the code and found the number on the bottom of the division can be 0 by the time this line runs.",
            "Following the values through the code shows the number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "The divisor's abstract value includes 0 on at least one path reaching this line, so the division raises ZeroDivisionError.",
            "Dividing by zero stops the program with an error, often only for the inputs nobody tried, like an empty list.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if count == 0:
                return 0
            return total / count
            """),

        AtEveryLevel(["analysis-null-used"], "Using something that can be None",
            "None is Python's way of saying there is nothing here - it is what a search gives back when it found no match. It has no attributes and no items, so asking it for one stops the program. FixFinder found a route through the code where the value is still None when this line uses it.",
            "On at least one way through the code, the value is None when this line uses it - for example a variable set to None and only sometimes given a real value, or a search that found nothing.",
            "The value's abstract domain includes None on at least one path reaching this line, so the attribute access, call or subscript raises AttributeError or TypeError.",
            "Reading an attribute of None, calling a method on it or taking an item from it stops the program with an error.",
            "Check for None before using it, or make sure every way through the code gives it a real value.",
            """
            found = re.match(pattern, text)
            if found is None:
                return ""
            return found.group(0)
            """),

        Pattern(["analysis-type-mismatch"], "Combining values of the wrong types",
            "The values at this line are certainly of types that cannot be combined this way - text and a number, or a number used where something with a length or items is needed.",
            "Python stops with a TypeError as soon as the line runs.",
            "Convert one side first: str(number) to join it to text, or int(text) to calculate with it.",
            """
            print("Age: " + str(age))
            """),

        AtEveryLevel(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Counting positions starts at 0, not 1, so a list of four things has positions 0, 1, 2 and 3 - there is no position 4. FixFinder knows how long this list is at this point in the code, and the position being asked for is past its last one.",
            "The list or text has a known length here, and the position asked for is past its end.",
            "The index is outside 0 to length - 1 on this path, so the subscript raises IndexError.",
            "Positions run from 0 to length - 1, so this stops the program with an IndexError.",
            "Use a position inside the list, such as -1 for the last item.",
            """
            points = [3, 5, 8]
            last = points[-1]
            """),

        Pattern(["analysis-empty-collection"], "Taking an item from something empty",
            "The list is certainly empty when this line runs, so there is nothing to take.",
            "pop() on an empty list stops the program with an IndexError.",
            "Check that it has items first.",
            """
            if stack:
                top = stack.pop()
            """),

        Pattern(["analysis-not-a-number"], "Converting text that is not a number",
            "The text given to int() or float() here can never be read as a number.",
            "The conversion stops the program with a ValueError.",
            "Convert text that holds digits, and check input with isdigit() or try/except before converting it.",
            """
            value = int("42")
            """),

        Pattern(["analysis-never-true"], "A condition that can never be true",
            "Following the values shows this condition is false every time it is reached - often two tests that contradict each other, or a check after the value has already been ruled out.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if mark > 100 or mark < 0:
                print("Out of range")
            """),

        Pattern(["analysis-always-true"], "A condition that is always true",
            "Following the values shows this condition is true every time it is reached, because an earlier test already settled it.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            if mark >= 50:
                result = "pass"
            else:
                result = "fail"
            """),

        Pattern(["analysis-loop-never-runs"], "A loop that never runs",
            "The loop's condition is already false the first time it is checked, so the body is skipped entirely.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            n = 10
            while n > 0:
                n -= 1
            """),

        Pattern(["analysis-loop-never-ends"], "A loop that never ends",
            "Nothing inside the loop changes what its condition tests, so once the condition is true it stays true for ever.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            n = 5
            while n > 0:
                print(n)
                n -= 1
            """),

        Pattern(["analysis-assert-always-fails"], "An assert that always fails",
            "The condition in this assert is false every time the line is reached.",
            "The program stops with an AssertionError here every time.",
            "Check the condition; if it describes what should be true, the code before it is setting the value wrongly.",
            """
            n = 10
            assert n > 5
            """),

        Pattern(["analysis-contract-broken"], "A call that breaks what the function checks for",
            "The function starts by checking its arguments and raising an error when they are wrong, and this call certainly gives it arguments it refuses.",
            "The function raises its error as soon as the call runs.",
            "Give the function an argument it accepts, or check the value before calling it.",
            """
            if x >= 0:
                print(root(x))
            """),

        Pattern(["analysis-wrong-arguments"], "A call with the wrong arguments",
            "The call does not give the function the arguments its definition asks for: one is missing, there is one too many, or a name is wrong.",
            "Python stops with a TypeError when the call runs.",
            "Give exactly the arguments the definition lists, in order or by their names.",
            """
            def area(width, height):
                return width * height

            print(area(3, 4))
            """),

        Pattern(["analysis-type-hint-broken"], "A value that does not match its type hint",
            "A type hint says what a parameter or return value should be, and this value can never be that type.",
            "Python does not stop at a wrong hint, but the code that trusts the hint - or a type checker - will be wrong about it.",
            "Return or pass a value of the hinted type, or change the hint to say what really happens.",
            """
            def label(score: int) -> str:
                if score > 50:
                    return "pass"
                return "fail"
            """),

        Pattern(["analysis-used-after-close"], "Using a file after it is closed",
            "Every way to this line closes the file first - often by leaving the with block that opened it.",
            "Reading or writing a closed file stops the program with a ValueError.",
            "Use the file inside the with block, or open it again.",
            """
            with open(path) as handle:
                first = handle.readline()
            """),

        Pattern(["analysis-lock-not-released"], "A lock that is not always released",
            "The lock is taken with acquire(), and on at least one way out of the function it is not released.",
            "Anything else that needs the lock waits for ever, so the program freezes.",
            "Use the lock in a with block, which always releases it.",
            """
            with lock:
                count += 1
            """),
        Pattern(["analysis-lost-update"], "An update two threads can lose",
            "Several threads run this line at once, and += is three steps - read, add, write back - so two threads can read the same old value and one addition is lost.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Hold a lock around the update, so only one thread does it at a time.",
            """
            with lock:
                counter += 1
            """),

        Pattern(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two places take the same two locks in opposite orders. If two threads each get their first lock, each waits for ever for the other's.",
            "The program freezes - a deadlock - and only sometimes, when the timing lines up.",
            "Always take the locks in the same order everywhere.",
            """
            with first_lock:
                with second_lock:
                    move(money)
            """),

        AtEveryLevel(["analysis-lock-cycle"], "Locks taken round a circle",
            "Picture people round a table, each holding one fork and waiting for the fork of the person beside them. Nobody lets go, so nobody ever eats. FixFinder found places in the program that take locks in an order that goes all the way round like that: each one holds a lock the next one is waiting for.",
            "Each of these places takes a lock while holding another, and following them round comes back to the first lock. With a thread at each place, every thread holds the lock the next one needs, and all of them wait for ever.",
            "The lock-order graph has a cycle whose edges no common lock guards, so one thread per edge can each block on the lock held by the next.",
            "The program freezes - a deadlock - and only when the threads' timing lines up, so it can pass every test and still hang.",
            "Give the locks one order and take them in that order everywhere - for example, always the lower-numbered one first.",
            """
            first, second = sorted([left, right], key=id)
            with first:
                with second:
                    eat()
            """),

        AtEveryLevel(["analysis-lock-reacquired"], "A lock taken again by the thread holding it",
            "A Lock is like a key only one person can hold at a time. This thread already has the key, and then waits for it to be handed over - but the only one who could hand it over is the thread itself, so it waits for ever.",
            "This thread already holds the lock, and here it tries to take it again. A threading.Lock cannot be taken twice, even by the thread holding it, so the thread waits for itself and never goes on.",
            "threading.Lock is not reentrant: acquire() by the owning thread blocks until a release() that thread can never reach - a deadlock with no second thread.",
            "The program hangs at this line whenever it gets here, with no error message.",
            "Use threading.RLock, which the thread holding it can take again, or arrange for the lock to be taken only once.",
            """
            self.lock = threading.RLock()
            """),

        Pattern(["analysis-run-not-start"], "run() called instead of start()",
            "Calling run() does the thread's work right here, on the thread that calls it, and waits for it to finish.",
            "Nothing runs at the same time, so the program is slower than it should be and never actually uses the thread.",
            "Call start(), which runs the work on the new thread.",
            """
            worker = threading.Thread(target=work)
            worker.start()
            """),
    ];

    /// <summary>What Go does when a program goes wrong, in Go's own words and code.</summary>
    public static IReadOnlyList<GuideEntry> Go { get; } =
    [
        Pattern(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "Dividing a whole number by zero panics with `integer divide by zero`, often only for the inputs nobody tried, like an empty slice.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if count == 0 {
                return 0
            }
            return total / count
            """),

        Pattern(["analysis-null-used"], "Reading something through a nil pointer",
            "On at least one way through the code the pointer is nil when this line reads a field through it - for example a value only some branches set.",
            "Reading a field through a nil pointer panics with `invalid memory address or nil pointer dereference`. A nil slice or map is fine to measure, walk or read from; a pointer is not.",
            "Check for nil first, or give the pointer a value on every way through the code.",
            """
            if node == nil {
                return 0
            }
            return node.value
            """),

        Pattern(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "The slice or array has a known length here, and the position asked for is past its end.",
            "Positions run from 0 to len(x)-1, so this panics with `index out of range`.",
            "Use a position inside the slice, such as len(x)-1 for the last item.",
            """
            if len(points) > 0 {
                last := points[len(points)-1]
                fmt.Println(last)
            }
            """),

        Pattern(["analysis-lock-not-released"], "A lock that is not always released",
            "One way out of this function leaves the mutex locked - usually a return between Lock and Unlock.",
            "Everything else that needs the lock waits for ever.",
            "Release it with defer, which runs on every way out of the function.",
            """
            c.mu.Lock()
            defer c.mu.Unlock()
            c.count += n
            """),

        Pattern(["analysis-lost-update"], "An update two goroutines can lose",
            "Several goroutines run this line at once with nothing holding them apart, and it reads a value, changes it and writes it back.",
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
        Pattern(["analysis-null-used"], "Using something that is null or undefined",
            "On at least one way through the code the value is null or undefined when this line uses it - a variable declared with no value, a search that found nothing, or a field that is not always set.",
            "Reading a property of null or undefined stops the program with a TypeError.",
            "Check it first, or reach it with ?. so the whole chain gives undefined instead of failing.",
            """
            const found = people.find((p) => p.id === id);
            if (!found) {
              return '';
            }
            return found.name;
            """),

        Pattern(["analysis-loop-never-ends"], "A loop that never ends",
            "Nothing inside the loop changes what its condition reads, so once the loop starts it never stops.",
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
        Pattern(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "Dividing a whole number by zero is undefined behaviour: on most machines it stops the program with a floating point exception.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0) {
                return 0;
            }
            return total / count;
            """),

        Pattern(["analysis-null-used"], "Going through a pointer that can be NULL",
            "On at least one way through the code the pointer is NULL when this line goes through it - malloc can come back with nothing, and a pointer is only set on some branches.",
            "Reading or writing through a null pointer is undefined behaviour: it usually stops the program with a segmentation fault.",
            "Check what you were given before using it.",
            """
            int *numbers = malloc(count * sizeof(int));
            if (numbers == NULL) {
                return 1;
            }
            numbers[0] = 1;
            """),

        Pattern(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "The array has a known size here, and the position asked for is past its end.",
            "C does not check positions, so this reads or writes memory that belongs to something else - undefined behaviour, and often a crash or a security hole.",
            "Use a position inside the array, such as size - 1 for the last item.",
            """
            int marks[3] = {1, 2, 3};
            int last = marks[2];
            """),

        Pattern(["analysis-use-after-free"], "Memory used after it is freed",
            "This line goes through a pointer whose memory was already freed on the line the message names - often a linked list freed in a loop that then reads the next node.",
            "The memory may already belong to something else, so this reads or writes whatever is there now: undefined behaviour, and a common security hole.",
            "Take what you need before freeing, and set the pointer to NULL afterwards.",
            """
            struct node *next = n->next;
            free(n);
            n = next;
            """),

        Pattern(["analysis-double-free"], "Memory freed twice",
            "Every way to this line has already freed the same pointer.",
            "Freeing memory twice corrupts what the allocator keeps about it: the program usually stops, and it can be exploited.",
            "Free once, and set the pointer to NULL so a second free does nothing.",
            """
            free(buffer);
            buffer = NULL;
            """),

        Pattern(["analysis-memory-leak"], "Memory nobody frees",
            "This memory is asked for here, and the function returns without freeing it, handing it back or storing it anywhere.",
            "The program keeps hold of memory it can never use again; in something long-running it grows until it stops.",
            "Free it before every way out of the function, or return it so the caller can.",
            """
            int *numbers = malloc(count * sizeof(int));
            /* ... */
            free(numbers);
            """),

        Pattern(["analysis-dangling-pointer"], "The address of something that is about to go",
            "The address given back belongs to this function, and everything of the function's own is gone once it returns.",
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

        Pattern(["analysis-uninitialised-read"], "A value read before it is given one",
            "Nothing has been put in this variable on any way to this line - C does not clear what it hands you.",
            "The value is whatever happened to be in that memory, so the program does something different each time it runs.",
            "Give it a value where it is declared.",
            """
            int total = 0;
            printf("%d\n", total);
            """),
    ];

    public static IReadOnlyList<GuideEntry> Java { get; } =
    [
        Pattern(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "Dividing a whole number by zero stops the program with an ArithmeticException, often only for the inputs nobody tried, like an empty array.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0) {
                return 0;
            }
            return total / count;
            """),

        Pattern(["analysis-null-used"], "Using something that can be null",
            "On at least one way through the code, the value is null when this line uses it - for example a variable set to null and only sometimes given a real value.",
            "Calling a method on null or reading a field of it stops the program with a NullPointerException.",
            "Check for null before using it, or make sure every way through the code gives it a real value.",
            """
            String message = "none";
            if (score > 90) {
                message = "top";
            }
            return message.toUpperCase();
            """),

        Pattern(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "The array or text has a known length here, and the position asked for is past its end.",
            "Positions run from 0 to length - 1, so this stops the program with an ArrayIndexOutOfBoundsException.",
            "Use a position inside the array, such as length - 1 for the last item.",
            """
            int[] points = {3, 5, 8};
            int last = points[points.length - 1];
            """),

        Pattern(["analysis-empty-collection"], "Taking an item from something empty",
            "The collection is certainly empty when this line runs, so there is nothing to take.",
            "Taking an item from an empty stack or queue stops the program with an exception.",
            "Check that it has items first.",
            """
            if (!stack.isEmpty()) {
                int top = stack.pop();
            }
            """),

        Pattern(["analysis-not-a-number"], "Converting text that is not a number",
            "The text given to Integer.parseInt or Double.parseDouble here can never be read as a number.",
            "The conversion stops the program with a NumberFormatException.",
            "Convert text that holds digits, and catch NumberFormatException around text that comes from outside the program.",
            """
            int value = Integer.parseInt("42");
            """),

        Pattern(["analysis-never-true"], "A condition that can never be true",
            "Following the values shows this condition is false every time it is reached - often two tests that contradict each other, or a check after the value has already been ruled out.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if (mark > 100 || mark < 0) {
                System.out.println("Out of range");
            }
            """),

        Pattern(["analysis-always-true"], "A condition that is always true",
            "Following the values shows this condition is true every time it is reached, because an earlier test already settled it.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            if (mark >= 50) {
                result = "pass";
            } else {
                result = "fail";
            }
            """),

        Pattern(["analysis-loop-never-runs"], "A loop that never runs",
            "The loop's condition is already false the first time it is checked, so the body is skipped entirely.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            int n = 10;
            while (n > 0) {
                n--;
            }
            """),

        Pattern(["analysis-loop-never-ends"], "A loop that never ends",
            "Nothing inside the loop changes what its condition tests, so once the condition is true it stays true for ever.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            for (int i = 0; i < 10; i++) {
                System.out.println(i);
            }
            """),

        Pattern(["analysis-assert-always-fails"], "An assert that always fails",
            "The condition in this assert is false every time the line is reached.",
            "With assertions turned on (java -ea) the program stops with an AssertionError here every time.",
            "Check the condition; if it describes what should be true, the code before it is setting the value wrongly.",
            """
            int n = 10;
            assert n > 5;
            """),

        Pattern(["analysis-contract-broken"], "A call that breaks what the method checks for",
            "The method starts by checking its arguments and throwing an exception when they are wrong, and this call certainly gives it arguments it refuses.",
            "The method throws its exception as soon as the call runs.",
            "Give the method an argument it accepts, or check the value before calling it.",
            """
            if (n >= 0) {
                System.out.println(half(n));
            }
            """),

        Pattern(["analysis-used-after-close"], "Using a stream after it is closed",
            "Every way to this line closes the stream first.",
            "Reading or writing a closed stream throws an IOException.",
            "Finish using the stream before closing it - try-with-resources closes it at the right time.",
            """
            try (BufferedReader reader = new BufferedReader(new FileReader(path))) {
                String first = reader.readLine();
            }
            """),

        Pattern(["analysis-lock-not-released"], "A lock that is not always released",
            "The lock is taken with lock(), and on at least one way out of the method - a return, or an exception - it is not unlocked.",
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
        Pattern(["analysis-lost-update"], "An update two threads can lose",
            "Several threads run this line at once on the same field, and ++ is three steps - read, add, write back - so two threads can read the same old value and one increment is lost.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Make the update synchronized, or use an AtomicInteger.",
            """
            private final AtomicInteger count = new AtomicInteger();

            public void run() {
                count.incrementAndGet();
            }
            """),

        Pattern(["analysis-stale-read"], "A flag a thread may never see change",
            "A thread loops on this field while another method sets it. The field is not volatile and nothing in the loop synchronises, so Java may keep using the value it read first.",
            "The thread may never stop, even after the flag is set.",
            "Declare the field volatile, so every thread sees each write.",
            """
            private volatile boolean running = true;
            """),

        Pattern(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two places take the same two locks in opposite orders. If two threads each get their first lock, each waits for ever for the other's.",
            "The program freezes - a deadlock - and only sometimes, when the timing lines up.",
            "Always take the locks in the same order everywhere.",
            """
            synchronized (first) {
                synchronized (second) {
                    move(money);
                }
            }
            """),

        Pattern(["analysis-lock-cycle"], "Locks taken round a circle",
            "Each of these places takes a lock while holding another, and following them round comes back to the first lock. With a thread at each place, every thread holds the lock the next one needs, and all of them wait for ever.",
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

        Pattern(["analysis-wait-without-lock"], "wait or notify without its lock",
            "wait(), notify() and notifyAll() must be called while holding the lock of the object they are called on, and here that lock is not held.",
            "The call throws an IllegalMonitorStateException every time.",
            "Call it inside synchronized (that object), or from a synchronized method of it.",
            """
            synchronized void take() throws InterruptedException {
                while (!full) {
                    wait();
                }
            }
            """),

        Pattern(["analysis-wait-not-in-loop"], "wait() not in a loop",
            "A waiting thread can wake up without being notified, or after another thread has already used what it waited for.",
            "The code after wait() can run while the condition it needs is still false.",
            "Wait in a while loop that checks the condition again each time it wakes.",
            """
            while (!full) {
                wait();
            }
            """),

        Pattern(["analysis-run-not-start"], "run() called instead of start()",
            "Calling run() does the thread's work right here, on the thread that calls it, and waits for it to finish.",
            "Nothing runs at the same time, so the program is slower than it should be and never actually uses the thread.",
            "Call start(), which runs the work on the new thread.",
            """
            Thread worker = new Thread(task);
            worker.start();
            """),
    ];

    public static IReadOnlyList<GuideEntry> CSharp { get; } =
    [
        Pattern(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "Dividing a whole number by zero stops the program with a DivideByZeroException, often only for the inputs nobody tried, like an empty list.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0)
                return 0;
            return total / count;
            """),

        Pattern(["analysis-null-used"], "Using something that can be null",
            "On at least one way through the code, the value is null when this line uses it - for example a variable set to null and only sometimes given a real value, or a ?. that gave null.",
            "Calling a method on null or reading a property of it stops the program with a NullReferenceException.",
            "Check for null before using it, give a default with ??, or make sure every way through the code gives it a real value.",
            """
            string? message = null;
            if (score > 90) message = "top";
            return (message ?? "none").ToUpper();
            """),

        Pattern(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "The array, list or text has a known length here, and the position asked for is past its end.",
            "Positions run from 0 to Length - 1, so this stops the program with an IndexOutOfRangeException (ArgumentOutOfRangeException for a List).",
            "Use a position inside the collection, such as ^1 for the last item.",
            """
            int[] points = { 3, 5, 8 };
            int last = points[^1];
            """),

        Pattern(["analysis-empty-collection"], "Taking an item from something empty",
            "The collection is certainly empty when this line runs, so there is nothing to take.",
            "Pop, Dequeue, Peek, First and Last on an empty collection stop the program with an InvalidOperationException.",
            "Check Count first, or use TryPop, TryDequeue or FirstOrDefault.",
            """
            if (stack.TryPop(out var top))
                Console.WriteLine(top);
            """),

        Pattern(["analysis-not-a-number"], "Converting text that is not a number",
            "The text given to int.Parse or double.Parse here can never be read as a number.",
            "The conversion stops the program with a FormatException.",
            "Convert text that holds digits, and use int.TryParse for text that comes from outside the program.",
            """
            if (int.TryParse(text, out var value))
                Console.WriteLine(value);
            """),

        Pattern(["analysis-never-true"], "A condition that can never be true",
            "Following the values shows this condition is false every time it is reached - often two tests that contradict each other, or a check after the value has already been ruled out.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if (mark > 100 || mark < 0)
                Console.WriteLine("Out of range");
            """),

        Pattern(["analysis-always-true"], "A condition that is always true",
            "Following the values shows this condition is true every time it is reached, because an earlier test already settled it.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            var result = mark >= 50 ? "pass" : "fail";
            """),

        Pattern(["analysis-loop-never-runs"], "A loop that never runs",
            "The loop's condition is already false the first time it is checked, so the body is skipped entirely.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            int n = 10;
            while (n > 0)
                n--;
            """),

        Pattern(["analysis-loop-never-ends"], "A loop that never ends",
            "Nothing inside the loop changes what its condition tests, so once the condition is true it stays true for ever.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            for (int i = 0; i < 10; i++)
                Console.WriteLine(i);
            """),

        Pattern(["analysis-contract-broken"], "A call that breaks what the method checks for",
            "The method starts by checking its arguments and throwing an exception when they are wrong, and this call certainly gives it arguments it refuses.",
            "The method throws its exception as soon as the call runs.",
            "Give the method an argument it accepts, or check the value before calling it.",
            """
            if (n >= 0)
                Console.WriteLine(Half(n));
            """),

        Pattern(["analysis-used-after-close"], "Using a stream after it is disposed",
            "Every way to this line closes or disposes the stream first - often by leaving the using block that opened it.",
            "Using a disposed stream throws an ObjectDisposedException.",
            "Use the stream inside the using block, or open it again.",
            """
            using (var reader = new StreamReader(path))
            {
                var first = reader.ReadLine();
            }
            """),

        Pattern(["analysis-lock-not-released"], "A lock that is not always released",
            "The lock is taken with Monitor.Enter, and on at least one way out of the method it is not released.",
            "Every other thread that needs the lock waits for ever, so the program freezes.",
            "Use a lock statement, which always releases it.",
            """
            lock (gate)
            {
                count++;
            }
            """),
        Pattern(["analysis-lost-update"], "An update two threads can lose",
            "Several threads run this line at once, and += is three steps - read, add, write back - so two threads can read the same old value and one addition is lost.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Use Interlocked.Add or Interlocked.Increment, or a lock statement around the update.",
            """
            Parallel.For(0, values.Length, i => Interlocked.Add(ref total, values[i]));
            """),

        Pattern(["analysis-stale-read"], "A flag a thread may never see change",
            "A thread loops on this field while another method sets it. The field is not volatile and nothing in the loop synchronises, so the compiler may keep using the value it read first.",
            "The thread may never stop, even after the flag is set.",
            "Declare the field volatile, or use a CancellationToken.",
            """
            private volatile bool running = true;
            """),

        Pattern(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two places take the same two locks in opposite orders. If two threads each get their first lock, each waits for ever for the other's.",
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

        Pattern(["analysis-lock-cycle"], "Locks taken round a circle",
            "Each of these places takes a lock while holding another, and following them round comes back to the first lock. With a thread at each place, every thread holds the lock the next one needs, and all of them wait for ever.",
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

        Pattern(["analysis-wait-without-lock"], "Monitor.Wait or Pulse without its lock",
            "Monitor.Wait, Pulse and PulseAll must be called while holding the lock of the object they are given, and here that lock is not held.",
            "The call throws a SynchronizationLockException every time.",
            "Call it inside lock (that object).",
            """
            lock (gate)
            {
                while (!ready)
                    Monitor.Wait(gate);
            }
            """),

        Pattern(["analysis-wait-not-in-loop"], "Monitor.Wait not in a loop",
            "A waiting thread can wake up after another thread has already used what it waited for.",
            "The code after the wait can run while the condition it needs is still false.",
            "Wait in a while loop that checks the condition again each time it wakes.",
            """
            while (!ready)
                Monitor.Wait(gate);
            """),
    ];
}
