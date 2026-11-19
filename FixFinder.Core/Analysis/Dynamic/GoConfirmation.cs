using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// What the analyses predicted of a Go function, tried for real: the function is called with exactly the values that
/// should break it - <c>average([]int{})</c> for a division by its length - from a small file built beside a copy of its
/// own, whose main is renamed so the program's own never runs, and only if it panics as predicted on the predicted line
/// does the finding become certain.
/// </summary>
/// <remarks>
/// Only a function of package main - not a method - whose every parameter the witness gives a value Go can write down: a
/// whole number, a number, a truth, nil for a pointer, a slice or a map, text of a length, or a slice of a length. A
/// parameter the witness leaves free - any value takes the same path - is given Go's own zero value for its type: 0, "",
/// false or nil. The parameters' types are read from the function's own first line, as Go writes them. The panic is
/// recovered, and the line it came from read from the stack, in the copy of the program's own file. A file that does not
/// build on its own, or needs a module, leaves the finding as it was. Go never downloads anything for it.
/// </remarks>
public static partial class GoConfirmation
{
    private const int MostRuns = 6;
    private const int LongestList = 100;
    private const string HarnessFile = "fixfinder_tries_it.go";
    private const string Said = "FIXFINDER|";

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    public static async Task<IReadOnlyList<AnalysisFinding>> ConfirmAsync(IReadOnlyList<AnalysisFinding> findings, IrProgram program, string go,
        CancellationToken cancellationToken)
    {
        var runs = findings
            .Select((finding, index) => (Finding: finding, Index: index))
            .Where(item => item.Finding.WitnessValues is { Count: > 0 } && Expected(item.Finding.CheckId) is not null && item.Finding.Function is not null &&
                           Path.GetExtension(item.Finding.Span.File).Equals(".go", StringComparison.OrdinalIgnoreCase))
            .Take(MostRuns)
            .Select(async item => (item.Index, Shown: await TryAsync(item.Finding, program, go, cancellationToken)));

        var confirmed = findings.ToList();
        foreach (var (index, shown) in await Task.WhenAll(runs))
            if (shown is { } evidence)
                confirmed[index] = confirmed[index] with { Confidence = Confidence.Certain, Confirmation = evidence };
        return confirmed;
    }

    /// <summary>What Go's panic says for each check's mistake.</summary>
    private static string? Expected(string check) => check switch
    {
        "analysis-division-by-zero" => "integer divide by zero",
        "analysis-null-used" => "nil pointer dereference",
        "analysis-index-out-of-range" or "analysis-empty-collection" => "index out of range",
        _ => null,
    };

    private static async Task<string?> TryAsync(AnalysisFinding finding, IrProgram program, string go, CancellationToken cancellationToken)
    {
        var function = program.AllFunctions.FirstOrDefault(each => each.FullName == finding.Function && each.Owner is null &&
                                                                   string.Equals(Path.GetFullPath(each.Span.File), Path.GetFullPath(finding.Span.File), StringComparison.OrdinalIgnoreCase));
        if (function is null || finding.WitnessValues!.Any(value => value.TypedAt is not null)) return null;

        string source;
        try
        {
            source = await File.ReadAllTextAsync(finding.Span.File, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (!MainPackage().IsMatch(source)) return null;

        // The parameters as the function's first line writes them, each given the witness's value in Go's words.
        var header = source.Replace("\r\n", "\n").Split('\n').ElementAtOrDefault(function.Span.Line - 1) ?? "";
        if (FunctionHeader().Match(header) is not { Success: true } signature || signature.Groups["name"].Value != function.Name ||
            Parameters(signature.Groups["parameters"].Value) is not { } parameters) return null;
        var arguments = new List<string>();
        foreach (var (name, type) in parameters)
        {
            var value = finding.WitnessValues!.FirstOrDefault(witnessed => witnessed.Parameter == name) is { } given ? GoValue(given, type) : ZeroValue(type);
            if (value is null) return null;
            arguments.Add(value);
        }

        var fileName = Path.GetFileName(finding.Span.File);
        if (fileName.Equals(HarnessFile, StringComparison.OrdinalIgnoreCase)) return null;

        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-confirm", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, fileName), ProgramMain().Replace(source, "func fixfinderProgramMain()"), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, HarnessFile), Harness(function.Name, arguments, fileName), new UTF8Encoding(false), cancellationToken);

            var ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = go,
                Arguments = $"run {HarnessFile} \"{fileName}\"",
                WorkingDirectory = folder,
                Timeout = RunTimeout,
                StandardInput = "",
                ExtraEnvironment = new Dictionary<string, string> { ["GOPROXY"] = "off", ["GOSUMDB"] = "off", ["GOTOOLCHAIN"] = "local" },
            }, cancellationToken);

            var outcome = ran.Lines.Select(line => line.Text).LastOrDefault(text => text.StartsWith(Said, StringComparison.Ordinal));
            if (outcome?[Said.Length..].Split('|', 3) is not [var panicked, var lineText, _] ||
                !int.TryParse(lineText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line) ||
                line != finding.Span.Line || !panicked.Contains(Expected(finding.CheckId)!, StringComparison.Ordinal)) return null;

            return $"Running `{function.Name}({string.Join(", ", arguments)})` stopped with a panic - {panicked} - on line {line}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The file that calls the function: its panic recovered, and the line it came from found on the stack in the program's
    /// own file, written out with what the panic said.
    /// </summary>
    private static string Harness(string function, IReadOnlyList<string> arguments, string fileName) => $$"""
        package main

        import (
            "fmt"
            "runtime"
            "strings"
        )

        func main() {
            defer func() {
                failure := recover()
                if failure == nil {
                    fmt.Println("{{Said}}|0|")
                    return
                }
                line := 0
                for depth := 0; depth < 64; depth++ {
                    _, file, at, ok := runtime.Caller(depth)
                    if !ok {
                        break
                    }
                    if strings.HasSuffix(strings.ReplaceAll(file, "\\", "/"), "/{{fileName}}") {
                        line = at
                        break
                    }
                }
                said := strings.ReplaceAll(fmt.Sprint(failure), "|", "/")
                fmt.Printf("{{Said}}%s|%d|\n", said, line)
            }()
            {{function}}({{string.Join(", ", arguments)}})
        }
        """;

    /// <summary>
    /// Each parameter's name and type as Go writes them, a type shared by names before it - a, b int - given to each; null
    /// for a list whose parameters are not all named.
    /// </summary>
    private static List<(string Name, string Type)>? Parameters(string list)
    {
        var parameters = new List<(string Name, string Type)>();
        var waiting = new List<string>();
        foreach (var part in SplitAtTopLevel(list).Select(part => part.Trim()).Where(part => part.Length > 0))
        {
            if (NameAndType().Match(part) is { Success: true } typed)
            {
                foreach (var name in waiting) parameters.Add((name, typed.Groups["type"].Value.Trim()));
                waiting.Clear();
                parameters.Add((typed.Groups["name"].Value, typed.Groups["type"].Value.Trim()));
            }
            else waiting.Add(part);
        }
        return waiting.Count == 0 ? parameters : null;
    }

    private static IEnumerable<string> SplitAtTopLevel(string list)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < list.Length; index++)
        {
            depth += list[index] switch { '(' or '[' or '{' => 1, ')' or ']' or '}' => -1, _ => 0 };
            if (list[index] == ',' && depth == 0)
            {
                yield return list[start..index];
                start = index + 1;
            }
        }
        yield return list[start..];
    }

    /// <summary>A witness's value written as Go writes it for the parameter's type; null when Go cannot write it.</summary>
    private static string? GoValue(WitnessValue value, string type)
    {
        var whole = value.Value.IsInteger ? value.Value.Numerator.ToString(CultureInfo.InvariantCulture) : null;
        var count = value.Value.IsInteger && value.Value >= 0 && value.Value <= LongestList ? (int)value.Value.Numerator : -1;

        return value.Kind switch
        {
            WitnessKind.Nothing when type.StartsWith('*') || type.StartsWith("[]", StringComparison.Ordinal) || type.StartsWith("map[", StringComparison.Ordinal) => "nil",
            WitnessKind.WholeNumber or WitnessKind.Number when WholeType().IsMatch(type) && whole is not null => whole,
            WitnessKind.WholeNumber or WitnessKind.Number when type is "float64" or "float32" => value.Value.ToDouble().ToString("R", CultureInfo.InvariantCulture),
            WitnessKind.Truth when type == "bool" => value.Value.IsZero ? "false" : "true",
            WitnessKind.Characters when type == "string" && count >= 0 => "\"" + new string('a', count) + "\"",
            WitnessKind.Items when type.StartsWith("[]", StringComparison.Ordinal) && count == 0 => type + "{}",
            WitnessKind.Items when type.StartsWith("[]", StringComparison.Ordinal) && count > 0 => $"make({type}, {count})",
            WitnessKind.Items when type.StartsWith("map[", StringComparison.Ordinal) && count == 0 => type + "{}",
            _ => null,
        };
    }

    /// <summary>Go's zero value for a type it can be written for without knowing the type's own declaration; null for any other.</summary>
    private static string? ZeroValue(string type) => type switch
    {
        _ when WholeType().IsMatch(type) || type is "float64" or "float32" => "0",
        "string" => "\"\"",
        "bool" => "false",
        "error" or "any" or "interface{}" => "nil",
        _ when type.StartsWith('*') || type.StartsWith("[]", StringComparison.Ordinal) || type.StartsWith("map[", StringComparison.Ordinal) => "nil",
        _ => null,
    };

    [GeneratedRegex(@"^\s*package\s+main\s*$", RegexOptions.Multiline)]
    private static partial Regex MainPackage();

    [GeneratedRegex(@"^func\s+main\s*\(\s*\)", RegexOptions.Multiline)]
    private static partial Regex ProgramMain();

    /// <summary>A function's first line: func name(parameters) - not a method, which names its receiver first.</summary>
    [GeneratedRegex(@"^\s*func\s+(?<name>(?>\w+))\s*\((?<parameters>[^()]*(?:\([^()]*\)[^()]*)*)\)")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"^(?<name>(?>\w+))\s+(?<type>\S.*)$")]
    private static partial Regex NameAndType();

    [GeneratedRegex(@"^u?int(?:8|16|32|64)?$|^byte$|^rune$")]
    private static partial Regex WholeType();
}
