using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Code no run of a program can reach, so no run can test it - a branch an earlier one takes every value from, a loop that
/// counts the wrong way and runs no times, code after return, a case or a handler never matched, a function defined twice, and
/// Python's misspelt __main__: found where it is, with the change each check writes, and left alone where the code can run.
/// </summary>
public class NeverRunningCodeTests : IDisposable
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
    // A ladder of >= written lowest first, of < written highest first, and of ranges; each put in order.
    [InlineData("app.py", "logic-python-branch-never-reached", "mark = 75\nif mark >= 40:\n    grade = 'pass'\nelif mark >= 70:\n    grade = 'distinction'\nelif mark >= 55:\n    grade = 'merit'\n", 4,
        "if mark >= 70:|    grade = 'distinction'|elif mark >= 55:|    grade = 'merit'|elif mark >= 40:|    grade = 'pass'")]
    [InlineData("app.py", "logic-python-branch-never-reached", "age = 9\nif age < 18:\n    print('minor')\nelif age < 13:\n    print('child')\n", 4,
        "if age < 13:|    print('child')|elif age < 18:|    print('minor')")]
    [InlineData("Main.java", "logic-branch-never-reached", "class Main {\n    String size(int n) {\n        if (n > 10) {\n            return \"big\";\n        } else if (n > 100) {\n            return \"huge\";\n        }\n        return \"small\";\n    }\n}\n", 5,
        "        if (n > 100) {|            return \"huge\";|        } else if (n > 10) {|            return \"big\";")]
    [InlineData("Program.cs", "logic-branch-never-reached", "int n = 5;\nif (n >= 1)\n{\n    Console.WriteLine(\"one or more\");\n}\nelse if (n >= 3)\n{\n    Console.WriteLine(\"three or more\");\n}\n", 6,
        "if (n >= 3)|{|    Console.WriteLine(\"three or more\");|}|else if (n >= 1)|{|    Console.WriteLine(\"one or more\");|}")]
    [InlineData("main.go", "logic-go-branch-never-reached", "package main\n\nfunc size(n int) string {\n\tif n > 1 {\n\t\treturn \"some\"\n\t} else if n > 5 {\n\t\treturn \"many\"\n\t}\n\treturn \"none\"\n}\n", 6,
        "\tif n > 5 {|\t\treturn \"many\"|\t} else if n > 1 {|\t\treturn \"some\"")]
    [InlineData("main.erl", "logic-erlang-branch-never-reached", "-module(main).\n-export([size/1]).\nsize(N) ->\n    if\n        N > 1 -> some;\n        N > 5 -> many;\n        true -> none\n    end.\n", 6,
        "        N > 5 -> many;|        N > 1 -> some;")]
    // Loops that count up from past their end.
    [InlineData("app.py", "logic-python-loop-never-runs", "for i in range(5, 0):\n    print(i)\n", 1, "for i in range(5, 0, -1):")]
    [InlineData("main.rb", "logic-ruby-loop-never-runs", "for i in 5..1\n  puts i\nend\n", 1, "for i in 5.downto(1)")]
    [InlineData("main.rb", "logic-ruby-loop-never-runs", "(5...0).each { puts 'tick' }\n", 1, "5.downto(1).each { puts 'tick' }")]
    [InlineData("main.rs", "logic-rust-loop-never-runs", "fn main() {\n    for i in 5..=1 {\n        println!(\"{}\", i);\n    }\n}\n", 2, "    for i in (1..=5).rev() {")]
    [InlineData("main.scala", "logic-scala-loop-never-runs", "for i <- 5 until 0 do println(i)\n", 1, "for i <- 5 until 0 by -1 do println(i)")]
    [InlineData("main.lua", "logic-lua-loop-never-runs", "for i = 5, 1 do\n  print(i)\nend\n", 1, "for i = 5, 1, -1 do")]
    [InlineData("main.fsx", "logic-fsharp-loop-never-runs", "for i = 5 to 1 do printfn \"%d\" i\n", 1, "for i = 5 downto 1 do printfn \"%d\" i")]
    [InlineData("main.pl", "logic-perl-loop-never-runs", "foreach my $i (5..1) {\n    print $i;\n}\n", 1, "foreach my $i (reverse(1..5)) {")]
    [InlineData("main.f90", "logic-fortran-loop-never-runs", "program p\n  integer :: i\n  do i = 5, 1\n    print *, i\n  end do\nend program p\n", 3, "  do i = 5, 1, -1")]
    // Python's misspelt __main__.
    [InlineData("app.py", "logic-python-main-guard-misspelt", "def main():\n    pass\n\nif __name__ == '__main':\n    main()\n", 4, "if __name__ == '__main__':")]
    public void AMistakeIsFoundWithItsChange(string fileName, string check, string code, int line, string changedTo)
    {
        // A chain can hold more than one branch that never runs; each is found, with the one change for the chain.
        var found = Assert.Single(Scan(fileName, code, check), finding => finding.Line == line);

        Assert.Equal(changedTo.Split('|'), found.Fix!.NewLines);
    }

    [Theory]
    // A handler moved before the one that took its errors.
    [InlineData("app.py", "logic-python-handler-never-reached", "try:\n    open('x')\nexcept OSError:\n    print('os')\nexcept FileNotFoundError:\n    print('missing')\n", 5, 3,
        "except FileNotFoundError:|    print('missing')|except OSError:|    print('os')")]
    [InlineData("app.py", "logic-python-handler-never-reached", "class AppError(Exception):\n    pass\n\n\ntry:\n    pass\nexcept Exception:\n    pass\nexcept AppError:\n    pass\n", 9, 7,
        "except AppError:|    pass|except Exception:|    pass")]
    [InlineData("main.rb", "logic-ruby-handler-never-reached", "begin\n  [].fetch(1)\nrescue\n  puts 'any'\nrescue IndexError\n  puts 'index'\nend\n", 5, 3,
        "rescue IndexError|  puts 'index'|rescue|  puts 'any'")]
    [InlineData("main.php", "logic-php-handler-never-reached", "<?php\ntry {\n    f();\n} catch (LogicException $e) {\n    echo 'logic';\n} catch (DomainException $e) {\n    echo 'domain';\n}\n", 6, 4,
        "} catch (DomainException $e) {|    echo 'domain';|} catch (LogicException $e) {|    echo 'logic';")]
    [InlineData("main.dart", "logic-dart-handler-never-reached", "void main() {\n  try {\n    f();\n  } on Exception {\n    print('a');\n  } on FormatException catch (e) {\n    print('b');\n  }\n}\n", 6, 4,
        "  } on FormatException catch (e) {|    print('b');|  } on Exception {|    print('a');")]
    [InlineData("main.cpp", "logic-cpp-handler-never-reached", "int main() {\n    try {\n        f();\n    } catch (const std::logic_error& e) {\n        g();\n    } catch (const std::invalid_argument& e) {\n        h();\n    }\n}\n", 6, 4,
        "    } catch (const std::invalid_argument& e) {|        h();|    } catch (const std::logic_error& e) {|        g();")]
    public void AHandlerThatCanNeverRunIsMovedBeforeTheOneThatTookItsErrors(string fileName, string check, string code, int line, int startLine, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.Equal(startLine, found.Fix!.StartLine);
        Assert.Equal(changedTo.Split('|'), found.Fix.NewLines);
    }

    [Theory]
    // A chain on two names, which cannot be put in one order.
    [InlineData("app.py", "logic-python-branch-never-reached", 6, "x, y = 1, 2\nif x > 5:\n    pass\nelif y > 3:\n    pass\nelif x > 10:\n    pass\n")]
    // Code after return, break, exit and stop.
    [InlineData("main.c", "logic-code-after-return", 4, "int f(void) {\n    int n = 1;\n    return n;\n    n++;\n}\n")]
    [InlineData("app.js", "logic-code-after-return", 4, "for (const n of [1]) {\n    console.log(n);\n    break;\n    console.log('after');\n}\n")]
    [InlineData("main.php", "logic-php-code-after-return", 4, "<?php\nfunction f() {\n    exit(1);\n    echo 'after';\n}\n")]
    [InlineData("main.pl", "logic-perl-code-after-return", 3, "sub f {\n    return 1;\n    print 'after';\n}\n")]
    [InlineData("main.ps1", "logic-powershell-code-after-return", 3, "function F {\n    return 1\n    Write-Output 'after'\n}\n")]
    [InlineData("main.f90", "logic-fortran-code-after-return", 5, "program p\n  integer :: i\n  do i = 1, 3\n    exit\n    print *, i\n  end do\nend program p\n")]
    // Duplicate cases, functions defined twice.
    [InlineData("app.js", "logic-js-duplicate-case", 6, "switch (x) {\n    case 'a':\n        f();\n        break;\n    case 'b':\n    case 'a':\n        g();\n}\n")]
    [InlineData("main.php", "logic-php-duplicate-case", 4, "<?php\nswitch ($x) {\n    case 1: f(); break;\n    case 1: g(); break;\n}\n")]
    [InlineData("app.py", "logic-python-defined-twice", 2, "class Shape:\n    def area(self):\n        return 0\n\n    def area(self):\n        return 1\n")]
    [InlineData("app.js", "logic-js-defined-twice", 2, "console.log(f());\nfunction f() { return 1; }\nfunction f() { return 2; }\n")]
    [InlineData("main.lua", "logic-lua-defined-twice", 1, "function greet() print('a') end\nfunction greet() print('b') end\ngreet()\n")]
    [InlineData("main.ps1", "logic-powershell-defined-twice", 1, "function Say-Hi { 'a' }\nfunction say-hi { 'b' }\nSay-Hi\n")]
    [InlineData("main.rb", "logic-ruby-code-after-return", 3, "def f\n  return 1\n  puts 'after'\nend\n")]
    [InlineData("main.rb", "logic-ruby-defined-twice", 1, "def greet\n  'a'\nend\ndef greet\n  'b'\nend\nputs greet\n")]
    public void CodeThatCanNeverRunIsFound(string fileName, string check, int line, string code) =>
        Assert.Equal(line, Assert.Single(Scan(fileName, code, check)).Line);

    [Theory]
    // Ladders in order, ranges that do not overlap, two names, a test of another kind, and text compared.
    [InlineData("app.py", "logic-python-branch-never-reached", "mark = 75\nif mark >= 70:\n    pass\nelif mark >= 55:\n    pass\nelif mark >= 40:\n    pass\n")]
    [InlineData("app.py", "logic-python-branch-never-reached", "mark = 75\nif 0 <= mark < 40:\n    pass\nelif 40 <= mark < 70:\n    pass\nelif mark >= 70:\n    pass\n")]
    [InlineData("app.py", "logic-python-branch-never-reached", "x, y = 1, 2\nif x > 5:\n    pass\nelif y > 10:\n    pass\nelif x > 3 and y:\n    pass\n")]
    [InlineData("Main.java", "logic-branch-never-reached", "class Main {\n    String size(int n) {\n        if (n > 100) {\n            return \"huge\";\n        } else if (n > 10) {\n            return \"big\";\n        } else if (n == 10) {\n            return \"ten\";\n        }\n        return \"small\";\n    }\n}\n")]
    [InlineData("main.rb", "logic-ruby-branch-never-reached", "name = 'a'\nif name == 'a'\n  puts 1\nelsif name == 'b'\n  puts 2\nend\n")]
    [InlineData("main.go", "logic-go-branch-never-reached", "package main\n\nfunc size(n int) string {\n\tif n < 0 {\n\t\treturn \"negative\"\n\t} else if n > 0 {\n\t\treturn \"positive\"\n\t}\n\treturn \"zero\"\n}\n")]
    // Loops that count down already, or up as written.
    [InlineData("app.py", "logic-python-loop-never-runs", "for i in range(5, 0, -1):\n    print(i)\nfor j in range(0, 5):\n    print(j)\n")]
    [InlineData("main.rs", "logic-rust-loop-never-runs", "fn main() {\n    for i in (1..=5).rev() {\n        println!(\"{}\", i);\n    }\n    for j in (0..5) {\n        println!(\"{}\", j);\n    }\n}\n")]
    [InlineData("main.scala", "logic-scala-loop-never-runs", "for i <- 5 to 1 by -1 do println(i)\nfor j <- 1 to 5 do println(j)\n")]
    [InlineData("main.lua", "logic-lua-loop-never-runs", "for i = 5, 1, -1 do print(i) end\nfor j = 1, 5 do print(j) end\n")]
    // A return that is a condition's, a label after return, a function set up after it, and a closing brace.
    [InlineData("main.pl", "logic-perl-code-after-return", "sub f {\n    return 1 if $_[0];\n    print 'after';\n}\n")]
    [InlineData("main.c", "logic-code-after-return", "int f(int n) {\n    if (n) goto done;\n    return 1;\ndone:\n    return 0;\n}\n")]
    [InlineData("app.js", "logic-code-after-return", "function f() {\n    return helper();\n    function helper() { return 1; }\n}\n")]
    [InlineData("main.php", "logic-php-code-after-return", "<?php\nfunction f($x) {\n    switch ($x) {\n        case 1:\n            return 'one';\n        case 2:\n            return 'two';\n    }\n}\n")]
    // Cases each their own, functions with different names, a property's setter, alternatives in if and else, and a call in between.
    [InlineData("app.js", "logic-js-duplicate-case", "switch (x) {\n    case 1:\n        break;\n    case 2:\n        break;\n}\n")]
    [InlineData("app.py", "logic-python-defined-twice", "class Box:\n    @property\n    def size(self):\n        return 1\n\n    @size.setter\n    def size(self, value):\n        pass\n")]
    [InlineData("app.py", "logic-python-defined-twice", "import sys\nif sys.platform == 'win32':\n    def clear():\n        pass\nelse:\n    def clear():\n        pass\n")]
    [InlineData("app.py", "logic-python-defined-twice", "def show():\n    print(1)\nshow()\ndef show():\n    print(2)\nshow()\n")]
    [InlineData("main.lua", "logic-lua-defined-twice", "function greet() print('a') end\ngreet()\nfunction greet() print('b') end\ngreet()\n")]
    // Handlers in order, and errors that do not come one from the other.
    [InlineData("app.py", "logic-python-handler-never-reached", "try:\n    pass\nexcept ValueError:\n    pass\nexcept Exception:\n    pass\nexcept KeyboardInterrupt:\n    pass\n")]
    [InlineData("main.rb", "logic-ruby-handler-never-reached", "begin\n  x\nrescue ArgumentError\n  puts 1\nrescue StandardError\n  puts 2\nend\n")]
    [InlineData("main.php", "logic-php-handler-never-reached", "<?php\ntry {\n    f();\n} catch (Exception $e) {\n    echo 1;\n} catch (TypeError $e) {\n    echo 2;\n}\n")]
    // The guard as Python writes it, and __name__ compared with another module's name.
    [InlineData("app.py", "logic-python-main-guard-misspelt", "if __name__ == \"__main__\":\n    pass\nif __name__ == 'tools':\n    pass\n")]
    // A return that is a condition's, a loop with its step, handlers in order, and methods of one name in two classes.
    [InlineData("main.rb", "logic-ruby-code-after-return", "def f(x)\n  return 1 if x\n  puts 'after'\nend\n")]
    [InlineData("main.f90", "logic-fortran-loop-never-runs", "program p\n  integer :: i\n  do i = 5, 1, -1\n    print *, i\n  end do\n  do i = 1, 5\n    print *, i\n  end do\nend program p\n")]
    [InlineData("main.dart", "logic-dart-handler-never-reached", "void main() {\n  try {\n    f();\n  } on FormatException {\n    print('a');\n  } on Exception {\n    print('b');\n  }\n}\n")]
    [InlineData("main.cpp", "logic-cpp-handler-never-reached", "int main() {\n    try {\n        f();\n    } catch (const std::invalid_argument& e) {\n        g();\n    } catch (const std::exception& e) {\n        h();\n    } catch (...) {\n        k();\n    }\n}\n")]
    [InlineData("main.rb", "logic-ruby-defined-twice", "class Square\n  def area\n    1\n  end\nend\n\nclass Circle\n  def area\n    2\n  end\nend\n")]
    public void CodeThatCanRunIsLeftAlone(string fileName, string check, string code) => Assert.Empty(Scan(fileName, code, check));

    /// <summary>
    /// The checks on a thread of their own, so one that never finishes fails the test at the time limit instead of holding up
    /// the run. A thread, not a task: a task may wait for the thread pool, which a run of many tests can keep busy.
    /// </summary>
    private IReadOnlyList<LogicFinding> ScanWithin(TimeSpan limit, string fileName, string code, string check)
    {
        IReadOnlyList<LogicFinding>? found = null;
        Exception? failed = null;
        var scanning = new Thread(() =>
        {
            try { found = Scan(fileName, code, check); }
            catch (Exception ex) { failed = ex; }
        }) { IsBackground = true };

        scanning.Start();
        Assert.True(scanning.Join(limit), $"The checks were still reading {fileName} after {limit.TotalSeconds} s");
        if (failed is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed).Throw();
        return found!;
    }

    /// <summary>prog_emit from TruthTableSolver's ttsolve.c, as it is written there.</summary>
    private const string ProgEmit = """
        static int prog_emit(Prog *p, int op, int a, int b, int c) {
            if (op >= I_AND0 && op <= I_AND3 && a > b) {
                int f = op - I_AND0;
                op = I_AND0 + (((f & 1) << 1) | ((f >> 1) & 1));
                int t = a; a = b; b = t;
            } else if (op == I_XOR && a > b) { int t = a; a = b; b = t; }
            if (p->htsize == 0 || p->ncode * 2 >= p->htsize) {
                int ns = p->htsize ? p->htsize * 2 : 256;
                free(p->ht);
                p->ht = xmalloc((size_t)ns * sizeof(int));
                for (int i = 0; i < ns; i++) p->ht[i] = -1;
                p->htsize = ns;
                for (int i = 0; i < p->ncode; i++) {
                    unsigned h = ins_hash(p->code[i].op, p->code[i].a, p->code[i].b, p->code[i].c) & (unsigned)(ns - 1);
                    while (p->ht[h] >= 0) h = (h + 1) & (unsigned)(ns - 1);
                    p->ht[h] = i;
                }
            }
            unsigned h = ins_hash(op, a, b, c) & (unsigned)(p->htsize - 1);
            while (p->ht[h] >= 0) {
                const Ins *x = &p->code[p->ht[h]];
                if (x->op == op && x->a == a && x->b == b && x->c == c) return x->d;
                h = (h + 1) & (unsigned)(p->htsize - 1);
            }
            if (p->ncode == p->cap) { p->cap = p->cap ? p->cap * 2 : 128; p->code = xrealloc(p->code, (size_t)p->cap * sizeof(Ins)); }
            Ins x = {op, p->nregs++, a, b, c};
            p->code[p->ncode] = x;
            p->ht[h] = p->ncode++;
            return x.d;
        }
        """;

    /// <summary>
    /// Chains with a branch whose block opens and closes on its own } else if line, by name: the kind of file, the check, the
    /// code, and the line of the branch found never to run, or 0 for none. Read as the } that closes that branch, the } the
    /// line starts with gave the same header again and again, and the check never finished. Kept out of InlineData: the
    /// robustness tests run every check on each InlineData row's code while xunit is finding the tests, where a check that
    /// never finishes holds up the whole run instead of failing one test at its time limit.
    /// </summary>
    private static readonly Dictionary<string, (string File, string Check, string Code, int Line)> ClosedOnTheirOwnLine = new()
    {
        // Neither condition is one comparison of a name with a number, so nothing is found.
        ["ttsolve.c prog_emit"] = ("ttsolve.c", "logic-branch-never-reached", ProgEmit, 0),
        // Found, and not put in order: that branch's block has no } line of its own to close the block moved into its place.
        ["c"] = ("main.c", "logic-branch-never-reached", "int f(int n) {\n    int r = 0;\n    if (n > 10) {\n        r = 1;\n    } else if (n > 100) { r = 2; }\n    return r;\n}\n", 5),
        // The chain goes on past it, to the next line.
        ["java, a branch after it"] = ("Main.java", "logic-branch-never-reached",
            "class Main {\n    int f(int n) {\n        int r = 0;\n        if (n > 100) {\n            r = 1;\n        } else if (n > 10) { r = 2; }\n        else if (n > 50) { r = 3; }\n        else { r = 4; }\n        return r;\n    }\n}\n", 7),
        // Go's and Rust's, with another else if after it on that line: the chain ends there, the branches before it read.
        ["go"] = ("main.go", "logic-go-branch-never-reached",
            "package main\n\nfunc size(n int) int {\n\tif n > 1 {\n\t\treturn 1\n\t} else if n > 5 {\n\t\treturn 5\n\t} else if n > 7 { return 7 } else if n > 3 {\n\t\treturn 3\n\t}\n\treturn 0\n}\n", 6),
        ["rust"] = ("main.rs", "logic-rust-branch-never-reached",
            "fn size(n: i32) -> i32 {\n    if n > 1 {\n        1\n    } else if n > 5 {\n        5\n    } else if n > 7 { 7 } else if n > 3 {\n        3\n    } else {\n        0\n    }\n}\n", 4),
    };

    [Theory]
    [InlineData("ttsolve.c prog_emit")]
    [InlineData("c")]
    [InlineData("java, a branch after it")]
    [InlineData("go")]
    [InlineData("rust")]
    public void AChainWithABranchClosedOnItsOwnLineIsReadInTime(string name)
    {
        var (fileName, check, code, line) = ClosedOnTheirOwnLine[name];

        var found = ScanWithin(TimeSpan.FromSeconds(10), fileName, code, check);

        if (line == 0)
        {
            Assert.Empty(found);
            return;
        }
        var finding = Assert.Single(found);
        Assert.Equal(line, finding.Line);
        Assert.Null(finding.Fix);
    }

    [Theory]
    [InlineData("score >= 70", "score", 70, true, double.PositiveInfinity, false)]
    [InlineData("50 <= score < 70", "score", 50, true, 70, false)]
    [InlineData("(score > 10 && score <= 20)", "score", 10, false, 20, true)]
    [InlineData("$n -lt 5", "$n", double.NegativeInfinity, false, 5, false)]
    [InlineData("N =:= 3", "N", 3, true, 3, true)]
    public void AConditionIsReadAsTheValuesItLetsThrough(string condition, string name, double low, bool lowIn, double high, bool highIn) =>
        Assert.Equal(new UnreachableBranchPatterns.Range(name, low, lowIn, high, highIn), UnreachableBranchPatterns.ValuesLetThrough(condition));
}
