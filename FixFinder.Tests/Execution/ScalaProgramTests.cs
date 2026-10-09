using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>Which files a Scala program is built from, the folder it starts in, and which main it runs.</summary>
public class ScalaProgramTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void AFileOfAnSbtProjectIsBuiltWithTheProjectsSourcesAndStartsWhereItsBuildSbtIs()
    {
        var buildSbt = Write(Path.Combine("marks", "build.sbt"), "scalaVersion := \"3.8.4\"\n");
        var main = Write(Path.Combine("marks", "src", "main", "scala", "app", "Main.scala"), "package app\n\n@main def run(): Unit = println(Student(\"Ada\").name)\n");
        var student = Write(Path.Combine("marks", "src", "main", "scala", "app", "Student.scala"), "package app\n\ncase class Student(name: String)\n");
        Write(Path.Combine("marks", "src", "test", "scala", "MainTest.scala"), "class MainTest\n");

        var layout = ScalaProgram.Of(main);

        Assert.Equal(new[] { main, student }, layout.Files);
        Assert.Equal(Path.Combine(_temp.Path, "marks"), layout.Folder);
        Assert.Equal(buildSbt, layout.BuildFile);
    }

    [Fact]
    public void AFileAnywhereElseIsBuiltWithTheFilesBesideItThatItsCodeUses()
    {
        var grades = Write(Path.Combine("week3", "Grades.scala"), "object Grades {\n  def main(args: Array[String]): Unit = println(Student(\"Ada\", 70))\n}\n");
        var student = Write(Path.Combine("week3", "Student.scala"), "case class Student(name: String, mark: Int) {\n  def passed: Boolean = Rules.passes(mark)\n}\n");
        var rules = Write(Path.Combine("week3", "Rules.scala"), "object Rules {\n  def passes(mark: Int): Boolean = mark >= 40\n}\n");
        Write(Path.Combine("week3", "Unrelated.scala"), "object Unrelated {\n  def main(args: Array[String]): Unit = println(2)\n}\n");

        Assert.Equal(new[] { grades, student, rules }, ScalaProgram.Of(grades).Files);
    }

    [Fact]
    public void AnotherExerciseWithAnObjectOfTheSameNameIsAnotherProgramAndIsLeftOut()
    {
        var first = Write(Path.Combine("exercises", "First.scala"), "object Main {\n  def main(args: Array[String]): Unit = println(Helper.twice(2))\n}\n");
        Write(Path.Combine("exercises", "Second.scala"), "object Main {\n  def main(args: Array[String]): Unit = println(3)\n}\n");
        var helper = Write(Path.Combine("exercises", "Helper.scala"), "object Helper {\n  def twice(n: Int): Int = n * 2\n}\n");

        Assert.Equal(new[] { first, helper }, ScalaProgram.Of(first).Files);
    }

    [Fact]
    public void AScriptIsAProgramOfItsOwn()
    {
        var script = Write(Path.Combine("scripts", "count.sc"), "val words = List(\"a\", \"b\")\nprintln(words.size)\n");

        Assert.Equal(new[] { script }, ScalaProgram.Of(script).Files);
    }

    [Theory]
    [InlineData("object Marks {\n  def main(args: Array[String]): Unit = println(1)\n}\n", "Marks")]
    [InlineData("package school.marks\n\nobject Marks {\n  def main(args: Array[String]): Unit = println(1)\n}\n", "school.marks.Marks")]
    [InlineData("@main def hello(): Unit = println(1)\n", "hello")]
    [InlineData("object Greeter extends App {\n  println(1)\n}\n", "Greeter")]
    [InlineData("object Notes {\n  // def main(args: Array[String]): Unit = ()\n}\n", null)]
    [InlineData("case class Student(name: String)\n", null)]
    public void AFilesMainIsTheClassScalaCliRunsItAs(string code, string? expected) =>
        Assert.Equal(expected, ScalaProgram.MainsIn(code).SingleOrDefault());

    [Fact]
    public void ScalaCliIsToldWhichMainToRunOnlyWhenTheProgramHasSeveral()
    {
        var report = Write(Path.Combine("several", "Report.scala"), "object Report {\n  def main(args: Array[String]): Unit = println(Tools.title)\n}\n");
        Write(Path.Combine("several", "Tools.scala"), "object Tools {\n  def title = \"Marks\"\n  def main(args: Array[String]): Unit = println(title)\n}\n");
        var alone = Write(Path.Combine("one", "Alone.scala"), "object Alone {\n  def main(args: Array[String]): Unit = println(1)\n}\n");

        Assert.Equal("Report", ScalaProgram.Of(report).MainClass);
        Assert.Null(ScalaProgram.Of(alone).MainClass);
    }
}
