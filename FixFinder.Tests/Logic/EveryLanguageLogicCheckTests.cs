using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// The same five logic mistakes written in each language as a beginner in it writes them - a largest started at 0, a total reset
/// inside its loop, the other end counted one too far, a changed copy thrown away, decimals compared exactly - each found by
/// the language's own check with its change, and found no more once the change is made. Each row is what the checks gave
/// for the program when these were written.
/// </summary>
public class EveryLanguageLogicCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string language, string fileName, string code, string check)
    {
        var folder = Path.Combine(_temp.Path, language.Replace("#", "sharp").Replace("+", "p"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == check).ToList();
    }

    private static string[] LinesOf(string fixLines) => fixLines.Length == 0 ? [] : fixLines.Split('|');

    /// <summary>The language, the file, the program, the check that finds it, its line, where its change starts, how many lines it replaces, and the lines it writes (split by |; none when it only takes a line out).</summary>
    public static TheoryData<string, string, string, string, int, int, int, string> Mistakes => new()
    {
        { "C", "pasted.c", "#include <stdio.h>\n\nint main(void) {\n    int temperatures[] = {-5, -2, -8};\n    int highest = 0;\n    for (int i = 0; i < 3; i++) {\n        if (temperatures[i] > highest) {\n            highest = temperatures[i];\n        }\n    }\n    printf(\"%d\\n\", highest);\n    return 0;\n}\n", "logic-best-starts-at-zero", 5, 5, 1, "    int highest = temperatures[0];" },
        { "C++", "pasted.cpp", "#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> temperatures = {-5, -2, -8};\n    int highest = 0;\n    for (int t : temperatures) {\n        if (t > highest) {\n            highest = t;\n        }\n    }\n    std::cout << highest << \"\\n\";\n}\n", "logic-best-starts-at-zero", 6, 6, 1, "    int highest = temperatures[0];" },
        { "C#", "pasted.cs", "int[] temperatures = { -5, -2, -8 };\nint highest = 0;\nforeach (int t in temperatures)\n{\n    if (t > highest)\n    {\n        highest = t;\n    }\n}\nConsole.WriteLine(highest);\n", "logic-best-starts-at-zero", 2, 2, 1, "int highest = temperatures[0];" },
        { "Dart", "pasted.dart", "void main() {\n  var temperatures = [-5, -2, -8];\n  var highest = 0;\n  for (var t in temperatures) {\n    if (t > highest) {\n      highest = t;\n    }\n  }\n  print(highest);\n}\n", "logic-dart-best-starts-at-zero", 3, 3, 1, "  var highest = temperatures[0];" },
        { "Elixir", "pasted.exs", "temperatures = [-5, -2, -8]\nhighest = Enum.reduce(temperatures, 0, fn t, best -> if t > best, do: t, else: best end)\nIO.puts(highest)\n", "logic-elixir-best-starts-at-zero", 2, 2, 1, "highest = Enum.reduce(temperatures, hd(temperatures), fn t, best -> if t > best, do: t, else: best end)" },
        { "Erlang", "marks.erl", "-module(marks).\n-export([main/0]).\n\nmain() ->\n    Temperatures = [-5, -2, -8],\n    Highest = lists:foldl(fun(T, Best) when T > Best -> T; (_, Best) -> Best end, 0, Temperatures),\n    io:format(\"~p~n\", [Highest]).\n", "logic-erlang-best-starts-at-zero", 6, 6, 1, "    Highest = lists:foldl(fun(T, Best) when T > Best -> T; (_, Best) -> Best end, hd(Temperatures), Temperatures)," },
        { "Fortran", "pasted.f90", "program marks\n  implicit none\n  integer :: temperatures(3) = [-5, -2, -8]\n  integer :: highest, i\n  highest = 0\n  do i = 1, 3\n    if (temperatures(i) > highest) then\n      highest = temperatures(i)\n    end if\n  end do\n  print *, highest\nend program marks\n", "logic-fortran-best-starts-at-zero", 5, 5, 1, "  highest = temperatures(1)" },
        { "F#", "pasted.fsx", "let temperatures = [ -5; -2; -8 ]\nlet mutable highest = 0\nfor t in temperatures do\n    if t > highest then\n        highest <- t\nprintfn \"%d\" highest\n", "logic-fsharp-best-starts-at-zero", 2, 2, 1, "let mutable highest = temperatures.[0]" },
        { "Go", "pasted.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\ttemperatures := []int{-5, -2, -8}\n\thighest := 0\n\tfor _, t := range temperatures {\n\t\tif t > highest {\n\t\t\thighest = t\n\t\t}\n\t}\n\tfmt.Println(highest)\n}\n", "logic-go-best-starts-at-zero", 7, 7, 1, "\thighest := temperatures[0]" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        int[] temperatures = {-5, -2, -8};\n        int highest = 0;\n        for (int t : temperatures) {\n            if (t > highest) {\n                highest = t;\n            }\n        }\n        System.out.println(highest);\n    }\n}\n", "logic-java-best-starts-at-zero", 4, 4, 1, "        int highest = temperatures[0];" },
        { "JavaScript", "pasted.js", "const temperatures = [-5, -2, -8];\nlet highest = 0;\nfor (const t of temperatures) {\n    if (t > highest) {\n        highest = t;\n    }\n}\nconsole.log(highest);\n", "logic-best-starts-at-zero", 2, 2, 1, "let highest = temperatures[0];" },
        { "Lua", "pasted.lua", "local temperatures = {-5, -2, -8}\nlocal highest = 0\nfor _, t in ipairs(temperatures) do\n  if t > highest then\n    highest = t\n  end\nend\nprint(highest)\n", "logic-lua-best-starts-at-zero", 2, 2, 1, "local highest = temperatures[1]" },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nmy @temperatures = (-5, -2, -8);\nmy $highest = 0;\nforeach my $t (@temperatures) {\n    if ($t > $highest) {\n        $highest = $t;\n    }\n}\nprint \"$highest\\n\";\n", "logic-perl-best-starts-at-zero", 5, 5, 1, "my $highest = $temperatures[0];" },
        { "PHP", "pasted.php", "<?php\n$temperatures = [-5, -2, -8];\n$highest = 0;\nforeach ($temperatures as $t) {\n    if ($t > $highest) {\n        $highest = $t;\n    }\n}\necho $highest, PHP_EOL;\n", "logic-php-best-starts-at-zero", 3, 3, 1, "$highest = $temperatures[0];" },
        { "PowerShell", "pasted.ps1", "$temperatures = @(-5, -2, -8)\n$highest = 0\nforeach ($t in $temperatures) {\n    if ($t -gt $highest) {\n        $highest = $t\n    }\n}\nWrite-Output $highest\n", "logic-powershell-best-starts-at-zero", 2, 2, 1, "$highest = $temperatures[0]" },
        { "Python", "pasted.py", "temperatures = [-5, -2, -8]\nhighest = 0\nfor t in temperatures:\n    if t > highest:\n        highest = t\nprint(highest)\n", "logic-python-best-starts-at-zero", 2, 2, 1, "highest = temperatures[0]" },
        { "Ruby", "pasted.rb", "temperatures = [-5, -2, -8]\nhighest = 0\ntemperatures.each do |t|\n  highest = t if t > highest\nend\nputs highest\n", "logic-ruby-best-starts-at-zero", 2, 2, 1, "highest = temperatures[0]" },
        { "Rust", "pasted.rs", "fn main() {\n    let temperatures = vec![-5, -2, -8];\n    let mut highest = 0;\n    for t in &temperatures {\n        if *t > highest {\n            highest = *t;\n        }\n    }\n    println!(\"{}\", highest);\n}\n", "logic-rust-best-starts-at-zero", 3, 3, 1, "    let mut highest = temperatures[0];" },
        { "Scala", "pasted.scala", "@main def run(): Unit =\n  val temperatures = List(-5, -2, -8)\n  var highest = 0\n  for t <- temperatures do\n    if t > highest then highest = t\n  println(highest)\n", "logic-scala-best-starts-at-zero", 3, 3, 1, "  var highest = temperatures(0)" },
        { "TypeScript", "pasted.ts", "const temperatures: number[] = [-5, -2, -8];\nlet highest: number = 0;\nfor (const t of temperatures) {\n    if (t > highest) {\n        highest = t;\n    }\n}\nconsole.log(highest);\n", "logic-best-starts-at-zero", 2, 2, 1, "let highest: number = temperatures[0];" },
        { "C", "pasted.c", "#include <stdio.h>\n\nint main(void) {\n    int marks[] = {60, 70, 80};\n    int total = 0;\n    for (int i = 0; i < 3; i++) {\n        total = 0;\n        total += marks[i];\n    }\n    printf(\"%d\\n\", total);\n    return 0;\n}\n", "logic-reset-in-loop", 7, 7, 1, "" },
        { "C++", "pasted.cpp", "#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> marks = {60, 70, 80};\n    int total = 0;\n    for (int mark : marks) {\n        total = 0;\n        total += mark;\n    }\n    std::cout << total << \"\\n\";\n}\n", "logic-reset-in-loop", 8, 8, 1, "" },
        { "C#", "pasted.cs", "int[] marks = { 60, 70, 80 };\nint total = 0;\nforeach (int mark in marks)\n{\n    total = 0;\n    total += mark;\n}\nConsole.WriteLine(total);\n", "logic-reset-in-loop", 5, 5, 1, "" },
        { "Dart", "pasted.dart", "void main() {\n  var marks = [60, 70, 80];\n  var total = 0;\n  for (var mark in marks) {\n    total = 0;\n    total += mark;\n  }\n  print(total);\n}\n", "logic-dart-reset-in-loop", 5, 5, 1, "" },
        { "Fortran", "pasted.f90", "program marks\n  implicit none\n  integer :: scores(3) = [60, 70, 80]\n  integer :: total, i\n  total = 0\n  do i = 1, 3\n    total = 0\n    total = total + scores(i)\n  end do\n  print *, total\nend program marks\n", "logic-fortran-reset-in-loop", 7, 7, 1, "" },
        { "F#", "pasted.fsx", "let marks = [ 60; 70; 80 ]\nlet mutable total = 0\nfor mark in marks do\n    total <- 0\n    total <- total + mark\nprintfn \"%d\" total\n", "logic-fsharp-reset-in-loop", 4, 4, 1, "" },
        { "Go", "pasted.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tmarks := []int{60, 70, 80}\n\ttotal := 0\n\tfor _, mark := range marks {\n\t\ttotal = 0\n\t\ttotal += mark\n\t}\n\tfmt.Println(total)\n}\n", "logic-go-reset-in-loop", 9, 9, 1, "" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        int[] marks = {60, 70, 80};\n        int total = 0;\n        for (int mark : marks) {\n            total = 0;\n            total += mark;\n        }\n        System.out.println(total);\n    }\n}\n", "logic-reset-in-loop", 6, 6, 1, "" },
        { "JavaScript", "pasted.js", "const marks = [60, 70, 80];\nlet total = 0;\nfor (const mark of marks) {\n    total = 0;\n    total += mark;\n}\nconsole.log(total);\n", "logic-reset-in-loop", 4, 4, 1, "" },
        { "Lua", "pasted.lua", "local marks = {60, 70, 80}\nlocal total = 0\nfor _, mark in ipairs(marks) do\n  total = 0\n  total = total + mark\nend\nprint(total)\n", "logic-lua-reset-in-loop", 4, 4, 1, "" },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nmy @marks = (60, 70, 80);\nmy $total = 0;\nforeach my $mark (@marks) {\n    $total = 0;\n    $total += $mark;\n}\nprint \"$total\\n\";\n", "logic-perl-reset-in-loop", 7, 7, 1, "" },
        { "PHP", "pasted.php", "<?php\n$marks = [60, 70, 80];\n$total = 0;\nforeach ($marks as $mark) {\n    $total = 0;\n    $total += $mark;\n}\necho $total, PHP_EOL;\n", "logic-php-reset-in-loop", 5, 5, 1, "" },
        { "PowerShell", "pasted.ps1", "$marks = @(60, 70, 80)\n$total = 0\nforeach ($mark in $marks) {\n    $total = 0\n    $total += $mark\n}\nWrite-Output $total\n", "logic-powershell-reset-in-loop", 4, 4, 1, "" },
        { "Python", "pasted.py", "marks = [60, 70, 80]\nfor mark in marks:\n    total = 0\n    total += mark\nprint(total)\n", "logic-python-reset-in-loop", 3, 2, 2, "total = 0|for mark in marks:" },
        { "Ruby", "pasted.rb", "marks = [60, 70, 80]\ntotal = 0\nmarks.each do |mark|\n  total = 0\n  total += mark\nend\nputs total\n", "logic-ruby-reset-in-loop", 4, 4, 1, "" },
        { "Rust", "pasted.rs", "fn main() {\n    let marks = vec![60, 70, 80];\n    let mut total = 0;\n    for mark in &marks {\n        total = 0;\n        total += mark;\n    }\n    println!(\"{}\", total);\n}\n", "logic-rust-reset-in-loop", 5, 5, 1, "" },
        { "Scala", "pasted.scala", "@main def run(): Unit =\n  val marks = List(60, 70, 80)\n  var total = 0\n  for mark <- marks do\n    total = 0\n    total += mark\n  println(total)\n", "logic-scala-reset-in-loop", 5, 5, 1, "" },
        { "TypeScript", "pasted.ts", "const marks: number[] = [60, 70, 80];\nlet total: number = 0;\nfor (const mark of marks) {\n    total = 0;\n    total += mark;\n}\nconsole.log(total);\n", "logic-reset-in-loop", 4, 4, 1, "" },
        { "C", "pasted.c", "#include <stdio.h>\n#include <string.h>\n\nint main(void) {\n    char word[] = \"level\";\n    int same = 1;\n    int length = strlen(word);\n    for (int i = 0; i < length; i++) {\n        if (word[i] != word[length - i]) {\n            same = 0;\n        }\n    }\n    printf(\"%d\\n\", same);\n    return 0;\n}\n", "logic-mirror-index", 9, 9, 1, "        if (word[i] != word[length - 1 - i]) {" },
        { "C++", "pasted.cpp", "#include <iostream>\n#include <string>\n\nint main() {\n    std::string word = \"level\";\n    bool same = true;\n    for (int i = 0; i < word.size(); i++) {\n        if (word[i] != word[word.size() - i]) {\n            same = false;\n        }\n    }\n    std::cout << same << \"\\n\";\n}\n", "logic-mirror-index", 8, 8, 1, "        if (word[i] != word[word.size() - 1 - i]) {" },
        { "C#", "pasted.cs", "string word = \"level\";\nbool same = true;\nfor (int i = 0; i < word.Length; i++)\n{\n    if (word[i] != word[word.Length - i])\n    {\n        same = false;\n    }\n}\nConsole.WriteLine(same);\n", "logic-mirror-index", 5, 5, 1, "    if (word[i] != word[word.Length - 1 - i])" },
        { "Dart", "pasted.dart", "void main() {\n  var word = 'level';\n  var same = true;\n  for (var i = 0; i < word.length; i++) {\n    if (word[i] != word[word.length - i]) {\n      same = false;\n    }\n  }\n  print(same);\n}\n", "logic-dart-mirror-index", 5, 5, 1, "    if (word[i] != word[word.length - 1 - i]) {" },
        { "Elixir", "pasted.exs", "word = \"level\"\nsame = Enum.all?(0..(String.length(word) - 1), fn i ->\n  String.at(word, i) == String.at(word, String.length(word) - i)\nend)\nIO.puts(same)\n", "logic-elixir-mirror-index", 3, 3, 1, "  String.at(word, i) == String.at(word, String.length(word) - 1 - i)" },
        { "F#", "pasted.fsx", "let word = \"level\"\nlet mutable same = true\nfor i in 0 .. word.Length - 1 do\n    if word.[i] <> word.[word.Length - i] then\n        same <- false\nprintfn \"%b\" same\n", "logic-fsharp-mirror-index", 4, 4, 1, "    if word.[i] <> word.[word.Length - 1 - i] then" },
        { "Go", "pasted.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\tword := \"level\"\n\tsame := true\n\tfor i := 0; i < len(word); i++ {\n\t\tif word[i] != word[len(word)-i] {\n\t\t\tsame = false\n\t\t}\n\t}\n\tfmt.Println(same)\n}\n", "logic-go-mirror-index", 9, 9, 1, "\t\tif word[i] != word[len(word) - 1-i] {" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        String word = \"level\";\n        boolean same = true;\n        for (int i = 0; i < word.length(); i++) {\n            if (word.charAt(i) != word.charAt(word.length() - i)) {\n                same = false;\n            }\n        }\n        System.out.println(same);\n    }\n}\n", "logic-mirror-index", 6, 6, 1, "            if (word.charAt(i) != word.charAt(word.length() - 1 - i)) {" },
        { "JavaScript", "pasted.js", "const word = \"level\";\nlet same = true;\nfor (let i = 0; i < word.length; i++) {\n    if (word[i] !== word[word.length - i]) {\n        same = false;\n    }\n}\nconsole.log(same);\n", "logic-mirror-index", 4, 4, 1, "    if (word[i] !== word[word.length - 1 - i]) {" },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nmy $word = \"level\";\nmy $same = 1;\nfor my $i (0 .. length($word) - 1) {\n    if (substr($word, $i, 1) ne substr($word, length($word) - $i, 1)) {\n        $same = 0;\n    }\n}\nprint \"$same\\n\";\n", "logic-perl-mirror-index", 7, 7, 1, "    if (substr($word, $i, 1) ne substr($word, length($word) - 1 - $i, 1)) {" },
        { "PHP", "pasted.php", "<?php\n$word = \"level\";\n$same = true;\nfor ($i = 0; $i < strlen($word); $i++) {\n    if ($word[$i] != $word[strlen($word) - $i]) {\n        $same = false;\n    }\n}\necho $same ? \"true\" : \"false\", PHP_EOL;\n", "logic-php-mirror-index", 5, 5, 1, "    if ($word[$i] != $word[strlen($word) - 1 - $i]) {" },
        { "PowerShell", "pasted.ps1", "$word = \"level\"\n$same = $true\nfor ($i = 0; $i -lt $word.Length; $i++) {\n    if ($word[$i] -ne $word[$word.Length - $i]) {\n        $same = $false\n    }\n}\nWrite-Output $same\n", "logic-powershell-mirror-index", 4, 4, 1, "    if ($word[$i] -ne $word[$word.Length - 1 - $i]) {" },
        { "Python", "pasted.py", "word = \"level\"\nsame = True\nfor i in range(len(word)):\n    if word[i] != word[len(word) - i]:\n        same = False\nprint(same)\n", "logic-python-mirror-index", 4, 4, 1, "    if word[i] != word[len(word) - 1 - i]:" },
        { "Ruby", "pasted.rb", "word = \"level\"\nsame = true\n(0...word.length).each do |i|\n  same = false if word[i] != word[word.length - i]\nend\nputs same\n", "logic-ruby-mirror-index", 4, 4, 1, "  same = false if word[i] != word[word.length - 1 - i]" },
        { "Rust", "pasted.rs", "fn main() {\n    let word = \"level\".as_bytes();\n    let mut same = true;\n    for i in 0..word.len() {\n        if word[i] != word[word.len() - i] {\n            same = false;\n        }\n    }\n    println!(\"{}\", same);\n}\n", "logic-rust-mirror-index", 5, 5, 1, "        if word[i] != word[word.len() - 1 - i] {" },
        { "Scala", "pasted.scala", "@main def run(): Unit =\n  val word = \"level\"\n  var same = true\n  for i <- 0 until word.length do\n    if word(i) != word(word.length - i) then same = false\n  println(same)\n", "logic-scala-mirror-index", 5, 5, 1, "    if word(i) != word(word.length - 1 - i) then same = false" },
        { "TypeScript", "pasted.ts", "const word: string = \"level\";\nlet same: boolean = true;\nfor (let i = 0; i < word.length; i++) {\n    if (word[i] !== word[word.length - i]) {\n        same = false;\n    }\n}\nconsole.log(same);\n", "logic-mirror-index", 4, 4, 1, "    if (word[i] !== word[word.length - 1 - i]) {" },
        { "C", "pasted.c", "#include <ctype.h>\n#include <stdio.h>\n\nint main(void) {\n    char letter = 'a';\n    toupper(letter);\n    printf(\"%c\\n\", letter);\n    return 0;\n}\n", "logic-c-result-discarded", 6, 6, 1, "    letter = toupper(letter);" },
        { "C#", "pasted.cs", "string name = \"ada\";\nname.ToUpper();\nConsole.WriteLine(name);\n", "logic-result-discarded", 2, 2, 1, "name = name.ToUpper();" },
        { "Dart", "pasted.dart", "void main() {\n  var name = 'ada';\n  name.toUpperCase();\n  print(name);\n}\n", "logic-dart-result-discarded", 3, 3, 1, "  name = name.toUpperCase();" },
        { "Elixir", "pasted.exs", "name = \"ada\"\nString.upcase(name)\nIO.puts(name)\n", "logic-elixir-result-discarded", 2, 2, 1, "name = String.upcase(name)" },
        { "Erlang", "marks.erl", "-module(marks).\n-export([main/0]).\n\nmain() ->\n    Name = \"ada\",\n    string:uppercase(Name),\n    io:format(\"~s~n\", [Name]).\n", "logic-erlang-result-discarded", 6, 6, 2, "    UpperName = string:uppercase(Name),|    io:format(\"~s~n\", [UpperName])." },
        { "F#", "pasted.fsx", "let name = \"ada\"\nname.ToUpper()\nprintfn \"%s\" name\n", "logic-fsharp-result-discarded", 2, 2, 2, "let upperName = name.ToUpper()|printfn \"%s\" upperName" },
        { "Go", "pasted.go", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc main() {\n\tname := \"ada\"\n\tstrings.ToUpper(name)\n\tfmt.Println(name)\n}\n", "logic-go-result-discarded", 10, 10, 1, "\tname = strings.ToUpper(name)" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        String name = \"ada\";\n        name.toUpperCase();\n        System.out.println(name);\n    }\n}\n", "logic-result-discarded", 4, 4, 1, "        name = name.toUpperCase();" },
        { "JavaScript", "pasted.js", "let name = \"ada\";\nname.toUpperCase();\nconsole.log(name);\n", "logic-result-discarded", 2, 2, 1, "name = name.toUpperCase();" },
        { "Lua", "pasted.lua", "local name = \"ada\"\nstring.upper(name)\nprint(name)\n", "logic-lua-result-discarded", 2, 2, 1, "name = string.upper(name)" },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nmy $name = \"ada\";\nuc($name);\nprint \"$name\\n\";\n", "logic-perl-result-discarded", 5, 5, 1, "$name = uc($name);" },
        { "PHP", "pasted.php", "<?php\n$name = \"ada\";\nstrtoupper($name);\necho $name, PHP_EOL;\n", "logic-php-result-discarded", 3, 3, 1, "$name = strtoupper($name);" },
        { "Python", "pasted.py", "name = \"ada\"\nname.upper()\nprint(name)\n", "logic-python-result-discarded", 2, 2, 1, "name = name.upper()" },
        { "Ruby", "pasted.rb", "name = \"ada\"\nname.upcase\nputs name\n", "logic-ruby-result-discarded", 2, 2, 1, "name = name.upcase" },
        { "Rust", "pasted.rs", "fn main() {\n    let name = String::from(\"ada\");\n    name.to_uppercase();\n    println!(\"{}\", name);\n}\n", "logic-rust-result-discarded", 3, 3, 1, "    let name = name.to_uppercase();" },
        { "TypeScript", "pasted.ts", "let name: string = \"ada\";\nname.toUpperCase();\nconsole.log(name);\n", "logic-result-discarded", 2, 2, 1, "name = name.toUpperCase();" },
        { "C", "pasted.c", "#include <stdio.h>\n\nint main(void) {\n    double total = 0.1 + 0.2;\n    if (total == 0.3) {\n        printf(\"equal\\n\");\n    } else {\n        printf(\"not equal\\n\");\n    }\n    return 0;\n}\n", "logic-float-equality", 5, 5, 1, "    if (total - 0.3 < 1e-9 && 0.3 - total < 1e-9) {" },
        { "C++", "pasted.cpp", "#include <iostream>\n\nint main() {\n    double total = 0.1 + 0.2;\n    if (total == 0.3) {\n        std::cout << \"equal\\n\";\n    } else {\n        std::cout << \"not equal\\n\";\n    }\n}\n", "logic-float-equality", 5, 5, 1, "    if (total - 0.3 < 1e-9 && 0.3 - total < 1e-9) {" },
        { "C#", "pasted.cs", "double total = 0.1 + 0.2;\nif (total == 0.3)\n{\n    Console.WriteLine(\"equal\");\n}\nelse\n{\n    Console.WriteLine(\"not equal\");\n}\n", "logic-float-equality", 2, 2, 1, "if (Math.Abs(total - 0.3) < 1e-9)" },
        { "Dart", "pasted.dart", "void main() {\n  var total = 0.1 + 0.2;\n  if (total == 0.3) {\n    print('equal');\n  } else {\n    print('not equal');\n  }\n}\n", "logic-dart-float-equality", 3, 3, 1, "  if ((total - 0.3).abs() < 1e-9) {" },
        { "Elixir", "pasted.exs", "total = 0.1 + 0.2\nif total == 0.3 do\n  IO.puts(\"equal\")\nelse\n  IO.puts(\"not equal\")\nend\n", "logic-elixir-float-equality", 2, 2, 1, "if abs(total - 0.3) < 1.0e-9 do" },
        { "Erlang", "marks.erl", "-module(marks).\n-export([main/0]).\n\nmain() ->\n    Total = 0.1 + 0.2,\n    if\n        Total == 0.3 -> io:format(\"equal~n\");\n        true -> io:format(\"not equal~n\")\n    end.\n", "logic-erlang-float-equality", 7, 7, 1, "        abs(Total - 0.3) < 1.0e-9 -> io:format(\"equal~n\");" },
        { "Fortran", "pasted.f90", "program marks\n  implicit none\n  double precision :: total\n  total = 0.1d0 + 0.2d0\n  if (total == 0.3d0) then\n    print *, 'equal'\n  else\n    print *, 'not equal'\n  end if\nend program marks\n", "logic-fortran-float-equality", 5, 5, 1, "  if (abs(total - 0.3d0) < 1d-9) then" },
        { "F#", "pasted.fsx", "let total = 0.1 + 0.2\nif total = 0.3 then printfn \"equal\" else printfn \"not equal\"\n", "logic-fsharp-float-equality", 2, 2, 1, "if abs (total - 0.3) < 1e-9 then printfn \"equal\" else printfn \"not equal\"" },
        { "Go", "pasted.go", "package main\n\nimport \"fmt\"\n\nfunc main() {\n\ttotal := 0.1\n\ttotal += 0.2\n\tif total == 0.3 {\n\t\tfmt.Println(\"equal\")\n\t} else {\n\t\tfmt.Println(\"not equal\")\n\t}\n}\n", "logic-go-float-equality", 8, 8, 1, "\tif total-0.3 < 1e-9 && 0.3-total < 1e-9 {" },
        { "Java", "Main.java", "public class Main {\n    public static void main(String[] args) {\n        double total = 0.1 + 0.2;\n        if (total == 0.3) {\n            System.out.println(\"equal\");\n        } else {\n            System.out.println(\"not equal\");\n        }\n    }\n}\n", "logic-float-equality", 4, 4, 1, "        if (Math.abs(total - 0.3) < 1e-9) {" },
        { "JavaScript", "pasted.js", "const total = 0.1 + 0.2;\nif (total === 0.3) {\n    console.log(\"equal\");\n} else {\n    console.log(\"not equal\");\n}\n", "logic-js-float-equality", 2, 2, 1, "if (Math.abs(total - 0.3) < 1e-9) {" },
        { "Lua", "pasted.lua", "local total = 0.1 + 0.2\nif total == 0.3 then\n  print(\"equal\")\nelse\n  print(\"not equal\")\nend\n", "logic-lua-float-equality", 2, 2, 1, "if math.abs(total - 0.3) < 1e-9 then" },
        { "Perl", "pasted.pl", "use strict;\nuse warnings;\n\nmy $total = 0.1 + 0.2;\nif ($total == 0.3) {\n    print \"equal\\n\";\n} else {\n    print \"not equal\\n\";\n}\n", "logic-perl-float-equality", 5, 5, 1, "if (abs($total - 0.3) < 1e-9) {" },
        { "PHP", "pasted.php", "<?php\n$total = 0.1 + 0.2;\nif ($total == 0.3) {\n    echo \"equal\", PHP_EOL;\n} else {\n    echo \"not equal\", PHP_EOL;\n}\n", "logic-php-float-equality", 3, 3, 1, "if (abs($total - 0.3) < 1e-9) {" },
        { "PowerShell", "pasted.ps1", "$total = 0.1 + 0.2\nif ($total -eq 0.3) {\n    Write-Output \"equal\"\n} else {\n    Write-Output \"not equal\"\n}\n", "logic-powershell-float-equality", 2, 2, 1, "if ([math]::Abs($total - 0.3) -lt 1e-9) {" },
        { "Python", "pasted.py", "total = 0.1 + 0.2\nif total == 0.3:\n    print(\"equal\")\nelse:\n    print(\"not equal\")\n", "logic-python-float-equality", 2, 2, 1, "if abs(total - 0.3) < 1e-9:" },
        { "Ruby", "pasted.rb", "total = 0.1 + 0.2\nif total == 0.3\n  puts \"equal\"\nelse\n  puts \"not equal\"\nend\n", "logic-ruby-float-equality", 2, 2, 1, "if (total - 0.3).abs < 1e-9" },
        { "Rust", "pasted.rs", "fn main() {\n    let total = 0.1 + 0.2;\n    if total == 0.3 {\n        println!(\"equal\");\n    } else {\n        println!(\"not equal\");\n    }\n}\n", "logic-rust-float-equality", 3, 3, 1, "    if (total - 0.3_f64).abs() < 1e-9 {" },
        { "Scala", "pasted.scala", "@main def run(): Unit =\n  val total = 0.1 + 0.2\n  if total == 0.3 then println(\"equal\") else println(\"not equal\")\n", "logic-scala-float-equality", 3, 3, 1, "  if math.abs(total - 0.3) < 1e-9 then println(\"equal\") else println(\"not equal\")" },
        { "TypeScript", "pasted.ts", "const total: number = 0.1 + 0.2;\nif (total === 0.3) {\n    console.log(\"equal\");\n} else {\n    console.log(\"not equal\");\n}\n", "logic-js-float-equality", 2, 2, 1, "if (Math.abs(total - 0.3) < 1e-9) {" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void TheMistakeIsFoundWithItsChange(string language, string fileName, string code, string check, int line, int fixStart, int fixRemoved, string fixLines)
    {
        var found = Assert.Single(Scan(language, fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.NotNull(found.Fix);
        Assert.Equal(fixStart, found.Fix!.StartLine);
        Assert.Equal(fixRemoved, found.Fix.RemoveCount);
        Assert.Equal(LinesOf(fixLines), found.Fix.NewLines);
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void OnceTheChangeIsMadeTheCheckFindsNothing(string language, string fileName, string code, string check, int line, int fixStart, int fixRemoved, string fixLines)
    {
        Assert.True(line > 0);

        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(fixStart - 1, fixRemoved);
        lines.InsertRange(fixStart - 1, LinesOf(fixLines));

        Assert.Empty(Scan(language, fileName, string.Join("\n", lines) + "\n", check));
    }
}
