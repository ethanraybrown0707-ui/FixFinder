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
/// What the analyses predicted of a Java method, tried for real: the method is called with exactly the values that should
/// break it - <c>average(new int[0])</c> for a division by its length - from a small class compiled beside a copy of its
/// file, and only if it stops with the predicted exception on the predicted line does the finding become certain. The
/// report then says what was run and what it stopped with.
/// </summary>
/// <remarks>
/// Only a static method whose every parameter the witness gives a value Java can write down: a whole number, a number, a
/// truth, null for an object, text of a length, or an array or list of a length. It is called through reflection, so a
/// private method is called too, and the program's main never runs. A file that does not compile on its own, a method of
/// an instance, and a value the witness does not give leave the finding as it was.
/// </remarks>
public static partial class JavaConfirmation
{
    private const int MostRuns = 6;
    private const int LongestList = 100;
    private const string HarnessName = "FixFinderTriesIt";
    private const string Said = "FIXFINDER|";

    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    public static async Task<IReadOnlyList<AnalysisFinding>> ConfirmAsync(IReadOnlyList<AnalysisFinding> findings, IrProgram program, string javac, string java,
        CancellationToken cancellationToken)
    {
        var runs = findings
            .Select((finding, index) => (Finding: finding, Index: index))
            .Where(item => item.Finding.WitnessValues is { Count: > 0 } && Expected(item.Finding.CheckId).Length > 0 && item.Finding.Function is not null &&
                           Path.GetExtension(item.Finding.Span.File).Equals(".java", StringComparison.OrdinalIgnoreCase))
            .Take(MostRuns)
            .Select(async item => (item.Index, Shown: await TryAsync(item.Finding, program, javac, java, cancellationToken)));

        var confirmed = findings.ToList();
        foreach (var (index, shown) in await Task.WhenAll(runs))
            if (shown is { } evidence)
                confirmed[index] = confirmed[index] with { Confidence = Confidence.Certain, Confirmation = evidence };
        return confirmed;
    }

    /// <summary>The exceptions each check predicts, as Java names them.</summary>
    private static string[] Expected(string check) => check switch
    {
        "analysis-division-by-zero" => ["ArithmeticException"],
        "analysis-null-used" => ["NullPointerException"],
        "analysis-index-out-of-range" => ["ArrayIndexOutOfBoundsException", "IndexOutOfBoundsException", "StringIndexOutOfBoundsException"],
        "analysis-empty-collection" => ["NoSuchElementException", "IndexOutOfBoundsException", "ArrayIndexOutOfBoundsException"],
        _ => [],
    };

    private static async Task<string?> TryAsync(AnalysisFinding finding, IrProgram program, string javac, string java, CancellationToken cancellationToken)
    {
        var method = program.AllFunctions.FirstOrDefault(function => function.FullName == finding.Function &&
                                                                    string.Equals(Path.GetFullPath(function.Span.File), Path.GetFullPath(finding.Span.File), StringComparison.OrdinalIgnoreCase));
        if (method is not { IsStatic: true, Owner: { } owner } || finding.WitnessValues!.Any(value => value.TypedAt is not null)) return null;

        // Each parameter a value from the witness, written as Java writes it, with the class reflection finds the method by.
        var arguments = new List<(string Name, string Value, string Class)>();
        foreach (var parameter in method.Parameters)
        {
            if (finding.WitnessValues!.FirstOrDefault(value => value.Parameter == parameter.Name) is not { } given ||
                JavaValue(given, parameter.Type) is not var (value, type)) return null;
            arguments.Add((parameter.Name, value, type));
        }

        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-confirm", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(finding.Span.File));
            File.Copy(finding.Span.File, copy);
            var source = await File.ReadAllTextAsync(copy, cancellationToken);
            var qualified = (Package().Match(source) is { Success: true } package ? package.Groups["name"].Value + "." : "") + owner.Replace('.', '$');
            var harness = Path.Combine(folder, HarnessName + ".java");
            await File.WriteAllTextAsync(harness, Harness(qualified, method.Name, arguments, Path.GetFileName(finding.Span.File)), new UTF8Encoding(false), cancellationToken);

            var classes = Path.Combine(folder, "classes");
            var compiled = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = javac,
                // With its variables' names kept, so Java's message names the one that was null.
                Arguments = $"-encoding UTF-8 -nowarn -g -d \"{classes}\" \"{copy}\" \"{harness}\"",
                WorkingDirectory = folder,
                Timeout = CompileTimeout,
            }, cancellationToken);
            if (!Directory.Exists(classes) || !File.Exists(Path.Combine(classes, HarnessName + ".class"))) return null;

            var ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = java,
                Arguments = $"-cp \"{classes}\" {HarnessName}",
                WorkingDirectory = Path.GetDirectoryName(finding.Span.File) ?? folder,
                Timeout = RunTimeout,
                StandardInput = "",
            }, cancellationToken);

            var outcome = ran.Lines.Select(line => line.Text).LastOrDefault(text => text.StartsWith(Said, StringComparison.Ordinal));
            if (outcome?[Said.Length..].Split('|', 3) is not [var thrown, var lineText, var message] ||
                !int.TryParse(lineText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line) ||
                line != finding.Span.Line || !Expected(finding.CheckId).Contains(thrown)) return null;

            var call = $"`{method.Name}({string.Join(", ", arguments.Select(argument => argument.Value))})`";
            var stopped = message.Length > 0 && message != "null" ? $"{thrown}: {message}" : thrown;
            return $"Running {call} stopped with {stopped} on line {line}.";
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
    /// The small class that calls the method: found by reflection with its parameters' classes, made callable even when
    /// private, called with the witness's values, and the exception it stops with written out with the line of the
    /// method's own file it came from.
    /// </summary>
    private static string Harness(string owner, string method, IReadOnlyList<(string Name, string Value, string Class)> arguments, string fileName)
    {
        var classes = string.Join(", ", arguments.Select(argument => argument.Class + ".class"));
        var values = string.Join(", ", arguments.Select(argument => argument.Value));
        return $$"""
            public class {{HarnessName}} {
                public static void main(String[] args) throws Exception {
                    Class<?> owner = Class.forName("{{owner}}");
                    java.lang.reflect.Method method = owner.getDeclaredMethod("{{method}}"{{(classes.Length > 0 ? ", " + classes : "")}});
                    if (!java.lang.reflect.Modifier.isStatic(method.getModifiers())) {
                        return;
                    }
                    method.setAccessible(true);
                    try {
                        method.invoke(null, new Object[] { {{values}} });
                        System.out.println("{{Said}}||0|");
                    } catch (java.lang.reflect.InvocationTargetException thrown) {
                        Throwable cause = thrown.getCause();
                        int line = 0;
                        for (StackTraceElement frame : cause.getStackTrace()) {
                            if ("{{fileName}}".equals(frame.getFileName())) {
                                line = frame.getLineNumber();
                                break;
                            }
                        }
                        String message = String.valueOf(cause.getMessage()).replace('\n', ' ').replace('|', '/');
                        System.out.println("{{Said}}" + cause.getClass().getSimpleName() + "|" + line + "|" + message);
                    }
                }
            }
            """;
    }

    /// <summary>A witness's value written as Java writes it for the parameter's type, with the class of that type; null when Java cannot write it.</summary>
    private static (string Value, string Class)? JavaValue(WitnessValue value, IrType type)
    {
        var whole = value.Value.IsInteger ? value.Value.Numerator.ToString(CultureInfo.InvariantCulture) : null;
        var count = value.Value.IsInteger && value.Value >= 0 && value.Value <= LongestList ? (int)value.Value.Numerator : -1;

        (string Value, string Class)? written = (value.Kind, type.Name) switch
        {
            (WitnessKind.Nothing, var name) when name is not ("int" or "long" or "short" or "byte" or "double" or "float" or "boolean" or "char") =>
                ("null", ClassOf(type) ?? ""),
            (WitnessKind.WholeNumber or WitnessKind.Number, "int" or "short" or "byte") when whole is not null => (whole, type.Name),
            (WitnessKind.WholeNumber or WitnessKind.Number, "long") when whole is not null => (whole + "L", "long"),
            (WitnessKind.WholeNumber or WitnessKind.Number, "Integer") when whole is not null => ($"Integer.valueOf({whole})", "Integer"),
            (WitnessKind.WholeNumber or WitnessKind.Number, "double" or "float") => (value.Value.ToDouble().ToString("R", CultureInfo.InvariantCulture) + (type.Name == "float" ? "f" : ""), type.Name),
            (WitnessKind.Truth, "boolean") => (value.Value.IsZero ? "false" : "true", "boolean"),
            (WitnessKind.Characters, "String") when count >= 0 => ("\"" + new string('a', count) + "\"", "String"),
            (WitnessKind.Items, "array") when count == 0 && type.Arguments is [{ Name: var item }] && ArrayItem(item) is not null =>
                ($"new {item}[0]", $"{item}[]"),
            (WitnessKind.Items, "array") when count > 0 && type.Arguments is [{ Name: var item }] && ArrayItem(item) is { } zero =>
                ($"new {item}[] {{ {string.Join(", ", Enumerable.Repeat(zero, count))} }}", $"{item}[]"),
            (WitnessKind.Items, "List" or "ArrayList" or "Collection" or "Iterable") when count >= 0 && type.Arguments is [{ Name: var item }] && ListItem(item) is { } zero =>
                ($"new java.util.ArrayList<{item}>(java.util.Collections.nCopies({count}, {zero}))", type.Name == "ArrayList" ? "java.util.ArrayList" : $"java.util.{type.Name}"),
            _ => null,
        };
        return written is { Class.Length: > 0 } ? written : null;
    }

    /// <summary>The class a parameter's type is found by, for null: its own name, an array's, or a list's.</summary>
    private static string? ClassOf(IrType type) => type.Name switch
    {
        "String" or "Integer" or "Long" or "Double" or "Boolean" or "Object" => type.Name,
        "array" when type.Arguments is [{ Name: var item }] => item + "[]",
        "List" or "Collection" or "Iterable" or "Map" or "Set" => "java.util." + type.Name,
        "ArrayList" or "HashMap" or "HashSet" or "LinkedList" => "java.util." + type.Name,
        _ => null,
    };

    /// <summary>The item an array of its type is filled with: 0 for numbers, false, and empty text.</summary>
    private static string? ArrayItem(string item) => item switch
    {
        "int" or "long" or "short" or "byte" => "0",
        "double" or "float" => "0",
        "boolean" => "false",
        "char" => "'a'",
        "String" => "\"\"",
        _ => null,
    };

    /// <summary>The item a list of its type is filled with.</summary>
    private static string? ListItem(string item) => item switch
    {
        "Integer" => "0",
        "Long" => "0L",
        "Double" => "0.0",
        "String" => "\"\"",
        "Boolean" => "false",
        _ => null,
    };

    [GeneratedRegex(@"^\s*package\s+(?<name>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex Package();
}
