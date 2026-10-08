using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Node's errors, each explained for someone new to programming.
/// </summary>
internal static class JavaScriptGuides
{
    private const string NothingRuns = "Node reads the whole file before running it, so while this is wrong none of the file runs.";

    private static GuideEntry Entry(
        string type, string pattern, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = [type],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Entry("SyntaxError", @"missing \) after argument list|Unexpected token|Unexpected end of input|Invalid or unexpected token|Unexpected identifier|Unexpected string|Unexpected number",
            "Node reads the whole file before running it, and could not make sense of the code at this point. Usually " +
            "something small is off: a bracket, brace or quote that is never closed, a missing comma between items, or a word " +
            "in a place it cannot go.",
            NothingRuns,
            "Look at the place named and the line before it, and balance the brackets and quotes.",
            """
            console.log("Total:", total);
            """),

        Entry("SyntaxError", @"has already been declared",
            "let and const each make a new name, and a name can only be made once in the same block. To change a variable " +
            "that already exists, leave out let or const and just assign to it.",
            NothingRuns,
            "Rename one, or assign to the existing variable without let or const.",
            """
            let count = 0;
            count = 5;
            """),

        Entry("SyntaxError", @"await is only valid in async functions",
            "await means 'wait here for this to finish without freezing everything else'. It only works inside a function " +
            "marked async, because JavaScript has to be able to pause that function and carry on with it later.",
            NothingRuns,
            "Mark the function async.",
            """
            async function load() {
              const response = await fetch(url);
              return response.json();
            }
            """),

        Entry("SyntaxError", @"Cannot use import statement outside a module|require is not defined in ES module scope",
            "Node has two ways of splitting a program into files: the newer import and export, and the older require. Each " +
            "file uses one or the other, and this one uses the kind its type of file does not allow.",
            NothingRuns,
            "Use one system throughout: rename the file to .mjs for import, or use require in a .js file.",
            """
            const fs = require("fs");
            """),

        Entry("SyntaxError", @"does not provide an export named",
            "import { name } takes one named thing out of another file. That file exports nothing by this name - check the " +
            "spelling, or it may be the file's default export, which is imported without braces.",
            "The program stops before any of it runs.",
            "Match the name the module exports, or import its default export without braces.",
            """
            import { formatPrice } from "./format.js";
            """),

        Entry("ReferenceError", @"is not defined",
            "A name has to be declared - with let, const, var, function, import or require - before the program can use it. " +
            "This name is not declared anywhere this line can see: it may be spelt differently, capitals included, or live " +
            "inside another block.",
            "The program crashes at this line.",
            "Check the spelling against the declaration, declare it where this line can see it, or require it.",
            """
            const total = prices.reduce((sum, price) => sum + price, 0);
            console.log(total);
            """),

        Entry("ReferenceError", @"before initialization",
            "A variable made with let or const exists from the start of its block, but cannot be used until the line that " +
            "declares it has run. This line uses it above that point.",
            "The program crashes at this line.",
            "Move the declaration above its first use.",
            """
            const rate = 0.2;
            console.log(price * rate);
            """),

        Entry("TypeError", @"Cannot read propert(?:y|ies) of (?:undefined|null)",
            "undefined and null both mean 'no value'. The code asks a no-value for one of its parts - user.name when user is " +
            "undefined - and there is nothing there to read. Often a function returned nothing, or an index went past the end " +
            "of a list.",
            "The program crashes at this line.",
            "Find out why the value is missing, and check it first - or use ?. to read the property only when the value exists.",
            """
            const city = user?.address?.city ?? "unknown";
            """),

        Entry("TypeError", @"is not a function",
            "Round brackets after something mean 'run it'. This is not a function: it may be spelt differently, belong to " +
            "another kind of value - arrays have sort, strings do not - or be a plain value rather than a method.",
            "The program crashes at this line.",
            "Check the spelling and what type the value really is.",
            """
            const names = ["b", "a"];
            names.sort();
            """),

        Entry("TypeError", @"Assignment to constant variable",
            "const means the variable always refers to the same value, so it cannot be given a new one. If the value is meant " +
            "to change, declare it with let.",
            "The program crashes at this line.",
            "Declare it with let if its value is meant to change.",
            """
            let total = 0;
            total += 5;
            """),

        Entry("TypeError", @"is not iterable",
            "for...of and the spread ... go through the items of something that has items in order - an array, a string, a " +
            "Map or a Set. A plain object is not like that, and neither is undefined; Object.entries turns an object's " +
            "contents into a list you can loop over.",
            "The program crashes at this line.",
            "Loop over an array, or use Object.entries(object) for an object.",
            """
            for (const [key, value] of Object.entries(prices)) {
              console.log(key, value);
            }
            """),

        Entry("TypeError", @"Class constructor .* cannot be invoked without 'new'",
            "A class is a blueprint for making objects, and new is how you make one. Calling the class like an ordinary " +
            "function, without new, is not allowed.",
            "The program crashes at this line.",
            "Put new in front of the class name.",
            """
            const account = new Account("Ada");
            """),

        Entry("RangeError", @"Maximum call stack size exceeded",
            "Each function call takes a little memory until it finishes. A function that keeps calling itself without stopping " +
            "keeps taking more, until the space runs out and the program crashes.",
            "The program runs out of stack space and crashes.",
            "Add a base case that returns without calling the function again.",
            """
            function factorial(n) {
              return n <= 1 ? 1 : n * factorial(n - 1);
            }
            """),

        Entry("RangeError", @"Invalid array length",
            "An array's length has to be a whole number, zero or more. The length given here is negative, has a fraction, or is " +
            "far too big.",
            "The program crashes at this line.",
            "Make sure the length is a whole number of zero or more.",
            """
            const slots = new Array(Math.max(0, Math.floor(count)));
            """),

        Entry("Error", @"Cannot find module",
            "require and import look for the file or package you name. Node could not find this one: a package may not be " +
            "installed yet, or the path to one of your own files is wrong - your own files start with ./ or ../.",
            "The program stops on its first lines.",
            "Install the package with npm, or fix the path - your own files start with ./",
            """
            const helper = require("./helper.js");
            """),
    ];
}
