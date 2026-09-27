using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// javac's errors and lint warnings and the JVM's exceptions, each explained three ways: for someone new to
/// programming, as it is usually taught, and in the language's own terms.
/// </summary>
internal static class JavaGuides
{
    private const string NothingRuns =
        "javac refuses to build the program while this is wrong, so no part of it runs - not even the lines before this one.";

    private static GuideEntry Compile(
        string pattern, string beginner, string explanation, string technical, string fix, string example, string? why = null) => new()
    {
        ExceptionTypes = ["compile error"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    private static GuideEntry Lint(string category, string beginner, string explanation, string technical, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["compile warning"],
        Message = new Regex($@"^\[{category}\]"),
        Guide = new MistakeGuide(explanation, why, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    private static GuideEntry Thrown(
        string type, string beginner, string explanation, string technical, string why, string fix, string example, string? pattern = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Compile(@"^cannot find symbol",
            "Every name in Java - a variable, a method, a class - has to be declared before it is used, and can only be used " +
            "inside the braces it was declared in. javac cannot find a declaration of this name that this line can see. Java " +
            "also treats capitals as different letters, so Count and count are two different names.",
            "The code uses a name - a variable, method or class - that javac cannot find where this line is. It may be misspelt " +
            "(Java is case-sensitive), declared inside a different block, or need an import.",
            "No variable, method or type with this name is in scope here. A local variable's scope ends with the block it is " +
            "declared in, a type from another package needs an import or its fully qualified name, and identifiers are " +
            "case-sensitive.",
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
            "Every Java statement must end with a semicolon, and this one does not.",
            "Declarations, expression statements, return, break and the like are terminated by a semicolon. The parser met a " +
            "token that cannot continue the statement where it expected the terminator, and reports where that token begins - " +
            "often the start of the next line.",
            "Put a semicolon at the end of the statement - usually the line before the one javac points at.",
            """
            int total = 0;
            total += price;
            """),

        Compile(@"^'?[)\]}(\[{]'? expected|^<identifier> expected|^illegal start of|^class, interface, enum, or record expected|^reached end of file while parsing|^unclosed|^not a statement|expected$",
            "javac reads code in a fixed shape: a class holds methods, and a method holds statements between braces. " +
            "Something here breaks that shape - a brace or bracket is missing or extra, a statement is outside every method, " +
            "or a word is in a place where it does not belong.",
            "javac could not read the code here: a bracket or brace is missing or extra, a statement is outside a method, or a word " +
            "is in the wrong place.",
            "The parser could not fit the tokens to Java's grammar. 'reached end of file while parsing' means a block was " +
            "never closed; 'illegal start of' and 'class, interface, enum, or record expected' usually mean a brace closed a " +
            "method or class too early, leaving the code after it outside.",
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
            "A value of a larger type, such as double or long, is stored in a smaller one, such as int, which could lose part of it.",
            "Assignment allows widening primitive conversions but not narrowing ones such as double to int or long to int - " +
            "except that a constant int expression may narrow to byte, short or char when its value fits. A cast makes the " +
            "narrowing explicit.",
            "Cast it if losing the fraction is what you want, or store it in the larger type.",
            """
            double average = total / 3.0;
            int rounded = (int) Math.round(average);
            """),

        Compile(@"^incompatible types",
            "Java checks that every value goes into a place made for its kind: text into a String, whole numbers into an int. " +
            "This line puts one kind of value where another kind is expected, and Java will not convert between them on its " +
            "own - text is never turned into a number by itself.",
            "A value of one type is stored in, returned as, or passed as a different type that it cannot be converted to.",
            "The expression's type is not assignable to the target type: no widening, boxing, unboxing or reference " +
            "conversion links them in this context. Converting between String and a number always goes through a method, " +
            "such as Integer.parseInt or String.valueOf.",
            "Convert the value - Integer.parseInt(text), String.valueOf(number) - or change the declared type to match.",
            """
            String text = "42";
            int number = Integer.parseInt(text);
            """),

        Compile(@"^missing return statement",
            "A method that says it gives back a value - an int, a String - has to give one back on every way through it. " +
            "javac found a way to reach the end of the method, past all the ifs and loops, without a return.",
            "The method promises to return a value, but there is a way through it that reaches the end without a return.",
            "A method with a non-void result type must not be able to complete normally. javac's reachability rules treat a " +
            "condition as unknown unless it is a constant expression, so an if without an else, or a loop whose condition is " +
            "not the constant true, can fall through to the end.",
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
            "The variable is read on a path where nothing has stored a value in it yet.",
            "Local variables get no default value, and every read must be of a definitely assigned variable. The flow " +
            "analysis treats if and loop conditions as unknown unless they are constant, so a variable assigned in only some " +
            "branches, or only inside a loop, is not definitely assigned after it.",
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
            "The method called here can throw a checked exception, and Java requires every checked exception to be caught or declared.",
            "Checked exceptions - Exception and its subclasses other than RuntimeException - must be caught by an enclosing " +
            "try/catch or declared in the method's throws clause. RuntimeException, Error and their subclasses are exempt.",
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
            "main is static, so it belongs to the class, not to an object - and this line uses an instance field or method without an object.",
            "A static method has no this, so a simple name that refers to an instance field or method has no object to be " +
            "resolved against. It must be qualified with a reference to an instance, or the member made static.",
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
            "A public class must be in a file with exactly the same name, capitals included.",
            "javac requires a top-level public class to be in a file named after it with the .java extension - the language " +
            "specification lets file-based compilers enforce this so that a class can be found from its name - so a file " +
            "holds at most one top-level public type.",
            "Rename the file to match the class, or rename the class to match the file.",
            """
            // in Main.java
            public class Main {
            }
            """),

        Compile(@"^'else' without 'if'",
            "else has to come straight after the block of an if. Something has come between them - most often a semicolon " +
            "right after if (...), which ends the if at once, or a closing brace in the wrong place.",
            "An else has no if directly before it - often because of a semicolon after the if's condition, or a brace in the wrong place.",
            "An else belongs to the if statement whose then-part it directly follows. if (condition); makes the empty " +
            "statement the then-part, so a block written after it is a separate statement, and the else after that has no if " +
            "to belong to.",
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
            "This statement comes after a return, break, continue or throw in the same block, so it can never run.",
            "Java requires every statement to be reachable by its conservative flow analysis. A statement after return, " +
            "break, continue or throw in the same block, or after while (true) with no break, is unreachable, which is a " +
            "compile error - C and C# at most warn about the same thing.",
            "Move the statement before the return, or remove it.",
            """
            System.out.println("done");
            return total;
            """),

        Compile(@"^bad operand types? for (?:binary|unary) operator",
            "Each operator works on certain kinds of values: && joins true-or-false conditions, < compares numbers. This " +
            "operator is given values it does not work on - two numbers joined with &&, say, or two Strings compared with <.",
            "The operator cannot be used with values of these types - such as && on numbers, or < on strings.",
            "The arithmetic and comparison operators apply to numeric operands after unboxing and binary numeric promotion, " +
            "and && and || need boolean operands. String defines no ordering operators, so Strings are ordered with compareTo.",
            "Use the operation that fits the types: .equals or .compareTo for strings, comparisons on each side of && for numbers.",
            """
            if (age > 12 && age < 20) {
                System.out.println("teenager");
            }
            """),

        Compile(@"cannot be applied to given types|no suitable (?:method|constructor) found|constructor .* in class .* cannot be applied",
            "When you call a method, the values in the brackets have to match what the method asks for - the same number of " +
            "them, of the right kinds, in the right order. This call does not match, so Java cannot tell what to do with it.",
            "The method or constructor is called with arguments that do not match its parameters in number or type.",
            "Overload resolution found no applicable method: every candidate takes a different number of arguments, or has a " +
            "parameter this argument cannot be converted to by method invocation conversion - widening, boxing and unboxing, " +
            "and varargs as a last resort.",
            "Pass the arguments its declaration lists, in that order and of those types.",
            """
            static int add(int a, int b) { return a + b; }

            int sum = add(2, 3);
            """),

        Compile(@"has private access",
            "private means only the class that declares something may use it, so other classes cannot reach in and change it " +
            "by accident. This line is in another class, so Java stops it.",
            "The field or method is private to its own class, so this class cannot use it directly.",
            "A private member is accessible only within the body of the top-level class that encloses its declaration; any " +
            "other class using it is a compile error, whatever package it is in.",
            "Call a public getter or method instead, or change the access if the other class is meant to use it.",
            """
            public int getBalance() {
                return balance;
            }
            """),

        Compile(@"is already defined in",
            "Inside one set of braces each name can be declared only once. This line declares a name that already exists " +
            "here again - to change its value, leave out the type and just assign to it.",
            "The same name is declared twice in the same scope.",
            "A name cannot be declared twice in the same scope, and unlike C and C++, Java does not let a local variable in an " +
            "inner block shadow a local or parameter of the same method either.",
            "Rename one of them, or remove the second declaration and just assign to the first.",
            """
            int count = 0;
            count = 5;
            """),

        Compile(@"cannot return a value from method whose result type is void|unexpected return value",
            "void means a method does its job without giving anything back. This one tries to give back a value with return, " +
            "which a void method cannot do; if it should give back a value, its declaration must say of what type.",
            "The method is declared void, so it cannot return a value.",
            "In a method whose result type is void, every return statement must have no expression; returning a value needs a " +
            "declared result type the value can be assigned to.",
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
            "A method has no return type - or a constructor's name does not match its class exactly.",
            "A method declaration needs a result type; one without it is only valid as a constructor, whose name must equal " +
            "the class's simple name, so javac reports a missing return type when the names differ.",
            "Add the return type (void if it returns nothing), or rename the constructor to match the class.",
            """
            static void printTotal(int total) {
                System.out.println(total);
            }
            """),

        Compile(@"cannot be dereferenced",
            "Values like int, double, char and boolean are simple values, not objects, so there is nothing to call with a " +
            "dot. Their wrapper classes - Integer, Character and the rest - have static methods that do the same jobs.",
            "A method is called on a primitive value such as an int or a char, and primitives have no methods.",
            "Primitive types have no members, so member access cannot be applied to an expression of primitive type. A " +
            "method needs a reference - the value boxed as an Integer, say - or a static method of the wrapper class.",
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
            "The class promises to implement an interface or extend an abstract class, but a method it must write is missing or spelt differently.",
            "A concrete class must implement every abstract method it inherits, with a method of the same name and parameter " +
            "types and a compatible return type. Any difference declares a new overload instead and leaves the abstract " +
            "method unimplemented, which @Override would have caught.",
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
            "The variable is final, so once it has a value it cannot be given another.",
            "A final variable can be assigned only once: a final local must be definitely unassigned where it is assigned, and " +
            "a blank final field must be assigned exactly once, in its initializer or in every constructor.",
            "Use a new variable for the new value, or remove final if the value is meant to change.",
            """
            int attempts = 0;
            attempts++;
            """),

        Compile(@"integer number too large",
            "Whole numbers written in Java code are ints unless you say otherwise, and an int only goes up to about 2.1 " +
            "billion. This number is bigger than that; an L on the end makes it a long, which holds far bigger numbers.",
            "The number is bigger than an int can hold.",
            "An integer literal without an L suffix has type int, whose range is -2,147,483,648 to 2,147,483,647. A decimal " +
            "literal outside it is a compile error even when it is being assigned to a long, so it needs the L.",
            "Add an L to make it a long: 10000000000L.",
            """
            long population = 8000000000L;
            """),

        Lint("fallthrough",
            "In a switch the program jumps to the matching case and then keeps going down through the cases below it until " +
            "it meets a break. This case has no break, so the next case's code runs as well.",
            "This case has no break, so after its own statements the program carries straight on into the next case's.",
            "With colon-style case labels, execution falls through into the next group of statements unless break, return or " +
            "throw ends the group; -Xlint:fallthrough reports a group that can run into the next label. Arrow-style case " +
            "labels never fall through.",
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
            "There is a semicolon straight after the if's condition, which ends the if with an empty statement.",
            "if (condition); has the empty statement as its then-part, so the block that follows is an ordinary statement run " +
            "whatever the condition; -Xlint:empty reports the empty statement after the if.",
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
            "This divides an integer by zero.",
            "Integer / and % by zero throw ArithmeticException when they run - floating-point division gives Infinity or NaN " +
            "instead - and -Xlint:divzero reports a divisor that is the constant zero.",
            "The program crashes with an ArithmeticException when the line runs.",
            "Divide by the value you meant, and check it is not zero first.",
            """
            int perPerson = people > 0 ? total / people : 0;
            """),

        Lint("rawtypes",
            "A list in Java should say what it holds: List<String> holds text. Without the <...> part, Java cannot check what " +
            "you put in, so a wrong kind of value can get in and only cause a crash much later.",
            "A generic class such as List or ArrayList is used without saying what it holds.",
            "A generic type used without type arguments is a raw type: its type parameters are erased and the compiler no " +
            "longer checks its elements. Raw types exist only so code from before generics still compiles.",
            "The compiler cannot check what goes in, so a wrong value slips in and fails later with a ClassCastException.",
            "Give the type in angle brackets: List<String>.",
            """
            List<String> names = new ArrayList<>();
            """),

        Lint("unchecked",
            "Java normally checks that only the right kind of value goes into a list. Here it cannot - usually because a list " +
            "was made without saying what it holds - so it warns that a wrong value could get in unseen.",
            "A value goes into or out of a generic collection without the compiler being able to check its type.",
            "An unchecked conversion or call - a raw type where a parameterized one is expected, or a cast to a parameterized " +
            "type - cannot be verified at run time because generic types are erased, so a wrong value can get in and cause a " +
            "ClassCastException somewhere else entirely.",
            "A value of the wrong type can get in unnoticed and crash the program somewhere far from here.",
            "Declare the collection with its type, List<String>, so every use is checked.",
            """
            List<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Lint("cast",
            "A cast - a type in brackets in front of a value - converts the value to that type. This value is already that " +
            "type, so the cast changes nothing and can go.",
            "This cast converts a value to the type it already has.",
            "The expression already has the cast's target type, so the cast is an identity conversion with no effect; " +
            "-Xlint:cast reports casts that are not needed.",
            "It does nothing but make the line harder to read.",
            "Remove the cast.",
            """
            int total = count * 2;
            """),

        Lint("static",
            "Something marked static belongs to the class as a whole: there is one of it, shared by every object. Reaching it " +
            "through one object makes it look like that object's own copy, which it is not.",
            "A static member is used through an object, which makes it look as if it belongs to that one object.",
            "A static member reached through an instance is resolved from the expression's static type when compiling; the " +
            "instance expression is evaluated and then ignored, so it is used even when it is null. -Xlint:static reports it.",
            "Readers assume each object has its own copy when all objects share one.",
            "Use it through the class name: ClassName.member.",
            """
            int created = Counter.total;
            """),

        Lint("finally",
            "The finally block always runs as the try ends. If finally itself returns or throws, that replaces how the try " +
            "ended - including an error, which then vanishes.",
            "The finally block cannot finish normally - it returns or throws - which replaces whatever the try block did.",
            "When a finally block completes abruptly, by return, throw, break or continue, the whole try statement completes " +
            "for that reason, and any exception or return value from the try or catch is discarded; -Xlint:finally reports a " +
            "finally clause that cannot complete normally.",
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
            "The code uses a method or class that Java marks as deprecated.",
            "The element is annotated @Deprecated, and -Xlint:deprecation reports uses of it from outside it. One marked " +
            "@Deprecated(forRemoval = true) is due to be removed in a future release.",
            "Deprecated code may be removed in a later version and often has known problems.",
            "Use the replacement the documentation names.",
            """
            Integer number = Integer.valueOf(5);
            """),

        Lint("overrides",
            "HashMap and HashSet find an object in two steps: first by a number worked out from it, its hashCode, and then " +
            "by checking equals. If two objects are equal but their numbers differ, the map looks in the wrong place and " +
            "never finds the match.",
            "The class overrides equals but not hashCode, or the other way round.",
            "Object's contract requires equal objects to have equal hash codes. Overriding equals without hashCode, or the " +
            "reverse, breaks it, so hash-based collections put equal keys in different buckets; -Xlint:overrides reports it.",
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
            "The line uses a variable that holds null - it was never given an object, or something returned null - as if it held one.",
            "Using a null reference - calling a method, reading a field or an array element, taking an array's length, " +
            "unboxing, or synchronizing on it - throws NullPointerException. Helpful messages (JEP 358), on by default from " +
            "Java 15, name the expression that was null.",
            "The program crashes at this line, and everything after it is skipped.",
            "Create the object before using it, or check for null before calling methods on it.",
            """
            List<String> names = new ArrayList<>();
            names.add("Ada");
            """),

        Thrown("ArrayIndexOutOfBoundsException",
            "An array's items are numbered from 0, so an array of five items has items 0 to 4. Asking for item 5, or for any " +
            "number below 0, goes outside it. A loop written with <= length goes one step too far on its last pass.",
            "The index is outside the array. Indexes run from 0 to length - 1, so a loop using <= length goes one too far.",
            "Every array access is checked when it runs: the index must satisfy 0 <= i < a.length, and otherwise the JVM " +
            "throws ArrayIndexOutOfBoundsException before anything is read or written.",
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
            "The index is outside the string. Characters run from 0 to length() - 1.",
            "charAt, substring and the other index-taking String methods check their indexes and throw " +
            "StringIndexOutOfBoundsException when one is out of range. For substring(begin, end), end may equal length(), " +
            "but begin may not be greater than end.",
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
            "The index is outside the list. Items run from 0 to size() - 1, and an empty list has none at all.",
            "List's get, set and remove(int) need 0 <= index < size(), and add(index, element) also allows index == size(); " +
            "anything else throws IndexOutOfBoundsException. Unlike an array's, a list's size changes as items are added " +
            "and removed.",
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
            "An integer is divided by zero.",
            "int and long / and % throw ArithmeticException for a zero divisor, while float and double division give " +
            "Infinity or NaN. BigDecimal.divide throws it too when the exact answer has no end.",
            "The program crashes whenever the divisor is zero - often when a count is zero because a list is empty.",
            "Check the divisor is not zero before dividing.",
            """
            int average = count == 0 ? 0 : total / count;
            """),

        Thrown("NumberFormatException",
            "Integer.parseInt turns text like \"42\" into a number. It can only do that when the text is exactly a whole number " +
            "- no spaces, no letters, no decimal point, and not empty.",
            "Integer.parseInt or a similar method was given text that is not a number, such as an empty string or \"12a\".",
            "Integer.parseInt accepts an optional sign followed by digits of the radix and nothing else - no whitespace, no " +
            "decimal point - with a value in int's range. Anything else throws NumberFormatException, a subclass of " +
            "IllegalArgumentException.",
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
            "The Scanner was asked for a number, but the next thing typed was not one.",
            "Scanner.nextInt() throws InputMismatchException when the next token is not an integer, or is out of range, and " +
            "leaves that token unread - so trying again without calling next() fails in the same way.",
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
            "The Scanner was asked for more input when there was none left to read.",
            "Scanner's next methods throw NoSuchElementException when the input is exhausted, while hasNext, hasNextInt and " +
            "hasNextLine test for more without reading any. An Iterator's next() throws the same exception when it has no " +
            "more elements.",
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
            "An object is cast to a type it is not.",
            "A reference cast is checked when it runs against the object's actual class, and throws ClassCastException when " +
            "the object is not an instance of the target type. instanceof with a pattern, from Java 16, tests and names the " +
            "object in one step.",
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
            "A collection is changed - items added or removed - while a for-each loop is walking over it.",
            "The iterators of ArrayList, HashMap and most java.util collections are fail-fast: each next() compares a " +
            "modification count and throws ConcurrentModificationException if the collection was structurally changed other " +
            "than through the iterator itself. The check is made on a best-effort basis, not guaranteed.",
            "The loop crashes, because it can no longer tell which items it has seen.",
            "Remove items with an Iterator's remove(), or with removeIf.",
            """
            names.removeIf(name -> name.isEmpty());
            """),

        Thrown("StackOverflowError",
            "Each method call takes a little memory until it finishes. A method that keeps calling itself without stopping " +
            "keeps taking more, until that space - the stack - runs out and the program crashes.",
            "A method keeps calling itself without ever reaching a case that stops it.",
            "Each call pushes a frame onto the thread's stack, whose size is fixed (-Xss). Unbounded recursion exhausts it, " +
            "and the JVM throws StackOverflowError - an Error, not an Exception. The JVM does not eliminate tail calls.",
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
            "The list cannot be changed - List.of and Arrays.asList make fixed lists - and this line adds to or removes from it.",
            "List.of returns an unmodifiable list whose every mutator throws UnsupportedOperationException, and Arrays.asList " +
            "a fixed-size view of the array that supports set but not add or remove. Copying either into an ArrayList gives " +
            "a list that can be changed.",
            "The program crashes at this line.",
            "Copy it into a new ArrayList before changing it.",
            """
            List<String> names = new ArrayList<>(List.of("Ada", "Alan"));
            names.add("Grace");
            """),

        Thrown("FileNotFoundException",
            "The program asked for a file that is not where Java looked. A name like data.txt is looked for in the folder the " +
            "program was started from, which may not be the folder the code is in.",
            "The file does not exist where Java looked - relative paths are looked up from the folder the program runs in.",
            "FileInputStream, FileReader and new Scanner(File) throw FileNotFoundException, a checked IOException, when the " +
            "path does not exist, is a directory, or cannot be opened. A relative path is resolved against the working " +
            "directory, the user.dir property.",
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
            "An array is created with a negative size.",
            "Creating an array evaluates its dimension expressions and throws NegativeArraySizeException if any of them is " +
            "negative; a size of zero is allowed and gives an empty array.",
            "The program crashes at this line.",
            "Check the size is zero or more before creating the array.",
            """
            int[] values = new int[Math.max(count, 0)];
            """),
    ];
}
