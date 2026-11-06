using FixFinder.Core.Analysis.Dynamic;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Tests;

/// <summary>
/// What held the missing value on the line a JavaScript program stopped on, read from Node's message and the column its trace
/// gives - each line and column here as Node 24 printed them - and where the change that skips the line is offered.
/// </summary>
public class JavaScriptMissingReadTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("let m; console.log(m.age);", 22, "Cannot read properties of undefined (reading 'age')", "reads age from {0}", "undefined", "m", null)]
    [InlineData("let m; console.log(m[\"age\"]);", 21, "Cannot read properties of undefined (reading 'age')", "reads age from {0}", "undefined", "m", null)]
    [InlineData("let m; m.age = 3;", 14, "Cannot set properties of undefined (setting 'age')", "sets age on {0}", "undefined", "m", null)]
    [InlineData("let m = null; console.log(m.age);", 29, "Cannot read properties of null (reading 'age')", "reads age from {0}", "null", "m", null)]
    [InlineData("console.log(find([\"ann\", \"bob\"], \"c\").length);", 38, "Cannot read properties of undefined (reading 'length')", "reads length from {0}", "undefined", null, "find")]
    [InlineData("const { age } = person;", 9, "Cannot destructure property 'age' of 'person' as it is undefined.", "takes age out of {0}", "undefined", "person", null)]
    [InlineData("for (const mark of marks) {", 20, "marks is not iterable", "goes through {0} one item at a time", null, "marks", null)]
    public void WhatHeldItIsReadFromTheMessageAndTheColumn(string line, int column, string message, string doing, string? value, string? name, string? calledFunction)
    {
        Assert.Equal((doing, value, name, calledFunction), JavaScriptMissingOrigin.Read(message, line, column));
    }

    [Theory]
    [InlineData("const ms = {a: {b: 1}}; console.log(ms.c.b);", 42, "Cannot read properties of undefined (reading 'b')")]
    [InlineData("f();", 1, "f is not a function")]
    [InlineData("let m; console.log(m.age);", 99, "Cannot read properties of undefined (reading 'age')")]
    public void WhatIsNotAPlainNameOrCallIsLeftAlone(string line, int column, string message)
    {
        Assert.Null(JavaScriptMissingOrigin.Read(message, line, column));
    }

    private SourceFile Program(string code)
    {
        var path = Path.Combine(_temp.Path, "main.js");
        File.WriteAllText(path, code);
        return SourceFile.Read(path)!;
    }

    private static RootCause Cause(string name, int crashLine) => new()
    {
        Name = name,
        Value = "undefined",
        CrashLine = crashLine,
        IntroducedLine = 1,
        IntroducedCode = "",
        Steps = [],
        Evidence = [],
        LinesInvolved = [1],
        WhatToDo = "",
    };

    [Theory]
    [InlineData("const member = find()\nconsole.log(member.age)\n", 2, "if (member != null) {|    console.log(member.age)|}")]
    [InlineData("function show(member) {\n    member.age = 31;\n}\n", 2, "    if (member != null) {|        member.age = 31;|    }")]
    public void AStatementOfItsOwnRunsOnlyWhenTheVariableHoldsAValue(string code, int crashLine, string guarded)
    {
        var fix = JavaScriptMissingOrigin.GuardFor(Cause("member", crashLine), Program(code), "reads age from member");

        Assert.NotNull(fix);
        Assert.Equal(guarded.Split('|'), fix!.NewLines);
        Assert.True(fix.OnlyStopsTheFailure);
    }

    [Theory]
    [InlineData("const member = find();\nconst age = member.age;\n", 2)]
    [InlineData("function age(member) {\n    return member.age;\n}\n", 2)]
    [InlineData("let total = 0;\ntotal = member.age;\n", 2)]
    [InlineData("const member = find();\nconsole.log(1 +\n    member.age);\n", 3)]
    public void ALineThatIsNotAStatementOfItsOwnIsLeftAlone(string code, int crashLine)
    {
        Assert.Null(JavaScriptMissingOrigin.GuardFor(Cause("member", crashLine), Program(code), "reads age from member"));
    }
}
