using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// The JavaScript mistakes of a beginner, of someone arriving from Python, Java or C#, and of later years - each rule's fix and
/// refusals from Node's own words, and what the Node parser now reads above the stack.
/// </summary>
/// <remarks>Cases that ask Node itself for names return early when Node is not installed, and a skip looks like a pass.</remarks>
public class JsRulesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static ParsedError Error(string type, string message, string? file, int line) => new()
    {
        LanguageId = "node",
        Confidence = 88,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = file is null ? [] : [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private LocalFix? Fix(string rule, ParsedError error) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });

    private static bool HasNode => TargetFactory.FindOnPath("node") is not null;

    private const string FieldWithoutThis = "class Counter {\n  constructor() {\n    this.count = 0;\n  }\n  increment() {\n    count++;\n  }\n}\n";
    private const string MethodWithoutThis = "class Greeter {\n  format(name) {\n    return \"Hello \" + name;\n  }\n  greet(name) {\n    return format(name);\n  }\n}\n";
    private const string ConstructorWithoutThis = "class Dog {\n  constructor(name) {\n    name = name;\n  }\n}\nconst d = new Dog(\"Rex\");\nconsole.log(d.name.toUpperCase());\n";
    private const string SuperMissing = "class Animal {\n  constructor(name) {\n    this.name = name;\n  }\n}\nclass Dog extends Animal {\n  constructor(name) {\n    this.sound = \"woof\";\n  }\n}\n";
    private const string Getter = "class Circle {\n  constructor(r) {\n    this.r = r;\n  }\n  get area() {\n    return Math.PI * this.r ** 2;\n  }\n}\nconsole.log(new Circle(2).area());\n";
    private const string Static = "class Dog {\n  static create(name) {\n    return new Dog(name);\n  }\n  constructor(name) {\n    this.name = name;\n  }\n}\nconst d = new Dog(\"Rex\");\nconsole.log(d.create(\"Fido\").name);\n";
    private const string NotAwaited = "async function load() {\n  return [1, 2, 3];\n}\n\nasync function main() {\n  const items = load();\n  console.log(items.map((x) => x * 2));\n}\n";
    private const string MapBracket = "const ages = new Map();\nages[\"Ada\"] = 36;\nconsole.log(ages.get(\"Ada\").toFixed(0));\n";
    private const string ThisInCallback = "class Timer {\n  constructor() {\n    this.ticks = 0;\n  }\n  start() {\n    [1, 2, 3].forEach(function () {\n      this.ticks++;\n    });\n  }\n}\n";
    private const string Detached = "class Counter {\n  constructor() {\n    this.count = 0;\n  }\n  increment() {\n    this.count++;\n  }\n}\nconst c = new Counter();\nconst inc = c.increment;\ninc();\n";
    private const string Setter = "class Person {\n  set name(value) {\n    this.name = value;\n  }\n}\n";

    [Theory]
    [InlineData("js-missing-closing-paren", "SyntaxError", "missing ) after argument list", "const total = 5;\nconsole.log(\"Total:\", total;\n", 2, "console.log(\"Total:\", total);")]
    [InlineData("js-apostrophe", "SyntaxError", "missing ) after argument list", "console.log('It's here');\n", 1, "console.log(\"It's here\");")]
    [InlineData("js-unclosed-string", "SyntaxError", "Invalid or unexpected token", "console.log(\"hello);\n", 1, "console.log(\"hello\");")]
    [InlineData("js-extra-closing-brace", "SyntaxError", "Unexpected token '}'", "function greet(name) {\n  console.log(\"Hello \" + name);\n}\n}\ngreet(\"Ada\");\n", 4, "")]
    [InlineData("js-elif", "SyntaxError", "Unexpected token '{'", "} elif (x === 2) {\n", 1, "} else if (x === 2) {")]
    [InlineData("js-condition-parentheses", "SyntaxError", "Unexpected identifier 'x'", "if x > 3 {\n", 1, "if (x > 3) {")]
    [InlineData("js-for-of", "SyntaxError", "Unexpected identifier 'item'", "for item in items {\n", 1, "for (const item of items) {")]
    [InlineData("js-for-of", "SyntaxError", "Missing initializer in const declaration", "for (const item : items) {\n", 1, "for (const item of items) {")]
    [InlineData("js-thin-arrow", "SyntaxError", "Unexpected token '>'", "const double = (x) -> x * 2;\n", 1, "const double = (x) => x * 2;")]
    [InlineData("js-keyword-typo", "SyntaxError", "Unexpected identifier 'greet'", "fucntion greet(name) {\n", 1, "function greet(name) {")]
    [InlineData("js-keyword-typo", "SyntaxError", "Unexpected identifier 'greet'", "def greet(name) {\n", 1, "function greet(name) {")]
    [InlineData("js-keyword-typo", "SyntaxError", "Unexpected identifier 'count'", "Let count = 1;\n", 1, "let count = 1;")]
    [InlineData("js-missing-comma", "SyntaxError", "Unexpected identifier 'age'", "const person = {\n  name: \"Ada\"\n  age: 36\n};\n", 3, "  name: \"Ada\",")]
    [InlineData("js-redeclared", "SyntaxError", "Identifier 'x' has already been declared", "let x = 1;\nlet x = 2;\n", 2, "x = 2;")]
    [InlineData("js-redeclared", "SyntaxError", "Identifier 'x' has already been declared", "const x = 1;\nlet x = 2;\n", 2, null)]
    [InlineData("js-await-outside-async", "SyntaxError", "await is only valid in async functions and the top level bodies of modules", "function main() {\n  await wait(10);\n}\n", 2, "async function main() {")]
    [InlineData("js-await-outside-async", "SyntaxError", "await is only valid in async functions and the top level bodies of modules", "  [1, 2].forEach((x) => {\n    const y = await load(x);\n  });\n", 2, "  [1, 2].forEach(async (x) => {")]
    [InlineData("js-foreign-print", "ReferenceError", "System is not defined", "System.out.println(\"hello\");\n", 1, "console.log(\"hello\");")]
    [InlineData("js-foreign-print", "ReferenceError", "print is not defined", "print(\"a\", b)\n", 1, "console.log(\"a\", b);")]
    [InlineData("js-foreign-print", "ReferenceError", "Console is not defined", "Console.WriteLine(\"{0}\", total);\n", 1, null)]
    [InlineData("js-foreign-word", "ReferenceError", "True is not defined", "const ready = True;\n", 1, "const ready = true;")]
    [InlineData("js-foreign-word", "ReferenceError", "None is not defined", "let result = None;\n", 1, "let result = null;")]
    [InlineData("js-len", "ReferenceError", "len is not defined", "console.log(len(items));\n", 1, "console.log(items.length);")]
    [InlineData("js-missing-this", "ReferenceError", "count is not defined", FieldWithoutThis, 6, "    this.count++;")]
    [InlineData("js-missing-this", "ReferenceError", "format is not defined", MethodWithoutThis, 6, "    return this.format(name);")]
    [InlineData("js-missing-this", "ReferenceError", "total is not defined", "function add() {\n  return total + 1;\n}\n", 2, null)]
    [InlineData("js-nearest-name", "ReferenceError", "totl is not defined", "const total = 5;\nconsole.log(totl);\n", 2, "console.log(total);")]
    [InlineData("js-nearest-name", "ReferenceError", "array is not defined", "const items = new array(3);\n", 1, "const items = new Array(3);")]
    [InlineData("js-class-without-new", "TypeError", "Class constructor Dog cannot be invoked without 'new'", "const d = Dog(\"Rex\");\n", 1, "const d = new Dog(\"Rex\");")]
    [InlineData("js-const-reassigned", "TypeError", "Assignment to constant variable.", "const count = 0;\ncount = count + 1;\n", 2, "let count = 0;")]
    [InlineData("js-const-reassigned", "TypeError", "Assignment to constant variable.", "for (const i = 0; i < 3; i++) {\n", 1, "for (let i = 0; i < 3; i++) {")]
    [InlineData("js-getter-called", "TypeError", "(intermediate value).area is not a function", Getter, 9, "console.log(new Circle(2).area);")]
    [InlineData("js-static-on-instance", "TypeError", "d.create is not a function", Static, 10, "console.log(Dog.create(\"Fido\").name);")]
    [InlineData("js-module-exports-typo", "TypeError", "module.exports.add is not a function", "module.export = { add: (a, b) => a + b };\nconsole.log(module.exports.add(1, 2));\n", 2, "module.exports = { add: (a, b) => a + b };")]
    [InlineData("js-promise-not-awaited", "TypeError", "items.map is not a function", NotAwaited, 7, "  const items = await load();")]
    [InlineData("js-array-called", "TypeError", "items is not a function", "const items = [1, 2, 3];\nconsole.log(items(0));\n", 2, "console.log(items[0]);")]
    [InlineData("js-member-not-function", "TypeError", "items.append is not a function", "const items = [];\nitems.append(1);\n", 2, "items.push(1);")]
    [InlineData("js-member-not-function", "TypeError", "items.size is not a function", "const items = [1, 2, 3];\nconsole.log(items.size());\n", 2, "console.log(items.length);")]
    [InlineData("js-member-not-function", "TypeError", "seen.push is not a function", "const seen = new Set();\nseen.push(1);\n", 2, "seen.add(1);")]
    [InlineData("js-member-not-function", "TypeError", "ages.get is not a function", "const ages = { Ada: 36 };\nconsole.log(ages.get(\"Ada\"));\n", 2, "console.log(ages[\"Ada\"]);")]
    [InlineData("js-member-not-function", "TypeError", "Math.squareRoot is not a function", "console.log(Math.squareRoot(16));\n", 1, "console.log(Math.sqrt(16));")]
    [InlineData("js-member-not-function", "TypeError", "d.bark is not a function", "const d = new Dog();\nd.bark();\n", 2, null)]
    [InlineData("js-reduce-without-initial", "TypeError", "Reduce of empty array with no initial value", "const total = prices.reduce((a, b) => a + b);\n", 1, "const total = prices.reduce((a, b) => a + b, 0);")]
    [InlineData("js-foreach-result", "TypeError", "Cannot read properties of undefined (reading 'length')", "const doubled = [1, 2, 3].forEach((x) => x * 2);\nconsole.log(doubled.length);\n", 2, "const doubled = [1, 2, 3].map((x) => x * 2);")]
    [InlineData("js-map-bracket", "TypeError", "Cannot read properties of undefined (reading 'toFixed')", MapBracket, 3, "ages.set(\"Ada\", 36);")]
    [InlineData("js-constructor-without-this", "TypeError", "Cannot read properties of undefined (reading 'toUpperCase')", ConstructorWithoutThis, 7, "    this.name = name;")]
    [InlineData("js-this-in-callback", "TypeError", "Cannot read properties of undefined (reading 'ticks')", ThisInCallback, 7, "    [1, 2, 3].forEach(() => {")]
    [InlineData("js-detached-method", "TypeError", "Cannot read properties of undefined (reading 'count')", Detached, 6, "const inc = c.increment.bind(c);")]
    [InlineData("js-super-missing", "ReferenceError", "Must call super constructor in derived class before accessing 'this' or returning from derived constructor", SuperMissing, 8, "    super(name);")]
    [InlineData("js-setter-recursion", "RangeError", "Maximum call stack size exceeded", Setter, 3, "    this._name = value;")]
    public void EachMistakeGetsItsFixOrARefusal(string rule, string type, string message, string source, int line, string? expected)
    {
        var file = Write("app.js", source);
        var fix = Fix(rule, Error(type, message, file, line));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    /// <summary>The brace goes where the indentation says the function ended, so the call written after it stays after it.</summary>
    [Fact]
    public void AMissingBraceGoesWhereTheBlocksIndentationEnds()
    {
        var file = Write("app.js", "function greet(name) {\n  console.log(\"Hello \" + name);\n\ngreet(\"Ada\");\n");

        var fix = Fix("js-missing-closing-brace", Error("SyntaxError", "Unexpected end of input", file, 5));

        Assert.NotNull(fix);
        Assert.Equal(3, fix!.StartLine);
        Assert.Equal("}", Assert.Single(fix.NewLines));
    }

    [Fact]
    public void RequireInAModuleBecomesAnImport()
    {
        var file = Write("app.mjs", "const fs = require(\"fs\");\n");

        var fix = Fix("js-require-in-module", Error("ReferenceError", "require is not defined in ES module scope, you can use import instead", file, 1));

        Assert.Equal("import fs from \"fs\";", Assert.Single(fix!.NewLines));
    }

    // ------------------------------------------------------------------ answered by Node itself

    /// <summary>
    /// Asked of Node when Node answers, and of the standard table when it does not - which a busy CI runner once made the
    /// difference between a fix and none.
    /// </summary>
    [Fact]
    public void AMisspeltStringMethodIsCorrected()
    {
        var file = Write("app.js", "const name = \"ada\";\nconsole.log(name.toUppercase());\n");
        var fix = Fix("js-member-not-function", Error("TypeError", "name.toUppercase is not a function", file, 2));

        Assert.NotNull(fix);
        Assert.Equal("console.log(name.toUpperCase());", Assert.Single(fix!.NewLines));
    }

    /// <summary>The fallback table may only name what Node really has, or a fix from it would not run.</summary>
    [Theory]
    [InlineData("String.prototype")]
    [InlineData("Array.prototype")]
    [InlineData("Set.prototype")]
    [InlineData("Map.prototype")]
    [InlineData("Math")]
    [InlineData("console")]
    [InlineData("builtins")]
    public void TheStandardTableOnlyNamesWhatNodeHas(string expression)
    {
        if (!HasNode) return;

        var node = JsRuntime.AskNode(expression);
        if (node.Count == 0) return;

        Assert.All(JsRuntime.Standard[expression], name => Assert.Contains(name, node));
    }

    /// <summary>A module a letter from one of Node's own is a typo - not a package called fss to install from a stranger.</summary>
    [Fact]
    public void AModuleALetterFromACoreOneIsATypo()
    {
        if (!HasNode) return;

        var file = Write("app.js", "const fs = require(\"fss\");\nconsole.log(fs.existsSync(\".\"));\n");
        var fix = Fix("js-core-module-typo", Error("Error", "Cannot find module 'fss'", file, 1));

        Assert.Equal("const fs = require(\"fs\");", Assert.Single(fix!.NewLines));
    }

    [Fact]
    public void AMisspeltNamedImportIsRenamedWithItsUses()
    {
        if (!HasNode) return;

        var file = Write("app.mjs", "import { readFileSnyc } from \"fs\";\nconsole.log(typeof readFileSnyc);\n");
        var fix = Fix("js-named-export-typo", Error("SyntaxError", "The requested module 'fs' does not provide an export named 'readFileSnyc'", file, 1));

        Assert.NotNull(fix);
        Assert.Equal("import { readFileSync } from \"fs\";|console.log(typeof readFileSync);", string.Join("|", fix!.NewLines));
    }

    // ------------------------------------------------------------------ the parser

    private static IReadOnlyList<CapturedLine> Lines(params string[] text) =>
        text.Select((line, i) => new CapturedLine(i, StreamKind.StdErr, line, TimeSpan.Zero)).ToList();

    /// <summary>A syntax error's stack is all Node's loader; the file and line are in the lines printed above it.</summary>
    [Fact]
    public void ASyntaxErrorIsPlacedByTheLineAboveTheMessage()
    {
        var error = new NodeStackTraceParser().Parse(Lines(
            @"C:\src\app.js:2",
            "console.log(\"Total:\", total;",
            "                      ^^^^^",
            "",
            "SyntaxError: missing ) after argument list",
            "    at wrapSafe (node:internal/modules/cjs/loader:1486:18)",
            "    at Module._compile (node:internal/modules/cjs/loader:1528:20)"));

        Assert.NotNull(error);
        Assert.Equal("SyntaxError", error!.ExceptionType);
        Assert.Equal(@"C:\src\app.js", error.Frames[0].File);
        Assert.Equal(2, error.Frames[0].Line);
        Assert.Equal(23, error.Frames[0].Column);
        Assert.Equal(1, error.Frames[1].Order);
    }

    /// <summary>A built-in's frame names no file - `at Array.reduce (&lt;anonymous&gt;)` - and the program's own frame comes after it.</summary>
    [Fact]
    public void AFrameWithNoFileDoesNotEndTheStack()
    {
        var error = new NodeStackTraceParser().Parse(Lines(
            @"C:\src\app.js:2",
            "const total = prices.reduce((a, b) => a + b);",
            "                     ^",
            "",
            "TypeError: Reduce of empty array with no initial value",
            "    at Array.reduce (<anonymous>)",
            @"    at Object.<anonymous> (C:\src\app.js:2:22)"));

        Assert.NotNull(error);
        Assert.Equal("node", error!.LanguageId);
        Assert.Contains(error.Frames, frame => frame.File == @"C:\src\app.js" && frame.Line == 2);
    }

    /// <summary>A missing module lists who asked for it before the stack; that list no longer hides the stack.</summary>
    [Fact]
    public void AMissingModuleIsReadPastItsRequireStack()
    {
        var error = new NodeStackTraceParser().Parse(Lines(
            "node:internal/modules/cjs/loader:1520",
            "  throw err;",
            "  ^",
            "",
            "Error: Cannot find module 'fss'",
            "Require stack:",
            @"- C:\src\app.js",
            "    at Module._resolveFilename (node:internal/modules/cjs/loader:1517:15)",
            @"    at Object.<anonymous> (C:\src\app.js:1:12)"));

        Assert.NotNull(error);
        Assert.Equal("node", error!.LanguageId);
        Assert.Equal("Cannot find module 'fss'", error.Message);
        Assert.Contains(error.Frames, frame => frame.File == @"C:\src\app.js" && frame.Line == 1);
    }
}
