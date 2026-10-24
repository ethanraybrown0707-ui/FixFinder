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
            "A comparison with == is always false, and with != always true, so a failed calculation is never noticed.",
            "Use Number.isNaN (JavaScript), Double.isNaN (Java) or double.IsNaN (C#).",
            """
            if (Number.isNaN(value)) {
              console.log("not a number");
            }
            """),

        GuideFor(["logic-c-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so value == NAN is false even when value is NaN. gcc says " +
            "nothing.",
            "A comparison with == is always false, and with != always true, so a value that is NaN is never noticed.",
            "Test with isnan(value), from math.h.",
            """
            #include <math.h>
            #include <stdio.h>

            int main(void) {
                double value = NAN;
                if (isnan(value)) {
                    printf("not a number\n");
                }
                return 0;
            }
            """),

        GuideFor(["logic-cpp-nan-comparison"], "Comparing with NaN",
            "NaN means 'not a number' - a value a calculation gives when it has no answer as a number. By the rules, " +
            "NaN is not equal to anything, not even itself, so value == NAN is false even when value is NaN. g++ says " +
            "nothing.",
            "A comparison with == is always false, and with != always true, so a value that is NaN is never noticed.",
            "Test with std::isnan(value), from cmath.",
            """
            #include <cmath>
            #include <iostream>

            int main() {
                double value = NAN;
                if (std::isnan(value)) {
                    std::cout << "not a number\n";
                }
            }
            """),

        GuideFor(["logic-cpp-array-printed"], "An array sent to cout",
            "cout writes a number, a string or a char array as text - but an array of numbers sent to it with << becomes the address " +
            "of its first item, and cout writes that address, such as 0x1a535ffcf8. g++ says nothing.",
            "The output shows an address instead of the items - a different one from run to run - which looks broken to anyone using " +
            "the program.",
            "Write each item in a loop: for (int mark : marks) std::cout << mark << \" \";",
            """
            #include <iostream>

            int main() {
                int marks[] = {70, 80, 90};
                std::cout << "Marks: ";
                for (int mark : marks) std::cout << mark << " ";
                std::cout << "\n";
            }
            """),

        GuideFor(["logic-cast-divided-as-whole-number"], "Rounding by hand that loses the places",
            "(int)(price * 100 + 0.5) rounds a price to a whole number of hundredths - 1235 for 12.345 - and dividing that by 100, " +
            "a whole number too, is whole-number division, which cuts the fraction off again: the result is 12. C, C++, Java and C# " +
            "all do the same, and none of their compilers says anything.",
            "The places the rounding was meant to keep are lost: 12.345 rounded to two places comes out as 12.",
            "Divide by 100.0 - a double - so the division keeps its fraction: (int)(price * 100 + 0.5) / 100.0 is 12.35.",
            """
            #include <stdio.h>

            int main(void) {
                double price = 12.345;
                double rounded = (int)(price * 100 + 0.5) / 100.0;
                printf("%.2f\n", rounded);
                return 0;
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

        GuideFor(["logic-js-search-as-condition"], "A search's position used as true or false",
            "indexOf gives a position: 0 when what it looks for is at the very start, and -1 when it is not there at all. As a " +
            "condition, 0 is false and -1 is true - so if (marks.indexOf(70)) is false when 70 is the first mark, and " +
            "if (marks.indexOf(99)) is true when 99 is not there. Node says nothing.",
            "The condition is wrong both ways: a match at the start counts as none, and no match counts as one.",
            "Ask whether it is there: marks.includes(70) is true wherever 70 is, the start too, and false only when it is not there.",
            """
            const marks = [70, 80];
            if (marks.includes(70)) {
              console.log("found 70");
            }
            """),

        GuideFor(["logic-index-of-above-zero"], "A match at the start taken for no match",
            "indexOf gives the position where what it looks for starts, counting from 0, and -1 when it is not there at all. A " +
            "match at the very start is at position 0, so indexOf(...) > 0 is false for it - as if it were not there. The same " +
            "holds for C#'s IndexOf and JavaScript's findIndex and search.",
            "A list or text that starts with what is looked for is treated as not containing it.",
            "Compare with >= 0, which is false only for -1 - or ask with contains, Contains or includes.",
            """
            if (text.indexOf("a") >= 0) {
                System.out.println("found");
            }
            """),

        GuideFor(["logic-caret-as-power"], "^ used as a power",
            "In maths side^2 means side squared, but in C, C++, Java, C# and JavaScript ^ is exclusive or: it compares two whole numbers bit by bit " +
            "and keeps the bits that differ. 5 is 101 in binary and 2 is 010, so 5 ^ 2 is 111 - which is 7, not 25.",
            "The program runs and works out a number - just the wrong one - with no error or warning.",
            "Multiply the number by itself: side * side, or side * side * side for a cube. Java and C# also have Math.pow and " +
            "Math.Pow, and JavaScript has **: side ** 2.",
            """
            int area = side * side;
            """),

        GuideFor(["logic-js-power-of-one-over"], "A root written as a power of 1 over a number",
            "In maths the square root of side can be written as side to the power 1/2. In JavaScript ** is worked out before /, so " +
            "side ** 1/2 is side ** 1 - side itself - divided by 2. With 16 it is 8, not 4.",
            "The program works out half of the number - or a third, for 1/3 - instead of its root, and node --check says nothing of it.",
            "Raise it to the fraction itself: side ** 0.5, or side ** (1 / 3) for a cube root, with the fraction in brackets - or use " +
            "Math.sqrt(side) and Math.cbrt(side).",
            """
            const side = 16;
            console.log(side ** 0.5);
            """),

        GuideFor(["logic-c-fgets-newline"], "A line read with its newline still in it",
            "fgets keeps the newline from pressing Enter at the end of the text it reads: typing yes leaves \"yes\\n\" in the buffer. " +
            "strcmp compares every character, the newline too, so it never finds the buffer equal to \"yes\".",
            "The comparison is false whatever was typed, so the program takes the other branch every time.",
            "Cut the text at its newline after reading it: answer[strcspn(answer, \"\\n\")] = '\\0'; - strcspn, from <string.h>, finds where it is.",
            """
            fgets(answer, sizeof answer, stdin);
            answer[strcspn(answer, "\n")] = '\0';
            if (strcmp(answer, "yes") == 0) {
                printf("yes\n");
            }
            """),

        GuideFor(["logic-cpp-getline-after-cin"], "getline after cin >> reads nothing",
            "std::cin >> age reads the number and stops just before the newline typed after it, which is left waiting. The getline " +
            "after it reads up to the next newline - and that is the one already waiting, so it reads the rest of the number's line: " +
            "nothing. g++ says nothing.",
            "The line read is always empty: with 21 and then Ada typed, name is empty and Ada is never read.",
            "Skip the newline first: std::getline(std::cin >> std::ws, name) reads past the newline - and any spaces - before the line.",
            """
            #include <iostream>
            #include <string>

            int main() {
                int age;
                std::string name;
                std::cin >> age;
                std::getline(std::cin >> std::ws, name);
                std::cout << "Name: [" << name << "] age " << age << "\n";
            }
            """),

        GuideFor(["logic-c-fgets-after-scanf"], "fgets after scanf reads nothing",
            "scanf(\"%d\", &age) reads the number and stops just before the newline typed after it, which is left waiting. The fgets " +
            "after it reads up to the next newline - and that is the one already waiting, so it reads the rest of the number's line: " +
            "the newline alone. gcc says nothing.",
            "The line read holds nothing that was typed: with 21 and then Ada typed, name is empty and Ada is never read.",
            "Read the rest of the number's line before fgets, up to and with its newline - stopping at the end of the input too, so it " +
            "never waits: for (int rest = getchar(); rest != '\\n' && rest != EOF; rest = getchar()) { }",
            """
            #include <stdio.h>
            #include <string.h>

            int main(void) {
                int age;
                char name[50];
                scanf("%d", &age);
                for (int rest = getchar(); rest != '\n' && rest != EOF; rest = getchar()) { }
                fgets(name, sizeof name, stdin);
                name[strcspn(name, "\n")] = 0;
                printf("Name: [%s] age %d\n", name, age);
                return 0;
            }
            """),

        GuideFor(["logic-java-nextline-after-nextint"], "nextLine after nextInt reads nothing",
            "nextInt reads the number and stops - before the newline from pressing Enter after it. The nextLine that follows reads to the " +
            "end of that line: it finds the newline straight away, and gives back empty text, without waiting for the next line typed.",
            "The text read is empty, so whatever was typed on the next line is never read where it was meant to be.",
            "Read the rest of the number's line with a nextLine of its own before reading the next line.",
            """
            int age = scanner.nextInt();
            scanner.nextLine();
            String name = scanner.nextLine();
            """),

        GuideFor(["logic-numbers-joined-as-text"], "Numbers joined to text instead of added",
            "+ adds two numbers, and joins text - and it works from left to right. In \"Total: \" + first + second, the text is joined " +
            "to first, which makes more text, and that text is then joined to second: with first 70 and second 80 it is Total: 7080. " +
            "Java, C# and JavaScript all do the same, and none of them warns.",
            "The two numbers are written side by side rather than added - and \"Next: \" + count + 1 writes 31 for a count of 3.",
            "Put the numbers that are to be added in brackets - \"Total: \" + (first + second) - so they are added before their sum " +
            "joins the text.",
            """
            public class Main {
                public static void main(String[] args) {
                    int first = 70;
                    int second = 80;
                    System.out.println("Total: " + (first + second));
                }
            }
            """),

        GuideFor(["logic-js-foreach-for-map"], "forEach where map was meant",
            "forEach runs its function for every item, for what the function does - printing, say - and then gives back undefined. It " +
            "keeps nothing the function works out. map gives back a new array of what the function worked out for each item.",
            "The name is given undefined instead of an array, with no error, so printing it shows undefined, and using it as an array " +
            "stops the program with a TypeError.",
            "Use map when the function works out a new value for each item: const doubled = marks.map((mark) => mark * 2);.",
            """
            const marks = [70, 80];
            const doubled = marks.map((mark) => mark * 2);
            console.log(doubled);
            """),

        GuideFor(["logic-pow-whole-number-fraction"], "A power of 1/2 that is 0",
            "In Java, C#, C and C++, / of two whole numbers gives a whole number, so 1/2 is 0. Math.pow(side, 1/2) is therefore " +
            "side to the power 0, which is 1 whatever side is - not its square root.",
            "The program works out 1 instead of the root, with no error or warning.",
            "Write the top of the fraction as a decimal, so the division keeps its fraction: Math.pow(side, 1.0 / 2) - or use " +
            "Math.sqrt(side).",
            """
            double root = Math.sqrt(side);
            """),

        GuideFor(["logic-java-compare-to-one"], "compareTo compared with 1 or -1",
            "compareTo gives back a negative number when the first comes before the second, zero when they are equal, and a " +
            "positive number when it comes after - but not only -1 and 1. \"apple\".compareTo(\"cherry\") is -2, as a and c are two " +
            "letters apart, so == -1 is false although apple does come first.",
            "The comparison is false for most pairs that do come first or after, with no error or warning.",
            "Ask for the sign alone: < 0 for before, > 0 for after.",
            """
            public class Main {
                public static void main(String[] args) {
                    System.out.println("apple".compareTo("cherry") < 0);
                }
            }
            """),

        GuideFor(["logic-java-array-equals"], "Two arrays compared with equals or ==",
            "An array's equals is the same as ==: both ask whether two names are the very same array, not whether two arrays " +
            "hold the same items. Two arrays made separately are never the same array, so they are never equal, even with the " +
            "same items in the same order.",
            "The comparison is false for arrays that hold the same items, with no error or warning.",
            "Compare the items with Arrays.equals(first, second), from java.util.Arrays.",
            """
            import java.util.Arrays;

            public class Main {
                public static void main(String[] args) {
                    int[] first = {70, 80};
                    int[] second = {70, 80};
                    System.out.println(Arrays.equals(first, second));
                }
            }
            """),

        GuideFor(["logic-octal-literal"], "A number written with a 0 in front",
            "In Java, C and C++ a whole number written with 0 in front is read in octal - base 8, where each place is worth 8 " +
            "times the one after it - so 010 is 8, and 0123 is 83. It is meant for file modes such as 0755, and rarely anything " +
            "else.",
            "The number is not the one written, with no error or warning - often in codes or times written with a leading 0.",
            "Write the number without the 0 in front: 10.",
            """
            int code = 10;
            """),

        GuideFor(["logic-java-boolean-assigned-in-condition"], "= in a condition on a boolean",
            "= gives a variable a value, and == compares. Java refuses = in a condition for most variables, but a boolean's value is " +
            "true or false, which a condition can use - so if (done = true) gives done the value true, and the condition is that " +
            "value: always true.",
            "The if always runs - or never, with = false - and the variable is changed as a side effect, with no error or warning.",
            "Ask for the boolean itself: if (done), or if (!done) for false.",
            """
            public class Main {
                public static void main(String[] args) {
                    boolean done = false;
                    if (done) {
                        System.out.println("done");
                    }
                }
            }
            """),

        GuideFor(["logic-percent-divided-first"], "A percentage that divides first",
            "In C, C++, Java and C#, / of two whole numbers gives a whole number - the part below 1 is thrown away. In " +
            "score / total * 100 the division comes first, so for 45 out of 60 it is 0, and 0 * 100 is 0.",
            "Every score below the total comes out as 0%, with no error or warning.",
            "Multiply first - score * 100 / total - so only the last division drops a fraction: 45 * 100 / 60 is 75.",
            """
            int percent = score * 100 / total;
            """),

        GuideFor(["logic-js-math-max-of-array"], "Math.max given an array",
            "Math.max and Math.min take numbers one by one - Math.max(70, 85, 90). Given an array, they turn it into one number from " +
            "its text, and the text of [70, 85, 90] is 70,85,90, which is no number: the answer is NaN. Node says nothing - and an " +
            "array of one item happens to work, which hides the mistake until there are more.",
            "The highest or lowest mark comes out as NaN, and every sum or comparison made with it is NaN or false too.",
            "Spread the array into its items with ...: Math.max(...marks) is 90.",
            """
            const marks = [70, 85, 90];
            console.log(Math.max(...marks));
            """),

        GuideFor(["logic-js-rows-shared"], "A grid whose rows are one row",
            "new Array(3).fill(new Array(3).fill(0)) makes one row and fills the grid with that one row three times. " +
            "Every row is the same array, so grid[0][0] = 1 changes the first item of every row. Node says nothing.",
            "A change to one row shows in all of them: after grid[0][0] = 1, the grid is [[1,0,0],[1,0,0],[1,0,0]].",
            "Make each row its own array: Array.from({ length: 3 }, () => new Array(3).fill(0)) makes a new row for " +
            "each.",
            """
            const grid = Array.from({ length: 3 }, () => new Array(3).fill(0));
            grid[0][0] = 1;
            console.log(JSON.stringify(grid));
            """),

        GuideFor(["logic-js-array-shared"], "A second name for an array is not a copy",
            "const backup = marks does not copy the array: it gives the same array a second name. A change made through either name " +
            "- marks.push(100) - is a change to the one array, so backup shows it too. Node says nothing.",
            "The second name does not keep the values the array had: after marks.push(100), backup prints [ 70, 80, 90, 100 ].",
            "Make a copy: const backup = [...marks]. The copy is a new array with the same items, and a change to one no longer " +
            "shows in the other.",
            """
            const marks = [70, 80, 90];
            const backup = [...marks];
            marks.push(100);
            console.log(backup);
            """),

        GuideFor(["logic-js-typeof-never-gives"], "typeof compared with a word it never gives",
            "typeof gives one of eight words: \"undefined\", \"object\", \"boolean\", \"number\", \"bigint\", \"string\", " +
            "\"symbol\" and \"function\". An array is an object, so typeof gives \"object\" for it - and for null - and \"number\" " +
            "for every number, whole or not. A comparison with any other word, such as \"array\", \"integer\" or \"String\", can " +
            "never be true, and Node says nothing.",
            "The condition is always false - or always true with !== - so the same branch runs whatever the value is.",
            "Ask what was meant: Array.isArray(marks) for an array, Number.isInteger(count) for a whole number, value === null for " +
            "null, and typeof's own words, all in small letters, for the rest.",
            """
            const marks = [70, 80];
            if (Array.isArray(marks)) {
              console.log("an array");
            }
            """),

        GuideFor(["logic-js-for-in-over-array"], "for...in over an array gives positions",
            "for...in goes over the names of an object's properties - and an array's are its positions, given as text: \"0\", " +
            "\"1\", \"2\". So for (const mark in marks) makes mark a position written as text, not the item there. for...of is " +
            "the loop that gives the items.",
            "The loop works with positions where it means items, with no error - and as the positions are text, + joins them: " +
            "adding up 70, 80 and 90 with total += mark comes out as 0012.",
            "Go over the items with for...of: for (const mark of marks). A loop that uses the position to read the item at it - " +
            "marks[mark] - works, and FixFinder leaves it alone.",
            """
            const marks = [70, 80, 90];
            let total = 0;
            for (const mark of marks) {
              total += mark;
            }
            console.log("Total: " + total);
            """),

        GuideFor(["logic-js-loop-copy-changed"], "A loop that changes its own copy",
            "for (let mark of marks) gives each item of marks to mark, a variable of the loop's own. Giving mark a new value changes that " +
            "variable, not the item in the array - so after the loop, marks is just as it was.",
            "The array is unchanged, with no error, so whatever reads it next works with the old values.",
            "Go over the positions and change the item at each: marks[index] += 5 - or make a new array with map.",
            """
            for (let index = 0; index < marks.length; index++) {
              marks[index] += 5;
            }
            """),
    ];
}
