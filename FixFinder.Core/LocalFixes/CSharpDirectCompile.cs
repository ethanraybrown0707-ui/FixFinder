using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes;

/// <summary>
/// Compiles one C# file the way <c>dotnet build</c> does, by running the compiler it runs with the
/// arguments it passes - without the second of MSBuild around it.
/// </summary>
/// <remarks>
/// <c>dotnet build Program.cs</c> spends about a second per check, and very little of it compiling:
/// the compiler itself, through the compiler server the SDK keeps running, answers in about a tenth of
/// that. So the arguments are asked for once per file name - a build that stops short of compiling
/// hands them back exactly, one per line - and every later check runs the SDK's own csc with them.
/// <para>
/// <b>Nothing about the compile is reconstructed.</b> The compiler is the one in the installed SDK,
/// not a copy FixFinder carries; the references, analyzers, source generators, warning levels, defines
/// and language version are the SDK's, captured rather than listed here. Only the paths change: the file
/// being checked, where its output goes, and the two lines of the SDK's generated settings that name
/// the file and its folder.
/// </para>
/// <para>
/// <b>And it is not trusted until it has agreed.</b> The first checks of each file name compile both
/// ways and compare the results - exit code, every error, every diagnostic line. One disagreement and
/// that name goes back to <c>dotnet build</c> for as long as FixFinder is open. A file with <c>#:</c>
/// directives, which can pull in packages and other projects, always uses <c>dotnet build</c>.
/// </para>
/// </remarks>
internal static partial class CSharpDirectCompile
{
    /// <summary>How many checks of each file name compile both ways before the direct one is trusted alone.</summary>
    internal const int ChecksCompared = 2;

    /// <summary>For measuring: every check compiles both ways, and any disagreement is recorded.</summary>
    internal static bool CompareEveryCheck { get; set; }

    /// <summary>Every disagreement seen, described, newest last.</summary>
    internal static ConcurrentQueue<string> Disagreements { get; } = new();

    private static int _comparisons;

    /// <summary>How many checks have compiled both ways and been compared.</summary>
    internal static int Comparisons => Volatile.Read(ref _comparisons);

    internal static void Compared() => Interlocked.Increment(ref _comparisons);

    private static readonly ConcurrentDictionary<string, Lazy<Task<Template?>>> Templates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What an argument does once the file being checked is known.</summary>
    internal enum Part
    {
        Verbatim,
        Source,
        Output,
        ReferenceOutput,
        GeneratedSource,
        GeneratedSettings,
    }

    internal sealed record Argument(Part Part, string Text, string? FileName = null, string? Content = null);

    /// <summary>
    /// The compile <c>dotnet build</c> runs for a file of one name, with the paths that change taken out.
    /// </summary>
    internal sealed class Template(string compiler, bool shared, IReadOnlyList<Argument> arguments, string probeFolder)
    {
        private int _agreed;
        private volatile bool _refused;

        public string Compiler { get; } = compiler;
        public bool Shared { get; } = shared;
        public IReadOnlyList<Argument> Arguments { get; } = arguments;

        /// <summary>The folder the capture ran in, as it appears in generated settings.</summary>
        public string ProbeFolder { get; } = probeFolder;

        public bool Refused => _refused;

        public bool Trusted => !_refused && !CompareEveryCheck && Volatile.Read(ref _agreed) >= ChecksCompared;

        public void Record(bool agreed)
        {
            if (agreed) Interlocked.Increment(ref _agreed);
            else _refused = true;
        }
    }

    /// <summary>
    /// The compile for a copy of a C# file at <paramref name="copy"/>, run in <paramref name="folder"/>.
    /// </summary>
    internal static TargetSpec SpecFor(Template template, string copy, string folder, TimeSpan timeout)
    {
        var obj = Path.Combine(folder, "obj");
        Directory.CreateDirectory(Path.Combine(obj, "refint"));

        var lines = new List<string>(template.Arguments.Count);

        foreach (var argument in template.Arguments)
        {
            switch (argument.Part)
            {
                case Part.Source:
                    lines.Add(Quote(copy));
                    break;

                case Part.Output:
                    lines.Add("/out:" + Quote(Path.Combine(obj, argument.FileName!)));
                    break;

                case Part.ReferenceOutput:
                    lines.Add("/refout:" + Quote(Path.Combine(obj, "refint", argument.FileName!)));
                    break;

                case Part.GeneratedSource or Part.GeneratedSettings:
                    var path = Path.Combine(obj, argument.FileName!);
                    File.WriteAllText(path, argument.Content!.Replace(template.ProbeFolder, folder, StringComparison.Ordinal), new UTF8Encoding(false));
                    lines.Add(argument.Part == Part.GeneratedSettings ? "/analyzerconfig:" + Quote(path) : Quote(path));
                    break;

                default:
                    lines.Add(argument.Text);
                    break;
            }
        }

        var response = Path.Combine(obj, "check.rsp");
        File.WriteAllLines(response, lines, new UTF8Encoding(false));

        return new TargetSpec
        {
            ExecutablePath = template.Compiler,
            // /noconfig only counts on the command line; in a response file the compiler ignores it and says so.
            Arguments = $"{(template.Shared ? "-shared " : "")}-nologo -noconfig \"@{response}\"",
            WorkingDirectory = folder,
            Timeout = timeout,
        };
    }

    /// <summary>
    /// The compiler's output as <c>dotnet build</c> prints it. A diagnostic with no file - a source
    /// generator's, say - comes out of the compiler as <c>error SYSLIB1062: ...</c>, and MSBuild names
    /// the tool that reported it: <c>CSC : error SYSLIB1062: ...</c>.
    /// </summary>
    internal static IReadOnlyList<CapturedLine> AsBuildPrintsIt(IReadOnlyList<CapturedLine> lines) =>
        lines.Select(line => Unplaced().IsMatch(line.Text) ? line with { Text = "CSC : " + line.Text } : line).ToList();

    [GeneratedRegex(@"^(?:error|warning|info) [A-Za-z]+\d+: ")]
    private static partial Regex Unplaced();

    /// <summary>The template for files of this name, captured once and shared by every check that asks.</summary>
    /// <remarks>
    /// Captured without the caller's cancellation, because it is shared: a check stopped because another
    /// rule's fix won must not take the capture with it for every check after.
    /// </remarks>
    internal static Task<Template?> TemplateFor(string fileName, string dotnet) =>
        Templates.GetOrAdd(fileName, name => new Lazy<Task<Template?>>(() => Task.Run(() => CaptureAsync(name, dotnet)))).Value;

    /// <summary>True when a C# file's own text can change how it is built, so only dotnet build will do.</summary>
    internal static bool HasDirectives(string text) =>
        text.Split('\n').Any(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal));

    private const string CaptureTargets = """
        <Project>
          <Target Name="FixFinderCaptureCompile" AfterTargets="CoreCompile" Condition="'$(FixFinderCompileArguments)' != ''">
            <WriteLinesToFile File="$(FixFinderCompileArguments)"
                              Lines="roslyn=$(RoslynTargetsPath);shared=$(UseSharedCompilation);toolpath=$(CscToolPath);toolexe=$(CscToolExe);@(CscCommandLineArgs)"
                              Overwrite="true" Encoding="utf-8" />
          </Target>
        </Project>
        """;

    private static async Task<Template?> CaptureAsync(string fileName, string dotnet)
    {
        var probeFolder = Path.Combine(CompileCheck.Root, "csharp-" + Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(probeFolder);

            var probe = Path.Combine(probeFolder, fileName);
            var targets = Path.Combine(probeFolder, "capture.targets");
            var captured = Path.Combine(probeFolder, "arguments.txt");

            await File.WriteAllTextAsync(probe, "Console.WriteLine();\n");
            await File.WriteAllTextAsync(targets, CaptureTargets);

            // A design-time build: the compiler is asked for its arguments and not run, which is how
            // editors learn them. The build then fails for want of the output it never made; only the
            // file of arguments matters.
            var spec = new TargetSpec
            {
                ExecutablePath = dotnet,
                Arguments =
                    $"build \"{probe}\" -nologo -v q -p:ProvideCommandLineArgs=true -p:SkipCompilerExecution=true " +
                    $"\"-p:CustomAfterMicrosoftCommonTargets={targets}\" \"-p:FixFinderCompileArguments={captured}\"",
                WorkingDirectory = probeFolder,
                Timeout = TimeSpan.FromMinutes(2),
            };

            var run = await new TargetRunner(new ParserRegistry()).RunAsync(spec, CancellationToken.None);

            if (run.Outcome is RunOutcome.LaunchFailed or RunOutcome.TimedOut or RunOutcome.Cancelled || !File.Exists(captured))
                return null;

            return Read(await File.ReadAllLinesAsync(captured), probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (Directory.Exists(probeFolder)) Directory.Delete(probeFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A template from the captured lines, or null when anything in them is not understood.
    /// </summary>
    /// <remarks>
    /// Refusing is always safe - it only means dotnet build - so anything unfamiliar is a refusal: a
    /// compiler swapped in by a package, a missing compiler, or an argument that points at the capture's
    /// own folders in a way not accounted for below.
    /// </remarks>
    internal static Template? Read(IReadOnlyList<string> lines, string probe)
    {
        if (lines.Count < 5) return null;

        string? Setting(int index, string name) =>
            lines[index].TrimStart('﻿') is var line && line.StartsWith(name + "=", StringComparison.Ordinal) ? line[(name.Length + 1)..] : null;

        if (Setting(0, "roslyn") is not { Length: > 0 } roslyn || Setting(1, "shared") is not { } shared ||
            Setting(2, "toolpath") is not "" || Setting(3, "toolexe") is not "")
            return null;

        var compiler = Path.Combine(Path.GetFullPath(roslyn), "bincore", "csc.exe");
        if (!File.Exists(compiler)) return null;

        var probeFolder = Path.GetDirectoryName(probe)!;
        var raw = lines.Skip(4).ToList();

        if (raw.FirstOrDefault(a => a.StartsWith("/out:", StringComparison.Ordinal)) is not { } output) return null;

        var obj = Path.GetDirectoryName(Unquote(output[5..]))!;
        var arguments = new List<Argument>();

        foreach (var text in raw)
        {
            if (text == "/noconfig") continue;

            if (Unquote(text).Equals(probe, StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add(new Argument(Part.Source, text));
            }
            else if (text.StartsWith("/out:", StringComparison.Ordinal))
            {
                arguments.Add(new Argument(Part.Output, text, Path.GetFileName(Unquote(text[5..]))));
            }
            else if (text.StartsWith("/refout:", StringComparison.Ordinal))
            {
                arguments.Add(new Argument(Part.ReferenceOutput, text, Path.GetFileName(Unquote(text[8..]))));
            }
            else if (!text.StartsWith('/') && Inside(Unquote(text), obj))
            {
                var path = Unquote(text);
                arguments.Add(new Argument(Part.GeneratedSource, text, Path.GetFileName(path), File.ReadAllText(path)));
            }
            else if (text.StartsWith("/analyzerconfig:", StringComparison.Ordinal) && Inside(Unquote(text[16..]), obj))
            {
                var path = Unquote(text[16..]);
                arguments.Add(new Argument(Part.GeneratedSettings, text, Path.GetFileName(path), File.ReadAllText(path)));
            }
            else if (text.Contains(obj, StringComparison.OrdinalIgnoreCase) || text.Contains(probeFolder, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            else
            {
                arguments.Add(new Argument(Part.Verbatim, text));
            }
        }

        if (arguments.Count(a => a.Part == Part.Source) != 1) return null;

        // Two generated files with one name would overwrite each other in the check's folder.
        var generated = arguments.Where(a => a.FileName is not null && a.Part is Part.GeneratedSource or Part.GeneratedSettings).Select(a => a.FileName!);
        if (generated.Count() != generated.Distinct(StringComparer.OrdinalIgnoreCase).Count()) return null;

        return new Template(compiler, !shared.Equals("false", StringComparison.OrdinalIgnoreCase), arguments, probeFolder);
    }

    /// <summary>
    /// Whether two checks of the same change said the same thing: the same exit code, the same errors
    /// in the same places, and the same diagnostics, once each folder's path is taken out.
    /// </summary>
    internal static bool Agree(CheckResult build, string buildFolder, CheckResult direct, string directFolder, out string difference)
    {
        difference = "";

        if (build.Ran != direct.Ran || build.ExitCode != direct.ExitCode)
        {
            difference = $"exit {build.ExitCode} from dotnet build, {direct.ExitCode} from the compiler";
            return false;
        }

        static List<string> Errors(CheckResult result) => result.Errors
            .Select(e => $"{LocalFixEngine.KeyOf(e)}@{(e.CulpritFrame ?? e.Frames.FirstOrDefault())?.Line}:{(e.CulpritFrame ?? e.Frames.FirstOrDefault())?.Column}")
            .Order(StringComparer.Ordinal).ToList();

        static List<string> Diagnostics(CheckResult result, string folder) => result.Lines
            .Select(l => l.Text.Replace(folder, "<folder>", StringComparison.OrdinalIgnoreCase).Trim())
            .Where(t => Diagnostic().IsMatch(t))
            .Order(StringComparer.Ordinal).ToList();

        if (!Errors(build).SequenceEqual(Errors(direct)))
        {
            difference = $"errors differ: [{string.Join("; ", Errors(build))}] against [{string.Join("; ", Errors(direct))}]";
            return false;
        }

        var fromBuild = Diagnostics(build, buildFolder);
        var fromCompiler = Diagnostics(direct, directFolder);

        if (!fromBuild.SequenceEqual(fromCompiler))
        {
            difference = $"diagnostics differ: [{string.Join(" | ", fromBuild)}] against [{string.Join(" | ", fromCompiler)}]";
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"\b(?:error|warning) [A-Za-z]+\d+: ")]
    private static partial Regex Diagnostic();

    private static bool Inside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private static string Quote(string path) => $"\"{path}\"";
}
