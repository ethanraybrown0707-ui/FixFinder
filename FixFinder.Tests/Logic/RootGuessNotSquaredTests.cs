using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A square root by Newton's method in each language that stops when the guess is near the number itself, with the change
/// that tests the guess's square; the same square roots testing it, and programs near the mistake but not it, which are
/// left alone. Each row is what the check gave for the
/// program when these were written.
/// </summary>
public class RootGuessNotSquaredTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private SourceFile Program(string fileName, string code)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return SourceFile.Read(path)!;
    }

    /// <summary>The file, the program, the check, the line it is said of, the line the change starts on, how many it takes away, and the lines it writes, one a line.</summary>
    public static TheoryData<string, string, string, int, int, int, string> Mistakes => new()
    {
        { "Program.cs", "using System;\nusing System.Globalization;\n\npublic static class Program\n{\n    static double SquareRoot(double x, double epsilon)\n    {\n        double guess = x / 2;\n        while (Math.Abs(x - guess) > epsilon)\n            guess = (guess + x / guess) / 2;\n        return guess;\n    }\n\n    public static void Main() => Console.WriteLine(SquareRoot(2, 0.001).ToString(\"F3\", CultureInfo.InvariantCulture));\n}\n", "logic-csharp-root-guess-not-squared", 9, 9, 1, "        while (Math.Abs(x - guess * guess) > epsilon)" },
        { "roots.c", "#include <math.h>\n#include <stdio.h>\n\ndouble square_root(double x, double epsilon) {\n    double guess = x / 2;\n    while (fabs(x - guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nint main(void) {\n    printf(\"%.3f\\n\", square_root(2, 0.001));\n    return 0;\n}\n", "logic-c-root-guess-not-squared", 6, 6, 1, "    while (fabs(x - guess * guess) > epsilon) {" },
        { "roots.cpp", "#include <cmath>\n#include <cstdio>\n\ndouble squareRoot(double x, double epsilon) {\n    double guess = x / 2;\n    while (std::abs(x - guess) > epsilon) {\n        guess = 0.5 * (guess + x / guess);\n    }\n    return guess;\n}\n\nint main() {\n    std::printf(\"%.3f\\n\", squareRoot(2, 0.001));\n    return 0;\n}\n", "logic-cpp-root-guess-not-squared", 6, 6, 1, "    while (std::abs(x - guess * guess) > epsilon) {" },
        { "roots.dart", "double squareRoot(double x, double epsilon) {\n  var guess = x / 2;\n  while ((x - guess).abs() > epsilon) {\n    guess = (guess + x / guess) / 2;\n  }\n  return guess;\n}\n\nvoid main() {\n  print(squareRoot(2, 0.001).toStringAsFixed(3));\n}\n", "logic-dart-root-guess-not-squared", 3, 3, 1, "  while ((x - guess * guess).abs() > epsilon) {" },
        { "roots.erl", "-module(roots).\n-export([main/0]).\n\nsquare_root(X, Epsilon) -> improve(X, X / 2, Epsilon).\n\nimprove(X, Guess, Epsilon) ->\n    case abs(X - Guess) > Epsilon of\n        true -> improve(X, (Guess + X / Guess) / 2, Epsilon);\n        false -> Guess\n    end.\n\nmain() ->\n    io:format(\"~.3f~n\", [square_root(2, 0.001)]).\n", "logic-erlang-root-guess-not-squared", 7, 7, 1, "    case abs(X - Guess * Guess) > Epsilon of" },
        { "roots.exs", "defmodule Roots do\n  def square_root(x, epsilon), do: improve(x, x / 2, epsilon)\n\n  defp improve(x, guess, epsilon) do\n    if abs(x - guess) > epsilon do\n      improve(x, (guess + x / guess) / 2, epsilon)\n    else\n      guess\n    end\n  end\nend\n\nIO.puts(:erlang.float_to_binary(Roots.square_root(2, 0.001), decimals: 3))\n", "logic-elixir-root-guess-not-squared", 5, 5, 1, "    if abs(x - guess * guess) > epsilon do" },
        { "roots.f90", "program roots\n  implicit none\n  print '(F5.3)', square_root(2.0d0, 0.001d0)\ncontains\n  double precision function square_root(x, epsilon)\n    double precision, intent(in) :: x, epsilon\n    double precision :: guess\n    guess = x / 2\n    do while (abs(x - guess) > epsilon)\n      guess = (guess + x / guess) / 2\n    end do\n    square_root = guess\n  end function square_root\nend program roots\n", "logic-fortran-root-guess-not-squared", 9, 9, 1, "    do while (abs(x - guess * guess) > epsilon)" },
        { "roots.fsx", "let squareRoot x epsilon =\n    let rec improve guess =\n        if abs (x - guess) > epsilon then improve ((guess + x / guess) / 2.0) else guess\n    improve (x / 2.0)\n\nprintfn \"%.3f\" (squareRoot 2.0 0.001)\n", "logic-fsharp-root-guess-not-squared", 3, 3, 1, "        if abs (x - guess * guess) > epsilon then improve ((guess + x / guess) / 2.0) else guess" },
        { "roots.go", "package main\n\nimport (\n\t\"fmt\"\n\t\"math\"\n)\n\nfunc squareRoot(x, epsilon float64) float64 {\n\tguess := x / 2\n\tfor math.Abs(x-guess) > epsilon {\n\t\tguess = (guess + x/guess) / 2\n\t}\n\treturn guess\n}\n\nfunc main() {\n\tfmt.Printf(\"%.3f\\n\", squareRoot(2, 0.001))\n}\n", "logic-go-root-guess-not-squared", 10, 10, 1, "\tfor math.Abs(x-guess*guess) > epsilon {" },
        { "roots.js", "function squareRoot(x, epsilon) {\n    let guess = x / 2;\n    while (Math.abs(x - guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nconsole.log(squareRoot(2, 0.001).toFixed(3));\n", "logic-js-root-guess-not-squared", 3, 3, 1, "    while (Math.abs(x - guess * guess) > epsilon) {" },
        { "roots.lua", "local function squareRoot(x, epsilon)\n  local guess = x / 2\n  while math.abs(x - guess) > epsilon do\n    guess = (guess + x / guess) / 2\n  end\n  return guess\nend\n\nprint(string.format(\"%.3f\", squareRoot(2, 0.001)))\n", "logic-lua-root-guess-not-squared", 3, 3, 1, "  while math.abs(x - guess * guess) > epsilon do" },
        { "roots.ml", "let square_root x epsilon =\n  let rec improve guess =\n    if Float.abs (x -. guess) > epsilon then improve ((guess +. x /. guess) /. 2.0) else guess\n  in\n  improve (x /. 2.0)\n\nlet () = Printf.printf \"%.3f\\n\" (square_root 2.0 0.001)\n", "logic-ocaml-root-guess-not-squared", 3, 3, 1, "    if Float.abs (x -. guess *. guess) > epsilon then improve ((guess +. x /. guess) /. 2.0) else guess" },
        { "roots.php", "<?php\nfunction squareRoot(float $x, float $epsilon): float {\n    $guess = $x / 2;\n    while (abs($x - $guess) > $epsilon) {\n        $guess = ($guess + $x / $guess) / 2;\n    }\n    return $guess;\n}\n\nprintf(\"%.3f\\n\", squareRoot(2, 0.001));\n", "logic-php-root-guess-not-squared", 4, 4, 1, "    while (abs($x - $guess * $guess) > $epsilon) {" },
        { "roots.pl", "use strict;\nuse warnings;\n\nsub square_root {\n    my ($x, $epsilon) = @_;\n    my $guess = $x / 2;\n    while (abs($x - $guess) > $epsilon) {\n        $guess = ($guess + $x / $guess) / 2;\n    }\n    return $guess;\n}\n\nprintf(\"%.3f\\n\", square_root(2, 0.001));\n", "logic-perl-root-guess-not-squared", 7, 7, 1, "    while (abs($x - $guess * $guess) > $epsilon) {" },
        { "roots.ps1", "function Get-SquareRoot($x, $epsilon) {\n    $guess = $x / 2\n    while ([Math]::Abs($x - $guess) -gt $epsilon) {\n        $guess = ($guess + $x / $guess) / 2\n    }\n    return $guess\n}\n\nWrite-Output (\"{0:F3}\" -f (Get-SquareRoot 2.0 0.001))\n", "logic-powershell-root-guess-not-squared", 3, 3, 1, "    while ([Math]::Abs($x - $guess * $guess) -gt $epsilon) {" },
        { "roots.rb", "def square_root(x, epsilon)\n  guess = x / 2.0\n  while (x - guess).abs > epsilon\n    guess = (guess + x / guess) / 2\n  end\n  guess\nend\n\nputs format(\"%.3f\", square_root(2, 0.001))\n", "logic-ruby-root-guess-not-squared", 3, 3, 1, "  while (x - guess * guess).abs > epsilon" },
        { "roots.rs", "fn square_root(x: f64, epsilon: f64) -> f64 {\n    let mut guess = x / 2.0;\n    while (x - guess).abs() > epsilon {\n        guess = (guess + x / guess) / 2.0;\n    }\n    guess\n}\n\nfn main() {\n    println!(\"{:.3}\", square_root(2.0, 0.001));\n}\n", "logic-rust-root-guess-not-squared", 3, 3, 1, "    while (x - guess * guess).abs() > epsilon {" },
        { "Roots.scala", "object Roots {\n  def squareRoot(x: Double, epsilon: Double): Double = {\n    var guess = x / 2\n    while (math.abs(x - guess) > epsilon) {\n      guess = (guess + x / guess) / 2\n    }\n    guess\n  }\n\n  def main(args: Array[String]): Unit = println(f\"${squareRoot(2, 0.001)}%.3f\")\n}\n", "logic-scala-root-guess-not-squared", 4, 4, 1, "    while (math.abs(x - guess * guess) > epsilon) {" },
        { "roots.ts", "function squareRoot(x: number, epsilon: number): number {\n    let guess = x / 2;\n    while (Math.abs(x - guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nconsole.log(squareRoot(2, 0.001).toFixed(3));\n", "logic-js-root-guess-not-squared", 3, 3, 1, "    while (Math.abs(x - guess * guess) > epsilon) {" },
        { "Sqrt.java", "public class Sqrt {\n    static double sqrt(double x, double epsilon) {\n        double approx = x / 2d;\n        while (Math.abs(x-approx) > epsilon) {\n            approx = 0.5d * (approx + x / approx);\n        }\n        return approx;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(String.format(\"%.3f\", sqrt(2, 0.001)));\n    }\n}\n", "logic-java-root-guess-not-squared", 4, 4, 1, "        while (Math.abs(x-approx*approx) > epsilon) {" },
        { "sqrt.py", "def sqrt(x, epsilon):\n    approx = x / 2\n    while abs(x - approx) > epsilon:\n        approx = 0.5 * (approx + x / approx)\n    return approx\n\n\nprint(f\"{sqrt(2, 0.001):.3f}\")\n", "logic-python-root-guess-not-squared", 3, 3, 1, "    while abs(x - approx ** 2) > epsilon:" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void AGuessTestedInsteadOfItsSquareIsFound(string fileName, string code, string check, int line, int changeAt, int removed, string added)
    {
        var found = Assert.Single(LogicPatterns.All.Where(pattern => pattern.Id == check).SelectMany(pattern => pattern.Find(Program(fileName, code))));

        Assert.Equal(line, found.Line);
        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(removed, found.Fix.RemoveCount);
        Assert.Equal(added.Split('\n'), found.Fix.NewLines);
    }

    /// <summary>
    /// The square roots testing the guess's square; Newton's method stopping when two guesses in a row are close; a walk
    /// halving the way to a target; a cube root testing the guess's cube; the square tested first, abs(guess * guess - x);
    /// and the number over the guess tested against the guess.
    /// </summary>
    [Theory]
    [InlineData("Program.cs", "using System;\nusing System.Globalization;\n\npublic static class Program\n{\n    static double SquareRoot(double x, double epsilon)\n    {\n        double guess = x / 2;\n        while (Math.Abs(x - guess * guess) > epsilon)\n            guess = (guess + x / guess) / 2;\n        return guess;\n    }\n\n    public static void Main() => Console.WriteLine(SquareRoot(2, 0.001).ToString(\"F3\", CultureInfo.InvariantCulture));\n}\n")]
    [InlineData("roots.c", "#include <math.h>\n#include <stdio.h>\n\ndouble square_root(double x, double epsilon) {\n    double guess = x / 2;\n    while (fabs(x - guess * guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nint main(void) {\n    printf(\"%.3f\\n\", square_root(2, 0.001));\n    return 0;\n}\n")]
    [InlineData("roots.cpp", "#include <cmath>\n#include <cstdio>\n\ndouble squareRoot(double x, double epsilon) {\n    double guess = x / 2;\n    while (std::abs(x - guess * guess) > epsilon) {\n        guess = 0.5 * (guess + x / guess);\n    }\n    return guess;\n}\n\nint main() {\n    std::printf(\"%.3f\\n\", squareRoot(2, 0.001));\n    return 0;\n}\n")]
    [InlineData("roots.dart", "double squareRoot(double x, double epsilon) {\n  var guess = x / 2;\n  while ((x - guess * guess).abs() > epsilon) {\n    guess = (guess + x / guess) / 2;\n  }\n  return guess;\n}\n\nvoid main() {\n  print(squareRoot(2, 0.001).toStringAsFixed(3));\n}\n")]
    [InlineData("roots.erl", "-module(roots).\n-export([main/0]).\n\nsquare_root(X, Epsilon) -> improve(X, X / 2, Epsilon).\n\nimprove(X, Guess, Epsilon) ->\n    case abs(X - Guess * Guess) > Epsilon of\n        true -> improve(X, (Guess + X / Guess) / 2, Epsilon);\n        false -> Guess\n    end.\n\nmain() ->\n    io:format(\"~.3f~n\", [square_root(2, 0.001)]).\n")]
    [InlineData("roots.exs", "defmodule Roots do\n  def square_root(x, epsilon), do: improve(x, x / 2, epsilon)\n\n  defp improve(x, guess, epsilon) do\n    if abs(x - guess * guess) > epsilon do\n      improve(x, (guess + x / guess) / 2, epsilon)\n    else\n      guess\n    end\n  end\nend\n\nIO.puts(:erlang.float_to_binary(Roots.square_root(2, 0.001), decimals: 3))\n")]
    [InlineData("roots.f90", "program roots\n  implicit none\n  print '(F5.3)', square_root(2.0d0, 0.001d0)\ncontains\n  double precision function square_root(x, epsilon)\n    double precision, intent(in) :: x, epsilon\n    double precision :: guess\n    guess = x / 2\n    do while (abs(x - guess * guess) > epsilon)\n      guess = (guess + x / guess) / 2\n    end do\n    square_root = guess\n  end function square_root\nend program roots\n")]
    [InlineData("roots.fsx", "let squareRoot x epsilon =\n    let rec improve guess =\n        if abs (x - guess * guess) > epsilon then improve ((guess + x / guess) / 2.0) else guess\n    improve (x / 2.0)\n\nprintfn \"%.3f\" (squareRoot 2.0 0.001)\n")]
    [InlineData("roots.go", "package main\n\nimport (\n\t\"fmt\"\n\t\"math\"\n)\n\nfunc squareRoot(x, epsilon float64) float64 {\n\tguess := x / 2\n\tfor math.Abs(x-guess*guess) > epsilon {\n\t\tguess = (guess + x/guess) / 2\n\t}\n\treturn guess\n}\n\nfunc main() {\n\tfmt.Printf(\"%.3f\\n\", squareRoot(2, 0.001))\n}\n")]
    [InlineData("roots.js", "function squareRoot(x, epsilon) {\n    let guess = x / 2;\n    while (Math.abs(x - guess * guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nconsole.log(squareRoot(2, 0.001).toFixed(3));\n")]
    [InlineData("roots.lua", "local function squareRoot(x, epsilon)\n  local guess = x / 2\n  while math.abs(x - guess * guess) > epsilon do\n    guess = (guess + x / guess) / 2\n  end\n  return guess\nend\n\nprint(string.format(\"%.3f\", squareRoot(2, 0.001)))\n")]
    [InlineData("roots.ml", "let square_root x epsilon =\n  let rec improve guess =\n    if Float.abs (x -. guess *. guess) > epsilon then improve ((guess +. x /. guess) /. 2.0) else guess\n  in\n  improve (x /. 2.0)\n\nlet () = Printf.printf \"%.3f\\n\" (square_root 2.0 0.001)\n")]
    [InlineData("roots.php", "<?php\nfunction squareRoot(float $x, float $epsilon): float {\n    $guess = $x / 2;\n    while (abs($x - $guess * $guess) > $epsilon) {\n        $guess = ($guess + $x / $guess) / 2;\n    }\n    return $guess;\n}\n\nprintf(\"%.3f\\n\", squareRoot(2, 0.001));\n")]
    [InlineData("roots.pl", "use strict;\nuse warnings;\n\nsub square_root {\n    my ($x, $epsilon) = @_;\n    my $guess = $x / 2;\n    while (abs($x - $guess * $guess) > $epsilon) {\n        $guess = ($guess + $x / $guess) / 2;\n    }\n    return $guess;\n}\n\nprintf(\"%.3f\\n\", square_root(2, 0.001));\n")]
    [InlineData("roots.ps1", "function Get-SquareRoot($x, $epsilon) {\n    $guess = $x / 2\n    while ([Math]::Abs($x - $guess * $guess) -gt $epsilon) {\n        $guess = ($guess + $x / $guess) / 2\n    }\n    return $guess\n}\n\nWrite-Output (\"{0:F3}\" -f (Get-SquareRoot 2.0 0.001))\n")]
    [InlineData("roots.rb", "def square_root(x, epsilon)\n  guess = x / 2.0\n  while (x - guess * guess).abs > epsilon\n    guess = (guess + x / guess) / 2\n  end\n  guess\nend\n\nputs format(\"%.3f\", square_root(2, 0.001))\n")]
    [InlineData("roots.rs", "fn square_root(x: f64, epsilon: f64) -> f64 {\n    let mut guess = x / 2.0;\n    while (x - guess * guess).abs() > epsilon {\n        guess = (guess + x / guess) / 2.0;\n    }\n    guess\n}\n\nfn main() {\n    println!(\"{:.3}\", square_root(2.0, 0.001));\n}\n")]
    [InlineData("Roots.scala", "object Roots {\n  def squareRoot(x: Double, epsilon: Double): Double = {\n    var guess = x / 2\n    while (math.abs(x - guess * guess) > epsilon) {\n      guess = (guess + x / guess) / 2\n    }\n    guess\n  }\n\n  def main(args: Array[String]): Unit = println(f\"${squareRoot(2, 0.001)}%.3f\")\n}\n")]
    [InlineData("roots.ts", "function squareRoot(x: number, epsilon: number): number {\n    let guess = x / 2;\n    while (Math.abs(x - guess * guess) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nconsole.log(squareRoot(2, 0.001).toFixed(3));\n")]
    [InlineData("Sqrt.java", "public class Sqrt {\n    static double sqrt(double x, double epsilon) {\n        double approx = x / 2d;\n        while (Math.abs(x-approx*approx) > epsilon) {\n            approx = 0.5d * (approx + x / approx);\n        }\n        return approx;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(String.format(\"%.3f\", sqrt(2, 0.001)));\n    }\n}\n")]
    [InlineData("sqrt.py", "def sqrt(x, epsilon):\n    approx = x / 2\n    while abs(x - approx ** 2) > epsilon:\n        approx = 0.5 * (approx + x / approx)\n    return approx\n\n\nprint(f\"{sqrt(2, 0.001):.3f}\")\n")]
    [InlineData("converged.py", "# prints 1.414\ndef square_root(x, epsilon):\n    previous = 0.0\n    guess = x / 2\n    while abs(guess - previous) > epsilon:\n        previous = guess\n        guess = (guess + x / guess) / 2\n    return guess\n\n\nprint(f\"{square_root(2, 0.001):.3f}\")\n")]
    [InlineData("cube_root.js", "// prints 3.000\nfunction cubeRoot(x, epsilon) {\n    let guess = x / 3;\n    while (Math.abs(x - guess * guess * guess) > epsilon) {\n        guess = (2 * guess + x / (guess * guess)) / 3;\n    }\n    return guess;\n}\n\nconsole.log(cubeRoot(27, 0.001).toFixed(3));\n")]
    [InlineData("halfway.py", "# prints 9.999\ndef approach(target, tolerance):\n    position = 0.0\n    while abs(target - position) > tolerance:\n        position = (position + target) / 2\n    return position\n\n\nprint(f\"{approach(10, 0.001):.3f}\")\n")]
    [InlineData("Relative.java", "// prints 1.414\npublic class Relative {\n    static double squareRoot(double x, double epsilon) {\n        double guess = x / 2;\n        while (Math.abs(x / guess - guess) > epsilon) {\n            guess = (guess + x / guess) / 2;\n        }\n        return guess;\n    }\n\n    public static void main(String[] args) {\n        System.out.println(String.format(\"%.3f\", squareRoot(2, 0.001)));\n    }\n}\n")]
    [InlineData("squared_first.c", "// prints 1.414\n#include <math.h>\n#include <stdio.h>\n\ndouble square_root(double x, double epsilon) {\n    double guess = x / 2;\n    while (fabs(guess * guess - x) > epsilon) {\n        guess = (guess + x / guess) / 2;\n    }\n    return guess;\n}\n\nint main(void) {\n    printf(\"%.3f\\n\", square_root(2, 0.001));\n    return 0;\n}\n")]
    public void ASquareOrAnotherTestIsLeftAlone(string fileName, string code)
    {
        // Each check of the file's own language, as FixFinder runs them.
        var file = Program(fileName, code);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        Assert.Empty(LogicPatterns.All.Where(pattern => pattern.Id.EndsWith("root-guess-not-squared", StringComparison.Ordinal) && pattern.Extensions.Contains(extension))
            .SelectMany(pattern => pattern.Find(file)));
    }
}
