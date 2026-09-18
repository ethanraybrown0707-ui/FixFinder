using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

internal static class JavaScriptGuides
{
    private const string NothingRuns = "Node reads the whole file before running it, so while this is wrong none of the file runs.";

    private static GuideEntry Entry(string type, string pattern, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = [type],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Entry("SyntaxError", @"missing \) after argument list|Unexpected token|Unexpected end of input|Invalid or unexpected token|Unexpected identifier|Unexpected string|Unexpected number",
            "Node could not read the code here: a bracket, brace, comma or quote is missing or extra, or a word is where it cannot go.",
            NothingRuns,
            "Look at the place named and the line before it, and balance the brackets and quotes.",
            """
            console.log("Total:", total);
            """),

        Entry("SyntaxError", @"has already been declared",
            "The same name is declared twice with let or const in the same block.",
            NothingRuns,
            "Rename one, or assign to the existing variable without let or const.",
            """
            let count = 0;
            count = 5;
            """),

        Entry("SyntaxError", @"await is only valid in async functions",
            "await is used inside a function that is not marked async.",
            NothingRuns,
            "Mark the function async.",
            """
            async function load() {
              const response = await fetch(url);
              return response.json();
            }
            """),

        Entry("SyntaxError", @"Cannot use import statement outside a module|require is not defined in ES module scope",
            "The file mixes the two module systems: import needs an ES module, require needs CommonJS.",
            NothingRuns,
            "Use one system throughout: rename the file to .mjs for import, or use require in a .js file.",
            """
            const fs = require("fs");
            """),

        Entry("SyntaxError", @"does not provide an export named",
            "The module being imported from has no export with this name - it is misspelt, or exported as the default.",
            "The program stops before any of it runs.",
            "Match the name the module exports, or import its default export without braces.",
            """
            import { formatPrice } from "./format.js";
            """),

        Entry("ReferenceError", @"is not defined",
            "The code uses a name that does not exist at this point - it is misspelt, declared in another block, or never imported.",
            "The program crashes at this line.",
            "Check the spelling against the declaration, declare it where this line can see it, or require it.",
            """
            const total = prices.reduce((sum, price) => sum + price, 0);
            console.log(total);
            """),

        Entry("ReferenceError", @"before initialization",
            "A let or const variable is used above the line that declares it.",
            "The program crashes at this line.",
            "Move the declaration above its first use.",
            """
            const rate = 0.2;
            console.log(price * rate);
            """),

        Entry("TypeError", @"Cannot read propert(?:y|ies) of (?:undefined|null)",
            "The code reads a property of a value that is undefined or null - usually a missing object, an index past the end, or a function that returned nothing.",
            "The program crashes at this line.",
            "Find out why the value is missing, and check it first - or use ?. to read the property only when the value exists.",
            """
            const city = user?.address?.city ?? "unknown";
            """),

        Entry("TypeError", @"is not a function",
            "Something is called with brackets, but it is not a function - the name is misspelt, the method belongs to another type, or it is a property.",
            "The program crashes at this line.",
            "Check the spelling and what type the value really is.",
            """
            const names = ["b", "a"];
            names.sort();
            """),

        Entry("TypeError", @"Assignment to constant variable",
            "A variable declared with const is given a new value.",
            "The program crashes at this line.",
            "Declare it with let if its value is meant to change.",
            """
            let total = 0;
            total += 5;
            """),

        Entry("TypeError", @"is not iterable",
            "for...of or spreading is used on something that is not a list, string, Map or Set.",
            "The program crashes at this line.",
            "Loop over an array, or use Object.entries(object) for an object.",
            """
            for (const [key, value] of Object.entries(prices)) {
              console.log(key, value);
            }
            """),

        Entry("TypeError", @"Class constructor .* cannot be invoked without 'new'",
            "A class is called like a function without new.",
            "The program crashes at this line.",
            "Put new in front of the class name.",
            """
            const account = new Account("Ada");
            """),

        Entry("RangeError", @"Maximum call stack size exceeded",
            "A function keeps calling itself without ever reaching a case that stops it.",
            "The program runs out of stack space and crashes.",
            "Add a base case that returns without calling the function again.",
            """
            function factorial(n) {
              return n <= 1 ? 1 : n * factorial(n - 1);
            }
            """),

        Entry("RangeError", @"Invalid array length",
            "An array is created with a negative or fractional length.",
            "The program crashes at this line.",
            "Make sure the length is a whole number of zero or more.",
            """
            const slots = new Array(Math.max(0, Math.floor(count)));
            """),

        Entry("Error", @"Cannot find module",
            "The module required here is not installed, or the relative path to your own file is wrong.",
            "The program stops on its first lines.",
            "Install the package with npm, or fix the path - your own files start with ./",
            """
            const helper = require("./helper.js");
            """),
    ];
}
