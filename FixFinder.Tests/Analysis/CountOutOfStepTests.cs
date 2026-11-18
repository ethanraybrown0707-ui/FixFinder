using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;

namespace FixFinder.Tests;

/// <summary>
/// A count left out of step: a stack's push appends to items and adds 1 to size, and its pop takes an item out and leaves
/// size as it was - found in Python, Java, C#, JavaScript, Go and C++. Left alone in each: each found program put right, a
/// running total of pushes that is not a count, a pop setting size from the list's length, one leaving the work to another
/// method, a clear giving both new values, and C++'s stack kept in an array with a top index.
/// </summary>
public class CountOutOfStepTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Analyse(string language, string code)
    {
        switch (language)
        {
            case "Python":
                if (PythonFrontend.FindInterpreter() is not { } python) return null;
                return AbstractChecks.Run(await PythonFrontend.ReadAsync([Write("sample.py", code)], python), new SourceText());

            case "Java":
                if (JavaFrontend.FindTools() is not { } tools) return null;
                var className = System.Text.RegularExpressions.Regex.Match(code, @"public class (\w+)").Groups[1].Value;
                return AbstractChecks.Run(await JavaFrontend.ReadAsync([Write($"{className}.java", code)], tools.Javac, tools.Java), new SourceText());

            case "JavaScript":
                return AbstractChecks.Run(await JavaScriptFrontend.ReadAsync([Write("main.js", code)]), new SourceText());

            case "Go":
                var file = Write("main.go", code);
                if (GoFrontend.FindGo(file) is not { } go) return null;
                return AbstractChecks.Run(await GoFrontend.ReadAsync([file], go), new SourceText());

            case "C":
                return AbstractChecks.Run(await CFrontend.ReadAsync([Write("main.c", code)]), new SourceText());

            case "C++":
                return AbstractChecks.Run(await CFrontend.ReadAsync([Write("main.cpp", code)]), new SourceText());

            default:
                return AbstractChecks.Run(await CSharpFrontend.ReadAsync([Write("Program.cs", code)]), new SourceText());
        }
    }

    private string Write(string name, string code)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, code);
        return path;
    }

    /// <summary>The language, the program, the line of the one-sided change, and what the check said of it here.</summary>
    public static IEnumerable<object[]> OutOfStep =>
    [
        new object[] { "C++", "#include <iostream>\n#include <vector>\n\nclass Stack {\npublic:\n    void push(int item) {\n        items.push_back(item);\n        size++;\n    }\n\n    int pop() {\n        int top = items.back();\n        items.pop_back();\n        return top;\n    }\n\n    int count() const {\n        return size;\n    }\n\nprivate:\n    std::vector<int> items;\n    int size = 0;\n};\n\nint main() {\n    Stack stack;\n    for (int item : {1, 2, 3}) {\n        stack.push(item);\n    }\n    stack.pop();\n    std::cout << stack.count() << \"\\n\";\n}\n", 13, "`pop` takes an item out of `items` but leaves `size` as it was, while `push` changes both - after it, `size` no longer says how many `items` holds" },
        new object[] { "C#", "using System;\nusing System.Collections.Generic;\n\nvar stack = new Stack();\nforeach (var item in new[] { 1, 2, 3 })\n{\n    stack.Push(item);\n}\nstack.Pop();\nConsole.WriteLine(stack.Count());\n\nclass Stack\n{\n    private readonly List<int> items = new List<int>();\n    private int size = 0;\n\n    public void Push(int item)\n    {\n        items.Add(item);\n        size++;\n    }\n\n    public int Pop()\n    {\n        var top = items[items.Count - 1];\n        items.RemoveAt(items.Count - 1);\n        return top;\n    }\n\n    public int Count()\n    {\n        return size;\n    }\n}\n", 26, "`Pop` takes an item out of `items` but leaves `size` as it was, while `Push` changes both - after it, `size` no longer says how many `items` holds" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\ntype Stack struct {\n\titems []int\n\tsize  int\n}\n\nfunc (stack *Stack) Push(item int) {\n\tstack.items = append(stack.items, item)\n\tstack.size++\n}\n\nfunc (stack *Stack) Pop() int {\n\ttop := stack.items[len(stack.items)-1]\n\tstack.items = stack.items[:len(stack.items)-1]\n\treturn top\n}\n\nfunc (stack *Stack) Count() int {\n\treturn stack.size\n}\n\nfunc main() {\n\tstack := &Stack{}\n\tfor _, item := range []int{1, 2, 3} {\n\t\tstack.Push(item)\n\t}\n\tstack.Pop()\n\tfmt.Println(stack.Count())\n}\n", 17, "`Pop` takes an item out of `items` but leaves `size` as it was, while `Push` changes both - after it, `size` no longer says how many `items` holds" },
        new object[] { "Java", "import java.util.ArrayList;\nimport java.util.List;\n\npublic class Stack {\n    private final List<Integer> items = new ArrayList<>();\n    private int size = 0;\n\n    void push(int item) {\n        items.add(item);\n        size++;\n    }\n\n    int pop() {\n        return items.remove(items.size() - 1);\n    }\n\n    int count() {\n        return size;\n    }\n\n    public static void main(String[] args) {\n        Stack stack = new Stack();\n        for (int item : new int[] {1, 2, 3}) {\n            stack.push(item);\n        }\n        stack.pop();\n        System.out.println(stack.count());\n    }\n}\n", 14, "`pop` takes an item out of `items` but leaves `size` as it was, while `push` changes both - after it, `size` no longer says how many `items` holds" },
        new object[] { "JavaScript", "class Stack {\n  constructor() {\n    this.items = [];\n    this.size = 0;\n  }\n\n  push(item) {\n    this.items.push(item);\n    this.size += 1;\n  }\n\n  pop() {\n    return this.items.pop();\n  }\n\n  count() {\n    return this.size;\n  }\n}\n\nconst stack = new Stack();\nfor (const item of [1, 2, 3]) {\n  stack.push(item);\n}\nstack.pop();\nconsole.log(stack.count());\n", 13, "`pop` takes an item out of `items` but leaves `size` as it was, while `push` changes both - after it, `size` no longer says how many `items` holds" },
        new object[] { "Python", "class Stack:\n    def __init__(self):\n        self.items = []\n        self.size = 0\n\n    def push(self, item):\n        self.items.append(item)\n        self.size += 1\n\n    def pop(self):\n        return self.items.pop()\n\n    def count(self):\n        return self.size\n\n\nstack = Stack()\nfor item in [1, 2, 3]:\n    stack.push(item)\nstack.pop()\nprint(stack.count())\n", 11, "`pop` takes an item out of `items` but leaves `size` as it was, while `push` changes both - after it, `size` no longer says how many `items` holds" },
    ];

    /// <summary>The language, and a program the check must leave alone: each found program put right, and the near misses.</summary>
    public static IEnumerable<object[]> NearMisses =>
    [
        new object[] { "C++", "#include <iostream>\n#include <vector>\n\nclass Stack {\npublic:\n    void push(int item) {\n        items.push_back(item);\n        size++;\n    }\n\n    int pop() {\n        int top = items.back();\n        items.pop_back();\n        size--;\n        return top;\n    }\n\n    int count() const {\n        return size;\n    }\n\nprivate:\n    std::vector<int> items;\n    int size = 0;\n};\n\nint main() {\n    Stack stack;\n    for (int item : {1, 2, 3}) {\n        stack.push(item);\n    }\n    stack.pop();\n    std::cout << stack.count() << \"\\n\";\n}\n" },
        new object[] { "C#", "using System;\nusing System.Collections.Generic;\n\nvar stack = new Stack();\nforeach (var item in new[] { 1, 2, 3 })\n{\n    stack.Push(item);\n}\nstack.Pop();\nConsole.WriteLine(stack.Count());\n\nclass Stack\n{\n    private readonly List<int> items = new List<int>();\n    private int size = 0;\n\n    public void Push(int item)\n    {\n        items.Add(item);\n        size++;\n    }\n\n    public int Pop()\n    {\n        var top = items[items.Count - 1];\n        items.RemoveAt(items.Count - 1);\n        size--;\n        return top;\n    }\n\n    public int Count()\n    {\n        return size;\n    }\n}\n" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\ntype Stack struct {\n\titems []int\n\tsize  int\n}\n\nfunc (stack *Stack) Push(item int) {\n\tstack.items = append(stack.items, item)\n\tstack.size++\n}\n\nfunc (stack *Stack) Pop() int {\n\ttop := stack.items[len(stack.items)-1]\n\tstack.items = stack.items[:len(stack.items)-1]\n\tstack.size--\n\treturn top\n}\n\nfunc (stack *Stack) Count() int {\n\treturn stack.size\n}\n\nfunc main() {\n\tstack := &Stack{}\n\tfor _, item := range []int{1, 2, 3} {\n\t\tstack.Push(item)\n\t}\n\tstack.Pop()\n\tfmt.Println(stack.Count())\n}\n" },
        new object[] { "Java", "import java.util.ArrayList;\nimport java.util.List;\n\npublic class Stack {\n    private final List<Integer> items = new ArrayList<>();\n    private int size = 0;\n\n    void push(int item) {\n        items.add(item);\n        size++;\n    }\n\n    int pop() {\n        size--;\n        return items.remove(items.size() - 1);\n    }\n\n    int count() {\n        return size;\n    }\n\n    public static void main(String[] args) {\n        Stack stack = new Stack();\n        for (int item : new int[] {1, 2, 3}) {\n            stack.push(item);\n        }\n        stack.pop();\n        System.out.println(stack.count());\n    }\n}\n" },
        new object[] { "JavaScript", "class Stack {\n  constructor() {\n    this.items = [];\n    this.size = 0;\n  }\n\n  push(item) {\n    this.items.push(item);\n    this.size += 1;\n  }\n\n  pop() {\n    this.size -= 1;\n    return this.items.pop();\n  }\n\n  count() {\n    return this.size;\n  }\n}\n\nconst stack = new Stack();\nfor (const item of [1, 2, 3]) {\n  stack.push(item);\n}\nstack.pop();\nconsole.log(stack.count());\n" },
        new object[] { "Python", "class Stack:\n    def __init__(self):\n        self.items = []\n        self.size = 0\n\n    def push(self, item):\n        self.items.append(item)\n        self.size += 1\n\n    def pop(self):\n        self.size -= 1\n        return self.items.pop()\n\n    def count(self):\n        return self.size\n\n\nstack = Stack()\nfor item in [1, 2, 3]:\n    stack.push(item)\nstack.pop()\nprint(stack.count())\n" },
        new object[] { "C++", "#include <iostream>\n\nclass ArrayStack {\npublic:\n    void push(int item) {\n        items[top] = item;\n        top++;\n    }\n\n    int pop() {\n        top--;\n        return items[top];\n    }\n\n    int count() const {\n        return top;\n    }\n\nprivate:\n    int items[10] = {};\n    int top = 0;\n};\n\nint main() {\n    ArrayStack stack;\n    stack.push(1);\n    stack.push(2);\n    stack.pop();\n    std::cout << stack.count() << \"\\n\";\n}\n" },
        new object[] { "C#", "using System;\nusing System.Collections.Generic;\n\nvar stack = new Near();\nstack.Push(1);\nstack.Push(2);\nstack.Drop();\nConsole.WriteLine($\"{stack.Size} {stack.Pushes}\");\n\nclass Near\n{\n    private readonly List<int> items = new List<int>();\n    public int Size { get; private set; }\n    public int Pushes;\n\n    public void Push(int item)\n    {\n        items.Add(item);\n        Pushes++;\n    }\n\n    public int Pop()\n    {\n        var top = items[items.Count - 1];\n        items.RemoveAt(items.Count - 1);\n        Size = items.Count;\n        return top;\n    }\n\n    public void Drop()\n    {\n        Pop();\n    }\n}\n" },
        new object[] { "Go", "package main\n\nimport \"fmt\"\n\ntype Stack struct {\n\titems  []int\n\tsize   int\n\tpushes int\n}\n\nfunc (stack *Stack) Push(item int) {\n\tstack.items = append(stack.items, item)\n\tstack.size++\n\tstack.pushes++\n}\n\nfunc (stack *Stack) Pop() int {\n\ttop := stack.items[len(stack.items)-1]\n\tstack.items = stack.items[:len(stack.items)-1]\n\tstack.size = len(stack.items)\n\treturn top\n}\n\nfunc (stack *Stack) Drop() {\n\tstack.Pop()\n}\n\nfunc main() {\n\tstack := &Stack{}\n\tstack.Push(1)\n\tstack.Push(2)\n\tstack.Drop()\n\tfmt.Println(stack.size, stack.pushes)\n}\n" },
        new object[] { "Java", "import java.util.ArrayList;\nimport java.util.List;\n\npublic class Near {\n    private final List<Integer> items = new ArrayList<>();\n    private int size = 0;\n    private int pushes = 0;\n\n    void push(int item) {\n        items.add(item);\n        size++;\n        pushes++;\n    }\n\n    int pop() {\n        int top = items.remove(items.size() - 1);\n        size = items.size();\n        return top;\n    }\n\n    void drop() {\n        pop();\n    }\n\n    public static void main(String[] args) {\n        Near stack = new Near();\n        stack.push(1);\n        stack.push(2);\n        stack.drop();\n        System.out.println(stack.size + \" \" + stack.pushes);\n    }\n}\n" },
        new object[] { "JavaScript", "class Stack {\n  constructor() {\n    this.items = [];\n    this.size = 0;\n  }\n\n  push(item) {\n    this.items.push(item);\n    this.size += 1;\n  }\n\n  pop() {\n    const top = this.items.pop();\n    this.size = this.items.length;\n    return top;\n  }\n\n  drop() {\n    this.pop();\n  }\n\n  clear() {\n    this.items = [];\n    this.size = 0;\n  }\n}\n\nconst stack = new Stack();\nstack.push(1);\nstack.push(2);\nstack.drop();\nconsole.log(stack.size);\n" },
        new object[] { "Python", "class History:\n    def __init__(self):\n        self.items = []\n        self.pushes = 0\n\n    def push(self, item):\n        self.items.append(item)\n        self.pushes += 1\n\n    def pop(self):\n        return self.items.pop()\n\n\nclass Stack:\n    def __init__(self):\n        self.items = []\n        self.size = 0\n\n    def push(self, item):\n        self.items.append(item)\n        self.size += 1\n\n    def pop(self):\n        top = self.items.pop()\n        self.size = len(self.items)\n        return top\n\n    def drop(self):\n        self.pop()\n\n    def clear(self):\n        self.items = []\n        self.size = 0\n\n\nhistory = History()\nhistory.push(1)\nhistory.pop()\nstack = Stack()\nstack.push(1)\nstack.push(2)\nstack.drop()\nprint(history.pushes, stack.size)\n" },
    ];

    [Theory]
    [MemberData(nameof(OutOfStep))]
    public async Task AMethodChangingOnlyOneOfTwoFieldsKeptTogetherIsFound(string language, string code, int line, string said)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var outOfStep = Assert.Single(findings, finding => finding.CheckId == CountOutOfStep.CheckId);
        Assert.Equal(line, outOfStep.Span.Line);
        Assert.Equal(said, outOfStep.Message);
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public async Task ACountKeptInStepOrNotACountIsLeftAlone(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.DoesNotContain(findings, finding => finding.CheckId == CountOutOfStep.CheckId);
    }
}
