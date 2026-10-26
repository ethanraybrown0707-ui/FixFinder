using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What Node.js says of a TypeScript file it cannot run by stripping its types, and what TypeScript's compiler, tsc, says of
/// its types - each explained for someone new to programming. Once its types are gone a TypeScript program is JavaScript, so
/// what stops it running is JavaScript's, and JavaScript's guides explain it.
/// </summary>
/// <remarks>
/// Every message matched here is one Node.js 24.19, or TypeScript 7.0.2, was seen to give, in its own words; what each type
/// error's guide says Node.js then does - ReferenceError: mraks is not defined, "5" + 1 is "51" - Node 24.19 did. Every example
/// is a whole program, and TypeScriptLiveTests checks each one with FixFinder's check and runs it with Node.js, and fails on
/// anything Node says - and, where this computer has tsc, on any type error tsc finds in it.
/// </remarks>
internal static class TypeScriptGuides
{
    private const string NothingRuns = "Node.js reads the whole file before it runs any of it, so while this is there no part of the program runs.";

    private static GuideEntry Refused(string code, string? pattern, string explanation, string fix, string example) => new()
    {
        Codes = [code],
        ExceptionTypes = ["SyntaxError"],
        Message = pattern is null ? null : new Regex(pattern),
        Guide = new MistakeGuide(explanation, NothingRuns, fix, example),
    };

    /// <summary>A type error tsc found - by its number of its own, or, with none, any that has no guide of its own.</summary>
    private static GuideEntry Typed(string? code, string explanation, string consequence, string fix, string example) => new()
    {
        Codes = code is null ? [] : [code],
        ExceptionTypes = [TypeScriptCompilerParser.TypeError],
        Guide = new MistakeGuide(explanation, consequence, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Refused("ERR_INVALID_TYPESCRIPT_SYNTAX", null,
            "Node.js could not read the TypeScript here. It runs TypeScript by taking the types out, and to do that it reads the " +
            "whole file first - and here it found something TypeScript does not allow. It says what it expected, at the line it " +
            "marks: often a bracket left open, a comma missing, or a type written where a value goes.",
            "Read what Node expected and where its ^ points, and write it there - close the bracket, or put in the comma.",
            """
            const marks: number[] = [70, 80, 90];
            console.log(marks.length);
            """),

        Refused("ERR_UNSUPPORTED_TYPESCRIPT_SYNTAX", @"enum is not supported",
            "An enum is not a type. TypeScript's own compiler turns it into code - an object that holds its members - and Node.js " +
            "runs TypeScript only by taking the types out, which leaves nothing in an enum's place, so it will not run the file.",
            "Write the members as an object of constants - const Grade = { Pass: \"pass\", Fail: \"fail\" } as const; - and Grade.Pass " +
            "works as before.",
            """
            const Grade = { Pass: "pass", Fail: "fail" } as const;
            const result: string = Grade.Pass;
            console.log(result);
            """),

        Refused("ERR_UNSUPPORTED_TYPESCRIPT_SYNTAX", @"namespace declaration is not supported",
            "A namespace is not a type. TypeScript's own compiler turns it into code that builds an object of its members, and " +
            "Node.js runs TypeScript only by taking the types out, so it will not run the file.",
            "Put the members in an object - const Marks = { best: 90 }; - or in a file of their own, which exports them.",
            """
            const Marks = { best: 90 };
            console.log(Marks.best);
            """),

        Refused("ERR_UNSUPPORTED_TYPESCRIPT_SYNTAX", @"parameter property is not supported",
            "constructor(public name: string) declares a field and fills it in, in one go - code TypeScript's own compiler writes. " +
            "Node.js runs TypeScript only by taking the types out, so it will not run the file.",
            "Declare the field in the class, and give it its value in the constructor: this.name = name.",
            """
            class Student {
              name: string;
              constructor(name: string) {
                this.name = name;
              }
            }
            console.log(new Student("Ada").name);
            """),

        Refused("ERR_UNSUPPORTED_TYPESCRIPT_SYNTAX", null,
            "This part of TypeScript is not a type: TypeScript's own compiler turns it into code of its own. Node.js runs TypeScript " +
            "only by taking the types out, which leaves nothing in its place, so it will not run the file.",
            "Write the same thing with plain JavaScript and types - an object of constants for an enum, a field and an assignment " +
            "for a parameter property.",
            """
            const best: number = 90;
            console.log(best);
            """),

        Typed("TS2322",
            "TypeScript's compiler found a value of one type given to a name declared to hold another - text given to a number, as " +
            "in let total: number = \"5\". The type after the colon says what the name may hold, and the value does not fit it.",
            "Node.js takes the types out and runs the program anyway, so the name holds the wrong kind of value: \"5\" + 1 is \"51\", " +
            "not 6.",
            "Give it a value of the type it is declared as - Number(\"5\") turns text into a number - or declare the type the value is.",
            """
            const total: number = Number("5");
            console.log(total + 1);
            """),

        Typed("TS2345",
            "A function is given a value of a type its parameter does not take - text where it takes a number. Each parameter's type " +
            "says what may be passed to it.",
            "The function runs with a value it was not written for, so what it works out is wrong, or it stops part way through.",
            "Pass a value of the parameter's type - Number(\"21\") for a number - or change the parameter's type if the function is " +
            "meant to take this one.",
            """
            function double(value: number): number {
              return value * 2;
            }

            console.log(double(Number("21")));
            """),

        Typed("TS2551",
            "The property named is not one this type has, and TypeScript's compiler names the one it thinks was meant - lenght for " +
            "length. Most often it is a letter out of place.",
            "When the program runs, the misspelt property is undefined, so whatever uses it goes wrong, with no error at this line.",
            "Write the name the compiler suggests.",
            """
            const marks: number[] = [70, 80, 90];
            console.log(marks.length);
            """),

        Typed("TS2339",
            "The property named is not one this type has - size on an array, whose count is length. TypeScript knows every property " +
            "a type has, so it knows this one is not there.",
            "When the program runs, the property is undefined, so whatever uses it goes wrong, with no error at this line.",
            "Use the property the type has for this - its name is in the type's documentation - or add the property to your own type.",
            """
            const marks: number[] = [70, 80, 90];
            console.log(marks.length);
            """),

        Typed("TS2552",
            "The name used here is not one the program declares, and TypeScript's compiler names the one it thinks was meant - mraks " +
            "for marks.",
            "When the program reaches this line, Node.js stops it with ReferenceError: the name is not defined.",
            "Write the name the compiler suggests.",
            """
            const marks: number[] = [70, 80, 90];
            console.log(marks);
            """),

        Typed("TS2304",
            "The name used here is not declared anywhere the program can see - with let, const, function or an import - and no name " +
            "close to it is either.",
            "When the program reaches this line, Node.js stops it with ReferenceError: the name is not defined.",
            "Declare the name before it is used, import it from where it is, or correct its spelling.",
            """
            const total = 70 + 80;
            console.log(total);
            """),

        Typed("TS18047",
            "This value can be null - document.getElementById, a search, or a type written with | null can give null - and it is " +
            "used as if it could not be. TypeScript's strict checks make the program say what happens when it is null.",
            "When the value is null, the program stops here with TypeError: Cannot read properties of null.",
            "Check for null first - if (found !== null) - and use the value inside the check.",
            """
            const found: string | null = ["Ada", "Bob"].includes("Ada") ? "Ada" : null;
            if (found !== null) {
              console.log(found.toUpperCase());
            }
            """),

        Typed("TS18048",
            "This value can be undefined - find gives undefined when nothing matches, and an optional value may not be there - and it " +
            "is used as if it were always there.",
            "When the value is undefined, the program stops here with TypeError: Cannot read properties of undefined.",
            "Check for undefined first - if (top !== undefined) - and use the value inside the check.",
            """
            const marks: number[] = [70, 80, 90];
            const top = marks.find((mark) => mark > 85);
            if (top !== undefined) {
              console.log(top.toFixed(1));
            }
            """),

        Typed("TS7006",
            "A parameter has no type, and nothing tells TypeScript what it is, so it is any - anything at all - and nothing about it " +
            "is checked. Strict TypeScript, as tsc --init sets a project up, asks for its type.",
            "Nothing passed to this parameter is checked, so a wrong value - text for a number - reaches the function without a word.",
            "Give the parameter its type: values: number[].",
            """
            function average(values: number[]): number {
              return values.reduce((sum, value) => sum + value, 0) / values.length;
            }

            console.log(average([70, 80, 90]));
            """),

        Typed("TS7053",
            "An object is looked up with a string that could be any text - scores[who] - but the object's type has only the keys " +
            "written in it, so TypeScript cannot say what the lookup gives.",
            "For a key the object does not have, the lookup gives undefined, and what uses it goes wrong.",
            "Give the object a type that takes any string as a key - Record<string, number> - or look it up only with a key it has.",
            """
            const scores: Record<string, number> = { Ada: 36, Bob: 20 };
            const who: string = "Ada";
            console.log(scores[who]);
            """),

        Typed("TS2741",
            "An object is given a type, but leaves out a property the type says it must have - a Student with no mark.",
            "When the program uses the missing property, it is undefined, so what is worked out from it is wrong.",
            "Give the object every property its type has, or mark the property optional in the type - mark?: number.",
            """
            interface Student {
              name: string;
              mark: number;
            }

            const ada: Student = { name: "Ada", mark: 90 };
            console.log(ada.name, ada.mark);
            """),

        Typed("TS2353",
            "An object written out in full has a property its type does not - age on a Student, which has only name and mark. Most " +
            "often it is misspelt, or belongs to another type.",
            "The property is kept, but nothing that uses the type can see it, so whatever it was meant for never happens.",
            "Take the property out, correct its name, or add it to the type.",
            """
            interface Student {
              name: string;
              mark: number;
            }

            const grace: Student = { name: "Grace", mark: 90 };
            console.log(grace.name);
            """),

        Typed("TS2367",
            "Two values are compared that can never be equal, because their types have nothing in common - a number with text, as " +
            "in mark === \"90\".",
            "The comparison is always false - or, with !==, always true - so one branch of the if never runs.",
            "Compare values of the same type: mark === 90.",
            """
            const mark: number = 90;
            if (mark === 90) {
              console.log("top");
            }
            """),

        Typed("TS2588",
            "A name declared with const is given a new value. const means it keeps the value it was declared with.",
            "When the program reaches this line, Node.js stops it with TypeError: Assignment to constant variable.",
            "Declare it with let if its value is meant to change.",
            """
            let count = 3;
            count = 4;
            console.log(count);
            """),

        Typed("TS2554",
            "A function is called with more or fewer values than it has parameters - greet() for a function that takes a name.",
            "A parameter left out is undefined inside the function, and a value too many is ignored, so the result is not what was meant.",
            "Pass one value for each parameter, in order - or give a parameter a default, name = \"there\", if it may be left out.",
            """
            function greet(name: string): string {
              return "Hello, " + name;
            }

            console.log(greet("Ada"));
            """),

        Typed("TS2362",
            "Arithmetic - *, /, - or % - is done with a value that is not a number, such as text: \"Total: \" * 2.",
            "The answer is NaN - not a number - and everything worked out from it is NaN too.",
            "Turn the value into a number first, with Number(...), or join text with + and a number written into it.",
            """
            const price = Number("2.5");
            console.log(price * 2);
            """),

        Typed("TS2365",
            "An operator is used between two types it does not work on - + between true and a number, or < between text and a " +
            "number.",
            "The value worked out is not what was meant - true + 5 is 6 - with no error to say so.",
            "Turn the values into the type the operator is meant for first - Number(passed) for a true or false.",
            """
            const passed = true;
            console.log(Number(passed) + 5);
            """),

        Typed("TS2349",
            "A value that is not a function is called, with brackets after it - word() for text.",
            "When the program reaches this line, Node.js stops it with TypeError: it is not a function.",
            "Call a method of the value instead - word.toUpperCase() - or call the function that was meant.",
            """
            const word = "hello";
            console.log(word.toUpperCase());
            """),

        Typed("TS2448",
            "A name declared with let or const is used before the line that declares it. Until that line runs, the name cannot be " +
            "used at all.",
            "When the program reaches this line, Node.js stops it with ReferenceError: Cannot access the name before initialization.",
            "Move the declaration above the first line that uses the name.",
            """
            const later = 5;
            console.log(later);
            """),

        Typed("TS2366",
            "A function says it gives back a value - : string - but there is a way through it that ends without a return.",
            "On that way through, the function gives back undefined, and whatever uses its result goes wrong.",
            "Return a value at the end of the function too - or on every way through it.",
            """
            function grade(mark: number): string {
              if (mark > 50) {
                return "pass";
              }
              return "fail";
            }

            console.log(grade(70));
            """),

        Typed(null,
            "TypeScript's compiler, tsc, found a value whose type does not fit how it is used here. Its message says which types it " +
            "compared, and where.",
            "Node.js takes the types out and runs the program anyway, so the value is not what the code expects, and the program can " +
            "go wrong where it is used.",
            "Read tsc's message: it names the type the value has and the type that was needed. Make the two agree.",
            """
            const total: number = 70 + 80;
            console.log(total);
            """),
    ];
}
