using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Two records compared with == or != - which asks whether they are one object, not whether they hold the same values -
/// found with the change to equals, in a class and in a Java 25 compact source file; and left alone where == is right: a
/// record against null, numbers, and objects that are not records.
/// </summary>
public class RecordEqualityTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == "logic-java-record-equality").ToList();
    }

    [Theory]
    [InlineData("App.java",
        "public class App {\n    record Point(int x, int y) {}\n\n    public static void main(String[] args) {\n        Point a = new Point(1, 2);\n        Point b = new Point(1, 2);\n        if (a == b) {\n            System.out.println(\"same point\");\n        }\n    }\n}\n",
        7, "        if (a.equals(b)) {")]
    [InlineData("Main.java",
        "record Point(int x, int y) {}\n\nvoid main() {\n    var a = new Point(1, 2);\n    var b = new Point(1, 2);\n    if (a != b) {\n        IO.println(\"different\");\n    }\n}\n",
        6, "    if (!a.equals(b)) {")]
    [InlineData("Main.java",
        "record Mark(String name, int score) {}\n\nboolean same(Mark first, Mark second) {\n    return first == second;\n}\n\nvoid main() {\n    IO.println(same(new Mark(\"Ada\", 70), new Mark(\"Ada\", 70)));\n}\n",
        4, "    return first.equals(second);")]
    public void RecordsComparedWithEqualsSignsAreComparedWithEquals(string fileName, string code, int line, string comparedWithEquals)
    {
        var found = Assert.Single(Scan(fileName, code));

        Assert.Equal(line, found.Line);
        Assert.Equal([comparedWithEquals], found.Fix?.NewLines);
    }

    [Theory]
    // A record compared with null is asking whether there is one at all.
    [InlineData("record Point(int x, int y) {}\n\nvoid main() {\n    Point a = new Point(1, 2);\n    Point b = null;\n    if (a == null || b == null) {\n        IO.println(\"missing\");\n    }\n}\n")]
    // A record's numbers compared with ==.
    [InlineData("record Point(int x, int y) {}\n\nvoid main() {\n    Point a = new Point(1, 2);\n    Point b = new Point(1, 2);\n    if (a.x() == b.x()) {\n        IO.println(\"same x\");\n    }\n}\n")]
    // Objects that are not records, in a file that has one.
    [InlineData("record Point(int x, int y) {}\n\nvoid main() {\n    Point a = new Point(1, 2);\n    Object first = a;\n    Object second = a;\n    if (first == second) {\n        IO.println(\"one object\");\n    }\n}\n")]
    // Records already compared with equals.
    [InlineData("record Point(int x, int y) {}\n\nvoid main() {\n    Point a = new Point(1, 2);\n    Point b = new Point(1, 2);\n    if (a.equals(b)) {\n        IO.println(\"same point\");\n    }\n}\n")]
    public void EqualsSignsThatAreRightAreLeftAlone(string code)
    {
        Assert.Empty(Scan("Main.java", code));
    }
}
