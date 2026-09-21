using System.Reflection;
using System.Text.RegularExpressions;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>CS4033</c>: <c>await</c> in a method that is not async.</summary>
public sealed partial class CSharpAwaitWithoutAsync : ILocalFixRule
{
    public string Id => "csharp-await-without-async";

    [GeneratedRegex(@"^(?<lead>\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed)\s+)*)(?<ret>void|[A-Za-z_][\w<>\[\],.?]*)\s+(?<name>[A-Za-z_]\w*)\s*\([^;]*$")]
    private static partial Regex Method();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS4033", "CS4032") || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, _, _) = at;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var unmatched = 0;

        for (var k = number - 2; k >= 0; k--)
        {
            for (var c = masked[k].Length - 1; c >= 0; c--)
            {
                if (masked[k][c] == '}') unmatched++;
                else if (masked[k][c] == '{' && unmatched-- == 0)
                {
                    var headerLine = masked[k][..c].Trim().Length == 0 && k > 0 ? k - 1 : k;

                    if (Method().Match(masked[headerLine]) is not { Success: true } method) return null;
                    if (method.Groups["name"].Value is "if" or "for" or "while" or "foreach" or "switch" or "using" or "lock") continue;

                    var ret = method.Groups["ret"];
                    var original = source.Lines[headerLine];
                    var task = ret.Value == "void" ? "Task" : $"Task<{ret.Value}>";

                    return LocalFix.ReplaceLine(
                        Id, $"Make {method.Groups["name"].Value} async",
                        "`await` only works inside a method marked `async`, which returns a Task.",
                        source.Path, headerLine + 1, original[..ret.Index] + "async " + task + original[(ret.Index + ret.Length)..]);
                }
            }
        }

        return null;
    }
}

/// <summary><c>static async void Main</c> - <c>A void or int returning entry point cannot be async</c>.</summary>
public sealed partial class CSharpAsyncMain : ILocalFixRule
{
    public string Id => "csharp-async-main";

    [GeneratedRegex(@"\basync\s+(?<type>void|int)\s+Main\b")]
    private static partial Regex Main();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS4009") || CSharpCode.Locate(context) is not { } at) return null;

        var (source, number, line, _) = at;
        if (Main().Match(line) is not { Success: true } main) return null;

        var type = main.Groups["type"];
        var task = type.Value == "void" ? "Task" : "Task<int>";

        return LocalFix.ReplaceLine(
            Id, $"Let an async Main return {task}",
            $"An `async void` method returns the moment it reaches its first `await`, and for `Main` that would end the program before the " +
            $"work is done. `async {task} Main` gives the runtime something to wait for.",
            source.Path, number, line[..type.Index] + task + line[(type.Index + type.Length)..]);
    }
}

/// <summary><c>string text = File.ReadAllTextAsync(path)</c> - a <c>Task&lt;string&gt;</c> where the string was meant.</summary>
public sealed partial class CSharpTaskNotAwaited : ILocalFixRule
{
    public string Id => "csharp-task-not-awaited";

    [GeneratedRegex(@"^Cannot implicitly convert type 'System\.Threading\.Tasks\.Task<(?<inner>.+)>' to '(?<target>.+)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (!CSharpCode.HasErrorCode(context, "CS0029") || Message().Match(context.Error.Message ?? "") is not { Success: true } message) return null;
        if (message.Groups["inner"].Value != message.Groups["target"].Value) return null;
        if (CSharpCode.Locate(context) is not { Index: >= 0 } at) return null;

        var (source, number, line, index) = at;
        if (line[index..].StartsWith("await", StringComparison.Ordinal)) return null;

        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var depths = Brackets.BraceDepths(masked);
        var header = number - 1;
        while (header > 0 && !(depths[header] < depths[number - 1] && Regex.IsMatch(masked[header], @"\)\s*\{?\s*$") && Regex.IsMatch(masked[header], @"\w+\s*\(") && !Regex.IsMatch(masked[header], @"^\s*(?:if|for|foreach|while|using|lock|switch|catch)\b"))) header--;

        var topLevel = !masked.Any(l => Regex.IsMatch(l, @"\bstatic\s+(?:async\s+)?\w+(?:<[^>]*>)?\s+Main\s*\("));
        if (!topLevel && !Regex.IsMatch(masked[header], @"\basync\b")) return null;

        return LocalFix.ReplaceLine(
            Id, "Wait for the result: await",
            $"A method ending in `Async` starts the work and returns straight away with a `Task<{message.Groups["inner"].Value}>` - a promise of " +
            $"the {message.Groups["inner"].Value} it will produce. `await` waits for it to finish and gives back the {message.Groups["inner"].Value} itself.",
            source.Path, number, line[..index] + "await " + line[index..]);
    }
}
