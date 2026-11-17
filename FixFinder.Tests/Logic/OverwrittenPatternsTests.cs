using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A value worked out and then lost, in the languages whose values FixFinder does not follow through the code: area gives
/// result width * height, and the very next line gives it width * width - found in TypeScript, Ruby, PHP, Lua, Perl, Dart,
/// PowerShell, Scala and Fortran. Left alone in each: a starting value given first, a value read before the next, one given
/// another only under a test, one a handler for an error reads - inside its text too - and one given what a function of the
/// file's own works out. A JavaScript file is left to the analysis that follows its values, though TypeScript is read with
/// JavaScript's checks.
/// </summary>
public class OverwrittenPatternsTests
{
    private static IReadOnlyList<LogicFinding> Scanned(string fileName, string code) =>
        LogicPatterns.Scan(new SourceFile { Path = Path.Combine(Path.GetTempPath(), fileName), Lines = code.Replace("\r\n", "\n").Split('\n') });

    /// <summary>The file, the program, the check, the line whose value is lost, and what the check said of it here.</summary>
    public static TheoryData<string, string, string, int, string> Lost => new()
    {
        { "area.ts", "function area(width: number, height: number): number {\n  let result: number = width * height;\n  result = width * width;\n  return result;\n}\n\nconsole.log(area(3, 4));\n", "logic-typescript-overwritten-before-read", 2, "`result` is given `width * height` on line 2, then given another value on line 3 before anything reads it, so the value from line 2 is never used" },
        { "area.rb", "def area(width, height)\n  result = width * height\n  result = width * width\n  result\nend\n\nputs area(3, 4)\n", "logic-ruby-overwritten-before-read", 2, "`result` is given `width * height` on line 2, then given another value on line 3 before anything reads it, so the value from line 2 is never used" },
        { "area.php", "<?php\nfunction area(int $width, int $height): int\n{\n    $result = $width * $height;\n    $result = $width * $width;\n    return $result;\n}\n\necho area(3, 4), PHP_EOL;\n", "logic-php-overwritten-before-read", 4, "`$result` is given `$width * $height` on line 4, then given another value on line 5 before anything reads it, so the value from line 4 is never used" },
        { "area.lua", "local function area(width, height)\n  local result = width * height\n  result = width * width\n  return result\nend\n\nprint(area(3, 4))\n", "logic-lua-overwritten-before-read", 2, "`result` is given `width * height` on line 2, then given another value on line 3 before anything reads it, so the value from line 2 is never used" },
        { "area.pl", "use strict;\nuse warnings;\n\nsub area {\n    my ($width, $height) = @_;\n    my $result = $width * $height;\n    $result = $width * $width;\n    return $result;\n}\n\nprint area(3, 4), \"\\n\";\n", "logic-perl-overwritten-before-read", 6, "`$result` is given `$width * $height` on line 6, then given another value on line 7 before anything reads it, so the value from line 6 is never used" },
        { "area.dart", "int area(int width, int height) {\n  var result = width * height;\n  result = width * width;\n  return result;\n}\n\nvoid main() {\n  print(area(3, 4));\n}\n", "logic-dart-overwritten-before-read", 2, "`result` is given `width * height` on line 2, then given another value on line 3 before anything reads it, so the value from line 2 is never used" },
        { "area.ps1", "function Get-Area($width, $height) {\n    $result = $width * $height\n    $result = $width * $width\n    return $result\n}\n\nGet-Area 3 4\n", "logic-powershell-overwritten-before-read", 2, "`$result` is given `$width * $height` on line 2, then given another value on line 3 before anything reads it, so the value from line 2 is never used" },
        { "Area.scala", "object Area {\n  def area(width: Int, height: Int): Int = {\n    var result = width * height\n    result = width * width\n    result\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(area(3, 4))\n  }\n}\n", "logic-scala-overwritten-before-read", 3, "`result` is given `width * height` on line 3, then given another value on line 4 before anything reads it, so the value from line 3 is never used" },
        { "area.f90", "program main\n  implicit none\n  print *, area(3, 4)\ncontains\n  integer function area(width, height)\n    integer, intent(in) :: width, height\n    integer :: result\n    result = width * height\n    result = width * width\n    area = result\n  end function area\nend program main\n", "logic-fortran-overwritten-before-read", 8, "`result` is given `width * height` on line 8, then given another value on line 9 before anything reads it, so the value from line 8 is never used" },
    };

    /// <summary>The file, and a program of near misses the check must leave alone.</summary>
    public static TheoryData<string, string> NearMisses => new()
    {
        { "left.ts", "function measure(text: string): number {\n  return text.length;\n}\n\nfunction totalOf(values: number[]): number {\n  let total = 0;\n  total = values.reduce((sum, value) => sum + value, 0);\n  return total;\n}\n\nfunction area(width: number, height: number): number {\n  let result = width * height;\n  console.log(result);\n  result = width * width;\n  return result;\n}\n\nfunction parse(text: string): number {\n  let value = text.length;\n  try {\n    value = JSON.parse(text);\n  } catch (error) {\n    console.log(\"not JSON, its length is\", value);\n  }\n  return value;\n}\n\nfunction sized(text: string): number {\n  let size = text.length * 2;\n  size = measure(text);\n  return size;\n}\n\nconsole.log(totalOf([1, 2]), area(3, 4), parse(\"x\"), sized(\"ab\"));\n" },
        { "left.rb", "def measure(text)\n  text.length\nend\n\ndef total_of(values)\n  total = 0\n  total = values.sum\n  total\nend\n\ndef area(width, height)\n  result = width * height\n  puts result\n  result = width * width\n  result\nend\n\ndef capped(value, limit)\n  shown = value * 2\n  shown = limit if shown > limit\n  shown\nend\n\ndef parse(text)\n  begin\n    value = text.length\n    value = Integer(text)\n  rescue ArgumentError\n    puts \"not a number, its length is #{value}\"\n  end\n  value\nend\n\ndef sized(text)\n  size = text.length * 2\n  size = measure(text)\n  size\nend\n\nputs total_of([1, 2]), area(3, 4), capped(5, 3), parse(\"x\"), sized(\"ab\")\n" },
        { "left.php", "<?php\nfunction measure(string $text): int\n{\n    return strlen($text);\n}\n\nfunction totalOf(array $values): int\n{\n    $total = 0;\n    $total = array_sum($values);\n    return $total;\n}\n\nfunction area(int $width, int $height): int\n{\n    $result = $width * $height;\n    echo $result, PHP_EOL;\n    $result = $width * $width;\n    return $result;\n}\n\nfunction parse(string $text): int\n{\n    try {\n        $value = strlen($text);\n        $value = json_decode($text, false, 512, JSON_THROW_ON_ERROR);\n    } catch (JsonException $error) {\n        echo \"not JSON, its length is $value\", PHP_EOL;\n    }\n    return $value;\n}\n\nfunction sized(string $text): int\n{\n    $size = strlen($text) * 2;\n    $size = measure($text);\n    return $size;\n}\n\necho totalOf([1, 2]), area(3, 4), parse(\"x\"), sized(\"ab\"), PHP_EOL;\n" },
        { "left.lua", "local function measure(text)\n  return #text\nend\n\nlocal function totalOf(values)\n  local total = 0\n  total = #values\n  return total\nend\n\nlocal function area(width, height)\n  local result = width * height\n  print(result)\n  result = width * width\n  return result\nend\n\nlocal function sized(text)\n  local size = #text * 2\n  size = measure(text)\n  return size\nend\n\nprint(totalOf({1, 2}), area(3, 4), sized(\"ab\"))\n" },
        { "left.pl", "use strict;\nuse warnings;\n\nsub measure {\n    my ($text) = @_;\n    return length($text);\n}\n\nsub total_of {\n    my @values = @_;\n    my $total = 0;\n    $total = scalar(@values);\n    return $total;\n}\n\nsub area {\n    my ($width, $height) = @_;\n    my $result = $width * $height;\n    print $result, \"\\n\";\n    $result = $width * $width;\n    return $result;\n}\n\nsub capped {\n    my ($value, $limit) = @_;\n    my $shown = $value * 2;\n    $shown = $limit if $shown > $limit;\n    return $shown;\n}\n\nsub sized {\n    my ($text) = @_;\n    my $size = length($text) * 2;\n    $size = measure($text);\n    return $size;\n}\n\nprint total_of(1, 2), area(3, 4), capped(5, 3), sized(\"ab\"), \"\\n\";\n" },
        { "left.dart", "int measure(String text) => text.length;\n\nint totalOf(List<int> values) {\n  var total = 0;\n  total = values.fold(0, (sum, value) => sum + value);\n  return total;\n}\n\nint area(int width, int height) {\n  var result = width * height;\n  print(result);\n  result = width * width;\n  return result;\n}\n\nint parse(String text) {\n  var value = text.length;\n  try {\n    value = int.parse(text);\n  } catch (error) {\n    print('not a number, its length is $value');\n  }\n  return value;\n}\n\nint sized(String text) {\n  var size = text.length * 2;\n  size = measure(text);\n  return size;\n}\n\nvoid main() {\n  print([totalOf([1, 2]), area(3, 4), parse('x'), sized('ab')]);\n}\n" },
        { "left.ps1", "function Measure-Text($text) {\n    return $text.Length\n}\n\nfunction Get-Total($values) {\n    $total = 0\n    $total = ($values | Measure-Object -Sum).Sum\n    return $total\n}\n\nfunction Get-Area($width, $height) {\n    $result = $width * $height\n    Write-Output $result\n    $result = $width * $width\n    return $result\n}\n\nfunction Get-Parsed($text) {\n    try {\n        $value = $text.Length\n        $value = [int]::Parse($text)\n    } catch {\n        Write-Output \"not a number, its length is $value\"\n    }\n    return $value\n}\n\nfunction Get-Sized($text) {\n    $size = $text.Length * 2\n    $size = Measure-Text $text\n    return $size\n}\n\nGet-Total @(1, 2)\nGet-Area 3 4\nGet-Parsed \"x\"\nGet-Sized \"ab\"\n" },
        { "Left.scala", "object Left {\n  def measure(text: String): Int = text.length\n\n  def totalOf(values: List[Int]): Int = {\n    var total = 0\n    total = values.sum\n    total\n  }\n\n  def area(width: Int, height: Int): Int = {\n    var result = width * height\n    println(result)\n    result = width * width\n    result\n  }\n\n  def parse(text: String): Int = {\n    var value = text.length\n    try {\n      value = text.toInt\n    } catch {\n      case _: NumberFormatException => println(s\"not a number, its length is $value\")\n    }\n    value\n  }\n\n  def sized(text: String): Int = {\n    var size = text.length * 2\n    size = measure(text)\n    size\n  }\n\n  def main(args: Array[String]): Unit = {\n    println(List(totalOf(List(1, 2)), area(3, 4), parse(\"x\"), sized(\"ab\")))\n  }\n}\n" },
        { "left.f90", "program main\n  implicit none\n  print *, total_of(3), area(3, 4)\ncontains\n  integer function total_of(count)\n    integer, intent(in) :: count\n    integer :: total\n    total = 0\n    total = count * 2\n    total_of = total\n  end function total_of\n\n  integer function area(width, height)\n    integer, intent(in) :: width, height\n    integer :: result\n    result = width * height\n    print *, result\n    result = width * width\n    area = result\n  end function area\nend program main\n" },
        { "area.js", "function area(width, height) {\n  let result = width * height;\n  result = width * width;\n  return result;\n}\n\nconsole.log(area(3, 4));\n" },
    };

    [Theory]
    [MemberData(nameof(Lost))]
    public void AValueGivenAnotherOnTheNextLineBeforeAnythingReadsItIsFound(string fileName, string code, string check, int line, string said)
    {
        var lost = Assert.Single(Scanned(fileName, code), finding => finding.PatternId.EndsWith("-overwritten-before-read", StringComparison.Ordinal));
        Assert.Equal(check, lost.PatternId);
        Assert.Equal(line, lost.Line);
        Assert.Equal(said, lost.Message);
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public void AValueThatIsReadOrIsOnlyAStartIsLeftAlone(string fileName, string code)
    {
        Assert.DoesNotContain(Scanned(fileName, code), finding => finding.PatternId.EndsWith("-overwritten-before-read", StringComparison.Ordinal));
    }
}
