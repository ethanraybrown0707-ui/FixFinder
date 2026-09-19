using FixFinder.Core.Analysis.Flow;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Tests;

/// <summary>Python read by its own parser into the shared IR, and lowered into control-flow graphs.</summary>
public class PythonFrontendTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IrProgram?> Read(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "app.py");
        await File.WriteAllTextAsync(path, code);
        return await PythonFrontend.ReadAsync([path], python);
    }

    private static ControlFlowGraph Graph(IrProgram program, string name) =>
        CfgBuilder.Build(program.AllFunctions.Single(f => f.Name == name));

    [Fact]
    public async Task AFunctionsParametersAndTypeHintsAreRead()
    {
        if (await Read("def grade(score: int, bonus: float = 0.5) -> str:\n    return 'A'\n") is not { } program) return;

        var grade = program.Functions.Single(f => f.Name == "grade");
        Assert.Equal(["score: int", "bonus: float"], grade.Parameters.Select(p => $"{p.Name}: {p.Type}"));
        Assert.NotNull(grade.Parameters[1].Default);
        Assert.Equal("str", grade.ReturnType.ToString());
    }

    [Fact]
    public async Task AndOrAndNotBecomeOneSimpleTestPerBranch()
    {
        if (await Read("def f(a, b):\n    if a and not b:\n        return 1\n    return 2\n") is not { } program) return;

        Assert.Equal("""
            B0: if a then B3 else B2
            B1: return 1
            B2: return 2
            B3: if b then B2 else B1
            B4: end
            B5: end
            """.ReplaceLineEndings("\n"), IrText.Of(Graph(program, "f")));
    }

    [Fact]
    public async Task AForElseRunsItsElseOnlyWhenTheLoopWasNotBroken()
    {
        const string code = "def search(items, target):\n    for i in range(len(items)):\n        if items[i] == target:\n            break\n" +
                            "    else:\n        return -1\n    return i\n";
        if (await Read(code) is not { } program) return;

        Assert.Equal("""
            B0: $items0 = range(len(items)); goto B1
            B1*: if more($items0) then B2 else B3
            B2: i = next($items0); if items[i] == target then B5 else B6
            B3: return -1
            B4: return i
            B5: goto B4
            B6: goto B1
            B7: end
            B8: end
            """.ReplaceLineEndings("\n"), IrText.Of(Graph(program, "search")));
    }

    [Fact]
    public async Task FinallyRunsBeforeAReturnInsideAHandlerAndBeforeAnEscapingException()
    {
        const string code = "def read(path):\n    try:\n        value = int(path)\n    except ValueError as error:\n        return None\n" +
                            "    finally:\n        print('done')\n    return value\n";
        if (await Read(code) is not { } program) return;

        var text = IrText.Of(Graph(program, "read"));
        Assert.Contains("error = <caught ValueError>; print(\"done\"); return null", text);
        Assert.Contains("print(\"done\"); raise", text);
        Assert.Contains("print(\"done\"); goto", text);
    }

    [Fact]
    public async Task AWithBlockReleasesItsResourceWhicheverWayItIsLeft()
    {
        if (await Read("def count(path):\n    with open(path) as handle:\n        return len(handle.read())\n") is not { } program) return;

        var graph = Graph(program, "count");
        var releasing = graph.Blocks.Where(b => b.Instructions.OfType<ReleaseInstruction>().Any()).ToList();

        Assert.Equal(2, releasing.Count);
        Assert.Contains(releasing, b => b.Terminator is Leave);
        Assert.Contains(releasing, b => b.Terminator is Raise);
    }

    [Fact]
    public async Task CodeAfterAReturnIsInABlockNothingReaches()
    {
        if (await Read("def f():\n    return 1\n    print('never')\n") is not { } program) return;

        var graph = Graph(program, "f");
        var reachable = graph.Reachable();
        var never = graph.Blocks.Single(b => b.Instructions.Any(i => IrText.Of(i) == "print(\"never\")"));

        Assert.DoesNotContain(never.Id, reachable);
    }

    [Fact]
    public async Task AnAssertGoesOnOnlyWhenItsConditionHolds()
    {
        if (await Read("def f(x):\n    assert x > 0, 'positive'\n    return x\n") is not { } program) return;

        var text = IrText.Of(Graph(program, "f"));
        Assert.Contains("if x > 0 then B1 else B2", text);
        Assert.Contains("B2: raise <AssertionError>", text);
    }

    [Fact]
    public async Task TheElseOfATryIsNotProtectedByItsHandlers()
    {
        const string code = "def f(x):\n    try:\n        y = int(x)\n    except ValueError:\n        y = 0\n    else:\n        y = y + 1\n    return y\n";
        if (await Read(code) is not { } program) return;

        var graph = Graph(program, "f");
        var handler = graph.Blocks.Single(b => b.Instructions.Any(i => IrText.Of(i) == "y = 0")).Id;
        var otherwise = graph.Blocks.Single(b => b.Instructions.Any(i => IrText.Of(i) == "y = y + 1"));

        Assert.DoesNotContain(handler, otherwise.ExceptionTargets);
    }

    [Fact]
    public async Task AClassKeepsItsFieldsIncludingThoseItsMethodsSetOnSelf()
    {
        const string code = "class Account:\n    rate = 0.05\n\n    def __init__(self, owner):\n        self.owner = owner\n        if owner:\n            self.balance = 0\n";
        if (await Read(code) is not { } program) return;

        var account = program.Classes.Single();
        Assert.Equal(["rate", "owner", "balance"], account.Fields.Select(f => f.Name));
        Assert.True(account.Fields[0].IsStatic);
        Assert.True(account.Methods.Single().IsConstructor);
    }

    [Fact]
    public async Task AFileThatDoesNotParseIsReportedRatherThanGuessedAt()
    {
        if (await Read("def f(:\n    pass\n") is not { } program) return;

        Assert.Empty(program.Functions);
        Assert.Contains(program.Problems, p => p.Contains("line 1", StringComparison.Ordinal));
    }
}
