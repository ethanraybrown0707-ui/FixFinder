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

        GuideFor(["logic-csharp-text-without-dollar"], "Braces in C# text with no $",
            "C# puts a value into text only when the text is interpolated - when a $ comes before its opening quote, as in " +
            "$\"Total: {total}\". Without the $, the braces are ordinary characters: {total} is printed as it is written, braces and " +
            "all. The compiler does not read inside the text: the most it says is warning CS0219, that total is assigned but its " +
            "value is never used - and only when total was given a fixed value, and the text was the one place meant to use it.",
            "The output shows {total} instead of the total, which looks broken to anyone using the program.",
            "Put a $ before the opening quote. Text that is given values to fill in - Console.WriteLine(\"{0}\", total) - uses " +
            "numbers in its braces, and needs no $.",
            """
            int total = 150;
            Console.WriteLine($"Total: {total}");
            """),

        GuideFor(["logic-loop-steps-over-end"], "A loop that steps over its end",
            "count != 10 keeps the loop going until count is exactly 10. Counting from 0 by 3, count is 0, 3, 6, 9, 12, 15 - it " +
            "steps over 10 without ever being it - so the test stays true. In JavaScript the loop never ends. An int in Java, C# " +
            "and C runs on until it passes the largest int and wraps round to the smallest, and lands on 10 only after " +
            "2,863,311,534 passes. None of their compilers says anything.",
            "The loop does its work for numbers far past the end - billions of times over, or for ever.",
            "Stop once the counter reaches the end or passes it: count < 10 when counting up, count > 10 when counting down.",
            """
            public class Main {
                public static void main(String[] args) {
                    for (int count = 0; count < 10; count += 3) {
                        System.out.println(count);
                    }
                }
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

        GuideFor(["logic-csharp-select-never-run"], "A Select that never runs",
            "Select does not run its lambda straight away: it gives back a sequence that works each item out only when something " +
            "reads it. On a line of its own, nothing reads it, so the lambda is never run - Console.WriteLine inside it prints " +
            "nothing. dotnet build says nothing.",
            "Nothing the lambda does happens: the marks are never printed.",
            "Use a foreach loop, which runs its body for each item straight away - and needs no value given back.",
            """
            var marks = new[] { 70, 80, 90 };
            foreach (var mark in marks) { Console.WriteLine(mark); }
            """),

        GuideFor(["logic-java-round-divided-as-whole-number"], "Rounding to two places that loses them",
            "Math.round gives a whole number - a long - so Math.round(price * 100) / 100 divides a whole number by a whole number, " +
            "which cuts off the fraction: for a price of 12.345 it is 12.0, not 12.35. javac says nothing, as a long can be stored " +
            "in a double.",
            "The places the rounding was meant to keep are lost: every result comes out as a whole number, with .0 after it.",
            "Divide by 100.0 - a double - so the division keeps its fraction: Math.round(price * 100) / 100.0 is 12.35.",
            """
            public class Main {
                public static void main(String[] args) {
                    double price = 12.345;
                    double rounded = Math.round(price * 100) / 100.0;
                    System.out.println(rounded);
                }
            }
            """),

        GuideFor(["logic-java-constant-overflow"], "Whole numbers too big for an int",
            "365 * 24 * 60 * 60 * 1000 is worked out in ints, because every number in it is one - and an int holds nothing above " +
            "2,147,483,647. The product, 31,536,000,000, wraps round to 1,471,228,928 before it is stored anywhere, even in a long. " +
            "javac says nothing.",
            "The number is wrong, and nothing says so: the milliseconds in a year come out as 1471228928.",
            "Make the first number a long with an L - 365L * 24 * 60 * 60 * 1000 - so the arithmetic is done in longs, and store it " +
            "in a long.",
            """
            public class Main {
                public static void main(String[] args) {
                    long millisPerYear = 365L * 24 * 60 * 60 * 1000;
                    System.out.println(millisPerYear);
                }
            }
            """),

        GuideFor(["logic-java-regex-character-as-text"], "A character split on that means something else",
            "split, replaceAll and replaceFirst read their first text as a regular expression, where some characters mean something " +
            "else: . is any character, | is \"or\", and $ is the end of the text. So \"3.14\".split(\".\") gives no parts at all, " +
            "\"a|b\".split(\"|\") splits between every character, and \"3.14\".replaceAll(\".\", \",\") gives ,,,,. javac says nothing.",
            "The text is split or replaced in the wrong places, with no error - a list of numbers read from a line comes back empty.",
            "Escape the character - split(\"\\\\.\") - so it means itself, or use replace, which takes its text as it is: " +
            "\"3.14\".replace(\".\", \",\") is 3,14.",
            """
            public class Main {
                public static void main(String[] args) {
                    String[] parts = "3.14".split("\\.");
                    System.out.println(parts.length);
                }
            }
            """),

        GuideFor(["logic-java-array-shared"], "A second name for an array is not a copy",
            "int[] backup = marks; does not copy the array: it gives the same array a second name. A change made through either name " +
            "- marks[0] = 100 - is a change to the one array, so backup shows it too; the same is so of a List. javac says nothing.",
            "The second name does not keep the values the array had: after marks[0] = 100, backup[0] prints 100.",
            "Make a copy: marks.clone() for an array, new ArrayList<>(marks) for a List. The copy has the same items, and a change " +
            "to one no longer shows in the other.",
            """
            public class Main {
                public static void main(String[] args) {
                    int[] marks = {70, 80, 90};
                    int[] backup = marks.clone();
                    marks[0] = 100;
                    System.out.println(backup[0]);
                }
            }
            """),

        GuideFor(["logic-csharp-array-shared"], "A second name for an array is not a copy",
            "int[] backup = marks; does not copy the array: it gives the same array a second name. A change made through either name " +
            "- marks[0] = 100 - is a change to the one array, so backup shows it too; the same is so of a List<T>. dotnet build says " +
            "nothing.",
            "The second name does not keep the values the array had: after marks[0] = 100, backup[0] prints 100.",
            "Make a copy: marks.ToArray() for an array, marks.ToList() for a List<T>. The copy has the same items, and a change to " +
            "one no longer shows in the other.",
            """
            int[] marks = { 70, 80, 90 };
            int[] backup = marks.ToArray();
            marks[0] = 100;
            Console.WriteLine(backup[0]);
            """),

        GuideFor(["logic-csharp-format-code-printed"], "A %d code that Console.WriteLine does not fill in",
            "%d and %s are codes from C's printf. Console.WriteLine fills in {0}, {1} and on with the values after the text - not " +
            "%d - so Console.WriteLine(\"Total: %d\", total) prints Total: %d, and the total is never printed at all.",
            "The value is missing from the output, with no error or warning.",
            "Write {0} where the value goes - Console.WriteLine(\"Total: {0}\", total) - or put the value into the text with $: " +
            "$\"Total: {total}\".",
            """
            int total = 150;
            Console.WriteLine($"Total: {total}");
            """),

        GuideFor(["logic-csharp-array-equals"], "Two arrays compared with Equals or ==",
            "An array's Equals is the same as ==: both ask whether two names are the very same array, not whether two arrays " +
            "hold the same items. Two arrays made separately are never the same array, so they are never equal, even with the " +
            "same items in the same order.",
            "The comparison is false for arrays that hold the same items, with no error or warning.",
            "Compare the items with SequenceEqual, from System.Linq: first.SequenceEqual(second).",
            """
            using System.Linq;

            int[] first = { 70, 80 };
            int[] second = { 70, 80 };
            Console.WriteLine(first.SequenceEqual(second));
            """),
    ];
}
