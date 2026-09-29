using System.Diagnostics;
using System.Text.Json;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers checking a Jupyter notebook: its code cells run in order as Run All runs them, from the notebook's folder, with
/// everything found said as the notebook is read - a cell, and a line in it.
/// </summary>
public class NotebookTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    /// <summary>A notebook as Jupyter saves one, in format 4: each cell's kind and its source, a code cell with no outputs yet.</summary>
    private string Notebook(string relative, params (string Kind, string Source)[] cells) => NotebookOf(relative, "python", cells);

    /// <param name="language">The language as the notebook's kernel names it: "python" for Python's, "R" for R's.</param>
    private string NotebookOf(string relative, string language, params (string Kind, string Source)[] cells)
    {
        var saved = cells.Select(cell => cell.Kind == "code"
            ? (object)new { cell_type = "code", execution_count = (int?)null, metadata = new { }, outputs = Array.Empty<object>(), source = SourceLines(cell.Source) }
            : new { cell_type = cell.Kind, metadata = new { }, source = SourceLines(cell.Source) });

        return Write(relative, JsonSerializer.Serialize(new
        {
            cells = saved,
            metadata = new
            {
                kernelspec = new { display_name = language, language, name = language.ToLowerInvariant() },
                language_info = new { name = language },
            },
            nbformat = 4,
            nbformat_minor = 5,
        }));
    }

    /// <summary>A cell's source as Jupyter saves it: a list of lines, each but the last ending in its newline.</summary>
    private static string[] SourceLines(string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        return [.. lines.Select((line, index) => index < lines.Length - 1 ? line + "\n" : line)];
    }

    /// <summary>Whether the Python here has a module installed - one a notebook imports, which FixFinder never installs.</summary>
    private static bool Installed(string module) => TargetFactory.FindOnPath("python") is { } python && Imports(python, module);

    /// <summary>
    /// A Python that has a module installed, for a test of a notebook that imports it: the one FIXFINDER_TEST_PACKAGES_PYTHON
    /// names, where packages were installed for the tests, or the one on PATH. Null when neither has it.
    /// </summary>
    internal static string? PythonWith(string module) =>
        new[] { Environment.GetEnvironmentVariable("FIXFINDER_TEST_PACKAGES_PYTHON"), TargetFactory.FindOnPath("python") }
            .OfType<string>()
            .FirstOrDefault(python => File.Exists(python) && Imports(python, module));

    private static bool Imports(string python, string module)
    {
        using var importing = Process.Start(new ProcessStartInfo(python, $"-c \"import {module}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        importing.StandardOutput.ReadToEnd();
        importing.StandardError.ReadToEnd();

        return importing.WaitForExit(60_000) && importing.ExitCode == 0;
    }

    /// <summary>A project folder whose VS Code settings name the Python its notebooks run with, as a project that uses one does.</summary>
    private void RunsWith(string folder, string python) =>
        Write(Path.Combine(folder, ".vscode", "settings.json"),
            JsonSerializer.Serialize(new Dictionary<string, string> { ["python.defaultInterpreterPath"] = python }));

    private async Task<CheckReport> CheckAsync(string file, string? expected = null, TimeSpan? timeLimit = null)
    {
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);
        if (timeLimit is { } limit) launch = launch with { Spec = launch.Spec!.WithTimeout(limit) };
        output.WriteLine($"how: {launch.Explanation}");

        using var http = new FixFinderHttpClient();
        var checker = new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Python,
            Expected = expected is null ? null : ExpectedBehaviour.From([new ExpectedRun(null, expected)], ""),
        };

        var report = await checker.CheckAsync(launch);
        Print(report);
        return report;
    }

    private void Print(CheckReport report)
    {
        output.WriteLine($"syntax: {report.SyntaxSummary} | logic: {report.LogicSummary}");
        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"[{finding.Severity}/{finding.Confidence}] {finding.Location} {finding.RuleId}: {finding.Title}");
            output.WriteLine($"    {finding.Explanation}");
            output.WriteLine($"    fix: {finding.SuggestedFix} | verified: {finding.Verified.Summary} {string.Join(" / ", finding.Verified.Steps.Select(step => step.Detail))}");
        }
    }

    /// <summary>Nothing in the report names the script the notebook's code was checked as, or a line of it outside a cell.</summary>
    private static void SaysNothingOfTheScript(CheckReport report, string script)
    {
        var scriptName = Path.GetFileName(script);

        foreach (var finding in report.Findings)
        {
            if (string.Equals(finding.File, script, StringComparison.OrdinalIgnoreCase)) Assert.NotNull(finding.InNotebook);

            foreach (var said in new[] { finding.Title, finding.Explanation, finding.WhyItMatters, finding.SuggestedFix, finding.FixCheckedBy ?? "" })
                Assert.DoesNotContain(scriptName, said, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var note in report.Notes) Assert.DoesNotContain(scriptName, note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ABugInACellIsReportedAtTheCellAndTheLineInItWhereJupyterWouldShowIt()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"marks\marks.ipynb",
            ("markdown", "# Marks"),
            ("code", "%matplotlib inline\nimport statistics"),
            ("code", "def average(values):\n    return sum(values) / len(values)"),
            ("markdown", "Now the averages"),
            ("code", "marks = [70, 65, 58]\naverage(marks)"),
            ("code", "resits = []\nprint(average(resits))"));

        var launch = TargetFactory.FromFile(notebook);
        Assert.StartsWith("Running the code cells of marks.ipynb in order, as Jupyter's Run All does, with", launch.Explanation, StringComparison.Ordinal);
        Assert.Equal(notebook, launch.ShownFile);

        var report = await CheckAsync(notebook);

        // Run All stops in average, called from the last cell with an empty list: line 2 of the third cell, the one average is in.
        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Contains("ZeroDivisionError: division by zero", stopped.Title, StringComparison.Ordinal);
        Assert.Equal(new NotebookPlace(Path.GetFullPath(notebook), 3, 2), stopped.InNotebook);
        Assert.Equal("marks.ipynb, cell 3, line 2", stopped.Location);

        // An editor is sent to the notebook, and told the cell and line.
        Assert.StartsWith($"{Path.GetFullPath(notebook)}:1:1: error: Cell 3, line 2: ", DiagnosticLines.Line(stopped, DiagnosticFormat.Gcc), StringComparison.Ordinal);

        // Which count "cell 3" is, since Jupyter's own number beside a cell is another one.
        Assert.Contains(report.Notes, note => note.StartsWith("marks.ipynb's cells are counted from its top, Markdown cells included", StringComparison.Ordinal));
        SaysNothingOfTheScript(report, launch.ChosenFile!);

        // What it printed places the failure as Jupyter's own traceback does, in the notebook's cells.
        var printed = report.Run!.Run!.Lines.Select(line => line.Text).ToList();
        Assert.Contains(printed, line => line.Contains($"File \"{Path.GetFullPath(notebook)}\", cell 3, line 2, in average", StringComparison.Ordinal));
        Assert.DoesNotContain(printed, line => line.Contains(Path.GetFileName(launch.ChosenFile!), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task APlotIsMadeWithoutAWindowSoTheCellsAfterItRunAsTheyDoInJupyter()
    {
        if (!LocalFixLiveTests.Available("python") || !Installed("matplotlib")) return;

        var notebook = Notebook(@"plots\plots.ipynb",
            ("code", "%matplotlib inline\nimport matplotlib.pyplot as plt"),
            ("code", "plt.plot([1, 2, 3], [2, 4, 1])\nplt.title(\"marks\")\nplt.show()"),
            ("code", "print(\"after the plot\")"));

        // A window would hold the run at show() until its time ran out, and the last cell would never run.
        var report = await CheckAsync(notebook, "after the plot", TimeSpan.FromSeconds(30));

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
        Assert.DoesNotContain(report.Notes, note => note.Contains("window", StringComparison.Ordinal) || note.Contains("Warning", StringComparison.Ordinal));

        // matplotlib's warning that the plot cannot be shown is FixFinder's doing, so it is not among what the notebook printed.
        Assert.DoesNotContain(report.Run!.Run!.Lines, line => line.Text.Contains("FigureCanvasAgg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhatAFindingSaysOfOtherLinesIsSaidInTheLinesOfTheirCells()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"grades\grades.ipynb",
            ("code", "import math\nprint(math.floor(64.5))"),
            ("code", "def describe(mark):\n    print(f\"{mark} marks\")"),
            ("markdown", "Grades"),
            ("code", "def grade(mark):\n    if mark >= 70:\n        return \"first\"\n    elif mark >= 70:\n        return \"upper second\"\n    return \"other\""),
            ("code", "label = describe(64)\nprint(label, grade(64))"));

        var report = await CheckAsync(notebook);

        // The line that uses describe's answer is in another cell, so its cell is named; the if the elif repeats is in its own.
        var printsInstead = Assert.Single(report.Findings, finding => finding.RuleId == "logic-python-print-instead-of-return");
        Assert.Equal("grades.ipynb, cell 2, line 1", printsInstead.Location);
        Assert.Contains("line 1 of cell 5 uses what it returns", printsInstead.Explanation, StringComparison.Ordinal);

        var repeated = Assert.Single(report.Findings, finding => finding.RuleId == "logic-python-duplicate-condition");
        Assert.Equal("grades.ipynb, cell 4, line 4", repeated.Location);
        Assert.Contains("checks the same thing as line 2,", repeated.Explanation, StringComparison.Ordinal);

        SaysNothingOfTheScript(report, TargetFactory.FromFile(notebook).ChosenFile!);
    }

    [Fact]
    public async Task ADivisionTriedForRealSaysTheLinesItRanCellByCell()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"tried\tried.ipynb",
            ("markdown", "# Averages"),
            ("code", "import statistics"),
            ("code", "def average(values):\n    total = sum(values)\n    return total / len(values)"),
            ("code", "print(average([3, 4]))"));

        var report = await CheckAsync(notebook);

        // Run with the empty list that breaks it; the lines it went through are the def and the two lines of the third cell.
        var division = Assert.Single(report.Findings, finding => finding.RuleId == "analysis-division-by-zero");
        Assert.Equal("tried.ipynb, cell 3, line 3", division.Location);
        Assert.Equal(
            "Running `average(values=[])` stopped with ZeroDivisionError: division by zero on line 3, where `total` was 0 and `values` was []. " +
            "Lines it ran: cell 3: 1-3.",
            division.Confirmation);
    }

    [Fact]
    public async Task AnExpressionEndingACellIsShownByJupyterSoItsValueIsNotSaidToBeThrownAway()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"names\names.ipynb",
            ("code", "name = \"ada lovelace\"\nname.title()\nprint(name)"),
            ("code", "name.upper()"));

        var report = await CheckAsync(notebook);

        // In the middle of a cell the new string is thrown away; at the end of one, Jupyter shows it under the cell.
        var discarded = Assert.Single(report.Findings, finding => finding.RuleId == "logic-python-result-discarded");
        Assert.Equal("names.ipynb, cell 1, line 2", discarded.Location);
    }

    [Fact]
    public async Task IPythonCommandsAndCellsInOtherLanguagesDoNotStopTheCodeBeingChecked()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        // Nothing is installed: the pip line stands for itself and does nothing, as every IPython command here does.
        var notebook = Notebook(@"magics\magics.ipynb",
            ("code", "%matplotlib inline\n!pip install --quiet requests"),
            ("code", "%%bash\nls -la\necho done"),
            ("code", "%time total = sum(range(10))\nprint(total)"),
            ("code", "%%time\ndoubled = total * 2\nprint(doubled)"));

        var report = await CheckAsync(notebook, "45\n90");

        Assert.DoesNotContain(report.Findings, finding => finding.Kind is FindingKind.Syntax or FindingKind.Runtime);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task IPythonCommandsThatChangeWhatComesAfterAreCarriedOutAndTheRestAreNamed()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"commands\data\marks.txt", "40\n2\n");
        Write(@"commands\helpers.py", "def total(values):\n    return sum(values)\n");
        var notebook = Notebook(@"commands\commands.ipynb",
            ("code", "pip install --quiet requests\n%run helpers.py\n%env GREETING=hello"),
            ("code", "%cd data\nwith open(\"marks.txt\") as source:\n    marks = [int(line) for line in source]"),
            ("code", "import os\nprint(os.environ[\"GREETING\"], total(marks))\nlen?"),
            ("code", "!wget https://example.com/scores.csv\nfiles = !ls"),
            ("code", "%%writefile notes.txt\nsome notes"));

        // %run defines total, %env sets GREETING and %cd goes into data, where marks.txt is; pip and len? change nothing read.
        var report = await CheckAsync(notebook, "hello 42");

        Assert.DoesNotContain(report.Findings, finding => finding.Kind is FindingKind.Syntax or FindingKind.Runtime);
        Assert.Equal("It printed what you expected", report.LogicSummary);

        // Nothing is fetched or written: the commands that would have been are named, with where they are.
        Assert.Contains(
            "commands.ipynb has IPython commands FixFinder does not carry out: \"!wget https://example.com/scores.csv\" (cell 4, line 1), " +
            "\"files = !ls\" (cell 4, line 2), \"%%writefile notes.txt\" (cell 5, line 1). Whatever they would have done - fetched or " +
            "written a file, defined a name - is missing when the cells after them run, and a failure that follows from that is not a " +
            "mistake in the notebook's code.",
            report.Notes);
        Assert.False(File.Exists(Path.Combine(_temp.Path, @"commands\notes.txt")));
        Assert.False(File.Exists(Path.Combine(_temp.Path, @"commands\data\scores.csv")));
    }

    [Fact]
    public async Task ANotebookWrittenForColabIsSaidToBeOneRatherThanToNeedAPackage()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"colab\colab.ipynb",
            ("code", "from google.colab import drive\ndrive.mount(\"/content/drive\")"),
            ("code", "print(\"mounted\")"));

        var report = await CheckAsync(notebook);

        // Whether Python names google.colab or only google as missing, it is Colab's module, and nothing is offered to install.
        var colab = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal("python-colab-only", colab.RuleId);
        Assert.Equal(Severity.Warning, colab.Severity);
        Assert.Equal("colab.ipynb, cell 1, line 1", colab.Location);
        Assert.StartsWith("google.colab is Google Colab's own module", colab.Explanations.At(ExplanationLevel.Beginner), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACellThatAwaitsOutsideAFunctionRunsAsItDoesInJupyter()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"awaiting\awaiting.ipynb",
            ("code", "import asyncio\n\n\nasync def doubled(value):\n    await asyncio.sleep(0)\n    return value * 2"),
            ("code", "result = await doubled(21)\nprint(result)"),
            ("code", "print(10 / (result - 42))"));

        var report = await CheckAsync(notebook);

        // Plain Python refuses the whole file; Jupyter runs it, and so does FixFinder - as far as the third cell's mistake.
        Assert.DoesNotContain(report.Findings, finding => finding.Kind == FindingKind.Syntax);
        Assert.Contains(report.Run!.Run!.Lines, line => line.Text == "42");
        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal("awaiting.ipynb, cell 3, line 1", stopped.Location);
    }

    [Fact]
    public async Task PlotlysShowCarriesOnAsInJupyterRatherThanOpeningABrowser()
    {
        if (PythonWith("plotly") is not { } python) return;

        RunsWith("plotly", python);
        var notebook = Notebook(@"plotly\charts.ipynb",
            ("code", "import plotly.graph_objects as go"),
            ("code", "figure = go.Figure(go.Bar(x=[\"a\", \"b\"], y=[3, 5]))\nfigure.show()"),
            ("code", "print(\"after the chart\")"));

        // Outside Jupyter, plotly would open a browser and wait for it to ask for the chart.
        var report = await CheckAsync(notebook, "after the chart", TimeSpan.FromSeconds(60));

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task CommandsWrittenWithoutTheirPercentAreReadAsIPythonReadsThem()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"bare\data\marks.txt", "40\n2\n");
        var notebook = Notebook(@"bare\bare.ipynb",
            ("code", "pip install --quiet requests\nls\npwd\ncd data\nmkdir results"),
            ("code", "with open(\"marks.txt\") as source:\n    marks = [int(line) for line in source]\ntime total = sum(marks)\nprint(total)"),
            ("code", "run = 2\nrun\nprint(run)"));

        var report = await CheckAsync(notebook, "42\n2");

        Assert.DoesNotContain(report.Findings, finding => finding.Kind is FindingKind.Syntax or FindingKind.Runtime);
        Assert.Equal("It printed what you expected", report.LogicSummary);

        // mkdir would make a folder, so it is named and not carried out; the notebook's own run is a name, not IPython's %run.
        Assert.Contains(report.Notes, note => note.Contains("\"mkdir results\" (cell 1, line 5)", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Notes, note => note.Contains("\"run\"", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(_temp.Path, @"bare\data\results")));
    }

    [Fact]
    public async Task DisplayShowsWhatItIsGivenOutsideJupyterToo()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"shown\shown.ipynb", ("code", "display(3 * 7)"));

        var report = await CheckAsync(notebook, "21");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public void ANotebookOfAnotherLanguageIsSaidToBeOneRatherThanRun()
    {
        var notebook = NotebookOf(@"r\analysis.ipynb", "R", ("code", "x <- c(1, 2, 3)\nmean(x)"));

        var launch = TargetFactory.FromFile(notebook);

        Assert.False(launch.Ok);
        Assert.Equal("analysis.ipynb is a notebook of R, and FixFinder checks the Python ones.", launch.Problem);
    }

    [Fact]
    public async Task ANotebooksOwnModulesAndDataFilesAreFoundBesideIt()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"project\helpers.py", "def load(path):\n    with open(path) as source:\n        return [int(line) for line in source]\n");
        Write(@"project\scores.txt", "12\n30\n");
        var notebook = Notebook(@"project\report.ipynb", ("code", "from helpers import load\nscores = load(\"scores.txt\")\nprint(sum(scores))"));

        var report = await CheckAsync(notebook, "42");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);

        // The module beside the notebook is part of the program the analyses read.
        Assert.Contains(Path.GetFullPath(Path.Combine(_temp.Path, @"project\helpers.py")), ProgramFiles.Of(TargetFactory.FromFile(notebook).ChosenFile!), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFolderOfNotebooksIsCheckedNotebookByNotebookWithTheModulesEachImports()
    {
        var helpers = Write(@"course\helpers.py", "def load(path):\n    return path\n");
        var firstWeek = Notebook(@"course\week1.ipynb", ("code", "from helpers import load\nprint(load(\"a\"))"));
        var secondWeek = Notebook(@"course\week2.ipynb", ("code", "print(2)"));

        var plan = ProjectScan.Of(Path.Combine(_temp.Path, "course"));

        // helpers.py belongs to the notebook that imports it, so it is checked as part of that one and not again on its own.
        Assert.Equal([firstWeek, secondWeek], plan.Programs.Select(program => program.Entry));
        Assert.Equal([firstWeek, helpers], plan.Programs[0].Files);
    }

    [Fact]
    public async Task ANotebookNamedAfterALibraryStillImportsTheLibrary()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"library\statistics.ipynb", ("code", "import statistics\nprint(statistics.mean([1, 2, 3]))"));

        var report = await CheckAsync(notebook, "2");

        Assert.Empty(report.Findings);
        Assert.Equal("It printed what you expected", report.LogicSummary);
    }

    [Fact]
    public async Task AFixToANotebookIsTriedOnACopyOfItsFolderWithItsDataFiles()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        Write(@"totals\marks.txt", "40\n2\n");
        var notebook = Notebook(@"totals\totals.ipynb",
            ("code", "with open(\"marks.txt\") as source:\n    marks = [int(line) for line in source]"),
            ("code", "def average(values):\n    return sum(values) / len(values)"),
            ("code", "print(average(marks))\nprint(average([]))"));

        var report = await CheckAsync(notebook);

        // The copy the fix is tried on has marks.txt beside it, as the notebook has, so its first cell reads the marks and
        // the run shows whether the fix works - rather than stopping at the missing file before it gets there.
        var byZero = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal("totals.ipynb, cell 2, line 2", byZero.Location);
        Assert.True(byZero.Verified.IsVerified, string.Join(" / ", byZero.Verified.Steps.Select(step => step.Detail)));
    }

    [Fact]
    public async Task ANotebookSavedAgainIsReadAsSavedWhenItIsCheckedOnSave()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"saved\saved.ipynb", ("code", "name = \"ada\"\nprint(name)"));
        var launch = TargetFactory.FromFile(notebook);

        // Saved again with a mistake in it after it was picked; the check that follows a save reads what was saved.
        Notebook(@"saved\saved.ipynb", ("code", "name = \"ada\"\nname.title()\nprint(name)"));

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckCodeAsync(launch);
        Print(report);

        var discarded = Assert.Single(report.Findings, finding => finding.RuleId == "logic-python-result-discarded");
        Assert.Equal("saved.ipynb, cell 1, line 2", discarded.Location);
    }

    [Fact]
    public async Task APrintedLineKeepsItsNumberWhileTheLinesOfTheCodeAreGivenInCells()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var notebook = Notebook(@"sums\sums.ipynb",
            ("code", "print(\"Totals\")\nprint(\"------\")\nlabel = \"sum\"\nwidth = 3\nprint(label.ljust(width))"),
            ("code", "def total_of(values):\n    result = 0\n    for value in values:\n        result -= value\n    return result"),
            ("code", "print(total_of([1, 2, 3]))"));

        var report = await CheckAsync(notebook, "Totals\n------\nsum\n6");

        // The fourth line printed is the wrong one; the line of code to change is the fourth of the second cell.
        var wrong = Assert.Single(report.Findings, finding => finding.RuleId == "wrong-output");
        Assert.Equal("sums.ipynb, cell 2, line 4", wrong.Location);
        Assert.Contains("Line 4 was \"-6\" where \"6\" was expected", wrong.Explanation, StringComparison.Ordinal);
        Assert.StartsWith("Change -= to += on line 4. Line 4 looks like", wrong.SuggestedFix, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyWhatIsSaidOfTheCodeIsPutInCellTerms()
    {
        // Cell 1 is script lines 1-3, two blank lines of FixFinder's own follow, and cell 3 - after a markdown cell - is lines 6-9.
        var notebookPath = Notebook(@"wording\wording.ipynb",
            ("code", "import json\ntotal = 0\nvalue = int(input())"),
            ("markdown", "The loop"),
            ("code", "for step in range(3):\n    total += step\nprint(total)\nprint(value)"));

        var (written, problem) = NotebookScript.Write(notebookPath);
        Assert.Null(problem);
        var notebook = written!;
        var scriptName = Path.GetFileName(notebook.Script);
        var fix = LocalFix.ReplaceLine("test-rule", "Add one", "Adds one.", notebook.Script, 7, "    total += step + 1");

        var inTheLoop = new Finding
        {
            Kind = FindingKind.Logic,
            Severity = Severity.Warning,
            Confidence = Confidence.Likely,
            File = notebook.Script,
            Line = 7,
            InNotebook = notebook.PlaceOf(7),
            Title = "Line up lines 7-9 with the line before",
            Explanation = "the loop that begins on line 6 runs 3 times; `total` was set on line 2 and `print(\"line 9\")` is quoted",
            WhyItMatters = "lines 2 or 7 decide it, and line 4 of helpers.py is where it is called",
            SuggestedFix = "Look at line 4, and at line 99",
            CorrectedExample = "",
            Witness = "the number typed at line 3 is 0",
            FixCheckedBy = $"A copy of {scriptName} with this change adds no new errors.",
            Verified = Verification.NotTested
                .With(VerificationStage.Ran, StageResult.Passed, "The copy ran to the end without failing.")
                .With(VerificationStage.MatchedExpectedOutput, StageResult.Failed, "line 2 was \"b\" where \"c\" was expected"),
            State = new StateTrace { Line = 7, Columns = ["total"], Rows = [new StateRow(1, ["0"], false)], Passes = 1 },
            Fix = fix,
            Change = CodeChange.From(fix, SourceFile.Read(notebook.Script)),
        };

        var worded = NotebookWording.AsInTheNotebook(inTheLoop, notebook);

        Assert.Equal("Line up lines 2-4 with the line before", worded.Title);
        Assert.Equal("the loop that begins on line 1 runs 3 times; `total` was set on line 2 of cell 1 and `print(\"line 9\")` is quoted", worded.Explanation);
        Assert.Equal("line 2 of cell 1 or line 2 decide it, and line 4 of helpers.py is where it is called", worded.WhyItMatters);
        Assert.Equal("Look at a line FixFinder added to run the notebook, and at line 99", worded.SuggestedFix);
        Assert.Equal("the number typed at line 3 of cell 1 is 0", worded.Witness);
        Assert.Equal("A copy of wording.ipynb with this change adds no new errors.", worded.FixCheckedBy);
        Assert.Equal(["The copy ran to the end without failing.", "line 2 was \"b\" where \"c\" was expected"], worded.Verified.Steps.Select(step => step.Detail));
        Assert.Equal(2, worded.State!.Line);

        // The change is numbered in its cell; the blank line of FixFinder's own before the cell is not shown as part of it.
        Assert.Equal([(ChangeKind.Context, 1), (ChangeKind.Removed, 2), (ChangeKind.Added, 2), (ChangeKind.Context, 3), (ChangeKind.Context, 4)],
            worded.Change!.Lines.Select(line => (line.Kind, line.Number)));

        // What the program said about its data stays as it said it, even where it has the word line and a number in it.
        const string saidByTheProgram = "Error tokenizing data. C error: Expected 3 fields in line 5, saw 4";
        var stopped = inTheLoop with
        {
            Kind = FindingKind.Runtime,
            Title = $"ParserError: {saidByTheProgram}",
            Explanation = $"Reading the file stopped on line 6: {saidByTheProgram}",
            Error = new ParsedError { LanguageId = "python", Confidence = 90, RawText = saidByTheProgram, FirstLineSequence = 0, Frames = [], Message = saidByTheProgram },
        };

        var stoppedWorded = NotebookWording.AsInTheNotebook(stopped, notebook);
        Assert.Equal($"ParserError: {saidByTheProgram}", stoppedWorded.Title);
        Assert.Equal($"Reading the file stopped on line 1: {saidByTheProgram}", stoppedWorded.Explanation);

        // A finding in a module the notebook imports keeps its own lines, and names the notebook's by their cells.
        var inTheModule = inTheLoop with
        {
            File = Path.Combine(Path.GetDirectoryName(notebookPath)!, "helpers.py"),
            InNotebook = null,
            Explanation = $"called from line 7 of {scriptName}; see line 3 here",
        };

        Assert.Equal("called from line 2 of cell 3 of wording.ipynb; see line 3 here", NotebookWording.AsInTheNotebook(inTheModule, notebook).Explanation);
    }
}
