using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for the Java and C# mistakes that compile and then give the wrong answer.</summary>
internal static class ManagedPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Pattern(["logic-csharp-console-read-number"], "Console.Read() used as a number",
            "Console.Read() reads one character and returns its character code. Typing 7 gives 55, and pressing Enter gives 13.",
            "Every calculation made with the value is wrong, and the next Console.Read() picks up the rest of what was typed.",
            "Read the whole line and parse it: int.Parse(Console.ReadLine()).",
            """
            int age = int.Parse(Console.ReadLine());
            """),

        Pattern(["logic-csharp-throw-ex"], "throw ex; loses where the error happened",
            "Inside a catch block, throw ex; throws the exception again with a new stack trace that starts at the catch block.",
            "The error report no longer points at the line that failed, which is the one thing needed to fix it.",
            "Write throw; on its own to pass the same exception on unchanged.",
            """
            catch (IOException ex)
            {
                Log(ex.Message);
                throw;
            }
            """),

        Pattern(["logic-csharp-blocking-wait"], "Blocking on a task inside an async method",
            ".Result, .Wait() and .GetAwaiter().GetResult() stop the thread until the task finishes, even inside a method that could await it.",
            "In desktop and web apps it can freeze the program for good, and exceptions arrive wrapped in an AggregateException.",
            "Use await, which waits without blocking and hands back the result or the exception as it is.",
            """
            string page = await client.GetStringAsync(url);
            """),

        Pattern(["logic-csharp-not-disposed"], "A file that is never closed",
            "A StreamReader, StreamWriter or FileStream holds the file open until it is closed or disposed.",
            "A writer that is never closed can lose everything written to it, and an open file stays locked against other programs.",
            "Declare it with using, so it is closed at the end of the block even if an exception is thrown.",
            """
            using var writer = new StreamWriter("scores.txt");
            writer.WriteLine(total);
            """),

        Pattern(["logic-case-never-matches"], "Comparing changed-case text with the wrong case",
            "After ToLower() or toLowerCase() every letter is lower case, so comparing with \"Yes\" can never be true (and upper case the same way round).",
            "The check always gives the same answer, so the branch it guards never runs, or always runs.",
            "Write the text it is compared with in the same case: \"yes\".",
            """
            if (answer.ToLower() == "yes")
            {
                Save();
            }
            """),

        Pattern(["logic-char-used-as-digit"], "A character used as a number",
            "A char is stored as its character code. Converting '7' to an int, or adding it to a total, uses 55 rather than 7.",
            "Sums of digits, check digits and anything read one character at a time come out far too large.",
            "Subtract '0' to get the digit's value: '7' - '0' is 7.",
            """
            int sum = 0;
            foreach (char c in number)
            {
                sum += c - '0';
            }
            """),

        Pattern(["logic-count-from-missing-key"], "Counting with a key that is not there yet",
            "The first time a key is counted it has no entry, so Java's get returns null and C#'s [ ] throws KeyNotFoundException.",
            "The program crashes on the first word, letter or item it tries to count.",
            "Start new keys from 0: getOrDefault(key, 0) in Java, GetValueOrDefault(key) in C#.",
            """
            counts.put(word, counts.getOrDefault(word, 0) + 1);
            """),

        Pattern(["logic-collection-printed"], "Printing an array or list directly",
            "Arrays do not turn their items into text. Java prints the type and an address, like [I@1b6d3586; C# prints the type name, like System.Int32[].",
            "The output shows nothing of what the array holds, so it looks like the program is broken.",
            "Java: Arrays.toString(values). C#: string.Join(\", \", values).",
            """
            System.out.println(Arrays.toString(scores));
            """),

        Pattern(["logic-local-hides-field"], "A constructor that sets a new local instead of the field",
            "Writing a type in front of a name, such as String name = n;, declares a new local variable that hides the field with the same name.",
            "The field keeps its default of null, 0 or false, and the rest of the class uses that.",
            "Remove the type and name the field: this.name = n;",
            """
            public Student(String n) {
                this.name = n;
            }
            """),

        Pattern(["logic-remove-while-counting-up"], "Removing items while counting up",
            "Removing the item at position i moves every later item down one place, but the loop then moves on to i + 1.",
            "The item just after each one removed is never checked, so two matching items side by side leave one behind.",
            "Loop from the end towards the start, so a removal only moves items already checked.",
            """
            for (int i = names.size() - 1; i >= 0; i--) {
                if (names.get(i).isEmpty()) names.remove(i);
            }
            """),

        Pattern(["logic-loop-copy-assigned"], "Assigning to the loop's copy of an item",
            "In a for-each loop the variable is a copy of each item, so assigning to it changes the copy and not the array or list.",
            "The values the loop was meant to change are left exactly as they were.",
            "Loop over the positions and assign to the array itself, or loop by reference in C++ (for (int& x : values)).",
            """
            for (int i = 0; i < scores.length; i++) {
                scores[i] = scores[i] * 2;
            }
            """),

        Pattern(["logic-loop-steps-away"], "A loop that counts the wrong way",
            "The condition keeps the loop going while i is below (or above) a limit, but the step moves i away from that limit.",
            "The loop never ends, or runs until the number overflows, and every pass does the wrong work.",
            "Step towards the limit: i++ with <, i-- with >.",
            """
            for (int i = 10; i > 0; i--) {
                System.out.println(i);
            }
            """),
    ];
}
