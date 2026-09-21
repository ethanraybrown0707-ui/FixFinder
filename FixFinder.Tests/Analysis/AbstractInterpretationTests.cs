using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Checking;

namespace FixFinder.Tests;

/// <summary>
/// Abstract interpretation of Python: each mistake is found on its line, and each shape of correct code that once
/// raised a false alarm in the standard library stays quiet.
/// </summary>
public class AbstractInterpretationTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Analyse(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "app.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        var program = await PythonFrontend.ReadAsync([path], python);
        Assert.Empty(program.Problems);
        return AbstractChecks.Run(program, new SourceText());
    }

    private static int LineOf(string code, string marker) =>
        code.ReplaceLineEndings("\n").Split('\n').Select((text, i) => (text, i)).First(l => l.text.Contains(marker, StringComparison.Ordinal)).i + 1;

    [Theory]
    [InlineData("analysis-division-by-zero", "Possible", "def average(values):\n    total = 0\n    count = 0\n    for v in values:\n        total += v\n        count += 1\n    return total / count\n", "total / count")]
    [InlineData("analysis-division-by-zero", "Certain", "def f():\n    zero = 0\n    return 10 / zero\n", "10 / zero")]
    [InlineData("analysis-null-used", "Possible", "def label(score):\n    message = None\n    if score > 90:\n        message = 'top'\n    return message.upper()\n", "message.upper()")]
    [InlineData("analysis-null-used", "Possible", "import re\n\ndef first(text):\n    found = re.match('a', text)\n    return found.group(0)\n", "found.group")]
    [InlineData("analysis-null-used", "Certain", "def f():\n    value = None\n    return value.strip()\n", "value.strip()")]
    [InlineData("analysis-type-mismatch", "Certain", "def describe(age):\n    return 'Age: ' + 30\n", "'Age: ' + 30")]
    [InlineData("analysis-type-mismatch", "Certain", "def f():\n    for x in 5:\n        print(x)\n", "for x in 5")]
    [InlineData("analysis-never-true", "Likely", "def grade(mark):\n    if mark > 100 and mark < 0:\n        return 'impossible'\n    return 'ok'\n", "mark > 100")]
    [InlineData("analysis-always-true", "Likely", "def grade(mark):\n    if mark >= 50:\n        return 'pass'\n    elif mark < 50:\n        return 'fail'\n", "elif mark < 50")]
    [InlineData("analysis-index-out-of-range", "Certain", "def f():\n    points = [3, 5, 8]\n    return points[3]\n", "points[3]")]
    [InlineData("analysis-empty-collection", "Certain", "def f():\n    stack = []\n    return stack.pop()\n", "stack.pop()")]
    [InlineData("analysis-loop-never-runs", "Likely", "def f():\n    n = 0\n    while n > 0:\n        n -= 1\n    return n\n", "while n > 0")]
    [InlineData("analysis-assert-always-fails", "Certain", "def f():\n    n = 3\n    assert n > 5\n    return n\n", "assert n > 5")]
    [InlineData("analysis-not-a-number", "Certain", "def f():\n    return int('abc')\n", "int('abc')")]
    public async Task EachMistakeIsFoundOnItsLine(string check, string confidence, string code, string marker)
    {
        if (await Analyse(code) is not { } findings) return;

        var finding = findings.SingleOrDefault(f => f.CheckId == check);
        Assert.True(finding is not null, $"{check} not found; found: {string.Join("; ", findings.Select(f => f.CheckId))}");
        Assert.Equal(LineOf(code, marker), finding!.Span.Line);
        Assert.Equal(Enum.Parse<Confidence>(confidence), finding.Confidence);
        Assert.StartsWith(AbstractChecks.FoundBy, finding.FoundBy);
    }

    [Theory]
    [InlineData("a guard on the collection", "def average(values):\n    if not values:\n        return 0\n    return sum(values) / len(values)\n")]
    [InlineData("a guard on the count", "def f(total, count):\n    if count > 0:\n        return total / count\n    return 0\n")]
    [InlineData("a strict guard on a decimal", "def f(alpha):\n    if alpha <= 0.0:\n        raise ValueError('alpha')\n    return 1.0 / alpha\n")]
    [InlineData("a list changed through its bound method", "def f(items):\n    parts = []\n    add = parts.append\n    for item in items:\n        add(item)\n    if len(parts) == 2:\n        return parts\n    return None\n")]
    [InlineData("a variable changed by a nested function", "def f(items):\n    found = False\n    def mark():\n        nonlocal found\n        found = True\n    mark()\n    if found:\n        return 1\n    return 0\n")]
    [InlineData("a default taken with or", "def capwords(s, sep=None):\n    return (sep or ' ').join(s.split(sep))\n")]
    [InlineData("a global set up by a call", "_db = None\n\ndef init():\n    global _db\n    _db = object()\n\ndef guess(url):\n    if _db is None:\n        init()\n    return _db.guess(url)\n")]
    [InlineData("an equality test on something unknown", "def f(maxsize):\n    if maxsize == 0:\n        return 0\n    elif maxsize is None:\n        return 1\n    return 2\n")]
    [InlineData("an object's own truth value", "class Amount:\n    def is_zero(self):\n        if not self:\n            return True\n        return False\n")]
    [InlineData("a check repeated inside a lock", "import threading\n_lock = threading.Lock()\n_order = None\n\ndef f():\n    global _order\n    if _order is None:\n        with _lock:\n            if _order is None:\n                _order = []\n    return _order\n")]
    [InlineData("a run-time check of a type hint", "def set_flags(flags: str):\n    if not isinstance(flags, str):\n        raise TypeError('flags')\n    return flags\n")]
    [InlineData("an attribute every object has", "def f():\n    return None.__class__\n")]
    [InlineData("the last item of a list of unknown length", "def last(items):\n    return items[-1]\n")]
    [InlineData("a switch set once", "def f():\n    debug = False\n    if debug:\n        print('debugging')\n    return 1\n")]
    public async Task CorrectCodeIsLeftAlone(string shape, string code)
    {
        if (await Analyse(code) is not { } findings) return;

        Assert.True(findings.Count == 0, $"{shape}: {string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"))}");
    }
}
