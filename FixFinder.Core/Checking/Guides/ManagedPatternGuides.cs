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

        GuideFor(["logic-csharp-chars-added"], "A char added to a number or a char",
            "A char is a number underneath - each character has a code, and 'A' is 65. Adding a whole number or another char to it " +
            "adds the codes and gives a number, so grade + 1 is 66, not 'B', and 'a' + 'b' is 195, not \"ab\". Console prints " +
            "that number.",
            "A number is printed where a letter or two letters were meant, with no error or warning.",
            "Turn the sum back into a character with (char)(grade + 1), or start from text to join two characters: \"\" + 'a' + 'b'.",
            """
            char grade = 'A';
            Console.WriteLine((char)(grade + 1));
            """),

        GuideFor(["logic-java-date-pattern-letters"], "A date pattern with the wrong letters",
            "In the patterns DateTimeFormatter.ofPattern and SimpleDateFormat take, the case of each letter matters: yyyy is the year, " +
            "but YYYY is the year the week belongs to - 30 December 2024 is in the first week of 2025, so it prints 2025; dd is the day " +
            "of the month, but DD is the day of the year - 5 February is 36; and MM is the month, but mm is the minutes.",
            "The date is printed wrongly with no error - and for YYYY only in the last days of December, so it can pass every test and " +
            "go wrong later.",
            "Use yyyy for the year, MM for the month and dd for the day: DateTimeFormatter.ofPattern(\"yyyy-MM-dd\").",
            """
            import java.time.LocalDate;
            import java.time.format.DateTimeFormatter;

            public class Main {
                public static void main(String[] args) {
                    LocalDate date = LocalDate.of(2024, 12, 30);
                    System.out.println(date.format(DateTimeFormatter.ofPattern("yyyy-MM-dd")));
                }
            }
            """),

        GuideFor(["logic-csharp-date-format-letters"], "A date format with the wrong letters",
            "In a date's format, the case of each letter matters: MM is the month, but mm is the minutes - so 9:30 on 5 February 2024 " +
            "with \"yyyy-mm-dd\" gives 2024-30-05. YYYY and DD are not letters a date's format knows at all, so they are printed as they " +
            "are: \"YYYY-MM-DD\" gives YYYY-02-DD.",
            "The date is printed wrongly, with no error.",
            "Use yyyy for the year, MM for the month and dd for the day: when.ToString(\"yyyy-MM-dd\").",
            """
            var when = new DateTime(2024, 2, 5, 9, 30, 0);
            Console.WriteLine(when.ToString("yyyy-MM-dd"));
            """),

        GuideFor(["logic-java-thread-run"], "run() called on a Thread",
            "A Thread is started with start(), which runs its work on a new thread, alongside the code that started it. run() is only the " +
            "method that holds the work: calling it runs the work there and then, on the thread that called it, like any other method - " +
            "so nothing runs alongside, and the program waits for it.",
            "The program does its work one piece at a time where it was meant to do them together, with no error - the worker printed that " +
            "it ran on main.",
            "Start the thread: worker.start().",
            """
            public class Main {
                public static void main(String[] args) throws InterruptedException {
                    Thread worker = new Thread(() -> System.out.println("working"));
                    worker.start();
                    worker.join();
                }
            }
            """),

        GuideFor(["logic-java-bigdecimal-from-double"], "A BigDecimal made from a double",
            "A double holds most decimals only nearly: 0.1 is stored as 0.1000000000000000055511151231257827021181583404541015625. " +
            "new BigDecimal(0.1) is given that double, and holds every digit of it - so the BigDecimal, used to keep money exact, starts with " +
            "the error in it.",
            "Sums that should come out exact are off in their last digits, and comparisons with equals fail.",
            "Make it from text: new BigDecimal(\"0.1\") - or, from a double you already have, BigDecimal.valueOf(price).",
            """
            import java.math.BigDecimal;

            public class Main {
                public static void main(String[] args) {
                    BigDecimal price = new BigDecimal("0.1");
                    System.out.println(price.add(new BigDecimal("0.2")));
                }
            }
            """),

        GuideFor(["logic-java-aslist-of-primitive-array"], "Arrays.asList of an int[]",
            "Arrays.asList makes a list of objects. An int[] - or any array of a primitive type - is itself one object, so " +
            "Arrays.asList(marks) is a list with one item, the whole array, not a list of its numbers.",
            "The list's size is 1, and contains, indexOf and the rest find none of the numbers, with no error.",
            "Make a list of the numbers: Arrays.stream(marks).boxed().collect(Collectors.toList()) - or declare the array as Integer[].",
            """
            import java.util.Arrays;
            import java.util.List;
            import java.util.stream.Collectors;

            public class Main {
                public static void main(String[] args) {
                    int[] marks = {70, 80, 90};
                    List<Integer> listed = Arrays.stream(marks).boxed().collect(Collectors.toList());
                    System.out.println(listed.contains(80));
                }
            }
            """),

        GuideFor(["logic-java-abs-of-hash"], "Math.abs of a hashCode, then %",
            "Math.abs makes a number positive - except the smallest int, -2147483648, whose positive is too big for an int, so Math.abs " +
            "gives it back as it is. A hashCode can be that number - \"polygenelubricants\".hashCode() is - and so can nextInt(). " +
            "Math.abs(key.hashCode()) % 10 is then negative: -8.",
            "Used as a position in an array of buckets, the negative number stops the program - for only the rare keys that hash to it.",
            "Take a remainder that is never negative: Math.floorMod(key.hashCode(), 10).",
            """
            public class Main {
                public static void main(String[] args) {
                    String key = "polygenelubricants";
                    System.out.println(Math.floorMod(key.hashCode(), 10));
                }
            }
            """),

        GuideFor(["logic-java-optional-get-unchecked"], "get() on an Optional nothing checked",
            "findFirst, findAny, max and min give an Optional: a box that holds the item, or is empty when the stream has nothing in " +
            "it - no item passed the filter, or the list was empty. get() takes the item out, and has nothing to give when the box " +
            "is empty.",
            "On an empty Optional, get() throws NoSuchElementException: No value present, and the program stops - only for the " +
            "inputs that leave the stream empty.",
            "Say what to use when there is nothing: orElse(0), or ifPresent to do something only when there is a value. Or, when " +
            "the program cannot go on without one, orElseThrow with an exception that says why.",
            """
            import java.util.List;

            public class Main {
                public static void main(String[] args) {
                    List<Integer> marks = List.of(70, 40, 90);
                    int high = marks.stream().filter(mark -> mark > 95).findFirst().orElse(0);
                    System.out.println(high);
                }
            }
            """),

        GuideFor(["logic-csharp-for-closure"], "A lambda made in a for loop uses the loop's counter",
            "A for loop has one counter for all its passes. A lambda that uses it keeps that one variable, not its value when the lambda " +
            "was made - so lambdas run after the loop all see the value that ended it: three lambdas made with i at 0, 1 and 2 all " +
            "printed 3. (A foreach gives each pass its own variable, and has no such trouble.)",
            "Every lambda does the same thing, with the last value, and the compiler says nothing.",
            "Copy the counter into a variable declared inside the loop, and use that: var current = i;",
            """
            var actions = new List<Action>();
            for (int i = 0; i < 3; i++)
            {
                var current = i;
                actions.Add(() => Console.Write(current));
            }
            foreach (var action in actions) action();
            """),

        GuideFor(["logic-csharp-task-printed"], "An async method's result used without await",
            "An async method gives back a Task: a promise of its value, finished later. Called without await, what you get is that Task, " +
            "not the value - so printing it prints the Task's type name, and using it as a number does not compile.",
            "The program prints the Task's type where it meant the value, and the compiler says nothing when the Task is kept in a variable.",
            "Wait for the value with await: var count = await CountAsync();",
            """
            var count = await CountAsync();
            Console.WriteLine(count);

            static async Task<int> CountAsync()
            {
                await Task.Delay(1);
                return 3;
            }
            """),

        GuideFor(["logic-java-test-missing-annotation"], "A test method without @Test",
            "JUnit runs the methods marked @Test - and no others. A method that asserts, without @Test above it, is an ordinary method " +
            "nothing calls.",
            "Whatever it checks is never checked, so the tests pass while the code is wrong - JUnit found 3 tests of 8 - and javac says nothing.",
            "Mark it with @Test.",
            """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            class AdderTest {
                static int add(int first, int second) {
                    return first + second;
                }

                @Test
                void addsTwoNumbers() {
                    assertEquals(7, add(5, 2));
                }
            }
            """),

        GuideFor(["logic-java-test-not-run-shape"], "A @Test method JUnit cannot run",
            "JUnit 5 runs a @Test method only when it is not private, not static, and gives back nothing. One that is private or static, " +
            "or gives back a value, is passed over - and JUnit says nothing of it.",
            "The test never runs, so what it checks is never checked - JUnit found 3 tests of 8.",
            "Make it void, and neither private nor static: @Test void addsTwoNumbers().",
            """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            class AdderTest {
                static int add(int first, int second) {
                    return first + second;
                }

                @Test
                void addsTwoNumbers() {
                    assertEquals(7, add(5, 2));
                }
            }
            """),

        GuideFor(["logic-java-setup-not-annotated"], "setUp without @BeforeEach",
            "JUnit 5 calls a method before each test only when it is marked @BeforeEach - and after each when marked @AfterEach. A name " +
            "such as setUp, which JUnit 3 went by, means nothing to it: the method is never called.",
            "What it was meant to set up is never there, so the tests that use it stop with NullPointerException, which does not say why.",
            "Mark it with @BeforeEach - or @AfterEach for tearDown.",
            """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import java.util.ArrayList;
            import java.util.List;
            import org.junit.jupiter.api.BeforeEach;
            import org.junit.jupiter.api.Test;

            class ListTest {
                private List<Integer> numbers;

                @BeforeEach
                void setUp() {
                    numbers = new ArrayList<>();
                }

                @Test
                void addsOne() {
                    numbers.add(1);
                    assertEquals(1, numbers.size());
                }
            }
            """),

        GuideFor(["logic-java-assertion-swallowed"], "An assertion caught in a test",
            "A failed assertion throws AssertionError - AssertionFailedError in JUnit 5 - and that is how JUnit knows a test failed. Inside " +
            "a try whose catch takes AssertionError, Error or Throwable, the failure is caught, and the test carries on and passes.",
            "The test can never fail, so it passes whether the code is right or wrong.",
            "Take the assertion out of the try - or, if the try is there for something else, catch only that exception.",
            """
            import static org.junit.jupiter.api.Assertions.assertEquals;

            import org.junit.jupiter.api.Test;

            class AdderTest {
                static int add(int first, int second) {
                    return first + second;
                }

                @Test
                void addsTwoNumbers() {
                    assertEquals(7, add(5, 2));
                }
            }
            """),
    ];
}
