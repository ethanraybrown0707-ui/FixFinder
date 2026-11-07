using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// The trees whose insert throws away its new node, run in each language with what a tree of 50, 30, 70, 20 and 40 should
/// print in order: the change the check offers is proved by a copy with it, which prints 20, 30, 40, 50 and 70 where the program
/// printed 50 alone. Each row is what FixFinder gave for the program on this computer when these were written. A language this computer cannot run is passed over, and a copy
/// Windows would not let start is not asked to be proved.
/// </summary>
/// <remarks>Each row builds its program, and a copy with the change, in seven of the languages - so the rows run alone, after the other tests.</remarks>
[Collection(BuildsRunAlone.Name)]
public class NewNodeLostLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string InOrder = "20\n30\n40\n50\n70\n";

    /// <summary>The file, the program, the check, the line the change starts on, how many it takes away, and the lines it writes.</summary>
    public static TheoryData<string, string, string, int, int, string> Programs => new()
    {
        { "tree.py", "class Node:\n    def __init__(self, value):\n        self.value = value\n        self.left = None\n        self.right = None\n\n\ndef insert(node, value):\n    if node is None:\n        return Node(value)\n    if value < node.value:\n        insert(node.left, value)\n    else:\n        insert(node.right, value)\n    return node\n\n\ndef in_order(node):\n    if node is not None:\n        in_order(node.left)\n        print(node.value)\n        in_order(node.right)\n\n\nroot = None\nfor value in [50, 30, 70, 20, 40]:\n    root = insert(root, value)\nin_order(root)\n", "logic-python-new-node-lost", 12, 3, "        node.left = insert(node.left, value)|    else:|        node.right = insert(node.right, value)" },
        { "tree.rb", "Node = Struct.new(:value, :left, :right)\n\ndef insert(node, value)\n  return Node.new(value, nil, nil) if node.nil?\n  if value < node.value\n    insert(node.left, value)\n  else\n    insert(node.right, value)\n  end\n  node\nend\n\ndef in_order(node)\n  return if node.nil?\n  in_order(node.left)\n  puts node.value\n  in_order(node.right)\nend\n\nroot = nil\n[50, 30, 70, 20, 40].each { |value| root = insert(root, value) }\nin_order(root)\n", "logic-ruby-new-node-lost", 6, 3, "    node.left = insert(node.left, value)|  else|    node.right = insert(node.right, value)" },
        { "tree.lua", "local function insert(node, value)\n  if node == nil then\n    return { value = value }\n  end\n  if value < node.value then\n    insert(node.left, value)\n  else\n    insert(node.right, value)\n  end\n  return node\nend\n\nlocal function in_order(node)\n  if node ~= nil then\n    in_order(node.left)\n    print(node.value)\n    in_order(node.right)\n  end\nend\n\nlocal root = nil\nfor _, value in ipairs({ 50, 30, 70, 20, 40 }) do\n  root = insert(root, value)\nend\nin_order(root)\n", "logic-lua-new-node-lost", 6, 3, "    node.left = insert(node.left, value)|  else|    node.right = insert(node.right, value)" },
        { "main.go", "package main\n\nimport \"fmt\"\n\ntype Node struct {\n\tValue       int\n\tLeft, Right *Node\n}\n\nfunc insert(node *Node, value int) *Node {\n\tif node == nil {\n\t\treturn &Node{Value: value}\n\t}\n\tif value < node.Value {\n\t\tinsert(node.Left, value)\n\t} else {\n\t\tinsert(node.Right, value)\n\t}\n\treturn node\n}\n\nfunc inOrder(node *Node) {\n\tif node != nil {\n\t\tinOrder(node.Left)\n\t\tfmt.Println(node.Value)\n\t\tinOrder(node.Right)\n\t}\n}\n\nfunc main() {\n\tvar root *Node\n\tfor _, value := range []int{50, 30, 70, 20, 40} {\n\t\troot = insert(root, value)\n\t}\n\tinOrder(root)\n}\n", "logic-go-new-node-lost", 15, 3, "\t\tnode.Left = insert(node.Left, value)|\t} else {|\t\tnode.Right = insert(node.Right, value)" },
        { "tree.js", "function insert(node, value) {\n    if (node === null) {\n        return { value: value, left: null, right: null };\n    }\n    if (value < node.value) {\n        insert(node.left, value);\n    } else {\n        insert(node.right, value);\n    }\n    return node;\n}\n\nfunction inOrder(node) {\n    if (node !== null) {\n        inOrder(node.left);\n        console.log(node.value);\n        inOrder(node.right);\n    }\n}\n\nlet root = null;\nfor (const value of [50, 30, 70, 20, 40]) {\n    root = insert(root, value);\n}\ninOrder(root);\n", "logic-js-new-node-lost", 6, 3, "        node.left = insert(node.left, value);|    } else {|        node.right = insert(node.right, value);" },
        { "tree.ts", "type TreeNode = { value: number; left: TreeNode | null; right: TreeNode | null };\n\nfunction insert(node: TreeNode | null, value: number): TreeNode {\n    if (node === null) {\n        return { value: value, left: null, right: null };\n    }\n    if (value < node.value) {\n        insert(node.left, value);\n    } else {\n        insert(node.right, value);\n    }\n    return node;\n}\n\nfunction inOrder(node: TreeNode | null): void {\n    if (node !== null) {\n        inOrder(node.left);\n        console.log(node.value);\n        inOrder(node.right);\n    }\n}\n\nlet root: TreeNode | null = null;\nfor (const value of [50, 30, 70, 20, 40]) {\n    root = insert(root, value);\n}\ninOrder(root);\n", "logic-js-new-node-lost", 8, 3, "        node.left = insert(node.left, value);|    } else {|        node.right = insert(node.right, value);" },
        { "Tree.java", "public class Tree {\n    static class Node {\n        int value;\n        Node left;\n        Node right;\n\n        Node(int value) {\n            this.value = value;\n        }\n    }\n\n    static Node insert(Node node, int value) {\n        if (node == null) {\n            return new Node(value);\n        }\n        if (value < node.value) {\n            insert(node.left, value);\n        } else {\n            insert(node.right, value);\n        }\n        return node;\n    }\n\n    static void inOrder(Node node) {\n        if (node != null) {\n            inOrder(node.left);\n            System.out.println(node.value);\n            inOrder(node.right);\n        }\n    }\n\n    public static void main(String[] args) {\n        Node root = null;\n        for (int value : new int[] {50, 30, 70, 20, 40}) {\n            root = insert(root, value);\n        }\n        inOrder(root);\n    }\n}\n", "logic-java-new-node-lost", 17, 3, "            node.left = insert(node.left, value);|        } else {|            node.right = insert(node.right, value);" },
        { "Tree.cs", "Node? root = null;\nforeach (var value in new[] { 50, 30, 70, 20, 40 })\n{\n    root = Insert(root, value);\n}\nInOrder(root);\n\nstatic Node Insert(Node? node, int value)\n{\n    if (node == null)\n    {\n        return new Node(value);\n    }\n    if (value < node.Value)\n    {\n        Insert(node.Left, value);\n    }\n    else\n    {\n        Insert(node.Right, value);\n    }\n    return node;\n}\n\nstatic void InOrder(Node? node)\n{\n    if (node != null)\n    {\n        InOrder(node.Left);\n        Console.WriteLine(node.Value);\n        InOrder(node.Right);\n    }\n}\n\nclass Node(int value)\n{\n    public int Value { get; } = value;\n    public Node? Left { get; set; }\n    public Node? Right { get; set; }\n}\n", "logic-csharp-new-node-lost", 16, 5, "        node.Left = Insert(node.Left, value);|    }|    else|    {|        node.Right = Insert(node.Right, value);" },
        { "tree.c", "#include <stdio.h>\n#include <stdlib.h>\n\nstruct Node {\n    int value;\n    struct Node *left;\n    struct Node *right;\n};\n\nstruct Node *insert(struct Node *node, int value) {\n    if (node == NULL) {\n        struct Node *made = malloc(sizeof(struct Node));\n        made->value = value;\n        made->left = NULL;\n        made->right = NULL;\n        return made;\n    }\n    if (value < node->value) {\n        insert(node->left, value);\n    } else {\n        insert(node->right, value);\n    }\n    return node;\n}\n\nvoid in_order(struct Node *node) {\n    if (node != NULL) {\n        in_order(node->left);\n        printf(\"%d\\n\", node->value);\n        in_order(node->right);\n    }\n}\n\nint main(void) {\n    struct Node *root = NULL;\n    int values[] = {50, 30, 70, 20, 40};\n    for (int i = 0; i < 5; i++) {\n        root = insert(root, values[i]);\n    }\n    in_order(root);\n    return 0;\n}\n", "logic-c-new-node-lost", 19, 3, "        node->left = insert(node->left, value);|    } else {|        node->right = insert(node->right, value);" },
        { "tree.cpp", "#include <iostream>\n\nstruct Node {\n    int value;\n    Node *left = nullptr;\n    Node *right = nullptr;\n    explicit Node(int value) : value(value) {}\n};\n\nNode *insert(Node *node, int value) {\n    if (node == nullptr) {\n        return new Node(value);\n    }\n    if (value < node->value) {\n        insert(node->left, value);\n    } else {\n        insert(node->right, value);\n    }\n    return node;\n}\n\nvoid inOrder(Node *node) {\n    if (node != nullptr) {\n        inOrder(node->left);\n        std::cout << node->value << \"\\n\";\n        inOrder(node->right);\n    }\n}\n\nint main() {\n    Node *root = nullptr;\n    for (int value : {50, 30, 70, 20, 40}) {\n        root = insert(root, value);\n    }\n    inOrder(root);\n    return 0;\n}\n", "logic-c-new-node-lost", 15, 3, "        node->left = insert(node->left, value);|    } else {|        node->right = insert(node->right, value);" },
        { "tree.dart", "class Node {\n  final int value;\n  Node? left;\n  Node? right;\n  Node(this.value);\n}\n\nNode insert(Node? node, int value) {\n  if (node == null) {\n    return Node(value);\n  }\n  if (value < node.value) {\n    insert(node.left, value);\n  } else {\n    insert(node.right, value);\n  }\n  return node;\n}\n\nvoid inOrder(Node? node) {\n  if (node != null) {\n    inOrder(node.left);\n    print(node.value);\n    inOrder(node.right);\n  }\n}\n\nvoid main() {\n  Node? root;\n  for (final value in [50, 30, 70, 20, 40]) {\n    root = insert(root, value);\n  }\n  inOrder(root);\n}\n", "logic-dart-new-node-lost", 13, 3, "    node.left = insert(node.left, value);|  } else {|    node.right = insert(node.right, value);" },
        { "tree.php", "<?php\n\nclass Node\n{\n    public ?Node $left = null;\n    public ?Node $right = null;\n\n    public function __construct(public int $value)\n    {\n    }\n}\n\nfunction insert(?Node $node, int $value): Node\n{\n    if ($node === null) {\n        return new Node($value);\n    }\n    if ($value < $node->value) {\n        insert($node->left, $value);\n    } else {\n        insert($node->right, $value);\n    }\n    return $node;\n}\n\nfunction inOrder(?Node $node): void\n{\n    if ($node !== null) {\n        inOrder($node->left);\n        echo $node->value . \"\\n\";\n        inOrder($node->right);\n    }\n}\n\n$root = null;\nforeach ([50, 30, 70, 20, 40] as $value) {\n    $root = insert($root, $value);\n}\ninOrder($root);\n", "logic-php-new-node-lost", 19, 3, "        $node->left = insert($node->left, $value);|    } else {|        $node->right = insert($node->right, $value);" },
        { "tree.pl", "use strict;\nuse warnings;\n\nsub insert {\n    my ($node, $value) = @_;\n    if (!defined $node) {\n        return { value => $value, left => undef, right => undef };\n    }\n    if ($value < $node->{value}) {\n        insert($node->{left}, $value);\n    } else {\n        insert($node->{right}, $value);\n    }\n    return $node;\n}\n\nsub in_order {\n    my ($node) = @_;\n    return unless defined $node;\n    in_order($node->{left});\n    print \"$node->{value}\\n\";\n    in_order($node->{right});\n}\n\nmy $root;\nfor my $value (50, 30, 70, 20, 40) {\n    $root = insert($root, $value);\n}\nin_order($root);\n", "logic-perl-new-node-lost", 10, 3, "        $node->{left} = insert($node->{left}, $value);|    } else {|        $node->{right} = insert($node->{right}, $value);" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public async Task TheChangeKeepsEveryNodeInTheTree(string fileName, string code, string check, int changeAt, int removed, string added)
    {
        var program = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(program, code);

        var launch = TargetFactory.FromFile(program, LiveAllowance.For(program, TimeSpan.FromMinutes(3)));
        if (!launch.Ok)
        {
            output.WriteLine($"Not run on this computer: {launch.Problem}");
            return;
        }

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry())
        {
            Language = CodeLanguage.Of(program) ?? CodeLanguage.Any,
            Expected = ExpectedBehaviour.From([new ExpectedRun(null, InOrder)], ""),
        }.CheckAsync(launch);
        foreach (var each in report.Findings)
            output.WriteLine($"[{each.RuleId}] {each.Line}: {each.Title} :: {string.Join("|", each.Fix?.NewLines ?? [])} :: {each.Verified.Summary}");

        if (report.Notes.Any(ApplicationControl.Refused) || report.Findings.Any(finding => ApplicationControl.Refused(finding.Verified.Summary)))
        {
            output.WriteLine("Windows did not let the program start here, so the change could not be proved.");
            return;
        }

        var kept = report.Findings.SingleOrDefault(finding => finding.RuleId == check);
        Assert.True(kept is not null, $"no {check} among {string.Join(", ", report.Findings.Select(finding => finding.RuleId))}");
        Assert.Equal(changeAt, kept!.Fix!.StartLine);
        Assert.Equal(removed, kept.Fix.RemoveCount);
        Assert.Equal(added.Split('|'), kept.Fix.NewLines);
        Assert.True(kept.Verified.IsVerified, kept.Verified.Summary);

        Assert.Equal(code, File.ReadAllText(program));
    }
}
