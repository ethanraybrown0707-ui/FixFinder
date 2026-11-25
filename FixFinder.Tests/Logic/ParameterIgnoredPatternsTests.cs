using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A value passed in and never used, in TypeScript, Ruby, PHP, Lua, Perl, Dart and PowerShell, and in F# and OCaml, where a
/// let of the parameter's name hides it: total price gives quantity 1 before anything reads it. Left alone in each: each
/// found program put right, a parameter read first, one given another value only sometimes, one read inside text before it
/// is given another, the caller's own variable - PHP's &amp;$counts, PowerShell's [ref] - Dart's this.width and Ruby's
/// &amp;block; and a JavaScript file, whose parameters the analysis follows.
/// </summary>
public class ParameterIgnoredPatternsTests
{
    private static IReadOnlyList<LogicFinding> Scanned(string fileName, string code) =>
        LogicPatterns.Scan(new SourceFile { Path = Path.Combine(Path.GetTempPath(), fileName), Lines = code.Replace("\r\n", "\n").Split('\n') })
            .Where(finding => finding.PatternId.EndsWith("-parameter-ignored", StringComparison.Ordinal))
            .ToList();

    /// <summary>The file, the program, and what the check said of it here: the finding's line, check and words.</summary>
    public static TheoryData<string, string, string> Found => new()
    {
        { "price.dart", "int totalPrice(int price, int quantity) {\n  quantity = 1;\n  return price * quantity;\n}\n\nvoid main() {\n  print(totalPrice(4, 3));\n}\n", "2|logic-dart-parameter-ignored|`quantity` is given `1` on line 2 before anything reads it, so the value the caller passes for `quantity` is never used" },
        { "price.fsx", "let totalPrice price quantity =\n    let quantity = 1\n    price * quantity\n\nprintfn \"%d\" (totalPrice 4 3)\n", "2|logic-fsharp-parameter-ignored|`let quantity = 1` on line 2 hides the parameter `quantity` before anything reads it, so the value the caller passes for `quantity` is never used" },
        { "price.lua", "local function total_price(price, quantity)\n  quantity = 1\n  return price * quantity\nend\n\nprint(total_price(4, 3))\n", "2|logic-lua-parameter-ignored|`quantity` is given `1` on line 2 before anything reads it, so the value the caller passes for `quantity` is never used" },
        { "price.ml", "let total_price price quantity =\n  let quantity = 1 in\n  price * quantity\n\nlet () = Printf.printf \"%d\\n\" (total_price 4 3)\n", "2|logic-ocaml-parameter-ignored|`let quantity = 1` on line 2 hides the parameter `quantity` before anything reads it, so the value the caller passes for `quantity` is never used" },
        { "price.php", "<?php\nfunction totalPrice(int $price, int $quantity): int\n{\n    $quantity = 1;\n    return $price * $quantity;\n}\n\necho totalPrice(4, 3), PHP_EOL;\n", "4|logic-php-parameter-ignored|`$quantity` is given `1` on line 4 before anything reads it, so the value the caller passes for `$quantity` is never used" },
        { "price.pl", "use strict;\nuse warnings;\n\nsub total_price {\n    my ($price, $quantity) = @_;\n    $quantity = 1;\n    return $price * $quantity;\n}\n\nprint total_price(4, 3), \"\\n\";\n", "6|logic-perl-parameter-ignored|`$quantity` is given `1` on line 6 before anything reads it, so the value the caller passes for `$quantity` is never used" },
        { "price.ps1", "function Get-TotalPrice($price, $quantity) {\n    $quantity = 1\n    return $price * $quantity\n}\n\nGet-TotalPrice 4 3\n", "2|logic-powershell-parameter-ignored|`$quantity` is given `1` on line 2 before anything reads it, so the value the caller passes for `$quantity` is never used" },
        { "price.rb", "def total_price(price, quantity)\n  quantity = 1\n  price * quantity\nend\n\nputs total_price(4, 3)\n", "2|logic-ruby-parameter-ignored|`quantity` is given `1` on line 2 before anything reads it, so the value the caller passes for `quantity` is never used" },
        { "price.ts", "function totalPrice(price: number, quantity: number): number {\n  quantity = 1;\n  return price * quantity;\n}\n\nconsole.log(totalPrice(4, 3));\n", "2|logic-typescript-parameter-ignored|`quantity` is given `1` on line 2 before anything reads it, so the value the caller passes for `quantity` is never used" },
    };

    /// <summary>The file, and a program the check must leave alone: each found program put right, and the near misses.</summary>
    public static TheoryData<string, string> NearMisses => new()
    {
        { "price.dart", "int totalPrice(int price, int quantity) {\n  return price * quantity;\n}\n\nvoid main() {\n  print(totalPrice(4, 3));\n}\n" },
        { "price.fsx", "let totalPrice price quantity =\n    price * quantity\n\nprintfn \"%d\" (totalPrice 4 3)\n" },
        { "price.lua", "local function total_price(price, quantity)\n  return price * quantity\nend\n\nprint(total_price(4, 3))\n" },
        { "price.ml", "let total_price price quantity =\n  price * quantity\n\nlet () = Printf.printf \"%d\\n\" (total_price 4 3)\n" },
        { "price.php", "<?php\nfunction totalPrice(int $price, int $quantity): int\n{\n    return $price * $quantity;\n}\n\necho totalPrice(4, 3), PHP_EOL;\n" },
        { "price.pl", "use strict;\nuse warnings;\n\nsub total_price {\n    my ($price, $quantity) = @_;\n    return $price * $quantity;\n}\n\nprint total_price(4, 3), \"\\n\";\n" },
        { "price.ps1", "function Get-TotalPrice($price, $quantity) {\n    return $price * $quantity\n}\n\nGet-TotalPrice 4 3\n" },
        { "price.rb", "def total_price(price, quantity)\n  price * quantity\nend\n\nputs total_price(4, 3)\n" },
        { "price.ts", "function totalPrice(price: number, quantity: number): number {\n  return price * quantity;\n}\n\nconsole.log(totalPrice(4, 3));\n" },
        { "near.dart", "class Box {\n  int width;\n\n  Box(this.width);\n\n  int widen(int extra) {\n    extra = extra < 0 ? 0 : extra;\n    return width + extra;\n  }\n}\n\nint atLeastOne(int quantity) {\n  if (quantity < 1) {\n    quantity = 1;\n  }\n  return quantity;\n}\n\nString cleaned(String name) {\n  name = name.trim();\n  return name.toUpperCase();\n}\n\nvoid main() {\n  print('${Box(3).widen(-1)} ${atLeastOne(0)} ${cleaned(\"  ann \")}');\n}\n" },
        { "near.fsx", "let cleaned (name: string) =\n    let name = name.Trim()\n    name.ToUpper()\n\nlet atLeastOne quantity =\n    let quantity = if quantity < 1 then 1 else quantity\n    quantity\n\nprintfn \"%s %d\" (cleaned \"  ann \") (atLeastOne 0)\n" },
        { "near.lua", "local function cleaned(name)\n  name = name:match(\"^%s*(.-)%s*$\")\n  return name:upper()\nend\n\nlocal function at_least_one(quantity)\n  if quantity < 1 then\n    quantity = 1\n  end\n  return quantity\nend\n\nlocal function with_default(size)\n  size = size or 1\n  return size\nend\n\nprint(cleaned(\"  ann \"), at_least_one(0), with_default())\n" },
        { "near.ml", "let cleaned name =\n  let name = String.trim name in\n  String.uppercase_ascii name\n\nlet at_least_one quantity =\n  let quantity = if quantity < 1 then 1 else quantity in\n  quantity\n\nlet () = Printf.printf \"%s %d\\n\" (cleaned \"  ann \") (at_least_one 0)\n" },
        { "near.php", "<?php\nfunction cleaned(string $name): string\n{\n    $name = trim($name);\n    return strtoupper($name);\n}\n\nfunction atLeastOne(int $quantity): int\n{\n    if ($quantity < 1) {\n        $quantity = 1;\n    }\n    return $quantity;\n}\n\nfunction clearCounts(array &$counts): void\n{\n    $counts = [];\n}\n\nfunction logged(int $step): int\n{\n    echo \"step $step\", PHP_EOL;\n    $step = 2;\n    return $step;\n}\n\n$counts = [1, 2];\nclearCounts($counts);\necho cleaned(\"  ann \"), \" \", atLeastOne(0), \" \", count($counts), \" \", logged(5), PHP_EOL;\n" },
        { "near.pl", "use strict;\nuse warnings;\n\nsub cleaned {\n    my ($name) = @_;\n    $name =~ s/^\\s+|\\s+$//g;\n    return uc $name;\n}\n\nsub at_least_one {\n    my $quantity = shift;\n    if ($quantity < 1) {\n        $quantity = 1;\n    }\n    return $quantity;\n}\n\nsub logged {\n    my ($step) = @_;\n    print \"step $step\\n\";\n    $step = 2;\n    return $step;\n}\n\nprint cleaned(\"  ann \"), \" \", at_least_one(0), \" \", logged(5), \"\\n\";\n" },
        { "near.ps1", "function Get-Cleaned($name) {\n    $name = $name.Trim()\n    return $name.ToUpper()\n}\n\nfunction Get-AtLeastOne {\n    param([int]$quantity)\n    if ($quantity -lt 1) {\n        $quantity = 1\n    }\n    return $quantity\n}\n\nfunction Reset-Count([ref]$count) {\n    $count.Value = 0\n}\n\n$total = 5\nReset-Count ([ref]$total)\nGet-Cleaned \"  ann \"\nGet-AtLeastOne 0\n$total\n" },
        { "near.rb", "def cleaned(name)\n  name = name.strip\n  name.upcase\nend\n\ndef at_least_one(quantity)\n  quantity = 1 if quantity < 1\n  quantity\nend\n\ndef logged(step)\n  puts \"step #{step}\"\n  step = 2\n  step\nend\n\ndef each_twice(items, &block)\n  block = proc { |item| item }\n  items.map { |item| block.call(item) * 2 }\nend\n\np cleaned(\"  ann \"), at_least_one(0), logged(5), each_twice([1, 2]) { |item| item }\n" },
        { "near.ts", "function cleaned(name: string): string {\n  name = name.trim();\n  return name.toUpperCase();\n}\n\nfunction atLeastOne(quantity: number): number {\n  if (quantity < 1) {\n    quantity = 1;\n  }\n  return quantity;\n}\n\nfunction logged(step: number): number {\n  console.log(`step ${step}`);\n  step = 2;\n  return step;\n}\n\nconsole.log(cleaned(\"  ann \"), atLeastOne(0), logged(5));\n" },
        { "price.js", "function totalPrice(price, quantity) {\n  quantity = 1;\n  return price * quantity;\n}\n\nconsole.log(totalPrice(4, 3));\n" },
    };

    [Theory]
    [MemberData(nameof(Found))]
    public void AParameterGivenAValueBeforeAnythingReadsItIsFound(string fileName, string code, string said)
    {
        Assert.Equal([said], Scanned(fileName, code).Select(finding => $"{finding.Line}|{finding.PatternId}|{finding.Message}"));
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public void AParameterReadFirstOrGivenToTheCallerIsLeftAlone(string fileName, string code)
    {
        Assert.Empty(Scanned(fileName, code));
    }
}
