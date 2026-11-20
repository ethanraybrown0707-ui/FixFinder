using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Analysis.Symbolic;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Analysis.Dynamic;

/// <summary>
/// What the analyses predicted of a JavaScript function, tried for real: the function is called with exactly the values
/// that should break it - <c>greet(null)</c> for a parameter the code lets be null - from the end of a copy of its own
/// file that keeps only the file's definitions, and only if it stops with a TypeError on the predicted line does the
/// finding become certain.
/// </summary>
/// <remarks>
/// JavaScript divides by zero into Infinity and reads past the end of a list as undefined, so the one mistake that stops
/// a JavaScript program is reading from null or undefined - and that is the one tried. The copy keeps each top-level
/// statement that only defines something - a function, a class, a constant, a require - and leaves every other one as an
/// empty line, so the program's own top-level code never runs and every line keeps its number. It is compiled by Node
/// under the program's own file name, so its own requires find what they find when the program runs. Only a function
/// declared at the top of a CommonJS file - not async, not a generator, not a method - whose every parameter the witness
/// gives a value JavaScript can write down, or leaves free: a free parameter is given undefined, as a call that leaves it
/// out gives it. An ES module, a file whose top-level statements share a line with one that runs, and a value that was
/// typed in leave the finding as it was.
/// </remarks>
public static partial class JavaScriptConfirmation
{
    private const int MostRuns = 6;
    private const int LongestList = 100;
    private const string LoaderFile = "fixfinder_tries_it.js";
    private const string CopyFile = "program_copy.js";
    private const string Said = "FIXFINDER|";
    private const string ExpectedError = "TypeError";

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    public static async Task<IReadOnlyList<AnalysisFinding>> ConfirmAsync(IReadOnlyList<AnalysisFinding> findings, IrProgram program, string node,
        CancellationToken cancellationToken)
    {
        var runs = findings
            .Select((finding, index) => (Finding: finding, Index: index))
            .Where(item => item.Finding.WitnessValues is { Count: > 0 } && item.Finding.CheckId == "analysis-null-used" && item.Finding.Function is not null &&
                           Path.GetExtension(item.Finding.Span.File).ToLowerInvariant() is ".js" or ".cjs")
            .Take(MostRuns)
            .Select(async item => (item.Index, Shown: await TryAsync(item.Finding, program, node, cancellationToken)));

        var confirmed = findings.ToList();
        foreach (var (index, shown) in await Task.WhenAll(runs))
            if (shown is { } evidence)
                confirmed[index] = confirmed[index] with { Confidence = Confidence.Certain, Confirmation = evidence };
        return confirmed;
    }

    private static async Task<string?> TryAsync(AnalysisFinding finding, IrProgram program, string node, CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(finding.Span.File);
        bool InThisFile(IrFunction function) => string.Equals(Path.GetFullPath(function.Span.File), file, StringComparison.OrdinalIgnoreCase);

        var moduleBody = program.AllFunctions.FirstOrDefault(each => each.Name == IrFunction.ModuleBody && InThisFile(each));
        var function = program.AllFunctions.FirstOrDefault(each => each.FullName == finding.Function && each.Owner is null &&
                                                                   each.EnclosedBy == IrFunction.ModuleBody && InThisFile(each));
        if (moduleBody is null || function is null || function.IsAsync || function.IsGenerator || finding.WitnessValues!.Any(value => value.TypedAt is not null)) return null;

        // Called by its name, so only a function the file declares at its top - function name(...) - not an arrow kept in a constant.
        if (!moduleBody.Body.Any(statement => statement is Declare { Initial: Opaque { What: "function" } } declared && declared.Variable == function.Name &&
                                              declared.Span.Line == function.Span.Line)) return null;

        var arguments = new List<string>();
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Kind != ParameterKind.Normal || !Identifier().IsMatch(parameter.Name)) return null;
            var value = finding.WitnessValues!.FirstOrDefault(witnessed => witnessed.Parameter == parameter.Name) is { } given ? JavaScriptValue(given) : "undefined";
            if (value is null) return null;
            arguments.Add(value);
        }

        if (await DefinitionsOfAsync(file, program, cancellationToken) is not { } kept) return null;

        var called = $"{function.Name}({string.Join(", ", arguments)})";
        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-confirm", Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, CopyFile);
            await File.WriteAllTextAsync(copy, string.Join("\n", kept) + "\n" + CallAtTheEnd(called, file), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, LoaderFile), Loader, new UTF8Encoding(false), cancellationToken);

            var ran = await new TargetRunner().RunAsync(new TargetSpec
            {
                ExecutablePath = node,
                Arguments = $"\"{Path.Combine(folder, LoaderFile)}\" \"{file}\" \"{copy}\"",
                WorkingDirectory = Path.GetDirectoryName(file)!,
                Timeout = RunTimeout,
                StandardInput = "",
            }, cancellationToken);

            var outcome = ran.Lines.Select(line => line.Text).LastOrDefault(text => text.StartsWith(Said, StringComparison.Ordinal));
            if (outcome?[Said.Length..].Split('|', 3) is not [var error, var lineText, var message] ||
                !int.TryParse(lineText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line) ||
                line != finding.Span.Line || error != ExpectedError) return null;

            return $"Running `{called}` stopped with {error}: {message} on line {line}.";
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
    /// A JavaScript file with only its definitions kept - each top-level statement that runs anything left as empty lines -
    /// for code put at its end to call its functions; null for an ES module, a file that cannot be read, or one whose kept
    /// and left statements share a line.
    /// </summary>
    internal static async Task<string[]?> DefinitionsOfAsync(string file, IrProgram program, CancellationToken cancellationToken)
    {
        var moduleBody = program.AllFunctions.FirstOrDefault(each => each.Name == IrFunction.ModuleBody &&
                                                                     string.Equals(Path.GetFullPath(each.Span.File), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        if (moduleBody is null) return null;

        string source;
        try
        {
            source = await File.ReadAllTextAsync(file, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var lines = source.Replace("\r\n", "\n").Split('\n');
        return lines.Any(line => ModuleSyntax().IsMatch(line)) ? null : DefinitionsOnly(lines, moduleBody.Body);
    }

    /// <summary>
    /// The file with each top-level statement that runs anything left as empty lines: a statement reaches from its first
    /// line to the line before the next one starts, and the lines before the first are kept - comments, a #! line. Null
    /// when a statement that is kept shares a line with one that is not, which no line-by-line copy can split.
    /// </summary>
    private static string[]? DefinitionsOnly(string[] lines, IReadOnlyList<Stmt> topLevel)
    {
        var starts = topLevel.Where(statement => statement.Span.Line >= 1).Select(statement => (statement.Span.Line, Kept: Defines(statement))).ToList();
        if (starts.GroupBy(start => start.Line).Any(sameLine => sameLine.Select(start => start.Kept).Distinct().Count() > 1)) return null;

        var kept = (string[])lines.Clone();
        var ordered = starts.DistinctBy(start => start.Line).OrderBy(start => start.Line).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Kept) continue;
            var lastLine = index + 1 < ordered.Count ? ordered[index + 1].Line - 1 : lines.Length;
            for (var line = ordered[index].Line; line <= lastLine && line <= lines.Length; line++) kept[line - 1] = "";
        }
        return kept;
    }

    /// <summary>
    /// Whether a top-level statement only defines something: a function, a class, a name with nothing given, or a value made
    /// of literals, names, functions and requires - nothing that calls the program's code, makes an object or waits.
    /// "use strict" and other strings standing alone are kept, as they change how the rest is read.
    /// </summary>
    private static bool Defines(Stmt statement) => statement switch
    {
        Declare { Initial: null } => true,
        Declare { Initial: { } value } => IrWalk.Within(value).All(part => part switch
        {
            Call { Callee: Name { Identifier: "require" } } => true,
            Call or NewObject or AssignValue or NextItem or MoreItems => false,
            Opaque { What: "await" or "yield" or "imported" } => false,
            _ => true,
        }),
        Evaluate { Value: Literal { Kind: LiteralKind.Text } } => true,
        _ => false,
    };

    /// <summary>
    /// The call put at the end of the copy, inside the file's own scope: what it threw - its name, the line of the
    /// program's own file it came from on the stack, and its message - written out in one line, then Node is ended.
    /// </summary>
    private static string CallAtTheEnd(string called, string file) => $$"""
        ;(function () {
            let fixfinderSaid = "{{Said}}|0|";
            try {
                {{called}};
            } catch (fixfinderFailure) {
                const fixfinderFrames = String(fixfinderFailure && fixfinderFailure.stack).split("\n");
                const fixfinderOwnFrame = fixfinderFrames.find(frame => frame.includes({{JsonSerializer.Serialize(file + ":")}})) || "";
                const fixfinderLine = /:(\d+):\d+\)?\s*$/.exec(fixfinderOwnFrame);
                const fixfinderName = (fixfinderFailure && fixfinderFailure.name) || typeof fixfinderFailure;
                const fixfinderMessage = String(fixfinderFailure && fixfinderFailure.message).replace(/[|\r\n]+/g, " ");
                fixfinderSaid = "{{Said}}" + fixfinderName + "|" + (fixfinderLine ? fixfinderLine[1] : 0) + "|" + fixfinderMessage;
            }
            process.stdout.write(fixfinderSaid + "\n", () => process.exit(0));
        })();
        """;

    /// <summary>Compiles the copy as the program's own file - its name, its folder's modules - so its requires resolve as the program's do.</summary>
    internal const string Loader = """
        const Module = require("module");
        const path = require("path");
        const fs = require("fs");
        const [, , programPath, copyPath] = process.argv;
        const programModule = new Module(programPath, null);
        programModule.filename = programPath;
        programModule.paths = Module._nodeModulePaths(path.dirname(programPath));
        programModule._compile(fs.readFileSync(copyPath, "utf8"), programPath);
        """;

    /// <summary>A witness's value as JavaScript writes it; null when it cannot be written.</summary>
    private static string? JavaScriptValue(WitnessValue value)
    {
        var count = value.Value.IsInteger && value.Value >= 0 && value.Value <= LongestList ? (int)value.Value.Numerator : -1;
        return value.Kind switch
        {
            WitnessKind.Nothing => "null",
            WitnessKind.Truth => value.Value.IsZero ? "false" : "true",
            WitnessKind.WholeNumber or WitnessKind.Number when value.Value.IsInteger => value.Value.Numerator.ToString(CultureInfo.InvariantCulture),
            WitnessKind.WholeNumber or WitnessKind.Number => value.Value.ToDouble().ToString("R", CultureInfo.InvariantCulture),
            WitnessKind.Characters when count >= 0 => "\"" + new string('a', count) + "\"",
            WitnessKind.Items when count >= 0 => "[" + string.Join(", ", Enumerable.Repeat("0", count)) + "]",
            _ => null,
        };
    }

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*$")]
    private static partial Regex Identifier();

    /// <summary>An ES module's import or export, which a CommonJS copy cannot hold.</summary>
    [GeneratedRegex(@"^\s*(?:import\s*[\w{*""']|export\s)")]
    private static partial Regex ModuleSyntax();
}
