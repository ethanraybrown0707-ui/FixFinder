using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>Guides for the logic patterns in Java, C#, JavaScript, C and C++.</summary>
internal static class BracePatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Pattern(["logic-self-assignment"], "A variable assigned to itself",
            "x = x; copies a value onto itself. In a constructor it usually means the parameter hides a field of the same name.",
            "The field the constructor was meant to set keeps its default - null, 0 or false - and later code uses that.",
            "Name the field with this. (this-> in C++): this.name = name;",
            """
            public Person(String name) {
                this.name = name;
            }
            """),

        Pattern(["logic-lost-increment"], "An increment that is undone",
            "count = count++; stores the value from before the increment back into count.",
            "The counter never changes, so loops and totals that depend on it go wrong.",
            "Write count++; on its own.",
            """
            count++;
            """),

        Pattern(["logic-off-by-one-length"], "A loop that runs one past the end",
            "Positions run from 0 to size - 1, so a loop that runs while i <= size reads one position too many.",
            "The last pass crashes in Java and C#, reads undefined in JavaScript, and reads other memory in C and C++.",
            "Loop while i < size.",
            """
            for (int i = 0; i < scores.length; i++) {
                total += scores[i];
            }
            """),

        Pattern(["logic-or-constant"], "A condition that is always true",
            "c == 'y' || 'Y' compares c only with 'y'; 'Y' on its own is a non-zero value, which always counts as true.",
            "The if always runs its block, whatever the value is.",
            "Compare with each value: c == 'y' || c == 'Y'.",
            """
            if (c == 'y' || c == 'Y') {
                confirm();
            }
            """),

        Pattern(["logic-duplicate-condition"], "A branch that can never be reached",
            "An else if checks the same condition as an earlier branch of the same chain.",
            "When the condition is true the earlier branch runs, so the later one never does.",
            "Change the later condition to the case it was meant to catch.",
            """
            if (score >= 70) {
                grade = 'A';
            } else if (score >= 50) {
                grade = 'B';
            }
            """),

        Pattern(["logic-empty-catch"], "An error caught and ignored",
            "An empty catch block throws the error away.",
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

        Pattern(["logic-modified-while-looping"], "A collection changed while looping over it",
            "A for-each loop checks that the collection has not changed since it started.",
            "Adding or removing an item inside the loop makes the next pass throw an exception.",
            "Loop over a copy, or remove items with removeIf / RemoveAll.",
            """
            names.removeIf(name -> name.isEmpty());
            """),

        Pattern(["logic-float-equality"], "Decimal numbers compared exactly",
            "Most decimal fractions cannot be stored exactly, so 0.1 + 0.2 is 0.30000000000000004, not 0.3.",
            "An exact comparison fails even when the numbers are equal for every practical purpose.",
            "Check that the difference is smaller than a tiny tolerance.",
            """
            if (Math.abs(total - 0.3) < 1e-9) {
                System.out.println("equal");
            }
            """),

        Pattern(["logic-java-scanner-skips-line"], "nextLine() straight after reading a number",
            "nextInt, nextDouble and next leave the Enter typed after the value in the input.",
            "The next nextLine() returns an empty string at once, so a question is skipped.",
            "Call nextLine() once after the number to use up the rest of its line.",
            """
            int age = in.nextInt();
            in.nextLine();
            String name = in.nextLine();
            """),

        Pattern(["logic-java-random-always-zero"], "A random number that is always 0",
            "(int) Math.random() is cast before it is multiplied, and Math.random() is always less than 1.",
            "The result is always 0 (or the smallest value), so the program is never random.",
            "Put brackets round the multiplication so the cast comes last.",
            """
            int roll = (int) (Math.random() * 6) + 1;
            """),

        Pattern(["logic-java-wrapper-equality"], "Integer objects compared with ==",
            "Integer, Long, Double and the other wrappers are objects, so == compares whether they are the same object.",
            "It works for small numbers, because Java shares them, and fails from 128 upwards.",
            "Compare with equals, or use int, long and double.",
            """
            if (a.equals(b)) {
                System.out.println("same");
            }
            """),

        Pattern(["logic-java-misspelt-override"], "A misspelt toString, equals or hashCode",
            "Java calls these methods only by their exact names, capitals included.",
            "The method is never used: printing the object still shows ClassName@1b6d3586.",
            "Spell the name exactly, and put @Override above it so the compiler checks.",
            """
            @Override
            public String toString() {
                return name + " (" + age + ")";
            }
            """),

        Pattern(["logic-string-built-in-loop"], "Text built with + inside a loop",
            "Strings never change, so each += makes a new string and copies all the text so far into it.",
            "For long loops the program slows down more and more as the text grows.",
            "Use StringBuilder (Java) or StringBuilder / string.Join (C#).",
            """
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < 5; i++) {
                line.append(i);
            }
            """),

        Pattern(["logic-csharp-async-void"], "An async void method",
            "Nothing can await an async void method, so its caller cannot tell when it has finished.",
            "An exception inside it cannot be caught by the caller and crashes the program.",
            "Return Task instead, and await it where it is called.",
            """
            static async Task SaveAsync()
            {
                await File.WriteAllTextAsync("data.txt", text);
            }
            """),

        Pattern(["logic-csharp-property-calls-itself"], "A property that calls itself",
            "Inside a property's getter or setter, its own name means the property again, not a stored value.",
            "Getting or setting it calls itself without end, and the program crashes with a StackOverflowException.",
            "Use an auto-property, { get; set; }, or a separate private field.",
            """
            public int Age { get; set; }
            """),

        Pattern(["logic-csharp-parse-unchecked"], "Parsing input that might not be a number",
            "int.Parse throws a FormatException when the text is not a number.",
            "One mistyped answer crashes the whole program.",
            "Use int.TryParse, which reports whether it worked instead of crashing.",
            """
            if (!int.TryParse(Console.ReadLine(), out var age))
            {
                Console.WriteLine("Please type a whole number.");
            }
            """),

        Pattern(["logic-js-loose-equality"], "Comparing with ==",
            "== converts both sides to the same type first, so \"1\" == 1 and \"\" == 0 are true.",
            "Values of different types compare as equal, which hides mistakes.",
            "Use === and !==.",
            """
            if (count === 0) {
              console.log("empty");
            }
            """),

        Pattern(["logic-js-template-in-quotes"], "${...} inside ordinary quotes",
            "Only a template literal, written with backticks, fills in ${...}.",
            "The output shows ${name} instead of the value.",
            "Write the string with backticks.",
            """
            console.log(`Hello ${name}`);
            """),

        Pattern(["logic-js-compare-with-new-array"], "Comparing with []",
            "[] makes a new array, and arrays are compared by identity, so nothing is ever equal to it.",
            "The comparison is always false, so the empty case is never handled.",
            "Check the array's length.",
            """
            if (items.length === 0) {
              console.log("nothing to show");
            }
            """),

        Pattern(["logic-nan-comparison"], "Comparing with NaN",
            "NaN is not equal to anything, not even another NaN.",
            "The comparison is always false, so a failed calculation is never noticed.",
            "Use Number.isNaN (JavaScript), Double.isNaN (Java) or double.IsNaN (C#).",
            """
            if (Number.isNaN(value)) {
              console.log("not a number");
            }
            """),

        Pattern(["logic-java-equals-overload"], "equals that takes the wrong type",
            "equals(Point other) is a new method beside Java's equals(Object), not a replacement for it.",
            "HashSet, HashMap and List.contains call equals(Object), so they never see yours and treat equal objects as different.",
            "Take an Object and check its type with instanceof.",
            """
            @Override
            public boolean equals(Object object) {
                if (!(object instanceof Point other)) return false;
                return x == other.x && y == other.y;
            }
            """),

        Pattern(["logic-java-chars-added"], "Two chars added together",
            "A char is a number underneath, so 'A' + 'B' adds 65 and 66.",
            "The program prints 131 instead of AB.",
            "Start with a string so + joins text: \"\" + first + second.",
            """
            System.out.println("" + first + second);
            """),
    ];
}
