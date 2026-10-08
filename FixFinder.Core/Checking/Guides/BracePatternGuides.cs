using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic patterns in Java, C#, JavaScript, C and C++, each explained for someone new to programming.
/// </summary>
internal static class BracePatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-self-assignment"], "A variable assigned to itself",
            "x = x; copies a value onto itself, which changes nothing. In a constructor it usually means the parameter has the " +
            "same name as a field, and the plain name means the parameter - so the field is never set. this.name says 'the " +
            "object's own name'.",
            "The field the constructor was meant to set keeps its default - null, 0 or false - and later code uses that.",
            "Name the field with this. (this-> in C++): this.name = name;",
            """
            public Person(String name) {
                this.name = name;
            }
            """),

        GuideFor(["logic-lost-increment"], "An increment that is undone",
            "count++ adds one to count, but the expression count++ itself has the old value. count = count++ adds one and then " +
            "puts the old value straight back, so count never changes.",
            "The counter never changes, so loops and totals that depend on it go wrong.",
            "Write count++; on its own.",
            """
            count++;
            """),

        GuideFor(["logic-off-by-one-length"], "A loop that runs one past the end",
            "Positions start at 0, so a list or array of five items has positions 0 to 4. A loop that keeps going while i <= 5 " +
            "makes one extra pass, and that pass asks for position 5, which does not exist.",
            "The last pass crashes in Java and C#, reads undefined in JavaScript, and reads other memory in C and C++.",
            "Loop while i < size.",
            """
            for (int i = 0; i < scores.length; i++) {
                total += scores[i];
            }
            """),

        GuideFor(["logic-or-constant"], "A condition that is always true",
            "c == 'y' || 'Y' reads like 'c is y or Y', but the computer sees two questions: 'is c equal to y?', or ''Y''. The " +
            "second is just a value, and in C, C++ and JavaScript any value that is not zero counts as true - so the whole " +
            "condition is always true.",
            "The if always runs its block, whatever the value is.",
            "Compare with each value: c == 'y' || c == 'Y'.",
            """
            if (c == 'y' || c == 'Y') {
                confirm();
            }
            """),

        GuideFor(["logic-duplicate-condition"], "A branch that can never be reached",
            "An if / else if chain checks from the top, runs the first branch whose condition is true, and skips the rest. This " +
            "else if checks exactly what an earlier branch checks, so whenever it would be true, the earlier branch has already run.",
            "When the condition is true the earlier branch runs, so the later one never does.",
            "Change the later condition to the case it was meant to catch.",
            """
            if (score >= 70) {
                grade = 'A';
            } else if (score >= 50) {
                grade = 'B';
            }
            """),

        GuideFor(["logic-empty-catch"], "An error caught and ignored",
            "catch is where the program deals with an error. An empty catch deals with it by doing nothing, so the error " +
            "vanishes and the program carries on - usually with a wrong or missing value - and nobody finds out why.",
            "When something goes wrong the program carries on with wrong values, and nothing says why.",
            "Handle the error: report it, use a sensible default, or let it propagate.",
            """
            try {
                age = Integer.parseInt(text);
            } catch (NumberFormatException e) {
                System.out.println("Not a number: " + text);
                age = 0;
            }
            """),

        GuideFor(["logic-modified-while-looping"], "A collection changed while looping over it",
            "A for-each loop walks through a list with a hidden helper that remembers where it is. Adding or removing items " +
            "while it walks would confuse it, so Java and C# check for that and stop the program with an error.",
            "Adding or removing an item inside the loop makes the next pass throw an exception.",
            "Loop over a copy, or remove items with removeIf / RemoveAll.",
            """
            names.removeIf(name -> name.isEmpty());
            """),

        GuideFor(["logic-float-equality"], "Decimal numbers compared exactly",
            "Computers store fractions like 0.1 in binary, where most of them can only be stored approximately - the way 1/3 " +
            "can only be written as 0.333... in decimal. Adding them leaves a tiny error, so 0.1 + 0.2 is not exactly 0.3, and " +
            "an exact comparison says no.",
            "An exact comparison fails even when the numbers are equal for every practical purpose.",
            "Check that the difference is smaller than a tiny tolerance.",
            """
            if (Math.abs(total - 0.3) < 1e-9) {
                System.out.println("equal");
            }
            """),

        GuideFor(["logic-java-scanner-skips-line"], "nextLine() straight after reading a number",
            "When someone types 42 and presses Enter, the input holds 42 followed by an invisible end of line. nextInt() takes " +
            "the 42 but leaves the end of line behind, so the next nextLine() finds it straight away and returns an empty line.",
            "The next nextLine() returns an empty string at once, so a question is skipped.",
            "Call nextLine() once after the number to use up the rest of its line.",
            """
            int age = in.nextInt();
            in.nextLine();
            String name = in.nextLine();
            """),

        GuideFor(["logic-java-random-always-zero"], "A random number that is always 0",
            "Math.random() gives a number from 0 up to, but not including, 1 - such as 0.73. (int) turns a number into a whole " +
            "number by chopping off the fraction, and chopping first turns every result into 0 - and 0 times anything is " +
            "still 0.",
            "The result is always 0 (or the smallest value), so the program is never random.",
            "Put brackets round the multiplication so the cast comes last.",
            """
            int roll = (int) (Math.random() * 6) + 1;
            """),

        GuideFor(["logic-java-wrapper-equality"], "Integer objects compared with ==",
            "An Integer is a box around a number, and == asks whether two boxes are the very same box. Java keeps ready-made " +
            "boxes for small numbers and hands out the same one each time, so == seems to work - but bigger numbers each get a " +
            "box of their own, and == says they are different.",
            "It works for small numbers, because Java shares them, and fails from 128 upwards.",
            "Compare with equals, or use int, long and double.",
            """
            if (a.equals(b)) {
                System.out.println("same");
            }
            """),

        GuideFor(["logic-java-misspelt-override"], "A misspelt toString, equals or hashCode",
            "Java looks for toString, equals and hashCode by their exact names, with the same capitals and the same " +
            "parameters. A method called tostring or ToString is just a different method, which nothing ever calls.",
            "The method is never used: printing the object still shows ClassName@1b6d3586.",
            "Spell the name exactly, and put @Override above it so the compiler checks.",
            """
            @Override
            public String toString() {
                return name + " (" + age + ")";
            }
            """),

        GuideFor(["logic-string-built-in-loop"], "Text built with + inside a loop",
            "Text in Java and C# cannot be changed once made, so s += \"x\" builds a brand-new piece of text and copies " +
            "everything so far into it. Doing that on every pass means copying more and more, so a long loop gets slower and " +
            "slower.",
            "For long loops the program slows down more and more as the text grows.",
            "Use StringBuilder (Java) or StringBuilder / string.Join (C#).",
            """
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < 5; i++) {
                line.append(i);
            }
            """),

        GuideFor(["logic-csharp-async-void"], "An async void method",
            "An async method normally hands back a promise - a Task - that it will finish later, so the caller can wait for " +
            "it. An async void method hands back nothing, so the caller can neither wait for it nor catch its errors.",
            "An exception inside it cannot be caught by the caller and crashes the program.",
            "Return Task instead, and await it where it is called.",
            """
            static async Task SaveAsync()
            {
                await File.WriteAllTextAsync("data.txt", text);
            }
            """),

        GuideFor(["logic-csharp-property-calls-itself"], "A property that calls itself",
            "A property looks like a variable but is really a pair of small methods, get and set. Inside them the property's " +
            "own name means the property again - so getting it gets it, which gets it, for ever, until the program crashes.",
            "Getting or setting it calls itself without end, and the program crashes with a StackOverflowException.",
            "Use an auto-property, { get; set; }, or a separate private field.",
            """
            public int Age { get; set; }
            """),

        GuideFor(["logic-csharp-parse-unchecked"], "Parsing input that might not be a number",
            "int.Parse turns text into a number only if the text is a number. If someone types 'twelve', or just presses " +
            "Enter, it stops the program with an error. TryParse tries, and tells you whether it worked instead of stopping.",
            "One mistyped answer crashes the whole program.",
            "Use int.TryParse, which reports whether it worked instead of crashing.",
            """
            if (!int.TryParse(Console.ReadLine(), out var age))
            {
                Console.WriteLine("Please type a whole number.");
            }
            """),

        GuideFor(["logic-js-loose-equality"], "Comparing with ==",
            "In JavaScript, == tries to turn both sides into the same type before comparing, so the text \"1\" equals the " +
            "number 1, and \"\" equals 0. === compares without converting anything, so values of different types are never equal.",
            "Values of different types compare as equal, which hides mistakes.",
            "Use === and !==.",
            """
            if (count === 0) {
              console.log("empty");
            }
            """),

        GuideFor(["logic-js-template-in-quotes"], "${...} inside ordinary quotes",
            "JavaScript only fills in ${name} with a value inside a template literal - text written between backticks, `like " +
            "this`, not ordinary quotes. Between ' or \" marks, ${name} is printed just as it was typed.",
            "The output shows ${name} instead of the value.",
            "Write the string with backticks.",
            """
            console.log(`Hello ${name}`);
            """),

        GuideFor(["logic-js-compare-with-new-array"], "Comparing with []",
            "[] makes a brand-new, empty array. Comparing arrays asks 'are these the very same array?', and a brand-new one is " +
            "never the same as any other - so the check is always false. To ask 'is it empty?', check the length.",
            "The comparison is always false, so the empty case is never handled.",
            "Check the array's length.",
            """
            if (items.length === 0) {
              console.log("nothing to show");
            }
            """),

        GuideFor(["logic-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - the result of a calculation that failed, like 0 / 0. By the rules, NaN is not equal " +
            "to anything, not even itself, so x == NaN is false even when x is NaN.",
            "The comparison is always false, so a failed calculation is never noticed.",
            "Use Number.isNaN (JavaScript), Double.isNaN (Java) or double.IsNaN (C#).",
            """
            if (Number.isNaN(value)) {
              console.log("not a number");
            }
            """),

        GuideFor(["logic-java-equals-overload"], "equals that takes the wrong type",
            "Java's collections look for a method called equals that takes any Object. A method equals(Point other) has a " +
            "different parameter, so Java treats it as a separate method - and the collections never call it.",
            "HashSet, HashMap and List.contains call equals(Object), so they never see yours and treat equal objects as different.",
            "Take an Object and check its type with instanceof.",
            """
            @Override
            public boolean equals(Object object) {
                if (!(object instanceof Point other)) return false;
                return x == other.x && y == other.y;
            }
            """),

        GuideFor(["logic-java-chars-added"], "Two chars added together",
            "Each char is stored as a number - 'A' is 65 and 'B' is 66 - so adding two chars adds the numbers, giving 131. " +
            "Putting a String first makes + join text instead.",
            "The program prints 131 instead of AB.",
            "Start with a string so + joins text: \"\" + first + second.",
            """
            System.out.println("" + first + second);
            """),
    ];
}
