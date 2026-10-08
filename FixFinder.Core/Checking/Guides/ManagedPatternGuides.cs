using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the Java and C# mistakes that compile and then give the wrong answer, each explained for someone new to
/// programming.
/// </summary>
internal static class ManagedPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-csharp-console-read-number"], "Console.Read() used as a number",
            "Console.Read() reads a single character and gives back its code - the number the computer uses for that " +
            "character. The character 7 has the code 55, so you get 55, not 7 - and the Enter key is still waiting to be read.",
            "Every calculation made with the value is wrong, and the next Console.Read() picks up the rest of what was typed.",
            "Read the whole line and parse it: int.Parse(Console.ReadLine()).",
            """
            int age = int.Parse(Console.ReadLine());
            """),

        GuideFor(["logic-csharp-throw-ex"], "throw ex; loses where the error happened",
            "A caught error carries a record of where it happened - its stack trace. throw ex; sends it on but rewrites that " +
            "record to start at the catch block, so the report no longer shows the line that really failed. throw; on its own " +
            "sends it on unchanged.",
            "The error report no longer points at the line that failed, which is the one thing needed to fix it.",
            "Write throw; on its own to pass the same exception on unchanged.",
            """
            catch (IOException ex)
            {
                Log(ex.Message);
                throw;
            }
            """),

        GuideFor(["logic-csharp-blocking-wait"], "Blocking on a task inside an async method",
            "await waits for slow work without stopping the program. .Result and .Wait() wait by stopping the program's thread " +
            "completely - and in desktop and web apps the work may need that same thread to finish, so each waits for the " +
            "other for ever.",
            "In desktop and web apps it can freeze the program for good, and exceptions arrive wrapped in an AggregateException.",
            "Use await, which waits without blocking and hands back the result or the exception as it is.",
            """
            string page = await client.GetStringAsync(url);
            """),

        GuideFor(["logic-csharp-not-disposed"], "A file that is never closed",
            "Opening a file with a StreamWriter is like borrowing it: you have to hand it back by closing it. Until then, what " +
            "you wrote may still be waiting in memory, and the file stays locked. using closes it for you at the end, even if " +
            "something goes wrong.",
            "A writer that is never closed can lose everything written to it, and an open file stays locked against other programs.",
            "Declare it with using, so it is closed at the end of the block even if an exception is thrown.",
            """
            using var writer = new StreamWriter("scores.txt");
            writer.WriteLine(total);
            """),

        GuideFor(["logic-case-never-matches"], "Comparing changed-case text with the wrong case",
            "ToLower() turns every letter into lower case, so its result can never contain a capital letter. Comparing it with " +
            "\"Yes\", which has a capital Y, can never be true - the comparison has to use \"yes\".",
            "The check always gives the same answer, so the branch it guards never runs, or always runs.",
            "Write the text it is compared with in the same case: \"yes\".",
            """
            if (answer.ToLower() == "yes")
            {
                Save();
            }
            """),

        GuideFor(["logic-char-used-as-digit"], "A character used as a number",
            "Each character is stored as a code: '0' is 48, '1' is 49 and so on up to '9', which is 57. Using the character as " +
            "a number uses its code, so '7' counts as 55. Taking away '0', which is 48, gives the digit's real value.",
            "Sums of digits, check digits and anything read one character at a time come out far too large.",
            "Subtract '0' to get the digit's value: '7' - '0' is 7.",
            """
            int sum = 0;
            foreach (char c in number)
            {
                sum += c - '0';
            }
            """),

        GuideFor(["logic-count-from-missing-key"], "Counting with a key that is not there yet",
            "The first time a word is counted, the dictionary has no entry for it yet. Java's get gives back null - nothing - " +
            "for a missing key, and adding 1 to nothing crashes; C#'s [ ] refuses a missing key with an error.",
            "The program crashes on the first word, letter or item it tries to count.",
            "Start new keys from 0: getOrDefault(key, 0) in Java, GetValueOrDefault(key) in C#.",
            """
            counts.put(word, counts.getOrDefault(word, 0) + 1);
            """),

        GuideFor(["logic-collection-printed"], "Printing an array or list directly",
            "Printing a whole array does not print what is in it. Java prints its type and a code, like [I@1b6d3586, and C# " +
            "prints its type's name, like System.Int32[]. To see the items, turn them into text first.",
            "The output shows nothing of what the array holds, so it looks like the program is broken.",
            "Java: Arrays.toString(values). C#: string.Join(\", \", values).",
            """
            System.out.println(Arrays.toString(scores));
            """),

        GuideFor(["logic-local-hides-field"], "A constructor that sets a new local instead of the field",
            "Writing a type in front of a name, like String name = n;, makes a brand-new variable that only exists inside the " +
            "constructor - even though the class has a field with the same name. So the field is never set.",
            "The field keeps its default of null, 0 or false, and the rest of the class uses that.",
            "Remove the type and name the field: this.name = n;",
            """
            public Student(String n) {
                this.name = n;
            }
            """),

        GuideFor(["logic-remove-while-counting-up"], "Removing items while counting up",
            "Removing the item at position i makes every item after it move down one place, so the next item slides into " +
            "position i. The loop then moves on to i + 1 and skips it - so two matching items in a row leave one behind.",
            "The item just after each one removed is never checked, so two matching items side by side leave one behind.",
            "Loop from the end towards the start, so a removal only moves items already checked.",
            """
            for (int i = names.size() - 1; i >= 0; i--) {
                if (names.get(i).isEmpty()) names.remove(i);
            }
            """),

        GuideFor(["logic-loop-copy-assigned"], "Assigning to the loop's copy of an item",
            "In a for-each loop the loop's variable holds a copy of each item. Changing the variable changes only the copy; the " +
            "array or list itself is left as it was. To change the items, go through the positions and change array[i].",
            "The values the loop was meant to change are left exactly as they were.",
            "Loop over the positions and assign to the array itself, or loop by reference in C++ (for (int& x : values)).",
            """
            for (int i = 0; i < scores.length; i++) {
                scores[i] = scores[i] * 2;
            }
            """),

        GuideFor(["logic-loop-steps-away"], "A loop that counts the wrong way",
            "A counting loop needs its counter to move towards the limit that ends it. Here the condition keeps the loop going " +
            "while i is below the limit, but each step makes i smaller - moving away from it - so the loop never reaches its " +
            "end, or stops only when the number overflows.",
            "The loop never ends, or runs until the number overflows, and every pass does the wrong work.",
            "Step towards the limit: i++ with <, i-- with >.",
            """
            for (int i = 10; i > 0; i--) {
                System.out.println(i);
            }
            """),
    ];
}
