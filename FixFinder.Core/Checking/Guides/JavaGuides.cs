using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// javac's errors and lint warnings and the JVM's exceptions, each explained for someone new to programming.
/// </summary>
internal static class JavaGuides
{
    private const string NothingRuns =
        "javac refuses to build the program while this is wrong, so no part of it runs - not even the lines before this one.";

    private static GuideEntry Compile(
        string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        ExceptionTypes = ["compile error"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry Lint(string category, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["compile warning"],
        Message = new Regex($@"^\[{category}\]"),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private static GuideEntry Thrown(
        string type, string explanation, string why, string fix, string example, string? pattern = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Compile(@"^cannot find symbol",
            "Every name in Java - a variable, a method, a class - has to be declared before it is used, and can only be used " +
            "inside the braces it was declared in. javac cannot find a declaration of this name that this line can see. Java " +
            "also treats capitals as different letters, so Count and count are two different names.",
            "Check the spelling and capitals against the declaration, declare the variable where this line can see it, or add the import.",
            """
            import java.util.ArrayList;

            ArrayList<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Compile(@"^';' expected",
            "Java needs a semicolon at the end of every statement, the way a sentence needs a full stop, and this one has " +
            "none. javac often notices only when it reaches the next line, so look at the end of the line before the one it " +
            "points at.",
            "Put a semicolon at the end of the statement - usually the line before the one javac points at.",
            """
            int total = 0;
            total += price;
            """),

        Compile(@"^'?[)\]}(\[{]'? expected|^<identifier> expected|^illegal start of|^class, interface, enum, or record expected|^reached end of file while parsing|^unclosed|^not a statement|expected$",
            "javac reads code in a fixed shape: a class holds methods, and a method holds statements between braces. " +
            "Something here breaks that shape - a brace or bracket is missing or extra, a statement is outside every method, " +
            "or a word is in a place where it does not belong.",
            "Match every opening brace and bracket with a closing one, and make sure statements are inside a method.",
            """
            public class Main {
                public static void main(String[] args) {
                    System.out.println("Hello");
                }
            }
            """),

        Compile(@"^incompatible types: possible lossy conversion",
            "Each type of number has a size: an int holds whole numbers, a double numbers with fractions, a long bigger whole " +
            "numbers. Putting a double or a long into an int could lose the fraction or the top digits, so Java makes you " +
            "say that is what you want.",
            "Cast it if losing the fraction is what you want, or store it in the larger type.",
            """
            double average = total / 3.0;
            int rounded = (int) Math.round(average);
            """),

        Compile(@"^incompatible types",
            "Java checks that every value goes into a place made for its kind: text into a String, whole numbers into an int. " +
            "This line puts one kind of value where another kind is expected, and Java will not convert between them on its " +
            "own - text is never turned into a number by itself.",
            "Convert the value - Integer.parseInt(text), String.valueOf(number) - or change the declared type to match.",
            """
            String text = "42";
            int number = Integer.parseInt(text);
            """),

        Compile(@"^missing return statement",
            "A method that says it gives back a value - an int, a String - has to give one back on every way through it. " +
            "javac found a way to reach the end of the method, past all the ifs and loops, without a return.",
            "Add a return after the if/else or loop, for the case none of the branches handled.",
            """
            static String grade(int score) {
                if (score >= 50) {
                    return "pass";
                }
                return "fail";
            }
            """),

        Compile(@"might not have been initialized",
            "A variable declared inside a method starts with no value at all, and Java will not let you read it until " +
            "something has been stored in it. On at least one way through the code - past an if, or around a loop that may " +
            "not run - this variable is read before that happens.",
            "Give the variable a starting value where it is declared, or make sure every branch assigns it.",
            """
            int largest = numbers[0];
            for (int n : numbers) {
                if (n > largest) largest = n;
            }
            """),

        Compile(@"^unreported exception",
            "Some methods, like one that reads a file, can fail in ways the program should be ready for. Java insists you say " +
            "what happens then: catch the problem with try/catch, or declare with throws that your method hands it on to " +
            "whoever called it.",
            "Wrap the call in try/catch, or add throws to the method's declaration.",
            """
            try {
                String text = Files.readString(Path.of("data.txt"));
            } catch (IOException e) {
                System.out.println("Could not read the file: " + e.getMessage());
            }
            """),

        Compile(@"cannot be referenced from a static context",
            "main is static, which means it belongs to the class itself rather than to any one object made from it. Fields " +
            "and methods without static belong to an object, so main can only use them through an object it creates with new.",
            "Make the field or method static, or create an object and call it through that.",
            """
            public static void main(String[] args) {
                Main app = new Main();
                app.run();
            }
            """),

        Compile(@"is public, should be declared in a file named",
            "Java keeps each public class in its own file, and the file's name has to be the class's name exactly - Main in " +
            "Main.java - with the same capitals.",
            "Rename the file to match the class, or rename the class to match the file.",
            """
            // in Main.java
            public class Main {
            }
            """),

        Compile(@"^'else' without 'if'",
            "else has to come straight after the block of an if. Something has come between them - most often a semicolon " +
            "right after if (...), which ends the if at once, or a closing brace in the wrong place.",
            "Remove the semicolon after the if's condition, and put braces around the if's block.",
            """
            if (age >= 18) {
                System.out.println("adult");
            } else {
                System.out.println("child");
            }
            """),

        Compile(@"^unreachable statement",
            "return, break, continue and throw all leave the current block straight away, so a line written after one of them " +
            "in the same block can never be reached - and Java treats that as a mistake.",
            "Move the statement before the return, or remove it.",
            """
            System.out.println("done");
            return total;
            """),

        Compile(@"^bad operand types? for (?:binary|unary) operator",
            "Each operator works on certain kinds of values: && joins true-or-false conditions, < compares numbers. This " +
            "operator is given values it does not work on - two numbers joined with &&, say, or two Strings compared with <.",
            "Use the operation that fits the types: .equals or .compareTo for strings, comparisons on each side of && for numbers.",
            """
            if (age > 12 && age < 20) {
                System.out.println("teenager");
            }
            """),

        Compile(@"cannot be applied to given types|no suitable (?:method|constructor) found|constructor .* in class .* cannot be applied",
            "When you call a method, the values in the brackets have to match what the method asks for - the same number of " +
            "them, of the right kinds, in the right order. This call does not match, so Java cannot tell what to do with it.",
            "Pass the arguments its declaration lists, in that order and of those types.",
            """
            static int add(int a, int b) { return a + b; }

            int sum = add(2, 3);
            """),

        Compile(@"has private access",
            "private means only the class that declares something may use it, so other classes cannot reach in and change it " +
            "by accident. This line is in another class, so Java stops it.",
            "Call a public getter or method instead, or change the access if the other class is meant to use it.",
            """
            public int getBalance() {
                return balance;
            }
            """),

        Compile(@"is already defined in",
            "Inside one set of braces each name can be declared only once. This line declares a name that already exists " +
            "here again - to change its value, leave out the type and just assign to it.",
            "Rename one of them, or remove the second declaration and just assign to the first.",
            """
            int count = 0;
            count = 5;
            """),

        Compile(@"cannot return a value from method whose result type is void|unexpected return value",
            "void means a method does its job without giving anything back. This one tries to give back a value with return, " +
            "which a void method cannot do; if it should give back a value, its declaration must say of what type.",
            "Declare the method with the type it returns, or remove the value from the return.",
            """
            static int doubled(int n) {
                return n * 2;
            }
            """),

        Compile(@"invalid method declaration; return type required",
            "Every method must say before its name what it gives back - void if nothing. A constructor is the exception: it " +
            "has no return type, but its name must be exactly the class's name, so a misspelt constructor looks to Java like " +
            "a method with no return type.",
            "Add the return type (void if it returns nothing), or rename the constructor to match the class.",
            """
            static void printTotal(int total) {
                System.out.println(total);
            }
            """),

        Compile(@"cannot be dereferenced",
            "Values like int, double, char and boolean are simple values, not objects, so there is nothing to call with a " +
            "dot. Their wrapper classes - Integer, Character and the rest - have static methods that do the same jobs.",
            "Use the wrapper class's static method - Integer.toString(n), Character.isDigit(c) - or compare with == directly.",
            """
            char c = text.charAt(0);
            if (Character.isDigit(c)) {
                System.out.println("starts with a digit");
            }
            """),

        Compile(@"is not abstract and does not override abstract method",
            "An interface is a promise: any class that implements it will have these methods. This class says it implements " +
            "the interface, or extends an abstract class, but one of the methods is missing - or has a different name or " +
            "different parameters.",
            "Add the missing method with exactly the name, parameters and return type the interface declares.",
            """
            class Circle implements Shape {
                @Override
                public double area() {
                    return Math.PI * r * r;
                }
            }
            """),

        Compile(@"cannot assign a value to final variable",
            "final means 'this is set once and never changes'. The variable already has its value, so this line cannot store " +
            "a new one in it.",
            "Use a new variable for the new value, or remove final if the value is meant to change.",
            """
            int attempts = 0;
            attempts++;
            """),

        Compile(@"integer number too large",
            "Whole numbers written in Java code are ints unless you say otherwise, and an int only goes up to about 2.1 " +
            "billion. This number is bigger than that; an L on the end makes it a long, which holds far bigger numbers.",
            "Add an L to make it a long: 10000000000L.",
            """
            long population = 8000000000L;
            """),

        Lint("fallthrough",
            "In a switch the program jumps to the matching case and then keeps going down through the cases below it until " +
            "it meets a break. This case has no break, so the next case's code runs as well.",
            "Choosing one option also runs the next, and the output is wrong in a way that is easy to miss.",
            "End the case with break - or, if falling through is intended, say so with a comment.",
            """
            switch (choice) {
                case 1:
                    System.out.println("one");
                    break;
                case 2:
                    System.out.println("two");
                    break;
            }
            """),

        Lint("empty",
            "A semicolon on its own is a complete, empty statement. Written straight after if (...), it becomes the whole of " +
            "the if - so the block underneath is not part of the if, and runs every time.",
            "The block below runs every time, whether the condition is true or not.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                System.out.println("pass");
            }
            """),

        Lint("divzero",
            "Dividing a whole number by zero has no answer, and Java stops the program with an error when it happens. The " +
            "number divided by is written here as zero, so this fails every time it runs.",
            "The program crashes with an ArithmeticException when the line runs.",
            "Divide by the value you meant, and check it is not zero first.",
            """
            int perPerson = people > 0 ? total / people : 0;
            """),

        Lint("rawtypes",
            "A list in Java should say what it holds: List<String> holds text. Without the <...> part, Java cannot check what " +
            "you put in, so a wrong kind of value can get in and only cause a crash much later.",
            "The compiler cannot check what goes in, so a wrong value slips in and fails later with a ClassCastException.",
            "Give the type in angle brackets: List<String>.",
            """
            List<String> names = new ArrayList<>();
            """),

        Lint("unchecked",
            "Java normally checks that only the right kind of value goes into a list. Here it cannot - usually because a list " +
            "was made without saying what it holds - so it warns that a wrong value could get in unseen.",
            "A value of the wrong type can get in unnoticed and crash the program somewhere far from here.",
            "Declare the collection with its type, List<String>, so every use is checked.",
            """
            List<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Lint("cast",
            "A cast - a type in brackets in front of a value - converts the value to that type. This value is already that " +
            "type, so the cast changes nothing and can go.",
            "It does nothing but make the line harder to read.",
            "Remove the cast.",
            """
            int total = count * 2;
            """),

        Lint("static",
            "Something marked static belongs to the class as a whole: there is one of it, shared by every object. Reaching it " +
            "through one object makes it look like that object's own copy, which it is not.",
            "Readers assume each object has its own copy when all objects share one.",
            "Use it through the class name: ClassName.member.",
            """
            int created = Counter.total;
            """),

        Lint("finally",
            "The finally block always runs as the try ends. If finally itself returns or throws, that replaces how the try " +
            "ended - including an error, which then vanishes.",
            "Exceptions from the try block are silently lost.",
            "Move the return out of the finally block.",
            """
            try {
                return load();
            } finally {
                close();
            }
            """),

        Lint("deprecation",
            "Java marks some methods and classes as deprecated: they still work for now but are not recommended, usually " +
            "because there is a better or safer one. The documentation for the one used here says what to use instead.",
            "Deprecated code may be removed in a later version and often has known problems.",
            "Use the replacement the documentation names.",
            """
            Integer number = Integer.valueOf(5);
            """),

        Lint("overrides",
            "HashMap and HashSet find an object in two steps: first by a number worked out from it, its hashCode, and then " +
            "by checking equals. If two objects are equal but their numbers differ, the map looks in the wrong place and " +
            "never finds the match.",
            "Two equal objects can then have different hash codes, so HashMap and HashSet lose or duplicate them.",
            "Override both together, using the same fields in each.",
            """
            @Override
            public int hashCode() {
                return Objects.hash(name, age);
            }
            """),

        Thrown("NullPointerException",
            "A variable for an object holds a reference - an arrow pointing at the object - and null means the arrow points " +
            "at nothing. This line follows the arrow to use the object, calling a method or reading a field, but there is no " +
            "object there.",
            "The program crashes at this line, and everything after it is skipped.",
            "Create the object before using it, or check for null before calling methods on it.",
            """
            List<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Thrown("ArrayIndexOutOfBoundsException",
            "An array's items are numbered from 0, so an array of five items has items 0 to 4. Asking for item 5, or for any " +
            "number below 0, goes outside it. A loop written with <= length goes one step too far on its last pass.",
            "The program crashes as soon as the index goes out of range - usually on the last pass of a loop.",
            "Use < array.length in the loop condition, or loop with for-each.",
            """
            for (int i = 0; i < scores.length; i++) {
                System.out.println(scores[i]);
            }
            """),

        Thrown("StringIndexOutOfBoundsException",
            "The letters of a String are numbered from 0, so the last one is at length() - 1. This asks for a position " +
            "outside the text - past the end, before the start, or anywhere at all in an empty string.",
            "The program crashes as soon as the index goes out of range.",
            "Check the index against text.length() before using it, and remember the last character is at length() - 1.",
            """
            if (!text.isEmpty()) {
                char last = text.charAt(text.length() - 1);
            }
            """),

        Thrown("IndexOutOfBoundsException",
            "The items in a list are numbered from 0 to size() - 1. This asks for a position outside that range - and an " +
            "empty list has no positions at all, so even get(0) fails.",
            "The program crashes as soon as the index goes out of range.",
            "Use < list.size() in the loop condition, or check the list is not empty before reading from it.",
            """
            for (int i = 0; i < names.size(); i++) {
                System.out.println(names.get(i));
            }
            """),

        Thrown("ArithmeticException",
            "Dividing a whole number by zero has no answer, so Java stops the program. The number divided by is zero at this " +
            "point - often because it counts items, and there were none.",
            "The program crashes whenever the divisor is zero - often when a count is zero because a list is empty.",
            "Check the divisor is not zero before dividing.",
            """
            int average = count == 0 ? 0 : total / count;
            """),

        Thrown("NumberFormatException",
            "Integer.parseInt turns text like \"42\" into a number. It can only do that when the text is exactly a whole number " +
            "- no spaces, no letters, no decimal point, and not empty.",
            "The program crashes as soon as it reads a value like that.",
            "Trim the text, check it before converting, or catch NumberFormatException and ask again.",
            """
            try {
                int age = Integer.parseInt(text.trim());
            } catch (NumberFormatException e) {
                System.out.println("Please type a whole number.");
            }
            """),

        Thrown("InputMismatchException",
            "A Scanner reads what was typed one piece at a time. nextInt() expects the next piece to be a whole number; this " +
            "one was not, and the Scanner stopped rather than guess.",
            "The program crashes at the first unexpected input.",
            "Check with hasNextInt() before nextInt(), and skip the bad input with next().",
            """
            while (!scanner.hasNextInt()) {
                scanner.next();
                System.out.println("Please type a whole number.");
            }
            int age = scanner.nextInt();
            """),

        Thrown("NoSuchElementException",
            "The program asked the Scanner for another piece of input, but the input had already run out - nothing more was " +
            "typed, or the file ended.",
            "The program stops at the question it asked.",
            "Give the program its input - in FixFinder, type the answers into the input box, one per line - or check hasNext() first.",
            """
            if (scanner.hasNextLine()) {
                String name = scanner.nextLine();
            }
            """),

        Thrown("ClassCastException",
            "A cast tells Java 'treat this object as this type'. Java checks it when the program runs, and this object is not " +
            "that type, so the cast fails.",
            "The program crashes at the cast.",
            "Check the object's type with instanceof before casting, or give the collection a type so no cast is needed.",
            """
            if (shape instanceof Circle circle) {
                System.out.println(circle.radius());
            }
            """),

        Thrown("ConcurrentModificationException",
            "A for-each loop walks through a list or set with a hidden helper that remembers where it is. Adding or removing " +
            "items directly while it walks confuses that helper, so Java stops the loop.",
            "The loop crashes, because it can no longer tell which items it has seen.",
            "Remove items with an Iterator's remove(), or with removeIf.",
            """
            names.removeIf(name -> name.isEmpty());
            """),

        Thrown("StackOverflowError",
            "Each method call takes a little memory until it finishes. A method that keeps calling itself without stopping " +
            "keeps taking more, until that space - the stack - runs out and the program crashes.",
            "The program runs out of stack space and crashes.",
            "Add a base case that returns without calling the method again, and make each call move closer to it.",
            """
            static int factorial(int n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }
            """),

        Thrown("UnsupportedOperationException",
            "Some lists are made so they cannot change: List.of makes one that can never change, and Arrays.asList makes one " +
            "whose length is fixed. Adding or removing items is refused, so Java stops the program.",
            "The program crashes at this line.",
            "Copy it into a new ArrayList before changing it.",
            """
            List<String> names = new ArrayList<>(List.of("Ada", "Alan"));
            names.add("Grace");
            """),

        Thrown("FileNotFoundException",
            "The program asked for a file that is not where Java looked. A name like data.txt is looked for in the folder the " +
            "program was started from, which may not be the folder the code is in.",
            "The program crashes, or stops reading, as soon as it tries to open the file.",
            "Check the file name and path, and catch the exception to tell the user.",
            """
            try (Scanner reader = new Scanner(new File("data.txt"))) {
                while (reader.hasNextLine()) System.out.println(reader.nextLine());
            } catch (FileNotFoundException e) {
                System.out.println("data.txt is missing");
            }
            """),

        Thrown("NegativeArraySizeException",
            "An array is made with a fixed number of places, and the number given here is below zero - there is no such thing " +
            "as an array with fewer than zero places.",
            "The program crashes at this line.",
            "Check the size is zero or more before creating the array.",
            """
            int[] values = new int[Math.max(count, 0)];
            """),

        Compile(@"(?:is|are) a preview feature and (?:is|are) disabled by default",
            "Java adds new things to the language over the years, and tries some of them out first as preview features, which " +
            "stay switched off unless they are asked for. In the JDK that built this program, something this line uses is still " +
            "one of those, so javac will not build it. A later Java may have made it an ordinary part of the language - the note " +
            "says which.",
            "Build the program with a JDK of the Java that made the feature standard - the note says which, and whether one is on " +
            "this computer - or write the code without it.",
            """
            public class Hello {
                public static void main(String[] args) {
                    System.out.println("Hello");
                }
            }
            """),

        Compile(@"(?:is|are) not supported in -source \d+",
            "Each version of Java added things to the language. This program is being compiled for an older version than the " +
            "one that brought in something this line uses, so javac refuses it, as it would for the course or project that chose " +
            "that version.",
            "Compile it for the Java the message names, or a later one - by changing the Java the project or Settings chooses - or " +
            "write the line without the feature.",
            """
            // var needs Java 10 or later; the type written out builds on every Java
            ArrayList<String> names = new ArrayList<>();
            """),

        Thrown("launcher error",
            "From Java 25, main does not have to be static. When it is not, Java first makes an object of the class to call main " +
            "on, using a constructor that takes nothing - and this class has no such constructor that Java may use: its " +
            "constructors are private, or all of them take something.",
            "The program never starts: java stops before any of its code runs.",
            "Make main static - public static void main(String[] args) - or give the class a constructor that takes nothing and is " +
            "not private.",
            """
            public class Game {
                public static void main(String[] args) {
                    new Game(3).play();
                }
            }
            """,
            pattern: @"^no non-private zero argument constructor found in class"),

        Thrown("launcher error",
            "From Java 25, main does not have to be static. When it is not, Java makes an object of the class to call main on - " +
            "and this class is abstract, which means no object can be made of it.",
            "The program never starts: java stops before any of its code runs.",
            "Make main static, or start the program from a class that is not abstract.",
            """
            public abstract class Shape {
                public static void main(String[] args) {
                    System.out.println(new Circle(2).area());
                }
            }
            """,
            pattern: @"^abstract class \S+ can not be instantiated"),

        Thrown("launcher error",
            "From Java 25, main does not have to be static. When it is not, Java makes an object of the class to call main on - " +
            "and this class is written inside another without static, so an object of it can only be made from an object of the " +
            "class around it, which Java does not have.",
            "The program never starts: java stops before any of its code runs.",
            "Make the class static, move it out to a file of its own, or make main static.",
            """
            public class Outer {
                static class Program {
                    void main() {
                        IO.println("Hello");
                    }
                }
            }
            """,
            pattern: @"^non-static inner class \S+ constructor can not be invoked"),
    ];
}
