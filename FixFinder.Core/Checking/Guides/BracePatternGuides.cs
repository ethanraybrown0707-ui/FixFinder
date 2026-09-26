using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic patterns in Java, C#, JavaScript, C and C++, each explained three ways: for someone new to
/// programming, as it is usually taught, and in the languages' own terms.
/// </summary>
internal static class BracePatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        AtEveryLevel(["logic-self-assignment"], "A variable assigned to itself",
            "x = x; copies a value onto itself, which changes nothing. In a constructor it usually means the parameter has the " +
            "same name as a field, and the plain name means the parameter - so the field is never set. this.name says 'the " +
            "object's own name'.",
            "x = x; copies a value onto itself. In a constructor it usually means the parameter hides a field of the same name.",
            "Inside the constructor the parameter's name shadows the field, so both sides of x = x refer to the parameter and " +
            "the assignment does nothing; the field is reached through this.x, or this->x in C++, and otherwise is never set.",
            "The field the constructor was meant to set keeps its default - null, 0 or false - and later code uses that.",
            "Name the field with this. (this-> in C++): this.name = name;",
            """
            public Person(String name) {
                this.name = name;
            }
            """),

        AtEveryLevel(["logic-lost-increment"], "An increment that is undone",
            "count++ adds one to count, but the expression count++ itself has the old value. count = count++ adds one and then " +
            "puts the old value straight back, so count never changes.",
            "count = count++; stores the value from before the increment back into count.",
            "Postfix ++ evaluates to the operand's value before the increment. In Java, C# and JavaScript the right-hand side, " +
            "increment included, is evaluated first and the old value is then assigned, undoing it; in C, and in C++ before " +
            "C++17, the two unsequenced changes to count are undefined behaviour.",
            "The counter never changes, so loops and totals that depend on it go wrong.",
            "Write count++; on its own.",
            """
            count++;
            """),

        AtEveryLevel(["logic-off-by-one-length"], "A loop that runs one past the end",
            "Positions start at 0, so a list or array of five items has positions 0 to 4. A loop that keeps going while i <= 5 " +
            "makes one extra pass, and that pass asks for position 5, which does not exist.",
            "Positions run from 0 to size - 1, so a loop that runs while i <= size reads one position too many.",
            "Valid indexes are 0 to length - 1, so the condition i <= length lets i equal length on the last pass: Java and C# " +
            "then throw an index exception, JavaScript reads undefined, and C and C++ access out of bounds, which is undefined " +
            "behaviour.",
            "The last pass crashes in Java and C#, reads undefined in JavaScript, and reads other memory in C and C++.",
            "Loop while i < size.",
            """
            for (int i = 0; i < scores.length; i++) {
                total += scores[i];
            }
            """),

        AtEveryLevel(["logic-or-constant"], "A condition that is always true",
            "c == 'y' || 'Y' reads like 'c is y or Y', but the computer sees two questions: 'is c equal to y?', or ''Y''. The " +
            "second is just a value, and in C, C++ and JavaScript any value that is not zero counts as true - so the whole " +
            "condition is always true.",
            "c == 'y' || 'Y' compares c only with 'y'; 'Y' on its own is a non-zero value, which always counts as true.",
            "|| has lower precedence than ==, so the condition is (c == 'y') || 'Y', and a non-zero character constant converts " +
            "to true in C, C++ and JavaScript, making it true whatever c is. Java and C# reject a char operand of || when " +
            "compiling.",
            "The if always runs its block, whatever the value is.",
            "Compare with each value: c == 'y' || c == 'Y'.",
            """
            if (c == 'y' || c == 'Y') {
                confirm();
            }
            """),

        AtEveryLevel(["logic-duplicate-condition"], "A branch that can never be reached",
            "An if / else if chain checks from the top, runs the first branch whose condition is true, and skips the rest. This " +
            "else if checks exactly what an earlier branch checks, so whenever it would be true, the earlier branch has already run.",
            "An else if checks the same condition as an earlier branch of the same chain.",
            "The conditions of an else-if chain are evaluated in order and only the first true branch runs, so a later " +
            "condition identical to an earlier one can only be reached when that condition is false: its branch is dead code.",
            "When the condition is true the earlier branch runs, so the later one never does.",
            "Change the later condition to the case it was meant to catch.",
            """
            if (score >= 70) {
                grade = 'A';
            } else if (score >= 50) {
                grade = 'B';
            }
            """),

        AtEveryLevel(["logic-empty-catch"], "An error caught and ignored",
            "catch is where the program deals with an error. An empty catch deals with it by doing nothing, so the error " +
            "vanishes and the program carries on - usually with a wrong or missing value - and nobody finds out why.",
            "An empty catch block throws the error away.",
            "An empty catch block swallows the exception - its type, message and stack trace are discarded - and execution " +
            "carries on after the try statement in whatever state the failed code left; at the least it should be logged or " +
            "rethrown.",
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

        AtEveryLevel(["logic-modified-while-looping"], "A collection changed while looping over it",
            "A for-each loop walks through a list with a hidden helper that remembers where it is. Adding or removing items " +
            "while it walks would confuse it, so Java and C# check for that and stop the program with an error.",
            "A for-each loop checks that the collection has not changed since it started.",
            "The enumerators of Java's ArrayList and C#'s List<T> record a modification count, and the loop's next step throws " +
            "ConcurrentModificationException or InvalidOperationException after any structural change not made through the " +
            "iterator itself. removeIf and RemoveAll filter in place without a loop.",
            "Adding or removing an item inside the loop makes the next pass throw an exception.",
            "Loop over a copy, or remove items with removeIf / RemoveAll.",
            """
            names.removeIf(name -> name.isEmpty());
            """),

        AtEveryLevel(["logic-float-equality"], "Decimal numbers compared exactly",
            "Computers store fractions like 0.1 in binary, where most of them can only be stored approximately - the way 1/3 " +
            "can only be written as 0.333... in decimal. Adding them leaves a tiny error, so 0.1 + 0.2 is not exactly 0.3, and " +
            "an exact comparison says no.",
            "Most decimal fractions cannot be stored exactly, so 0.1 + 0.2 is 0.30000000000000004, not 0.3.",
            "A double is IEEE 754 binary64, in which 0.1, 0.2 and 0.3 cannot be represented: each is rounded to the nearest " +
            "binary fraction, and the rounding errors make 0.1 + 0.2 == 0.3 false. Compare within a tolerance suited to the " +
            "sizes involved, or use a decimal type for money.",
            "An exact comparison fails even when the numbers are equal for every practical purpose.",
            "Check that the difference is smaller than a tiny tolerance.",
            """
            if (Math.abs(total - 0.3) < 1e-9) {
                System.out.println("equal");
            }
            """),

        AtEveryLevel(["logic-java-scanner-skips-line"], "nextLine() straight after reading a number",
            "When someone types 42 and presses Enter, the input holds 42 followed by an invisible end of line. nextInt() takes " +
            "the 42 but leaves the end of line behind, so the next nextLine() finds it straight away and returns an empty line.",
            "nextInt, nextDouble and next leave the Enter typed after the value in the input.",
            "nextInt, nextDouble and next consume only the token and leave the line separator in the buffer, and nextLine " +
            "returns the rest of the current line up to that separator, which is empty. One extra nextLine() call, or reading " +
            "every line with nextLine() and parsing it, avoids it.",
            "The next nextLine() returns an empty string at once, so a question is skipped.",
            "Call nextLine() once after the number to use up the rest of its line.",
            """
            int age = in.nextInt();
            in.nextLine();
            String name = in.nextLine();
            """),

        AtEveryLevel(["logic-java-random-always-zero"], "A random number that is always 0",
            "Math.random() gives a number from 0 up to, but not including, 1 - such as 0.73. (int) turns a number into a whole " +
            "number by chopping off the fraction, and chopping first turns every result into 0 - and 0 times anything is " +
            "still 0.",
            "(int) Math.random() is cast before it is multiplied, and Math.random() is always less than 1.",
            "A cast binds more tightly than *, so (int) Math.random() * 6 is ((int) Math.random()) * 6, and Math.random() " +
            "returns a double in [0.0, 1.0), which truncates to 0. Brackets round the product make the cast apply last.",
            "The result is always 0 (or the smallest value), so the program is never random.",
            "Put brackets round the multiplication so the cast comes last.",
            """
            int roll = (int) (Math.random() * 6) + 1;
            """),

        AtEveryLevel(["logic-java-wrapper-equality"], "Integer objects compared with ==",
            "An Integer is a box around a number, and == asks whether two boxes are the very same box. Java keeps ready-made " +
            "boxes for small numbers and hands out the same one each time, so == seems to work - but bigger numbers each get a " +
            "box of their own, and == says they are different.",
            "Integer, Long, Double and the other wrappers are objects, so == compares whether they are the same object.",
            "== on Integer, Long and the other wrappers compares references. Autoboxing goes through Integer.valueOf, which " +
            "caches -128 to 127, so identity holds only within that range; equals, or unboxing to int, compares values.",
            "It works for small numbers, because Java shares them, and fails from 128 upwards.",
            "Compare with equals, or use int, long and double.",
            """
            if (a.equals(b)) {
                System.out.println("same");
            }
            """),

        AtEveryLevel(["logic-java-misspelt-override"], "A misspelt toString, equals or hashCode",
            "Java looks for toString, equals and hashCode by their exact names, with the same capitals and the same " +
            "parameters. A method called tostring or ToString is just a different method, which nothing ever calls.",
            "Java calls these methods only by their exact names, capitals included.",
            "A method overrides only when its name and parameter types match the superclass method exactly; a misspelt name " +
            "declares a new method, so Object's version still runs. @Override makes the compiler reject a method that " +
            "overrides nothing.",
            "The method is never used: printing the object still shows ClassName@1b6d3586.",
            "Spell the name exactly, and put @Override above it so the compiler checks.",
            """
            @Override
            public String toString() {
                return name + " (" + age + ")";
            }
            """),

        AtEveryLevel(["logic-string-built-in-loop"], "Text built with + inside a loop",
            "Text in Java and C# cannot be changed once made, so s += \"x\" builds a brand-new piece of text and copies " +
            "everything so far into it. Doing that on every pass means copying more and more, so a long loop gets slower and " +
            "slower.",
            "Strings never change, so each += makes a new string and copies all the text so far into it.",
            "Strings are immutable, so each concatenation allocates a new string and copies both operands - n appends copy " +
            "O(n^2) characters in all - while a StringBuilder appends into a growable buffer at an amortised constant cost " +
            "per character.",
            "For long loops the program slows down more and more as the text grows.",
            "Use StringBuilder (Java) or StringBuilder / string.Join (C#).",
            """
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < 5; i++) {
                line.append(i);
            }
            """),

        AtEveryLevel(["logic-csharp-async-void"], "An async void method",
            "An async method normally hands back a promise - a Task - that it will finish later, so the caller can wait for " +
            "it. An async void method hands back nothing, so the caller can neither wait for it nor catch its errors.",
            "Nothing can await an async void method, so its caller cannot tell when it has finished.",
            "An async void method returns no Task, so nothing can await it; an exception escaping it is raised directly on the " +
            "SynchronizationContext that was current when it started, or on the thread pool, where it usually ends the " +
            "process. async void is meant only for event handlers.",
            "An exception inside it cannot be caught by the caller and crashes the program.",
            "Return Task instead, and await it where it is called.",
            """
            static async Task SaveAsync()
            {
                await File.WriteAllTextAsync("data.txt", text);
            }
            """),

        AtEveryLevel(["logic-csharp-property-calls-itself"], "A property that calls itself",
            "A property looks like a variable but is really a pair of small methods, get and set. Inside them the property's " +
            "own name means the property again - so getting it gets it, which gets it, for ever, until the program crashes.",
            "Inside a property's getter or setter, its own name means the property again, not a stored value.",
            "Within a property's accessors its name refers to the property itself, so get => Age or set => Age = value " +
            "recurses without end until the stack overflows. The value has to live in a backing field - a private field, or " +
            "the hidden one the compiler makes for an auto-property.",
            "Getting or setting it calls itself without end, and the program crashes with a StackOverflowException.",
            "Use an auto-property, { get; set; }, or a separate private field.",
            """
            public int Age { get; set; }
            """),

        AtEveryLevel(["logic-csharp-parse-unchecked"], "Parsing input that might not be a number",
            "int.Parse turns text into a number only if the text is a number. If someone types 'twelve', or just presses " +
            "Enter, it stops the program with an error. TryParse tries, and tells you whether it worked instead of stopping.",
            "int.Parse throws a FormatException when the text is not a number.",
            "int.Parse throws FormatException for text that is not an integer, ArgumentNullException for null - which " +
            "Console.ReadLine returns at the end of the input - and OverflowException for a value outside int's range; " +
            "TryParse returns false instead of throwing.",
            "One mistyped answer crashes the whole program.",
            "Use int.TryParse, which reports whether it worked instead of crashing.",
            """
            if (!int.TryParse(Console.ReadLine(), out var age))
            {
                Console.WriteLine("Please type a whole number.");
            }
            """),

        AtEveryLevel(["logic-js-loose-equality"], "Comparing with ==",
            "In JavaScript, == tries to turn both sides into the same type before comparing, so the text \"1\" equals the " +
            "number 1, and \"\" equals 0. === compares without converting anything, so values of different types are never equal.",
            "== converts both sides to the same type first, so \"1\" == 1 and \"\" == 0 are true.",
            "== is the loose equality comparison: it converts its operands - strings and booleans to numbers, objects to " +
            "primitives - and treats null and undefined as equal only to each other. === compares type and value with no " +
            "conversion, NaN aside.",
            "Values of different types compare as equal, which hides mistakes.",
            "Use === and !==.",
            """
            if (count === 0) {
              console.log("empty");
            }
            """),

        AtEveryLevel(["logic-js-template-in-quotes"], "${...} inside ordinary quotes",
            "JavaScript only fills in ${name} with a value inside a template literal - text written between backticks, `like " +
            "this`, not ordinary quotes. Between ' or \" marks, ${name} is printed just as it was typed.",
            "Only a template literal, written with backticks, fills in ${...}.",
            "Substitution happens only in template literals, delimited by backticks; in a string literal in single or double " +
            "quotes, ${...} has no meaning and is kept as written.",
            "The output shows ${name} instead of the value.",
            "Write the string with backticks.",
            """
            console.log(`Hello ${name}`);
            """),

        AtEveryLevel(["logic-js-compare-with-new-array"], "Comparing with []",
            "[] makes a brand-new, empty array. Comparing arrays asks 'are these the very same array?', and a brand-new one is " +
            "never the same as any other - so the check is always false. To ask 'is it empty?', check the length.",
            "[] makes a new array, and arrays are compared by identity, so nothing is ever equal to it.",
            "Objects, arrays included, compare by reference under both == and ===, and the literal [] makes a fresh array, so " +
            "no array or object is ever equal to it. With ==, a string or number may still be converted and match - \"\" == [] " +
            "is true - which is no test of emptiness either.",
            "The comparison is always false, so the empty case is never handled.",
            "Check the array's length.",
            """
            if (items.length === 0) {
              console.log("nothing to show");
            }
            """),

        AtEveryLevel(["logic-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - the result of a calculation that failed, like 0 / 0. By the rules, NaN is not equal " +
            "to anything, not even itself, so x == NaN is false even when x is NaN.",
            "NaN is not equal to anything, not even another NaN.",
            "IEEE 754 makes NaN unordered, so every comparison with it is false except !=, which is true - NaN == NaN " +
            "included. Number.isNaN, Double.isNaN and double.IsNaN test for it directly.",
            "The comparison is always false, so a failed calculation is never noticed.",
            "Use Number.isNaN (JavaScript), Double.isNaN (Java) or double.IsNaN (C#).",
            """
            if (Number.isNaN(value)) {
              console.log("not a number");
            }
            """),

        AtEveryLevel(["logic-java-equals-overload"], "equals that takes the wrong type",
            "Java's collections look for a method called equals that takes any Object. A method equals(Point other) has a " +
            "different parameter, so Java treats it as a separate method - and the collections never call it.",
            "equals(Point other) is a new method beside Java's equals(Object), not a replacement for it.",
            "equals(Point) overloads Object.equals(Object) instead of overriding it, and overloads are chosen by static type, " +
            "so collections, which call equals(Object), get Object's identity comparison. @Override would reject the declaration.",
            "HashSet, HashMap and List.contains call equals(Object), so they never see yours and treat equal objects as different.",
            "Take an Object and check its type with instanceof.",
            """
            @Override
            public boolean equals(Object object) {
                if (!(object instanceof Point other)) return false;
                return x == other.x && y == other.y;
            }
            """),

        AtEveryLevel(["logic-java-chars-added"], "Two chars added together",
            "Each char is stored as a number - 'A' is 65 and 'B' is 66 - so adding two chars adds the numbers, giving 131. " +
            "Putting a String first makes + join text instead.",
            "A char is a number underneath, so 'A' + 'B' adds 65 and 66.",
            "Binary numeric promotion widens both char operands of + to int, so 'A' + 'B' is the int 131. + joins text only " +
            "when an operand is a String, and it works left to right, so \"\" + a + b joins them.",
            "The program prints 131 instead of AB.",
            "Start with a string so + joins text: \"\" + first + second.",
            """
            System.out.println("" + first + second);
            """),
    ];
}
