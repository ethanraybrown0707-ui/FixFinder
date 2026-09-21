using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

internal static class CSharpGuides
{
    private const string NothingRuns =
        "The C# compiler refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Code(string[] codes, string explanation, string fix, string example, string? why = null) => new()
    {
        Codes = codes,
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry Thrown(string type, string explanation, string why, string fix, string example, string? pattern = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Code(["CS0103"],
            "The code uses a name that does not exist where this line is - it is misspelt (C# is case-sensitive), declared inside " +
            "another block, or needs a using directive.",
            "Check the spelling and capitals against the declaration, or declare it where this line can see it.",
            """
            int total = 0;
            foreach (var price in prices)
            {
                total += price;
            }
            Console.WriteLine(total);
            """),

        Code(["CS1002"],
            "Every C# statement must end with a semicolon, and this one does not.",
            "Put a semicolon at the end of the statement.",
            """
            var name = "Ada";
            Console.WriteLine(name);
            """),

        Code(["CS1513", "CS1514", "CS1026", "CS1003", "CS1525", "CS1001", "CS1022", "CS1519", "CS8124", "CS1010", "CS1012"],
            "The compiler could not read the code here: a brace, bracket or parenthesis is missing or extra, or a word is in a place " +
            "the language does not allow it.",
            "Match every opening brace and bracket with its closing partner, and check the line before the one named.",
            """
            if (score > 50)
            {
                Console.WriteLine("pass");
            }
            """),

        Code(["CS0029", "CS0266"],
            "A value of one type is stored in a variable, returned or passed as a different type it cannot turn into by itself.",
            "Convert it - int.Parse(text), value.ToString(), (int)Math.Round(d) - or change the declared type to match.",
            """
            string text = Console.ReadLine() ?? "";
            int age = int.Parse(text);
            """),

        Code(["CS0161"],
            "The method promises to return a value, but there is a way through it that reaches the end without a return.",
            "Add a return after the if/else or loop, for the case none of the branches handled.",
            """
            static string Grade(int score)
            {
                if (score >= 50) return "pass";
                return "fail";
            }
            """),

        Code(["CS0165"],
            "The variable is read on a path where nothing has stored a value in it yet.",
            "Give it a starting value where it is declared, or make sure every branch assigns it.",
            """
            int largest = numbers[0];
            foreach (var n in numbers)
            {
                if (n > largest) largest = n;
            }
            """),

        Code(["CS0246"],
            "The type or namespace named here cannot be found: it is misspelt, or it needs a using directive or a package.",
            "Check the spelling and capitals, and add the using directive for its namespace.",
            """
            using System.Collections.Generic;

            var names = new List<string>();
            """),

        Code(["CS1061", "CS0117"],
            "The type has no member with this name - it is misspelt, belongs to a different type, or is a property being called as a method.",
            "Check the spelling and capitals against the type's members.",
            """
            var names = new List<string> { "Ada" };
            Console.WriteLine(names.Count);
            """),

        Code(["CS0120"],
            "Main is static, so it belongs to the class, not to an object - and this line uses an instance member without an object.",
            "Make the member static, or create an object and use the member through it.",
            """
            var app = new Program();
            app.Run();
            """),

        Code(["CS1503", "CS1502"],
            "An argument is of a type the method's parameter cannot accept.",
            "Convert the argument to the parameter's type, or call the overload meant for this type.",
            """
            int count = int.Parse(countText);
            PrintTimes(message, count);
            """),

        Code(["CS7036", "CS1501"],
            "The method or constructor is called with a different number of arguments than it takes.",
            "Pass exactly the arguments its declaration lists, or give the extra parameters default values.",
            """
            static int Add(int a, int b) => a + b;

            var sum = Add(2, 3);
            """),

        Code(["CS0019", "CS0023"],
            "The operator cannot be used with values of these types - such as && on numbers, or + on a bool.",
            "Use the operation that fits the types, or convert one side first.",
            """
            if (age > 12 && age < 20)
            {
                Console.WriteLine("teenager");
            }
            """),

        Code(["CS0122"],
            "The member is private or protected in its own type, so this code cannot reach it.",
            "Use a public property or method instead, or change the member's access if it is meant to be used here.",
            """
            public int Balance { get; private set; }
            """),

        Code(["CS0128", "CS0136"],
            "A variable with this name is already declared in this scope or one around it.",
            "Rename one of them, or assign to the existing variable instead of declaring a new one.",
            """
            var count = 0;
            count = 5;
            """),

        Code(["CS0127", "CS0126"],
            "The return does not match the method's declared return type - a value returned from a void method, or none from a method that needs one.",
            "Change the method's return type to what it returns, or return a value of the declared type.",
            """
            static int Doubled(int n)
            {
                return n * 2;
            }
            """),

        Code(["CS0535", "CS0534"],
            "The class promises to implement an interface or abstract class, but a member it must write is missing or spelt differently.",
            "Add the missing member with exactly the name, parameters and return type declared.",
            """
            class Circle : IShape
            {
                public double Area() => Math.PI * Radius * Radius;
            }
            """),

        Code(["CS0200", "CS0191", "CS0198"],
            "The property or field can only be set when the object is created, and this line changes it later.",
            "Set it in the constructor, or give the property a setter if it is meant to change.",
            """
            public string Name { get; set; }
            """),

        Code(["CS0428", "CS1955"],
            "A method is used without calling it, or something that is not a method is called with brackets.",
            "Add () to call a method; remove () from a property or field.",
            """
            var count = names.Count;
            var text = number.ToString();
            """),

        Code(["CS4033", "CS4032"],
            "await can only be used inside a method marked async.",
            "Mark the method async and make it return Task or Task<T>.",
            """
            static async Task Main()
            {
                var text = await File.ReadAllTextAsync("data.txt");
            }
            """),

        Code(["CS0106", "CS0112", "CS0621"],
            "The modifier is not allowed on this kind of member.",
            "Remove the modifier the message names.",
            """
            public void Run()
            {
            }
            """),

        Code(["CS0115", "CS0506"],
            "The method is marked override, but there is no matching virtual method in a base class - it is misspelt or the base method is not virtual.",
            "Match the base method's name and parameters exactly, and mark the base method virtual.",
            """
            public override string ToString() => $"{Name} ({Age})";
            """),

        Code(["CS0162"],
            "This code comes after a return, break, continue or throw, so it can never run.",
            "Move it before the return, or remove it.",
            """
            Console.WriteLine("done");
            return total;
            """,
            why: "Whatever this code was meant to do never happens, and nothing tells you."),

        Code(["CS0168", "CS0219", "CS8321", "CS0169", "CS0414"],
            "Something is declared, and perhaps given a value, but never used.",
            "Remove it, or use it where it was meant to be used.",
            """
            var total = prices.Sum();
            Console.WriteLine(total);
            """,
            why: "An unused variable is often a sign the code uses a different variable by mistake, and it makes the code harder to read."),

        Code(["CS0649"],
            "The field is never given a value, so it always holds its default - null, 0 or false.",
            "Assign it in the constructor or where it is declared.",
            """
            private readonly List<string> _names = new();
            """,
            why: "Anything that reads it gets the default, and a null default crashes with a NullReferenceException."),

        Code(["CS1717", "CS1718"],
            "A variable is assigned to, or compared with, itself.",
            "Use the other variable you meant - often a parameter with a similar name, as in this.name = name.",
            """
            public Person(string name)
            {
                this.name = name;
            }
            """,
            why: "The line does nothing useful, so the value you meant to store or check is never used."),

        Code(["CS0665"],
            "The condition uses = (assignment) where == (comparison) was meant.",
            "Use == to compare.",
            """
            if (done == true)
            {
                Console.WriteLine("finished");
            }
            """,
            why: "The condition is always the same, so the if never does what it was written to decide."),

        Code(["CS0252", "CS0253"],
            "Two objects are compared with ==, which here compares whether they are the same object, not whether they are equal.",
            "Compare with .Equals(), or cast both sides to string when they are strings.",
            """
            if (first.Equals(second))
            {
                Console.WriteLine("same");
            }
            """,
            why: "Equal values stored in different objects compare as different, so the check fails when it should pass."),

        Code(["CS0472"],
            "A value type such as int is compared with null, and it can never be null.",
            "Remove the null check, or use int? if the value really can be missing.",
            """
            int? age = null;
            if (age is null) Console.WriteLine("unknown");
            """,
            why: "The condition always gives the same answer, so the code guarding against a missing value never runs."),

        Code(["CS4014"],
            "An async method is called without await, so the code carries on before it finishes.",
            "Put await in front of the call, and make the calling method async.",
            """
            await SaveAsync(data);
            """,
            why: "The work may not be done when the next line runs, and any exception it throws is silently lost."),

        Code(["CS1998"],
            "The method is marked async but never awaits anything, so it runs synchronously anyway.",
            "Remove async and return the value directly - or await the asynchronous work it was meant to wait for.",
            """
            static int Count(List<string> names) => names.Count;
            """,
            why: "It looks asynchronous but blocks, and it adds overhead for nothing."),

        Code(["CS0108", "CS0114"],
            "A member has the same name as one in the base class, so it hides it instead of overriding it.",
            "Use override if it should replace the base member, or new if hiding is really what you want.",
            """
            public override string Describe() => "circle";
            """,
            why: "Code that uses the base type still calls the base member, so the new one is ignored depending on how the object is held."),

        Code(["CS0642"],
            "There is a semicolon straight after the if, while or for, which makes its body empty.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50)
            {
                Console.WriteLine("pass");
            }
            """,
            why: "The block below runs every time, whatever the condition."),

        Code(["CS0659", "CS0661", "CS0660"],
            "The class overrides Equals or == without GetHashCode.",
            "Override GetHashCode using the same fields Equals compares.",
            """
            public override int GetHashCode() => HashCode.Combine(Name, Age);
            """,
            why: "Equal objects can get different hash codes, so Dictionary and HashSet lose or duplicate them."),

        Code(["CS8600", "CS8601", "CS8602", "CS8603", "CS8604", "CS8618", "CS8625"],
            "A value that may be null is used where the code assumes it is not.",
            "Check for null first, give a default with ??, or make sure the value is always set.",
            """
            var name = Console.ReadLine() ?? "";
            Console.WriteLine(name.Length);
            """,
            why: "If the value is null when the line runs, the program crashes with a NullReferenceException."),

        Thrown("NullReferenceException",
            "The line uses a variable or property that holds null - no object was created, or something returned null - as if it held one.",
            "The program crashes at this line, and everything after it is skipped.",
            "Create the object before using it, or check for null first.",
            """
            var names = new List<string>();
            names.Add("Ada");
            """),

        Thrown("IndexOutOfRangeException",
            "The index is outside the array. Indexes run from 0 to Length - 1, so a loop using <= Length goes one too far.",
            "The program crashes as soon as the index goes out of range - usually on the last pass of a loop.",
            "Use < array.Length in the loop condition, or loop with foreach.",
            """
            for (var i = 0; i < scores.Length; i++)
            {
                Console.WriteLine(scores[i]);
            }
            """),

        Thrown("ArgumentOutOfRangeException",
            "An index or count is outside what the list or string allows - an empty list has no items at all.",
            "The program crashes at this line.",
            "Check the index against Count or Length first.",
            """
            if (names.Count > 0)
            {
                Console.WriteLine(names[0]);
            }
            """),

        Thrown("FormatException",
            "int.Parse or a similar method was given text that is not in the right form, such as an empty string or \"12a\".",
            "The program crashes as soon as someone types a value like that.",
            "Use int.TryParse, which says whether the text was a number instead of crashing.",
            """
            if (int.TryParse(text, out var age))
            {
                Console.WriteLine(age + 1);
            }
            """),

        Thrown("InvalidCastException",
            "An object is cast to a type it is not.",
            "The program crashes at the cast.",
            "Check the type with is before casting.",
            """
            if (shape is Circle circle)
            {
                Console.WriteLine(circle.Radius);
            }
            """),

        Thrown("DivideByZeroException",
            "An integer is divided by zero.",
            "The program crashes whenever the divisor is zero - often when a count is zero because a list is empty.",
            "Check the divisor is not zero before dividing.",
            """
            var average = count == 0 ? 0 : total / count;
            """),

        Thrown("KeyNotFoundException",
            "The dictionary has no entry with this key.",
            "The program crashes whenever the key is missing.",
            "Use TryGetValue, or check ContainsKey first.",
            """
            if (prices.TryGetValue("apple", out var price))
            {
                Console.WriteLine(price);
            }
            """),

        Thrown("InvalidOperationException",
            "A collection was changed while a foreach loop was walking over it.",
            "The loop crashes, because it can no longer tell which items it has seen.",
            "Loop over a copy, .ToList(), or use RemoveAll.",
            """
            names.RemoveAll(name => name.Length == 0);
            """,
            pattern: @"Collection was modified"),

        Thrown("InvalidOperationException",
            "First, Single, Max or a similar method was called on a sequence with no items.",
            "The program crashes whenever the sequence is empty.",
            "Use FirstOrDefault, or check Any() before asking for the first or largest item.",
            """
            var first = names.FirstOrDefault() ?? "(none)";
            """,
            pattern: @"Sequence contains no"),

        Thrown("InvalidOperationException",
            "The object is not in a state where this operation is allowed.",
            "The program crashes at this line.",
            "Read the message for what state is needed, and make sure it holds before this line.",
            """
            if (queue.Count > 0)
            {
                var next = queue.Dequeue();
            }
            """),

        Thrown("StackOverflowException",
            "A method keeps calling itself without ever reaching a case that stops it.",
            "The program runs out of stack space and is killed.",
            "Add a base case that returns without calling the method again, and make each call move closer to it.",
            """
            static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
            """),

        Thrown("FileNotFoundException",
            "The file does not exist where the program looked - relative paths are looked up from the folder it runs in.",
            "The program crashes as soon as it tries to open the file.",
            "Check the name and path, and check File.Exists first.",
            """
            if (File.Exists("data.txt"))
            {
                var text = File.ReadAllText("data.txt");
            }
            """),

        Thrown("OverflowException",
            "A number is too big or too small for the type it is converted to.",
            "The program crashes at this line.",
            "Use a larger type such as long, or check the value's range first.",
            """
            long population = long.Parse(text);
            """),

        Thrown("NotImplementedException",
            "The program reached a method whose body is still the placeholder throw new NotImplementedException().",
            "Anything that calls this method crashes.",
            "Write the method's body.",
            """
            public double Area() => Width * Height;
            """),

        Thrown("ArgumentNullException",
            "A method was given null for an argument that must have a value.",
            "The program crashes at the call.",
            "Make sure the value passed is not null - check it, or give it a default.",
            """
            var words = (line ?? "").Split(' ');
            """),
    ];
}
