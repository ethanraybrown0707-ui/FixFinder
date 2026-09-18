using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

internal static class JavaGuides
{
    private const string NothingRuns =
        "javac refuses to build the program while this is wrong, so no part of it runs - not even the lines before this one.";

    private static GuideEntry Compile(string pattern, string explanation, string fix, string example, string? why = null) => new()
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

    private static GuideEntry Thrown(string type, string explanation, string why, string fix, string example, string? pattern = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Compile(@"^cannot find symbol",
            "The code uses a name - a variable, method or class - that javac cannot find where this line is. It may be misspelt " +
            "(Java is case-sensitive), declared inside a different block, or need an import.",
            "Check the spelling and capitals against the declaration, declare the variable where this line can see it, or add the import.",
            """
            import java.util.ArrayList;

            ArrayList<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Compile(@"^';' expected",
            "Every Java statement must end with a semicolon, and this one does not.",
            "Put a semicolon at the end of the statement - usually the line before the one javac points at.",
            """
            int total = 0;
            total += price;
            """),

        Compile(@"^'?[)\]}(\[{]'? expected|^<identifier> expected|^illegal start of|^class, interface, enum, or record expected|^reached end of file while parsing|^unclosed|^not a statement|expected$",
            "javac could not read the code here: a bracket or brace is missing or extra, a statement is outside a method, or a word " +
            "is in the wrong place.",
            "Match every opening brace and bracket with a closing one, and make sure statements are inside a method.",
            """
            public class Main {
                public static void main(String[] args) {
                    System.out.println("Hello");
                }
            }
            """),

        Compile(@"^incompatible types: possible lossy conversion",
            "A value of a larger type, such as double or long, is stored in a smaller one, such as int, which could lose part of it.",
            "Cast it if losing the fraction is what you want, or store it in the larger type.",
            """
            double average = total / 3.0;
            int rounded = (int) Math.round(average);
            """),

        Compile(@"^incompatible types",
            "A value of one type is stored in, returned as, or passed as a different type that it cannot be converted to.",
            "Convert the value - Integer.parseInt(text), String.valueOf(number) - or change the declared type to match.",
            """
            String text = "42";
            int number = Integer.parseInt(text);
            """),

        Compile(@"^missing return statement",
            "The method promises to return a value, but there is a way through it that reaches the end without a return.",
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
            "The variable is read on a path where nothing has stored a value in it yet.",
            "Give the variable a starting value where it is declared, or make sure every branch assigns it.",
            """
            int largest = numbers[0];
            for (int n : numbers) {
                if (n > largest) largest = n;
            }
            """),

        Compile(@"^unreported exception",
            "The method called here can throw a checked exception, and Java requires every checked exception to be caught or declared.",
            "Wrap the call in try/catch, or add throws to the method's declaration.",
            """
            try {
                String text = Files.readString(Path.of("data.txt"));
            } catch (IOException e) {
                System.out.println("Could not read the file: " + e.getMessage());
            }
            """),

        Compile(@"cannot be referenced from a static context",
            "main is static, so it belongs to the class, not to an object - and this line uses an instance field or method without an object.",
            "Make the field or method static, or create an object and call it through that.",
            """
            public static void main(String[] args) {
                Main app = new Main();
                app.run();
            }
            """),

        Compile(@"is public, should be declared in a file named",
            "A public class must be in a file with exactly the same name, capitals included.",
            "Rename the file to match the class, or rename the class to match the file.",
            """
            // in Main.java
            public class Main {
            }
            """),

        Compile(@"^'else' without 'if'",
            "An else has no if directly before it - often because of a semicolon after the if's condition, or a brace in the wrong place.",
            "Remove the semicolon after the if's condition, and put braces around the if's block.",
            """
            if (age >= 18) {
                System.out.println("adult");
            } else {
                System.out.println("child");
            }
            """),

        Compile(@"^unreachable statement",
            "This statement comes after a return, break, continue or throw in the same block, so it can never run.",
            "Move the statement before the return, or remove it.",
            """
            System.out.println("done");
            return total;
            """),

        Compile(@"^bad operand types? for (?:binary|unary) operator",
            "The operator cannot be used with values of these types - such as && on numbers, or < on strings.",
            "Use the operation that fits the types: .equals or .compareTo for strings, comparisons on each side of && for numbers.",
            """
            if (age > 12 && age < 20) {
                System.out.println("teenager");
            }
            """),

        Compile(@"cannot be applied to given types|no suitable (?:method|constructor) found|constructor .* in class .* cannot be applied",
            "The method or constructor is called with arguments that do not match its parameters in number or type.",
            "Pass the arguments its declaration lists, in that order and of those types.",
            """
            static int add(int a, int b) { return a + b; }

            int sum = add(2, 3);
            """),

        Compile(@"has private access",
            "The field or method is private to its own class, so this class cannot use it directly.",
            "Call a public getter or method instead, or change the access if the other class is meant to use it.",
            """
            public int getBalance() {
                return balance;
            }
            """),

        Compile(@"is already defined in",
            "The same name is declared twice in the same scope.",
            "Rename one of them, or remove the second declaration and just assign to the first.",
            """
            int count = 0;
            count = 5;
            """),

        Compile(@"cannot return a value from method whose result type is void|unexpected return value",
            "The method is declared void, so it cannot return a value.",
            "Declare the method with the type it returns, or remove the value from the return.",
            """
            static int doubled(int n) {
                return n * 2;
            }
            """),

        Compile(@"invalid method declaration; return type required",
            "A method has no return type - or a constructor's name does not match its class exactly.",
            "Add the return type (void if it returns nothing), or rename the constructor to match the class.",
            """
            static void printTotal(int total) {
                System.out.println(total);
            }
            """),

        Compile(@"cannot be dereferenced",
            "A method is called on a primitive value such as an int or a char, and primitives have no methods.",
            "Use the wrapper class's static method - Integer.toString(n), Character.isDigit(c) - or compare with == directly.",
            """
            char c = text.charAt(0);
            if (Character.isDigit(c)) {
                System.out.println("starts with a digit");
            }
            """),

        Compile(@"is not abstract and does not override abstract method",
            "The class promises to implement an interface or extend an abstract class, but a method it must write is missing or spelt differently.",
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
            "The variable is final, so once it has a value it cannot be given another.",
            "Use a new variable for the new value, or remove final if the value is meant to change.",
            """
            int attempts = 0;
            attempts++;
            """),

        Compile(@"integer number too large",
            "The number is bigger than an int can hold.",
            "Add an L to make it a long: 10000000000L.",
            """
            long population = 8000000000L;
            """),

        Lint("fallthrough",
            "This case has no break, so after its own statements the program carries straight on into the next case's.",
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
            "There is a semicolon straight after the if's condition, which ends the if with an empty statement.",
            "The block below runs every time, whether the condition is true or not.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50) {
                System.out.println("pass");
            }
            """),

        Lint("divzero",
            "This divides an integer by zero.",
            "The program crashes with an ArithmeticException when the line runs.",
            "Divide by the value you meant, and check it is not zero first.",
            """
            int perPerson = people > 0 ? total / people : 0;
            """),

        Lint("rawtypes",
            "A generic class such as List or ArrayList is used without saying what it holds.",
            "The compiler cannot check what goes in, so a wrong value slips in and fails later with a ClassCastException.",
            "Give the type in angle brackets: List<String>.",
            """
            List<String> names = new ArrayList<>();
            """),

        Lint("unchecked",
            "A value goes into or out of a generic collection without the compiler being able to check its type.",
            "A value of the wrong type can get in unnoticed and crash the program somewhere far from here.",
            "Declare the collection with its type, List<String>, so every use is checked.",
            """
            List<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Lint("cast",
            "This cast converts a value to the type it already has.",
            "It does nothing but make the line harder to read.",
            "Remove the cast.",
            """
            int total = count * 2;
            """),

        Lint("static",
            "A static member is used through an object, which makes it look as if it belongs to that one object.",
            "Readers assume each object has its own copy when all objects share one.",
            "Use it through the class name: ClassName.member.",
            """
            int created = Counter.total;
            """),

        Lint("finally",
            "The finally block cannot finish normally - it returns or throws - which replaces whatever the try block did.",
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
            "The code uses a method or class that Java marks as deprecated.",
            "Deprecated code may be removed in a later version and often has known problems.",
            "Use the replacement the documentation names.",
            """
            Integer number = Integer.valueOf(5);
            """),

        Lint("overrides",
            "The class overrides equals but not hashCode, or the other way round.",
            "Two equal objects can then have different hash codes, so HashMap and HashSet lose or duplicate them.",
            "Override both together, using the same fields in each.",
            """
            @Override
            public int hashCode() {
                return Objects.hash(name, age);
            }
            """),

        Thrown("NullPointerException",
            "The line uses a variable that holds null - it was never given an object, or something returned null - as if it held one.",
            "The program crashes at this line, and everything after it is skipped.",
            "Create the object before using it, or check for null before calling methods on it.",
            """
            List<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Thrown("ArrayIndexOutOfBoundsException",
            "The index is outside the array. Indexes run from 0 to length - 1, so a loop using <= length goes one too far.",
            "The program crashes as soon as the index goes out of range - usually on the last pass of a loop.",
            "Use < array.length in the loop condition, or loop with for-each.",
            """
            for (int i = 0; i < scores.length; i++) {
                System.out.println(scores[i]);
            }
            """),

        Thrown("StringIndexOutOfBoundsException",
            "The index is outside the string. Characters run from 0 to length() - 1.",
            "The program crashes as soon as the index goes out of range.",
            "Check the index against text.length() before using it, and remember the last character is at length() - 1.",
            """
            if (!text.isEmpty()) {
                char last = text.charAt(text.length() - 1);
            }
            """),

        Thrown("IndexOutOfBoundsException",
            "The index is outside the list. Items run from 0 to size() - 1, and an empty list has none at all.",
            "The program crashes as soon as the index goes out of range.",
            "Use < list.size() in the loop condition, or check the list is not empty before reading from it.",
            """
            for (int i = 0; i < names.size(); i++) {
                System.out.println(names.get(i));
            }
            """),

        Thrown("ArithmeticException",
            "An integer is divided by zero.",
            "The program crashes whenever the divisor is zero - often when a count is zero because a list is empty.",
            "Check the divisor is not zero before dividing.",
            """
            int average = count == 0 ? 0 : total / count;
            """),

        Thrown("NumberFormatException",
            "Integer.parseInt or a similar method was given text that is not a number, such as an empty string or \"12a\".",
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
            "The Scanner was asked for a number, but the next thing typed was not one.",
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
            "The Scanner was asked for more input when there was none left to read.",
            "The program stops at the question it asked.",
            "Give the program its input - in FixFinder, type the answers into the input box, one per line - or check hasNext() first.",
            """
            if (scanner.hasNextLine()) {
                String name = scanner.nextLine();
            }
            """),

        Thrown("ClassCastException",
            "An object is cast to a type it is not.",
            "The program crashes at the cast.",
            "Check the object's type with instanceof before casting, or give the collection a type so no cast is needed.",
            """
            if (shape instanceof Circle circle) {
                System.out.println(circle.radius());
            }
            """),

        Thrown("ConcurrentModificationException",
            "A collection is changed - items added or removed - while a for-each loop is walking over it.",
            "The loop crashes, because it can no longer tell which items it has seen.",
            "Remove items with an Iterator's remove(), or with removeIf.",
            """
            names.removeIf(name -> name.isEmpty());
            """),

        Thrown("StackOverflowError",
            "A method keeps calling itself without ever reaching a case that stops it.",
            "The program runs out of stack space and crashes.",
            "Add a base case that returns without calling the method again, and make each call move closer to it.",
            """
            static int factorial(int n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }
            """),

        Thrown("UnsupportedOperationException",
            "The list cannot be changed - List.of and Arrays.asList make fixed lists - and this line adds to or removes from it.",
            "The program crashes at this line.",
            "Copy it into a new ArrayList before changing it.",
            """
            List<String> names = new ArrayList<>(List.of("Ada", "Alan"));
            names.add("Grace");
            """),

        Thrown("FileNotFoundException",
            "The file does not exist where Java looked - relative paths are looked up from the folder the program runs in.",
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
            "An array is created with a negative size.",
            "The program crashes at this line.",
            "Check the size is zero or more before creating the array.",
            """
            int[] values = new int[Math.max(count, 0)];
            """),
    ];
}
