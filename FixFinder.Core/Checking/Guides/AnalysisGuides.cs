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

        AtEveryLevel(["analysis-type-mismatch"], "Combining values of the wrong types",
            "Some values just do not go together: you cannot add a number to a piece of text, or ask a number how long it is. FixFinder followed the values through the code and found that at this line they are certainly of kinds that cannot be combined this way.",
            "The values at this line are certainly of types that cannot be combined this way - text and a number, or a number used where something with a length or items is needed.",
            "The operands' abstract types admit no combination the operation supports - a str and an int for +, say, or an int given to len() - on every path reaching this line, so it raises TypeError.",
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

        AtEveryLevel(["analysis-empty-collection"], "Taking an item from something empty",
            "pop() takes the last item off a list, and there has to be an item there to take. On every way to this line the list has nothing in it, so there is nothing to take.",
            "The list is certainly empty when this line runs, so there is nothing to take.",
            "The collection's abstract length is exactly 0 on every path reaching this line, so taking an element from it fails - list.pop() raises IndexError.",
            "pop() on an empty list stops the program with an IndexError.",
            "Check that it has items first.",
            """
            if stack:
                top = stack.pop()
            """),

        AtEveryLevel(["analysis-not-a-number"], "Converting text that is not a number",
            "int() and float() turn text into a number, but only when the text looks like one - \"42\" works, \"forty-two\" does not. The text here can never look like a number, so the conversion always fails.",
            "The text given to int() or float() here can never be read as a number.",
            "The argument is known here to be text that no int or float literal matches, so int() or float() raises ValueError whenever the line runs.",
            "The conversion stops the program with a ValueError.",
            "Convert text that holds digits, and check input with isdigit() or try/except before converting it.",
            """
            value = int("42")
            """),

        AtEveryLevel(["analysis-never-true"], "A condition that can never be true",
            "An if runs its block only when its condition is true. FixFinder followed the values to this point and found that, however the program gets here, the condition is false - so the block underneath never runs.",
            "Following the values shows this condition is false every time it is reached - often two tests that contradict each other, or a check after the value has already been ruled out.",
            "Abstract interpretation gives the condition the value false on every path reaching this line - the variables' ranges exclude every value that would satisfy it - so the code it guards is dead.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if mark > 100 or mark < 0:
                print("Out of range")
            """),

        AtEveryLevel(["analysis-always-true"], "A condition that is always true",
            "A condition is meant to decide between two things. Here an earlier check has already made sure this one holds, so it is true every time and decides nothing.",
            "Following the values shows this condition is true every time it is reached, because an earlier test already settled it.",
            "On every path reaching this line the abstract state already implies the condition - usually because an earlier branch ruled out its opposite - so the test is redundant, and any else branch is dead.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            if mark >= 50:
                result = "pass"
            else:
                result = "fail"
            """),

        AtEveryLevel(["analysis-loop-never-runs"], "A loop that never runs",
            "A loop checks its condition before its first pass. Here the condition is already false the very first time, so the loop's body is skipped completely.",
            "The loop's condition is already false the first time it is checked, so the body is skipped entirely.",
            "The loop's condition is false in the abstract state on entry, before any iteration, so the body is unreachable.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            n = 10
            while n > 0:
                n -= 1
            """),

        AtEveryLevel(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "Nothing inside the loop changes what its condition tests, so once the condition is true it stays true for ever.",
            "No variable the condition reads is changed in the loop body, so the condition keeps its value on every pass: if it holds when the loop starts, the loop never ends.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            n = 5
            while n > 0:
                print(n)
                n -= 1
            """),

        AtEveryLevel(["analysis-assert-always-fails"], "An assert that always fails",
            "assert checks that something is true, and stops the program if it is not. FixFinder followed the values to this line and found the condition is false every time it is reached - so the program always stops here.",
            "The condition in this assert is false every time the line is reached.",
            "The asserted condition is false in the abstract state of every path reaching the assert, so it raises AssertionError whenever it runs - unless Python is started with -O, which removes asserts.",
            "The program stops with an AssertionError here every time.",
            "Check the condition; if it describes what should be true, the code before it is setting the value wrongly.",
            """
            n = 10
            assert n > 5
            """),

        AtEveryLevel(["analysis-contract-broken"], "A call that breaks what the function checks for",
            "Some functions start by checking what they have been given and refusing anything wrong by raising an error. This call gives the function something its own check refuses, so the error is certain.",
            "The function starts by checking its arguments and raising an error when they are wrong, and this call certainly gives it arguments it refuses.",
            "The callee's entry guard - a precondition check that raises - is violated by the argument values this call passes on every path, so the call raises the callee's exception.",
            "The function raises its error as soon as the call runs.",
            "Give the function an argument it accepts, or check the value before calling it.",
            """
            if x >= 0:
                print(root(x))
            """),

        AtEveryLevel(["analysis-wrong-arguments"], "A call with the wrong arguments",
            "A function's def line lists the values it needs. This call gives it a different set - one missing, one too many, or one with a name the function does not have - so Python cannot match them up.",
            "The call does not give the function the arguments its definition asks for: one is missing, there is one too many, or a name is wrong.",
            "Binding this call's positional and keyword arguments to the resolved function's parameters fails - a required parameter is left unbound, a positional argument has no parameter, or a keyword names none - so the call raises TypeError.",
            "Python stops with a TypeError when the call runs.",
            "Give exactly the arguments the definition lists, in order or by their names.",
            """
            def area(width, height):
                return width * height

            print(area(3, 4))
            """),

        AtEveryLevel(["analysis-type-hint-broken"], "A value that does not match its type hint",
            "A type hint is a note saying what kind of value a function takes or gives back, like score: int. Python does not enforce the note, but here the value can never be the kind the note says.",
            "A type hint says what a parameter or return value should be, and this value can never be that type.",
            "The value's inferred type is disjoint from the annotated one, so a static type checker such as mypy would reject it; the interpreter itself ignores annotations when it runs the code.",
            "Python does not stop at a wrong hint, but the code that trusts the hint - or a type checker - will be wrong about it.",
            "Return or pass a value of the hinted type, or change the hint to say what really happens.",
            """
            def label(score: int) -> str:
                if score > 50:
                    return "pass"
                return "fail"
            """),

        AtEveryLevel(["analysis-used-after-close"], "Using a file after it is closed",
            "A file has to be open to read from it or write to it. Leaving a with block closes the file automatically - and every way to this line has already closed it.",
            "Every way to this line closes the file first - often by leaving the with block that opened it.",
            "On every path reaching this line the file object is closed - typically because the with statement's __exit__ has run - so the operation raises ValueError: I/O operation on closed file.",
            "Reading or writing a closed file stops the program with a ValueError.",
            "Use the file inside the with block, or open it again.",
            """
            with open(path) as handle:
                first = handle.readline()
            """),

        AtEveryLevel(["analysis-lock-not-released"], "A lock that is not always released",
            "A lock lets only one thread at a time do something. Taking it with acquire() means giving it back with release(). On one way out of this function it is never given back, so every other thread that wants it waits for ever.",
            "The lock is taken with acquire(), and on at least one way out of the function it is not released.",
            "A path from the acquire() to a return reaches no release(), so the lock stays held; a with statement releases it on every way out, an exception included.",
            "Anything else that needs the lock waits for ever, so the program freezes.",
            "Use the lock in a with block, which always releases it.",
            """
            with lock:
                count += 1
            """),
        AtEveryLevel(["analysis-lost-update"], "An update two threads can lose",
            "An update like count += 1 looks like one step, but the computer does it in three: read the value, add, write it back. If two threads do it at the same moment, both can read the same old value, and one of the additions is lost.",
            "Several threads run this line at once, and += is three steps - read, add, write back - so two threads can read the same old value and one addition is lost.",
            "The read-modify-write of += is not atomic - it is several bytecode steps, and the GIL can switch threads between them - and no common lock orders the threads here, so concurrent updates can overwrite each other.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Hold a lock around the update, so only one thread does it at a time.",
            """
            with lock:
                counter += 1
            """),

        AtEveryLevel(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two threads each need the same two locks. One takes the first and then the second; the other takes them the other way round. If each gets its first lock at the same moment, each waits for the other's - for ever.",
            "Two places take the same two locks in opposite orders. If two threads each get their first lock, each waits for ever for the other's.",
            "The lock-order graph has an edge from one lock to the other at one place and back again at another, with no common lock guarding both, so two threads can each hold one and block on the other - a cycle of length two.",
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

        AtEveryLevel(["analysis-loop-can-get-stuck"], "A loop that can come back round with nothing changed",
            "A loop ends when its variables move far enough - lo and hi meeting, say. Here there is a situation where going round once leaves them exactly where they were, so the next time round is the same as the last, and so is every one after it. FixFinder made sure that situation can really happen: it fits everything the loop's own code always keeps true.",
            "In the state shown, one way through the loop changes none of the variables its condition tests, so the loop repeats that same state for ever. FixFinder checked the state is one the loop can reach: it keeps every relation the loop's own code keeps.",
            "A fixed point of the loop body's transition relation exists inside the loop's inductive invariants and its guard, so the loop does not terminate from that state.",
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

        AtEveryLevel(["analysis-code-injection"], "Running what the user typed as code",
            "eval() treats the text it is given as a piece of Python and runs it. Here the text is whatever the person running the program typed - so they can type any Python at all, and it runs as if it were part of your program.",
            "Text the person running the program controls reaches eval() or exec(), which run it as Python code - with every permission the program has.",
            "A taint flow from an input source reaches a code-evaluation sink with no sanitisation in between.",
            "Anything typed runs: it can read or delete files, or do anything else the program is allowed to.",
            "For a number use int() or float(); for a Python literal such as a list, ast.literal_eval().",
            """
            count = int(input("How many? "))
            """),

        AtEveryLevel(["analysis-command-injection"], "Running a shell command built from what the user typed",
            "The shell reads a command as text, and some characters in it - like ; or | - mean 'and now run another command'. Building the command from what someone typed lets them add a command of their own.",
            "The command is built from text the person running the program controls, and run through the shell - where a ; starts a second command of their choosing.",
            "A taint flow runs from an input source to a shell-command sink - os.system, os.popen, or subprocess with shell=True - with no sanitisation in between, so shell metacharacters in the input are acted on by the shell.",
            "Whoever runs the program can make it run any command at all.",
            "Pass the command and its arguments as a list to subprocess.run, without shell=True.",
            """
            subprocess.run(["ls", folder])
            """),

        AtEveryLevel(["analysis-sql-injection"], "SQL built from what the user typed",
            "A database reads the SQL it is given as instructions. Pasting what someone typed straight into those instructions means that if they type SQL, the database obeys it as if you had written it.",
            "The query is built by putting text the person running the program controls into the SQL itself, so what they type is read as SQL - SQL injection.",
            "A taint flow runs from an input source into the text of a query passed to execute(), so the input is parsed as SQL; with ? placeholders the statement stays fixed and the values go separately, as parameters that are never parsed as SQL.",
            "Typing ' OR '1'='1 can show every row, and worse can change or delete data.",
            "Keep the SQL fixed and pass the values separately, with ? placeholders.",
            """
            connection.execute("SELECT * FROM orders WHERE customer = ?", (name,))
            """),

        AtEveryLevel(["analysis-thread-started-twice"], "A thread started twice",
            "A Thread object is for one run of its work. Once start() has been called on it, it can never be started again - even after it has finished. Each new run needs a new Thread.",
            "A thread object runs its work once. After start() has been called on it, it can never be started again - not even after it has finished.",
            "threading.Thread.start() may be called at most once per object; a second call raises RuntimeError, 'threads can only be started once', whatever state the thread is in.",
            "The second start() stops the program with RuntimeError: threads can only be started once.",
            "Make a new Thread for each piece of work.",
            """
            for attempt in range(3):
                worker = threading.Thread(target=work)
                worker.start()
                worker.join()
            """),

        AtEveryLevel(["analysis-join-before-start"], "Waiting for a thread that was never started",
            "join() means 'wait here until that thread has finished'. This thread has not been started yet, so there is nothing to wait for, and Python stops with an error.",
            "join() waits for a thread to finish, but this thread has not been started yet, so there is nothing to wait for.",
            "Thread.join() on a thread whose start() has not run raises RuntimeError, 'cannot join thread before it is started'.",
            "The program stops with RuntimeError: cannot join thread before it is started.",
            "Call start() before join().",
            """
            worker.start()
            worker.join()
            """),

        AtEveryLevel(["analysis-finally-overrides"], "Leaving a finally block with return, break or continue",
            "The finally block always runs as the try block ends - even when it ended with an error. A return, break or continue in finally ends things right there, so an error the try raised is thrown away, and the try's own return value is replaced.",
            "A finally block runs however the try block ends. A return, break or continue in it takes over: it replaces the value the try block returned, and an error the try block raised is dropped as if it never happened.",
            "Leaving a finally clause with return, break or continue discards any exception in flight and overrides a pending return value from the try; PEP 765 makes Python 3.14 warn about it.",
            "Errors disappear without a trace, and a function returns something other than what its try block returned.",
            "Keep finally for cleaning up - closing, releasing - and return from the try block instead.",
            """
            try:
                return open(path).read()
            finally:
                print("done")
            """),

        AtEveryLevel(["analysis-unassigned-after-error"], "A variable that may have no value after an error",
            "The variable is only given its value inside the try block. If the line that gives it fails, the program jumps straight to the except block - and the variable never gets a value at all, so reading it afterwards fails too.",
            "The variable gets its value only inside the try block, so when the try fails before that line, it has none - and the code that reads it after the except block, or in the finally block, raises UnboundLocalError.",
            "The name is bound only on the try block's normal path; on the exceptional edge from before the binding it is unbound where it is read.",
            "The program stops with UnboundLocalError - often hiding the error that caused it.",
            "Give the variable a value before the try, or in the except block, or leave the function in the except block.",
            """
            try:
                number = int(text)
            except ValueError:
                number = 0
            """),

        AtEveryLevel(["analysis-resource-not-closed"], "A file that is never closed",
            "Opening a file is like borrowing it: it has to be given back by closing it. On this way out of the function nothing closes it, so it is left for Python to tidy up whenever it gets round to it - and until then the file stays open.",
            "The function opens a file and keeps it - nothing else is given it to close - but on this way out of the function it is never closed.",
            "The file object is owned by this function - it is not returned, stored or passed on - and some path to a return reaches no close(). It is then closed only when the object is garbage-collected: straight away under CPython's reference counting, at no set time under other implementations, and with a ResourceWarning when warnings are shown.",
            "Nothing closes the file on this way out, so it is left for Python to clean up: CPython closes it when the last reference goes, but other Pythons may keep it open, with what was written still unsaved, for as long as they like.",
            "Open it with with, which closes it however the function ends.",
            """
            with open(path) as handle:
                return handle.read()
            """),

        AtEveryLevel(["analysis-changed-while-looping"], "A collection changed while a loop walks over it",
            "A for loop walks through a list one position at a time. Taking an item out moves everything after it back one place, so the loop steps over the item that moved into the gap - and here the change is made under another name for the same list, or inside a function the loop calls, where it is easy to miss.",
            "The loop's collection is changed while the loop is still walking over it - through another name that holds the same collection, or in a function the loop calls. A list then skips or repeats items; a dictionary or set stops the loop with RuntimeError.",
            "A structural change to the iterated object - reached through an alias, or through a callee's effect summary - happens between two steps of its iterator.",
            "Items are missed without any error, or the loop stops with RuntimeError part-way through.",
            "Loop over a copy - for item in list(items): - or collect what to change and change it after the loop.",
            """
            for mark in list(marks):
                if mark < 50:
                    drop(marks, mark)
            """),

        AtEveryLevel(["analysis-read-before-join"], "Reading a result before the threads have finished",
            "Starting a thread is like asking someone to count a pile of coins while you get on with something else. Reading the total straight away gets whatever they have counted so far, not the final answer. join() is waiting for them to say they have finished.",
            "The thread was started, but this line runs before the join() that waits for it, so the thread may still be changing the value. The line reads whatever it holds at that moment - often not the final result.",
            "No happens-before edge orders the thread's writes before this read: only join() creates one, and the read comes before it.",
            "The program shows a value from part-way through - different on each run, and usually wrong.",
            "Read the result after join() has returned for every thread that changes it.",
            """
            worker.start()
            worker.join()
            print(total)
            """),

        AtEveryLevel(["analysis-data-race"], "Shared data used without the lock that guards it",
            "A lock is like a talking stick: only whoever holds it may change the shared value. Here one thread holds the stick while it changes the value, but another changes it without the stick - so the stick stops nothing. A lock only helps if every piece of code that uses the value takes the same one - even code that only reads it.",
            "This data is used with a lock in one place and without it - or with a different lock - in another, by threads that can run at the same time. A lock only protects data if everything that uses the data holds that same lock.",
            "The two accesses' locksets are disjoint and neither happens before the other, so they race.",
            "Changes can be lost and totals come out wrong - differently from one run to the next.",
            "Hold the same lock everywhere the data is used.",
            """
            with lock:
                total += 1
            """),

        AtEveryLevel(["analysis-run-not-start"], "run() called instead of start()",
            "start() tells a thread to go and do its work alongside the rest of the program. run() is the work itself - calling it directly just does the work right here, the ordinary way, with no new thread at all.",
            "Calling run() does the thread's work right here, on the thread that calls it, and waits for it to finish.",
            "Thread.run() is the method start() calls on the new thread; calling it directly runs the target on the calling thread, synchronously, so nothing runs concurrently.",
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
        AtEveryLevel(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "The divisor's abstract value includes 0 on at least one path reaching this line. Integer division or remainder by zero is a run-time panic in Go, and a constant zero divisor a compile error.",
            "Dividing a whole number by zero panics with `integer divide by zero`, often only for the inputs nobody tried, like an empty slice.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if count == 0 {
                return 0
            }
            return total / count
            """),

        AtEveryLevel(["analysis-null-used"], "Reading something through a nil pointer",
            "A pointer holds the address of a value, and nil means it holds no address at all. On one route through the code this pointer is still nil when the line tries to read a field through it.",
            "On at least one way through the code the pointer is nil when this line reads a field through it - for example a value only some branches set.",
            "The pointer's abstract value includes nil on at least one path reaching this line, so the dereference in the field selector panics at run time. A nil slice or map, by contrast, can be measured, ranged over and read.",
            "Reading a field through a nil pointer panics with `invalid memory address or nil pointer dereference`. A nil slice or map is fine to measure, walk or read from; a pointer is not.",
            "Check for nil first, or give the pointer a value on every way through the code.",
            """
            if node == nil {
                return 0
            }
            return node.value
            """),

        AtEveryLevel(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Positions in a slice start at 0, so a slice of three things has positions 0, 1 and 2. FixFinder knows how long this slice is here, and the position asked for is past its last one.",
            "The slice or array has a known length here, and the position asked for is past its end.",
            "The index is outside 0 to len - 1 for the slice's known length on this path, so the run-time bounds check panics.",
            "Positions run from 0 to len(x)-1, so this panics with `index out of range`.",
            "Use a position inside the slice, such as len(x)-1 for the last item.",
            """
            if len(points) > 0 {
                last := points[len(points)-1]
                fmt.Println(last)
            }
            """),

        AtEveryLevel(["analysis-lock-not-released"], "A lock that is not always released",
            "A mutex lets one goroutine at a time into a part of the code: Lock() to go in, Unlock() to come out. On one way out of this function Unlock() is never reached - often because of a return in between - so everything waiting for the mutex waits for ever.",
            "One way out of this function leaves the mutex locked - usually a return between Lock and Unlock.",
            "A path from Lock() to a return reaches no Unlock(), so the mutex stays locked; defer mu.Unlock() straight after Lock() runs on every return and while a panic unwinds.",
            "Everything else that needs the lock waits for ever.",
            "Release it with defer, which runs on every way out of the function.",
            """
            c.mu.Lock()
            defer c.mu.Unlock()
            c.count += n
            """),

        AtEveryLevel(["analysis-lost-update"], "An update two goroutines can lose",
            "count++ looks like one step, but the computer reads count, adds one and writes it back. If two goroutines do that at the same moment, both can read the same old value, and one of the additions disappears.",
            "Several goroutines run this line at once with nothing holding them apart, and it reads a value, changes it and writes it back.",
            "count++ is a read-modify-write that is not atomic, and no happens-before edge - a mutex, a channel, an atomic - orders the goroutines running it, so the updates race; go test -race reports it.",
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
        AtEveryLevel(["analysis-null-used"], "Using something that is null or undefined",
            "null and undefined both mean 'no value'. On one route through the code this value is still null or undefined when the line asks it for one of its parts - and a no-value has no parts. It happens with a variable declared without a value, a search that found nothing, or a field that is only sometimes set.",
            "On at least one way through the code the value is null or undefined when this line uses it - a variable declared with no value, a search that found nothing, or a field that is not always set.",
            "The value may be null or undefined on at least one path reaching this line, so the property access throws TypeError; optional chaining, ?., gives undefined instead.",
            "Reading a property of null or undefined stops the program with a TypeError.",
            "Check it first, or reach it with ?. so the whole chain gives undefined instead of failing.",
            """
            const found = people.find((p) => p.id === id);
            if (!found) {
              return '';
            }
            return found.name;
            """),

        AtEveryLevel(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "Nothing inside the loop changes what its condition reads, so once the loop starts it never stops.",
            "No variable the condition reads is changed in the loop body, so the condition keeps its value on every pass - and since the loop runs on JavaScript's single thread, no timer or event handler can run to change it either.",
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
        AtEveryLevel(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "The divisor's abstract value includes 0 on at least one path reaching this line; integer division or remainder by zero is undefined behaviour in C and C++, and on x86 it raises SIGFPE.",
            "Dividing a whole number by zero is undefined behaviour: on most machines it stops the program with a floating point exception.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0) {
                return 0;
            }
            return total / count;
            """),

        AtEveryLevel(["analysis-null-used"], "Going through a pointer that can be NULL",
            "A pointer holds the address of some memory, and NULL means it holds none. malloc gives back NULL when there is no memory to give, and a pointer set only on some branches may still be NULL. On one route through the code, this one is NULL here.",
            "On at least one way through the code the pointer is NULL when this line goes through it - malloc can come back with nothing, and a pointer is only set on some branches.",
            "The pointer may be NULL on at least one path reaching this dereference, and dereferencing a null pointer is undefined behaviour - usually a segmentation fault, though an optimiser may assume it cannot happen and drop later checks.",
            "Reading or writing through a null pointer is undefined behaviour: it usually stops the program with a segmentation fault.",
            "Check what you were given before using it.",
            """
            int *numbers = malloc(count * sizeof(int));
            if (numbers == NULL) {
                return 1;
            }
            numbers[0] = 1;
            """),

        AtEveryLevel(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "An array of three places has positions 0, 1 and 2. FixFinder knows this array's size here, and the position asked for is past the end - and C does not stop you: it just reads or writes whatever memory lies beyond.",
            "The array has a known size here, and the position asked for is past its end.",
            "The index lies outside 0 to size - 1 of an array whose size is known on this path; the access is undefined behaviour, and neither C nor C++ checks bounds.",
            "C does not check positions, so this reads or writes memory that belongs to something else - undefined behaviour, and often a crash or a security hole.",
            "Use a position inside the array, such as size - 1 for the last item.",
            """
            int marks[3] = {1, 2, 3};
            int last = marks[2];
            """),

        AtEveryLevel(["analysis-use-after-free"], "Memory used after it is freed",
            "free gives memory back so it can be reused. Afterwards the pointer still holds the old address, but the memory is not yours any more - and this line uses it anyway.",
            "This line goes through a pointer whose memory was already freed on the line the message names - often a linked list freed in a loop that then reads the next node.",
            "A path reaching this dereference has passed the pointer to free() with nothing assigned to it since, so the access touches memory whose lifetime has ended - undefined behaviour, and exploitable once the allocator has reused the block.",
            "The memory may already belong to something else, so this reads or writes whatever is there now: undefined behaviour, and a common security hole.",
            "Take what you need before freeing, and set the pointer to NULL afterwards.",
            """
            struct node *next = n->next;
            free(n);
            n = next;
            """),

        AtEveryLevel(["analysis-double-free"], "Memory freed twice",
            "Each block of memory from malloc has to be given back with free exactly once. This line frees memory that has already been freed.",
            "Every way to this line has already freed the same pointer.",
            "Every path to this free() has already freed the same pointer. A second free is undefined behaviour that can corrupt the allocator's lists - glibc often stops the program with 'double free detected' - and attackers can exploit it.",
            "Freeing memory twice corrupts what the allocator keeps about it: the program usually stops, and it can be exploited.",
            "Free once, and set the pointer to NULL so a second free does nothing.",
            """
            free(buffer);
            buffer = NULL;
            """),

        AtEveryLevel(["analysis-memory-leak"], "Memory nobody frees",
            "Memory from malloc stays yours until you give it back with free. This function gets some, then finishes without freeing it, returning it or storing it anywhere - so nothing can ever free it.",
            "This memory is asked for here, and the function returns without freeing it, handing it back or storing it anywhere.",
            "The allocation's pointer never escapes - it is not returned, stored or passed on - and a path reaches the function's exit without free(), so the block becomes unreachable and is not reclaimed before the process exits.",
            "The program keeps hold of memory it can never use again; in something long-running it grows until it stops.",
            "Free it before every way out of the function, or return it so the caller can.",
            """
            int *numbers = malloc(count * sizeof(int));
            /* ... */
            free(numbers);
            """),

        AtEveryLevel(["analysis-dangling-pointer"], "The address of something that is about to go",
            "A function's own variables only exist while it runs. Giving back the address of one of them is like giving someone the address of a room that is about to be knocked down: by the time they get there, something else is in its place.",
            "The address given back belongs to this function, and everything of the function's own is gone once it returns.",
            "The returned pointer designates an object with automatic storage duration in this function, whose lifetime ends at the return, so the caller receives a dangling pointer, and any use of it is undefined behaviour.",
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

        AtEveryLevel(["analysis-uninitialised-read"], "A value read before it is given one",
            "A variable declared inside a C function does not start at 0 - it starts with whatever was left in that memory. Nothing has been stored in this one on any way to this line, so its value is leftover junk.",
            "Nothing has been put in this variable on any way to this line - C does not clear what it hands you.",
            "On every path to this read the automatic variable is unassigned and its address has not been taken, so its value is indeterminate and reading it is undefined behaviour.",
            "The value is whatever happened to be in that memory, so the program does something different each time it runs.",
            "Give it a value where it is declared.",
            """
            int total = 0;
            printf("%d\n", total);
            """),
    ];

    public static IReadOnlyList<GuideEntry> Java { get; } =
    [
        AtEveryLevel(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "The divisor's abstract value includes 0 on at least one path reaching this line, and integer / and % by zero throw ArithmeticException; floating-point division would give Infinity or NaN instead.",
            "Dividing a whole number by zero stops the program with an ArithmeticException, often only for the inputs nobody tried, like an empty array.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0) {
                return 0;
            }
            return total / count;
            """),

        AtEveryLevel(["analysis-null-used"], "Using something that can be null",
            "A variable for an object holds a reference - an arrow to the object - and null means the arrow points at nothing. On one route through the code this one is still null when the line uses it.",
            "On at least one way through the code, the value is null when this line uses it - for example a variable set to null and only sometimes given a real value.",
            "The reference may be null on at least one path reaching this dereference, so the method call or field access throws NullPointerException.",
            "Calling a method on null or reading a field of it stops the program with a NullPointerException.",
            "Check for null before using it, or make sure every way through the code gives it a real value.",
            """
            String message = "none";
            if (score > 90) {
                message = "top";
            }
            return message.toUpperCase();
            """),

        AtEveryLevel(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Positions in an array start at 0, so an array of three things has positions 0, 1 and 2. FixFinder knows this array's length here, and the position asked for is past its last one.",
            "The array or text has a known length here, and the position asked for is past its end.",
            "The index lies outside 0 to length - 1 for the array's known length on this path, so the bounds check throws ArrayIndexOutOfBoundsException - StringIndexOutOfBoundsException for a String.",
            "Positions run from 0 to length - 1, so this stops the program with an ArrayIndexOutOfBoundsException.",
            "Use a position inside the array, such as length - 1 for the last item.",
            """
            int[] points = {3, 5, 8};
            int last = points[points.length - 1];
            """),

        AtEveryLevel(["analysis-empty-collection"], "Taking an item from something empty",
            "pop() and remove() take an item out of a stack or a queue, and there has to be an item there to take. On every way to this line the collection has nothing in it.",
            "The collection is certainly empty when this line runs, so there is nothing to take.",
            "The collection's abstract size is exactly 0 on every path reaching this line, so the removal fails: Stack.pop throws EmptyStackException, and a Deque's pop, removeFirst and element throw NoSuchElementException.",
            "Taking an item from an empty stack or queue stops the program with an exception.",
            "Check that it has items first.",
            """
            if (!stack.isEmpty()) {
                int top = stack.pop();
            }
            """),

        AtEveryLevel(["analysis-not-a-number"], "Converting text that is not a number",
            "Integer.parseInt turns text into a number, but only when the text is written as a number - \"42\" works, \"forty-two\" does not. The text here can never be read as a number, so the conversion always fails.",
            "The text given to Integer.parseInt or Double.parseDouble here can never be read as a number.",
            "The argument is known here to be text the parse method rejects - for Integer.parseInt, anything but an optional sign and digits within int's range - so it throws NumberFormatException on every path.",
            "The conversion stops the program with a NumberFormatException.",
            "Convert text that holds digits, and catch NumberFormatException around text that comes from outside the program.",
            """
            int value = Integer.parseInt("42");
            """),

        AtEveryLevel(["analysis-never-true"], "A condition that can never be true",
            "An if runs its block only when its condition is true. FixFinder followed the values to this point and found that, however the program gets here, the condition is false - so the block underneath never runs.",
            "Following the values shows this condition is false every time it is reached - often two tests that contradict each other, or a check after the value has already been ruled out.",
            "Abstract interpretation gives the condition the value false on every path reaching this line - the variables' ranges exclude every value that would satisfy it - so the code it guards is dead.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if (mark > 100 || mark < 0) {
                System.out.println("Out of range");
            }
            """),

        AtEveryLevel(["analysis-always-true"], "A condition that is always true",
            "A condition is meant to decide between two things. Here an earlier check has already made sure this one holds, so it is true every time and decides nothing.",
            "Following the values shows this condition is true every time it is reached, because an earlier test already settled it.",
            "On every path reaching this line the abstract state already implies the condition - usually because an earlier branch ruled out its opposite - so the test is redundant, and any else branch is dead.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            if (mark >= 50) {
                result = "pass";
            } else {
                result = "fail";
            }
            """),

        AtEveryLevel(["analysis-loop-never-runs"], "A loop that never runs",
            "A loop checks its condition before its first pass. Here the condition is already false the very first time, so the loop's body is skipped completely.",
            "The loop's condition is already false the first time it is checked, so the body is skipped entirely.",
            "The loop's condition is false in the abstract state on entry, before any iteration, so the body is unreachable.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            int n = 10;
            while (n > 0) {
                n--;
            }
            """),

        AtEveryLevel(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "Nothing inside the loop changes what its condition tests, so once the condition is true it stays true for ever.",
            "No variable the condition reads is changed in the loop body, so the condition keeps its value on every pass: if it holds when the loop starts, the loop never ends.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            for (int i = 0; i < 10; i++) {
                System.out.println(i);
            }
            """),

        AtEveryLevel(["analysis-assert-always-fails"], "An assert that always fails",
            "assert checks that something is true, and stops the program if it is not. The condition here is false every time the line is reached - though Java only checks asserts when it is run with -ea.",
            "The condition in this assert is false every time the line is reached.",
            "The asserted condition is false in the abstract state of every path reaching it. Java evaluates assertions only when they are enabled, with -ea, and then throws AssertionError.",
            "With assertions turned on (java -ea) the program stops with an AssertionError here every time.",
            "Check the condition; if it describes what should be true, the code before it is setting the value wrongly.",
            """
            int n = 10;
            assert n > 5;
            """),

        AtEveryLevel(["analysis-contract-broken"], "A call that breaks what the method checks for",
            "Some methods start by checking what they have been given and throwing an exception if it is wrong. This call gives the method something its own check refuses, so the exception is certain.",
            "The method starts by checking its arguments and throwing an exception when they are wrong, and this call certainly gives it arguments it refuses.",
            "The callee's entry guard - a check that throws, such as IllegalArgumentException - is violated by the argument values this call passes on every path, so the call throws.",
            "The method throws its exception as soon as the call runs.",
            "Give the method an argument it accepts, or check the value before calling it.",
            """
            if (n >= 0) {
                System.out.println(half(n));
            }
            """),

        AtEveryLevel(["analysis-used-after-close"], "Using a stream after it is closed",
            "A stream has to be open to read from it or write to it. Every way to this line has already closed it - try-with-resources closes it at the end of its block.",
            "Every way to this line closes the stream first.",
            "On every path to this operation the stream has been closed - by close(), or at the end of a try-with-resources - and the java.io readers and writers then throw IOException, 'Stream closed'.",
            "Reading or writing a closed stream throws an IOException.",
            "Finish using the stream before closing it - try-with-resources closes it at the right time.",
            """
            try (BufferedReader reader = new BufferedReader(new FileReader(path))) {
                String first = reader.readLine();
            }
            """),

        AtEveryLevel(["analysis-lock-not-released"], "A lock that is not always released",
            "A lock lets one thread at a time into a part of the code: lock() to go in, unlock() to come out. On one way out of this method - a return, or an exception - unlock() is never reached, so every other thread waiting for the lock waits for ever.",
            "The lock is taken with lock(), and on at least one way out of the method - a return, or an exception - it is not unlocked.",
            "A path from lock() to a method exit, normal or exceptional, reaches no unlock(). Unlike synchronized, an explicit Lock is never released automatically, so the idiom is lock() followed at once by try { ... } finally { unlock(); }.",
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
        AtEveryLevel(["analysis-lost-update"], "An update two threads can lose",
            "An update like count += 1 looks like one step, but the computer does it in three: read the value, add, write it back. If two threads do it at the same moment, both can read the same old value, and one of the additions is lost.",
            "Several threads run this line at once on the same field, and ++ is three steps - read, add, write back - so two threads can read the same old value and one increment is lost.",
            "++ on a shared field is a read-modify-write that is not atomic, and no lock or happens-before edge orders the threads running it, so increments can be lost; volatile alone does not help - synchronized or AtomicInteger does.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Make the update synchronized, or use an AtomicInteger.",
            """
            private final AtomicInteger count = new AtomicInteger();

            public void run() {
                count.incrementAndGet();
            }
            """),

        AtEveryLevel(["analysis-stale-read"], "A flag a thread may never see change",
            "To run fast, each thread may keep its own copy of a value instead of looking it up every time. Without volatile, a thread looping on this flag may keep using the copy it read at the start - and never see another thread change it.",
            "A thread loops on this field while another method sets it. The field is not volatile and nothing in the loop synchronises, so Java may keep using the value it read first.",
            "Without volatile or synchronization there is no happens-before edge from the writer's store to the reader's loads, so the Java memory model lets the reader never see it, and the JIT compiler may lift the read out of the loop. A volatile write is visible to every later volatile read.",
            "The thread may never stop, even after the flag is set.",
            "Declare the field volatile, so every thread sees each write.",
            """
            private volatile boolean running = true;
            """),

        AtEveryLevel(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two threads each need the same two locks. One takes the first and then the second; the other takes them the other way round. If each gets its first lock at the same moment, each waits for the other's - for ever.",
            "Two places take the same two locks in opposite orders. If two threads each get their first lock, each waits for ever for the other's.",
            "The lock-order graph has an edge from one lock to the other at one place and back again at another, with no common lock guarding both, so two threads can each hold one and block on the other - a cycle of length two.",
            "The program freezes - a deadlock - and only sometimes, when the timing lines up.",
            "Always take the locks in the same order everywhere.",
            """
            synchronized (first) {
                synchronized (second) {
                    move(money);
                }
            }
            """),

        AtEveryLevel(["analysis-lock-cycle"], "Locks taken round a circle",
            "Picture people round a table, each holding one fork and waiting for the fork of the person beside them. Nobody lets go, so nobody ever eats. These places take locks in an order that goes all the way round like that: each one holds a lock the next one is waiting for.",
            "Each of these places takes a lock while holding another, and following them round comes back to the first lock. With a thread at each place, every thread holds the lock the next one needs, and all of them wait for ever.",
            "The lock-order graph has a cycle whose edges no common lock guards, so with one thread per edge each can hold its monitor and block on the next - a deadlock the JVM does not break, though jstack reports it.",
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

        AtEveryLevel(["analysis-loop-can-get-stuck"], "A loop that can come back round with nothing changed",
            "A loop ends when its variables move far enough - lo and hi meeting, say. There is a situation where going round once leaves them exactly where they were, so the next pass is the same as the last, and so is every one after it. FixFinder made sure that situation can really happen: it fits everything the loop's own code always keeps true.",
            "In the state shown, one way through the loop changes none of the variables its condition tests, so the loop repeats that same state for ever. FixFinder checked the state is one the loop can reach: it keeps every relation the loop's own code keeps.",
            "A fixed point of the loop body's transition relation exists inside the loop's inductive invariants and its guard, so the loop does not terminate from that state.",
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

        AtEveryLevel(["analysis-command-injection"], "Running a command built from outside text",
            "A command is a program's name followed by its arguments. Building it as one piece of text from what someone typed lets them slip in arguments, or a different program, of their own.",
            "The command line is built from text the person running the program controls - what they typed, or the program's arguments - so that text can add arguments of its own choosing.",
            "A taint flow runs from an input source - the program's arguments or what was typed - to Runtime.exec(String), which splits the string on whitespace into the program and its arguments, so the input chooses them; the String[] form and ProcessBuilder keep each argument separate.",
            "Whoever runs the program can change what the command does.",
            "Pass the command and each argument separately, as a String[] or to a ProcessBuilder.",
            """
            new ProcessBuilder("ls", folder).start();
            """),

        AtEveryLevel(["analysis-sql-injection"], "SQL built from outside text",
            "A database reads the SQL it is given as instructions. Joining what someone typed into those instructions means that if they type SQL, the database obeys it as if you had written it.",
            "The query is built by joining text the person running the program controls into the SQL itself, so that text is read as SQL - SQL injection.",
            "A taint flow reaches the text of a query given to executeQuery, executeUpdate, execute or prepareStatement. A PreparedStatement with ? placeholders keeps the statement fixed, and setString sends each value as a parameter that is never parsed as SQL.",
            "Typing ' OR '1'='1 can show every row, and worse can change or delete data.",
            "Use a PreparedStatement with ? placeholders, and set each value with setString.",
            """
            PreparedStatement query = connection.prepareStatement("SELECT * FROM orders WHERE customer = ?");
            query.setString(1, customer);
            """),

        AtEveryLevel(["analysis-thread-started-twice"], "A thread started twice",
            "A Thread object is for one run of its work. Once start() has been called on it, it can never be started again - even after it has finished.",
            "A Thread object runs its work once. After start() has been called on it, it can never be started again - not even after it has finished.",
            "Thread.start() may be called only once per Thread; any later call throws IllegalThreadStateException, whatever state the thread is in.",
            "The second start() throws an IllegalThreadStateException.",
            "Make a new Thread for each piece of work.",
            """
            for (int i = 0; i < 3; i++) {
                Thread worker = new Thread(task);
                worker.start();
                worker.join();
            }
            """),

        AtEveryLevel(["analysis-join-before-start"], "Waiting for a thread that was never started",
            "join() means 'wait here until that thread has finished'. This thread has not been started, so Java does not wait at all - join() returns straight away, and the code after it runs as if the work were done.",
            "join() waits for a thread to finish, but this thread has not been started yet, so join() returns at once without waiting for anything.",
            "join() waits only while isAlive() is true, and a thread that has not been started is not alive, so the call returns at once and gives no happens-before edge for work that has not yet run.",
            "The code after join() runs as if the thread's work were done, when it has not even begun.",
            "Call start() before join().",
            """
            worker.start();
            worker.join();
            """),

        AtEveryLevel(["analysis-finally-overrides"], "Leaving a finally block with return, break or continue",
            "The finally block always runs as the try block ends - even when it ended with an exception. A return, break or continue in finally ends things right there, so an exception the try threw is thrown away, and the try's own return value is replaced.",
            "A finally block runs however the try block ends. A return, break or continue in it takes over: it replaces the value the try block returned, and an exception the try block threw is dropped as if it never happened.",
            "When a finally block completes abruptly with return, break or continue, the whole try statement completes that way and any pending exception or return value is discarded; javac's -Xlint:finally warns about it.",
            "Exceptions disappear without a trace, and a method returns something other than what its try block returned.",
            "Keep finally for cleaning up - closing, releasing - and return from the try block instead.",
            """
            try {
                return Integer.parseInt(text);
            } finally {
                System.out.println("done");
            }
            """),

        AtEveryLevel(["analysis-resource-not-closed"], "A file or stream that is never closed",
            "Opening a file or a stream is like borrowing it: it has to be given back by closing it. On one way out of this method it is never closed - and a writer that is never closed may never save what it was given.",
            "The method opens a file or stream and keeps it - it is not returned, stored or wrapped in another stream - but on this way out of the method it is never closed.",
            "The stream is owned by this method - not returned, stored or wrapped by another stream - and some path to an exit reaches no close(). A buffered writer such as FileWriter writes its buffer out only when flushed or closed, and nothing does that when it is garbage-collected, so the text is lost; try-with-resources closes it on every exit.",
            "A writer that is never closed may never write out what it holds, so the file is left empty or cut short; any stream left open holds on to the file.",
            "Open it in a try-with-resources, which closes it however the method ends.",
            """
            try (FileWriter writer = new FileWriter("report.txt")) {
                writer.write("total: " + total);
            }
            """),

        AtEveryLevel(["analysis-changed-while-looping"], "A collection changed while a loop walks over it",
            "A for-each loop walks through a collection with a hidden helper that remembers where it is. Here the collection is changed while the loop is going - under another name for the same collection, or in a method the loop calls - so Java stops the loop.",
            "The loop's collection is changed while the for-each loop is still walking over it - through another name that holds the same collection, or in a method the loop calls.",
            "A structural change reaches the iterated collection between two steps of its fail-fast iterator - through an alias, or through a called method's effect summary - so the next next() throws ConcurrentModificationException.",
            "The loop can stop with a ConcurrentModificationException, often only for some data.",
            "Loop over a copy, or remove through the iterator with it.remove(), or collect what to remove and call removeAll after the loop.",
            """
            for (String item : new ArrayList<>(items)) {
                discard(item);
            }
            """),

        AtEveryLevel(["analysis-read-before-join"], "Reading a result before the threads have finished",
            "Starting work on another thread is like asking someone to count a pile of coins while you get on with something else. Reading the total straight away gets whatever they have counted so far, not the final answer; waiting for them to finish is what join does.",
            "The thread was started, but this line runs before the join() that waits for it, so the thread may still be changing the value. The line reads whatever it holds at that moment - often not the final result.",
            "No happens-before edge orders the thread's writes before this read - Thread.join() would create one, and the read comes before it - so the Java memory model allows it to see any value written so far, or the initial one.",
            "The program shows a value from part-way through - different on each run, and usually wrong.",
            "Read the result after join() has returned for every thread that changes it.",
            """
            worker.start();
            worker.join();
            System.out.println(total);
            """),

        AtEveryLevel(["analysis-data-race"], "Shared data used without the lock that guards it",
            "A lock is like a talking stick: only whoever holds it may use the shared value. Here one place holds the stick while it uses the value, but another uses it without the stick - so the stick protects nothing. A lock only helps if every piece of code that uses the value takes the same one - even code that only reads it.",
            "This data is used with a lock in one place and without it - or with a different lock - in another, by threads that can run at the same time. A lock only protects data if everything that uses the data holds that same lock.",
            "The two accesses' locksets are disjoint and neither happens before the other, so they form a data race under the Java memory model: updates can be lost and reads can see out-of-date values.",
            "Changes can be lost, and a thread can read an out-of-date value - differently from one run to the next.",
            "Hold the same lock everywhere the data is used - for example, make the method that reads it synchronized too.",
            """
            public synchronized int getBalance() {
                return balance;
            }
            """),

        AtEveryLevel(["analysis-wait-without-lock"], "wait or notify without its lock",
            "wait() and notify() let threads signal each other about an object, and they only work while the thread holds that object's lock - inside synchronized on that object. Here the lock is not held.",
            "wait(), notify() and notifyAll() must be called while holding the lock of the object they are called on, and here that lock is not held.",
            "Object.wait, notify and notifyAll require the calling thread to own the object's monitor, and throw IllegalMonitorStateException when it does not.",
            "The call throws an IllegalMonitorStateException every time.",
            "Call it inside synchronized (that object), or from a synchronized method of it.",
            """
            synchronized void take() throws InterruptedException {
                while (!full) {
                    wait();
                }
            }
            """),

        AtEveryLevel(["analysis-wait-not-in-loop"], "wait() not in a loop",
            "wait() puts a thread to sleep until it is told something has changed. But it can wake up without being told, or find that another thread has already used what it was waiting for. Checking again in a loop makes sure the condition really holds.",
            "A waiting thread can wake up without being notified, or after another thread has already used what it waited for.",
            "Object.wait allows spurious wakeups, and between the notify and getting the monitor back another thread may change the state, so the Object.wait documentation requires waiting in a loop that tests the condition again.",
            "The code after wait() can run while the condition it needs is still false.",
            "Wait in a while loop that checks the condition again each time it wakes.",
            """
            while (!full) {
                wait();
            }
            """),

        AtEveryLevel(["analysis-run-not-start"], "run() called instead of start()",
            "start() tells a thread to go and do its work alongside the rest of the program. run() is the work itself - calling it directly just does the work right here, the ordinary way, with no new thread at all.",
            "Calling run() does the thread's work right here, on the thread that calls it, and waits for it to finish.",
            "Thread.run() is what the new thread executes after start(); calling it directly runs the Runnable synchronously on the calling thread, so nothing runs concurrently.",
            "Nothing runs at the same time, so the program is slower than it should be and never actually uses the thread.",
            "Call start(), which runs the work on the new thread.",
            """
            Thread worker = new Thread(task);
            worker.start();
            """),
    ];

    public static IReadOnlyList<GuideEntry> CSharp { get; } =
    [
        AtEveryLevel(["analysis-division-by-zero"], "Dividing by something that can be zero",
            "Dividing splits something into equal parts, and there is no way to split something into zero parts. FixFinder followed the values along one route through the code and found the whole number being divided by can be 0 when this line runs.",
            "Following the values through the code shows the whole number being divided by is, or can be, 0 at this line - for example a count that stays 0 when a loop never runs.",
            "The divisor's abstract value includes 0 on at least one path reaching this line; integer and decimal division by zero throw DivideByZeroException, while double division would give infinity or NaN.",
            "Dividing a whole number by zero stops the program with a DivideByZeroException, often only for the inputs nobody tried, like an empty list.",
            "Check the divisor first and decide what the answer should be when it is 0.",
            """
            if (count == 0)
                return 0;
            return total / count;
            """),

        AtEveryLevel(["analysis-null-used"], "Using something that can be null",
            "A variable for an object holds a reference - an arrow to the object - and null means it points at nothing. On one route through the code this one is still null when the line uses it.",
            "On at least one way through the code, the value is null when this line uses it - for example a variable set to null and only sometimes given a real value, or a ?. that gave null.",
            "The reference may be null on at least one path reaching this member access, so it throws NullReferenceException; ?. and ?? deal with null explicitly.",
            "Calling a method on null or reading a property of it stops the program with a NullReferenceException.",
            "Check for null before using it, give a default with ??, or make sure every way through the code gives it a real value.",
            """
            string? message = null;
            if (score > 90) message = "top";
            return (message ?? "none").ToUpper();
            """),

        AtEveryLevel(["analysis-index-out-of-range"], "Asking for a position that does not exist",
            "Positions start at 0, so a collection of three things has positions 0, 1 and 2. FixFinder knows this one's length here, and the position asked for is past its last one.",
            "The array, list or text has a known length here, and the position asked for is past its end.",
            "The index lies outside 0 to Length - 1, or Count - 1, for the collection's known size on this path. Array and string indexers throw IndexOutOfRangeException and List<T>'s throws ArgumentOutOfRangeException; ^1 indexes the last element.",
            "Positions run from 0 to Length - 1, so this stops the program with an IndexOutOfRangeException (ArgumentOutOfRangeException for a List).",
            "Use a position inside the collection, such as ^1 for the last item.",
            """
            int[] points = { 3, 5, 8 };
            int last = points[^1];
            """),

        AtEveryLevel(["analysis-empty-collection"], "Taking an item from something empty",
            "Pop, Dequeue and First take an item out, and there has to be one there to take. On every way to this line the collection is empty.",
            "The collection is certainly empty when this line runs, so there is nothing to take.",
            "The collection's abstract count is exactly 0 on every path reaching this line, so Stack<T>.Pop and Peek, Queue<T>.Dequeue and Peek, and Enumerable.First and Last throw InvalidOperationException; the Try and OrDefault forms report it instead.",
            "Pop, Dequeue, Peek, First and Last on an empty collection stop the program with an InvalidOperationException.",
            "Check Count first, or use TryPop, TryDequeue or FirstOrDefault.",
            """
            if (stack.TryPop(out var top))
                Console.WriteLine(top);
            """),

        AtEveryLevel(["analysis-not-a-number"], "Converting text that is not a number",
            "int.Parse turns text into a number, but only when the text is written as a number. The text here can never be read as one, so the conversion always fails.",
            "The text given to int.Parse or double.Parse here can never be read as a number.",
            "The argument is known here to be text the parse method's number style rejects - for int.Parse, anything but surrounding whitespace, an optional sign and digits - so it throws FormatException on every path.",
            "The conversion stops the program with a FormatException.",
            "Convert text that holds digits, and use int.TryParse for text that comes from outside the program.",
            """
            if (int.TryParse(text, out var value))
                Console.WriteLine(value);
            """),

        AtEveryLevel(["analysis-never-true"], "A condition that can never be true",
            "An if runs its block only when its condition is true. FixFinder followed the values to this point and found that, however the program gets here, the condition is false - so the block underneath never runs.",
            "Following the values shows this condition is false every time it is reached - often two tests that contradict each other, or a check after the value has already been ruled out.",
            "Abstract interpretation gives the condition the value false on every path reaching this line - the variables' ranges exclude every value that would satisfy it - so the code it guards is dead.",
            "The code it guards never runs, so whatever it was meant to handle is not handled.",
            "Check the comparison and which variable it tests; one of the two tests is usually the wrong way round.",
            """
            if (mark > 100 || mark < 0)
                Console.WriteLine("Out of range");
            """),

        AtEveryLevel(["analysis-always-true"], "A condition that is always true",
            "A condition is meant to decide between two things. Here an earlier check has already made sure this one holds, so it is true every time and decides nothing.",
            "Following the values shows this condition is true every time it is reached, because an earlier test already settled it.",
            "On every path reaching this line the abstract state already implies the condition - usually because an earlier branch ruled out its opposite - so the test is redundant, and any else branch is dead.",
            "The check does nothing. It is harmless, but it usually means the logic is not quite what was intended.",
            "Use else instead of a test that can only be true, or remove the check.",
            """
            var result = mark >= 50 ? "pass" : "fail";
            """),

        AtEveryLevel(["analysis-loop-never-runs"], "A loop that never runs",
            "A loop checks its condition before its first pass. Here the condition is already false the very first time, so the loop's body is skipped completely.",
            "The loop's condition is already false the first time it is checked, so the body is skipped entirely.",
            "The loop's condition is false in the abstract state on entry, before any iteration, so the body is unreachable.",
            "Whatever the loop was meant to do - count, total, search - never happens.",
            "Check the starting value and the comparison: often it should be < rather than >, or the variable should start somewhere else.",
            """
            int n = 10;
            while (n > 0)
                n--;
            """),

        AtEveryLevel(["analysis-loop-never-ends"], "A loop that never ends",
            "A loop keeps going until its condition becomes false. Nothing inside this loop changes anything the condition checks, so if the condition is true once, it stays true for ever.",
            "Nothing inside the loop changes what its condition tests, so once the condition is true it stays true for ever.",
            "No variable the condition reads is changed in the loop body, so the condition keeps its value on every pass: if it holds when the loop starts, the loop never ends.",
            "The program stops at this loop and never gets any further: it looks frozen until it is closed.",
            "Change what the condition tests inside the loop - count it down, read the next input into it - or leave the loop with break.",
            """
            for (int i = 0; i < 10; i++)
                Console.WriteLine(i);
            """),

        AtEveryLevel(["analysis-contract-broken"], "A call that breaks what the method checks for",
            "Some methods start by checking what they have been given and throwing an exception if it is wrong. This call gives the method something its own check refuses, so the exception is certain.",
            "The method starts by checking its arguments and throwing an exception when they are wrong, and this call certainly gives it arguments it refuses.",
            "The callee's entry guard - a check that throws, such as ArgumentException or ArgumentOutOfRangeException - is violated by the argument values this call passes on every path, so the call throws.",
            "The method throws its exception as soon as the call runs.",
            "Give the method an argument it accepts, or check the value before calling it.",
            """
            if (n >= 0)
                Console.WriteLine(Half(n));
            """),

        AtEveryLevel(["analysis-used-after-close"], "Using a stream after it is disposed",
            "A stream has to be open to use it. Leaving a using block disposes of the stream - closes it - and every way to this line has already done that.",
            "Every way to this line closes or disposes the stream first - often by leaving the using block that opened it.",
            "On every path to this operation the stream has been disposed - often at the end of a using block - and Stream, TextReader and TextWriter members then throw ObjectDisposedException.",
            "Using a disposed stream throws an ObjectDisposedException.",
            "Use the stream inside the using block, or open it again.",
            """
            using (var reader = new StreamReader(path))
            {
                var first = reader.ReadLine();
            }
            """),

        AtEveryLevel(["analysis-lock-not-released"], "A lock that is not always released",
            "A lock lets one thread at a time into a part of the code. Monitor.Enter takes it and Monitor.Exit gives it back; on one way out of this method Exit is never reached, so every other thread that wants the lock waits for ever.",
            "The lock is taken with Monitor.Enter, and on at least one way out of the method it is not released.",
            "A path from Monitor.Enter to a method exit, normal or exceptional, reaches no Monitor.Exit. The lock statement expands to Enter with Exit in a finally block, so it releases the lock on every exit.",
            "Every other thread that needs the lock waits for ever, so the program freezes.",
            "Use a lock statement, which always releases it.",
            """
            lock (gate)
            {
                count++;
            }
            """),
        AtEveryLevel(["analysis-lost-update"], "An update two threads can lose",
            "An update like count += 1 looks like one step, but the computer does it in three: read the value, add, write it back. If two threads do it at the same moment, both can read the same old value, and one of the additions is lost.",
            "Several threads run this line at once, and += is three steps - read, add, write back - so two threads can read the same old value and one addition is lost.",
            "+= on a shared field is a read-modify-write that is not atomic, and no lock orders the threads running it, so updates can be lost; Interlocked.Add does the whole update as one atomic operation.",
            "The total comes out wrong - smaller than it should be - and differently each time, so the mistake is hard to reproduce.",
            "Use Interlocked.Add or Interlocked.Increment, or a lock statement around the update.",
            """
            Parallel.For(0, values.Length, i => Interlocked.Add(ref total, values[i]));
            """),

        AtEveryLevel(["analysis-stale-read"], "A flag a thread may never see change",
            "To run fast, a thread may keep its own copy of a value instead of looking it up each time. Without volatile, a thread looping on this flag may keep using the copy it read first - and never see another thread change it.",
            "A thread loops on this field while another method sets it. The field is not volatile and nothing in the loop synchronises, so the compiler may keep using the value it read first.",
            "Without volatile, a lock or a memory barrier, the JIT compiler may lift the field read out of the loop, so the loop never sees another thread's write; a volatile read has acquire semantics and is made afresh on every pass.",
            "The thread may never stop, even after the flag is set.",
            "Declare the field volatile, or use a CancellationToken.",
            """
            private volatile bool running = true;
            """),

        AtEveryLevel(["analysis-lock-order"], "Locks taken in opposite orders",
            "Two threads each need the same two locks. One takes the first and then the second; the other takes them the other way round. If each gets its first lock at the same moment, each waits for the other's - for ever.",
            "Two places take the same two locks in opposite orders. If two threads each get their first lock, each waits for ever for the other's.",
            "The lock-order graph has an edge from one lock to the other at one place and back again at another, with no common lock guarding both, so two threads can each hold one and block on the other - a cycle of length two.",
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

        AtEveryLevel(["analysis-lock-cycle"], "Locks taken round a circle",
            "Picture people round a table, each holding one fork and waiting for the fork of the person beside them. Nobody lets go, so nobody ever eats. These places take locks in an order that goes all the way round like that: each one holds a lock the next one is waiting for.",
            "Each of these places takes a lock while holding another, and following them round comes back to the first lock. With a thread at each place, every thread holds the lock the next one needs, and all of them wait for ever.",
            "The lock-order graph has a cycle whose edges no common lock guards, so with one thread per edge each can hold its lock and block on the next; Monitor does nothing to detect or break the deadlock.",
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

        AtEveryLevel(["analysis-loop-can-get-stuck"], "A loop that can come back round with nothing changed",
            "A loop ends when its variables move far enough - lo and hi meeting, say. There is a situation where going round once leaves them exactly where they were, so the next pass is the same as the last, and so is every one after it. FixFinder made sure that situation can really happen: it fits everything the loop's own code always keeps true.",
            "In the state shown, one way through the loop changes none of the variables its condition tests, so the loop repeats that same state for ever. FixFinder checked the state is one the loop can reach: it keeps every relation the loop's own code keeps.",
            "A fixed point of the loop body's transition relation exists inside the loop's inductive invariants and its guard, so the loop does not terminate from that state.",
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

        AtEveryLevel(["analysis-command-injection"], "Starting a program named by outside text",
            "Process.Start runs another program, chosen by the text it is given. Here that text comes from whoever runs the program, so they get to choose which program runs.",
            "Process.Start is given text the person running the program controls - what they typed, or the program's arguments - so that text chooses what runs.",
            "A taint flow runs from an input source to Process.Start's file name, so the input selects the executable - and, with UseShellExecute, which is the default on .NET Framework though not on .NET Core, a document or URL handler too.",
            "Whoever runs the program can make it start a program of their choosing.",
            "Check the text against the commands the program means to run before starting one.",
            """
            if (allowed.Contains(tool)) Process.Start(tool);
            """),

        AtEveryLevel(["analysis-sql-injection"], "SQL built from outside text",
            "A database reads SQL as instructions. Building the SQL from what someone typed means that if they type SQL, the database obeys it as if you had written it.",
            "The command's text is built from text the person running the program controls, so that text is read as SQL - SQL injection.",
            "A taint flow reaches the text of a SQL command, so the input is parsed as SQL; a parameter such as @name, given its value through Parameters, keeps the command text fixed and sends the value without it ever being parsed as SQL.",
            "Typing ' OR '1'='1 can show every row, and worse can change or delete data.",
            "Keep the SQL fixed and pass the values as parameters.",
            """
            var command = new SqlCommand("SELECT * FROM Orders WHERE Customer = @name", connection);
            command.Parameters.AddWithValue("@name", name);
            """),

        AtEveryLevel(["analysis-thread-started-twice"], "A thread started twice",
            "A Thread object is for one run of its work. Once Start() has been called on it, it can never be started again - even after it has finished.",
            "A Thread object runs its work once. After Start() has been called on it, it can never be started again - not even after it has finished.",
            "Thread.Start may be called only once per Thread; any later call throws ThreadStateException, whatever state the thread is in. Task.Run makes a new task for each piece of work.",
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

        AtEveryLevel(["analysis-join-before-start"], "Waiting for a thread that was never started",
            "Join() means 'wait here until that thread has finished'. This thread has not been started yet, and .NET refuses to wait for a thread that has not started.",
            "Join() waits for a thread to finish, but this thread has not been started yet.",
            "Thread.Join throws ThreadStateException when the thread is still in the Unstarted state.",
            "Join() throws a ThreadStateException.",
            "Call Start() before Join().",
            """
            worker.Start();
            worker.Join();
            """),

        AtEveryLevel(["analysis-resource-not-closed"], "A file or stream that is never disposed of",
            "Opening a file with a stream is like borrowing it: it has to be given back by disposing of it. On one way out of this method it is never disposed of - and a writer that is not may never save what it was given.",
            "The method opens a file or stream and keeps it - it is not returned, stored or wrapped in another stream - but on this way out of the method it is never disposed of.",
            "The stream is owned by this method - not returned, stored or wrapped - and some path to an exit reaches no Dispose(). StreamWriter's buffer is not written out by finalization, so the text in it is lost, and the file stays open until the FileStream is finalized; a using declaration disposes of it on every exit.",
            "A writer that is never disposed of may never write out what it holds, so the file is left empty or cut short; any stream left open holds on to the file.",
            "Declare it with using, which disposes of it however the method ends.",
            """
            using var writer = new StreamWriter("report.txt");
            writer.Write($"total: {total}");
            """),

        AtEveryLevel(["analysis-changed-while-looping"], "A collection changed while a loop walks over it",
            "A foreach loop walks through a collection with a hidden helper that remembers where it is. Here the collection is changed while the loop is going - under another name, or in a method the loop calls - so .NET stops the loop.",
            "The loop's collection is changed while the foreach loop is still walking over it - through another name that holds the same collection, or in a method the loop calls.",
            "A change reaches the iterated collection between two MoveNext calls - through an alias, or through a called method's effect summary - so the enumerator's version check throws InvalidOperationException, 'Collection was modified'.",
            "The next step of the loop throws InvalidOperationException: the collection was modified.",
            "Loop over a copy with ToList(), or collect what to change and change it after the loop.",
            """
            foreach (var name in names.ToList())
            {
                alias.Add(name + "!");
            }
            """),

        AtEveryLevel(["analysis-read-before-join"], "Reading a result before the threads have finished",
            "Starting work on another thread is like asking someone to count a pile of coins while you get on with something else. Reading the total straight away gets whatever they have counted so far, not the final answer; waiting for them to finish is what join does.",
            "The work was started on another thread, but this line runs before the Join() or Wait() that waits for it, so the work may still be changing the value. The line reads whatever it holds at that moment - often not the final result.",
            "No happens-before edge orders the other thread's writes before this read - Thread.Join, Task.Wait and await would create one, and the read comes before them - so it can see any value written so far.",
            "The program shows a value from part-way through - different on each run, and usually wrong.",
            "Read the result after the thread's Join() or the task's Wait() - or after awaiting it.",
            """
            task.Wait();
            Console.WriteLine(total);
            """),

        AtEveryLevel(["analysis-data-race"], "Shared data used without the lock that guards it",
            "A lock is like a talking stick: only whoever holds it may use the shared value. Here one place holds the stick while it uses the value, but another uses it without the stick - so the stick protects nothing. A lock only helps if every piece of code that uses the value takes the same one - even code that only reads it.",
            "This data is used with a lock in one place and without it - or with a different lock - in another, by threads that can run at the same time. A lock only protects data if everything that uses the data holds that same lock.",
            "The two accesses' locksets are disjoint and neither happens before the other, so they race: .NET makes single aligned reads and writes of pointer size or less atomic, but not read-modify-write sequences, and it promises nothing about when one thread sees another's write.",
            "Changes can be lost, and a thread can read an out-of-date value - differently from one run to the next.",
            "Take the same lock everywhere the data is used.",
            """
            lock (gate)
            {
                total += amount;
            }
            """),

        AtEveryLevel(["analysis-wait-without-lock"], "Monitor.Wait or Pulse without its lock",
            "Monitor.Wait and Pulse let threads signal each other about an object, and they only work while the thread holds that object's lock - inside lock on that object. Here the lock is not held.",
            "Monitor.Wait, Pulse and PulseAll must be called while holding the lock of the object they are given, and here that lock is not held.",
            "Monitor.Wait, Pulse and PulseAll require the calling thread to own the object's lock, and throw SynchronizationLockException when it does not.",
            "The call throws a SynchronizationLockException every time.",
            "Call it inside lock (that object).",
            """
            lock (gate)
            {
                while (!ready)
                    Monitor.Wait(gate);
            }
            """),

        AtEveryLevel(["analysis-wait-not-in-loop"], "Monitor.Wait not in a loop",
            "Monitor.Wait puts a thread to sleep until it is told something changed. By the time it wakes and gets the lock back, another thread may already have used what it was waiting for. Checking again in a loop makes sure the condition really holds.",
            "A waiting thread can wake up after another thread has already used what it waited for.",
            "After a Pulse the waiting thread has to get the lock back, and another thread can take it first and change the state, so the condition has to be tested again in a loop after Monitor.Wait returns.",
            "The code after the wait can run while the condition it needs is still false.",
            "Wait in a while loop that checks the condition again each time it wakes.",
            """
            while (!ready)
                Monitor.Wait(gate);
            """),
    ];
}
