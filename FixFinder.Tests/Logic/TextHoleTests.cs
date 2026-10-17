using FixFinder.Core;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A name in braces in C# text with no $, and #{...} in Ruby text in single quotes: found where the text was meant to hold a value,
/// and left alone where the braces are meant as they are written or are filled in some other way.
/// </summary>
public class TextHolePatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string patternId)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == patternId).ToList();
    }

    [Theory]
    [InlineData("int total = 150;\nConsole.WriteLine(\"Total: {total}\");\n", 2, "Console.WriteLine($\"Total: {total}\");")]
    [InlineData("string name = Console.ReadLine();\nstring greeting = \"Hello, {name}\";\nConsole.WriteLine(greeting);\n", 2, "string greeting = $\"Hello, {name}\";")]
    [InlineData("Show(70);\nstatic void Show(int mark)\n{\n    Console.WriteLine(\"Mark: {mark}\" + \"!\");\n}\n", 4, "    Console.WriteLine($\"Mark: {mark}\" + \"!\");")]
    [InlineData("double total = 1.5;\nConsole.WriteLine(\"Total: {total:F2}\");\n", 2, "Console.WriteLine($\"Total: {total:F2}\");")]
    [InlineData("var marks = new[] { 70, 80 };\nforeach (var mark in marks)\n    Console.WriteLine(\"Mark: {mark}\");\n", 3, "    Console.WriteLine($\"Mark: {mark}\");")]
    public void CSharpTextWithANameInBracesAndNoDollarIsFound(string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan("main.cs", code, "logic-csharp-text-without-dollar"));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // Already interpolated, plainly and verbatim.
    [InlineData("int total = 150;\nConsole.WriteLine($\"Total: {total}\");\nConsole.WriteLine($@\"Total: {total}\");\nConsole.WriteLine(@$\"Total: {total}\");\n")]
    // Given the values to fill in: composite formatting numbers its braces.
    [InlineData("int total = 150;\nConsole.WriteLine(\"Total: {0}\", total);\n")]
    // A name the program never declares: braces meant as they are written.
    [InlineData("Console.WriteLine(\"Write {braces} around a name\");\n")]
    // An attribute's route, whose {id} the web framework fills in.
    [InlineData("class Items\n{\n    [HttpGet(\"{id}\")]\n    public int Get(int id) => id;\n}\n")]
    // Text kept in a name and filled in later.
    [InlineData("string name = \"Ada\";\nstring template = \"Hello, {name}\";\nConsole.WriteLine(template.Replace(\"{name}\", name));\n")]
    // Interpolated text whose braces hold quotes of their own.
    [InlineData("var door = new { State = \"Open\" };\nbool changed = true;\nConsole.WriteLine($\"{(changed ? \"changed\" : \"ignored\"),-7} now {door.State}\");\n")]
    // Raw text, and a comment.
    [InlineData("int total = 150;\nConsole.WriteLine(\"\"\"Total: {total}\"\"\");\n// Console.WriteLine(\"Total: {total}\");\n")]
    public void CSharpBracesMeantAsTheyAreOrFilledInElsewhereAreLeftAlone(string code) =>
        Assert.Empty(Scan("main.cs", code, "logic-csharp-text-without-dollar"));

    [Theory]
    [InlineData("name = \"Ada\"\nputs 'Hello, #{name}'\n", 2, "puts \"Hello, #{name}\"")]
    [InlineData("marks = [70, 80]\nputs 'Total: #{marks.sum}'\n", 2, "puts \"Total: #{marks.sum}\"")]
    [InlineData("def greet(name)\n  'Hello, #{name}'\nend\nputs greet(\"Ada\")\n", 2, "  \"Hello, #{name}\"")]
    public void RubyInterpolationInSingleQuotesIsFoundAndTheQuotesBecomeDouble(string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan("main.rb", code, "logic-ruby-interpolation-in-single-quotes"));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Fact]
    public void RubyTextWithADoubleQuoteOfItsOwnIsFoundWithNoChange()
    {
        var found = Assert.Single(Scan("main.rb", "name = \"Ada\"\nputs 'She said \"hi\" to #{name}'\n", "logic-ruby-interpolation-in-single-quotes"));
        Assert.Equal(2, found.Line);
        Assert.Null(found.Fix);
    }

    [Theory]
    // Already in double quotes.
    [InlineData("name = \"Ada\"\nputs \"Hello, #{name}\"\n")]
    // A name the program never uses: text that shows how #{} is written.
    [InlineData("puts 'Write #{braces} in double quotes'\n")]
    // A comment.
    [InlineData("name = \"Ada\"\n# puts 'Hello, #{name}'\n")]
    public void RubySingleQuotesMeantAsTheyAreAreLeftAlone(string code) =>
        Assert.Empty(Scan("main.rb", code, "logic-ruby-interpolation-in-single-quotes"));

    [Theory]
    [InlineData("main.php", "logic-php-variable-in-single-quotes", "<?php\n$name = \"Ada\";\necho 'Hello, $name';\n", 3, "echo \"Hello, $name\";")]
    [InlineData("main.php", "logic-php-variable-in-single-quotes", "<?php\n$marks = [70, 80];\nforeach ($marks as $mark) {\n    echo 'Mark: $mark' . \"\\n\";\n}\n", 4, "    echo \"Mark: $mark\" . \"\\n\";")]
    [InlineData("main.pl", "logic-perl-variable-in-single-quotes", "use strict;\nuse warnings;\nmy $name = \"Ada\";\nprint 'Hello, $name';\n", 4, "print \"Hello, $name\";")]
    [InlineData("main.fsx", "logic-fsharp-text-without-dollar", "let name = \"Ada\"\nprintfn \"Hello, {name}\"\n", 2, "printfn $\"Hello, {name}\"")]
    [InlineData("main.fsx", "logic-fsharp-text-without-dollar", "let total = 150\nlet message = \"Total: {total}\"\nprintfn \"%s\" message\n", 2, "let message = $\"Total: {total}\"")]
    [InlineData("main.scala", "logic-scala-text-without-s", "@main def run(): Unit =\n  val name = \"Ada\"\n  println(\"Hello, $name\")\n", 3, "  println(s\"Hello, $name\")")]
    [InlineData("main.scala", "logic-scala-text-without-s", "@main def run(): Unit =\n  val marks = List(70, 80)\n  println(\"Total: ${marks.sum}\")\n", 3, "  println(s\"Total: ${marks.sum}\")")]
    public void ANameInTextThatPutsNothingInIsFoundAndChanged(string fileName, string patternId, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // A " of its own, which double quotes would end the text at.
    [InlineData("main.php", "logic-php-variable-in-single-quotes", "<?php\n$name = \"Ada\";\necho 'She said \"hi\" to $name';\n", 3)]
    // An @ of its own, which Perl's double quotes would read as an array.
    [InlineData("main.pl", "logic-perl-variable-in-single-quotes", "use strict;\nuse warnings;\nmy $name = \"Ada\";\nprint 'Mail $name at ada@example.com';\n", 4)]
    public void TextThatDoubleQuotesWouldReadDifferentlyIsFoundWithNoChange(string fileName, string patternId, string code, int line)
    {
        var found = Assert.Single(Scan(fileName, code, patternId));
        Assert.Equal(line, found.Line);
        Assert.Null(found.Fix);
    }

    [Theory]
    // PHP: double quotes already; a $ before a number; a variable the program never has; a comment.
    [InlineData("main.php", "logic-php-variable-in-single-quotes", "<?php\n$name = \"Ada\";\necho \"Hello, $name\";\necho 'Price: $5';\necho 'Hello, $nobody';\n// echo 'Hello, $name';\n")]
    // Perl: the same, with Perl's comment.
    [InlineData("main.pl", "logic-perl-variable-in-single-quotes", "use strict;\nuse warnings;\nmy $name = \"Ada\";\nprint \"Hello, $name\";\nprint 'Price: $5';\n# print 'Hello, $name';\n")]
    // F#: interpolated already; a printf format, whose values follow it; a name the program never uses; a comment.
    [InlineData("main.fsx", "logic-fsharp-text-without-dollar", "let name = \"Ada\"\nprintfn $\"Hello, {name}\"\nprintfn \"Hello, %s {name}\" name\nprintfn \"Write {braces} around a name\"\n// printfn \"Hello, {name}\"\n")]
    // Scala: s, f and raw already; a $ before a number; a name the program never uses.
    [InlineData("main.scala", "logic-scala-text-without-s", "@main def run(): Unit =\n  val name = \"Ada\"\n  val total = 1.5\n  println(s\"Hello, $name\")\n  println(f\"$total%.2f\")\n  println(raw\"$name\")\n  println(\"Costs $5\")\n  println(\"Hello, $nobody\")\n")]
    public void TextMeantAsItIsOrAlreadyFilledInIsLeftAlone(string fileName, string patternId, string code) =>
        Assert.Empty(Scan(fileName, code, patternId));
}

/// <summary>
/// The mistake and its change run with each language's own toolchain, as FixFinder Learn runs a lesson: the mistake prints the
/// braces as they are written, and the change the value. A language this computer cannot run is passed over, and said to be.
/// </summary>
public class TextHoleLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string, string, string, string, int, string, string> Mistakes => new()
    {
        { "C#", "main.cs", "logic-csharp-text-without-dollar", "int first = 70;\nint second = 80;\nint total = first + second;\nConsole.WriteLine(\"Total: {total}\");\n", 4, "Total: {total}", "Total: 150" },
        { "Ruby", "main.rb", "logic-ruby-interpolation-in-single-quotes", "name = \"Ada\"\nputs 'Hello, #{name}'\n", 2, "Hello, #{name}", "Hello, Ada" },
        { "PHP", "main.php", "logic-php-variable-in-single-quotes", "<?php\n$name = \"Ada\";\necho 'Hello, $name';\n", 3, "Hello, $name", "Hello, Ada" },
        { "Perl", "main.pl", "logic-perl-variable-in-single-quotes", "use strict;\nuse warnings;\nmy $name = \"Ada\";\nprint 'Hello, $name';\n", 4, "Hello, $name", "Hello, Ada" },
        { "F#", "main.fsx", "logic-fsharp-text-without-dollar", "let name = \"Ada\"\nprintfn \"Hello, {name}\"\n", 2, "Hello, {name}", "Hello, Ada" },
        // name is used again after the text, so the build has no unused-name warning to print before the program's output.
        { "Scala", "main.scala", "logic-scala-text-without-s", "@main def run(): Unit =\n  val name = \"Ada\"\n  println(\"Hello, $name\")\n  println(name.length)\n", 3, "Hello, $name\n3", "Hello, Ada\n3" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakePrintsTheBracesAndTheChangeTheValue(string languageName, string fileName, string patternId, string code, int line, string mistakePrints, string changePrints)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);

        var language = CodeLanguage.All.Single(each => each.Name == languageName);
        var found = Assert.Single(LogicPatterns.Scan(SourceFile.Read(path)!), finding => finding.PatternId == patternId);
        Assert.Equal(line, found.Line);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines[found.Fix!.StartLine - 1] = found.Fix.NewLines.Single();
        var changed = string.Join("\n", lines) + "\n";

        var wrong = await SnippetRunner.RunAsync(language, code);
        if (!wrong.Ran)
        {
            output.WriteLine($"Not run on this computer: {wrong.WhyNotRun}");
            return;
        }

        var right = await SnippetRunner.RunAsync(language, changed);
        output.WriteLine($"the mistake: {wrong.Said(languageName)}\nthe change:  {right.Said(languageName)}");

        Assert.True(wrong.Shows(Behaviour.Printing(mistakePrints)), wrong.Said(languageName));
        Assert.True(right.Shows(Behaviour.Printing(changePrints)), right.Said(languageName));
    }
}
