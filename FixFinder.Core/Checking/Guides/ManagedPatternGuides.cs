using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the Java and C# mistakes that compile and then give the wrong answer, each explained three ways: for
/// someone new to programming, as it is usually taught, and in the languages' own terms.
/// </summary>
internal static class ManagedPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        AtEveryLevel(["logic-csharp-console-read-number"], "Console.Read() used as a number",
            "Console.Read() reads a single character and gives back its code - the number the computer uses for that " +
            "character. The character 7 has the code 55, so you get 55, not 7 - and the Enter key is still waiting to be read.",
            "Console.Read() reads one character and returns its character code. Typing 7 gives 55, and pressing Enter gives 13.",
            "Console.Read returns the next UTF-16 code unit from standard input as an int, or -1 at the end of the input, so 7 " +
            "gives 55, and the rest of the line, its newline included, stays buffered for the next read. Console.ReadLine with " +
            "int.Parse or TryParse reads a number.",
            "Every calculation made with the value is wrong, and the next Console.Read() picks up the rest of what was typed.",
            "Read the whole line and parse it: int.Parse(Console.ReadLine()).",
            """
            int age = int.Parse(Console.ReadLine());
            """),

        AtEveryLevel(["logic-csharp-throw-ex"], "throw ex; loses where the error happened",
            "A caught error carries a record of where it happened - its stack trace. throw ex; sends it on but rewrites that " +
            "record to start at the catch block, so the report no longer shows the line that really failed. throw; on its own " +
            "sends it on unchanged.",
            "Inside a catch block, throw ex; throws the exception again with a new stack trace that starts at the catch block.",
            "throw ex; rethrows the same exception object but resets its stack trace to the point of the rethrow, while throw; " +
            "rethrows it with the original trace; ExceptionDispatchInfo.Capture(ex).Throw() does the same outside a catch block.",
            "The error report no longer points at the line that failed, which is the one thing needed to fix it.",
            "Write throw; on its own to pass the same exception on unchanged.",
            """
            catch (IOException ex)
            {
                Log(ex.Message);
                throw;
            }
            """),

        AtEveryLevel(["logic-csharp-blocking-wait"], "Blocking on a task inside an async method",
            "await waits for slow work without stopping the program. .Result and .Wait() wait by stopping the program's thread " +
            "completely - and in desktop and web apps the work may need that same thread to finish, so each waits for the " +
            "other for ever.",
            ".Result, .Wait() and .GetAwaiter().GetResult() stop the thread until the task finishes, even inside a method that could await it.",
            "Blocking on a Task holds the calling thread. Where a single-threaded SynchronizationContext is current - WinForms, " +
            "WPF, ASP.NET before Core - the task's continuation needs that thread, so the two deadlock. .Result and .Wait() " +
            "also wrap a failure in an AggregateException.",
            "In desktop and web apps it can freeze the program for good, and exceptions arrive wrapped in an AggregateException.",
            "Use await, which waits without blocking and hands back the result or the exception as it is.",
            """
            string page = await client.GetStringAsync(url);
            """),

        AtEveryLevel(["logic-csharp-not-disposed"], "A file that is never closed",
            "Opening a file with a StreamWriter is like borrowing it: you have to hand it back by closing it. Until then, what " +
            "you wrote may still be waiting in memory, and the file stays locked. using closes it for you at the end, even if " +
            "something goes wrong.",
            "A StreamReader, StreamWriter or FileStream holds the file open until it is closed or disposed.",
            "A StreamWriter keeps text in its own buffer until it is flushed or disposed, and nothing flushes that buffer if the " +
            "program ends without disposing it, so the text can be lost; the file's handle also stays open until then. A using " +
            "declaration disposes it at the end of the enclosing scope, even when an exception is thrown.",
            "A writer that is never closed can lose everything written to it, and an open file stays locked against other programs.",
            "Declare it with using, so it is closed at the end of the block even if an exception is thrown.",
            """
            using var writer = new StreamWriter("scores.txt");
            writer.WriteLine(total);
            """),

        AtEveryLevel(["logic-case-never-matches"], "Comparing changed-case text with the wrong case",
            "ToLower() turns every letter into lower case, so its result can never contain a capital letter. Comparing it with " +
            "\"Yes\", which has a capital Y, can never be true - the comparison has to use \"yes\".",
            "After ToLower() or toLowerCase() every letter is lower case, so comparing with \"Yes\" can never be true (and upper case the same way round).",
            "After ToLower or toLowerCase the string holds no character that lower-casing would change, so an exact comparison " +
            "with a literal containing a capital letter is always false - and the reverse after ToUpper. A case-insensitive " +
            "comparison, such as equalsIgnoreCase or StringComparison.OrdinalIgnoreCase, needs no change of case at all.",
            "The check always gives the same answer, so the branch it guards never runs, or always runs.",
            "Write the text it is compared with in the same case: \"yes\".",
            """
            if (answer.ToLower() == "yes")
            {
                Save();
            }
            """),

        AtEveryLevel(["logic-char-used-as-digit"], "A character used as a number",
            "Each character is stored as a code: '0' is 48, '1' is 49 and so on up to '9', which is 57. Using the character as " +
            "a number uses its code, so '7' counts as 55. Taking away '0', which is 48, gives the digit's real value.",
            "A char is stored as its character code. Converting '7' to an int, or adding it to a total, uses 55 rather than 7.",
            "A char is a UTF-16 code unit and converts to int as its code, so '7' is 55. The digits are consecutive from '0', " +
            "48, so c - '0' is the digit's value; Character.getNumericValue and char.GetNumericValue handle other scripts too.",
            "Sums of digits, check digits and anything read one character at a time come out far too large.",
            "Subtract '0' to get the digit's value: '7' - '0' is 7.",
            """
            int sum = 0;
            foreach (char c in number)
            {
                sum += c - '0';
            }
            """),

        AtEveryLevel(["logic-count-from-missing-key"], "Counting with a key that is not there yet",
            "The first time a word is counted, the dictionary has no entry for it yet. Java's get gives back null - nothing - " +
            "for a missing key, and adding 1 to nothing crashes; C#'s [ ] refuses a missing key with an error.",
            "The first time a key is counted it has no entry, so Java's get returns null and C#'s [ ] throws KeyNotFoundException.",
            "Map.get returns null for a missing key, and unboxing that null in get(key) + 1 throws NullPointerException; the " +
            "Dictionary indexer throws KeyNotFoundException. getOrDefault, merge(key, 1, Integer::sum) and GetValueOrDefault " +
            "supply the missing zero.",
            "The program crashes on the first word, letter or item it tries to count.",
            "Start new keys from 0: getOrDefault(key, 0) in Java, GetValueOrDefault(key) in C#.",
            """
            counts.put(word, counts.getOrDefault(word, 0) + 1);
            """),

        AtEveryLevel(["logic-collection-printed"], "Printing an array or list directly",
            "Printing a whole array does not print what is in it. Java prints its type and a code, like [I@1b6d3586, and C# " +
            "prints its type's name, like System.Int32[]. To see the items, turn them into text first.",
            "Arrays do not turn their items into text. Java prints the type and an address, like [I@1b6d3586; C# prints the type name, like System.Int32[].",
            "Arrays inherit Object's toString - in Java the type descriptor, @ and the identity hash in hex - and ToString - " +
            "in C# the type's full name. Arrays.toString and string.Join format the elements. Lists differ: Java's ArrayList " +
            "prints its elements, while C#'s List<T> prints its type name.",
            "The output shows nothing of what the array holds, so it looks like the program is broken.",
            "Java: Arrays.toString(values). C#: string.Join(\", \", values).",
            """
            System.out.println(Arrays.toString(scores));
            """),

        AtEveryLevel(["logic-local-hides-field"], "A constructor that sets a new local instead of the field",
            "Writing a type in front of a name, like String name = n;, makes a brand-new variable that only exists inside the " +
            "constructor - even though the class has a field with the same name. So the field is never set.",
            "Writing a type in front of a name, such as String name = n;, declares a new local variable that hides the field with the same name.",
            "A local variable declared with a field's name shadows the field for the rest of the block, so the assignment sets " +
            "the local and the field keeps its default of null, 0 or false; this.name always means the field.",
            "The field keeps its default of null, 0 or false, and the rest of the class uses that.",
            "Remove the type and name the field: this.name = n;",
            """
            public Student(String n) {
                this.name = n;
            }
            """),

        AtEveryLevel(["logic-remove-while-counting-up"], "Removing items while counting up",
            "Removing the item at position i makes every item after it move down one place, so the next item slides into " +
            "position i. The loop then moves on to i + 1 and skips it - so two matching items in a row leave one behind.",
            "Removing the item at position i moves every later item down one place, but the loop then moves on to i + 1.",
            "List.remove(int) and RemoveAt shift every later element one place left, so moving the index on after a removal " +
            "skips the element that slid into the empty slot. Counting down, or removeIf and RemoveAll, avoids it.",
            "The item just after each one removed is never checked, so two matching items side by side leave one behind.",
            "Loop from the end towards the start, so a removal only moves items already checked.",
            """
            for (int i = names.size() - 1; i >= 0; i--) {
                if (names.get(i).isEmpty()) names.remove(i);
            }
            """),

        AtEveryLevel(["logic-loop-copy-assigned"], "Assigning to the loop's copy of an item",
            "In a for-each loop the loop's variable holds a copy of each item. Changing the variable changes only the copy; the " +
            "array or list itself is left as it was. To change the items, go through the positions and change array[i].",
            "In a for-each loop the variable is a copy of each item, so assigning to it changes the copy and not the array or list.",
            "The enhanced for loop copies each element into a new local variable, so assigning to it rebinds the local and " +
            "writes nothing back - C# goes further and makes the foreach variable read-only. Changing the elements needs an " +
            "indexed loop, or in C++ a range-for by reference, for (int& x : values).",
            "The values the loop was meant to change are left exactly as they were.",
            "Loop over the positions and assign to the array itself, or loop by reference in C++ (for (int& x : values)).",
            """
            for (int i = 0; i < scores.length; i++) {
                scores[i] = scores[i] * 2;
            }
            """),

        AtEveryLevel(["logic-loop-steps-away"], "A loop that counts the wrong way",
            "A counting loop needs its counter to move towards the limit that ends it. Here the condition keeps the loop going " +
            "while i is below the limit, but each step makes i smaller - moving away from it - so the loop never reaches its " +
            "end, or stops only when the number overflows.",
            "The condition keeps the loop going while i is below (or above) a limit, but the step moves i away from that limit.",
            "The condition bounds the counter in one direction while the update moves it the other way, so the update never " +
            "makes the condition false. In Java and C# the int then wraps round after about 2^31 passes, which ends the loop; " +
            "in C and C++ signed overflow is undefined behaviour.",
            "The loop never ends, or runs until the number overflows, and every pass does the wrong work.",
            "Step towards the limit: i++ with <, i-- with >.",
            """
            for (int i = 10; i > 0; i--) {
                System.out.println(i);
            }
            """),
    ];
}
