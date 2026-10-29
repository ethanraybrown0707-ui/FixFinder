using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A crash and the analysis that proved it are one mistake, so one finding: each crash is tied to the check that finds the same
/// mistake by its exception and - where one exception means more than one mistake - by its message, as Python 3.13, Java 21 and
/// .NET 10 were seen to write it; and the finding kept has the input that fails, the lines that decide it, the run that
/// confirmed it and what the language itself said.
/// </summary>
public class OneMistakeOneFindingTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ParsedError Crash(string language, string type, string? message) => new()
    {
        LanguageId = language, Confidence = 90, RawText = "", FirstLineSequence = 0, Frames = [], ExceptionType = type, Message = message,
    };

    [Theory]
    [InlineData("marks.py", "python", "IndexError", "pop from empty list", "analysis-empty-collection")]
    [InlineData("marks.py", "python", "IndexError", "pop from an empty deque", "analysis-empty-collection")]
    [InlineData("marks.py", "python", "IndexError", "list index out of range", "analysis-index-out-of-range")]
    [InlineData("Marks.java", "java", "java.util.EmptyStackException", null, "analysis-empty-collection")]
    [InlineData("Marks.java", "java", "java.util.NoSuchElementException", null, "analysis-empty-collection")]
    [InlineData("Marks.java", "java", "java.util.NoSuchElementException", "No value present", "logic-java-optional-get-unchecked")]
    [InlineData("fetch.py", "python", "RuntimeError", "asyncio.run() cannot be called from a running event loop", "logic-python-asyncio-run-in-async")]
    [InlineData("fetch.py", "python", "RuntimeError", "dictionary changed size during iteration", null)]
    [InlineData("Marks.scala", "scala", "java.util.NoSuchElementException", "None.get", "logic-scala-option-get")]
    [InlineData("Marks.cs", "csharp", "System.InvalidOperationException", "Stack empty.", "analysis-empty-collection")]
    [InlineData("Marks.cs", "csharp", "System.InvalidOperationException", "Sequence contains no elements", "analysis-empty-collection")]
    [InlineData("Marks.cs", "csharp", "System.InvalidOperationException", "Collection was modified; enumeration operation may not execute.", null)]
    [InlineData("marks.py", "python", "ValueError", "I/O operation on closed file.", "analysis-used-after-close")]
    [InlineData("marks.py", "python", "sqlite3.ProgrammingError", "Cannot operate on a closed database.", "analysis-connection-used-after-close")]
    [InlineData("ping.py", "python", "OSError", "[WinError 10057] A request to send or receive data was disallowed because the socket is not connected and (when sending on a datagram socket using a sendto call) no address was supplied", "analysis-used-before-connect")]
    [InlineData("ping.py", "python", "OSError", "[WinError 10038] An operation was attempted on something that is not a socket", "analysis-connection-used-after-close")]
    [InlineData("Ping.java", "java", "java.net.SocketException", "Socket is not connected", "analysis-used-before-connect")]
    [InlineData("Ping.java", "java", "java.net.SocketException", "Socket is closed", "analysis-connection-used-after-close")]
    [InlineData("Ping.cs", "csharp", "System.InvalidOperationException", "The operation is not allowed on non-connected sockets.", "analysis-used-before-connect")]
    [InlineData("Ping.cs", "csharp", "System.ObjectDisposedException", "Cannot access a disposed object. Object name: 'System.Net.Sockets.TcpClient'.", "analysis-connection-used-after-close")]
    [InlineData("count.py", "python", "RecursionError", "maximum recursion depth exceeded", "analysis-recursion-never-ends")]
    [InlineData("Count.java", "java", "java.lang.StackOverflowError", null, "analysis-recursion-never-ends")]
    [InlineData("count.js", "node", "RangeError", "Maximum call stack size exceeded", "analysis-recursion-never-ends")]
    public void ACrashIsTiedToTheCheckForTheSameMistakeByItsNameAndMessage(string file, string language, string type, string? message, string? family)
    {
        var finding = FindingFactory.FromError(Crash(language, type, message), FindingKind.Runtime, Severity.Error, Confidence.Certain, Path.Combine(@"C:\work", file));

        Assert.Equal(family, finding.Family);
    }

    [Fact]
    public void APopFromAnEmptyListIsExplainedAsThatNotAsAnIndexPastTheEnd()
    {
        var popped = Guidebook.For(@"C:\work\marks.py", FindingKind.Runtime, error: Crash("python", "IndexError", "pop from empty list"));
        var pastTheEnd = Guidebook.For(@"C:\work\marks.py", FindingKind.Runtime, error: Crash("python", "IndexError", "list index out of range"));

        Assert.StartsWith("pop() takes an item off a list", popped.Explanation, StringComparison.Ordinal);
        Assert.StartsWith("The items in a list are numbered from 0", pastTheEnd.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Java's NoSuchElementException is get() on an empty Optional when it says "No value present", and the Scanner run out
    /// of input when it says "No line found" - nextLine - or nothing at all - nextInt and next - as Java 21 was seen to say.
    /// </summary>
    [Theory]
    [InlineData("No value present", "get() takes the value out of an Optional")]
    [InlineData("No line found", "The program asked the Scanner for another piece of input")]
    [InlineData(null, "The program asked the Scanner for another piece of input")]
    public void AnEmptyOptionalAndAScannerOutOfInputAreToldApartByTheirMessage(string? message, string explanationStart)
    {
        var guide = Guidebook.For(@"C:\work\Main.java", FindingKind.Runtime, error: Crash("java", "java.util.NoSuchElementException", message));

        Assert.StartsWith(explanationStart, guide.Explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// An error the program raised itself, at its own raise or throw, is explained as a check refusing a value - not by the
    /// guide for the error's name, whose ValueError is int("forty-two"). A ValueError Python raised itself keeps that guide.
    /// </summary>
    [Theory]
    [InlineData("marks.py", "python", "ValueError", "no marks to average", "marks = []\nif not marks:\n    raise ValueError(\"no marks to average\")\n", 3, "The program's own code stopped it here, on purpose: this line is a raise")]
    [InlineData("Main.java", "java", "java.lang.IllegalArgumentException", "age must not be negative", "class Main {\n    static int checkedAge(int age) {\n        if (age < 0) {\n            throw new IllegalArgumentException(\"age must not be negative\");\n        }\n        return age;\n    }\n}\n", 4, "The program's own code stopped it here, on purpose: this line is a throw")]
    [InlineData("marks.py", "python", "ValueError", "invalid literal for int() with base 10: 'forty-two'", "mark = int(\"forty-two\")\n", 1, "The value is the right kind of thing but has the wrong content")]
    public void AnErrorTheProgramRaisedItselfIsExplainedAsACheckRefusingAValue(string file, string language, string type, string message, string code, int line, string explanationStart)
    {
        var path = Path.Combine(_temp.Path, file);
        File.WriteAllText(path, code);
        var frame = new ErrorFrame { Order = 0, File = path, Line = line, RawLine = "" };
        var crash = new ParsedError
        {
            LanguageId = language, Confidence = 90, RawText = "", FirstLineSequence = 0, ExceptionType = type, Message = message,
            Frames = [frame], CulpritFrame = frame,
        };

        var finding = FindingFactory.FromError(crash, FindingKind.Runtime, Severity.Error, Confidence.Certain, path);

        Assert.StartsWith(explanationStart, finding.Explanation, StringComparison.Ordinal);
    }

    private async Task<CheckReport?> CheckPythonAsync(string name, string code)
    {
        if (TargetFactory.FindOnPath("python") is null)
        {
            output.WriteLine("Not run on this computer: no Python.");
            return null;
        }

        var file = Path.Combine(_temp.Path, name);
        File.WriteAllText(file, code);
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(2)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Python }.CheckAsync(launch);
        foreach (var each in report.Findings) output.WriteLine($"{each.Line}: {each.RuleId}: {each.Found} | {each.Witness} | {each.Confirmation}");
        return report;
    }

    /// <summary>
    /// divide(10, 0) breaks divide's contract - it raises ValueError when count is 0 - and the run stops with that ValueError,
    /// raised at line 9 through the call at line 13. The two are one finding, at the call, where the fix belongs, with Python's
    /// own message.
    /// </summary>
    [Fact]
    public async Task ACrashABrokenContractRaisedIsOneFindingAtTheCall()
    {
        if (await CheckPythonAsync("divide.py",
                "def validate(score):\n    if score > 100:\n        raise ValueError(\"score out of range\")\n    return score\n\n\n" +
                "def divide(total, count):\n    if count == 0:\n        raise ValueError(\"count must not be 0\")\n    return total / count\n\n\n" +
                "print(divide(10, 0))\nprint(validate(150))\n") is not { } report) return;

        Assert.Equal([13, 14], report.Findings.Select(finding => finding.Line!.Value).Order());
        var call = Assert.Single(report.Findings, finding => finding.Line == 13);
        Assert.Equal("analysis-contract-broken", call.RuleId);
        Assert.Equal(("ValueError", "count must not be 0"), (call.Error?.ShortExceptionType, call.Error?.Message));
    }

    /// <summary>
    /// is_even and is_odd call each other with no way out. Python stops the run with RecursionError wherever the room runs out,
    /// in one of the two; the analysis finds the ring at is_even. The two are one finding, at is_even, made certain by the run.
    /// </summary>
    [Fact]
    public async Task ARecursionCrashAndTheRingTheAnalysisFoundAreOneFinding()
    {
        if (await CheckPythonAsync("parity.py", "def is_even(n):\n    return is_odd(n - 1)\n\n\ndef is_odd(n):\n    return is_even(n - 1)\n\n\nprint(is_even(4))\n") is not { } report) return;

        var finding = Assert.Single(report.Findings);
        Assert.Equal((2, "analysis-recursion-never-ends", Confidence.Certain), (finding.Line, finding.RuleId, finding.Confidence));
        Assert.Equal(("RecursionError", "maximum recursion depth exceeded"), (finding.Error?.ShortExceptionType, finding.Error?.Message));
    }

    /// <summary>
    /// asyncio.run inside an async def: Python 3.13 stopped with RuntimeError: asyncio.run() cannot be called from a running
    /// event loop, at line 9. The run and the check that finds the call are one finding, with the change - await fetch() -
    /// that was run and printed 42.
    /// </summary>
    [Fact]
    public async Task AsyncioRunInsideAnAsyncDefIsOneFindingWithItsChange()
    {
        if (await CheckPythonAsync("fetch.py",
                "import asyncio\n\n\nasync def fetch():\n    return 42\n\n\nasync def main():\n    value = asyncio.run(fetch())\n    print(value)\n\n\nasyncio.run(main())\n")
            is not { } report) return;

        var finding = Assert.Single(report.Findings);
        Assert.Equal((9, "logic-python-asyncio-run-in-async"), (finding.Line, finding.RuleId));
        Assert.Equal(("RuntimeError", "asyncio.run() cannot be called from a running event loop"), (finding.Error?.ShortExceptionType, finding.Error?.Message));
        Assert.Equal(["    value = await fetch()"], finding.Fix?.NewLines);
    }

    /// <summary>
    /// take(False) pops from an empty list: Python stops the run there, and the analysis proves it with the input that does it.
    /// The two are one finding, with the input, the run that confirmed it, the lines that decide it and Python's own words.
    /// </summary>
    [Fact]
    public async Task ACrashTheAnalysisProvedIsOneFindingWithTheInputThePathAndThePythonError()
    {
        if (await CheckPythonAsync("take.py", "def take(flag):\n    items = []\n    if flag:\n        items.append(1)\n    return items.pop()\n\n\nprint(take(True))\nprint(take(False))\n") is not { } report) return;

        var finding = Assert.Single(report.Findings);
        Assert.Equal((5, "analysis-empty-collection", Confidence.Certain), (finding.Line, finding.RuleId, finding.Confidence));
        Assert.Equal("On one way through the code `items` is always empty here - for example when `flag` is False - so `items.pop()` fails with IndexError.", finding.Found);
        Assert.Equal("`flag` is False", finding.Witness);
        Assert.StartsWith("Running `take(flag=False)` stopped with IndexError: pop from empty list on line 5", finding.Confirmation);
        Assert.Equal([2, 5], finding.Slice);
        Assert.Equal(("IndexError", "pop from empty list"), (finding.Error?.ShortExceptionType, finding.Error?.Message));
    }
}
