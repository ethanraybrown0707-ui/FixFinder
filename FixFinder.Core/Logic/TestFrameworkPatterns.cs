using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.LocalFixes.Rules;

namespace FixFinder.Core.Logic;

/// <summary>
/// Tests that never run, or can never fail - so a program can be wrong and its tests still pass. Each was run with the language's
/// own test runner, testing an add that subtracts, and none of the runners or their warnings said anything:
/// Python's unittest ran no method without test at the start of its name, and no test in a class that is not a TestCase - Ran
/// 3 tests of 5; it never called a set-up named setup, so its test stopped with AttributeError; self.assertTrue(add(5, 2), 7)
/// passed, as the 7 is only the message shown on failure; a test whose assertEqual sits in a try with except AssertionError:
/// pass passed; and so did a test that asserts nothing. Ruby's minitest ran no method without test_ at its start - 1 runs - and
/// assert add(5, 2), 7 passed, the 7 again the message; Elixir's ExUnit ran no def test_add, only test blocks; and go test ran
/// no testAdd, only functions whose names start with Test - with nothing from ruby -w, Elixir or go vet. pytest 9.1, even with
/// -W error, collected no function without test at its start and no class without Test at its start - 2 tests of 4 - and assert
/// add(5, 2), 7 passed. JUnit 1.11.4 found 3 tests of 8, with nothing from it or javac -Xlint:all: it ran no method that asserts
/// without @Test, no @Test method that is private, static or gives back a value, and never called a setUp with no @BeforeEach,
/// whose test stopped with NullPointerException; a test whose assertEquals sits in a try that catches AssertionError passed.
/// Perl's Test::More passed ok(add(5, 2), 7), only remarking on a test named 7, and EUnit ran no test_add, which erlc calls
/// unused; those are here too - erlc's warning and the check are one finding. Elixir warns of assert result = 7, which is left to it.
/// </summary>
public static partial class TestFrameworkPatterns
{
    private static readonly IReadOnlySet<string> PythonFiles = CodePattern.Files(".py", ".pyw");
    private static readonly IReadOnlySet<string> JavaFiles = CodePattern.Files(".java");

    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-python-test-never-run", PythonFiles, Syntax.Python, PythonTestNeverRun, Severity.Error, Confidence.Likely),
        new CodePattern("logic-python-test-class-not-testcase", PythonFiles, Syntax.Python, PythonClassNotTestCase, Severity.Error, Confidence.Likely),
        new CodePattern("logic-python-setup-misspelt", PythonFiles, Syntax.Python, PythonSetUpMisspelt, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-assert-true-with-expected", PythonFiles, Syntax.Python, PythonAssertTrueWithExpected, Severity.Error, Confidence.Likely),
        new CodePattern("logic-python-assertion-swallowed", PythonFiles, Syntax.Python, PythonAssertionSwallowed, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-test-checks-nothing", PythonFiles, Syntax.Python, PythonTestChecksNothing, Severity.Suggestion, Confidence.Possible),
        new CodePattern("logic-ruby-test-never-run", CodePattern.Files(".rb"), Syntax.Python, RubyTestNeverRun, Severity.Error, Confidence.Likely),
        new CodePattern("logic-ruby-assert-with-expected", CodePattern.Files(".rb"), Syntax.Python, RubyAssertWithExpected, Severity.Error, Confidence.Likely),
        new CodePattern("logic-elixir-test-never-run", CodePattern.Files(".exs", ".ex"), Syntax.Python, ElixirTestNeverRun, Severity.Error, Confidence.Certain),
        new CodePattern("logic-go-test-never-run", CodePattern.Files(".go"), Syntax.CLike, GoTestNeverRun, Severity.Error, Confidence.Likely),
        new CodePattern("logic-perl-ok-with-expected", CodePattern.Files(".pl", ".pm", ".t"), Syntax.Perl, PerlOkWithExpected, Severity.Error, Confidence.Likely),
        new CodePattern("logic-erlang-test-never-run", CodePattern.Files(".erl"), Syntax.Erlang, ErlangTestNeverRun, Severity.Error, Confidence.Certain),
        new CodePattern("logic-python-pytest-test-never-run", PythonFiles, Syntax.Python, PytestTestNeverRun, Severity.Error, Confidence.Likely),
        new CodePattern("logic-python-pytest-class-not-collected", PythonFiles, Syntax.Python, PytestClassNotCollected, Severity.Error, Confidence.Likely),
        new CodePattern("logic-python-assert-with-expected", PythonFiles, Syntax.Python, PythonAssertWithExpected, Severity.Error, Confidence.Likely),
        new CodePattern("logic-java-test-missing-annotation", JavaFiles, Syntax.CLike, JavaTestMissingAnnotation, Severity.Error, Confidence.Likely),
        new CodePattern("logic-java-test-not-run-shape", JavaFiles, Syntax.CLike, JavaTestNotRunShape, Severity.Error, Confidence.Certain),
        new CodePattern("logic-java-setup-not-annotated", JavaFiles, Syntax.CLike, JavaSetUpNotAnnotated, Severity.Error, Confidence.Likely),
        new CodePattern("logic-java-assertion-swallowed", JavaFiles, Syntax.CLike, JavaAssertionSwallowed, Severity.Error, Confidence.Certain),
    ];

    // ---- pytest ----

    /// <summary>Whether a file is one pytest collects tests from: named test_*.py or *_test.py, or importing pytest.</summary>
    private static bool PytestFile(SourceFile source, IReadOnlyList<string> masked)
    {
        var name = Path.GetFileName(source.Path);
        return Regex.IsMatch(name, @"^test_.*\.py$|_test\.py$", RegexOptions.IgnoreCase) ||
               masked.Any(line => Regex.IsMatch(line, @"^\s*(?:import\s+pytest\b|from\s+pytest\s+import\b)"));
    }

    [GeneratedRegex(@"^def\s+(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^)]*)\)\s*(?:->\s*[^:]+)?:")]
    private static partial Regex TopLevelFunction();

    private static IEnumerable<LogicFinding> PytestTestNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!PytestFile(source, masked)) yield break;
        var code = string.Join("\n", masked);
        // A file of unittest's TestCases is unittest's, and a function there that is not a test is checked as unittest checks it.
        if (!masked.Any(line => Regex.IsMatch(line, @"^def\s+test\w*\s*\("))) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (TopLevelFunction().Match(masked[index]) is not { Success: true } function) continue;
            var name = function.Groups["name"].Value;
            if (name.StartsWith("test", StringComparison.Ordinal) || name.StartsWith('_') || Decorated(masked, index)) continue;

            var body = IndentedBody(masked, index);
            if (!Enumerable.Range(body.First, body.End - body.First).Any(line => Regex.IsMatch(masked[line], @"^\s*assert\b"))) continue;
            // A helper the tests call is not a test.
            if (Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(name)}\b").Count > 1) continue;

            var line = source.Lines[index];
            var at = function.Groups["name"].Index;
            yield return new LogicFinding(id, index + 1,
                $"{name} asserts, but pytest collects only the functions whose names start with test - so it never runs, and what it asserts is " +
                "never checked: pytest collected 2 tests of 4",
                LocalFix.ReplaceLine(id, $"Name it as a test: test_{name}",
                    "pytest collects every function whose name starts with test, and runs it.",
                    source.Path, index + 1, line[..at] + "test_" + line[at..]));
        }
    }

    private static IEnumerable<LogicFinding> PytestClassNotCollected(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!PytestFile(source, masked)) yield break;
        var classes = PythonClasses(masked);
        foreach (var type in classes)
        {
            if (type.Name.StartsWith("Test", StringComparison.Ordinal) || IsTestCase(type, classes) || (type.Bases.Trim().Length > 0 && type.Bases.Trim() != "object")) continue;
            // Tests written for pytest: test_ methods with bare asserts.
            var tests = MethodsOf(masked, type).Count(method => method.Name.StartsWith("test", StringComparison.Ordinal) &&
                Enumerable.Range(method.Body.First, method.Body.End - method.Body.First).Any(inside => Regex.IsMatch(masked[inside], @"^\s*assert\b")));
            if (tests == 0) continue;

            var stem = Regex.Replace(type.Name, "Tests?$", "");
            var collected = "Test" + (stem.Length > 0 ? stem : type.Name);
            var written = source.Lines[type.Line];
            var at = written.IndexOf(type.Name, StringComparison.Ordinal);
            yield return new LogicFinding(id, type.Line + 1,
                $"pytest collects tests only from classes whose names start with Test, so {type.Name} is never looked in, and its {tests} " +
                $"test{(tests == 1 ? "" : "s")} never run{(tests == 1 ? "s" : "")}: pytest collected 2 tests of 4",
                LocalFix.ReplaceLine(id, $"Name it as pytest looks for: {collected}",
                    "pytest looks in every class whose name starts with Test, and runs its test methods.",
                    source.Path, type.Line + 1, written[..at] + collected + written[(at + type.Name.Length)..]));
        }
    }

    private static IEnumerable<LogicFinding> PythonAssertWithExpected(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var assertion = Regex.Match(masked[index], @"^(?<indent>\s*)assert\s+(?<rest>.+?)\s*$");
            if (!assertion.Success || assertion.Groups["rest"].Value.TrimStart().StartsWith('(')) continue;

            var rest = assertion.Groups["rest"];
            var parts = Arguments("(" + rest.Value + ")", 0, rest.Value.Length + 1);
            if (parts.Count != 2) continue;

            var line = source.Lines[index];
            var offset = rest.Index - 1;
            var value = line[(parts[0].Start + offset)..(parts[0].End + offset)].Trim();
            var expected = line[(parts[1].Start + offset)..(parts[1].End + offset)].Trim();
            // A message - text in quotes, an f-string, or text built with % or format - is what the second part is for.
            if (!Regex.IsMatch(expected, @"^(?:-?\d+(?:\.\d+)?|True|False|None|\[[^\]]*\]|\([^)]*\))$") ||
                Regex.IsMatch(value, @"==|!=|<|>|\bin\b|\bis\b|\bnot\b|\band\b|\bor\b|^(?:isinstance|callable|all|any|hasattr)\(")) continue;

            yield return new LogicFinding(id, index + 1,
                $"After the comma comes only the message shown when the assert fails, so assert {value}, {expected} passes whenever {value} is " +
                $"anything but 0, empty or None - {expected} is never compared: assert add(5, 2), 7 passed with add giving 3",
                LocalFix.ReplaceLine(id, $"Compare the two: assert {value} == {expected}",
                    $"== makes the assert fail unless {value} equals {expected}.",
                    source.Path, index + 1, $"{assertion.Groups["indent"].Value}assert {value} == {expected}"));
        }
    }

    // ---- JUnit 5 ----

    /// <summary>Whether a Java file holds JUnit 5's tests: it imports from org.junit.jupiter, and marks a method @Test.</summary>
    private static bool JupiterTests(IReadOnlyList<string> masked) =>
        masked.Any(line => Regex.IsMatch(line, @"^\s*import\s+(?:static\s+)?org\.junit\.jupiter\.")) && masked.Any(line => Regex.IsMatch(line, @"^\s*@(?:org\.junit\.jupiter\.api\.)?Test\b"));

    /// <summary>The annotations on the lines above a method's header, back to the first line that is not one.</summary>
    private static List<string> AnnotationsAbove(IReadOnlyList<string> masked, int header)
    {
        var annotations = new List<string>();
        for (var above = header - 1; above >= 0; above--)
        {
            var trimmed = masked[above].Trim();
            if (trimmed.Length == 0) continue;
            if (!trimmed.StartsWith('@')) break;
            annotations.Add(trimmed);
        }
        return annotations;
    }

    [GeneratedRegex(@"^(?<indent>\s*)(?<modifiers>(?:(?:public|protected|private|static|final|synchronized)\s+)*)(?<returns>[\w<>\[\],\s]+?)\s+(?<name>[a-zA-Z_]\w*)\s*\(\s*\)\s*(?:throws\s+[\w.,\s]+)?\{\s*$")]
    private static partial Regex JavaNoArgumentMethod();

    /// <summary>An annotation as the file can write it: by its own name when the file imports it, or its whole name when not.</summary>
    private static string Annotation(IReadOnlyList<string> masked, string name) =>
        masked.Any(line => Regex.IsMatch(line, $@"^\s*import\s+org\.junit\.jupiter\.api\.(?:{name}|\*)\s*;")) ? "@" + name : "@org.junit.jupiter.api." + name;

    private static IEnumerable<LogicFinding> JavaTestMissingAnnotation(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!JupiterTests(masked)) yield break;
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            if (JavaNoArgumentMethod().Match(masked[index]) is not { Success: true } method || method.Groups["returns"].Value.Trim() != "void") continue;
            if (AnnotationsAbove(masked, index).Count > 0 || method.Groups["modifiers"].Value.Contains("static", StringComparison.Ordinal)) continue;

            var name = method.Groups["name"].Value;
            if (Regex.IsMatch(name, "^(?:setUp|setup|tearDown|teardown|init|before|after|main)$", RegexOptions.IgnoreCase)) continue;
            if (BraceBlocks.Body(masked, index) is not { } body ||
                !Enumerable.Range(body.First, body.End - body.First).Any(line => Regex.IsMatch(masked[line], @"\bassert\w*\s*\(|\bfail\s*\("))) continue;
            if (Regex.Matches(code, $@"\b{Regex.Escape(name)}\s*\(").Count > 1) continue;

            var annotation = Annotation(masked, "Test");
            yield return new LogicFinding(id, index + 1,
                $"{name} asserts, but JUnit runs only the methods marked @Test - so it never runs, and what it asserts is never checked: JUnit found 3 " +
                "tests of 8",
                LocalFix.Insert(id, $"Mark it as a test: {annotation}",
                    "JUnit runs every method marked @Test.",
                    source.Path, index + 1, [method.Groups["indent"].Value + annotation]));
        }
    }

    private static IEnumerable<LogicFinding> JavaTestNotRunShape(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!JupiterTests(masked)) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (JavaNoArgumentMethod().Match(masked[index]) is not { Success: true } method) continue;
            if (!AnnotationsAbove(masked, index).Any(annotation => Regex.IsMatch(annotation, @"^@(?:org\.junit\.jupiter\.api\.)?Test\b"))) continue;

            var modifiers = method.Groups["modifiers"].Value;
            var returns = method.Groups["returns"].Value.Trim();
            var reasons = new List<string>();
            if (Regex.IsMatch(modifiers, @"\bprivate\b")) reasons.Add("is private");
            if (Regex.IsMatch(modifiers, @"\bstatic\b")) reasons.Add("is static");
            if (returns != "void") reasons.Add($"gives back {returns}");
            if (reasons.Count == 0) continue;

            var line = source.Lines[index];
            var modifiersGroup = method.Groups["modifiers"];
            var kept = Regex.Replace(modifiersGroup.Value, @"\b(?:private|static)\s+", "");
            var name = method.Groups["name"].Value;
            yield return new LogicFinding(id, index + 1,
                $"JUnit runs a @Test method only when it is not private, not static, and gives back nothing - {name} {string.Join(" and ", reasons)}," +
                "so it is never run, with nothing said: JUnit found 3 tests of 8",
                returns != "void" ? null : LocalFix.ReplaceLine(id, $"Make it a method JUnit runs: void {name}()",
                    "A test method that is neither private nor static is one JUnit can find and call on the test class.",
                    source.Path, index + 1, line[..modifiersGroup.Index] + kept + line[(modifiersGroup.Index + modifiersGroup.Length)..]));
        }
    }

    private static IEnumerable<LogicFinding> JavaSetUpNotAnnotated(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!JupiterTests(masked)) yield break;
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            if (JavaNoArgumentMethod().Match(masked[index]) is not { Success: true } method || method.Groups["returns"].Value.Trim() != "void") continue;
            var name = method.Groups["name"].Value;
            var tearing = Regex.IsMatch(name, "^(?:tearDown|teardown|cleanUp|cleanup|after)$");
            if (!tearing && !Regex.IsMatch(name, "^(?:setUp|setup|init|before)$")) continue;
            if (AnnotationsAbove(masked, index).Count > 0 || method.Groups["modifiers"].Value.Contains("static", StringComparison.Ordinal)) continue;
            if (Regex.Matches(code, $@"\b{Regex.Escape(name)}\s*\(").Count > 1) continue;

            var annotation = Annotation(masked, tearing ? "AfterEach" : "BeforeEach");
            yield return new LogicFinding(id, index + 1,
                $"JUnit calls a method {(tearing ? "after" : "before")} each test only when it is marked {annotation} - a name such as {name} means " +
                $"nothing to it - so {name} is never called: what it sets up is never there, and a test that uses it stopped with " +
                "NullPointerException",
                LocalFix.Insert(id, $"Mark it to run {(tearing ? "after" : "before")} each test: {annotation}",
                    $"JUnit calls every method marked {annotation} {(tearing ? "after" : "before")} each test.",
                    source.Path, index + 1, [method.Groups["indent"].Value + annotation]));
        }
    }

    private static IEnumerable<LogicFinding> JavaAssertionSwallowed(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!JupiterTests(masked) && !masked.Any(line => Regex.IsMatch(line, @"^\s*import\s+(?:static\s+)?org\.junit\."))) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (!Regex.IsMatch(masked[index], @"^\s*try\s*\{\s*$") || Brackets.FirstBlockEnd(masked, index) is not { } tryEnd) continue;
            if (!Enumerable.Range(index + 1, tryEnd - index - 1).Any(line => Regex.IsMatch(masked[line], @"\bassert\w*\s*\(|\bfail\s*\("))) continue;

            // A catch that takes a failed assertion - AssertionError, AssertionFailedError, Error, Throwable - and lets it go.
            var handler = Regex.Match(masked[tryEnd], @"\bcatch\s*\(\s*(?:final\s+)?(?<caught>[\w.|\s]+?)\s+\w+\s*\)\s*\{");
            if (!handler.Success || !Regex.IsMatch(handler.Groups["caught"].Value, @"\b(?:AssertionError|AssertionFailedError|Error|Throwable)\b")) continue;
            if (Brackets.FirstBlockEnd(masked, tryEnd) is not { } handlerEnd) continue;
            if (Enumerable.Range(tryEnd, handlerEnd - tryEnd + 1).Any(line => Regex.IsMatch(masked[line], @"\bthrow\b|\bfail\s*\("))) continue;

            yield return new LogicFinding(id, tryEnd + 1,
                $"A failed assertion throws {handler.Groups["caught"].Value.Trim()} - and this catch takes it, and does not throw it again - so the test " +
                "passes whether the assertion holds or not: it can never fail",
                null);
        }
    }

    // ---- Perl's Test::More, and Erlang's EUnit ----

    [GeneratedRegex(@"^(?<indent>\s*)ok(?:\s*\((?<arguments>.+)\)|\s+(?<arguments>[^(].*?))\s*;\s*$")]
    private static partial Regex PerlOk();

    private static IEnumerable<LogicFinding> PerlOkWithExpected(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!Regex.IsMatch(string.Join("\n", masked), @"\buse\s+Test::(?:More|Simple)\b")) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            if (PerlOk().Match(masked[index]) is not { Success: true } ok) continue;

            var argumentsGroup = ok.Groups["arguments"];
            var parts = Arguments("(" + argumentsGroup.Value + ")", 0, argumentsGroup.Value.Length + 1);
            if (parts.Count != 2) continue;

            var line = source.Lines[index];
            var offset = argumentsGroup.Index - 1;
            var value = line[(parts[0].Start + offset)..(parts[0].End + offset)].Trim();
            var expected = line[(parts[1].Start + offset)..(parts[1].End + offset)].Trim();
            // The name a test is given is text; a number, or a scalar holding one, is the value meant to be compared.
            if (!Regex.IsMatch(expected, @"^(?:-?\d+(?:\.\d+)?|\$[A-Za-z_]\w*)$") || Regex.IsMatch(value, @"==|!=|\beq\b|\bne\b|[<>]|=~")) continue;

            yield return new LogicFinding(id, index + 1,
                $"ok's second argument is the test's name, so ok({value}, {expected}) passes whenever {value} is true - {expected} is never compared: " +
                $"ok(add(5, 2), 7) passed with add giving 3, Test::More only remarking on a test named 7",
                LocalFix.ReplaceLine(id, $"Compare the two: is({value}, {expected});",
                    $"is fails unless {value} equals {expected}, and shows both when it does.",
                    source.Path, index + 1, $"{ok.Groups["indent"].Value}is({value}, {expected});"));
        }
    }

    private static IEnumerable<LogicFinding> ErlangTestNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        // The include's file name is in quotes, which masking blanks, so it is read from the lines as written.
        if (!source.Lines.Any(line => Regex.IsMatch(line, @"^\s*-include(?:_lib)?\s*\(\s*""[^""]*eunit\.hrl""\s*\)"))) yield break;
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            var head = Regex.Match(masked[index], @"^test_(?<name>[a-z]\w*)\s*\(\s*\)\s*->");
            if (!head.Success) continue;

            var name = head.Groups["name"].Value;
            var function = "test_" + name;
            if (Regex.Matches(code, $@"\b{Regex.Escape(function)}\s*\(").Count > 1) continue;
            var exported = Regex.IsMatch(code, $@"-export\s*\(\s*\[[^\]]*\b{Regex.Escape(function)}\s*/\s*0");

            var line = source.Lines[index];
            yield return new LogicFinding(id, index + 1,
                $"EUnit runs the functions whose names end in _test - {name}_test - and {function} starts with test_ instead, so it is never run, " +
                "and what it asserts is never checked: erlc says only that it is unused",
                exported ? null : LocalFix.ReplaceLine(id, $"Name it as EUnit does: {name}_test()",
                    $"EUnit finds every function of no arguments whose name ends in _test, exports it, and runs it.",
                    source.Path, index + 1, $"{name}_test" + line[(head.Groups["name"].Index + head.Groups["name"].Length)..]));
        }
    }

    private static int IndentOf(string line) => line.Length - line.TrimStart().Length;

    /// <summary>The lines of a block that starts after <paramref name="header"/>: those indented further than it, up to the first that is not.</summary>
    private static (int First, int End) IndentedBody(IReadOnlyList<string> masked, int header)
    {
        var indent = IndentOf(masked[header]);
        var end = header + 1;
        for (var line = header + 1; line < masked.Count; line++)
        {
            if (masked[line].Trim().Length == 0) continue;
            if (IndentOf(masked[line]) <= indent) break;
            end = line + 1;
        }
        return (header + 1, end);
    }

    // ---- Python's unittest ----

    /// <summary>A Python class: its name, and what it derives from.</summary>
    private sealed record PythonClass(int Line, string Name, string Bases, (int First, int End) Body);

    [GeneratedRegex(@"^(?<indent>\s*)class\s+(?<name>[A-Za-z_]\w*)\s*(?:\((?<bases>[^)]*)\))?\s*:")]
    private static partial Regex PythonClassHeader();

    [GeneratedRegex(@"^(?<indent>\s*)(?:async\s+)?def\s+(?<name>[A-Za-z_]\w*)\s*\(\s*self\b")]
    private static partial Regex PythonMethod();

    [GeneratedRegex(@"\bself\s*\.\s*(?:assert\w*|fail)\s*\(|^\s*assert\b|\bpytest\s*\.\s*raises\b")]
    private static partial Regex PythonAssertion();

    /// <summary>The methods unittest calls itself, other than tests.</summary>
    private static readonly HashSet<string> UnittestOwn = new(StringComparer.Ordinal)
    {
        "setUp", "tearDown", "setUpClass", "tearDownClass", "asyncSetUp", "asyncTearDown", "run", "debug", "id", "shortDescription",
        "addCleanup", "doCleanups", "subTest", "countTestCases", "defaultTestResult",
    };

    private static List<PythonClass> PythonClasses(IReadOnlyList<string> masked) =>
        masked.Select((line, index) => (Index: index, Match: PythonClassHeader().Match(line)))
            .Where(each => each.Match.Success)
            .Select(each => new PythonClass(each.Index, each.Match.Groups["name"].Value, each.Match.Groups["bases"].Value, IndentedBody(masked, each.Index)))
            .ToList();

    /// <summary>Whether a class is a TestCase - derives from unittest's TestCase, or from a class of the file that does.</summary>
    private static bool IsTestCase(PythonClass type, IReadOnlyList<PythonClass> classes, int depth = 0) =>
        Regex.IsMatch(type.Bases, @"\b(?:IsolatedAsync)?TestCase\b") ||
        (depth < 10 && classes.Any(other => other.Name != type.Name && Regex.IsMatch(type.Bases, $@"(?<![\w.]){Regex.Escape(other.Name)}\b") && IsTestCase(other, classes, depth + 1)));

    /// <summary>The methods of a class: each one's header line, name and body.</summary>
    private static IEnumerable<(int Line, string Name, (int First, int End) Body)> MethodsOf(IReadOnlyList<string> masked, PythonClass type)
    {
        var methodIndent = -1;
        for (var line = type.Body.First; line < type.Body.End; line++)
        {
            if (PythonMethod().Match(masked[line]) is not { Success: true } method) continue;
            if (methodIndent < 0) methodIndent = method.Groups["indent"].Length;
            if (method.Groups["indent"].Length != methodIndent) continue;
            yield return (line, method.Groups["name"].Value, IndentedBody(masked, line));
        }
    }

    private static bool Decorated(IReadOnlyList<string> masked, int line)
    {
        var above = line - 1;
        while (above >= 0 && masked[above].Trim().Length == 0) above--;
        return above >= 0 && masked[above].TrimStart().StartsWith('@');
    }

    private static IEnumerable<LogicFinding> PythonTestNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var classes = PythonClasses(masked);
        var code = string.Join("\n", masked);
        foreach (var type in classes.Where(type => IsTestCase(type, classes)))
        {
            foreach (var (line, name, body) in MethodsOf(masked, type))
            {
                if (name.StartsWith("test", StringComparison.Ordinal) || name.StartsWith('_') || UnittestOwn.Contains(name) || Decorated(masked, line)) continue;
                if (!Enumerable.Range(body.First, body.End - body.First).Any(inside => PythonAssertion().IsMatch(masked[inside]))) continue;
                // A helper the tests call is not a test.
                if (Regex.Matches(code, $@"(?<![\w]){Regex.Escape(name)}\b").Count > 1) continue;

                var written = source.Lines[line];
                var at = written.IndexOf("def " + name, StringComparison.Ordinal) + 4;
                yield return new LogicFinding(id, line + 1,
                    $"{name} checks with assertions, but unittest runs only the methods whose names start with test - so it never runs, and whatever it " +
                    $"checks is never checked: unittest ran 3 tests of 5",
                    LocalFix.ReplaceLine(id, $"Name it as a test: test_{name}",
                        "unittest finds every method of a TestCase whose name starts with test, and runs it.",
                        source.Path, line + 1, written[..at] + "test_" + written[at..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> PythonClassNotTestCase(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var classes = PythonClasses(masked);
        var code = string.Join("\n", masked);
        var importsModule = Regex.IsMatch(code, @"(?m)^\s*import\s+unittest\b");
        var importsName = Regex.IsMatch(code, @"(?m)^\s*from\s+unittest\s+import\s+.*\bTestCase\b");
        if (!importsModule && !importsName) yield break;

        foreach (var type in classes)
        {
            if (type.Bases.Trim().Length > 0 && type.Bases.Trim() != "object") continue;
            var tests = MethodsOf(masked, type).Where(method => method.Name.StartsWith("test", StringComparison.Ordinal) &&
                Enumerable.Range(method.Body.First, method.Body.End - method.Body.First).Any(inside => Regex.IsMatch(masked[inside], @"\bself\s*\.\s*assert\w*\s*\("))).ToList();
            if (tests.Count == 0) continue;

            var written = source.Lines[type.Line];
            var nameEnd = written.IndexOf(type.Name, StringComparison.Ordinal) + type.Name.Length;
            var colon = written.IndexOf(':', nameEnd);
            var testCase = importsModule ? "unittest.TestCase" : "TestCase";
            yield return new LogicFinding(id, type.Line + 1,
                $"{type.Name} has tests that call self.assert..., but it is not a TestCase, so unittest does not look in it - its {tests.Count} " +
                $"test{(tests.Count == 1 ? "" : "s")} never run{(tests.Count == 1 ? "s" : "")}, and self.assertEqual is not even a method of it",
                LocalFix.ReplaceLine(id, $"Make it a TestCase: class {type.Name}({testCase}):",
                    $"Deriving from {testCase} gives the class its assert methods, and makes unittest run its tests.",
                    source.Path, type.Line + 1, written[..nameEnd] + $"({testCase})" + written[colon..]));
        }
    }

    /// <summary>A set-up or tear-down misspelt, and the name unittest calls.</summary>
    private static readonly IReadOnlyDictionary<string, string> MisspeltSetUp = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["setup"] = "setUp", ["set_up"] = "setUp", ["SetUp"] = "setUp", ["setUP"] = "setUp", ["Setup"] = "setUp",
        ["teardown"] = "tearDown", ["tear_down"] = "tearDown", ["TearDown"] = "tearDown", ["tearDOWN"] = "tearDown", ["Teardown"] = "tearDown",
        ["setupClass"] = "setUpClass", ["setUpclass"] = "setUpClass", ["teardownClass"] = "tearDownClass", ["tearDownclass"] = "tearDownClass",
    };

    private static IEnumerable<LogicFinding> PythonSetUpMisspelt(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var classes = PythonClasses(masked);
        foreach (var type in classes.Where(type => IsTestCase(type, classes)))
        {
            var methods = MethodsOf(masked, type).ToList();
            foreach (var (line, name, _) in methods)
            {
                if (!MisspeltSetUp.TryGetValue(name, out var meant) || methods.Any(other => other.Name == meant)) continue;

                var written = source.Lines[line];
                var at = written.IndexOf("def " + name, StringComparison.Ordinal) + 4;
                yield return new LogicFinding(id, line + 1,
                    $"unittest calls {meant} before{(meant.StartsWith("tear", StringComparison.Ordinal) ? " - after -" : "")} each test, with that exact spelling, " +
                    $"so {name} is never called: what it sets up is never there, and a test that uses it stops with AttributeError",
                    LocalFix.ReplaceLine(id, $"Spell it as unittest does: {meant}",
                        $"unittest calls {meant} itself, so what it does is done for every test.",
                        source.Path, line + 1, written[..at] + meant + written[(at + name.Length)..]));
            }
        }
    }

    /// <summary>The arguments inside a call's brackets, split at its own commas - not those inside brackets or quotes.</summary>
    private static List<(int Start, int End)> Arguments(string masked, int open, int close)
    {
        var parts = new List<(int, int)>();
        var depth = 0;
        var start = open + 1;
        for (var at = open + 1; at < close; at++)
        {
            if (masked[at] is '(' or '[' or '{') depth++;
            else if (masked[at] is ')' or ']' or '}') depth--;
            else if (masked[at] == ',' && depth == 0) { parts.Add((start, at)); start = at + 1; }
        }
        parts.Add((start, close));
        return parts;
    }

    private static IEnumerable<LogicFinding> PythonAssertTrueWithExpected(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match call in Regex.Matches(masked[index], @"\bself\s*\.\s*(?<method>assertTrue)\s*\("))
            {
                if (Brackets.ClosingParenthesis(masked[index], call.Index + call.Length - 1) is not { } close) continue;
                var arguments = Arguments(masked[index], call.Index + call.Length - 1, close);
                if (arguments.Count != 2) continue;

                var line = source.Lines[index];
                var value = line[arguments[0].Start..arguments[0].End].Trim();
                var expected = line[arguments[1].Start..arguments[1].End].Trim();
                // A message - text in quotes, an f-string, msg= - is what the second argument is for.
                if (Regex.IsMatch(expected, @"^(?:[rRbBfFuU]{0,2}[""']|msg\s*=)") || value.Length == 0 || expected.Length == 0) continue;

                var method = call.Groups["method"];
                yield return new LogicFinding(id, index + 1,
                    $"assertTrue's second argument is only the message shown when it fails, so assertTrue({value}, {expected}) passes whenever {value} is " +
                    $"anything but 0, empty or None - {expected} is never compared: assertTrue(add(5, 2), 7) passed with add giving 3",
                    LocalFix.ReplaceLine(id, $"Compare the two: self.assertEqual({value}, {expected})",
                        $"assertEqual fails unless {value} equals {expected}.",
                        source.Path, index + 1, line[..method.Index] + "assertEqual" + line[(method.Index + method.Length)..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> PythonAssertionSwallowed(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (!Regex.IsMatch(masked[index], @"^\s*try\s*:\s*$")) continue;
            var tryBody = IndentedBody(masked, index);
            if (!Enumerable.Range(tryBody.First, tryBody.End - tryBody.First).Any(inside => PythonAssertion().IsMatch(masked[inside]))) continue;

            // The handlers at the try's indentation that catch a failed assertion and let it go: AssertionError, Exception, or all.
            for (var line = tryBody.End; line < masked.Count; line++)
            {
                if (masked[line].Trim().Length == 0) continue;
                if (IndentOf(masked[line]) != IndentOf(masked[index])) break;
                var handler = Regex.Match(masked[line], @"^\s*except\b\s*(?<caught>\([^)]*\)|[\w.]+)?\s*(?:as\s+\w+)?\s*:");
                if (!handler.Success) break;

                var caught = handler.Groups["caught"].Value;
                if (caught.Length > 0 && !Regex.IsMatch(caught, @"\b(?:AssertionError|Exception|BaseException)\b")) continue;
                var handlerBody = IndentedBody(masked, line);
                if (Enumerable.Range(handlerBody.First, handlerBody.End - handlerBody.First).Any(inside => Regex.IsMatch(masked[inside], @"\braise\b|\bself\s*\.\s*fail\s*\(|\bpytest\s*\.\s*fail\b"))) continue;

                yield return new LogicFinding(id, line + 1,
                    $"A failed assertion raises AssertionError, and this except catches it{(caught.Length == 0 ? " - it catches everything -" : $" - {caught} -")} and " +
                    "does not raise it again, so the test passes whether the assertion holds or not: it can never fail",
                    null);
                break;
            }
        }
    }

    private static IEnumerable<LogicFinding> PythonTestChecksNothing(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var classes = PythonClasses(masked);
        foreach (var type in classes.Where(type => IsTestCase(type, classes)))
        {
            foreach (var (line, name, body) in MethodsOf(masked, type))
            {
                if (!name.StartsWith("test", StringComparison.Ordinal) || Decorated(masked, line)) continue;
                var lines = Enumerable.Range(body.First, body.End - body.First).Select(inside => masked[inside]).ToList();
                // Anything that can fail it: an assertion, raise, fail, a helper of its own class or one named for checking.
                if (lines.Any(inside => PythonAssertion().IsMatch(inside) || Regex.IsMatch(inside, @"\braise\b|\bself\s*\.\s*\w+\s*\(|\b(?:assert|check|verify|expect|ensure)\w*\s*\(", RegexOptions.IgnoreCase))) continue;
                if (lines.All(inside => inside.Trim().Length == 0 || inside.Trim() == "pass" || inside.Trim() == "...")) continue;

                yield return new LogicFinding(id, line + 1,
                    $"{name} runs code but asserts nothing about what it gives back, so it passes whatever that code does - unless it crashes - and " +
                    "cannot catch a wrong answer: it passed with add giving 3 for 5 + 2",
                    null);
            }
        }
    }

    // ---- Ruby's minitest ----

    [GeneratedRegex(@"^(?<indent>\s*)class\s+(?<name>[A-Z]\w*)\s*<\s*Mini[Tt]est::Test\b")]
    private static partial Regex MinitestClass();

    private static IEnumerable<LogicFinding> RubyTestNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        var ownNames = new HashSet<string>(StringComparer.Ordinal) { "setup", "teardown", "before_setup", "after_setup", "before_teardown", "after_teardown", "initialize" };
        for (var index = 0; index < masked.Count; index++)
        {
            if (MinitestClass().Match(masked[index]) is not { Success: true } type) continue;
            var classIndent = type.Groups["indent"].Length;
            var privateFrom = int.MaxValue;
            for (var line = index + 1; line < masked.Count; line++)
            {
                if (masked[line].Trim().Length == 0) continue;
                if (IndentOf(masked[line]) <= classIndent) break;
                if (Regex.IsMatch(masked[line], @"^\s*(?:private|protected)\s*$")) privateFrom = line;

                var method = Regex.Match(masked[line], @"^(?<indent>\s*)def\s+(?<name>[a-z_]\w*[?!]?)\s*$");
                if (!method.Success || line > privateFrom) continue;
                var name = method.Groups["name"].Value;
                if (name.StartsWith("test_", StringComparison.Ordinal) || ownNames.Contains(name)) continue;

                // Its body: up to the end at its own indentation.
                var end = Enumerable.Range(line + 1, masked.Count - line - 1).FirstOrDefault(after =>
                    Regex.IsMatch(masked[after], @"^\s*end\b") && IndentOf(masked[after]) == method.Groups["indent"].Length, -1);
                if (end < 0) continue;
                if (!Enumerable.Range(line + 1, end - line - 1).Any(inside => Regex.IsMatch(masked[inside], @"^\s*(?:assert|refute)\w*\b"))) continue;
                if (Regex.Matches(code, $@"(?<![\w.:]){Regex.Escape(name)}(?![\w?!])").Count > 1) continue;

                var written = source.Lines[line];
                var at = written.IndexOf("def " + name, StringComparison.Ordinal) + 4;
                yield return new LogicFinding(id, line + 1,
                    $"{name} checks with assertions, but minitest runs only the methods whose names start with test_ - so it never runs, and whatever it " +
                    "checks is never checked: 1 runs, where there were 2 tests",
                    LocalFix.ReplaceLine(id, $"Name it as a test: test_{name}",
                        "minitest finds every method of a Minitest::Test whose name starts with test_, and runs it.",
                        source.Path, line + 1, written[..at] + "test_" + written[at..]));
            }
        }
    }

    private static IEnumerable<LogicFinding> RubyAssertWithExpected(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!Regex.IsMatch(string.Join("\n", masked), @"\bMini[Tt]est::Test\b")) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            var assertion = Regex.Match(masked[index], @"^(?<indent>\s*)assert(?:\s*\((?<arguments>.+)\)|\s+(?<arguments>[^(].*?))\s*$");
            if (!assertion.Success) continue;

            var argumentsGroup = assertion.Groups["arguments"];
            var parts = Arguments("(" + argumentsGroup.Value + ")", 0, argumentsGroup.Value.Length + 1);
            if (parts.Count != 2) continue;

            var line = source.Lines[index];
            var offset = argumentsGroup.Index - 1;
            var value = line[(parts[0].Start + offset)..(parts[0].End + offset)].Trim();
            var expected = line[(parts[1].Start + offset)..(parts[1].End + offset)].Trim();
            // A message - text in quotes, or a block that makes one - is what the second argument is for.
            if (Regex.IsMatch(expected, @"^(?:[""']|%[qQ]?[\[({]|->|proc\b|lambda\b)") || value.Length == 0 || Regex.IsMatch(value, @"[=!<>]=|[<>]|\.(?:nil|empty|include|any|all)\?")) continue;

            yield return new LogicFinding(id, index + 1,
                $"assert's second argument is only the message shown when it fails, so assert {value}, {expected} passes whenever {value} is anything " +
                $"but nil or false - {expected} is never compared: assert add(5, 2), 7 passed with add giving 3",
                LocalFix.ReplaceLine(id, $"Compare the two: assert_equal {expected}, {value}",
                    $"assert_equal fails unless {value} equals {expected} - the value expected first.",
                    source.Path, index + 1, $"{assertion.Groups["indent"].Value}assert_equal {expected}, {value}"));
        }
    }

    // ---- Elixir's ExUnit, and go test ----

    private static IEnumerable<LogicFinding> ElixirTestNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!Regex.IsMatch(string.Join("\n", masked), @"\buse\s+ExUnit\.Case\b")) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            var defined = Regex.Match(masked[index], @"^(?<indent>\s*)def\s+test_(?<name>[a-z_]\w*)\s+do\s*$");
            if (!defined.Success) continue;

            var name = defined.Groups["name"].Value;
            var description = name.Replace('_', ' ');
            yield return new LogicFinding(id, index + 1,
                $"ExUnit runs the blocks written with test \"...\" do - a function named test_{name} is an ordinary function, and nothing calls it, so " +
                "what it asserts is never checked: ExUnit ran 1 test of 2",
                LocalFix.ReplaceLine(id, $"Write it as a test: test \"{description}\" do",
                    "test \"...\" do makes a test ExUnit runs, named by its text.",
                    source.Path, index + 1, $"{defined.Groups["indent"].Value}test \"{description}\" do"));
        }
    }

    [GeneratedRegex(@"^func\s+(?<name>[A-Za-z_]\w*)\s*\(\s*(?<t>[a-z_]\w*)\s+\*testing\.T\s*\)\s*\{")]
    private static partial Regex GoTestShape();

    private static IEnumerable<LogicFinding> GoTestNeverRun(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        if (!source.Path.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase)) yield break;
        var code = string.Join("\n", masked);

        for (var index = 0; index < masked.Count; index++)
        {
            if (GoTestShape().Match(masked[index]) is not { Success: true } function) continue;
            var name = function.Groups["name"].Value;
            if (name.StartsWith("Test", StringComparison.Ordinal)) continue;
            // A helper the tests call, with the *testing.T handed on, is not a test.
            if (Regex.Matches(code, $@"\b{Regex.Escape(name)}\b").Count > 1) continue;

            var bare = name.StartsWith("test", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
            var testName = "Test" + (bare.Length == 0 ? "It" : char.ToUpperInvariant(bare[0]) + bare[1..]);
            var line = source.Lines[index];
            var nameGroup = function.Groups["name"];
            yield return new LogicFinding(id, index + 1,
                $"go test runs only the functions whose names start with Test and a capital letter - so {name} never runs, and nothing it checks is " +
                "ever checked: go test ran the other test alone, and go vet said nothing",
                LocalFix.ReplaceLine(id, $"Name it as a test: {testName}",
                    $"go test finds every func Test...(t *testing.T) in a _test.go file, and runs it.",
                    source.Path, index + 1, line[..nameGroup.Index] + testName + line[(nameGroup.Index + nameGroup.Length)..]));
        }
    }
}
