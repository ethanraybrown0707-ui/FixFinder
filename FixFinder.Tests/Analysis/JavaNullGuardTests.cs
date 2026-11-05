using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// What a Java line that stopped on null was doing, read from Java's own message; and the change that makes the line run
/// only when the variable is not null - offered only for a line that is a statement of its own.
/// </summary>
public class JavaNullGuardTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private SourceFile Program(string code)
    {
        var path = Path.Combine(_temp.Path, "Main.java");
        File.WriteAllText(path, code);
        return SourceFile.Read(path)!;
    }

    private static RootCause Cause(string name, int crashLine, bool changeTheCause = false) => new()
    {
        Name = name,
        Value = "null",
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
    [InlineData("Cannot invoke \"java.util.Map.get(Object)\" because \"member\" is null", "calls get on {0}")]
    [InlineData("Cannot invoke \"String.length()\" because the return value of \"Lookup.find(java.util.List, String)\" is null", "calls length on {0}")]
    [InlineData("Cannot invoke \"java.lang.Integer.intValue()\" because \"age\" is null", "uses {0} as a plain int")]
    [InlineData("Cannot read field \"total\" because \"report\" is null", "reads the field total of {0}")]
    [InlineData("Cannot assign field \"total\" because \"report\" is null", "sets the field total of {0}")]
    [InlineData("Cannot load from int array because \"marks\" is null", "reads an item of {0}")]
    [InlineData("Cannot store to int array because \"marks\" is null", "stores an item in {0}")]
    [InlineData("Cannot read the array length because \"marks\" is null", "asks for the length of {0}")]
    public void WhatTheLineDidIsReadFromJavasOwnMessage(string message, string doing)
    {
        Assert.Equal(doing, JavaNullOrigin.DoingIn(message));
    }

    [Theory]
    [InlineData("class Main {\n    void show(String name) {\n        System.out.println(name.length());\n    }\n}\n", 3,
        "        if (name != null) {|            System.out.println(name.length());|        }")]
    [InlineData("class Main {\n\tvoid show(String name) {\n\t\tSystem.out.println(name.length());\n\t}\n}\n", 3,
        "\t\tif (name != null) {|\t\t\tSystem.out.println(name.length());|\t\t}")]
    public void AStatementOfItsOwnRunsOnlyWhenTheVariableIsNotNull(string code, int crashLine, string guarded)
    {
        var fix = JavaNullOrigin.GuardFor(Cause("name", crashLine), Program(code), "calls length on name");

        Assert.NotNull(fix);
        Assert.Equal(crashLine, fix!.StartLine);
        Assert.Equal(1, fix.RemoveCount);
        Assert.Equal(guarded.Split('|'), fix.NewLines);
        Assert.True(fix.OnlyStopsTheFailure);
    }

    [Theory]
    [InlineData("class Main {\n    int size(String name) {\n        int size = name.length();\n        return size;\n    }\n}\n", 3)]
    [InlineData("class Main {\n    int size(String name) {\n        return name.length();\n    }\n}\n", 3)]
    [InlineData("class Main {\n    void show(String name) {\n        if (name.isEmpty()) {\n        }\n    }\n}\n", 3)]
    [InlineData("class Main {\n    void show(String name) {\n        System.out.println(name.length()\n            + 1);\n    }\n}\n", 3)]
    [InlineData("class Main {\n    void show(String name) {\n        System.out.println(1 +\n            name.length());\n    }\n}\n", 4)]
    public void ALineThatIsNotAStatementOfItsOwnIsLeftAlone(string code, int crashLine)
    {
        Assert.Null(JavaNullOrigin.GuardFor(Cause("name", crashLine), Program(code), "calls length on name"));
    }

    [Fact]
    public void AValueAMethodGaveStraightToTheLineHasNoNameToCheck()
    {
        var code = "class Main {\n    void show() {\n        System.out.println(find().length());\n    }\n}\n";

        Assert.Null(JavaNullOrigin.GuardFor(Cause("find(...)", 3), Program(code), "calls length on find(...)"));
    }
}
