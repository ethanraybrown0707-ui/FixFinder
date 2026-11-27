using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Measures;

namespace FixFinder.Tests;

/// <summary>
/// Python's match statement is read as the if and elif it does the work of, so every analysis reads what is inside it; and
/// a method a class defines inside an if, a try or a with, and a class written inside a class, are read as the class's own.
/// </summary>
public class PythonMatchAndClassBodyTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IrProgram?> ReadAsync(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;
        var path = Path.Combine(_temp.Path, "shapes.py");
        await File.WriteAllTextAsync(path, code);
        return await PythonFrontend.ReadAsync([path], python);
    }

    /// <summary>
    /// Each case is an if on its pattern - == for a value, its alternatives joined by or, is for None, a condition not worked
    /// out for a list pattern, and the guard joined by and - and case _ is the else; the names a pattern captures are set
    /// before its if, so the guard and the body read names that were given values.
    /// </summary>
    [Fact]
    public async Task AMatchIsReadAsTheIfsItDoesTheWorkOf()
    {
        const string code = """
            def describe(command):
                match command:
                    case "start" | "begin":
                        return 1
                    case "stop":
                        return 2
                    case [verb, noun] if verb == "go":
                        return noun
                    case None:
                        return 0
                    case _:
                        return -1
            """;
        if (await ReadAsync(code) is not { } program) return;

        var describe = Assert.Single(program.AllFunctions, function => function.Name == "describe");
        var first = Assert.IsType<If>(Assert.Single(describe.Body));
        var either = Assert.IsType<Binary>(first.Condition);
        Assert.Equal(BinaryOperator.Or, either.Operator);
        Assert.All([either.Left, either.Right], side => Assert.Equal(BinaryOperator.Equal, Assert.IsType<Binary>(side).Operator));

        var second = Assert.IsType<If>(Assert.Single(first.Else));
        Assert.Equal(BinaryOperator.Equal, Assert.IsType<Binary>(second.Condition).Operator);

        // The list pattern captures verb and noun before its if, whose guard is joined to it with and.
        Assert.Equal(3, second.Else.Count);
        Assert.Equal(["verb", "noun"], second.Else.Take(2).Select(set => Assert.IsType<Name>(Assert.IsType<Assign>(set).Target).Identifier));
        var guarded = Assert.IsType<If>(second.Else[2]);
        Assert.Equal(BinaryOperator.And, Assert.IsType<Binary>(guarded.Condition).Operator);

        var none = Assert.IsType<If>(Assert.Single(guarded.Else));
        Assert.Equal(BinaryOperator.Is, Assert.IsType<Binary>(none.Condition).Operator);
        Assert.IsType<Return>(Assert.Single(none.Else));

        // 4 ifs, the or of "start" | "begin" and the and of the guard.
        var measured = Complexity.Of(describe);
        Assert.Equal((4, 2, 7), (measured.Ifs, measured.AndsAndOrs, measured.Value));
    }

    /// <summary>A subject worked out by a call is worked out once, into a name of FixFinder's own, which each case then reads.</summary>
    [Fact]
    public async Task AMatchSubjectThatIsACallIsWorkedOutOnce()
    {
        const string code = """
            def first_word(text):
                match text.split():
                    case [word, *rest]:
                        return word
                    case _:
                        return ""
            """;
        if (await ReadAsync(code) is not { } program) return;

        var body = Assert.Single(program.AllFunctions, function => function.Name == "first_word").Body;
        var worked = Assert.IsType<Assign>(body[0]);
        Assert.Equal("match subject", Assert.IsType<Name>(worked.Target).Identifier);
        Assert.IsType<Call>(worked.Value);
        Assert.Single(IrWalk.Statements(body).OfType<Assign>().Select(set => set.Value).OfType<Call>());
    }

    /// <summary>
    /// A method a class defines in each branch of an if, or in a try and its except, and a class written inside a class, are
    /// read; a field set in each branch of an if is not read as two fields.
    /// </summary>
    [Fact]
    public async Task MethodsInsideAnIfOrATryAndAClassInsideAClassAreRead()
    {
        const string code = """
            import sys

            class Shape:
                class Unit:
                    def scale(self, value):
                        return value * 2

                if sys.platform == "win32":
                    def area(self):
                        return 1
                    mode = "windows"
                else:
                    def area(self):
                        return 2
                    mode = "other"

                try:
                    import math
                    def perimeter(self):
                        return math.pi
                except ImportError:
                    def perimeter(self):
                        return 3

                size = 3
            """;
        if (await ReadAsync(code) is not { } program) return;

        var shape = Assert.Single(program.Classes, type => type.Name == "Shape");
        Assert.Equal(["area", "area", "perimeter", "perimeter"], shape.Methods.Select(method => method.Name));
        Assert.Equal(["size"], shape.Fields.Select(field => field.Name));

        var unit = Assert.Single(program.Classes, type => type.Name == "Unit");
        Assert.Equal("Unit", Assert.Single(unit.Methods).Owner);
    }
}
