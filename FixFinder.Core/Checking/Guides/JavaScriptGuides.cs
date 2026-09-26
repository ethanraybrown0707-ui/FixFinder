using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Node's errors, each explained three ways: for someone new to programming, as it is usually taught, and in the
/// language's own terms.
/// </summary>
internal static class JavaScriptGuides
{
    private const string NothingRuns = "Node reads the whole file before running it, so while this is wrong none of the file runs.";

    private static GuideEntry Entry(
        string type, string pattern, string beginner, string explanation, string technical, string why, string fix, string example) => new()
    {
        ExceptionTypes = [type],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example) { ForBeginners = beginner, ForTechnical = technical },
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Entry("SyntaxError", @"missing \) after argument list|Unexpected token|Unexpected end of input|Invalid or unexpected token|Unexpected identifier|Unexpected string|Unexpected number",
            "Node reads the whole file before running it, and could not make sense of the code at this point. Usually " +
            "something small is off: a bracket, brace or quote that is never closed, a missing comma between items, or a word " +
            "in a place it cannot go.",
            "Node could not read the code here: a bracket, brace, comma or quote is missing or extra, or a word is where it cannot go.",
            "The parser met a token the ECMAScript grammar does not allow here - after an unbalanced bracket or quote, or where " +
            "a comma or operator is missing - and V8 reports the first token it could not use, which is often after the real " +
            "mistake. Automatic semicolon insertion can move the reported place too.",
            NothingRuns,
            "Look at the place named and the line before it, and balance the brackets and quotes.",
            """
            console.log("Total:", total);
            """),

        Entry("SyntaxError", @"has already been declared",
            "let and const each make a new name, and a name can only be made once in the same block. To change a variable " +
            "that already exists, leave out let or const and just assign to it.",
            "The same name is declared twice with let or const in the same block.",
            "let, const and class may not declare a name already bound in the same scope - by var, let, const, function, class " +
            "or a parameter - and this is checked when the script is parsed, before anything runs. Only var may be declared " +
            "twice.",
            NothingRuns,
            "Rename one, or assign to the existing variable without let or const.",
            """
            let count = 0;
            count = 5;
            """),

        Entry("SyntaxError", @"await is only valid in async functions",
            "await means 'wait here for this to finish without freezing everything else'. It only works inside a function " +
            "marked async, because JavaScript has to be able to pause that function and carry on with it later.",
            "await is used inside a function that is not marked async.",
            "await is a keyword only inside async functions and, in ES modules, at the top level (ES2022); anywhere else it is " +
            "a syntax error. An async function returns a Promise and is suspended at each await until the awaited promise settles.",
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
            "The file mixes the two module systems: import needs an ES module, require needs CommonJS.",
            "Node treats .mjs files, and .js files under a package.json with \"type\": \"module\", as ES modules, where import is " +
            "allowed and require is not defined; .cjs files and other .js files are CommonJS, where an import statement is a " +
            "syntax error. A dynamic import() works in both.",
            NothingRuns,
            "Use one system throughout: rename the file to .mjs for import, or use require in a .js file.",
            """
            const fs = require("fs");
            """),

        Entry("SyntaxError", @"does not provide an export named",
            "import { name } takes one named thing out of another file. That file exports nothing by this name - check the " +
            "spelling, or it may be the file's default export, which is imported without braces.",
            "The module being imported from has no export with this name - it is misspelt, or exported as the default.",
            "ES module imports are linked before any code runs: a named import must match one of the target module's export " +
            "names exactly, capitals included, and a default export is imported without braces. A mismatch is a SyntaxError " +
            "when the modules are linked.",
            "The program stops before any of it runs.",
            "Match the name the module exports, or import its default export without braces.",
            """
            import { formatPrice } from "./format.js";
            """),

        Entry("ReferenceError", @"is not defined",
            "A name has to be declared - with let, const, var, function, import or require - before the program can use it. " +
            "This name is not declared anywhere this line can see: it may be spelt differently, capitals included, or live " +
            "inside another block.",
            "The code uses a name that does not exist at this point - it is misspelt, declared in another block, or never imported.",
            "Resolving the identifier walked the scope chain out to the global object without finding a binding, so reading it " +
            "throws ReferenceError. let and const are block-scoped, and in strict mode assigning to an undeclared name throws " +
            "too, rather than creating a global.",
            "The program crashes at this line.",
            "Check the spelling against the declaration, declare it where this line can see it, or require it.",
            """
            const total = prices.reduce((sum, price) => sum + price, 0);
            console.log(total);
            """),

        Entry("ReferenceError", @"before initialization",
            "A variable made with let or const exists from the start of its block, but cannot be used until the line that " +
            "declares it has run. This line uses it above that point.",
            "A let or const variable is used above the line that declares it.",
            "let, const and class bindings are hoisted but stay uninitialised until their declaration is evaluated - the " +
            "temporal dead zone - and any use before then throws ReferenceError, where a var would read as undefined.",
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
            "The code reads a property of a value that is undefined or null - usually a missing object, an index past the end, or a function that returned nothing.",
            "Reading a property of undefined or null throws TypeError, since they are the only values with no properties; " +
            "optional chaining, ?., gives undefined instead. A missing property, an index out of range and a function without " +
            "a return all produce undefined.",
            "The program crashes at this line.",
            "Find out why the value is missing, and check it first - or use ?. to read the property only when the value exists.",
            """
            const city = user?.address?.city ?? "unknown";
            """),

        Entry("TypeError", @"is not a function",
            "Round brackets after something mean 'run it'. This is not a function: it may be spelt differently, belong to " +
            "another kind of value - arrays have sort, strings do not - or be a plain value rather than a method.",
            "Something is called with brackets, but it is not a function - the name is misspelt, the method belongs to another type, or it is a property.",
            "A call needs a callable callee; this expression evaluated to undefined or another value that is not a function - " +
            "often a misspelt method, or one that exists on a different type's prototype.",
            "The program crashes at this line.",
            "Check the spelling and what type the value really is.",
            """
            const names = ["b", "a"];
            names.sort();
            """),

        Entry("TypeError", @"Assignment to constant variable",
            "const means the variable always refers to the same value, so it cannot be given a new one. If the value is meant " +
            "to change, declare it with let.",
            "A variable declared with const is given a new value.",
            "A const binding cannot be reassigned, and trying to throws TypeError when the line runs. const does not freeze the " +
            "value itself: an object or array held in a const can still be changed.",
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
            "for...of or spreading is used on something that is not a list, string, Map or Set.",
            "for...of, spread and array destructuring call the value's [Symbol.iterator] method, and plain objects, numbers, " +
            "undefined and null have none, so a TypeError is thrown. Object.keys, Object.values and Object.entries return " +
            "arrays, which are iterable.",
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
            "A class is called like a function without new.",
            "A class constructor throws TypeError when called as a function; it can only be invoked to construct, with new or " +
            "super(). A function declared with function can be called either way.",
            "The program crashes at this line.",
            "Put new in front of the class name.",
            """
            const account = new Account("Ada");
            """),

        Entry("RangeError", @"Maximum call stack size exceeded",
            "Each function call takes a little memory until it finishes. A function that keeps calling itself without stopping " +
            "keeps taking more, until the space runs out and the program crashes.",
            "A function keeps calling itself without ever reaching a case that stops it.",
            "Each call pushes a frame onto V8's fixed-size stack, and unbounded recursion overflows it, so V8 throws RangeError. " +
            "Node does not implement proper tail calls, so tail recursion counts too.",
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
            "An array is created with a negative or fractional length.",
            "new Array(n) with one numeric argument, and setting length, need a whole number from 0 to 2^32 - 1; any other " +
            "number throws RangeError.",
            "The program crashes at this line.",
            "Make sure the length is a whole number of zero or more.",
            """
            const slots = new Array(Math.max(0, Math.floor(count)));
            """),

        Entry("Error", @"Cannot find module",
            "require and import look for the file or package you name. Node could not find this one: a package may not be " +
            "installed yet, or the path to one of your own files is wrong - your own files start with ./ or ../.",
            "The module required here is not installed, or the relative path to your own file is wrong.",
            "Node resolves a specifier starting with ./, ../ or / as a path relative to the importing file, and a bare name as " +
            "a package, searched for in node_modules folders up the directory tree. require may add .js, .json or .node to a " +
            "path, but an ES module import needs the full file name, extension included.",
            "The program stops on its first lines.",
            "Install the package with npm, or fix the path - your own files start with ./",
            """
            const helper = require("./helper.js");
            """),
    ];
}
