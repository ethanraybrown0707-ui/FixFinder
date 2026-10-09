using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// The C# compiler's errors and warnings and .NET's exceptions, each explained for someone new to programming.
/// </summary>
internal static class CSharpGuides
{
    private const string NothingRuns =
        "The C# compiler refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Code(
        string[] codes, string explanation, string fix, string example, string? why = null) => new()
    {
        Codes = codes,
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
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
        Code(["CS0103"],
            "Every name in C# - a variable, a method, a class - has to be declared before it is used, and a variable can only " +
            "be used inside the braces it was declared in. This line uses a name that does not exist at this point. C# treats " +
            "capitals as different letters, so Total and total are two different names.",
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
            "C# needs a semicolon at the end of every statement, like a full stop at the end of a sentence, and this one is " +
            "missing it.",
            "Put a semicolon at the end of the statement.",
            """
            var name = "Ada";
            Console.WriteLine(name);
            """),

        Code(["CS1513", "CS1514", "CS1026", "CS1003", "CS1525", "CS1001", "CS1022", "CS1519", "CS8124", "CS1010", "CS1012"],
            "The compiler reads code in a fixed shape: braces wrap each block, and every opening bracket needs its closing " +
            "partner. Something here breaks that shape - a brace or bracket is missing or extra, or a word is where the " +
            "language does not allow it.",
            "Match every opening brace and bracket with its closing partner, and check the line before the one named.",
            """
            if (score > 50)
            {
                Console.WriteLine("pass");
            }
            """),

        Code(["CS0029", "CS0266"],
            "C# checks that every value goes into a place made for its kind: text into a string, whole numbers into an int. " +
            "This line puts one kind of value where another is expected, and C# will not convert it by itself - you have to " +
            "say how, with int.Parse for text, for example.",
            "Convert it - int.Parse(text), value.ToString(), (int)Math.Round(d) - or change the declared type to match.",
            """
            string text = Console.ReadLine() ?? "";
            int age = int.Parse(text);
            """),

        Code(["CS0161"],
            "A method that says it gives back a value has to give one back on every way through it. There is a way to reach " +
            "the end of this method - past the ifs and loops - without a return.",
            "Add a return after the if/else or loop, for the case none of the branches handled.",
            """
            static string Grade(int score)
            {
                if (score >= 50) return "pass";
                return "fail";
            }
            """),

        Code(["CS0165"],
            "A variable declared inside a method starts with no value, and C# will not let you read it until something has " +
            "been stored in it. On at least one way through the code, this variable is read before that happens.",
            "Give it a starting value where it is declared, or make sure every branch assigns it.",
            """
            int largest = numbers[0];
            foreach (var n in numbers)
            {
                if (n > largest) largest = n;
            }
            """),

        Code(["CS0246"],
            "The type named here - a class like List or StreamReader - lives in a namespace, a named group of types. The " +
            "compiler cannot find it: it may be spelt differently, the file may need a using line naming its namespace, or a " +
            "package may need adding to the project.",
            "Check the spelling and capitals, and add the using directive for its namespace.",
            """
            using System.Collections.Generic;

            var names = new List<string>();
            """),

        Code(["CS1061", "CS0117"],
            "A dot after a value asks it for one of its parts - a property like Count, a method like Add. This type has " +
            "nothing by that name: check the spelling and capitals, or the value may be a different type from the one you " +
            "thought.",
            "Check the spelling and capitals against the type's members.",
            """
            var names = new List<string> { "Ada" };
            Console.WriteLine(names.Count);
            """),

        Code(["CS0120"],
            "Main is static: it belongs to the class itself, not to any object made from it. Fields and methods without " +
            "static belong to an object, so Main can only use them through an object it creates with new.",
            "Make the member static, or create an object and use the member through it.",
            """
            var app = new Program();
            app.Run();
            """),

        Code(["CS1503", "CS1502"],
            "Each value you pass to a method must be the kind its parameter asks for. This argument is a different kind - text " +
            "where a number is wanted, for example - and C# will not convert it by itself.",
            "Convert the argument to the parameter's type, or call the overload meant for this type.",
            """
            int count = int.Parse(countText);
            PrintTimes(message, count);
            """),

        Code(["CS7036", "CS1501"],
            "A method lists the values it needs. This call gives it more or fewer than that, so C# cannot match them up.",
            "Pass exactly the arguments its declaration lists, or give the extra parameters default values.",
            """
            static int Add(int a, int b) => a + b;

            var sum = Add(2, 3);
            """),

        Code(["CS0019", "CS0023"],
            "Each operator works on certain kinds of values: && joins true-or-false conditions, + adds numbers or joins text. " +
            "This operator is given values it cannot work with.",
            "Use the operation that fits the types, or convert one side first.",
            """
            if (age > 12 && age < 20)
            {
                Console.WriteLine("teenager");
            }
            """),

        Code(["CS0122"],
            "private means only the class that declares something can use it, and protected also lets classes built on it use " +
            "it. This line is somewhere else, so C# stops it.",
            "Use a public property or method instead, or change the member's access if it is meant to be used here.",
            """
            public int Balance { get; private set; }
            """),

        Code(["CS0128", "CS0136"],
            "Inside a block each name can be declared only once - and in C#, a name used in an outer block cannot be declared " +
            "again in an inner one either. To change a variable's value, just assign to it, without the type in front.",
            "Rename one of them, or assign to the existing variable instead of declaring a new one.",
            """
            var count = 0;
            count = 5;
            """),

        Code(["CS0127", "CS0126"],
            "void means a method does its job without giving anything back, and any other type means it must give back a value " +
            "of that type. The return here does not match what the method's declaration says.",
            "Change the method's return type to what it returns, or return a value of the declared type.",
            """
            static int Doubled(int n)
            {
                return n * 2;
            }
            """),

        Code(["CS0535", "CS0534"],
            "An interface is a promise: any class that implements it will have these members. This class says it implements " +
            "the interface, or inherits an abstract class, but one of the members is missing - or has a different name, " +
            "parameters or return type.",
            "Add the missing member with exactly the name, parameters and return type declared.",
            """
            class Circle : IShape
            {
                public double Area() => Math.PI * Radius * Radius;
            }
            """),

        Code(["CS0200", "CS0191", "CS0198"],
            "Some properties and fields can only be given their value while the object is being made, in its constructor, and " +
            "never changed afterwards. This line tries to change one later.",
            "Set it in the constructor, or give the property a setter if it is meant to change.",
            """
            public string Name { get; set; }
            """),

        Code(["CS0428", "CS1955"],
            "A method does something when you call it with brackets: number.ToString(). A property is a value you read without " +
            "brackets: names.Count. This line mixes the two up - a method without its brackets, or a property with them.",
            "Add () to call a method; remove () from a property or field.",
            """
            var count = names.Count;
            var text = number.ToString();
            """),

        Code(["CS4033", "CS4032"],
            "await means 'wait here for this to finish without freezing the program'. It only works inside a method marked " +
            "async, because C# has to rewrite that method so it can pause and carry on later.",
            "Mark the method async and make it return Task or Task<T>.",
            """
            static async Task Main()
            {
                var text = await File.ReadAllTextAsync("data.txt");
            }
            """),

        Code(["CS0106", "CS0112", "CS0621"],
            "Words like public, static, virtual and abstract change what a declaration means, and each kind of declaration " +
            "only accepts some of them. This one has a word it cannot take.",
            "Remove the modifier the message names.",
            """
            public void Run()
            {
            }
            """),

        Code(["CS0115", "CS0506"],
            "override replaces a method the base class allows to be replaced, and it has to match that method exactly. There " +
            "is no matching method in the base class: the name or parameters may differ, or the base method is not marked " +
            "virtual.",
            "Match the base method's name and parameters exactly, and mark the base method virtual.",
            """
            public override string ToString() => $"{Name} ({Age})";
            """),

        Code(["CS0162"],
            "return, break, continue and throw all leave the current block right away, so a line after one of them in the same " +
            "block can never be reached. C# warns you, because code that never runs is usually a mistake.",
            "Move it before the return, or remove it.",
            """
            Console.WriteLine("done");
            return total;
            """,
            why: "Whatever this code was meant to do never happens, and nothing tells you."),

        Code(["CS0168", "CS0219", "CS8321", "CS0169", "CS0414"],
            "The code makes something - a variable, a field, a small function - and never uses it. Often it is left over from " +
            "earlier code, or a sign that a variable with a similar name is being used by mistake.",
            "Remove it, or use it where it was meant to be used.",
            """
            var total = prices.Sum();
            Console.WriteLine(total);
            """,
            why: "An unused variable is often a sign the code uses a different variable by mistake, and it makes the code harder to read."),

        Code(["CS0649"],
            "The field is declared, but nothing ever stores a value in it, so it always holds its starting default: null for an " +
            "object, 0 for a number, false for a true-or-false value.",
            "Assign it in the constructor or where it is declared.",
            """
            private readonly List<string> _names = new();
            """,
            why: "Anything that reads it gets the default, and a null default crashes with a NullReferenceException."),

        Code(["CS1717", "CS1718"],
            "The line stores a variable's value back into the same variable, or compares it with itself, which changes or " +
            "checks nothing. Usually another variable with a similar name was meant - as with this.name = name in a constructor.",
            "Use the other variable you meant - often a parameter with a similar name, as in this.name = name.",
            """
            public Person(string name)
            {
                this.name = name;
            }
            """,
            why: "The line does nothing useful, so the value you meant to store or check is never used."),

        Code(["CS0665"],
            "One = stores a value; two == compare values. The condition here uses one =, so instead of checking the variable it " +
            "sets it, and the check always comes out the same.",
            "Use == to compare.",
            """
            if (done == true)
            {
                Console.WriteLine("finished");
            }
            """,
            why: "The condition is always the same, so the if never does what it was written to decide."),

        Code(["CS0252", "CS0253"],
            "When one side of == is typed as object, == asks whether the two sides are the very same object in memory, not " +
            "whether they hold equal values. Two equal strings can be separate objects, so the check can fail when it should " +
            "pass.",
            "Compare with .Equals(), or cast both sides to string when they are strings.",
            """
            if (first.Equals(second))
            {
                Console.WriteLine("same");
            }
            """,
            why: "Equal values stored in different objects compare as different, so the check fails when it should pass."),

        Code(["CS0472"],
            "A value like an int always holds a number - it can never be empty - so comparing it with null always gives the " +
            "same answer. To allow 'no value', declare it with a question mark: int?.",
            "Remove the null check, or use int? if the value really can be missing.",
            """
            int? age = null;
            if (age is null) Console.WriteLine("unknown");
            """,
            why: "The condition always gives the same answer, so the code guarding against a missing value never runs."),

        Code(["CS4014"],
            "An async method starts some work and hands back a promise that it will finish later. Without await, the program " +
            "does not wait for it - it carries straight on, and if the work fails, nobody ever hears about it.",
            "Put await in front of the call, and make the calling method async.",
            """
            await SaveAsync(data);
            """,
            why: "The work may not be done when the next line runs, and any exception it throws is silently lost."),

        Code(["CS1998"],
            "async lets a method wait for slow work without freezing the program. This method is marked async but never waits " +
            "for anything, so it runs from start to finish straight away, and the async does nothing useful.",
            "Remove async and return the value directly - or await the asynchronous work it was meant to wait for.",
            """
            static int Count(List<string> names) => names.Count;
            """,
            why: "It looks asynchronous but blocks, and it adds overhead for nothing."),

        Code(["CS0108", "CS0114"],
            "A class built on another can replace the base class's methods, but only with override. This member has the same " +
            "name as one in the base class and no override, so it hides the old one instead - and code that holds the object " +
            "as the base type still calls the old one.",
            "Use override if it should replace the base member, or new if hiding is really what you want.",
            """
            public override string Describe() => "circle";
            """,
            why: "Code that uses the base type still calls the base member, so the new one is ignored depending on how the object is held."),

        Code(["CS0642"],
            "A semicolon on its own is a complete, empty statement. Put straight after if (...), while (...) or for (...), it " +
            "becomes the whole body - so the block underneath is not part of it, and runs every time.",
            "Remove the semicolon after the condition.",
            """
            if (score > 50)
            {
                Console.WriteLine("pass");
            }
            """,
            why: "The block below runs every time, whatever the condition."),

        Code(["CS0659", "CS0661", "CS0660"],
            "Dictionary and HashSet find an object in two steps: first by a number worked out from it, GetHashCode, and then " +
            "by checking Equals. If equal objects give different numbers, the dictionary looks in the wrong place and misses " +
            "the match.",
            "Override GetHashCode using the same fields Equals compares.",
            """
            public override int GetHashCode() => HashCode.Combine(Name, Age);
            """,
            why: "Equal objects can get different hash codes, so Dictionary and HashSet lose or duplicate them."),

        Code(["CS8600", "CS8601", "CS8602", "CS8603", "CS8604", "CS8618", "CS8625"],
            "C# can keep track of which variables might hold null, which means nothing at all. This value might be null here, " +
            "but the code uses it as if it certainly held something - which would crash if it really were null.",
            "Check for null first, give a default with ??, or make sure the value is always set.",
            """
            var name = Console.ReadLine() ?? "";
            Console.WriteLine(name.Length);
            """,
            why: "If the value is null when the line runs, the program crashes with a NullReferenceException."),

        Thrown("NullReferenceException",
            "A variable for an object holds a reference - an arrow pointing at the object - and null means it points at " +
            "nothing. This line follows the arrow to use the object, calling a method or reading a property, but there is no " +
            "object there.",
            "The program crashes at this line, and everything after it is skipped.",
            "Create the object before using it, or check for null first.",
            """
            var names = new List<string>();
            names.Add("Ada");
            """),

        Thrown("IndexOutOfRangeException",
            "An array's items are numbered from 0, so an array of five items has items 0 to 4. Asking for item 5, or for any " +
            "number below 0, goes outside it. A loop written with <= Length goes one step too far on its last pass.",
            "The program crashes as soon as the index goes out of range - usually on the last pass of a loop.",
            "Use < array.Length in the loop condition, or loop with foreach.",
            """
            for (var i = 0; i < scores.Length; i++)
            {
                Console.WriteLine(scores[i]);
            }
            """),

        Thrown("ArgumentOutOfRangeException",
            "A list's items are numbered from 0 to Count - 1, and a string's letters from 0 to Length - 1. This asks for a " +
            "position, or a count, outside that range - and an empty list has no positions at all.",
            "The program crashes at this line.",
            "Check the index against Count or Length first.",
            """
            if (names.Count > 0)
            {
                Console.WriteLine(names[0]);
            }
            """),

        Thrown("FormatException",
            "int.Parse turns text like \"42\" into a number, but only when the text is exactly a number. Empty text, letters or " +
            "a decimal point make it stop rather than guess.",
            "The program crashes as soon as someone types a value like that.",
            "Use int.TryParse, which says whether the text was a number instead of crashing.",
            """
            if (int.TryParse(text, out var age))
            {
                Console.WriteLine(age + 1);
            }
            """),

        Thrown("InvalidCastException",
            "A cast tells C# 'treat this object as this type'. It is checked while the program runs, and this object is not " +
            "that type, so the cast fails.",
            "The program crashes at the cast.",
            "Check the type with is before casting.",
            """
            if (shape is Circle circle)
            {
                Console.WriteLine(circle.Radius);
            }
            """),

        Thrown("DivideByZeroException",
            "Dividing a whole number by zero has no answer, so the program stops. The number divided by is zero here - often " +
            "because it counts items, and there were none.",
            "The program crashes whenever the divisor is zero - often when a count is zero because a list is empty.",
            "Check the divisor is not zero before dividing.",
            """
            var average = count == 0 ? 0 : total / count;
            """),

        Thrown("KeyNotFoundException",
            "A dictionary finds each value by its key, like a word in a glossary. This key is not in the dictionary - it may " +
            "be spelt or capitalised differently, or not added yet.",
            "The program crashes whenever the key is missing.",
            "Use TryGetValue, or check ContainsKey first.",
            """
            if (prices.TryGetValue("apple", out var price))
            {
                Console.WriteLine(price);
            }
            """),

        Thrown("InvalidOperationException",
            "A foreach loop walks through a list with a hidden helper that remembers where it is. Adding or removing items " +
            "directly while it walks confuses that helper, so the program stops.",
            "The loop crashes, because it can no longer tell which items it has seen.",
            "Loop over a copy, .ToList(), or use RemoveAll.",
            """
            names.RemoveAll(name => name.Length == 0);
            """,
            pattern: @"Collection was modified"),

        Thrown("InvalidOperationException",
            "First, Single, Max and similar methods pick one item out of a list. When the list is empty there is nothing to " +
            "pick, so they stop the program rather than make something up.",
            "The program crashes whenever the sequence is empty.",
            "Use FirstOrDefault, or check Any() before asking for the first or largest item.",
            """
            var first = names.FirstOrDefault() ?? "(none)";
            """,
            pattern: @"Sequence contains no"),

        Thrown("InvalidOperationException",
            "The object can only do this when it is ready for it - a queue must have items before you take one out, for " +
            "example - and right now it is not.",
            "The program crashes at this line.",
            "Read the message for what state is needed, and make sure it holds before this line.",
            """
            if (queue.Count > 0)
            {
                var next = queue.Dequeue();
            }
            """),

        Thrown("StackOverflowException",
            "Each method call takes a little memory until it finishes. A method that keeps calling itself without stopping " +
            "keeps taking more, until that space - the stack - runs out and the program is shut down.",
            "The program runs out of stack space and is killed.",
            "Add a base case that returns without calling the method again, and make each call move closer to it.",
            """
            static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
            """),

        Thrown("FileNotFoundException",
            "The program asked for a file that is not where it looked. A name like data.txt is looked for in the folder the " +
            "program is running in, which may not be where the project's files are - often it is the bin folder.",
            "The program crashes as soon as it tries to open the file.",
            "Check the name and path, and check File.Exists first.",
            """
            if (File.Exists("data.txt"))
            {
                var text = File.ReadAllText("data.txt");
            }
            """),

        Thrown("OverflowException",
            "Each type of number has a range: an int only goes up to about 2.1 billion. This number is bigger, or smaller, " +
            "than the type it is being turned into can hold.",
            "The program crashes at this line.",
            "Use a larger type such as long, or check the value's range first.",
            """
            long population = long.Parse(text);
            """),

        Thrown("NotImplementedException",
            "When a method is first created as a placeholder, its body is often just throw new NotImplementedException() - a " +
            "note saying 'write this later'. The program reached one of those placeholders.",
            "Anything that calls this method crashes.",
            "Write the method's body.",
            """
            public double Area() => Width * Height;
            """),

        Thrown("ArgumentNullException",
            "The method needs a real value for one of its inputs and was given null - nothing - instead, so it stopped " +
            "straight away rather than fail somewhere later.",
            "The program crashes at the call.",
            "Make sure the value passed is not null - check it, or give it a default.",
            """
            var words = (line ?? "").Split(' ');
            """),
    ];
}
