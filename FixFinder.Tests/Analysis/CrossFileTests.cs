using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Analysis across the files of a program: a call is followed into the function it runs in another file - by import,
/// require, a module imported whole, or a C function defined in another file - and a finding that points at a line in
/// another file names that file. A call that could run more than one function, or a name that is set again, is not
/// followed at all, so nothing is said about it that the code does not show.
/// </summary>
public class CrossFileTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes each file - a path inside the test's folder, and its code - and gives back their full paths in order.</summary>
    private List<string> Write(params (string Name, string Code)[] files)
    {
        var paths = new List<string>();
        foreach (var (name, code) in files)
        {
            var path = Path.Combine(_temp.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, code.ReplaceLineEndings("\n"));
            paths.Add(path);
        }

        return paths;
    }

    private IReadOnlyList<AnalysisFinding> Checked(IrProgram program, AnalysisCache? cache = null)
    {
        foreach (var problem in program.Problems) output.WriteLine($"problem: {problem}");

        var findings = AbstractChecks.Run(program, new SourceText(), cache);
        foreach (var finding in findings)
            output.WriteLine($"{Path.GetFileName(finding.Span.File)}:{finding.Span.Line} {finding.CheckId}: {finding.Message}");
        return findings;
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(params (string Name, string Code)[] files) =>
        PythonFrontend.FindInterpreter() is { } python ? Checked(await PythonFrontend.ReadAsync(Write(files), python)) : null;

    private async Task<IReadOnlyList<AnalysisFinding>> JavaScript(params (string Name, string Code)[] files) =>
        Checked(await JavaScriptFrontend.ReadAsync(Write(files)));

    private async Task<IReadOnlyList<AnalysisFinding>> C(params (string Name, string Code)[] files) =>
        Checked(await CFrontend.ReadAsync(Write(files)));

    private static AnalysisFinding FoundIn(IReadOnlyList<AnalysisFinding> findings, string check, string file, int line) =>
        Assert.Single(findings, f => f.CheckId == check && Path.GetFileName(f.Span.File) == file && f.Span.Line == line);

    private const string PythonZero = "def zero():\n    return 0\n";

    // ---- Python ----

    [Theory]
    [InlineData("from helpers import zero\n\nprint(10 / zero())\n")]
    [InlineData("from helpers import zero as nothing\n\nprint(10 / nothing())\n")]
    [InlineData("import helpers\n\nprint(10 / helpers.zero())\n")]
    [InlineData("import helpers as h\n\nprint(10 / h.zero())\n")]
    [InlineData("from helpers import *\n\nprint(10 / zero())\n")]
    [InlineData("from helpers import zero\n\n\ndef share():\n    return 10 / zero()\n\n\nshare()\n")]
    public async Task APythonFunctionImportedFromAnotherFileIsFollowed(string main)
    {
        if (await Python(("main.py", main), ("helpers.py", PythonZero)) is not { } findings) return;

        var line = main.Split('\n').ToList().FindIndex(text => text.Contains("10 /", StringComparison.Ordinal)) + 1;
        FoundIn(findings, "analysis-division-by-zero", "main.py", line);
    }

    [Fact]
    public async Task APackageModuleImportedRelativelyIsFollowed()
    {
        if (await Python(
                ("main.py", "from shop.checkout import total\n\nprint(total())\n"),
                ("shop/__init__.py", ""),
                ("shop/prices.py", PythonZero),
                ("shop/checkout.py", "from .prices import zero\n\n\ndef total():\n    return 100 / zero()\n")) is not { } findings) return;

        FoundIn(findings, "analysis-division-by-zero", "checkout.py", 5);
    }

    /// <summary>Each of two modules has a value(); the one imported is the one followed, and it never returns 0.</summary>
    [Fact]
    public async Task TheFunctionOfTheModuleImportedIsTheOneFollowed()
    {
        if (await Python(
                ("main.py", "from safe import value\n\nprint(10 / value())\n"),
                ("safe.py", "def value():\n    return 5\n"),
                ("risky.py", "def value():\n    return 0\n")) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    /// <summary>
    /// len here is Python's own, though another file of the program defines a len that returns 0: a name the module
    /// neither defines nor imports is not taken to be some other module's function.
    /// </summary>
    [Fact]
    public async Task ANameTheModuleDoesNotImportIsNotAnotherModulesFunction()
    {
        if (await Python(
                ("main.py", "items = [1, 2, 3]\nprint(10 / len(items))\n"),
                ("tools.py", "def len(things):\n    return 0\n")) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    /// <summary>zero is imported and then defined again, so which one a call runs depends on the order - and neither is assumed.</summary>
    [Fact]
    public async Task ANameBoundTwiceIsNotFollowed()
    {
        if (await Python(
                ("main.py", "from helpers import zero\n\n\ndef zero():\n    return 1\n\n\nprint(10 / zero())\n"),
                ("helpers.py", PythonZero)) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    /// <summary>
    /// Top-level code runs in order, so zero does not exist yet on the line that calls it: that line fails with a
    /// NameError, and saying it divides by zero would describe something that never happens. A call from inside a
    /// function runs later, once zero is defined.
    /// </summary>
    [Fact]
    public async Task ATopLevelCallAboveTheDefItUsesIsNotFollowed()
    {
        if (await Python(("main.py", "print(10 / zero())\n\n\n" + PythonZero + "\n\ndef share():\n    return 10 / zero()\n\n\nprint(share())\n")) is not { } findings) return;

        var division = Assert.Single(findings, f => f.CheckId == "analysis-division-by-zero");
        Assert.Equal(9, division.Span.Line);
    }

    /// <summary>The line the text is run on is in the other file, and the finding says which file.</summary>
    [Fact]
    public async Task TaintFollowedIntoAnotherFileNamesTheFile()
    {
        if (await Python(
                ("main.py", "from runner import run\n\nrun(input())\n"),
                ("runner.py", "def run(code):\n    return eval(code)\n")) is not { } findings) return;

        var injection = FoundIn(findings, Taint.CodeRule, "main.py", 3);
        Assert.Contains("which runs it as Python code at line 2 of runner.py", injection.Message);
        Assert.Contains("the user typed at line 3", injection.Message);
    }

    [Fact]
    public async Task AContractBrokenFromAnotherFileNamesTheFile()
    {
        if (await Python(
                ("main.py", "import maths\n\nprint(maths.root(-4))\n"),
                ("maths.py", "def root(x):\n    if x < 0:\n        raise ValueError('negative')\n    return x ** 0.5\n")) is not { } findings) return;

        var broken = FoundIn(findings, "analysis-contract-broken", "main.py", 3);
        Assert.Contains("it raises ValueError (line 2 of maths.py)", broken.Message);
    }

    /// <summary>
    /// Every Python module has variables of its own: add() fills the tracker module's items, not the list main.py walks
    /// over, so nothing changes under the loop.
    /// </summary>
    [Fact]
    public async Task AnotherModulesVariableIsNotTheCallersOwn()
    {
        if (await Python(
                ("main.py", "import tracker\n\nitems = [1, 2, 3]\nfor item in items:\n    tracker.add(item)\n"),
                ("tracker.py", "items = []\n\n\ndef add(item):\n    items.append(item)\n")) is not { } findings) return;

        Assert.DoesNotContain(findings, f => f.CheckId == ChangedWhileLooping.Rule);
    }

    // ---- JavaScript ----

    private const string FindsNothing = "function find(name) {\n  return null;\n}\n";

    [Theory]
    [InlineData("helpers.js", FindsNothing + "\nmodule.exports = { find };\n", "const { find } = require('./helpers');\n\nconsole.log(find('ada').length);\n")]
    [InlineData("helpers.js", FindsNothing + "\nmodule.exports = { find };\n", "const { find: look } = require('./helpers.js');\n\nconsole.log(look('ada').length);\n")]
    [InlineData("helpers.js", FindsNothing + "\nmodule.exports = { find };\n", "const helpers = require('./helpers');\n\nconsole.log(helpers.find('ada').length);\n")]
    [InlineData("helpers.js", FindsNothing + "\nexports.find = find;\n", "const find = require('./helpers').find;\n\nconsole.log(find('ada').length);\n")]
    [InlineData("helpers.js", FindsNothing + "\nmodule.exports = find;\n", "const find = require('./helpers');\n\nconsole.log(find('ada').length);\n")]
    [InlineData("helpers.mjs", "export " + FindsNothing, "import { find as look } from './helpers.mjs';\n\nconsole.log(look('ada').length);\n")]
    [InlineData("helpers.mjs", "export default " + FindsNothing, "import look from './helpers.mjs';\n\nconsole.log(look('ada').length);\n")]
    [InlineData("helpers.mjs", "export const find = (name) => null;\n", "import * as helpers from './helpers.mjs';\n\nconsole.log(helpers.find('ada').length);\n")]
    [InlineData("helpers.mjs", FindsNothing + "\nexport { find as look };\n", "import { look } from './helpers.mjs';\n\nconsole.log(look('ada').length);\n")]
    public async Task AJavaScriptFunctionImportedFromAnotherFileIsFollowed(string helpers, string helpersCode, string main)
    {
        var findings = await JavaScript((Path.ChangeExtension("main", Path.GetExtension(helpers)), main), (helpers, helpersCode));

        // The whole call is quoted, not only the name of what it calls, which is a function rather than null.
        var used = FoundIn(findings, "analysis-null-used", Path.ChangeExtension("main", Path.GetExtension(helpers)), 3);
        Assert.Contains("('ada')` is null here", used.Message);
    }

    [Fact]
    public async Task AJavaScriptFunctionInTheSameFileIsFollowed()
    {
        var findings = await JavaScript(("main.js", FindsNothing + "\nconsole.log(find('ada').length);\n"));

        FoundIn(findings, "analysis-null-used", "main.js", 5);
    }

    /// <summary>A function declaration is hoisted - ready before the first line runs - so a call above it runs it.</summary>
    [Fact]
    public async Task AJavaScriptFunctionDeclarationCanBeCalledAboveIt()
    {
        var findings = await JavaScript(("main.js", "console.log(find('ada').length);\n\n" + FindsNothing));

        FoundIn(findings, "analysis-null-used", "main.js", 1);
    }

    /// <summary>A const is not hoisted: the call above it fails with a ReferenceError, not with what find would return.</summary>
    [Fact]
    public async Task ATopLevelCallAboveTheConstItUsesIsNotFollowed()
    {
        var findings = await JavaScript(("main.js", "console.log(find('ada').length);\n\nconst find = (name) => null;\n"));

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-null-used");
    }

    /// <summary>
    /// A bare find() inside a method is the module's find, not the class's own - JavaScript has no implicit this - and
    /// the module's find returns something.
    /// </summary>
    [Fact]
    public async Task AJavaScriptMethodCallingANameAloneDoesNotCallItsOwnMethod()
    {
        var findings = await JavaScript(("main.js", """
            class Shelf {
              find(name) {
                return null;
              }

              label() {
                return find('ada').length;
              }
            }

            function find(name) {
              return 'found';
            }

            console.log(new Shelf().label());
            """));

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-null-used");
    }

    [Theory]
    [InlineData(FindsNothing + "\nfind = function (name) {\n  return 'found';\n};\n\nconsole.log(find('ada').length);\n")]
    [InlineData("let find = require('./helpers').find;\n\nconsole.log(find('ada').length);\n")]
    [InlineData("const helpers = require('./helpers');\n\nconsole.log(helpers.missing('ada').length);\n")]
    [InlineData("const helpers = require('helpers');\n\nconsole.log(helpers.find('ada').length);\n")]
    public async Task AJavaScriptCallThatMayRunSomethingElseIsNotFollowed(string main)
    {
        var findings = await JavaScript(("main.js", main), ("helpers.js", FindsNothing + "\nmodule.exports = { find };\n"));

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-null-used" && Path.GetFileName(f.Span.File) == "main.js");
    }

    /// <summary>An export made inside a branch may not have been made, and a name exported twice may be either value.</summary>
    [Theory]
    [InlineData(FindsNothing + "\nif (process.env.DEBUG) {\n  module.exports.find = find;\n}\n")]
    [InlineData(FindsNothing + "\nmodule.exports.find = find;\nmodule.exports.find = (name) => 'found';\n")]
    [InlineData(FindsNothing + "\nexports.find = find;\nmodule.exports = {};\n")]
    public async Task AnExportThatMayBeSomethingElseIsNotFollowed(string helpers)
    {
        var findings = await JavaScript(("main.js", "const helpers = require('./helpers');\n\nconsole.log(helpers.find('ada').length);\n"), ("helpers.js", helpers));

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-null-used" && Path.GetFileName(f.Span.File) == "main.js");
    }

    // ---- C ----

    [Fact]
    public async Task ACFunctionDefinedInAnotherFileIsFollowed()
    {
        var findings = await C(
            ("main.c", "int zero(void);\n\nint main(void)\n{\n    return 10 / zero();\n}\n"),
            ("helpers.c", "int zero(void)\n{\n    return 0;\n}\n"));

        var division = FoundIn(findings, "analysis-division-by-zero", "main.c", 5);
        Assert.Contains("`zero()` is always 0 here", division.Message);
    }

    /// <summary>A static function in the caller's own file is the one it calls, whatever other files define under the name.</summary>
    [Fact]
    public async Task ACFunctionInTheCallersOwnFileComesFirst()
    {
        var findings = await C(
            ("main.c", "static int value(void)\n{\n    return 5;\n}\n\nint main(void)\n{\n    return 10 / value();\n}\n"),
            ("other.c", "int value(void)\n{\n    return 0;\n}\n"));

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    /// <summary>Two other files each define value(), so which one is linked in is not something the code alone says.</summary>
    [Fact]
    public async Task ACFunctionDefinedInTwoOtherFilesIsNotFollowed()
    {
        var findings = await C(
            ("main.c", "int value(void);\n\nint main(void)\n{\n    return 10 / value();\n}\n"),
            ("first.c", "int value(void)\n{\n    return 0;\n}\n"),
            ("second.c", "int value(void)\n{\n    return 0;\n}\n"));

        Assert.DoesNotContain(findings, f => f.CheckId == "analysis-division-by-zero");
    }

    // ---- Checking again ----

    /// <summary>
    /// look is find under a name of its own, so a check that reuses what it found must know main depends on find: once
    /// find returns something, main's finding goes.
    /// </summary>
    [Fact]
    public async Task ChangingAnImportedFunctionChecksItsCallersAgain()
    {
        var cache = new AnalysisCache();
        var main = ("main.js", "const { find: look } = require('./helpers');\n\nfunction show() {\n  return look('ada').length;\n}\n\nconsole.log(show());\n");

        var before = Checked(await JavaScriptFrontend.ReadAsync(Write(main, ("helpers.js", FindsNothing + "\nmodule.exports = { find };\n"))), cache);
        FoundIn(before, "analysis-null-used", "main.js", 4);

        var after = Checked(await JavaScriptFrontend.ReadAsync(Write(main, ("helpers.js", "function find(name) {\n  return 'found';\n}\n\nmodule.exports = { find };\n"))), cache);
        Assert.DoesNotContain(after, f => f.CheckId == "analysis-null-used");
    }
}
