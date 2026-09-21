using System.Diagnostics;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>The mistakes people make learning Python, one per topic of a beginner's course, and what FixFinder does with each.</summary>
public class PythonCourseTests : IDisposable
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

    private LocalFixContext Context(ParsedError error) => new() { Error = error, SourceRoot = _temp.Path };

    private static string Traceback(string file, int line, string echo, string underline, string type, string message) =>
        string.Join("\n",
            "Traceback (most recent call last):",
            $"  File \"{file}\", line {line}, in <module>",
            $"    {echo}",
            underline,
            $"{type}: {message}");

    [Theory]
    [InlineData("age", "1", "print(int(age) + 1)")]
    [InlineData("price", "0.5", "print(float(price) + 0.5)")]
    [InlineData("\"Total: \"", "total", "print(\"Total: \" + str(total))")]
    [InlineData("first", "count", null)]
    public void WhichSideOfPlusToConvertDependsOnWhatIsOnTheLeft(string left, string right, string? expected)
    {
        var code = $"print({left} + {right})";
        var file = Write("app.py", "age = \"18\"\n" + code + "\n");
        var message = "can only concatenate str (not \"int\") to str";

        var underline = new string(' ', 4 + "print(".Length) + new string('~', left.Length + 1) + "^" + new string('~', right.Length + 1);
        var error = Error("TypeError", message, file, 2, Traceback(file, 2, code, underline, "TypeError", message));

        Assert.Equal(expected, new PythonStrConcatenation().Propose(Context(error))?.NewLines[0]);
    }

    [Fact]
    public void AnUnderlineThatCutsANameIsNotActedOn()
    {
        const string code = "print(\"Total: \" + total)";
        var file = Write("app.py", code + "\n");
        var message = "can only concatenate str (not \"int\") to str";
        var shortUnderline = new string(' ', 10) + new string('~', 10) + "^" + new string('~', 5);
        var error = Error("TypeError", message, file, 1, Traceback(file, 1, code, shortUnderline, "TypeError", message));

        Assert.Null(new PythonStrConcatenation().Propose(Context(error)));
    }

    [Theory]
    [InlineData("else if age == 18:", "elif age == 18:")]
    [InlineData("    else if ready:", "    elif ready:")]
    public void ElseIfIsElif(string line, string expected)
    {
        var file = Write("app.py", "if age > 18:\n    pass\n" + line + "\n    pass\n");

        Assert.Equal(expected, new PythonElseIf().Propose(Context(Error("SyntaxError", "expected ':'", file, 3)))!.NewLines[0]);
    }

    [Theory]
    [InlineData("else age < 18:", "elif age < 18:")]
    [InlineData("else age < 18  # children", "elif age < 18:  # children")]
    public void AnElseWithAConditionIsAnElif(string line, string expected)
    {
        var file = Write("app.py", "if age > 18:\n    pass\n" + line + "\n    pass\n");

        Assert.Equal(expected, new PythonElseWithCondition().Propose(Context(Error("SyntaxError", "expected ':'", file, 3)))!.NewLines[0]);
    }

    [Theory]
    [InlineData("if age => 18:", "if age >= 18:")]
    [InlineData("if age =< 18:", "if age <= 18:")]
    [InlineData("if age <> 18:", "if age != 18:")]
    [InlineData("if label == \"=>\" and age => 18:", "if label == \"=>\" and age >= 18:")]
    public void ComparisonsWrittenBackwardsAreTurnedRound(string line, string expected)
    {
        var file = Write("app.py", line + "\n    pass\n");

        Assert.Equal(expected, new PythonArrowOperator().Propose(Context(Error("SyntaxError", "invalid syntax", file, 1)))!.NewLines[0]);
    }

    [Theory]
    [InlineData("// This is a comment", "# This is a comment")]
    [InlineData("    //note", "    # note")]
    public void ASlashCommentBecomesAHashComment(string line, string expected)
    {
        var file = Write("app.py", line + "\nprint(1)\n");

        Assert.Equal(expected, new PythonSlashComment().Propose(Context(Error("SyntaxError", "invalid syntax", file, 1)))!.NewLines[0]);
    }

    [Fact]
    public void ALambdaLosesItsReturn()
    {
        var file = Write("app.py", "square = lambda x: return x * x\n");

        Assert.Equal("square = lambda x: x * x",
            new PythonLambdaReturn().Propose(Context(Error("SyntaxError", "invalid syntax", file, 1)))!.NewLines[0]);
    }

    [Theory]
    [InlineData("numbers = [1, 2, 3\nprint(numbers)\n", "numbers = [1, 2, 3]")]
    [InlineData("squares = [x*x for x in range(10)\nprint(squares)\n", "squares = [x*x for x in range(10)]")]
    [InlineData("data = [\n    1,\n    2,\nprint(data)\n", null)]
    [InlineData("values = [1, 2,\n    3,\nprint(values)\n", null)]
    public void AnUnclosedBracketIsClosedOnlyWhereTheLinePlainlyEnds(string source, string? expected)
    {
        var file = Write("app.py", source);

        Assert.Equal(expected, new PythonUnclosedBracket().Propose(Context(Error("SyntaxError", "'[' was never closed", file, 1)))?.NewLines[0]);
    }

    [Fact]
    public void NowIsCalledOnTheDatetimeClass()
    {
        var file = Write("app.py", "import datetime\nprint(datetime.now())\n");

        Assert.Equal("print(datetime.datetime.now())",
            new PythonDatetimeClass().Propose(Context(Error("AttributeError", "module 'datetime' has no attribute 'now'", file, 2)))!.NewLines[0]);
    }

    [Fact]
    public void SuperIsCalledBeforeItIsUsed()
    {
        var file = Write("app.py", "class Dog(Animal):\n    def __init__(self):\n        super.__init__()\n");

        Assert.Equal("        super().__init__()",
            new PythonSuperCall().Propose(Context(Error("TypeError", "descriptor '__init__' of 'super' object needs an argument", file, 3)))!.NewLines[0]);
    }

    [Theory]
    [InlineData("class Dog:\n    def __int__(self, name):\n        self.name = name\n\ndog = Dog(\"Rex\")\n", "    def __init__(self, name):")]
    [InlineData("class Dog:\n    def __init__(self):\n        pass\n    def __int__(self):\n        pass\n\ndog = Dog(\"Rex\")\n", null)]
    public void AMisspeltInitIsRenamedOnlyWhenTheClassHasNone(string source, string? expected)
    {
        var file = Write("app.py", source);
        var line = source.Split('\n').ToList().FindIndex(l => l.StartsWith("dog")) + 1;

        Assert.Equal(expected, new PythonInitTypo().Propose(Context(Error("TypeError", "Dog() takes no arguments", file, line)))?.NewLines[0]);
    }

    [Theory]
    [InlineData("name = \"Ethan\"\nprint(nmae)\n", "nmae", "print(name)")]
    [InlineData("name = \"Ethan\"\npritn(name)\n", "pritn", "pritn(name)")]
    [InlineData("cat = 1\ncap = 2\nprint(cae)\n", "cae", null)]
    public void ASwappedLetterIsMatchedToANameTheFileDefines(string source, string wrong, string? expected)
    {
        var file = Write("app.py", source);
        var fix = new PythonNearestName().Propose(Context(Error("NameError", $"name '{wrong}' is not defined", file, 2)));

        if (wrong == "pritn")
        {
            Assert.Equal("print(name)", fix!.NewLines[0]);
            return;
        }

        Assert.Equal(expected, fix?.NewLines[0]);
    }

    [Fact]
    public void ANameInsideAnFStringIsFoundByPythonsUnderline()
    {
        const string code = "print(f\"Hello {nmae}\")";
        var file = Write("app.py", "name = \"Ethan\"\n" + code + "\n");
        var raw = Traceback(file, 2, code, new string(' ', 19) + "^^^^", "NameError", "name 'nmae' is not defined");

        var fix = new PythonNearestName().Propose(Context(Error("NameError", "name 'nmae' is not defined", file, 2, raw)))!;

        Assert.Equal("print(f\"Hello {name}\")", fix.NewLines[0]);
    }

    [Fact]
    public void AMisspeltAttributeIsMatchedToWhatTheClassDefines()
    {
        var file = Write("app.py", "class Dog:\n    def __init__(self, name):\n        self.name = name\n\ndog = Dog(\"Rex\")\nprint(dog.nmae)\n");

        Assert.Equal("print(dog.name)",
            new PythonNearestName().Propose(Context(Error("AttributeError", "'Dog' object has no attribute 'nmae'", file, 6)))!.NewLines[0]);
    }

    [Fact]
    public void AChainedErrorIsCorrectedOnItsOwnLineNotItsCauses()
    {
        var own = new ErrorFrame { Order = 0, File = "app.py", Line = 3, RawLine = "" };
        var cause = new ErrorFrame { Order = 0, File = "app.py", Line = 2, RawLine = "" };

        var error = new ParsedError
        {
            LanguageId = "python",
            Confidence = 90,
            RawText = "",
            FirstLineSequence = 0,
            ExceptionType = "NameError",
            Message = "name 'valueerror' is not defined. Did you mean: 'ValueError'?",
            Frames = [own],
            CulpritFrame = cause,
        };

        Assert.Same(own, LocalFixContext.OwnFrame(error));
    }

    [Fact]
    public void TypedInputTravelsWithTheSpecAndNothingElseChanges()
    {
        var spec = new TargetSpec { ExecutablePath = "python", Arguments = "app.py", WorkingDirectory = _temp.Path, Timeout = TimeSpan.FromSeconds(7) };

        var typed = spec.WithInput("Ethan\n18");

        Assert.Equal("Ethan\n18", typed.StandardInput);
        Assert.Equal(spec.DisplayCommandLine, typed.DisplayCommandLine);
        Assert.Equal(spec.Timeout, typed.Timeout);
        Assert.Null(spec.WithInput("").StandardInput);
    }

    private static readonly Lazy<string?> Python = new(() =>
    {
        if (TargetFactory.FindOnPath("python") is not { } python) return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(python, "-c \"import sys; print(sys.version_info >= (3, 12))\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            });

            var answer = process!.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(30_000);

            return answer == "True" ? python : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    });

    public static TheoryData<string, string, string, string> Topics => new()
    {
        { "1. variables", "name = \"Ethan\"\nage = 18\nprint(nmae)\n", "local:python-nearest-name", "print(name)" },
        { "2. data types", "age = \"18\"\nprint(age + 1)\n", "local:python-str-concatenation", "print(int(age) + 1)" },
        { "7. comparison", "age = 18\nif age => 18:\n    print(\"Adult\")\n", "local:python-comparison-operator", "if age >= 18:" },
        { "10. elif", "age = 18\nif age > 18:\n    print(\"Adult\")\nelse if age == 18:\n    print(\"Just 18\")\n", "local:python-else-if", "elif age == 18:" },
        { "11. else", "age = 10\nif age > 18:\n    print(\"Adult\")\nelse age < 18:\n    print(\"Child\")\n", "local:python-else-with-condition", "elif age < 18:" },
        { "19. lists", "numbers = [1, 2, 3\nprint(numbers)\n", "local:python-unclosed-bracket", "numbers = [1, 2, 3]" },
        { "25. dictionary methods", "person = {\"name\": \"Ethan\", \"age\": 18}\nfor value in person.value():\n    print(value)\n", "python:did-you-mean", "for value in person.values():" },
        { "30. f-strings", "name = \"Ethan\"\nprint(f\"Hello {nmae}\")\n", "local:python-nearest-name", "print(f\"Hello {name}\")" },
        { "31. comments", "// This is a comment\nprint(\"Hi\")\n", "local:python-slash-comment", "# This is a comment" },
        { "33. importing modules", "import maths\nprint(maths.sqrt(4))\n", "local:python-stdlib-module-typo", "import math\nprint(math.sqrt(4))" },
        { "35. standard libraries", "import datetime\nprint(datetime.now())\n", "local:python-datetime-class", "print(datetime.datetime.now())" },
        { "37. specific exceptions", "try:\n    int(\"abc\")\nexcept valueerror:\n    print(\"not a number\")\n", "python:did-you-mean", "except ValueError:" },
        { "43. constructors", "class Dog:\n    def __int__(self, name):\n        self.name = name\n\ndog = Dog(\"Rex\")\n", "local:python-init-typo", "def __init__(self, name):" },
        { "44. instance attributes", "class Dog:\n    def __init__(self, name):\n        self.name = name\n\ndog = Dog(\"Rex\")\nprint(dog.nmae)\n", "local:python-nearest-name", "print(dog.name)" },
        { "45. inheritance", "class Animal:\n    def __init__(self):\n        self.alive = True\n\nclass Dog(Animal):\n    def __init__(self):\n        super.__init__()\n\nDog()\n", "local:python-super-call", "super().__init__()" },
        { "46. list comprehensions", "squares = [x*x for x in range(10)\nprint(squares)\n", "local:python-unclosed-bracket", "squares = [x*x for x in range(10)]" },
        { "47. lambda", "square = lambda x: return x * x\nprint(square(3))\n", "local:python-lambda-return", "square = lambda x: x * x" },
    };

    [Theory]
    [MemberData(nameof(Topics))]
    public async Task TheBeginnersMistakeIsFixed(string topic, string source, string id, string expected)
    {
        if (Python.Value is null) return;

        var outcome = await RunOffline(Write("app.py", source));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{topic}: {outcome.Result} - {outcome.Headline}");
        Assert.Equal(id, outcome.Best!.Id);

        Assert.DoesNotContain(outcome.Candidates, candidate => candidate.Id.StartsWith("pip:", StringComparison.Ordinal));

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));

        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnInterpreterThatCouldNotBeAskedIsAskedAgainNextTime()
    {
        var missing = Path.Combine(_temp.Path, "no-such-python", "python.exe");

        Assert.Empty(PythonStandardLibrary.Names(missing));
        Assert.False(PythonStandardLibrary.IsRemembered(missing));
    }

    [Fact]
    public void APackageNamedLikeAStandardModuleIsStillOfferedForInstall()
    {
        if (Python.Value is not { } python) return;

        var typo = Write("typo.py", "import maths\nprint(maths.sqrt(4))\n");
        var package = Write("package.py", "import lxml\nprint(lxml.etree)\n");
        var spec = new TargetSpec { ExecutablePath = python, WorkingDirectory = _temp.Path };

        Assert.Null(MissingModule.For(Error("ModuleNotFoundError", "No module named 'maths'", typo, 1), spec));
        Assert.Equal("pip:lxml", MissingModule.For(Error("ModuleNotFoundError", "No module named 'lxml'", package, 1), spec)?.Id);
    }

    [Fact]
    public async Task ProgramsThatAskForInputAreTypedTheAnswers()
    {
        if (Python.Value is null) return;

        var script = Write("echo.py", "print(input() + \"!\")\n");
        var plan = TargetFactory.FromFile(script, TimeSpan.FromMinutes(1));

        var run = await new TargetRunner(new ParserRegistry()).RunAsync(plan.Spec!.WithInput("hello"), CancellationToken.None);

        Assert.Contains(run.Lines, line => line.Text == "hello!");
    }

    [Fact]
    public async Task AProgramThatStopsAtInputIsToldToBeTypedIntoAndThenFixed()
    {
        if (Python.Value is null) return;

        var script = Write("greet.py", "name = input(\"Name: \")\nprint(\"Hi \" + nmae)\n");

        var untyped = await RunOffline(script);

        Assert.Equal("EOFError", untyped.Error?.ExceptionType);
        Assert.Contains("ask for input", untyped.Headline, StringComparison.Ordinal);
        Assert.Empty(untyped.Candidates);

        var typed = await RunOffline(script, "Ethan");

        if (ApplicationControl.Refused(typed)) return;
        Assert.True(typed.Result == SessionResult.FoundFix, $"{typed.Result} - {typed.Headline}");
        Assert.Equal("local:python-nearest-name", typed.Best!.Id);
    }

    [Fact]
    public async Task AskingForMoreThanWasTypedIsSaidPlainly()
    {
        if (Python.Value is null) return;

        var script = Write("two.py", "first = input()\nsecond = input()\nprint(first, second)\n");

        var outcome = await RunOffline(script, "only one");

        Assert.Contains("more input than the 1 line you gave it", outcome.Headline, StringComparison.Ordinal);
    }

    private static async Task<SessionOutcome> RunOffline(string path, string? input = null)
    {
        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(2));
        Assert.True(plan.Ok, plan.Problem);

        if (input is not null) plan = plan with { Spec = plan.Spec!.WithInput(input) };

        using var http = new FixFinderHttpClient();

        return await new FixFinderSession(http, new FixSourceRegistry())
            .RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));
    }
}
