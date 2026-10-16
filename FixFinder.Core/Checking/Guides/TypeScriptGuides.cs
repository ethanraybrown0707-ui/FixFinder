using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What Node.js says of a TypeScript file it cannot run by stripping its types - each explained for someone new to
/// programming. Once its types are gone a TypeScript program is JavaScript, so what stops it running is JavaScript's, and
/// JavaScript's guides explain it.
/// </summary>
/// <remarks>
/// Every message matched here is one Node.js 24.19 was seen to give, in its own words. Every example is a whole program, and
/// TypeScriptLiveTests checks each one with FixFinder's check and runs it with Node.js, and fails on anything Node says.
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
    ];
}
