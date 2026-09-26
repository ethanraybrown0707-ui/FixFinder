using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// The C# compiler's errors and warnings and .NET's exceptions, each explained three ways: for someone new to
/// programming, as it is usually taught, and in the language's own terms.
/// </summary>
internal static class CSharpGuides
{
    private const string NothingRuns =
        "The C# compiler refuses to build the program while this is wrong, so no part of it runs.";

    private static GuideEntry Code(
        string[] codes, string beginner, string explanation, string technical, string fix, string example, string? why = null) => new()
    {
        Codes = codes,
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example) { ForBeginners = beginner, ForTechnical = technical },
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
        Code(["CS0103"],
            "Every name in C# - a variable, a method, a class - has to be declared before it is used, and a variable can only " +
            "be used inside the braces it was declared in. This line uses a name that does not exist at this point. C# treats " +
            "capitals as different letters, so Total and total are two different names.",
            "The code uses a name that does not exist where this line is - it is misspelt (C# is case-sensitive), declared inside " +
            "another block, or needs a using directive.",
            "Simple-name lookup found no local, parameter, member or type with this name in any enclosing scope. A local's " +
            "scope is the block it is declared in, and a type or static member from another namespace needs a using directive " +
            "or using static. Identifiers are case-sensitive.",
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
            "Every C# statement must end with a semicolon, and this one does not.",
            "Declarations, expression statements, return, break and the like are terminated by a semicolon; the parser met a " +
            "token that cannot continue the statement where it expected one.",
            "Put a semicolon at the end of the statement.",
            """
            var name = "Ada";
            Console.WriteLine(name);
            """),

        Code(["CS1513", "CS1514", "CS1026", "CS1003", "CS1525", "CS1001", "CS1022", "CS1519", "CS8124", "CS1010", "CS1012"],
            "The compiler reads code in a fixed shape: braces wrap each block, and every opening bracket needs its closing " +
            "partner. Something here breaks that shape - a brace or bracket is missing or extra, or a word is where the " +
            "language does not allow it.",
            "The compiler could not read the code here: a brace, bracket or parenthesis is missing or extra, or a word is in a place " +
            "the language does not allow it.",
            "The parser could not fit the tokens to C#'s grammar. CS1513 and CS1026 mean a block or an argument list was never " +
            "closed, and CS1525 that no expression can begin with this token. Error recovery often reports the problem a line " +
            "or more after the real mistake.",
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
            "A value of one type is stored in a variable, returned or passed as a different type it cannot turn into by itself.",
            "No implicit conversion exists from the expression's type to the target type (CS0029); where an explicit one does, " +
            "such as double to int or a base class to a derived class, CS0266 asks for a cast. Reading a number from text is " +
            "never a conversion but a method call such as int.Parse.",
            "Convert it - int.Parse(text), value.ToString(), (int)Math.Round(d) - or change the declared type to match.",
            """
            string text = Console.ReadLine() ?? "";
            int age = int.Parse(text);
            """),

        Code(["CS0161"],
            "A method that says it gives back a value has to give one back on every way through it. There is a way to reach " +
            "the end of this method - past the ifs and loops - without a return.",
            "The method promises to return a value, but there is a way through it that reaches the end without a return.",
            "The end of a non-void method's body must be unreachable. Flow analysis treats a condition as unknown unless it is " +
            "constant, so an if without an else, or a loop that may not run, can reach the end.",
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
            "The variable is read on a path where nothing has stored a value in it yet.",
            "A local must be definitely assigned before every read. Flow analysis treats if and loop conditions as unknown " +
            "unless they are constant, and an out argument counts as an assignment. Fields, unlike locals, start with their " +
            "default value.",
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
            "The type or namespace named here cannot be found: it is misspelt, or it needs a using directive or a package.",
            "Type lookup searched the enclosing namespaces, the using directives - global ones included - and the referenced " +
            "assemblies, and found no type with this name and number of type parameters. Add the using directive, qualify the " +
            "name, or reference the assembly or NuGet package that defines it.",
            "Check the spelling and capitals, and add the using directive for its namespace.",
            """
            using System.Collections.Generic;

            var names = new List<string>();
            """),

        Code(["CS1061", "CS0117"],
            "A dot after a value asks it for one of its parts - a property like Count, a method like Add. This type has " +
            "nothing by that name: check the spelling and capitals, or the value may be a different type from the one you " +
            "thought.",
            "The type has no member with this name - it is misspelt, belongs to a different type, or is a property being called as a method.",
            "Member lookup on the expression's static type - its inherited members and, for CS1061, any extension methods in " +
            "scope - found nothing by that name. In C# a property is used without brackets and a method with them.",
            "Check the spelling and capitals against the type's members.",
            """
            var names = new List<string> { "Ada" };
            Console.WriteLine(names.Count);
            """),

        Code(["CS0120"],
            "Main is static: it belongs to the class itself, not to any object made from it. Fields and methods without " +
            "static belong to an object, so Main can only use them through an object it creates with new.",
            "Main is static, so it belongs to the class, not to an object - and this line uses an instance member without an object.",
            "A static member has no this, so an instance field, property or method named by its simple name has no instance to " +
            "bind to. Qualify it with an instance, or make the member static.",
            "Make the member static, or create an object and use the member through it.",
            """
            var app = new Program();
            app.Run();
            """),

        Code(["CS1503", "CS1502"],
            "Each value you pass to a method must be the kind its parameter asks for. This argument is a different kind - text " +
            "where a number is wanted, for example - and C# will not convert it by itself.",
            "An argument is of a type the method's parameter cannot accept.",
            "Overload resolution settled on a method, but this argument has no implicit conversion to the matching parameter's " +
            "type, so it needs converting first or a different overload.",
            "Convert the argument to the parameter's type, or call the overload meant for this type.",
            """
            int count = int.Parse(countText);
            PrintTimes(message, count);
            """),

        Code(["CS7036", "CS1501"],
            "A method lists the values it needs. This call gives it more or fewer than that, so C# cannot match them up.",
            "The method or constructor is called with a different number of arguments than it takes.",
            "No overload takes this many arguments (CS1501), or a required parameter has no argument (CS7036). Only optional " +
            "parameters with defaults and a params array let a call pass fewer or more arguments than there are parameters.",
            "Pass exactly the arguments its declaration lists, or give the extra parameters default values.",
            """
            static int Add(int a, int b) => a + b;

            var sum = Add(2, 3);
            """),

        Code(["CS0019", "CS0023"],
            "Each operator works on certain kinds of values: && joins true-or-false conditions, + adds numbers or joins text. " +
            "This operator is given values it cannot work with.",
            "The operator cannot be used with values of these types - such as && on numbers, or + on a bool.",
            "No predefined or user-defined operator accepts operands of these types: && and || need bool, arithmetic needs " +
            "numeric types, and C# never converts between bool and numbers.",
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
            "The member is private or protected in its own type, so this code cannot reach it.",
            "The member's declared accessibility - private, protected, internal or private protected - does not include the " +
            "place it is used from; accessibility is checked when compiling, against the containing type and assembly.",
            "Use a public property or method instead, or change the member's access if it is meant to be used here.",
            """
            public int Balance { get; private set; }
            """),

        Code(["CS0128", "CS0136"],
            "Inside a block each name can be declared only once - and in C#, a name used in an outer block cannot be declared " +
            "again in an inner one either. To change a variable's value, just assign to it, without the type in front.",
            "A variable with this name is already declared in this scope or one around it.",
            "A local's name must be unique in its declaration space (CS0128), and C# does not let a local or parameter take a " +
            "name that already means something in an enclosing local scope (CS0136), where C and C++ would allow the shadowing.",
            "Rename one of them, or assign to the existing variable instead of declaring a new one.",
            """
            var count = 0;
            count = 5;
            """),

        Code(["CS0127", "CS0126"],
            "void means a method does its job without giving anything back, and any other type means it must give back a value " +
            "of that type. The return here does not match what the method's declaration says.",
            "The return does not match the method's declared return type - a value returned from a void method, or none from a method that needs one.",
            "In a void method, or an async method returning Task, every return must have no expression (CS0127); in a method " +
            "with a result type, every return needs an expression convertible to it (CS0126).",
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
            "The class promises to implement an interface or abstract class, but a member it must write is missing or spelt differently.",
            "A class must implement every member of the interfaces it lists (CS0535), and override every abstract member it " +
            "inherits unless it is abstract itself (CS0534), with the same name, parameter types and return type.",
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
            "The property or field can only be set when the object is created, and this line changes it later.",
            "A get-only property can be assigned only in a constructor or its initializer (CS0200), and a readonly field only in " +
            "its declaration or a constructor of its own type (CS0191; CS0198 for a static readonly field and the static " +
            "constructor). An init accessor also allows object initializers.",
            "Set it in the constructor, or give the property a setter if it is meant to change.",
            """
            public string Name { get; set; }
            """),

        Code(["CS0428", "CS1955"],
            "A method does something when you call it with brackets: number.ToString(). A property is a value you read without " +
            "brackets: names.Count. This line mixes the two up - a method without its brackets, or a property with them.",
            "A method is used without calling it, or something that is not a method is called with brackets.",
            "A method's name with no argument list is a method group, which converts only to a matching delegate type (CS0428); " +
            "calling a property, field or other member that is not a method with () is CS1955.",
            "Add () to call a method; remove () from a property or field.",
            """
            var count = names.Count;
            var text = number.ToString();
            """),

        Code(["CS4033", "CS4032"],
            "await means 'wait here for this to finish without freezing the program'. It only works inside a method marked " +
            "async, because C# has to rewrite that method so it can pause and carry on later.",
            "await can only be used inside a method marked async.",
            "await is only valid in an async method, lambda or local function, whose body the compiler turns into a state " +
            "machine; such a method returns void, Task, Task<T>, ValueTask or another task-like type. Top-level statements " +
            "may await directly.",
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
            "The modifier is not allowed on this kind of member.",
            "Each kind of declaration admits only certain modifiers: CS0106 is a modifier not valid on this item, CS0112 " +
            "override, virtual or abstract on a static member, and CS0621 virtual or abstract on a private one.",
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
            "The method is marked override, but there is no matching virtual method in a base class - it is misspelt or the base method is not virtual.",
            "An override must match an inherited virtual, abstract or override member by name and signature (CS0115), and a " +
            "base member that is none of those cannot be overridden at all (CS0506).",
            "Match the base method's name and parameters exactly, and mark the base method virtual.",
            """
            public override string ToString() => $"{Name} ({Age})";
            """),

        Code(["CS0162"],
            "return, break, continue and throw all leave the current block right away, so a line after one of them in the same " +
            "block can never be reached. C# warns you, because code that never runs is usually a mistake.",
            "This code comes after a return, break, continue or throw, so it can never run.",
            "Reachability analysis proved that the statement can never start - it follows a jump or a throw, or sits behind a " +
            "constant false condition - and the compiler warns CS0162. Java rejects the same code outright.",
            "Move it before the return, or remove it.",
            """
            Console.WriteLine("done");
            return total;
            """,
            why: "Whatever this code was meant to do never happens, and nothing tells you."),

        Code(["CS0168", "CS0219", "CS8321", "CS0169", "CS0414"],
            "The code makes something - a variable, a field, a small function - and never uses it. Often it is left over from " +
            "earlier code, or a sign that a variable with a similar name is being used by mistake.",
            "Something is declared, and perhaps given a value, but never used.",
            "CS0168 is a local declared and never used, CS0219 a local assigned but never read, CS8321 a local function never " +
            "called, CS0169 a private field never used and CS0414 a private field assigned but never read.",
            "Remove it, or use it where it was meant to be used.",
            """
            var total = prices.Sum();
            Console.WriteLine(total);
            """,
            why: "An unused variable is often a sign the code uses a different variable by mistake, and it makes the code harder to read."),

        Code(["CS0649"],
            "The field is declared, but nothing ever stores a value in it, so it always holds its starting default: null for an " +
            "object, 0 for a number, false for a true-or-false value.",
            "The field is never given a value, so it always holds its default - null, 0 or false.",
            "No code assigns the field, so it keeps default(T) for its whole life - null for a reference type. The compiler " +
            "can only be sure of that for private and internal fields.",
            "Assign it in the constructor or where it is declared.",
            """
            private readonly List<string> _names = new();
            """,
            why: "Anything that reads it gets the default, and a null default crashes with a NullReferenceException."),

        Code(["CS1717", "CS1718"],
            "The line stores a variable's value back into the same variable, or compares it with itself, which changes or " +
            "checks nothing. Usually another variable with a similar name was meant - as with this.name = name in a constructor.",
            "A variable is assigned to, or compared with, itself.",
            "CS1717 is an assignment whose target and source are the same variable - often a parameter with a field's name, " +
            "missing this. - and CS1718 a comparison of a variable with itself, whose result is fixed (floating-point NaN " +
            "aside).",
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
            "The condition uses = (assignment) where == (comparison) was meant.",
            "An assignment is an expression in C#, so if (done = true) assigns and then tests the constant it assigned; the " +
            "compiler warns CS0665. Only a bool assignment compiles there, since C# has no conversion from numbers to bool.",
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
            "Two objects are compared with ==, which here compares whether they are the same object, not whether they are equal.",
            "With an operand whose static type is object, == binds to reference equality rather than string's overloaded ==, " +
            "and CS0252 or CS0253 warns of a possibly unintended reference comparison. Equals, or a cast to string, compares " +
            "the values.",
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
            "A value type such as int is compared with null, and it can never be null.",
            "A non-nullable value type is never null, so == null is lifted to Nullable<T> and is always false (CS0472). " +
            "Declared as int? it becomes Nullable<int>, whose HasValue can be false.",
            "Remove the null check, or use int? if the value really can be missing.",
            """
            int? age = null;
            if (age is null) Console.WriteLine("unknown");
            """,
            why: "The condition always gives the same answer, so the code guarding against a missing value never runs."),

        Code(["CS4014"],
            "An async method starts some work and hands back a promise that it will finish later. Without await, the program " +
            "does not wait for it - it carries straight on, and if the work fails, nobody ever hears about it.",
            "An async method is called without await, so the code carries on before it finishes.",
            "Calling an async method returns a Task that nothing here observes: execution carries on before it completes, and " +
            "an exception it throws is stored in the Task and never rethrown. Await it, or keep it and await it later.",
            "Put await in front of the call, and make the calling method async.",
            """
            await SaveAsync(data);
            """,
            why: "The work may not be done when the next line runs, and any exception it throws is silently lost."),

        Code(["CS1998"],
            "async lets a method wait for slow work without freezing the program. This method is marked async but never waits " +
            "for anything, so it runs from start to finish straight away, and the async does nothing useful.",
            "The method is marked async but never awaits anything, so it runs synchronously anyway.",
            "An async method with no await runs synchronously to completion, yet still goes through the state machine the " +
            "compiler generates for it. Return the value, or Task.FromResult, directly - or await the work it was meant to.",
            "Remove async and return the value directly - or await the asynchronous work it was meant to wait for.",
            """
            static int Count(List<string> names) => names.Count;
            """,
            why: "It looks asynchronous but blocks, and it adds overhead for nothing."),

        Code(["CS0108", "CS0114"],
            "A class built on another can replace the base class's methods, but only with override. This member has the same " +
            "name as one in the base class and no override, so it hides the old one instead - and code that holds the object " +
            "as the base type still calls the old one.",
            "A member has the same name as one in the base class, so it hides it instead of overriding it.",
            "A member with the name of an inherited one hides it (CS0108; CS0114 when the inherited one is virtual): a call " +
            "through a base-typed reference binds to the base member when compiling instead of dispatching virtually. override " +
            "opts into virtual dispatch, and new makes the hiding explicit.",
            "Use override if it should replace the base member, or new if hiding is really what you want.",
            """
            public override string Describe() => "circle";
            """,
            why: "Code that uses the base type still calls the base member, so the new one is ignored depending on how the object is held."),

        Code(["CS0642"],
            "A semicolon on its own is a complete, empty statement. Put straight after if (...), while (...) or for (...), it " +
            "becomes the whole body - so the block underneath is not part of it, and runs every time.",
            "There is a semicolon straight after the if, while or for, which makes its body empty.",
            "An embedded statement that is only a semicolon is the empty statement, so the braces after it form an independent " +
            "block; the compiler warns CS0642, possible mistaken empty statement.",
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
            "The class overrides Equals or == without GetHashCode.",
            "GetHashCode must return equal values for objects Equals considers equal. CS0659 is Equals overridden without " +
            "GetHashCode, and CS0660 and CS0661 an operator == defined without overriding Equals or GetHashCode - either way " +
            "hash-based collections can no longer find equal keys.",
            "Override GetHashCode using the same fields Equals compares.",
            """
            public override int GetHashCode() => HashCode.Combine(Name, Age);
            """,
            why: "Equal objects can get different hash codes, so Dictionary and HashSet lose or duplicate them."),

        Code(["CS8600", "CS8601", "CS8602", "CS8603", "CS8604", "CS8618", "CS8625"],
            "C# can keep track of which variables might hold null, which means nothing at all. This value might be null here, " +
            "but the code uses it as if it certainly held something - which would crash if it really were null.",
            "A value that may be null is used where the code assumes it is not.",
            "With nullable reference types on, flow analysis tracks whether each expression may be null. These warnings mark a " +
            "maybe-null value dereferenced (CS8602), stored, passed or returned where null is not allowed (CS8600, CS8601, " +
            "CS8603, CS8604), a null literal given to a non-nullable type (CS8625), or a non-nullable field left null when a " +
            "constructor ends (CS8618). They are warnings: the program still builds and fails only when it runs.",
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
            "The line uses a variable or property that holds null - no object was created, or something returned null - as if it held one.",
            "Using a member through a null reference - calling an instance method, reading a field or property, indexing, or " +
            "unboxing null to a value type - throws NullReferenceException. The ?. and ?? operators, and nullable reference " +
            "type warnings, deal with it before it runs.",
            "The program crashes at this line, and everything after it is skipped.",
            "Create the object before using it, or check for null first.",
            """
            var names = new List<string>();
            names.Add("Ada");
            """),

        Thrown("IndexOutOfRangeException",
            "An array's items are numbered from 0, so an array of five items has items 0 to 4. Asking for item 5, or for any " +
            "number below 0, goes outside it. A loop written with <= Length goes one step too far on its last pass.",
            "The index is outside the array. Indexes run from 0 to Length - 1, so a loop using <= Length goes one too far.",
            "Every array access is bounds-checked when it runs: the index must satisfy 0 <= i < array.Length, or the runtime " +
            "throws IndexOutOfRangeException. A string's indexer throws the same exception.",
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
            "An index or count is outside what the list or string allows - an empty list has no items at all.",
            "List<T>'s indexer, Substring, RemoveAt, Insert and similar methods check their index and count arguments and " +
            "throw ArgumentOutOfRangeException, whose ParamName says which one. A List<T> index must be below Count, however " +
            "much Capacity it has.",
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
            "int.Parse or a similar method was given text that is not in the right form, such as an empty string or \"12a\".",
            "By default int.Parse accepts surrounding whitespace, an optional sign and digits (NumberStyles.Integer), and " +
            "throws FormatException for anything else; a number outside int's range throws OverflowException instead. TryParse " +
            "reports failure through its return value.",
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
            "An object is cast to a type it is not.",
            "An explicit reference conversion or an unboxing is checked when it runs against the object's actual type, and " +
            "throws InvalidCastException on a mismatch - unboxing must be to exactly the value type that was boxed. The is and " +
            "as operators test without throwing.",
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
            "An integer is divided by zero.",
            "Integer and decimal division and remainder by zero throw DivideByZeroException, while float and double division " +
            "give infinity or NaN, as IEEE 754 arithmetic does.",
            "The program crashes whenever the divisor is zero - often when a count is zero because a list is empty.",
            "Check the divisor is not zero before dividing.",
            """
            var average = count == 0 ? 0 : total / count;
            """),

        Thrown("KeyNotFoundException",
            "A dictionary finds each value by its key, like a word in a glossary. This key is not in the dictionary - it may " +
            "be spelt or capitalised differently, or not added yet.",
            "The dictionary has no entry with this key.",
            "The Dictionary<TKey, TValue> indexer throws KeyNotFoundException when no key equals this one under the " +
            "dictionary's comparer - for strings, by default, an exact, case-sensitive match. TryGetValue and " +
            "GetValueOrDefault look it up without throwing.",
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
            "A collection was changed while a foreach loop was walking over it.",
            "List<T> and most .NET collections check a version number on each MoveNext and throw InvalidOperationException, " +
            "'Collection was modified', once the collection has been changed other than through the enumerator. Since .NET " +
            "Core 3.0, Dictionary<TKey, TValue>.Remove is the exception and may be called during enumeration.",
            "The loop crashes, because it can no longer tell which items it has seen.",
            "Loop over a copy, .ToList(), or use RemoveAll.",
            """
            names.RemoveAll(name => name.Length == 0);
            """,
            pattern: @"Collection was modified"),

        Thrown("InvalidOperationException",
            "First, Single, Max and similar methods pick one item out of a list. When the list is empty there is nothing to " +
            "pick, so they stop the program rather than make something up.",
            "First, Single, Max or a similar method was called on a sequence with no items.",
            "Enumerable.First, Last, Single, and Min, Max and Average over non-nullable value types, throw " +
            "InvalidOperationException for an empty sequence, and Single also when there is more than one item. FirstOrDefault " +
            "and the other OrDefault methods return default(T) instead.",
            "The program crashes whenever the sequence is empty.",
            "Use FirstOrDefault, or check Any() before asking for the first or largest item.",
            """
            var first = names.FirstOrDefault() ?? "(none)";
            """,
            pattern: @"Sequence contains no"),

        Thrown("InvalidOperationException",
            "The object can only do this when it is ready for it - a queue must have items before you take one out, for " +
            "example - and right now it is not.",
            "The object is not in a state where this operation is allowed.",
            "InvalidOperationException means the call is not valid in the object's current state, whatever its arguments - " +
            "Dequeue on an empty Queue<T>, or reading Nullable<T>.Value when it has none. ObjectDisposedException, for an " +
            "object already disposed, derives from it. The message names the condition.",
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
            "A method keeps calling itself without ever reaching a case that stops it.",
            "Each call uses a frame on the thread's fixed-size stack - 1 MB for the main thread on Windows by default - and " +
            "unbounded recursion exhausts it. A StackOverflowException cannot be caught: the runtime ends the process.",
            "The program runs out of stack space and is killed.",
            "Add a base case that returns without calling the method again, and make each call move closer to it.",
            """
            static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
            """),

        Thrown("FileNotFoundException",
            "The program asked for a file that is not where it looked. A name like data.txt is looked for in the folder the " +
            "program is running in, which may not be where the project's files are - often it is the bin folder.",
            "The file does not exist where the program looked - relative paths are looked up from the folder it runs in.",
            "File and stream methods throw FileNotFoundException, an IOException, when the path does not exist. A relative " +
            "path resolves against the process's current directory, Environment.CurrentDirectory, which depends on how the " +
            "program was started - launched from an IDE, it is often the output folder under bin.",
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
            "A number is too big or too small for the type it is converted to.",
            "Parse methods and Convert.ToInt32 throw OverflowException for a value outside the target type's range, as do " +
            "casts and arithmetic in a checked context; in the default unchecked context, integer arithmetic silently wraps " +
            "around instead.",
            "The program crashes at this line.",
            "Use a larger type such as long, or check the value's range first.",
            """
            long population = long.Parse(text);
            """),

        Thrown("NotImplementedException",
            "When a method is first created as a placeholder, its body is often just throw new NotImplementedException() - a " +
            "note saying 'write this later'. The program reached one of those placeholders.",
            "The program reached a method whose body is still the placeholder throw new NotImplementedException().",
            "NotImplementedException is only ever thrown by code itself, usually a method stub an IDE generated. Unlike " +
            "NotSupportedException, it means the member has not been written yet, not that it is unsupported on purpose.",
            "Anything that calls this method crashes.",
            "Write the method's body.",
            """
            public double Area() => Width * Height;
            """),

        Thrown("ArgumentNullException",
            "The method needs a real value for one of its inputs and was given null - nothing - instead, so it stopped " +
            "straight away rather than fail somewhere later.",
            "A method was given null for an argument that must have a value.",
            "Methods check their reference arguments on entry - with ArgumentNullException.ThrowIfNull, for example - and " +
            "throw with ParamName naming the parameter, so the null is reported where it was passed rather than where it " +
            "would have been used.",
            "The program crashes at the call.",
            "Make sure the value passed is not null - check it, or give it a default.",
            """
            var words = (line ?? "").Split(' ');
            """),
    ];
}
