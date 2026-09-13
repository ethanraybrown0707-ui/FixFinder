using System.Diagnostics;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Seventy more Python constructs, each broken the way people break it, and the fix for every one
/// whose message pins the answer down.
/// </summary>
/// <remarks>
/// Written after running all seventy through FixFinder and reading what Python really said. That run
/// also found two fixes already being offered that compiled and were wrong: <c>import this</c> for
/// <c>this.name</c>, and <c>print(self)</c> for Python's own <c>Did you mean: 'self.name'?</c>. Both
/// are pinned here.
/// <para>
/// The unit cases need nothing installed. The live ones need Python 3.12 or later, and skip without
/// it - a live case finishing in single milliseconds did not run.
/// </para>
/// </remarks>
public class PythonConstructTests : IDisposable
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

    private static ParsedError Error(string type, string message, string file, int line, string? raw = null) => new()
    {
        LanguageId = "python",
        Confidence = 90,
        RawText = raw ?? message,
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private string? Propose(string rule, ParsedError error)
    {
        var fix = LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, SourceRoot = _temp.Path });
        return fix is null ? null : string.Join("|", fix.NewLines);
    }

    // ------------------------------------------------------------------ every rule, and what it refuses

    [Theory]
    // Habits from other languages.
    [InlineData("python-this-for-self", "NameError", "name 'this' is not defined. Did you forget to import 'this'?", "class Dog:\n    def __init__(self, name):\n        this.name = name\n", 3, "        self.name = name")]
    [InlineData("python-this-for-self", "NameError", "name 'this' is not defined. Did you forget to import 'this'?", "this.name = 1\n", 1, null)]
    [InlineData("python-forgotten-import", "NameError", "name 'this' is not defined. Did you forget to import 'this'?", "this.name = 1\n", 1, null)]
    [InlineData("python-null", "NameError", "name 'null' is not defined", "value = null\n", 1, "value = None")]
    [InlineData("python-foreign-syntax", "SyntaxError", "invalid syntax", "count = 0\ncount++\n", 2, "count += 1")]
    [InlineData("python-foreign-syntax", "SyntaxError", "invalid syntax", "if x > 1 && x < 10:\n    pass\n", 1, "if x > 1 and x < 10:")]
    [InlineData("python-foreign-syntax", "SyntaxError", "invalid syntax", "if !ready || done:\n    pass\n", 1, "if not ready or done:")]
    [InlineData("python-foreign-syntax", "SyntaxError", "invalid syntax", "if a != b:\n    pass\n", 1, null)]
    [InlineData("python-foreign-syntax", "SyntaxError", "invalid syntax", "let total = 5\n", 1, "total = 5")]
    [InlineData("python-foreign-syntax", "SyntaxError", "invalid syntax", "dog = new Dog()\n", 1, "dog = Dog()")]
    [InlineData("python-foreign-syntax", "SyntaxError", "expected 'except' or 'finally' block", "try:\n    pass\ncatch ValueError:\n    pass\n", 3, "except ValueError:")]
    [InlineData("python-foreign-syntax", "SyntaxError", "invalid syntax", "print(\"a && b\") +\n", 1, null)]
    [InlineData("python-else-if", "SyntaxError", "invalid syntax", "if x:\n    pass\nelseif y:\n    pass\n", 3, "elif y:")]
    [InlineData("python-foreign-method", "AttributeError", "'str' object has no attribute 'length'", "print(text.length)\n", 1, "print(len(text))")]
    [InlineData("python-foreign-method", "AttributeError", "'list' object has no attribute 'size'", "print(items.size() + 1)\n", 1, "print(len(items) + 1)")]
    [InlineData("python-foreign-method", "AttributeError", "'list' object has no attribute 'push'", "items.push(3)\n", 1, "items.append(3)")]
    [InlineData("python-foreign-method", "AttributeError", "'str' object has no attribute 'toUpperCase'", "print(text.toUpperCase())\n", 1, "print(text.upper())")]
    [InlineData("python-foreign-method", "AttributeError", "'dict' object has no attribute 'has_key'", "if d.has_key(\"a\"):\n    pass\n", 1, "if \"a\" in d:")]
    [InlineData("python-foreign-method", "AttributeError", "'str' object has no attribute 'equals'", "if name.equals(\"Ethan\"):\n    pass\n", 1, "if name == \"Ethan\":")]
    [InlineData("python-foreign-method", "AttributeError", "'list' object has no attribute 'contains'", "found = items.contains(3) and ready\n", 1, "found = 3 in items and ready")]
    [InlineData("python-foreign-method", "AttributeError", "'list' object has no attribute 'contains'", "print(items.contains(3) + 1)\n", 1, "print((3 in items) + 1)")]
    [InlineData("python-foreign-method", "AttributeError", "'dict' object has no attribute 'iteritems'", "for k, v in d.iteritems():\n    pass\n", 1, "for k, v in d.items():")]
    // Python 2.
    [InlineData("python-except-comma", "SyntaxError", "multiple exception types must be parenthesized", "try:\n    pass\nexcept ValueError, e:\n    pass\n", 3, "except ValueError as e:")]
    [InlineData("python-except-comma", "SyntaxError", "multiple exception types must be parenthesized", "try:\n    pass\nexcept ValueError, TypeError:\n    pass\n", 3, "except (ValueError, TypeError):")]
    [InlineData("python-raise-string", "TypeError", "exceptions must derive from BaseException", "raise \"boom\"\n", 1, "raise Exception(\"boom\")")]
    [InlineData("python-2-to-3-name", "NameError", "name 'raw_input' is not defined", "name = raw_input(\"Name: \")\n", 1, "name = input(\"Name: \")")]
    [InlineData("python-print-redirect", "TypeError", "unsupported operand type(s) for >>: 'builtin_function_or_method' and '_io.TextIOWrapper'. Did you mean \"print(<message>, file=<output_stream>)\"?", "import sys\nprint >>sys.stderr, \"oops\"\n", 2, "print(\"oops\", file=sys.stderr)")]
    // Classes and functions.
    [InlineData("python-missing-self", "TypeError", "Dog.bark() takes 0 positional arguments but 1 was given", "class Dog:\n    def bark():\n        pass\n\nDog().bark()\n", 5, "    def bark(self):")]
    [InlineData("python-missing-self", "TypeError", "Dog.sit() takes 1 positional argument but 2 were given", "class Dog:\n    def sit(place):\n        pass\n\nDog().sit(1)\n", 5, "    def sit(self, place):")]
    [InlineData("python-missing-self", "TypeError", "Dog.sit() takes 1 positional argument but 3 were given", "class Dog:\n    def sit(place):\n        pass\n\nDog().sit(1, 2)\n", 5, null)]
    [InlineData("python-dunder-str-return", "TypeError", "__str__ returned non-string (type int)", "class Dog:\n    def __str__(self):\n        return 5\n\nprint(Dog())\n", 5, "        return str(5)")]
    [InlineData("python-def-without-parentheses", "SyntaxError", "expected '('", "def greet:\n    pass\n", 1, "def greet():")]
    [InlineData("python-global-assignment", "SyntaxError", "invalid syntax", "global count = 0\n", 1, "count = 0")]
    [InlineData("python-global-assignment", "SyntaxError", "invalid syntax", "def f():\n    global count = 0\n", 2, "    global count|    count = 0")]
    // Built-in types used the wrong way.
    [InlineData("python-range-for-int", "TypeError", "'int' object is not iterable", "for i in len(items):\n    pass\n", 1, "for i in range(len(items)):")]
    [InlineData("python-float-division", "TypeError", "list indices must be integers or slices, not float", "print(items[len(items) / 2])\n", 1, "print(items[len(items) // 2])")]
    [InlineData("python-float-division", "TypeError", "'float' object cannot be interpreted as an integer", "for i in range(n / 2):\n    pass\n", 1, "for i in range(n // 2):")]
    [InlineData("python-float-division", "TypeError", "list indices must be integers or slices, not float", "print(items[a / b / c])\n", 1, null)]
    [InlineData("python-join-non-strings", "TypeError", "sequence item 0: expected str instance, int found", "print(\", \".join(numbers))\n", 1, "print(\", \".join(map(str, numbers)))")]
    [InlineData("python-sorted-not-sort", "TypeError", "'NoneType' object is not iterable", "for n in numbers.sort():\n    pass\n", 1, "for n in sorted(numbers):")]
    [InlineData("python-sorted-not-sort", "TypeError", "'NoneType' object is not iterable", "for n in numbers.sort(reverse=True):\n    pass\n", 1, "for n in sorted(numbers, reverse=True):")]
    [InlineData("python-in-place-result", "AttributeError", "'NoneType' object has no attribute 'append'", "numbers = [1]\nnumbers = numbers.append(2)\nnumbers.append(3)\n", 3, "numbers.append(2)")]
    [InlineData("python-in-place-result", "AttributeError", "'NoneType' object has no attribute 'append'", "numbers = None\nnumbers.append(3)\n", 2, null)]
    [InlineData("python-called-constant", "TypeError", "'float' object is not callable", "import math\nprint(math.pi())\n", 2, "print(math.pi)")]
    [InlineData("python-isinstance-string", "TypeError", "isinstance() arg 2 must be a type, a tuple of types, or a union", "print(isinstance(x, \"int\"))\n", 1, "print(isinstance(x, int))")]
    [InlineData("python-changed-during-iteration", "RuntimeError", "dictionary changed size during iteration", "for k in d:\n    del d[k]\n", 1, "for k in list(d):")]
    [InlineData("python-changed-during-iteration", "RuntimeError", "dictionary changed size during iteration", "for k, v in d.items():\n    pass\n", 1, "for k, v in list(d.items()):")]
    // Imports.
    [InlineData("python-import-from-backwards", "SyntaxError", "invalid syntax", "import from math sqrt\n", 1, "from math import sqrt")]
    [InlineData("python-relative-import", "ImportError", "attempted relative import with no known parent package", "from .nowhere import greet\n", 1, null)]
    // Strings, brackets and layout.
    [InlineData("python-fstring-brace", "SyntaxError", "f-string: expecting '}'", "print(f\"Hello {name\")\n", 1, "print(f\"Hello {name}\")")]
    [InlineData("python-unterminated-string", "SyntaxError", "unterminated string literal (detected at line 1)", "print(\"hello)\n", 1, "print(\"hello\")")]
    [InlineData("python-unterminated-string", "SyntaxError", "unterminated string literal (detected at line 1)", "print('It's here')\n", 1, "print(\"It's here\")")]
    [InlineData("python-unmatched-closing", "SyntaxError", "unmatched ')'", "print(1))\n", 1, "print(1)")]
    [InlineData("python-unexpected-indent", "IndentationError", "unexpected indent", "x = 1\n    y = 2\nprint(x, y)\n", 2, "y = 2")]
    [InlineData("python-unexpected-indent", "IndentationError", "unexpected indent", "x = 1\n    y = 2\n    z = 3\nprint(x)\n", 2, "y = 2|z = 3")]
    [InlineData("python-unindent-mismatch", "IndentationError", "unindent does not match any outer indentation level", "def f():\n    if True:\n        x = 1\n      return x\n", 4, "    return x")]
    [InlineData("python-tabs-and-spaces", "TabError", "inconsistent use of tabs and spaces in indentation", "def f():\n\tx = 1\n        return x\n", 3, "        x = 1")]
    public void EachConstructGetsItsFixOrARefusal(string rule, string type, string message, string source, int line, string? expected)
    {
        var file = Write("app.py", source);

        Assert.Equal(expected, Propose(rule, Error(type, message, file, line)));
    }

    /// <summary>Only when the module really is beside the script is the dot dropped.</summary>
    [Fact]
    public void ARelativeImportIsMadeAbsoluteWhenTheModuleIsThere()
    {
        Write("helpers.py", "def greet():\n    pass\n");
        var file = Write("app.py", "from .helpers import greet\n");

        Assert.Equal("from helpers import greet",
            Propose("python-relative-import", Error("ImportError", "attempted relative import with no known parent package", file, 1)));
    }

    /// <summary>Python underlines the whole comparison with carets; the operator the message names splits it.</summary>
    [Theory]
    [InlineData("if age > 18:", "   ^^^^^^^^", "str", "int", ">", "if int(age) > 18:")]
    [InlineData("if 18 <= age:", "   ^^^^^^^^^", "int", "str", "<=", "if 18 <= int(age):")]
    [InlineData("if name > other:", "   ^^^^^^^^^^^^", "str", "int", ">", null)]
    public void TextComparedWithANumberIsConverted(string code, string underline, string a, string b, string op, string? expected)
    {
        var file = Write("app.py", code + "\n    pass\n");
        var message = $"'{op}' not supported between instances of '{a}' and '{b}'";

        var raw = string.Join("\n",
            "Traceback (most recent call last):",
            $"  File \"{file}\", line 1, in <module>",
            $"    {code}",
            "    " + underline,
            $"TypeError: {message}");

        Assert.Equal(expected, Propose("python-comparison-types", Error("TypeError", message, file, 1, raw)));
    }

    // ------------------------------------------------------------------ live, through a real Python

    private static readonly Lazy<bool> Python = new(() =>
    {
        if (TargetFactory.FindOnPath("python") is not { } python) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(python, "-c \"import sys; print(sys.version_info >= (3, 12))\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            });

            var answer = process!.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(30_000);

            return answer == "True";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    });

    /// <summary>The program, the answer's id, text the copied fix must contain, and text it must not.</summary>
    public static TheoryData<string, string, string, string, string?> Constructs => new()
    {
        { "unterminated string", "print(\"hello)\n", "local:python-unterminated-string", "print(\"hello\")", null },
        { "apostrophe in quotes", "print('It's here')\n", "local:python-unterminated-string", "print(\"It's here\")", null },
        { "extra closing bracket", "print(1))\n", "local:python-unmatched-closing", "print(1)", "))" },
        { "unexpected indent", "x = 1\n    y = 2\nprint(x, y)\n", "local:python-unexpected-indent", "x = 1\ny = 2\nprint(x, y)", null },
        { "unindent mismatch", "def f():\n    if True:\n        x = 1\n      return x\n\nprint(f())\n", "local:python-unindent-mismatch", "x = 1\n    return x", null },
        { "tabs and spaces", "def f():\n\tx = 1\n        return x\nprint(f())\n", "local:python-tabs-and-spaces", "        x = 1", "\t" },
        { "def without brackets", "def greet:\n    print(\"hi\")\n", "local:python-def-without-parentheses", "def greet():", null },
        { "plus plus", "count = 0\ncount++\nprint(count)\n", "local:python-foreign-syntax", "count += 1", null },
        { "double ampersand", "x = 5\nif x > 1 && x < 10:\n    print(x)\n", "local:python-foreign-syntax", "if x > 1 and x < 10:", null },
        { "exclamation not", "ready = False\nif !ready:\n    print(\"wait\")\n", "local:python-foreign-syntax", "if not ready:", null },
        { "let declaration", "let total = 5\nprint(total)\n", "local:python-foreign-syntax", "total = 5", "let" },
        { "null", "value = null\nprint(value)\n", "local:python-null", "value = None", null },
        { "this for self", "class Dog:\n    def __init__(self, name):\n        this.name = name\n\nDog(\"Rex\")\n", "local:python-this-for-self", "self.name = name", "import this" },
        { "new keyword", "class Dog:\n    pass\n\ndog = new Dog()\n", "local:python-foreign-syntax", "dog = Dog()", "new" },
        { "catch for except", "try:\n    int(\"x\")\ncatch ValueError:\n    print(\"bad\")\n", "local:python-foreign-syntax", "except ValueError:", null },
        { "elseif", "x = 1\nif x > 1:\n    print(\"big\")\nelseif x == 1:\n    print(\"one\")\n", "local:python-else-if", "elif x == 1:", null },
        { "except comma", "try:\n    int(\"x\")\nexcept ValueError, e:\n    print(e)\n", "local:python-except-comma", "except ValueError as e:", null },
        { "raise a string", "raise \"something went wrong\"\n", "local:python-raise-string", "raise Exception(\"something went wrong\")", null },
        { "raw_input", "name = raw_input(\"Name: \")\nprint(name)\n", "local:python-2-to-3-name", "name = input(\"Name: \")", null },
        { "xrange", "for i in xrange(3):\n    print(i)\n", "python:did-you-mean", "for i in range(3):", null },
        { "has_key", "d = {\"a\": 1}\nif d.has_key(\"a\"):\n    print(\"yes\")\n", "local:python-foreign-method", "if \"a\" in d:", null },
        { "iteritems", "d = {\"a\": 1}\nfor k, v in d.iteritems():\n    print(k, v)\n", "local:python-foreign-method", "for k, v in d.items():", null },
        { "missing self parameter", "class Dog:\n    def bark():\n        print(\"Woof\")\n\nDog().bark()\n", "local:python-missing-self", "def bark(self):", null },
        { "missing self dot", "class Dog:\n    def __init__(self, name):\n        self.name = name\n    def speak(self):\n        print(name)\n\nDog(\"Rex\").speak()\n", "python:did-you-mean", "print(self.name)", "print(self)\n" },
        { "length", "text = \"hello\"\nprint(text.length)\n", "local:python-foreign-method", "print(len(text))", null },
        { "push", "items = [1, 2]\nitems.push(3)\nprint(items)\n", "local:python-foreign-method", "items.append(3)", null },
        { "toUpperCase", "text = \"hi\"\nprint(text.toUpperCase())\n", "local:python-foreign-method", "print(text.upper())", null },
        { "for in integer", "for i in 10:\n    print(i)\n", "local:python-range-for-int", "for i in range(10):", null },
        { "for in len", "items = [\"a\", \"b\"]\nfor i in len(items):\n    print(items[i])\n", "local:python-range-for-int", "for i in range(len(items)):", null },
        { "float index", "items = [1, 2, 3, 4]\nprint(items[len(items) / 2])\n", "local:python-float-division", "print(items[len(items) // 2])", null },
        { "range of float", "for i in range(10 / 2):\n    print(i)\n", "local:python-float-division", "for i in range(10 // 2):", null },
        { "compare text with number", "age = \"20\"\nif age > 18:\n    print(\"Adult\")\n", "local:python-comparison-types", "if int(age) > 18:", null },
        { "join numbers", "numbers = [1, 2, 3]\nprint(\", \".join(numbers))\n", "local:python-join-non-strings", "print(\", \".join(map(str, numbers)))", null },
        { "sort returns None", "numbers = [3, 1, 2]\nfor n in numbers.sort():\n    print(n)\n", "local:python-sorted-not-sort", "for n in sorted(numbers):", null },
        { "assign the append result", "numbers = [1, 2]\nnumbers = numbers.append(3)\nnumbers.append(4)\n", "local:python-in-place-result", "[1, 2]\nnumbers.append(3)", null },
        { "calling a constant", "import math\nprint(math.pi())\n", "local:python-called-constant", "print(math.pi)", "pi()" },
        { "isinstance with a string", "x = 5\nprint(isinstance(x, \"int\"))\n", "local:python-isinstance-string", "print(isinstance(x, int))", null },
        { "dict changed during loop", "d = {\"a\": 1, \"b\": 2}\nfor k in d:\n    del d[k]\n", "local:python-changed-during-iteration", "for k in list(d):", null },
        { "__str__ returns a number", "class Dog:\n    def __str__(self):\n        return 5\n\nprint(Dog())\n", "local:python-dunder-str-return", "return str(5)", null },
        { "global with assignment", "global count = 0\n", "local:python-global-assignment", "count = 0", "global" },
        { "f-string brace", "name = \"Ethan\"\nprint(f\"Hello {name\")\n", "local:python-fstring-brace", "print(f\"Hello {name}\")", null },
        { "import from backwards", "import from math sqrt\n", "local:python-import-from-backwards", "from math import sqrt", null },
        { "module name case", "import Math\nprint(Math.sqrt(4))\n", "local:python-stdlib-module-typo", "import math\nprint(math.sqrt(4))", null },
        { "print redirect", "import sys\nprint >>sys.stderr, \"oops\"\n", "local:python-print-redirect", "print(\"oops\", file=sys.stderr)", null },
        { "java equals", "name = \"Ethan\"\nif name.equals(\"Ethan\"):\n    print(\"hi\")\n", "local:python-foreign-method", "if name == \"Ethan\":", null },
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public async Task TheConstructIsFixedLive(string construct, string source, string id, string expected, string? absent)
    {
        if (!Python.Value) return;

        var plan = TargetFactory.FromFile(Write("app.py", source), TimeSpan.FromMinutes(2));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct}: {outcome.Result} - {outcome.Headline}");
        Assert.Equal(id, outcome.Best!.Id);
        Assert.DoesNotContain(outcome.Candidates, candidate => candidate.Id.StartsWith("pip:", StringComparison.Ordinal));

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);

        var text = copied!.Text.ReplaceLineEndings("\n") + "\n";

        Assert.Contains(expected, text, StringComparison.Ordinal);
        if (absent is not null) Assert.DoesNotContain(absent, text, StringComparison.Ordinal);
    }
}
