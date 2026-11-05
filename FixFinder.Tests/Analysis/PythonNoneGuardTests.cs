using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// The change that makes the line a Python program stopped on run only when the value is not None - offered only for a line
/// that is a statement of its own, since skipping an assignment or a return changes more than the line.
/// </summary>
public class PythonNoneGuardTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private SourceFile Program(string code)
    {
        var path = Path.Combine(_temp.Path, "program.py");
        File.WriteAllText(path, code);
        return SourceFile.Read(path)!;
    }

    private static RootCause Cause(string name, int crashLine, bool changeTheCause = false) => new()
    {
        Name = name,
        Value = "None",
        CrashLine = crashLine,
        IntroducedLine = 1,
        IntroducedCode = "",
        Steps = [],
        Evidence = [],
        LinesInvolved = [1],
        WhatToDo = "",
        ChangeTheCause = changeTheCause,
    };

    [Theory]
    [InlineData("member = None\nprint(member[\"age\"])\n", 2, "if member is not None:|    print(member[\"age\"])")]
    [InlineData("def show(member):\n    print(member[\"age\"])\n", 2, "    if member is not None:|        print(member[\"age\"])")]
    [InlineData("def show(member):\n\tprint(member[\"age\"])\n", 2, "\tif member is not None:|\t\tprint(member[\"age\"])")]
    [InlineData("total = 0\nmember = None\ntotal += member[\"age\"]\n", 3, "if member is not None:|    total += member[\"age\"]")]
    public void AStatementOfItsOwnRunsOnlyWhenTheValueIsThere(string code, int crashLine, string guarded)
    {
        var fix = PythonNoneOrigin.GuardFor(Cause("member", crashLine), Program(code), "picks an item out of member with [ ]");

        Assert.NotNull(fix);
        Assert.Equal(crashLine, fix!.StartLine);
        Assert.Equal(1, fix.RemoveCount);
        Assert.Equal(guarded.Split('|'), fix.NewLines);
    }

    [Theory]
    [InlineData("member = None\nage = member[\"age\"]\n", 2)]
    [InlineData("def age(member):\n    return member[\"age\"]\n", 2)]
    [InlineData("member = None\nif member[\"age\"] > 18:\n    print(1)\n", 2)]
    [InlineData("member = None\nprint(member[\"age\"],\n      1)\n", 2)]
    [InlineData("member = None\nprint(1,\n      member[\"age\"])\n", 3)]
    public void ALineThatIsNotAStatementOfItsOwnIsLeftAlone(string code, int crashLine)
    {
        Assert.Null(PythonNoneOrigin.GuardFor(Cause("member", crashLine), Program(code), "picks an item out of member with [ ]"));
    }

    [Fact]
    public void AMistakeOnTheLineThatGaveTheNoneIsNotHiddenBehindACheck()
    {
        // items = items.sort() is the line to change; skipping print(items[0]) would only hide it.
        var code = "items = [3, 1, 2]\nitems = items.sort()\nprint(items[0])\n";

        Assert.Null(PythonNoneOrigin.GuardFor(Cause("items", 3, changeTheCause: true), Program(code), "picks an item out of items with [ ]"));
    }

    [Fact]
    public void AValueACallGaveStraightToTheLineHasNoNameToCheck()
    {
        var code = "def find():\n    return None\n\n\nprint(find()[\"age\"])\n";

        Assert.Null(PythonNoneOrigin.GuardFor(Cause("find()", 5), Program(code), "picks an item out of find() with [ ]"));
    }
}
