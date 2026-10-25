using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Mistakes eight languages run without a word: found where they are, with the line each check's change writes, and left
/// alone where the code means what it says.
/// </summary>
public class SilentMistakePatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string check)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == check).ToList();
    }

    [Theory]
    [InlineData("Program.cs", "logic-csharp-chars-added", "char grade = 'A';\nConsole.WriteLine(grade + 2);\n", 2, "Console.WriteLine((char)(grade + 2));")]
    [InlineData("Program.cs", "logic-csharp-chars-added", "char first = 'a', second = 'b';\nConsole.Write(first + second);\n", 2, "Console.Write(\"\" + first + second);")]
    [InlineData("Main.java", "logic-java-boolean-assigned-in-condition", "class Main {\n    void run(boolean found) { while (found = false) { } }\n}\n", 2, "    void run(boolean found) { while (!found) { } }")]
    [InlineData("app.py", "logic-python-or-before-in", "word = input()\nif \"a\" or \"e\" or \"i\" in word:\n    print(word)\n", 2, "if \"a\" in word or \"e\" in word or \"i\" in word:")]
    [InlineData("app.py", "logic-python-or-before-in", "word = input()\nif 'x' or 'q' not in word:\n    print(word)\n", 2, "if 'x' not in word and 'q' not in word:")]
    [InlineData("main.rb", "logic-ruby-each-with-index-order", "marks = [70, 80]\nmarks.each_with_index { |i, mark| puts mark * i }\n", 2, "marks.each_with_index { |mark, i| puts mark * i }")]
    [InlineData("main.php", "logic-php-search-as-condition", "<?php\n$names = [\"Ada\"];\nif (!array_search(\"Ada\", $names)) { echo \"no\"; }\n", 3, "if (array_search(\"Ada\", $names) === false) { echo \"no\"; }")]
    [InlineData("main.pl", "logic-perl-loop-to-last-index", "my @marks = (70, 80);\nfor (my $i = 0; $i < $#marks; $i++) {\n    print $marks[$i];\n}\n", 2, "for (my $i = 0; $i <= $#marks; $i++) {")]
    [InlineData("main.f90", "logic-fortran-single-precision-constant", "program marks\n  real(8) :: rate\n  rate = 0.3\nend program marks\n", 3, "  rate = 0.3d0")]
    [InlineData("main.f90", "logic-fortran-single-precision-constant", "program marks\n  double precision :: rate = 1.1\nend program marks\n", 2, "  double precision :: rate = 1.1d0")]
    [InlineData("main.ps1", "logic-powershell-arguments-with-commas", "function Add-Marks($first, $second, $third) { $first + $second + $third }\nAdd-Marks 70, 80, 90\n", 2, "Add-Marks 70 80 90")]
    public void AMistakeIsFoundWithItsChange(string fileName, string check, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // A char printed with text, and two numbers added.
    [InlineData("Program.cs", "logic-csharp-chars-added", "char grade = 'A';\nint mark = 70;\nConsole.WriteLine(mark + 1);\nConsole.WriteLine(\"Grade \" + grade);\n")]
    // A boolean compared, and a value given to a name of another kind.
    [InlineData("Main.java", "logic-java-boolean-assigned-in-condition", "class Main {\n    void run(boolean done) { if (done == true) { } }\n}\n")]
    // Each piece of text looked up, and text inside text.
    [InlineData("app.py", "logic-python-or-before-in", "word = input()\nif \"a\" in word or \"e\" in word:\n    print(word)\n")]
    [InlineData("app.py", "logic-python-or-before-in", "print('\"a\" or \"e\" in word')\n")]
    // The item named first, and two names that are both items.
    [InlineData("main.rb", "logic-ruby-each-with-index-order", "marks = [70, 80]\nmarks.each_with_index do |mark, index|\n  puts mark\nend\n")]
    [InlineData("main.rb", "logic-ruby-each-with-index-order", "pairs = [[1, 2]]\npairs.each_with_index { |pair, other| p pair }\n")]
    // Compared with false strictly, and a search's result kept.
    [InlineData("main.php", "logic-php-search-as-condition", "<?php\n$word = \"apple\";\nif (strpos($word, \"a\") !== false) { echo \"yes\"; }\n$at = strpos($word, \"p\");\n")]
    // Up to the last position, and below the count.
    [InlineData("main.pl", "logic-perl-loop-to-last-index", "my @marks = (70, 80);\nfor (my $i = 0; $i <= $#marks; $i++) {\n    print $marks[$i];\n}\n")]
    [InlineData("main.pl", "logic-perl-loop-to-last-index", "my @marks = (70, 80);\nfor (my $i = 0; $i < scalar(@marks); $i++) {\n    print $marks[$i];\n}\n")]
    // Written as a double, held exactly by a single, and a default real given a decimal.
    [InlineData("main.f90", "logic-fortran-single-precision-constant", "program marks\n  double precision :: rate, half\n  rate = 0.1d0\n  half = 0.5\nend program marks\n")]
    [InlineData("main.f90", "logic-fortran-single-precision-constant", "program marks\n  real :: rate\n  rate = 0.1\nend program marks\n")]
    // Values separated by spaces, and a list given to a function that takes one on purpose, with fewer values than parameters.
    [InlineData("main.ps1", "logic-powershell-arguments-with-commas", "function Get-Area($width, $height) { $width * $height }\nGet-Area 3 4\n")]
    [InlineData("main.ps1", "logic-powershell-arguments-with-commas", "function Show-Marks($marks, $title, $footer) { $marks }\nShow-Marks 70, 80\n")]
    public void CodeThatMeansWhatItSaysIsLeftAlone(string fileName, string check, string code) => Assert.Empty(Scan(fileName, code, check));
}
