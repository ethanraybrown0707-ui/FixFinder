using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// A flattening of a list of numbers and lists in each language that gives an item that is not a list back to itself, with
/// the change that gives back the item; the same flattenings giving it back, and programs near the mistake but not it,
/// which are left alone. Each row is what the check gave for the
/// program when these were written.
/// </summary>
public class LeafFlattenedAgainTests : IDisposable
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
        { "flatten.dart", "List<Object> flatten(List<Object> items) {\n  final flat = <Object>[];\n  for (final item in items) {\n    if (item is List<Object>) {\n      flat.addAll(flatten(item));\n    } else {\n      flat.add(flatten([item]));\n    }\n  }\n  return flat;\n}\n\nvoid main() {\n  print(flatten([\n    [1, [2, 3]],\n    4,\n    [\n      [5]\n    ],\n    <Object>[]\n  ]).join(' '));\n}\n", "logic-dart-leaf-flattened-again", 7, 7, 1, "      flat.add(item);" },
        { "flatten.erl", "-module(flatten).\n-export([main/0]).\n\nflatten([]) -> [];\nflatten([Item | Rest]) when is_list(Item) -> flatten(Item) ++ flatten(Rest);\nflatten([Item | Rest]) -> [flatten([Item]) | flatten(Rest)].\n\nmain() ->\n    io:format(\"~s~n\", [lists:join(\" \", [integer_to_list(N) || N <- flatten([[1, [2, 3]], 4, [[5]], []])])]).\n", "logic-erlang-leaf-flattened-again", 6, 6, 1, "flatten([Item | Rest]) -> [Item | flatten(Rest)]." },
        { "flatten.exs", "defmodule Flat do\n  def flatten(items) do\n    Enum.flat_map(items, fn\n      item when is_list(item) -> flatten(item)\n      item -> [flatten([item])]\n    end)\n  end\nend\n\nIO.puts(Enum.join(Flat.flatten([[1, [2, 3]], 4, [[5]], []]), \" \"))\n", "logic-elixir-leaf-flattened-again", 5, 5, 1, "      item -> [item]" },
        { "flatten.go", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc flatten(items []any) []any {\n\tvar flat []any\n\tfor _, item := range items {\n\t\tif inner, isList := item.([]any); isList {\n\t\t\tflat = append(flat, flatten(inner)...)\n\t\t} else {\n\t\t\tflat = append(flat, flatten([]any{item}))\n\t\t}\n\t}\n\treturn flat\n}\n\nfunc main() {\n\tnested := []any{[]any{1, []any{2, 3}}, 4, []any{[]any{5}}, []any{}}\n\tfmt.Println(strings.Trim(fmt.Sprint(flatten(nested)), \"[]\"))\n}\n", "logic-go-leaf-flattened-again", 14, 14, 1, "\t\t\tflat = append(flat, item)" },
        { "Flatten.java", "import java.util.ArrayList;\nimport java.util.List;\nimport java.util.StringJoiner;\n\npublic class Flatten {\n    public static Object flatten(Object arr) {\n        if (arr instanceof ArrayList) {\n            ArrayList narr = (ArrayList) arr;\n            ArrayList result = new ArrayList(50);\n            for (Object x : narr) {\n                if (x instanceof ArrayList) {\n                    result.addAll((ArrayList) flatten(x));\n                } else {\n                    result.add(flatten(x));\n                }\n            }\n            return result;\n        } else {\n            return flatten(arr);\n        }\n    }\n\n    static ArrayList<Object> list(Object... items) {\n        return new ArrayList<>(List.of(items));\n    }\n\n    public static void main(String[] args) {\n        StringJoiner line = new StringJoiner(\" \");\n        for (Object number : (ArrayList) flatten(list(list(1, list(2, 3)), 4, list(list(5)), list()))) line.add(String.valueOf(number));\n        System.out.println(line);\n    }\n}\n", "logic-java-leaf-flattened-again", 14, 14, 1, "                    result.add(x);" },
        { "flatten.js", "function* flatten(items) {\n  for (const item of items) {\n    if (Array.isArray(item)) {\n      yield* flatten(item);\n    } else {\n      yield flatten(item);\n    }\n  }\n}\n\nconsole.log([...flatten([[1, [2, 3]], 4, [[5]], []])].join(\" \"));\n", "logic-js-leaf-flattened-again", 6, 6, 1, "      yield item;" },
        { "flatten.lua", "local function flatten(items)\n  local flat = {}\n  for _, item in ipairs(items) do\n    if type(item) == \"table\" then\n      for _, each in ipairs(flatten(item)) do\n        flat[#flat + 1] = each\n      end\n    else\n      flat[#flat + 1] = flatten({ item })\n    end\n  end\n  return flat\nend\n\nprint(table.concat(flatten({ { 1, { 2, 3 } }, 4, { { 5 } }, {} }), \" \"))\n", "logic-lua-leaf-flattened-again", 9, 9, 1, "      flat[#flat + 1] = item" },
        { "flatten.php", "<?php\nfunction flatten(array $items): array {\n    $flat = [];\n    foreach ($items as $item) {\n        if (is_array($item)) {\n            $flat = array_merge($flat, flatten($item));\n        } else {\n            $flat[] = flatten([$item]);\n        }\n    }\n    return $flat;\n}\n\necho implode(\" \", flatten([[1, [2, 3]], 4, [[5]], []])), \"\\n\";\n", "logic-php-leaf-flattened-again", 8, 8, 1, "            $flat[] = $item;" },
        { "flatten.pl", "use strict;\nuse warnings;\n\nsub flatten {\n    my @flat;\n    for my $item (@_) {\n        if (ref $item eq 'ARRAY') {\n            push @flat, flatten(@$item);\n        } else {\n            push @flat, flatten($item);\n        }\n    }\n    return @flat;\n}\n\nprint join(\" \", flatten([1, [2, 3]], 4, [[5]], [])), \"\\n\";\n", "logic-perl-leaf-flattened-again", 10, 10, 1, "            push @flat, $item;" },
        { "flatten.ps1", "function Get-Flat($items) {\n    $flat = [System.Collections.Generic.List[object]]::new()\n    foreach ($item in $items) {\n        if ($item -is [array]) {\n            foreach ($each in (Get-Flat $item)) { $flat.Add($each) }\n        } else {\n            $flat.Add((Get-Flat @(,$item)))\n        }\n    }\n    return ,$flat.ToArray()\n}\n\n(Get-Flat @(@(1, @(2, 3)), 4, @(,@(5)), @())) -join \" \"\n", "logic-powershell-leaf-flattened-again", 7, 7, 1, "            $flat.Add($item)" },
        { "flatten.py", "def flatten(arr):\n    for x in arr:\n        if isinstance(x, list):\n            for y in flatten(x):\n                yield y\n        else:\n            yield flatten(x)\n\n\nprint(*flatten([[1, [2, 3]], 4, [[5]], []]))\n", "logic-python-leaf-flattened-again", 7, 7, 1, "            yield x" },
        { "flatten.rb", "def flatten(items)\n  items.flat_map do |item|\n    if item.is_a?(Array)\n      flatten(item)\n    else\n      [flatten([item])]\n    end\n  end\nend\n\nputs flatten([[1, [2, 3]], 4, [[5]], []]).join(\" \")\n", "logic-ruby-leaf-flattened-again", 6, 6, 1, "      [item]" },
        { "Flatten.scala", "object Flatten {\n  def flatten(items: List[Any]): List[Any] = items.flatMap {\n    case inner: List[Any] @unchecked => flatten(inner)\n    case item => List(flatten(List(item)))\n  }\n\n  def main(args: Array[String]): Unit = println(flatten(List(List(1, List(2, 3)), 4, List(List(5)), List())).mkString(\" \"))\n}\n", "logic-scala-leaf-flattened-again", 4, 4, 1, "    case item => List(item)" },
        { "flatten.ts", "type Nested = number | Nested[];\n\nfunction flatten(items: Nested[]): Nested[] {\n  const flat: Nested[] = [];\n  for (const item of items) {\n    if (Array.isArray(item)) {\n      flat.push(...flatten(item));\n    } else {\n      flat.push(...flatten([item]));\n    }\n  }\n  return flat;\n}\n\nconsole.log(flatten([[1, [2, 3]], 4, [[5]], []]).join(\" \"));\n", "logic-js-leaf-flattened-again", 9, 9, 1, "      flat.push(item);" },
        { "Program.cs", "using System;\nusing System.Collections.Generic;\n\npublic static class Program\n{\n    static IEnumerable<object> Flatten(List<object> items)\n    {\n        foreach (var item in items)\n        {\n            if (item is List<object> inner)\n            {\n                foreach (var each in Flatten(inner))\n                {\n                    yield return each;\n                }\n            }\n            else\n            {\n                yield return Flatten(new List<object> { item });\n            }\n        }\n    }\n\n    public static void Main()\n    {\n        var nested = new List<object> { new List<object> { 1, new List<object> { 2, 3 } }, 4, new List<object> { new List<object> { 5 } }, new List<object>() };\n        Console.WriteLine(string.Join(\" \", Flatten(nested)));\n    }\n}\n", "logic-csharp-leaf-flattened-again", 19, 19, 1, "                yield return item;" },
    };

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void AnItemFlattenedAgainIsFound(string fileName, string code, string check, int line, int changeAt, int removed, string added)
    {
        var found = Assert.Single(LogicPatterns.All.Where(pattern => pattern.Id == check).SelectMany(pattern => pattern.Find(Program(fileName, code))));

        Assert.Equal(line, found.Line);
        Assert.Equal(changeAt, found.Fix!.StartLine);
        Assert.Equal(removed, found.Fix.RemoveCount);
        Assert.Equal(added.Split('\n'), found.Fix.NewLines);
    }

    /// <summary>
    /// The flattenings giving back the item; the total of a list of numbers and lists, each number added as it is; a copy,
    /// each list copied and each number kept; how deep the lists go; and each number shown through a function of its own.
    /// </summary>
    [Theory]
    [InlineData("flatten.dart", "List<Object> flatten(List<Object> items) {\n  final flat = <Object>[];\n  for (final item in items) {\n    if (item is List<Object>) {\n      flat.addAll(flatten(item));\n    } else {\n      flat.add(item);\n    }\n  }\n  return flat;\n}\n\nvoid main() {\n  print(flatten([\n    [1, [2, 3]],\n    4,\n    [\n      [5]\n    ],\n    <Object>[]\n  ]).join(' '));\n}\n")]
    [InlineData("flatten.erl", "-module(flatten).\n-export([main/0]).\n\nflatten([]) -> [];\nflatten([Item | Rest]) when is_list(Item) -> flatten(Item) ++ flatten(Rest);\nflatten([Item | Rest]) -> [Item | flatten(Rest)].\n\nmain() ->\n    io:format(\"~s~n\", [lists:join(\" \", [integer_to_list(N) || N <- flatten([[1, [2, 3]], 4, [[5]], []])])]).\n")]
    [InlineData("flatten.exs", "defmodule Flat do\n  def flatten(items) do\n    Enum.flat_map(items, fn\n      item when is_list(item) -> flatten(item)\n      item -> [item]\n    end)\n  end\nend\n\nIO.puts(Enum.join(Flat.flatten([[1, [2, 3]], 4, [[5]], []]), \" \"))\n")]
    [InlineData("flatten.go", "package main\n\nimport (\n\t\"fmt\"\n\t\"strings\"\n)\n\nfunc flatten(items []any) []any {\n\tvar flat []any\n\tfor _, item := range items {\n\t\tif inner, isList := item.([]any); isList {\n\t\t\tflat = append(flat, flatten(inner)...)\n\t\t} else {\n\t\t\tflat = append(flat, item)\n\t\t}\n\t}\n\treturn flat\n}\n\nfunc main() {\n\tnested := []any{[]any{1, []any{2, 3}}, 4, []any{[]any{5}}, []any{}}\n\tfmt.Println(strings.Trim(fmt.Sprint(flatten(nested)), \"[]\"))\n}\n")]
    [InlineData("Flatten.java", "import java.util.ArrayList;\nimport java.util.List;\nimport java.util.StringJoiner;\n\npublic class Flatten {\n    public static Object flatten(Object arr) {\n        if (arr instanceof ArrayList) {\n            ArrayList narr = (ArrayList) arr;\n            ArrayList result = new ArrayList(50);\n            for (Object x : narr) {\n                if (x instanceof ArrayList) {\n                    result.addAll((ArrayList) flatten(x));\n                } else {\n                    result.add(x);\n                }\n            }\n            return result;\n        } else {\n            return flatten(arr);\n        }\n    }\n\n    static ArrayList<Object> list(Object... items) {\n        return new ArrayList<>(List.of(items));\n    }\n\n    public static void main(String[] args) {\n        StringJoiner line = new StringJoiner(\" \");\n        for (Object number : (ArrayList) flatten(list(list(1, list(2, 3)), 4, list(list(5)), list()))) line.add(String.valueOf(number));\n        System.out.println(line);\n    }\n}\n")]
    [InlineData("flatten.js", "function* flatten(items) {\n  for (const item of items) {\n    if (Array.isArray(item)) {\n      yield* flatten(item);\n    } else {\n      yield item;\n    }\n  }\n}\n\nconsole.log([...flatten([[1, [2, 3]], 4, [[5]], []])].join(\" \"));\n")]
    [InlineData("flatten.lua", "local function flatten(items)\n  local flat = {}\n  for _, item in ipairs(items) do\n    if type(item) == \"table\" then\n      for _, each in ipairs(flatten(item)) do\n        flat[#flat + 1] = each\n      end\n    else\n      flat[#flat + 1] = item\n    end\n  end\n  return flat\nend\n\nprint(table.concat(flatten({ { 1, { 2, 3 } }, 4, { { 5 } }, {} }), \" \"))\n")]
    [InlineData("flatten.php", "<?php\nfunction flatten(array $items): array {\n    $flat = [];\n    foreach ($items as $item) {\n        if (is_array($item)) {\n            $flat = array_merge($flat, flatten($item));\n        } else {\n            $flat[] = $item;\n        }\n    }\n    return $flat;\n}\n\necho implode(\" \", flatten([[1, [2, 3]], 4, [[5]], []])), \"\\n\";\n")]
    [InlineData("flatten.pl", "use strict;\nuse warnings;\n\nsub flatten {\n    my @flat;\n    for my $item (@_) {\n        if (ref $item eq 'ARRAY') {\n            push @flat, flatten(@$item);\n        } else {\n            push @flat, $item;\n        }\n    }\n    return @flat;\n}\n\nprint join(\" \", flatten([1, [2, 3]], 4, [[5]], [])), \"\\n\";\n")]
    [InlineData("flatten.ps1", "function Get-Flat($items) {\n    $flat = [System.Collections.Generic.List[object]]::new()\n    foreach ($item in $items) {\n        if ($item -is [array]) {\n            foreach ($each in (Get-Flat $item)) { $flat.Add($each) }\n        } else {\n            $flat.Add($item)\n        }\n    }\n    return ,$flat.ToArray()\n}\n\n(Get-Flat @(@(1, @(2, 3)), 4, @(,@(5)), @())) -join \" \"\n")]
    [InlineData("flatten.py", "def flatten(arr):\n    for x in arr:\n        if isinstance(x, list):\n            for y in flatten(x):\n                yield y\n        else:\n            yield x\n\n\nprint(*flatten([[1, [2, 3]], 4, [[5]], []]))\n")]
    [InlineData("flatten.rb", "def flatten(items)\n  items.flat_map do |item|\n    if item.is_a?(Array)\n      flatten(item)\n    else\n      [item]\n    end\n  end\nend\n\nputs flatten([[1, [2, 3]], 4, [[5]], []]).join(\" \")\n")]
    [InlineData("Flatten.scala", "object Flatten {\n  def flatten(items: List[Any]): List[Any] = items.flatMap {\n    case inner: List[Any] @unchecked => flatten(inner)\n    case item => List(item)\n  }\n\n  def main(args: Array[String]): Unit = println(flatten(List(List(1, List(2, 3)), 4, List(List(5)), List())).mkString(\" \"))\n}\n")]
    [InlineData("flatten.ts", "type Nested = number | Nested[];\n\nfunction flatten(items: Nested[]): Nested[] {\n  const flat: Nested[] = [];\n  for (const item of items) {\n    if (Array.isArray(item)) {\n      flat.push(...flatten(item));\n    } else {\n      flat.push(item);\n    }\n  }\n  return flat;\n}\n\nconsole.log(flatten([[1, [2, 3]], 4, [[5]], []]).join(\" \"));\n")]
    [InlineData("Program.cs", "using System;\nusing System.Collections.Generic;\n\npublic static class Program\n{\n    static IEnumerable<object> Flatten(List<object> items)\n    {\n        foreach (var item in items)\n        {\n            if (item is List<object> inner)\n            {\n                foreach (var each in Flatten(inner))\n                {\n                    yield return each;\n                }\n            }\n            else\n            {\n                yield return item;\n            }\n        }\n    }\n\n    public static void Main()\n    {\n        var nested = new List<object> { new List<object> { 1, new List<object> { 2, 3 } }, 4, new List<object> { new List<object> { 5 } }, new List<object>() };\n        Console.WriteLine(string.Join(\" \", Flatten(nested)));\n    }\n}\n")]
    [InlineData("copy.js", "// A copy of a list of numbers and lists, each list copied and each number kept: prints [[1,[2,3]],4,[[5]],[]].\nfunction copy(items) {\n  return items.map((item) => (Array.isArray(item) ? copy(item) : item));\n}\n\nconsole.log(JSON.stringify(copy([[1, [2, 3]], 4, [[5]], []])));\n")]
    [InlineData("depth.rb", "# How deep the lists go, a number counting for nothing more: prints 3.\ndef depth(items)\n  deepest = 1\n  items.each do |item|\n    if item.is_a?(Array)\n      deepest = [deepest, 1 + depth(item)].max\n    else\n      deepest = [deepest, 1].max\n    end\n  end\n  deepest\nend\n\nputs depth([[1, [2, 3]], 4, [[5]], []])\n")]
    [InlineData("shown.py", "# Each number shown as text through a function of its own, the lists gone through: prints 1 2 3 4 5.\ndef shown(number):\n    return str(number)\n\n\ndef walk(items, out):\n    for item in items:\n        if isinstance(item, list):\n            walk(item, out)\n        else:\n            out.append(shown(item))\n    return out\n\n\nprint(\" \".join(walk([[1, [2, 3]], 4, [[5]], []], [])))\n")]
    [InlineData("total.py", "# The total of a list of numbers and lists, each number added as it is: prints 15.\ndef total(items):\n    added = 0\n    for item in items:\n        if isinstance(item, list):\n            added += total(item)\n        else:\n            added += item\n    return added\n\n\nprint(total([[1, [2, 3]], 4, [[5]], []]))\n")]
    public void AnItemGivenBackIsLeftAlone(string fileName, string code)
    {
        // Each check of the file's own language, as FixFinder runs them.
        var file = Program(fileName, code);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        Assert.Empty(LogicPatterns.All.Where(pattern => pattern.Id.EndsWith("leaf-flattened-again", StringComparison.Ordinal) && pattern.Extensions.Contains(extension))
            .SelectMany(pattern => pattern.Find(file)));
    }
}
