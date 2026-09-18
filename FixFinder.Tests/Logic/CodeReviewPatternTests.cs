using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>The logic and style patterns: each case is found where the mistake is and fixed as shown (lines separated by ¦),
/// found with no fix to offer (FOUND), or left alone (null) where the same shape is right.</summary>
public class CodeReviewPatternTests : IDisposable
{
    private const string Found = "FOUND";

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private SourceFile Source(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, text);
        return SourceFile.Read(path)!;
    }

    private void Check(string pattern, string file, string code, string? expected)
    {
        var source = Source(file, code);
        var finding = LogicPatterns.Scan(source).FirstOrDefault(f => f.PatternId == pattern);

        if (expected is null)
        {
            Assert.Null(finding);
            return;
        }

        Assert.NotNull(finding);
        if (expected == Found) return;

        Assert.NotNull(finding!.Fix);
        var fixedLines = finding.Fix!.ApplyTo(source)!;

        foreach (var part in expected.Split('¦'))
            Assert.Contains(part, fixedLines);
    }

    [Theory]
    [InlineData("logic-python-or-constant", "if answer == \"yes\" or \"y\":\n    go()\n", "if answer in (\"yes\", \"y\"):")]
    [InlineData("logic-python-or-constant", "while choice != \"q\" or \"Q\":\n    go()\n", "while choice not in (\"q\", \"Q\"):")]
    [InlineData("logic-python-or-constant", "if n == 1 or 2 or 3:\n    go()\n", "if n in (1, 2, 3):")]
    [InlineData("logic-python-or-constant", "if answer == \"yes\" or answer == \"y\":\n    go()\n", null)]
    [InlineData("logic-python-or-constant", "value = n == 1 or default\n", null)]
    [InlineData("logic-python-none-returned-assigned", "names = [\"b\", \"a\"]\nnames = names.sort()\n", "names.sort()")]
    [InlineData("logic-python-none-returned-assigned", "ordered = names.sort()\n", "ordered = sorted(names)")]
    [InlineData("logic-python-none-returned-assigned", "items = items.append(4)\n", "items.append(4)")]
    [InlineData("logic-python-none-returned-assigned", "deck = random.shuffle(deck)\n", "random.shuffle(deck)")]
    [InlineData("logic-python-none-returned-assigned", "total = calculator.add(2, 3)\n", null)]
    [InlineData("logic-python-none-returned-assigned", "class Stack:\n    def append(self, x):\n        return self\ns = s.append(1)\n", null)]
    [InlineData("logic-python-missing-f-prefix", "name = input()\nprint(\"Hello {name}\")\n", "print(f\"Hello {name}\")")]
    [InlineData("logic-python-missing-f-prefix", "total = 3\nprint('Total: {total:.2f}')\n", "print(f'Total: {total:.2f}')")]
    [InlineData("logic-python-missing-f-prefix", "name = input()\nprint(\"Hello {name}\".format(name=name))\n", null)]
    [InlineData("logic-python-missing-f-prefix", "template = \"Hello {name}\"\nprint(template.format(name=\"x\"))\n", null)]
    [InlineData("logic-python-missing-f-prefix", "print(\"use {braces} here\")\n", null)]
    [InlineData("logic-python-method-not-called", "answer = input()\nif answer.isdigit:\n    go()\n", "if answer.isdigit():")]
    [InlineData("logic-python-method-not-called", "name = input().strip\n", "name = input().strip()")]
    [InlineData("logic-python-method-not-called", "names.sort(key=str.lower)\n", null)]
    [InlineData("logic-python-method-not-called", "shout = map(str.upper, words)\n", null)]
    [InlineData("logic-python-method-not-called", "if answer.isdigit():\n    go()\n", null)]
    [InlineData("logic-python-modified-while-looping", "for n in numbers:\n    if n < 0:\n        numbers.remove(n)\n", "for n in list(numbers):")]
    [InlineData("logic-python-modified-while-looping", "for n in numbers:\n    if n == target:\n        numbers.remove(n)\n        break\n", null)]
    [InlineData("logic-python-modified-while-looping", "for n in numbers:\n    kept.append(n)\n", null)]
    [InlineData("logic-python-input-used-as-number", "age = input(\"Age: \")\nif age >= 18:\n    print(\"adult\")\n", "age = int(input(\"Age: \"))")]
    [InlineData("logic-python-input-used-as-number", "price = input()\nprint(price * 1.2)\n", "price = float(input())")]
    [InlineData("logic-python-input-used-as-number", "n = input()\nfor i in range(n):\n    print(i)\n", "n = int(input())")]
    [InlineData("logic-python-input-used-as-number", "age = input()\nage = int(age)\nif age >= 18:\n    pass\n", null)]
    [InlineData("logic-python-input-used-as-number", "name = input()\nprint(\"Hi \" + name)\n", null)]
    [InlineData("logic-python-print-instead-of-return", "def area(w, h):\n    print(w * h)\n\ntotal = area(2, 3) + 1\n", "    return w * h")]
    [InlineData("logic-python-print-instead-of-return", "def area(w, h):\n    print(w * h)\n\narea(2, 3)\n", null)]
    [InlineData("logic-python-print-instead-of-return", "def area(w, h):\n    print(w * h)\n    return w * h\n\ntotal = area(2, 3)\n", null)]
    [InlineData("logic-python-return-print", "def area(w, h):\n    return print(w * h)\n", "    print(w * h)¦    return w * h")]
    [InlineData("logic-python-statement-has-no-effect", "count = 0\ncount + 1\n", "count += 1")]
    [InlineData("logic-python-statement-has-no-effect", "total = (a +\n    b)\n", null)]
    [InlineData("logic-python-shadowed-builtin", "sum = 0\nfor n in [1, 2]:\n    sum += n\nprint(sum([3, 4]))\n", Found)]
    [InlineData("logic-python-shadowed-builtin", "total = 0\nprint(sum([3, 4]))\n", null)]
    [InlineData("logic-python-unreachable-code", "def f():\n    return 1\n    print(\"never\")\n", Found)]
    [InlineData("logic-python-unreachable-code", "def f(x):\n    if x:\n        return 1\n    return 2\n", null)]
    [InlineData("logic-python-unreachable-code", "def f(x):\n    return (x +\n        1)\n", null)]
    [InlineData("logic-python-duplicate-condition", "if n > 5:\n    a()\nelif n > 5:\n    b()\n", Found)]
    [InlineData("logic-python-duplicate-condition", "if n > 5:\n    a()\nelif n > 3:\n    b()\n", null)]
    [InlineData("logic-python-endless-while-true", "while True:\n    print(\"menu\")\n", Found)]
    [InlineData("logic-python-endless-while-true", "while True:\n    if input() == \"q\":\n        break\n", null)]
    [InlineData("logic-python-range-skips-last", "for i in range(len(names) - 1):\n    print(names[i])\n", "for i in range(len(names)):")]
    [InlineData("logic-python-range-skips-last", "for i in range(len(xs) - 1):\n    print(xs[i + 1] - xs[i])\n", null)]
    [InlineData("logic-python-bare-except", "try:\n    go()\nexcept:\n    print(\"failed\")\n", "except Exception:")]
    [InlineData("logic-python-bare-except", "try:\n    go()\nexcept ValueError:\n    print(\"failed\")\n", null)]
    [InlineData("logic-python-silent-except", "try:\n    n = int(s)\nexcept ValueError:\n    pass\n", "except ValueError as error:¦    print(\"Something went wrong:\", error)")]
    [InlineData("logic-python-silent-except", "try:\n    n = int(s)\nexcept ValueError:\n    n = 0\n", null)]
    [InlineData("logic-python-shared-class-list", "class Basket:\n    items = []\n\n    def add(self, x):\n        self.items.append(x)\n", Found)]
    [InlineData("logic-python-shared-class-list", "class Basket:\n    def __init__(self):\n        self.items = []\n\n    def add(self, x):\n        self.items.append(x)\n", null)]
    [InlineData("logic-python-none-comparison", "if value == None:\n    go()\n", "if value is None:")]
    [InlineData("logic-python-none-comparison", "if value != None:\n    go()\n", "if value is not None:")]
    [InlineData("logic-python-bool-comparison", "if done == True:\n    go()\n", "if done:")]
    [InlineData("logic-python-bool-comparison", "while found == False:\n    go()\n", "while not found:")]
    [InlineData("logic-python-type-comparison", "if type(x) == int:\n    go()\n", "if isinstance(x, int):")]
    [InlineData("logic-python-range-len-loop", "for i in range(len(names)):\n    print(names[i])\n", "for name in names:¦    print(name)")]
    [InlineData("logic-python-range-len-loop", "for i in range(len(names)):\n    print(i, names[i])\n", null)]
    [InlineData("logic-python-range-len-loop", "for i in range(len(names)):\n    names[i] = names[i].upper()\n", null)]
    [InlineData("logic-python-file-not-closed", "f = open(\"out.txt\", \"w\")\nf.write(\"hi\")\n", Found)]
    [InlineData("logic-python-file-not-closed", "f = open(\"out.txt\", \"w\")\nf.write(\"hi\")\nf.close()\n", null)]
    [InlineData("logic-python-shadowed-builtin", "list = [3, 1]\nlist.append(2)\nprint(list((1, 2)))\n", "items = [3, 1]¦items.append(2)¦print(list((1, 2)))")]
    [InlineData("logic-python-shared-class-list", "class Basket:\n    items = []\n\n    def add(self, x):\n        self.items.append(x)\n", "    def __init__(self):¦        self.items = []")]
    [InlineData("logic-python-returns-nothing-sometimes", "def grade(s):\n    if s >= 50:\n        return 'pass'\n    elif s >= 0:\n        print('fail')\n", Found)]
    [InlineData("logic-python-returns-nothing-sometimes", "def grade(s):\n    if s >= 50:\n        return 'pass'\n    else:\n        return 'fail'\n", null)]
    [InlineData("logic-python-returns-nothing-sometimes", "def grade(s):\n    if s >= 50:\n        return 'pass'\n    return 'fail'\n", null)]
    [InlineData("logic-python-returns-nothing-sometimes", "def show(s):\n    print(s)\n", null)]
    [InlineData("logic-python-floor-division-average", "average = sum(xs) // len(xs)\n", "average = sum(xs) / len(xs)")]
    [InlineData("logic-python-floor-division-average", "def mean(xs):\n    return sum(xs) // len(xs)\n", "    return sum(xs) / len(xs)")]
    [InlineData("logic-python-floor-division-average", "pages = total // per_page\n", null)]
    [InlineData("logic-python-attribute-not-set", "class C:\n    def __init__(self):\n        self.count = 0\n\n    def add(self):\n        count = self.count + 1\n", "        self.count = self.count + 1")]
    [InlineData("logic-python-attribute-not-set", "class C:\n    def __init__(self):\n        self.count = 0\n\n    def show(self):\n        count = self.count + 1\n        print(count)\n", null)]
    public void PythonMistakesAreFoundAndFixed(string pattern, string code, string? expected) => Check(pattern, "app.py", code, expected);

    [Theory]
    [InlineData("logic-python-shadowed-builtin", "def parse(text: str) -> int:\n    return len(text)\n")]
    [InlineData("logic-python-shadowed-builtin", "class Node:\n    def __init__(self, value, next=None):\n        self.next = next\n")]
    [InlineData("logic-python-statement-has-no-effect", "def f():\n    return -1\n")]
    [InlineData("logic-python-method-not-called", "print(basket.items)\n")]
    [InlineData("logic-python-file-not-closed", "text = open(\"a.txt\").read()\n")]
    [InlineData("logic-python-missing-f-prefix", "name = 1\ncode = 'JSON.parse(\"{name: \\'Ada\\'}\")'\n")]
    [InlineData("logic-python-print-instead-of-return", "def main():\n    await asyncio.sleep(1)\n    print(\"done\")\n\nasyncio.run(main())\n")]
    [InlineData("logic-python-unreachable-code", "def total(values):\nreturn sum(values)\n\nprint(total([1]))\n")]
    public void CorrectPythonIsLeftAlone(string pattern, string code) => Check(pattern, "app.py", code, null);

    [Theory]
    [InlineData("logic-self-assignment", "Person.java", "class Person {\n    private String name;\n    Person(String name) {\n        name = name;\n    }\n}\n", "        this.name = name;")]
    [InlineData("logic-self-assignment", "Person.cs", "class Person\n{\n    private string name;\n    public Person(string name)\n    {\n        name = name;\n    }\n}\n", "        this.name = name;")]
    [InlineData("logic-self-assignment", "person.cpp", "class Person {\n    int age;\npublic:\n    Person(int age) {\n        age = age;\n    }\n};\n", "        this->age = age;")]
    [InlineData("logic-self-assignment", "App.java", "class App {\n    void f() {\n        int x = 1;\n        x = x;\n    }\n}\n", Found)]
    [InlineData("logic-self-assignment", "Person.java", "class Person {\n    Person(String name) {\n        this.name = name;\n    }\n}\n", null)]
    [InlineData("logic-lost-increment", "App.java", "        count = count++;\n", "        count++;")]
    [InlineData("logic-lost-increment", "App.java", "        count = other++;\n", null)]
    [InlineData("logic-off-by-one-length", "App.java", "        for (int i = 0; i <= scores.length; i++) {\n            total += scores[i];\n        }\n", "        for (int i = 0; i < scores.length; i++) {")]
    [InlineData("logic-off-by-one-length", "App.java", "        for (int i = 0; i <= names.size(); i++) {\n            System.out.println(names.get(i));\n        }\n", "        for (int i = 0; i < names.size(); i++) {")]
    [InlineData("logic-off-by-one-length", "Program.cs", "for (var i = 0; i <= items.Count; i++)\n{\n    Console.WriteLine(items[i]);\n}\n", "for (var i = 0; i < items.Count; i++)")]
    [InlineData("logic-off-by-one-length", "app.js", "for (let i = 0; i <= names.length; i++) {\n  console.log(names[i]);\n}\n", "for (let i = 0; i < names.length; i++) {")]
    [InlineData("logic-off-by-one-length", "app.c", "int main(void) {\n    int marks[5];\n    for (int i = 0; i <= 5; i++) marks[i] = 0;\n}\n", "    for (int i = 0; i < 5; i++) marks[i] = 0;")]
    [InlineData("logic-off-by-one-length", "App.java", "        for (int i = 1; i <= count.length; i++) {\n            System.out.println(i);\n        }\n", null)]
    [InlineData("logic-or-constant", "app.js", "if (answer === \"yes\" || \"y\") {\n  go();\n}\n", "if (answer === \"yes\" || answer === \"y\") {")]
    [InlineData("logic-or-constant", "app.c", "    if (c == 'y' || 'Y') {\n", "    if (c == 'y' || c == 'Y') {")]
    [InlineData("logic-or-constant", "app.js", "if (answer === \"yes\" || answer === \"y\") {\n", null)]
    [InlineData("logic-duplicate-condition", "App.java", "        if (n > 5) {\n            a();\n        } else if (n > 5) {\n            b();\n        }\n", Found)]
    [InlineData("logic-duplicate-condition", "App.java", "        if (n > 5) {\n            a();\n        } else if (n > 3) {\n            b();\n        }\n", null)]
    [InlineData("logic-empty-catch", "App.java", "        try {\n            go();\n        } catch (Exception e) {\n        }\n", "        } catch (Exception e) {¦            System.err.println(\"Something went wrong: \" + e.getMessage());")]
    [InlineData("logic-empty-catch", "Program.cs", "try\n{\n    Go();\n}\ncatch { }\n", "catch (Exception ex) {¦    Console.Error.WriteLine($\"Something went wrong: {ex.Message}\");")]
    [InlineData("logic-empty-catch", "app.js", "try {\n  go();\n} catch (err) {}\n", "} catch (err) {¦  console.error(\"Something went wrong:\", err);")]
    [InlineData("logic-empty-catch", "App.java", "        } catch (Exception e) {\n            log(e);\n        }\n", null)]
    [InlineData("logic-modified-while-looping", "App.java", "        for (String name : names) {\n            if (name.isEmpty()) {\n                names.remove(name);\n            }\n        }\n", "        for (String name : new java.util.ArrayList<>(names)) {")]
    [InlineData("logic-modified-while-looping", "Program.cs", "foreach (var name in names)\n{\n    names.Remove(name);\n}\n", "foreach (var name in names.ToList())")]
    [InlineData("logic-modified-while-looping", "App.java", "        for (String name : names) {\n            if (name.isEmpty()) {\n                names.remove(name);\n                break;\n            }\n        }\n", null)]
    [InlineData("logic-float-equality", "App.java", "        double total = 0.1 + 0.2;\n        if (total == 0.3) {\n", "        if (Math.abs(total - 0.3) < 1e-9) {")]
    [InlineData("logic-float-equality", "App.java", "        int total = 3;\n        if (total == 3) {\n", null)]
    [InlineData("logic-java-scanner-skips-line", "App.java", "        Scanner in = new Scanner(System.in);\n        int age = in.nextInt();\n        String name = in.nextLine();\n", "        int age = in.nextInt();¦        in.nextLine();¦        String name = in.nextLine();")]
    [InlineData("logic-java-scanner-skips-line", "App.java", "        Scanner in = new Scanner(System.in);\n        String name = in.nextLine();\n        int age = in.nextInt();\n", null)]
    [InlineData("logic-java-random-always-zero", "App.java", "        int roll = (int) Math.random() * 6 + 1;\n", "        int roll = (int) (Math.random() * 6) + 1;")]
    [InlineData("logic-java-random-always-zero", "App.java", "        int roll = (int) (Math.random() * 6) + 1;\n", null)]
    [InlineData("logic-java-wrapper-equality", "App.java", "        Integer a = 1000;\n        Integer b = 1000;\n        if (a == b) {\n", "        if (a.equals(b)) {")]
    [InlineData("logic-java-wrapper-equality", "App.java", "        int a = 1000;\n        int b = 1000;\n        if (a == b) {\n", null)]
    [InlineData("logic-java-misspelt-override", "Point.java", "class Point {\n    public String tostring() {\n        return \"p\";\n    }\n}\n", "    public String toString() {")]
    [InlineData("logic-java-misspelt-override", "Point.java", "class Point {\n    public String toString() {\n        return \"p\";\n    }\n}\n", null)]
    [InlineData("logic-string-built-in-loop", "App.java", "        String line = \"\";\n        for (int i = 0; i < 5; i++) {\n            line += i;\n        }\n", Found)]
    [InlineData("logic-csharp-async-void", "Program.cs", "static async void Save()\n{\n    await Task.Delay(1);\n}\n", "static async Task Save()")]
    [InlineData("logic-csharp-async-void", "Form.cs", "private async void Button_Click(object sender, EventArgs e)\n{\n}\n", null)]
    [InlineData("logic-csharp-property-calls-itself", "Person.cs", "class Person\n{\n    public int Age { get { return Age; } set { Age = value; } }\n}\n", "    public int Age { get; set; }")]
    [InlineData("logic-csharp-property-calls-itself", "Person.cs", "class Person\n{\n    private int age;\n    public int Age { get { return age; } set { age = value; } }\n}\n", null)]
    [InlineData("logic-csharp-parse-unchecked", "Program.cs", "var age = int.Parse(Console.ReadLine());\n", Found)]
    [InlineData("logic-js-loose-equality", "app.js", "if (count == 0) {\n", "if (count === 0) {")]
    [InlineData("logic-js-loose-equality", "app.js", "if (value == null) {\n", null)]
    [InlineData("logic-js-template-in-quotes", "app.js", "console.log(\"Hello ${name}\");\n", "console.log(`Hello ${name}`);")]
    [InlineData("logic-js-template-in-quotes", "app.js", "console.log(`Hello ${name}`);\n", null)]
    [InlineData("logic-js-compare-with-new-array", "app.js", "if (items === []) {\n", "if (items.length === 0) {")]
    [InlineData("logic-nan-comparison", "app.js", "if (value === NaN) {\n", "if (Number.isNaN(value)) {")]
    [InlineData("logic-nan-comparison", "App.java", "        if (x == Double.NaN) {\n", "        if (Double.isNaN(x)) {")]
    [InlineData("logic-empty-catch", "Program.cs", "try\n{\n    Go();\n}\ncatch (Exception)\n{\n}\n", "catch (Exception ex)¦{¦    Console.Error.WriteLine($\"Something went wrong: {ex.Message}\");¦}")]
    [InlineData("logic-java-equals-overload", "Point.java", "class Point {\n    int x;\n    public boolean equals(Point other) {\n        return x == other.x;\n    }\n}\n", "    public boolean equals(Object object) {¦        if (!(object instanceof Point other)) return false;")]
    [InlineData("logic-java-equals-overload", "Point.java", "class Point {\n    public boolean equals(Object other) {\n        return true;\n    }\n}\n", null)]
    [InlineData("logic-integer-division", "App.java", "        int total = 7;\n        double average = total / marks.size();\n", "        double average = (double) total / marks.size();")]
    [InlineData("logic-integer-division", "Program.cs", "int total = 7;\ndouble average = total / items.Count;\n", "double average = (double) total / items.Count;")]
    [InlineData("logic-java-chars-added", "App.java", "        char a = 'A';\n        char b = 'B';\n        System.out.println(a + b);\n", "        System.out.println(\"\" + a + b);")]
    [InlineData("logic-java-chars-added", "App.java", "        int a = 1;\n        int b = 2;\n        System.out.println(a + b);\n", null)]
    public void BraceLanguageMistakesAreFoundAndFixed(string pattern, string file, string code, string? expected) => Check(pattern, file, code, expected);

    [Theory]
    [InlineData("logic-csharp-console-read-number", "Program.cs", "int age = Console.Read();\nConsole.WriteLine(age + 1);\n", "int age = int.Parse(Console.ReadLine());")]
    [InlineData("logic-csharp-console-read-number", "Program.cs", "double price = Convert.ToDouble(Console.Read());\n", "double price = double.Parse(Console.ReadLine());")]
    [InlineData("logic-csharp-console-read-number", "Program.cs", "int key = Console.Read();\nif (key == 'q') return;\n", null)]
    [InlineData("logic-csharp-console-read-number", "Program.cs", "char c = (char)Console.Read();\n", null)]
    [InlineData("logic-csharp-throw-ex", "Program.cs", "try\n{\n    Load();\n}\ncatch (IOException ex)\n{\n    Log(ex);\n    throw ex;\n}\n", "    throw;")]
    [InlineData("logic-csharp-throw-ex", "Program.cs", "try\n{\n    Load();\n}\ncatch (IOException ex)\n{\n    throw new InvalidDataException(\"bad file\", ex);\n}\n", null)]
    [InlineData("logic-csharp-blocking-wait", "Program.cs", "static async Task Main()\n{\n    var page = client.GetStringAsync(url).Result;\n    Console.WriteLine(page);\n}\n", "    var page = await client.GetStringAsync(url);")]
    [InlineData("logic-csharp-blocking-wait", "Program.cs", "static async Task Run()\n{\n    Task saving = SaveAsync();\n    saving.Wait();\n}\n", "    await saving;")]
    [InlineData("logic-csharp-blocking-wait", "Program.cs", "static void Main()\n{\n    var page = client.GetStringAsync(url).Result;\n}\n", null)]
    [InlineData("logic-csharp-blocking-wait", "Game.cs", "static async Task Play()\n{\n    Console.WriteLine(match.Result);\n}\n", null)]
    [InlineData("logic-csharp-not-disposed", "Program.cs", "var writer = new StreamWriter(\"out.txt\");\nwriter.WriteLine(total);\n", "using var writer = new StreamWriter(\"out.txt\");")]
    [InlineData("logic-csharp-not-disposed", "Program.cs", "var reader = File.OpenText(path);\nConsole.WriteLine(reader.ReadLine());\n", "using var reader = File.OpenText(path);")]
    [InlineData("logic-csharp-not-disposed", "Program.cs", "using var writer = new StreamWriter(\"out.txt\");\nwriter.WriteLine(total);\n", null)]
    [InlineData("logic-csharp-not-disposed", "Program.cs", "var writer = new StreamWriter(\"out.txt\");\nwriter.WriteLine(total);\nwriter.Close();\n", null)]
    [InlineData("logic-case-never-matches", "Program.cs", "if (answer.ToLower() == \"Yes\")\n{\n}\n", "if (answer.ToLower() == \"yes\")")]
    [InlineData("logic-case-never-matches", "App.java", "        if (answer.toUpperCase().equals(\"quit\")) {\n", "        if (answer.toUpperCase().equals(\"QUIT\")) {")]
    [InlineData("logic-case-never-matches", "app.js", "if (answer.toLowerCase() === \"Y\") {\n", "if (answer.toLowerCase() === \"y\") {")]
    [InlineData("logic-case-never-matches", "Program.cs", "if (answer.ToLower() == \"yes\")\n{\n}\n", null)]
    [InlineData("logic-char-used-as-digit", "Program.cs", "string number = Console.ReadLine();\nint sum = 0;\nfor (int i = 0; i < number.Length; i++)\n{\n    sum += number[i];\n}\n", "    sum += number[i] - '0';")]
    [InlineData("logic-char-used-as-digit", "App.java", "        String code = \"1234\";\n        int first = Integer.valueOf(code.charAt(0));\n", "        int first = (code.charAt(0) - '0');")]
    [InlineData("logic-char-used-as-digit", "Program.cs", "char c = '7';\nint value = Convert.ToInt32(c);\n", "int value = (c - '0');")]
    [InlineData("logic-char-used-as-digit", "Program.cs", "string number = \"2024\";\nint sum = 0;\nforeach (char c in number)\n{\n    sum += c;\n}\n", "    sum += c - '0';")]
    [InlineData("logic-char-used-as-digit", "Program.cs", "List<int> numbers = new();\nint sum = 0;\nforeach (var n in numbers)\n{\n    sum += n;\n}\n", null)]
    [InlineData("logic-char-used-as-digit", "Program.cs", "string number = \"12\";\nint sum = 0;\nsum += number.Length;\n", null)]
    [InlineData("logic-count-from-missing-key", "App.java", "        Map<String, Integer> counts = new HashMap<>();\n        for (String word : words) {\n            counts.put(word, counts.get(word) + 1);\n        }\n", "            counts.put(word, counts.getOrDefault(word, 0) + 1);")]
    [InlineData("logic-count-from-missing-key", "Program.cs", "var counts = new Dictionary<string, int>();\nforeach (var word in words)\n{\n    counts[word]++;\n}\n", "    counts[word] = counts.GetValueOrDefault(word) + 1;")]
    [InlineData("logic-count-from-missing-key", "Program.cs", "var counts = new Dictionary<string, int>();\nforeach (var word in words)\n{\n    if (!counts.ContainsKey(word)) counts[word] = 0;\n    counts[word]++;\n}\n", null)]
    [InlineData("logic-collection-printed", "App.java", "        int[] scores = {3, 5, 8};\n        System.out.println(scores);\n", "        System.out.println(java.util.Arrays.toString(scores));")]
    [InlineData("logic-collection-printed", "App.java", "import java.util.Arrays;\n\nclass App {\n    static void show(int[] scores) {\n        System.out.println(\"Scores: \" + scores);\n    }\n}\n", "        System.out.println(\"Scores: \" + Arrays.toString(scores));")]
    [InlineData("logic-collection-printed", "Program.cs", "int[] scores = { 3, 5, 8 };\nConsole.WriteLine(scores);\n", "Console.WriteLine(string.Join(\", \", scores));")]
    [InlineData("logic-collection-printed", "Program.cs", "var names = new List<string>();\nConsole.WriteLine($\"Names: {names}\");\n", "Console.WriteLine($\"Names: {string.Join(\", \", names)}\");")]
    [InlineData("logic-collection-printed", "Program.cs", "var names = new List<string> { \"Ada\", \"Grace\" };\nConsole.WriteLine(names);\n", "Console.WriteLine(string.Join(\", \", names));")]
    [InlineData("logic-collection-printed", "App.java", "        int[] scores = {3, 5, 8};\n        System.out.println(Arrays.toString(scores));\n", null)]
    [InlineData("logic-collection-printed", "Program.cs", "int[] scores = { 3, 5, 8 };\nConsole.WriteLine(scores.Length);\n", null)]
    [InlineData("logic-local-hides-field", "Student.java", "class Student {\n    private String name;\n\n    public Student(String n) {\n        String name = n;\n    }\n}\n", "        this.name = n;")]
    [InlineData("logic-local-hides-field", "Student.cs", "class Student\n{\n    private string name;\n\n    public Student(string n)\n    {\n        string name = n;\n    }\n}\n", "        this.name = n;")]
    [InlineData("logic-local-hides-field", "Student.java", "class Student {\n    private String name;\n\n    public Student(String n) {\n        String name = n.trim();\n        this.name = name;\n    }\n}\n", null)]
    [InlineData("logic-remove-while-counting-up", "App.java", "        for (int i = 0; i < names.size(); i++) {\n            if (names.get(i).isEmpty()) {\n                names.remove(i);\n            }\n        }\n", "        for (int i = names.size() - 1; i >= 0; i--) {")]
    [InlineData("logic-remove-while-counting-up", "Program.cs", "for (int i = 0; i < names.Count; i++)\n{\n    if (names[i] == \"\") names.RemoveAt(i);\n}\n", "for (int i = names.Count - 1; i >= 0; i--)")]
    [InlineData("logic-remove-while-counting-up", "App.java", "        for (int i = 0; i < names.size(); i++) {\n            if (names.get(i).isEmpty()) {\n                names.remove(i);\n                i--;\n            }\n        }\n", null)]
    [InlineData("logic-loop-copy-assigned", "App.java", "        for (int score : scores) {\n            score = score * 2;\n        }\n", Found)]
    [InlineData("logic-loop-copy-assigned", "main.cpp", "    for (int x : values) {\n        x = 0;\n    }\n", "    for (int& x : values) {")]
    [InlineData("logic-loop-copy-assigned", "app.js", "for (let item of items) {\n    item = item.trim();\n}\n", Found)]
    [InlineData("logic-loop-copy-assigned", "App.java", "        for (String line : lines) {\n            line = line.trim();\n            System.out.println(line);\n        }\n", null)]
    [InlineData("logic-loop-steps-away", "App.java", "        for (int i = 10; i > 0; i++) {\n", "        for (int i = 10; i > 0; i--) {")]
    [InlineData("logic-loop-steps-away", "Program.cs", "for (int i = 0; i < 10; i--)\n{\n}\n", "for (int i = 0; i < 10; i++)")]
    [InlineData("logic-loop-steps-away", "app.js", "for (let i = 0; i <= n; i -= 1) {\n", "for (let i = 0; i <= n; i += 1) {")]
    [InlineData("logic-loop-steps-away", "main.c", "for (int i = 10; i > 0; i--) {\n", null)]
    [InlineData("logic-loop-steps-away", "App.java", "        for (int i = start; i >= 0 && i <= last; i++) {\n", null)]
    [InlineData("logic-csharp-blocking-wait", "Program.cs", "static async Task Run()\n{\n    Task<int> count = CountAsync();\n    await Task.WhenAll(count);\n    Console.WriteLine(count.Result);\n}\n", null)]
    [InlineData("logic-loop-copy-assigned", "App.java", "        for (Node node : heads) {\n            while (node != null) {\n                total += node.value;\n                node = node.next;\n            }\n        }\n", null)]
    public void JavaAndCSharpMistakesAreFoundAndFixed(string pattern, string file, string code, string? expected) => Check(pattern, file, code, expected);

    [Fact]
    public void EveryPatternHasAGuide()
    {
        var missing = LogicPatterns.All.Select(p => p.Id).Where(id => !Core.Checking.Guides.Guidebook.HasRule(id)).ToList();

        Assert.True(missing.Count == 0, "No guide for: " + string.Join(", ", missing));
    }

    [Fact]
    public void AStringOrACommentIsNeverReadAsCode()
    {
        var source = Source("app.py", "# if answer == \"yes\" or \"y\":\nprint(\"x = x.sort()\")\n");

        Assert.DoesNotContain(LogicPatterns.Scan(source), f => f.PatternId.StartsWith("logic-python-", StringComparison.Ordinal));
    }
}
