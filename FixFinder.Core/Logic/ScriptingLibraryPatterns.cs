using System.Text.RegularExpressions;
using FixFinder.Core.Checking;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Logic;

/// <summary>
/// Ruby's, PHP's, Perl's, Lua's and PowerShell's own library used in a way that runs, says nothing, and does something else -
/// each run with ruby -w, php with every error reported, perl -w with use strict and use warnings, Lua 5.4 and Windows
/// PowerShell, none of which said anything: Hash.new([]) shares one array as every key's default, so groups[:red] &lt;&lt; "Ada"
/// changed that array and left the hash {}; a foreach by reference leaves its variable pointing at the last item, and a
/// foreach after it with the same name wrote over it - 10,20,30 became 10,20,20; rtrim("test.txt", ".txt") takes off
/// characters, not text - "tes"; my $name = @_ gives $name the count of the arguments - "Hello, 1"; @first == @second
/// compares two counts - two arrays of three different numbers were "same"; t[#t / 2] is t[1.5] for three items, and Lua
/// gave nil; "hello world" -contains "world" asks whether a list holds "world" as one of its items - False for text;
/// $items -eq $null with $items an array gives back the items that are $null, and if counts that list - so @(1, 2, $null,
/// $null) -eq $null was true, and @(0, $null) -ne $null false, where $null on the left gave the right answer to both; and two
/// answers from Read-Host added joined as text - "34" for 3 and 4.
/// </summary>
public static partial class ScriptingLibraryPatterns
{
    public static IReadOnlyList<ILogicPattern> All { get; } =
    [
        new CodePattern("logic-ruby-hash-default-shared", CodePattern.Files(".rb"), Syntax.Python, RubyHashDefaultShared, Severity.Error, Confidence.Certain),
        new CodePattern("logic-php-foreach-reference-kept", CodePattern.Files(".php"), Syntax.CLike, PhpForeachReferenceKept, Severity.Error, Confidence.Certain),
        new CodePattern("logic-php-trim-word", CodePattern.Files(".php"), Syntax.CLike, PhpTrimWord),
        new CodePattern("logic-perl-arguments-counted", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlArgumentsCounted, Severity.Error, Confidence.Certain),
        new CodePattern("logic-perl-arrays-compared-by-count", CodePattern.Files(".pl", ".pm"), Syntax.Perl, PerlArraysComparedByCount, Severity.Error, Confidence.Certain),
        new CodePattern("logic-lua-index-divided", CodePattern.Files(".lua"), Syntax.Lua, LuaIndexDivided, Severity.Error),
        new CodePattern("logic-powershell-contains-on-text", CodePattern.Files(".ps1"), Syntax.Python, PowerShellContainsOnText, Severity.Error),
        new CodePattern("logic-powershell-null-on-the-right", CodePattern.Files(".ps1"), Syntax.Python, PowerShellNullOnTheRight),
        new CodePattern("logic-powershell-read-host-added", CodePattern.Files(".ps1"), Syntax.Python, PowerShellReadHostAdded, Severity.Error),
    ];

    /// <summary><c>groups = Hash.new([])</c> - a default that can be changed: an array, a hash or text.</summary>
    [GeneratedRegex(@"^(?<indent>\s*)(?<name>@{0,2}[a-z_]\w*)\s*=\s*(?<made>Hash\.new\s*\(\s*(?<default>\[\s*\]|\{\s*\}|""""|''|Array\.new|String\.new)\s*\))\s*$")]
    private static partial Regex HashWithSharedDefault();

    private static IEnumerable<LogicFinding> RubyHashDefaultShared(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (HashWithSharedDefault().Match(masked[index]) is not { Success: true } made) continue;

            var name = made.Groups["name"].Value;
            // The default changed in place, through a key that is not there: << , push, concat, an item given a value.
            var changed = new Regex($@"(?<![\w@]){Regex.Escape(name)}\s*\[[^\]]+\]\s*(?:<<|\.\s*(?:push|concat|merge!|store|insert|unshift|append)\b|\[[^\]]*\]\s*=(?!=))");
            var changedOn = Enumerable.Range(index + 1, masked.Count - index - 1).FirstOrDefault(line => changed.IsMatch(masked[line]), -1);
            if (changedOn < 0) continue;

            var line = source.Lines[index];
            var madeGroup = made.Groups["made"];
            var empty = made.Groups["default"].Value;
            var fresh = empty is "Array.new" ? "[]" : empty is "String.new" ? "\"\"" : empty;
            var changedTo = $"Hash.new {{ |hash, key| hash[key] = {fresh} }}";

            yield return new LogicFinding(id, index + 1,
                $"Hash.new({empty}) gives every missing key the same one {empty}, and does not store it - so {name}[...] on line {changedOn + 1} " +
                $"changes that one default for every key, and {name} itself stays empty: groups[:red] << \"Ada\" left the hash {{}}",
                LocalFix.ReplaceLine(id, $"Give each key its own: {changedTo}",
                    "A block runs for each missing key, makes a new default, and stores it under that key.",
                    source.Path, index + 1, line[..madeGroup.Index] + changedTo + line[(madeGroup.Index + madeGroup.Length)..]));
        }
    }

    /// <summary><c>foreach ($marks as &amp;$mark) {</c>, or with a key.</summary>
    [GeneratedRegex(@"\bforeach\s*\(\s*(?<array>\$\w+)\s+as\s+(?:\$\w+\s*=>\s*)?&\s*(?<item>\$\w+)\s*\)")]
    private static partial Regex ForeachByReference();

    private static IEnumerable<LogicFinding> PhpForeachReferenceKept(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ForeachByReference().Match(masked[index]) is not { Success: true } loop || BraceBlocks.Body(masked, index) is not { } body) continue;

            var item = loop.Groups["item"].Value;
            var escaped = Regex.Escape(item);
            var unset = new Regex($@"\bunset\s*\(\s*{escaped}\s*\)");
            // The same name given a value after the loop - by another foreach, or by =.
            var reused = new Regex($@"\bforeach\s*\([^)]*\bas\s+(?:\$\w+\s*=>\s*)?{escaped}\s*\)|^\s*{escaped}\s*=(?![=>])");
            var reusedOn = -1;
            for (var later = body.End + 1; later < masked.Count && !unset.IsMatch(masked[later]); later++)
                if (reused.IsMatch(masked[later])) { reusedOn = later; break; }
            if (reusedOn < 0) continue;

            var indent = CodeText.Indentation(source.Lines[index]);
            var array = loop.Groups["array"].Value;

            yield return new LogicFinding(id, reusedOn + 1,
                $"After the foreach by reference on line {index + 1}, {item} still points at the last item of {array} - so giving {item} a value here " +
                $"writes it into that last item: 10,20,30 became 10,20,20",
                LocalFix.Insert(id, $"Let go of the reference after the loop: unset({item});",
                    $"unset ends {item}'s link to the last item, so {item} given a value later is a new variable, and {array} is left as it is.",
                    source.Path, body.End + 2, [$"{indent}unset({item});"]));
        }
    }

    /// <summary><c>rtrim($file, ".txt")</c> or <c>ltrim($url, "www.")</c> - text that reads as an ending or a beginning, given as characters.</summary>
    [GeneratedRegex(@"\b(?<function>rtrim|ltrim)\s*\(\s*(?<subject>[^,()]+(?:\([^()]*\))?)\s*,\s*(?<quote>[""'])(?<text>[^""'\\]{2,})\k<quote>\s*\)")]
    private static partial Regex TrimText();

    private static IEnumerable<LogicFinding> PhpTrimWord(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            foreach (Match trimmed in TrimText().Matches(line))
            {
                if (masked[index].Length <= trimmed.Index || !masked[index][trimmed.Index..].StartsWith(trimmed.Groups["function"].Value, StringComparison.Ordinal)) continue;

                var text = trimmed.Groups["text"].Value;
                if (!Regex.IsMatch(text, "[A-Za-z]{2}")) continue;

                var function = trimmed.Groups["function"].Value;
                var ending = function == "rtrim";
                var subject = trimmed.Groups["subject"].Value.Trim();
                // The text as a regular expression: each character that is not a letter or a digit escaped.
                var pattern = Regex.Replace(text, "[^A-Za-z0-9]", character => "\\" + character.Value);
                var changed = $"preg_replace('/{(ending ? "" : "^")}{pattern}{(ending ? "$" : "")}/', '', {subject})";

                yield return new LogicFinding(id, index + 1,
                    $"{function} takes away characters, not text: \"{text}\" is the set of characters {string.Join(", ", text.Distinct().Select(character => $"'{character}'"))}, " +
                    $"and every one of them is taken off the {(ending ? "end" : "start")} until another is met - rtrim(\"test.txt\", \".txt\") is \"tes\"",
                    LocalFix.ReplaceLine(id, $"Take off that text: {changed}",
                        $"The regular expression matches \"{text}\" itself, only at the {(ending ? "end" : "start")}, and replaces it with nothing.",
                        source.Path, index + 1, line[..trimmed.Index] + changed + line[(trimmed.Index + trimmed.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"^(?<indent>\s*)my\s+(?<name>\$\w+)\s*=\s*@_\s*;")]
    private static partial Regex ScalarFromArguments();

    private static IEnumerable<LogicFinding> PerlArgumentsCounted(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            if (ScalarFromArguments().Match(masked[index]) is not { Success: true } given) continue;

            var name = given.Groups["name"].Value;
            var line = source.Lines[index];

            yield return new LogicFinding(id, index + 1,
                $"An array given to one scalar gives it the array's count, so {name} is how many arguments there were, not the first of them: " +
                "greet(\"Ada\") printed \"Hello, 1\"",
                LocalFix.ReplaceLine(id, $"Take the first argument: my ({name}) = @_;",
                    $"With brackets, my ({name}) is a list of one, and the arguments fill it in order, so {name} is the first.",
                    source.Path, index + 1, $"{given.Groups["indent"].Value}my ({name}) = @_;" + line[(given.Index + given.Length)..]));
        }
    }

    [GeneratedRegex(@"(?<![\w$@])(?<left>@\w+)\s*(?<test>==|!=)\s*(?<right>@\w+)\b")]
    private static partial Regex ArraysCompared();

    private static IEnumerable<LogicFinding> PerlArraysComparedByCount(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match compared in ArraysCompared().Matches(masked[index]))
            {
                var left = compared.Groups["left"].Value;
                var right = compared.Groups["right"].Value;
                var equal = compared.Groups["test"].Value == "==";
                var changed = $"\"{left}\" {(equal ? "eq" : "ne")} \"{right}\"";
                var line = source.Lines[index];

                yield return new LogicFinding(id, index + 1,
                    $"{compared.Groups["test"].Value} compares numbers, and an array used as a number is its count - so {compared.Value} asks whether " +
                    "the two have as many items, not whether they hold the same: (1, 2, 3) and (4, 5, 6) were \"same\"",
                    LocalFix.ReplaceLine(id, $"Compare what they hold: {changed}",
                        "In double quotes each array is written out, its items joined by spaces, and eq compares those two pieces of text.",
                        source.Path, index + 1, line[..compared.Index] + changed + line[(compared.Index + compared.Length)..]));
            }
        }
    }

    /// <summary><c>marks[#marks / 2]</c> - a position worked out with /, which in Lua always gives a fraction.</summary>
    [GeneratedRegex(@"(?<![\w.])(?<table>[A-Za-z_][\w.]*)\s*\[(?<position>[^\[\]]*?[^/\[]/(?!/)[^\[\]]*)\]")]
    private static partial Regex IndexDivided();

    private static IEnumerable<LogicFinding> LuaIndexDivided(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match indexed in IndexDivided().Matches(masked[index]))
            {
                var position = indexed.Groups["position"];
                var line = source.Lines[index];
                var slash = position.Index + masked[index][position.Index..(position.Index + position.Length)].IndexOf('/');
                var table = indexed.Groups["table"].Value;

                yield return new LogicFinding(id, index + 1,
                    $"/ in Lua always gives a float, so {position.Value.Trim()} is a fraction whenever it does not come out whole - #marks / 2 is 1.5 " +
                    $"for three items - and {table}[1.5] is nil, as a table's items are at whole positions",
                    LocalFix.ReplaceLine(id, "Divide to a whole number: //",
                        "// divides and rounds down, so the position is always whole - 3 // 2 is 1.",
                        source.Path, index + 1, line[..slash] + "//" + line[(slash + 1)..]));
            }
        }
    }

    /// <summary><c>$sentence -contains "world"</c> - -contains, or -notcontains, with text on the left.</summary>
    [GeneratedRegex(@"(?<left>\$\w+|""[^""]*""|'[^']*')\s+-(?<test>[ci]?(?:not)?contains)\s+(?<right>""[^""]*""|'[^']*'|\$\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex ContainsTest();

    private static IEnumerable<LogicFinding> PowerShellContainsOnText(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var code = string.Join("\n", masked);
        for (var index = 0; index < masked.Count; index++)
        {
            var line = source.Lines[index];
            foreach (Match test in ContainsTest().Matches(line))
            {
                if (masked[index].Length <= test.Groups["test"].Index || masked[index][test.Groups["test"].Index] == ' ') continue;

                var left = test.Groups["left"].Value;
                // Text on the left: in quotes, or a variable given text in quotes or from Read-Host, and never a list.
                var isText = !left.StartsWith('$') ||
                             (Regex.IsMatch(code, $@"(?im)^\s*{Regex.Escape(left)}\s*=\s*(?:""|'|Read-Host\b)") &&
                              !Regex.IsMatch(code, $@"(?im)^\s*{Regex.Escape(left)}\s*=\s*(?:@\(|.*,)"));
                if (!isText) continue;

                var right = test.Groups["right"].Value;
                var testName = test.Groups["test"].Value;
                var not = testName.Contains("not", StringComparison.OrdinalIgnoreCase);
                var inner = right.StartsWith('$') ? right : right[1..^1];
                var changed = $"{left} -{(not ? "notlike" : "like")} \"*{inner}*\"";

                yield return new LogicFinding(id, index + 1,
                    $"-{testName} asks whether a list has {right} as one of its items, and text is a list of one - itself - so it is true only when " +
                    $"the whole text is {right}: \"hello world\" -contains \"world\" was False",
                    LocalFix.ReplaceLine(id, $"Look inside the text: {changed}",
                        $"-like with * either side matches text that has {inner} anywhere in it.",
                        source.Path, index + 1, line[..test.Index] + changed + line[(test.Index + test.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"(?<left>\$\w+)\s+-(?<test>[ci]?(?:eq|ne))\s+\$null\b", RegexOptions.IgnoreCase)]
    private static partial Regex NullOnTheRight();

    private static IEnumerable<LogicFinding> PowerShellNullOnTheRight(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match compared in NullOnTheRight().Matches(masked[index]))
            {
                var left = compared.Groups["left"].Value;
                if (left.Equals("$null", StringComparison.OrdinalIgnoreCase)) continue;

                var line = source.Lines[index];
                var test = compared.Groups["test"].Value;
                var changed = $"$null -{test} {left}";

                yield return new LogicFinding(id, index + 1,
                    $"When {left} is an array, -{test} does not compare it: it gives back the items that are{(test.EndsWith("ne", StringComparison.OrdinalIgnoreCase) ? " not" : "")} " +
                    "$null, and an if takes that list as true or false by what is in it - so @(1, 2, $null, $null) -eq $null was true, and " +
                    "@(0, $null) -ne $null false",
                    LocalFix.ReplaceLine(id, $"Put $null on the left: {changed}",
                        "With $null on the left, -eq compares $null with the whole value, array or not.",
                        source.Path, index + 1, line[..compared.Index] + changed + line[(compared.Index + compared.Length)..]));
            }
        }
    }

    [GeneratedRegex(@"^(?<indent>\s*)(?<name>\$\w+)\s*=\s*(?<read>Read-Host\b.*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadHostLine();

    private static IEnumerable<LogicFinding> PowerShellReadHostAdded(string id, SourceFile source, IReadOnlyList<string> masked)
    {
        var readLines = masked.Select((line, index) => (Index: index, Match: ReadHostLine().Match(line))).Where(each => each.Match.Success)
            .GroupBy(each => each.Match.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        if (readLines.Count == 0) yield break;

        for (var index = 0; index < masked.Count; index++)
        {
            foreach (Match added in Regex.Matches(masked[index], @"(?<![\w$])(?<left>\$\w+)\s*\+\s*(?<right>\$\w+|\d+(?:\.\d+)?)(?![\w.])"))
            {
                var left = added.Groups["left"].Value;
                if (!readLines.TryGetValue(left, out var read) || read.Index >= index) continue;

                var right = added.Groups["right"].Value;
                // A number on the right, or another answer from Read-Host - not text, which + is meant to join to.
                if (right.StartsWith('$') && !readLines.ContainsKey(right)) continue;

                var readLine = source.Lines[read.Index];
                var readCall = read.Match.Groups["read"];

                yield return new LogicFinding(id, index + 1,
                    $"{left} holds the text Read-Host gave back on line {read.Index + 1}, and + with text on its left joins text - so " +
                    $"{added.Value} was \"34\" for 3 and 4, not 7",
                    LocalFix.ReplaceLine(id, $"Read it as a whole number: {left} = [int]({readLine.Substring(readCall.Index, readCall.Length)})",
                        "[int] turns the text typed into a whole number, so + adds it.",
                        source.Path, read.Index + 1, $"{read.Match.Groups["indent"].Value}{left} = [int]({readLine.Substring(readCall.Index, readCall.Length)})"));
            }
        }
    }
}
