using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Mistakes made the same way in many languages - =+ for +=, an odd number looked for with % 2 == 1, a date written with the
/// wrong letters, a number asked as a condition where 0 is true, and a misspelt name given a value: found where they are,
/// with the line each check's change writes, and left alone where the code means what it says.
/// </summary>
public class CrossLanguageMistakeTests : IDisposable
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
    // =+ and =- for += and -=.
    [InlineData("Main.java", "logic-plus-equals-reversed", "class Main {\n    static int total;\n    static void add(int mark) {\n        total =+ mark;\n    }\n}\n", 4, "        total += mark;")]
    [InlineData("main.c", "logic-plus-equals-reversed", "int total;\nvoid take(int mark) {\n    total =- mark;\n}\n", 3, "    total -= mark;")]
    [InlineData("app.js", "logic-plus-equals-reversed", "let total = 0;\nfunction add(mark) {\n    total=+mark;\n}\n", 3, "    total += mark;")]
    [InlineData("Program.cs", "logic-plus-equals-reversed", "int[] counts = new int[3];\ncounts[0] =+ 2;\n", 2, "counts[0] += 2;")]
    [InlineData("main.go", "logic-go-plus-equals-reversed", "package main\n\nvar total int\n\nfunc add(mark int) {\n\ttotal =+ mark\n}\n", 6, "\ttotal += mark")]
    [InlineData("app.py", "logic-python-plus-equals-reversed", "total = 0\nfor mark in [5, 10]:\n    total =+ mark\n", 3, "    total += mark")]
    [InlineData("main.rb", "logic-ruby-plus-equals-reversed", "def add(mark)\n  @total =+ mark\nend\n", 2, "  @total += mark")]
    [InlineData("main.php", "logic-php-plus-equals-reversed", "<?php\n$totals = [0];\n$totals[0] =+ 5;\n", 3, "$totals[0] += 5;")]
    [InlineData("main.ps1", "logic-powershell-plus-equals-reversed", "$total = 0\n$total =- 5\n", 2, "$total -= 5")]
    // An odd number looked for with % 2 == 1, and an even one with != 1.
    [InlineData("Main.java", "logic-odd-test-negative", "class Main {\n    boolean odd(int n) { return n % 2 == 1; }\n}\n", 2, "    boolean odd(int n) { return n % 2 != 0; }")]
    [InlineData("main.c", "logic-odd-test-negative", "int even(int x) {\n    return x % 2 != 1;\n}\n", 2, "    return x % 2 == 0;")]
    [InlineData("app.js", "logic-odd-test-negative", "function odd(n) {\n    return n % 2 === 1;\n}\n", 2, "    return n % 2 !== 0;")]
    [InlineData("Program.cs", "logic-odd-test-negative", "static bool OddSum(int a, int b) => (a + b) % 2 == 1;\n", 1, "static bool OddSum(int a, int b) => (a + b) % 2 != 0;")]
    [InlineData("main.go", "logic-go-odd-test-negative", "package main\n\nfunc odd(n int) bool {\n\treturn n%2 == 1\n}\n", 4, "\treturn n%2 != 0")]
    [InlineData("main.rs", "logic-rust-odd-test-negative", "fn odd(n: i32) -> bool {\n    n % 2 == 1\n}\n", 2, "    n % 2 != 0")]
    [InlineData("main.scala", "logic-scala-odd-test-negative", "def odd(n: Int) = n % 2 == 1\n", 1, "def odd(n: Int) = n % 2 != 0")]
    [InlineData("main.php", "logic-php-odd-test-negative", "<?php\nfunction odd($n) { return $n % 2 == 1; }\n", 2, "function odd($n) { return $n % 2 != 0; }")]
    [InlineData("main.f90", "logic-fortran-odd-test-negative", "logical function odd(n)\n  integer :: n\n  odd = mod(n, 2) .eq. 1\nend function odd\n", 3, "  odd = mod(n, 2) .ne. 0")]
    [InlineData("main.erl", "logic-erlang-odd-test-negative", "-module(main).\n-export([odd/1]).\nodd(N) -> N rem 2 =:= 1.\n", 3, "odd(N) -> N rem 2 =/= 0.")]
    [InlineData("main.exs", "logic-elixir-odd-test-negative", "odd = fn n -> rem(n, 2) == 1 end\n", 1, "odd = fn n -> rem(n, 2) != 0 end")]
    [InlineData("main.fsx", "logic-fsharp-odd-test-negative", "let odd n = n % 2 = 1\n", 1, "let odd n = n % 2 <> 0")]
    [InlineData("main.ps1", "logic-powershell-odd-test-negative", "function Test-Odd($n) { $n % 2 -eq 1 }\n", 1, "function Test-Odd($n) { $n % 2 -ne 0 }")]
    // A date written with the wrong letters.
    [InlineData("app.py", "logic-python-date-format-minutes", "from datetime import datetime\nprint(datetime.now().strftime(\"%d/%M/%Y\"))\n", 2, "print(datetime.now().strftime(\"%d/%m/%Y\"))")]
    [InlineData("main.rb", "logic-ruby-date-format-minutes", "puts Time.now.strftime('%Y%M%d')\n", 1, "puts Time.now.strftime('%Y%m%d')")]
    [InlineData("main.pl", "logic-perl-date-format-minutes", "use POSIX qw(strftime);\nprint strftime(\"%d-%M-%Y\", localtime);\n", 2, "print strftime(\"%d-%m-%Y\", localtime);")]
    [InlineData("main.exs", "logic-elixir-date-format-minutes", "IO.puts(Calendar.strftime(DateTime.utc_now(), \"%d.%M.%Y\"))\n", 1, "IO.puts(Calendar.strftime(DateTime.utc_now(), \"%d.%m.%Y\"))")]
    [InlineData("Main.java", "logic-java-date-pattern-letters", "class Main {\n    Object pattern = java.time.format.DateTimeFormatter.ofPattern(\"dd/MM/YYYY\");\n}\n", 2, "    Object pattern = java.time.format.DateTimeFormatter.ofPattern(\"dd/MM/yyyy\");")]
    [InlineData("Main.java", "logic-java-date-pattern-letters", "class Main {\n    Object pattern = java.time.format.DateTimeFormatter.ofPattern(\"YYYY-MM-DD\");\n}\n", 2, "    Object pattern = java.time.format.DateTimeFormatter.ofPattern(\"yyyy-MM-dd\");")]
    [InlineData("Main.java", "logic-java-date-pattern-letters", "class Main {\n    Object pattern = new java.text.SimpleDateFormat(\"dd.mm.yyyy\");\n}\n", 2, "    Object pattern = new java.text.SimpleDateFormat(\"dd.MM.yyyy\");")]
    [InlineData("Program.cs", "logic-csharp-date-format-letters", "Console.WriteLine(DateTime.Now.ToString(\"dd-mm-yyyy\"));\n", 1, "Console.WriteLine(DateTime.Now.ToString(\"dd-MM-yyyy\"));")]
    [InlineData("Program.cs", "logic-csharp-date-format-letters", "var when = DateTime.Now;\nConsole.WriteLine($\"Today is {when:YYYY-MM-DD}\");\n", 2, "Console.WriteLine($\"Today is {when:yyyy-MM-dd}\");")]
    [InlineData("main.fsx", "logic-fsharp-date-format-letters", "printfn \"%s\" (System.DateTime.Now.ToString(\"yyyy-mm-dd\"))\n", 1, "printfn \"%s\" (System.DateTime.Now.ToString(\"yyyy-MM-dd\"))")]
    [InlineData("main.ps1", "logic-powershell-date-format-letters", "$today = Get-Date -Format \"dd-mm-yyyy\"\n", 1, "$today = Get-Date -Format \"dd-MM-yyyy\"")]
    [InlineData("main.ps1", "logic-powershell-date-format-letters", "Write-Output (\"{0:yyyy-mm-dd}\" -f (Get-Date))\n", 1, "Write-Output (\"{0:yyyy-MM-dd}\" -f (Get-Date))")]
    [InlineData("main.go", "logic-go-date-layout-letters", "package main\n\nimport \"time\"\n\nvar shown = time.Now().Format(\"DD/MM/YYYY\")\n", 5, "var shown = time.Now().Format(\"02/01/2006\")")]
    [InlineData("main.go", "logic-go-date-layout-letters", "package main\n\nimport \"time\"\n\nvar shown = time.Now().Format(\"YYYY-MM-DD HH:MM\")\n", 5, "var shown = time.Now().Format(\"2006-01-02 15:04\")")]
    [InlineData("main.go", "logic-go-date-layout-letters", "package main\n\nimport \"time\"\n\nvar shown = time.Now().Format(\"dd/mm/yyyy\")\n", 5, "var shown = time.Now().Format(\"02/01/2006\")")]
    [InlineData("main.php", "logic-php-date-minutes-as-month", "<?php\necho date(\"d/m/Y H:m\");\n", 2, "echo date(\"d/m/Y H:i\");")]
    // A number asked as a condition, where 0 is true.
    [InlineData("main.rb", "logic-ruby-zero-is-true", "n = 3\nputs 'odd' if n % 2\n", 2, "puts 'odd' if n % 2 != 0")]
    [InlineData("main.rb", "logic-ruby-zero-is-true", "items = []\nunless items.size\n  puts 'none'\nend\n", 2, "unless items.size > 0")]
    [InlineData("main.lua", "logic-lua-zero-is-true", "local n = 3\nwhile n % 2 do\n  n = n + 1\nend\n", 2, "while n % 2 ~= 0 do")]
    [InlineData("main.lua", "logic-lua-zero-is-true", "local items = {}\nif #items then print('some') end\n", 2, "if #items > 0 then print('some') end")]
    [InlineData("main.exs", "logic-elixir-zero-is-true", "items = []\nif Enum.count(items), do: IO.puts(\"some\")\n", 2, "if Enum.count(items) > 0, do: IO.puts(\"some\")")]
    // A misspelt name given a value - a letter left out, and two swapped.
    [InlineData("app.py", "logic-python-misspelt-name", "total = 0\nfor mark in [5, 10]:\n    totl = total + mark\nprint(total)\n", 3, "    total = total + mark")]
    [InlineData("app.py", "logic-python-misspelt-name", "count = 0\nfor word in ['a', 'b']:\n    conut = count + 1\nprint(count)\n", 3, "    count = count + 1")]
    [InlineData("main.lua", "logic-lua-misspelt-name", "local total = 0\nfor _, mark in ipairs({5}) do\n  totl = total + mark\nend\nprint(total)\n", 3, "  total = total + mark")]
    [InlineData("app.js", "logic-js-misspelt-name", "let total = 0;\nfor (const mark of [5]) {\n    totl = total + mark;\n}\nconsole.log(total);\n", 3, "    total = total + mark;")]
    [InlineData("main.php", "logic-php-misspelt-name", "<?php\n$total = 0;\nforeach ([5] as $mark) {\n    $totl = $total + $mark;\n}\necho $total;\n", 4, "    $total = $total + $mark;")]
    [InlineData("main.ps1", "logic-powershell-misspelt-name", "$total = 0\nforeach ($mark in 5, 10) {\n    $totl = $total + $mark\n}\n$total\n", 3, "    $total = $total + $mark")]
    public void AMistakeIsFoundWithItsChange(string fileName, string check, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    // A plus or minus sign written with a space after the =, a negative number given, =++, and += itself.
    [InlineData("app.py", "logic-python-plus-equals-reversed", "x = +5\ny =-1\ny += 2\n")]
    [InlineData("main.c", "logic-plus-equals-reversed", "int x;\nvoid set(void) {\n    x = -1;\n    x =-1;\n    x += 2;\n}\n")]
    [InlineData("app.js", "logic-plus-equals-reversed", "let count = 0;\nlet n = 1;\nn =++count;\n")]
    [InlineData("main.php", "logic-php-plus-equals-reversed", "<?php\n$sign = -1;\n$x = +$sign;\n")]
    // A loop counter counting up from 0, a length, an unsigned number, a Go range's position, and != 0 already.
    [InlineData("Main.java", "logic-odd-test-negative", "class Main {\n    void run(int[] xs) {\n        for (int i = 0; i < xs.length; i++) {\n            if (i % 2 == 1) { }\n        }\n    }\n}\n")]
    [InlineData("app.js", "logic-odd-test-negative", "const xs = [1];\nconsole.log(xs.length % 2 === 1);\n")]
    [InlineData("main.c", "logic-odd-test-negative", "unsigned int n = 5;\nint odd(void) { return n % 2 == 1; }\n")]
    [InlineData("main.rs", "logic-rust-odd-test-negative", "fn odd(n: usize) -> bool {\n    n % 2 == 1\n}\n")]
    [InlineData("main.go", "logic-go-odd-test-negative", "package main\n\nfunc f(xs []int) {\n\tfor i := range xs {\n\t\tif i%2 == 1 {\n\t\t\txs[i] = 0\n\t\t}\n\t}\n}\n")]
    [InlineData("Main.java", "logic-odd-test-negative", "class Main {\n    boolean odd(int n) { return n % 2 != 0; }\n}\n")]
    [InlineData("main.f90", "logic-fortran-odd-test-negative", "logical function odd(n)\n  integer :: n\n  odd = modulo(n, 2) == 1\nend function odd\n")]
    [InlineData("main.ps1", "logic-powershell-odd-test-negative", "foreach ($i in 0..9) { if ($i % 2 -eq 1) { $i } }\n")]
    // Minutes beside an hour, the month written as the month, a week's year with its week, and formats that are not dates.
    [InlineData("app.py", "logic-python-date-format-minutes", "from datetime import datetime\nnow = datetime.now()\nprint(now.strftime(\"%H:%M\"), now.strftime(\"%Y-%m-%d %H:%M\"))\n")]
    [InlineData("Main.java", "logic-java-date-pattern-letters", "class Main {\n    Object week = java.time.format.DateTimeFormatter.ofPattern(\"YYYY-ww\");\n    Object time = java.time.format.DateTimeFormatter.ofPattern(\"yyyy-MM-dd HH:mm\");\n}\n")]
    [InlineData("Program.cs", "logic-csharp-date-format-letters", "var when = DateTime.Now;\nConsole.WriteLine(3.5.ToString(\"0.00\") + when.ToString(\"HH:mm\") + when.ToString(\"D\") + when.ToString(\"yyyy-MM-dd\"));\n")]
    [InlineData("main.go", "logic-go-date-layout-letters", "package main\n\nimport \"time\"\n\nvar shown = time.Now().Format(\"2006-01-02 15:04\")\n")]
    [InlineData("main.php", "logic-php-date-minutes-as-month", "<?php\necho date(\"H:i\"), date(\"Y-m-d\");\n")]
    // Numbers compared, and a value that can be nil.
    [InlineData("main.rb", "logic-ruby-zero-is-true", "n = 3\nitems = []\nputs 'even' if n % 2 == 0\nputs 'none' if items.empty?\nputs 'odd' if n.odd?\n")]
    [InlineData("main.lua", "logic-lua-zero-is-true", "local n, items, name = 3, {}, nil\nif n % 2 == 0 then print('even') end\nif #items > 0 then print('some') end\nif name then print(name) end\n")]
    [InlineData("main.exs", "logic-elixir-zero-is-true", "n = 3\nitems = []\nif rem(n, 2) == 0, do: IO.puts(\"even\")\nif items != [], do: IO.puts(\"some\")\n")]
    // The name read later, a name far from any other, one apart only by a digit, a keyword argument, the name in text, and a declaration.
    [InlineData("app.py", "logic-python-misspelt-name", "total = 0\nfor mark in [1]:\n    totl = total + mark\n    print(totl)\n")]
    [InlineData("app.py", "logic-python-misspelt-name", "total = 0\nfor mark in [1]:\n    subtotal = total + mark\n")]
    [InlineData("app.py", "logic-python-misspelt-name", "total = 0\ntotal2 = total + 1\n")]
    [InlineData("app.py", "logic-python-misspelt-name", "color = 'red'\nprint(\n    colour=color,\n)\n")]
    [InlineData("app.py", "logic-python-misspelt-name", "total = 0\ntotl = total + 1\nprint(f\"{totl}\")\n")]
    [InlineData("app.js", "logic-js-misspelt-name", "let total = 0;\nlet totl = total + 1;\n")]
    [InlineData("main.ps1", "logic-powershell-misspelt-name", "$Total = 0\n$total = $Total + 1\n")]
    public void CodeThatMeansWhatItSaysIsLeftAlone(string fileName, string check, string code) => Assert.Empty(Scan(fileName, code, check));

    [Theory]
    [InlineData("total", "totl", true)]
    [InlineData("count", "conut", true)]
    [InlineData("marks", "mark", true)]
    [InlineData("total", "total2", false)]
    [InlineData("sum", "sun", false)]
    [InlineData("total", "subtotal", false)]
    [InlineData("count", "cnt", false)]
    public void NamesOneLetterApartAreTold(string first, string second, bool apart) => Assert.Equal(apart, MisspeltNamePatterns.OneLetterApart(first, second));
}
